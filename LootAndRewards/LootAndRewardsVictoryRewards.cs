// ============================================================================
// LootAndRewardsVictoryRewards.cs - 胜利奖励箱流程
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        internal CharacterMainControl GetPlayerCharacterForArenaLoot()
        {
            return playerCharacter as CharacterMainControl;
        }

        /// <summary>所有敌人击败后交给标准竞技场模块收尾。</summary>
        private void OnAllEnemiesDefeated_LootAndRewards()
        {
            wavesArenaRuntime.OnAllEnemiesDefeated_LootAndRewards();
        }

        internal void SetArenaVictoryActiveForArena(bool active) { bossRushArenaActive = active; }
        internal void UnsubscribeArenaBossDeathsForArena() { Health.OnDead -= OnEnemyDiedWithDamageInfo; }
        internal void CheckClearAchievementsForArena() { CheckClearAchievements(); }
        internal void TryCreateReturnInteractableForArena() { TryCreateReturnInteractable(); }

        private void StartVictoryRewardShadowCrate_LootAndRewards(int highQualityCount)
        {
            wavesArenaRuntime.StartVictoryRewardShadowCrate_LootAndRewards(highQualityCount);
        }

        private void CompleteVictoryRewardShadowCrate_LootAndRewards()
        {
            wavesArenaRuntime.CompleteVictoryRewardShadowCrate_LootAndRewards();
        }

        internal void NotifyVictoryRewardShadowCrateDisposed_LootAndRewards(VictoryRewardShadowCrateController controller)
        {
            wavesArenaRuntime.NotifyVictoryRewardShadowCrateDisposed_LootAndRewards(controller);
        }

        internal bool TryStartModeGRewardMaterialization_LootAndRewards(
            int[] fixedTypeIds, ItemStatsSystem.Inventory targetInventory,
            Action<int, ItemStatsSystem.Item, bool> onItemCommitted,
            Action<int, int, int> onAllCompleted, out string failureReason)
        {
            return wavesArenaRuntime.TryStartModeGRewardMaterialization_LootAndRewards(
                fixedTypeIds, targetInventory, onItemCommitted, onAllCompleted, out failureReason);
        }

        internal void CancelModeGRewardMaterialization_LootAndRewards()
        {
            wavesArenaRuntime.CancelModeGRewardMaterialization_LootAndRewards();
        }

        internal void SpawnDifficultyRewardLootboxAtWorldPosition_LootAndRewards(int highQualityCount, Vector3 worldPosition)
        {
            wavesArenaRuntime.SpawnDifficultyRewardLootboxAtWorldPosition_LootAndRewards(highQualityCount, worldPosition);
        }

        internal void SpawnDifficultyRewardLootboxFallback_LootAndRewards(int highQualityCount)
        {
            wavesArenaRuntime.SpawnDifficultyRewardLootboxFallback_LootAndRewards(highQualityCount);
        }

    }
}
