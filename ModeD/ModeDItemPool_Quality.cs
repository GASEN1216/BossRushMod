using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    internal sealed partial class ModeDItemPool
    {
        private const float BossRushStyleCrownWeightScale = 0.1f;

        internal int GetRandomItemByQuality(List<int> pool, int minQuality, int maxQuality)
        {
            // P1-11: 先检查空池
            if (pool == null || pool.Count == 0)
            {
                return 0;
            }

            try
            {
                // P1-11 优化：使用有限随机抽样代替分配 filtered List
                // 最多尝试 30 次，如果都找不到符合品质的，就直接返回随机的一个
                const int MAX_QUALITY_ATTEMPTS = 30;

                for (int attempt = 0; attempt < MAX_QUALITY_ATTEMPTS; attempt++)
                {
                    int id = pool[UnityEngine.Random.Range(0, pool.Count)];
                    try
                    {
                        var meta = ItemAssetsCollection.GetMetaData(id);
                        if (meta.quality >= minQuality && meta.quality <= maxQuality)
                        {
                            return id;
                        }
                    }
                    catch {}
                }

                // 没有找到符合品质要求的，随机返回一个
                return pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            catch
            {
                return pool.Count > 0 ? pool[0] : 0;
            }
        }

        internal float ComputeModeDStyleEnemyLootBonusFactor(int qualityLevel)
        {
            return Mathf.InverseLerp(1f, 6f, Mathf.Clamp(qualityLevel, 1, 6));
        }

        internal int RollLegacyDesiredQualityForModeDStyleEnemyLoot(int qualityLevel, int minQuality, int maxQuality)
        {
            int clampedMin = Mathf.Clamp(minQuality, 1, 8);
            int clampedMax = Mathf.Clamp(maxQuality, clampedMin, 8);
            if (clampedMin >= clampedMax)
            {
                return clampedMin;
            }

            LegacyBossLootQualityDistribution distribution =
                LegacyBossLootProbabilityModel.BuildDistribution(ComputeModeDStyleEnemyLootBonusFactor(qualityLevel));

            double totalProbability = 0.0;
            for (int q = clampedMin; q <= clampedMax; q++)
            {
                totalProbability += distribution.GetProbabilityForQuality(q);
            }

            if (totalProbability <= 0.0)
            {
                return UnityEngine.Random.Range(clampedMin, clampedMax + 1);
            }

            double roll = UnityEngine.Random.value * totalProbability;
            for (int q = clampedMin; q <= clampedMax; q++)
            {
                roll -= distribution.GetProbabilityForQuality(q);
                if (roll <= 0.0)
                {
                    return q;
                }
            }

            return clampedMax;
        }

        internal int TryGetRandomItemByExactQualityBucket(Dictionary<int, List<int>> poolByQuality, int exactQuality)
        {
            if (poolByQuality == null)
            {
                return 0;
            }

            int clampedQuality = Mathf.Clamp(exactQuality, 1, 8);
            List<int> bucket;
            if (!poolByQuality.TryGetValue(clampedQuality, out bucket) || bucket == null || bucket.Count == 0)
            {
                return 0;
            }

            return bucket[UnityEngine.Random.Range(0, bucket.Count)];
        }

        internal int PickBossRushStyleBucketItemId(List<int> bucket)
        {
            if (bucket == null || bucket.Count == 0)
            {
                return 0;
            }

            float totalWeight = 0f;
            for (int i = 0; i < bucket.Count; i++)
            {
                int id = bucket[i];
                totalWeight += (id == 1254) ? BossRushStyleCrownWeightScale : 1f;
            }

            if (totalWeight <= 0f)
            {
                return bucket[UnityEngine.Random.Range(0, bucket.Count)];
            }

            float roll = UnityEngine.Random.value * totalWeight;
            for (int i = 0; i < bucket.Count; i++)
            {
                int id = bucket[i];
                roll -= (id == 1254) ? BossRushStyleCrownWeightScale : 1f;
                if (roll <= 0f)
                {
                    return id;
                }
            }

            return bucket[bucket.Count - 1];
        }

        internal int PickBossRushStyleQualityByLegacyDistribution(
            LegacyBossLootQualityDistribution distribution,
            Dictionary<int, List<int>> qualityBuckets,
            int minQuality,
            int maxQuality)
        {
            double totalWeight = 0.0;
            for (int quality = minQuality; quality <= maxQuality; quality++)
            {
                List<int> bucket;
                if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                totalWeight += distribution.GetProbabilityForQuality(quality);
            }

            if (totalWeight <= 0.0)
            {
                return 0;
            }

            double roll = UnityEngine.Random.value * totalWeight;
            for (int quality = minQuality; quality <= maxQuality; quality++)
            {
                List<int> bucket;
                if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                roll -= distribution.GetProbabilityForQuality(quality);
                if (roll <= 0.0)
                {
                    return quality;
                }
            }

            return 0;
        }

        internal int PickBossRushStyleQualityByNonLegacyWeights(
            float bonusFactor,
            Dictionary<int, List<int>> qualityBuckets,
            int minQuality,
            int maxQuality)
        {
            float highChance = Mathf.Clamp01(bonusFactor);
            float lowTotalWeight = 1f - highChance;
            float[] qualityWeights = new float[9];

            for (int quality = 1; quality <= 4; quality++)
            {
                qualityWeights[quality] = lowTotalWeight * 0.25f;
            }

            qualityWeights[5] = highChance * 0.4f;
            qualityWeights[6] = highChance * 0.3f;
            qualityWeights[7] = highChance * 0.2f;
            qualityWeights[8] = highChance * 0.1f;

            float totalWeight = 0f;
            for (int quality = minQuality; quality <= maxQuality; quality++)
            {
                List<int> bucket;
                if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                totalWeight += qualityWeights[quality];
            }

            if (totalWeight <= 0f)
            {
                return 0;
            }

            float roll = UnityEngine.Random.value * totalWeight;
            for (int quality = minQuality; quality <= maxQuality; quality++)
            {
                List<int> bucket;
                if (!qualityBuckets.TryGetValue(quality, out bucket) || bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                roll -= qualityWeights[quality];
                if (roll <= 0f)
                {
                    return quality;
                }
            }

            return 0;
        }
    }
}
