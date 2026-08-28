using System;

namespace BossRush
{
    /// <summary>
    /// Mode H 虚拟筹码（设计提案 §17.5 的 2026-08-27 定价修订、§25.1）。
    ///
    /// 冻结公式（净赔率语义，`x1` 即一赔一、本金翻倍）：
    /// <code>
    /// grossVirtualPayout   = 胜利 ? reservedVirtualStake * (1 + lockedOdds) : 0
    /// netVirtualProfit     = grossVirtualPayout - reservedVirtualStake
    /// settledBalance       = clamp(balanceAfterReservation + grossVirtualPayout, 0, 30)
    /// rewardCandidateCount = 1 + min(2, floor(max(0, netVirtualProfit) / 2))
    /// </code>
    ///
    /// 其它冻结项：
    /// - 每季初始 6 点，上限 30；每场可下注 `0..min(2, virtualStakeCredits)`，0 点始终合法；
    /// - `LoadoutLocked` 原子写入 `virtualStakeBalanceBeforeReservation` 并更新
    ///   `reservedVirtualStake` / `virtualStakeCredits`，不接触真实物品；
    /// - 开战前技术中止按赛前快照恢复原余额并把 `reservedVirtualStake` 清零；
    /// - `MatchSettling` 把 `settledBalance` 写回 `virtualStakeCredits` 并清零 `reservedVirtualStake`；
    /// - 败场 `rewardKind=None`，不产生奖励候选。
    ///
    /// 真实押品路径的“件数”仍按原始整数倍率（`x3` = 3 件）结算，不套用本类的净赔率语义。
    /// </summary>
    internal static class ModeHVirtualStakeController
    {
        #region 纯公式

        /// <summary>本场允许的最大下注额：`min(2, virtualStakeCredits)`，且不为负。</summary>
        public static int GetMaxStake(int virtualStakeCredits)
        {
            if (virtualStakeCredits <= 0) return 0;
            return virtualStakeCredits < ModeHConfig.MaxVirtualStakePerMatch
                ? virtualStakeCredits
                : ModeHConfig.MaxVirtualStakePerMatch;
        }

        /// <summary>下注额是否合法（0 点始终合法）。</summary>
        public static bool IsStakeLegal(int stake, int virtualStakeCredits)
        {
            return stake >= 0 && stake <= GetMaxStake(virtualStakeCredits);
        }

        /// <summary>把下注额夹到合法区间。</summary>
        public static int ClampStake(int stake, int virtualStakeCredits)
        {
            if (stake < 0) return 0;
            int max = GetMaxStake(virtualStakeCredits);
            return stake > max ? max : stake;
        }

        /// <summary>`grossVirtualPayout = 胜利 ? stake * (1 + odds) : 0`。</summary>
        public static int ComputeGrossPayout(int reservedStake, int lockedOdds, bool won)
        {
            if (!won || reservedStake <= 0) return 0;
            if (lockedOdds < ModeHConfig.MinOdds) lockedOdds = ModeHConfig.MinOdds;
            if (lockedOdds > ModeHConfig.MaxOdds) lockedOdds = ModeHConfig.MaxOdds;
            return reservedStake * (1 + lockedOdds);
        }

        /// <summary>`netVirtualProfit = gross - stake`（败场为 `-stake`）。</summary>
        public static int ComputeNetProfit(int reservedStake, int grossPayout)
        {
            return grossPayout - reservedStake;
        }

        /// <summary>`settledBalance = clamp(balanceAfterReservation + gross, 0, 30)`。</summary>
        public static int ComputeSettledBalance(int balanceAfterReservation, int grossPayout)
        {
            int settled = balanceAfterReservation + grossPayout;
            if (settled < 0) return 0;
            return settled > ModeHConfig.MaxVirtualStakeCredits ? ModeHConfig.MaxVirtualStakeCredits : settled;
        }

        /// <summary>
        /// `rewardCandidateCount = 1 + min(2, floor(max(0, net) / 2))`。
        /// 败场由调用方按 `rewardKind=None` 处理，不调用本方法。
        /// </summary>
        public static int ComputeRewardCandidateCount(int netProfit)
        {
            if (netProfit < 0) netProfit = 0;
            int extra = netProfit / ModeHConfig.RewardCandidateNetDivisor;
            int cap = ModeHConfig.MaxRewardCandidateCount - 1;
            if (extra > cap) extra = cap;
            return 1 + extra;
        }

        #endregion

        #region 锁盘与结算

        /// <summary>
        /// 锁盘保留：原子写入赛前快照余额，并更新根字段
        /// `reservedVirtualStake` 与 `virtualStakeCredits`。
        /// </summary>
        public static bool TryReserve(
            ModeHSeasonDto season,
            ModeHPreMatchSnapshotDto snapshot,
            int stake,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (season == null || snapshot == null)
            {
                failureReasonId = "stake_season_missing";
                return false;
            }
            if (season.reservedVirtualStake != 0)
            {
                failureReasonId = "stake_already_reserved";
                return false;
            }
            if (!IsStakeLegal(stake, season.virtualStakeCredits))
            {
                failureReasonId = "stake_out_of_range";
                return false;
            }

            snapshot.virtualStakeBalanceBeforeReservation = season.virtualStakeCredits;
            snapshot.reservedVirtualStake = stake;
            season.reservedVirtualStake = stake;
            season.virtualStakeCredits = snapshot.virtualStakeBalanceBeforeReservation - stake;
            return true;
        }

        /// <summary>
        /// 开战前技术中止：按赛前快照恢复原余额并把 `reservedVirtualStake` 清零。
        /// 幂等——快照缺失或已无保留时直接返回 true。
        /// </summary>
        public static bool RestoreReservation(ModeHSeasonDto season, ModeHPreMatchSnapshotDto snapshot)
        {
            if (season == null) return false;
            if (snapshot != null)
            {
                season.virtualStakeCredits = ClampBalance(snapshot.virtualStakeBalanceBeforeReservation);
            }
            season.reservedVirtualStake = 0;
            return true;
        }

        /// <summary>
        /// `MatchSettling` 结算：写回 `settledBalance`、清零 `reservedVirtualStake`，
        /// 并把下注事实写入本场战报。返回本场奖励候选数（败场为 0）。
        /// </summary>
        public static int Settle(
            ModeHSeasonDto season,
            ModeHPreMatchSnapshotDto snapshot,
            ModeHMatchReportDto report,
            int lockedOdds,
            bool won)
        {
            if (season == null || report == null) return 0;

            int reserved = season.reservedVirtualStake;
            int balanceBefore = snapshot != null
                ? snapshot.virtualStakeBalanceBeforeReservation
                : season.virtualStakeCredits + reserved;
            int balanceAfterReservation = balanceBefore - reserved;
            if (balanceAfterReservation < 0) balanceAfterReservation = 0;

            int gross = ComputeGrossPayout(reserved, lockedOdds, won);
            int settled = ComputeSettledBalance(balanceAfterReservation, gross);

            season.virtualStakeCredits = settled;
            season.reservedVirtualStake = 0;

            report.lockedOdds = lockedOdds;
            report.virtualStakeBalanceBefore = balanceBefore;
            report.virtualStakeAmount = reserved;
            report.grossVirtualPayout = gross;
            report.virtualStakeBalanceAfter = settled;

            if (!won) return 0;
            return ComputeRewardCandidateCount(ComputeNetProfit(reserved, gross));
        }

        private static int ClampBalance(int balance)
        {
            if (balance < 0) return 0;
            return balance > ModeHConfig.MaxVirtualStakeCredits ? ModeHConfig.MaxVirtualStakeCredits : balance;
        }

        #endregion

        #region 战痕门与自检

        /// <summary>`x3+` 才开放战痕 offer（§17.4）。</summary>
        public static bool MeetsScarOfferGate(int lockedOdds)
        {
            return lockedOdds >= ModeHConfig.ScarOfferMinOdds;
        }

        /// <summary>
        /// 冻结取值表自检（§17.5 九行）。任一行不符即内容/代码不一致，
        /// 由入口 fail-closed；`ModeHVirtualStakeGuard.py` 逐行断言同一张表。
        /// </summary>
        public static bool VerifyFrozenTable(out string failureReasonId)
        {
            failureReasonId = null;

            // { stake, odds, expectedGross, expectedNet, expectedCandidates }
            int[][] winRows = new int[][]
            {
                new int[] { 0, 1, 0, 0, 1 },
                new int[] { 1, 1, 2, 1, 1 },
                new int[] { 1, 3, 4, 3, 2 },
                new int[] { 1, 5, 6, 5, 3 },
                new int[] { 2, 1, 4, 2, 2 },
                new int[] { 2, 2, 6, 4, 3 },
                new int[] { 2, 3, 8, 6, 3 },
                new int[] { 2, 5, 12, 10, 3 }
            };

            for (int i = 0; i < winRows.Length; i++)
            {
                int stake = winRows[i][0];
                int odds = winRows[i][1];
                int gross = ComputeGrossPayout(stake, odds, true);
                if (gross != winRows[i][2])
                {
                    failureReasonId = "stake_table_gross_mismatch:" + stake + "x" + odds;
                    return false;
                }
                int net = ComputeNetProfit(stake, gross);
                if (net != winRows[i][3])
                {
                    failureReasonId = "stake_table_net_mismatch:" + stake + "x" + odds;
                    return false;
                }
                if (ComputeRewardCandidateCount(net) != winRows[i][4])
                {
                    failureReasonId = "stake_table_candidates_mismatch:" + stake + "x" + odds;
                    return false;
                }
            }

            // 第九行：任意下注、失败 -> gross 0、net -stake、无奖励候选
            for (int stake = 0; stake <= ModeHConfig.MaxVirtualStakePerMatch; stake++)
            {
                for (int odds = ModeHConfig.MinOdds; odds <= ModeHConfig.MaxOdds; odds++)
                {
                    int gross = ComputeGrossPayout(stake, odds, false);
                    if (gross != 0)
                    {
                        failureReasonId = "stake_table_loss_gross_nonzero";
                        return false;
                    }
                    if (ComputeNetProfit(stake, gross) != -stake)
                    {
                        failureReasonId = "stake_table_loss_net_mismatch";
                        return false;
                    }
                }
            }

            if (ModeHConfig.InitialVirtualStakeCredits != 6 || ModeHConfig.MaxVirtualStakeCredits != 30)
            {
                failureReasonId = "stake_bounds_mismatch";
                return false;
            }
            return true;
        }

        #endregion
    }
}
