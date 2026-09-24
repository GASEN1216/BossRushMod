using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.ItemBuilders;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        private const float LOOT_WARNING_LOG_INTERVAL = 5f;
        internal void LogLootWarningLimited(string key, string message, Exception e = null)
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
            ModBehaviour.DevLog("[BossRush] [WARNING] " + message + (e != null ? ": " + e.Message : string.Empty));
        }

        internal void InitializeItemValueCacheAsync()
        {
            if (ItemValueCacheInitialized || ItemValueCacheInitializing)
            {
                return;
            }
            ItemValueCacheInitializing = true;
            owner.StartCoroutine(InitializeItemValueCacheCoroutine());
        }

        /// <summary>
        /// 物品价值缓存初始化协程 - 分帧处理避免卡顿
        /// </summary>
        private IEnumerator InitializeItemValueCacheCoroutine()
        {
            if (ItemValueCache == null)
            {
                ItemValueCache = new Dictionary<int, ItemValueCacheEntry>();
            }
            else
            {
                ItemValueCache.Clear();
            }

            if (LegacyBossLootCandidateIds == null)
            {
                LegacyBossLootCandidateIds = new List<int>();
            }
            else
            {
                LegacyBossLootCandidateIds.Clear();
            }

            if (LegacyBossLootCandidateIdsByQuality == null)
            {
                LegacyBossLootCandidateIdsByQuality = new Dictionary<int, List<int>>();
            }
            else
            {
                LegacyBossLootCandidateIdsByQuality.Clear();
            }

            LegacyBossLootCandidateCacheInitialized = false;

            ModBehaviour.DevLog("[BossRush] 开始初始化物品价值缓存...");

            // 收集所有候选物品ID
            HashSet<int> idSet = BuildGeneralBossLootCandidateIdSet();

            ModBehaviour.DevLog("[BossRush] 物品价值缓存：共需处理 " + idSet.Count + " 个物品");

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
                        ItemValueCache[itemId] = entry;

                        LegacyBossLootCandidateIds.Add(itemId);
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

            ItemValueCacheInitialized = true;
            LegacyBossLootCandidateCacheInitialized = true;
            ItemValueCacheInitializing = false;
            ModBehaviour.DevLog("[BossRush] 物品价值缓存初始化完成，共缓存 " + ItemValueCache.Count + " 个物品，Boss 掉落候选缓存=" + LegacyBossLootCandidateIds.Count);
        }

        /// <summary>
        /// 从缓存获取物品价值信息
        /// </summary>
        internal bool TryGetCachedItemValue(int itemId, out int value, out int quality)
        {
            ItemValueCacheEntry entry;
            if (ItemValueCache != null && ItemValueCache.TryGetValue(itemId, out entry))
            {
                value = entry.value;
                quality = entry.quality;
                return true;
            }
            value = 0;
            quality = -1;
            return false;
        }

        internal HashSet<int> BuildGeneralBossLootCandidateIdSet()
        {
            HashSet<int> idSet = new HashSet<int>();
            BuildGeneralBossLootCandidateIdSet(idSet);
            return idSet;
        }

        internal bool BuildGeneralBossLootCandidateIdSet(HashSet<int> idSet)
        {
            if (idSet == null)
            {
                return false;
            }

            idSet.Clear();
            try
            {
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData != null && tagsData.AllTags != null)
                {
                    List<Duckov.Utilities.Tag> baseExclude = LootExcludeTagPolicy.BuildExcludeTags(tagsData);

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
                            if (id > 0 && !LootBlacklistRegistry.Contains(id))
                            {
                                idSet.Add(id);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 收集候选物品ID失败: " + e.Message);
            }

            return idSet.Count > 0;
        }

        private void AddLegacyBossLootCandidateToQualityBucket(int itemId, int quality)
        {
            if (itemId <= 0 || quality < 1 || quality > 8)
            {
                return;
            }

            if (LegacyBossLootCandidateIdsByQuality == null)
            {
                LegacyBossLootCandidateIdsByQuality = new Dictionary<int, List<int>>();
            }

            List<int> bucket = null;
            if (!LegacyBossLootCandidateIdsByQuality.TryGetValue(quality, out bucket) || bucket == null)
            {
                bucket = new List<int>();
                LegacyBossLootCandidateIdsByQuality[quality] = bucket;
            }

            bucket.Add(itemId);
        }

        internal int GetBossLootCandidateQuality(int itemId)
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

        internal void BuildLegacyBossLootQualityBucketsFromIds(IEnumerable<int> ids, Dictionary<int, List<int>> buckets)
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
            if (destination == null || LegacyBossLootCandidateIdsByQuality == null)
            {
                return;
            }

            ClearLegacyBossLootQualityBucketLists(destination);
            foreach (KeyValuePair<int, List<int>> pair in LegacyBossLootCandidateIdsByQuality)
            {
                if (pair.Value == null || pair.Value.Count == 0)
                {
                    continue;
                }

                List<int> bucket = null;
                if (!destination.TryGetValue(pair.Key, out bucket) || bucket == null)
                {
                    bucket = new List<int>(pair.Value.Count);
                    destination[pair.Key] = bucket;
                }
                bucket.AddRange(pair.Value);
            }
        }

        private void ClearLegacyBossLootQualityBucketLists(Dictionary<int, List<int>> buckets)
        {
            if (buckets == null)
            {
                return;
            }

            foreach (KeyValuePair<int, List<int>> pair in buckets)
            {
                if (pair.Value != null)
                {
                    pair.Value.Clear();
                }
            }
        }

        internal void ClearLegacyBossGuaranteeQualityBucketsScratch()
        {
            ClearLegacyBossLootQualityBucketLists(legacyBossGuaranteeQualityBucketsScratch);
        }

        internal bool TryGetLegacyBossLootCandidates(List<int> candidateIds, Dictionary<int, List<int>> qualityBuckets = null)
        {
            if (candidateIds == null)
            {
                return false;
            }

            candidateIds.Clear();

            if (LegacyBossLootCandidateCacheInitialized && LegacyBossLootCandidateIds != null && LegacyBossLootCandidateIds.Count > 0)
            {
                candidateIds.AddRange(LegacyBossLootCandidateIds);
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
                ClearLegacyBossLootQualityBucketLists(qualityBuckets);
                BuildLegacyBossLootQualityBucketsFromIds(candidateIds, qualityBuckets);
            }

            return true;
        }

        internal float ComputeLegacyBossLootBonusFactor(float maxHealth, float killDuration)
        {
            float healthFactor = 0f;
            float refMin = MinBossBaseHealth;
            float refMax = MaxBossBaseHealth;
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
        internal static float ComputeBossKillSpeedFactor(float maxHealth, float killDuration)
        {
            float referenceWindow = 60f * (1f + maxHealth / 500f);
            if (referenceWindow <= 0f)
            {
                return 0f;
            }
            return Mathf.Clamp01(1f - (killDuration / referenceWindow));
        }
        internal bool InventoryContainsItemAtLeastQuality(Inventory inv, int minimumQuality)
        {
            if (inv == null)
            {
                return false;
            }

            try
            {
                List<Item> content = inv.Content;
                if (content == null)
                {
                    return false;
                }

                for (int i = 0; i < content.Count; i++)
                {
                    Item item = content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    int quality = 0;
                    try
                    {
                        quality = item.Quality;
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("InventoryContainsItemAtLeastQuality_quality", "读取掉落箱物品品质失败", e);
                    }

                    if (quality >= minimumQuality)
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("InventoryContainsItemAtLeastQuality_scan", "扫描掉落箱保底品质失败", e);
            }

            return false;
        }

        internal int GetLegacyBossGuaranteeTypeId(int desiredQuality, out int actualQuality)
        {
            actualQuality = -1;

            // 复用实例级 scratch 容器，避免每次保底判定都分配候选列表/品质桶字典。
            // TryGetLegacyBossLootCandidates 内部会先 Clear 候选列表并重建品质桶，
            // 这里仅需在使用前清空品质桶的复用列表，使用后由 finally 再次清空释放引用。
            List<int> candidateIds = legacyBossGuaranteeCandidateScratch;
            Dictionary<int, List<int>> qualityBuckets = legacyBossGuaranteeQualityBucketsScratch;
            ClearLegacyBossGuaranteeQualityBucketsScratch();

            try
            {
                if (!TryGetLegacyBossLootCandidates(candidateIds, qualityBuckets))
                {
                    return -1;
                }

                for (int quality = desiredQuality; quality >= 5; quality--)
                {
                    List<int> bucket = null;
                    if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                    {
                        continue;
                    }

                    actualQuality = quality;
                    return bucket[UnityEngine.Random.Range(0, bucket.Count)];
                }

                return -1;
            }
            finally
            {
                candidateIds.Clear();
                ClearLegacyBossGuaranteeQualityBucketsScratch();
            }
        }

        internal bool TryAddLegacyBossGuaranteeItem(Inventory inv)
        {
            if (inv == null)
            {
                return false;
            }

            if (InventoryContainsItemAtLeastQuality(inv, 5))
            {
                return false;
            }

            int desiredQuality = LegacyBossLootProbabilityModel.RollGuaranteeQuality(UnityEngine.Random.value);
            int actualQuality = -1;
            int rewardTypeId = GetLegacyBossGuaranteeTypeId(desiredQuality, out actualQuality);
            if (rewardTypeId <= 0)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 原版战利品保底失败：未找到可用的 Q5+ 候选，desiredQuality=" + desiredQuality);
                return false;
            }

            Item reward = null;
            try
            {
                reward = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                if (reward == null)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 原版战利品保底失败：实例化物品失败, typeId=" + rewardTypeId);
                    return false;
                }

                if (!inv.AddItem(reward))
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 原版战利品保底失败：AddItem 失败, typeId=" + rewardTypeId);
                    return false;
                }

                ModBehaviour.DevLog("[BossRush] 原版战利品保底已追加: desiredQuality=" + desiredQuality + ", actualQuality=" + actualQuality + ", typeId=" + rewardTypeId);
                reward = null;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 原版战利品保底追加失败: " + e.Message);
                return false;
            }
            finally
            {
                try
                {
                    if (reward != null)
                    {
                        reward.DestroyTree();
                    }
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("TryAddLegacyBossGuaranteeItem_cleanup", "清理未成功追加的保底物品失败", e);
                }
            }
        }

    }
}
