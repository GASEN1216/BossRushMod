// ============================================================================
// WishFountainRewardOdds.cs - 许愿抽奖的品质掷骰与游玩时长封顶
// ============================================================================
// 从 WishFountainRewardSelection.cs 原样提出（2026-11-21，那份文件超过新文件 1200 行预算）。
// 权重表与封顶阈值在 WishFountainService.cs；结构守卫 tests/WishRewardOddsGuard.py。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Saves;

namespace BossRush
{
    public static partial class WishFountainService
    {
        /// <summary>本档游玩时长允许的最高品质；读不出时长按最低一档封顶。</summary>
        private static int GetWishRewardQualityCapByPlayTime(out double playedHours)
        {
            playedHours = -1d;
            try
            {
                playedHours = GameClock.GetRealTimePlayedOfSaveSlot(SavesSystem.CurrentSlot).TotalHours;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 读取游玩时长失败，按最低一档封顶: " + e.Message);
                return WishRewardQualityCapBelowThreshold[0];
            }

            for (int i = 0; i < WishRewardPlayHoursThresholds.Length; i++)
            {
                if (playedHours < WishRewardPlayHoursThresholds[i])
                {
                    return WishRewardQualityCapBelowThreshold[i];
                }
            }

            return 8;
        }

        private static bool HasWishRewardBucket(Dictionary<int, List<int>> buckets, int quality)
        {
            List<int> bucket;
            return buckets != null && buckets.TryGetValue(quality, out bucket) && bucket != null && bucket.Count > 0;
        }

        private static int RollWishRewardQuality(WishRewardMatchResult match, Dictionary<int, List<int>> availableBuckets, int maxQuality)
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

            bool anyWeight = false;
            for (int i = 0; i < weights.Length; i++)
            {
                if (i + 1 > maxQuality || !HasWishRewardBucket(availableBuckets, i + 1))
                {
                    weights[i] = 0f;
                    continue;
                }

                weights[i] *= multipliers[i];
                if (weights[i] > 0f) anyWeight = true;
            }

            if (!anyWeight)
            {
                // 封顶以下一件都没有（奖池异常）：取有货的最低品质，宁可给差的也不越过时长封顶去给好的。
                for (int quality = 1; quality <= weights.Length; quality++)
                {
                    if (HasWishRewardBucket(availableBuckets, quality))
                    {
                        return quality;
                    }
                }
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
    }
}
