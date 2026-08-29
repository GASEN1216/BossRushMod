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

                int fallbackDelivered;
                int sent = CourierService.QuickDeliverItems(
                    new Item[] { item }, bannerText, true, out fallbackDelivered);
                if (sent <= 0 && fallbackDelivered <= 0)
                {
                    failureReason = "deliver_failed";
                    // 快递与回退两路都没送出去，此时物品确为无主游离树，销毁避免泄漏
                    TryDestroy(item);
                    return false;
                }

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
            int[] candidates = GetCandidates(quality);
            if (candidates == null || candidates.Length <= 0) return -1;

            // sequence 把「天」和「格」揉进同一条流，避免同一天不同格抽到同一件
            int sequence = dayIndex * 100 + slot;
            ModeHSeedStream stream = ModeHSeedStream.Create(seed, RewardDomain, sequence);
            return candidates[stream.NextInt(candidates.Length)];
        }

        /// <summary>
        /// 取某品质的候选 id 表（已过黑名单）。**非空结果**才缓存，Search 只跑一次。
        /// </summary>
        private static int[] GetCandidates(int quality)
        {
            lock (_lock)
            {
                int[] cached;
                if (_candidateCache.TryGetValue(quality, out cached)) return cached;
            }

            int[] built = BuildCandidates(quality);

            // 空结果不入缓存。官方 Search 自带降品质兜底（result.Length < 1 就一路降到
            // quality < 0，见 鸭科夫源码/ItemStatsSystem/ItemAssetsCollection.cs:270-277），
            // 合法的空池几乎不可能出现，所以空数组必然是 ItemAssetsCollection 未就绪或
            // Search 瞬时异常的故障残影。缓存它会把一次瞬时失败放大成该品质整会话
            // no_candidate，每次开面板补发都失败刷 WARNING。
            // 非空结果照旧缓存：全表扫描不能每次签到都跑。
            if (built == null || built.Length <= 0) return built;

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
