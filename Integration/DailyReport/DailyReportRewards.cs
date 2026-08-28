// ============================================================================
// DailyReportRewards.cs - 日报奖励发放（P0 步骤 5）
// ============================================================================
// 冻结契约：
//   - 奖品口径（owner 决策）：**全物品表按品质随机一件**，只过 LootBlacklistRegistry，
//     不限定标签。抽到顶级武器是签到长线应得的，不额外设保护。
//   - 发放走 CourierService.QuickDeliverItems -> PlayerStorage 快递缓冲：
//     玩家在战斗中跨天也安全（东西进官方 StorageDock 待领，不塞进战斗背包）。
//   - 抽取用 ModeHSeedStream（纯 PRNG，与 Mode H 赛季语义无关，只借算法），
//     domain 用自己的字符串，保证同一 (seed, day, slot) 重试得到同一件奖品。
//   - 候选表按品质缓存：ItemAssetsCollection.Search 是全表扫描，不能每次签到都跑。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>日报奖励发放。静态无状态（除候选表缓存）。</summary>
    internal static class DailyReportRewards
    {
        #region 常量与缓存

        /// <summary>确定性随机域：与其他系统的随机流互不干扰。</summary>
        private const string RewardDomain = "bossrush_daily_reward";

        /// <summary>按品质缓存的候选物品 id 表。Search 是全表扫描，不能每次签到都跑。</summary>
        private static readonly Dictionary<int, int[]> _candidateCache = new Dictionary<int, int[]>();

        private static readonly object _lock = new object();

        #endregion

        #region 里程碑发奖

        /// <summary>
        /// 发放一件指定品质的随机奖品。成功返回 true。
        /// 调用方在成功后才调 DailyReportService.MarkMilestoneClaimed（先发后标记）。
        /// </summary>
        internal static bool TryGrantMilestone(int quality, long seed, int dayIndex, int slot,
            out string failureReason)
        {
            failureReason = null;
            if (quality <= 0)
            {
                failureReason = "invalid_quality";
                return false;
            }

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

                Item item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null)
                {
                    failureReason = "instantiate_failed";
                    return false;
                }

                string bannerText = L10n.T(
                    "《鸭科夫日报》签到奖励已寄往快递站",
                    "Daily check-in reward sent to your delivery point");

                int sent = CourierService.QuickDeliverItems(new Item[] { item }, bannerText, true);
                if (sent <= 0)
                {
                    failureReason = "deliver_failed";
                    // 快递失败时销毁，避免留下无主的游离 Item 树
                    TryDestroy(item);
                    return false;
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
                Duckov.Economy.EconomyManager.Add(amount);
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
        /// 同一 (seed, dayIndex, slot) 永远得到同一件，因此发放失败后重试不会换奖品。
        /// </summary>
        private static int PickRewardTypeId(int quality, long seed, int dayIndex, int slot)
        {
            int[] candidates = GetCandidates(quality);
            if (candidates == null || candidates.Length <= 0) return -1;

            // sequence 把「天」和「格」揉进同一条流，避免同一天不同格抽到同一件
            int sequence = dayIndex * 100 + slot;
            ModeHSeedStream stream = ModeHSeedStream.Create(seed, RewardDomain, sequence);
            return candidates[stream.NextInt(candidates.Length)];
        }

        /// <summary>
        /// 取某品质的候选 id 表（已过黑名单）。结果缓存，Search 只跑一次。
        /// </summary>
        private static int[] GetCandidates(int quality)
        {
            lock (_lock)
            {
                int[] cached;
                if (_candidateCache.TryGetValue(quality, out cached)) return cached;
            }

            int[] built = BuildCandidates(quality);

            lock (_lock)
            {
                _candidateCache[quality] = built;
            }
            return built;
        }

        private static int[] BuildCandidates(int quality)
        {
            try
            {
                if (ItemAssetsCollection.Instance == null) return new int[0];

                LootBlacklistRegistry.EnsureInitialized();

                // 全表按品质随机：requireTags 留空，只卡品质区间。
                ItemFilter filter = new ItemFilter();
                filter.requireTags = null;
                filter.minQuality = quality;
                filter.maxQuality = quality;
                filter.caliber = string.Empty;

                int[] raw = ItemAssetsCollection.Search(filter);
                if (raw == null || raw.Length <= 0) return new int[0];

                List<int> safe = new List<int>(raw.Length);
                for (int i = 0; i < raw.Length; i++)
                {
                    int id = raw[i];
                    if (id <= 0) continue;
                    if (LootBlacklistRegistry.Contains(id)) continue;
                    safe.Add(id);
                }

                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "品质 " + quality
                    + " 候选池：" + safe.Count + " 件（原始 " + raw.Length + " 件）");
                return safe.ToArray();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                    + "[WARNING] 构建品质 " + quality + " 候选池失败: " + e.Message);
                return new int[0];
            }
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

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _candidateCache.Clear();
            }
        }

        #endregion
    }
}
