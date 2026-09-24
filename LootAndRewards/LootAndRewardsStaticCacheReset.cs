using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal static void ResetLootAndRewardsStaticCaches()
        {
            CachedLootBoxTemplateWithLoader = null;
            CachedDifficultyRewardLootBoxTemplate = null;
            CachedVictoryRewardVisualLootBoxTemplate = null;

            // Quest tag 的反射缓存已收敛到共享的排除口径里，连同它一起复位。
            LootExcludeTagPolicy.ResetStaticCaches();

            EnemyPresetsInitialized = false;

            if (ItemValueCache != null)
            {
                ItemValueCache.Clear();
                ItemValueCache = null;
            }
            ItemValueCacheInitialized = false;
            ItemValueCacheInitializing = false;

            if (LegacyBossLootCandidateIds != null)
            {
                LegacyBossLootCandidateIds.Clear();
                LegacyBossLootCandidateIds = null;
            }

            if (LegacyBossLootCandidateIdsByQuality != null)
            {
                foreach (KeyValuePair<int, List<int>> pair in LegacyBossLootCandidateIdsByQuality)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.Clear();
                    }
                }
                LegacyBossLootCandidateIdsByQuality.Clear();
                LegacyBossLootCandidateIdsByQuality = null;
            }
            LegacyBossLootCandidateCacheInitialized = false;
        }
    }
}
