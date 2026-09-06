// ============================================================================
// PetNestSaveCoordinator.cs - 遗种巢落盘协调器（门面）
// ============================================================================
// 状态机（pending 合并 / IsSaving 推迟 / 每帧闸 / 重试预算 / 「欠一次 SaveFile」独立记账）
// 自 2026-09-06 起只有一份实现：Common/Lifecycle/BossRushSaveCoordinatorEngine.cs。
// 本文件只保留：
//   - 遗种巢特有的实物资产屏障：实物变更前置检查（RequireAssetSnapshot），之后的所有
//     落盘和官方采集都必须连同主角物品 / 仓库 / 缓冲区 / 经济数据一起写
//     （AssetSnapshotBoundaryGuard）；
//   - 与其他三个协调器不同，遗种巢**不设基地闸**：RequestFlush 在任何场景都立即试写，
//     只有 Tick 的重试按基地门控（引擎参数 deferOutsideBaseScene=false）；
//   - 一行式门面，PetNestSaveCoordinator.* 的调用面不变。
//
// 硬约束（tests/PetNestSaveCoordinatorGuard.py 守卫）：
//   - 本类是遗种巢**唯一**的物理落盘入口（经引擎，每批至多一次 SaveFile），
//     整个 PetNest/ 目录零处 SaveFile 直调；
//   - SaveFile(false) 不触发 OnCollectSaveData，因此绝不能把它单独当作
//     「仓库采集已完成」或「物理落盘原子性」的证明。
// ============================================================================

using System;
using Saves;
using ItemStatsSystem;
using Duckov.Economy;

namespace BossRush
{
    /// <summary>遗种巢落盘协调器门面。Bundle_v2 每批一次 Save + 一次 SaveFile（由共享引擎执行）。</summary>
    internal static class PetNestSaveCoordinator
    {
        #region 状态

        private static readonly object _lock = new object();

        /// <summary>实物变更前置检查通过后置位；之后的所有落盘都必须连同实物一起写。</summary>
        private static bool _assetSnapshotRequired;

        // false：实物资产屏障要求 Store 之后立刻 durable，任何场景都立即试写；
        // Tick 的重试仍按基地门控且不计预算（引擎统一处理）。
        private static readonly BossRushSaveCoordinatorEngine _engine =
            new BossRushSaveCoordinatorEngine(new Source(), false);

        /// <summary>是否存在等待落盘的批次。</summary>
        internal static bool HasDeferredFlush { get { return _engine.HasDeferredFlush; } }

        /// <summary>最后一次失败原因。</summary>
        internal static string LastError { get { return _engine.LastError; } }

        #endregion

        #region 实物资产屏障

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
            lock (_lock) { _assetSnapshotRequired = true; }
            return true;
        }

        /// <summary>采集实物快照：主角物品 / 仓库 / 缓冲区 / 经济数据。无义务时直接返回 true。</summary>
        internal static bool CollectPendingAssets(out string error)
        {
            error = null;
            bool required;
            lock (_lock) { required = _assetSnapshotRequired; }
            if (!required) return true;
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

        /// <summary>实物资产屏障的同步 durable 落盘：绕过每帧闸立即写。</summary>
        internal static bool RequestAssetFlush(out string error)
        {
            return _engine.RequestFlush(out error, true);
        }

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
            return _engine.RequestFlush(out error);
        }

        /// <summary>无返回值的便捷入口（调用方不关心失败细节时用）。</summary>
        internal static void RequestFlush()
        {
            string error;
            _engine.RequestFlush(out error);
        }

        /// <summary>切档 / 删档：清空 deferred 状态与实物义务（旧槽欠的不该拿新槽去补）。</summary>
        internal static void NotifySlotChanged()
        {
            _engine.NotifySlotChanged();
            lock (_lock) { _assetSnapshotRequired = false; }
        }

        /// <summary>宿主 tick：重试被推迟的批次。未 deferred 时 O(1) 早返。</summary>
        internal static void Tick()
        {
            _engine.Tick();
        }

        /// <summary>宿主销毁时尽力提交一次；失败只记录，不抛出。</summary>
        internal static bool TryFlushOnHostDestroy()
        {
            return _engine.TryFlushOnHostDestroy();
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。连带复位持久层。</summary>
        internal static void ResetStaticCaches()
        {
            _engine.Reset();
            lock (_lock) { _assetSnapshotRequired = false; }
            PetNestPersistence.ResetStaticCaches();
        }

        #endregion

        #region 引擎数据源

        private sealed class Source : IBossRushSaveBatchSource
        {
            public string LogPrefix { get { return "[PetNest] "; } }

            public bool HasPendingWrite { get { return PetNestPersistence.HasAnyPendingWrite; } }

            public bool IsStoreFaulted { get { return PetNestPersistence.IsAnyStoreFaulted; } }

            public bool HasSnapshotObligation { get { lock (_lock) { return _assetSnapshotRequired; } } }

            public string LastError { get { return null; } }

            // typed 待办清空后的物理写重试，也重新采集容器。
            public bool CollectSnapshot(out string error)
            {
                return CollectPendingAssets(out error);
            }

            public bool FlushPending()
            {
                return PetNestPersistence.Bundle.FlushPending();
            }

            public void OnPhysicalSaveSucceeded()
            {
                lock (_lock) { _assetSnapshotRequired = false; }
                PetNestMuseumStats.EvaluatePersistedAchievements();
            }
        }

        #endregion
    }
}
