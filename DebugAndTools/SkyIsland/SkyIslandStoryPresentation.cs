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
//   顺带记下**官方任务系统 `Duckov.Quests` 刻意不接**：`Quest`/`Task` 是 MonoBehaviour prefab、
//   `QuestGiverID` 是写死的 enum（没有 mod 的位置）、`QuestManager` 会把 mod 任务序列化进
//   官方存档键（卸载 mod 后官方对缺失 id 打 LogError，属 AGENTS §10 需 owner 签字），
//   而岛上委托是按出击计、不进存档的。完整理由归档在
//   `tests/SkyIslandOfficialApiReuseGuard.py` 的文件头与 `CODE_REVIEW_FINDINGS.md`。
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
//   面板高度被 `GetReferenceViewportSize()` 夹住，容不下时**按固定优先级**收缩：
//       页脚 > 选项 > 标题 > 正文 > 插图
//   正文收缩到下限后套 ScrollRect（共享 `ConfigureScrollRect`），滚动而不是溢出。
//   这样「文字超出 UI」不再是调参问题，而是结构上不可能发生。
//
// 【操作与官方界面对齐】
//   - 鼠标、数字键 1–9（选项左侧有键帽）、ESC 关闭；
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
        /// 即使底下是纯白（亮度 1.0），标题也有 10.7:1；插图最亮那张的 p99（0.747）下是 12.7:1。
        /// 带子留 18% 透光，插图的颜色仍然透得出来，不是一条纯色横杠。
        /// </summary>
        private const float HeroTitleBandAlpha = 0.82f;
        private const float HeroFadeMin = 56f;

        /// <summary>
        /// 整屏底图的压暗量。**不烤进图里**，因为压暗量是跟正文对比度绑定的，
        /// 必须跟着 <see cref="BossRushUIColors.Surface"/> 走（口径同 BossRushUI_图集规格.md
        /// 里「描边不要烤进九宫格」那条）。
        ///
        /// 0.72 是按**实测**最坏情况定的：13 张模糊底图里最亮那张（skyisland_bg_S2）的 p99 亮度是
        /// 0.747，压完之后正文 TextSecondary 仍有约 5.0:1，过 4.5:1。
        /// 中位亮度只有 0.098–0.397，所以绝大多数区域实际余量大得多。
        /// 再往下压就守不住了；再往上压图就看不见了——那正是这一轮要消灭的「纯色板」。
        /// </summary>
        private const float BackgroundTintAlpha = 0.72f;

        /// <summary>
        /// 选项行底的不透明度。**刻意不满**：让区域底图从行底透出来，行才像「浮在这张图上」，
        /// 而不是一排糊在图上的黑块。
        ///
        /// 0.78 是下界不是随手取的：再透一点，描边对行底的对比度就跌破 WCAG 1.4.11 的 3:1
        /// （实算 0.78 → 3.09:1、0.65 → 2.96:1），而那圈边是「这一行是可点控件」的唯一证据。
        /// 标签文字这边余量很大（13:1 以上），瓶颈自始至终是边。
        /// </summary>
        private const float ChoiceRowAlpha = 0.78f;

        /// <summary>
        /// 立绘在主视觉里的边长。
        ///
        /// 208 而不是原来的 160：去掉圆角底板之后立绘直接站在插图上，160 的半身像放在 880 宽的
        /// 面板里像一张贴纸。六张源图实测 alpha **竖向铺满**（内容高 100%、只有左右留白 77–97%），
        /// 所以按边长放大不会出现「人缩在角落」——`preserveAspect` 会把整幅高度用满。
        ///
        /// 立绘现在贴**主视觉右下角**、不留 inset：它被面板的圆角一起切，像人站在画面里，
        /// 而不是浮在一个方框里。标题因此让到左边，两者不再争同一块高度
        /// （旧写法 `titleBlock = max(PortraitSize, titleHeight)` 会被 160 的立绘顶出一条
        /// 204 的实底带，把 247 高的插图盖掉 83%）。
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
        private TextMeshProUGUI body;
        private RectTransform bodyViewport;
        private ScrollRect bodyScroll;
        private ZombieModeUIHelper.ModalInputLease input;
        /// <summary>注册给官方 HUDManager 的隐藏令牌，即当前面板的 canvas 根。</summary>
        private GameObject hideToken;
        private bool inputSubscribed;

        /// <summary>可操作项：选项按顺序在前，页脚「继续旅程」在最后。键盘/手柄的当前项按这个顺序走。</summary>
        private readonly List<Button> buttons = new List<Button>();
        private readonly List<Image> buttonImages = new List<Image>();
        private readonly List<Color> buttonColors = new List<Color>();
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
            // 选项回执会重开面板（SkyIslandWorldStory.Refreshed）。重开时：
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
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = TitleFontMin;
            titleText.fontSizeMax = TitleFontMax;
            // 自动缩到下限还放不下就省略号，绝不让它画到框外。
            titleText.overflowMode = TextOverflowModes.Ellipsis;
            float titleHeight = Mathf.Max(TitleMinHeight,
                BossRushUI.MeasureTextHeight(titleText, titleWidth, TitleMinHeight));
            titleText.enableAutoSizing = true;   // MeasureTextHeight 会关掉，这里恢复
            titleText.fontSizeMin = TitleFontMin;
            titleText.fontSizeMax = TitleFontMax;
            titleText.overflowMode = TextOverflowModes.Ellipsis;

            // ---- 主视觉：标题（与立绘）压在插图上，插图全出血 ----
            // 旧版是「插图一条 + 表头一条」两块各占高度，插图只铺 ContentWidth、
            // 剩下大半个面板是一块纯色板。现在合成一块：插图铺满面板宽、标题压在它的渐隐上。
            // 立绘不再和标题抢同一块高度（它在右边、贴底），所以标题块就是标题本身。
            float titleBlock = titleHeight;
            float heroFloor = Mathf.Max(HeroMinHeight, titleBlock + HeroInset * 2f);
            // 但 hero 至少要装得下立绘：立绘贴底，高度就是它的边长。
            if (portrait != null) heroFloor = Mathf.Max(heroFloor, PortraitSize);
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
            float bodyNatural = BossRushUI.MeasureTextHeight(bodyText,
                ContentWidth - ScrollbarGutter, BodyMinHeight);

            // ---- 2. 按优先级挤：页脚 > 选项 > 正文 > 主视觉 ----
            // 主视觉**不再能被整条撤掉**（标题在它上面），只能压到 heroFloor；
            // 从上往下依次是：hero（无上 Pad，全出血）→ Gap → 分隔线 → Gap → 正文
            // → Gap → 选项 → 下 Pad。三个 Gap 里有一个算在 dividerBlock 里。
            //
            // 页脚「继续旅程」已删（2026-09-13）：它的 onClick 就是 Dispose()，与 ESC 完全等价
            // （OnCancel 与 Tick 里的 Escape 分支），纯冗余还占掉 48 + 14 px。
            // 关闭提示改成主视觉右上角的 ESC 键帽。
            float dividerBlock = DividerHeight + Gap;
            float chrome = Pad + heroHeight + dividerBlock + choicesHeight + Gap * 2f;
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
            Image surface = panel.gameObject.AddComponent<Image>();
            surface.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(surface, 18, BossRushUISkinPart.Panel);

            float cursor = panelHeight * 0.5f;   // 面板局部坐标；hero 全出血，顶上没有 Pad

            // 背景层必须是**第一个**子物体：它在面板底图之上、在所有内容之下。
            // 主视觉也建在里面，这样一个 Mask 同时把整屏底图与插图切成面板的圆角。
            RectTransform background = BuildBackground(panel,
                SkyIslandUiArt.GetPanelBackground(banner));
            BuildHero(background, titleText, portrait, banner, heroHeight, titleWidth, cursor);
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
            // 缩放下会被采样吃掉，时有时无。divider 图自带 1px 高光 + 1px 暗边，接缝也柔。
            BossRushUI.ApplyPanelSkin(dividerImage, 2, BossRushUISkinPart.Rule);
            cursor -= dividerBlock;

            BuildBody(panel, bodyText, bodyHeight, bodyNatural, cursor);
            cursor -= bodyHeight + Gap;

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
        /// 没有插图时（居民面板只有立绘）hero 退化成一条只有底图的表头，标题照常压在渐隐上。
        /// </summary>
        private static void BuildHero(RectTransform background, TextMeshProUGUI title, Sprite portrait,
            Sprite banner, float height, float titleWidth, float top)
        {
            RectTransform hero = MakeRect(background, "Hero",
                new Vector2(0f, top - height * 0.5f), new Vector2(PanelWidth, height));

            if (banner != null)
            {
                Image art = Stretched(hero, "Art");
                art.sprite = banner;
                art.type = Image.Type.Simple;
                // 按「填满并裁剪」而不是留黑边：preserveAspect 会在两侧留出背景色，
                // 而面板底色和插图色调不一致，看着像图没铺满。
                art.preserveAspect = false;
            }

            float titleHeight = title.rectTransform.sizeDelta.y;
            float titleBlock = portrait != null ? Mathf.Max(PortraitSize, titleHeight) : titleHeight;
            // 标题在 titleBlock 里垂直居中，所以它的上沿离 hero 底边 inset + titleBlock/2 + titleHeight/2；
            // 再加一个 inset 当余量，就是实底带要有的高度。
            // 立绘不算进来——它是一块自带描边的不透明底板，不需要垫暗；
            // 按 titleBlock 算的话居民面板会被 160 的立绘顶出一条 204 的带子，把插图盖掉 83%。
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

            // 关闭提示。页脚「继续旅程」删掉之后 ESC 是唯一出口，必须有个看得见的说明——
            // 尤其零选项的面板（收下信之后、纪念物）连一个可导航项都没有，只剩 ESC 能关。
            // 放主视觉右上角，不占任何版式高度；建在最后所以画在立绘之上。
            KeyCap(hero, "ESC", 44f, new Vector2(
                PanelWidth * 0.5f - HeroInset - 22f,
                height * 0.5f - HeroInset - KeyCapSize * 0.5f));
        }

        private void BuildBody(RectTransform panel, TextMeshProUGUI text, float height,
            float natural, float top)
        {
            RectTransform viewport = MakeRect(panel, "Body",
                new Vector2(0f, top - height * 0.5f), new Vector2(ContentWidth, height));
            bodyViewport = viewport;
            body = text;
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
            Color rowColor = BossRushUIColors.SurfaceRaised;
            rowColor.a = ChoiceRowAlpha;
            image.color = rowColor;
            // 卡片档（panel_raised），不是按钮档：选项是列表行。
            BossRushUI.ApplyPanelSkin(image, 10, BossRushUISkinPart.Card);
            // 描边是这一行「是一个独立可点区域」的唯一视觉证据：
            // SurfaceRaised 对面板底 Surface 实算只有 1.03:1（亮云海）/ 1.07:1（暗地形），
            // 远低于非文本 3:1——不画边的话玩家看到的只是五行浮着的字。
            BossRushUI.ApplyPanelStroke(image, 10, BossRushUISkinPart.Card, BossRushUIColors.Stroke);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            // 悬停/按下态走共享色板：旧版没配，鼠标移上去毫无反馈。
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = BossRushUI.GetHoverColor(BossRushUIColors.SurfaceRaised);
            colors.pressedColor = BossRushUI.GetPressedColor(BossRushUIColors.SurfaceRaised);
            colors.disabledColor = BossRushUI.GetDisabledColor(BossRushUIColors.SurfaceRaised);
            button.colors = colors;

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
            button.onClick.AddListener(delegate { SetBodyText(select()); });
            Register(button, image, rowColor);
            if (animate)
                BossRushUIEntranceAnimation.Play(rect.gameObject, index * ChoiceStagger,
                    ChoiceEntrance, ChoiceEntranceRise);
        }


        /// <param name="entranceDelay">错峰入场的延迟秒数；负数表示这次不播（重开面板）。</param>
        private static void KeyCap(RectTransform parent, string key, float width, Vector2 position)
        {
            RectTransform cap = MakeRect(parent, "KeyCap", position, new Vector2(width, KeyCapSize));
            Image capImage = cap.gameObject.AddComponent<Image>();
            capImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(capImage, 6);
            capImage.raycastTarget = false;
            TextMeshProUGUI glyph = MakeText(cap, key, 13f, BossRushUIColors.TextSecondary,
                TextAlignmentOptions.Center);
            glyph.rectTransform.sizeDelta = new Vector2(width, KeyCapSize);
            glyph.enableWordWrapping = false;
        }

        /// <summary>
        /// 换正文。**必须重新量高**：选项回执可能比原正文长得多，
        /// 直接赋值就会把新文本画到面板外——旧版正是这么做的。
        /// 面板高度已经定死，所以这里只在既有视口内调整并按需上滚动条。
        /// </summary>
        private void SetBodyText(string value)
        {
            if (body == null || bodyViewport == null) return;
            body.text = value ?? string.Empty;
            float width = ContentWidth - ScrollbarGutter;
            float viewportHeight = bodyViewport.rect.height;
            float natural = Mathf.Max(viewportHeight,
                Mathf.Ceil(body.GetPreferredValues(body.text, width, float.PositiveInfinity).y) + 4f);
            body.rectTransform.sizeDelta = new Vector2(width, natural);
            body.rectTransform.anchoredPosition = Vector2.zero;
            if (natural <= viewportHeight + 0.5f) return;
            if (bodyScroll == null)
            {
                if (bodyViewport.GetComponent<RectMask2D>() == null)
                    bodyViewport.gameObject.AddComponent<RectMask2D>();
                bodyScroll = bodyViewport.gameObject.AddComponent<ScrollRect>();
                bodyScroll.content = body.rectTransform;
                bodyScroll.viewport = bodyViewport;
                BossRushUI.ConfigureScrollRect(bodyScroll);
            }
            bodyScroll.verticalNormalizedPosition = 1f;
        }

        #endregion

        #region 键盘与手柄

        private void Register(Button button, Image image, Color color)
        {
            buttons.Add(button);
            buttonImages.Add(image);
            buttonColors.Add(color);
        }

        /// <summary>
        /// 键盘方向键/手柄的当前项。只改当前项的底色，不去动 EventSystem 的选中态：
        /// 那个选中态在鼠标点过之后会一直挂着高亮，鼠标玩家看着像按钮卡住了。
        /// </summary>
        private void Select(int index)
        {
            if (buttons.Count == 0) return;
            index = Mathf.Clamp(index, 0, buttons.Count - 1);
            if (selected >= 0 && selected < buttonImages.Count && buttonImages[selected] != null)
                buttonImages[selected].color = buttonColors[selected];
            selected = index;
            if (buttonImages[index] != null)
                buttonImages[index].color = BossRushUI.GetHoverColor(buttonColors[index]);
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
            body = null;
            bodyViewport = null;
            bodyScroll = null;
            buttons.Clear();
            buttonImages.Clear();
            buttonColors.Clear();
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
            text.text = value ?? string.Empty;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
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
