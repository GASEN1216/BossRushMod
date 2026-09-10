// ============================================================================
// SkyIslandHud.cs - 天空岛局内指引
// ============================================================================
// 【为什么不再在屏幕正上方写字】
//   旧版直接复用 `ArenaPrototypeControls.CreateHud`：屏幕正中偏上一块 920×90 的裸文字，
//   常驻四行（地名 / 目标 / 物资与委托 / 「地图键查阅全岛 · 站进撤离环停留 3 秒返航 · 保存状态」）。
//   那是**原型期的调试文本**，不是给玩家看的指引：正上方是视线焦点，常驻长句会一直抢注意力，
//   而且实测中文要 4 行 100 px、英文 6 行 150 px，90 px 的框根本装不下（超出部分直接画到框外）。
//
// 【现在的口径，照主流游戏的做法分四层】
//   1. **常驻只留一张小卡**，贴右边缘（与 `CampaignHud` 同一套视觉语言与同一列），
//      只放「当前区域 + 当前目标 + 进度小标」。宽 320，高按内容实测。
//      目标真的变了才在目标上方挂一行「目标更新」眉题，几秒后自己收掉。
//   2. **区域名做成本趟第一次走进时的一次性大标题**（小字群岛名 + 大字地名 + 细线），
//      同一个区域一趟只出一次，两次之间还要隔几秒。给足仪式感，然后**消失**，不会每过一座桥念一遍。
//   3. **临场提示走中下方字幕**（Boss 机制、战斗门控、清场）：按字数停留，排队不吞；
//      警示（Boss 机制、门控原因）插队并打断正在播的普通字幕（规则见 SkyIslandCaptionQueue）。
//      塞进角落小卡里的「离开它脚下的那一圈」等于没说，排在「航路已清理」后面读到时第一波已经炸完。
//   4. **其余全部按需出现**：撤离读条交给官方 `EvacuationCountdownUI`（见 SkyIslandExtractionCountdown），
//      存档状态**正常时完全不显示**——只有出问题才在卡片里常驻一行。静默是高级感的一部分。
//
// 【跟随官方 HUD 显隐】
//   官方 `HUDManager` 在打开背包/地图等界面、对话、捏脸、拍照模式时会淡出整块 HUD。
//   本 HUD 逐条照抄同一组条件一起让位，外加会话侧的「装配中 / 返航中 / 死亡 / 剧情面板打开」。
//   否则右侧卡片和区域大标题会浮在官方地图、背包与撤离结算画面上面。
//
// 【避让】位置全部对照官方 HUD 预制体实测（UnityPy 读 resources.assets 的 LevelManager/HUDCanvas）。
//   官方 HUD 画布是 2560×1440 参考、按短边缩放；本库画布是 1920×1080 Expand。两者都取 min(宽比, 高比)，
//   所以本库 1 单位恒等于官方 4/3 单位，下面的换算与屏幕比例无关。
//   - 右上角：官方「操作说明」提示栈（SimpleIndicators / IndicatorHUD）钉在那里，默认一行开关，
//     玩家展开后是 11 行、一路长到屏幕中部。卡片每 0.25 秒读一次它的实际下沿，排在它下面
//     （`BossRushUI.GetTopRightHudTop`，CampaignHud 同口径）；契约在岛上仍武装时本卡再下移一档。
//   - 左上角是官方时间显示与随机事件徽章，所以卡片不走左边。
//   - 底部：快捷栏、血条、子弹类型之上还有**交互读条**（ActionProgress_Slider，搜箱开门时出现），
//     顶边在本库单位 243。字幕以底边钉在它上方、最多两行；区域大标题再排在字幕上方，三者纵向错开。
//
// UI 硬约束（AGENTS 4.14）：Canvas 走 `BossRushUI.CreateCanvasRoot(..., interactive:false)`，
// sortingOrder 用常量，颜色只用 token，文本一律 TMP + 共享字体，所有 Graphic 的
// raycastTarget 置 false —— HUD 必须让点击穿透。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>天空岛局内指引。非交互，点击必须穿透。会话独占一个实例。</summary>
    internal sealed class SkyIslandHud : IDisposable
    {
        #region 版式常量

        private const float CardWidth = 320f;
        private const float CardRight = -24f;
        /// <summary>
        /// 建出来时的顶边 y（与 `CampaignHud` 同一个起点、同 `BossRushUI.TopRightHudTopMin`）。
        /// 运行时由 <see cref="ApplyCardAnchor"/> 按官方提示栈的实际下沿改写；契约在武装时本卡再下移 CardStackOffset。
        /// </summary>
        private const float CardTop = -110f;
        private const float CardStackOffset = -112f;
        /// <summary>重新量一次官方提示栈下沿的间隔（秒）。</summary>
        private const float CardLayoutInterval = 0.25f;
        private const float CardPadX = 14f;
        private const float CardPadY = 12f;
        private const float AccentBarWidth = 3f;

        private const float TitleFont = 15f;
        private const float BodyFont = 13f;
        private const float ChipFont = 12f;

        /// <summary>
        /// 区域大标题相对屏幕中心的 y。**负数 = 中线偏下**，这是刻意的：
        /// 屏幕中上方是视线焦点，往那儿贴字正是要甩掉的网游味。
        /// 取 -150 而不是更低：下面要给字幕（底边钉在官方交互读条之上、最多两行）让出位置——
        /// 1080 高的画布里，标题下沿那行操作提示连同上浮动画都必须高于字幕上沿（守卫按常量复算）。
        /// 离线预览（tools/preview_sky_island_panel.py）读的就是这个常量，别改成字面量。
        /// </summary>
        private const float AreaTitleY = -150f;
        /// <summary>大标题里操作提示行相对标题中心的 y 与行高。守卫用它复算「提示行不压字幕」。</summary>
        private const float BannerHintY = -42f;
        private const float BannerHintHeight = 26f;
        private const float AreaTitleFont = 44f;
        private const float AreaOverlineFont = 14f;
        /// <summary>
        /// 字距，TMP 的单位是 1/100 em。地名这类仪式感文字拉开一点字距才有「题字」的味道，
        /// 挤在一起就只是个标签；眉题是小字，要拉得更开才压得住。
        /// </summary>
        private const float AreaTitleSpacing = 8f;
        private const float AreaOverlineSpacing = 28f;
        /// <summary>淡入时从下方升起的像素数。只升 10 px：有「浮上来」的动势，又不至于晃眼。</summary>
        private const float AreaTitleRise = 10f;

        /// <summary>区域大标题的淡入 / 停留 / 淡出秒数。</summary>
        private const float BannerFadeIn = 0.45f;
        private const float BannerHold = 2.2f;
        private const float BannerFadeOut = 1.0f;
        /// <summary>上一次大标题收掉之后至少隔多少秒才出下一次：连着走过两个地标时不连弹。</summary>
        private const float BannerMinGap = 8f;

        /// <summary>
        /// 官方底部 HUD 堆叠的最高点：本库 1080 高画布里距底边的单位数。
        /// 实测（UnityPy 读 LevelManager/HUDCanvas，官方 2560×1440 参考 × 0.75 换算）：快捷栏顶 112、
        /// 子弹类型 187、**交互读条 ActionProgress_Slider 顶 243**——搜箱、开门、交互读秒时它就在屏幕中下方。
        /// 旧注释以为「快捷栏连同武器名约占底部 140 px」，字幕中心放在 -330（距底 210），两行字幕会整段压住读条。
        /// </summary>
        private const float OfficialBottomStackTop = 244f;
        /// <summary>
        /// 字幕**底边**相对屏幕中心的 y（字幕根的轴心在底边，文字向上长）。1080 高时底边距底 254，
        /// 在官方底部堆叠之上 10 个单位；更高的屏幕比例下画布更高，这段距离只会更大。
        /// 这是主流游戏放旁白与临场提示的位置，视线稍一下移就能读到，又不压正前方的战斗视野。
        /// </summary>
        private const float CaptionY = -286f;
        private const float CaptionWidth = 1000f;
        private const float CaptionFont = 20f;
        /// <summary>字幕最多两行的高度，再长就省略号收尾：字幕是临场提示，不是段落。</summary>
        private const float CaptionMaxHeight = 60f;
        /// <summary>字幕压暗底比文字高出的量（上下各一半）。压暗是柔边，下沿淡尾允许轻轻搭到读条上。</summary>
        private const float CaptionScrimPadding = 40f;
        private const float CaptionFadeIn = 0.25f;
        private const float CaptionFadeOut = 0.5f;
        /// <summary>被警示打断时当前这条字幕的淡出秒数：要快，噬风的预警窗口只有 1.4 秒。</summary>
        private const float CaptionPreemptFade = 0.15f;
        /// <summary>停留时长按字数估：保底 2.6 秒、封顶 5.2 秒。</summary>
        private const float CaptionHoldMin = 2.6f;
        private const float CaptionHoldMax = 5.2f;
        private const float CaptionHoldPerChar = 0.06f;
        /// <summary>后面还排着字幕时，当前这条最多停这么久（口径同官方 NotificationText.durationIfPending）。</summary>
        private const float CaptionHoldIfPending = 1.6f;
        private const int CaptionQueueLimit = 3;

        /// <summary>「目标更新」眉题的停留秒数。</summary>
        private const float ObjectiveUpdatedHold = 6f;

        /// <summary>跟随官方 HUD 显隐的淡变秒数。官方 HUDManager 走 FadeGroup，这里取同量级的短淡变。</summary>
        private const float HideFade = 0.15f;

        #endregion

        private Canvas canvas;
        private CanvasGroup rootGroup;
        private GameObject card;
        private RectTransform cardRect;
        private TextMeshProUGUI regionText, objectiveText, chipText, extractionText, statusText;

        private RectTransform bannerRect;
        private CanvasGroup bannerGroup;
        private TextMeshProUGUI bannerOverline, bannerTitle, bannerHint;

        private RectTransform captionRect, captionShade;
        private CanvasGroup captionGroup;
        private TextMeshProUGUI captionText;

        private string region = string.Empty, objective = string.Empty, chips = string.Empty;
        private string extraction, status, landingHint, pendingTitle, captionShowing;
        private float bannerAge = -1f, sinceBanner = BannerMinGap, captionAge = -1f, captionHold;
        private float objectiveUpdatedAge = -1f, visibility = 1f;
        private bool stacked, captionWarning;
        /// <summary>被警示打断的时刻（captionAge 读数）与当时的不透明度；captionCutAge 为 -1 表示没被打断。</summary>
        private float captionCutAge = -1f, captionCutAlpha, captionAlpha;
        /// <summary>卡片顶边避让官方右上角提示栈：节流计时与上一次写下的顶边。</summary>
        private float layoutTimer, appliedCardTop = -1f;

        /// <summary>本趟已经出过大标题的区域。同一个区域一趟只出一次。</summary>
        private readonly HashSet<string> titled = new HashSet<string>(StringComparer.Ordinal);
        private readonly SkyIslandCaptionQueue captions = new SkyIslandCaptionQueue(CaptionQueueLimit);

        internal SkyIslandHud(Transform owner)
        {
            canvas = BossRushUI.CreateCanvasRoot("SkyIslandHud", BossRushUILayers.HudOverlay, false);
            if (owner != null) canvas.transform.SetParent(owner, false);
            rootGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = false;
            rootGroup.interactable = false;
            BuildCard();
            BuildBanner();
            BuildCaption();
            Apply();
        }

        #region 构建

        private void BuildCard()
        {
            card = ZombieModeUIHelper.CreateRect("SkyIslandTracker", canvas.transform,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(CardRight, CardTop), new Vector2(CardWidth, 96f), new Vector2(1f, 1f));
            cardRect = card.GetComponent<RectTransform>();
            Image background = card.AddComponent<Image>();
            background.color = BossRushUIColors.Surface;
            background.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(background, 10);

            // 左侧的一条 accent 竖线：只有 3 px，但它把「这是一块有主的信息」说清楚了。
            GameObject bar = ZombieModeUIHelper.CreateRect("AccentBar", card.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(AccentBarWidth * 0.5f + 4f, 0f), new Vector2(AccentBarWidth, -16f),
                new Vector2(0.5f, 0.5f));
            Image barImage = bar.AddComponent<Image>();
            barImage.color = BossRushUIColors.Accent;
            barImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(barImage, 2);

            regionText = Text("Region", card.transform, TitleFont, BossRushUIColors.Accent,
                TextAlignmentOptions.Left);
            objectiveText = Text("Objective", card.transform, BodyFont, BossRushUIColors.TextSecondary,
                TextAlignmentOptions.TopLeft);
            chipText = Text("Chips", card.transform, ChipFont, BossRushUIColors.TextSecondary,
                TextAlignmentOptions.Left);
            extractionText = Text("Extraction", card.transform, TitleFont, BossRushUIColors.SuccessText,
                TextAlignmentOptions.Left);
            statusText = Text("Status", card.transform, BodyFont, BossRushUIColors.WarningText,
                TextAlignmentOptions.TopLeft);
        }

        private void BuildBanner()
        {
            // 放在**中线偏下**，不放屏幕中上方。中上方是视线焦点，往那儿贴字正是要甩掉的
            // 那种网游味；区域名这类仪式感元素在主流游戏里也基本都在下半屏（RDR2 左下、
            // 对马岛与老头环中下），既看得见又不挡正前方的战斗视野。
            GameObject root = ZombieModeUIHelper.CreateRect("SkyIslandAreaTitle", canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, AreaTitleY), new Vector2(900f, 150f), new Vector2(0.5f, 0.5f));
            bannerRect = root.GetComponent<RectTransform>();
            bannerGroup = root.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.blocksRaycasts = false;
            bannerGroup.interactable = false;

            // 压暗底垫在文字下面。岛上抬头是一片高亮云海，浅色字直接压上去几乎读不出来。
            // 二维柔边，没有硬边，看不出贴了块板。
            AddScrim(root.transform, new Vector2(1180f, 230f));

            // 「小字所属 + 大字地名」是区域标题的通用层级：地点挂在上一级地域之下，一眼读出从属。
            bannerOverline = CenteredText("Overline", root.transform, AreaOverlineFont,
                BossRushUIColors.TextSecondary, 44f, 22f);
            bannerOverline.characterSpacing = AreaOverlineSpacing;

            bannerTitle = CenteredText("Title", root.transform, AreaTitleFont,
                BossRushUIColors.TextPrimary, 10f, 60f);
            bannerTitle.characterSpacing = AreaTitleSpacing;

            // 细分隔线：大标题下方一道短横，是「区域名」这类仪式感元素的通用写法。
            GameObject rule = ZombieModeUIHelper.CreateRect("Rule", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -24f), new Vector2(120f, 1f), new Vector2(0.5f, 0.5f));
            Image ruleImage = rule.AddComponent<Image>();
            ruleImage.color = BossRushUIColors.Divider;
            ruleImage.raycastTarget = false;

            bannerHint = CenteredText("Hint", root.transform, 15f, BossRushUIColors.TextSecondary, BannerHintY, BannerHintHeight);
        }

        private void BuildCaption()
        {
            // 轴心在**底边**：字幕向上长，底边始终钉在官方底部堆叠之上，两行也不会往下压住交互读条。
            GameObject root = ZombieModeUIHelper.CreateRect("SkyIslandCaption", canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, CaptionY), new Vector2(CaptionWidth, 40f), new Vector2(0.5f, 0f));
            captionRect = root.GetComponent<RectTransform>();
            captionGroup = root.AddComponent<CanvasGroup>();
            captionGroup.alpha = 0f;
            captionGroup.blocksRaycasts = false;
            captionGroup.interactable = false;
            // 与区域大标题同一张二维柔边压暗：字幕同样压在高亮云海上。
            captionShade = AddScrim(root.transform, new Vector2(CaptionWidth + 180f, 40f + CaptionScrimPadding));
            captionText = ZombieModeUIHelper.CreateText("Text", root.transform, string.Empty, CaptionFont,
                new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            captionText.raycastTarget = false;
            // 字幕字号固定：长句折行，而不是缩成小字。高度由 StartCaption 实测，封顶两行、再长省略号收尾。
            captionText.enableAutoSizing = false;
            captionText.fontSize = CaptionFont;
            captionText.enableWordWrapping = true;
            captionText.overflowMode = TextOverflowModes.Ellipsis;
            BossRushUI.ApplyGameFont(captionText);
        }

        private static RectTransform AddScrim(Transform parent, Vector2 size)
        {
            Sprite scrim = SkyIslandUiArt.GetTitleScrim();
            if (scrim == null) return null;
            GameObject shade = ZombieModeUIHelper.CreateRect("Scrim", parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, size, new Vector2(0.5f, 0.5f));
            Image image = shade.AddComponent<Image>();
            image.sprite = scrim;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            return shade.GetComponent<RectTransform>();
        }

        private static TextMeshProUGUI CenteredText(string name, Transform parent, float size, Color color,
            float y, float height)
        {
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText(name, parent, string.Empty, size,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(880f, height),
                TextAlignmentOptions.Center, color);
            text.raycastTarget = false;
            BossRushUI.ApplyGameFont(text);
            return text;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, float size, Color color,
            TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText(name, parent, string.Empty, size,
                new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero,
                new Vector2(-(CardPadX * 2f + AccentBarWidth + 6f), 20f), alignment, color);
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            // HUD 是固定框，绝不能用 Overflow：超出的行会直接画到卡片外面
            // （`BossRushUI.MeasureTextHeight` 的注释也明说这一点）。这里靠 Apply() 实测高度，
            // 万一还是不够就省略号收尾，不越界。
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        #endregion

        #region 对外写入

        /// <summary>
        /// 当前区域。会话只在玩家真正走进某个地标的到访半径时才调用，桥上保持上一个。
        /// 本趟**第一次**走进的区域才登记大标题；真正出不出、什么时候出由 Tick 按最小间隔决定。
        /// </summary>
        internal void SetRegion(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(region, value, StringComparison.Ordinal)) return;
            region = value;
            if (value.Length > 0 && !titled.Contains(value)) pendingTitle = value;
            Apply();
        }

        internal void SetObjective(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(objective, value, StringComparison.Ordinal)) return;
            // 真的变了才算「目标更新」：进岛时写入初始目标不算，清空也不算。
            if (objective.Length > 0 && value.Length > 0) objectiveUpdatedAge = 0f;
            objective = value;
            Apply();
        }

        /// <summary>卡片此刻登记的目标文本（不含「目标更新」眉题）。只读，给 F3 验收比对「显示的」。</summary>
        internal string ObjectiveText { get { return objective; } }

        internal void SetChips(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(chips, value, StringComparison.Ordinal)) return;
            chips = value;
            Apply();
        }

        /// <summary>撤离读秒的文字兜底。传 null 表示不在圈里或官方读条已接管——整行消失，不留空位。</summary>
        internal void SetExtraction(string value)
        {
            if (string.Equals(extraction, value, StringComparison.Ordinal)) return;
            extraction = value;
            Apply();
        }

        /// <summary>
        /// **持续性**问题（存档写屏障、换槽、保存待重试）：卡片里常驻一行，问题消失时传 null 收掉。
        /// 刻意不走字幕：字幕几秒就淡掉，而这类问题在解决前一直成立；反过来也不能每隔半秒重播一次字幕，
        /// 那会把同一时间里真正的临场提示（Boss 机制、战斗门控）全部挤掉。
        /// </summary>
        internal void SetStatus(string value)
        {
            if (string.IsNullOrEmpty(value)) value = null;
            if (string.Equals(status, value, StringComparison.Ordinal)) return;
            status = value;
            Apply();
        }

        /// <summary>
        /// 一次性字幕：排队播放，停留时长按字数估，播完淡出。<paramref name="warning"/> 用警示色并且**插队**：
        /// 排在全部普通字幕之前、队满时不先丢它，正在播的普通字幕快速淡出让位（规则见 SkyIslandCaptionQueue）。
        /// </summary>
        internal void Caption(string value, bool warning)
        {
            bool preempt;
            SkyIslandCaptionQueue.Admission admission = captions.Admit(value, warning,
                captionAge >= 0f ? captionShowing : null, captionWarning, out preempt);
            // 同一句正在播：把停留重新拉满而不是再排一遍（战斗门控提示会被玩家连按触发）。
            // 已经在被打断淡出的那条不再拉回来。
            if (admission == SkyIslandCaptionQueue.Admission.RefreshShowing)
            {
                if (captionCutAge < 0f && captionAge > CaptionFadeIn) captionAge = CaptionFadeIn;
                return;
            }
            if (preempt && captionCutAge < 0f)
            {
                captionCutAge = captionAge;
                captionCutAlpha = captionAlpha;
            }
        }

        /// <summary>
        /// 落地时的一次性操作提示。只跟**第一次**区域大标题出现一次，之后清空：
        /// 每进一个新区域都把操作说明再念一遍，正是廉价感的来源。
        /// </summary>
        internal void SetLandingHint(string value)
        {
            landingHint = string.IsNullOrEmpty(value) ? null : value;
        }

        #endregion

        #region 每帧

        /// <summary>
        /// 由会话每帧驱动。只推进显隐、淡入淡出与过期，不重排版式。
        /// </summary>
        /// <param name="suppressed">会话侧的隐藏条件：装配中、返航中、死亡、剧情面板打开。</param>
        internal void Tick(float unscaledDelta, bool suppressed)
        {
            if (canvas == null) return;
            bool hidden = suppressed || BossRushUI.IsOfficialHudHidden();
            float target = hidden ? 0f : 1f;
            if (visibility != target)
            {
                visibility = Mathf.MoveTowards(visibility, target, unscaledDelta / HideFade);
                if (rootGroup != null) rootGroup.alpha = visibility;
            }
            // 隐藏期间不推进大标题与字幕：玩家开着地图的那几秒，不该把它们在背后悄悄播完。
            // 暂停菜单同理：它不隐藏官方 HUD（PauseMenu 是 UIPanel 不是 View），但画布 sortingOrder 10000
            // 整个盖在上面；本 HUD 的淡变走 unscaled 时间，不停下来的话一条 Boss 机制提示会在暂停菜单背后播完。
            if (hidden || BossRushUI.IsGamePaused()) return;

            layoutTimer -= unscaledDelta;
            if (layoutTimer <= 0f)
            {
                layoutTimer = CardLayoutInterval;
                ApplyCardAnchor();
            }

            sinceBanner += unscaledDelta;
            TickBanner(unscaledDelta);
            TickCaption(unscaledDelta);
            if (objectiveUpdatedAge >= 0f)
            {
                objectiveUpdatedAge += unscaledDelta;
                if (objectiveUpdatedAge >= ObjectiveUpdatedHold)
                {
                    objectiveUpdatedAge = -1f;
                    Apply();
                }
            }

            // 契约 HUD 随时可能出现/消失，卡片跟着让位，避免两块叠在同一列。
            bool campaignArmed = false;
            try { campaignArmed = CampaignObjectiveTracker.IsArmed; }
            catch (Exception) { /* 契约系统不可用时按未武装处理 */ }
            if (campaignArmed != stacked)
            {
                stacked = campaignArmed;
                ApplyCardAnchor();
            }
        }

        /// <summary>
        /// 卡片顶边：排在官方右上角「操作说明」提示栈的实际下沿之下（与 CampaignHud 共用 BossRushUI 的同一个口径），
        /// 契约武装时再下移一档让出契约追踪条。位置真的变了才写 RectTransform。
        /// 官方 HUD 显隐条件（`HUDManager.ShouldDisplay` 的公开部分）同样收在 BossRushUI.IsOfficialHudHidden，
        /// 隐藏令牌读不到也不需要读：本 Mod 唯一注册令牌的是剧情面板，已由会话传进来的 suppressed 覆盖。
        /// </summary>
        private void ApplyCardAnchor()
        {
            if (cardRect == null) return;
            float top = BossRushUI.GetTopRightHudTop(canvas) + (stacked ? -CardStackOffset : 0f);
            if (Mathf.Abs(top - appliedCardTop) < 0.5f) return;
            appliedCardTop = top;
            cardRect.anchoredPosition = new Vector2(CardRight, -top);
        }

        private void TickBanner(float delta)
        {
            if (bannerAge < 0f)
            {
                if (pendingTitle == null || sinceBanner < BannerMinGap) return;
                string next = pendingTitle;
                pendingTitle = null;
                // 等间隔的这几秒里玩家可能已经走开：只给「此刻所在」的区域出标题，过期的直接丢掉，
                // 下次真正走回那里时再登记。
                if (!string.Equals(next, region, StringComparison.Ordinal) || !titled.Add(next)) return;
                StartBanner(next);
            }

            bannerAge += delta;
            float total = BannerFadeIn + BannerHold + BannerFadeOut;
            if (bannerAge >= total)
            {
                bannerAge = -1f;
                sinceBanner = 0f;
                if (bannerGroup != null) bannerGroup.alpha = 0f;
                return;
            }
            float alpha;
            float rise = 0f;
            if (bannerAge < BannerFadeIn)
            {
                alpha = Smooth(bannerAge / BannerFadeIn);
                rise = (1f - alpha) * AreaTitleRise;
            }
            else if (bannerAge < BannerFadeIn + BannerHold)
            {
                alpha = 1f;
            }
            else
            {
                alpha = 1f - Smooth((bannerAge - BannerFadeIn - BannerHold) / BannerFadeOut);
            }
            if (bannerGroup != null) bannerGroup.alpha = alpha;
            if (bannerRect != null) bannerRect.anchoredPosition = new Vector2(0f, AreaTitleY - rise);
        }

        private void StartBanner(string title)
        {
            if (bannerOverline != null) bannerOverline.text = L10n.T("晴岚群岛", "QINGLAN ARCHIPELAGO");
            if (bannerTitle != null) bannerTitle.text = title;
            if (bannerHint != null) bannerHint.text = landingHint ?? string.Empty;
            landingHint = null;
            bannerAge = 0f;
            if (bannerGroup != null) bannerGroup.alpha = 0f;
            if (bannerRect != null) bannerRect.anchoredPosition = new Vector2(0f, AreaTitleY - AreaTitleRise);
        }

        private void TickCaption(float delta)
        {
            if (captionAge < 0f)
            {
                SkyIslandCaptionQueue.Entry next;
                if (!captions.TryDequeue(out next)) return;
                StartCaption(next.Text, next.Warning);
            }

            captionAge += delta;
            float alpha;
            if (captionCutAge >= 0f)
            {
                // 被警示打断：从打断那一刻的不透明度快速淡出，给排在队首的警示让位。
                float cut = (captionAge - captionCutAge) / CaptionPreemptFade;
                if (cut >= 1f) { EndCaption(); return; }
                alpha = captionCutAlpha * (1f - cut);
            }
            else
            {
                // 后面还排着的时候缩短当前这条，别让一串公告拖成十几秒。
                float hold = captions.Count > 0 ? Mathf.Min(captionHold, CaptionHoldIfPending) : captionHold;
                float total = CaptionFadeIn + hold + CaptionFadeOut;
                if (captionAge >= total) { EndCaption(); return; }
                alpha = captionAge < CaptionFadeIn
                    ? Smooth(captionAge / CaptionFadeIn)
                    : (captionAge < CaptionFadeIn + hold
                        ? 1f
                        : 1f - Smooth((captionAge - CaptionFadeIn - hold) / CaptionFadeOut));
            }
            captionAlpha = alpha;
            if (captionGroup != null) captionGroup.alpha = alpha;
        }

        private void EndCaption()
        {
            captionAge = -1f;
            captionShowing = null;
            captionWarning = false;
            captionCutAge = -1f;
            captionAlpha = 0f;
            if (captionGroup != null) captionGroup.alpha = 0f;
        }

        private void StartCaption(string text, bool warning)
        {
            captionShowing = text;
            captionWarning = warning;
            captionCutAge = -1f;
            captionAge = 0f;
            captionAlpha = 0f;
            captionHold = Mathf.Clamp(text.Length * CaptionHoldPerChar + CaptionHoldMin * 0.5f,
                CaptionHoldMin, CaptionHoldMax);
            if (captionGroup != null) captionGroup.alpha = 0f;
            if (captionText == null) return;
            captionText.color = warning ? BossRushUIColors.WarningText : BossRushUIColors.TextPrimary;
            captionText.text = text;
            // 先量后排：一行就是一行高，两行就给两行，压暗底跟着文字高度走；再长封顶两行、省略号收尾。
            float height = Mathf.Clamp(
                Mathf.Ceil(captionText.GetPreferredValues(text, CaptionWidth, float.PositiveInfinity).y) + 8f,
                CaptionFont * 1.6f, CaptionMaxHeight);
            if (captionRect != null) captionRect.sizeDelta = new Vector2(CaptionWidth, height);
            if (captionShade != null) captionShade.sizeDelta = new Vector2(CaptionWidth + 180f, height + CaptionScrimPadding);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        #endregion

        #region 版式

        /// <summary>
        /// 重排卡片：**先量后排**，卡片高度由实际内容决定。
        /// 只在内容真的变化时调用（写入方法自己短路），不是每帧路径。
        /// </summary>
        private void Apply()
        {
            if (card == null) return;
            float width = CardWidth - CardPadX * 2f - AccentBarWidth - 6f;
            float left = CardPadX + AccentBarWidth + 6f;
            float y = -CardPadY;

            y -= Row(regionText, region, width, left, y, TitleFont);
            y -= Row(objectiveText, ObjectiveDisplay(), width, left, y, BodyFont);
            y -= Row(chipText, chips, width, left, y, ChipFont);
            y -= Row(extractionText, extraction, width, left, y, TitleFont);
            y -= Row(statusText, status, width, left, y, BodyFont);

            // 一行都没有就整张卡片收掉。装配期间（还没就绪、什么都没得说）留一块空底板在那儿，
            // 恰恰是「廉价感」的来源之一：没内容就不该有框。
            bool anything = region.Length > 0 || objective.Length > 0 || chips.Length > 0
                || !string.IsNullOrEmpty(extraction) || !string.IsNullOrEmpty(status);
            if (card.activeSelf != anything) card.SetActive(anything);
            cardRect.sizeDelta = new Vector2(CardWidth, Mathf.Max(48f, -y + CardPadY));
        }

        /// <summary>目标行的实际文本：目标刚变过时，在上方挂一行强调色的「目标更新」眉题。</summary>
        private string ObjectiveDisplay()
        {
            if (objectiveUpdatedAge < 0f || objective.Length == 0) return objective;
            return "<size=85%><color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.Accent) + ">"
                + L10n.T("目标更新", "Objective updated") + "</color></size>\n" + objective;
        }

        /// <summary>摆一行并返回它占掉的高度（含行距）。内容为空时整行隐藏、不占位。</summary>
        private static float Row(TextMeshProUGUI text, string value, float width, float left,
            float top, float font)
        {
            if (text == null) return 0f;
            if (string.IsNullOrEmpty(value))
            {
                if (text.gameObject.activeSelf) text.gameObject.SetActive(false);
                return 0f;
            }
            if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
            text.text = value;
            float height = Mathf.Max(font * 1.3f,
                Mathf.Ceil(text.GetPreferredValues(value, width, float.PositiveInfinity).y) + 2f);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left, top);
            return height + 4f;
        }

        #endregion

        public void Dispose()
        {
            if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null;
            rootGroup = null;
            card = null;
            cardRect = null;
            regionText = objectiveText = chipText = extractionText = statusText = null;
            bannerRect = null;
            bannerGroup = null;
            bannerOverline = bannerTitle = bannerHint = null;
            captionRect = captionShade = null;
            captionGroup = null;
            captionText = null;
            captions.Clear();
        }
    }

    /// <summary>
    /// 世界空间提示字的「走近才浮现」。
    ///
    /// 常驻的浮空字远远就亮着，满屏都是网游式的头顶标语。主流做法是靠光、模型与地图点让人
    /// 远远注意到「那里有东西」，走近了才浮出文字说明它是什么。硬开关（SetActive）会「啪」地弹出，
    /// 这里按距离做连续的 smoothstep 淡变，完全透明时顺手关掉渲染，不白占 draw call。
    ///
    /// 纯表现层：不碰交互体，官方交互提示照常由 InteractableBase 负责。
    /// 支持两种载体：世界空间 TextMeshPro（淡 alpha + 关 Renderer），或世界空间 Canvas（淡 CanvasGroup + 关 Canvas）。
    /// </summary>
    internal sealed class SkyIslandProximityLabel : MonoBehaviour
    {
        /// <summary>
        /// 透明度量化步长。TMP 的 alpha 每改一次都要重建整块文字网格（顶点色跟着重算），
        /// 旧写法按 1% 阈值更新，玩家走过一次 5 米的淡变带要重建上百次；按 5% 一档最多 20 次，肉眼看不出台阶。
        /// </summary>
        private const float AlphaStep = 0.05f;
        /// <summary>估算玩家接近速度的上限（米/秒）：离得远时据此推迟下一次距离检查。</summary>
        private const float ApproachSpeed = 8f;
        private const float MaxRecheckSeconds = 1f;

        private float near, far;
        private TMP_Text text;
        private Renderer textRenderer;
        private Canvas worldCanvas;
        private CanvasGroup group;
        private float applied = -1f, nextCheck;

        /// <param name="near">这个距离以内完全显形。</param>
        /// <param name="far">这个距离以外完全消失。</param>
        internal static void Attach(GameObject target, float near, float far)
        {
            if (target == null) return;
            SkyIslandProximityLabel label = target.GetComponent<SkyIslandProximityLabel>();
            if (label == null) label = target.AddComponent<SkyIslandProximityLabel>();
            label.near = Mathf.Max(0f, near);
            label.far = Mathf.Max(label.near + 0.5f, far);
            label.worldCanvas = target.GetComponent<Canvas>();
            if (label.worldCanvas != null)
            {
                label.group = target.GetComponent<CanvasGroup>();
                if (label.group == null) label.group = target.AddComponent<CanvasGroup>();
                label.group.blocksRaycasts = false;
                label.group.interactable = false;
            }
            else
            {
                label.text = target.GetComponent<TMP_Text>();
                label.textRenderer = target.GetComponent<Renderer>();
            }
            // 先按完全透明起步，第一次 LateUpdate 再按实际距离打开，不会在建出来那一帧闪一下。
            label.ApplyAlpha(0f);
        }

        private void LateUpdate()
        {
            // 完全隐形且离得远时不必每帧量距离：按「以最快接近速度走到淡变带外沿还要多久」推迟下一次检查，
            // 最多隔 1 秒。纪念物与船点招牌大部分时间离玩家很远，这里通常只是一次时间比较。
            if (applied <= 0f && Time.unscaledTime < nextCheck) return;
            float alpha = 0f;
            CharacterMainControl main = CharacterMainControl.Main;
            if (main != null)
            {
                float distance = Vector3.Distance(main.transform.position, transform.position);
                float t = Mathf.Clamp01((far - distance) / (far - near));
                alpha = Mathf.Round(t * t * (3f - 2f * t) / AlphaStep) * AlphaStep;
                if (alpha <= 0f)
                    nextCheck = Time.unscaledTime + Mathf.Min(MaxRecheckSeconds, (distance - far) / ApproachSpeed);
            }
            else
            {
                nextCheck = Time.unscaledTime + MaxRecheckSeconds;
            }
            // 量化后同一档不重写；「归零」这一档照样要写下去，否则 Renderer 会以最低一档的 alpha 一直开着。
            if (Mathf.Abs(alpha - applied) < AlphaStep * 0.5f) return;
            ApplyAlpha(alpha);
        }

        private void ApplyAlpha(float alpha)
        {
            applied = alpha;
            bool visible = alpha > 0.001f;
            if (group != null) group.alpha = alpha;
            if (worldCanvas != null && worldCanvas.enabled != visible) worldCanvas.enabled = visible;
            if (text != null) text.alpha = alpha;
            if (textRenderer != null && textRenderer.enabled != visible) textRenderer.enabled = visible;
        }
    }
}
