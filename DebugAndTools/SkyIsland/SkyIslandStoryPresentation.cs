// ============================================================================
// SkyIslandStoryPresentation.cs - 天空岛剧情/装置面板
// ============================================================================
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
//   - 手柄走官方 `UIInputManager`：方向键移动当前项、确认执行、取消关闭。
//     旧版只认鼠标，手柄玩家能用交互键打开面板，却既选不了选项、也关不掉它；
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

        /// <summary>插图横幅的最大高度。容不下时它第一个被压缩，因为它只是观感。</summary>
        private const float BannerMaxHeight = 232f;
        private const float BannerMinHeight = 96f;

        /// <summary>
        /// 头像边长。立绘源图是 512×512，132 会把细节丢掉一大半，且居民面板的表头会显得空。
        /// 160 是离线预览（tools/preview_sky_island_panel.py）比出来的：既用上了立绘，
        /// 又不会把只有一两个选项的居民面板撑得过高。
        /// </summary>
        private const float PortraitSize = 160f;
        private const float TitleMinHeight = 42f;
        private const float TitleFontMin = 22f;
        private const float TitleFontMax = 31f;

        /// <summary>正文压到这个高度还放不下就上滚动条，不再继续压。</summary>
        private const float BodyMinHeight = 76f;
        private const float BodyPreferredMax = 300f;

        private const float ChoiceMinHeight = 48f;
        private const float ChoicePadY = 11f;
        private const float ChoicePadX = 18f;
        private const float FooterHeight = 48f;

        /// <summary>
        /// 选项左侧数字键帽占掉的宽度（键帽 24 + 间距 12）。量高与摆放必须扣同一个数，
        /// 否则量出来的折行与实际摆出来的折行对不上；布局属性测试读的也是这个常量。
        /// </summary>
        private const float KeyHintWidth = 36f;
        private const float KeyCapSize = 24f;

        /// <summary>正文右侧给滚动条留的空。<see cref="BossRushUI.ConfigureScrollRect"/> 要求 20px。</summary>
        private const float ScrollbarGutter = 20f;

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
            GameObject previousHideToken = hideToken;
            hideToken = null;
            Dispose();
            if (choices == null) choices = new List<Choice>();

            canvas = BossRushUI.CreateCanvasRoot("SkyIslandStory", BossRushUILayers.Modal, true);
            BossRushUI.CreateBackdrop(canvas.transform);

            float maxPanelHeight = Mathf.Clamp(
                ZombieModeUIHelper.GetReferenceViewportSize().y - 140f, 520f, 980f);

            // ---- 1. 先把每块的自然高度量出来（此时还没决定面板多高）----
            float titleWidth = portrait != null ? ContentWidth - PortraitSize - Gap : ContentWidth;
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

            float headerHeight = portrait != null ? Mathf.Max(PortraitSize, titleHeight) : titleHeight;

            float bannerHeight = 0f;
            if (banner != null && banner.rect.height > 0f)
            {
                float aspect = banner.rect.width / banner.rect.height;
                bannerHeight = Mathf.Clamp(ContentWidth / Mathf.Max(0.01f, aspect),
                    BannerMinHeight, BannerMaxHeight);
            }

            var choiceHeights = new List<float>(choices.Count);
            float choicesHeight = 0f;
            for (int i = 0; i < choices.Count; i++)
            {
                TextMeshProUGUI probe = MakeText(canvas.transform, choices[i].Label, 21f,
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

            TextMeshProUGUI bodyText = MakeText(canvas.transform, text, 20f,
                BossRushUIColors.TextSecondary, TextAlignmentOptions.TopLeft);
            float bodyNatural = Mathf.Min(BodyPreferredMax,
                BossRushUI.MeasureTextHeight(bodyText, ContentWidth - ScrollbarGutter, BodyMinHeight));

            // ---- 2. 按优先级挤：页脚 > 选项 > 标题 > 正文 > 插图 ----
            float dividerBlock = 1f + Gap;
            float chrome = Pad * 2f + headerHeight + dividerBlock + choicesHeight
                + FooterHeight + Gap * 3f;
            float bodyHeight = bodyNatural;
            float panelHeight = chrome + bannerHeight + (bannerHeight > 0f ? Gap : 0f) + bodyHeight;
            if (panelHeight > maxPanelHeight)
            {
                float excess = panelHeight - maxPanelHeight;
                float bodyGive = Mathf.Min(excess, Mathf.Max(0f, bodyHeight - BodyMinHeight));
                bodyHeight -= bodyGive;
                excess -= bodyGive;
                if (excess > 0f && bannerHeight > 0f)
                {
                    float bannerGive = Mathf.Min(excess, Mathf.Max(0f, bannerHeight - BannerMinHeight));
                    bannerHeight -= bannerGive;
                    excess -= bannerGive;
                    // 压到下限还不够就整条撤掉插图：观感让位给「按钮必须点得到」。
                    if (excess > 0f) { excess -= bannerHeight + Gap; bannerHeight = 0f; }
                }
                panelHeight = Mathf.Min(maxPanelHeight,
                    chrome + bannerHeight + (bannerHeight > 0f ? Gap : 0f) + bodyHeight);
            }

            // ---- 3. 从上往下摆 ----
            RectTransform panel = MakeRect(canvas.transform, "StoryPanel", Vector2.zero,
                new Vector2(PanelWidth, panelHeight));
            Image surface = panel.gameObject.AddComponent<Image>();
            surface.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(surface, 18);

            float cursor = panelHeight * 0.5f - Pad;   // 面板局部坐标，从顶边往下走

            if (bannerHeight > 0f)
            {
                BuildBanner(panel, banner, bannerHeight, cursor);
                cursor -= bannerHeight + Gap;
            }

            BuildHeader(panel, titleText, portrait, headerHeight, titleWidth, cursor);
            cursor -= headerHeight + Gap;

            RectTransform divider = MakeRect(panel, "Divider",
                new Vector2(0f, cursor - 0.5f), new Vector2(ContentWidth, 1f));
            Image dividerImage = divider.gameObject.AddComponent<Image>();
            dividerImage.color = BossRushUIColors.Divider;
            dividerImage.raycastTarget = false;
            cursor -= dividerBlock;

            BuildBody(panel, bodyText, bodyHeight, bodyNatural, cursor);
            cursor -= bodyHeight + Gap;

            for (int i = 0; i < choices.Count; i++)
            {
                float h = choiceHeights[i];
                BuildChoice(panel, choices[i], i, h, cursor);
                cursor -= h + Gap * 0.5f;
            }
            choiceCount = choices.Count;

            BuildFooter(panel, panelHeight);

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
            if (previousHideToken != null)
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

        private static void BuildBanner(RectTransform panel, Sprite banner, float height, float top)
        {
            RectTransform frame = MakeRect(panel, "Banner",
                new Vector2(0f, top - height * 0.5f), new Vector2(ContentWidth, height));
            Image image = frame.gameObject.AddComponent<Image>();
            image.sprite = banner;
            image.type = Image.Type.Simple;
            // 横幅按「填满并裁剪」而不是留黑边：preserveAspect 会在两侧留出背景色，
            // 而面板底色和插图色调不一致，看着像图没铺满。
            image.preserveAspect = false;
            image.raycastTarget = false;
            // 底部压一层竖向渐隐，让插图和正文之间不是硬切。
            // **必须是真渐变**：纯色半透明横条会有上下两条硬边，比不加还难看。
            Sprite gradient = SkyIslandUiArt.GetBannerFade();
            if (gradient == null) return;
            RectTransform fade = MakeRect(frame, "Fade",
                new Vector2(0f, -height * 0.5f + 22f), new Vector2(ContentWidth, 44f));
            Image fadeImage = fade.gameObject.AddComponent<Image>();
            fadeImage.sprite = gradient;
            fadeImage.type = Image.Type.Simple;
            fadeImage.raycastTarget = false;
        }

        private static void BuildHeader(RectTransform panel, TextMeshProUGUI title, Sprite portrait,
            float height, float titleWidth, float top)
        {
            RectTransform header = MakeRect(panel, "Header",
                new Vector2(0f, top - height * 0.5f), new Vector2(ContentWidth, height));
            float titleX = 0f;
            if (portrait != null)
            {
                RectTransform avatar = MakeRect(header,
                    "Portrait", new Vector2(-(ContentWidth - PortraitSize) * 0.5f, 0f),
                    new Vector2(PortraitSize, PortraitSize));
                Image plate = avatar.gameObject.AddComponent<Image>();
                plate.color = BossRushUIColors.SurfaceRaised;
                BossRushUI.ApplyPanelSkin(plate, 12);
                plate.raycastTarget = false;
                RectTransform face = MakeRect(avatar, "Face", Vector2.zero,
                    new Vector2(PortraitSize - 10f, PortraitSize - 10f));
                Image faceImage = face.gameObject.AddComponent<Image>();
                faceImage.sprite = portrait;
                faceImage.preserveAspect = true;   // 立绘不能拉变形
                faceImage.raycastTarget = false;
                titleX = (PortraitSize + Gap) * 0.5f;
            }
            title.rectTransform.SetParent(header, false);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax =
                title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            title.rectTransform.sizeDelta = new Vector2(titleWidth, height);
            title.rectTransform.anchoredPosition = new Vector2(titleX, 0f);
            title.alignment = portrait != null ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
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

        private void BuildChoice(RectTransform panel, Choice choice, int index, float height, float top)
        {
            RectTransform rect = MakeRect(panel, "Choice",
                new Vector2(0f, top - height * 0.5f), new Vector2(ContentWidth, height));
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(image, 10);
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

            TextMeshProUGUI label = MakeText(rect, choice.Label, 21f,
                BossRushUI.GetButtonTextColor(BossRushUIColors.SurfaceRaised),
                TextAlignmentOptions.Left);
            label.rectTransform.sizeDelta = new Vector2(ChoiceLabelWidth, height - ChoicePadY * 2f);
            label.rectTransform.anchoredPosition = new Vector2(KeyHintWidth * 0.5f, 0f);
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;

            Func<string> select = choice.Select;
            button.onClick.AddListener(delegate { SetBodyText(select()); });
            Register(button, image, BossRushUIColors.SurfaceRaised);
        }

        private void BuildFooter(RectTransform panel, float panelHeight)
        {
            RectTransform rect = MakeRect(panel, "Continue",
                new Vector2(0f, -panelHeight * 0.5f + Pad + FooterHeight * 0.5f),
                new Vector2(ContentWidth, FooterHeight));
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = BossRushUIColors.Accent;
            BossRushUI.ApplyPanelSkin(image, 10);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = BossRushUI.GetHoverColor(BossRushUIColors.Accent);
            colors.pressedColor = BossRushUI.GetPressedColor(BossRushUIColors.Accent);
            button.colors = colors;
            TextMeshProUGUI label = MakeText(rect, L10n.T("继续旅程", "Continue"), 21f,
                BossRushUI.GetButtonTextColor(BossRushUIColors.Accent), TextAlignmentOptions.Center);
            label.rectTransform.sizeDelta = new Vector2(ContentWidth - ChoicePadX * 2f - 96f, FooterHeight);
            // 键位提示做成右侧的小键帽，而不是把「· ESC」拼进按钮文字里。
            KeyCap(rect, "ESC", 44f, new Vector2(ContentWidth * 0.5f - ChoicePadX - 22f, 0f));
            button.onClick.AddListener(delegate { Dispose(); });
            Register(button, image, BossRushUIColors.Accent);
        }

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

        private void OnNavigate(global::UIInputEventData data)
        {
            if (!Visible || data == null || buttons.Count == 0) return;
            int step = data.vector.y > 0.5f ? -1 : (data.vector.y < -0.5f ? 1 : 0);
            if (step == 0) return;
            Select(selected < 0 ? (step > 0 ? 0 : buttons.Count - 1) : selected + step);
            data.Use();
        }

        private void OnConfirm(global::UIInputEventData data)
        {
            if (!Visible || data == null) return;
            // 还没有当前项时，第一次确认只把焦点放到第一项：先让玩家看清自己选中了什么，
            // 也免得打开面板的那一下交互键被同时当成「确认」，直接替玩家点掉第一个选项。
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
            if (hideToken != null)
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
