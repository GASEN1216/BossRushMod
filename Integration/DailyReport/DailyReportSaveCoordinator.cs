// ============================================================================
// DailyReportSaveCoordinator.cs - 日报落盘协调器（P0 步骤 2）
// ============================================================================
// 硬约束（形态照 PetNest/PetNestSaveCoordinator.cs）：
//   - 本类是日报**唯一**调用 SavesSystem.SaveFile 的地方，且每批至多一次；
//   - SavesSystem.IsSaving 时不强写，只登记 deferred，由宿主 tick 重试；
//   - 物理落盘只发生在基地场景：SaveFile 会做备份拷贝 + 整档同步写盘，而跨天由
//     计时器触发、可能落在交火帧上；非基地一律推迟到回基地补写，
//     宿主销毁与关停走 bypassSceneGate 兜底（最后机会，宁可写一次也不丢当日进度）。
//     非基地的 Tick 不消耗 deferred 重试预算，否则长时间战斗会把 pending 丢成 budget_exhausted；
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

        /// <summary>
        /// 已把 pending 交给 SavesSystem、但物理 SaveFile 还没成功。
        /// 必须与 `_deferredFlushPending` 分开记：`FlushPending()` 一旦成功，
        /// `HasPendingWrite` 就变 false，此后只靠它判断「有没有事要做」会把
        /// 「还欠一次 SaveFile」误判成「无事可做」，重试链就此断掉。
        /// 形态照 Integration/Codex/CodexSaveCoordinator.cs。
        /// </summary>
        private static bool _saveFilePending;

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
            return FlushBatch(out error, false);
        }

        /// <summary>无返回值的便捷入口（调用方不关心失败细节时用）。</summary>
        internal static void RequestFlush()
        {
            string error;
            FlushBatch(out error, false);
        }

        /// <summary>切档 / 删档：清空 deferred 状态。</summary>
        internal static void NotifySlotChanged()
        {
            lock (_lock)
            {
                _deferredFlushPending = false;
                _deferredRetryCount = 0;
                // 欠账位随槽/卸载一起清：旧槽欠的 SaveFile 不该拿新槽去补
                _saveFilePending = false;
                _lastError = null;
            }
        }

        /// <summary>宿主 tick：重试被 IsSaving 推迟的批次。未 deferred 时 O(1) 早返。</summary>
        internal static void Tick()
        {
            if (!_deferredFlushPending) return;
            // 非基地：保留 pending、不试写、**不计重试预算**。
            // 战斗可以持续远超 600 帧，若在这里消耗预算会把 pending 直接丢成 budget_exhausted。
            if (!IsBaseLevelSafe()) return;
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
            FlushBatch(out error, false);
        }

        /// <summary>宿主销毁时尽力提交一次；失败只记录，不抛出。</summary>
        internal static bool TryFlushOnHostDestroy()
        {
            try
            {
                string error;
                // 销毁/关停是最后机会，必须绕过场景闸：宁可在战斗帧写一次，也不能丢当日进度
                return FlushBatch(out error, true);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 批次落盘（唯一物理写入点）

        /// <param name="bypassSceneGate">
        /// 绕过「只在基地物理落盘」的闸。只有宿主销毁与关停这类最后机会才允许传 true。
        /// </param>
        private static bool FlushBatch(out string error, bool bypassSceneGate)
        {
            error = null;
            bool saveFileOwed;
            lock (_lock)
            {
                saveFileOwed = _saveFilePending;
            }

            // 「没有 pending」**不等于**「无事可做」：FlushPending 成功后 pending 即被消费，
            // 若随后的 SaveFile 失败，这里只看 HasPendingWrite 会直接早返 true，
            // 把重试与宿主销毁兜底一起吃掉——数据停在 SavesSystem 内存里永不落盘。
            if (!DailyReportPersistence.HasPendingWrite && !saveFileOwed)
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
                // 官方 SaveFile 会做备份文件拷贝 + 整档同步写盘。跨天由计时器触发，
                // 完全可能落在交火帧上（每约 24 现实分钟一次），因此非基地一律推迟；
                // pending 仍在持久层，回基地由 Tick 补写，官方任何一次存盘也会顺带带走。
                if (!bypassSceneGate && !IsBaseLevelSafe())
                {
                    lock (_lock) { _deferredFlushPending = true; }
                    error = "flush_deferred_not_base";
                    return false;
                }

                if (SavesSystem.IsSaving)
                {
                    lock (_lock) { _deferredFlushPending = true; }
                    error = "flush_deferred_is_saving";
                    return false;
                }

                if (DailyReportPersistence.HasPendingWrite)
                {
                    if (!DailyReportPersistence.FlushPending())
                    {
                        error = "key_flush_failed";
                        _lastError = error;
                        lock (_lock) { _deferredFlushPending = true; }
                        return false;
                    }

                    // 已进 SavesSystem 内存，但还没写盘：从这一刻起就欠一次 SaveFile。
                    lock (_lock) { _saveFilePending = true; }
                }

                if (DailyReportPersistence.IsStoreFaulted)
                {
                    error = "store_faulted";
                    _lastError = error;
                    return false;
                }

                // 每批至多一次物理落盘：这是日报唯一的 SaveFile 调用点。
                // 跨子系统的每帧闸：五个协调器各有独立 SaveFile 调用点，
                // 回基地首帧极易挤在同一帧。被拒时沿用已有的 deferred + Tick 重试链
                // 在后续帧补写，与 IsSaving 分支同语义。
                if (!BossRushSaveFileThrottle.TryBeginSaveFile(bypassSceneGate))
                {
                    lock (_lock) { _deferredFlushPending = true; }
                    error = "flush_deferred_savefile_frame_busy";
                    return false;
                }

                SavesSystem.SaveFile(false);

                lock (_lock)
                {
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                    _saveFilePending = false;
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

        /// <summary>
        /// 当前是否在基地场景。读不到 LevelManager 时按「非基地」处理（保守推迟），
        /// 宿主销毁/关停路径有 bypassSceneGate 兜底，不会因此丢盘。
        /// 形态与 PetNestRuntimeModule.IsBaseScene 一致。
        /// </summary>
        private static bool IsBaseLevelSafe()
        {
            try
            {
                return LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel;
            }
            catch (Exception)
            {
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
                // 欠账位随槽/卸载一起清：旧槽欠的 SaveFile 不该拿新槽去补
                _saveFilePending = false;
                _lastError = null;
            }
            DailyReportPersistence.ResetStaticCaches();
        }

        #endregion
    }
}
