// ============================================================================
// CampaignSaveCoordinator.cs - 鸭王征程落盘协调器（门面）
// ============================================================================
// 状态机（pending 合并 / IsSaving 推迟 / 只在基地物理落盘 / 每帧闸 / 重试预算 /
// 「欠一次 SaveFile」独立记账）自 2026-09-06 起只有一份实现：
// Common/Lifecycle/BossRushSaveCoordinatorEngine.cs。本文件只保留：
//   - 战役特有的现金快照义务：奖金与 Completed 必须同批采集，物理写失败或节流后
//     仍保留采集义务，只有 SaveFile 成功才清（ContentCashSnapshotGuard）；
//   - 一行式门面，CampaignSaveCoordinator.* 的调用面不变；
//   - 契约仍然成立：本类是战役**唯一**的物理落盘入口（经引擎，每批至多一次 SaveFile），
//     进度服务只调 CampaignPersistence.Store() 入队，绝不自己落盘。
// ============================================================================

using System;
using Saves;
using Duckov.Economy;

namespace BossRush
{
    /// <summary>战役落盘协调器门面。pending 合并成一批，一批一次 SaveFile（由共享引擎执行）。</summary>
    internal static class CampaignSaveCoordinator
    {
        #region 状态

        private static readonly object _lock = new object();

        /// <summary>奖金与 Completed 必须同批采集；物理写失败或节流后仍保留采集义务。</summary>
        private static bool _cashSnapshotRequired;
        private static string _cashError;

        private static readonly BossRushSaveCoordinatorEngine _engine =
            new BossRushSaveCoordinatorEngine(new Source(), true);

        internal static bool HasDeferredFlush { get { return _engine.HasDeferredFlush; } }

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

        internal static void EnsureSubscribed()
        {
            CampaignPersistence.EnsureSubscribed();
        }

        internal static void ShutdownSubscription()
        {
            CampaignPersistence.ShutdownSubscription();
        }

        /// <summary>请求把当前 pending 落盘；失败时保留 pending，由 Tick 重试。</summary>
        internal static void RequestFlush()
        {
            string error;
            _engine.RequestFlush(out error);
        }

        /// <summary>切档 / 删档：清空 deferred 状态与现金义务。</summary>
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

        internal static void ResetStaticCaches()
        {
            ShutdownSubscription();
            _engine.Reset();
            lock (_lock)
            {
                _cashSnapshotRequired = false;
                _cashError = null;
            }
        }

        #endregion

        #region 引擎数据源

        private sealed class Source : IBossRushSaveBatchSource
        {
            public string LogPrefix { get { return CampaignTuning.LogPrefix; } }

            public bool HasPendingWrite { get { return CampaignPersistence.HasPendingWrite; } }

            public bool IsStoreFaulted { get { return CampaignPersistence.IsStoreFaulted; } }

            public bool HasSnapshotObligation { get { lock (_lock) { return _cashSnapshotRequired; } } }

            public string LastError { get { return CampaignPersistence.LastError; } }

            public bool CollectSnapshot(out string error)
            {
                error = null;
                if (CollectPendingCash()) return true;
                lock (_lock) { error = _cashError ?? "cash_snapshot_unavailable"; }
                return false;
            }

            public bool FlushPending()
            {
                return CampaignPersistence.FlushPending();
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
