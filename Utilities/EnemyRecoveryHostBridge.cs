using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private readonly EnemyRecoveryMonitor enemyRecoveryMonitor = new EnemyRecoveryMonitor();

        private void BindEnemyRecoveryServices(ZombieModeRuntimeModule zombieRuntime)
        {
            zombieRuntime.BindEnemyRecoveryMonitor(enemyRecoveryMonitor);
            wavesArenaRuntime.BindEnemyRecoveryMonitor(enemyRecoveryMonitor);
            enemyRecoveryMonitor.BindModeQueries(() => modeDActive, () => modeEActive, () => modeFActive,
                () => IsActive, () => IsZombieModeActive, () => ModeGRuntimeGates.IsModeGRunInProgress);
            enemyRecoveryMonitor.BindTrackedEnemies(() => modeDCurrentWaveEnemies, () => modeEAliveEnemies,
                () => modeFState.ActiveBosses, ModeGRuntimeGates.GetTrackedBosses,
                zombieRuntime.MonitorZombieModeEnemyRecovery, wavesArenaRuntime.MonitorNormalBossRushRecovery);
            enemyRecoveryMonitor.BindSpawnPositions(() => modeESpawnAllocation, GetModeEFlattenedSpawnPoints,
                GetCurrentSceneSpawnPoints, position => GenerateFallbackSpawnPointsAroundPlayer(position),
                zombieRuntime.AppendZombieModeRecoverySpawnCandidates, zombieRuntime.TryGetZombieModeReliableSpawnPosition);
            enemyRecoveryMonitor.BindRecoveryPolicies(marker => ((ZombieModeEnemyRuntimeMarker)marker).IsBoss,
                () => ZombieModeTuning.NormalZombieDistantRecoveryDistance,
                () => ZombieModeTuning.NormalZombieDistantRecoveryDelaySeconds,
                (go, marker) => ZombieModeRuntimeModule.GetZombieModeEnemyAI(go, (ZombieModeEnemyRuntimeMarker)marker),
                ApplyModeFPressureToBoss);
        }

        private void ClearEnemyRecoveryMonitorState() { enemyRecoveryMonitor.ClearEnemyRecoveryMonitorState(); }
        private void RegisterEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchorPosition)
        { enemyRecoveryMonitor.RegisterEnemyRecoveryAnchor(enemy, anchorPosition); }
        private void UnregisterEnemyRecovery(CharacterMainControl enemy) { enemyRecoveryMonitor.UnregisterEnemyRecovery(enemy); }
        private void UpdateEnemyRecoveryMonitor() { enemyRecoveryMonitor.UpdateEnemyRecoveryMonitor(); }
    }
}
