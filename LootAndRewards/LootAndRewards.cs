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
        private const int LOOT_LOW_QUALITY_MIN = 1;

        /// <summary>普通品质最大值</summary>
        private const int LOOT_LOW_QUALITY_MAX = 4;

        /// <summary>高品质最小值</summary>
        private const int LOOT_HIGH_QUALITY_MIN = 5;

        /// <summary>高品质最大值</summary>
        private const int LOOT_HIGH_QUALITY_MAX = 8;

        /// <summary>血量加成系数（每100血量增加的高品质概率，0.05即5%）</summary>
        private const float LOOT_HEALTH_BONUS_RATE = 0.05f;

        /// <summary>击杀时间加成系数（最快击杀时的最大加成，0.1即10%）</summary>
        private const float LOOT_TIME_BONUS_RATE = 0.1f;

        /// <summary>原版 Boss 战利品 Q5+ 保底的最小 Boss 最大生命值门槛</summary>
        private const float LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH = 250f;

        // ============================================================================

        private static InteractableLootbox _cachedLootBoxTemplateWithLoader = null;
        private static InteractableLootbox _cachedDifficultyRewardLootBoxTemplate = null;
        private static InteractableLootbox _cachedVictoryRewardVisualLootBoxTemplate = null;
        private VictoryRewardShadowCrateController _activeVictoryRewardShadowCrateController = null;
        private bool _difficultyRewardSpawnPositionOverrideActive = false;
        private Vector3 _difficultyRewardSpawnPositionOverride = Vector3.zero;

        /// <summary>
        /// 检查物品ID是否在掉落黑名单中
        /// </summary>
        private static bool IsItemBlacklisted(int itemId)
        {
            return LootBlacklistRegistry.Contains(itemId);
        }

        private void AddUniqueLootExcludeTag(List<Duckov.Utilities.Tag> excludeTags, Duckov.Utilities.Tag tag)
        {
            if (excludeTags == null || tag == null || excludeTags.Contains(tag))
            {
                return;
            }

            excludeTags.Add(tag);
        }

        // ============================================================
        // Quest tag 反射查找缓存（审查 §2.5）
        // ============================================================
        // 之前每次 inventory 转移检查都对 N 件物品做 3 段反射；当前鸭科夫版本
        // GameplayDataSettings.TagsData 没有 Quest 字段，AllTags 也无同名 Tag，
        // 反射永远失败。第一次失败后用 sentinel 把后续调用降为 O(1)。
        private static Duckov.Utilities.Tag s_cachedQuestTag;
        private static bool s_questTagSearched;
        private static bool s_questTagPermanentlyMissing;

        private Duckov.Utilities.Tag TryFindQuestTag(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        {
            if (tagsData == null)
            {
                return null;
            }

            if (s_cachedQuestTag != null)
            {
                return s_cachedQuestTag;
            }
            if (s_questTagPermanentlyMissing)
            {
                return null;
            }
            if (s_questTagSearched)
            {
                return s_cachedQuestTag;
            }

            s_questTagSearched = true;

            const BindingFlags publicInstanceIgnoreCase =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            try
            {
                FieldInfo questField = tagsData.GetType().GetField("Quest", publicInstanceIgnoreCase);
                if (questField != null && typeof(Duckov.Utilities.Tag).IsAssignableFrom(questField.FieldType))
                {
                    s_cachedQuestTag = questField.GetValue(tagsData) as Duckov.Utilities.Tag;
                    if (s_cachedQuestTag != null) return s_cachedQuestTag;
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("TryFindQuestTag_field", "通过字段读取 Quest 标签失败", e);
            }

            try
            {
                PropertyInfo questProperty = tagsData.GetType().GetProperty("Quest", publicInstanceIgnoreCase);
                if (questProperty != null && typeof(Duckov.Utilities.Tag).IsAssignableFrom(questProperty.PropertyType))
                {
                    s_cachedQuestTag = questProperty.GetValue(tagsData, null) as Duckov.Utilities.Tag;
                    if (s_cachedQuestTag != null) return s_cachedQuestTag;
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("TryFindQuestTag_property", "通过属性读取 Quest 标签失败", e);
            }

            try
            {
                if (tagsData.AllTags != null)
                {
                    for (int i = 0; i < tagsData.AllTags.Count; i++)
                    {
                        Duckov.Utilities.Tag tag = tagsData.AllTags[i];
                        if (tag != null && string.Equals(tag.name, "Quest", StringComparison.OrdinalIgnoreCase))
                        {
                            s_cachedQuestTag = tag;
                            return tag;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("TryFindQuestTag_alltags", "遍历 AllTags 查找 Quest 标签失败", e);
            }

            // 三段反射全部失败：标记永久缺失，下次直接早返。让维护者从日志看出
            // "Quest tag 转移阻断永久 disable"是当前鸭科夫版本的事实而非 mod bug。
            s_questTagPermanentlyMissing = true;
            ModBehaviour.DevLog("[LootAndRewards] Quest tag lookup failed; transfer-block by Quest tag is permanently disabled in this build");
            return null;
        }

        // Quest items should stay out of generic reward/drop pools even if they match other tags.
        private List<Duckov.Utilities.Tag> BuildGeneralLootExcludeTags(Duckov.Utilities.GameplayDataSettings.TagsData tagsData, bool includeCharacterTag = false)
        {
            List<Duckov.Utilities.Tag> excludeTags = new List<Duckov.Utilities.Tag>();
            if (tagsData == null)
            {
                return excludeTags;
            }

            if (includeCharacterTag)
            {
                AddUniqueLootExcludeTag(excludeTags, tagsData.Character);
            }

            AddUniqueLootExcludeTag(excludeTags, tagsData.DestroyOnLootBox);
            AddUniqueLootExcludeTag(excludeTags, tagsData.DontDropOnDeadInSlot);
            AddUniqueLootExcludeTag(excludeTags, tagsData.LockInDemoTag);
            AddUniqueLootExcludeTag(excludeTags, TryFindQuestTag(tagsData));

            return excludeTags;
        }

        private void MergeGeneralLootExcludeTags(List<Duckov.Utilities.Tag> excludeList, Duckov.Utilities.GameplayDataSettings.TagsData tagsData, bool includeCharacterTag = false)
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

        private List<EnemyPresetInfo> enemyPresets = new List<EnemyPresetInfo>();
        private float minBossBaseHealth = 100f;
        private float maxBossBaseHealth = 100f;

        // [性能优化] 敌人预设初始化标记，避免每次传送都重复扫描
        private static bool _enemyPresetsInitialized = false;

        private readonly Dictionary<CharacterMainControl, float> bossSpawnTimes = new Dictionary<CharacterMainControl, float>();
        private readonly Dictionary<CharacterMainControl, int> bossOriginalLootCounts = new Dictionary<CharacterMainControl, int>();
        private readonly HashSet<CharacterMainControl> countedDeadBosses = new HashSet<CharacterMainControl>();
        private readonly HashSet<CharacterMainControl> bossRushLootboxPathBosses = new HashSet<CharacterMainControl>();
        private readonly Dictionary<CharacterMainControl, Action<DamageInfo>> trackedBossLootHooks
            = new Dictionary<CharacterMainControl, Action<DamageInfo>>();
        private readonly List<Item> modeFPlunderPenaltyScratch = new List<Item>();
        private const float LOOT_WARNING_LOG_INTERVAL = 5f;
        private readonly Dictionary<string, float> lootNextWarningLogTimes = new Dictionary<string, float>();

        private void LogLootWarningLimited(string key, string message, Exception e = null)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(message))
            {
                return;
            }

            float now = Time.unscaledTime;
            float nextLogTime;
            if (lootNextWarningLogTimes.TryGetValue(key, out nextLogTime) && now < nextLogTime)
            {
                return;
            }

            lootNextWarningLogTimes[key] = now + LOOT_WARNING_LOG_INTERVAL;
            DevLog("[BossRush] [WARNING] " + message + (e != null ? ": " + e.Message : string.Empty));
        }

        private string GetBossLootTrackingDebugName(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return "<null>";
            }

            try
            {
                if (character.gameObject != null && !string.IsNullOrEmpty(character.gameObject.name))
                {
                    return character.gameObject.name;
                }
            }
            catch (Exception)
            {
                return "<unnamed>";
            }

            return "<unnamed>";
        }

        private void RollbackBossRandomLootTrackingRegistration(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return;
            }

            bossSpawnTimes.Remove(character);
            bossOriginalLootCounts.Remove(character);
            countedDeadBosses.Remove(character);
            trackedBossLootHooks.Remove(character);
            FinalizeBossRushLootboxPathTracking(character);
        }

        private bool infiniteHellMode = false;
        private int infiniteHellWaveIndex = 0;
        private long infiniteHellCashPool = 0L;
        // 已发放的最高里程碑阶数（每100波递进，0表示尚未发放任何里程碑奖励）
        private int infiniteHellMilestoneRewardTier = 0;
        private long infiniteHellWaveCashThisWave = 0L;
        private List<int> infiniteHellHighQualityItemPool = null;
        private bool infiniteHellHighQualityItemPoolInitialized = false;

        // ============================================================================
        // 物品价值缓存系统 - 避免Boss死亡时同步实例化大量物品导致卡顿
        // ============================================================================
        private static Dictionary<int, ItemValueCacheEntry> _itemValueCache = null;
        private static bool _itemValueCacheInitialized = false;
        private static bool _itemValueCacheInitializing = false;
        private static List<int> _legacyBossLootCandidateIds = null;
        private static Dictionary<int, List<int>> _legacyBossLootCandidateIdsByQuality = null;
        private static bool _legacyBossLootCandidateCacheInitialized = false;

        private void RegisterBossRandomLootTracking(CharacterMainControl character, int originalLootCount = 3, float spawnTimeOffset = 1f)
        {
            try
            {
                if (character == null)
                {
                    return;
                }

                bossSpawnTimes[character] = Time.time + spawnTimeOffset;
                bossOriginalLootCounts[character] = Mathf.Max(0, originalLootCount);
                countedDeadBosses.Remove(character);
                MarkBossRushLootboxPathTracking(character);

                Action<DamageInfo> existingHandler = null;
                if (trackedBossLootHooks.TryGetValue(character, out existingHandler) && existingHandler != null)
                {
                    try
                    {
                        character.BeforeCharacterSpawnLootOnDead -= existingHandler;
                    }
                    catch (Exception e)
                    {
                        DevLog("[BossRush] [WARNING] 重绑随机掉落追踪前取消旧事件失败: boss="
                            + GetBossLootTrackingDebugName(character) + ", " + e.Message);
                    }
                }

                CharacterMainControl capturedCharacter = character;
                Action<DamageInfo> handler = (dmgInfo) => OnBossBeforeSpawnLoot(capturedCharacter, dmgInfo);
                trackedBossLootHooks[character] = handler;

                try
                {
                    character.BeforeCharacterSpawnLootOnDead += handler;
                }
                catch (Exception e)
                {
                    RollbackBossRandomLootTrackingRegistration(character);
                    DevLog("[BossRush] [WARNING] 注册随机掉落追踪事件失败，已回滚追踪状态: boss="
                        + GetBossLootTrackingDebugName(character) + ", " + e.Message);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 注册随机掉落追踪失败: " + e.Message);
            }
        }

        private void ClearBossRandomLootTracking(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return;
            }

            Action<DamageInfo> handler = null;
            if (trackedBossLootHooks.TryGetValue(character, out handler))
            {
                try
                {
                    if (!(character == null) && handler != null)
                    {
                        character.BeforeCharacterSpawnLootOnDead -= handler;
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 清理随机掉落追踪事件失败: boss="
                        + GetBossLootTrackingDebugName(character) + ", " + e.Message);
                }
            }

            bossSpawnTimes.Remove(character);
            bossOriginalLootCounts.Remove(character);
            countedDeadBosses.Remove(character);
            trackedBossLootHooks.Remove(character);
        }

        private void MarkBossRushLootboxPathTracking(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return;
            }

            if (config != null &&
                config.enableRandomBossLoot &&
                !infiniteHellMode &&
                !modeEActive &&
                !modeFActive)
            {
                bossRushLootboxPathBosses.Add(character);
            }
            else
            {
                bossRushLootboxPathBosses.Remove(character);
            }
        }

        private void FinalizeBossRushLootboxPathTracking(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null))
            {
                return;
            }

            bossRushLootboxPathBosses.Remove(character);
            FrostmourneBlueBossDropHandler.CancelPendingBossRushLootboxDrop(character);
            PhantomWitchScytheBossDropHandler.CancelPendingBossRushLootboxDrop(character);
        }

        internal void RefreshBossRushLootboxPathTrackingForTrackedBosses()
        {
            if (bossSpawnTimes.Count == 0)
            {
                bossRushLootboxPathBosses.Clear();
                return;
            }

            List<CharacterMainControl> staleBosses = null;
            List<CharacterMainControl> trackedBosses =
                new List<CharacterMainControl>(bossSpawnTimes.Keys);

            for (int i = 0; i < trackedBosses.Count; i++)
            {
                CharacterMainControl boss = trackedBosses[i];
                if (boss == null)
                {
                    if (staleBosses == null)
                    {
                        staleBosses = new List<CharacterMainControl>();
                    }

                    staleBosses.Add(boss);
                    continue;
                }

                MarkBossRushLootboxPathTracking(boss);
            }

            if (staleBosses == null)
            {
                return;
            }

            for (int i = 0; i < staleBosses.Count; i++)
            {
                CharacterMainControl staleBoss = staleBosses[i];
                bossSpawnTimes.Remove(staleBoss);
                bossOriginalLootCounts.Remove(staleBoss);
                countedDeadBosses.Remove(staleBoss);
                trackedBossLootHooks.Remove(staleBoss);
                bossRushLootboxPathBosses.Remove(staleBoss);
            }
        }

        /// <summary>
        /// 物品价值缓存条目
        /// </summary>
        private struct ItemValueCacheEntry
        {
            public int value;
            public int quality;
        }

        /// <summary>
        /// 初始化物品价值缓存（异步，在后台分帧处理避免卡顿）
        /// </summary>
        private void InitializeItemValueCacheAsync()
        {
            if (_itemValueCacheInitialized || _itemValueCacheInitializing)
            {
                return;
            }
            _itemValueCacheInitializing = true;
            StartCoroutine(InitializeItemValueCacheCoroutine());
        }

        /// <summary>
        /// 物品价值缓存初始化协程 - 分帧处理避免卡顿
        /// </summary>
        private IEnumerator InitializeItemValueCacheCoroutine()
        {
            if (_itemValueCache == null)
            {
                _itemValueCache = new Dictionary<int, ItemValueCacheEntry>();
            }
            else
            {
                _itemValueCache.Clear();
            }

            if (_legacyBossLootCandidateIds == null)
            {
                _legacyBossLootCandidateIds = new List<int>();
            }
            else
            {
                _legacyBossLootCandidateIds.Clear();
            }

            if (_legacyBossLootCandidateIdsByQuality == null)
            {
                _legacyBossLootCandidateIdsByQuality = new Dictionary<int, List<int>>();
            }
            else
            {
                _legacyBossLootCandidateIdsByQuality.Clear();
            }

            _legacyBossLootCandidateCacheInitialized = false;

            DevLog("[BossRush] 开始初始化物品价值缓存...");

            // 收集所有候选物品ID
            HashSet<int> idSet = BuildGeneralBossLootCandidateIdSet();

            DevLog("[BossRush] 物品价值缓存：共需处理 " + idSet.Count + " 个物品");

            // 分帧处理：每帧处理一定数量的物品
            const int itemsPerFrame = 20;
            int processedCount = 0;
            List<int> idList = new List<int>(idSet);

            for (int i = 0; i < idList.Count; i++)
            {
                int itemId = idList[i];
                try
                {
                    Item temp = ItemAssetsCollection.InstantiateSync(itemId);
                    if (temp != null)
                    {
                        ItemValueCacheEntry entry = new ItemValueCacheEntry();
                        try { entry.value = temp.Value; } catch { entry.value = 0; }
                        try { entry.quality = temp.Quality; } catch { entry.quality = -1; }
                        _itemValueCache[itemId] = entry;

                        _legacyBossLootCandidateIds.Add(itemId);
                        AddLegacyBossLootCandidateToQualityBucket(itemId, entry.quality);
                        UnityEngine.Object.Destroy(temp.gameObject);
                    }
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("InitializeItemValueCache_item", "初始化物品价值缓存时处理单个物品失败", e);
                }

                processedCount++;

                // 每处理一定数量的物品，等待下一帧
                if (processedCount >= itemsPerFrame)
                {
                    processedCount = 0;
                    yield return null;
                }
            }

            _itemValueCacheInitialized = true;
            _legacyBossLootCandidateCacheInitialized = true;
            _itemValueCacheInitializing = false;
            DevLog("[BossRush] 物品价值缓存初始化完成，共缓存 " + _itemValueCache.Count + " 个物品，Boss 掉落候选缓存=" + _legacyBossLootCandidateIds.Count);
        }

        /// <summary>
        /// 从缓存获取物品价值信息
        /// </summary>
        private bool TryGetCachedItemValue(int itemId, out int value, out int quality)
        {
            ItemValueCacheEntry entry;
            if (_itemValueCache != null && _itemValueCache.TryGetValue(itemId, out entry))
            {
                value = entry.value;
                quality = entry.quality;
                return true;
            }
            value = 0;
            quality = -1;
            return false;
        }

        private HashSet<int> BuildGeneralBossLootCandidateIdSet()
        {
            HashSet<int> idSet = new HashSet<int>();
            try
            {
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData != null && tagsData.AllTags != null)
                {
                    List<Duckov.Utilities.Tag> baseExclude = BuildGeneralLootExcludeTags(tagsData);

                    foreach (Duckov.Utilities.Tag tag in tagsData.AllTags)
                    {
                        if (tag == null || baseExclude.Contains(tag))
                        {
                            continue;
                        }

                        ItemFilter filter = default(ItemFilter);
                        filter.requireTags = new Duckov.Utilities.Tag[] { tag };
                        filter.excludeTags = baseExclude.ToArray();
                        filter.minQuality = 1;
                        filter.maxQuality = 8;

                        int[] ids = ItemAssetsCollection.Search(filter);
                        if (ids == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < ids.Length; i++)
                        {
                            int id = ids[i];
                            if (id > 0 && !IsItemBlacklisted(id))
                            {
                                idSet.Add(id);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 收集候选物品ID失败: " + e.Message);
            }

            return idSet;
        }

        private void AddLegacyBossLootCandidateToQualityBucket(int itemId, int quality)
        {
            if (itemId <= 0 || quality < 1 || quality > 8)
            {
                return;
            }

            if (_legacyBossLootCandidateIdsByQuality == null)
            {
                _legacyBossLootCandidateIdsByQuality = new Dictionary<int, List<int>>();
            }

            List<int> bucket = null;
            if (!_legacyBossLootCandidateIdsByQuality.TryGetValue(quality, out bucket) || bucket == null)
            {
                bucket = new List<int>();
                _legacyBossLootCandidateIdsByQuality[quality] = bucket;
            }

            bucket.Add(itemId);
        }

        private int GetBossLootCandidateQuality(int itemId)
        {
            int value = 0;
            int quality = -1;
            if (TryGetCachedItemValue(itemId, out value, out quality) && quality > 0)
            {
                return quality;
            }

            try
            {
                var meta = ItemAssetsCollection.GetMetaData(itemId);
                if (meta.id > 0 && meta.quality > 0)
                {
                    return meta.quality;
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("GetBossLootCandidateQuality_meta", "读取 Boss 掉落候选品质元数据失败", e);
            }

            return 1;
        }

        private void BuildLegacyBossLootQualityBucketsFromIds(IEnumerable<int> ids, Dictionary<int, List<int>> buckets)
        {
            if (ids == null || buckets == null)
            {
                return;
            }

            foreach (int itemId in ids)
            {
                int quality = GetBossLootCandidateQuality(itemId);
                if (quality < 1 || quality > 8)
                {
                    continue;
                }

                List<int> bucket = null;
                if (!buckets.TryGetValue(quality, out bucket) || bucket == null)
                {
                    bucket = new List<int>();
                    buckets[quality] = bucket;
                }

                bucket.Add(itemId);
            }
        }

        private void CopyLegacyBossLootQualityBuckets(Dictionary<int, List<int>> destination)
        {
            if (destination == null || _legacyBossLootCandidateIdsByQuality == null)
            {
                return;
            }

            destination.Clear();
            foreach (KeyValuePair<int, List<int>> pair in _legacyBossLootCandidateIdsByQuality)
            {
                if (pair.Value == null || pair.Value.Count == 0)
                {
                    continue;
                }

                destination[pair.Key] = new List<int>(pair.Value);
            }
        }

        private bool TryGetLegacyBossLootCandidates(List<int> candidateIds, Dictionary<int, List<int>> qualityBuckets = null)
        {
            if (candidateIds == null)
            {
                return false;
            }

            candidateIds.Clear();

            if (_legacyBossLootCandidateCacheInitialized && _legacyBossLootCandidateIds != null && _legacyBossLootCandidateIds.Count > 0)
            {
                candidateIds.AddRange(_legacyBossLootCandidateIds);
                if (qualityBuckets != null)
                {
                    CopyLegacyBossLootQualityBuckets(qualityBuckets);
                }
                return true;
            }

            HashSet<int> dynamicIds = BuildGeneralBossLootCandidateIdSet();
            if (dynamicIds.Count == 0)
            {
                return false;
            }

            candidateIds.AddRange(dynamicIds);
            if (qualityBuckets != null)
            {
                qualityBuckets.Clear();
                BuildLegacyBossLootQualityBucketsFromIds(candidateIds, qualityBuckets);
            }

            return true;
        }

        private float ComputeLegacyBossLootBonusFactor(float maxHealth, float killDuration)
        {
            float healthFactor = 0f;
            float refMin = minBossBaseHealth;
            float refMax = maxBossBaseHealth;
            if (refMax > refMin && refMin > 0f)
            {
                healthFactor = Mathf.InverseLerp(refMin, refMax, maxHealth);
            }
            else
            {
                healthFactor = Mathf.Clamp01((maxHealth - 100f) / 1000f);
            }

            float speedFactor = ComputeBossKillSpeedFactor(maxHealth, killDuration);
            return Mathf.Clamp01((healthFactor * 0.8f) + (speedFactor * 0.2f));
        }

        /// <summary>
        /// 基于 Boss 最大血量和击杀耗时的 0..1 击杀速度评分。
        /// 血量越高 referenceWindow 越宽（容忍更长耗时），越早击杀评分越高。
        /// </summary>
        private static float ComputeBossKillSpeedFactor(float maxHealth, float killDuration)
        {
            float referenceWindow = 60f * (1f + maxHealth / 500f);
            if (referenceWindow <= 0f)
            {
                return 0f;
            }
            return Mathf.Clamp01(1f - (killDuration / referenceWindow));
        }
    }
}
