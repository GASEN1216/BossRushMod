using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛档次物品池的唯一查询点（搜刮点与 Boss 战利品共用）。
    ///
    /// - 用 `GetAllTypeIds` 而**不是** `Search`：官方 `Search` 在结果为空时会自行降级
    ///   `minQuality`/`maxQuality` 反复重搜，会把「星工遗存」悄悄降成生活杂物。
    ///   这里池空就如实为空，由调用方 fail-open（该点没有产出，其余点照常）。
    /// - 池形状与 `LootAndRewards.BuildGeneralBossLootCandidateIdSet` **一致**：
    ///   逐个官方 tag 做 `requireTags` 再取并集。这样无 tag 物品（占位件、内部道具、
    ///   无标签任务件）天然落在池外——只给 `excludeTags` 是挡不住它们的。
    /// - 排除标签走共享口径 `LootExcludeTagPolicy`（DestroyOnLootBox / DontDropOnDeadInSlot /
    ///   LockInDemoTag / Quest），另加 `Special` 与 `Character` 收窄。
    ///   `Special` 只能收窄、不能替代前面几个（官方有奖池把它列进 requireTags）。
    /// - 自定义内容与不该流通的官方 ID 靠 `LootBlacklistRegistry` 二次拦截。
    /// - 缓存按进程持有、由会话销毁时复位，避免每个搜刮点重复扫全表。
    /// </summary>
    internal static class SkyIslandLootPools
    {
        // 按品质带缓存（key = min * 100 + max），常规带与保底带共用同一条查询与同一份缓存。
        private static readonly Dictionary<int, int[]> cache = new Dictionary<int, int[]>();

        internal static int[] Get(SkyIslandLootTier tier)
        {
            return GetBand(SkyIslandLootTables.MinQuality(tier), SkyIslandLootTables.MaxQuality(tier));
        }

        /// <summary>档次保底带；该档没有保底时返回空数组，由调用方退回常规带。</summary>
        internal static int[] GetGuaranteeBand(SkyIslandLootTier tier)
        {
            int min = SkyIslandLootTables.GuaranteeMinQuality(tier);
            if (min <= 0) return EmptyPool;
            return GetBand(min, SkyIslandLootTables.MaxQuality(tier));
        }

        private static readonly int[] EmptyPool = new int[0];

        internal static int[] GetBand(int minQuality, int maxQuality)
        {
            if (minQuality > maxQuality) return EmptyPool;
            int key = minQuality * 100 + maxQuality;
            int[] cached;
            if (cache.TryGetValue(key, out cached)) return cached;
            var result = new List<int>();
            // 只有完整跑完的查询才进缓存。标签表还没就绪、或查询中途抛异常时得到的空池若也缓存，
            // 缓存要到模块销毁才清，于是本进程之后每一趟出击的箱子都是空的。
            bool complete = false;
            try
            {
                GameplayDataSettings.TagsData tags = GameplayDataSettings.Tags;
                if (tags != null && tags.AllTags != null)
                {
                    List<Tag> exclude = LootExcludeTagPolicy.BuildExcludeTags(tags, true, true);
                    Tag[] excludeArray = exclude.ToArray();
                    var unique = new HashSet<int>();
                    foreach (Tag tag in tags.AllTags)
                    {
                        if (tag == null || exclude.Contains(tag)) continue;
                        ItemFilter filter = default(ItemFilter);
                        filter.requireTags = new[] { tag };
                        filter.excludeTags = excludeArray;
                        filter.minQuality = minQuality;
                        filter.maxQuality = maxQuality;
                        filter.caliber = string.Empty;
                        int[] ids = ItemAssetsCollection.GetAllTypeIds(filter);
                        if (ids == null) continue;
                        for (int i = 0; i < ids.Length; i++)
                            if (ids[i] > 0 && !LootBlacklistRegistry.Contains(ids[i])) unique.Add(ids[i]);
                    }
                    result.AddRange(unique);
                    // 单件价值上限：皇冠、神秘钥匙这类收藏品不进岛上的池（CR-2026-09-11-001）。每个品质带每个进程只算一次。
                    result.RemoveAll(id => !WithinValueCap(id));
                    complete = true;
                }
                // 排序让同一 seed 在不同机器上抽到同一件：HashSet 的枚举顺序不稳定。
                result.Sort();
            }
            catch (Exception e)
            {
                complete = false;
                Debug.LogWarning("[SkyIslandLoot] 物资池查询失败 band=" + minQuality + "-" + maxQuality + "：" + e.Message);
            }
            cached = result.ToArray();
            if (complete) cache[key] = cached;
            Debug.Log("[SkyIslandLoot] POOL band=" + minQuality + "-" + maxQuality + " size=" + cached.Length);
            return cached;
        }

        /// <summary>
        /// 官方价值不超过 <see cref="SkyIslandLootTables.MaxPoolItemValue"/> 才进池。查不到 prefab 的原样放行：
        /// 装箱那一步本来就会先问 prefab，缺资源的物品在那里被跳过，这里不重复判断。
        /// </summary>
        private static bool WithinValueCap(int typeId)
        {
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                return prefab == null || SkyIslandLootTables.AllowedInPool(prefab.Value);
            }
            catch (Exception)
            {
                return true;
            }
        }

        internal static void ResetStaticCaches() { cache.Clear(); }
    }
}
