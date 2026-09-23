// ============================================================================
// DailyReportUI_Dashboard.cs - 日报面板的卡片式版面（DailyReportView 的 partial 续）
// ============================================================================
// owner 2026-09-20：「我想要把日报弄成和这个 100% 都一样的效果，而不是现在那样好丑都是文字。」
// 参考图 docs/testing/image-9.png。
//
// 旧版是一条可滚动的报纸：整页只有分隔线和大段文字，信息全靠读。
// 新版是仪表盘：一张**无字底图**（Assets/ui/DailyReport/daily_report_bg.png）画出
// 卡片、标题药丸与图标徽章，动态签到格、按钮和文字按同一份版面表
// （Assets/Data/DailyReportLayout.json，DailyReportLayoutTable 读）摆进去。
//
// 为什么底图与版面表必须同源：底图是程序合成的（tools/gen_daily_report_ui.py），
// 版面表由同一次运行写出，于是「卡片画在哪」与「字写在哪」永远一致；
// 改版面只改脚本重跑，不存在两处手抄坐标漂移。
//
// 硬约束：
//   - 整版保持定高：短标题在块内 autoSize，长正文在各卡片内部滚动，避免丢行；
//   - 颜色沿用本文件顶部的纸面局部配色（底图用的就是这一组），不引入第二套 token；
//   - 字体一律 ZombieModeUIHelper.CreateText + BossRushUI.ApplyGameFont（内置 Arial 渲染不了中文）。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class DailyReportView
    {
        #region 版面构建

        /// <summary>
        /// 按版面表铺一整张仪表盘。只在运行时装配调用一次。
        ///
        /// 分工（2026-09-20 第三轮收敛，避免同一处被画两遍）：
        ///   - **底图**（tools/gen_daily_report_ui.py 合成）只画不会变的装饰：
        ///     纸面、卡片、标题药丸、图标徽章、分隔线、数值块底；
        ///   - **运行时**画所有会变色 / 会响应状态的东西：签到格、签到按钮、图例色块。
        /// 两边的矩形都取自同一份版面表，所以位置天生对齐；颜色只有 C# 一个来源。
        /// </summary>
        private void BuildDashboard(RectTransform rootRect)
        {
            GameObject panel = ZombieModeUIHelper.CreateRect(
                "Paper", rootRect, new Vector2(0.5f, 0.5f),
                new Vector2(DailyReportLayoutTable.PanelWidth, DailyReportLayoutTable.PanelHeight));
            paperFrame = panel.GetComponent<RectTransform>();
            panelRect = paperFrame;

            // 底图只含固定装饰；签到格、按钮和图例色块由后续运行时控件绘制。
            Image background = panel.AddComponent<Image>();
            background.raycastTarget = true;
            Sprite sprite = DailyReportBackground.Load();
            if (sprite != null)
            {
                background.sprite = sprite;
                background.type = Image.Type.Simple;
                background.color = Color.white;
            }
            else
            {
                // 底图缺席：退回纯纸色底，文字位置不变，面板照常可用
                background.color = PaperBase;
                BossRushUI.ApplyPanelSkin(background, 12);
            }

            BuildHeader();
            BuildIncomeCard();
            BuildStatusCard();
            BuildSignInCard();
        }

        private void BuildHeader()
        {
            // 报名做成参考图那样的大号粗体压满报头（2026-09-22 第四轮：46 号在 1333 宽的报头里显得小气）
            Rect title = DailyReportLayoutTable.Get("title");
            mastheadText = CreateText("Masthead", title, 0f, 0.68f, 58f,
                TextAlignmentOptions.BottomLeft, PaperInk, false);
            mastheadText.fontStyle = FontStyles.Bold;
            mastheadText.characterSpacing = 6f;

            subtitleText = CreateText("Subtitle", title, 0.72f, 1f, 20f,
                TextAlignmentOptions.TopLeft, PaperInkSoft, false);

            Rect meta = DailyReportLayoutTable.Get("infoMeta");
            metaIssueText = CreateIconText("MetaIssue", meta, 0f, 0.5f, 20f, PaperInk);
            metaDeadlineText = CreateIconText("MetaDeadline", meta, 0.5f, 1f, 20f, PaperInk);

            Rect weather = DailyReportLayoutTable.Get("infoWeather");
            weatherText = CreateIconText("Weather", weather, 0f, 1f, 20f, PaperInk);
        }

        private void BuildIncomeCard()
        {
            incomeTitleText = CreatePillText("IncomeTitle", DailyReportLayoutTable.Get("incomePill"));

            statsText = CreateIconText("Income", DailyReportLayoutTable.Get("incomeLeft"), 0f, 1f, 22f, PaperInk);
            bountyText = CreateIconText("Bounty", DailyReportLayoutTable.Get("incomeRight"), 0f, 1f, 22f, PaperInk);

            headlineText = CreateText("Tip", DailyReportLayoutTable.Get("incomeTip"), 0f, 1f, 21f,
                TextAlignmentOptions.Left, PaperInk, true);
            // 战绩表按两列排（DailyReportView.JoinColumns），18 号三行正好装进这块，不再露半行
            Rect note = DailyReportLayoutTable.Get("incomeNote");
            headlineBodyText = CreateText("IncomeNote", new Rect(note.x + 14f, note.y + 8f, note.width - 28f, note.height - 12f),
                0f, 1f, 18f, TextAlignmentOptions.TopLeft, PaperInkSoft, true);
        }

        private void BuildStatusCard()
        {
            statusTitleText = CreatePillText("StatusTitle", DailyReportLayoutTable.Get("statusPill"));

            fortuneText = CreateIconText("Fortune", DailyReportLayoutTable.Get("statusLeft"), 0f, 1f, 20f, PaperInk);
            editorText = CreateIconText("Editor", DailyReportLayoutTable.Get("statusRight"), 0f, 1f, 20f, PaperInk);
            sideText = CreateIconText("Luck", DailyReportLayoutTable.Get("statusLuck"), 0f, 1f, 20f, PaperInk);
            gossipText = CreateIconText("Gossip", DailyReportLayoutTable.Get("statusTaboo"), 0f, 1f, 20f, PaperInkSoft);
        }

        private void BuildSignInCard()
        {
            signInTitleText = CreatePillText("SignInTitle", DailyReportLayoutTable.Get("signinPill"));

            int cells = DailyReportLayoutTable.Columns * DailyReportLayoutTable.Rows;
            if (cells > DailyReportTuning.DaysPerPeriod) cells = DailyReportTuning.DaysPerPeriod;
            for (int i = 0; i < cells; i++)
            {
                Rect cellRect = DailyReportLayoutTable.GetCell(i);
                GameObject cell = ZombieModeUIHelper.CreateRect(
                    "Cell" + (i + 1), panelRect, new Vector2(0.5f, 0.5f),
                    new Vector2(cellRect.width, cellRect.height));
                cell.GetComponent<RectTransform>().anchoredPosition = DailyReportLayoutTable.ToAnchored(cellRect);

                Image image = cell.AddComponent<Image>();
                image.color = CellEmpty;
                BossRushUI.ApplyPanelSkin(image, 6, BossRushUISkinPart.Card);
                signInCells.Add(image);

                TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                    "Label", cell.transform, string.Empty, 20f,
                    Vector2.zero, new Vector2(cellRect.width, cellRect.height),
                    TextAlignmentOptions.Center, PaperInk);
                BossRushUI.ApplyGameFont(label);
                LockFontSize(label, 20f);
                signInCellLabels.Add(label);
            }

            // 签到按钮：底图**不画**它（颜色随 idle / hover / disabled 变），
            // 这里画的这一层就是玩家看到的唯一一层。
            Rect buttonRect = DailyReportLayoutTable.Get("button");
            GameObject buttonObj = ZombieModeUIHelper.CreateRect(
                "SignIn", panelRect, new Vector2(0.5f, 0.5f),
                new Vector2(buttonRect.width, buttonRect.height));
            buttonObj.GetComponent<RectTransform>().anchoredPosition =
                DailyReportLayoutTable.ToAnchored(buttonRect);
            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = ButtonIdle;
            BossRushUI.ApplyPanelSkin(buttonImage, 10, BossRushUISkinPart.Card);
            signInButton = buttonObj.AddComponent<Button>();
            signInButton.targetGraphic = buttonImage;
            signInButton.onClick.AddListener(OnSignInClicked);
            ZombieModeUIHelper.ApplyButtonColors(signInButton, ButtonIdle, ButtonHover, ButtonDisabled);

            signInButtonText = ZombieModeUIHelper.CreateText(
                "SignInLabel", buttonObj.transform, string.Empty, 24f,
                Vector2.zero, new Vector2(buttonRect.width - 16f, buttonRect.height - 10f),
                TextAlignmentOptions.Center, BossRushUI.GetButtonTextColor(ButtonIdle));
            BossRushUI.ApplyGameFont(signInButtonText);
            LockFontSize(signInButtonText, 24f);
            signInButtonText.fontStyle = FontStyles.Bold;

            // 参考图：按钮下的期数 / 连签信息居中排，不套盒子（底图只画一条细线）
            signInStatusText = CreateText("SignInStatus", DailyReportLayoutTable.Get("sideText"), 0f, 1f, 19f,
                TextAlignmentOptions.Top, PaperInkSoft, true);

            BuildLegend();
            BuildCloseButton();
        }

        private void BuildLegend()
        {
            Rect legend = DailyReportLayoutTable.Get("legend");
            float swatch = DailyReportLayoutTable.LegendSwatch;
            float itemWidth = DailyReportLayoutTable.LegendItemWidth;
            // 顺序与 RefreshLabels 的文案一一对应：已签到 / 未签到 / 今日可签 / 奖励格 / 奖励已领
            Color[] colors = { CellSigned, CellEmpty, CellToday, CellMilestone, CellMilestoneDone };
            int count = Mathf.Min(colors.Length, DailyReportLayoutTable.LegendCount);

            // 图例色块与签到格共用同一组颜色常量：它们必须始终一致，
            // 所以只能有一个来源，底图不再烤第二份。
            for (int i = 0; i < count; i++)
            {
                Rect box = new Rect(legend.x + i * itemWidth,
                    legend.y + (legend.height - swatch) * 0.5f, swatch, swatch);
                GameObject swatchObj = ZombieModeUIHelper.CreateRect(
                    "LegendSwatch" + i, panelRect, new Vector2(0.5f, 0.5f), new Vector2(swatch, swatch));
                swatchObj.GetComponent<RectTransform>().anchoredPosition =
                    DailyReportLayoutTable.ToAnchored(box);
                Image image = swatchObj.AddComponent<Image>();
                image.color = colors[i];
                image.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(image, 4, BossRushUISkinPart.Card);

                Rect labelRect = new Rect(legend.x + i * itemWidth + swatch + 8f, legend.y,
                    itemWidth - swatch - 14f, legend.height);
                TextMeshProUGUI label = CreateText("LegendLabel" + i, labelRect, 0f, 1f, 18f,
                    TextAlignmentOptions.Left, PaperInkSoft, false);
                // 允许缩到 13 号：英文标签比中文长；框高已按一行中文行高给足（版面表 legend 高 36）
                label.fontSizeMin = 13f;
                legendLabels.Add(label);
            }
        }

        private void BuildCloseButton()
        {
            Rect signin = DailyReportLayoutTable.Get("signin");
            Rect closeRect = new Rect(
                signin.x + signin.width - 176f,
                signin.y + signin.height + 14f, 160f, 44f);

            Button close = ZombieModeUIHelper.CreateButton(
                "Close", panelRect, string.Empty,
                new Vector2(0.5f, 0.5f),
                DailyReportLayoutTable.ToAnchored(closeRect),
                new Vector2(closeRect.width, closeRect.height),
                PaperRaised, 19f, new Vector2(closeRect.width - 12f, closeRect.height - 8f),
                OnCloseClicked, true);
            if (close != null)
            {
                closeText = close.GetComponentInChildren<TextMeshProUGUI>();
                BossRushUI.ApplyGameFont(closeText);
                LockFontSize(closeText, 19f);
                if (closeText != null) closeText.color = BossRushUI.GetButtonTextColor(PaperRaised);
            }
        }

        #endregion

        #region 摆位辅助

        /// <summary>
        /// 在版面块 box 的 [topFraction, bottomFraction] 这一段里放一行文字。
        /// 分段而不是再拆一份矩形表：一块卡片里上下两行的比例是排版细节，
        /// 不该让底图脚本也知道。
        /// </summary>
        private TextMeshProUGUI CreateText(string name, Rect box, float topFraction, float bottomFraction,
            float fontSize, TextAlignmentOptions alignment, Color color, bool wrap)
        {
            Rect slice = new Rect(box.x, box.y + box.height * topFraction,
                box.width, box.height * (bottomFraction - topFraction));

            TextMeshProUGUI text = ZombieModeUIHelper.CreateText(
                name, panelRect, string.Empty, fontSize,
                DailyReportLayoutTable.ToAnchored(slice), new Vector2(slice.width, slice.height),
                alignment, color);
            BossRushUI.ApplyGameFont(text);
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Min(18f, fontSize);
            text.fontSizeMax = fontSize;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (wrap)
            {
                AllowWrap(text);
                // 长悬赏、战绩和英文正文不能丢行：只在这张卡片内部滚动，保持整版版式。
                GameObject viewport = ZombieModeUIHelper.CreateRect(name + "Viewport", panelRect,
                    new Vector2(0.5f, 0.5f), new Vector2(slice.width, slice.height));
                RectTransform viewRect = viewport.GetComponent<RectTransform>();
                viewRect.anchoredPosition = DailyReportLayoutTable.ToAnchored(slice);
                Image hit = viewport.AddComponent<Image>();
                hit.color = Color.clear;
                viewport.AddComponent<RectMask2D>();
                ScrollRect scroll = viewport.AddComponent<ScrollRect>();
                scroll.viewport = viewRect;
                scroll.content = text.rectTransform;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 8f;
                text.rectTransform.SetParent(viewRect, false);
                text.rectTransform.anchorMin = new Vector2(0f, 1f);
                text.rectTransform.anchorMax = new Vector2(1f, 1f);
                text.rectTransform.pivot = new Vector2(0.5f, 1f);
                text.rectTransform.anchoredPosition = Vector2.zero;
                // 横向 stretch 后清掉原固定宽度；否则正文会变成视口的两倍宽并被遮罩裁掉。
                // 高度交给下方 ContentSizeFitter，保留整段正文的纵向滚动。
                text.rectTransform.sizeDelta = Vector2.zero;
                text.enableAutoSizing = false;
                text.overflowMode = TextOverflowModes.Overflow;
                // 内容高度必须**按实际文本**长出来。写死成 viewport 高度
                // （sizeDelta = slice.height）会让 ScrollRect 认为内容刚好装得下，
                // Clamped 模式下一格都滚不动——控件在、事件在、就是滚不到最后一行。
                // ContentSizeFitter 让 TMP 的 preferredHeight 决定内容高度，
                // 刷新文案时由布局系统自动重算，不需要在每个 SetText 后手工同步。
                ContentSizeFitter fitter = text.gameObject.GetComponent<ContentSizeFitter>();
                if (fitter == null) fitter = text.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            else text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>带图标徽章的块：文字从徽章右侧起排（缩进量来自版面表）。</summary>
        private TextMeshProUGUI CreateIconText(string name, Rect box, float topFraction, float bottomFraction,
            float fontSize, Color color)
        {
            float indent = DailyReportLayoutTable.IconTextIndent;
            Rect inset = new Rect(box.x + indent, box.y, box.width - indent - 12f, box.height);
            TextMeshProUGUI text = CreateText(name, inset, topFraction, bottomFraction, fontSize,
                TextAlignmentOptions.Left, color, true);
            return text;
        }

        /// <summary>标题药丸上的文字：底图已经画好药丸底与左端圆标，这里只写字。</summary>
        private TextMeshProUGUI CreatePillText(string name, Rect pill)
        {
            Rect inset = new Rect(pill.x + 48f, pill.y, pill.width - 58f, pill.height);
            TextMeshProUGUI text = CreateText(name, inset, 0f, 1f, 22f,
                TextAlignmentOptions.Left, PillInk, false);
            text.fontStyle = FontStyles.Bold;
            return text;
        }

        #endregion
    }
}
