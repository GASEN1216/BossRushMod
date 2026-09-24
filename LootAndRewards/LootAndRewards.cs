// ============================================================================
// LootAndRewards.cs - 掉落与奖励系统
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的掉落和奖励系统，包括：
//   - Boss 掉落物生成和随机化
//   - 通关奖励箱生成
//   - 无间炼狱模式的现金池和特殊奖励
//   - 掉落物品黑名单管理
//
// 主要功能：
//   - OnInfiniteHellWaveCompleted: 无间炼狱单波完成处理
//   - OnAllEnemiesDefeated: 所有敌人击败后的通关处理
//   - SpawnDifficultyRewardLootbox: 生成通关奖励箱
//   - GetRandomInfiniteHellHighQualityRewardTypeID: 获取共享高品质奖励池物品
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

using ItemValueCacheEntry = BossRush.WavesArenaRuntimeModule.ItemValueCacheEntry;

namespace BossRush
{
    /// <summary>
    /// 掉落与奖励系统模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // ============================================================================
        // 掉落系统内部常量
        // ============================================================================

        /// <summary>普通品质最小值</summary>
        internal const int LOOT_LOW_QUALITY_MIN = 1;

        /// <summary>普通品质最大值</summary>
        internal const int LOOT_LOW_QUALITY_MAX = 4;

        /// <summary>高品质最小值</summary>
        internal const int LOOT_HIGH_QUALITY_MIN = 5;

        /// <summary>高品质最大值</summary>
        internal const int LOOT_HIGH_QUALITY_MAX = 8;

        /// <summary>血量加成系数（每100血量增加的高品质概率，0.05即5%）</summary>
        private const float LOOT_HEALTH_BONUS_RATE = 0.05f;

        /// <summary>击杀时间加成系数（最快击杀时的最大加成，0.1即10%）</summary>
        private const float LOOT_TIME_BONUS_RATE = 0.1f;

        /// <summary>原版 Boss 战利品 Q5+ 保底的最小 Boss 最大生命值门槛</summary>
        private const float LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH = 250f;

        // ============================================================================

        private VictoryRewardShadowCrateController _activeVictoryRewardShadowCrateController
        {
            get { return wavesArenaRuntime._activeVictoryRewardShadowCrateController; }
            set { wavesArenaRuntime._activeVictoryRewardShadowCrateController = value; }
        }
        private bool _difficultyRewardSpawnPositionOverrideActive
        {
            get { return wavesArenaRuntime._difficultyRewardSpawnPositionOverrideActive; }
            set { wavesArenaRuntime._difficultyRewardSpawnPositionOverrideActive = value; }
        }
        private Vector3 _difficultyRewardSpawnPositionOverride
        {
            get { return wavesArenaRuntime._difficultyRewardSpawnPositionOverride; }
            set { wavesArenaRuntime._difficultyRewardSpawnPositionOverride = value; }
        }

        /// <summary>
        /// 检查物品ID是否在掉落黑名单中
        /// </summary>
        private static bool IsItemBlacklisted(int itemId)
        {
            return LootBlacklistRegistry.Contains(itemId);
        }

        private void AddUniqueLootExcludeTag(List<Duckov.Utilities.Tag> excludeTags, Duckov.Utilities.Tag tag)
        {
            LootExcludeTagPolicy.AddUnique(excludeTags, tag);
        }

        // ============================================================
        // Quest tag 反射查找（唯一实现在 LootExcludeTagPolicy）
        // ============================================================
        // GameplayDataSettings.TagsData 没有 Quest 字段，但 AllTags 里有名为 Quest 的 Tag（2026-09-11 离线官方物品表 69 件带它），
        // 按名字查找。查找、缓存与「查不到」日志都收在 LootExcludeTagPolicy 里，
        // 这里只做转发，保持 ModeD 等既有调用点不变。
        private Duckov.Utilities.Tag TryFindQuestTag(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        {
            return LootExcludeTagPolicy.TryFindQuestTag(tagsData);
        }

        // 通用随机奖池的排除口径唯一定义在 LootExcludeTagPolicy：
        // Boss 奖励箱、通关奖励、空投与天空岛搜刮点共用同一份列表，避免各写各的漏排。
        internal List<Duckov.Utilities.Tag> BuildGeneralLootExcludeTags(Duckov.Utilities.GameplayDataSettings.TagsData tagsData, bool includeCharacterTag = false)
        {
            return LootExcludeTagPolicy.BuildExcludeTags(tagsData, includeCharacterTag);
        }

        internal void MergeGeneralLootExcludeTags(List<Duckov.Utilities.Tag> excludeList, Duckov.Utilities.GameplayDataSettings.TagsData tagsData, bool includeCharacterTag = false)
        {
            if (excludeList == null || tagsData == null)
            {
                return;
            }

            List<Duckov.Utilities.Tag> baseExclude = BuildGeneralLootExcludeTags(tagsData, includeCharacterTag);
            for (int i = 0; i < baseExclude.Count; i++)
            {
                AddUniqueLootExcludeTag(excludeList, baseExclude[i]);
            }
        }


        // [性能优化] 敌人预设初始化标记，避免每次传送都重复扫描

        private Dictionary<CharacterMainControl, float> bossSpawnTimes
        {
            get { return wavesArenaRuntime.bossSpawnTimes; }
        }
        private Dictionary<CharacterMainControl, int> bossOriginalLootCounts
        {
            get { return wavesArenaRuntime.bossOriginalLootCounts; }
        }
        private HashSet<CharacterMainControl> countedDeadBosses { get { return wavesArenaRuntime.CountedDeadBosses; } }
        private HashSet<CharacterMainControl> bossRushLootboxPathBosses
        {
            get { return wavesArenaRuntime.bossRushLootboxPathBosses; }
        }
        private Dictionary<CharacterMainControl, Action<DamageInfo>> trackedBossLootHooks
        {
            get { return wavesArenaRuntime.trackedBossLootHooks; }
        }
        private List<CharacterMainControl> bossRushLootboxPathTrackedBossScratch
        {
            get { return wavesArenaRuntime.bossRushLootboxPathTrackedBossScratch; }
        }
        private List<CharacterMainControl> bossRushLootboxPathStaleBossScratch
        {
            get { return wavesArenaRuntime.bossRushLootboxPathStaleBossScratch; }
        }
        private readonly List<Item> modeFPlunderPenaltyScratch = new List<Item>();
        private List<int> legacyBossGuaranteeCandidateScratch
        {
            get { return wavesArenaRuntime.legacyBossGuaranteeCandidateScratch; }
        }
        private Dictionary<int, List<int>> legacyBossGuaranteeQualityBucketsScratch
        {
            get { return wavesArenaRuntime.legacyBossGuaranteeQualityBucketsScratch; }
        }
        private List<Item> difficultyRewardPreferredScratch
        {
            get { return wavesArenaRuntime.difficultyRewardPreferredScratch; }
        }
        private List<Item> difficultyRewardFallbackHighQualityScratch
        {
            get { return wavesArenaRuntime.difficultyRewardFallbackHighQualityScratch; }
        }
        private List<Item> difficultyRewardKeepScratch
        {
            get { return wavesArenaRuntime.difficultyRewardKeepScratch; }
        }

        private void LogLootWarningLimited(string key, string message, Exception e = null)
        {
            wavesArenaRuntime.LogLootWarningLimited(key, message, e);
        }

        private bool infiniteHellMode
        {
            get { return wavesArenaRuntime.InfiniteHellMode; }
            set { wavesArenaRuntime.InfiniteHellMode = value; }
        }

        private int infiniteHellWaveIndex
        {
            get { return wavesArenaRuntime.InfiniteHellWaveIndex; }
            set { wavesArenaRuntime.InfiniteHellWaveIndex = value; }
        }

        private long infiniteHellCashPool
        {
            get { return wavesArenaRuntime.InfiniteHellCashPool; }
            set { wavesArenaRuntime.InfiniteHellCashPool = value; }
        }

        private int infiniteHellMilestoneRewardTier
        {
            get { return wavesArenaRuntime.InfiniteHellMilestoneRewardTier; }
            set { wavesArenaRuntime.InfiniteHellMilestoneRewardTier = value; }
        }

        private long infiniteHellWaveCashThisWave
        {
            get { return wavesArenaRuntime.InfiniteHellWaveCashThisWave; }
            set { wavesArenaRuntime.InfiniteHellWaveCashThisWave = value; }
        }

        private List<int> infiniteHellHighQualityItemPool
        {
            get { return wavesArenaRuntime.InfiniteHellHighQualityItemPool; }
        }

        private HashSet<int> infiniteHellHighQualityCandidateIdScratch
        {
            get { return wavesArenaRuntime.InfiniteHellHighQualityCandidateIdScratch; }
        }

        private List<int> infiniteHellHighQualityPreferredScratch
        {
            get { return wavesArenaRuntime.InfiniteHellHighQualityPreferredScratch; }
        }

        private List<int> infiniteHellHighQualityFallbackScratch
        {
            get { return wavesArenaRuntime.InfiniteHellHighQualityFallbackScratch; }
        }

        private bool infiniteHellHighQualityItemPoolInitialized
        {
            get { return wavesArenaRuntime.InfiniteHellHighQualityItemPoolInitialized; }
            set { wavesArenaRuntime.InfiniteHellHighQualityItemPoolInitialized = value; }
        }

        // ============================================================================
        // 物品价值缓存系统 - 避免Boss死亡时同步实例化大量物品导致卡顿
        // ============================================================================
        private static InteractableLootbox _cachedLootBoxTemplateWithLoader
        {
            get { return WavesArenaRuntimeModule.CachedLootBoxTemplateWithLoader; }
            set { WavesArenaRuntimeModule.CachedLootBoxTemplateWithLoader = value; }
        }

        private static InteractableLootbox _cachedDifficultyRewardLootBoxTemplate
        {
            get { return WavesArenaRuntimeModule.CachedDifficultyRewardLootBoxTemplate; }
            set { WavesArenaRuntimeModule.CachedDifficultyRewardLootBoxTemplate = value; }
        }

        private static InteractableLootbox _cachedVictoryRewardVisualLootBoxTemplate
        {
            get { return WavesArenaRuntimeModule.CachedVictoryRewardVisualLootBoxTemplate; }
            set { WavesArenaRuntimeModule.CachedVictoryRewardVisualLootBoxTemplate = value; }
        }

        private static Dictionary<int, ItemValueCacheEntry> _itemValueCache
        {
            get { return WavesArenaRuntimeModule.ItemValueCache; }
            set { WavesArenaRuntimeModule.ItemValueCache = value; }
        }

        private static bool _itemValueCacheInitialized
        {
            get { return WavesArenaRuntimeModule.ItemValueCacheInitialized; }
            set { WavesArenaRuntimeModule.ItemValueCacheInitialized = value; }
        }

        private static bool _itemValueCacheInitializing
        {
            get { return WavesArenaRuntimeModule.ItemValueCacheInitializing; }
            set { WavesArenaRuntimeModule.ItemValueCacheInitializing = value; }
        }

        private static List<int> _legacyBossLootCandidateIds
        {
            get { return WavesArenaRuntimeModule.LegacyBossLootCandidateIds; }
            set { WavesArenaRuntimeModule.LegacyBossLootCandidateIds = value; }
        }

        private static Dictionary<int, List<int>> _legacyBossLootCandidateIdsByQuality
        {
            get { return WavesArenaRuntimeModule.LegacyBossLootCandidateIdsByQuality; }
            set { WavesArenaRuntimeModule.LegacyBossLootCandidateIdsByQuality = value; }
        }

        private static bool _legacyBossLootCandidateCacheInitialized
        {
            get { return WavesArenaRuntimeModule.LegacyBossLootCandidateCacheInitialized; }
            set { WavesArenaRuntimeModule.LegacyBossLootCandidateCacheInitialized = value; }
        }

        private static void ResetLootAndRewardsStaticCaches()
        {
            WavesArenaRuntimeModule.ResetLootAndRewardsStaticCaches();
        }


        private void RegisterBossRandomLootTracking(CharacterMainControl character, int originalLootCount = 3, float spawnTimeOffset = 1f)
        {
            wavesArenaRuntime.RegisterBossRandomLootTracking(character, originalLootCount, spawnTimeOffset);
        }

        private void ClearBossRandomLootTracking(CharacterMainControl character)
        {
            wavesArenaRuntime.ClearBossRandomLootTracking(character);
        }

        private void MarkBossRushLootboxPathTracking(CharacterMainControl character)
        {
            wavesArenaRuntime.MarkBossRushLootboxPathTracking(character);
        }

        private void FinalizeBossRushLootboxPathTracking(CharacterMainControl character)
        {
            wavesArenaRuntime.FinalizeBossRushLootboxPathTracking(character);
        }

        internal void RefreshBossRushLootboxPathTrackingForTrackedBosses()
        {
            wavesArenaRuntime.RefreshBossRushLootboxPathTrackingForTrackedBosses();
        }

        internal bool ShouldTrackBossRushLootboxPathForArena()
        {
            return config != null && config.enableRandomBossLoot && !infiniteHellMode && !modeEActive && !modeFActive;
        }

        internal void OnBossBeforeSpawnLootForArena(CharacterMainControl character, DamageInfo damageInfo)
        {
            OnBossBeforeSpawnLoot(character, damageInfo);
        }

        /// <summary>
        /// 初始化物品价值缓存（异步，在后台分帧处理避免卡顿）
        /// </summary>
        private void InitializeItemValueCacheAsync() { wavesArenaRuntime.InitializeItemValueCacheAsync(); }
        internal bool TryGetCachedItemValue(int itemId, out int value, out int quality)
        {
            return wavesArenaRuntime.TryGetCachedItemValue(itemId, out value, out quality);
        }
        private HashSet<int> BuildGeneralBossLootCandidateIdSet()
        {
            return wavesArenaRuntime.BuildGeneralBossLootCandidateIdSet();
        }
        private int GetBossLootCandidateQuality(int itemId)
        {
            return wavesArenaRuntime.GetBossLootCandidateQuality(itemId);
        }
        private void BuildLegacyBossLootQualityBucketsFromIds(IEnumerable<int> ids, Dictionary<int, List<int>> buckets)
        {
            wavesArenaRuntime.BuildLegacyBossLootQualityBucketsFromIds(ids, buckets);
        }
        internal bool BuildGeneralBossLootCandidateIdSet(HashSet<int> idSet)
        {
            return wavesArenaRuntime.BuildGeneralBossLootCandidateIdSet(idSet);
        }
        private void ClearLegacyBossGuaranteeQualityBucketsScratch()
        {
            wavesArenaRuntime.ClearLegacyBossGuaranteeQualityBucketsScratch();
        }
        private bool TryGetLegacyBossLootCandidates(List<int> candidateIds, Dictionary<int, List<int>> qualityBuckets = null)
        {
            return wavesArenaRuntime.TryGetLegacyBossLootCandidates(candidateIds, qualityBuckets);
        }
        private float ComputeLegacyBossLootBonusFactor(float maxHealth, float killDuration)
        {
            return wavesArenaRuntime.ComputeLegacyBossLootBonusFactor(maxHealth, killDuration);
        }
        private static float ComputeBossKillSpeedFactor(float maxHealth, float killDuration)
        {
            return WavesArenaRuntimeModule.ComputeBossKillSpeedFactor(maxHealth, killDuration);
        }
    }
}
