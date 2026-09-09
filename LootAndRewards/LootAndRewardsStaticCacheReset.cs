using System.Collections.Generic;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private static void ResetLootAndRewardsStaticCaches()
        {
            _cachedLootBoxTemplateWithLoader = null;
            _cachedDifficultyRewardLootBoxTemplate = null;
            _cachedVictoryRewardVisualLootBoxTemplate = null;

            // Quest tag 的反射缓存已收敛到共享的排除口径里，连同它一起复位。
            LootExcludeTagPolicy.ResetStaticCaches();

            _enemyPresetsInitialized = false;

            if (_itemValueCache != null)
            {
                _itemValueCache.Clear();
                _itemValueCache = null;
            }
            _itemValueCacheInitialized = false;
            _itemValueCacheInitializing = false;

            if (_legacyBossLootCandidateIds != null)
            {
                _legacyBossLootCandidateIds.Clear();
                _legacyBossLootCandidateIds = null;
            }

            if (_legacyBossLootCandidateIdsByQuality != null)
            {
                foreach (KeyValuePair<int, List<int>> pair in _legacyBossLootCandidateIdsByQuality)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.Clear();
                    }
                }
                _legacyBossLootCandidateIdsByQuality.Clear();
                _legacyBossLootCandidateIdsByQuality = null;
            }
            _legacyBossLootCandidateCacheInitialized = false;
        }
    }
}
