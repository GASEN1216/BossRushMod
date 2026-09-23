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
using System.Text;
using System.Text.RegularExpressions;
using Duckov.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>天空岛局内指引。非交互，点击必须穿透。会话独占一个实例。</summary>
    internal sealed partial class SkyIslandHud : IDisposable
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
        /// <summary>
        /// 右上卡片底的不透明度。比面板 token（0.92）更实：这张卡没有 Backdrop 垫底、直接压在场景上，
        /// 而游戏是 Linear 色彩空间，8% 的亮云海透过来会把 15px 的区域名（Accent）压到 3.54:1；0.96 时最亮处 4.88:1。
        /// </summary>
        private const float CardSurfaceAlpha = 0.96f;

        /// <summary>
        /// 卡片入场 / 退场秒数与从右侧滑入的距离。
        /// 入场走 ease-out（元素真的在位移，ease-out 模拟自然停稳）；退场线性——没人在看它离开。
        /// 旧写法是 <c>SetActive</c> 硬开关：卡片「啪」地弹出，与同文件里刻意做成连续淡变的
        /// <see cref="SkyIslandProximityLabel"/> 自相矛盾。
        /// </summary>
        private const float CardFadeIn = 0.22f;
        private const float CardFadeOut = 0.18f;
        private const float CardSlideIn = 8f;

        /// <summary>
        /// 「目标更新」时左侧强调竖条闪一下：总时长、上升段（秒）、闪到多亮（向白插值的比例）与写入的量化档数。
        /// 眉题本身挂 6 秒，但静止的一行字很容易整条错过；闪一下是「卡片变了」的唯一动作提示。
        /// 上升段 ease-out 提亮、之后线性落回，只在量化档位变化时写 <c>Image.color</c>。
        /// 旧写法是 0.5 秒的硬切换（ObjectiveFlashRise 没有任何引用）、亮暗比只有 1.22:1（2026-09-14 审核 F-09）。
        /// </summary>
        private const float ObjectiveFlash = 0.5f;
        private const float ObjectiveFlashRise = 0.15f;
        private const float ObjectiveFlashLift = 0.6f;
        private const int ObjectiveFlashSteps = 8;

        /// <summary>
        /// 右上卡的字号梯度（2026-09-23 审美审查 UE-01）：玩家瞄一眼要读到的是「现在该做什么」，所以目标是卡片的主角——
        /// 15px 正文色；地名退成 12px 眉题（强调色、拉开字距），进度行 12px 次级色。旧版反过来：地名 15px 强调色抢了主位，
        /// 目标是 13px 灰字，整块读起来像一块调试框。撤离读秒仍用 15px：站在圈里的那几秒它就是最要紧的数。
        /// </summary>
        private const float RegionFont = 12f;
        private const float TitleFont = 15f;
        private const float BodyFont = 15f;
        private const float ChipFont = 12f;
        /// <summary>地名眉题的字距（1/100 em）：小字拉开才读作标签，而不是一行正文。</summary>
        private const float RegionSpacing = 6f;
        /// <summary>目标行最多占几行高（含「目标更新」眉题与拆开的分句）：再长就省略号收尾，卡片不无限长高。</summary>
        private const int ObjectiveMaxLines = 7;
        /// <summary>目标与进度行之间那道分隔线的占位高度：分隔线档的 rect 至少 8 高才画得出一条线（见 CreateSeparator）。</summary>
        private const float CardRuleHeight = 8f;
        /// <summary>
        /// 卡片的过渡（UE-11）：高度补间、新出现的行淡入、目标换字（旧字淡出 → 换字 → 新字淡入）。
        /// 旧写法三件事都是当帧硬切：挂「目标更新」那一帧卡片猛地长高一行，6 秒后又猛地缩回，新目标当帧顶掉旧目标。
        /// </summary>
        private const float CardResize = 0.16f;
        private const float RowFadeIn = 0.15f;
        private const float ObjectiveSwapOut = 0.1f;
        private const float ObjectiveSwapIn = 0.2f;

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
        /// <summary>
        /// 眉题与地名相对标题根中心的 y 与行高。
        /// 从字面量提成常量是为了让 <c>tests/SkyIslandUiContrastGuard.py</c> 能复算每一行
        /// 到底坐在多厚的压暗底上——三行里有两行是 4.5:1 门槛的小字，位置错一档就不达标。
        /// </summary>
        private const float BannerOverlineY = 44f;
        private const float BannerOverlineHeight = 22f;
        private const float BannerTitleY = 10f;
        private const float BannerTitleHeight = 60f;
        private const float AreaTitleFont = 44f;
        private const float AreaOverlineFont = 14f;
        /// <summary>落地操作提示行的字号。与眉题同属 4.5:1 门槛的小字。</summary>
        private const float BannerHintFont = 15f;
        /// <summary>
        /// 字距，TMP 的单位是 1/100 em。地名这类仪式感文字拉开一点字距才有「题字」的味道，
        /// 挤在一起就只是个标签；眉题是小字，要拉得更开才压得住。
        /// </summary>
        private const float AreaTitleSpacing = 8f;
        private const float AreaOverlineSpacing = 28f;
        /// <summary>淡入时从下方升起的像素数。只升 10 px：有「浮上来」的动势，又不至于晃眼。</summary>
        private const float AreaTitleRise = 10f;

        /// <summary>
        /// 大标题下那道短横线的最终宽度与高度。
        /// **高度不能是 1**：图集里的 <c>divider</c>（8×8 / border 2）亮带落在可拉伸的中心区，
        /// rect 高 1 时上下 border 各分到 0.5px、中心区归零，整条线一个像素都画不出来。
        /// 8 高时中心区剩 4px，亮带画成约 2px 实线外加上下柔边。
        /// </summary>
        private const float BannerRuleWidth = 180f;
        private const float BannerRuleHeight = 8f;
        /// <summary>
        /// 细线的颜色与不透明度（UE-10）：琥珀色（WarningText 当「金色」用），呼应原版暖琥珀画风。
        /// 旧值是 Divider（a=0.32 的蓝灰），压在近黑的压暗底上几乎看不见，整块大标题只剩黑白两色。
        /// </summary>
        private const float BannerRuleAlpha = 0.8f;
        /// <summary>地名淡入时从这个字距收拢到 <see cref="AreaTitleSpacing"/>（EaseOut）：一次性的「题字」揭示，每个区域一趟只播一次。</summary>
        private const float AreaTitleRevealSpacing = 16f;

        /// <summary>
        /// 区域大标题压暗底的基准尺寸，以及英文长句时允许扩到多宽。
        /// 压暗底两侧各有 <see cref="SkyIslandUiArt.ScrimHorizontalEdge"/> 的淡出区，
        /// 文字必须整行落在中间的平台上，否则行尾会滑进淡出区、背景又变回亮云海。
        /// <see cref="StartBanner"/> 按实测文字宽度反算宽度，见那里的注释。
        /// </summary>
        private const float BannerScrimWidth = 1180f;
        private const float BannerScrimHeight = 340f;
        private const float BannerScrimMaxWidth = 1700f;

        /// <summary>
        /// 区域大标题的淡入 / 停留 / 淡出秒数。停留从 2.2 拉到 3.0（UE-10）：三行字读完要这么久。
        /// 落地那一次（带操作提示）仍停 2.2：F3 自动验收 SKY_AUTO_LAND_DOCK_WORLD 在落地后约 4 秒断言大标题已淡出
        /// （步骤表 wait_real:3），要加长得先同步步骤表，见 2026-09-23 修复报告。
        /// </summary>
        private const float BannerFadeIn = 0.45f;
        private const float BannerHold = 3.0f;
        private const float BannerLandingHold = 2.2f;
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
        /// <summary>
        /// 字幕最多两行的高度，再长就省略号收尾：字幕是临场提示，不是段落。
        /// 游戏字体的行高比字面大，旧值 60 实际只排得下一行：2026-09-15 第五轮截图里 7 条长字幕全被截成一行、丢掉后半句
        /// （云蚋提示少了「贴近了才打得中」）。70 装得下两行；上沿 CaptionY + 70 = -216，仍在区域大标题操作提示行最低处 -215 之下
        /// （SkyIslandHudGuard 按常量复算）。一行被截断由 F3 自动验收的溢出判据判红。
        /// </summary>
        private const float CaptionMaxHeight = 70f;
        /// <summary>
        /// 字幕压暗底比文字**高**多少：高度 = 文字高 ÷ 平台占比，与
        /// <see cref="FitBannerScrim"/> 反算宽度是同一条算式。
        ///
        /// 旧写法是「文字高 + 40」的定值，两行字幕时文字会顶进上下淡出区里：
        /// 88 高的压暗底上，两行文字占 v∈[0.227, 0.773]，而平台只有 [0.28, 0.72]。
        /// 按比例给之后，一行两行都整个坐在平台上。压暗是柔边，下沿淡尾允许轻轻搭到官方读条上。
        /// </summary>
        private const float CaptionScrimPadding = 40f;
        /// <summary>字幕压暗底的最窄宽度：两三个字的短提示也要有一块看得出形状的底。</summary>
        private const float CaptionScrimMinWidth = 360f;
        private const float CaptionFadeIn = 0.25f;
        private const float CaptionFadeOut = 0.5f;
        /// <summary>字幕淡入时从下方升起的像素数。与大标题同一套动势语言，只是幅度更小。</summary>
        private const float CaptionRise = 6f;
        /// <summary>被警示打断时当前这条字幕的淡出秒数：要快，噬风的预警窗口只有 1.4 秒。</summary>
        private const float CaptionPreemptFade = 0.15f;
        /// <summary>停留时长按字数估：保底 2.6 秒、封顶 5.2 秒。</summary>
        private const float CaptionHoldMin = 2.6f;
        private const float CaptionHoldMax = 5.2f;
        private const float CaptionHoldPerChar = 0.06f;
        /// <summary>后面还排着字幕时，当前这条最多停这么久（口径同官方 NotificationText.durationIfPending）。</summary>
        private const float CaptionHoldIfPending = 1.6f;
        private const int CaptionQueueLimit = 3;
        /// <summary>
        /// 警示字幕的形态（UE-25）：开播时 0.12 秒从 1.04 缩回 1（EaseOut），字下方一道琥珀细线。
        /// 旧版警示只换了字色，Boss 机制预警（噬风只给 1.4 秒）扫一眼和「航路已清理」一样。
        /// 细线画在字幕根底边之下（不占 CaptionMaxHeight，守卫复算的上沿不变），仍在官方底部堆叠之上。
        /// </summary>
        private const float WarningPulse = 0.12f;
        private const float WarningPulseScale = 1.04f;
        private const float WarningRuleHeight = 2f;
        private const float WarningRuleOffset = -5f;

        /// <summary>「目标更新」眉题的停留秒数。</summary>
        private const float ObjectiveUpdatedHold = 6f;

        /// <summary>跟随官方 HUD 显隐的淡变秒数。官方 HUDManager 走 FadeGroup，这里取同量级的短淡变。</summary>
        private const float HideFade = 0.15f;

        #endregion

        private Canvas canvas;
        private CanvasGroup rootGroup;
        private GameObject card;
        private RectTransform cardRect;
        private CanvasGroup cardGroup;
        private Image cardAccentBar;
        private TextMeshProUGUI regionText, objectiveText, chipText, extractionText, statusText;
        /// <summary>目标与进度行之间的分隔线（UE-01）。</summary>
        private RectTransform chipRule;
        /// <summary>
        /// 卡片五行（地名 / 目标 / 进度 / 撤离读秒 / 存档状态）各自的淡入进度（0..1）。卡片已经显着时才新出现的行
        /// 从 0 淡到 1（UE-11）；卡片整块入场时行不单独淡，跟着卡片一起走。
        /// </summary>
        private readonly TextMeshProUGUI[] rowTexts = new TextMeshProUGUI[5];
        private readonly float[] rowReveal = { 1f, 1f, 1f, 1f, 1f };
        private bool rowsRevealing;
        /// <summary>卡片高度补间：当前高、起点、终点、已过秒数（-1 表示到位，常态只有一次比较）。</summary>
        private float cardHeight = 96f, cardHeightFrom = 96f, cardHeightTo = 96f, cardHeightAge = -1f;
        /// <summary>
        /// 卡片上此刻**显示着**的目标。<see cref="objective"/> 是最新登记的（F3 读它），换字时先淡出旧字再换成它，
        /// <see cref="objectiveSwap"/> 是这段过渡的秒表（-1 表示没在换）。
        /// </summary>
        private string shownObjective = string.Empty;
        private float objectiveSwap = -1f;

        private RectTransform bannerRect, bannerScrim, bannerRule;
        private CanvasGroup bannerGroup;
        private TextMeshProUGUI bannerOverline, bannerTitle, bannerHint;
        /// <summary>这一次大标题的停留秒数：落地那次与其后各区域不一样长（见 <see cref="BannerHold"/>）。</summary>
        private float bannerHold = BannerHold;

        private RectTransform captionRect, captionShade;
        private CanvasGroup captionGroup;
        private TextMeshProUGUI captionText;
        /// <summary>警示字幕下方的琥珀细线（UE-25）。普通字幕时关着。</summary>
        private RectTransform captionWarningRule;

        private string region = string.Empty, objective = string.Empty, chips = string.Empty;
        private string extraction, status, landingHint, pendingTitle, captionShowing;
        private float bannerAge = -1f, sinceBanner = BannerMinGap, captionAge = -1f, captionHold;
        private float objectiveUpdatedAge = -1f, visibility = 1f;
        /// <summary>卡片显隐：当前不透明度、目标不透明度、当前滑入偏移。</summary>
        private float cardVisible, cardTarget, cardSlide = CardSlideIn;
        private bool stacked, captionWarning;
        /// <summary>被警示打断的时刻（captionAge 读数）与当时的不透明度；captionCutAge 为 -1 表示没被打断。</summary>
        private float captionCutAge = -1f, captionCutAlpha, captionAlpha;
        /// <summary>卡片顶边避让官方右上角提示栈：节流计时与上一次写下的顶边。</summary>
        /// <summary>初值等于建造时写下的顶边，这样第一次 WriteCardTransform 不会把卡片甩到屏幕顶上。</summary>
        private float layoutTimer, appliedCardTop = -CardTop;

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
            cardGroup = card.AddComponent<CanvasGroup>();
            cardGroup.alpha = 0f;
            cardGroup.blocksRaycasts = false;
            cardGroup.interactable = false;
            Image background = card.AddComponent<Image>();
            Color cardSurface = BossRushUIColors.Surface;
            cardSurface.a = CardSurfaceAlpha;
            background.color = cardSurface;
            background.raycastTarget = false;
            // 卡片档（panel_raised），不是按钮档：它是一块常驻信息板，不是一个可点的按钮。
            BossRushUI.ApplyPanelSkin(background, 10, BossRushUISkinPart.Card);
            // 描边必须是独立 Image：图集里烤进 panel_raised 的那圈内描边被 Surface 乘完只剩
            // 2.9/255 的通道差（实算），看不见。而这张卡没有 Backdrop 垫底，直接压在场景上——
            // 暗地形下卡底对背景只有 1.50:1，没有边就等于没有轮廓。
            BossRushUI.ApplyPanelStroke(background, 10, BossRushUISkinPart.Card, BossRushUIColors.Stroke);

            // 左侧的一条 accent 竖线：只有 3 px，但它把「这是一块有主的信息」说清楚了。
            GameObject bar = ZombieModeUIHelper.CreateRect("AccentBar", card.transform,
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(AccentBarWidth * 0.5f + 4f, 0f), new Vector2(AccentBarWidth, -16f),
                new Vector2(0.5f, 0.5f));
            Image barImage = bar.AddComponent<Image>();
            barImage.color = BossRushUIColors.Accent;
            barImage.raycastTarget = false;
            // 细条档：3px 宽的竖条一律走程序化半径。注入图集后若按旧的两档落进按钮图
            // （32×32 / border 10），Unity 会把 10+10 等比压进 3px 并把中心区归零，
            // 画出来是按钮圆角的一道糊痕——比程序化还差。
            BossRushUI.ApplyPanelSkin(barImage, 2, BossRushUISkinPart.Hairline);
            cardAccentBar = barImage;
            card.SetActive(false);

            // 地名是眉题：12px 强调色、拉开字距；目标是主角：15px 正文色（UE-01，字号梯度见 RegionFont）。
            regionText = Text("Region", card.transform, RegionFont, BossRushUIColors.Accent,
                TextAlignmentOptions.Left);
            regionText.characterSpacing = RegionSpacing;
            objectiveText = Text("Objective", card.transform, BodyFont, BossRushUIColors.TextPrimary,
                TextAlignmentOptions.TopLeft);
            // 进度是另一类信息：靠一道细线和留白分组，不再套一层盒子。
            GameObject rule = ZombieModeUIHelper.CreateSeparator("ChipsRule", card.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, CardRuleHeight, BossRushUIColors.Divider);
            chipRule = rule.GetComponent<RectTransform>();
            chipRule.pivot = new Vector2(0f, 1f);
            rule.SetActive(false);
            chipText = Text("Chips", card.transform, ChipFont, BossRushUIColors.TextSecondary,
                TextAlignmentOptions.Left);
            extractionText = Text("Extraction", card.transform, TitleFont, BossRushUIColors.SuccessText,
                TextAlignmentOptions.Left);
            statusText = Text("Status", card.transform, BodyFont, BossRushUIColors.WarningText,
                TextAlignmentOptions.TopLeft);
            rowTexts[0] = regionText;
            rowTexts[1] = objectiveText;
            rowTexts[2] = chipText;
            rowTexts[3] = extractionText;
            rowTexts[4] = statusText;
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
            bannerScrim = AddScrim(root.transform, new Vector2(BannerScrimWidth, BannerScrimHeight),
                SkyIslandUiArt.GetTitleScrim(), SkyIslandUiArt.ScrimPeak);

            // 「小字所属 + 大字地名」是区域标题的通用层级：地点挂在上一级地域之下，一眼读出从属。
            // 三行都用 TextPrimary，层级靠字号与字距：游戏是 Linear 色彩空间，TextSecondary 的小字压在云海高光上，
            // 压暗底加到 0.84 也只有 2.9:1，过不了正文 4.5:1（2026-09-14 审核 F-01）。
            bannerOverline = CenteredText("Overline", root.transform, AreaOverlineFont,
                BossRushUIColors.TextPrimary, BannerOverlineY, BannerOverlineHeight);
            bannerOverline.characterSpacing = AreaOverlineSpacing;

            bannerTitle = CenteredText("Title", root.transform, AreaTitleFont,
                BossRushUIColors.TextPrimary, BannerTitleY, BannerTitleHeight);
            bannerTitle.characterSpacing = AreaTitleSpacing;

            // 细分隔线：大标题下方一道短横，是「区域名」这类仪式感元素的通用写法。
            // 旧写法是 120×1 的裸 Image（没有 sprite）：1px 的纯色四边形在非整数画布缩放下
            // 会被采样吃掉，时有时无。改走分隔线档（图集 divider：中间约 2px 的亮带、上下柔边），高度 8。
            GameObject rule = ZombieModeUIHelper.CreateRect("Rule", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -24f), new Vector2(BannerRuleWidth, BannerRuleHeight),
                new Vector2(0.5f, 0.5f));
            Image ruleImage = rule.AddComponent<Image>();
            Color ruleColor = BossRushUIColors.WarningText;
            ruleColor.a = BannerRuleAlpha;
            ruleImage.color = ruleColor;
            ruleImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(ruleImage, 2, BossRushUISkinPart.Rule);
            bannerRule = rule.GetComponent<RectTransform>();

            // 落地提示是 15px 小字，按线性色彩空间复算，TextSecondary 压在 0.84 的压暗底上亮云海里只有 2.8:1（审核 F-01），用 TextPrimary。
            bannerHint = CenteredText("Hint", root.transform, BannerHintFont,
                BossRushUIColors.TextPrimary, BannerHintY, BannerHintHeight);
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
            // 与区域大标题同一套二维柔边压暗，横向淡出更窄、峰值更高（字幕里有警示色）。
            // 宽度按每条字幕的实测文字宽反算（FitCaptionScrim），这里只是建出来时的占位尺寸。
            captionShade = AddScrim(root.transform, new Vector2(CaptionWidth, ScrimHeightFor(40f)),
                SkyIslandUiArt.GetCaptionScrim(), SkyIslandUiArt.CaptionScrimPeak);
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

            // 警示字幕下方的琥珀细线（UE-25）：锚在字幕根底边之下，随字幕一起淡；宽度每条按实测字宽的一半给（StartCaption）。
            GameObject warningRule = ZombieModeUIHelper.CreateRect("WarningRule", root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, WarningRuleOffset), new Vector2(120f, WarningRuleHeight), new Vector2(0.5f, 1f));
            Image warningImage = warningRule.AddComponent<Image>();
            Color warningColor = BossRushUIColors.WarningText;
            warningColor.a = BannerRuleAlpha;
            warningImage.color = warningColor;
            warningImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(warningImage, 1, BossRushUISkinPart.Hairline);
            captionWarningRule = warningRule.GetComponent<RectTransform>();
            warningRule.SetActive(false);
        }

        /// <summary>
        /// 压暗底。贴图只存形状，颜色取 <see cref="BossRushUIColors.Backdrop"/>、峰值不透明度由调用方给。
        /// 贴图生成失败时退成同色纯色块并告警：没有柔边难看，但文字总算垫着——
        /// 旧写法直接返回 null，大标题裸压在亮云海上且不报错（CR-2026-09-13-003 点名、2026-09-14 审核 F-25）。
        /// </summary>
        private static RectTransform AddScrim(Transform parent, Vector2 size, Sprite scrim, float peak)
        {
            GameObject shade = ZombieModeUIHelper.CreateRect("Scrim", parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, size, new Vector2(0.5f, 0.5f));
            Image image = shade.AddComponent<Image>();
            Color color = BossRushUIColors.Backdrop;
            color.a = peak;
            if (scrim != null)
            {
                image.sprite = scrim;
                image.type = Image.Type.Simple;
            }
            else
            {
                // 纯色块整块铺满、不打折：这是贴图生成失败的错误路径，可读性优先于柔边观感（小字要的 4.5:1 就靠这个峰值）。
                Debug.LogWarning("[SkyIsland] 压暗底贴图不可用，退成纯色底：HUD 文字仍有底垫着，但没有柔边");
            }
            image.color = color;
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
            // 换字要有过渡（UE-11）：卡片已经显着、新旧都不是空的，就先把旧字淡出，再换（TickObjectiveSwap）。
            // 卡片还没出来、或是从空到有 / 从有到空，直接换：那时本来就有卡片或整行的淡变。
            bool visible = card != null && card.activeSelf && cardVisible > 0f;
            if (!visible || shownObjective.Length == 0 || value.Length == 0)
            {
                shownObjective = value;
                objectiveSwap = -1f;
                SetRowAlpha(1);
                Apply();
                return;
            }
            // 正在淡入时又换了：从当前的透明度接着往下淡，不跳回全亮。
            if (objectiveSwap >= ObjectiveSwapOut)
                objectiveSwap = (1f - ObjectiveSwapAlpha()) * ObjectiveSwapOut;
            else if (objectiveSwap < 0f)
                objectiveSwap = 0f;
        }

        /// <summary>卡片此刻登记的目标文本（不含「目标更新」眉题）。只读，给 F3 验收比对「显示的」。</summary>
        internal string ObjectiveText { get { return objective; } }

        internal void SetChips(string value)
        {
            value = value ?? string.Empty;
            if (string.Equals(chips, value, StringComparison.Ordinal)) return;
            chips = value;
            chipsDisplay = value.Length == 0 ? string.Empty : CountPattern.Replace(value, CountMarkup);
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
            bool hidden = suppressed || BossRushUI.IsOfficialHudHidden() || BossRushUI.IsGamePaused();
            float target = hidden ? 0f : 1f;
            if (visibility != target)
            {
                visibility = Mathf.MoveTowards(visibility, target, unscaledDelta / HideFade);
                if (rootGroup != null) rootGroup.alpha = visibility;
            }
            // 隐藏期间不推进大标题与字幕：玩家开着地图的那几秒，不该把它们在背后悄悄播完。
            // 暂停菜单同理，并且从 2026-09-14 起它也让本 HUD 淡出（常驻 HUD 同一口径：官方隐藏 HUD、暂停、拍照模式都收起）。
            // PauseMenu 是 UIPanel 不是 View，不让官方 HUD 隐藏；本 HUD 的淡变走 unscaled 时间，不停下来的话一条 Boss 机制提示会在暂停菜单背后播完。
            if (hidden) return;

            layoutTimer -= unscaledDelta;
            if (layoutTimer <= 0f)
            {
                layoutTimer = CardLayoutInterval;
                ApplyCardAnchor();
            }

            TickCard(unscaledDelta);
            TickCardHeight(unscaledDelta);
            TickObjectiveSwap(unscaledDelta);
            TickRows(unscaledDelta);
            sinceBanner += unscaledDelta;
            TickBanner(unscaledDelta);
            TickCaption(unscaledDelta);
            if (objectiveUpdatedAge >= 0f)
            {
                objectiveUpdatedAge += unscaledDelta;
                TickAccentFlash();
                if (objectiveUpdatedAge >= ObjectiveUpdatedHold)
                {
                    objectiveUpdatedAge = -1f;
                    TickAccentFlash();
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
        /// 官方 HUD 显隐条件（`HUDManager.ShouldDisplay` 的五条，含官方游戏机注册的隐藏令牌）收在 BossRushUI.IsOfficialHudHidden。
        /// </summary>
        private void ApplyCardAnchor()
        {
            if (cardRect == null) return;
            float top = BossRushUI.GetTopRightHudTop(canvas) + (stacked ? -CardStackOffset : 0f);
            if (Mathf.Abs(top - appliedCardTop) < 0.5f) return;
            appliedCardTop = top;
            // 位置由 WriteCardTransform 统一落笔：顶边与入场滑入偏移是同一个 anchoredPosition 的两半，
            // 各写各的会互相覆盖（滑入时被定时重排拽回去，重排时被滑入拽回去）。
            WriteCardTransform();
        }

        /// <summary>
        /// 卡片淡入淡出。**到位之后每帧只有一次浮点比较**，不写 CanvasGroup、不写 RectTransform
        /// （口径同 <see cref="ApplyCardAnchor"/> 的「值没变就不写」与 SkyIslandMapMarkers.Apply 的早退）。
        /// </summary>
        private void TickCard(float delta)
        {
            if (card == null || cardVisible == cardTarget) return;
            cardVisible = cardTarget > cardVisible
                ? Mathf.MoveTowards(cardVisible, 1f, delta / CardFadeIn)
                : Mathf.MoveTowards(cardVisible, 0f, delta / CardFadeOut);
            float shown = CardShown(cardVisible, cardTarget);
            if (cardGroup != null) cardGroup.alpha = shown;
            cardSlide = (1f - shown) * CardSlideIn;
            WriteCardTransform();
            if (cardVisible <= 0f && card.activeSelf) card.SetActive(false);
        }

        /// <summary>
        /// 卡片此刻的显示量。入场 ease-out：卡片真的从右侧滑进来，ease-out 才像自然停稳；
        /// 退场不套曲线——线性就够，而且更快离场。
        /// </summary>
        private static float CardShown(float visible, float target)
        {
            return target > 0f ? BossRushUI.EaseOut(visible) : visible;
        }

        /// <summary>
        /// 显示目标换向时把进度换算到新曲线上，让显示量连续：否则淡入到一半被收回，
        /// 显示量会在一帧里从 ease-out 值跳回线性值（例如 0.75 → 0.50，2026-09-14 审核 F-27）。
        /// </summary>
        private void RetargetCard(float target)
        {
            if (target == cardTarget) return;
            float shown = CardShown(cardVisible, cardTarget);
            // EaseOut 的反函数：1 - (1 - v)² = shown → v = 1 - sqrt(1 - shown)。
            cardVisible = target > 0f ? 1f - Mathf.Sqrt(Mathf.Clamp01(1f - shown)) : shown;
            cardTarget = target;
        }

        /// <summary>强调竖条上一次写下的闪烁档位。值没变就不重写 Image.color。</summary>
        private int cardAccentStep;

        /// <summary>
        /// 「目标更新」时左侧强调竖条闪一下：<see cref="ObjectiveFlashRise"/> 秒 ease-out 提亮，
        /// 到 <see cref="ObjectiveFlash"/> 线性落回。按 <see cref="ObjectiveFlashSteps"/> 档量化，档位变了才写 <c>Image.color</c>。
        /// </summary>
        private void TickAccentFlash()
        {
            if (cardAccentBar == null) return;
            float level = 0f;
            float age = objectiveUpdatedAge;
            if (age >= 0f && age < ObjectiveFlash)
            {
                level = age < ObjectiveFlashRise
                    ? BossRushUI.EaseOut(age / ObjectiveFlashRise)
                    : 1f - (age - ObjectiveFlashRise) / (ObjectiveFlash - ObjectiveFlashRise);
            }
            int step = Mathf.Clamp(Mathf.RoundToInt(level * ObjectiveFlashSteps), 0, ObjectiveFlashSteps);
            if (step == cardAccentStep) return;
            cardAccentStep = step;
            cardAccentBar.color = Color.Lerp(BossRushUIColors.Accent, Color.white,
                ObjectiveFlashLift * step / ObjectiveFlashSteps);
        }

        /// <summary>卡片的最终位置 = 避让官方提示栈算出来的顶边 + 入场滑入偏移。值没变就不写。</summary>
        private void WriteCardTransform()
        {
            if (cardRect == null) return;
            Vector2 target = new Vector2(CardRight + cardSlide, -appliedCardTop);
            if ((cardRect.anchoredPosition - target).sqrMagnitude < 0.01f) return;
            cardRect.anchoredPosition = target;
        }

        /// <summary>
        /// 定卡片高度：<paramref name="snap"/> 时直接落定（卡片整块入场本来就有淡入与滑入）；
        /// 卡片已经显着时从当前高度补间过去（UE-11），不再「目标更新」那一帧猛地长高一行。
        /// </summary>
        private void SetCardHeight(float target, bool snap)
        {
            if (cardRect == null) return;
            if (snap)
            {
                cardHeight = cardHeightFrom = cardHeightTo = target;
                cardHeightAge = -1f;
                cardRect.sizeDelta = new Vector2(CardWidth, target);
                return;
            }
            if (Mathf.Abs(target - cardHeightTo) < 0.5f) return;
            cardHeightFrom = cardHeight;
            cardHeightTo = target;
            cardHeightAge = 0f;
        }

        /// <summary>卡片高度补间：SmoothStep、<see cref="CardResize"/> 秒到位。到位之后每帧只有一次比较。</summary>
        private void TickCardHeight(float delta)
        {
            if (cardHeightAge < 0f || cardRect == null) return;
            cardHeightAge += delta;
            float t = Mathf.Clamp01(cardHeightAge / CardResize);
            cardHeight = Mathf.Lerp(cardHeightFrom, cardHeightTo, BossRushUI.SmoothStep(t));
            cardRect.sizeDelta = new Vector2(CardWidth, cardHeight);
            if (t >= 1f) cardHeightAge = -1f;
        }

        /// <summary>目标换字此刻的透明度：旧字 <see cref="ObjectiveSwapOut"/> 秒线性淡出 → 换字 → 新字 <see cref="ObjectiveSwapIn"/> 秒 EaseOut 淡入。</summary>
        private float ObjectiveSwapAlpha()
        {
            if (objectiveSwap < 0f) return 1f;
            if (objectiveSwap < ObjectiveSwapOut) return 1f - objectiveSwap / ObjectiveSwapOut;
            return BossRushUI.EaseOut((objectiveSwap - ObjectiveSwapOut) / ObjectiveSwapIn);
        }

        /// <summary>推进目标换字；旧字淡没的那一帧换成最新登记的目标并重排（卡片高度随之补间）。没在换时第一句就返回。</summary>
        private void TickObjectiveSwap(float delta)
        {
            if (objectiveSwap < 0f) return;
            bool fadingOut = objectiveSwap < ObjectiveSwapOut;
            objectiveSwap += delta;
            if (fadingOut && objectiveSwap >= ObjectiveSwapOut)
            {
                shownObjective = objective;
                Apply();
            }
            if (objectiveSwap >= ObjectiveSwapOut + ObjectiveSwapIn) objectiveSwap = -1f;
            SetRowAlpha(1);
        }

        /// <summary>卡片显着时新出现的行淡入。没有在淡的行时第一句就返回。</summary>
        private void TickRows(float delta)
        {
            if (!rowsRevealing) return;
            bool any = false;
            for (int i = 0; i < rowReveal.Length; i++)
            {
                if (rowReveal[i] >= 1f) continue;
                rowReveal[i] = Mathf.MoveTowards(rowReveal[i], 1f, delta / RowFadeIn);
                SetRowAlpha(i);
                if (rowReveal[i] < 1f) any = true;
            }
            rowsRevealing = any;
        }

        /// <summary>
        /// 一行的透明度 = 行淡入 ×（目标行）换字过渡。TMP 的 alpha 每写一次重建一次网格，所以只在过渡期间、值真的变了才写。
        /// </summary>
        private void SetRowAlpha(int index)
        {
            TextMeshProUGUI text = rowTexts[index];
            if (text == null) return;
            float alpha = BossRushUI.SmoothStep(rowReveal[index]) * (index == 1 ? ObjectiveSwapAlpha() : 1f);
            if (Mathf.Abs(text.alpha - alpha) > 0.001f) text.alpha = alpha;
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
            float total = BannerFadeIn + bannerHold + BannerFadeOut;
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
                alpha = BossRushUI.SmoothStep(bannerAge / BannerFadeIn);
                // 淡入走 SmoothStep、位移走 ease-out（AGENTS §4.14 的分工）：大标题是真的在往上走。
                rise = (1f - BossRushUI.EaseOut(bannerAge / BannerFadeIn)) * AreaTitleRise;
                // 细横线在同一段时间里从中心向两端展开。ease-out：线是在「划出去」，
                // 和大标题整块原地淡入（SmoothStep）是两种动作，用两条曲线。
                if (bannerRule != null)
                    bannerRule.sizeDelta = new Vector2(
                        BossRushUI.EaseOut(bannerAge / BannerFadeIn) * BannerRuleWidth, BannerRuleHeight);
                // 地名字距在同一段时间里从宽收拢（UE-10）：一次性的「题字」揭示。一趟每区一次，TMP 重排约 27 帧。
                if (bannerTitle != null)
                    bannerTitle.characterSpacing = Mathf.Lerp(AreaTitleRevealSpacing, AreaTitleSpacing,
                        BossRushUI.EaseOut(bannerAge / BannerFadeIn));
            }
            else if (bannerAge < BannerFadeIn + bannerHold)
            {
                alpha = 1f;
                if (bannerRule != null && bannerRule.sizeDelta.x < BannerRuleWidth)
                    bannerRule.sizeDelta = new Vector2(BannerRuleWidth, BannerRuleHeight);
                if (bannerTitle != null && bannerTitle.characterSpacing != AreaTitleSpacing)
                    bannerTitle.characterSpacing = AreaTitleSpacing;
            }
            else
            {
                alpha = 1f - BossRushUI.SmoothStep((bannerAge - BannerFadeIn - bannerHold) / BannerFadeOut);
            }
            if (bannerGroup != null) bannerGroup.alpha = alpha;
            if (bannerRect != null) bannerRect.anchoredPosition = new Vector2(0f, AreaTitleY - rise);
        }

        private void StartBanner(string title)
        {
            if (bannerOverline != null) bannerOverline.text = L10n.T("晴岚群岛", "QINGLAN ARCHIPELAGO");
            if (bannerTitle != null) bannerTitle.text = title;
            if (bannerHint != null) bannerHint.text = landingHint ?? string.Empty;
            bannerHold = landingHint != null ? BannerLandingHold : BannerHold;
            landingHint = null;
            // 压暗底按字距最宽的那一刻量（揭示开始时），收拢之后只会更窄，行首行尾始终在平台里。
            if (bannerTitle != null) bannerTitle.characterSpacing = AreaTitleRevealSpacing;
            FitBannerScrim();
            bannerAge = 0f;
            if (bannerGroup != null) bannerGroup.alpha = 0f;
            if (bannerRect != null) bannerRect.anchoredPosition = new Vector2(0f, AreaTitleY - AreaTitleRise);
            // 细横线从 0 宽度揭示，和淡入同步收尾。
            if (bannerRule != null) bannerRule.sizeDelta = new Vector2(0f, BannerRuleHeight);
        }

        /// <summary>
        /// 按本次三行文字的**实测**宽度反算压暗底该多宽。
        ///
        /// 压暗底两侧各有 <see cref="SkyIslandUiArt.ScrimHorizontalEdge"/> 的淡出区，中间
        /// <c>1-2×edge</c> 才是罩得住文字的平台。文字半宽必须落在平台半宽里，于是
        /// <c>宽度 ≥ 文字宽 / (1-2×edge)</c>。不这么算的话，英文长句（落地提示行整句能到 700 px）
        /// 的行首行尾会滑进淡出区、背后又变回亮云海——正是这一行此前 2.54:1 的成因之一。
        /// 每次出大标题算一次（一趟出击几次），不是每帧路径。
        /// </summary>
        private void FitBannerScrim()
        {
            if (bannerScrim == null) return;
            float widest = 0f;
            widest = Mathf.Max(widest, MeasuredWidth(bannerOverline));
            widest = Mathf.Max(widest, MeasuredWidth(bannerTitle));
            widest = Mathf.Max(widest, MeasuredWidth(bannerHint));
            float plateau = Mathf.Max(0.05f, 1f - 2f * SkyIslandUiArt.ScrimHorizontalEdge);
            float width = Mathf.Clamp(widest / plateau, BannerScrimWidth, BannerScrimMaxWidth);
            Vector2 size = new Vector2(width, BannerScrimHeight);
            if ((bannerScrim.sizeDelta - size).sqrMagnitude < 0.01f) return;
            bannerScrim.sizeDelta = size;
        }

        /// <summary>
        /// 罩住 <paramref name="textHeight"/> 那么高的文字需要多高的压暗底：
        /// 文字必须整个落在平台上，而平台只占 <c>1-2×ScrimEdge</c>，所以高度要除以它。
        /// 再兜一个下限，免得一行短字幕的压暗底缩得比 CaptionScrimPadding 还小。
        /// </summary>
        private static float ScrimHeightFor(float textHeight)
        {
            float plateau = Mathf.Max(0.05f, 1f - 2f * SkyIslandUiArt.ScrimEdge);
            return Mathf.Max(textHeight + CaptionScrimPadding, textHeight / plateau);
        }

        private static float MeasuredWidth(TextMeshProUGUI text)
        {
            if (text == null || string.IsNullOrEmpty(text.text)) return 0f;
            return text.GetPreferredValues(text.text, float.PositiveInfinity, float.PositiveInfinity).x;
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
                    ? BossRushUI.SmoothStep(captionAge / CaptionFadeIn)
                    : (captionAge < CaptionFadeIn + hold
                        ? 1f
                        : 1f - BossRushUI.SmoothStep((captionAge - CaptionFadeIn - hold) / CaptionFadeOut));
            }
            captionAlpha = alpha;
            if (captionGroup != null) captionGroup.alpha = alpha;
            // 淡入期间从下方升起，与区域大标题同一套动势语言（幅度更小：字幕是提示不是仪式）。
            // 轴心在底边，所以这里改的是根的 y；淡出不再动位置——退场不需要动势。
            if (captionRect != null && captionCutAge < 0f && captionAge < CaptionFadeIn)
            {
                float rise = (1f - BossRushUI.EaseOut(captionAge / CaptionFadeIn)) * CaptionRise;
                captionRect.anchoredPosition = new Vector2(0f, CaptionY - rise);
            }
            // 警示字幕开播的一下缩放脉冲（UE-25）：1.04 → 1，EaseOut。轴心在底边，往上收，不压官方底部堆叠。
            if (captionRect != null && captionWarning)
            {
                float scale = captionAge < WarningPulse
                    ? Mathf.Lerp(WarningPulseScale, 1f, BossRushUI.EaseOut(captionAge / WarningPulse))
                    : 1f;
                if (captionRect.localScale.x != scale) captionRect.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private void EndCaption()
        {
            captionAge = -1f;
            captionShowing = null;
            captionWarning = false;
            captionCutAge = -1f;
            captionAlpha = 0f;
            if (captionGroup != null) captionGroup.alpha = 0f;
            if (captionRect != null)
            {
                captionRect.anchoredPosition = new Vector2(0f, CaptionY);
                captionRect.localScale = Vector3.one;
            }
            if (captionWarningRule != null && captionWarningRule.gameObject.activeSelf) captionWarningRule.gameObject.SetActive(false);
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
            if (captionRect != null) captionRect.anchoredPosition = new Vector2(0f, CaptionY - CaptionRise);
            if (captionText == null) return;
            captionText.color = warning ? BossRushUIColors.WarningText : BossRushUIColors.TextPrimary;
            captionText.text = text;
            // 先量后排：一行就是一行高，两行就给两行，压暗底跟着文字高度走；再长封顶两行、省略号收尾。
            float height = Mathf.Clamp(
                Mathf.Ceil(captionText.GetPreferredValues(text, CaptionWidth, float.PositiveInfinity).y) + 8f,
                CaptionFont * 1.6f, CaptionMaxHeight);
            if (captionRect != null)
            {
                captionRect.sizeDelta = new Vector2(CaptionWidth, height);
                captionRect.localScale = warning ? new Vector3(WarningPulseScale, WarningPulseScale, 1f) : Vector3.one;
            }
            FitCaptionScrim(height);
            // 警示字幕下方的琥珀细线，宽度取实测字宽的一半（UE-25）；普通字幕关掉。
            if (captionWarningRule != null)
            {
                if (warning)
                    captionWarningRule.sizeDelta = new Vector2(
                        Mathf.Max(48f, Mathf.Min(CaptionWidth, MeasuredWidth(captionText)) * 0.5f), WarningRuleHeight);
                if (captionWarningRule.gameObject.activeSelf != warning) captionWarningRule.gameObject.SetActive(warning);
            }
        }

        /// <summary>
        /// 字幕压暗底按本条的**实测**文字宽反算，口径同 <see cref="FitBannerScrim"/>：平台只占
        /// <c>1-2×CaptionScrimHorizontalEdge</c>，行首行尾必须落在平台里。旧写法宽度恒为 CaptionWidth+180，
        /// 一行 800 px 的英文字幕行尾压暗只剩 0.40（2026-09-14 审核 F-04）。每条字幕开播算一次，不是每帧路径。
        /// </summary>
        private void FitCaptionScrim(float textHeight)
        {
            if (captionShade == null) return;
            float plateau = Mathf.Max(0.05f, 1f - 2f * SkyIslandUiArt.CaptionScrimHorizontalEdge);
            float textWidth = Mathf.Min(CaptionWidth, MeasuredWidth(captionText));
            Vector2 size = new Vector2(Mathf.Max(CaptionScrimMinWidth, textWidth / plateau), ScrimHeightFor(textHeight));
            if ((captionShade.sizeDelta - size).sqrMagnitude < 0.01f) return;
            captionShade.sizeDelta = size;
        }

        #endregion

        public void Dispose()
        {
            if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null;
            rootGroup = null;
            card = null;
            cardRect = null;
            cardGroup = null;
            cardAccentBar = null;
            regionText = objectiveText = chipText = extractionText = statusText = null;
            bannerRect = bannerScrim = bannerRule = null;
            bannerGroup = null;
            bannerOverline = bannerTitle = bannerHint = null;
            captionRect = captionShade = null;
            captionGroup = null;
            captionText = null;
            captions.Clear();
        }
    }
}
