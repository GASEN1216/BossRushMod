// ============================================================================
// CampaignSaveCoordinator.cs - 鸭王征程落盘协调器
// ============================================================================
// 形态照 Integration/Codex/CodexSaveCoordinator.cs：
//   - 本类是战役**唯一**调用 SavesSystem.SaveFile 的地方，且每批至多一次；
//     进度服务只调 CampaignPersistence.Store()（入队），绝不自己落盘。
//   - SavesSystem.IsSaving 时不强写，只登记 deferred，由宿主 tick 重试。
//   - 物理落盘只发生在基地场景：SaveFile 会做备份拷贝 + 整档同步写盘，
//     而契约完成天然可能发生在交火帧上。非基地一律推迟到回基地补写，
//     宿主销毁与关停走 bypassSceneGate 兜底。
//     **非基地的 Tick 不消耗重试预算**，否则长时间战斗会把 pending 丢成预算耗尽。
//   - SaveFile(false) 不触发 OnCollectSaveData，因此不能拿它当「采集已完成」的证明。
// ============================================================================

using System;
using Saves;

namespace BossRush
{
    /// <summary>战役落盘协调器。pending 合并成一批，一批一次 SaveFile。</summary>
    internal static class CampaignSaveCoordinator
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _deferredFlushPending;
        private static int _deferredRetryCount;

        /// <summary>
        /// pending 已经交给 SavesSystem、但物理落盘（SaveFile）尚未成功。
        ///
        /// 必须与 `_deferredFlushPending` 分开记：`FlushPending()` 一旦成功，
        /// `HasPendingWrite` 就变 false，此后只靠它判断「有没有事要做」会把
        /// 「还欠一次 SaveFile」误判成「无事可做」，重试链就此断掉。
        /// </summary>
        private static bool _saveFilePending;
        private static string _lastError;

        /// <summary>deferred 重试上限；超预算保留 pending 并报告失败，不静默丢弃。</summary>
        private const int MaxDeferredRetries = 600;

        internal static bool HasDeferredFlush { get { return _deferredFlushPending; } }

        internal static string LastError { get { return _lastError; } }

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

        /// <summary>
        /// 请求把当前 pending 落盘。成功返回 true；
        /// IsSaving / 非基地 / 写失败时返回 false 并保留 pending（由 Tick 重试）。
        /// </summary>
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
                _saveFilePending = false;
                _lastError = null;
            }
        }

        /// <summary>宿主 tick：重试被推迟的批次。未 deferred 时 O(1) 早返。</summary>
        internal static void Tick()
        {
            if (!_deferredFlushPending) return;
            // 非基地：保留 pending、不试写、**不计重试预算**。
            // 战斗可以持续远超 600 帧，在这里消耗预算会把 pending 直接丢成预算耗尽。
            if (!IsBaseLevelSafe()) return;

            lock (_lock)
            {
                _deferredRetryCount++;
                if (_deferredRetryCount > MaxDeferredRetries)
                {
                    _lastError = "flush_deferred_budget_exhausted";
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix
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
                // 销毁/关停是最后机会，必须绕过场景闸：宁可在战斗帧写一次，也不能丢进度
                return FlushBatch(out error, true);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 落盘

        private static bool FlushBatch(out string error, bool bypassSceneGate)
        {
            error = null;
            try
            {
                bool saveFileOwed;
                lock (_lock)
                {
                    saveFileOwed = _saveFilePending;
                }

                // 「没有 pending」**不等于**「无事可做」：FlushPending 成功后 pending 即被消费，
                // 若随后的 SaveFile 失败，这里只看 HasPendingWrite 会直接早返 true，
                // 把重试与宿主销毁兜底一起吃掉——数据停在 SavesSystem 内存里永不落盘。
                if (!CampaignPersistence.HasPendingWrite && !saveFileOwed)
                {
                    lock (_lock)
                    {
                        _deferredFlushPending = false;
                        _deferredRetryCount = 0;
                    }
                    return true;
                }

                if (!bypassSceneGate && !IsBaseLevelSafe())
                {
                    error = "flush_deferred_not_base_scene";
                    lock (_lock)
                    {
                        _deferredFlushPending = true;
                        _lastError = error;
                    }
                    return false;
                }

                if (CampaignPersistence.HasPendingWrite)
                {
                    if (!CampaignPersistence.FlushPending())
                    {
                        error = CampaignPersistence.LastError ?? "flush_pending_failed";
                        lock (_lock)
                        {
                            _deferredFlushPending = true;
                            _lastError = error;
                        }
                        return false;
                    }

                    // 已进 SavesSystem 内存，但还没写盘：从这一刻起就欠一次 SaveFile，
                    // 直到 SaveFile 真的成功才清掉。
                    lock (_lock)
                    {
                        _saveFilePending = true;
                    }
                }

                try
                {
                    if (SavesSystem.IsSaving)
                    {
                        error = "savefile_deferred_is_saving";
                        lock (_lock)
                        {
                            _deferredFlushPending = true;
                            _lastError = error;
                        }
                        return false;
                    }

                    SavesSystem.SaveFile(false);
                }
                catch (Exception e)
                {
                    error = "savefile_failed:" + e.GetType().Name;
                    lock (_lock)
                    {
                        _deferredFlushPending = true;
                        _lastError = error;
                    }
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[ERROR] SaveFile 异常: " + e.Message);
                    return false;
                }

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
                error = "flush_batch_failed:" + e.GetType().Name;
                lock (_lock)
                {
                    _deferredFlushPending = true;
                    _lastError = error;
                }
                return false;
            }
        }

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

        internal static void ResetStaticCaches()
        {
            ShutdownSubscription();
            lock (_lock)
            {
                _deferredFlushPending = false;
                _deferredRetryCount = 0;
                _saveFilePending = false;
                _lastError = null;
            }
        }

        #endregion
    }
}
