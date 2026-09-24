using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private System.Collections.Generic.List<EnemyPresetInfo> enemyPresets
        {
            get { return wavesArenaRuntime.EnemyPresets; }
            set { wavesArenaRuntime.EnemyPresets = value; }
        }

        private int _enemyPresetInitializationScanCount
        {
            get { return wavesArenaRuntime.EnemyPresetInitializationScanCount; }
            set { wavesArenaRuntime.EnemyPresetInitializationScanCount = value; }
        }

        private float minBossBaseHealth
        {
            get { return wavesArenaRuntime.MinBossBaseHealth; }
            set { wavesArenaRuntime.MinBossBaseHealth = value; }
        }

        private float maxBossBaseHealth
        {
            get { return wavesArenaRuntime.MaxBossBaseHealth; }
            set { wavesArenaRuntime.MaxBossBaseHealth = value; }
        }

        private static bool _enemyPresetsInitialized
        {
            get { return WavesArenaRuntimeModule.EnemyPresetsInitialized; }
            set { WavesArenaRuntimeModule.EnemyPresetsInitialized = value; }
        }

        private bool waitingForNextWave
        {
            get { return wavesArenaRuntime.WaitingForNextWave; }
            set { wavesArenaRuntime.WaitingForNextWave = value; }
        }

        private float waveCountdown
        {
            get { return wavesArenaRuntime.WaveCountdown; }
            set { wavesArenaRuntime.WaveCountdown = value; }
        }

        private int lastWaveCountdownSeconds
        {
            get { return wavesArenaRuntime.LastWaveCountdownSeconds; }
            set { wavesArenaRuntime.LastWaveCountdownSeconds = value; }
        }

        private float waveIntegrityCheckTimer
        {
            get { return wavesArenaRuntime.WaveIntegrityCheckTimer; }
            set { wavesArenaRuntime.WaveIntegrityCheckTimer = value; }
        }

        private float daXingXingCleanTimer
        {
            get { return wavesArenaRuntime.DaXingXingCleanTimer; }
            set { wavesArenaRuntime.DaXingXingCleanTimer = value; }
        }

        private int totalEnemies
        {
            get { return wavesArenaRuntime.TotalEnemies; }
            set { wavesArenaRuntime.TotalEnemies = value; }
        }

        private int defeatedEnemies
        {
            get { return wavesArenaRuntime.DefeatedEnemies; }
            set { wavesArenaRuntime.DefeatedEnemies = value; }
        }

        private string nextWaveBossName
        {
            get { return wavesArenaRuntime.NextWaveBossName; }
            set { wavesArenaRuntime.NextWaveBossName = value; }
        }

        private int bossesPerWave
        {
            get { return wavesArenaRuntime.BossesPerWave; }
            set { wavesArenaRuntime.BossesPerWave = value; }
        }

        private int bossesInCurrentWaveTotal
        {
            get { return wavesArenaRuntime.BossesInCurrentWaveTotal; }
            set { wavesArenaRuntime.BossesInCurrentWaveTotal = value; }
        }

        private int bossesInCurrentWaveRemaining
        {
            get { return wavesArenaRuntime.BossesInCurrentWaveRemaining; }
            set { wavesArenaRuntime.BossesInCurrentWaveRemaining = value; }
        }

        private System.Collections.Generic.List<MonoBehaviour> currentWaveBosses
        {
            get { return wavesArenaRuntime.CurrentWaveBosses; }
        }

        private void DisableAllSpawners()
        {
            wavesArenaRuntime.DisableAllSpawners();
        }

        internal void TryFixStuckWaveIfNoBossAlive()
        {
            wavesArenaRuntime.TryFixStuckWaveIfNoBossAlive();
        }

        internal bool TickWavesArenaRuntime(float deltaTime)
        {
            return wavesArenaRuntime.TickWavesArenaRuntime(deltaTime);
        }

        internal void TickWavesArenaBossCleanupRuntime(float deltaTime)
        {
            wavesArenaRuntime.TickWavesArenaBossCleanupRuntime(deltaTime);
        }

        /// <summary>
        /// Mode G 运行状态的全 partial 共享 no-throw 读取（异常视为未运行，保持 Legacy 行为）。
        /// 只反映 lifecycle（LifecyclePhase != None），绝不包含 late sink quarantine。
        /// </summary>
        internal static bool IsModeGRunInProgressSafe()
        {
            try
            {
                return ModeGRuntimeGates.IsModeGRunInProgress;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Mode H 是否正在进行（no-throw，未运行时恒 false）。
        /// 供 Legacy 清怪循环等旧路径做加法分支使用（设计提案 §19.2）。
        /// </summary>
        internal static bool IsModeHRunInProgressSafe()
        {
            try
            {
                return ModeHRuntimeGates.IsModeHRunOwnerActive;
            }
            catch
            {
                return false;
            }
        }
    }
}
