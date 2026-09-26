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
        #region LootAndRewards

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
        internal static bool IsItemBlacklisted(int itemId)
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

        #endregion

        #region LootAndRewardsInfiniteHell

        private void OnInfiniteHellWaveCompleted_LootAndRewards()
        {
            wavesArenaRuntime.OnInfiniteHellWaveCompleted_LootAndRewards();
        }

        internal void CheckInfiniteHellAchievementsForArena(int wave) { CheckInfiniteHellAchievements(wave); }
        internal GameObject ArenaRewardSignGameObject { get { return _bossRushSignGameObject; } }
        internal BossRushSignInteractable ArenaRewardSignInteract { get { return bossRushSignInteract; } }
        internal MonoBehaviour ArenaPlayerCharacter { get { return playerCharacter; } }
        internal bool UseInteractBetweenWavesForArena { get { return config != null && config.useInteractBetweenWaves; } }
        internal void LogLootWarningLimitedForArena(string key, string message, Exception e) { LogLootWarningLimited(key, message, e); }

        private int GetRandomInfiniteHellHighQualityRewardTypeID()
        {
            return wavesArenaRuntime.GetRandomInfiniteHellHighQualityRewardTypeID();
        }


        #endregion

        #region LootAndRewardsVictoryRewards

        internal CharacterMainControl GetPlayerCharacterForArenaLoot()
        {
            return playerCharacter as CharacterMainControl;
        }

        /// <summary>所有敌人击败后交给标准竞技场模块收尾。</summary>
        private void OnAllEnemiesDefeated_LootAndRewards()
        {
            wavesArenaRuntime.OnAllEnemiesDefeated_LootAndRewards();
        }

        internal void SetArenaVictoryActiveForArena(bool active) { bossRushArenaActive = active; }
        internal void UnsubscribeArenaBossDeathsForArena() { Health.OnDead -= OnEnemyDiedWithDamageInfo; }
        internal void CheckClearAchievementsForArena() { CheckClearAchievements(); }
        internal void TryCreateReturnInteractableForArena() { TryCreateReturnInteractable(); }

        private void StartVictoryRewardShadowCrate_LootAndRewards(int highQualityCount)
        {
            wavesArenaRuntime.StartVictoryRewardShadowCrate_LootAndRewards(highQualityCount);
        }

        private void CompleteVictoryRewardShadowCrate_LootAndRewards()
        {
            wavesArenaRuntime.CompleteVictoryRewardShadowCrate_LootAndRewards();
        }

        internal void NotifyVictoryRewardShadowCrateDisposed_LootAndRewards(VictoryRewardShadowCrateController controller)
        {
            wavesArenaRuntime.NotifyVictoryRewardShadowCrateDisposed_LootAndRewards(controller);
        }

        internal bool TryStartModeGRewardMaterialization_LootAndRewards(
            int[] fixedTypeIds, ItemStatsSystem.Inventory targetInventory,
            Action<int, ItemStatsSystem.Item, bool> onItemCommitted,
            Action<int, int, int> onAllCompleted, out string failureReason)
        {
            return wavesArenaRuntime.TryStartModeGRewardMaterialization_LootAndRewards(
                fixedTypeIds, targetInventory, onItemCommitted, onAllCompleted, out failureReason);
        }

        internal void CancelModeGRewardMaterialization_LootAndRewards()
        {
            wavesArenaRuntime.CancelModeGRewardMaterialization_LootAndRewards();
        }

        internal void SpawnDifficultyRewardLootboxAtWorldPosition_LootAndRewards(int highQualityCount, Vector3 worldPosition)
        {
            wavesArenaRuntime.SpawnDifficultyRewardLootboxAtWorldPosition_LootAndRewards(highQualityCount, worldPosition);
        }

        internal void SpawnDifficultyRewardLootboxFallback_LootAndRewards(int highQualityCount)
        {
            wavesArenaRuntime.SpawnDifficultyRewardLootboxFallback_LootAndRewards(highQualityCount);
        }


        #endregion

        #region LootAndRewardsRandomBossLoot

        internal bool ShouldUseBossDeadBoxPrefabForArena()
        {
            return config != null && !config.lootBoxBlocksBullets;
        }

        internal bool IsModeEOrFActiveForArenaLoot()
        {
            return modeEActive || modeFActive;
        }

        /// <summary>
        /// 玩家死亡保护（BossRush期间）- 参考keep_items_on_death实现（LootAndRewards 分部实现）
        /// 不干预游戏死亡流程，只阻止物品掉落
        /// </summary>
        private void OnPlayerDeathInBossRush_LootAndRewards(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                // 检查是否是BossRush期间的玩家死亡
                if (!IsActive) return;

                var character = deadHealth.GetComponent<CharacterMainControl>();
                if (character == null) return;

                // 检查是否是玩家
                bool isPlayer = false;
                try
                {
                    isPlayer = CharacterMainControlExtensions.IsMainCharacter(character);
                }
                catch
                {
                    isPlayer = (character == CharacterMainControl.Main);
                }

                if (!isPlayer) return;

                DevLog("[BossRush] 检测到玩家死亡，不再掉落物品，直接结束BossRush");

                // 结束BossRush
                SetBossRushRuntimeActive(false);
                bossRushArenaActive = false;
                currentBoss = null;
                bossRushIntegrationRuntime.CleanupAmmoShopOnPlayerDeath(LogLootWarningLimited);

                // 取消敌人死亡监听
                Health.OnDead -= OnEnemyDiedWithDamageInfo;

                // 如果是 Mode D 模式，结束 Mode D
                if (modeDActive)
                {
                    EndModeD();
                }

                // 如果是 Mode E 模式，结束 Mode E
                if (modeEActive)
                {
                    EndModeE();
                }

                ShowMessage(L10n.T("BossRush挑战失败！", "BossRush challenge failed!"));
            }
            catch (Exception e)
            {
                DevLog("[BossRush] OnPlayerDeathInBossRush错误: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>Boss 掉落事件沿用旧订阅委托，实际处理归竞技场模块。</summary>
        private void OnBossBeforeSpawnLoot_LootAndRewards(CharacterMainControl bossMain, DamageInfo dmgInfo)
        {
            wavesArenaRuntime.OnBossBeforeSpawnLoot_LootAndRewards(bossMain, dmgInfo);
        }

        internal bool IsRandomBossLootEnabledForArena() { return config != null && config.enableRandomBossLoot; }
        internal bool UseLegacyBossLootProbabilitiesForArena() { return config != null && config.useLegacyBossLootProbabilities; }
        internal void TryAddBackMountainSeedToCharacterItemForArena(CharacterMainControl boss) { TryAddBackMountainSeedToCharacterItem(boss); }
        internal void TryDropBackMountainSeedIntoWorldForArena(CharacterMainControl boss) { TryDropBackMountainSeedIntoWorld(boss); }
        internal bool TryHandleModeFBossPreLootPlunderForArena(CharacterMainControl killer, CharacterMainControl victim) { return TryHandleModeFBossPreLootPlunder(killer, victim); }
        internal int ConsumeModeFBossPendingHighQualityLootPenaltyCountForArena(CharacterMainControl boss) { return ConsumeModeFBossPendingHighQualityLootPenaltyCount(boss); }
        internal int ConsumeModeFBossCarriedHighQualityLootCountForArena(CharacterMainControl boss) { return ConsumeModeFBossCarriedHighQualityLootCount(boss); }

        private void RandomizeBossLoot_LootAndRewards(
            CharacterMainControl bossMain,
            int totalCount,
            float killDuration,
            float highChanceBonusByHealth,
            bool useLegacyProbabilities,
            float legacyBonusFactor,
            float bossMaxHealth,
            int modeFPlunderLootBonusCount = 0,
            int modeFPlunderLootPenaltyCount = 0)
        {
            wavesArenaRuntime.RandomizeBossLoot_LootAndRewards(bossMain, totalCount, killDuration,
                highChanceBonusByHealth, useLegacyProbabilities, legacyBonusFactor, bossMaxHealth,
                modeFPlunderLootBonusCount, modeFPlunderLootPenaltyCount);
        }

        #endregion

        #region LootAndRewardsSpecialLoot

        /// <summary>
        /// 调试：记录 Boss 掉落实际物品列表（LootAndRewards 分部实现）
        /// </summary>
        private IEnumerator LogBossLootInventory_LootAndRewards(InteractableLootbox lootbox)
        {
            return wavesArenaRuntime.LogBossLootInventory_LootAndRewards(lootbox);
        }

        private IEnumerator CleanupDifficultyRewardLootboxInventory_LootAndRewards(InteractableLootbox lootbox, int highQualityCount)
        {
            return wavesArenaRuntime.CleanupDifficultyRewardLootboxInventory_LootAndRewards(lootbox, highQualityCount);
        }

        private void ClearDifficultyRewardCleanupScratch()
        {
            wavesArenaRuntime.ClearDifficultyRewardCleanupScratch();
        }

        private bool InventoryContainsItemAtLeastQuality(Inventory inv, int minimumQuality)
        {
            return wavesArenaRuntime.InventoryContainsItemAtLeastQuality(inv, minimumQuality);
        }

        private int GetLegacyBossGuaranteeTypeId(int desiredQuality, out int actualQuality)
        {
            return wavesArenaRuntime.GetLegacyBossGuaranteeTypeId(desiredQuality, out actualQuality);
        }

        private bool TryAddLegacyBossGuaranteeItem(Inventory inv)
        {
            return wavesArenaRuntime.TryAddLegacyBossGuaranteeItem(inv);
        }

        /// <summary>
        /// 判断Boss是否是龙裔遗族（支持多Boss模式）
        /// 通过GameObject名称或预设nameKey判断，不依赖单一实例引用
        /// </summary>
        private bool IsDragonDescendantBoss(CharacterMainControl boss)
        {
            return wavesArenaRuntime.IsDragonDescendantBoss(boss);
        }

        private bool IsDragonKingBoss(CharacterMainControl boss)
        {
            return wavesArenaRuntime.IsDragonKingBoss(boss);
        }

        private IEnumerator AddDragonDescendantLoot(Inventory inv)
        {
            return wavesArenaRuntime.AddDragonDescendantLoot(inv);
        }

        private IEnumerator AddDragonKingLoot(Inventory inv)
        {
            return wavesArenaRuntime.AddDragonKingLoot(inv);
        }

        private bool TryAddDragonKingLootItem(Inventory inv, int typeId, string itemName)
        {
            return wavesArenaRuntime.TryAddDragonKingLootItem(inv, typeId, itemName);
        }

        internal IEnumerator AddBossSpecialLootToLootboxCoroutine(
            InteractableLootbox lootbox,
            CharacterMainControl bossMain,
            bool useLegacyBossLootProbabilities,
            float bossMaxHealth,
            int modeFPlunderLootBonusCount = 0,
            int modeFPlunderLootPenaltyCount = 0)
        {
            return wavesArenaRuntime.AddBossSpecialLootToLootboxCoroutine(
                lootbox, bossMain, useLegacyBossLootProbabilities, bossMaxHealth,
                modeFPlunderLootBonusCount, modeFPlunderLootPenaltyCount);
        }

        internal void TryAddBackMountainSeedLootForArena(Inventory inv, CharacterMainControl boss)
        {
            TryAddBackMountainSeedLoot(inv, boss);
        }

        internal bool ShouldDeferBlueBossExtraDropToBossRushLootbox(CharacterMainControl bossMain)
        {
            return wavesArenaRuntime.ShouldDeferBlueBossExtraDropToBossRushLootbox(bossMain);
        }

        internal void ReturnPendingExtraLootToCharacterItem(CharacterMainControl bossMain)
        {
            wavesArenaRuntime.ReturnPendingExtraLootToCharacterItem(bossMain);
        }

        internal void DropPendingExtraLootIntoWorld(CharacterMainControl bossMain)
        {
            wavesArenaRuntime.DropPendingExtraLootIntoWorld(bossMain);
        }

        /// <summary>
        /// 额外掉落（寒霜长矛 / 女巫镰刀 / 遗种蛋 / 词缀熔石）是否必须 defer，
        /// 即"不能直接塞进 `boss.CharacterItem.Inventory`"。
        ///
        /// 判据是**官方那只 characterItem 掉落箱到底会不会被创建**，而不是
        /// "会不会有 BossRush 奖励箱"——本 Mod 有两条路径都会把它关掉：
        ///   1. BossRush 奖励箱路径：`RandomizeBossLoot_LootAndRewards` 置
        ///      `dropBoxOnDead = false` 后另建带全新本地 Inventory 的箱子；
        ///   2. 无间炼狱：`OnBossBeforeSpawnLoot` 同样置 false，但连箱子都不建，
        ///      奖励改走世界掉落。
        /// 两条路径下写进 characterItem 的物品都会被静默丢掉，所以都要 defer。
        /// 消费点分别是 `AddBossSpecialLootToLootboxCoroutine`（进箱）与
        /// `DropPendingExtraLootIntoWorld`（落地）。
        /// </summary>
        internal bool ShouldDeferExtraBossDropToModPath(CharacterMainControl bossMain)
        {
            return wavesArenaRuntime.ShouldDeferExtraBossDropToModPath(bossMain);
        }


        #endregion

        #region LootAndRewardsRuntimeHooks

        /// <summary>
        /// 无间炼狱单波完成：掉落现金、更新显示并准备下一波
        /// </summary>
        private void OnInfiniteHellWaveCompleted()
        {
            OnInfiniteHellWaveCompleted_LootAndRewards();
            return;
        }

        /// <summary>
        /// 所有敌人击败完成
        /// </summary>
        private void OnAllEnemiesDefeated()
        {
            // 每日挑战通关结算

            OnAllEnemiesDefeated_LootAndRewards();
            return;
        }

        /// <summary>
        /// 玩家死亡保护（BossRush 期间），参考 keep_items_on_death 实现
        /// 不干预游戏死亡流程，只阻止物品掉落
        /// </summary>
        private void OnPlayerDeathInBossRush(Health deadHealth, DamageInfo damageInfo)
        {
            // 每日挑战死亡结算

            OnPlayerDeathInBossRush_LootAndRewards(deadHealth, damageInfo);
            return;
        }

        private void OnBossBeforeSpawnLoot(CharacterMainControl bossMain, DamageInfo dmgInfo)
        {
            OnBossBeforeSpawnLoot_LootAndRewards(bossMain, dmgInfo);
            return;
        }

        #endregion

        #region ModeEFLootboxTracker

        private readonly AwenLootSweepRuntime awenLootSweepRuntime = new AwenLootSweepRuntime();

        private void BindAwenLootSweepRuntime()
        {
            awenLootSweepRuntime.BindServices(TryGetActiveModeEFLootboxContext, CanUseAwenLootSweepInCurrentMode,
                IsAwenLootSweepSessionStillValid, () => courierNPCInstance, () => courierController,
                (typeId, name, bubble, drop) => modeFRuntime.TryGiveItemToPlayerOrDrop(typeId, name, bubble, drop),
                ShowBigBanner, ShowMessage);
        }

        private bool TryGetActiveModeEFLootboxContext(out BossRushTrackedLootboxMode mode, out int sessionToken)
        {
            mode = BossRushTrackedLootboxMode.None;
            sessionToken = 0;

            if (modeFActive && modeFState.IsActive && CurrentModeFSessionToken > 0)
            {
                mode = BossRushTrackedLootboxMode.ModeF;
                sessionToken = CurrentModeFSessionToken;
                return true;
            }

            if (modeEActive && CurrentModeESessionToken > 0)
            {
                mode = BossRushTrackedLootboxMode.ModeE;
                sessionToken = CurrentModeESessionToken;
                return true;
            }

            return false;
        }

        internal void ResetModeEFLootboxTrackerState()
        { awenLootSweepRuntime.ResetModeEFLootboxTrackerState(); }

        internal void CaptureModeEFLootboxBaseline()
        { awenLootSweepRuntime.CaptureModeEFLootboxBaseline(); }

        internal bool CanUseAwenLootSweepInCurrentMode()
        {
            return (IsActive && !IsModeDActive) || (IsBossRushArenaActive && !IsModeDActive) || modeEActive || modeFActive;
        }

        internal void InvalidateAwenLootSweepTargetCache()
        { awenLootSweepRuntime.InvalidateAwenLootSweepTargetCache(); }

        internal int GetCurrentAwenLootSweepTargetCount()
        { return awenLootSweepRuntime.GetCurrentAwenLootSweepTargetCount(); }

        internal int CopyCurrentAwenLootSweepTargets(List<AwenLootSweepTarget> output)
        { return awenLootSweepRuntime.CopyCurrentAwenLootSweepTargets(output); }

        internal int CopyFreshAwenLootSweepTargets(List<AwenLootSweepTarget> output)
        { return awenLootSweepRuntime.CopyFreshAwenLootSweepTargets(output); }

        internal void NotifyAwenLootSweepRunnerDestroyed(AwenLootSweepRunner runner)
        { awenLootSweepRuntime.NotifyAwenLootSweepRunnerDestroyed(runner); }

        internal bool IsAwenLootSweepSessionStillValid(BossRushTrackedLootboxMode mode, int sessionToken, int relatedScene)
        {
            switch (mode)
            {
                case BossRushTrackedLootboxMode.ModeE:
                    return IsModeESessionStillValid(sessionToken, relatedScene);
                case BossRushTrackedLootboxMode.ModeF:
                    return IsModeFSessionStillValid(sessionToken, relatedScene);
                default:
                    return false;
            }
        }

        internal void TryRegisterModeEFLootbox(InteractableLootbox lootbox)
        { awenLootSweepRuntime.TryRegisterModeEFLootbox(lootbox); }

        internal void RegisterModeEFBossDeathForSweepToken()
        { awenLootSweepRuntime.RegisterModeEFBossDeathForSweepToken(); }

        internal bool CanUseAwenLootSweepToken()
        { return awenLootSweepRuntime.CanUseAwenLootSweepToken(); }

        internal bool CanUseAwenLootSweepToken(CharacterMainControl player, bool showFailureFeedback)
        { return awenLootSweepRuntime.CanUseAwenLootSweepToken(player, showFailureFeedback); }

        internal bool TryActivateAwenLootSweepToken(CharacterMainControl player)
        { return awenLootSweepRuntime.TryActivateAwenLootSweepToken(player); }

        internal bool TryRefundAwenLootSweepToken()
        { return awenLootSweepRuntime.TryRefundAwenLootSweepToken(); }


        #endregion
    }
}
