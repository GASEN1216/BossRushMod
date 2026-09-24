// ============================================================================
// WavesArena.cs - 波次与竞技场管理
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的波次系统和竞技场逻辑，包括：
//   - 波次敌人生成和管理
//   - 玩家传送到官方挑战场景
//   - 波次间隔倒计时
//
// 主要功能：
//   - StartBossRush: 开始 BossRush 模式
//   - TeleportToBossRushAsync: 异步传送到竞技场
//   - SpawnNextEnemy: 生成下一波敌人
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
    /// <summary>
    /// 波次与竞技场管理模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static HashSet<string> EarlyWaveExcludedBosses { get { return WavesArenaRuntimeModule.EarlyWaveExcludedBosses; } }

        private void EnsureEarlyWavesNoStrongBoss()
        {
            wavesArenaRuntime.EnsureEarlyWavesNoStrongBoss();
        }


        public void StartNextWaveCountdown(bool showInitialBanner = true, bool suppressImmediateRepeatBanner = false)
        {
            wavesArenaRuntime.StartNextWaveCountdown(showInitialBanner, suppressImmediateRepeatBanner);
        }

        internal void ShowNextWaveCountdownBanner(int secondsInt)
        {
            wavesArenaRuntime.ShowNextWaveCountdownBanner(secondsInt);
        }

        /// <summary>
        /// 敌人死亡事件处理（带DamageInfo参数）
        /// <para>仅用于普通模式（弹指可灭/有点意思/无间炼狱），Mode D 有独立的死亡处理逻辑</para>
        /// </summary>
        private void OnEnemyDiedWithDamageInfo(Health deadHealth, DamageInfo damageInfo)
        {
            wavesArenaRuntime.OnEnemyDiedWithDamageInfo(deadHealth, damageInfo);
        }

        private void HandleBossDeath(CharacterMainControl bossMain, DamageInfo damageInfo)
        {
            wavesArenaRuntime.HandleBossDeath(bossMain, damageInfo);
        }

        internal void ProceedAfterWaveFinished() { wavesArenaRuntime.ProceedAfterWaveFinished(); }
        private void OnBossSpawnFailed(EnemyPresetInfo preset) { wavesArenaRuntime.OnBossSpawnFailed(preset); }
        internal void UnregisterEnemyRecoveryForArena(CharacterMainControl boss) { UnregisterEnemyRecovery(boss); }
        internal bool CheckBossKillAchievementsOnceForArena(CharacterMainControl boss) { return CheckBossKillAchievementsOnce(boss); }
        internal bool ArenaUsesInteractBetweenWaves { get { return config != null && config.useInteractBetweenWaves; } }

        private void InitializeEnemyPresets() { wavesArenaRuntime.InitializeEnemyPresets(); }
        internal bool EnsureEnemyPresetsReadyForGameplayCatalogs() { return wavesArenaRuntime.EnsureEnemyPresetsReadyForGameplayCatalogs(); }
        internal int EnemyPresetInitializationScanCount { get { return wavesArenaRuntime.EnemyPresetInitializationScanCount; } }
        private EnemyPresetInfo PickRandomEnemyForInfiniteHell() { return wavesArenaRuntime.PickRandomEnemyForInfiniteHell(); }
        private static bool IsRuntimeCharacterPresetClone(CharacterRandomPreset preset) { return WavesArenaRuntimeModule.IsRuntimeCharacterPresetClone(preset); }
        private string GetLocalizedCharacterName(string nameKey) { return wavesArenaRuntime.GetLocalizedCharacterName(nameKey); }

        internal void ResetBossPoolFilterStateForArena() { ResetBossPoolFilterStateForEnemyPresetRefresh(); }
        internal bool IsBossPoolFilterInitializedForArena { get { return bossPoolFilterInitialized; } }
        internal void InitializeBossPoolFilterForArena() { InitializeBossPoolFilter(); }
        internal void RegisterDragonDescendantPresetForArena() { RegisterDragonDescendantPreset(); }
        internal void RegisterDragonKingPresetForArena() { RegisterDragonKingPreset(); }
        internal void RegisterPhantomWitchPresetForArena() { RegisterPhantomWitchPreset(); }
        internal bool IsManagedBossPresetForArena(EnemyPresetInfo preset) { return IsManagedBossPreset(preset); }
        internal void NotifyArenaPresetCatalogsRefreshed()
        {
            if (PetNestRuntime != null) PetNestRuntime.NotifyEnemyPresetsRefreshed();
            if (CodexRuntime != null) CodexRuntime.NotifyEnemyPresetsRefreshed();
        }

    }
}
