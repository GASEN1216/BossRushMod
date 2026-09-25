using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void RegisterZombieModeEventListeners(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RegisterZombieModeEventListeners(runId);
        }

        private void BeginZombieModePreparation(int runId, bool initial, bool extractionOpportunity)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.BeginZombieModePreparation(runId, initial, extractionOpportunity);
        }

        private void TickZombieModeWaveController(float deltaTime)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeWaveController(deltaTime);
        }

        private void StartZombieModeWave(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.StartZombieModeWave(runId);
        }

        private bool IsZombieModeAmbientZombieSpawnPhase(ZombieModeCombatPhase phase)
        {
            return ZombieModeRuntimeModule.IsZombieModeAmbientZombieSpawnPhase(phase);
        }

        private int GetZombieModeAmbientPressureTarget()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeAmbientPressureTarget() : 0;
        }

        private int GetZombieModeWavePressureTarget(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeWavePressureTarget(wave);
        }

        private int GetZombieModePacingWave()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null
                ? module.GetZombieModePacingWave()
                : Mathf.Max(1, zombieModeRunState.CurrentWave + (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat ? 0 : 1));
        }

        private static int GetZombieModeWaveCycleIndex(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeWaveCycleIndex(wave);
        }

        private static float GetZombieModeBossHealthScale(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossHealthScale(wave);
        }

        private static int GetZombieModeBossCountForWave(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossCountForWave(wave);
        }

        private static float GetZombieModeBossDamageScale(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossDamageScale(wave);
        }

        private static float GetZombieModeBossRewardScale(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossRewardScale(wave);
        }

        private static int GetZombieModeBossRewardSelectionCount(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossRewardSelectionCount(wave);
        }

        private static int GetZombieModeNormalWaveStageIndex(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeNormalWaveStageIndex(wave);
        }

        private float GetZombieModeWaveSpeedMultiplier(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeWaveSpeedMultiplier(wave);
        }

        private float GetZombieModeSpawnPointMinPlayerDistance()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeSpawnPointMinPlayerDistance() : ZombieModeTuning.EarlyWaveSpawnPointMinPlayerDistance;
        }

        private bool IsZombieModeBossWave(int wave)
        {
            return ZombieModeRuntimeModule.IsZombieModeBossWave(wave);
        }

        private void BeginZombieModeExtractionOpportunity(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.BeginZombieModeExtractionOpportunity(runId);
        }

        private void SpawnZombieModeDeathStars(int runId, Vector3 position, int totalValue, int starCount)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.SpawnZombieModeDeathStars(runId, position, totalValue, starCount);
        }

        internal bool IsZombieModePlayerInsideActiveSafeZoneForWaveRuntimeModule()
        {
            return zombieModeRuntimeModule.IsZombieModePlayerInsideActiveSafeZone();
        }

        internal void HandleZombieModeOptionHealthHurtForWaveRuntimeModule(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            HandleZombieModeOptionHealthHurt(runId, health, damageInfo, victim, marker);
        }

        internal void HandleZombieModeOptionHealthDeadForWaveRuntimeModule(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            HandleZombieModeOptionHealthDead(runId, health, damageInfo, victim, marker);
        }

        internal void TrySpawnZombieModeBossDropForWaveRuntimeModule(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)
        {
            TrySpawnZombieModeBossDrop(runId, marker, position);
        }

        internal void TrySpawnZombieModeEnemyDropForWaveRuntimeModule(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)
        {
            TrySpawnZombieModeEnemyDrop(runId, marker, position);
        }

        internal void CleanupZombieModeEnemiesNearPlayerSafeZoneForWaveRuntimeModule(int runId, string reason)
        {
            CleanupZombieModeEnemiesNearPlayerSafeZone(runId, reason);
        }

        internal void CleanupZombieModeExpiredDropCandidatesForWaveRuntimeModule(bool forceWaveCleanup)
        {
            CleanupZombieModeExpiredDropCandidates(forceWaveCleanup);
        }

        internal void SpawnPendingZombieModeEliteSquadForWaveRuntimeModule(int runId)
        {
            SpawnPendingZombieModeEliteSquad(runId);
        }

        internal Vector3 GetZombieModeSpawnPositionForWaveRuntimeModule()
        {
            return GetZombieModeSpawnPosition();
        }

        internal Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieForWaveRuntimeModule(
            int runId,
            Vector3 position,
            Func<bool> isSpawnPhaseStillAllowed)
        {
            return TrySpawnZombieModeNormalZombieAsync(
                runId,
                position,
                isSpawnPhaseStillAllowed: isSpawnPhaseStillAllowed);
        }

        internal ZombieModeBossKind GetZombieModeBossKindForWaveRuntimeModule(int bossIndex)
        {
            return GetZombieModeBossKindForIndex(bossIndex);
        }

        internal Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieForWaveRuntimeModule(
            int runId, Vector3 position, ZombieModeEnemyKind forcedEnemyKind, bool forceEnemyKind, Func<bool> isSpawnPhaseStillAllowed)
        {
            return TrySpawnZombieModeNormalZombieAsync(runId, position, forcedEnemyKind, forceEnemyKind, isSpawnPhaseStillAllowed);
        }

        internal Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeBossForWaveRuntimeModule(
            int runId,
            Vector3 position,
            ZombieModeBossKind kind)
        {
            return TrySpawnZombieModeBossAsync(runId, position, kind);
        }

        internal Vector3 GetZombieModeBossSpawnPositionForWaveRuntimeModule(int bossIndex)
        {
            return GetZombieModeBossSpawnPosition(bossIndex);
        }

        internal int CollectZombieModeRuntimeEnemyMarkersForWaveRuntimeModule(
            int runId,
            List<ZombieModeEnemyRuntimeMarker> results,
            bool includeBosses)
        {
            return CollectZombieModeRuntimeEnemyMarkers(runId, results, includeBosses);
        }

        internal bool TryGetZombieModeReliableSpawnPositionForWaveRuntimeModule(out Vector3 position)
        {
            return TryGetZombieModeReliableSpawnPosition(out position);
        }

        internal void RecycleZombieModeTemporaryNpcsForWaveRuntimeModule(int runId)
        {
            RecycleZombieModeTemporaryNpcs(runId);
        }

        internal void RecycleZombieModeTemporaryRealNpcsForWaveRuntimeModule(int runId)
        {
            RecycleZombieModeTemporaryRealNpcs(runId);
        }

        internal bool TryGiveZombieModeWaveClearHealingItemForWaveRuntimeModule()
        {
            return TryGiveZombieModeWaveClearHealingItem();
        }

        internal bool HasZombieModePendingPurificationStarsForWaveRuntimeModule()
        {
            return HasZombieModePendingPurificationStars();
        }

        internal void ForceCollectZombieModePendingPurificationStarsForWaveRuntimeModule(int runId)
        {
            ForceCollectZombieModePendingPurificationStars(runId);
        }

        internal bool CreateZombieModePurificationPointForWaveRuntimeModule(int runId, Vector3 position, int value)
        {
            return CreateZombieModePurificationPoint(runId, position, value);
        }
        private bool CollectZombieModeSpawnPoints(int runId)
        {
            return zombieModeRuntimeModule.CollectZombieModeSpawnPoints(runId);
        }

        private Vector3 GetZombieModeSpawnPosition()
        {
            return zombieModeRuntimeModule.GetZombieModeSpawnPosition();
        }

        private bool TryGetZombieModeReliableSpawnPosition(out Vector3 position)
        {
            return zombieModeRuntimeModule.TryGetZombieModeReliableSpawnPosition(out position);
        }

        private bool TryGetNearestZombieModeMapSpawnPositionToPlayer(out Vector3 position)
        {
            return zombieModeRuntimeModule.TryGetNearestZombieModeMapSpawnPositionToPlayer(out position);
        }

        private Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync(
            int runId,
            Vector3 position,
            ZombieModeEnemyKind forcedEnemyKind = ZombieModeEnemyKind.Normal,
            bool forceEnemyKind = false,
            System.Func<bool> isSpawnPhaseStillAllowed = null)
        {
            return zombieModeRuntimeModule.TrySpawnZombieModeNormalZombieAsync(runId, position, forcedEnemyKind, forceEnemyKind, isSpawnPhaseStillAllowed);
        }

        private Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync(int runId, Vector3 position, ZombieModeBossKind kind)
        {
            return zombieModeRuntimeModule.TrySpawnZombieModeBossAsync(runId, position, kind);
        }

        private ZombieModeBossKind GetZombieModeBossKindForIndex(int bossIndex)
        {
            return zombieModeRuntimeModule.GetZombieModeBossKindForIndex(bossIndex);
        }

        private Vector3 GetZombieModeBossSpawnPosition(int bossIndex)
        {
            return zombieModeRuntimeModule.GetZombieModeBossSpawnPosition(bossIndex);
        }

    }
}
