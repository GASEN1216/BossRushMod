// E/F、丧尸商店与随机事件共用的商人预设和分类商品目录。
// 保留原静态缓存与搜索顺序；宿主只装配预设、标签查询。
using System;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed class ModeEFMerchantCatalog
    {
        private readonly Func<IReadOnlyDictionary<string, CharacterRandomPreset>> getCharacterPresets;
        private readonly Func<string, Duckov.Utilities.Tag> findTag;
        private IReadOnlyDictionary<string, CharacterRandomPreset> CharacterPresets { get { return getCharacterPresets(); } }

        internal ModeEFMerchantCatalog(Func<IReadOnlyDictionary<string, CharacterRandomPreset>> getCharacterPresets,
            Func<string, Duckov.Utilities.Tag> findTag)
        {
            this.getCharacterPresets = getCharacterPresets;
            this.findTag = findTag;
        }

        internal bool IsExcludedMedicalItem(int id) { return modeEMedicalShopExcludedIds.Contains(id); }

        internal static void ResetStaticCaches()
        {
            cachedModeEMerchantPreset = null;
            if (modeEMerchantCategoryItemCache != null)
            {
                modeEMerchantCategoryItemCache.Clear();
            }
        }

        /// <summary>Mode E 医疗品商店需排除的原版物品 TypeID</summary>
        private static readonly HashSet<int> modeEMedicalShopExcludedIds = new HashSet<int>
        {
            88, 89, 136, 331, 1428, 1429
        };

        /// <summary>缓存的 Mode E 商人预设，避免重复扫描所有 CharacterRandomPreset</summary>
        private static CharacterRandomPreset cachedModeEMerchantPreset = null;

        /// <summary>缓存的 Mode E 商店分类商品 ID，Key 为分类后缀（如 Gun / Medical）</summary>
        private static readonly Dictionary<string, int[]> modeEMerchantCategoryItemCache = new Dictionary<string, int[]>();

        internal CharacterRandomPreset GetModeEMerchantPreset()
        {
            try
            {
                if (cachedModeEMerchantPreset != null)
                {
                    return cachedModeEMerchantPreset;
                }

                CharacterRandomPreset merchantPreset = null;

                if (CharacterPresets != null && CharacterPresets.Count > 0)
                {
                    foreach (var kvp in CharacterPresets)
                    {
                        string nameKey = kvp.Key;
                        if (string.IsNullOrEmpty(nameKey)) continue;
                        if (nameKey.Contains("Merchant") && nameKey.Contains("Myst"))
                        {
                            merchantPreset = kvp.Value;
                            break;
                        }
                    }

                    if (merchantPreset == null)
                    {
                        foreach (var kvp in CharacterPresets)
                        {
                            string nameKey = kvp.Key;
                            if (string.IsNullOrEmpty(nameKey)) continue;
                            if (nameKey.Contains("Merchant"))
                            {
                                merchantPreset = kvp.Value;
                                break;
                            }
                        }
                    }
                }

                if (merchantPreset != null && cachedModeEMerchantPreset == null)
                {
                    cachedModeEMerchantPreset = merchantPreset;
                }

                if (merchantPreset == null)
                {
                    CharacterRandomPreset[] allPresets = ObjectCache.GetCharacterPresets();
                    CharacterRandomPreset fallbackMerchant = null;
                    for (int i = 0; i < allPresets.Length; i++)
                    {
                        CharacterRandomPreset preset = allPresets[i];
                        if (preset == null) continue;

                        try
                        {
                            string nameKey = preset.nameKey;
                            if (string.IsNullOrEmpty(nameKey)) continue;
                            if (nameKey.Contains("Merchant"))
                            {
                                if (nameKey.Contains("Myst"))
                                {
                                    merchantPreset = preset;
                                    break;
                                }

                                if (fallbackMerchant == null)
                                {
                                    fallbackMerchant = preset;
                                }
                            }
                        }
                        catch { }
                    }

                    if (merchantPreset == null)
                    {
                        merchantPreset = fallbackMerchant;
                    }

                    if (merchantPreset == null)
                    {
                        FieldInfo iconField = BossRushEagerReflectionCache.CharacterRandomPreset_CharacterIconType;
                        if (iconField != null)
                        {
                            for (int i = 0; i < allPresets.Length; i++)
                            {
                                CharacterRandomPreset preset = allPresets[i];
                                if (preset == null) continue;

                                try
                                {
                                    if ((int)iconField.GetValue(preset) == 4)
                                    {
                                        merchantPreset = preset;
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

                if (merchantPreset != null)
                {
                    cachedModeEMerchantPreset = merchantPreset;
                }

                return merchantPreset;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] GetModeEMerchantPreset 失败: " + e.Message);
                return null;
            }
        }

        internal List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> GetModeEMerchantCategories(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        {
            var categories = new List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>>();
            if (tagsData == null)
            {
                return categories;
            }

            if (tagsData.Gun != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Gun },
                    "BossRush_ModeE_Shop_Gun", "Gun"));

            Duckov.Utilities.Tag meleeTag = findTag("MeleeWeapon");
            if (meleeTag != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { meleeTag },
                    "BossRush_ModeE_Shop_Melee", "Melee"));

            Duckov.Utilities.Tag accessoryTag = findTag("Accessory");
            if (accessoryTag != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { accessoryTag },
                    "BossRush_ModeE_Shop_Accessory", "Accessory"));

            if (tagsData.Bullet != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Bullet },
                    "BossRush_ModeE_Shop_Bullet", "Bullet"));

            if (tagsData.Helmat != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Helmat },
                    "BossRush_ModeE_Shop_Helmat", "Helmat"));

            if (tagsData.Armor != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Armor },
                    "BossRush_ModeE_Shop_Armor", "Armor"));

            if (tagsData.Backpack != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Backpack },
                    "BossRush_ModeE_Shop_Backpack", "Backpack"));

            Duckov.Utilities.Tag totemTag = findTag("Totem");
            if (totemTag != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { totemTag },
                    "BossRush_ModeE_Shop_Totem", "Totem"));

            Duckov.Utilities.Tag maskTag = findTag("Mask");
            if (maskTag == null) maskTag = findTag("FaceMask");
            Duckov.Utilities.Tag headsetTag = findTag("Headset");
            var faceWearTags = new List<Duckov.Utilities.Tag>();
            if (maskTag != null) faceWearTags.Add(maskTag);
            if (headsetTag != null) faceWearTags.Add(headsetTag);
            if (faceWearTags.Count > 0)
                categories.Add(System.Tuple.Create(
                    faceWearTags,
                    "BossRush_ModeE_Shop_Mask", "Mask"));

            Duckov.Utilities.Tag medTag = findTag("Medic");
            if (medTag == null) medTag = findTag("Medical");
            if (medTag == null) medTag = findTag("Consumable");
            if (medTag == null) medTag = findTag("Healing");
            if (medTag != null)
            {
                var medTags = new List<Duckov.Utilities.Tag> { medTag };
                Duckov.Utilities.Tag injectorTag = findTag("Injector");
                if (injectorTag != null)
                {
                    medTags.Add(injectorTag);
                }
                categories.Add(System.Tuple.Create(
                    medTags,
                    "BossRush_ModeE_Shop_Medical", "Medical"));
            }

            Duckov.Utilities.Tag foodTag = findTag("Food");
            if (foodTag != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { foodTag },
                    "BossRush_ModeE_Shop_Food", "Food"));

            if (tagsData.Bait != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Bait },
                    "BossRush_ModeE_Shop_Bait", "Bait"));

            return categories;
        }

        internal void PrewarmModeEMerchantCaches()
        {
            try
            {
                GetModeEMerchantPreset();

                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null)
                {
                    return;
                }

                Duckov.Utilities.Tag[] emptyExclude = new Duckov.Utilities.Tag[0];
                List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> categories = GetModeEMerchantCategories(tagsData);
                for (int i = 0; i < categories.Count; i++)
                {
                    System.Tuple<List<Duckov.Utilities.Tag>, string, string> category = categories[i];
                    List<int> allIds = ModeESearchItemsMultiTag(category.Item1, emptyExclude);
                    if (allIds == null || allIds.Count == 0)
                    {
                        continue;
                    }

                    if (category.Item3 == "Medical")
                    {
                        allIds.RemoveAll(id => modeEMedicalShopExcludedIds.Contains(id));
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] PrewarmModeEMerchantCaches failed: " + e.Message);
            }
        }

        internal System.Collections.IEnumerator WarmModeEMerchantCachesAsync()
        {
            GetModeEMerchantPreset();
            yield return null;

            Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
            if (tagsData == null)
            {
                yield break;
            }

            Duckov.Utilities.Tag[] emptyExclude = new Duckov.Utilities.Tag[0];
            List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> categories = GetModeEMerchantCategories(tagsData);
            for (int i = 0; i < categories.Count; i++)
            {
                System.Tuple<List<Duckov.Utilities.Tag>, string, string> category = categories[i];
                List<int> allIds = ModeESearchItemsMultiTag(category.Item1, emptyExclude);
                if (allIds != null && category.Item3 == "Medical")
                {
                    allIds.RemoveAll(id => modeEMedicalShopExcludedIds.Contains(id));
                }

                yield return null;
            }
        }

        private string BuildModeEMerchantCategoryCacheKey(List<Duckov.Utilities.Tag> tags, Duckov.Utilities.Tag[] excludeTags)
        {
            string key = string.Empty;

            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    Duckov.Utilities.Tag tag = tags[i];
                    key += (tag != null ? tag.name : "<null>") + "|";
                }
            }

            key += "#";

            if (excludeTags != null)
            {
                for (int i = 0; i < excludeTags.Length; i++)
                {
                    Duckov.Utilities.Tag tag = excludeTags[i];
                    key += (tag != null ? tag.name : "<null>") + "|";
                }
            }

            return key;
        }

        private int[] ModeESearchItems(Duckov.Utilities.Tag tag, Duckov.Utilities.Tag[] excludeTags)
        {
            try
            {
                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { tag };
                filter.excludeTags = excludeTags;
                // 品质限制：1及以上（排除品质0的物品）
                filter.minQuality = 1;
                filter.maxQuality = 99;
                int[] results = ItemAssetsCollection.Search(filter);
                ModBehaviour.DevLog("[ModeE] ModeESearchItems Tag=" + tag.name + " 找到 " + (results != null ? results.Length : 0) + " 个物品");
                return results;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] ModeESearchItems 失败: " + e.Message);
                return null;
            }
        }

        // ====================================================================
        // 多 Tag 合并搜索物品ID（取并集，去重）
        // ====================================================================

        /// <summary>
        /// 根据多个 Tag 搜索物品ID，合并结果（并集去重）
        /// </summary>
        internal List<int> ModeESearchItemsMultiTag(List<Duckov.Utilities.Tag> tags, Duckov.Utilities.Tag[] excludeTags)
        {
            string cacheKey = BuildModeEMerchantCategoryCacheKey(tags, excludeTags);
            int[] cachedIds;
            if (modeEMerchantCategoryItemCache.TryGetValue(cacheKey, out cachedIds))
            {
                return cachedIds != null ? new List<int>(cachedIds) : new List<int>();
            }

            var idSet = new HashSet<int>();
            foreach (var tag in tags)
            {
                int[] ids = ModeESearchItems(tag, excludeTags);
                if (ids != null)
                {
                    foreach (int id in ids)
                    {
                        idSet.Add(id);
                    }
                }
            }

            List<int> result = new List<int>(idSet);
            modeEMerchantCategoryItemCache[cacheKey] = result.ToArray();
            return result;
        }

        internal int[] GetModeEMerchantCategoryPoolIds(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                return new int[0];
            }

            try
            {
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null)
                {
                    return new int[0];
                }

                List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> categories = GetModeEMerchantCategories(tagsData);
                Duckov.Utilities.Tag[] emptyExclude = new Duckov.Utilities.Tag[0];
                for (int i = 0; i < categories.Count; i++)
                {
                    System.Tuple<List<Duckov.Utilities.Tag>, string, string> category = categories[i];
                    if (!string.Equals(category.Item3, suffix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    List<int> allIds = ModeESearchItemsMultiTag(category.Item1, emptyExclude);
                    if (allIds == null)
                    {
                        return new int[0];
                    }

                    if (suffix == "Medical")
                    {
                        allIds.RemoveAll(id => modeEMedicalShopExcludedIds.Contains(id));
                    }

                    return allIds.ToArray();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] GetModeEMerchantCategoryPoolIds 失败: " + e.Message);
            }

            return new int[0];
        }

    }
}
