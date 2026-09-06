// ============================================================================
// DailyReportSaveCoordinator.cs - 日报落盘协调器（门面）
// ============================================================================
// 状态机（pending 合并 / IsSaving 推迟 / 只在基地物理落盘 / 每帧闸 / 重试预算 /
// 「欠一次 SaveFile」独立记账）自 2026-09-06 起只有一份实现：
// Common/Lifecycle/BossRushSaveCoordinatorEngine.cs。本文件只保留：
//   - 日报特有的现金快照义务：现金发放后的领取标记不得先于 EconomyData 落盘，
//     失败重试时重新采集（ContentCashSnapshotGuard）；
//   - 一行式门面，DailyReportSaveCoordinator.* 的调用面不变；
//   - 契约仍然成立（DailyReportPersistenceGuard）：本类是日报**唯一**的物理落盘入口
//     （经引擎，每批至多一次 SaveFile）；跨天由计时器触发、可能落在交火帧上，
//     非基地一律推迟到回基地补写，宿主销毁与关停绕闸兜底。
// ============================================================================

using System;
using Saves;
using Duckov.Economy;

namespace BossRush
{
    /// <summary>日报落盘协调器门面。pending 合并成一批，一批一次 SaveFile（由共享引擎执行）。</summary>
    internal static class DailyReportSaveCoordinator
    {
        #region 状态

        private static readonly object _lock = new object();

        /// <summary>现金发放后的领取标记不得先于 EconomyData 落盘，失败重试时重新采集。</summary>
        private static bool _cashSnapshotRequired;
        private static string _cashError;

        private static readonly BossRushSaveCoordinatorEngine _engine =
            new BossRushSaveCoordinatorEngine(new Source(), true);

        /// <summary>是否存在等待落盘的批次。</summary>
        internal static bool HasDeferredFlush { get { return _engine.HasDeferredFlush; } }

        /// <summary>最后一次失败原因。</summary>
        internal static string LastError { get { return _engine.LastError; } }

        #endregion

        #region 现金快照义务

        internal static bool TryPrepareCashReward()
        {
            try
            {
                if (SavesSystem.IsSaving || SavesSystem.CurrentSlot < 0 || EconomyManager.Instance == null)
                    return false;
                lock (_lock) { _cashSnapshotRequired = true; }
                return true;
            }
            catch (Exception) { return false; }
        }

        internal static bool CollectPendingCash()
        {
            bool required;
            lock (_lock) { required = _cashSnapshotRequired; }
            if (!required) return true;
            try
            {
                if (EconomyManager.Instance == null || SavesSystem.CurrentSlot < 0 || SavesSystem.IsSaving)
                    return false;
                SavesSystem.Save<EconomyManager.SaveData>("EconomyData",
                    (EconomyManager.SaveData)EconomyManager.Instance.GenerateSaveData());
                return true;
            }
            catch (Exception e)
            {
                lock (_lock) { _cashError = "cash_snapshot_failed:" + e.GetType().Name; }
                return false;
            }
        }

        #endregion

        #region 对外入口

        /// <summary>幂等订阅存档生命周期。</summary>
        internal static void EnsureSubscribed()
        {
            DailyReportPersistence.EnsureSubscribed();
        }

        /// <summary>幂等退订。</summary>
        internal static void ShutdownSubscription()
        {
            DailyReportPersistence.ShutdownSubscription();
        }

        /// <summary>
        /// 请求把当前 pending 落盘。成功返回 true；
        /// IsSaving / 非基地 / 写失败时返回 false 并保留 pending（由 Tick 重试）。
        /// </summary>
        internal static bool RequestFlush(out string error)
        {
            return _engine.RequestFlush(out error);
        }

        /// <summary>无返回值的便捷入口（调用方不关心失败细节时用）。</summary>
        internal static void RequestFlush()
        {
            string error;
            _engine.RequestFlush(out error);
        }

        /// <summary>切档 / 删档：清空 deferred 状态与现金义务（旧槽欠的不该拿新槽去补）。</summary>
        internal static void NotifySlotChanged()
        {
            _engine.NotifySlotChanged();
            lock (_lock)
            {
                _cashSnapshotRequired = false;
                _cashError = null;
            }
        }

        /// <summary>宿主 tick：重试被推迟的批次。未 deferred 时 O(1) 早返。</summary>
        internal static void Tick()
        {
            _engine.Tick();
        }

        /// <summary>宿主销毁时尽力提交一次（绕过基地闸与每帧闸）；失败只记录，不抛出。</summary>
        internal static bool TryFlushOnHostDestroy()
        {
            return _engine.TryFlushOnHostDestroy();
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            _engine.Reset();
            lock (_lock)
            {
                _cashSnapshotRequired = false;
                _cashError = null;
            }
            DailyReportPersistence.ResetStaticCaches();
        }

        #endregion

        #region 引擎数据源

        private sealed class Source : IBossRushSaveBatchSource
        {
            public string LogPrefix { get { return DailyReportTuning.LogPrefix; } }

            public bool HasPendingWrite { get { return DailyReportPersistence.HasPendingWrite; } }

            public bool IsStoreFaulted { get { return DailyReportPersistence.IsStoreFaulted; } }

            public bool HasSnapshotObligation { get { lock (_lock) { return _cashSnapshotRequired; } } }

            public string LastError { get { return DailyReportPersistence.LastError; } }

            public bool CollectSnapshot(out string error)
            {
                error = null;
                if (CollectPendingCash()) return true;
                lock (_lock) { error = _cashError ?? "cash_snapshot_unavailable"; }
                return false;
            }

            public bool FlushPending()
            {
                return DailyReportPersistence.FlushPending();
            }

            public void OnPhysicalSaveSucceeded()
            {
                lock (_lock)
                {
                    _cashSnapshotRequired = false;
                    _cashError = null;
                }
            }
        }

        #endregion
    }
}
