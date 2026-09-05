// ============================================================================
// DailyReportCodec.cs - 日报存档编解码（P0 步骤 2）
// ============================================================================
// 硬约束：
//   - 整个 DTO 编成**一个扁平 JSON 对象**，schemaVersion 作为顶层字段；
//     今日/昨日统计用 `t_` / `y_` 前缀展平。扁平化是发布后冻结的存档字段面，
//     不因解析器升级而改。
//   - 写侧走 Utilities/SimpleJsonHelper.cs 的 Append* 系列（字节格式不变）；
//     读侧自 2026-09-06 起走全 Mod 共享的节点解析器 Common/Data/BossRushJsonValue.cs，
//     不再依赖「key 互不为带引号前缀」这类提取器约束，新增字段只需保持向后兼容默认值。
//   - 解码全程 no-throw，失败由 DailyReportPersistence 走写屏障 fail-closed。
// ============================================================================

using System;
using System.Text;

namespace BossRush
{
    /// <summary>日报 DTO 的 JSON 编解码。纯函数，无状态。</summary>
    internal static class DailyReportCodec
    {
        #region 默认值

        /// <summary>
        /// 新档默认值。**所有默认值只在这里给一处**，避免"构造出来的默认"与
        /// "读档读出来的默认"两套语义漂移。
        /// </summary>
        internal static DailyReportData CreateDefault()
        {
            DailyReportData data = new DailyReportData();
            data.DayIndex = 1;
            data.CarrySeconds = 0d;
            data.LastSettledDayIndex = 0;
            data.PeriodIndex = 1;
            data.PeriodSignedCount = 0;
            data.PeriodClaimedMask = 0;
            data.Streak = 0;
            data.LastSignedDayIndex = 0;
            data.TotalSignedDays = 0;
            data.BountySeed = 0L;          // 0 = 未派生，首次使用时由 Service 派生并冻结
            data.BountyDayIndex = 0;
            data.BountyKindId = string.Empty;
            data.BountyTarget = 0;
            data.BountyProgress = 0;
            data.BountyCompleted = false;
            data.BountyRewardClaimed = false;
            data.Today = new DailyReportStats();
            data.Yesterday = new DailyReportStats();
            data.HasYesterday = false;
            data.PendingIssueBanner = false;
            data.LastUpdatedTicks = 0L;
            return data;
        }

        #endregion

        #region 编码

        /// <summary>把 DTO 编成一个扁平 JSON 对象字符串。</summary>
        internal static string Encode(DailyReportData data)
        {
            if (data == null) return null;

            StringBuilder sb = SimpleJsonHelper.GetBuilder();
            sb.Append('{');

            SimpleJsonHelper.AppendInt(sb, "schemaVersion", DailyReportTuning.CurrentSchemaVersion);

            SimpleJsonHelper.AppendInt(sb, "dayIndex", data.DayIndex);
            SimpleJsonHelper.AppendFloat(sb, "carrySeconds", (float)data.CarrySeconds);
            SimpleJsonHelper.AppendInt(sb, "lastSettledDayIndex", data.LastSettledDayIndex);

            SimpleJsonHelper.AppendInt(sb, "periodIndex", data.PeriodIndex);
            SimpleJsonHelper.AppendInt(sb, "periodSignedCount", data.PeriodSignedCount);
            SimpleJsonHelper.AppendInt(sb, "periodClaimedMask", data.PeriodClaimedMask);
            SimpleJsonHelper.AppendInt(sb, "streak", data.Streak);
            SimpleJsonHelper.AppendInt(sb, "lastSignedDayIndex", data.LastSignedDayIndex);
            SimpleJsonHelper.AppendInt(sb, "totalSignedDays", data.TotalSignedDays);

            SimpleJsonHelper.AppendLong(sb, "bountySeed", data.BountySeed);
            SimpleJsonHelper.AppendInt(sb, "bountyDayIndex", data.BountyDayIndex);
            SimpleJsonHelper.AppendString(sb, "bountyKindId", data.BountyKindId ?? string.Empty);
            SimpleJsonHelper.AppendInt(sb, "bountyTarget", data.BountyTarget);
            SimpleJsonHelper.AppendInt(sb, "bountyProgress", data.BountyProgress);
            SimpleJsonHelper.AppendBool(sb, "bountyCompleted", data.BountyCompleted);
            SimpleJsonHelper.AppendBool(sb, "bountyRewardClaimed", data.BountyRewardClaimed);

            SimpleJsonHelper.AppendBool(sb, "hasYesterday", data.HasYesterday);
            // 可选追加字段；旧档缺失时解码回落 false，保持向后兼容。
            SimpleJsonHelper.AppendBool(sb, "pendingIssueBanner", data.PendingIssueBanner);

            AppendStats(sb, "t_", data.Today);
            AppendStats(sb, "y_", data.Yesterday);

            // 最后一个字段不带逗号
            SimpleJsonHelper.AppendLong(sb, "lastUpdatedTicks", data.LastUpdatedTicks, false);

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendStats(StringBuilder sb, string prefix, DailyReportStats stats)
        {
            DailyReportStats s = stats ?? new DailyReportStats();
            SimpleJsonHelper.AppendInt(sb, prefix + "kills", s.Kills);
            SimpleJsonHelper.AppendInt(sb, prefix + "bossKills", s.BossKills);
            SimpleJsonHelper.AppendInt(sb, prefix + "deaths", s.Deaths);
            SimpleJsonHelper.AppendInt(sb, prefix + "raids", s.Raids);
            SimpleJsonHelper.AppendInt(sb, prefix + "extractions", s.Extractions);
            SimpleJsonHelper.AppendLong(sb, prefix + "moneyEarned", s.MoneyEarned);
            SimpleJsonHelper.AppendLong(sb, prefix + "moneySpent", s.MoneySpent);
            SimpleJsonHelper.AppendFloat(sb, prefix + "damageDealt", s.DamageDealt);
            SimpleJsonHelper.AppendFloat(sb, prefix + "damageTaken", s.DamageTaken);
            SimpleJsonHelper.AppendFloat(sb, prefix + "maxSingleHit", s.MaxSingleHit);
        }

        #endregion

        #region 解码

        /// <summary>
        /// 读取 payload 的 schemaVersion。缺字段或读不动返回 -1（走写屏障，绝不覆盖）。
        /// </summary>
        internal static int ReadSchemaVersion(string json)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            try
            {
                BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
                return root != null ? root.GetInt("schemaVersion", -1) : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// 解码 payload。失败返回 null（由持久化层 fail-closed）。
        /// 调用方应先用 ReadSchemaVersion 校验版本。
        /// </summary>
        internal static DailyReportData Decode(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
                if (root == null || root.Kind != BossRushJsonKind.Object) return null;

                DailyReportData data = new DailyReportData();

                data.DayIndex = root.GetInt("dayIndex", 0);
                data.CarrySeconds = root.GetFloat("carrySeconds", 0f);
                data.LastSettledDayIndex = root.GetInt("lastSettledDayIndex", 0);

                data.PeriodIndex = root.GetInt("periodIndex", 0);
                data.PeriodSignedCount = root.GetInt("periodSignedCount", 0);
                data.PeriodClaimedMask = root.GetInt("periodClaimedMask", 0);
                data.Streak = root.GetInt("streak", 0);
                data.LastSignedDayIndex = root.GetInt("lastSignedDayIndex", 0);
                data.TotalSignedDays = root.GetInt("totalSignedDays", 0);

                data.BountySeed = root.GetLong("bountySeed", 0L);
                data.BountyDayIndex = root.GetInt("bountyDayIndex", 0);
                data.BountyKindId = root.GetString("bountyKindId", string.Empty);
                data.BountyTarget = root.GetInt("bountyTarget", 0);
                data.BountyProgress = root.GetInt("bountyProgress", 0);
                data.BountyCompleted = root.GetBool("bountyCompleted", false);
                data.BountyRewardClaimed = root.GetBool("bountyRewardClaimed", false);

                data.HasYesterday = root.GetBool("hasYesterday", false);
                data.PendingIssueBanner = root.GetBool("pendingIssueBanner", false);
                data.Today = DecodeStats(root, "t_");
                data.Yesterday = DecodeStats(root, "y_");

                data.LastUpdatedTicks = root.GetLong("lastUpdatedTicks", 0L);

                Sanitize(data);
                return data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static DailyReportStats DecodeStats(BossRushJsonValue root, string prefix)
        {
            DailyReportStats s = new DailyReportStats();
            s.Kills = root.GetInt(prefix + "kills", 0);
            s.BossKills = root.GetInt(prefix + "bossKills", 0);
            s.Deaths = root.GetInt(prefix + "deaths", 0);
            s.Raids = root.GetInt(prefix + "raids", 0);
            s.Extractions = root.GetInt(prefix + "extractions", 0);
            s.MoneyEarned = root.GetLong(prefix + "moneyEarned", 0L);
            s.MoneySpent = root.GetLong(prefix + "moneySpent", 0L);
            s.DamageDealt = root.GetFloat(prefix + "damageDealt", 0f);
            s.DamageTaken = root.GetFloat(prefix + "damageTaken", 0f);
            s.MaxSingleHit = root.GetFloat(prefix + "maxSingleHit", 0f);
            return s;
        }

        /// <summary>
        /// 读档后的取值收敛。老档字段缺失时解码回落 0，这里把不合法的 0 拉回合法域，
        /// 避免"第 0 天""第 0 期"这种不存在的状态渗进玩法逻辑。
        /// </summary>
        private static void Sanitize(DailyReportData data)
        {
            if (data.DayIndex < 1) data.DayIndex = 1;
            if (data.PeriodIndex < 1) data.PeriodIndex = 1;

            if (data.PeriodSignedCount < 0) data.PeriodSignedCount = 0;
            if (data.PeriodSignedCount > DailyReportTuning.DaysPerPeriod)
            {
                data.PeriodSignedCount = DailyReportTuning.DaysPerPeriod;
            }

            if (data.Streak < 0) data.Streak = 0;
            if (data.TotalSignedDays < 0) data.TotalSignedDays = 0;
            if (data.LastSignedDayIndex < 0) data.LastSignedDayIndex = 0;
            if (data.LastSettledDayIndex < 0) data.LastSettledDayIndex = 0;

            if (data.CarrySeconds < 0d) data.CarrySeconds = 0d;
            if (data.CarrySeconds >= DailyReportTuning.GameSecondsPerDay)
            {
                // 余数只可能因为浮点写回精度落在边界上；越界一律钳回当日内，
                // 不在这里补跨天（补天只属于 Service 的 rollover 循环）。
                data.CarrySeconds = DailyReportTuning.GameSecondsPerDay - 1d;
            }

            if (data.Today == null) data.Today = new DailyReportStats();
            if (data.Yesterday == null) data.Yesterday = new DailyReportStats();
            if (data.BountyKindId == null) data.BountyKindId = string.Empty;
        }

        #endregion
    }
}
