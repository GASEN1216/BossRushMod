// ============================================================================
// LootAndRewardsInfiniteHell.cs - 无间炼狱奖励流程
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
        private void OnInfiniteHellWaveCompleted_LootAndRewards()
        {
            wavesArenaRuntime.OnInfiniteHellWaveCompleted_LootAndRewards();
        }

        internal void CheckInfiniteHellAchievementsForArena(int wave) { CheckInfiniteHellAchievements(wave); }
        internal GameObject ArenaRewardSignGameObject { get { return _bossRushSignGameObject; } }
        internal BossRushSignInteractable ArenaRewardSignInteract { get { return bossRushSignInteract; } }
        internal MonoBehaviour ArenaPlayerCharacter { get { return playerCharacter; } }
        internal bool UseInteractBetweenWavesForArena { get { return config != null && config.useInteractBetweenWaves; } }
        internal void LogLootWarningLimitedForArena(string key, string message, Exception e) { LogLootWarningLimited(key, message, e); }

        private int GetRandomInfiniteHellHighQualityRewardTypeID()
        {
            return wavesArenaRuntime.GetRandomInfiniteHellHighQualityRewardTypeID();
        }

    }
}
