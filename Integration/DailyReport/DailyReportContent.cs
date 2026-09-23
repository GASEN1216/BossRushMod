// ============================================================================
// DailyReportContent.cs - 一期日报的版面数据与文案生成（P1）
// ============================================================================
// 设计要点：
//   - 文案**不进本地化表**：日报正文是每天变的动态长文，塞进 LocalizationManager 的
//     全局字符串表既污染表也无法按天变；一律走 L10n.T(cn, en) 双语内联。
//     只有官方系统会去查表的 key（建筑名、交互名）才走 Localization/DailyReportLocalization.cs。
//   - 选稿用确定性随机：同一天重开游戏、反复开合面板，头条稿件不会变。
//   - 天气预报口径：`WeatherManager.GetWeather(TimeSpan)` 是 (种子, 世界时间) 的纯函数，
//     所以能精确预报。但它预报的是**官方世界时间的明天**，与日报自算的期号并不对齐
//     （玩家睡一觉两者就错位），因此明确标注「明日此时（世界时间）」这个采样时点。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Weathers;

namespace BossRush
{
    /// <summary>一期日报的渲染数据。只读快照，UI 直接消费。</summary>
    internal sealed class DailyReportIssue
    {
        /// <summary>期号（= 被报道的那一天的天号）。</summary>
        internal int IssueNumber;

        /// <summary>是否有昨日数据（第一天时没有）。</summary>
        internal bool HasYesterday;

        /// <summary>头条标题。</summary>
        internal string Headline;

        /// <summary>头条正文。</summary>
        internal string HeadlineBody;

        /// <summary>战绩栏各行。</summary>
        internal List<string> StatLines;

        /// <summary>昨日悬赏的结果公布行。</summary>
        internal string BountyResultLine;

        /// <summary>今日悬赏标题。</summary>
        internal string TodayBountyTitle;

        /// <summary>今日悬赏小字。</summary>
        internal string TodayBountyFlavor;

        /// <summary>与结算判据一致的当前状态。</summary>
        internal string TodayBountyStatus;

        /// <summary>今日悬赏进度。</summary>
        internal int TodayBountyProgress;

        /// <summary>今日悬赏目标。</summary>
        internal int TodayBountyTarget;

        /// <summary>今日悬赏奖金。</summary>
        internal long TodayBountyCash;

        /// <summary>天气预报行。</summary>
        internal string WeatherLine;

        /// <summary>运势行。</summary>
        internal string FortuneLine;

        /// <summary>杂谈行。</summary>
        internal string GossipLine;
    }

    /// <summary>日报版面生成。纯函数，无状态。</summary>
    internal static class DailyReportContent
    {
        #region 确定性随机域

        private const string NewsDomain = "bossrush_daily_news";
        private const string FortuneDomain = "bossrush_daily_fortune";
        private const string GossipDomain = "bossrush_daily_gossip";

        #endregion

        #region 版面组装

        /// <summary>按当前存档状态组装当期日报。</summary>
        internal static DailyReportIssue BuildCurrentIssue()
        {
            DailyReportIssue issue = new DailyReportIssue();
            issue.StatLines = new List<string>();

            try
            {
                DailyReportData data = DailyReportService.Data;
                if (data == null) return BuildEmptyIssue(issue);

                // 必须走 EnsureBountySeed 而不是直读字段：新档 BountySeed 为 0，
                // 直读会让首次开报纸用 0 号种子选头条/运势/杂谈，之后任一悬赏查询
                // 冻结真种子，同一天重开整版换稿，违背「同日重开不换稿」的承诺。
                long seed = DailyReportService.EnsureBountySeed(data);
                if (seed == 0L) return BuildEmptyIssue(issue);
                // 被报道的是"昨天"，即刚结算完的那一天
                int reportedDay = data.DayIndex - 1;
                if (reportedDay < 1) reportedDay = 1;

                issue.IssueNumber = data.DayIndex;
                issue.HasYesterday = data.HasYesterday;

                DailyReportStats y = data.Yesterday ?? new DailyReportStats();
                BuildHeadline(issue, y, seed, reportedDay, data.HasYesterday);
                BuildStatLines(issue, y, data.HasYesterday);
                BuildBountyResult(issue, data);
                BuildTodayBounty(issue, data);

                issue.WeatherLine = BuildWeatherLine();
                issue.FortuneLine = BuildFortuneLine(seed, data.DayIndex);
                issue.GossipLine = BuildGossipLine(seed, data.DayIndex);

                return issue;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 版面组装失败: " + e.Message);
                return BuildEmptyIssue(issue);
            }
        }

        private static DailyReportIssue BuildEmptyIssue(DailyReportIssue issue)
        {
            if (issue.StatLines == null) issue.StatLines = new List<string>();
            issue.Headline = L10n.T("本期暂无消息", "No news this issue");
            issue.HeadlineBody = L10n.T(
                "排字工人还在等昨天的稿子。",
                "The typesetters are still waiting on yesterday's copy.");
            return issue;
        }

        #endregion

        #region 头条

        /// <summary>
        /// 头条选题优先级：Boss 击杀 &gt; 高击杀 &gt; 多次阵亡 &gt; 大额进账 &gt; 撤离 &gt; 无事发生。
        /// 同一档位有多条备稿，用当天种子选一条，保证重开不换稿。
        /// </summary>
        private static void BuildHeadline(DailyReportIssue issue, DailyReportStats y,
            long seed, int reportedDay, bool hasYesterday)
        {
            ModeHSeedStream stream = ModeHSeedStream.Create(seed, NewsDomain, reportedDay);

            if (!hasYesterday)
            {
                issue.Headline = L10n.T("创刊号：你的战报，从今天开始", "First Issue: Your Story Starts Today");
                issue.HeadlineBody = L10n.T("先看看今日悬赏，再安排出击。下一期会刊登你的战绩。",
                    "Check today's bounty before planning a run. Your recap appears in the next issue.");
                return;
            }

            if (!y.HasAnyActivity)
            {
                issue.Headline = Pick(ref stream, new string[]
                {
                    L10n.T("鸭科夫今日无事发生", "Nothing Happened in Duckov Today"),
                    L10n.T("广场鸽子占领头版", "Pigeons Seize the Front Page"),
                    L10n.T("本报记者无稿可写", "Our Reporter Has Nothing to Report"),
                });
                issue.HeadlineBody = Pick(ref stream, new string[]
                {
                    L10n.T("编辑部决定用这块版面登一张鸽子晒太阳的照片。",
                        "The editors filled this space with a photo of a pigeon sunbathing."),
                    L10n.T("据可靠消息，昨日全城最激烈的冲突发生在食堂打饭队伍。",
                        "Sources confirm yesterday's fiercest conflict occurred in the canteen queue."),
                });
                return;
            }

            if (y.BossKills > 0)
            {
                issue.Headline = y.BossKills >= 3
                    ? L10n.T("血洗！一日之内 " + y.BossKills + " 名首领覆灭",
                        "Bloodbath! " + y.BossKills + " Bosses Fell in a Single Day")
                    : L10n.T("首领覆灭：目击者称场面一度失控",
                        "Boss Down: Witnesses Say Things Got Out of Hand");
                issue.HeadlineBody = L10n.T(
                    "昨日确认击杀 " + y.Kills + " 名敌人，其中 " + y.BossKills
                        + " 名是首领。治安委员会拒绝置评。",
                    "Yesterday's " + y.Kills + " confirmed kills included " + y.BossKills
                        + " bosses. The Safety Board declined to comment.");
                return;
            }

            if (y.Kills >= 50)
            {
                issue.Headline = L10n.T("清剿行动：昨日 " + y.Kills + " 名敌人退场",
                    "Sweep Operation: " + y.Kills + " Hostiles Removed");
                issue.HeadlineBody = L10n.T(
                    "统计员表示已经数不过来了，建议改用称重计量。",
                    "Our statistician has stopped counting and suggests weighing them instead.");
                return;
            }

            if (y.Deaths >= 3)
            {
                issue.Headline = L10n.T("某鸭昨日 " + y.Deaths + " 次进医院",
                    "Local Duck Hospitalized " + y.Deaths + " Times Yesterday");
                issue.HeadlineBody = L10n.T(
                    "医保局已发函关切，并附上了一份保费调整通知。",
                    "The Health Bureau has sent a letter of concern, along with a premium adjustment notice.");
                return;
            }

            if (y.MoneyEarned >= 20000L)
            {
                issue.Headline = L10n.T("暴富传闻：昨日进账 " + y.MoneyEarned + " 金",
                    "Fortune Rumors: " + y.MoneyEarned + " Earned Yesterday");
                issue.HeadlineBody = L10n.T(
                    "商会连夜召开会议，讨论是否需要调整汇率。",
                    "The merchant guild convened overnight to discuss adjusting the exchange rate.");
                return;
            }

            if (y.Extractions > 0)
            {
                issue.Headline = L10n.T("平安归来：昨日成功撤离 " + y.Extractions + " 次",
                    "Safe Return: " + y.Extractions + " Successful Extractions");
                issue.HeadlineBody = L10n.T(
                    "撤离调度处对此表示满意，并提醒下次记得带够弹药。",
                    "Extraction Dispatch is satisfied, and reminds you to pack enough ammo next time.");
                return;
            }

            issue.Headline = L10n.T("昨日战报", "Yesterday's Dispatch");
            issue.HeadlineBody = y.Deaths > 0
                ? L10n.T("昨日阵亡 " + y.Deaths + " 次。整理补给，下一趟量力而行。",
                    "Deaths yesterday: " + y.Deaths + ". Restock and plan your next run.")
                : y.Kills > 0
                ? L10n.T("昨日击杀 " + y.Kills + " 名敌人。明细见下方战绩栏。",
                    "Enemies defeated yesterday: " + y.Kills + ". Details in the stats below.")
                : L10n.T("昨日没怎么开枪，忙着整备和赶路。记录见下栏。",
                    "A quiet day: mostly restocking and walking. Yesterday's record is below.");
        }

        #endregion

        #region 战绩栏

        private static void BuildStatLines(DailyReportIssue issue, DailyReportStats y, bool hasYesterday)
        {
            if (!hasYesterday)
            {
                issue.StatLines.Add(L10n.T("（本报创刊号，暂无昨日数据）",
                    "(Inaugural issue - no data for yesterday)"));
                return;
            }

            issue.StatLines.Add(L10n.T("击杀：" + y.Kills + "　首领：" + y.BossKills,
                "Kills: " + y.Kills + "   Bosses: " + y.BossKills));
            issue.StatLines.Add(L10n.T("出击：" + y.Raids + "　撤离：" + y.Extractions
                    + "　阵亡：" + y.Deaths,
                "Deployments: " + y.Raids + "   Extractions: " + y.Extractions
                    + "   Deaths: " + y.Deaths));
            issue.StatLines.Add(L10n.T("进账：" + y.MoneyEarned + "　支出：" + y.MoneySpent,
                "Earned: " + y.MoneyEarned + "   Spent: " + y.MoneySpent));

            // 输出只计敌方角色；承伤包含环境伤害。
            issue.StatLines.Add(L10n.T(
                "输出：" + y.DamageDealt.ToString("0") + "　承伤：" + y.DamageTaken.ToString("0"),
                "Damage dealt: " + y.DamageDealt.ToString("0") + "   Taken: " + y.DamageTaken.ToString("0")));

            if (y.MaxSingleHit > 0f)
            {
                issue.StatLines.Add(L10n.T(
                    "最大单次伤害：" + y.MaxSingleHit.ToString("0"),
                    "Biggest single hit: " + y.MaxSingleHit.ToString("0")));
            }
        }

        #endregion

        #region 悬赏栏

        private static void BuildBountyResult(DailyReportIssue issue, DailyReportData data)
        {
            if (string.IsNullOrEmpty(data.BountyKindId))
            {
                issue.BountyResultLine = string.Empty;
                return;
            }

            DailyReportBountyDef def = DailyReportBounty.Rebuild(data.BountyKindId, data.BountyTarget);
            string title = DailyReportBounty.DescribeTitle(def);

            string resultDay = L10n.T("第 " + data.BountyDayIndex + " 天", "Day " + data.BountyDayIndex);
            if (data.BountyCompleted)
            {
                issue.BountyResultLine = data.BountyRewardClaimed
                    ? L10n.T("【" + resultDay + "达成】" + title + " · 奖金已到账",
                        "[" + resultDay + ": cleared] " + title + " · Cash received")
                    : L10n.T("【" + resultDay + "达成】" + title + " · 奖金待补发",
                        "[" + resultDay + ": cleared] " + title + " · Cash pending");
            }
            else
            {
                issue.BountyResultLine = L10n.T(
                    "【" + resultDay + "未达成】" + title + "（" + data.BountyProgress + "/" + data.BountyTarget + "）",
                    "[" + resultDay + ": incomplete] " + title + " (" + data.BountyProgress + "/" + data.BountyTarget + ")");
            }
            long pending = DailyReportService.GetPendingBountyCash(data);
            if (pending > 0L)
                issue.BountyResultLine += L10n.T(" · 待到账共 " + pending + " 金",
                    " · Total pending: " + pending);

        }

        private static void BuildTodayBounty(DailyReportIssue issue, DailyReportData data)
        {
            DailyReportBountyDef def = DailyReportService.GetActiveBounty();
            if (def == null)
            {
                issue.TodayBountyTitle = L10n.T("今日无悬赏", "No bounty today");
                issue.TodayBountyFlavor = string.Empty;
                return;
            }

            issue.TodayBountyTitle = DailyReportBounty.DescribeTitle(def);
            issue.TodayBountyFlavor = DailyReportBounty.DescribeFlavor(def);
            issue.TodayBountyStatus = DailyReportBounty.DescribeStatus(def, data.Today);
            issue.TodayBountyCash = def.CashReward;
            issue.TodayBountyTarget = def.Target;
            issue.TodayBountyProgress = DailyReportBounty.EvaluateProgress(def, data.Today);
        }

        #endregion

        #region 天气 / 运势 / 杂谈

        /// <summary>
        /// 明日天气预报。只在组装版面时查一次：官方 WeatherManager 内部有单条缓存，
        /// 高频异时查询会把它顶掉，影响正在运行的天气表现。
        /// </summary>
        private static string BuildWeatherLine()
        {
            try
            {
                if (GameClock.Instance == null || WeatherManager.Instance == null)
                    return L10n.T("暂无天气预报。", "Forecast unavailable.");
                if (WeatherManager.Instance.ForceWeather)
                    return L10n.T("当前固定天气：" + DescribeWeather(WeatherManager.Instance.ForceWeatherValue),
                        "Fixed weather: " + DescribeWeatherEn(WeatherManager.Instance.ForceWeatherValue));
                TimeSpan tomorrow = GameClock.Now + TimeSpan.FromDays(1d);
                Weather w = WeatherManager.GetWeather(tomorrow);
                return L10n.T("明日此时（世界时间）：" + DescribeWeather(w),
                    "Tomorrow at this world time: " + DescribeWeatherEn(w));
            }
            catch (Exception)
            {
                return L10n.T("气象台今日休假。", "The weather station is off duty today.");
            }
        }

        private static string DescribeWeather(Weather w)
        {
            switch (w)
            {
                case Weather.Sunny: return "晴，适合出门捡垃圾";
                case Weather.Cloudy: return "多云，视野尚可";
                case Weather.Rainy: return "有雨，出发前检查补给";
                case Weather.Snow: return "降雪，留意沿途敌情";
                case Weather.Stormy_I: return "风暴警报（一级），非必要不出门";
                case Weather.Stormy_II: return "风暴警报（二级），出门等于送死";
                default: return "天象不明";
            }
        }

        private static string DescribeWeatherEn(Weather w)
        {
            switch (w)
            {
                case Weather.Sunny: return "clear - good day for scavenging";
                case Weather.Cloudy: return "cloudy - visibility acceptable";
                case Weather.Rainy: return "rain - check supplies before leaving";
                case Weather.Snow: return "snow - watch for hostiles";
                case Weather.Stormy_I: return "storm warning (level 1) - stay in unless necessary";
                case Weather.Stormy_II: return "storm warning (level 2) - going out is suicide";
                default: return "unreadable";
            }
        }

        private static string BuildFortuneLine(long seed, int dayIndex)
        {
            ModeHSeedStream stream = ModeHSeedStream.Create(seed, FortuneDomain, dayIndex);

            string[] good =
            {
                L10n.T("贴脸输出", "point-blank fire"),
                L10n.T("开箱子", "opening crates"),
                L10n.T("囤积弹药", "hoarding ammo"),
                L10n.T("与 NPC 攀谈", "chatting up NPCs"),
                L10n.T("原地发呆", "standing still and thinking"),
                L10n.T("清空仓库", "clearing out storage"),
            };
            string[] bad =
            {
                L10n.T("空手开门", "opening doors empty-handed"),
                L10n.T("背身换弹", "reloading with your back turned"),
                L10n.T("贪最后一个箱子", "greeding one last crate"),
                L10n.T("独自远行", "traveling alone"),
                L10n.T("相信自己的血量", "trusting your health bar"),
                L10n.T("裸奔", "going out unarmored"),
            };

            string g = Pick(ref stream, good);
            string b = Pick(ref stream, bad);
            return L10n.T("趣味运势（无加成）　宜：" + g + "　忌：" + b,
                "Fortune (flavor only) - Do: " + g + " / Don't: " + b);
        }

        private static string BuildGossipLine(long seed, int dayIndex)
        {
            ModeHSeedStream stream = ModeHSeedStream.Create(seed, GossipDomain, dayIndex);
            return Pick(ref stream, new string[]
            {
                L10n.T("编辑部提醒：出发前先看悬赏，顺路做，别为奖金丢掉整包战利品。",
                    "Editor's tip: check the bounty before deploying. A full loot bag is worth bringing home."),
                L10n.T("读者来信：带够弹药和药品之后，我终于不再给敌人送快递了。",
                    "Reader mail: packing enough ammo and medicine has greatly reduced my donations to the enemy."),
                L10n.T("报童便条：签到奖品先去快递站找，报箱只负责报纸。",
                    "Paperboy's note: check-in prizes go to the delivery point. This box holds the paper."),
                L10n.T("本报提醒：睡觉不会催出下一期，排字工也得慢慢干。",
                    "A reminder: sleeping won't rush the next issue. Our typesetters need their time."),
                L10n.T("仓库小贴士：处理用不上的战利品，再给下一趟留点空间。",
                    "Storage tip: sell surplus loot to make room for the next run."),
                L10n.T("调度处提示：百战留痕是选手的比赛，不计入你的日报悬赏。",
                    "Dispatch: Black Market Cup matches belong to your fighters, so they do not advance your bounties."),
            });
        }

        private static string Pick(ref ModeHSeedStream stream, string[] options)
        {
            if (options == null || options.Length <= 0) return string.Empty;
            return options[stream.NextInt(options.Length)];
        }

        #endregion
    }
}
