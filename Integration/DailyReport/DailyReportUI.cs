// ============================================================================
// DailyReportUI.cs - 《鸭科夫日报》阅读面板（P0/P1）
// ============================================================================
// 形态照 Integration/WishFountain/WishFountainUI.cs：继承官方 Duckov.UI.View +
// FadeGroup 运行时装配，挂在 GameplayUIManager 下。
// 选它而不是自建 Canvas 的理由：官方 View 栈自带 ESC / 焦点互斥，
// 不必像 AchievementRuntimeHooks 那样手动判 View.ActiveView == null。
//
// 遵循 AGENTS.md 4.14（tests/BossRushUISharedLibraryGuard.py 守卫）：
//   - sortingOrder 只用 BossRushUILayers 常量，不写魔法数字；
//   - 遮罩用 BossRushUIColors.Backdrop，不引入第二套黑色；
//   - 底图走 BossRushUI.ApplyPanelSkin，字体走 ZombieModeUIHelper.GetGameFont()；
//   - 不碰 CanvasScaler（宿主 GameplayUIManager 已经配好，这里只做子层）。
//
// 报纸的"泛黄纸张"是**局部配色**，不是第二套设计 token：
// ApplyPanelSkin 只给形状不给色，颜色由调用方传入，所以浅色纸面与共享库不冲突。
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
    public class DailyReportView : View
    {
        #region 纸张配色（局部，不进共享 token）

        private static readonly Color PaperBase = new Color(0.91f, 0.87f, 0.78f, 0.99f);
        private static readonly Color PaperRaised = new Color(0.86f, 0.82f, 0.72f, 1f);
        private static readonly Color PaperInk = new Color(0.13f, 0.11f, 0.09f, 1f);
        private static readonly Color PaperInkSoft = new Color(0.34f, 0.30f, 0.25f, 1f);
        private static readonly Color PaperRule = new Color(0.42f, 0.36f, 0.28f, 0.55f);
        private static readonly Color CellEmpty = new Color(0.78f, 0.74f, 0.65f, 1f);
        private static readonly Color CellSigned = new Color(0.36f, 0.46f, 0.30f, 1f);
        private static readonly Color CellMilestone = new Color(0.72f, 0.55f, 0.20f, 1f);
        private static readonly Color CellMilestoneDone = new Color(0.52f, 0.42f, 0.18f, 1f);

        #endregion

        #region 布局常量

        private const float PanelWidth = 1000f;
        private const float PanelHeight = 760f;
        private const float Margin = 28f;

        /// <summary>签到墙右侧留给签到按钮与状态的宽度。</summary>
        private const float SignInSideWidth = 250f;

        /// <summary>签到格边长。34 太小，格子里还要塞"31★"这种两位数带星。</summary>
        private const float SignInCellSize = 38f;

        /// <summary>图例行高。</summary>
        private const float LegendRowHeight = 20f;

        private const int HostSortingOrder = BossRushUILayers.Panel;

        #endregion

        #region 状态

        /// <summary>当前实例（单例，由 Bridge 持有）。</summary>
        public static DailyReportView Instance { get; private set; }

        private FadeGroup fadeGroup;
        private RectTransform panelRect;

        private TextMeshProUGUI mastheadText;
        private TextMeshProUGUI issueText;
        private TextMeshProUGUI headlineText;
        private TextMeshProUGUI headlineBodyText;
        private TextMeshProUGUI statsText;
        private TextMeshProUGUI sideText;
        private TextMeshProUGUI bountyText;
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
            view.BuildLayout(rootRect);

            root.SetActive(true);
            view.HideImmediately();
            Instance = view;
            return view;
        }

        private void BuildLayout(RectTransform rootRect)
        {
            GameObject panel = ZombieModeUIHelper.CreateRect(
                "Paper", rootRect, new Vector2(0.5f, 0.5f), new Vector2(PanelWidth, PanelHeight));
            panelRect = panel.GetComponent<RectTransform>();

            Image paper = panel.AddComponent<Image>();
            paper.color = PaperBase;
            BossRushUI.ApplyPanelSkin(paper, 12);

            float innerWidth = PanelWidth - Margin * 2f;

            // 纵向游标：每块自己申报高度，画完把游标推下去。
            // 早先是逐块手算 `top - 118f` 这类绝对偏移，任何一块改高度都要重算后面所有块，
            // 结果就是上半页空、下半页挤。用游标之后加减一块不影响别处。
            float y = PanelHeight * 0.5f - Margin;

            // ---- 报头 ----
            mastheadText = CreateBlock(
                "Masthead", L10n.T("鸭 科 夫 日 报", "THE DUCKOV DAILY"), 38f,
                innerWidth, 48f, ref y, TextAlignmentOptions.Center, PaperInk, 0f);

            issueText = CreateBlock(
                "Issue", string.Empty, 17f,
                innerWidth, 24f, ref y, TextAlignmentOptions.Center, PaperInkSoft, 2f);

            AdvanceRule(panelRect, ref y, innerWidth);

            // ---- 头条 ----
            headlineText = CreateBlock(
                "Headline", string.Empty, 28f,
                innerWidth, 38f, ref y, TextAlignmentOptions.Center, PaperInk, 4f);

            headlineBodyText = CreateBlock(
                "HeadlineBody", string.Empty, 17f,
                innerWidth, 46f, ref y, TextAlignmentOptions.Top, PaperInkSoft, 4f);
            AllowWrap(headlineBodyText);

            AdvanceRule(panelRect, ref y, innerWidth);

            // ---- 双栏：左战绩 / 右天气运势杂谈 ----
            float columnWidth = innerWidth * 0.5f - 14f;
            float columnCenterX = innerWidth * 0.25f + 7f;
            const float columnBodyHeight = 168f;

            float titleY = y - 13f;
            CreateColumnTitle("StatsTitle", L10n.T("昨 日 战 绩", "YESTERDAY"),
                -columnCenterX, titleY, columnWidth);
            CreateColumnTitle("SideTitle", L10n.T("气 象 与 杂 谈", "WEATHER & GOSSIP"),
                columnCenterX, titleY, columnWidth);
            y -= 30f;

            float bodyY = y - columnBodyHeight * 0.5f;
            statsText = CreateColumnBody("Stats", -columnCenterX, bodyY, columnWidth, columnBodyHeight);
            sideText = CreateColumnBody("Side", columnCenterX, bodyY, columnWidth, columnBodyHeight);
            y -= columnBodyHeight + 6f;

            AdvanceRule(panelRect, ref y, innerWidth);

            // ---- 悬赏栏 ----
            bountyText = CreateBlock(
                "Bounty", string.Empty, 16f,
                innerWidth, 62f, ref y, TextAlignmentOptions.TopLeft, PaperInkSoft, 4f);
            AllowWrap(bountyText);

            AdvanceRule(panelRect, ref y, innerWidth);

            // ---- 签到墙 ----
            BuildSignInGrid(panelRect, innerWidth, ref y);

            // ---- 关闭 ----
            ZombieModeUIHelper.CreateButton(
                "Close", panelRect, L10n.T("合上报纸", "Close"),
                new Vector2(0.5f, 0.5f),
                new Vector2(innerWidth * 0.5f - 70f, -PanelHeight * 0.5f + Margin + 18f),
                new Vector2(140f, 36f),
                PaperRaised, 16f, new Vector2(130f, 30f),
                OnCloseClicked, true);
        }

        /// <summary>铺一整幅宽的文本块，并把游标推到它下面。</summary>
        private TextMeshProUGUI CreateBlock(
            string name, string content, float fontSize, float width, float height,
            ref float y, TextAlignmentOptions alignment, Color color, float gapAfter)
        {
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText(
                name, panelRect, content, fontSize,
                new Vector2(0f, y - height * 0.5f), new Vector2(width, height),
                alignment, color);
            LockFontSize(text, fontSize);
            y -= height + gapAfter;
            return text;
        }

        /// <summary>画一条分隔线并把游标推过它。</summary>
        private void AdvanceRule(RectTransform parent, ref float y, float width)
        {
            y -= 5f;
            CreateRule(parent, new Vector2(0f, y), width);
            y -= 8f;
        }

        private void CreateColumnTitle(string name, string content, float x, float y, float width)
        {
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText(
                name, panelRect, content, 19f,
                new Vector2(x, y), new Vector2(width, 26f),
                TextAlignmentOptions.Center, PaperInk);
            LockFontSize(text, 19f);
        }

        private TextMeshProUGUI CreateColumnBody(string name, float x, float y, float width, float height)
        {
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText(
                name, panelRect, string.Empty, 17f,
                new Vector2(x, y), new Vector2(width, height),
                TextAlignmentOptions.TopLeft, PaperInkSoft);
            LockFontSize(text, 17f);
            AllowWrap(text);
            return text;
        }

        /// <summary>
        /// 签到墙：一期 30 格，10 列 3 行，右侧是签到按钮与状态。
        ///
        /// 格子有四种配色（未签 / 已签 / 里程碑未签 / 里程碑已领），
        /// **必须配图例**——四种土黄绿褐在纸面上彼此接近，没有图例玩家分不出
        /// 哪格是"再签就能领奖"、哪格只是"还没到"。
        /// </summary>
        private void BuildSignInGrid(RectTransform parent, float innerWidth, ref float y)
        {
            const int columns = 10;
            const int rows = 3;
            const float rowGap = 8f;

            float gridWidth = innerWidth - SignInSideWidth;
            float gapX = (gridWidth - columns * SignInCellSize) / (columns - 1);
            if (gapX < 2f) gapX = 2f;
            float startX = -innerWidth * 0.5f + SignInCellSize * 0.5f;

            float gridTop = y;

            for (int i = 0; i < DailyReportTuning.DaysPerPeriod; i++)
            {
                int row = i / columns;
                int col = i % columns;
                if (row >= rows) break;

                float x = startX + col * (SignInCellSize + gapX);
                float cellY = gridTop - SignInCellSize * 0.5f - row * (SignInCellSize + rowGap);

                GameObject cell = ZombieModeUIHelper.CreateRect(
                    "Cell" + (i + 1), parent, new Vector2(0.5f, 0.5f),
                    new Vector2(SignInCellSize, SignInCellSize));
                cell.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, cellY);

                Image img = cell.AddComponent<Image>();
                img.color = CellEmpty;
                BossRushUI.ApplyPanelSkin(img, 6);
                signInCells.Add(img);

                TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                    "Label", cell.transform, string.Empty, 15f,
                    Vector2.zero, new Vector2(SignInCellSize, SignInCellSize),
                    TextAlignmentOptions.Center, PaperInk);
                LockFontSize(label, 15f);
                signInCellLabels.Add(label);
            }

            float gridHeight = rows * SignInCellSize + (rows - 1) * rowGap;

            // 签到按钮与状态：竖向对齐到网格中线
            float rightX = innerWidth * 0.5f - SignInSideWidth * 0.5f + 10f;
            float gridMidY = gridTop - gridHeight * 0.5f;

            signInButton = ZombieModeUIHelper.CreateButton(
                "SignIn", parent, L10n.T("签 到", "CHECK IN"),
                new Vector2(0.5f, 0.5f),
                new Vector2(rightX, gridMidY + 34f),
                new Vector2(200f, 46f),
                CellMilestone, 19f, new Vector2(190f, 40f),
                OnSignInClicked, true);

            if (signInButton != null)
            {
                signInButtonText = signInButton.GetComponentInChildren<TextMeshProUGUI>();
            }

            signInStatusText = ZombieModeUIHelper.CreateText(
                "SignInStatus", parent, string.Empty, 15f,
                new Vector2(rightX, gridMidY - 30f), new Vector2(220f, 76f),
                TextAlignmentOptions.Top, PaperInkSoft);
            LockFontSize(signInStatusText, 15f);
            AllowWrap(signInStatusText);

            y = gridTop - gridHeight - 10f;

            BuildSignInLegend(parent, ref y, innerWidth);
        }

        /// <summary>四色图例：一行色块 + 说明，紧贴签到墙下沿。</summary>
        private void BuildSignInLegend(RectTransform parent, ref float y, float innerWidth)
        {
            const float swatch = 14f;
            float centerY = y - LegendRowHeight * 0.5f;
            float x = -innerWidth * 0.5f + swatch * 0.5f + 2f;

            x = AddLegendItem(parent, x, centerY, swatch, CellEmpty,
                L10n.T("未签", "Upcoming"));
            x = AddLegendItem(parent, x, centerY, swatch, CellSigned,
                L10n.T("已签", "Signed"));
            x = AddLegendItem(parent, x, centerY, swatch, CellMilestone,
                L10n.T("★ 奖励格", "★ Reward"));
            AddLegendItem(parent, x, centerY, swatch, CellMilestoneDone,
                L10n.T("奖励已领", "Claimed"));

            y -= LegendRowHeight;
        }

        /// <summary>画一个「色块 + 文字」并返回下一个条目的起始 x。</summary>
        private float AddLegendItem(
            RectTransform parent, float x, float centerY, float swatch, Color color, string label)
        {
            GameObject box = ZombieModeUIHelper.CreateRect(
                "LegendSwatch", parent, new Vector2(0.5f, 0.5f), new Vector2(swatch, swatch));
            box.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, centerY);
            Image image = box.AddComponent<Image>();
            image.color = color;
            BossRushUI.ApplyPanelSkin(image, 4);
            image.raycastTarget = false;

            const float labelWidth = 92f;
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText(
                "LegendLabel", parent, label, 14f,
                new Vector2(x + swatch * 0.5f + 6f + labelWidth * 0.5f, centerY),
                new Vector2(labelWidth, LegendRowHeight),
                TextAlignmentOptions.Left, PaperInkSoft);
            LockFontSize(text, 14f);
            text.raycastTarget = false;

            return x + swatch + 12f + labelWidth;
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

            if (open) return;
            Open();
        }

        /// <summary>按当前存档状态刷新全部文本与格子。</summary>
        public void Refresh()
        {
            try
            {
                DailyReportData data = DailyReportService.Data;
                DailyReportIssue issue = DailyReportContent.BuildCurrentIssue();
                if (data == null || issue == null) return;

                SetText(issueText, L10n.T(
                    "第 " + issue.IssueNumber + " 期　·　今日为第 " + data.DayIndex + " 天　·　当日进度 "
                        + Mathf.RoundToInt(DailyReportService.DayProgress01 * 100f) + "%",
                    "Issue " + issue.IssueNumber + "  ·  Day " + data.DayIndex + "  ·  today "
                        + Mathf.RoundToInt(DailyReportService.DayProgress01 * 100f) + "%"));

                SetText(headlineText, issue.Headline);
                SetText(headlineBodyText, issue.HeadlineBody);
                SetText(statsText, JoinLines(issue.StatLines));
                SetText(sideText, issue.WeatherLine + "\n\n" + issue.FortuneLine + "\n\n" + issue.GossipLine);
                SetText(bountyText, BuildBountyBlock(issue));

                RefreshSignInGrid(data);
                RefreshSignInButton(data);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "[WARNING] 面板刷新失败: " + e.Message);
            }
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

            result += L10n.T("【今日悬赏】", "[Today's Bounty] ") + issue.TodayBountyTitle + progress;
            if (!string.IsNullOrEmpty(issue.TodayBountyFlavor))
            {
                result += "\n" + issue.TodayBountyFlavor;
            }
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
                    cell.color = signed ? CellMilestoneDone : CellMilestone;
                }
                else
                {
                    cell.color = signed ? CellSigned : CellEmpty;
                }

                if (label == null) continue;

                // 显示的是累计天号：第 2 期第 1 格显示 31
                int display = DailyReportService.ToDisplayDayNumber(data.PeriodIndex, slot);
                label.text = isMilestone ? display + "★" : display.ToString();
                label.color = (signed || isMilestone) ? new Color(0.96f, 0.94f, 0.88f, 1f) : PaperInk;
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
            string milestoneLine = nextMilestone > 0
                ? L10n.T("距下个奖励还差 " + (nextMilestone - data.PeriodSignedCount) + " 天",
                    (nextMilestone - data.PeriodSignedCount) + " day(s) to next reward")
                : L10n.T("本期奖励已全部领取", "All rewards claimed this period");

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
                                "签到成功：累计第 " + result.DisplayDayNumber + " 天",
                                "Checked in - day " + result.DisplayDayNumber));
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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
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
            target.text = value ?? string.Empty;
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

        private static void CreateRule(RectTransform parent, Vector2 position, float width)
        {
            GameObject rule = ZombieModeUIHelper.CreateRect(
                "Rule", parent, new Vector2(0.5f, 0.5f), new Vector2(width, 2f));
            rule.GetComponent<RectTransform>().anchoredPosition = position;
            Image image = rule.AddComponent<Image>();
            image.color = PaperRule;
            image.raycastTarget = false;
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
