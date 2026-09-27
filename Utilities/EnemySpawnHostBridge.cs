using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private readonly ModeEFSpawnPostprocessScheduler spawnPostprocess = new ModeEFSpawnPostprocessScheduler();
        private EnemySpawnRuntime enemySpawnRuntime;

        private void BindSpawnPostprocessServices()
        {
            spawnPostprocess.BindServices(modeDItemPool.MaterializeNextSharedModeEnemyEquipmentPlanStep,
                character => ApplyBossStatMultiplier(character),
                (character, count) => wavesArenaRuntime.RegisterBossRandomLootTracking(character, count),
                modeDItemPool.CleanupSharedModeEnemyEquipmentMaterializationPlan,
                wavesArenaRuntime.ClearBossRandomLootTracking);
            enemySpawnRuntime = new EnemySpawnRuntime(spawnPostprocess);
            enemySpawnRuntime.BindPresetQueries(() => cachedCharacterPresets,
                GetRandomBossPreset, GetRandomMinionPreset,
                () => modeEDragonDescendantSpawned, () => modeEDragonKingSpawned);
            enemySpawnRuntime.BindSpecialBossServices(IsDragonDescendantPreset, IsDragonKingPreset,
                IsPhantomWitchPreset, IsManagedBossPreset,
                (position, child, notify, defer, nonWave, active) => SpawnDragonDescendant(position, child, notify, defer, nonWave, active),
                (position, notify, defer, nonWave, active) => SpawnDragonKing(position, notify, defer, nonWave, active),
                (position, notify, defer, nonWave, active) => SpawnPhantomWitch(position,
                    notifyBossRushOnFailure: notify, deferActivationUntilNextFrame: defer,
                    isNonWaveSpawn: nonWave, isActiveCheck: active),
                CleanupCancelledDragonDescendant, character => CleanupCancelledDragonKing(character),
                CleanupFailedPhantomWitchSpawn);
            enemySpawnRuntime.BindEquipmentServices(modeDRuntime.NormalizeDamageMultiplier,
                modeDItemPool.EquipEnemyForModeD, modeDItemPool.CreateSharedModeEnemyEquipmentMaterializationPlan,
                character => ApplyBossStatMultiplier(character),
                (character, count) => wavesArenaRuntime.RegisterBossRandomLootTracking(character, count));
            enemySpawnRuntime.BindOwnedEnemyTracking(wavesArenaRuntime.IsDaXingXingPreset, () => wavesArenaRuntime.OwnedDaXingXing);
        }

        internal void EnsureCharacterPresetsCacheReady() { modeDRuntime.EnsureCharacterPresetsCacheReady(); }
        internal void ClearModeEFSpawnPostprocessScheduler() { spawnPostprocess.ClearModeEFSpawnPostprocessScheduler(); }
        private void TickModeEFSpawnPostprocessScheduler() { spawnPostprocess.TickModeEFSpawnPostprocessScheduler(); }

        internal static Func<EnemyPresetInfo, Vector3, object, bool, UniTask<ManagedBossPrepareResult>> ManagedBossSpawnDispatcher
        {
            get { return EnemySpawnRuntime.ManagedBossSpawnDispatcher; }
            set { EnemySpawnRuntime.ManagedBossSpawnDispatcher = value; }
        }

        internal void SpawnEnemyCore(
            EnemyPresetInfo preset,
            Vector3 position,
            bool isBoss,
            Func<bool> isActiveCheck,
            Action<EnemySpawnContext> onSpawned,
            Action onFailed = null,
            int waveIndex = 1,
            bool skipDragonDescendant = false,
            bool skipDragonKing = false,
            bool applyEquipment = true,
            bool applyBossMultiplier = true,
            CharacterRandomPreset directPreset = null,
            bool skipBossRushLootTracking = false,
            bool normalizeDamageMultiplier = true,
            bool deferActivationUntilNextFrame = false,
            Func<EnemySpawnContext, bool> onCommit = null)
        { enemySpawnRuntime.SpawnEnemyCore(preset, position, isBoss, isActiveCheck, onSpawned, onFailed, waveIndex, skipDragonDescendant, skipDragonKing, applyEquipment, applyBossMultiplier, directPreset, skipBossRushLootTracking, normalizeDamageMultiplier, deferActivationUntilNextFrame, onCommit); }

        internal UniTask<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync(
            EnemyPresetInfo preset,
            Vector3 position,
            bool isBoss,
            Func<bool> isActiveCheck,
            int waveIndex = 1,
            bool skipDragonDescendant = false,
            bool skipDragonKing = false,
            bool applyEquipment = true,
            bool applyBossMultiplier = true,
            CharacterRandomPreset directPreset = null,
            bool skipBossRushLootTracking = false,
            bool normalizeDamageMultiplier = true,
            bool deferActivationUntilNextFrame = false,
            Func<EnemySpawnContext, bool> onCommit = null,
            EnemySpawnCoreOptions options = null)
        { return enemySpawnRuntime.SpawnEnemyCoreInternalAsync(preset, position, isBoss, isActiveCheck, waveIndex, skipDragonDescendant, skipDragonKing, applyEquipment, applyBossMultiplier, directPreset, skipBossRushLootTracking, normalizeDamageMultiplier, deferActivationUntilNextFrame, onCommit, options); }

    }
}
