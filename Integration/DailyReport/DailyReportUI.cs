// ============================================================================
// DailyReportUI.cs - 《鸭科夫日报》阅读面板（P0/P1）
// ============================================================================
// 形态照 Integration/WishFountain/WishFountainUI.cs：继承官方 Duckov.UI.View +
// FadeGroup 运行时装配，挂在 GameplayUIManager 下。
// 选它而不是自建 Canvas 的理由：官方 View 栈自带 ESC / 焦点互斥，
// 报纸双栏与签到墙需要自定义排版；滚动和焦点复用官方 ScrollRect / View。
//
// 遵循 AGENTS.md 4.14（tests/BossRushUISharedLibraryGuard.py 守卫）：
//   - sortingOrder 只用 BossRushUILayers 常量，不写魔法数字；
//   - 遮罩用 BossRushUIColors.Backdrop，不引入第二套黑色；
//   - 底图走 BossRushUI.ApplyPanelSkin，字体走 ZombieModeUIHelper.GetGameFont()；
//   - 不碰 CanvasScaler（宿主 GameplayUIManager 已经配好，这里只做子层）。
//
// 报纸的"泛黄纸张"是**局部配色**，不是第二套设计 token：
// ApplyPanelSkin 只给形状不给色，颜色由调用方传入，所以浅色纸面与共享库不冲突。
//
// 2026-09-20 改版（owner：「好丑都是文字」）：版面从「可滚动的报纸长条」换成
// 「卡片仪表盘」。底图与坐标表由 tools/gen_daily_report_ui.py 一次产出，
// 摆位代码在同名 partial DailyReportUI_Dashboard.cs，本文件只留生命周期、
// 数据刷新与交互。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Duckov.UI;
using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>《鸭科夫日报》阅读面板。</summary>
    public partial class DailyReportView : View
    {
        #region 纸张配色（局部，不进共享 token）

        // 2026-09-23 第五轮按参考图 docs/testing/image-9.png 取色：纸面与底图生成器的 PAPER 同值
        // （底图缺席时的纯色兜底要和底图一个颜色），签到格是浅米色胶囊 / 深绿 / 暖黄，按钮是深金棕。
        private static readonly Color PaperBase = new Color(0.925f, 0.89f, 0.82f, 0.99f);
        private static readonly Color PaperInk = new Color(0.13f, 0.11f, 0.09f, 1f);
        private static readonly Color PaperInkSoft = new Color(0.34f, 0.30f, 0.25f, 1f);
        private static readonly Color CellEmpty = new Color(0.89f, 0.86f, 0.80f, 1f);
        private static readonly Color CellSigned = new Color(0.22f, 0.40f, 0.18f, 1f);
        private static readonly Color CellMilestone = new Color(0.86f, 0.70f, 0.34f, 1f);
        private static readonly Color CellMilestoneDone = new Color(0.45f, 0.35f, 0.12f, 1f);
        /// <summary>今天还没签时，下一格（今天要签的那一格）点亮成暖黄，参考图的「当日签到」。</summary>
        private static readonly Color CellToday = new Color(0.97f, 0.79f, 0.36f, 1f);
        /// <summary>今日可签那一格呼吸到的最亮色（两端都走深字，对比度与 CellToday 同档）。</summary>
        private static readonly Color CellTodayBright = new Color(1f, 0.87f, 0.52f, 1f);
        /// <summary>标题缎带上的字色。缎带是墨绿 / 炭灰 / 暖棕，字必须是浅色（对比度由 DailyReportPresentationGuard 复算）。</summary>
        private static readonly Color PillInk = new Color(0.97f, 0.95f, 0.91f, 1f);
        /// <summary>签到按钮：深金棕底 + 白字（L 约 0.158，白字约 4.6:1）。旧的亮金底配白字只有约 3:1。</summary>
        private static readonly Color ButtonIdle = new Color(0.53f, 0.415f, 0.215f, 1f);
        private static readonly Color ButtonDisabled = new Color(0.47f, 0.42f, 0.34f, 1f);
        /// <summary>签到按钮的暖金描边（参考图的按钮有一圈金边）。</summary>
        private static readonly Color ButtonEdge = new Color(0.86f, 0.70f, 0.34f, 0.9f);
        /// <summary>富文本里的次要墨色（标签、注脚）。预先转好，不每次刷新拼。</summary>
        private static readonly string InkSoftHex = "#" + ColorUtility.ToHtmlStringRGB(PaperInkSoft);

        #endregion

        #region 布局常量

        // 面板尺寸与底图 / 版面表同源，不在这里另写一份数字
        private const float PanelWidth = DailyReportLayoutTable.PanelWidth;
        private const float PanelHeight = DailyReportLayoutTable.PanelHeight;

        private const int HostSortingOrder = BossRushUILayers.Panel;
        /// <summary>今日可签格呼吸的半周期（秒）。</summary>
        private const float TodayPulseHalfPeriod = 0.6f;

        #endregion

        #region 状态

        /// <summary>当前实例（单例，由 Bridge 持有）。</summary>
        public static DailyReportView Instance { get; private set; }

        private FadeGroup fadeGroup;
        private RectTransform panelRect;
        private RectTransform paperFrame;
        private readonly List<TextMeshProUGUI> legendLabels = new List<TextMeshProUGUI>();
        private float nextRefreshTime;
        private bool displayedChinese;
        private int displayedDay, displayedPercent, displayedBountyProgress, displayedDeaths, displayedMinutes;
        private long displayedEarned, displayedSpent;

        private TextMeshProUGUI mastheadText;      // 报头大字
        private TextMeshProUGUI subtitleText;      // 报头副标题
        private TextMeshProUGUI metaIssueText;     // 第 N 期 · 今日为第 N 天
        private TextMeshProUGUI metaDeadlineText;  // 距离下期
        private TextMeshProUGUI weatherText;       // 天气 / 世界时间
        private TextMeshProUGUI incomeTitleText;   // 药丸：今日收益
        private TextMeshProUGUI statusTitleText;   // 药丸：今日状态
        private TextMeshProUGUI signInTitleText;   // 药丸：今日签到
        private TextMeshProUGUI headlineText;      // 黄条：今日悬赏
        private TextMeshProUGUI headlineBodyText;  // 收益卡底部：昨日战绩
        private TextMeshProUGUI statsText;         // 本日进账
        private TextMeshProUGUI sideText;          // 编辑部提醒
        private TextMeshProUGUI fortuneText;       // 趣味运势
        private TextMeshProUGUI editorText;        // 头条
        private TextMeshProUGUI gossipText;        // 杂谈
        private TextMeshProUGUI bountyText;        // 悬赏奖金
        private TextMeshProUGUI signInStatusText;
        private Button signInButton;
        private TextMeshProUGUI signInButtonText;

        private readonly List<Image> signInCells = new List<Image>();
        private readonly List<TextMeshProUGUI> signInCellLabels = new List<TextMeshProUGUI>();
        /// <summary>今天还没签时要签的那一格（呼吸提示）；已签或没有时为 null。</summary>
        private Image todayCell;
        /// <summary>卸载路径上的关闭：跳过淡出直接隐藏，宿主马上就要销毁。</summary>
        private bool closingForCleanup;

        #endregion

        #region 运行时装配

        /// <summary>运行时创建面板。parent 传 GameplayUIManager.Instance.transform。</summary>
        public static DailyReportView CreateRuntime(Transform parent)
        {
            if (parent == null) return null;

            GameObject host = new GameObject(
                "BossRush_DailyReportViewHost",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            host.transform.SetParent(parent, false);

            RectTransform hostRect = host.GetComponent<RectTransform>();
            StretchRect(hostRect);
            ConfigureHostCanvas(parent, host.GetComponent<Canvas>());

            GameObject root = new GameObject(
                "DailyReportView",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image),
                typeof(FadeGroup),
                typeof(CanvasGroupFade));
            root.transform.SetParent(host.transform, false);
            root.SetActive(false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            StretchRect(rootRect);

            // 根节点的 Image 只负责拦点击（透明）；看得见的遮罩是下面那层，走共享的暗角遮罩。
            Image overlay = root.GetComponent<Image>();
            overlay.color = Color.clear;
            overlay.raycastTarget = true;
            GameObject backdropObj = ZombieModeUIHelper.CreateRect(
                "Backdrop", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image backdrop = backdropObj.AddComponent<Image>();
            backdrop.color = BossRushUIColors.Backdrop;
            backdrop.raycastTarget = false;
            BossRushUIKit.StyleBackdrop(backdrop);

            FadeGroup fade = root.GetComponent<FadeGroup>();
            fade.manageGameObjectActive = true;
            ConfigureFadeGroup(root, fade);

            DailyReportView view = root.AddComponent<DailyReportView>();
            view.fadeGroup = fade;
            view.BuildDashboard(rootRect);

            root.SetActive(true);
            view.HideImmediately();
            Instance = view;
            return view;
        }

        #endregion

        #region 打开 / 刷新

        /// <summary>刷新内容并打开。</summary>
        public void RefreshAndOpen()
        {
            // 开面板时顺手补发上次没发成功的奖励（幂等）
            DailyReportService.TryRedeliverPendingMilestones();
            DailyReportService.TryRedeliverPendingBountyReward();

            Refresh();
            DailyReportService.ConsumeIssueBanner();

            if (open) return;
            Open();
        }

        /// <summary>按当前存档状态刷新全部文本与格子。</summary>
        public void Refresh()
        {
            try
            {
                DailyReportIssue issue = DailyReportContent.BuildCurrentIssue();
                DailyReportData data = DailyReportService.Data;
                if (data == null || issue == null) return;

                RefreshLabels();
                displayedChinese = L10n.IsChinese;
                displayedDay = data.DayIndex;
                displayedPercent = Mathf.RoundToInt(DailyReportService.DayProgress01 * 100f);
                displayedBountyProgress = issue.TodayBountyProgress;
                displayedDeaths = data.Today != null ? data.Today.Deaths : 0;
                displayedEarned = data.Today != null ? data.Today.MoneyEarned : 0L;
                displayedSpent = data.Today != null ? data.Today.MoneySpent : 0L;
                displayedMinutes = DailyReportService.GetRemainingPlayMinutes();

                SetText(metaIssueText, L10n.T(
                    "第 " + issue.IssueNumber + " 期 · 今日为第 " + data.DayIndex + " 天\n本期进度 "
                        + displayedPercent + "%",
                    "Issue " + issue.IssueNumber + " · Day " + data.DayIndex + "\nProgress " + displayedPercent + "%"));

                SetText(metaDeadlineText, displayedMinutes < 0
                    ? L10n.T("日报时钟已停", "Daily clock stopped")
                    : displayedMinutes == 0
                    ? L10n.T("等待出刊结算", "Awaiting settlement")
                    : L10n.T("距离下期 " + displayedMinutes + " 分钟",
                        "Next issue in " + displayedMinutes + " min"));

                SetText(weatherText, issue.WeatherLine);
                SetText(statsText, BuildIncomeBlock(issue, data));
                SetText(bountyText, BuildBountyValueBlock(issue));
                SetText(headlineText, BuildBountyBlock(issue));
                SetText(headlineBodyText, JoinColumns(issue.StatLines));
                SetText(fortuneText, issue.FortuneLine);
                SetText(editorText, issue.Headline);
                SetText(sideText, issue.HeadlineBody);
                SetText(gossipText, issue.GossipLine);

                RefreshSignInGrid(data);
                RefreshSignInButton(data);
                FitPaper();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 面板刷新失败: " + e.Message);
            }
        }

        /// <summary>
        /// 收益块三级字号：小号次要色标签 / 大号粗体数字（千分位）/ 小号注脚。
        /// 参考图的核心是「一眼看到大数字」；字号用百分比，块放不下时 autoSize 整块等比缩（UA-06）。
        /// </summary>
        private static string BuildIncomeBlock(DailyReportIssue issue, DailyReportData data)
        {
            long earned = data.Today != null ? data.Today.MoneyEarned : 0L;
            long spent = data.Today != null ? data.Today.MoneySpent : 0L;
            return BuildValueBlock(L10n.T("本日进账", "Today's income"), earned,
                L10n.T("支出 ", "Spent ") + FormatCash(spent));
        }

        /// <summary>奖金块：今日悬赏奖金 + 结算时机，与收益块同一套三级字号。</summary>
        private static string BuildBountyValueBlock(DailyReportIssue issue)
        {
            return BuildValueBlock(L10n.T("奖金", "Bounty"), issue.TodayBountyCash,
                L10n.T("下期结算", "Paid next issue"));
        }

        private static string BuildValueBlock(string label, long value, string note)
        {
            return "<size=75%><color=" + InkSoftHex + ">" + label + "</color></size>\n"
                + "<size=200%><b>" + FormatCash(value) + "</b></size><size=85%> " + L10n.T("金", "cash") + "</size>\n"
                + "<size=70%><color=" + InkSoftHex + ">" + note + "</color></size>";
        }

        /// <summary>千分位；固定用不变区域，免得系统区域把分隔符换成点或空格。</summary>
        private static string FormatCash(long value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string BuildBountyBlock(DailyReportIssue issue)
        {
            string result = string.Empty;
            if (!string.IsNullOrEmpty(issue.BountyResultLine))
            {
                result += issue.BountyResultLine + "\n";
            }

            string progress = issue.TodayBountyTarget > 0
                ? "（" + issue.TodayBountyProgress + "/" + issue.TodayBountyTarget + "）"
                : string.Empty;

            result += L10n.T("【今日悬赏】", "[Today's Bounty] ") + issue.TodayBountyTitle + progress
                + L10n.T(" · 奖金 " + issue.TodayBountyCash + " 金（下期结算）",
                    " · " + issue.TodayBountyCash + " cash (next issue)");
            if (!string.IsNullOrEmpty(issue.TodayBountyStatus)) result += "\n" + issue.TodayBountyStatus;
            return result;
        }

        private void RefreshSignInGrid(DailyReportData data)
        {
            bool signedToday = DailyReportService.IsSignedToday;
            todayCell = null;
            for (int i = 0; i < signInCells.Count; i++)
            {
                int slot = i + 1;
                Image cell = signInCells[i];
                TextMeshProUGUI label = i < signInCellLabels.Count ? signInCellLabels[i] : null;
                if (cell == null) continue;

                bool signed = slot <= data.PeriodSignedCount;
                int quality = DailyReportService.GetMilestoneQuality(data.PeriodIndex, slot);
                bool isMilestone = quality > 0;
                bool isToday = !signedToday && slot == data.PeriodSignedCount + 1;

                if (isMilestone)
                {
                    // 里程碑「已领」配色以实际领取掩码为准，不能只看签没签：
                    // 发奖失败待补发的格子若显示成已领，图例就在撒谎。
                    cell.color = DailyReportService.IsMilestoneClaimed(data, slot)
                        ? CellMilestoneDone
                        : CellMilestone;
                }
                else
                {
                    cell.color = signed ? CellSigned : (isToday ? CellToday : CellEmpty);
                    if (isToday) todayCell = cell;
                }

                if (label == null) continue;

                // 显示的是累计天号：第 2 期第 1 格显示 31
                int display = DailyReportService.ToDisplayDayNumber(data.PeriodIndex, slot);
                label.text = isMilestone ? display + "★" : display.ToString();
                label.color = BossRushUI.GetButtonTextColor(cell.color);
            }
        }

        private void RefreshSignInButton(DailyReportData data)
        {
            bool signed = DailyReportService.IsSignedToday;

            if (signInButton != null)
            {
                signInButton.interactable = !signed;
            }
            if (signInButtonText != null)
            {
                signInButtonText.text = signed
                    ? L10n.T("今日已签", "SIGNED")
                    : L10n.T("签 到", "CHECK IN");
            }

            int nextMilestone = FindNextMilestoneSlot(data);
            int pendingCount = data.PendingMilestones != null ? data.PendingMilestones.Count : 0;
            string milestoneLine = pendingCount > 0
                ? L10n.T("待补发奖品 " + pendingCount + " 件，再次打开重试",
                    pendingCount + " prize(s) pending; reopen to retry")
                : nextMilestone > 0
                ? L10n.T("再签 " + (nextMilestone - data.PeriodSignedCount) + " 天：品质 "
                    + DailyReportService.GetMilestoneQuality(data.PeriodIndex, nextMilestone),
                    (nextMilestone - data.PeriodSignedCount) + " check-ins to Q"
                    + DailyReportService.GetMilestoneQuality(data.PeriodIndex, nextMilestone))
                : L10n.T("本期奖励格已签满", "All reward slots signed");

            SetText(signInStatusText, L10n.T(
                "第 " + data.PeriodIndex + " 期　" + data.PeriodSignedCount + "/"
                    + DailyReportTuning.DaysPerPeriod + "\n连续签到 " + data.Streak
                    + " 天　累计 " + data.TotalSignedDays + " 天\n" + milestoneLine,
                "Period " + data.PeriodIndex + "  " + data.PeriodSignedCount + "/"
                    + DailyReportTuning.DaysPerPeriod + "\nStreak " + data.Streak
                    + "  Total " + data.TotalSignedDays + "\n" + milestoneLine));
        }

        /// <summary>本期下一个尚未抵达的里程碑格位；没有则返回 0。</summary>
        private static int FindNextMilestoneSlot(DailyReportData data)
        {
            for (int slot = data.PeriodSignedCount + 1; slot <= DailyReportTuning.DaysPerPeriod; slot++)
            {
                if (DailyReportService.GetMilestoneQuality(data.PeriodIndex, slot) > 0) return slot;
            }
            return 0;
        }

        #endregion

        // 只在打开时以一秒频率检查变化；正文、天气与格子仅在值变化时重建。
        private void Update()
        {
            if (!open) return;
            PulseTodayCell();
            if (displayedChinese == L10n.IsChinese
                && (BossRushUI.IsGamePaused() || Time.unscaledTime < nextRefreshTime)) return;
            nextRefreshTime = Time.unscaledTime + 1f;
            DailyReportData data = DailyReportService.Data;
            if (data == null) return;
            if (displayedChinese != L10n.IsChinese || displayedDay != data.DayIndex
                || displayedPercent != Mathf.RoundToInt(DailyReportService.DayProgress01 * 100f)
                || displayedBountyProgress != DailyReportService.GetActiveBountyProgress()
                || displayedDeaths != (data.Today != null ? data.Today.Deaths : 0)
                || displayedEarned != (data.Today != null ? data.Today.MoneyEarned : 0L)
                || displayedSpent != (data.Today != null ? data.Today.MoneySpent : 0L)
                || displayedMinutes != DailyReportService.GetRemainingPlayMinutes()) Refresh();
        }

        /// <summary>
        /// 今日可签那一格在 CellToday 与 CellTodayBright 之间呼吸（1.2 秒一周期，SmoothStep 缓动），
        /// 提示「今天还能签」。走 unscaled 时间，与帧率无关；暂停时不动。只改这一格的颜色。
        /// </summary>
        private void PulseTodayCell()
        {
            if (todayCell == null || BossRushUI.IsGamePaused()) return;
            float wave = BossRushUI.SmoothStep(Mathf.PingPong(Time.unscaledTime / TodayPulseHalfPeriod, 1f));
            todayCell.color = Color.Lerp(CellToday, CellTodayBright, wave);
        }

        /// <summary>签到成功：刚签的那一格从下方淡入落位，像盖了一个章（共享入场动画）。</summary>
        private void PlaySignedStamp(DailyReportData data)
        {
            if (data == null) return;
            int index = data.PeriodSignedCount - 1;
            if (index < 0 || index >= signInCells.Count || signInCells[index] == null) return;
            BossRushUIEntranceAnimation.Play(signInCells[index].gameObject, 0f, 0.28f, 10f);
        }

        private void FitPaper()
        {
            RectTransform root = transform as RectTransform;
            if (root == null || paperFrame == null || root.rect.width <= 0f || root.rect.height <= 0f) return;
            float scale = Mathf.Min(1f, Mathf.Min((root.rect.width - 24f) / PanelWidth,
                (root.rect.height - 24f) / PanelHeight));
            Vector3 target = Vector3.one * Mathf.Max(0.1f, scale);
            if (paperFrame.localScale != target) paperFrame.localScale = target;
        }

        private void OnRectTransformDimensionsChange() { FitPaper(); }

        private void RefreshLabels()
        {
            SetText(mastheadText, L10n.T("鸭科夫日报", "THE DUCKOV DAILY"));
            SetText(subtitleText, L10n.T("D U C K   N E W S", "D U C K   N E W S"));
            SetText(incomeTitleText, L10n.T("今日收益", "TODAY'S INCOME"));
            SetText(statusTitleText, L10n.T("鸭科夫 · 今日状态", "DUCKOV · TODAY"));
            SetText(signInTitleText, L10n.T("今日签到", "CHECK-IN"));
            // 与 BuildLegend 的色块顺序一一对应
            string[] labels = { L10n.T("已签到", "Signed"), L10n.T("未签到", "Upcoming"),
                L10n.T("今日可签", "Today"), L10n.T("★ 奖励格", "★ Reward"), L10n.T("奖励已领", "Claimed") };
            for (int i = 0; i < legendLabels.Count && i < labels.Length; i++) SetText(legendLabels[i], labels[i]);
        }

        #region 交互

        private void OnSignInClicked()
        {
            try
            {
                DailyReportSignInResult result = DailyReportService.SignInAndClaim();

                switch (result.Outcome)
                {
                    case DailyReportSignInOutcome.Success:
                        if (result.HitMilestone && result.MilestoneQuality > 0)
                        {
                            ShowBanner(L10n.T(
                                "签到成功！品质 " + result.MilestoneQuality + " 奖品已寄往快递站",
                                "Checked in! A quality-" + result.MilestoneQuality
                                    + " reward was sent to your delivery point"));
                        }
                        else
                        {
                            ShowBanner(L10n.T(
                                DailyReportService.Data.PendingMilestones.Exists(debt => debt.IsDaily && debt.SignDayIndex == DailyReportService.Data.DayIndex)
                                    ? "签到成功，小礼待补发；再次打开报纸重试" : "签到成功，小礼已寄往快递站",
                                DailyReportService.Data.PendingMilestones.Exists(debt => debt.IsDaily && debt.SignDayIndex == DailyReportService.Data.DayIndex)
                                    ? "Checked in; daily gift pending. Reopen to retry." : "Checked in! Daily gift sent to your delivery point."));
                        }
                        break;

                    case DailyReportSignInOutcome.AlreadySigned:
                        ShowBanner(L10n.T("今天已经签过了", "Already checked in today"));
                        break;

                    case DailyReportSignInOutcome.PersistBlocked:
                        ShowBanner(L10n.T("存档暂不可写，签到未受理", "Save is not writable; check-in refused"));
                        break;
                }

                Refresh();
                if (result.Outcome == DailyReportSignInOutcome.Success) PlaySignedStamp(DailyReportService.Data);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 签到点击异常: " + e.Message);
            }
        }

        private static void ShowBanner(string text)
        {
            try
            {
                if (ModBehaviour.Instance != null) ModBehaviour.Instance.ShowBigBanner(text);
            }
            catch (Exception)
            {
                // 提示失败不影响主流程
            }
        }

        #endregion

        #region View 生命周期

        internal static void CleanupRuntime()
        {
            DailyReportView view = Instance;
            if (view == null) return;
            view.closingForCleanup = true;
            try { view.Close(); }
            finally
            {
                // Host Canvas 也属于日报，不能在卸载后留在 GameplayUIManager 上。
                GameObject host = view.transform.parent != null ? view.transform.parent.gameObject : view.gameObject;
                Instance = null;
                Destroy(host);
            }
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            if (transform.parent != null) transform.parent.SetAsLastSibling();
            transform.SetAsLastSibling();
            if (fadeGroup != null) fadeGroup.Show();
        }

        /// <summary>
        /// 关闭与打开对称：走官方 FadeGroup 的 0.18 秒淡出（UA-08，旧版一帧消失）。
        /// 卸载路径上宿主马上被销毁，不等淡出，直接隐藏。
        /// </summary>
        protected override void OnClose()
        {
            base.OnClose();
            if (fadeGroup == null) return;
            if (closingForCleanup) fadeGroup.SkipHide();
            else fadeGroup.Hide();
        }

        private void HideImmediately()
        {
            if (fadeGroup != null) fadeGroup.SkipHide();
        }

        protected override void OnDestroy()
        {
            try { base.OnDestroy(); }
            finally
            {
                if (Instance == this) Instance = null;
            }
        }

        #endregion

        #region 装配辅助

        /// <summary>
        /// 关掉自动缩放并改用截断而不是省略号。
        /// ZombieModeUIHelper.CreateTMPText 默认开 autoSizing + Ellipsis，
        /// 那套默认适合短标签，报纸正文用会被缩字并加省略号。
        /// </summary>
        private static void LockFontSize(TextMeshProUGUI text, float size)
        {
            if (text == null) return;
            text.enableAutoSizing = false;
            text.fontSize = size;
            text.overflowMode = TextOverflowModes.Truncate;
        }

        private static void AllowWrap(TextMeshProUGUI text)
        {
            if (text == null) return;
            text.enableWordWrapping = true;
            text.margin = new Vector4(6f, 2f, 6f, 2f);
        }

        private static void SetText(TextMeshProUGUI target, string value)
        {
            if (target == null) return;
            value = value ?? string.Empty;
            if (target.text == value) return;
            target.text = value;
            ScrollRect scroll = target.GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.content == target.rectTransform && scroll.viewport != null)
            {
                float height = Mathf.Max(scroll.viewport.rect.height,
                    target.GetPreferredValues(value, scroll.viewport.rect.width, float.PositiveInfinity).y + 8f);
                target.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                scroll.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>
        /// 战绩小表两列排：每两条并成一行，右列用 TMP 的 pos 标签对齐到半宽处。
        /// 五六条战绩一条一行要五六行，那块只放得下三行多，末行总是露半截（2026-09-22 实测）。
        /// </summary>
        private static string JoinColumns(List<string> lines)
        {
            if (lines == null || lines.Count <= 0) return string.Empty;
            string result = string.Empty;
            for (int i = 0; i < lines.Count; i += 2)
            {
                if (i > 0) result += "\n";
                result += lines[i];
                if (i + 1 < lines.Count) result += "<pos=50%>" + lines[i + 1];
            }
            return result;
        }

        private static void StretchRect(RectTransform rect)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void ConfigureHostCanvas(Transform parent, Canvas hostCanvas)
        {
            if (hostCanvas == null) return;

            Canvas parentCanvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            if (parentCanvas != null)
            {
                hostCanvas.renderMode = parentCanvas.renderMode;
                hostCanvas.worldCamera = parentCanvas.worldCamera;
                hostCanvas.planeDistance = parentCanvas.planeDistance;
                hostCanvas.sortingLayerID = parentCanvas.sortingLayerID;
            }

            hostCanvas.overrideSorting = true;
            hostCanvas.sortingOrder = parentCanvas != null
                ? Mathf.Max(HostSortingOrder, parentCanvas.sortingOrder + 20)
                : HostSortingOrder;
        }

        private static void ConfigureFadeGroup(GameObject root, FadeGroup fadeGroup)
        {
            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            CanvasGroupFade canvasFade = root.GetComponent<CanvasGroupFade>();
            ConfigureCanvasGroupFade(canvasFade, canvasGroup);

            FieldInfo fadeElementsField = typeof(FadeGroup).GetField(
                "fadeElements", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fadeElementsField != null)
            {
                fadeElementsField.SetValue(fadeGroup, new List<FadeElement> { canvasFade });
            }
        }

        private static void ConfigureCanvasGroupFade(CanvasGroupFade canvasFade, CanvasGroup canvasGroup)
        {
            if (canvasFade == null || canvasGroup == null) return;

            SetPrivateInstanceField(canvasFade, "canvasGroup", canvasGroup);
            SetPrivateInstanceField(canvasFade, "showingCurve", AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
            // 官方 FadeTask 用曲线作为 Lerp(beginAlpha, targetAlpha, progress) 的进度。
            // 淡出目标已经是 0；曲线仍须 0 -> 1，否则会先透明、再亮起、最后突然隐藏。
            SetPrivateInstanceField(canvasFade, "hidingCurve", AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
            SetPrivateInstanceField(canvasFade, "fadeDuration", 0.18f);
            SetPrivateInstanceField(canvasFade, "manageBlockRaycast", true);

            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }

        private static void SetPrivateInstanceField(object target, string fieldName, object value)
        {
            if (target == null) return;
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) field.SetValue(target, value);
        }

        #endregion
    }
}
