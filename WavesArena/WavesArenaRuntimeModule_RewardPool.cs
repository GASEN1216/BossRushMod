using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        /// <summary>
        /// 从共享高品质奖励池中随机取一个奖励 TypeID。
        /// 当前候选规则为 Quality>=5，且优先选择价格>=10000 的条目；调用方如需更高门槛，需要自行二次过滤。
        /// </summary>
        internal int GetRandomInfiniteHellHighQualityRewardTypeID()
        {
            // 皇冠（1254）权重降为0.1，与其他模式保持一致
            const int CROWN_TYPE_ID = 1254;
            const float CROWN_REROLL_CHANCE = 0.9f;

            if (InfiniteHellHighQualityItemPoolInitialized && InfiniteHellHighQualityItemPool.Count > 0)
            {
                return RollInfiniteHellHighQualityRewardTypeID(CROWN_TYPE_ID, CROWN_REROLL_CHANCE);
            }

            InfiniteHellHighQualityItemPoolInitialized = true;

            const int priceThreshold = 10000;
            List<int> preferred = InfiniteHellHighQualityPreferredScratch;
            List<int> fallbackHighQuality = InfiniteHellHighQualityFallbackScratch;
            preferred.Clear();
            fallbackHighQuality.Clear();

            try
            {
                if (!owner.BuildGeneralBossLootCandidateIdSet(InfiniteHellHighQualityCandidateIdScratch))
                {
                    return -1;
                }

                foreach (int candidateId in InfiniteHellHighQualityCandidateIdScratch)
                {
                    int v = 0;
                    int quality = -1;

                    if (!TryGetInfiniteHellRewardCandidateValueQuality(candidateId, out v, out quality))
                    {
                        continue;
                    }

                    if (quality >= 5)
                    {
                        if (v >= priceThreshold)
                        {
                            preferred.Add(candidateId);
                        }
                        else
                        {
                            fallbackHighQuality.Add(candidateId);
                        }
                    }
                }

                List<int> pool = null;

                if (preferred.Count > 0)
                {
                    pool = preferred;
                }
                else if (fallbackHighQuality.Count > 0)
                {
                    pool = fallbackHighQuality;
                }

                if (pool == null || pool.Count == 0)
                {
                    return -1;
                }

                InfiniteHellHighQualityItemPool.Clear();
                InfiniteHellHighQualityItemPool.AddRange(pool);
            }
            finally
            {
                ClearInfiniteHellHighQualityRewardScratch();
            }

            return RollInfiniteHellHighQualityRewardTypeID(CROWN_TYPE_ID, CROWN_REROLL_CHANCE);
        }

        private int RollInfiniteHellHighQualityRewardTypeID(int crownTypeId, float crownRerollChance)
        {
            if (InfiniteHellHighQualityItemPool.Count <= 0)
            {
                return -1;
            }

            int index = UnityEngine.Random.Range(0, InfiniteHellHighQualityItemPool.Count);
            int finalId = InfiniteHellHighQualityItemPool[index];

            // 如果抽到皇冠，90%概率重新抽取
            if (finalId == crownTypeId && UnityEngine.Random.value < crownRerollChance)
            {
                index = UnityEngine.Random.Range(0, InfiniteHellHighQualityItemPool.Count);
                finalId = InfiniteHellHighQualityItemPool[index];
            }

            return finalId;
        }

        private bool TryGetInfiniteHellRewardCandidateValueQuality(int candidateId, out int value, out int quality)
        {
            if (owner.TryGetCachedItemValue(candidateId, out value, out quality))
            {
                return true;
            }

            value = 0;
            quality = -1;
            Item temp = null;
            try
            {
                temp = ItemAssetsCollection.InstantiateSync(candidateId);
                if (temp == null)
                {
                    return false;
                }

                try { value = temp.Value; } catch { value = 0; }
                try { quality = temp.Quality; } catch { quality = -1; }
                return true;
            }
            catch
            {
                value = 0;
                quality = -1;
                return false;
            }
            finally
            {
                if (temp != null && temp.gameObject != null)
                {
                    UnityEngine.Object.Destroy(temp.gameObject);
                }
            }
        }

        private void ClearInfiniteHellHighQualityRewardScratch()
        {
            InfiniteHellHighQualityCandidateIdScratch.Clear();
            InfiniteHellHighQualityPreferredScratch.Clear();
            InfiniteHellHighQualityFallbackScratch.Clear();
        }
    }
}
