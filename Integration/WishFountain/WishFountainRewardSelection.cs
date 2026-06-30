// ============================================================================
// WishFountainRewardSelection.cs - 许愿奖励选择与动画
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
        private static WishRewardPoolSelection BuildWishRewardPoolSelectionFromMatchedItems(WishRewardMatchResult match)
        {
            WishRewardPoolSelection selection = new WishRewardPoolSelection();
            if (match == null || match.matchedItemTypeIds.Count <= 0)
            {
                return selection;
            }

            selection.poolMode = "matched_item_tags";

            HashSet<string> filterTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (int typeId in match.matchedItemTypeIds)
            {
                WishRewardCandidate matchedCandidate;
                if (!wishRewardCandidatesByTypeId.TryGetValue(typeId, out matchedCandidate) || matchedCandidate == null)
                {
                    continue;
                }

                foreach (string tagName in matchedCandidate.tagNames)
                {
                    if (string.IsNullOrEmpty(tagName))
                    {
                        continue;
                    }

                    filterTags.Add(tagName);
                    selection.matchedItemTags.Add(tagName);
                }
            }

            foreach (KeyValuePair<int, WishRewardCandidate> kvp in wishRewardCandidatesByTypeId)
            {
                WishRewardCandidate candidate = kvp.Value;
                if (candidate == null)
                {
                    continue;
                }

                // Intentionally OR across all cached item tags: approved design is full-tag OR filtering.
                foreach (string tagName in candidate.tagNames)
                {
                    if (filterTags.Contains(tagName))
                    {
                        selection.filteredTypeIds.Add(candidate.typeId);
                        break;
                    }
                }
            }

            return selection;
        }

        private static WishRewardPoolSelection BuildWishRewardPoolSelectionFromMatchedCategories(WishRewardMatchResult match)
        {
            WishRewardPoolSelection selection = new WishRewardPoolSelection();
            if (match == null || match.matchedCategoryIds.Count <= 0)
            {
                return selection;
            }

            selection.poolMode = "matched_categories";

            foreach (string categoryId in match.matchedCategoryIds)
            {
                HashSet<int> categoryCandidates;
                if (!wishRewardCategoryCandidateIds.TryGetValue(categoryId, out categoryCandidates) ||
                    categoryCandidates == null)
                {
                    continue;
                }

                foreach (int typeId in categoryCandidates)
                {
                    selection.filteredTypeIds.Add(typeId);
                }
            }

            return selection;
        }

        private static WishRewardPoolSelection BuildWishRewardPoolSelection(WishRewardMatchResult match)
        {
            WishRewardPoolSelection selection = BuildWishRewardPoolSelectionFromMatchedItems(match);
            if (selection.HasFilteredPool)
            {
                return selection;
            }

            selection = BuildWishRewardPoolSelectionFromMatchedCategories(match);
            if (selection.HasFilteredPool)
            {
                return selection;
            }

            if (match != null && match.HasAnyMatch)
            {
                selection.poolMode = "legacy_empty_filtered_pool";
                selection.fallbackReason = "empty_filtered_pool";
            }
            return selection;
        }

        private static bool ShouldUseLegacyWishRewardOdds(WishRewardPoolSelection selection)
        {
            return selection != null && string.Equals(selection.poolMode, "legacy_empty_filtered_pool", StringComparison.Ordinal);
        }

        private static Dictionary<int, List<int>> BuildWishRewardQualityBucketsForSelection(WishRewardPoolSelection selection)
        {
            Dictionary<int, List<int>> buckets = new Dictionary<int, List<int>>();
            if (selection == null || !selection.HasFilteredPool)
            {
                foreach (KeyValuePair<int, List<int>> kvp in wishRewardQualityBuckets)
                {
                    buckets[kvp.Key] = new List<int>(kvp.Value);
                }
                return buckets;
            }

            foreach (int typeId in selection.filteredTypeIds)
            {
                WishRewardCandidate candidate;
                if (!wishRewardCandidatesByTypeId.TryGetValue(typeId, out candidate) || candidate == null)
                {
                    continue;
                }

                List<int> bucket;
                if (!buckets.TryGetValue(candidate.quality, out bucket))
                {
                    bucket = new List<int>();
                    buckets[candidate.quality] = bucket;
                }

                bucket.Add(typeId);
            }

            return buckets;
        }

        private static string GetWishRewardDisplayNameFromItem(
            Dictionary<int, WishRewardItemDefinition> customItemsByTypeId,
            int typeId,
            Item item)
        {
            WishRewardItemDefinition customItem;
            if (customItemsByTypeId != null && customItemsByTypeId.TryGetValue(typeId, out customItem))
            {
                return L10n.T(customItem.displayNameCN, customItem.displayNameEN);
            }

            if (item != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(item.DisplayName))
                    {
                        return item.DisplayName;
                    }
                }
                catch
                {
                }

                try
                {
                    if (!string.IsNullOrEmpty(item.name))
                    {
                        return item.name;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string GetWishRewardDisplayNameFromItem(int typeId, Item item)
        {
            return GetWishRewardDisplayNameFromItem(wishRewardCustomItemsByTypeId, typeId, item);
        }

        private static string NormalizeWishRewardText(string text)
        {
            text = StandardizeText(text);
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(text.Length + 8);
            bool previousWasSpace = true;

            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                if (char.IsLetterOrDigit(c) || c > 127)
                {
                    sb.Append(c);
                    previousWasSpace = false;
                }
                else
                {
                    if (!previousWasSpace)
                    {
                        sb.Append(' ');
                        previousWasSpace = true;
                    }
                }
            }

            return sb.ToString().Trim();
        }

        private static bool IsWishRewardLatinAlias(string alias)
        {
            if (string.IsNullOrEmpty(alias))
            {
                return false;
            }

            for (int i = 0; i < alias.Length; i++)
            {
                char c = alias[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == ' ')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool ShouldUseWishRewardChineseAlias(string normalizedAlias)
        {
            if (string.IsNullOrEmpty(normalizedAlias))
            {
                return false;
            }

            if (normalizedAlias.Length < 2)
            {
                return false;
            }

            return true;
        }

        private static bool MatchesAnyWishRewardAlias(string normalizedText, string[] zhAliases, string[] enAliases)
        {
            if (string.IsNullOrEmpty(normalizedText))
            {
                return false;
            }

            if (zhAliases != null)
            {
                for (int i = 0; i < zhAliases.Length; i++)
                {
                    string normalizedAlias = NormalizeWishRewardText(zhAliases[i]);
                    if (!ShouldUseWishRewardChineseAlias(normalizedAlias))
                    {
                        continue;
                    }

                    if (normalizedText.Contains(normalizedAlias))
                    {
                        return true;
                    }
                }
            }

            if (enAliases != null)
            {
                string paddedText = " " + normalizedText + " ";
                for (int i = 0; i < enAliases.Length; i++)
                {
                    string alias = NormalizeWishRewardText(enAliases[i]);
                    if (string.IsNullOrEmpty(alias) || !IsWishRewardLatinAlias(alias))
                    {
                        continue;
                    }

                    if (paddedText.Contains(" " + alias + " "))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static WishRewardMatchResult MatchWishRewardKeywords(string standardizedWishText)
        {
            WishRewardMatchResult result = new WishRewardMatchResult();
            string normalizedText = NormalizeWishRewardText(standardizedWishText);
            if (string.IsNullOrEmpty(normalizedText))
            {
                return result;
            }

            foreach (KeyValuePair<string, WishRewardCategoryDefinition> kvp in wishRewardCategoriesById)
            {
                WishRewardCategoryDefinition category = kvp.Value;
                if (category == null)
                {
                    continue;
                }

                if (MatchesAnyWishRewardAlias(normalizedText, category.zhAliases, category.enAliases))
                {
                    result.matchedCategoryIds.Add(category.categoryId);
                }
            }

            foreach (KeyValuePair<int, WishRewardItemDefinition> kvp in wishRewardCustomItemsByTypeId)
            {
                WishRewardItemDefinition itemDefinition = kvp.Value;
                if (itemDefinition == null)
                {
                    continue;
                }

                if (MatchesAnyWishRewardAlias(normalizedText, itemDefinition.zhAliases, itemDefinition.enAliases) ||
                    MatchesAnyWishRewardAlias(normalizedText,
                        new string[] { itemDefinition.displayNameCN },
                        new string[] { itemDefinition.displayNameEN }))
                {
                    result.matchedItemTypeIds.Add(itemDefinition.typeId);
                    if (!string.IsNullOrEmpty(itemDefinition.categoryId))
                    {
                        result.matchedCategoryIds.Add(itemDefinition.categoryId);
                    }
                }
            }

            return result;
        }

        private static string TruncateWishRewardLogValue(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "(empty)";
            }

            if (maxLength <= 0 || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "...";
        }

        private static string FormatWishRewardCategoryMatchesForLog(WishRewardMatchResult match)
        {
            if (match == null || match.matchedCategoryIds.Count <= 0)
            {
                return "(none)";
            }

            List<string> categoryIds = new List<string>(match.matchedCategoryIds);
            categoryIds.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", categoryIds);
        }

        private static string FormatWishRewardItemMatchesForLog(WishRewardMatchResult match)
        {
            if (match == null || match.matchedItemTypeIds.Count <= 0)
            {
                return "(none)";
            }

            List<string> itemEntries = new List<string>();
            foreach (int typeId in match.matchedItemTypeIds)
            {
                string displayName = null;

                WishRewardItemDefinition itemDefinition;
                if (wishRewardCustomItemsByTypeId.TryGetValue(typeId, out itemDefinition) && itemDefinition != null)
                {
                    displayName = L10n.T(itemDefinition.displayNameCN, itemDefinition.displayNameEN);
                }

                if (string.IsNullOrEmpty(displayName))
                {
                    WishRewardCandidate candidate;
                    if (wishRewardCandidatesByTypeId.TryGetValue(typeId, out candidate) && candidate != null)
                    {
                        displayName = candidate.displayName;
                    }
                }

                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = "Item " + typeId;
                }

                itemEntries.Add(typeId + ":" + displayName);
            }

            itemEntries.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", itemEntries);
        }

        private static string FormatWishRewardMatchedItemTagsForLog(WishRewardPoolSelection selection)
        {
            if (selection == null || selection.matchedItemTags.Count <= 0)
            {
                return "(none)";
            }

            List<string> tags = new List<string>(selection.matchedItemTags);
            tags.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", tags);
        }

        private static void LogWishRewardRoll(
            string wishText,
            WishRewardMatchResult match,
            WishRewardPoolSelection selection,
            int rolledQuality,
            int selectedTypeId,
            string rewardDisplayName)
        {
            try
            {
                string normalizedWishText = TruncateWishRewardLogValue(NormalizeWishRewardText(wishText), 120);
                string matchedCategories = FormatWishRewardCategoryMatchesForLog(match);
                string matchedItems = FormatWishRewardItemMatchesForLog(match);
                string matchedItemTags = FormatWishRewardMatchedItemTagsForLog(selection);
                string poolMode = selection != null && !string.IsNullOrEmpty(selection.poolMode)
                    ? selection.poolMode
                    : "(unknown)";
                string filteredCandidateCount = selection != null
                    ? selection.filteredTypeIds.Count.ToString()
                    : "0";
                string fallbackReason = selection != null && !string.IsNullOrEmpty(selection.fallbackReason)
                    ? selection.fallbackReason
                    : "(none)";
                string selectedReward = string.IsNullOrEmpty(rewardDisplayName)
                    ? "(none)"
                    : TruncateWishRewardLogValue(rewardDisplayName, 80);

                ModBehaviour.DevLog(
                    "[WishFountain] reward roll normalizedWishText=\"" + normalizedWishText +
                    "\" matchedCategories=" + matchedCategories +
                    " matchedItems=" + matchedItems +
                    " matchedItemTags=" + matchedItemTags +
                    " poolMode=" + poolMode +
                    " filteredCandidateCount=" + filteredCandidateCount +
                    " fallbackReason=" + fallbackReason +
                    " rolledQuality=Q" + rolledQuality +
                    " selectedTypeId=" + selectedTypeId +
                    " selectedReward=" + selectedReward);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 记录许愿奖励抽取日志失败: " + e.Message);
            }
        }

        private static void LogWishRewardAnimationDiagnostics(
            WishRewardPoolSelection selection,
            int rewardTypeId,
            int winnerIndex,
            int legacyFillCount)
        {
            try
            {
                string poolMode = selection != null && !string.IsNullOrEmpty(selection.poolMode)
                    ? selection.poolMode
                    : "(unknown)";

                ModBehaviour.DevLog(
                    "[WishFountain] reward animation poolMode=" + poolMode +
                    " winnerTypeId=" + rewardTypeId +
                    " winnerIndex=" + winnerIndex +
                    " animationLegacyFillCount=" + legacyFillCount);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 记录许愿奖励动画日志失败: " + e.Message);
            }
        }

        private static float GetWishRewardMaxItemBiasForQuality(int quality)
        {
            if (quality <= 4)
            {
                return WISH_REWARD_ITEM_BIAS_CAP_Q1_TO_Q4;
            }

            if (quality <= 6)
            {
                return WISH_REWARD_ITEM_BIAS_CAP_Q5_TO_Q6;
            }

            if (quality == 7)
            {
                return WISH_REWARD_ITEM_BIAS_CAP_Q7;
            }

            return WISH_REWARD_ITEM_BIAS_CAP_Q8;
        }

        private static int RollWishRewardQuality(WishRewardMatchResult match, Dictionary<int, List<int>> availableBuckets)
        {
            float[] weights = new float[WishRewardBaseQualityWeights.Length];
            float[] multipliers = new float[WishRewardBaseQualityWeights.Length];

            for (int i = 0; i < WishRewardBaseQualityWeights.Length; i++)
            {
                weights[i] = WishRewardBaseQualityWeights[i];
                multipliers[i] = 1f;
            }

            foreach (string categoryId in match.matchedCategoryIds)
            {
                WishRewardCategoryDefinition category;
                if (!wishRewardCategoriesById.TryGetValue(categoryId, out category) || category == null)
                {
                    continue;
                }

                for (int i = 0; i < category.preferredQualities.Length; i++)
                {
                    int quality = category.preferredQualities[i];
                    if (quality < 1 || quality > weights.Length)
                    {
                        continue;
                    }

                    multipliers[quality - 1] = Mathf.Min(
                        WISH_REWARD_QUALITY_BIAS_CAP,
                        multipliers[quality - 1] * Mathf.Max(1f, category.qualityBiasMultiplier));
                }
            }

            foreach (int typeId in match.matchedItemTypeIds)
            {
                WishRewardCandidate candidate;
                if (!wishRewardCandidatesByTypeId.TryGetValue(typeId, out candidate) || candidate == null)
                {
                    continue;
                }

                int qualityIndex = candidate.quality - 1;
                if (qualityIndex < 0 || qualityIndex >= multipliers.Length)
                {
                    continue;
                }

                multipliers[qualityIndex] = Mathf.Min(
                    WISH_REWARD_QUALITY_BIAS_CAP,
                    multipliers[qualityIndex] * WISH_REWARD_EXACT_ITEM_QUALITY_BIAS);
            }

            for (int i = 0; i < weights.Length; i++)
            {
                List<int> bucket;
                if (availableBuckets != null && availableBuckets.TryGetValue(i + 1, out bucket) && bucket != null && bucket.Count > 0)
                {
                    continue;
                }

                weights[i] = 0f;
            }

            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] *= multipliers[i];
            }

            return RollWishRewardWeightedIndex(weights) + 1;
        }

        private static int RollWishRewardWeightedIndex(float[] weights)
        {
            if (weights == null || weights.Length == 0)
            {
                return -1;
            }

            float totalWeight = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] > 0f)
                {
                    totalWeight += weights[i];
                }
            }

            if (totalWeight <= 0f)
            {
                return 0;
            }

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cursor = 0f;

            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f)
                {
                    continue;
                }

                cursor += weights[i];
                if (roll <= cursor)
                {
                    return i;
                }
            }

            return weights.Length - 1;
        }

        private static int RollWishRewardItemInQuality(
            int rolledQuality,
            WishRewardMatchResult match,
            Dictionary<int, List<int>> activeBuckets,
            out string rewardDisplayName)
        {
            rewardDisplayName = null;

            List<int> bucket;
            if (activeBuckets == null || !activeBuckets.TryGetValue(rolledQuality, out bucket) || bucket == null || bucket.Count <= 0)
            {
                return -1;
            }

            float[] weights = new float[bucket.Count];
            for (int i = 0; i < bucket.Count; i++)
            {
                int typeId = bucket[i];
                WishRewardCandidate candidate;
                if (!wishRewardCandidatesByTypeId.TryGetValue(typeId, out candidate) || candidate == null)
                {
                    weights[i] = 0f;
                    continue;
                }

                float weight = 1f;
                float itemBiasCap = GetWishRewardMaxItemBiasForQuality(candidate.quality);

                foreach (string categoryId in match.matchedCategoryIds)
                {
                    if (!candidate.categoryIds.Contains(categoryId))
                    {
                        continue;
                    }

                    WishRewardCategoryDefinition category;
                    if (!wishRewardCategoriesById.TryGetValue(categoryId, out category) || category == null)
                    {
                        continue;
                    }

                    weight = Mathf.Min(
                        itemBiasCap,
                        weight * Mathf.Max(1f, category.itemBiasMultiplier));
                }

                if (match.matchedItemTypeIds.Contains(typeId))
                {
                    WishRewardItemDefinition itemDefinition;
                    if (wishRewardCustomItemsByTypeId.TryGetValue(typeId, out itemDefinition) && itemDefinition != null)
                    {
                        weight = Mathf.Min(
                            itemBiasCap,
                            weight * Mathf.Max(1f, itemDefinition.itemBiasMultiplier));
                    }
                }

                weights[i] = weight;
            }

            int selectedIndex = RollWishRewardWeightedIndex(weights);
            if (selectedIndex < 0 || selectedIndex >= bucket.Count)
            {
                return -1;
            }

            int selectedTypeId = bucket[selectedIndex];
            WishRewardCandidate selectedCandidate;
            if (wishRewardCandidatesByTypeId.TryGetValue(selectedTypeId, out selectedCandidate) && selectedCandidate != null)
            {
                rewardDisplayName = selectedCandidate.displayName;
            }

            return selectedTypeId;
        }

        private static int RollWishRewardTypeIdCore(
            string wishText,
            WishRewardMatchResult match,
            out string rewardDisplayName,
            out WishRewardPoolSelection outSelection)
        {
            rewardDisplayName = null;
            outSelection = null;

            EnsureWishRewardPoolInitialized();
            if (!wishRewardPoolInitialized)
            {
                return -1;
            }

            WishRewardPoolSelection selection = BuildWishRewardPoolSelection(match);
            outSelection = selection;
            Dictionary<int, List<int>> activeBuckets = BuildWishRewardQualityBucketsForSelection(selection);
            WishRewardMatchResult effectiveMatch = ShouldUseLegacyWishRewardOdds(selection)
                ? new WishRewardMatchResult()
                : match;
            int rolledQuality = RollWishRewardQuality(effectiveMatch, activeBuckets);
            int selectedTypeId = RollWishRewardItemInQuality(rolledQuality, effectiveMatch, activeBuckets, out rewardDisplayName);
            LogWishRewardRoll(wishText, match, selection, rolledQuality, selectedTypeId, rewardDisplayName);
            return selectedTypeId;
        }


        private static bool TryPickWishRewardAnimationCandidate(
            List<int> primaryPool,
            List<int> secondaryPool,
            List<int> tertiaryPool,
            List<int> existingSequence,
            HashSet<int> usedTypeIds,
            int winningTypeId,
            out int pickedTypeId)
        {
            pickedTypeId = -1;
            List<int>[] pools = new List<int>[] { primaryPool, secondaryPool, tertiaryPool };
            int lastTypeId = existingSequence.Count > 0 ? existingSequence[existingSequence.Count - 1] : -1;

            for (int poolIndex = 0; poolIndex < pools.Length; poolIndex++)
            {
                List<int> pool = pools[poolIndex];
                if (pool == null || pool.Count <= 0)
                {
                    continue;
                }

                List<int> eligibleCandidates = new List<int>();
                List<int> preferredCandidates = new List<int>();
                for (int i = 0; i < pool.Count; i++)
                {
                    int candidateTypeId = pool[i];
                    if (candidateTypeId <= 0 || candidateTypeId == winningTypeId)
                    {
                        continue;
                    }

                    if (usedTypeIds != null && usedTypeIds.Contains(candidateTypeId))
                    {
                        continue;
                    }

                    eligibleCandidates.Add(candidateTypeId);
                    if (candidateTypeId != lastTypeId)
                    {
                        preferredCandidates.Add(candidateTypeId);
                    }
                }

                if (preferredCandidates.Count > 0)
                {
                    pickedTypeId = preferredCandidates[UnityEngine.Random.Range(0, preferredCandidates.Count)];
                    return true;
                }

                if (eligibleCandidates.Count > 0)
                {
                    pickedTypeId = eligibleCandidates[UnityEngine.Random.Range(0, eligibleCandidates.Count)];
                    return true;
                }
            }

            return false;
        }

        private static void ResolveWishRewardAnimationPoolsForSlotIndex(
            int slotIndex,
            List<int> lowQuality,
            List<int> midQuality,
            List<int> highQuality,
            out List<int> primaryPool,
            out List<int> secondaryPool,
            out List<int> tertiaryPool)
        {
            if (slotIndex % 7 == 0)
            {
                primaryPool = highQuality;
                secondaryPool = midQuality;
                tertiaryPool = lowQuality;
            }
            else if (slotIndex % 3 == 0)
            {
                primaryPool = midQuality;
                secondaryPool = lowQuality;
                tertiaryPool = highQuality;
            }
            else
            {
                primaryPool = lowQuality;
                secondaryPool = midQuality;
                tertiaryPool = highQuality;
            }
        }

        private static void AddWishRewardAnimationCandidateToBuckets(
            WishRewardCandidate candidate,
            int rewardTypeId,
            List<int> lowQuality,
            List<int> midQuality,
            List<int> highQuality)
        {
            if (candidate == null || candidate.typeId == rewardTypeId)
            {
                return;
            }

            if (candidate.quality <= 3)
            {
                lowQuality.Add(candidate.typeId);
            }
            else if (candidate.quality <= 5)
            {
                midQuality.Add(candidate.typeId);
            }
            else
            {
                highQuality.Add(candidate.typeId);
            }
        }

        private static void BuildWishRewardAnimationBucketsFromSelection(
            WishRewardPoolSelection selection,
            int rewardTypeId,
            List<int> lowQuality,
            List<int> midQuality,
            List<int> highQuality)
        {
            if (selection == null || !selection.HasFilteredPool)
            {
                return;
            }

            foreach (int typeId in selection.filteredTypeIds)
            {
                WishRewardCandidate candidate;
                if (!wishRewardCandidatesByTypeId.TryGetValue(typeId, out candidate))
                {
                    continue;
                }

                AddWishRewardAnimationCandidateToBuckets(candidate, rewardTypeId, lowQuality, midQuality, highQuality);
            }
        }

        private static void BuildWishRewardAnimationBucketsFromLegacyPool(
            int rewardTypeId,
            List<int> lowQuality,
            List<int> midQuality,
            List<int> highQuality)
        {
            foreach (KeyValuePair<int, WishRewardCandidate> kvp in wishRewardCandidatesByTypeId)
            {
                AddWishRewardAnimationCandidateToBuckets(kvp.Value, rewardTypeId, lowQuality, midQuality, highQuality);
            }
        }

        private static bool TryAppendWishRewardAnimationCandidateForSlot(
            int slotIndex,
            bool useFilteredPool,
            List<int> filteredLowQuality,
            List<int> filteredMidQuality,
            List<int> filteredHighQuality,
            List<int> legacyLowQuality,
            List<int> legacyMidQuality,
            List<int> legacyHighQuality,
            List<int> sequence,
            HashSet<int> usedTypeIds,
            int rewardTypeId,
            ref int legacyFillCount)
        {
            List<int> filteredPrimaryPool;
            List<int> filteredSecondaryPool;
            List<int> filteredTertiaryPool;
            List<int> legacyPrimaryPool;
            List<int> legacySecondaryPool;
            List<int> legacyTertiaryPool;

            ResolveWishRewardAnimationPoolsForSlotIndex(
                slotIndex,
                filteredLowQuality,
                filteredMidQuality,
                filteredHighQuality,
                out filteredPrimaryPool,
                out filteredSecondaryPool,
                out filteredTertiaryPool);
            ResolveWishRewardAnimationPoolsForSlotIndex(
                slotIndex,
                legacyLowQuality,
                legacyMidQuality,
                legacyHighQuality,
                out legacyPrimaryPool,
                out legacySecondaryPool,
                out legacyTertiaryPool);

            int pickedTypeId = -1;
            bool pickedFromFiltered = useFilteredPool && TryPickWishRewardAnimationCandidate(
                filteredPrimaryPool,
                filteredSecondaryPool,
                filteredTertiaryPool,
                sequence,
                usedTypeIds,
                rewardTypeId,
                out pickedTypeId);

            if (!pickedFromFiltered)
            {
                if (!TryPickWishRewardAnimationCandidate(
                    legacyPrimaryPool,
                    legacySecondaryPool,
                    legacyTertiaryPool,
                    sequence,
                    usedTypeIds,
                    rewardTypeId,
                    out pickedTypeId))
                {
                    return false;
                }

                if (useFilteredPool)
                {
                    legacyFillCount++;
                }
            }

            sequence.Add(pickedTypeId);
            if (usedTypeIds != null)
            {
                usedTypeIds.Add(pickedTypeId);
            }

            return true;
        }

        private static List<int> BuildWishRewardAnimationSequence(int rewardTypeId, WishRewardPoolSelection selection,
            out int outWinnerIndex,
            out int legacyFillCount)
        {
            const int targetSequenceLength = 45;
            const int desiredWinnerIndex = 32;
            outWinnerIndex = desiredWinnerIndex;
            legacyFillCount = 0;

            EnsureWishRewardPoolInitialized();

            List<int> filteredLowQuality = new List<int>();
            List<int> filteredMidQuality = new List<int>();
            List<int> filteredHighQuality = new List<int>();
            List<int> legacyLowQuality = new List<int>();
            List<int> legacyMidQuality = new List<int>();
            List<int> legacyHighQuality = new List<int>();

            BuildWishRewardAnimationBucketsFromSelection(
                selection,
                rewardTypeId,
                filteredLowQuality,
                filteredMidQuality,
                filteredHighQuality);
            BuildWishRewardAnimationBucketsFromLegacyPool(
                rewardTypeId,
                legacyLowQuality,
                legacyMidQuality,
                legacyHighQuality);

            bool useFilteredPool = selection != null && selection.HasFilteredPool;

            HashSet<int> usedTypeIds = new HashSet<int>();
            usedTypeIds.Add(rewardTypeId);

            List<int> prefixSequence = new List<int>(desiredWinnerIndex);
            for (int i = 0; i < desiredWinnerIndex; i++)
            {
                if (!TryAppendWishRewardAnimationCandidateForSlot(
                    i,
                    useFilteredPool,
                    filteredLowQuality,
                    filteredMidQuality,
                    filteredHighQuality,
                    legacyLowQuality,
                    legacyMidQuality,
                    legacyHighQuality,
                    prefixSequence,
                    usedTypeIds,
                    rewardTypeId,
                    ref legacyFillCount))
                {
                    break;
                }
            }

            List<int> sequence = new List<int>(targetSequenceLength);
            sequence.AddRange(prefixSequence);
            outWinnerIndex = sequence.Count;
            sequence.Add(rewardTypeId);

            for (int i = outWinnerIndex + 1; i < targetSequenceLength; i++)
            {
                if (!TryAppendWishRewardAnimationCandidateForSlot(
                    i,
                    useFilteredPool,
                    filteredLowQuality,
                    filteredMidQuality,
                    filteredHighQuality,
                    legacyLowQuality,
                    legacyMidQuality,
                    legacyHighQuality,
                    sequence,
                    usedTypeIds,
                    rewardTypeId,
                    ref legacyFillCount))
                {
                    break;
                }
            }

            return sequence;
        }

        private static bool TryGiveWishRewardItem(int typeId)
        {
            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null)
                {
                    return false;
                }

                bool sent = false;
                try
                {
                    sent = ItemUtilities.SendToPlayerCharacterInventory(item, false);
                }
                catch
                {
                }

                if (!sent)
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null)
                    {
                        item.Drop(player.transform.position + Vector3.up * 0.3f, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                        sent = true;
                    }
                }

                if (!sent)
                {
                    try { item.DestroyTree(); } catch { }
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 发放许愿奖励失败: " + e.Message);
                try
                {
                    if (item != null)
                    {
                        item.DestroyTree();
                    }
                }
                catch
                {
                }
                return false;
            }
        }

        private static void OnWishRewardAnimationFinished(int rewardTypeId, string rewardDisplayName)
        {
            if (!TryGiveWishRewardItem(rewardTypeId))
            {
                ShowWishRewardFailureBubble();
                return;
            }

            SaveWishRewardNextAvailableUtc(DateTime.UtcNow.AddHours(WISH_REWARD_COOLDOWN_HOURS));
            ShowWishRewardResultBubble(rewardDisplayName);
        }

        internal static void TryStartWishRewardAnimationAfterSuccessfulSend(string wishText)
        {
            if (!IsWishRewardReady())
            {
                ShowWishRewardCooldownBubble();
                return;
            }

            WishRewardMatchResult match = MatchWishRewardKeywords(wishText);
            string rewardDisplayName;
            WishRewardPoolSelection selection;
            int rewardTypeId = RollWishRewardTypeIdCore(wishText, match, out rewardDisplayName, out selection);
            if (rewardTypeId <= 0 || string.IsNullOrEmpty(rewardDisplayName))
            {
                ShowWishRewardFailureBubble();
                return;
            }

            int animWinnerIndex;
            int legacyFillCount;
            List<int> animSequence = BuildWishRewardAnimationSequence(
                rewardTypeId,
                selection,
                out animWinnerIndex,
                out legacyFillCount);
            LogWishRewardAnimationDiagnostics(selection, rewardTypeId, animWinnerIndex, legacyFillCount);
            WishFountainRewardAnimationView.PlayRuntime(
                rewardTypeId,
                rewardDisplayName,
                animSequence,
                animWinnerIndex,
                OnWishRewardAnimationFinished);
        }

        // ============================================================================
        // 发送服务
        // ============================================================================

        /// <summary>检查是否在冷却中</summary>
    }
}
