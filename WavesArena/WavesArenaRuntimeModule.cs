using System.Collections.Generic;

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
        internal struct ItemValueCacheEntry
        {
            public int value;
            public int quality;
        }

        internal static InteractableLootbox CachedLootBoxTemplateWithLoader { get; set; }
        internal static InteractableLootbox CachedDifficultyRewardLootBoxTemplate { get; set; }
        internal static InteractableLootbox CachedVictoryRewardVisualLootBoxTemplate { get; set; }
        internal static System.Collections.Generic.Dictionary<int, ItemValueCacheEntry> ItemValueCache { get; set; }
        internal static bool ItemValueCacheInitialized { get; set; }
        internal static bool ItemValueCacheInitializing { get; set; }
        internal static System.Collections.Generic.List<int> LegacyBossLootCandidateIds { get; set; }
        internal static System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>> LegacyBossLootCandidateIdsByQuality { get; set; }
        internal static bool LegacyBossLootCandidateCacheInitialized { get; set; }
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
        internal int CurrentEnemyIndex { get; set; }
        internal UnityEngine.MonoBehaviour CurrentBoss { get; set; }
        internal UnityEngine.Vector3 DemoChallengeStartPosition { get; set; }
        internal readonly System.Collections.Generic.HashSet<CharacterMainControl> CountedDeadBosses =
            new System.Collections.Generic.HashSet<CharacterMainControl>();
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

        #region 前期波次Boss排除

        /// <summary>
        /// 前期波次需要排除的强力 Boss 名称列表
        /// 包括：口口口口、四骑士、龙裔遗族和焚天龙皇
        /// </summary>
        internal static readonly HashSet<string> EarlyWaveExcludedBosses = new HashSet<string>
        {
            "Cname_StormBoss1",    // 口口口口 或 四骑士
            "Cname_StormBoss2",    // 口口口口 或 四骑士
            "Cname_StormBoss3",    // 口口口口 或 四骑士
            "Cname_StormBoss4",    // 口口口口 或 四骑士
            "Cname_StormBoss5",    // 口口口口 或 四骑士
            "DragonDescendant",    // 龙裔遗族
            "boss_dragonking",     // 焚天龙皇
        };

        /// <summary>
        /// 检查是否是前期波次需要排除的强力Boss
        /// </summary>
        private bool IsEarlyWaveExcludedBoss(string bossName)
        {
            if (string.IsNullOrEmpty(bossName)) return false;
            return EarlyWaveExcludedBosses.Contains(bossName);
        }

        /// <summary>
        /// 预处理：确保前20波不出现强力Boss
        /// 在挑战开始时调用一次，将前20位中的强力Boss与后面的普通Boss交换
        /// </summary>
        internal void EnsureEarlyWavesNoStrongBoss()
        {
            if (EnemyPresets == null || EnemyPresets.Count <= 20) return;

            int swapCount = 0;
            int nextSwapTarget = 20; // 从第20位开始找可交换的普通Boss

            for (int i = 0; i < 20 && i < EnemyPresets.Count; i++)
            {
                if (!IsEarlyWaveExcludedBoss(EnemyPresets[i].name)) continue;

                // 找一个第10位之后的普通Boss来交换
                while (nextSwapTarget < EnemyPresets.Count &&
                       IsEarlyWaveExcludedBoss(EnemyPresets[nextSwapTarget].name))
                {
                    nextSwapTarget++;
                }

                if (nextSwapTarget >= EnemyPresets.Count) break; // 没有可交换的了

                // 交换
                var tmp = EnemyPresets[i];
                EnemyPresets[i] = EnemyPresets[nextSwapTarget];
                EnemyPresets[nextSwapTarget] = tmp;
                nextSwapTarget++;
                swapCount++;
            }

            if (swapCount > 0)
            {
                ModBehaviour.DevLog("[BossRush] 前20波强力Boss预处理完成，交换了 " + swapCount + " 个Boss");
            }
        }

        #endregion

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
            ReleaseBossRandomLootTrackingOnDestroy();
            if (current == this) current = null;
            milestoneDelivery = null;
            owner = null;
        }
    }
}
