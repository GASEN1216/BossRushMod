// ============================================================================
// DailyReportUI_Dashboard.cs - 日报面板的卡片式版面（DailyReportView 的 partial 续）
// ============================================================================
// owner 2026-09-20：「我想要把日报弄成和这个 100% 都一样的效果，而不是现在那样好丑都是文字。」
// 参考图 docs/testing/image-9.png。
//
// 旧版是一条可滚动的报纸：整页只有分隔线和大段文字，信息全靠读。
// 新版是仪表盘：一张**无字底图**（Assets/ui/DailyReport/daily_report_bg.png）画出
// 纸面、卡片投影、斜切缎带与分隔线；图标、吉祥物、签到格、按钮和文字按同一份版面表
// （Assets/Data/DailyReportLayout.json，DailyReportLayoutTable 读）摆进去。
//
// 为什么底图与版面表必须同源：底图是程序合成的（tools/gen_daily_report_ui.py），
// 版面表由同一次运行写出，于是「卡片画在哪」与「字写在哪」永远一致；
// 改版面只改脚本重跑，不存在两处手抄坐标漂移。
//
// 2026-09-23 第五轮（owner：「总体差不多但还是有差距」）：
//   - 图标与吉祥物是独立 Sprite（底图进包被压到 1024 宽再放大，烤进去的小图标会糊成灰块），
//     位置取版面表 "icons"；取不到就不画那一格、文字也不缩进，不退回汉字或灰方块；
//   - 签到格改成程序化圆角胶囊 + 共享投影（BossRushUIDepth），不再用图集卡片图（它烤进去的内描边
//     在浅色乘色下会显出一圈框）；图例色块改成圆点；
//   - 数值块不再滚动，标签 / 大号数字 / 注脚三级字号；卡片内正文在块内垂直居中。
//
// 硬约束：
//   - 整版保持定高：悬赏独占加高区域并完整换行，其余长正文在卡片内部滚动；
//   - 颜色沿用 DailyReportUI.cs 顶部的纸面局部配色（底图用的就是这一组），不引入第二套 token；
//   - 字体一律 ZombieModeUIHelper.CreateText + BossRushUI.ApplyGameFont（内置 Arial 渲染不了中文）；
//   - 卡片投影烤在底图里，面板本身**不**走 ApplyPanelStroke / ApplyFramedPanelSkin，
//     否则共享的 Depth_Shadow 会在烤好的投影外再叠一圈。运行时共享投影只给运行时画的签到格与按钮。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class DailyReportView
    {
        #region 版面常量

        /// <summary>期数块上 55% 写两行（期数 · 天数 / 本期进度），下 45% 写距离下期；生成器按同一比例摆两枚图标。</summary>
        private const float MetaSplit = 0.55f;
        /// <summary>签到格圆角。≥ 共享投影能取到的弧半径（注入图集时卡片档 14），投影的洞才不会压到格子四角。</summary>
        private const int CellRadius = 14;
        /// <summary>签到按钮圆角。</summary>
        private const int ButtonRadius = 12;

        #endregion

        #region 版面构建

        /// <summary>
        /// 按版面表铺一整张仪表盘。只在运行时装配调用一次。
        ///
        /// 分工（2026-09-20 第三轮收敛，避免同一处被画两遍）：
        ///   - **底图**（tools/gen_daily_report_ui.py 合成）只画不会变的装饰：
        ///     纸面、卡片与投影、标题缎带、分隔线、悬赏提示条；
        ///   - **运行时**画图标、吉祥物，以及所有会变色 / 会响应状态的东西：签到格、签到按钮、图例色块。
        /// 两边的矩形都取自同一份版面表，所以位置天生对齐；颜色只有 C# 一个来源。
        /// </summary>
        private void BuildDashboard(RectTransform rootRect)
        {
            GameObject panel = ZombieModeUIHelper.CreateRect(
                "Paper", rootRect, new Vector2(0.5f, 0.5f),
                new Vector2(DailyReportLayoutTable.PanelWidth, DailyReportLayoutTable.PanelHeight));
            paperFrame = panel.GetComponent<RectTransform>();
            panelRect = paperFrame;

            // 底图只含固定装饰；图标、签到格、按钮和图例色块由后续运行时控件绘制。
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
            // 吉祥物：整只鸭压在报头左侧（参考图口径，不再套圆框）
            CreateArt("Mascot", DailyReportBackground.LoadArt(DailyReportBackground.MascotFile),
                DailyReportLayoutTable.Get("mascot"), panelRect);

            // 报名做成参考图那样的大号粗体压满报头，「— DUCK NEWS —」居中排在下面（两侧横线烤在底图里）
            Rect title = DailyReportLayoutTable.Get("title");
            mastheadText = CreateText("Masthead", title, 0f, 0.70f, 72f,
                TextAlignmentOptions.Bottom, PaperInk, false);
            mastheadText.fontStyle = FontStyles.Bold;
            mastheadText.characterSpacing = 8f;

            subtitleText = CreateText("Subtitle", title, 0.76f, 1f, 18f,
                TextAlignmentOptions.Center, PaperInkSoft, false);

            Rect meta = DailyReportLayoutTable.Get("infoMeta");
            metaIssueText = CreateIconText("MetaIssue", meta, 0f, MetaSplit, 19f, PaperInk, "issue", true);
            metaDeadlineText = CreateIconText("MetaDeadline", meta, MetaSplit, 1f, 19f, PaperInk, "deadline", true);

            Rect weather = DailyReportLayoutTable.Get("infoWeather");
            weatherText = CreateIconText("Weather", weather, 0f, 1f, 19f, PaperInk, "weather", true);
        }

        private void BuildIncomeCard()
        {
            incomeTitleText = CreatePillText("IncomeTitle", DailyReportLayoutTable.Get("incomePill"), "ribbon_income");

            // 数值块只有「标签 / 大号数字 / 注脚」三行，不需要滚动：关掉换行走 autoSize，
            // 富文本里的字号用百分比，数字太长时整块等比缩，不会被截成半个数字。
            statsText = CreateIconText("Income", DailyReportLayoutTable.Get("incomeLeft"), 0f, 1f, 22f, PaperInk, "income", false);
            bountyText = CreateIconText("Bounty", DailyReportLayoutTable.Get("incomeRight"), 0f, 1f, 22f, PaperInk, "bounty", false);

            // 撤掉关闭按钮后，版面表把空出的高度给悬赏；全文直接显示，滚轮不再移动悬赏。
            headlineText = CreateIconText("Tip", DailyReportLayoutTable.Get("incomeTip"), 0f, 1f, 20f, PaperInk, "tip", true, false);
            // 战绩表按两列排（DailyReportView.JoinColumns），18 号三行正好装进这块，不再露半行
            Rect note = DailyReportLayoutTable.Get("incomeNote");
            headlineBodyText = CreateText("IncomeNote", new Rect(note.x + 10f, note.y + 4f, note.width - 20f, note.height - 6f),
                0f, 1f, 18f, TextAlignmentOptions.TopLeft, PaperInkSoft, true);
        }

        private void BuildStatusCard()
        {
            statusTitleText = CreatePillText("StatusTitle", DailyReportLayoutTable.Get("statusPill"), "ribbon_status");

            // 四行从上到下：头条（粗体）→ 头条正文 → 趣味运势 → 编辑部便条。
            editorText = CreateIconText("Editor", DailyReportLayoutTable.Get("statusLeft"), 0f, 1f, 20f, PaperInk, "headline", true);
            editorText.fontStyle = FontStyles.Bold;
            sideText = CreateIconText("Luck", DailyReportLayoutTable.Get("statusRight"), 0f, 1f, 19f, PaperInkSoft, "broadcast", true);
            fortuneText = CreateIconText("Fortune", DailyReportLayoutTable.Get("statusLuck"), 0f, 1f, 19f, PaperInk, "fortune", true);
            gossipText = CreateIconText("Gossip", DailyReportLayoutTable.Get("statusTaboo"), 0f, 1f, 19f, PaperInkSoft, "gossip", true);
        }

        private void BuildSignInCard()
        {
            signInTitleText = CreatePillText("SignInTitle", DailyReportLayoutTable.Get("signinPill"), "ribbon_signin");

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
                // 圆角胶囊：Hairline 档一律程序化圆角，不吃图集——图集卡片图 panel_raised 烤进去的内描边
                // 在浅色乘色下会显出一圈框，30 格排出来就是「灰方块墙」（审美审查 UA-07）。
                BossRushUI.ApplyPanelSkin(image, CellRadius, BossRushUISkinPart.Hairline);
                // 格子是运行时画的，底图里没有它的投影；这里挂共享的柔和投影 + 顶边高光，与参考图的立体胶囊一致。
                BossRushUIDepth.ApplySurfaceDepth(image, CellRadius, BossRushUISkinPart.Card);
                signInCells.Add(image);

                TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                    "Label", cell.transform, string.Empty, 20f,
                    Vector2.zero, new Vector2(cellRect.width, cellRect.height),
                    TextAlignmentOptions.Center, PaperInk);
                BossRushUI.ApplyGameFont(label);
                LockFontSize(label, 20f);
                label.fontStyle = FontStyles.Bold;
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
            BossRushUI.ApplyPanelSkin(buttonImage, ButtonRadius, BossRushUISkinPart.Button);
            signInButton = buttonObj.AddComponent<Button>();
            signInButton.targetGraphic = buttonImage;
            signInButton.onClick.AddListener(OnSignInClicked);
            // 悬停色走共享口径（白字底提亮会跌破 4.5:1 时改压暗），按钮手感、音效与投影由 ApplyButtonColors 挂上
            ZombieModeUIHelper.ApplyButtonColors(signInButton, ButtonIdle, BossRushUI.GetHoverColor(ButtonIdle), ButtonDisabled);
            // 参考图的签到按钮有一圈暖金边
            BossRushUI.ApplyPanelStroke(buttonImage, ButtonRadius, BossRushUISkinPart.Button, ButtonEdge);

            // 礼盒图标压在按钮文字左侧；取不到图标时文字照常居中
            float labelShift = 0f;
            Rect giftRect = DailyReportLayoutTable.GetIcon("gift");
            if (CreateArt("Gift", DailyReportBackground.LoadIcon("gift"), giftRect, buttonObj.transform) != null)
            {
                labelShift = 18f;
            }

            signInButtonText = ZombieModeUIHelper.CreateText(
                "SignInLabel", buttonObj.transform, string.Empty, 24f,
                new Vector2(labelShift, 0f), new Vector2(buttonRect.width - 16f - labelShift * 2f, buttonRect.height - 10f),
                TextAlignmentOptions.Center, BossRushUI.GetButtonTextColor(ButtonIdle));
            BossRushUI.ApplyGameFont(signInButtonText);
            LockFontSize(signInButtonText, 24f);
            signInButtonText.fontStyle = FontStyles.Bold;

            // 参考图：按钮下的期数 / 连签信息居中排，不套盒子（首行两侧的细线烤在底图里）
            signInStatusText = CreateText("SignInStatus", DailyReportLayoutTable.Get("sideText"), 0f, 1f, 19f,
                TextAlignmentOptions.Top, PaperInkSoft, true);

            BuildLegend();
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
                // 圆点（参考图口径）：半径取色块边长一半，Hairline 档程序化，不会被图集 border 压成歪圆
                BossRushUI.ApplyPanelSkin(image, Mathf.Max(1, Mathf.RoundToInt(swatch * 0.5f)), BossRushUISkinPart.Hairline);

                Rect labelRect = new Rect(legend.x + i * itemWidth + swatch + 8f, legend.y,
                    itemWidth - swatch - 14f, legend.height);
                TextMeshProUGUI label = CreateText("LegendLabel" + i, labelRect, 0f, 1f, 18f,
                    TextAlignmentOptions.Left, PaperInkSoft, false);
                // 允许缩到 13 号：英文标签比中文长；框高已按一行中文行高给足（版面表 legend 高 36）
                label.fontSizeMin = 13f;
                legendLabels.Add(label);
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
            float fontSize, TextAlignmentOptions alignment, Color color, bool wrap, bool scrollBody = true)
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
            if (wrap) AllowWrap(text);
            if (wrap && scrollBody)
            {
                // 战绩和英文正文在各自卡片内滚动，悬赏由独立加高区域容纳全文。
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
                // 与共享 ConfigureScrollRect 同一灵敏度；8 的时候滚一格只动 8 个单位，手感发黏（UA-09）
                scroll.scrollSensitivity = 32f;
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
                // 内容**至少**与视口等高：短正文按自身的垂直对齐（Left = 居中）落在块中间，
                // 与左侧图标对齐；长正文照常按实际高度长出来、可以滚。
                // （PreferredSize 取 max(minHeight, preferredHeight)，LayoutElement 优先级高于 TMP。）
                LayoutElement minimum = text.gameObject.GetComponent<LayoutElement>();
                if (minimum == null) minimum = text.gameObject.AddComponent<LayoutElement>();
                minimum.minHeight = slice.height;
            }
            else
            {
                text.enableWordWrapping = wrap;
                if (wrap)
                {
                    // 中英悬赏保留所有行，长英文按整块缩至至少 16 号，不截断为省略号。
                    text.fontSizeMin = 16f;
                    text.overflowMode = TextOverflowModes.Overflow;
                }
            }
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 带图标的块：先按版面表摆图标，文字从图标右沿 + 间距起排。
        /// 图标取不到（包里没有这张图）就不画那一格，文字也不缩进——不退回汉字或灰方块。
        /// </summary>
        private TextMeshProUGUI CreateIconText(string name, Rect box, float topFraction, float bottomFraction,
            float fontSize, Color color, string iconId, bool wrap, bool scrollBody = true)
        {
            float indent = 8f;
            Rect icon = DailyReportLayoutTable.GetIcon(iconId);
            if (CreateArt("Icon_" + iconId, DailyReportBackground.LoadIcon(iconId), icon, panelRect) != null)
            {
                indent = icon.xMax + DailyReportLayoutTable.IconTextGap - box.x;
            }
            Rect inset = new Rect(box.x + indent, box.y, box.width - indent - 8f, box.height);
            return CreateText(name, inset, topFraction, bottomFraction, fontSize,
                TextAlignmentOptions.Left, color, wrap, scrollBody);
        }

        /// <summary>标题缎带上的文字：底图已经画好缎带，这里摆缎带头上的图标并写字。</summary>
        private TextMeshProUGUI CreatePillText(string name, Rect pill, string iconId)
        {
            float indent = 18f;
            if (CreateArt("Icon_" + iconId, DailyReportBackground.LoadIcon(iconId),
                    DailyReportLayoutTable.GetIcon(iconId), panelRect) != null)
            {
                indent = DailyReportLayoutTable.PillTextIndent;
            }
            // 右侧让出缎带的斜切段（约 20）
            Rect inset = new Rect(pill.x + indent, pill.y, pill.width - indent - 24f, pill.height);
            TextMeshProUGUI text = CreateText(name, inset, 0f, 1f, 23f,
                TextAlignmentOptions.Left, PillInk, false);
            text.fontStyle = FontStyles.Bold;
            return text;
        }

        /// <summary>
        /// 按版面表的矩形（面板坐标）摆一张图。parent 不是面板时按父节点中心换算。
        /// Sprite 或矩形缺席返回 null，调用方据此不画、不缩进。
        /// </summary>
        private Image CreateArt(string name, Sprite sprite, Rect rect, Transform parent)
        {
            if (sprite == null || rect.width <= 0f || rect.height <= 0f || parent == null) return null;
            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f),
                new Vector2(rect.width, rect.height));
            RectTransform objRect = obj.GetComponent<RectTransform>();
            Vector2 position = DailyReportLayoutTable.ToAnchored(rect);
            RectTransform parentRect = parent as RectTransform;
            if (parentRect != null && parentRect != panelRect)
            {
                // 父节点（签到按钮）自身也是按版面表居中摆在面板上的：换算成相对父节点中心的偏移
                position -= parentRect.anchoredPosition;
            }
            objRect.anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        #endregion
    }
}
