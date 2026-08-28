// ============================================================================
// DailyReportBounty.cs - 每日悬赏目录与判定（P1）
// ============================================================================
// 设计要点：
//   - **当日悬赏不占存档**：它是 (bountySeed, dayIndex) 的纯函数，任何时候都能重算，
//     因此重启 / 读档必然得到同一条，不需要"抽完存下来"这一步。
//     存档里那组 Bounty* 字段记的是**已结算的昨日悬赏**，即当期日报要公布的那条。
//   - **进度不单独计数**：直接把今日统计（DailyReportStats）当作进度源。
//     少一套计数器就少一处双重计数和漏退订的风险。
//   - 悬赏奖励给现金，不给物品：避免与签到的品质梯度奖品互相稀释。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>悬赏类型。id 字符串是存档契约的一部分，禁止改名。</summary>
    internal enum DailyReportBountyKind
    {
        /// <summary>累计击杀 N 个敌人。</summary>
        Kills = 0,

        /// <summary>累计击杀 N 只 Boss。</summary>
        BossKills = 1,

        /// <summary>成功撤离 N 次。</summary>
        Extractions = 2,

        /// <summary>当日净赚 N 金。</summary>
        EarnMoney = 3,

        /// <summary>当日零死亡（需至少出击一次，防止挂机白嫖）。</summary>
        NoDeath = 4,
    }

    /// <summary>一条悬赏的定义。</summary>
    internal sealed class DailyReportBountyDef
    {
        /// <summary>稳定 id（存档与展示都用它）。</summary>
        internal string Id;

        /// <summary>类型。</summary>
        internal DailyReportBountyKind Kind;

        /// <summary>目标值。</summary>
        internal int Target;

        /// <summary>完成奖金。</summary>
        internal long CashReward;
    }

    /// <summary>每日悬赏目录。纯函数，无状态。</summary>
    internal static class DailyReportBounty
    {
        #region 确定性随机域

        private const string BountyDomain = "bossrush_daily_bounty";

        #endregion

        #region 目录（数值待 owner 微调；改这里即可，不影响结构）

        /// <summary>
        /// 悬赏模板池。每天从这里等概率抽一条，再按同一条流抽档位。
        /// 三档目标值对应「轻松 / 常规 / 硬核」，奖金随目标递增。
        /// </summary>
        private static readonly DailyReportBountyTemplate[] Templates =
        {
            new DailyReportBountyTemplate("kills", DailyReportBountyKind.Kills,
                new int[] { 30, 60, 120 }, new long[] { 800L, 1600L, 3200L }),

            new DailyReportBountyTemplate("boss_kills", DailyReportBountyKind.BossKills,
                new int[] { 1, 3, 5 }, new long[] { 1200L, 3000L, 5000L }),

            new DailyReportBountyTemplate("extractions", DailyReportBountyKind.Extractions,
                new int[] { 1, 2, 4 }, new long[] { 700L, 1400L, 2800L }),

            new DailyReportBountyTemplate("earn_money", DailyReportBountyKind.EarnMoney,
                new int[] { 5000, 15000, 40000 }, new long[] { 900L, 2000L, 4500L }),

            new DailyReportBountyTemplate("no_death", DailyReportBountyKind.NoDeath,
                new int[] { 1, 1, 1 }, new long[] { 1500L, 1500L, 1500L }),
        };

        private sealed class DailyReportBountyTemplate
        {
            internal readonly string Id;
            internal readonly DailyReportBountyKind Kind;
            internal readonly int[] Tiers;
            internal readonly long[] Rewards;

            internal DailyReportBountyTemplate(string id, DailyReportBountyKind kind,
                int[] tiers, long[] rewards)
            {
                Id = id;
                Kind = kind;
                Tiers = tiers;
                Rewards = rewards;
            }
        }

        #endregion

        #region 选取

        /// <summary>
        /// 取某天的悬赏。纯函数：同一 (seed, dayIndex) 永远同一条，
        /// 因此重启、读档、跨会话都不会换题。
        /// </summary>
        internal static DailyReportBountyDef SelectForDay(long seed, int dayIndex)
        {
            try
            {
                if (Templates.Length <= 0) return null;

                ModeHSeedStream stream = ModeHSeedStream.Create(seed, BountyDomain, dayIndex);
                DailyReportBountyTemplate template = Templates[stream.NextInt(Templates.Length)];
                if (template == null || template.Tiers == null || template.Tiers.Length <= 0) return null;

                int tier = stream.NextInt(template.Tiers.Length);
                DailyReportBountyDef def = new DailyReportBountyDef();
                def.Id = template.Id;
                def.Kind = template.Kind;
                def.Target = template.Tiers[tier];
                def.CashReward = (template.Rewards != null && tier < template.Rewards.Length)
                    ? template.Rewards[tier]
                    : 0L;
                return def;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>按 id 与目标值还原一条定义（用于展示已结算的昨日悬赏）。</summary>
        internal static DailyReportBountyDef Rebuild(string id, int target)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Templates.Length; i++)
            {
                if (!string.Equals(Templates[i].Id, id, StringComparison.Ordinal)) continue;
                DailyReportBountyDef def = new DailyReportBountyDef();
                def.Id = Templates[i].Id;
                def.Kind = Templates[i].Kind;
                def.Target = target;
                def.CashReward = 0L; // 已结算记录不再需要奖金数值
                return def;
            }
            return null;
        }

        #endregion

        #region 判定

        /// <summary>按今日统计算出进度（已按目标值截顶）。</summary>
        internal static int EvaluateProgress(DailyReportBountyDef def, DailyReportStats stats)
        {
            if (def == null || stats == null) return 0;

            int raw;
            switch (def.Kind)
            {
                case DailyReportBountyKind.Kills:
                    raw = stats.Kills;
                    break;
                case DailyReportBountyKind.BossKills:
                    raw = stats.BossKills;
                    break;
                case DailyReportBountyKind.Extractions:
                    raw = stats.Extractions;
                    break;
                case DailyReportBountyKind.EarnMoney:
                    long net = stats.MoneyEarned;
                    raw = net > int.MaxValue ? int.MaxValue : (int)net;
                    break;
                case DailyReportBountyKind.NoDeath:
                    // 需要至少出击一次才算数：否则整天待在基地也能白拿。
                    raw = (stats.Raids > 0 && stats.Deaths == 0) ? 1 : 0;
                    break;
                default:
                    raw = 0;
                    break;
            }

            if (raw < 0) raw = 0;
            if (def.Target > 0 && raw > def.Target) raw = def.Target;
            return raw;
        }

        /// <summary>是否达成。</summary>
        internal static bool IsComplete(DailyReportBountyDef def, DailyReportStats stats)
        {
            if (def == null || def.Target <= 0) return false;
            return EvaluateProgress(def, stats) >= def.Target;
        }

        #endregion

        #region 文案

        /// <summary>悬赏标题（含目标值）。</summary>
        internal static string DescribeTitle(DailyReportBountyDef def)
        {
            if (def == null) return L10n.T("今日无悬赏", "No bounty today");

            switch (def.Kind)
            {
                case DailyReportBountyKind.Kills:
                    return L10n.T("清剿 " + def.Target + " 名敌人",
                        "Eliminate " + def.Target + " enemies");
                case DailyReportBountyKind.BossKills:
                    return L10n.T("讨伐 " + def.Target + " 只首领",
                        "Slay " + def.Target + " bosses");
                case DailyReportBountyKind.Extractions:
                    return L10n.T("成功撤离 " + def.Target + " 次",
                        "Extract successfully " + def.Target + " times");
                case DailyReportBountyKind.EarnMoney:
                    return L10n.T("单日进账 " + def.Target + " 金",
                        "Earn " + def.Target + " in a single day");
                case DailyReportBountyKind.NoDeath:
                    return L10n.T("出击且全身而退（零阵亡）",
                        "Deploy and survive (zero deaths)");
                default:
                    return L10n.T("今日无悬赏", "No bounty today");
            }
        }

        /// <summary>悬赏的报纸腔小字说明。</summary>
        internal static string DescribeFlavor(DailyReportBountyDef def)
        {
            if (def == null) return string.Empty;

            switch (def.Kind)
            {
                case DailyReportBountyKind.Kills:
                    return L10n.T("治安委员会：近郊野兽泛滥，见者有赏。",
                        "Public Safety Board: vermin overrun the outskirts. Bounty on sight.");
                case DailyReportBountyKind.BossKills:
                    return L10n.T("头版悬赏：几张熟面孔又出现在了通缉栏上。",
                        "Front-page bounty: familiar faces are back on the wanted board.");
                case DailyReportBountyKind.Extractions:
                    return L10n.T("撤离调度处提醒：活着回来才算数。",
                        "Extraction Dispatch reminds you: only coming back counts.");
                case DailyReportBountyKind.EarnMoney:
                    return L10n.T("商会告示：今日汇率不错，适合做买卖。",
                        "Merchant notice: favorable rates today. Good day for trade.");
                case DailyReportBountyKind.NoDeath:
                    return L10n.T("医保局温馨提示：少来几趟，我们也省点钱。",
                        "Health Bureau: fewer visits, lower premiums. For both of us.");
                default:
                    return string.Empty;
            }
        }

        #endregion
    }
}
