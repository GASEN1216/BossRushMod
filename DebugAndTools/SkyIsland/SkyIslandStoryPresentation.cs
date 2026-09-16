// ============================================================================
// SkyIslandStoryPresentation.cs - 天空岛剧情/装置面板
// ============================================================================
// 【为什么这块面板还在，而不是全走官方对话】
//   叙事已经搬去官方对话了（`SkyIslandResidentDialogue`，官方 `Dialogues.DialogueUI` +
//   我们自己封装的 `DialogueManager`）。这块自绘面板保留下来只为一件事：**模态**。
//   官方 `DialogueUI` 只给「台词 + 纯文本选项」，**给不了 `timeScale = 0`**，
//   而面板里挂着眠苔的苔药、浮舟的整备与三处合成台——
//   没有模态门，它就是战斗中的免费暂停 + 回血站。
//   模态由共享租约压 `Time.timeScale`（口径见 `SkyIslandSession` 的面板门一节），
//   蚊群、灶火计时、撤离读条都跟着冻结，这是**被依赖的行为**，不是副作用。
//   分工因此固定下来：**说话走官方，办事留这里**。要动这条先读
//   `SkyIslandResidentDialogue.cs` 的文件头。
//
//   官方任务系统接**跨局主线**：`SkyIslandOfficialQuestBridge` 把 Jeff 序章与岛上三条主线（任务表
//   `SkyIslandOfficialQuestTable`，给予者是苇白 / 浮舟 / 钟守）投影成官方 Quest / Task，并在官方存档快照里
//   过滤自己的 ID，防止卸载后出现孤儿任务；本面板仍是**写事实的地方**，官方任务只是投影与入口。
//   岛上居民委托仍按单次出击计、不进存档，所以继续留在本面板，不混进跨局 Quest。
//   边界与卸载兼容理由见 `tests/SkyIslandOfficialApiReuseGuard.py` 与 `CODE_REVIEW_FINDINGS.md`。
//
// 【为什么重写布局】
//   旧版把每个元素钉在写死的 anchoredPosition 上（标题 y=275、正文 y=139、
//   第 i 个选项 y=5-i*52、「继续」y=-282），字号也写死、`overflowMode` 用 TMP 默认的
//   Overflow —— 超框既不裁剪也不省略，**直接画到面板外**。离线量过一遍：
//     · 标题框 750×55 / 字号 30，三条英文 PointName 实际要 810~975 px，
//       折成两行需 72 px，第二行整行掉在面板外；
//     · 第 6 个选项（y=-233..-277）会和固定在 y=-260..-304 的「继续旅程」叠在一起，
//       今天最多 5 个选项还撞不上，但这是靠数出来的巧合，不是靠结构保证的。
//   （选项按钮与正文实测**不**超框：50 条选项、352 条正文各 0 超框，别顺手改它们的字号。）
//
// 【现在的口径：先量后排，容不下就按优先级挤】
//   所有高度都用共享的 `BossRushUI.MeasureTextHeight` 实测，再从上往下摆。
//   面板高度被 `GetReferenceViewportSize()` 夹住，容不下时**按固定顺序**收缩：
//       选项与标题不压 → 先压正文 → 再压主视觉（压到装得下标题与立绘的地板为止）
//   （旧口径里排第一的「页脚」已于 2026-09-13 删除。）
//   正文收缩到下限后套 ScrollRect（共享 `ConfigureScrollRect`），滚动而不是溢出。
//   这样「文字超出 UI」不再是调参问题，而是结构上不可能发生。
//   换正文（选项回执、手记子页、名册翻页）同样走这套算术：`SetBodyText` 按新正文整页重建，
//   不是塞进打开时量好的视口；空正文收成 0 高（2026-09-15 第五轮 D3/D5）。
//
// 【操作与官方界面对齐】
//   - 鼠标、数字键 1–9（选项左侧有键帽）、ESC 关闭；右上角 ESC 键帽也能用鼠标点；
//   - 鼠标悬停与键盘当前项是**同一种**高亮（焦点色，见 ChoiceFocusLift），不再一暗一亮；
//   - 键盘导航走官方 `UIInputManager`：W / S 移动当前项、Enter 确认、Esc 取消。
//     注意官方输入资产「Duckov Controls」只有键鼠方案、**没有任何手柄绑定**（UnityPy 读 globalgamemanagers.assets 确认），
//     手柄只有经 Steam Input 之类映射到这几个键时才走得到这里——别再把它写成「原生手柄支持」；
//   - 面板开着时官方 HUD 一起淡出（`HUDManager` 隐藏令牌），与官方对话界面同口径；
//   - 首次打开有一次共享的淡入微放大；选项回执引起的重开不重播，免得每点一次都弹一下。
//
// 【插图 fail-open】
//   插图由 `SkyIslandUiArt` 提供，没有就退成无插图布局。装置面板上挂着
//   K1/K2/K3 与敲响归航钟，绝不能因为一张图没出来就打不开。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class SkyIslandStoryPresentation : IDisposable
    {
        internal sealed class Choice
        {
            internal string Label;
            internal Func<string> Select;
            internal Choice(string label, Func<string> select) { Label = label; Select = select; }
        }

        #region 布局常量

        private const float PanelWidth = 880f;
        private const float Pad = 26f;
        private const float Gap = 14f;
        private const float ContentWidth = PanelWidth - Pad * 2f;

        /// <summary>
        /// 主视觉（hero）的高度上下限。**它是全出血的**：左右顶到面板边、上边就是面板顶边，
        /// 圆角由背景层的 Mask 切。容不下时它在正文之后第二个被压缩，但**不会被整条撤掉**——
        /// 标题现在压在它上面。
        ///
        /// 上限 264：横幅 1024×288 铺满 880 宽正好 247，留一点余量给将来换比例的图。
        /// 下限 172：没有横幅（居民面板只有立绘）时 hero 退化成一条纯底图的表头，
        /// 仍然要装得下标题；真正的地板是 <c>max(HeroMinHeight, 内容高 + 上下 inset)</c>。
        /// </summary>
        private const float HeroMaxHeight = 264f;
        private const float HeroMinHeight = 172f;

        /// <summary>主视觉里内容（立绘 / 标题）距 hero 边缘的内边距。</summary>
        private const float HeroInset = 22f;

        /// <summary>
        /// 主视觉底部压暗占 hero 高度的比例（实底带 + 上方淡出的总高）。
        /// </summary>
        private const float HeroFadeFraction = 0.62f;

        /// <summary>
        /// 标题实底带的不透明度，以及带子上方淡出段的最小高度。
        ///
        /// 【为什么不能只用一条渐变（CR-2026-09-13-007）】<see cref="SkyIslandUiArt.GetBannerFade"/>
        /// 是 <c>alpha = t²</c>，不透明度全堆在底边。标题上沿离 hero 底边 123 px、渐隐总高 153 px 时，
        /// 那里的 t=0.44、alpha 只有 **0.19**——标题最上面那行等于直接压在没处理过的插图上，
        /// 亮云那一块就读不出来了。这和区域大标题压暗底那条 bug 是同一类：
        /// **把不透明度放到了文字不在的地方**。
        ///
        /// 所以改成「实底带罩住标题 + 带子上方再淡出到透明」。0.82 是按最坏情况定的：
        /// 即使底下是纯白（亮度 1.0），按游戏的线性色彩空间混合，标题（最小 24px，WCAG 大字 3:1）仍有 4.09:1。
        /// 带子留 18% 透光，插图的颜色仍然透得出来，不是一条纯色横杠。
        /// </summary>
        private const float HeroTitleBandAlpha = 0.82f;
        private const float HeroFadeMin = 56f;

        /// <summary>
        /// 整屏底图的压暗量。**不烤进图里**，因为压暗量是跟正文对比度绑定的，
        /// 必须跟着 <see cref="BossRushUIColors.Surface"/> 走（口径同 BossRushUI_图集规格.md
        /// 里「描边不要烤进九宫格」那条）。
        ///
        /// 0.82 按**实测**最坏情况、按游戏的线性色彩空间定：13 张模糊底图里最亮那张（skyisland_bg_S2）的
        /// p99 相对亮度是 0.747，压完之后正文 TextPrimary 5.07:1，过 4.5:1。
        /// 旧值 0.72 是按 sRGB 口径算的（还把相对亮度当 sRGB 灰度代入），线性复算正文只有 3.64:1（2026-09-14 审核 F-01）。
        /// 中位亮度只有 0.098–0.397，所以绝大多数区域余量大得多，区域底图的色调仍透得出来。
        /// </summary>
        private const float BackgroundTintAlpha = 0.82f;

        /// <summary>
        /// 选项行底的不透明度。**刻意不满**：让区域底图从行底透出来，行才像「浮在这张图上」，
        /// 而不是一排糊在图上的黑块。
        ///
        /// 瓶颈是行边：描边对行底要过 WCAG 1.4.11 的 3:1，那圈边是「这一行是可点控件」的唯一证据。
        /// 配合 0.82 的底图压暗与满覆盖描边，线性复算最亮底图上最差 3.20:1；标签文字余量很大（10:1 以上）。
        /// </summary>
        private const float ChoiceRowAlpha = 0.78f;

        /// <summary>
        /// 选项的**焦点**：鼠标悬停与键盘方向键是同一个「当前项」（悬停即选中，见 BuildChoice 里订的 PointerEnter），
        /// 表现是**强调色的行边 + 行底轻提亮**（<see cref="FocusColor"/>、<see cref="SetFocused"/>）。
        ///
        /// 【为什么焦点靠行边、不靠整行变亮（2026-09-14 审核 F-01）】游戏是 Linear 色彩空间，亮底图会从半透明行底透出来。
        /// 旧写法让焦点行整行向白 0.39，线性复算焦点行对常态行最差只有 2.13:1；要靠整行填色拉到 3:1，
        /// 白字标签在焦点行上又会跌破 4.5:1——两条要求在亮底图上没有交集。主流做法（也是 WCAG 1.4.11 对焦点指示物的口径）
        /// 是让**指示物**对相邻颜色够 3:1：焦点行的边换成 Accent，对焦点行底最差 4.40:1；
        /// 行底只轻提 0.12 当悬停手感，标签在焦点行上最差 9.8:1。
        ///
        /// 行底色仍然住在 ColorBlock 里、Graphic 置白（<c>ZombieModeUIHelper.ApplyButtonColors</c> 的口径）：
        /// 行底写进 <c>image.color</c> 会和 ColorTint 相乘，悬停反而变暗（2026-09-14 修过的就是这个）。
        /// </summary>
        private const float ChoiceFocusLift = 0.12f;
        private const float ChoiceFocusAlpha = 0.86f;

        /// <summary>
        /// 立绘在主视觉里的边长。
        ///
        /// 208 而不是原来的 160：去掉圆角底板之后立绘直接站在插图上，160 的半身像放在 880 宽的
        /// 面板里像一张贴纸。六张源图实测 alpha **竖向铺满**（内容高 100%、只有左右留白 77–97%），
        /// 所以按边长放大不会出现「人缩在角落」——`preserveAspect` 会把整幅高度用满。
        ///
        /// 立绘现在贴**主视觉右下角**、不留 inset：它被面板的圆角一起切，像人站在画面里，
        /// 而不是浮在一个方框里。标题因此让到左边，两者不再争同一块高度
        /// （旧写法 `titleBlock = max(PortraitSize, titleHeight)` 会被立绘顶出一条实底带，把插图盖掉一大半）。
        /// `Show` 与 `BuildHero` 现在共用 Show 量出来的那一份标题块：2026-09-13 只改了 Show，
        /// BuildHero 里那份 max 一直留着（208 的立绘把带子顶到 169 高、盖掉 247 高插图的 68%），2026-09-14 统一。
        /// </summary>
        private const float PortraitSize = 208f;

        /// <summary>
        /// 立绘脚下落影的尺寸（相对 <see cref="PortraitSize"/>）。
        /// 抠图直接压在插图上时边缘会和背景糊在一起、人像「浮」着；脚下垫一团横向柔光就钉住了。
        /// 用的是 <see cref="SkyIslandUiArt.GetRadialGlow"/> 那张共享径向图，非等比拉成扁椭圆。
        /// </summary>
        private const float PortraitShadowWidth = 1.10f;
        private const float PortraitShadowHeight = 0.34f;
        private const float TitleMinHeight = 42f;
        // 字号整体上调一档（2026-09-13）：1920×1080 参考画布上 20/21/31 偏小，
        // 玩家反馈「字比较小」。正文与选项各 +2，标题上限 +3、下限 +2。
        private const float TitleFontMin = 24f;
        private const float TitleFontMax = 34f;

        /// <summary>
        /// 正文与选项字号。量高的探针与实际摆放必须用同一个值，所以收成常量。
        /// 正文 24：正文现在只剩一句导语（长文去了官方笔记图鉴与官方对话），
        /// 它是面板里**第一眼要读的主信息**，不该再是一行小灰字。
        /// </summary>
        private const float BodyFont = 24f;
        private const float ChoiceFont = 23f;

        /// <summary>正文压到这个高度还放不下就上滚动条，不再继续压。</summary>
        private const float BodyMinHeight = 76f;
        private const float BodyPreferredMax = 300f;

        private const float ChoiceMinHeight = 48f;
        private const float ChoicePadY = 11f;
        private const float ChoicePadX = 18f;

        /// <summary>
        /// 选项左侧数字键帽占掉的宽度（键帽 24 + 间距 12）。量高与摆放必须扣同一个数，
        /// 否则量出来的折行与实际摆出来的折行对不上；布局属性测试读的也是这个常量。
        /// </summary>
        private const float KeyHintWidth = 36f;
        private const float KeyCapSize = 24f;

        /// <summary>正文右侧给滚动条留的空。<see cref="BossRushUI.ConfigureScrollRect"/> 要求 20px。</summary>
        private const float ScrollbarGutter = 20f;

        /// <summary>
        /// 标题下分隔线的高度。**不能是 1**：图集里的 <c>divider</c>（8×8 / border 2）亮带落在
        /// 可拉伸中心区，rect 高 1 时上下 border 各分到 0.5px、中心区归零，线整条画不出来。
        /// 版式算术（<c>dividerBlock</c>）与布局属性测试都读这一个常量，不要在别处写第二份。
        /// </summary>
        private const float DividerHeight = 8f;

        /// <summary>
        /// 选项错峰入场：第 i 行延迟 <c>i×ChoiceStagger</c> 秒，各自 <c>ChoiceEntrance</c> 秒
        /// ease-out 淡入并上浮 <c>ChoiceEntranceRise</c> 像素。最多 6 个选项，尾巴收在约 0.29 秒。
        ///
        /// 只改 CanvasGroup.alpha 与 anchoredPosition，**不碰 interactable**：玩家第一帧就按数字键
        /// 照常生效。动效绝不能变成输入延迟。重开面板（选项回执）不重播，与打开动画同一条口径。
        /// </summary>
        private const float ChoiceStagger = 0.025f;
        private const float ChoiceEntrance = 0.16f;
        private const float ChoiceEntranceRise = 6f;

        #endregion

        private Canvas canvas;
        private ScrollRect bodyScroll;
        /// <summary>
        /// 这一页的原始参数（正文存未加标签的原文）。换正文（<see cref="SetBodyText"/>）按同一页整页重建，
        /// 量高、挤压与滚动和首次打开是同一份算术。<see cref="Dispose"/> 里清空：选项捕获着回调。
        /// </summary>
        private string shownTitle;
        private string shownText;
        private IList<Choice> shownChoices;
        private Sprite shownPortrait;
        private Sprite shownBanner;
        /// <summary>一次同步点击只提交最后一页：先更新玩法与选项，再用最终回执量高，避免成功操作连续建两张画布。</summary>
        private sealed class PendingPage
        {
            internal string Title, Text;
            internal IList<Choice> Choices;
            internal Sprite Portrait, Banner;
        }
        private bool selecting;
        private PendingPage pendingPage;
        /// <summary>当前面板根。换正文重建后按旧底边摆放（见 SetBodyText）。</summary>
        private RectTransform panelRect;
        private ZombieModeUIHelper.ModalInputLease input;
        /// <summary>注册给官方 HUDManager 的隐藏令牌，即当前面板的 canvas 根。</summary>
        private GameObject hideToken;
        private bool inputSubscribed;

        /// <summary>
        /// 可操作项：只有选项（页脚「继续旅程」已删），键盘的当前项按这个顺序走。
        /// 右上角 ESC 键帽**不在**这里面：它只给鼠标点，键盘关面板走 Esc / OnCancel。
        /// </summary>
        private readonly List<Button> buttons = new List<Button>();
        /// <summary>每个选项的常态行底色。它住在 ColorBlock.normalColor 里，焦点移走时换回它。</summary>
        private readonly List<Color> buttonColors = new List<Color>();
        /// <summary>每个选项行的描边：焦点指示物，当前项换成 Accent、移走时换回 Stroke（见 SetFocused）。</summary>
        private readonly List<Image> buttonStrokes = new List<Image>();
        private int choiceCount;
        private int selected = -1;
        /// <summary>导航键当前按住的方向（-1 上 / 1 下 / 0 中位）。按边沿走一步，见 <see cref="OnNavigate"/>。</summary>
        private int navigateHeld;

        internal bool Visible { get { return canvas != null; } }

        internal void Show(string title, string text, IList<Choice> choices)
        {
            Show(title, text, choices, null, null);
        }

        /// <summary>
        /// 建面板。<paramref name="portrait"/> 与 <paramref name="banner"/> 都可以为 null，
        /// 缺图时布局自动收拢，不留空洞。
        /// </summary>
        internal void Show(string title, string text, IList<Choice> choices,
            Sprite portrait, Sprite banner)
        {
            // 按钮回调内的重开只记参数；RunChoice 收到回执之后一次建完，模态租约在执行动作期间不中断。
            if (selecting)
            {
                pendingPage = new PendingPage { Title = title, Text = text, Choices = choices, Portrait = portrait, Banner = banner };
                return;
            }
            // 选项回执会重开面板（SkyIslandWorldStory.Refreshed），换正文（SetBodyText）也走这里。重开时：
            // 1. 先挂新令牌再摘旧令牌，官方 HUD 全程保持隐藏，不会在两次之间闪回来一下；
            // 2. 不重播打开动画，否则每点一个选项面板都要「弹」一下。
            bool reopening = canvas != null;
            // 3. 保留键盘的当前项：玩家正用 W/S + Enter 连着操作（接委托 → 交付），
            //    旧版每重开一次都丢焦点，下一次 Enter 只会「重新高亮第一项」，白白多按一下还跳回了顶上。
            int previousSelected = reopening ? selected : -1;
            GameObject previousHideToken = hideToken;
            hideToken = null;
            Dispose();
            if (choices == null) choices = new List<Choice>();
            shownTitle = title;
            shownText = text;
            shownChoices = choices;
            shownPortrait = portrait;
            shownBanner = banner;

            canvas = BossRushUI.CreateCanvasRoot("SkyIslandStory", BossRushUILayers.Modal, true);
            BossRushUI.CreateBackdrop(canvas.transform);

            float maxPanelHeight = Mathf.Clamp(
                ZombieModeUIHelper.GetReferenceViewportSize().y - 140f, 520f, 980f);

            // ---- 1. 先把每块的自然高度量出来（此时还没决定面板多高）----
            // 标题压在主视觉上、靠左；立绘贴右下角，所以标题的可用宽度是「面板宽 − 左 inset − 立绘 − 间距」。
            float heroContentWidth = PanelWidth - HeroInset * 2f;
            float titleWidth = portrait != null
                ? PanelWidth - HeroInset - PortraitSize - Gap
                : heroContentWidth;
            TextMeshProUGUI titleText = MakeText(canvas.transform, title, TitleFontMax,
                BossRushUIColors.TextPrimary, TextAlignmentOptions.Left);
            // 标题**先缩字号保一行**，缩到 TitleFontMin 还放不下才折行（海报式主视觉标题的通用写法）。
            // 旧写法开 TMP 自动缩放、却按 TitleFontMax 量高：长英文标题在 34pt 折成两行，
            // 实底带随之占到主视觉的 52–58%，超过「≤ 一半」（2026-09-14 审核 F-11）。
            titleText.enableAutoSizing = false;
            titleText.fontSize = FitTitleFont(titleText, title, titleWidth);
            float titleHeight = Mathf.Max(TitleMinHeight,
                BossRushUI.MeasureTextHeight(titleText, titleWidth, TitleMinHeight));
            // MeasureTextHeight 把溢出改成了 Overflow；折到两行还放不下就省略号，绝不让它画到框外。
            titleText.overflowMode = TextOverflowModes.Ellipsis;

            // ---- 主视觉：标题（与立绘）压在插图上，插图全出血 ----
            // 旧版是「插图一条 + 表头一条」两块各占高度，插图只铺 ContentWidth、
            // 剩下大半个面板是一块纯色板。现在合成一块：插图铺满面板宽、标题压在它的渐隐上。
            // 立绘不再和标题抢同一块高度（它在右边、贴底），所以标题块就是标题本身。
            float titleBlock = titleHeight;
            float heroFloor = Mathf.Max(HeroMinHeight, titleBlock + HeroInset * 2f);
            // 但 hero 至少要装得下立绘，而且立绘头顶要给右上角的 ESC 键帽留出位置：立绘贴底、高度是它的边长，
            // 键帽在右上角 HeroInset 处。只按立绘边长算的话，247 高的主视觉里键帽压住晴禾头顶 6.5 px（2026-09-14 审核 F-18）。
            if (portrait != null) heroFloor = Mathf.Max(heroFloor, PortraitSize + HeroInset + KeyCapSize + 4f);
            float heroArtHeight = 0f;
            if (banner != null && banner.rect.height > 0f)
            {
                float aspect = banner.rect.width / banner.rect.height;
                // **按 PanelWidth 算，不是 ContentWidth**：全出血。
                heroArtHeight = Mathf.Min(PanelWidth / Mathf.Max(0.01f, aspect), HeroMaxHeight);
            }
            float heroHeight = Mathf.Max(heroFloor, heroArtHeight);

            var choiceHeights = new List<float>(choices.Count);
            float choicesHeight = 0f;
            for (int i = 0; i < choices.Count; i++)
            {
                TextMeshProUGUI probe = MakeText(canvas.transform, choices[i].Label, ChoiceFont,
                    BossRushUIColors.TextPrimary, TextAlignmentOptions.Left);
                float h = Mathf.Max(ChoiceMinHeight,
                    BossRushUI.MeasureTextHeight(probe, ChoiceLabelWidth, 26f) + ChoicePadY * 2f);
                // **必须 DestroyImmediate**：`Destroy` 要等到帧末才真正移除，而量高用的探针
                // 此刻是 canvas 的子物体、带着选项文字挂在屏幕正中 —— 用延迟销毁的话，
                // 这一帧会把所有选项文字重叠着闪一下再消失。对象是运行时创建、非 prefab 资产，
                // 这里 DestroyImmediate 安全且确定（口径同 SkyIslandRewardCrate 摘 LootBoxLoader）。
                UnityEngine.Object.DestroyImmediate(probe.gameObject);
                choiceHeights.Add(h);
                choicesHeight += h + (i > 0 ? Gap * 0.5f : 0f);
            }

            // 正文用 TextPrimary 不是 TextSecondary：它是主信息不是注脚。
            TextMeshProUGUI bodyText = MakeText(canvas.transform, text, BodyFont,
                BossRushUIColors.TextPrimary, TextAlignmentOptions.TopLeft);
            // 空正文（居民功能面板常见）收成 0 高，连同它下面那道 Gap 一起省掉，选项紧接分隔线（2026-09-15 第五轮 D5）。
            // 有正文按自然高度量、不再垫到 BodyMinHeight：一行回执就是一行高；BodyMinHeight 只管「挤到多矮就上滚动条」。
            bool hasBody = !string.IsNullOrEmpty(text);
            float bodyNatural = hasBody ? BossRushUI.MeasureTextHeight(bodyText,
                ContentWidth - ScrollbarGutter, 0f) : 0f;
            float bodyGap = hasBody ? Gap : 0f;

            // ---- 2. 按顺序挤：选项不压，先压正文，再压主视觉 ----
            // 主视觉**不再能被整条撤掉**（标题在它上面），只能压到 heroFloor；
            // 从上往下依次是：hero（无上 Pad，全出血）→ Gap → 分隔线 → Gap → 正文
            // → Gap → 选项 → 下 Pad。三个 Gap 里有一个算在 dividerBlock 里；
            // 空正文时正文与它下面那道 Gap 都是 0（bodyGap）。
            //
            // 页脚「继续旅程」已删（2026-09-13）：它的 onClick 就是 Dispose()，与 ESC 完全等价
            // （OnCancel 与 Tick 里的 Escape 分支），纯冗余还占掉 48 + 14 px。
            // 关闭提示改成主视觉右上角的 ESC 键帽。
            float dividerBlock = DividerHeight + Gap;
            float chrome = Pad + heroHeight + dividerBlock + choicesHeight + Gap + bodyGap;
            // 限制的是视口，不能截断自然高度，否则长正文不会建立完整的滚动内容。
            float bodyHeight = Mathf.Min(BodyPreferredMax, bodyNatural);
            float panelHeight = chrome + bodyHeight;
            if (panelHeight > maxPanelHeight)
            {
                float excess = panelHeight - maxPanelHeight;
                float bodyGive = Mathf.Min(excess, Mathf.Max(0f, bodyHeight - BodyMinHeight));
                bodyHeight -= bodyGive;
                excess -= bodyGive;
                if (excess > 0f)
                {
                    float heroGive = Mathf.Min(excess, Mathf.Max(0f, heroHeight - heroFloor));
                    heroHeight -= heroGive;
                    chrome -= heroGive;
                }
                panelHeight = Mathf.Min(maxPanelHeight, chrome + bodyHeight);
            }

            // ---- 3. 从上往下摆 ----
            RectTransform panel = MakeRect(canvas.transform, "StoryPanel", Vector2.zero,
                new Vector2(PanelWidth, panelHeight));
            panelRect = panel;
            Image surface = panel.gameObject.AddComponent<Image>();
            surface.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(surface, 18, BossRushUISkinPart.Panel);

            float cursor = panelHeight * 0.5f;   // 面板局部坐标；hero 全出血，顶上没有 Pad

            // 背景层必须是**第一个**子物体：它在面板底图之上、在所有内容之下。
            // 主视觉也建在里面，这样一个 Mask 同时把整屏底图与插图切成面板的圆角。
            RectTransform background = BuildBackground(panel,
                SkyIslandUiArt.GetPanelBackground(banner));
            BuildHero(background, titleText, portrait, banner, heroHeight, titleWidth, titleBlock, cursor, Dispose);
            cursor -= heroHeight + Gap;

            // 描边在背景层**之后**加，否则会被整屏底图盖住。
            // 走独立 Image + Stroke token：图集里烤进 panel_surface 的那圈内描边，
            // 被 BossRushUIColors.Surface(0.045,0.055,0.065) 乘完之后屏幕上只剩 2.9/255 的通道差
            // （实算），等于没有。面板要有边，边就必须自己有颜色。
            BossRushUI.ApplyPanelStroke(surface, 18, BossRushUISkinPart.Panel, BossRushUIColors.Stroke);

            RectTransform divider = MakeRect(panel, "Divider",
                new Vector2(0f, cursor - DividerHeight * 0.5f), new Vector2(ContentWidth, DividerHeight));
            Image dividerImage = divider.gameObject.AddComponent<Image>();
            dividerImage.color = BossRushUIColors.Divider;
            dividerImage.raycastTarget = false;
            // 旧写法是 ContentWidth×1 的裸 Image（没有 sprite）：1px 的纯色四边形在非整数画布
            // 缩放下会被采样吃掉，时有时无。divider 图（8×8、border 2）中间两行实、上下各一行半透明柔边，
            // 线头两列半透明；没注入图集时 ApplyPanelSkin 的 Rule 档画同形的程序化条。
            BossRushUI.ApplyPanelSkin(dividerImage, 2, BossRushUISkinPart.Rule);
            cursor -= dividerBlock;

            BuildBody(panel, bodyText, bodyHeight, bodyNatural, cursor);
            cursor -= bodyHeight + bodyGap;

            for (int i = 0; i < choices.Count; i++)
            {
                float h = choiceHeights[i];
                BuildChoice(panel, choices[i], i, h, cursor, !reopening);
                cursor -= h + Gap * 0.5f;
            }
            choiceCount = choices.Count;
            if (previousSelected >= 0) Select(previousSelected);

            if (!reopening) BossRushUI.PlayOpenAnimation(panel.gameObject);
            input = ZombieModeUIHelper.ClaimModalInput(canvas.gameObject, "SkyIslandStory");

            // 与官方对话界面同口径：剧情面板开着时官方 HUD（血条、快捷栏）一起淡出。
            // 令牌必须在 Dispose 里成对注销——官方只在事件发生时重算显隐，
            // 令牌对象被销毁并不会让 HUD 自己回来。
            try
            {
                global::HUDManager.RegisterHideToken(canvas.gameObject);
                hideToken = canvas.gameObject;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] 剧情面板隐藏官方 HUD 失败：" + e.Message);
            }
            // 引用判空，理由同 Dispose：旧画布若已被外部销毁，Unity 的 != null 会让它漏注销。
            if (!ReferenceEquals(previousHideToken, null))
            {
                try { global::HUDManager.UnregisterHideToken(previousHideToken); }
                catch (Exception e) { Debug.LogWarning("[SkyIsland] 注销旧的 HUD 隐藏令牌失败：" + e.Message); }
            }
            SubscribeInput();
        }

        /// <summary>选项文字的可用宽度：扣掉左右内边距与左侧键帽。量高与摆放共用。</summary>
        private static float ChoiceLabelWidth
        {
            get { return ContentWidth - ChoicePadX * 2f - KeyHintWidth; }
        }

        /// <summary>
        /// 标题字号：<see cref="TitleFontMax"/> 一行放得下就用它；放不下按宽度等比缩，最小 <see cref="TitleFontMin"/>。
        /// 缩到最小还放不下的，由调用方按这个字号折行量高。
        /// </summary>
        private static float FitTitleFont(TextMeshProUGUI text, string title, float width)
        {
            if (string.IsNullOrEmpty(title) || width <= 1f) return TitleFontMax;
            text.fontSize = TitleFontMax;
            float oneLine = text.GetPreferredValues(title, float.PositiveInfinity, float.PositiveInfinity).x;
            if (oneLine <= width) return TitleFontMax;
            return Mathf.Max(TitleFontMin, Mathf.Floor(TitleFontMax * width / oneLine));
        }

        #region 分块构建

        /// <summary>
        /// 整屏底图层。返回的容器同时承载**底图**与**主视觉**，两者共用一个 Mask 切圆角。
        ///
        /// 【为什么面板底要是一张图】改之前面板只有顶上一条 ContentWidth 宽的插图，
        /// 剩下大半屏是 <c>BossRushUIColors.Surface</c> 的纯色板——玩家一眼看出「这是代码画的方框」。
        /// 现在整块面板底铺的是那一区场景横幅派生出来的模糊底图
        /// （<c>tools/gen_sky_island_panel_backgrounds.py</c>，220×236，为模糊而生所以小图足够），
        /// 每个区域有自己的色调：钟庭偏暖、听雨洞偏青。
        ///
        /// 【为什么要 Mask】底图是一张矩形贴图，直接铺上去四角会戳出面板的圆角。
        /// Mask 以面板同一张九宫格底图当模板（<c>showMaskGraphic=false</c>，模板本身不画、只写模板缓冲）。
        /// 容器向内缩 1px：模板是二值的，被切出来的边是硬边；留 1px 让面板自己那圈带抗锯齿的
        /// 圆角露在外面，接缝就看不出来了（描边也正好压在这一圈上）。
        ///
        /// fail-open：底图缺失时什么都不铺，面板退回纯色底，照常能开。
        /// </summary>
        private static RectTransform BuildBackground(RectTransform panel, Sprite background)
        {
            RectTransform clip = MakeRect(panel, "Background", Vector2.zero, Vector2.zero);
            clip.anchorMin = Vector2.zero;
            clip.anchorMax = Vector2.one;
            clip.offsetMin = new Vector2(1f, 1f);
            clip.offsetMax = new Vector2(-1f, -1f);

            Image stencil = clip.gameObject.AddComponent<Image>();
            stencil.color = Color.white;
            stencil.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(stencil, 18, BossRushUISkinPart.Panel);
            Mask mask = clip.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            if (background != null)
            {
                Image art = Stretched(clip, "Art");
                art.sprite = background;
                art.type = Image.Type.Simple;
                // 底图已经模糊过，非等比拉伸看不出来；preserveAspect 反而会在两侧留出空档。
                art.preserveAspect = false;

                // 压暗层：压暗量跟正文对比度绑定，所以归代码管、不烤进图里
                // （同 BossRushUI_图集规格.md 里「描边不要烤进九宫格」那条）。
                Image tint = Stretched(clip, "Tint");
                Color tinted = BossRushUIColors.Surface;
                tinted.a = BackgroundTintAlpha;
                tint.color = tinted;
            }
            return clip;
        }

        /// <summary>拉满父矩形、不吃点击的 Image。</summary>
        private static Image Stretched(RectTransform parent, string name)
        {
            RectTransform rect = MakeRect(parent, name, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// 主视觉：**全出血**的区域插图 + 压在它下部的标题（与立绘）。
        ///
        /// 建在背景层里，所以顶上两个角跟着面板一起被切圆。<paramref name="top"/> 是面板顶边
        /// （不是 <c>顶边 - Pad</c>）：插图左右顶到面板边、上边顶到面板顶边，这就是「出血」。
        ///
        /// 【标题为什么能压在图上还读得清】底部渐隐的高度取
        /// <c>max(hero 高 × HeroFadeFraction, 标题块 + 上下 inset)</c>——
        /// 后面那一半是兜底：两行的长标题不会有一行骑到亮云上。渐隐本身是
        /// <see cref="SkyIslandUiArt.GetBannerFade"/> 的真渐变（下端接近面板底色、上端全透），
        /// 不是一条半透明横条（那会有上下两条硬边）。
        ///
        /// 没有插图时（插图缺失的 fail-open 路径）hero 退化成一条只有底图的表头，标题照常压在渐隐上。
        ///
        /// <paramref name="titleBlock"/> 由 Show 传进来，**这里不另算**：立绘贴右下角、不和标题抢高度，
        /// 标题块就是标题本身。旧写法在这里又按 <c>max(PortraitSize, titleHeight)</c> 算了一遍，
        /// 居民面板的实底带被顶到 169 高、盖掉 247 高插图的 68%；而 Show、布局属性测试与离线预览都按标题本身算，
        /// 三处都看不出来。<paramref name="close"/> 接到右上角的 ESC 键帽上。
        /// </summary>
        private static void BuildHero(RectTransform background, TextMeshProUGUI title, Sprite portrait,
            Sprite banner, float height, float titleWidth, float titleBlock, float top,
            UnityEngine.Events.UnityAction close)
        {
            RectTransform hero = MakeRect(background, "Hero",
                new Vector2(0f, top - height * 0.5f), new Vector2(PanelWidth, height));

            if (banner != null)
            {
                Image art = Stretched(hero, "Art");
                art.sprite = banner;
                art.type = Image.Type.Simple;
                // 直接拉伸铺满（不裁剪）而不是留边：preserveAspect 会在两侧留出背景色，
                // 而面板底色和插图色调不一致，看着像图没铺满。hero 高度随面板变，插图会有轻微的纵向拉伸。
                art.preserveAspect = false;
            }

            float titleHeight = titleBlock;
            // 标题在 titleBlock 里垂直居中，所以它的上沿离 hero 底边 inset + titleBlock/2 + titleHeight/2；
            // 再加一个 inset 当余量，就是实底带要有的高度。
            // 立绘不算进来：它是抠图、贴右下角，不需要垫暗（口径见 PortraitSize）。
            float bandHeight = Mathf.Min(height,
                HeroInset * 2f + titleBlock * 0.5f + titleHeight * 0.5f);

            RectTransform band = MakeRect(hero, "TitleBand",
                new Vector2(0f, -height * 0.5f + bandHeight * 0.5f),
                new Vector2(PanelWidth, bandHeight));
            Image bandImage = band.gameObject.AddComponent<Image>();
            Color banded = BossRushUIColors.Surface;
            banded.a = HeroTitleBandAlpha;
            bandImage.color = banded;
            bandImage.raycastTarget = false;

            // 带子上方再淡出到全透，免得带子的上沿是一条硬边。
            Sprite gradient = SkyIslandUiArt.GetBannerFade();
            float falloff = Mathf.Min(height - bandHeight,
                Mathf.Max(HeroFadeMin, height * HeroFadeFraction - bandHeight));
            if (gradient != null && falloff > 1f)
            {
                RectTransform fade = MakeRect(hero, "Fade",
                    new Vector2(0f, -height * 0.5f + bandHeight + falloff * 0.5f),
                    new Vector2(PanelWidth, falloff));
                Image fadeImage = fade.gameObject.AddComponent<Image>();
                fadeImage.sprite = gradient;
                fadeImage.type = Image.Type.Simple;
                fadeImage.raycastTarget = false;
                // 渐变图的下端是**全不透明**的面板底色，而带子只有 0.82；直接接会有一条亮缝。
                // 让渐变整体乘上同一个 alpha，两者在接缝处就完全一致。
                Color faded = Color.white;
                faded.a = HeroTitleBandAlpha;
                fadeImage.color = faded;
            }

            // 立绘与标题都建在渐隐**之后**，所以画在它上面。
            float left = -PanelWidth * 0.5f + HeroInset;
            float bottom = -height * 0.5f + HeroInset;

            if (portrait != null)
            {
                // 【为什么没有底板了】立绘源图就是 512×512 的**抠图**（#ff00ff 色键 + soft matte，
                // 见 tools/gen_sky_island_ui_art.py；实测四角 alpha=0、透明像素占 35–50%）。
                // 旧写法在它底下垫了一块 SurfaceRaised 圆角板 + 描边，于是玩家看到的是
                // 「一个黑方块里贴了张小图」而不是「一个人站在那里」。板子纯属多余。
                //
                // 现在贴主视觉的**右下角、不留 inset**：立绘被面板的圆角一起切（背景层的 Mask），
                // 像人站在画面里。标题因此让到左边，两者不再争同一块高度。
                float portraitX = PanelWidth * 0.5f - PortraitSize * 0.5f;
                float portraitY = -height * 0.5f + PortraitSize * 0.5f;

                // 落影先建、画在立绘下面：抠图直接压在插图上，边缘会和背景糊在一起、人像「浮」着。
                // 脚下垫一团横向柔光就钉住了。非等比拉成扁椭圆，共享 SkyIslandUiArt 那张径向图。
                Sprite glow = SkyIslandUiArt.GetRadialGlow();
                if (glow != null)
                {
                    RectTransform shadow = MakeRect(hero, "PortraitShadow",
                        new Vector2(portraitX, -height * 0.5f),
                        new Vector2(PortraitSize * PortraitShadowWidth,
                                    PortraitSize * PortraitShadowHeight));
                    Image shadowImage = shadow.gameObject.AddComponent<Image>();
                    shadowImage.sprite = glow;
                    shadowImage.color = BossRushUIColors.Backdrop;
                    shadowImage.raycastTarget = false;
                }

                RectTransform face = MakeRect(hero, "Portrait",
                    new Vector2(portraitX, portraitY), new Vector2(PortraitSize, PortraitSize));
                Image faceImage = face.gameObject.AddComponent<Image>();
                faceImage.sprite = portrait;
                faceImage.preserveAspect = true;   // 立绘不能拉变形
                faceImage.raycastTarget = false;
            }

            title.rectTransform.SetParent(hero, false);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax =
                title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            title.rectTransform.sizeDelta = new Vector2(titleWidth, titleHeight);
            title.rectTransform.anchoredPosition =
                new Vector2(left + titleWidth * 0.5f, bottom + titleBlock * 0.5f);
            // 压在图上的标题一律左对齐：这是海报式主视觉的通用写法，居中会把画面挤没。
            title.alignment = TextAlignmentOptions.Left;

            // 关闭提示，同时是**可点的**关闭按钮。页脚「继续旅程」删掉之后 ESC 是唯一出口；
            // 零选项的面板（收下信之后、纪念物）连一个可导航项都没有，只认键盘 ESC 的话，
            // 纯鼠标玩家看得见键帽却点不动它（旧版 raycastTarget=false）。
            // 放主视觉右上角，不占任何版式高度；建在最后所以画在立绘之上。
            // 它不进 buttons：键盘与手柄照旧走 Esc / OnCancel，数字键与 W/S 的项数不变。
            Image escCap = KeyCap(hero, "ESC", 44f, new Vector2(
                PanelWidth * 0.5f - HeroInset - 22f,
                height * 0.5f - HeroInset - KeyCapSize * 0.5f), BossRushUIColors.TextPrimary);
            escCap.raycastTarget = true;
            Button closeButton = escCap.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = escCap;
            closeButton.navigation = new Navigation { mode = Navigation.Mode.None };
            // 三态照 ZombieModeUIHelper.ApplyButtonColors 的口径（Graphic 置白、绝对色进 ColorBlock），
            // 但不直接调它：它会把名为 Text 的子物体改成按钮字色，键帽上的字色由 KeyCap 定。
            escCap.color = Color.white;
            ColorBlock escColors = closeButton.colors;
            escColors.normalColor = BossRushUIColors.Surface;
            escColors.highlightedColor = BossRushUI.GetHoverColor(BossRushUIColors.Surface);
            escColors.pressedColor = BossRushUI.GetPressedColor(BossRushUIColors.Surface);
            escColors.selectedColor = BossRushUIColors.Surface;
            escColors.colorMultiplier = 1f;
            escColors.fadeDuration = 0.08f;
            closeButton.colors = escColors;
            // 刚挂上的 Button 已把渲染色置白，赋完 colors 会从白渐变到常态色（0.08 秒）。回执重开面板不播入场动画，
            // 这几帧白底会直接露出来——立即落到常态色（2026-09-14 审核 F-21，选项行同理）。
            escCap.CrossFadeColor(escColors.normalColor * escColors.colorMultiplier, 0f, true, true);
            if (close != null) closeButton.onClick.AddListener(close);
        }

        private void BuildBody(RectTransform panel, TextMeshProUGUI text, float height,
            float natural, float top)
        {
            RectTransform viewport = MakeRect(panel, "Body",
                new Vector2(0f, top - height * 0.5f), new Vector2(ContentWidth, height));
            text.rectTransform.SetParent(viewport, false);
            text.rectTransform.anchorMin = new Vector2(0f, 1f);
            text.rectTransform.anchorMax = new Vector2(0f, 1f);
            text.rectTransform.pivot = new Vector2(0f, 1f);
            text.rectTransform.anchoredPosition = Vector2.zero;
            text.rectTransform.sizeDelta = new Vector2(ContentWidth - ScrollbarGutter,
                Mathf.Max(height, natural));

            // 只有真的放不下才挂滚动，免得短正文也吃一个滚动条的宽度。
            if (natural <= height + 0.5f) return;
            viewport.gameObject.AddComponent<RectMask2D>();
            bodyScroll = viewport.gameObject.AddComponent<ScrollRect>();
            bodyScroll.content = text.rectTransform;
            bodyScroll.viewport = viewport;
            BossRushUI.ConfigureScrollRect(bodyScroll);
        }

        private void BuildChoice(RectTransform panel, Choice choice, int index, float height, float top,
            bool animate)
        {
            RectTransform rect = MakeRect(panel, "Choice",
                new Vector2(0f, top - height * 0.5f), new Vector2(ContentWidth, height));
            Image image = rect.gameObject.AddComponent<Image>();
            // 行底不铺满：让区域底图透出来，行才像浮在这张图上。alpha 的下界见 ChoiceRowAlpha。
            // 行底色**不写进 image.color**：它住在 ColorBlock.normalColor 里（见下面的 ApplyButtonColors）。
            Color rowColor = BossRushUIColors.SurfaceRaised;
            rowColor.a = ChoiceRowAlpha;
            // 卡片档（panel_raised），不是按钮档：选项是列表行。
            BossRushUI.ApplyPanelSkin(image, 10, BossRushUISkinPart.Card);
            // 描边是这一行「是一个独立可点区域」的视觉证据，也是焦点指示物：当前项的边换成 Accent（见 SetFocused）。
            // SurfaceRaised 对面板底 Surface 实算只有 1.03:1（亮云海）/ 1.07:1（暗地形）——不画边的话玩家看到的只是几行浮着的字。
            Image stroke = BossRushUI.ApplyPanelStroke(image, 10, BossRushUISkinPart.Card, BossRushUIColors.Stroke);
            buttonStrokes.Add(stroke);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            // 常态 / 悬停 / 按下住在 ColorBlock 里、Graphic 置白（ZombieModeUIHelper.ApplyButtonColors 同一个口径）。
            // 旧写法行底写在 image.color、悬停又给绝对色 GetHoverColor(SurfaceRaised)：ColorTint 两者相乘，
            // 鼠标移上去反而变暗；键盘 Select() 却换成更亮的底色。现在两边都是 FocusColor。
            ZombieModeUIHelper.ApplyButtonColors(button, rowColor, FocusColor(rowColor),
                BossRushUI.GetDisabledColor(rowColor));
            // 不进 EventSystem 的选中态：当前项由 Select() 画；鼠标点过之后 EventSystem 的 selected
            // 会一直挂在那一行上，和当前项变成两处高亮（导航本来就走官方 UIInputManager，不走 Selectable）。
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            // 刚挂上的 Button 已把渲染色置白，ApplyButtonColors 赋完 colors 会从白渐变到行底色（0.08 秒）；
            // 回执重开面板不播错峰入场，这几帧整排白底会直接露出来——立即落到常态色（2026-09-14 审核 F-21）。
            image.CrossFadeColor(rowColor, 0f, true, true);
            // 悬停即选中：鼠标停在哪一行，键盘的当前项就跟到哪一行，两者永远是同一个（主流 PC 菜单的写法）。
            // 用内置 EventTrigger 只订 PointerEnter；点击仍由 Button 自己处理（ExecuteEvents 对同一物体上的每个处理器都派发）。
            int row = index;
            UnityEngine.EventSystems.EventTrigger hover = rect.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            UnityEngine.EventSystems.EventTrigger.Entry enter = new UnityEngine.EventSystems.EventTrigger.Entry();
            enter.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            enter.callback.AddListener(delegate { if (Visible && row < buttons.Count) Select(row); });
            hover.triggers.Add(enter);

            // 数字键帽：把「按几」直接画在选项旁边，而不是让玩家去猜有没有快捷键。只给 1–9。
            if (index < 9)
                KeyCap(rect, (index + 1).ToString(), KeyCapSize,
                    new Vector2(-ContentWidth * 0.5f + ChoicePadX + KeyCapSize * 0.5f, 0f));

            TextMeshProUGUI label = MakeText(rect, choice.Label, ChoiceFont,
                BossRushUI.GetButtonTextColor(BossRushUIColors.SurfaceRaised),
                TextAlignmentOptions.Left);
            label.rectTransform.sizeDelta = new Vector2(ChoiceLabelWidth, height - ChoicePadY * 2f);
            label.rectTransform.anchoredPosition = new Vector2(KeyHintWidth * 0.5f, 0f);
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;

            Func<string> select = choice.Select;
            // 回调返回 null 表示「正文不动」：跳到子页、返回上一页时，新开面板自己的正文就是对的，不能被覆盖成空。
            button.onClick.AddListener(delegate { RunChoice(select); });
            Register(button, rowColor);
            if (animate)
                BossRushUIEntranceAnimation.Play(rect.gameObject, index * ChoiceStagger,
                    ChoiceEntrance, ChoiceEntranceRise);
        }


        /// <summary>
        /// 键帽底板 + 字。默认**不吃点击**：数字键帽画在选项行里，点击要归整行。
        /// 右上角 ESC 键帽由 BuildHero 拿返回值另接成可点的关闭按钮。
        /// </summary>
        private static Image KeyCap(RectTransform parent, string key, float width, Vector2 position, Color glyphColor)
        {
            RectTransform cap = MakeRect(parent, "KeyCap", position, new Vector2(width, KeyCapSize));
            Image capImage = cap.gameObject.AddComponent<Image>();
            capImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(capImage, 6);
            capImage.raycastTarget = false;
            TextMeshProUGUI glyph = MakeText(cap, key, 13f, glyphColor, TextAlignmentOptions.Center);
            glyph.rectTransform.sizeDelta = new Vector2(width, KeyCapSize);
            glyph.enableWordWrapping = false;
            return capImage;
        }

        /// <summary>
        /// 数字键帽：坐在压暗过的选项行里，次级字色余量足够。右上角 ESC 键帽压在**没模糊过的插图**上
        /// （最亮处接近纯白），13px 的 TextSecondary 按线性色彩空间只有 3.9:1、悬停 3.3:1，
        /// 那一个由 BuildHero 传 TextPrimary（2026-09-14 审核 F-19）。
        /// </summary>
        private static Image KeyCap(RectTransform parent, string key, float width, Vector2 position)
        {
            return KeyCap(parent, key, width, position, BossRushUIColors.TextSecondary);
        }

        /// <summary>
        /// 合并一次点击里的「更新选项 + 写回执」。重入、异常与主动关闭不留下待提交页面；
        /// 跳页返回 null 时保留目标页导语，回执改变正文时按点击前的底边摆放，不先排一张临时导语页。
        /// </summary>
        private void RunChoice(Func<string> select)
        {
            if (!Visible || selecting || select == null) return;
            bool anchored = panelRect != null;
            float bottom = anchored ? panelRect.anchoredPosition.y - panelRect.sizeDelta.y * 0.5f : 0f;
            string reply;
            PendingPage page;
            selecting = true;
            try
            {
                reply = select();
                page = pendingPage;
            }
            finally
            {
                selecting = false;
                pendingPage = null;
            }
            if (!Visible) return;
            if (page == null)
            {
                if (reply != null) SetBodyText(reply);
                return;
            }
            bool changedBody = reply != null && !string.Equals(reply, page.Text, StringComparison.Ordinal);
            Show(page.Title, reply ?? page.Text, page.Choices, page.Portrait, page.Banner);
            if (changedBody) RestoreBottom(anchored, bottom);
        }

        /// <summary>
        /// 换正文：**按新正文整页重建**（走 <c>Show</c>）。量高、挤压与滚动和首次打开是同一份算术——
        /// 上限一致、超出才滚动，空正文收成 0 高。
        ///
        /// 【为什么不在原视口里换字】视口是打开那一刻按导语量的：手记子页（岛上的灯 / 群岛之物 / 这一趟）、
        /// 名册翻页、谜题回执一换进来就只剩一两行在滚（2026-09-15 第五轮 D3），一行合成回执底下又空出按长正文量的一大块（D5）。
        ///
        /// 重建走 Show 的 reopening 分支：不重播打开与错峰动画、保留键盘当前项、先挂新 HUD 令牌再摘旧的，
        /// 与选项回执重开（SkyIslandWorldStory.Refreshed）同一条路。重建后按**旧底边**摆放：选项行尽量留在原处，
        /// 鼠标底下还是刚点的那一行（悬停即选中），长高的部分往上长；贴到屏幕上下各留 Pad 的边界时整体挪回来。
        /// 面板已经收起（挑战开始、引风）时什么都不做：回执由调用方改走字幕（SkyIslandPlaytimeFlowGuard §3）。
        /// </summary>
        private void SetBodyText(string value)
        {
            if (!Visible) return;
            value = value ?? string.Empty;
            // 跳子页、返回上一页时回调返回的就是新页自己的正文：同一句不再重建。
            if (string.Equals(value, shownText, StringComparison.Ordinal)) return;
            bool anchored = panelRect != null;
            float bottom = anchored ? panelRect.anchoredPosition.y - panelRect.sizeDelta.y * 0.5f : 0f;
            Show(shownTitle, value, shownChoices, shownPortrait, shownBanner);
            RestoreBottom(anchored, bottom);
        }

        private void RestoreBottom(bool anchored, float bottom)
        {
            if (!anchored || panelRect == null) return;
            float height = panelRect.sizeDelta.y;
            float room = Mathf.Max(0f, (ZombieModeUIHelper.GetReferenceViewportSize().y - height) * 0.5f - Pad);
            panelRect.anchoredPosition = new Vector2(0f, Mathf.Clamp(bottom + height * 0.5f, -room, room));
        }

        #endregion

        #region 键盘与手柄

        private void Register(Button button, Color color)
        {
            buttons.Add(button);
            buttonColors.Add(color);
        }

        /// <summary>
        /// 键盘方向键的当前项。**与鼠标悬停同一个焦点色**：只把这一行 ColorBlock 的 normalColor
        /// 换成 <see cref="FocusColor"/>，由 Button 自己的 ColorTint 过渡过去——写法同
        /// <c>ZombieModeUIHelper.SetButtonBaseColor</c>（页签、拍铃靠它变色）。
        /// 不去动 EventSystem 的选中态：选项的 navigation 是 None（见 BuildChoice）。
        /// </summary>
        private void Select(int index)
        {
            if (buttons.Count == 0) return;
            index = Mathf.Clamp(index, 0, buttons.Count - 1);
            if (selected >= 0 && selected < buttons.Count) SetFocused(selected, false);
            selected = index;
            SetFocused(index, true);
        }

        private void SetFocused(int index, bool focused)
        {
            Button button = buttons[index];
            if (button == null) return;
            ColorBlock colors = button.colors;
            colors.normalColor = focused ? FocusColor(buttonColors[index]) : buttonColors[index];
            button.colors = colors;
            // 焦点指示物是行边（见 ChoiceFocusLift）：当前项 Accent，移走换回 Stroke。
            Image stroke = index < buttonStrokes.Count ? buttonStrokes[index] : null;
            if (stroke != null) stroke.color = focused ? BossRushUIColors.Accent : BossRushUIColors.Stroke;
        }

        /// <summary>焦点色：鼠标悬停与键盘当前项共用这一个。系数与 WCAG 实算见 <see cref="ChoiceFocusLift"/>。</summary>
        private static Color FocusColor(Color row)
        {
            Color focus = Color.Lerp(row, Color.white, ChoiceFocusLift);
            focus.a = ChoiceFocusAlpha;
            return focus;
        }

        /// <summary>执行第 index 项。回调可能重开或关掉面板，调用方执行完必须立刻返回。</summary>
        private void Press(int index)
        {
            if (index < 0 || index >= buttons.Count) return;
            Button button = buttons[index];
            if (button == null || !button.interactable) return;
            button.onClick.Invoke();
        }

        private void SubscribeInput()
        {
            if (inputSubscribed) return;
            try
            {
                global::UIInputManager.OnNavigate += OnNavigate;
                global::UIInputManager.OnConfirm += OnConfirm;
                global::UIInputManager.OnCancel += OnCancel;
                inputSubscribed = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] 剧情面板订阅官方 UI 输入失败，手柄将无法操作面板：" + e.Message);
            }
        }

        private void UnsubscribeInput()
        {
            if (!inputSubscribed) return;
            inputSubscribed = false;
            try
            {
                global::UIInputManager.OnNavigate -= OnNavigate;
                global::UIInputManager.OnConfirm -= OnConfirm;
                global::UIInputManager.OnCancel -= OnCancel;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIsland] 剧情面板退订官方 UI 输入失败：" + e.Message);
            }
        }

        /// <summary>
        /// 导航**按边沿走一步**。官方 `UIInputManager.Bind` 把 UI_Navigate 的 started / performed / canceled
        /// 三个阶段全订上了，`OnInputActionNavigate` 又不看阶段、每次新建一个事件对象（Use 去不了重）；
        /// 而 UI_Navigate 是 Value 型 Vector2（W/S 组合键），Input System 在同一次处理里先 Started 再 Performed。
        /// 于是按一下 W/S 会收到两条同向事件——逐条走一步就是一按跳两格。回到中位（|y| ≤ 0.5）才重新武装。
        /// </summary>
        private void OnNavigate(global::UIInputEventData data)
        {
            if (!Visible || data == null || buttons.Count == 0) return;
            int step = data.vector.y > 0.5f ? -1 : (data.vector.y < -0.5f ? 1 : 0);
            if (step == 0) { navigateHeld = 0; return; }
            data.Use();
            if (navigateHeld == step) return;
            navigateHeld = step;
            Select(selected < 0 ? (step > 0 ? 0 : buttons.Count - 1) : selected + step);
        }

        private void OnConfirm(global::UIInputEventData data)
        {
            if (!Visible || data == null) return;
            // 还没有当前项时，第一次确认只把焦点放到第一项：先让玩家看清自己选中了什么。
            // 默认键位下交互（F）与确认（Enter）不是同一个键，而且交互完成（InteractableBase.OnTimeOut）
            // 发生在 CA_Interact 的 Update 里、晚于同一次按键的输入派发，同帧误触发本来就不会出现；
            // 这一步留作玩家改键把两者绑到同一个键时的保险。
            if (selected < 0) Select(0);
            else Press(selected);
            data.Use();
        }

        private void OnCancel(global::UIInputEventData data)
        {
            if (!Visible) return;
            Dispose();
            if (data != null) data.Use();
        }

        internal void Tick()
        {
            if (!Visible) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Dispose(); return; }
            // 数字键直选：主流 PC 对话界面的通用写法，手不必离开键盘去找鼠标。
            int keyed = Mathf.Min(choiceCount, 9);
            for (int i = 0; i < keyed; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Press(i);
                    return;
                }
            }
            ZombieModeUIHelper.EnforceModalInputPause();
        }

        #endregion

        public void Dispose()
        {
            pendingPage = null;
            UnsubscribeInput();
            if (input != null) input.Release();
            input = null;
            // 用**引用**判空，不用 Unity 的 `!= null`：画布若已随场景卸载被销毁，Unity 判它为空，旧写法就会跳过注销，
            // 而官方 `HUDManager.hideTokens` 是静态列表，死条目会一直留在里面（官方显隐判定虽然跳过已销毁条目，
            // 但列表只增不减，也少了一次 onHideTokensChanged 让官方 HUD 立刻重算）。
            if (!ReferenceEquals(hideToken, null))
            {
                try { global::HUDManager.UnregisterHideToken(hideToken); }
                catch (Exception e) { Debug.LogWarning("[SkyIsland] 注销 HUD 隐藏令牌失败：" + e.Message); }
                hideToken = null;
            }
            if (canvas != null) UnityEngine.Object.Destroy(canvas.gameObject);
            canvas = null;
            panelRect = null;
            shownTitle = null;
            shownText = null;
            shownChoices = null;
            shownPortrait = null;
            shownBanner = null;
            bodyScroll = null;
            buttons.Clear();
            buttonColors.Clear();
            buttonStrokes.Clear();
            choiceCount = 0;
            selected = -1;
            navigateHeld = 0;
        }

        #region 基础构件

        private static RectTransform MakeRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string value, float size,
            Color color, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI text = MakeRect(parent, "Text", Vector2.zero, new Vector2(ContentWidth, 40f))
                .gameObject.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(text);
            text.fontSize = size;
            text.text = KeepCountsTogether(value);
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 「名称 + 计数」不许被折行拆开（2026-09-15 第五轮 D8）：「（已读 / 0/4）」「到 / 访区域 5/12」「Brass / Scrap 0/3」「Greenear / Sheaf 2」。
        /// 分隔符（行首、换行、「· 」、全角或半角左括号、全角冒号、「: 」）之后、以计数结尾（后面紧跟行尾、换行、「 ·」或右括号）
        /// 的一小段包进 TMP 的 &lt;nobr&gt;：中文字之间 TMP 可以任意断，U+00A0 只管得住空格，所以用标签。
        /// 段长上限 24 字：最长一段加计数在正文与选项的一行里都放得下（布局属性测试按这个上限复算），TMP 不会被迫逐字断。
        /// 规则文案本身不带标签（隔离回归逐字核对的是纯文本）；F3 验收读面板文字时剥掉标签再匹配。
        /// </summary>
        private static readonly Regex CountedRun = new Regex(
            @"(^|\n|· |（|\(|：|: )([^\n·（）()：:<>]{1,24} \d+(?:/\d+)?)(?=$|\n| ·|）|\))",
            RegexOptions.CultureInvariant);

        private static string KeepCountsTogether(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOf("<nobr>", StringComparison.Ordinal) >= 0) return value;
            return CountedRun.Replace(value, "$1<nobr>$2</nobr>");
        }

        #endregion
    }

    public sealed class SkyIslandStoryInteractable : BossRushBuildingInteractableBase
    {
        private string label;
        private Action interact;
        protected override string InteractNameKey
        {
            get
            {
                string key = "BossRush_SkyIsland_Story_" + name;
                LocalizationHelper.InjectLocalization(key, label ?? L10n.T("群岛记事", "Archipelago record"));
                return key;
            }
        }
        protected override string LogPrefix { get { return "[SkyIsland] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIsland]"; } }
        protected override bool IsBuildingInteractable() { return interact != null; }
        internal void Bind(string title, Action action) { label = title; interact = action; }
        /// <summary>切了语言：换交互名与头顶的字，回调不动（标题由 owner 按当前语言重取）。</summary>
        internal void Relabel(string title)
        {
            label = title;
            Transform sign = transform.Find("Label");
            TextMeshPro text = sign != null ? sign.GetComponent<TextMeshPro>() : null;
            if (text != null) text.text = title;
        }
        protected override void OnInteractCompleted() { if (interact != null) interact(); }
        internal static GameObject Create(Transform parent, Vector3 position, string name, string title, Action action)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = position;
            go.layer = LayerMask.NameToLayer("Interactable");
            BoxCollider trigger = go.AddComponent<BoxCollider>(); trigger.isTrigger = true;
            trigger.center = Vector3.up; trigger.size = new Vector3(3, 2, 3);
            go.AddComponent<SkyIslandStoryInteractable>().Bind(title, action);
            GameObject sign = new GameObject("Label", typeof(TextMeshPro));
            sign.transform.SetParent(go.transform, false); sign.transform.localPosition = Vector3.up * 2.5f;
            sign.transform.rotation = Quaternion.Euler(60, 0, 0);
            TextMeshPro text = sign.GetComponent<TextMeshPro>(); text.font = ZombieModeUIHelper.GetGameFont();
            text.text = title; text.fontSize = 3; text.alignment = TextAlignmentOptions.Center;
            // 纪念物上方的字不再是远远就亮着的黄字：点位上的光已经把「那里有东西」说清楚了，
            // 字只在走近时浮现，用正文色而不是警示色（它不是警告）。
            text.color = BossRushUIColors.TextPrimary;
            text.rectTransform.sizeDelta = new Vector2(18, 5);
            SkyIslandProximityLabel.Attach(sign, 6f, 11f);
            return go;
        }
    }
}
