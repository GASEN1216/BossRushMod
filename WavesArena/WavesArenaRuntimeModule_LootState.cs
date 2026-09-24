using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal VictoryRewardShadowCrateController _activeVictoryRewardShadowCrateController = null;
        internal bool _difficultyRewardSpawnPositionOverrideActive = false;
        internal Vector3 _difficultyRewardSpawnPositionOverride = Vector3.zero;
        internal readonly Dictionary<CharacterMainControl, float> bossSpawnTimes = new Dictionary<CharacterMainControl, float>();
        internal readonly Dictionary<CharacterMainControl, int> bossOriginalLootCounts = new Dictionary<CharacterMainControl, int>();
        internal readonly HashSet<CharacterMainControl> bossRushLootboxPathBosses = new HashSet<CharacterMainControl>();
        internal readonly Dictionary<CharacterMainControl, Action<DamageInfo>> trackedBossLootHooks
            = new Dictionary<CharacterMainControl, Action<DamageInfo>>();
        internal readonly List<CharacterMainControl> bossRushLootboxPathTrackedBossScratch = new List<CharacterMainControl>(16);
        internal readonly List<CharacterMainControl> bossRushLootboxPathStaleBossScratch = new List<CharacterMainControl>(4);
        internal readonly List<int> legacyBossGuaranteeCandidateScratch = new List<int>(1024);
        internal readonly Dictionary<int, List<int>> legacyBossGuaranteeQualityBucketsScratch = new Dictionary<int, List<int>>(8);
        internal readonly List<Item> difficultyRewardPreferredScratch = new List<Item>(32);
        internal readonly List<Item> difficultyRewardFallbackHighQualityScratch = new List<Item>(32);
        internal readonly List<Item> difficultyRewardKeepScratch = new List<Item>(32);
        internal readonly Dictionary<string, float> lootNextWarningLogTimes = new Dictionary<string, float>();
    }
}
