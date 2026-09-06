// ============================================================================
// CodexPersistence.cs - 鸭皇图鉴存档管线（门面）
// ============================================================================
// 单 key JSON 整存的状态机（幂等订阅 / 槽位烙印 / 写屏障 / 回读核对 / pending 入队）
// 自 2026-09-06 起只有一份实现：Common/Lifecycle/BossRushSlotJsonStore.cs。
// 本文件只保留图鉴自己的绑定：存档 key、schema、编解码、盖章、下游复位通知，
// 以及 CodexPersistence.* 不变的调用面。
//
// 图鉴特有的约束（CodexPersistenceGuard）：
//   - 图鉴是纯收藏进度，覆盖等于抹掉玩家几十小时的记录，宁可这一局不记：
//     未知 / 更高 schemaVersion、payload 不可读时只读不写（共享实现保证）；
//   - 战斗中不写盘：Store 只入队 pending，物理落盘统一由 CodexSaveCoordinator 触发，
//     且协调器只在基地场景真正 SaveFile（宿主销毁与关停例外，见其头注释）；
//   - 切档 / 删档 / 槽位漂移必须同时复位协调器、采集器与目录三个下游，
//     否则新槽会继承上一个槽的去重集与目录快照。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>图鉴单 key 存档门面。</summary>
    internal static class CodexPersistence
    {
        private static readonly BossRushSlotJsonStore<CodexData> _store =
            new BossRushSlotJsonStore<CodexData>(new BossRushSlotJsonStoreSpec<CodexData>
            {
                StorageKey = CodexTuning.StorageKey,
                SchemaVersion = CodexTuning.CurrentSchemaVersion,
                LogPrefix = CodexTuning.LogPrefix,
                DisplayName = "图鉴",
                Encode = CodexCodec.Encode,
                ReadSchemaVersion = CodexCodec.ReadSchemaVersion,
                Decode = CodexCodec.Decode,
                CreateDefault = CodexCodec.CreateDefault,
                BeforeStore = StampBeforeStore,
                NotifySlotChanged = NotifySlotChangedDownstream,
                BeforeCollectSaveData = null,
            });

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
        internal static CodexData LoadOrInit()
        {
            return _store.LoadOrInit();
        }

        /// <summary>当前缓存（未加载时先加载）。</summary>
        internal static CodexData Current { get { return _store.Current; } }

        /// <summary>
        /// 入队一次写入。战斗中不落盘，只更新缓存与 pending；
        /// 物理落盘由 CodexSaveCoordinator 统一触发。
        /// </summary>
        internal static bool Store(CodexData value)
        {
            return _store.Store(value);
        }

        /// <summary>把 pending 写进 ES3 缓存。IsSaving 时返回 false 并保留 pending（由协调器重试）。</summary>
        internal static bool FlushPending()
        {
            return _store.FlushPending();
        }

        #endregion

        #region 绑定钩子

        /// <summary>Store 前盖时间戳。</summary>
        private static void StampBeforeStore(CodexData value)
        {
            value.LastUpdatedTicks = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// 切档 / 删档 / 槽位漂移的下游复位：协调器、采集器与目录三者同时换，
        /// 否则新槽会继承上一个槽的去重集与目录快照。
        /// </summary>
        private static void NotifySlotChangedDownstream()
        {
            CodexSaveCoordinator.NotifySlotChanged();
            CodexKillCollector.NotifySlotChanged();
            CodexBossCatalog.NotifySlotChanged();
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。会先退订。</summary>
        internal static void ResetStaticCaches()
        {
            _store.ResetAll();
        }

        #endregion
    }
}
