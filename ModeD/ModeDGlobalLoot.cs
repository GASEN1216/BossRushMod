// ============================================================================
// ModeDGlobalLoot.cs - Mode D 全局物品池管理
// ============================================================================
// 模块说明：
//   管理 Mode D 模式下的全局物品掉落池。
//   从游戏的全物品库中收集可掉落物品，排除黑名单和不应掉落的物品。
//   
// 主要功能：
//   - 构建和缓存全局掉落物品池
//   - 提供随机物品生成接口
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// Mode D 全物品池随机掉落辅助模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>上次尝试构建全局掉落池的时间（用于节流，避免 TagsData 未就绪时高频重试）</summary>
        private static float lastGlobalPoolAttemptTime = -999f;
        /// <summary>全局掉落池构建尝试的最小间隔（秒）</summary>
        private const float GLOBAL_POOL_RETRY_INTERVAL = 5f;
        /// <summary>按品质缓存的全局掉落池，供 Legacy 品质分布精确选档使用。</summary>
        private static Dictionary<int, List<int>> modeDGlobalItemPoolByQuality = null;

        /// <summary>
        /// 仅在 TagsData 已就绪时预热全局掉落池，避免触发重试节流影响正式入口构建
        /// </summary>
        private void TryPrewarmModeDGlobalItemPool()
        {
            if (modeDGlobalItemPoolInitialized && modeDGlobalItemPool != null && modeDGlobalItemPool.Count > 0)
            {
                return;
            }

            try
            {
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null || tagsData.AllTags == null || tagsData.AllTags.Count == 0)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            EnsureModeDGlobalItemPool();
        }

        /// <summary>
        /// 构建/缓存 Mode D 全局掉落物品池
        /// <para>从全物品池中收集可掉落物品，排除黑名单和不应掉落的 Tag</para>
        /// </summary>
        private void EnsureModeDGlobalItemPool()
        {
            if (modeDGlobalItemPoolInitialized && modeDGlobalItemPool != null && modeDGlobalItemPool.Count > 0)
            {
                return;
            }

            // 节流：避免 TagsData 未就绪时高频重试
            if (Time.unscaledTime - lastGlobalPoolAttemptTime < GLOBAL_POOL_RETRY_INTERVAL)
            {
                return;
            }
            lastGlobalPoolAttemptTime = Time.unscaledTime;

            if (modeDGlobalItemPool == null)
            {
                modeDGlobalItemPool = new List<int>();
            }
            else
            {
                modeDGlobalItemPool.Clear();
            }

            if (modeDGlobalItemPoolByQuality == null)
            {
                modeDGlobalItemPoolByQuality = new Dictionary<int, List<int>>();
            }

            for (int quality = 1; quality <= 8; quality++)
            {
                List<int> bucket;
                if (!modeDGlobalItemPoolByQuality.TryGetValue(quality, out bucket) || bucket == null)
                {
                    bucket = new List<int>();
                    modeDGlobalItemPoolByQuality[quality] = bucket;
                }
                else
                {
                    bucket.Clear();
                }
            }

            try
            {
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null || tagsData.AllTags == null || tagsData.AllTags.Count == 0)
                {
                    // TagsData 未就绪，不设置初始化标记，允许下次重试
                    return;
                }

                List<Duckov.Utilities.Tag> baseExclude = BuildGeneralLootExcludeTags(tagsData);

                // P1-3 优化：把 ToArray() 提到循环外，避免每次循环都分配数组
                Duckov.Utilities.Tag[] excludeArray = baseExclude.ToArray();

                List<Duckov.Utilities.Tag> includeTags = new List<Duckov.Utilities.Tag>();
                for (int i = 0; i < tagsData.AllTags.Count; i++)
                {
                    Duckov.Utilities.Tag tag = tagsData.AllTags[i];
                    if (tag == null)
                    {
                        continue;
                    }
                    if (baseExclude.Contains(tag))
                    {
                        continue;
                    }
                    includeTags.Add(tag);
                }

                HashSet<int> idSet = new HashSet<int>();

                for (int i = 0; i < includeTags.Count; i++)
                {
                    Duckov.Utilities.Tag requireTag = includeTags[i];
                    if (requireTag == null)
                    {
                        continue;
                    }

                    ItemFilter filter = default(ItemFilter);
                    filter.requireTags = new Duckov.Utilities.Tag[] { requireTag };
                    filter.excludeTags = excludeArray;
                    filter.minQuality = 1;
                    filter.maxQuality = 8;

                    int[] ids = ItemAssetsCollection.Search(filter);
                    if (ids == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < ids.Length; j++)
                    {
                        int id = ids[j];
                        if (id > 0)
                        {
                            if (IsItemBlacklisted(id))
                            {
                                continue;
                            }
                            idSet.Add(id);
                        }
                    }
                }

                if (idSet.Count > 0)
                {
                    foreach (int id in idSet)
                    {
                        modeDGlobalItemPool.Add(id);

                        int quality = 1;
                        try
                        {
                            quality = Mathf.Clamp(ItemAssetsCollection.GetMetaData(id).quality, 1, 8);
                        }
                        catch {}

                        List<int> bucket;
                        if (modeDGlobalItemPoolByQuality.TryGetValue(quality, out bucket) && bucket != null)
                        {
                            bucket.Add(id);
                        }
                    }
                }

                // P1-3 修复：只在池非空时设置初始化标记，避免"空池锁死"
                // 如果 Search 返回空（TagsData 未就绪或其他原因），保持 false 允许下次重试
                if (modeDGlobalItemPool.Count > 0)
                {
                    modeDGlobalItemPoolInitialized = true;
                    DevLog("[ModeD] 全局掉落池构建完成：物品数=" + modeDGlobalItemPool.Count);
                }
                else
                {
                    // 池为空，不设置初始化标记，允许下次重试
                    DevLog("[ModeD] [WARNING] 全局掉落池为空，未设置初始化标记，将稍后重试");
                }
            }
            catch
            {
            }
        }

        private bool TryPickRandomModeDGlobalItemIdInRange(int minQ, int maxQ, out int selectedId)
        {
            selectedId = 0;

            int clampedMin = Mathf.Clamp(minQ, 1, 8);
            int clampedMax = Mathf.Clamp(maxQ, clampedMin, 8);
            int seen = 0;

            if (modeDGlobalItemPoolByQuality == null)
            {
                return false;
            }

            for (int quality = clampedMin; quality <= clampedMax; quality++)
            {
                List<int> bucket;
                if (!modeDGlobalItemPoolByQuality.TryGetValue(quality, out bucket) ||
                    bucket == null ||
                    bucket.Count == 0)
                {
                    continue;
                }

                for (int i = 0; i < bucket.Count; i++)
                {
                    int id = bucket[i];
                    if (id <= 0)
                    {
                        continue;
                    }

                    seen++;
                    if (seen == 1 || UnityEngine.Random.Range(0, seen) == 0)
                    {
                        selectedId = id;
                    }
                }
            }

            return selectedId > 0;
        }

        private int ApplyModeDGlobalItemSpecialSelectionRules(int selectedId, int minQ, int maxQ)
        {
            if (selectedId <= 0)
            {
                return 0;
            }

            // 皇冠（1254）权重降为0.1，与其他模式保持一致
            // 实现方式：90%概率在同一品质区间内重新抽取一次
            const int CROWN_TYPE_ID = 1254;
            const float CROWN_REROLL_CHANCE = 0.9f;

            if (selectedId != CROWN_TYPE_ID || UnityEngine.Random.value >= CROWN_REROLL_CHANCE)
            {
                return selectedId;
            }

            int rerolledId;
            if (TryPickRandomModeDGlobalItemIdInRange(minQ, maxQ, out rerolledId) && rerolledId > 0)
            {
                return rerolledId;
            }

            return selectedId;
        }

        /// <summary>
        /// 从全物品池中随机选一个物品实例
        /// </summary>
        /// <param name="minQ">最低品质（当前版本不再过滤，仅为兼容签名）</param>
        /// <param name="maxQ">最高品质（当前版本不再过滤，仅为兼容签名）</param>
        /// <returns>随机物品实例，失败返回 null</returns>
        internal Item CreateRandomGlobalItemForModeD(int minQ, int maxQ)
        {
            try
            {
                EnsureModeDGlobalItemPool();

                if (modeDGlobalItemPool == null || modeDGlobalItemPool.Count == 0)
                {
                    return null;
                }

                int id;
                if (!TryPickRandomModeDGlobalItemIdInRange(1, 8, out id))
                {
                    return null;
                }

                id = ApplyModeDGlobalItemSpecialSelectionRules(id, 1, 8);
                return ItemAssetsCollection.InstantiateSync(id);
            }
            catch
            {
                return null;
            }
        }

        internal Item CreateRandomGlobalItemForModeD(int minQ, int maxQ, float enemyHealth)
        {
            try
            {
                EnsureModeDGlobalItemPool();

                if (modeDGlobalItemPool == null || modeDGlobalItemPool.Count == 0)
                {
                    return null;
                }

                int clampedMin = Mathf.Clamp(minQ, 1, 8);
                int clampedMax = Mathf.Clamp(maxQ, clampedMin, 8);
                float bonusFactor = ComputeModeDStyleEnemyLootBonusFactorFromHealth(enemyHealth);

                int selectedId = 0;
                if (ShouldUseLegacyModeDStyleEnemyLootQualityDistribution())
                {
                    LegacyBossLootQualityDistribution distribution =
                        LegacyBossLootProbabilityModel.BuildDistribution(bonusFactor);
                    int selectedQuality = PickBossRushStyleQualityByLegacyDistribution(
                        distribution,
                        modeDGlobalItemPoolByQuality,
                        clampedMin,
                        clampedMax);

                    List<int> bucket;
                    if (selectedQuality > 0 &&
                        modeDGlobalItemPoolByQuality != null &&
                        modeDGlobalItemPoolByQuality.TryGetValue(selectedQuality, out bucket) &&
                        bucket != null &&
                        bucket.Count > 0)
                    {
                        selectedId = bucket[UnityEngine.Random.Range(0, bucket.Count)];
                    }
                }
                else
                {
                    int selectedQuality = PickBossRushStyleQualityByNonLegacyWeights(
                        bonusFactor,
                        modeDGlobalItemPoolByQuality,
                        clampedMin,
                        clampedMax);

                    List<int> bucket;
                    if (selectedQuality > 0 &&
                        modeDGlobalItemPoolByQuality != null &&
                        modeDGlobalItemPoolByQuality.TryGetValue(selectedQuality, out bucket) &&
                        bucket != null &&
                        bucket.Count > 0)
                    {
                        selectedId = bucket[UnityEngine.Random.Range(0, bucket.Count)];
                    }
                }

                if (selectedId <= 0)
                {
                    if (!TryPickRandomModeDGlobalItemIdInRange(clampedMin, clampedMax, out selectedId))
                    {
                        return null;
                    }
                }

                selectedId = ApplyModeDGlobalItemSpecialSelectionRules(selectedId, clampedMin, clampedMax);
                return ItemAssetsCollection.InstantiateSync(selectedId);
            }
            catch
            {
                try
                {
                    int clampedMin = Mathf.Clamp(minQ, 1, 8);
                    int clampedMax = Mathf.Clamp(maxQ, clampedMin, 8);
                    int fallbackId;
                    if (TryPickRandomModeDGlobalItemIdInRange(clampedMin, clampedMax, out fallbackId))
                    {
                        fallbackId = ApplyModeDGlobalItemSpecialSelectionRules(fallbackId, clampedMin, clampedMax);
                        return ItemAssetsCollection.InstantiateSync(fallbackId);
                    }
                }
                catch
                {
                }

                return null;
            }
        }
    }
}
