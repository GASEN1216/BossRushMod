using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Utilities;

namespace BossRush
{
    // Mode D 的发装池由同一个实例持有，Mode E/F 借用这份池时不会另建副本。
    internal sealed partial class ModeDItemPool
    {
        private Item lastGivenWeapon;
        private WavesArenaRuntimeModule lootCatalog;
        private Func<bool> useLegacyLoot;
        private Func<bool> needsMeleeStarterKit;
        private Func<int, string[], bool> isStarterCandidateAllowed;

        // 只绑定共享配装所需的查询；E / F 与 D 使用同一套池和物化步骤。
        internal void BindQueries(WavesArenaRuntimeModule catalog, Func<bool> legacyLoot,
            Func<bool> meleeStarterKit, Func<int, string[], bool> starterCandidateAllowed)
        {
            lootCatalog = catalog;
            useLegacyLoot = legacyLoot;
            needsMeleeStarterKit = meleeStarterKit;
            isStarterCandidateAllowed = starterCandidateAllowed;
        }

        internal bool modeDItemPoolsInitialized;
        internal readonly List<int> modeDWeaponPool = new List<int>();
        internal readonly List<int> modeDArmortPool = new List<int>();
        internal readonly Dictionary<int, List<int>> modeDArmortPoolByQuality = CreateModeDQualityBuckets();
        internal readonly List<int> modeDHelmetPool = new List<int>();
        internal readonly Dictionary<int, List<int>> modeDHelmetPoolByQuality = CreateModeDQualityBuckets();
        internal readonly List<int> modeDAmmoPool = new List<int>();
        internal readonly Dictionary<int, List<int>> modeDAmmoPoolByQuality = CreateModeDQualityBuckets();
        internal readonly List<int> modeDMedicalPool = new List<int>();
        internal readonly Dictionary<int, List<int>> modeDMedicalPoolByQuality = CreateModeDQualityBuckets();
        internal readonly List<int> modeDMeleePool = new List<int>();
        internal readonly List<int> modeDTotemPool = new List<int>();
        internal readonly Dictionary<int, List<int>> modeDTotemPoolByQuality = CreateModeDQualityBuckets();
        internal readonly List<int> modeDMaskPool = new List<int>();
        internal readonly Dictionary<int, List<int>> modeDMaskPoolByQuality = CreateModeDQualityBuckets();
        internal readonly List<int> modeDBackpackPool = new List<int>();
        internal readonly List<int> modeDAccessoryPool = new List<int>();
        internal readonly Dictionary<int, List<int>> modeDAccessoryPoolByQuality = CreateModeDQualityBuckets();
        internal static List<int> modeDGlobalItemPool;
        internal static bool modeDGlobalItemPoolInitialized;
        internal static Dictionary<int, List<int>> modeDGlobalItemPoolByQuality;
        internal static float lastGlobalPoolAttemptTime = -999f;

        internal static void ResetGlobalLootStaticCaches()
        {
            lastGlobalPoolAttemptTime = -999f;

            if (modeDGlobalItemPool != null)
            {
                modeDGlobalItemPool.Clear();
                modeDGlobalItemPool = null;
            }
            modeDGlobalItemPoolInitialized = false;

            if (modeDGlobalItemPoolByQuality != null)
            {
                foreach (KeyValuePair<int, List<int>> pair in modeDGlobalItemPoolByQuality)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.Clear();
                    }
                }
                modeDGlobalItemPoolByQuality.Clear();
                modeDGlobalItemPoolByQuality = null;
            }
        }

        internal void InitializeModeDItemPools(Func<string, Duckov.Utilities.Tag> findTagByName)
        {
            if (modeDItemPoolsInitialized) return;

            try
            {
                bool logEnabled = ModBehaviour.VerboseStartupDebugLogsEnabled;
                if (logEnabled)
                {
                    ModBehaviour.DevLog("[ModeD] 开始初始化物品池...");
                }

                modeDWeaponPool.Clear();
                modeDArmortPool.Clear();
                modeDHelmetPool.Clear();
                modeDAmmoPool.Clear();
                modeDMedicalPool.Clear();
                modeDMeleePool.Clear();
                modeDTotemPool.Clear();
                modeDMaskPool.Clear();
                modeDBackpackPool.Clear();
                ClearModeDQualityBuckets(modeDArmortPoolByQuality);
                ClearModeDQualityBuckets(modeDHelmetPoolByQuality);
                ClearModeDQualityBuckets(modeDAmmoPoolByQuality);
                ClearModeDQualityBuckets(modeDMedicalPoolByQuality);
                ClearModeDQualityBuckets(modeDTotemPoolByQuality);
                ClearModeDQualityBuckets(modeDMaskPoolByQuality);

                // 获取 Tag 系统
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null)
                {
                    if (logEnabled)
                    {
                        ModBehaviour.DevLog("[ModeD] [WARNING] 无法获取 TagsData");
                    }
                    return;
                }

                // 发装池保留更多“带标签即可入池”的物品，只排除 demo / quest。
                List<Duckov.Utilities.Tag> baseExclude = BuildModeDEquipmentPoolExcludeTags(tagsData);
                Duckov.Utilities.Tag[] excludeArray = baseExclude.ToArray();

                // 武器池（Gun Tag）- 黑名单物品已在 SearchItemsByTag 中统一过滤
                if (tagsData.Gun != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Gun, excludeArray);
                    AddDistinctItemIds(modeDWeaponPool, ids);
                }

                // 护甲池（Armor Tag）- 黑名单物品已在 SearchItemsByTag 中统一过滤
                if (tagsData.Armor != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Armor, excludeArray);
                    AddDistinctItemIds(modeDArmortPool, ids);
                }

                // 头盔池（Helmat Tag）- 黑名单物品已在 SearchItemsByTag 中统一过滤
                if (tagsData.Helmat != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Helmat, excludeArray);
                    AddDistinctItemIds(modeDHelmetPool, ids);
                }

                // 弹药池（Bullet Tag）
                if (tagsData.Bullet != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Bullet, excludeArray);
                    AddDistinctItemIds(modeDAmmoPool, ids);
                }

                // 医疗品池（优先使用原版 Medic Tag，兼容旧 Medical/Consumable/Healing）
                Duckov.Utilities.Tag medicalTag = FindTagByNameInInit("Medic");
                if (medicalTag == null) medicalTag = FindTagByNameInInit("Medical");
                if (medicalTag == null) medicalTag = FindTagByNameInInit("Consumable");
                if (medicalTag == null) medicalTag = FindTagByNameInInit("Healing");
                if (medicalTag != null)
                {
                    int[] ids = SearchItemsByTag(medicalTag, excludeArray);
                    AddDistinctItemIds(modeDMedicalPool, ids);
                }

                // P1-8: 预建近战武器池（MeleeWeapon Tag）
                Duckov.Utilities.Tag meleeTag = FindTagByNameInInit("MeleeWeapon");
                if (meleeTag != null)
                {
                    int[] ids = SearchItemsByTag(meleeTag, excludeArray);
                    AddDistinctItemIds(modeDMeleePool, ids);
                }

                // P1-8: 预建图腾池（Totem Tag）- 黑名单物品已在 SearchItemsByTag 中统一过滤
                Duckov.Utilities.Tag totemTag = FindTagByNameInInit("Totem");
                if (totemTag != null)
                {
                    int[] ids = SearchItemsByTag(totemTag, excludeArray);
                    AddDistinctItemIds(modeDTotemPool, ids);
                }

                // P1-8: 预建面具池（Mask / FaceMask / Headset 任一标签都算）
                Duckov.Utilities.Tag maskTag = FindTagByNameInInit("Mask");
                if (maskTag != null)
                {
                    int[] ids = SearchItemsByTag(maskTag, excludeArray);
                    AddDistinctItemIds(modeDMaskPool, ids);
                }

                Duckov.Utilities.Tag faceMaskTag = FindTagByNameInInit("FaceMask");
                if (faceMaskTag != null)
                {
                    int[] ids = SearchItemsByTag(faceMaskTag, excludeArray);
                    AddDistinctItemIds(modeDMaskPool, ids);
                }

                Duckov.Utilities.Tag headsetTag = FindTagByNameInInit("Headset");
                if (headsetTag != null)
                {
                    int[] ids = SearchItemsByTag(headsetTag, excludeArray);
                    AddDistinctItemIds(modeDMaskPool, ids);
                }

                // P1-8: 预建背包池（使用 GameplayDataSettings.Tags.Backpack）
                if (tagsData.Backpack != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Backpack, excludeArray);
                    AddDistinctItemIds(modeDBackpackPool, ids);
                }

                RebuildModeDQualityBuckets(modeDArmortPool, modeDArmortPoolByQuality);
                RebuildModeDQualityBuckets(modeDHelmetPool, modeDHelmetPoolByQuality);
                RebuildModeDQualityBuckets(modeDAmmoPool, modeDAmmoPoolByQuality);
                RebuildModeDQualityBuckets(modeDMedicalPool, modeDMedicalPoolByQuality);
                RebuildModeDQualityBuckets(modeDTotemPool, modeDTotemPoolByQuality);
                RebuildModeDQualityBuckets(modeDMaskPool, modeDMaskPoolByQuality);
                InitializeAccessoryPool(findTagByName);

                modeDItemPoolsInitialized = true;
                if (logEnabled)
                {
                    ModBehaviour.DevLog("[ModeD] 物品池初始化完成: " +
                           "武器=" + modeDWeaponPool.Count +
                           ", 护甲=" + modeDArmortPool.Count +
                           ", 头盔=" + modeDHelmetPool.Count +
                           ", 弹药=" + modeDAmmoPool.Count +
                           ", 医疗=" + modeDMedicalPool.Count +
                           ", 近战=" + modeDMeleePool.Count +
                           ", 图腾=" + modeDTotemPool.Count +
                           ", 面具=" + modeDMaskPool.Count +
                           ", 背包=" + modeDBackpackPool.Count);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] InitializeModeDItemPools 失败: " + e.Message);
            }
        }

        private static Dictionary<int, List<int>> CreateModeDQualityBuckets()
        {
            Dictionary<int, List<int>> buckets = new Dictionary<int, List<int>>();
            for (int quality = 1; quality <= 8; quality++)
            {
                buckets[quality] = new List<int>();
            }

            return buckets;
        }

        private static void ClearModeDQualityBuckets(Dictionary<int, List<int>> buckets)
        {
            if (buckets == null)
            {
                return;
            }

            for (int quality = 1; quality <= 8; quality++)
            {
                List<int> bucket;
                if (!buckets.TryGetValue(quality, out bucket) || bucket == null)
                {
                    buckets[quality] = new List<int>();
                    continue;
                }

                bucket.Clear();
            }
        }

        private static void AddDistinctItemIds(List<int> targetPool, int[] ids)
        {
            if (targetPool == null || ids == null || ids.Length == 0)
            {
                return;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                if (id > 0 && !targetPool.Contains(id))
                {
                    targetPool.Add(id);
                }
            }
        }

        private List<Duckov.Utilities.Tag> BuildModeDEquipmentPoolExcludeTags(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        {
            List<Duckov.Utilities.Tag> excludeTags = new List<Duckov.Utilities.Tag>();
            if (tagsData == null)
            {
                return excludeTags;
            }

            LootExcludeTagPolicy.AddUnique(excludeTags, tagsData.LockInDemoTag);
            LootExcludeTagPolicy.AddUnique(excludeTags, LootExcludeTagPolicy.TryFindQuestTag(tagsData));

            return excludeTags;
        }

        private void RebuildModeDQualityBuckets(List<int> sourcePool, Dictionary<int, List<int>> buckets)
        {
            ClearModeDQualityBuckets(buckets);
            if (sourcePool == null || buckets == null || sourcePool.Count == 0)
            {
                return;
            }

            for (int i = 0; i < sourcePool.Count; i++)
            {
                int id = sourcePool[i];
                if (id <= 0)
                {
                    continue;
                }

                int quality = 1;
                try
                {
                    quality = Mathf.Clamp(ItemAssetsCollection.GetMetaData(id).quality, 1, 8);
                }
                catch
                {
                }

                List<int> bucket;
                if (!buckets.TryGetValue(quality, out bucket) || bucket == null)
                {
                    bucket = new List<int>();
                    buckets[quality] = bucket;
                }

                bucket.Add(id);
            }
        }

        /// <summary>
        /// 根据Tag搜索物品（包含所有品质），自动过滤黑名单物品
        /// </summary>
        private int[] SearchItemsByTag(Duckov.Utilities.Tag tag, Duckov.Utilities.Tag[] excludeTags)
        {
            try
            {
                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { tag };
                filter.excludeTags = excludeTags;
                filter.minQuality = 1;
                filter.maxQuality = 8; // 包含所有品质
                int[] rawIds = ItemAssetsCollection.Search(filter);

                if (rawIds == null || rawIds.Length == 0)
                {
                    return rawIds;
                }

                // 过滤掉黑名单物品（统一使用 LootBlacklistRegistry）
                List<int> filteredIds = new List<int>(rawIds.Length);
                for (int i = 0; i < rawIds.Length; i++)
                {
                    int id = rawIds[i];
                    if (id > 0 && !ModBehaviour.IsItemBlacklisted(id))
                    {
                        filteredIds.Add(id);
                    }
                }
                return filteredIds.ToArray();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 根据名称查找Tag（用于初始化）
        /// </summary>
        internal Duckov.Utilities.Tag FindTagByNameInInit(string tagName)
        {
            try
            {
                foreach (var tag in GameplayDataSettings.Tags.AllTags)
                {
                    if (tag != null && tag.name == tagName)
                    {
                        return tag;
                    }
                }
            }
            catch {}
            return null;
        }

        internal void InitializeAccessoryPool(Func<string, Duckov.Utilities.Tag> findTagByName)
        {
            try
            {
                modeDAccessoryPool.Clear();
                ClearModeDQualityBuckets(modeDAccessoryPoolByQuality);

                // 通过名字查找配件 Tag
                Duckov.Utilities.Tag accessoryTag = findTagByName("Accessory");
                if (accessoryTag == null)
                {
                    ModBehaviour.DevLog("[ModeD] 未找到 Accessory Tag，跳过配件池初始化");
                    return;
                }

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { accessoryTag };
                filter.minQuality = 1;
                filter.maxQuality = 8; // 包含所有品质
                int[] accessoryIds = ItemAssetsCollection.Search(filter);

                AddDistinctItemIds(modeDAccessoryPool, accessoryIds);

                RebuildModeDQualityBuckets(modeDAccessoryPool, modeDAccessoryPoolByQuality);

                ModBehaviour.DevLog("[ModeD] 配件池初始化完成，数量: " + modeDAccessoryPool.Count);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] InitializeAccessoryPool 失败: " + e.Message);
            }
        }

    }
}
