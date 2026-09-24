namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private static WavesArenaRuntimeModule current;
        private int waveGeneration;
        private InfiniteHellMilestoneDelivery milestoneDelivery;
        private static System.Reflection.FieldInfo _cachedCreatedField = null;
        private static bool _createdFieldCached = false;
        internal bool SpawnersDisabled { get; set; }
        internal System.Collections.Generic.List<EnemyPresetInfo> EnemyPresets { get; set; } =
            new System.Collections.Generic.List<EnemyPresetInfo>();
        internal int EnemyPresetInitializationScanCount { get; set; }
        internal float MinBossBaseHealth { get; set; } = 100f;
        internal float MaxBossBaseHealth { get; set; } = 100f;
        internal static bool EnemyPresetsInitialized { get; set; }

        internal bool WaitingForNextWave { get; set; }
        internal float WaveCountdown { get; set; }
        internal int LastWaveCountdownSeconds { get; set; } = -1;
        internal float WaveIntegrityCheckTimer { get; set; }
        internal float DaXingXingCleanTimer { get; set; }
        internal int TotalEnemies { get; set; }
        internal int DefeatedEnemies { get; set; }
        internal string NextWaveBossName { get; set; }
        internal int BossesPerWave { get; set; } = 1;
        internal int BossesInCurrentWaveTotal { get; set; }
        internal int BossesInCurrentWaveRemaining { get; set; }
        internal readonly System.Collections.Generic.List<UnityEngine.MonoBehaviour> CurrentWaveBosses =
            new System.Collections.Generic.List<UnityEngine.MonoBehaviour>();
        internal bool InfiniteHellMode { get; set; }
        internal int InfiniteHellWaveIndex { get; set; }
        internal long InfiniteHellCashPool { get; set; }
        // 已发放的最高里程碑阶数（每100波递进，0表示尚未发放任何里程碑奖励）
        internal int InfiniteHellMilestoneRewardTier { get; set; }
        internal long InfiniteHellWaveCashThisWave { get; set; }
        internal readonly System.Collections.Generic.List<int> InfiniteHellHighQualityItemPool =
            new System.Collections.Generic.List<int>(256);
        internal readonly System.Collections.Generic.HashSet<int> InfiniteHellHighQualityCandidateIdScratch =
            new System.Collections.Generic.HashSet<int>();
        internal readonly System.Collections.Generic.List<int> InfiniteHellHighQualityPreferredScratch =
            new System.Collections.Generic.List<int>(128);
        internal readonly System.Collections.Generic.List<int> InfiniteHellHighQualityFallbackScratch =
            new System.Collections.Generic.List<int>(128);
        internal bool InfiniteHellHighQualityItemPoolInitialized { get; set; }

        internal static void ResetMilestones(ModBehaviour expectedOwner)
        {
            if (current != null && current.owner == expectedOwner)
                current.milestoneDelivery = new InfiniteHellMilestoneDelivery();
        }

        internal static void EnqueueMilestone(ModBehaviour expectedOwner, int tier, UnityEngine.Vector3 position)
        {
            if (current != null && current.owner == expectedOwner && current.milestoneDelivery != null)
                current.milestoneDelivery.Enqueue(tier, position);
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (milestoneDelivery != null) milestoneDelivery.Tick(owner);
        }


        internal static System.Func<bool> CaptureValidity(ModBehaviour expectedOwner, bool beginWave, bool requireActive)
        {
            WavesArenaRuntimeModule runtime = current;
            if (runtime == null || runtime.owner != expectedOwner) return () => false;
            if (beginWave) runtime.waveGeneration++;
            int generation = runtime.waveGeneration;
            int sceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            CharacterMainControl player = CharacterMainControl.Main;
            return () => runtime.owner != null && runtime.owner == expectedOwner
                && current == runtime && runtime.waveGeneration == generation
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle == sceneHandle
                && player != null && CharacterMainControl.Main == player
                && player.Health != null && !player.Health.IsDead
                && (!requireActive || expectedOwner.IsActive);
        }

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            waveGeneration++;
            milestoneDelivery = null;
        }

        public override string ModuleName
        {
            get { return "WavesArena"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            current = this;
        }

        public override void OnDestroy()
        {
            waveGeneration++;
            if (current == this) current = null;
            milestoneDelivery = null;
            owner = null;
        }
    }
}
