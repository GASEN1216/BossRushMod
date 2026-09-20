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

        private static readonly Color PaperBase = new Color(0.91f, 0.87f, 0.78f, 0.99f);
        private static readonly Color PaperRaised = new Color(0.86f, 0.82f, 0.72f, 1f);
        private static readonly Color PaperInk = new Color(0.13f, 0.11f, 0.09f, 1f);
        private static readonly Color PaperInkSoft = new Color(0.34f, 0.30f, 0.25f, 1f);
        private static readonly Color PaperRule = new Color(0.42f, 0.36f, 0.28f, 0.55f);
        private static readonly Color CellEmpty = new Color(0.78f, 0.74f, 0.65f, 1f);
        private static readonly Color CellSigned = new Color(0.28f, 0.38f, 0.23f, 1f);
        private static readonly Color CellMilestone = new Color(0.86f, 0.70f, 0.34f, 1f);
        private static readonly Color CellMilestoneDone = new Color(0.45f, 0.35f, 0.12f, 1f);
        /// <summary>标题药丸上的字色。药丸底是深绿 / 深灰，字必须是浅色。</summary>
        private static readonly Color PillInk = new Color(0.97f, 0.95f, 0.91f, 1f);
        private static readonly Color ButtonIdle = new Color(0.69f, 0.52f, 0.19f, 1f);
        private static readonly Color ButtonHover = new Color(0.78f, 0.61f, 0.26f, 1f);
        private static readonly Color ButtonDisabled = new Color(0.55f, 0.49f, 0.38f, 1f);

        #endregion

        #region 布局常量

        // 面板尺寸与底图 / 版面表同源，不在这里另写一份数字
        private const float PanelWidth = DailyReportLayoutTable.PanelWidth;
        private const float PanelHeight = DailyReportLayoutTable.PanelHeight;

        private const int HostSortingOrder = BossRushUILayers.Panel;

        #endregion

        #region 状态

        /// <summary>当前实例（单例，由 Bridge 持有）。</summary>
        public static DailyReportView Instance { get; private set; }

        private FadeGroup fadeGroup;
        private RectTransform panelRect;
        private RectTransform paperFrame;
        private TextMeshProUGUI closeText;
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

            Image overlay = root.GetComponent<Image>();
            overlay.color = BossRushUIColors.Backdrop;
            overlay.raycastTarget = true;

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
                    "第 " + issue.IssueNumber + " 期\n今日为第 " + data.DayIndex + " 天 · 进度 "
                        + displayedPercent + "%",
                    "Issue " + issue.IssueNumber + "\nDay " + data.DayIndex + " · " + displayedPercent + "%"));

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
                SetText(headlineBodyText, JoinLines(issue.StatLines));
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

        /// <summary>收益块：本日进账 + 当日进度（与报头的百分比同源）。</summary>
        private static string BuildIncomeBlock(DailyReportIssue issue, DailyReportData data)
        {
            long earned = data.Today != null ? data.Today.MoneyEarned : 0L;
            long spent = data.Today != null ? data.Today.MoneySpent : 0L;
            return L10n.T("本日进账", "Today's income") + "\n<size=30><b>" + earned + "</b></size> "
                + L10n.T("金", "cash")
                + "  <size=17>(" + L10n.T("支出 ", "spent ") + spent + ")</size>";
        }

        /// <summary>奖金块：今日悬赏奖金 + 结算时机。</summary>
        private static string BuildBountyValueBlock(DailyReportIssue issue)
        {
            return L10n.T("奖金", "Bounty") + "\n<size=30><b>" + issue.TodayBountyCash + "</b></size> "
                + L10n.T("金（下期结算）", "cash (next issue)");
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
            for (int i = 0; i < signInCells.Count; i++)
            {
                int slot = i + 1;
                Image cell = signInCells[i];
                TextMeshProUGUI label = i < signInCellLabels.Count ? signInCellLabels[i] : null;
                if (cell == null) continue;

                bool signed = slot <= data.PeriodSignedCount;
                int quality = DailyReportService.GetMilestoneQuality(data.PeriodIndex, slot);
                bool isMilestone = quality > 0;

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
                    cell.color = signed ? CellSigned : CellEmpty;
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
            SetText(closeText, L10n.T("合上报纸", "Close"));
            string[] labels = { L10n.T("未签", "Upcoming"), L10n.T("已签", "Signed"),
                L10n.T("★ 奖励格", "★ Reward"), L10n.T("奖励已领", "Claimed") };
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
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 签到点击异常: " + e.Message);
            }
        }

        private void OnCloseClicked()
        {
            try
            {
                Close();
            }
            catch (Exception)
            {
                // 关闭失败不影响玩法
            }
        }

        /// <summary>ESC / 取消键。</summary>
        protected override void OnCancel()
        {
            base.OnCancel();
            OnCloseClicked();
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

        protected override void OnClose()
        {
            base.OnClose();
            if (fadeGroup != null) fadeGroup.SkipHide();
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

        private static string JoinLines(List<string> lines)
        {
            if (lines == null || lines.Count <= 0) return string.Empty;
            string result = string.Empty;
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) result += "\n";
                result += lines[i];
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
            SetPrivateInstanceField(canvasFade, "hidingCurve", AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
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
