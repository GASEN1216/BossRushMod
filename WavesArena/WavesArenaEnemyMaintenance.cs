using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Duckov;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 强制杀死所有敌人（用于F10调试，忽略范围限制）
        /// 直接调用Health.Kill()而不是Destroy，确保触发死亡事件
        /// </summary>
        private void ForceKillAllEnemies() { wavesArenaRuntime.ForceKillAllEnemies(); }
        private void ClearEnemiesForBossRush() { wavesArenaRuntime.ClearEnemiesForBossRush(); }
        private IEnumerator ContinuousClearEnemiesUntilWaveStart()
        {
            return wavesArenaRuntime.ContinuousClearEnemiesUntilWaveStart();
        }

        internal void RefreshCharacterCacheForArena() { RefreshCharacterCache(); }
        internal List<CharacterMainControl> ArenaCharacterCache { get { return WavesArenaRuntimeModule.CharacterCache; } }
        internal bool ArenaCharacterCacheNeedsRefresh
        {
            get { return WavesArenaRuntimeModule.CharacterCacheNeedsRefresh; }
            set { WavesArenaRuntimeModule.CharacterCacheNeedsRefresh = value; }
        }
        internal List<GameObject> ArenaReusableDestroyList { get { return WavesArenaRuntimeModule.ReusableDestroyList; } }
        internal bool ArenaCenterSetForCleanup { get { return WavesArenaRuntimeModule.ArenaCenterSet; } }
        internal Vector3 ArenaCenterForCleanup { get { return WavesArenaRuntimeModule.ArenaCenter; } }
        internal CharacterRandomPreset ArenaEggSpawnPreset { get { return BossRushAudioRuntimeService.EggSpawnPreset; } }
        internal bool IsModeETrackedEnemyForArena(CharacterMainControl enemy) { return modeEFEnemyRegistry.IsTracked(enemy); }
        internal bool IsDeathWraithCharacterForArena(CharacterMainControl enemy)
        {
            return IsDeathWraithCharacter_DeathWraith(enemy);
        }
        internal WaitForSeconds ArenaSharedWait05s { get { return sharedWait05s; } }
    }
}
