// ============================================================================
// DailyReportModels.cs - 日报运行时数据模型（P0 步骤 1）
// ============================================================================
// 设计要点（设计文档 §3.3）：
//   - **DTO 保持扁平**：里程碑领取用位掩码而不是 token 列表，昨日快照用前缀字段而
//     不是嵌套对象。这样编解码只需仓库既有的 Utilities/SimpleJsonHelper.cs
//     （扁平对象工具），不必再引入第三套 JSON 解析器
//     （ModeH 有 ModeHJsonValue、遗种巢有 PetNestJson，都与各自模块语义绑定）。
//   - 普通 C# 类，不加 [Serializable]：本模块走 Save<string> 整存 JSON，
//     不用 ES3 typed save，因此不受 assembly-qualified 类型名变更影响。
//   - 无字段初始化器；默认值统一由 DailyReportCodec.CreateDefault() 给，
//     避免"构造出来的默认"和"读档读出来的默认"两套语义漂移。
// ============================================================================

namespace BossRush
{
    /// <summary>一天的战绩统计。今日累计与昨日快照共用这个形状。</summary>
    internal sealed class DailyReportStats
    {
        /// <summary>击杀敌人总数（玩家造成的击杀）。</summary>
        internal int Kills;

        /// <summary>其中 Boss 击杀数。</summary>
        internal int BossKills;

        /// <summary>玩家死亡次数。</summary>
        internal int Deaths;

        /// <summary>出击次数（进入 raid）。</summary>
        internal int Raids;

        /// <summary>成功撤离次数。</summary>
        internal int Extractions;

        /// <summary>收入（金）。</summary>
        internal long MoneyEarned;

        /// <summary>支出（金，正数）。</summary>
        internal long MoneySpent;

        /// <summary>造成的总伤害（P1 起采集）。</summary>
        internal float DamageDealt;

        /// <summary>承受的总伤害（P1 起采集）。</summary>
        internal float DamageTaken;

        /// <summary>最大单次伤害（P1 起采集）。</summary>
        internal float MaxSingleHit;

        /// <summary>是否有任何有效数据（全零时日报走「今日无事发生」文案）。</summary>
        internal bool HasAnyActivity
        {
            get
            {
                return Kills > 0 || Deaths > 0 || Raids > 0 || Extractions > 0
                    || MoneyEarned > 0L || MoneySpent > 0L;
            }
        }

        /// <summary>清零（跨天时重置今日累计）。</summary>
        internal void Reset()
        {
            Kills = 0;
            BossKills = 0;
            Deaths = 0;
            Raids = 0;
            Extractions = 0;
            MoneyEarned = 0L;
            MoneySpent = 0L;
            DamageDealt = 0f;
            DamageTaken = 0f;
            MaxSingleHit = 0f;
        }

        /// <summary>深拷贝（跨天时把今日转存为昨日快照）。</summary>
        internal DailyReportStats Clone()
        {
            DailyReportStats copy = new DailyReportStats();
            copy.Kills = Kills;
            copy.BossKills = BossKills;
            copy.Deaths = Deaths;
            copy.Raids = Raids;
            copy.Extractions = Extractions;
            copy.MoneyEarned = MoneyEarned;
            copy.MoneySpent = MoneySpent;
            copy.DamageDealt = DamageDealt;
            copy.DamageTaken = DamageTaken;
            copy.MaxSingleHit = MaxSingleHit;
            return copy;
        }
    }

    /// <summary>
    /// 日报持久化状态。一个存档槽一份。
    /// </summary>
    internal sealed class DailyReportData
    {
        #region 计时

        /// <summary>当前是第几天（新档 = 1，跨天后自增）。</summary>
        internal int DayIndex;

        /// <summary>
        /// 当天已累计的游戏内秒数（0 ~ GameSecondsPerDay）。
        /// 跨天时入队一次；平时借官方 OnCollectSaveData 顺带写。
        /// </summary>
        internal double CarrySeconds;

        /// <summary>已完成结算的最大天号，用于 rollover 幂等（防重入重复结算）。</summary>
        internal int LastSettledDayIndex;

        #endregion

        #region 签到

        /// <summary>当前期号（1-based）。满 DaysPerPeriod 翻期，断签不回退期号。</summary>
        internal int PeriodIndex;

        /// <summary>本期已签到格数（0 ~ DaysPerPeriod）。断签清 0。</summary>
        internal int PeriodSignedCount;

        /// <summary>
        /// 本期里程碑领取位掩码：bit(n-1) 置位表示本期第 n 格的奖励已发放。
        /// 用掩码而不是 token 列表：天然幂等、定长、翻期/断签一次清零即可。
        /// </summary>
        internal int PeriodClaimedMask;

        /// <summary>当前连续签到天数（展示用；断签清 0）。</summary>
        internal int Streak;

        /// <summary>最后一次签到对应的天号（0 = 从未签到）。跨天时据此判断是否断签。</summary>
        internal int LastSignedDayIndex;

        /// <summary>累计签到天数（跨期累加，只增不减）。</summary>
        internal int TotalSignedDays;

        #endregion

        #region 悬赏（P1 起使用，P0 只落字段占位以免后续改 schema）

        /// <summary>悬赏随机种子。首次加载时派生一次后冻结，保证重启不重抽。</summary>
        internal long BountySeed;

        /// <summary>当前悬赏所属天号。</summary>
        internal int BountyDayIndex;

        /// <summary>当前悬赏类型 id。</summary>
        internal string BountyKindId;

        /// <summary>当前悬赏目标值。</summary>
        internal int BountyTarget;

        /// <summary>当前悬赏进度。</summary>
        internal int BountyProgress;

        /// <summary>当前悬赏是否已完成。</summary>
        internal bool BountyCompleted;

        /// <summary>当前悬赏奖励是否已发放（幂等）。</summary>
        internal bool BountyRewardClaimed;

        #endregion

        #region 统计

        /// <summary>今日累计统计。</summary>
        internal DailyReportStats Today;

        /// <summary>昨日快照（当期日报的内容源）。</summary>
        internal DailyReportStats Yesterday;

        /// <summary>昨日快照是否有效（第一天时无昨日）。</summary>
        internal bool HasYesterday;

        #endregion

        #region 元数据

        /// <summary>最后写入时间（UTC ticks），仅用于诊断。</summary>
        internal long LastUpdatedTicks;

        #endregion

        /// <summary>
        /// 深拷贝。入队持久化前必须拷贝一份再改，避免把还在被运行时改写的实例交给编码器。
        /// </summary>
        internal DailyReportData Clone()
        {
            DailyReportData copy = new DailyReportData();
            copy.DayIndex = DayIndex;
            copy.CarrySeconds = CarrySeconds;
            copy.LastSettledDayIndex = LastSettledDayIndex;
            copy.PeriodIndex = PeriodIndex;
            copy.PeriodSignedCount = PeriodSignedCount;
            copy.PeriodClaimedMask = PeriodClaimedMask;
            copy.Streak = Streak;
            copy.LastSignedDayIndex = LastSignedDayIndex;
            copy.TotalSignedDays = TotalSignedDays;
            copy.BountySeed = BountySeed;
            copy.BountyDayIndex = BountyDayIndex;
            copy.BountyKindId = BountyKindId;
            copy.BountyTarget = BountyTarget;
            copy.BountyProgress = BountyProgress;
            copy.BountyCompleted = BountyCompleted;
            copy.BountyRewardClaimed = BountyRewardClaimed;
            copy.Today = Today != null ? Today.Clone() : new DailyReportStats();
            copy.Yesterday = Yesterday != null ? Yesterday.Clone() : new DailyReportStats();
            copy.HasYesterday = HasYesterday;
            copy.LastUpdatedTicks = LastUpdatedTicks;
            return copy;
        }
    }
}
