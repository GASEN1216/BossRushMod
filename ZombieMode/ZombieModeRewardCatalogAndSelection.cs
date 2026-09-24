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

        internal void ShowZombieModeRewardSelectionPresentationForRuntimeModule(int runId, bool restEditorExpanded)
        {
            ZombieModeRewardSelectionView current = zombieModeRewardUiRoot != null
                ? zombieModeRewardUiRoot.GetComponent<ZombieModeRewardSelectionView>()
                : null;
            if (current != null && current.TryRebuild(runId, restEditorExpanded)) return;
            ClearZombieModeRewardShell();
            zombieModeRewardUiRoot = new GameObject("ZombieMode_RewardSelection");
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.RewardUi, zombieModeRewardUiRoot, zombieModeRewardUiRoot, null);
            ZombieModeRewardSelectionView view = zombieModeRewardUiRoot.AddComponent<ZombieModeRewardSelectionView>();
            view.Initialize(runId, this, restEditorExpanded);
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
            return ZombieModeLifestealChanceCapPercent;
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
    }
}
