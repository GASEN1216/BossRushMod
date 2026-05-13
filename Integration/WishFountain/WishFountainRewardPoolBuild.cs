// ============================================================================
// WishFountainRewardPoolBuild.cs - 许愿奖励冷却与池构建
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ItemStatsSystem;
using Saves;

namespace BossRush
{
    public static partial class WishFountainService
    {
        private static void OnWishRewardSaveFileChanged()
        {
            wishRewardCooldownLoaded = false;
            cachedWishRewardNextAvailableTicks = 0L;
        }

        private static void EnsureWishRewardCooldownLoaded()
        {
            if (wishRewardCooldownLoaded)
            {
                return;
            }

            wishRewardCooldownLoaded = true;
            cachedWishRewardNextAvailableTicks = 0L;

            try
            {
                if (SavesSystem.KeyExisits(WISH_REWARD_NEXT_AVAILABLE_SAVE_KEY))
                {
                    cachedWishRewardNextAvailableTicks = SavesSystem.Load<long>(WISH_REWARD_NEXT_AVAILABLE_SAVE_KEY);
                }
            }
            catch (Exception e)
            {
                cachedWishRewardNextAvailableTicks = 0L;
                ModBehaviour.DevLog("[WishFountain] [WARNING] 读取许愿奖励冷却失败: " + e.Message);
            }
        }

        private static void SaveWishRewardNextAvailableUtc(DateTime nextAvailableUtc)
        {
            cachedWishRewardNextAvailableTicks = nextAvailableUtc.Ticks;
            wishRewardCooldownLoaded = true;

            try
            {
                SavesSystem.Save<long>(WISH_REWARD_NEXT_AVAILABLE_SAVE_KEY, cachedWishRewardNextAvailableTicks);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 保存许愿奖励冷却失败: " + e.Message);
            }
        }

        public static bool IsWishRewardReady()
        {
            EnsureWishRewardCooldownLoaded();
            return DateTime.UtcNow.Ticks >= cachedWishRewardNextAvailableTicks;
        }

        public static int GetWishRewardCooldownRemainingSeconds()
        {
            EnsureWishRewardCooldownLoaded();

            long remainingTicks = cachedWishRewardNextAvailableTicks - DateTime.UtcNow.Ticks;
            if (remainingTicks <= 0L)
            {
                return 0;
            }

            return Mathf.CeilToInt((float)TimeSpan.FromTicks(remainingTicks).TotalSeconds);
        }

        internal static void ClearWishRewardCooldownForDevMode()
        {
            cachedWishRewardNextAvailableTicks = 0L;
            wishRewardCooldownLoaded = true;

            try
            {
                SavesSystem.Save<long>(WISH_REWARD_NEXT_AVAILABLE_SAVE_KEY, 0L);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 清除许愿奖励冷却失败: " + e.Message);
            }
        }

        internal static void ClearSendCooldownForDevMode()
        {
            lastSendTime = -999f;
            IsSending = false;
        }

        private static string FormatWishRewardCooldownForBubble(int remainingSeconds)
        {
            TimeSpan remain = TimeSpan.FromSeconds(Mathf.Max(0, remainingSeconds));
            StringBuilder sb = new StringBuilder(32);

            if (remain.Hours > 0)
            {
                sb.Append(remain.Hours).Append("h");
            }

            if (remain.Minutes > 0)
            {
                sb.Append(remain.Minutes).Append("m");
            }

            sb.Append(remain.Seconds).Append("s");
            return sb.ToString();
        }

        private static void ShowWishRewardBubble(string text, float duration = 2.4f)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            try
            {
                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    text,
                    player.transform,
                    2.5f,
                    false,
                    false,
                    -1f,
                    duration);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 显示许愿奖励气泡失败: " + e.Message);
            }
        }

        private static void ShowWishRewardCooldownBubble()
        {
            int remaining = GetWishRewardCooldownRemainingSeconds();
            string formatted = FormatWishRewardCooldownForBubble(remaining);
            ShowWishRewardBubble(L10n.T(
                "许愿抽奖冷却：" + formatted,
                "Wish Gacha Cooldown: " + formatted));
        }

        private static void ShowWishRewardResultBubble(string rewardDisplayName)
        {
            if (string.IsNullOrEmpty(rewardDisplayName))
            {
                rewardDisplayName = L10n.T("未知奖励", "Unknown reward");
            }

            ShowWishRewardBubble(L10n.T(
                "我许到了一件：" + rewardDisplayName,
                "I wished for: " + rewardDisplayName), 2.8f);
        }

        private static void ShowWishRewardFailureBubble()
        {
            ShowWishRewardBubble(L10n.T(
                "星愿奖励发放失败，请稍后再试",
                "Wish reward delivery failed. Please try again later"), 2.8f);
        }

        internal static void ShowWishCloseReminderBubble()
        {
            if (IsWishRewardReady())
            {
                ShowWishRewardBubble(L10n.T(
                    "你这家伙快去许愿领奖励！！",
                    "Hey you, go make a wish and claim your reward!!"), 2.8f);
                return;
            }

            ShowWishRewardCooldownBubble();
        }

        private static bool HasSpecialWishRewardTag(Item item)
        {
            if (item == null || item.Tags == null)
            {
                return false;
            }

            Duckov.Utilities.Tag specialTag = null;
            try { specialTag = Duckov.Utilities.GameplayDataSettings.Tags.Special; } catch { }
            return specialTag != null && item.Tags.Contains(specialTag);
        }

        private static bool IsWishRewardExplicitlyAllowedCustomItem(
            Dictionary<int, WishRewardItemDefinition> customItemsByTypeId,
            int typeId)
        {
            WishRewardItemDefinition definition;
            return customItemsByTypeId != null
                && customItemsByTypeId.TryGetValue(typeId, out definition)
                && definition != null
                && definition.enabledInWishRewardPool;
        }

        private static void ResetWishRewardPoolCaches()
        {
            wishRewardCandidatesByTypeId.Clear();
            wishRewardQualityBuckets.Clear();
            wishRewardCategoriesById.Clear();
            wishRewardCategoryCandidateIds.Clear();
            wishRewardCustomItemsByTypeId.Clear();
            wishRewardPoolInitialized = false;
        }

        private static bool CanAttemptWishRewardPoolInitialization()
        {
            return Time.realtimeSinceStartup - lastWishRewardPoolInitAttemptRealtime >= WISH_REWARD_POOL_INIT_RETRY_SECONDS;
        }

        private static WishRewardPoolBuildContext CreateWishRewardPoolBuildContext()
        {
            WishRewardPoolBuildContext context = new WishRewardPoolBuildContext();

            for (int i = 0; i < WishRewardCategories.Length; i++)
            {
                WishRewardCategoryDefinition category = WishRewardCategories[i];
                if (category == null || string.IsNullOrEmpty(category.categoryId))
                {
                    continue;
                }

                context.categoriesById[category.categoryId] = category;
                context.categoryCandidateIds[category.categoryId] = new HashSet<int>();
            }

            for (int i = 0; i < WishRewardCustomItems.Length; i++)
            {
                WishRewardItemDefinition itemDefinition = WishRewardCustomItems[i];
                if (itemDefinition == null || itemDefinition.typeId <= 0)
                {
                    continue;
                }

                context.customItemsByTypeId[itemDefinition.typeId] = itemDefinition;
            }

            return context;
        }

        private static void CommitWishRewardPoolBuild(WishRewardPoolBuildContext context)
        {
            ResetWishRewardPoolCaches();
            if (context == null)
            {
                return;
            }

            foreach (KeyValuePair<int, WishRewardCandidate> kvp in context.candidatesByTypeId)
            {
                wishRewardCandidatesByTypeId[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<int, List<int>> kvp in context.qualityBuckets)
            {
                wishRewardQualityBuckets[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<string, WishRewardCategoryDefinition> kvp in context.categoriesById)
            {
                wishRewardCategoriesById[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<string, HashSet<int>> kvp in context.categoryCandidateIds)
            {
                wishRewardCategoryCandidateIds[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<int, WishRewardItemDefinition> kvp in context.customItemsByTypeId)
            {
                wishRewardCustomItemsByTypeId[kvp.Key] = kvp.Value;
            }

            wishRewardPoolInitialized = wishRewardCandidatesByTypeId.Count > 0;
        }

        private static void AddWishRewardCategoryCandidate(WishRewardPoolBuildContext context, string categoryId, int typeId)
        {
            if (context == null || string.IsNullOrEmpty(categoryId) || typeId <= 0)
            {
                return;
            }

            HashSet<int> set;
            if (!context.categoryCandidateIds.TryGetValue(categoryId, out set))
            {
                set = new HashSet<int>();
                context.categoryCandidateIds[categoryId] = set;
            }

            set.Add(typeId);

            WishRewardCandidate candidate;
            if (context.candidatesByTypeId.TryGetValue(typeId, out candidate))
            {
                candidate.categoryIds.Add(categoryId);
            }
        }

        private static void AddWishRewardQualityBucket(WishRewardPoolBuildContext context, int quality, int typeId)
        {
            if (context == null)
            {
                return;
            }

            List<int> bucket;
            if (!context.qualityBuckets.TryGetValue(quality, out bucket))
            {
                bucket = new List<int>();
                context.qualityBuckets[quality] = bucket;
            }

            if (!bucket.Contains(typeId))
            {
                bucket.Add(typeId);
            }
        }

        private static void EnsureWishRewardPoolInitialized()
        {
            if (wishRewardPoolInitialized)
            {
                return;
            }

            if (!CanAttemptWishRewardPoolInitialization())
            {
                return;
            }

            TryBuildWishRewardPoolSynchronously();
        }

        private static bool TryBuildWishRewardPoolSynchronously()
        {
            lastWishRewardPoolInitAttemptRealtime = Time.realtimeSinceStartup;

            try
            {
                WishRewardPoolBuildContext context = CreateWishRewardPoolBuildContext();
                BuildWishRewardPoolSynchronously(context);
                CommitWishRewardPoolBuild(context);
                return wishRewardPoolInitialized;
            }
            catch (Exception e)
            {
                ResetWishRewardPoolCaches();
                ModBehaviour.DevLog("[WishFountain] [WARNING] 同步构建许愿奖励池失败: " + e.Message);
                return false;
            }
        }

        private static IEnumerable<int> EnumerateWishRewardBasePoolCandidateIds()
        {
            Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
            if (tagsData == null || tagsData.AllTags == null || tagsData.AllTags.Count == 0)
            {
                yield break;
            }

            List<Duckov.Utilities.Tag> excludeTags = BuildWishRewardExcludeTags(tagsData);
            HashSet<int> yieldedIds = new HashSet<int>();

            for (int i = 0; i < tagsData.AllTags.Count; i++)
            {
                Duckov.Utilities.Tag requireTag = tagsData.AllTags[i];
                if (requireTag == null || excludeTags.Contains(requireTag))
                {
                    continue;
                }

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { requireTag };
                filter.excludeTags = excludeTags.ToArray();
                filter.minQuality = WISH_REWARD_MIN_QUALITY;
                filter.maxQuality = 8;

                int[] ids = ItemAssetsCollection.Search(filter);
                if (ids == null)
                {
                    continue;
                }

                for (int j = 0; j < ids.Length; j++)
                {
                    int id = ids[j];
                    if (id <= 0 || LootBlacklistRegistry.Contains(id))
                    {
                        continue;
                    }

                    if (yieldedIds.Add(id))
                    {
                        yield return id;
                    }
                }
            }
        }

        private static void BuildWishRewardPoolSynchronously(WishRewardPoolBuildContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            BuildWishRewardBasePool(context);
            BuildWishRewardCategoryMemberships(context);
            AddWishRewardCustomOverrides(context);

            if (context.candidatesByTypeId.Count <= 0)
            {
                throw new InvalidOperationException("Wish reward pool build produced no candidates.");
            }
        }

        private static IEnumerator BuildWishRewardPoolIncrementally(WishRewardPoolBuildContext context)
        {
            if (context == null)
            {
                yield break;
            }

            int processed = 0;
            foreach (int typeId in EnumerateWishRewardBasePoolCandidateIds())
            {
                TryRegisterWishRewardCandidate(context, typeId, false);
                processed++;

                if (processed % WISH_REWARD_POOL_BUILD_YIELD_INTERVAL == 0)
                {
                    yield return null;
                }
            }

            BuildWishRewardCategoryMemberships(context);
            yield return null;
            AddWishRewardCustomOverrides(context);

            if (context.candidatesByTypeId.Count <= 0)
            {
                throw new InvalidOperationException("Wish reward pool build produced no candidates.");
            }
        }

        private static bool TryAdvanceWishRewardPoolBuildEnumerator(
            IEnumerator enumerator,
            out object current,
            out Exception error)
        {
            current = null;
            error = null;

            if (enumerator == null)
            {
                return false;
            }

            try
            {
                if (!enumerator.MoveNext())
                {
                    return false;
                }

                current = enumerator.Current;
                return true;
            }
            catch (Exception e)
            {
                error = e;
                return false;
            }
        }

        internal static IEnumerator WarmupWishRewardPoolAfterDelay()
        {
            if (wishRewardPoolInitialized || wishRewardPoolWarmupInProgress)
            {
                yield break;
            }

            wishRewardPoolWarmupInProgress = true;
            try
            {
                yield return null;
                yield return null;

                WishRewardPoolBuildContext context = null;
                Exception warmupError = null;

                try
                {
                    context = CreateWishRewardPoolBuildContext();
                }
                catch (Exception e)
                {
                    warmupError = e;
                }

                if (warmupError == null && context != null)
                {
                    IEnumerator incrementalBuilder = BuildWishRewardPoolIncrementally(context);
                    while (warmupError == null)
                    {
                        object currentYield;
                        Exception stepError;
                        if (!TryAdvanceWishRewardPoolBuildEnumerator(incrementalBuilder, out currentYield, out stepError))
                        {
                            warmupError = stepError;
                            break;
                        }

                        yield return currentYield;
                    }
                }

                if (warmupError == null && context != null && !wishRewardPoolInitialized)
                {
                    try
                    {
                        CommitWishRewardPoolBuild(context);
                    }
                    catch (Exception e)
                    {
                        warmupError = e;
                    }
                }

                if (warmupError != null)
                {
                    if (!wishRewardPoolInitialized)
                    {
                        ResetWishRewardPoolCaches();
                    }

                    ModBehaviour.DevLog("[WishFountain] [WARNING] 预热构建许愿奖励池失败: " + warmupError.Message);
                }
            }
            finally
            {
                wishRewardPoolWarmupInProgress = false;
            }
        }

        private static void BuildWishRewardBasePool(WishRewardPoolBuildContext context)
        {
            foreach (int id in EnumerateWishRewardBasePoolCandidateIds())
            {
                TryRegisterWishRewardCandidate(context, id, false);
            }
        }

        private static void AddWishRewardCustomOverrides(WishRewardPoolBuildContext context)
        {
            if (context == null)
            {
                return;
            }

            for (int i = 0; i < WishRewardCustomItems.Length; i++)
            {
                WishRewardItemDefinition definition = WishRewardCustomItems[i];
                if (definition == null || definition.typeId <= 0 || !definition.enabledInWishRewardPool)
                {
                    continue;
                }

                TryRegisterWishRewardCandidate(context, definition.typeId, true);
                AddWishRewardCategoryCandidate(context, definition.categoryId, definition.typeId);
            }
        }

        private static void BuildWishRewardCategoryMemberships(WishRewardPoolBuildContext context)
        {
            if (context == null)
            {
                return;
            }

            Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
            if (tagsData != null)
            {
                Duckov.Utilities.Tag[] excludeTagsArray = BuildWishRewardExcludeTags(tagsData).ToArray();
                RegisterWishRewardTagCategory(context, tagsData, excludeTagsArray, "gun", new string[] { "Gun" });
                RegisterWishRewardTagCategory(context, tagsData, excludeTagsArray, "weapon", new string[] { "Gun", "Weapon", "MeleeWeapon" });
                RegisterWishRewardTagCategory(context, tagsData, excludeTagsArray, "helmet", new string[] { "Helmat", "Helmet" });
                RegisterWishRewardTagCategory(context, tagsData, excludeTagsArray, "armor", new string[] { "Armor" });
                RegisterWishRewardTagCategory(context, tagsData, excludeTagsArray, "travel", new string[] { "Backpack" });
                RegisterWishRewardTagCategory(context, tagsData, excludeTagsArray, "gift", new string[] { "Food", "Special" });
                RegisterWishRewardTagCategory(context, tagsData, excludeTagsArray, "healing", new string[] { "Food", "Medical", "Special" });
            }

            foreach (KeyValuePair<int, WishRewardCandidate> kvp in context.candidatesByTypeId)
            {
                int typeId = kvp.Key;
                WishRewardCandidate candidate = kvp.Value;
                if (candidate == null)
                {
                    continue;
                }

                WishRewardItemDefinition customItem;
                if (context.customItemsByTypeId.TryGetValue(typeId, out customItem))
                {
                    AddWishRewardCategoryCandidate(context, customItem.categoryId, typeId);
                }

                string normalizedName = NormalizeWishRewardText(candidate.displayName);
                if (string.IsNullOrEmpty(normalizedName))
                {
                    continue;
                }

                foreach (KeyValuePair<string, WishRewardCategoryDefinition> categoryKvp in context.categoriesById)
                {
                    WishRewardCategoryDefinition category = categoryKvp.Value;
                    if (category == null)
                    {
                        continue;
                    }

                    if (MatchesAnyWishRewardAlias(normalizedName, category.zhAliases, category.enAliases))
                    {
                        AddWishRewardCategoryCandidate(context, category.categoryId, typeId);
                    }
                }
            }
        }

        private static void RegisterWishRewardTagCategory(
            WishRewardPoolBuildContext context,
            Duckov.Utilities.GameplayDataSettings.TagsData tagsData,
            Duckov.Utilities.Tag[] excludeTagsArray,
            string categoryId,
            string[] memberNames)
        {
            if (context == null || tagsData == null || string.IsNullOrEmpty(categoryId) || memberNames == null)
            {
                return;
            }

            for (int i = 0; i < memberNames.Length; i++)
            {
                Duckov.Utilities.Tag requiredTag = TryGetWishRewardTagByMemberName(tagsData, memberNames[i]);
                if (requiredTag == null)
                {
                    continue;
                }

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { requiredTag };
                filter.excludeTags = excludeTagsArray;
                filter.minQuality = WISH_REWARD_MIN_QUALITY;
                filter.maxQuality = 8;

                int[] ids = ItemAssetsCollection.Search(filter);
                if (ids == null)
                {
                    continue;
                }

                for (int j = 0; j < ids.Length; j++)
                {
                    int typeId = ids[j];
                    if (!context.candidatesByTypeId.ContainsKey(typeId))
                    {
                        continue;
                    }

                    AddWishRewardCategoryCandidate(context, categoryId, typeId);
                }
            }
        }

        private static Duckov.Utilities.Tag TryGetWishRewardTagByMemberName(
            Duckov.Utilities.GameplayDataSettings.TagsData tagsData,
            string memberName)
        {
            if (tagsData == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            MemberInfo cached;
            if (wishRewardTagMemberCache.TryGetValue(memberName, out cached))
            {
                if (cached == null)
                {
                    return null;
                }

                try
                {
                    FieldInfo cachedField = cached as FieldInfo;
                    if (cachedField != null)
                    {
                        return cachedField.GetValue(tagsData) as Duckov.Utilities.Tag;
                    }

                    PropertyInfo cachedProperty = cached as PropertyInfo;
                    if (cachedProperty != null)
                    {
                        return cachedProperty.GetValue(tagsData, null) as Duckov.Utilities.Tag;
                    }
                }
                catch
                {
                }

                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            try
            {
                FieldInfo field = tagsData.GetType().GetField(memberName, flags);
                if (field != null && typeof(Duckov.Utilities.Tag).IsAssignableFrom(field.FieldType))
                {
                    wishRewardTagMemberCache[memberName] = field;
                    return field.GetValue(tagsData) as Duckov.Utilities.Tag;
                }
            }
            catch
            {
            }

            try
            {
                PropertyInfo property = tagsData.GetType().GetProperty(memberName, flags);
                if (property != null && typeof(Duckov.Utilities.Tag).IsAssignableFrom(property.PropertyType))
                {
                    wishRewardTagMemberCache[memberName] = property;
                    return property.GetValue(tagsData, null) as Duckov.Utilities.Tag;
                }
            }
            catch
            {
            }

            wishRewardTagMemberCache[memberName] = null;
            return null;
        }

        private static void AddWishRewardExcludeTag(List<Duckov.Utilities.Tag> excludeTags, Duckov.Utilities.Tag tag)
        {
            if (excludeTags == null || tag == null || excludeTags.Contains(tag))
            {
                return;
            }

            excludeTags.Add(tag);
        }

        private static Duckov.Utilities.Tag TryFindWishRewardQuestTag(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        {
            if (tagsData == null)
            {
                return null;
            }

            // 先走缓存反射路径
            Duckov.Utilities.Tag result = TryGetWishRewardTagByMemberName(tagsData, "Quest");
            if (result != null)
            {
                return result;
            }

            // 回退：遍历 AllTags 按名称查找
            try
            {
                if (tagsData.AllTags != null)
                {
                    for (int i = 0; i < tagsData.AllTags.Count; i++)
                    {
                        Duckov.Utilities.Tag tag = tagsData.AllTags[i];
                        if (tag != null && string.Equals(tag.name, "Quest", StringComparison.OrdinalIgnoreCase))
                        {
                            return tag;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static List<Duckov.Utilities.Tag> BuildWishRewardExcludeTags(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        {
            List<Duckov.Utilities.Tag> excludeTags = new List<Duckov.Utilities.Tag>();
            if (tagsData == null)
            {
                return excludeTags;
            }

            AddWishRewardExcludeTag(excludeTags, tagsData.Character);
            AddWishRewardExcludeTag(excludeTags, tagsData.DestroyOnLootBox);
            AddWishRewardExcludeTag(excludeTags, tagsData.DontDropOnDeadInSlot);
            AddWishRewardExcludeTag(excludeTags, tagsData.LockInDemoTag);
            AddWishRewardExcludeTag(excludeTags, TryFindWishRewardQuestTag(tagsData));
            return excludeTags;
        }

        private static bool TryRegisterWishRewardCandidate(
            WishRewardPoolBuildContext context,
            int typeId,
            bool allowBlacklistedOverride)
        {
            if (context == null || typeId <= 0)
            {
                return false;
            }

            if (!allowBlacklistedOverride && LootBlacklistRegistry.Contains(typeId))
            {
                return false;
            }

            if (context.candidatesByTypeId.ContainsKey(typeId))
            {
                return true;
            }

            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab == null)
                {
                    return false;
                }

                int quality = 0;
                try { quality = prefab.Quality; } catch { quality = 0; }
                if (quality < WISH_REWARD_MIN_QUALITY || quality > 8)
                {
                    return false;
                }

                if (HasSpecialWishRewardTag(prefab)
                    && !IsWishRewardExplicitlyAllowedCustomItem(context.customItemsByTypeId, typeId))
                {
                    return false;
                }

                string displayName = GetWishRewardDisplayNameFromItem(context.customItemsByTypeId, typeId, prefab);
                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = "Item " + typeId;
                }

                WishRewardCandidate candidate = new WishRewardCandidate
                {
                    typeId = typeId,
                    quality = quality,
                    displayName = displayName
                };

                CollectWishRewardTagNames(prefab, candidate.tagNames);
                context.candidatesByTypeId[typeId] = candidate;
                AddWishRewardQualityBucket(context, quality, typeId);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 注册许愿奖励候选失败 typeId=" + typeId + ": " + e.Message);
                return false;
            }
        }
    }
}
