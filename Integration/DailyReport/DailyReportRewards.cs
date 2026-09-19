// ============================================================================
// DailyReportRewards.cs - 日报奖励发放（P0 步骤 5）
// ============================================================================
// 冻结契约：
//   - 奖品口径（owner 决策）：**全物品表按品质随机一件**，只过 LootBlacklistRegistry，
//     不限定标签。抽到顶级武器是签到长线应得的，不额外设保护。
//   - 发放走 CourierService.QuickDeliverItems -> PlayerStorage 快递缓冲：
//     玩家在战斗中跨天也安全（东西进官方 StorageDock 待领，不塞进战斗背包）。
//   - 抽取用 ModeHSeedStream（纯 PRNG，与 Mode H 赛季语义无关，只借算法），
//     domain 用自己的字符串，保证同一 (seed, 签到当日, slot) 重试得到同一件奖品。
//     注意 day 这一维是**签到当日**，不是补发当日：调用方用
//     DailyReportService.ResolveMilestoneSignDayIndex 从存档字段推导，
//     否则跨天补发会抽到另一件，承诺就破了。
//   - 候选表复用 BossRushQualityItemPool，与远征共用精确品质过滤与缓存。
// ============================================================================

using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>日报奖励发放。候选与缓存由共享品质池持有。</summary>
    internal static class DailyReportRewards
    {
        #region 常量与缓存

        /// <summary>确定性随机域：与其他系统的随机流互不干扰。</summary>
        private const string RewardDomain = "bossrush_daily_reward";

        #endregion

        #region 里程碑发奖

        /// <summary>
        /// 发放一件指定品质的随机奖品。成功返回 true。
        /// 调用方在成功后才调 DailyReportService.MarkMilestoneClaimed（先发后标记）。
        /// </summary>
        /// <param name="dayIndex">
        /// **签到当日**的天号（不是补发当日）。它与 slot 一起决定抽到哪一件，
        /// 传错会让跨天补发换奖品。
        /// </param>
        internal static bool TryGrantMilestone(int quality, long seed, int dayIndex, int slot,
            out string failureReason)
        {
            failureReason = null;
            if (quality <= 0)
            {
                failureReason = "invalid_quality";
                return false;
            }

            Item item = null;
            bool delivered = false;
            try
            {
                int typeId = PickRewardTypeId(quality, seed, dayIndex, slot);
                if (typeId <= 0)
                {
                    failureReason = "no_candidate";
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[WARNING] 品质 " + quality + " 没有可用奖品候选，本次里程碑跳过");
                    return false;
                }

                // 官方缺 prefab 时会返回同 TypeID 的空壳；不能把空壳当作已兑现的奖励。
                if (ItemAssetsCollection.Instance == null || ItemAssetsCollection.GetPrefab(typeId) == null)
                {
                    failureReason = "prefab_unavailable";
                    return false;
                }
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null)
                {
                    failureReason = "instantiate_failed";
                    return false;
                }

                string bannerText = L10n.T(
                    "《鸭科夫日报》签到奖励已寄往快递站",
                    "Daily check-in reward sent to your delivery point");

                int fallbackDelivered;
                int sent = CourierService.QuickDeliverItems(
                    new Item[] { item }, bannerText, true, out fallbackDelivered);
                if (sent <= 0 && fallbackDelivered <= 0)
                {
                    failureReason = "deliver_failed";
                    return false;
                }
                delivered = true;

                if (sent <= 0)
                {
                    // 快递站入库失败但回退已把物品直接交给玩家：视为已送达。
                    // 绝不能走上面的销毁分支——那会把玩家刚拿到手的奖品凭空抹掉，
                    // 且因返回 false 而在下次开面板时重抽补发（一件变两件或换一件）。
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "里程碑奖励经回退路径直接交付玩家：第 " + slot + " 格，typeId=" + typeId);
                }

                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "里程碑发奖成功：第 " + slot
                    + " 格，品质 " + quality + "，typeId=" + typeId);
                return true;
            }
            catch (Exception e)
            {
                failureReason = "exception:" + e.GetType().Name;
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[ERROR] 里程碑发奖异常: " + e.Message);
                return false;
            }
            finally
            {
                if (!delivered) TryDestroy(item);
            }
        }

        /// <summary>
        /// 发放悬赏奖励（现金）。悬赏给钱不给物，避免与签到奖品的稀有度体系打架。
        /// </summary>
        internal static bool TryGrantBountyCash(long amount, out string failureReason)
        {
            failureReason = null;
            if (amount <= 0L)
            {
                failureReason = "invalid_amount";
                return false;
            }

            try
            {
                bool added = false;

                // 悬赏奖金不是玩家当天赚的钱，不能计进被报道那天的「进账」。
                // 结算顺序是「先算悬赏再转存昨日快照」（进度判定必须用今日统计），
                // 所以在发放侧屏蔽这一笔，而不是调换结算顺序。
                DailyReportStatsCollector.SetMoneyDeltaSuppressed(true);
                try
                {
                    // 官方 Add 在 EconomyManager.Instance == null 时**返回 false 且不抛异常**
                    // （它是场景级 MonoBehaviour，没有 DontDestroyOnLoad）。吞掉这个返回值
                    // 会让调用方置 claimed 并落盘，补发被闸死 = 现金永久丢失，
                    // 与本系统「先发后标记、宁可重发不吞奖」的纪律相悖。
                    added = Duckov.Economy.EconomyManager.Add(amount);
                }
                finally
                {
                    DailyReportStatsCollector.SetMoneyDeltaSuppressed(false);
                }

                if (!added)
                {
                    failureReason = "economy_unavailable";
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[WARNING] 悬赏奖金未入账（EconomyManager 不可用），保留未领状态待补发：" + amount);
                    return false;
                }

                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "悬赏奖金发放：" + amount);
                return true;
            }
            catch (Exception e)
            {
                failureReason = "exception:" + e.GetType().Name;
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[ERROR] 悬赏奖金发放异常: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 候选抽取

        /// <summary>
        /// 按品质确定性抽一个 typeId。无候选返回 -1。
        /// 同一 (seed, dayIndex, slot) 永远得到同一件，因此发放失败后重试不会换奖品——
        /// 前提是调用方传的 dayIndex 是**签到当日**（跨天补发也必须传原来那天）。
        /// </summary>
        private static int PickRewardTypeId(int quality, long seed, int dayIndex, int slot)
        {
            int[] candidates = BossRushQualityItemPool.GetCandidates(quality);
            if (candidates == null || candidates.Length <= 0) return -1;

            // sequence 把「天」和「格」揉进同一条流，避免同一天不同格抽到同一件
            int sequence = dayIndex * 100 + slot;
            ModeHSeedStream stream = ModeHSeedStream.Create(seed, RewardDomain, sequence);
            return candidates[stream.NextInt(candidates.Length)];
        }

        private static void TryDestroy(Item item)
        {
            try
            {
                if (item != null) item.DestroyTree();
            }
            catch (Exception)
            {
                // 清理失败不影响主流程
            }
        }

        #endregion

    }
}
