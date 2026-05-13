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

            s_cachedQuestTag = null;
            s_questTagSearched = false;
            s_questTagPermanentlyMissing = false;

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
