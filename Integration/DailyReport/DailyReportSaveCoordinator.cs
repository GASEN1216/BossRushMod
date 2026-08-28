// ============================================================================
// DailyReportSaveCoordinator.cs - 日报落盘协调器（P0 步骤 2）
// ============================================================================
// 硬约束（形态照 PetNest/PetNestSaveCoordinator.cs）：
//   - 本类是日报**唯一**调用 SavesSystem.SaveFile 的地方，且每批至多一次；
//   - SavesSystem.IsSaving 时不强写，只登记 deferred，由宿主 tick 重试；
//   - deferred 重试有预算上限，超预算保留 pending 并报告失败，不静默丢弃；
//   - SaveFile(false) 不触发 OnCollectSaveData，因此绝不能把它单独当作
//     "采集已完成"或"物理落盘原子性"的证明。
// ============================================================================

using System;
using Saves;

namespace BossRush
{
    /// <summary>日报落盘协调器。pending 合并成一批，一批一次 SaveFile。</summary>
    internal static class DailyReportSaveCoordinator
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _deferredFlushPending;
        private static int _deferredRetryCount;
        private static string _lastError;

        /// <summary>deferred 重试上限；超过后保留 pending 并报告失败，不静默丢弃。</summary>
        private const int MaxDeferredRetries = 600;

        /// <summary>是否存在等待落盘的批次。</summary>
        internal static bool HasDeferredFlush { get { return _deferredFlushPending; } }

        /// <summary>最后一次失败原因。</summary>
        internal static string LastError { get { return _lastError; } }

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
        /// IsSaving 或写失败时返回 false 并保留 pending（由 Tick 重试）。
        /// </summary>
        internal static bool RequestFlush(out string error)
        {
            return FlushBatch(out error);
        }

        /// <summary>无返回值的便捷入口（调用方不关心失败细节时用）。</summary>
        internal static void RequestFlush()
        {
            string error;
            FlushBatch(out error);
        }

        /// <summary>切档 / 删档：清空 deferred 状态。</summary>
        internal static void NotifySlotChanged()
        {
            lock (_lock)
            {
                _deferredFlushPending = false;
                _deferredRetryCount = 0;
                _lastError = null;
            }
        }

        /// <summary>宿主 tick：重试被 IsSaving 推迟的批次。未 deferred 时 O(1) 早返。</summary>
        internal static void Tick()
        {
            if (!_deferredFlushPending) return;
            lock (_lock)
            {
                _deferredRetryCount++;
                if (_deferredRetryCount > MaxDeferredRetries)
                {
                    _lastError = "flush_deferred_budget_exhausted";
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[ERROR] 存档 deferred 重试预算耗尽，pending 保留待下次写入触发");
                    return;
                }
            }
            string error;
            FlushBatch(out error);
        }

        /// <summary>宿主销毁时尽力提交一次；失败只记录，不抛出。</summary>
        internal static bool TryFlushOnHostDestroy()
        {
            try
            {
                string error;
                return FlushBatch(out error);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 批次落盘（唯一物理写入点）

        private static bool FlushBatch(out string error)
        {
            error = null;
            if (!DailyReportPersistence.HasPendingWrite)
            {
                lock (_lock)
                {
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                }
                return true;
            }

            try
            {
                if (SavesSystem.IsSaving)
                {
                    lock (_lock) { _deferredFlushPending = true; }
                    error = "flush_deferred_is_saving";
                    return false;
                }

                if (!DailyReportPersistence.FlushPending())
                {
                    error = "key_flush_failed";
                    _lastError = error;
                    lock (_lock) { _deferredFlushPending = true; }
                    return false;
                }

                if (DailyReportPersistence.IsStoreFaulted)
                {
                    error = "store_faulted";
                    _lastError = error;
                    return false;
                }

                // 每批至多一次物理落盘：这是日报唯一的 SaveFile 调用点。
                SavesSystem.SaveFile(false);

                lock (_lock)
                {
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                    _lastError = null;
                }
                return true;
            }
            catch (Exception e)
            {
                error = "flush_exception:" + e.GetType().Name;
                _lastError = error;
                lock (_lock) { _deferredFlushPending = true; }
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[ERROR] 存档批次落盘异常: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _deferredFlushPending = false;
                _deferredRetryCount = 0;
                _lastError = null;
            }
            DailyReportPersistence.ResetStaticCaches();
        }

        #endregion
    }
}
