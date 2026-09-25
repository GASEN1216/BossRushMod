// ============================================================================
// ZombieModePollution_RuntimeSkills.cs - special and elite runtime skill handling
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void HandleZombieModeSpecialDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.HandleZombieModeSpecialDeathEffects(runId, marker, character);
        }

        private void HandleZombieModeEliteDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.HandleZombieModeEliteDeathEffects(runId, marker, character);
        }

        private Cysharp.Threading.Tasks.UniTask SpawnZombieModeSmallSplitAsync(int runId, Vector3 position)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(Cysharp.Threading.Tasks.UniTask);
            }
            return module.SpawnZombieModeSmallSplitAsync(runId, position);
        }

        internal void TryExecuteZombieModeEnemyRuntimeSkill(ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryExecuteZombieModeEnemyRuntimeSkill(marker);
        }

        internal void RefreshZombieModeCommanderAuraTargets(
            int runId,
            CharacterMainControl commander,
            float radius,
            Dictionary<int, ZombieModeCommanderAuraTargetRuntime> trackedTargets)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefreshZombieModeCommanderAuraTargets(runId, commander, radius, trackedTargets);
        }

        private void StartZombieModeTelegraphedAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            float delay,
            string label,
            bool followSourcePosition = false)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.StartZombieModeTelegraphedAreaDamage(runId, source, origin, radius, damage, delay, label, followSourcePosition);
        }

        public void SpawnZombieModeDamageCloud(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float duration,
            float damagePerSecond,
            float tickInterval,
            string cloudName,
            Color cloudColor,
            bool followSourcePosition)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.SpawnZombieModeDamageCloud(runId, source, origin, radius, duration, damagePerSecond, tickInterval, cloudName, cloudColor, followSourcePosition);
        }

        public void TryExecuteZombieModeHarasserProjectileImpact(
            int runId,
            CharacterMainControl source,
            Vector3 impactPosition,
            float damage,
            float slowRadius,
            float slowPercent,
            float slowDuration)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryExecuteZombieModeHarasserProjectileImpact(runId, source, impactPosition, damage, slowRadius, slowPercent, slowDuration);
        }

        public void TryExecuteZombieModeTelegraphedAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryExecuteZombieModeTelegraphedAreaDamage(runId, source, origin, radius, damage);
        }

        public void TryApplyZombieModePlayerSlowInArea(int runId, Vector3 origin, float radius, float percent, float duration)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryApplyZombieModePlayerSlowInArea(runId, origin, radius, percent, duration);
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, Vector3 origin, float radius, float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeAreaDamageToPlayer(runId, origin, radius, damage);
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeAreaDamageToPlayer(runId, source, origin, radius, damage);
        }

        public void DealZombieModeExplosionAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            bool canHurtSelf = false)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeExplosionAreaDamage(runId, source, origin, radius, damage, canHurtSelf);
        }

        // Compatibility entry for callers outside the Zombie runtime module.
        internal Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieForRuntimeModule(
            int runId,
            Vector3 position,
            ZombieModeEnemyKind forcedEnemyKind,
            bool forceEnemyKind,
            System.Func<bool> isSpawnPhaseStillAllowed)
        {
            return TrySpawnZombieModeNormalZombieAsync(
                runId,
                position,
                forcedEnemyKind,
                forceEnemyKind,
                isSpawnPhaseStillAllowed);
        }

    }
}
