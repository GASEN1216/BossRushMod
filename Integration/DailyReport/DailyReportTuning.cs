// ============================================================================
// DailyReportTuning.cs - 鸭科夫日报数值常量单点（P0 步骤 1）
// ============================================================================
// 归位依据 AGENTS.md 4.8「Config 三层归位」第 2 层：玩法强耦合常量放模块配置类。
// 只有入口总开关 dailyReportEnabled 走 Config/ConfigDailyReport.cs + ModConfig（第 1 层），
// 其余数值一律在这里，owner 审定后单点改。
//
// 设计文档：docs/未来拓展/设计/P2-日报系统.md
// ============================================================================

namespace BossRush
{
    /// <summary>日报数值常量。无状态、无逻辑，只有 const / static readonly。</summary>
    internal static class DailyReportTuning
    {
        #region 计时（口径见设计文档 §3.1）

        /// <summary>
        /// 一个游戏日的游戏内秒数。**必须**与官方 GameClock.SecondsPerDay 一致（86300，
        /// 不是 86400）：官方 StepTime 里就是 `while (secondsOfDay > 86300.0)`。
        /// 我们不读官方私有常量，只在这里镜像一份并由 guard 守卫。
        /// </summary>
        internal const double GameSecondsPerDay = 86300d;

        /// <summary>
        /// 官方时钟倍率兜底值（GameClock.clockTimeScale 默认 60：1 现实秒 = 60 游戏秒）。
        /// 运行时优先读实例字段，读不到才用这个，保证玩家改过倍率时跟随。
        /// </summary>
        internal const float DefaultClockTimeScale = 60f;

        /// <summary>
        /// 单帧增量保险帽（现实秒）。加载完成后的首帧 deltaTime 可能是个大尖峰，
        /// 钳一下避免把一次卡顿算成几分钟游戏时间。正常帧远小于它，不影响精度。
        /// </summary>
        internal const float MaxRealDeltaPerFrame = 2f;

        #endregion

        #region 签到（口径见设计文档 §2）

        /// <summary>每期天数（UI 一页 30 格）。满期翻下一期，防止签到墙无限变长。</summary>
        internal const int DaysPerPeriod = 30;

        /// <summary>第 1 期的里程碑格位（1-based），与 MilestoneQualitiesFirstPeriod 一一对应。</summary>
        internal static readonly int[] MilestoneSlotsFirstPeriod = { 7, 15, 24, 30 };

        /// <summary>第 1 期各里程碑发放的物品品质。</summary>
        internal static readonly int[] MilestoneQualitiesFirstPeriod = { 5, 6, 7, 8 };

        /// <summary>第 2 期起的里程碑格位（每 7 格一给）。</summary>
        internal static readonly int[] MilestoneSlotsLaterPeriod = { 7, 14, 21, 28 };

        /// <summary>第 2 期起里程碑的固定品质。</summary>
        internal const int MilestoneQualityLaterPeriod = 8;

        /// <summary>抽奖品时的最大重试次数（抽中黑名单物品就重抽）。</summary>
        internal const int RewardRollMaxAttempts = 8;

        #endregion

        #region 建筑（发布后冻结，见 AGENTS.md §5 契约）

        /// <summary>
        /// 报箱建筑 ID。玩家放置记录会以此进官方 BuildingData 存档，
        /// **发布后永不可改名**，否则老存档里的报箱会变成缺 prefab 的幽灵。
        /// </summary>
        internal const string MailboxBuildingId = "bossrush_daily_mailbox";

        /// <summary>报箱造价（金）。</summary>
        internal const long MailboxCost = 500L;

        /// <summary>报箱限建数量。</summary>
        internal const int MailboxMaxAmount = 1;

        #endregion

        #region 存档 key（v1 冻结，只增不改）

        /// <summary>日报状态：天数 / 余数 / 签到 / 悬赏 / 昨日快照。</summary>
        internal const string StorageKey = "BossRush_DailyReport_v1";

        /// <summary>当前 schema 版本。更高版本 fail-closed 只读，不覆盖。</summary>
        internal const int CurrentSchemaVersion = 1;

        #endregion

        #region 本地化与身份前缀

        /// <summary>本地化 key 前缀（唯一入口 Localization/DailyReportLocalization.cs）。</summary>
        internal const string LocalizationPrefix = "BossRush_DailyReport_";

        /// <summary>模块名（运行时模块 host 日志与 owner label）。</summary>
        internal const string ModuleName = "DailyReport";

        /// <summary>日志前缀。</summary>
        internal const string LogPrefix = "[DailyReport] ";

        #endregion
    }
}
