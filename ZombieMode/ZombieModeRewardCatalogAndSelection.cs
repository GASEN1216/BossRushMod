using Cysharp.Threading.Tasks;
using ItemStatsSystem.Stats;
using ItemStatsSystem;
using Duckov.Utilities;
using Duckov.Buffs;
using System.Collections;
// ============================================================================
// ZombieModeRewardCatalogAndSelection.cs - 丧尸模式奖励候选与选择宿主兼容桥
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void ShowZombieModeRewardSelection(int runId, bool bossNode, bool restEditorExpanded = false)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ShowZombieModeRewardSelection(runId, bossNode, restEditorExpanded);
        }

        internal Color GetZombieModeRewardAccentColor(ZombieModeRewardType rewardType)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeRewardAccentColor(rewardType) : Color.clear;
        }

        public IList<ZombieModeRewardType> GetZombieModeRewardOptions(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeRewardOptions(runId) : new List<ZombieModeRewardType>();
        }

        public int GetZombieModeRewardFreeRefreshes(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeRewardFreeRefreshes(runId) : 0;
        }

        public int GetZombieModePurificationPoints(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModePurificationPoints(runId) : 0;
        }

        public int GetZombieModeRewardPaidRefreshCost(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeRewardPaidRefreshCost(runId) : 0;
        }

        public bool IsZombieModeBossRewardNode(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.IsZombieModeBossRewardNode(runId);
        }

        public string GetZombieModeRewardTitle(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeRewardTitle(runId) : string.Empty;
        }

        public string GetZombieModeRewardDisplayText(int runId, ZombieModeRewardType rewardType)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeRewardDisplayText(runId, rewardType) : rewardType.ToString();
        }

        public void SelectZombieModeReward(int runId, ZombieModeRewardType rewardType)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.SelectZombieModeReward(runId, rewardType);
        }

        public void RefreshZombieModeRewardSelection(int runId, bool paid)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefreshZombieModeRewardSelection(runId, paid);
        }

        public bool SpendZombieModePurificationPoints(int cost, string reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.SpendZombieModePurificationPoints(cost, reason);
        }

        private void RefundZombieModePurificationPoints(int amount, string reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefundZombieModePurificationPoints(amount, reason);
        }

        internal int GetZombieModeBossRewardSelectionCountForRewardRuntimeModule(int wave)
        {
            return GetZombieModeBossRewardSelectionCount(wave);
        }

        internal float GetZombieModeBossRewardScaleForRewardRuntimeModule(int wave)
        {
            return GetZombieModeBossRewardScale(wave);
        }

        internal bool HasZombieModeRecyclableBackpackJunkForRewardRuntimeModule()
        {
            return HasZombieModeRecyclableBackpackJunk();
        }

        internal string GetZombieModeAttributeKeyForRewardRuntimeModule(ZombieModeRewardType rewardType)
        {
            return GetZombieModeAttributeKey(rewardType);
        }

        internal float GetZombieModeAttributeCapForRewardRuntimeModule(ZombieModeRewardType rewardType)
        {
            return GetZombieModeAttributeCap(rewardType);
        }

        internal ZombieModeTemporaryNpc FindZombieModeTemporaryNpcForRewardRuntimeModule(string serviceType)
        {
            return FindZombieModeTemporaryNpc(serviceType);
        }

        internal ZombieModeTemporaryRealNpcRecord FindZombieModeTemporaryRealNpcForRewardRuntimeModule(string npcType)
        {
            return FindZombieModeTemporaryRealNpc(npcType);
        }

        internal int GetZombieModeLifestealRewardChanceGainForRuntimeModule(ZombieModeRewardType rewardType)
        {
            return GetZombieModeLifestealRewardChanceGain(rewardType);
        }

        internal int GetZombieModeLifestealChanceCapForRewardRuntimeModule()
        {
            return ZombieModeRuntimeModule.ZombieModeLifestealChanceCapPercent;
        }

        internal int GetZombieModeOptionTradeoffPurificationCostForRewardRuntimeModule(ZombieModeRewardType rewardType)
        {
            return GetZombieModeOptionTradeoffPurificationCost(rewardType);
        }

        internal int CalculateZombieModePurificationRewardPointsForRewardRuntimeModule(bool bossNode)
        {
            return CalculateZombieModePurificationRewardPoints(bossNode);
        }

        internal string GetZombieModePendingTemporaryNpcServiceTypeForRewardRuntimeModule(ZombieModeRewardType rewardType)
        {
            return GetZombieModePendingTemporaryNpcServiceType(rewardType);
        }

        internal bool ApplyZombieModeRewardForRewardRuntimeModule(ZombieModeRewardType rewardType)
        {
            return ApplyZombieModeReward(rewardType);
        }

        internal void BeginZombieModeExtractionOpportunityForRewardRuntimeModule(int runId)
        {
            BeginZombieModeExtractionOpportunity(runId);
        }

        internal void BeginZombieModePreparationForRewardRuntimeModule(int runId, bool initial, bool extractionOpportunity)
        {
            BeginZombieModePreparation(runId, initial, extractionOpportunity);
        }

        internal void SpawnZombieModeTemporaryNpcForRewardRuntimeModule(int runId, string serviceType, bool bossNodeStock)
        {
            SpawnZombieModeTemporaryNpc(runId, serviceType, bossNodeStock);
        }

        internal void SpawnZombieModeTemporaryRealNpcForRewardRuntimeModule(int runId, string npcType)
        {
            SpawnZombieModeTemporaryRealNpc(runId, npcType);
        }
        private bool ApplyZombieModeReward(ZombieModeRewardType rewardType)
        {
            return zombieModeRuntimeModule.ApplyZombieModeReward(rewardType);
        }

        private string GetZombieModePendingTemporaryNpcServiceType(ZombieModeRewardType rewardType)
        {
            return zombieModeRuntimeModule.GetZombieModePendingTemporaryNpcServiceType(rewardType);
        }

        private void SpawnZombieModeTemporaryRealNpc(int runId, string npcType)
        {
            zombieModeRuntimeModule.SpawnZombieModeTemporaryRealNpc(runId, npcType);
        }

        private ZombieModeTemporaryRealNpcRecord FindZombieModeTemporaryRealNpc(string npcType)
        {
            return zombieModeRuntimeModule.FindZombieModeTemporaryRealNpc(npcType);
        }

        public bool IsZombieModeTemporaryRealNpc(Component component)
        {
            return zombieModeRuntimeModule.IsZombieModeTemporaryRealNpc(component);
        }

        public bool CanAffordZombieModePurificationPointsForRealNpc(Component component, int cost)
        {
            return zombieModeRuntimeModule.CanAffordZombieModePurificationPointsForRealNpc(component, cost);
        }

        public bool TrySpendZombieModePurificationPointsForRealNpc(Component component, int cost, string reason)
        {
            return zombieModeRuntimeModule.TrySpendZombieModePurificationPointsForRealNpc(component, cost, reason);
        }

        public void RefundZombieModePurificationPointsForRealNpc(Component component, int cost, bool shouldRefund)
        {
            zombieModeRuntimeModule.RefundZombieModePurificationPointsForRealNpc(component, cost, shouldRefund);
        }

        public int GetZombieModePurificationPointsForRealNpcUi(Component component)
        {
            return zombieModeRuntimeModule.GetZombieModePurificationPointsForRealNpcUi(component);
        }

        public string GetZombieModeNpcHealCurrencyLabel(Component component, int cost)
        {
            return zombieModeRuntimeModule.GetZombieModeNpcHealCurrencyLabel(component, cost);
        }

        private void RemoveZombieModeAttributeModifiers()
        {
            zombieModeRuntimeModule.RemoveZombieModeAttributeModifiers();
        }

        private void SpawnZombieModeTemporaryNpc(int runId, string serviceType, bool bossNodeStock)
        {
            zombieModeRuntimeModule.SpawnZombieModeTemporaryNpc(runId, serviceType, bossNodeStock);
        }

        private void TickZombieModeTemporaryNpcProtection()
        {
            zombieModeRuntimeModule.TickZombieModeTemporaryNpcProtection();
        }

        private void SpawnPendingZombieModeEliteSquad(int runId)
        {
            zombieModeRuntimeModule.SpawnPendingZombieModeEliteSquad(runId);
        }

        private void SettleZombieModeFailureInsuranceShell(int runId)
        {
            zombieModeRuntimeModule.SettleZombieModeFailureInsuranceShell(runId);
        }

        private string GetZombieModeAttributeKey(ZombieModeRewardType rewardType)
        {
            return zombieModeRuntimeModule.GetZombieModeAttributeKey(rewardType);
        }

        private float GetZombieModeAttributeCap(ZombieModeRewardType rewardType)
        {
            return zombieModeRuntimeModule.GetZombieModeAttributeCap(rewardType);
        }

        public void OpenZombieModeTemporaryNpcServiceUi(int runId, string serviceType)
        {
            zombieModeRuntimeModule.OpenZombieModeTemporaryNpcServiceUi(runId, serviceType);
        }

        private ZombieModeTemporaryNpc FindZombieModeTemporaryNpc(string serviceType)
        {
            return zombieModeRuntimeModule.FindZombieModeTemporaryNpc(serviceType);
        }

        public ZombieModeNpcCatalog.MerchantStockEntry[] GetZombieModeMerchantStock(int runId, string serviceType)
        {
            return zombieModeRuntimeModule.GetZombieModeMerchantStock(runId, serviceType);
        }

        public ZombieModeNpcCatalog.NurseServiceEntry[] GetZombieModeNurseServices(int runId, string serviceType)
        {
            return zombieModeRuntimeModule.GetZombieModeNurseServices(runId, serviceType);
        }

        public int GetZombieModeNpcServiceRemaining(int runId, string serviceType, int index)
        {
            return zombieModeRuntimeModule.GetZombieModeNpcServiceRemaining(runId, serviceType, index);
        }

        public int GetZombieModeNpcServicePrice(int runId, int basePrice)
        {
            return zombieModeRuntimeModule.GetZombieModeNpcServicePrice(runId, basePrice);
        }

        public bool TryPurchaseZombieModeMerchantStock(int runId, string serviceType, int stockIndex)
        {
            return zombieModeRuntimeModule.TryPurchaseZombieModeMerchantStock(runId, serviceType, stockIndex);
        }

        public bool TryUseZombieModeNurseService(int runId, string serviceType, int serviceIndex)
        {
            return zombieModeRuntimeModule.TryUseZombieModeNurseService(runId, serviceType, serviceIndex);
        }

        private int CalculateZombieModePurificationRewardPoints(bool bossNode)
        {
            return zombieModeRuntimeModule.CalculateZombieModePurificationRewardPoints(bossNode);
        }

        private void ClearZombieModeRewardShell()
        {
            zombieModeRuntimeModule.ClearZombieModeRewardShell();
        }

        private int GetZombieModeLifestealRewardChanceGain(ZombieModeRewardType rewardType)
        {
            return zombieModeRuntimeModule.GetZombieModeLifestealRewardChanceGain(rewardType);
        }

        private int GetZombieModeOptionTradeoffPurificationCost(ZombieModeRewardType rewardType)
        {
            return zombieModeRuntimeModule.GetZombieModeOptionTradeoffPurificationCost(rewardType);
        }

        private void RemoveZombieModeOptionRuntimeEffects()
        {
            zombieModeRuntimeModule.RemoveZombieModeOptionRuntimeEffects();
        }

        internal bool CanTriggerZombieModeProjectileTrailDamage(int runId)
        {
            return zombieModeRuntimeModule.CanTriggerZombieModeProjectileTrailDamage(runId);
        }

        internal void UnregisterZombieModePlayerProjectileRuntime(int projectileId)
        {
            zombieModeRuntimeModule.UnregisterZombieModePlayerProjectileRuntime(projectileId);
        }

        public void ApplyZombieModeProjectileRewardEffects(Projectile projectile)
        {
            zombieModeRuntimeModule.ApplyZombieModeProjectileRewardEffects(projectile);
        }

        private void HandleZombieModeOptionHealthHurt(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            zombieModeRuntimeModule.HandleZombieModeOptionHealthHurt(runId, health, damageInfo, victim, marker);
        }

        private void HandleZombieModeOptionHealthDead(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            zombieModeRuntimeModule.HandleZombieModeOptionHealthDead(runId, health, damageInfo, victim, marker);
        }

        private bool InitializeZombieModeContainersShell(int runId)
        {
            return zombieModeRuntimeModule.InitializeZombieModeContainersShell(runId);
        }

        private void UnlockZombieModeContainersForActiveRun(int runId)
        {
            zombieModeRuntimeModule.UnlockZombieModeContainersForActiveRun(runId);
        }

        private void TrySpawnZombieModeEnemyDrop(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)
        {
            zombieModeRuntimeModule.TrySpawnZombieModeEnemyDrop(runId, marker, position);
        }

        private void TrySpawnZombieModeBossDrop(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)
        {
            zombieModeRuntimeModule.TrySpawnZombieModeBossDrop(runId, marker, position);
        }

        private void TickZombieModeDropsAndPerformance(float deltaTime)
        {
            zombieModeRuntimeModule.TickZombieModeDropsAndPerformance(deltaTime);
        }

        private void CleanupZombieModeExpiredDropCandidates()
        {
            zombieModeRuntimeModule.CleanupZombieModeExpiredDropCandidates();
        }

        private void CleanupZombieModeExpiredDropCandidates(bool forceWaveCleanup)
        {
            zombieModeRuntimeModule.CleanupZombieModeExpiredDropCandidates(forceWaveCleanup);
        }

        private void RecycleZombieModeTemporaryNpcs(int runId)
        {
            zombieModeRuntimeModule.RecycleZombieModeTemporaryNpcs(runId);
        }

        private void RecycleZombieModeTemporaryRealNpcs(int runId)
        {
            zombieModeRuntimeModule.RecycleZombieModeTemporaryRealNpcs(runId);
        }

        private void RecycleZombieModeSafeZoneBoundTemporaryNpcs(int runId)
        {
            zombieModeRuntimeModule.RecycleZombieModeSafeZoneBoundTemporaryNpcs(runId);
        }

        private void RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(int runId)
        {
            zombieModeRuntimeModule.RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(runId);
        }

        private bool HasZombieModeRecyclableBackpackJunk()
        {
            return zombieModeRuntimeModule.HasZombieModeRecyclableBackpackJunk();
        }

        private int FindRandomItemTypeByTags(string[] requiredTags, int minQuality, int maxQuality)
        {
            return zombieModeRuntimeModule.FindRandomItemTypeByTags(requiredTags, minQuality, maxQuality);
        }

        private bool IsZombieModeRewardCandidateAllowed(int typeId, string[] requiredTags)
        {
            return zombieModeRuntimeModule.IsZombieModeRewardCandidateAllowed(typeId, requiredTags);
        }

        private int[] GetZombieModeRewardCandidateIds(string[] requiredTags, int minQuality, int maxQuality)
        {
            return zombieModeRuntimeModule.GetZombieModeRewardCandidateIds(requiredTags, minQuality, maxQuality);
        }

        private Tag FindZombieModeTagByName(string tagName)
        {
            return zombieModeRuntimeModule.FindZombieModeTagByName(tagName);
        }

        private bool TryGiveZombieModeWaveClearHealingItem()
        {
            return zombieModeRuntimeModule.TryGiveZombieModeWaveClearHealingItem();
        }

        private int TryGiveRandomItemByTagsTimes(string[] requiredTags, int minQuality, int maxQuality, int times)
        {
            return zombieModeRuntimeModule.TryGiveRandomItemByTagsTimes(requiredTags, minQuality, maxQuality, times);
        }

        private string TryReadZombieModeItemCaliber(Item item)
        {
            return zombieModeRuntimeModule.TryReadZombieModeItemCaliber(item);
        }

        private bool TryGiveZombieModeStarterAmmo(string caliber, int totalCount)
        {
            return zombieModeRuntimeModule.TryGiveZombieModeStarterAmmo(caliber, totalCount);
        }

        private bool TryGiveRandomItemByTags(string[] requiredTags, int minQuality, int maxQuality)
        {
            return zombieModeRuntimeModule.TryGiveRandomItemByTags(requiredTags, minQuality, maxQuality);
        }

        internal void ShowZombieModeRewardSelectionPresentationForRuntimeModule(int runId, bool restEditorExpanded)
        {
            zombieModeRuntimeModule.ShowZombieModeRewardSelectionPresentationForRuntimeModule(runId, restEditorExpanded);
        }

        private void SetZombieModeEnemyTargetToMainPlayer(AICharacterController ai)
        {
            ZombieModeRuntimeModule.SetZombieModeEnemyTargetToMainPlayer(ai);
        }

        internal void RefreshZombieModeGravityWellTargets(int runId, Vector3 origin, float radius, float pullStrength)
        {
            zombieModeRuntimeModule.RefreshZombieModeGravityWellTargets(runId, origin, radius, pullStrength);
        }

        private CharacterMainControl TryFindZombieModeNearestEnemyTarget(int runId, CharacterMainControl exclude, float radius)
        {
            return zombieModeRuntimeModule.TryFindZombieModeNearestEnemyTarget(runId, exclude, radius);
        }

        private int CollectZombieModeRuntimeEnemyMarkers(
            int runId,
            List<ZombieModeEnemyRuntimeMarker> results,
            bool includeBosses)
        {
            return zombieModeRuntimeModule.CollectZombieModeRuntimeEnemyMarkers(runId, results, includeBosses);
        }
    }
}
