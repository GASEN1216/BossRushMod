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
        internal List<CharacterMainControl> ArenaCharacterCache { get { return _cachedCharacters; } }
        internal bool ArenaCharacterCacheNeedsRefresh
        {
            get { return _characterCacheNeedsRefresh; }
            set { _characterCacheNeedsRefresh = value; }
        }
        internal List<GameObject> ArenaReusableDestroyList { get { return _reusableDestroyList; } }
        internal bool ArenaCenterSetForCleanup { get { return _arenaCenterSet; } }
        internal Vector3 ArenaCenterForCleanup { get { return _arenaCenter; } }
        internal CharacterRandomPreset ArenaEggSpawnPreset { get { return eggSpawnPreset; } }
        internal bool IsModeETrackedEnemyForArena(CharacterMainControl enemy) { return modeEAliveEnemySet.Contains(enemy); }
        internal bool IsDeathWraithCharacterForArena(CharacterMainControl enemy)
        {
            return IsDeathWraithCharacter_DeathWraith(enemy);
        }
        internal WaitForSeconds ArenaSharedWait05s { get { return sharedWait05s; } }
    }
}
