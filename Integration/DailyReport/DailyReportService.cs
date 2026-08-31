// ============================================================================
// DailyReportService.cs - 日报核心状态机：自算计时、跨天结算、签到（P0 步骤 3）
// ============================================================================
// 冻结契约（设计文档 docs/未来拓展/设计/P2-日报系统.md §3）：
//
// 1) 计时口径：本类**只**累计宿主 OnUpdate 的 `deltaTime * clockTimeScale`，
//    与官方 GameClock.Update 的 `StepTime(Time.deltaTime * clockTimeScale)` 逐帧同源
//    （鸭科夫源码/TeamSoda.Duckov.Core/GameClock.cs:191）。
//    **绝不订阅 GameClock.OnGameClockStep**，也不读 GameClock.Day：
//      - 睡觉（SleepView）与 Continue 跳早 7 点（LevelManager.OnNewBoot）走的是
//        StepTimeTil，不经 Update，因此天然被排除，不需要任何"跳变阈值"启发式；
//      - 读档时 GameClock.Load() 会额外 fire 一次 OnGameClockStep，我们没订阅，零影响；
//      - 暂停时 Time.timeScale=0 -> deltaTime=0，与官方钟同步停表。
//    这三条是选用"自算"而非"跟随官方 Day"的全部理由，改动前务必先读它们。
//
// 2) 余数写盘：每帧只改内存里的 _carrySeconds；只有跨天时才入队一次持久化，
//    平时的余数借官方 OnCollectSaveData 顺带写（零额外 IO）。
//
// 3) 签到状态机：
//    - 一天只能签一次（LastSignedDayIndex == DayIndex 即已签）；
//    - 跨天时若刚结束的那天没签 -> 断签：streak / periodSignedCount /
//      periodClaimedMask 全部清零，**periodIndex 保留**（owner 决策：清回本期第 0 格）；
//    - 本期签满 DaysPerPeriod 后不立刻翻期，而是在**下一次签到时**先翻期再签，
//      这样 UI 上"第 30 格已签"的状态能停留一整天（owner 决策：满 30 下一天变第 2 期）。
//
// 4) 里程碑发奖用位掩码 PeriodClaimedMask 做幂等，不用 token 列表；
//    发奖顺序是**先发后标记**（MarkMilestoneClaimed），宁可极端情况下重发也不吞奖励。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>签到结果分类。</summary>
    internal enum DailyReportSignInOutcome
    {
        /// <summary>签到成功。</summary>
        Success = 0,

        /// <summary>今天已经签过了。</summary>
        AlreadySigned = 1,

        /// <summary>存档处于写屏障/故障，签到不予受理（fail-closed）。</summary>
        PersistBlocked = 2,
    }

    /// <summary>一次签到的结果快照。</summary>
    internal struct DailyReportSignInResult
    {
        /// <summary>结果分类。</summary>
        internal DailyReportSignInOutcome Outcome;

        /// <summary>签到后本期已签格数（1-based 的当前格位）。</summary>
        internal int PeriodSlot;

        /// <summary>签到后的期号。</summary>
        internal int PeriodIndex;

        /// <summary>UI 展示用的累计天号（第 2 期第 1 格 = 31）。</summary>
        internal int DisplayDayNumber;

        /// <summary>本次是否踩中里程碑格。</summary>
        internal bool HitMilestone;

        /// <summary>里程碑对应的物品品质（未踩中为 0）。</summary>
        internal int MilestoneQuality;
    }

    /// <summary>日报核心状态机。静态单例，无 MonoBehaviour。</summary>
    internal static class DailyReportService
    {
        #region 运行时状态（不进存档的部分）

        private static readonly object _lock = new object();

        /// <summary>当天已累计的游戏内秒数。热路径只改它，不碰存档。</summary>
        private static double _carrySeconds;

        /// <summary>是否已从存档初始化过 _carrySeconds。</summary>
        private static bool _initialized;

        /// <summary>有新一期日报待提示（跨天发生在战斗中时挂起，回基地再提示）。</summary>
        private static bool _pendingIssueBanner;

        /// <summary>本进程内累计跨天次数（诊断用）。</summary>
        private static int _rolloverCount;

        #endregion

        #region 只读查询

        /// <summary>当前持久化数据（未加载时先加载）。</summary>
        internal static DailyReportData Data { get { return DailyReportPersistence.Current; } }

        /// <summary>当天已累计的游戏内秒数。</summary>
        internal static double CarrySeconds { get { lock (_lock) { return _carrySeconds; } } }

        /// <summary>当天进度（0~1），UI 显示"距下一期还有多久"。</summary>
        internal static float DayProgress01
        {
            get
            {
                double c = CarrySeconds;
                double p = c / DailyReportTuning.GameSecondsPerDay;
                if (p < 0d) return 0f;
                if (p > 1d) return 1f;
                return (float)p;
            }
        }

        /// <summary>是否有待提示的新一期。</summary>
        internal static bool HasPendingIssueBanner { get { return _pendingIssueBanner; } }

        /// <summary>本进程内跨天次数（诊断用）。</summary>
        internal static int RolloverCount { get { return _rolloverCount; } }

        /// <summary>今天是否已签到。</summary>
        internal static bool IsSignedToday
        {
            get
            {
                try
                {
                    DailyReportData data = Data;
                    return data != null && data.LastSignedDayIndex == data.DayIndex;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        #endregion

        #region 计时（宿主每帧调用；热路径必须零分配）

        /// <summary>
        /// 宿主 tick。realDeltaSeconds 传宿主 OnUpdate 的 deltaTime（**受 timeScale 影响的那个**，
        /// 与官方 GameClock.Update 同源）。
        /// </summary>
        internal static void Tick(float realDeltaSeconds)
        {
            if (realDeltaSeconds <= 0f) return;

            // 加载完成后的首帧 deltaTime 可能是个大尖峰，钳一下避免把一次卡顿算成几分钟游戏时间。
            if (realDeltaSeconds > DailyReportTuning.MaxRealDeltaPerFrame)
            {
                realDeltaSeconds = DailyReportTuning.MaxRealDeltaPerFrame;
            }

            if (!_initialized)
            {
                if (!TryInitializeFromSave()) return;
            }

            double advanced = realDeltaSeconds * (double)ReadClockTimeScale();

            bool needSettle;
            lock (_lock)
            {
                _carrySeconds += advanced;
                needSettle = _carrySeconds >= DailyReportTuning.GameSecondsPerDay;
            }

            // 绝大多数帧在这里返回：一次乘加 + 一次比较，零分配零反射。
            if (!needSettle) return;

            SettleRollover();
        }

        /// <summary>
        /// 读官方时钟倍率。读不到时用兜底常量，保证玩家改过倍率时我们跟随。
        /// </summary>
        private static float ReadClockTimeScale()
        {
            try
            {
                GameClock clock = GameClock.Instance;
                if (clock != null && clock.clockTimeScale > 0f) return clock.clockTimeScale;
            }
            catch (Exception)
            {
                // 官方类型缺失/未初始化：用兜底值，不打日志（这是每帧路径）
            }
            return DailyReportTuning.DefaultClockTimeScale;
        }

        /// <summary>从存档恢复当天余数。成功返回 true。</summary>
        private static bool TryInitializeFromSave()
        {
            try
            {
                DailyReportData data = DailyReportPersistence.LoadOrInit();
                if (data == null) return false;
                lock (_lock)
                {
                    _carrySeconds = data.CarrySeconds;
                    _initialized = true;
                }
                _pendingIssueBanner = data.PendingIssueBanner;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 计时器初始化失败: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 跨天结算

        /// <summary>
        /// 跨天结算。while 循环只是兜底：单帧增量至多
        /// MaxRealDeltaPerFrame * clockTimeScale（默认 2*60=120 游戏秒），
        /// 远小于一天的 86300，实际不可能一帧跨多天。
        /// </summary>
        private static void SettleRollover()
        {
            try
            {
                DailyReportData data = DailyReportPersistence.LoadOrInit();
                if (data == null) return;

                int settled = 0;
                while (true)
                {
                    lock (_lock)
                    {
                        if (_carrySeconds < DailyReportTuning.GameSecondsPerDay) break;
                        _carrySeconds -= DailyReportTuning.GameSecondsPerDay;
                    }
                    SettleOneDay(data);
                    settled++;

                    // 安全阀：正常永远是 1；异常情况下也不允许在一帧里空转。
                    if (settled >= 8) break;
                }

                if (settled <= 0) return;

                lock (_lock)
                {
                    data.CarrySeconds = _carrySeconds;
                }
                _rolloverCount += settled;
                _pendingIssueBanner = true;
                data.PendingIssueBanner = true;

                Persist(data);

                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "跨天结算完成，当前第 "
                    + data.DayIndex + " 天，本期已签 " + data.PeriodSignedCount + " 格");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[ERROR] 跨天结算异常: " + e.Message);
            }
        }

        /// <summary>结算一天：断签判定 -> 快照转存 -> 天号推进。</summary>
        private static void SettleOneDay(DailyReportData data)
        {
            // 幂等安全网：同一天不重复结算。正常路径下 DayIndex 每次都自增，不会命中。
            if (data.LastSettledDayIndex == data.DayIndex && data.DayIndex > 0)
            {
                data.DayIndex++;
                return;
            }

            // 0) 悬赏结算：必须在 Today 被重置之前算，进度源就是今日统计
            SettleBounty(data);

            // 1) 断签：刚结束的这天没签到 -> 清回本期第 0 格（期号保留）
            if (data.LastSignedDayIndex != data.DayIndex)
            {
                if (data.Streak != 0 || data.PeriodSignedCount != 0 || data.PeriodClaimedMask != 0)
                {
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "第 " + data.DayIndex
                        + " 天未签到，连续签到与本期进度清零（期号保留为第 "
                        + data.PeriodIndex + " 期）");
                }
                data.Streak = 0;
                data.PeriodSignedCount = 0;
                data.PeriodClaimedMask = 0;
            }

            // 2) 今日统计转存为昨日快照，作为新一期日报的内容源
            data.Yesterday = data.Today != null ? data.Today.Clone() : new DailyReportStats();
            data.HasYesterday = true;
            if (data.Today == null) data.Today = new DailyReportStats();
            data.Today.Reset();

            // 3) 天号推进
            data.LastSettledDayIndex = data.DayIndex;
            data.DayIndex++;
        }

        #endregion

        #region 悬赏

        /// <summary>
        /// 当日悬赏。它是 (bountySeed, dayIndex) 的纯函数，不占存档：
        /// 任何时候重算都得到同一条，因此重启/读档不会换题。
        /// </summary>
        internal static DailyReportBountyDef GetActiveBounty()
        {
            try
            {
                DailyReportData data = DailyReportPersistence.Current;
                if (data == null) return null;
                return DailyReportBounty.SelectForDay(EnsureBountySeed(data), data.DayIndex);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>当日悬赏的实时进度（UI 用）。</summary>
        internal static int GetActiveBountyProgress()
        {
            try
            {
                DailyReportData data = DailyReportPersistence.Current;
                if (data == null) return 0;
                DailyReportBountyDef def = DailyReportBounty.SelectForDay(
                    EnsureBountySeed(data), data.DayIndex);
                return DailyReportBounty.EvaluateProgress(def, data.Today);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 首次使用时派生并冻结悬赏种子。一旦写入就不再变，
        /// 否则每次启动都会换题，"重启不重抽"的保证就没了。
        /// </summary>
        internal static long EnsureBountySeed(DailyReportData data)
        {
            if (data == null) return 0L;
            if (data.BountySeed != 0L) return data.BountySeed;

            long derived = DateTime.UtcNow.Ticks ^ ((long)DailyReportTuning.CurrentSchemaVersion << 32);
            if (derived == 0L) derived = 1L;
            data.BountySeed = derived;
            Persist(data);
            ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "悬赏种子已派生并冻结");
            return derived;
        }

        /// <summary>
        /// 结算刚结束那一天的悬赏。必须在 Today 被重置之前调用。
        /// 达成则立刻发奖并记 claimed；发奖失败保留未领状态，由玩家下次开报纸时补发。
        /// </summary>
        private static void SettleBounty(DailyReportData data)
        {
            try
            {
                DailyReportBountyDef def = DailyReportBounty.SelectForDay(
                    EnsureBountySeed(data), data.DayIndex);
                if (def == null)
                {
                    data.BountyKindId = string.Empty;
                    data.BountyTarget = 0;
                    data.BountyProgress = 0;
                    data.BountyCompleted = false;
                    data.BountyRewardClaimed = false;
                    return;
                }

                DailyReportStats today = data.Today ?? new DailyReportStats();
                int progress = DailyReportBounty.EvaluateProgress(def, today);
                bool completed = progress >= def.Target && def.Target > 0;

                data.BountyDayIndex = data.DayIndex;
                data.BountyKindId = def.Id;
                data.BountyTarget = def.Target;
                data.BountyProgress = progress;
                data.BountyCompleted = completed;
                data.BountyRewardClaimed = false;

                if (!completed) return;

                // 写屏障下不得发钱：钱进的是官方存档，而 BountyRewardClaimed 落不了盘，
                // 下个会话会重新判定并再发一次（可跨会话反复领）。
                // 与 TrySignInToday 的同款前置检查保持一致。
                if (DailyReportPersistence.IsStoreFaulted || DailyReportPersistence.HasWriteBarrier)
                {
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[WARNING] 存档写屏障生效，悬赏奖金暂不发放（已领标记无法持久化）");
                    return;
                }

                string reason;
                if (DailyReportRewards.TryGrantBountyCash(def.CashReward, out reason))
                {
                    data.BountyRewardClaimed = true;
                }
                else
                {
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[WARNING] 悬赏奖金发放失败（" + reason + "），保留未领状态待补发");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 悬赏结算异常: " + e.Message);
            }
        }

        /// <summary>
        /// 补发上一期未成功发放的悬赏奖金（打开日报面板时调用）。
        /// 幂等：claimed 置位后不再重复发。
        /// </summary>
        internal static void TryRedeliverPendingBountyReward()
        {
            try
            {
                DailyReportData data = DailyReportPersistence.Current;
                if (data == null) return;
                if (!data.BountyCompleted || data.BountyRewardClaimed) return;
                if (string.IsNullOrEmpty(data.BountyKindId)) return;
                // 与 SettleBounty 同理：写屏障下补发同样会造成跨会话重复发钱
                if (DailyReportPersistence.IsStoreFaulted || DailyReportPersistence.HasWriteBarrier) return;

                DailyReportBountyDef settled = DailyReportBounty.SelectForDay(
                    EnsureBountySeed(data), data.BountyDayIndex);
                if (settled == null || !string.Equals(settled.Id, data.BountyKindId, StringComparison.Ordinal))
                {
                    return;
                }

                string reason;
                if (DailyReportRewards.TryGrantBountyCash(settled.CashReward, out reason))
                {
                    data.BountyRewardClaimed = true;
                    Persist(data);
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "悬赏奖金补发成功");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 悬赏补发异常: " + e.Message);
            }
        }

        #endregion

        #region 签到

        /// <summary>
        /// 今日签到。一天只受理一次；本期签满后在这里先翻期再签。
        /// 不在这里发奖：奖励由调用方按 result.MilestoneQuality 发放，
        /// 发放成功后再调 MarkMilestoneClaimed（先发后标记，宁可重发不吞奖）。
        /// </summary>
        internal static bool TrySignInToday(out DailyReportSignInResult result)
        {
            result = new DailyReportSignInResult();
            try
            {
                if (DailyReportPersistence.IsStoreFaulted || DailyReportPersistence.HasWriteBarrier)
                {
                    result.Outcome = DailyReportSignInOutcome.PersistBlocked;
                    return false;
                }

                DailyReportData data = DailyReportPersistence.LoadOrInit();
                if (data == null)
                {
                    result.Outcome = DailyReportSignInOutcome.PersistBlocked;
                    return false;
                }

                if (data.LastSignedDayIndex == data.DayIndex)
                {
                    result.Outcome = DailyReportSignInOutcome.AlreadySigned;
                    result.PeriodSlot = data.PeriodSignedCount;
                    result.PeriodIndex = data.PeriodIndex;
                    result.DisplayDayNumber = ToDisplayDayNumber(data.PeriodIndex, data.PeriodSignedCount);
                    return false;
                }

                // 本期已签满：下一次签到先翻期，再从新一期第 1 格开始。
                if (data.PeriodSignedCount >= DailyReportTuning.DaysPerPeriod)
                {
                    data.PeriodIndex++;
                    data.PeriodSignedCount = 0;
                    data.PeriodClaimedMask = 0;
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "签到墙翻页，进入第 "
                        + data.PeriodIndex + " 期");
                }

                data.PeriodSignedCount++;
                data.Streak++;
                data.TotalSignedDays++;
                data.LastSignedDayIndex = data.DayIndex;

                int slot = data.PeriodSignedCount;
                int quality = GetMilestoneQuality(data.PeriodIndex, slot);

                result.Outcome = DailyReportSignInOutcome.Success;
                result.PeriodSlot = slot;
                result.PeriodIndex = data.PeriodIndex;
                result.DisplayDayNumber = ToDisplayDayNumber(data.PeriodIndex, slot);
                result.HitMilestone = quality > 0 && !IsMilestoneClaimed(data, slot);
                result.MilestoneQuality = result.HitMilestone ? quality : 0;

                Persist(data);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[ERROR] 签到异常: " + e.Message);
                result.Outcome = DailyReportSignInOutcome.PersistBlocked;
                return false;
            }
        }

        /// <summary>
        /// 签到 + 发奖的一站式入口（UI 按钮调这个）。
        /// 顺序是**先发后标记**：发放成功才置掩码，宁可极端情况下重发也不吞奖励。
        /// </summary>
        internal static DailyReportSignInResult SignInAndClaim()
        {
            DailyReportSignInResult result;
            if (!TrySignInToday(out result)) return result;
            if (!result.HitMilestone || result.MilestoneQuality <= 0) return result;

            try
            {
                DailyReportData data = DailyReportPersistence.Current;
                if (data == null) return result;

                string reason;
                bool granted = DailyReportRewards.TryGrantMilestone(
                    result.MilestoneQuality, EnsureBountySeed(data),
                    ResolveMilestoneSignDayIndex(data, result.PeriodSlot),
                    result.PeriodSlot, out reason);

                if (granted)
                {
                    MarkMilestoneClaimed(result.PeriodSlot);
                }
                else
                {
                    // 没发出去就不置掩码：下次开面板时 TryRedeliverPendingMilestone 会补。
                    result.HitMilestone = false;
                    result.MilestoneQuality = 0;
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[WARNING] 里程碑奖励发放失败（" + reason + "），保留未领状态待补发");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 签到发奖异常: " + e.Message);
            }
            return result;
        }

        /// <summary>
        /// 补发本期已签到但发放失败的里程碑奖励（打开面板时调用）。
        /// 只补「格位已签到 && 掩码未置位」的里程碑，幂等。
        /// </summary>
        internal static void TryRedeliverPendingMilestones()
        {
            try
            {
                DailyReportData data = DailyReportPersistence.Current;
                if (data == null) return;
                if (data.PeriodSignedCount <= 0) return;

                for (int slot = 1; slot <= data.PeriodSignedCount; slot++)
                {
                    int quality = GetMilestoneQuality(data.PeriodIndex, slot);
                    if (quality <= 0) continue;
                    if (IsMilestoneClaimed(data, slot)) continue;

                    string reason;
                    // 用「签到当日」而不是当前 DayIndex：跨天补发必须抽回同一件，
                    // 见 ResolveMilestoneSignDayIndex 与 DailyReportRewards 头注释的确定性承诺。
                    if (DailyReportRewards.TryGrantMilestone(quality, EnsureBountySeed(data),
                        ResolveMilestoneSignDayIndex(data, slot), slot, out reason))
                    {
                        MarkMilestoneClaimed(slot);
                        ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                            + "里程碑奖励补发成功：第 " + slot + " 格");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 里程碑补发异常: " + e.Message);
            }
        }

        /// <summary>
        /// 推导本期第 slot 格是在第几天签到的。**不占存档**：
        /// 断签会把 PeriodSignedCount 清零（SettleOneDay 第 1 步），因此
        /// 1..PeriodSignedCount 这些格必然是**连续**签下来的，
        /// 第 slot 格的天号 = LastSignedDayIndex - (PeriodSignedCount - slot)。
        ///
        /// 奖品序列必须用这个值而不是当前 DayIndex，否则第 N 天发放失败、第 M 天补发
        /// 会抽到另一件，破坏 DailyReportRewards 头注释「同一 (seed, 签到当日, slot)
        /// 重试得到同一件奖品」的确定性承诺。当日签到路径下两者恒等
        /// （LastSignedDayIndex == DayIndex 且 PeriodSignedCount == slot），行为不变。
        /// </summary>
        private static int ResolveMilestoneSignDayIndex(DailyReportData data, int slot)
        {
            if (data == null) return 1;
            int day = data.LastSignedDayIndex - (data.PeriodSignedCount - slot);
            return day < 1 ? 1 : day;
        }

        /// <summary>里程碑奖励发放成功后调用，置位掩码防重发。</summary>
        internal static void MarkMilestoneClaimed(int slot)
        {
            try
            {
                DailyReportData data = DailyReportPersistence.LoadOrInit();
                if (data == null) return;
                if (slot < 1 || slot > DailyReportTuning.DaysPerPeriod) return;
                data.PeriodClaimedMask |= (1 << (slot - 1));
                Persist(data);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 里程碑标记失败: " + e.Message);
            }
        }

        /// <summary>本期该格里程碑是否已发放。</summary>
        internal static bool IsMilestoneClaimed(DailyReportData data, int slot)
        {
            if (data == null || slot < 1 || slot > DailyReportTuning.DaysPerPeriod) return false;
            return (data.PeriodClaimedMask & (1 << (slot - 1))) != 0;
        }

        /// <summary>
        /// 某期某格的里程碑品质。不是里程碑格返回 0。
        /// 第 1 期：7/15/24/30 -> 品质 5/6/7/8；第 2 期起：7/14/21/28 -> 品质 8。
        /// </summary>
        internal static int GetMilestoneQuality(int periodIndex, int slot)
        {
            if (slot < 1 || slot > DailyReportTuning.DaysPerPeriod) return 0;

            if (periodIndex <= 1)
            {
                int[] slots = DailyReportTuning.MilestoneSlotsFirstPeriod;
                int[] qualities = DailyReportTuning.MilestoneQualitiesFirstPeriod;
                for (int i = 0; i < slots.Length && i < qualities.Length; i++)
                {
                    if (slots[i] == slot) return qualities[i];
                }
                return 0;
            }

            int[] laterSlots = DailyReportTuning.MilestoneSlotsLaterPeriod;
            for (int i = 0; i < laterSlots.Length; i++)
            {
                if (laterSlots[i] == slot) return DailyReportTuning.MilestoneQualityLaterPeriod;
            }
            return 0;
        }

        /// <summary>把（期号, 格位）换算成 UI 展示的累计天号：第 2 期第 1 格 = 31。</summary>
        internal static int ToDisplayDayNumber(int periodIndex, int slot)
        {
            int p = periodIndex < 1 ? 1 : periodIndex;
            return (p - 1) * DailyReportTuning.DaysPerPeriod + slot;
        }

        #endregion

        #region 统计写入（供采集器调用；全部无分配）

        /// <summary>今日击杀 +1（isBoss 时同时计入 Boss 击杀）。</summary>
        internal static void ReportKill(bool isBoss)
        {
            DailyReportStats today = TryGetTodayStats();
            if (today == null) return;
            today.Kills++;
            if (isBoss) today.BossKills++;
        }

        /// <summary>玩家死亡 +1。</summary>
        internal static void ReportPlayerDeath()
        {
            DailyReportStats today = TryGetTodayStats();
            if (today == null) return;
            today.Deaths++;
        }

        /// <summary>出击 +1。</summary>
        internal static void ReportRaidStarted()
        {
            DailyReportStats today = TryGetTodayStats();
            if (today == null) return;
            today.Raids++;
        }

        /// <summary>成功撤离 +1。</summary>
        internal static void ReportExtraction()
        {
            DailyReportStats today = TryGetTodayStats();
            if (today == null) return;
            today.Extractions++;
        }

        /// <summary>累计造成的伤害，并顺带维护最大单次伤害。</summary>
        internal static void ReportDamageDealt(float amount)
        {
            if (amount <= 0f) return;
            DailyReportStats today = TryGetTodayStats();
            if (today == null) return;
            today.DamageDealt += amount;
            if (amount > today.MaxSingleHit) today.MaxSingleHit = amount;
        }

        /// <summary>累计承受的伤害。</summary>
        internal static void ReportDamageTaken(float amount)
        {
            if (amount <= 0f) return;
            DailyReportStats today = TryGetTodayStats();
            if (today == null) return;
            today.DamageTaken += amount;
        }

        /// <summary>金钱变动（delta 正数记收入，负数记支出）。</summary>
        internal static void ReportMoneyDelta(long delta)
        {
            if (delta == 0L) return;
            DailyReportStats today = TryGetTodayStats();
            if (today == null) return;
            if (delta > 0L) today.MoneyEarned += delta;
            else today.MoneySpent += -delta;
        }

        /// <summary>
        /// 取今日统计对象。**不触发存档加载以外的任何工作**；
        /// 存档不可用时返回 null，调用方静默丢弃这次统计（统计不是关键路径）。
        /// </summary>
        private static DailyReportStats TryGetTodayStats()
        {
            try
            {
                DailyReportData data = DailyReportPersistence.Current;
                return data != null ? data.Today : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region 生命周期钩子

        /// <summary>
        /// 官方存盘采集时把内存里的当天余数同步进 DTO。
        /// 未初始化时什么都不做：绝不用 0 覆盖存档里的有效余数。
        /// </summary>
        internal static void SyncCarrySecondsToPersistence()
        {
            if (!_initialized) return;
            try
            {
                DailyReportData data = DailyReportPersistence.Current;
                if (data == null) return;

                // Current 可能在这一步检测到槽位漂移并回调 NotifySlotChanged 复位运行时。
                // 复位后 _carrySeconds 已经不属于这份数据了，再写回去等于把新槽的
                // 当日余数清零，所以取到数据之后必须重新确认初始化标志。
                if (!_initialized) return;

                lock (_lock)
                {
                    data.CarrySeconds = _carrySeconds;
                }
                DailyReportPersistence.Store(data.Clone());
            }
            catch (Exception)
            {
                // no-throw：存档采集路径不得抛
            }
        }

        /// <summary>
        /// 切档 / 删档：丢弃运行时累计，下一帧从新槽重新初始化。
        /// 除官方 OnSetFile / OnSaveDeleted 之外，持久层在读取侧发现槽位漂移
        /// （开关关闭期间换档，没有回调可用）时也会调这里，保证数据换了计时也换。
        /// </summary>
        internal static void NotifySlotChanged()
        {
            lock (_lock)
            {
                _carrySeconds = 0d;
                _initialized = false;
            }
            _pendingIssueBanner = false;
        }

        /// <summary>提示已发出，清挂起标志。</summary>
        internal static void ConsumeIssueBanner()
        {
            _pendingIssueBanner = false;
            try
            {
                DailyReportData data = DailyReportPersistence.LoadOrInit();
                if (data == null || !data.PendingIssueBanner) return;
                data.PendingIssueBanner = false;
                Persist(data);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                    + "[WARNING] 清除日报未读提示失败: " + e.Message);
            }
        }

        /// <summary>把当前数据入队持久化并请求落盘。</summary>
        private static void Persist(DailyReportData data)
        {
            if (data == null) return;
            if (DailyReportPersistence.Store(data.Clone()))
            {
                DailyReportSaveCoordinator.RequestFlush();
            }
        }

        /// <summary>调试用：直接推进游戏内秒数（F3 快进，避免冒烟要等 24 现实分钟）。</summary>
        internal static void DebugAdvanceGameSeconds(double gameSeconds)
        {
            if (gameSeconds <= 0d) return;
            if (!_initialized && !TryInitializeFromSave()) return;
            lock (_lock)
            {
                _carrySeconds += gameSeconds;
            }
            SettleRollover();
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _carrySeconds = 0d;
                _initialized = false;
            }
            _pendingIssueBanner = false;
            _rolloverCount = 0;
        }

        #endregion
    }
}
