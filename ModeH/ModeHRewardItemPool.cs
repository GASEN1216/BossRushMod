using System;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Mode H 胜利同品质奖励的物品池（设计提案 §17.5、§22.3）。
    ///
    /// 冻结契约：
    /// - “同品质”只能指 `gameQuality` **完全相等**，不套用赔率评分的封顶；
    /// - 因此**不能**用官方 `ItemAssetsCollection.Search`：它在结果为空时会
    ///   自行降级 `minQuality`/`maxQuality` 反复重搜（见官方
    ///   `ItemAssetsCollection.Search` 的 `DownGradeSearch` 循环），
    ///   会把 Q7 的奖励悄悄降成 Q3。这里改用 `GetAllTypeIds`，它只做一次
    ///   过滤、不降级，池为空时如实返回空数组由调用方 fail-closed；
    /// - 抽取用 `ModeHSeedStream`，同一 (runSeed, txId, 序号) 永远抽到同一件，
    ///   所以崩溃重放不会换奖励；
    /// - 本类只实例化游离物品，**不写库存**。入库由
    ///   `ModeHInventoryPersistenceBridge.TryAddAtEmpty` 负责。
    /// </summary>
    internal static class ModeHRewardItemPool
    {
        /// <summary>抽取域名，保证与其它随机流不共用序列。</summary>
        private const string RewardPickDomain = "modeh_reward_same_quality";

        /// <summary>
        /// 按 `quality` 完全相等抽一个 typeId。池为空或品质越界时返回 0 并给出原因。
        /// </summary>
        internal static int TryPickSameQualityTypeId(
            int quality, long runSeed, string txId, int slotIndex, out string failureReasonId)
        {
            failureReasonId = null;
            if (quality < ModeHConfig.MinGameQuality || quality > ModeHConfig.MaxGameQuality)
            {
                failureReasonId = "reward_quality_out_of_range";
                return 0;
            }

            int[] candidates;
            try
            {
                ItemFilter filter = new ItemFilter();
                filter.minQuality = quality;
                filter.maxQuality = quality;
                filter.caliber = string.Empty;
                candidates = ItemAssetsCollection.GetAllTypeIds(filter);
            }
            catch (Exception e)
            {
                failureReasonId = "reward_pool_query_failed:" + e.GetType().Name;
                return 0;
            }

            if (candidates == null || candidates.Length == 0)
            {
                failureReasonId = "reward_pool_empty_for_quality";
                return 0;
            }

            // txId 参与 domain，使不同 journal 的同序号奖励互不相同，
            // 但同一 journal 的重放仍然确定。
            ModeHSeedStream stream = ModeHSeedStream.Create(
                runSeed, RewardPickDomain + "|" + (txId != null ? txId : string.Empty), slotIndex);
            int typeId = candidates[stream.NextInt(candidates.Length)];
            if (typeId <= 0)
            {
                failureReasonId = "reward_pool_invalid_type_id";
                return 0;
            }
            return typeId;
        }

        /// <summary>实例化一件奖励物品。失败返回 null，不抛。</summary>
        internal static Item TryInstantiate(int typeId, out string failureReasonId)
        {
            failureReasonId = null;
            Item item;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(typeId);
            }
            catch (Exception e)
            {
                failureReasonId = "reward_instantiate_threw:" + e.GetType().Name;
                return null;
            }
            if (item == null)
            {
                failureReasonId = "reward_instantiate_returned_null";
                return null;
            }
            return item;
        }

        /// <summary>
        /// 入库失败时销毁尚未交给玩家的奖励实例，避免留下游离物品。
        /// </summary>
        internal static void DestroyUngranted(Item item)
        {
            if (item == null) return;
            try
            {
                if (item.gameObject != null) UnityEngine.Object.Destroy(item.gameObject);
            }
            catch (Exception)
            {
                // 清理失败不影响结算结论，保持静默
            }
        }
    }
}
