// ============================================================================
// DailyReportPersistence.cs - 日报存档管线（门面）
// ============================================================================
// 单 key JSON 整存的状态机（幂等订阅 / 槽位烙印 / 写屏障 / 回读核对 / pending 入队）
// 自 2026-09-06 起只有一份实现：Common/Lifecycle/BossRushSlotJsonStore.cs。
// 本文件只保留日报自己的绑定：存档 key、schema、编解码、盖章、官方采集前置步骤
// （同步当天余数 + 采集现金快照）、下游复位通知、Dev 构建的 Store 失败注入，
// 以及 DailyReportPersistence.* 不变的调用面。
//
// 日报特有的约束（DailyReportPersistenceGuard / ContentCashSnapshotGuard）：
//   - 官方存盘前的采集点先把运行时的当天余数同步进 DTO、再采集现金，最后才把 pending
//     合并进 ES3 缓存；现金未就绪时本次不写，领取标记不得先于 EconomyData 落盘；
//   - 切档 / 删档 / 槽位漂移必须同时复位协调器与 Service 的运行时计时状态，
//     否则会出现「数据换了、计时没换」。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>日报单 key 存档门面。</summary>
    internal static class DailyReportPersistence
    {
        private static readonly BossRushSlotJsonStore<DailyReportData> _store =
            new BossRushSlotJsonStore<DailyReportData>(new BossRushSlotJsonStoreSpec<DailyReportData>
            {
                StorageKey = DailyReportTuning.StorageKey,
                SchemaVersion = DailyReportTuning.CurrentSchemaVersion,
                LogPrefix = DailyReportTuning.LogPrefix,
                DisplayName = "日报",
                Encode = DailyReportCodec.Encode,
                ReadSchemaVersion = DailyReportCodec.ReadSchemaVersion,
                Decode = DailyReportCodec.Decode,
                CreateDefault = DailyReportCodec.CreateDefault,
                BeforeStore = StampBeforeStore,
                NotifySlotChanged = NotifySlotChangedDownstream,
                BeforeCollectSaveData = BeforeCollectSaveData,
            });

        /// <summary>Dev 构建的验收注入：置位后 Store 一律拒绝，用于复现「存档不可写」分支。</summary>
        private static bool _validationRejectStore;

        #region 只读查询

        /// <summary>是否已订阅官方存档事件。</summary>
        internal static bool IsSubscribed { get { return _store.IsSubscribed; } }

        /// <summary>单向故障：写入路径出过异常之后不再尝试写。</summary>
        internal static bool IsStoreFaulted { get { return _store.IsStoreFaulted; } }

        /// <summary>写屏障：未知版本 / 不可读 payload，只读不写。</summary>
        internal static bool HasWriteBarrier { get { return _store.HasWriteBarrier; } }

        /// <summary>是否有待落盘批次。</summary>
        internal static bool HasPendingWrite { get { return _store.HasPendingWrite; } }

        /// <summary>最后一次失败原因（诊断用）。</summary>
        internal static string LastError { get { return _store.LastError; } }

        #endregion

        #region 订阅（幂等）

        /// <summary>幂等订阅官方存档事件。模块 bootstrap 调一次。</summary>
        internal static void EnsureSubscribed()
        {
            _store.EnsureSubscribed();
        }

        /// <summary>幂等退订。宿主销毁 / 开关关闭时调用。</summary>
        internal static void ShutdownSubscription()
        {
            _store.ShutdownSubscription();
        }

        #endregion

        #region 加载 / 入队

        /// <summary>加载或初始化。幂等：缓存命中且槽位一致时直接返回。</summary>
        internal static DailyReportData LoadOrInit()
        {
            return _store.LoadOrInit();
        }

        /// <summary>当前缓存（未加载时先加载）。</summary>
        internal static DailyReportData Current { get { return _store.Current; } }

        /// <summary>
        /// 入队一次写入。战斗中不落盘，只更新缓存与 pending；
        /// 物理落盘由 DailyReportSaveCoordinator 统一触发。
        /// </summary>
        internal static bool Store(DailyReportData value)
        {
            if (value == null) return false;
            if (_validationRejectStore && ModBehaviour.DevModeEnabled) return false;
            return _store.Store(value);
        }

        /// <summary>Dev 构建专用：注入 Store 失败（F3 验收复现「存档暂不可写」分支）。</summary>
        internal static void SetValidationRejectStore(bool reject)
        {
            if (!ModBehaviour.DevModeEnabled) return;
            _validationRejectStore = reject;
        }

        /// <summary>把 pending 写进 ES3 缓存。IsSaving 时返回 false 并保留 pending（由协调器重试）。</summary>
        internal static bool FlushPending()
        {
            return _store.FlushPending();
        }

        #endregion

        #region 绑定钩子

        /// <summary>Store 前盖时间戳。</summary>
        private static void StampBeforeStore(DailyReportData value)
        {
            value.LastUpdatedTicks = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// 官方存盘前的采集点：
        ///   1) 把运行时的当天余数同步进 DTO（借官方存盘顺带写，零额外 IO）；
        ///   2) 采集现金快照；未就绪则本次不把 pending 交给 SavesSystem（领取标记不得先于现金落盘）。
        /// </summary>
        private static bool BeforeCollectSaveData()
        {
            DailyReportService.SyncCarrySecondsToPersistence();
            if (!DailyReportSaveCoordinator.CollectPendingCash()) return false;
            return true;
        }

        /// <summary>切档 / 删档 / 槽位漂移的下游复位：协调器与 Service 的计时状态同时换。</summary>
        private static void NotifySlotChangedDownstream()
        {
            DailyReportSaveCoordinator.NotifySlotChanged();
            DailyReportService.NotifySlotChanged();
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。会先退订。</summary>
        internal static void ResetStaticCaches()
        {
            _store.ResetAll();
            _validationRejectStore = false;
        }

        #endregion
    }
}
