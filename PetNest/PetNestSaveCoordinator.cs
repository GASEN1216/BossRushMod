// ============================================================================
// PetNestSaveCoordinator.cs - 遗种巢落盘协调器（实施计划 步骤 2）
// ============================================================================
// 硬约束（tests/PetNestSaveCoordinatorGuard.py 守卫）：
//   - 本类是遗种巢**唯一**调用 SavesSystem.SaveFile 的地方，且每批至多一次；
//   - SavesSystem.IsSaving 时不强写，只登记 deferred，由宿主 tick 重试；
//   - deferred 重试有预算上限，超预算保留 pending 并报告失败，不静默丢弃；
//   - SaveFile(false) 不触发 OnCollectSaveData，因此绝不能把它单独当作
//     "仓库采集已完成"或"物理落盘原子性"的证明（同 ModeHSaveFlushCoordinator）。
//
// 形态照 ModeH/ModeHSaveFlushCoordinator.cs，语义按遗种巢 Bundle_v2 调整。
// ============================================================================

using System;
using Saves;
using ItemStatsSystem;
using Duckov.Economy;

namespace BossRush
{
    /// <summary>遗种巢落盘协调器。Bundle_v2 每批一次 Save + 一次 SaveFile。</summary>
    internal static class PetNestSaveCoordinator
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _deferredFlushPending;
        private static int _deferredRetryCount;
        private static string _lastError;

        /// <summary>deferred 重试上限；超过后保留 pending 并报告失败，不静默丢弃。</summary>
        /// <summary>
        /// 已把 pending 交给 SavesSystem、但物理 SaveFile 还没成功。
        /// 与 `_deferredFlushPending` 分开记，否则 FlushPending 成功后
        /// HasAnyPendingWrite 变 false，「还欠一次 SaveFile」会被误判成「无事可做」。
        /// </summary>
        private static bool _saveFilePending;
        private static bool _assetSnapshotRequired;

        /// <summary>实物变更前置检查；之后的所有落盘和官方采集都必须连同实物一起写。</summary>
        internal static bool RequireAssetSnapshot(out string error)
        {
            error = null;
            if (SavesSystem.IsSaving || SavesSystem.CurrentSlot < 0 || CharacterMainControl.Main == null
                || CharacterMainControl.Main.CharacterItem == null || PlayerStorage.Instance == null
                || !PlayerStorage.Instance.HasInitialized() || PlayerStorage.Loading
                || PlayerStorage.Inventory == null || PlayerStorageBuffer.Instance == null
                || EconomyManager.Instance == null)
            {
                error = "asset_save_not_ready";
                return false;
            }
            _assetSnapshotRequired = true;
            return true;
        }

        internal static bool CollectPendingAssets(out string error)
        {
            error = null;
            if (!_assetSnapshotRequired) return true;
            if (!RequireAssetSnapshot(out error)) return false;
            try
            {
                CharacterMainControl.Main.CharacterItem.Save("MainCharacterItemData");
                PlayerStorage.Inventory.Save("PlayerStorage");
                PlayerStorageBuffer.SaveBuffer();
                SavesSystem.Save<EconomyManager.SaveData>("EconomyData",
                    (EconomyManager.SaveData)EconomyManager.Instance.GenerateSaveData());
                return true;
            }
            catch (Exception e) { error = "asset_collect_failed:" + e.GetType().Name; return false; }
        }

        internal static bool RequestAssetFlush(out string error)
        {
            return FlushBatch(out error, true);
        }

        private const int MaxDeferredRetries = 600;

        /// <summary>是否存在等待落盘的批次。</summary>
        internal static bool HasDeferredFlush { get { return _deferredFlushPending; } }

        /// <summary>最后一次失败原因。</summary>
        internal static string LastError { get { return _lastError; } }

        #endregion

        #region 对外入口

        /// <summary>幂等订阅 Bundle 与 v1 迁移源的生命周期。</summary>
        internal static void EnsureSubscribed()
        {
            PetNestPersistence.EnsureSubscribed();
        }

        /// <summary>幂等退订。</summary>
        internal static void ShutdownSubscription()
        {
            PetNestPersistence.ShutdownSubscription();
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
                // 欠账位随槽/卸载一起清：旧槽欠的 SaveFile 不该拿新槽去补
                _saveFilePending = false;
                _assetSnapshotRequired = false;
                _lastError = null;
            }
        }

        /// <summary>宿主 tick：重试被 IsSaving 推迟的批次。未 deferred 时 O(1) 早返。</summary>
        internal static void Tick()
        {
            if (!_deferredFlushPending) return;
            // 非基地：保留 pending、不试写、**不计重试预算**。
            // 战斗可以持续远超 600 帧，若在这里消耗预算会把 pending 直接丢成 budget_exhausted。
            // 口径与图鉴 / 日报 / 征程三个协调器一致。
            if (!IsBaseLevelSafe()) return;
            lock (_lock)
            {
                _deferredRetryCount++;
                if (_deferredRetryCount > MaxDeferredRetries)
                {
                    _lastError = "flush_deferred_budget_exhausted";
                    _deferredFlushPending = false;
                    _deferredRetryCount = 0;
                    ModBehaviour.DevLog("[PetNest] [ERROR] 存档 deferred 重试预算耗尽，pending 保留待下次写入触发");
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
                return FlushBatch(out error, true);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 批次落盘（唯一物理写入点）

        private static bool FlushBatch(out string error, bool forceAssetWrite = false)
        {
            error = null;
            bool saveFileOwed;
            lock (_lock)
            {
                saveFileOwed = _saveFilePending;
            }

            // 「没有 pending」**不等于**「无事可做」：FlushPending 成功后 pending 即被消费，
            // 若随后的 SaveFile 失败，这里只看 HasAnyPendingWrite 会直接早返 true，
            // 把重试与宿主销毁兜底一起吃掉——数据停在 SavesSystem 内存里永不落盘。
            // 形态照 Integration/Codex/CodexSaveCoordinator.cs。
            if (!PetNestPersistence.HasAnyPendingWrite && !saveFileOwed)
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

                // typed 待办清空后的物理写重试，也重新采集容器。
                if (!CollectPendingAssets(out error))
                {
                    _deferredFlushPending = true;
                    return false;
                }
                if (PetNestPersistence.HasAnyPendingWrite)
                {
                    if (!PetNestPersistence.Bundle.FlushPending())
                    {
                        error = "key_flush_failed";
                        _lastError = error;
                        lock (_lock) { _deferredFlushPending = true; }
                        return false;
                    }

                    // 已进 SavesSystem 内存，但还没写盘：从这一刻起就欠一次 SaveFile。
                    lock (_lock) { _saveFilePending = true; }
                }

                if (PetNestPersistence.IsAnyStoreFaulted)
                {
                    error = "store_faulted";
                    _lastError = error;
                    return false;
                }

                // 每批至多一次物理落盘：这是遗种巢唯一的 SaveFile 调用点。
                // 跨子系统的每帧闸：五个协调器各有独立 SaveFile 调用点，
                // 回基地首帧极易挤在同一帧。被拒时沿用已有的 deferred + Tick 重试链
                // 在后续帧补写，与 IsSaving 分支同语义。
                if (!BossRushSaveFileThrottle.TryBeginSaveFile(forceAssetWrite))
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
                    _assetSnapshotRequired = false;
                    _lastError = null;
                }
                return true;
            }
            catch (Exception e)
            {
                error = "flush_exception:" + e.GetType().Name;
                _lastError = error;
                lock (_lock) { _deferredFlushPending = true; }
                ModBehaviour.DevLog("[PetNest] [ERROR] 存档批次落盘异常: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 辅助

        /// <summary>
        /// 当前是否在基地关卡。no-throw：取不到 LevelManager 时按"不在基地"处理，
        /// 于是 Tick 保留 pending 不试写，等宿主销毁路径的绕闸兜底。
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
                _assetSnapshotRequired = false;
                _deferredFlushPending = false;
                _deferredRetryCount = 0;
                // 欠账位随槽/卸载一起清：旧槽欠的 SaveFile 不该拿新槽去补
                _saveFilePending = false;
                _lastError = null;
            }
            PetNestPersistence.ResetStaticCaches();
        }

        #endregion
    }
}
