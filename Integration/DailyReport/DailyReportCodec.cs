// ============================================================================
// DailyReportCodec.cs - 日报存档编解码（P0 步骤 2）
// ============================================================================
// 硬约束：
//   - 整个 DTO 编成**一个扁平 JSON 对象**，schemaVersion 作为顶层字段。
//     扁平化是刻意的：这样只需仓库既有的 Utilities/SimpleJsonHelper.cs，
//     不必再造第三套 JSON 解析器（ModeH 有 ModeHJsonValue、遗种巢有 PetNestJson，
//     两者都与各自模块语义绑定，互相 import 会让彼此成为对方的升级阻塞项）。
//   - 今日/昨日统计用 `t_` / `y_` 前缀展平，不做嵌套对象。
//   - 提取器按 `"key":` 前缀匹配，因此**所有 key 必须两两不构成"带引号前缀"关系**；
//     现有 key 集合已核对（dayIndex / lastSettledDayIndex / bountyDayIndex /
//     lastSignedDayIndex 因为模式带前导引号而互不误命中）。
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
        /// 读取 payload 的 schemaVersion。缺字段返回 -1（走写屏障，绝不覆盖）。
        /// </summary>
        internal static int ReadSchemaVersion(string json)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            try
            {
                if (json.IndexOf("\"schemaVersion\":", StringComparison.Ordinal) < 0) return -1;
                return SimpleJsonHelper.ExtractInt(json, "schemaVersion");
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
                DailyReportData data = new DailyReportData();

                data.DayIndex = SimpleJsonHelper.ExtractInt(json, "dayIndex");
                data.CarrySeconds = SimpleJsonHelper.ExtractFloat(json, "carrySeconds");
                data.LastSettledDayIndex = SimpleJsonHelper.ExtractInt(json, "lastSettledDayIndex");

                data.PeriodIndex = SimpleJsonHelper.ExtractInt(json, "periodIndex");
                data.PeriodSignedCount = SimpleJsonHelper.ExtractInt(json, "periodSignedCount");
                data.PeriodClaimedMask = SimpleJsonHelper.ExtractInt(json, "periodClaimedMask");
                data.Streak = SimpleJsonHelper.ExtractInt(json, "streak");
                data.LastSignedDayIndex = SimpleJsonHelper.ExtractInt(json, "lastSignedDayIndex");
                data.TotalSignedDays = SimpleJsonHelper.ExtractInt(json, "totalSignedDays");

                data.BountySeed = SimpleJsonHelper.ExtractLong(json, "bountySeed");
                data.BountyDayIndex = SimpleJsonHelper.ExtractInt(json, "bountyDayIndex");
                data.BountyKindId = SimpleJsonHelper.ExtractString(json, "bountyKindId");
                data.BountyTarget = SimpleJsonHelper.ExtractInt(json, "bountyTarget");
                data.BountyProgress = SimpleJsonHelper.ExtractInt(json, "bountyProgress");
                data.BountyCompleted = SimpleJsonHelper.ExtractBool(json, "bountyCompleted");
                data.BountyRewardClaimed = SimpleJsonHelper.ExtractBool(json, "bountyRewardClaimed");

                data.HasYesterday = SimpleJsonHelper.ExtractBool(json, "hasYesterday");
                data.Today = DecodeStats(json, "t_");
                data.Yesterday = DecodeStats(json, "y_");

                data.LastUpdatedTicks = SimpleJsonHelper.ExtractLong(json, "lastUpdatedTicks");

                Sanitize(data);
                return data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static DailyReportStats DecodeStats(string json, string prefix)
        {
            DailyReportStats s = new DailyReportStats();
            s.Kills = SimpleJsonHelper.ExtractInt(json, prefix + "kills");
            s.BossKills = SimpleJsonHelper.ExtractInt(json, prefix + "bossKills");
            s.Deaths = SimpleJsonHelper.ExtractInt(json, prefix + "deaths");
            s.Raids = SimpleJsonHelper.ExtractInt(json, prefix + "raids");
            s.Extractions = SimpleJsonHelper.ExtractInt(json, prefix + "extractions");
            s.MoneyEarned = SimpleJsonHelper.ExtractLong(json, prefix + "moneyEarned");
            s.MoneySpent = SimpleJsonHelper.ExtractLong(json, prefix + "moneySpent");
            s.DamageDealt = SimpleJsonHelper.ExtractFloat(json, prefix + "damageDealt");
            s.DamageTaken = SimpleJsonHelper.ExtractFloat(json, prefix + "damageTaken");
            s.MaxSingleHit = SimpleJsonHelper.ExtractFloat(json, prefix + "maxSingleHit");
            return s;
        }

        /// <summary>
        /// 读档后的取值收敛。老档字段缺失时提取器返回 0，这里把不合法的 0 拉回合法域，
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
