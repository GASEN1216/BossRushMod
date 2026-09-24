// ============================================================================
// WishFountainUI.cs - 布满了灰尘的星愿许愿台运行时 View 面板
// ============================================================================
// 模块说明：
//   使用 Duckov 原版 View / FadeGroup 体系运行时创建许愿面板：
//   - 固定高度的多行 TMP_InputField，超长内容通过垂直滚动条浏览
//   - 文本框聚焦高亮、固定提示文案、字数统计与状态提示
//   - 匿名勾选默认关闭，发送成功后走原版 NotificationText 大横幅
//   - 发送成功后停 0.6 秒（状态淡入 + 确认音）再淡出关闭并开始抽奖，失败时保留输入内容供重试
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    public partial class WishFountainView : View
    {
        public static WishFountainView Instance { get; private set; }

        private FadeGroup fadeGroup;
        private TMP_InputField inputField;
        private Toggle anonymousToggle;
        private Button confirmButton;
        private Button cancelButton;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI hintText;
        private TextMeshProUGUI placeholderText;
        private TextMeshProUGUI inputFocusHintText;
        private TextMeshProUGUI countText;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI confirmButtonText;
        private TextMeshProUGUI cancelButtonText;
        private TextMeshProUGUI anonymousToggleLabelText;
        private Image inputContainerImage;
        private Image inputContainerStroke;
        private Scrollbar inputScrollbar;

        private bool sending;
        private bool successDisplayed;
        private bool submittedThisSession;
        private bool hasExplicitStatus;
        private Color statusColor = BossRushUIColors.WarningText;
        private Coroutine autoCloseCoroutine;
        private Coroutine danmakuWarmupCoroutine;

        private RectTransform panelRectTransform;
        private WishFountainDanmakuView danmakuView;
        private List<string> cachedDanmakuContents;
        private LayoutElement inputContainerLayoutElement;
        private Action<List<string>> danmakuFetchSuccessHandler;
        private Action<string> danmakuFetchFailureHandler;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private int lastCooldownRemaining = -1;
        private bool lastCooldownState;
        private bool cooldownStateInitialized;
        private bool lastInputFocusState;
        private bool inputFocusStateInitialized;
        private int danmakuRequestVersion;
        private bool danmakuWarmupScheduled;

        private const float BASE_PANEL_WIDTH = 820f;
        private const float MIN_PANEL_WIDTH = 400f;
        private const float PANEL_WIDTH_RATIO = 0.55f;
        private const float MAX_HEIGHT_RATIO = 0.85f;
        private const float BASE_INPUT_HEIGHT = 186f;
        private const float MIN_INPUT_HEIGHT = 100f;
        private const float FIXED_CONTENT_HEIGHT = 486f;
        // 字号只用四级（UI 共识第 7 节，2026-09-24 对照审查 A-09）：标题 32 / 按钮 20 / 正文 18 / 注脚 16。
        // 单行框高 ≥ 字号 × 1.45 + 4：18 号 → 32，16 号 → 28。
        private const float TITLE_FONT_SIZE = 32f;
        private const float BUTTON_FONT_SIZE = 20f;
        private const float BODY_FONT_SIZE = 18f;
        private const float NOTE_FONT_SIZE = 16f;
        private const float BODY_LINE_HEIGHT = 32f;
        private const float NOTE_LINE_HEIGHT = 28f;
        private const int HOST_TOPMOST_SORTING_ORDER = BossRushUILayers.HudOverlay;

        public static WishFountainView CreateRuntime(Transform parent)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject host = new GameObject(
                "BossRush_WishFountainViewHost",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            host.transform.SetParent(parent, false);

            RectTransform hostRect = host.GetComponent<RectTransform>();
            StretchRect(hostRect);
            ConfigureHostCanvas(parent, host.GetComponent<Canvas>());

            GameObject root = new GameObject(
                "WishFountainView",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(FadeGroup),
                typeof(CanvasGroupFade));
            root.transform.SetParent(host.transform, false);
            root.SetActive(false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            StretchRect(rootRect);

            BossRushUI.CreateBackdrop(rootRect);   // 遮罩走 Backdrop token + 共享暗角（UD-17），挡点击

            FadeGroup fadeGroup = root.GetComponent<FadeGroup>();
            fadeGroup.manageGameObjectActive = true;

            ConfigureFadeGroup(root, fadeGroup);

            WishFountainView view = root.AddComponent<WishFountainView>();
            view.fadeGroup = fadeGroup;
            view.BuildLayout(rootRect);
            view.EnsureDanmakuLayer();
            view.ScheduleDanmakuWarmup();

            root.SetActive(true);
            view.HideImmediately();
            return view;
        }

        protected override void Awake()
        {
            base.Awake();

            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            if (inputField != null)
            {
                inputField.onValueChanged.AddListener(OnInputValueChanged);
            }

            if (anonymousToggle != null)
            {
                anonymousToggle.onValueChanged.AddListener(OnAnonymousToggleChanged);
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(OnConfirmButtonClicked);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(OnCancelButtonClicked);
            }

            RefreshLocalizedTexts();
            RefreshUIState();
        }

        protected override void OnDestroy()
        {
            if (inputField != null)
            {
                inputField.onValueChanged.RemoveListener(OnInputValueChanged);
            }

            if (anonymousToggle != null)
            {
                anonymousToggle.onValueChanged.RemoveListener(OnAnonymousToggleChanged);
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(OnConfirmButtonClicked);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(OnCancelButtonClicked);
            }

            if (Instance == this)
            {
                Instance = null;
            }

            if (danmakuWarmupCoroutine != null && ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StopCoroutine(danmakuWarmupCoroutine);
                danmakuWarmupCoroutine = null;
            }

            CancelDanmakuFetch();
            cachedDanmakuContents = null;
            danmakuView = null;
            DestroyFeelResources();

            base.OnDestroy();
        }

        public void ResetAndOpen()
        {
            ReleaseSuccessBeatOnClose();   // 成功停顿中被重开：先把抽奖发出去，再清输入

            sending = false;
            successDisplayed = false;
            submittedThisSession = false;
            hasExplicitStatus = false;
            statusColor = BossRushUIColors.WarningText;
            cooldownStateInitialized = false;
            inputFocusStateInitialized = false;
            lastCooldownRemaining = -1;
            lastCooldownState = false;
            lastInputFocusState = false;

            if (inputField != null)
            {
                inputField.text = "";
                inputField.interactable = true;
            }

            if (anonymousToggle != null)
            {
                anonymousToggle.isOn = false;
                anonymousToggle.interactable = true;
            }

            RefreshLocalizedTexts();
            RefreshUIState();

            if (open)
            {
                RefreshDanmakuDisplay();
                StartCoroutine(FocusInputFieldNextFrame());
                return;
            }

            Open();
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            if (transform.parent != null)
            {
                transform.parent.SetAsLastSibling();
            }
            transform.SetAsLastSibling();

            if (fadeGroup != null)
            {
                fadeGroup.Show();
            }

            AdjustPanelForResolution();
            RefreshDanmakuDisplay();
            StartCoroutine(FocusInputFieldNextFrame());
        }

        protected override void OnClose()
        {
            base.OnClose();

            if (fadeGroup != null)
            {
                fadeGroup.Hide();   // 淡出关闭（0.18 秒），弹幕层随根节点一起淡出
            }

            CancelDanmakuFetch();
            ReleaseSuccessBeatOnClose();
            MaybeShowCloseReminder();

            sending = false;
            successDisplayed = false;
            hasExplicitStatus = false;
            cooldownStateInitialized = false;
            inputFocusStateInitialized = false;
            lastCooldownRemaining = -1;
            lastCooldownState = false;
            lastInputFocusState = false;

            if (EventSystem.current != null)
            {
                GameObject selected = EventSystem.current.currentSelectedGameObject;
                if (selected != null && selected.transform.IsChildOf(transform))
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }

        protected override void OnConfirm()
        {
            if (successDisplayed)
            {
                Close();
                return;
            }

            GameObject current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (anonymousToggle != null && current == anonymousToggle.gameObject)
            {
                anonymousToggle.isOn = !anonymousToggle.isOn;
                return;
            }

            if (cancelButton != null && current == cancelButton.gameObject)
            {
                Close();
                return;
            }

            if (inputField != null
                && (current == inputField.gameObject
                    || (inputField.textViewport != null && current == inputField.textViewport.gameObject)))
            {
                return;
            }

            OnConfirmButtonClicked();
        }

        private void Update()
        {
            if (!open)
            {
                return;
            }

            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
            {
                AdjustPanelForResolution();
            }

            bool inputFocused = IsInputFieldFocused();
            if (!inputFocusStateInitialized || inputFocused != lastInputFocusState)
            {
                lastInputFocusState = inputFocused;
                inputFocusStateInitialized = true;
                RefreshInputFieldVisualState();
            }

            bool inCooldown = WishFountainService.IsInCooldown();
            if (!cooldownStateInitialized || inCooldown != lastCooldownState)
            {
                lastCooldownState = inCooldown;
                cooldownStateInitialized = true;
                RefreshUIState();
                return;
            }

            if (!sending && !successDisplayed && inCooldown)
            {
                int remaining = WishFountainService.GetCooldownRemaining();
                if (remaining != lastCooldownRemaining)
                {
                    RefreshUIState();
                }
            }
        }

        private void BuildLayout(RectTransform rootRect)
        {
            TMP_FontAsset defaultFont = ZombieModeUIHelper.GetGameFont();
            if (defaultFont == null)
            {
                defaultFont = TMP_Settings.defaultFontAsset;
            }

            GameObject panel = CreateUIObject("Panel", rootRect, typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(BASE_PANEL_WIDTH, 0f);
            panelRectTransform = panelRect;

            ContentSizeFitter panelFitter = panel.GetComponent<ContentSizeFitter>();
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            Image panelImage = panel.GetComponent<Image>();
            // 投影由共享层给（外沿柔和暗晕），不再用 UI.Shadow 复制出一块 14px 硬边黑板（UD-18）
            BossRushUI.ApplyFramedPanelSkin(panelImage, 14, BossRushUISkinPart.Panel);
            panelImage.color = BossRushUIColors.Surface;

            VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(32, 32, 24, 34);
            panelLayout.spacing = 16f;
            panelLayout.childControlHeight = true;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childForceExpandWidth = true;

            AddTopTrace(panelRect);   // 顶边左段 Accent 细线，代替全宽 4px 亮天蓝条（UD-19）

            GameObject headerBlock = CreateUIObject("HeaderBlock", panelRect, typeof(VerticalLayoutGroup));
            RectTransform headerRect = headerBlock.GetComponent<RectTransform>();
            VerticalLayoutGroup headerLayout = headerBlock.GetComponent<VerticalLayoutGroup>();
            headerLayout.spacing = 8f;
            headerLayout.childControlHeight = true;
            headerLayout.childControlWidth = true;
            headerLayout.childForceExpandHeight = false;
            headerLayout.childForceExpandWidth = true;

            titleText = CreateText("Title", headerRect, defaultFont, TITLE_FONT_SIZE, FontStyles.Bold, TextAlignmentOptions.Center);
            SetPreferredHeight(titleText.rectTransform, 44f);

            hintText = CreateText("Hint", headerRect, defaultFont, BODY_FONT_SIZE, FontStyles.Normal, TextAlignmentOptions.Center);
            hintText.enableWordWrapping = true;
            hintText.color = BossRushUIColors.TextSecondary;
            SetPreferredHeight(hintText.rectTransform, BODY_LINE_HEIGHT);

            GameObject contentCard = CreateUIObject("ContentCard", panelRect, typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform contentCardRect = contentCard.GetComponent<RectTransform>();
            Image contentCardImage = contentCard.GetComponent<Image>();
            BossRushUI.ApplyFramedPanelSkin(contentCardImage, 10, BossRushUISkinPart.Card);
            contentCardImage.color = BossRushUIColors.SurfaceRaised;
            VerticalLayoutGroup contentCardLayout = contentCard.GetComponent<VerticalLayoutGroup>();
            contentCardLayout.padding = new RectOffset(20, 20, 16, 16);
            contentCardLayout.spacing = 12f;
            contentCardLayout.childControlHeight = true;
            contentCardLayout.childControlWidth = true;
            contentCardLayout.childForceExpandHeight = false;
            contentCardLayout.childForceExpandWidth = true;

            GameObject inputHeaderRow = CreateUIObject("InputHeaderRow", contentCardRect, typeof(HorizontalLayoutGroup));
            RectTransform inputHeaderRowRect = inputHeaderRow.GetComponent<RectTransform>();
            SetPreferredHeight(inputHeaderRowRect, NOTE_LINE_HEIGHT);
            HorizontalLayoutGroup inputHeaderLayout = inputHeaderRow.GetComponent<HorizontalLayoutGroup>();
            inputHeaderLayout.spacing = 8f;
            inputHeaderLayout.childControlWidth = true;
            inputHeaderLayout.childControlHeight = true;
            inputHeaderLayout.childForceExpandHeight = false;
            inputHeaderLayout.childForceExpandWidth = false;
            inputHeaderLayout.childAlignment = TextAnchor.MiddleLeft;

            TextMeshProUGUI inputCaption = CreateText("InputCaption", inputHeaderRowRect, defaultFont, NOTE_FONT_SIZE, FontStyles.Bold, TextAlignmentOptions.Left);
            inputCaption.text = L10n.T("心愿内容", "Wish Content");
            inputCaption.color = BossRushUIColors.TextSecondary;
            SetPreferredWidth(inputCaption.rectTransform, 110f);
            SetPreferredHeight(inputCaption.rectTransform, NOTE_LINE_HEIGHT);

            GameObject inputHeaderSpacer = CreateUIObject("InputHeaderSpacer", inputHeaderRowRect, typeof(LayoutElement));
            LayoutElement inputHeaderSpacerElement = inputHeaderSpacer.GetComponent<LayoutElement>();
            inputHeaderSpacerElement.flexibleWidth = 1f;

            inputFocusHintText = CreateText("InputFocusHint", inputHeaderRowRect, defaultFont, NOTE_FONT_SIZE, FontStyles.Normal, TextAlignmentOptions.Right);
            // 常驻的一句提醒不是警告：次色（A-10），警告色只留给真出了问题的状态行
            inputFocusHintText.color = BossRushUIColors.TextSecondary;
            SetPreferredWidth(inputFocusHintText.rectTransform, 300f);
            SetPreferredHeight(inputFocusHintText.rectTransform, NOTE_LINE_HEIGHT);

            GameObject inputFrame = CreateUIObject("InputFrame", contentCardRect, typeof(LayoutElement));
            RectTransform inputFrameRect = inputFrame.GetComponent<RectTransform>();
            inputContainerLayoutElement = inputFrame.GetComponent<LayoutElement>();
            inputContainerLayoutElement.minHeight = BASE_INPUT_HEIGHT;
            inputContainerLayoutElement.preferredHeight = BASE_INPUT_HEIGHT;
            inputContainerLayoutElement.flexibleHeight = 0f;

            GameObject inputContainer = CreateUIObject("InputContainer", inputFrameRect, typeof(Image));
            RectTransform inputContainerRect = inputContainer.GetComponent<RectTransform>();
            StretchRect(inputContainerRect);
            inputContainerImage = inputContainer.GetComponent<Image>();
            inputContainerImage.color = BossRushUIColors.Surface;
            // 圆角卡片 + 独立描边环（焦点态换 Accent），代替 UI.Outline 叠四份网格的歪边（UD-19）
            inputContainerStroke = BossRushUI.ApplyFramedPanelSkin(inputContainerImage, 8, BossRushUISkinPart.Card);

            GameObject inputViewport = CreateUIObject("Viewport", inputContainerRect, typeof(RectMask2D));
            RectTransform inputViewportRect = inputViewport.GetComponent<RectTransform>();
            inputViewportRect.anchorMin = new Vector2(0f, 0f);
            inputViewportRect.anchorMax = new Vector2(1f, 1f);
            inputViewportRect.offsetMin = new Vector2(16f, 16f);
            inputViewportRect.offsetMax = new Vector2(-36f, -16f);

            GameObject inputTextGO = CreateUIObject("Text", inputViewportRect, typeof(TextMeshProUGUI));
            RectTransform inputTextRect = inputTextGO.GetComponent<RectTransform>();
            inputTextRect.anchorMin = new Vector2(0f, 0f);
            inputTextRect.anchorMax = new Vector2(1f, 1f);
            inputTextRect.pivot = new Vector2(0.5f, 0.5f);
            inputTextRect.offsetMin = Vector2.zero;
            inputTextRect.offsetMax = Vector2.zero;
            TextMeshProUGUI inputText = inputTextGO.GetComponent<TextMeshProUGUI>();
            ConfigureTMPText(inputText, defaultFont, BODY_FONT_SIZE, TextAlignmentOptions.TopLeft);
            inputText.enableWordWrapping = true;
            inputText.overflowMode = TextOverflowModes.Overflow;

            GameObject placeholderGO = CreateUIObject("Placeholder", inputViewportRect, typeof(TextMeshProUGUI));
            RectTransform placeholderRect = placeholderGO.GetComponent<RectTransform>();
            StretchRect(placeholderRect);
            placeholderText = placeholderGO.GetComponent<TextMeshProUGUI>();
            ConfigureTMPText(placeholderText, defaultFont, BODY_FONT_SIZE, TextAlignmentOptions.TopLeft);
            placeholderText.enableWordWrapping = true;
            placeholderText.color = WithAlpha(BossRushUIColors.TextSecondary, 0.7f);

            inputField = inputContainer.AddComponent<TMP_InputField>();
            inputField.textViewport = inputViewportRect;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            inputField.characterLimit = WishFountainService.MAX_CHARS;
            inputField.richText = false;
            inputField.scrollSensitivity = 20f;
            inputField.pointSize = BODY_FONT_SIZE;
            inputField.customCaretColor = true;
            inputField.caretColor = BossRushUIColors.Accent;
            inputField.caretWidth = 3;
            inputField.selectionColor = WithAlpha(BossRushUIColors.Accent, 0.35f);
            // 颜色过渡由 RefreshInputFieldVisualState() 手动控制，不使用 Unity 内置 ColorBlock
            inputField.transition = Selectable.Transition.None;
            inputField.targetGraphic = inputContainerImage;

            GameObject scrollbarGO = CreateUIObject("VerticalScrollbar", inputContainerRect, typeof(Image), typeof(Scrollbar));
            RectTransform scrollbarRect = scrollbarGO.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-18f, 10f);
            scrollbarRect.offsetMax = new Vector2(-8f, -10f);

            Image scrollbarTrackImage = scrollbarGO.GetComponent<Image>();

            inputScrollbar = scrollbarGO.GetComponent<Scrollbar>();
            inputScrollbar.direction = Scrollbar.Direction.BottomToTop;
            inputScrollbar.numberOfSteps = 0;

            GameObject slidingArea = CreateUIObject("SlidingArea", scrollbarRect, typeof(RectTransform));
            RectTransform slidingAreaRect = slidingArea.GetComponent<RectTransform>();
            slidingAreaRect.anchorMin = Vector2.zero;
            slidingAreaRect.anchorMax = Vector2.one;
            slidingAreaRect.offsetMin = Vector2.zero;
            slidingAreaRect.offsetMax = Vector2.zero;

            GameObject handle = CreateUIObject("Handle", slidingAreaRect, typeof(Image));
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            StretchRect(handleRect);
            Image handleImage = handle.GetComponent<Image>();

            inputScrollbar.targetGraphic = handleImage;
            inputScrollbar.handleRect = handleRect;
            StyleInputScrollbar(scrollbarTrackImage, handleImage, inputScrollbar);

            inputField.verticalScrollbar = inputScrollbar;

            GameObject metaRow = CreateUIObject("MetaRow", contentCardRect, typeof(HorizontalLayoutGroup));
            HorizontalLayoutGroup metaLayout = metaRow.GetComponent<HorizontalLayoutGroup>();
            metaLayout.childControlWidth = true;
            metaLayout.childControlHeight = true;
            metaLayout.childForceExpandHeight = false;
            metaLayout.childForceExpandWidth = false;
            metaLayout.spacing = 12f;
            metaLayout.childAlignment = TextAnchor.MiddleLeft;

            anonymousToggle = CreateToggle(metaRow.GetComponent<RectTransform>(), defaultFont, out anonymousToggleLabelText);

            GameObject metaSpacer = CreateUIObject("MetaSpacer", metaRow.GetComponent<RectTransform>(), typeof(LayoutElement));
            LayoutElement metaSpacerElement = metaSpacer.GetComponent<LayoutElement>();
            metaSpacerElement.flexibleWidth = 1f;

            countText = CreateText("CountText", metaRow.GetComponent<RectTransform>(), defaultFont, NOTE_FONT_SIZE, FontStyles.Normal, TextAlignmentOptions.Right);
            SetPreferredWidth(countText.rectTransform, 170f);
            SetPreferredHeight(countText.rectTransform, NOTE_LINE_HEIGHT);

            // 状态行：不再是又一层直角底色卡，只留文字 + 左侧状态色细竖条（UD-19）
            GameObject statusCard = CreateUIObject("StatusCard", contentCardRect);
            RectTransform statusCardRect = statusCard.GetComponent<RectTransform>();
            SetPreferredHeight(statusCardRect, 48f);
            statusRail = CreateStatusRail(statusCardRect);

            statusText = CreateText("StatusText", statusCardRect, defaultFont, BODY_FONT_SIZE, FontStyles.Normal, TextAlignmentOptions.Left);
            statusText.enableWordWrapping = true;
            statusText.rectTransform.anchorMin = Vector2.zero;
            statusText.rectTransform.anchorMax = Vector2.one;
            statusText.rectTransform.offsetMin = new Vector2(14f, 8f);
            statusText.rectTransform.offsetMax = new Vector2(-14f, -8f);

            // 操作栏：去掉全宽直角底色条，只用一条分隔线分区（UD-19）
            GameObject actionBar = CreateUIObject("ActionBar", panelRect, typeof(VerticalLayoutGroup));
            RectTransform actionBarRect = actionBar.GetComponent<RectTransform>();
            VerticalLayoutGroup actionBarLayout = actionBar.GetComponent<VerticalLayoutGroup>();
            actionBarLayout.padding = new RectOffset(18, 18, 12, 10);
            actionBarLayout.spacing = 8f;
            actionBarLayout.childControlHeight = true;
            actionBarLayout.childControlWidth = true;
            actionBarLayout.childForceExpandHeight = false;
            actionBarLayout.childForceExpandWidth = true;

            GameObject divider = ZombieModeUIHelper.CreateSeparator("Divider", actionBarRect, Vector2.zero, Vector2.one, Vector2.zero, 2f, BossRushUIColors.Divider);
            SetPreferredHeight(divider.GetComponent<RectTransform>(), 8f);

            GameObject buttonRow = CreateUIObject("ButtonRow", actionBarRect, typeof(HorizontalLayoutGroup));
            HorizontalLayoutGroup buttonLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
            buttonLayout.childControlWidth = true;
            buttonLayout.childControlHeight = true;
            buttonLayout.childForceExpandHeight = false;
            buttonLayout.childForceExpandWidth = false;
            buttonLayout.spacing = 16f;
            buttonLayout.childAlignment = TextAnchor.MiddleCenter;

            // 主操作放最右（A-08，UI 共识第 4 节）：先建次级「取消」，再建 AccentFill 的「许愿」
            cancelButton = CreateButton(buttonRow.GetComponent<RectTransform>(), defaultFont, out cancelButtonText, 180f, 46f, false);
            confirmButton = CreateButton(buttonRow.GetComponent<RectTransform>(), defaultFont, out confirmButtonText, 248f, 46f, true);
        }

        private void AdjustPanelForResolution()
        {
            if (panelRectTransform == null)
            {
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            RectTransform parentRect = panelRectTransform.parent as RectTransform;
            float availWidth = parentRect != null ? parentRect.rect.width : Screen.width;
            float availHeight = parentRect != null ? parentRect.rect.height : Screen.height;

            if (availWidth <= 0f || availHeight <= 0f)
            {
                return;
            }

            float panelWidth = Mathf.Clamp(availWidth * PANEL_WIDTH_RATIO, MIN_PANEL_WIDTH, BASE_PANEL_WIDTH);
            panelRectTransform.sizeDelta = new Vector2(panelWidth, panelRectTransform.sizeDelta.y);

            float maxPanelHeight = availHeight * MAX_HEIGHT_RATIO;
            float defaultTotalHeight = FIXED_CONTENT_HEIGHT + BASE_INPUT_HEIGHT;

            if (inputContainerLayoutElement != null)
            {
                if (defaultTotalHeight > maxPanelHeight)
                {
                    float inputHeight = Mathf.Max(MIN_INPUT_HEIGHT, BASE_INPUT_HEIGHT - (defaultTotalHeight - maxPanelHeight));
                    inputContainerLayoutElement.minHeight = inputHeight;
                    inputContainerLayoutElement.preferredHeight = inputHeight;
                }
                else
                {
                    inputContainerLayoutElement.minHeight = BASE_INPUT_HEIGHT;
                    inputContainerLayoutElement.preferredHeight = BASE_INPUT_HEIGHT;
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRectTransform);
        }

        private void EnsureDanmakuLayer()
        {
            if (danmakuView != null)
            {
                return;
            }

            Transform root = transform;
            if (root == null)
            {
                return;
            }

            danmakuView = WishFountainDanmakuView.CreateRuntime(root, panelRectTransform);
            if (panelRectTransform != null)
            {
                panelRectTransform.SetAsLastSibling();
            }
        }

        private void RefreshDanmakuDisplay()
        {
            EnsureDanmakuLayer();
            ScheduleDanmakuWarmup();
            if (danmakuView == null)
            {
                return;
            }

            if (cachedDanmakuContents == null || cachedDanmakuContents.Count <= 0)
            {
                cachedDanmakuContents = WishFountainService.TryLoadDanmakuCacheSnapshot();
            }

            if (cachedDanmakuContents != null && cachedDanmakuContents.Count > 0)
            {
                danmakuView.Show(cachedDanmakuContents);
            }
            else
            {
                danmakuView.Hide();
            }

            CancelDanmakuFetch();

            int requestVersion = ++danmakuRequestVersion;
            danmakuFetchSuccessHandler =
                wishContents =>
                {
                    if (requestVersion != danmakuRequestVersion)
                    {
                        return;
                    }

                    bool hadVisibleDanmaku = cachedDanmakuContents != null && cachedDanmakuContents.Count > 0;
                    cachedDanmakuContents = wishContents != null && wishContents.Count > 0
                        ? new List<string>(wishContents)
                        : null;

                    if (!open || danmakuView == null)
                    {
                        return;
                    }

                    if (cachedDanmakuContents != null && cachedDanmakuContents.Count > 0)
                    {
                        danmakuView.UpdateContents(cachedDanmakuContents, hadVisibleDanmaku);
                        if (panelRectTransform != null)
                        {
                            panelRectTransform.SetAsLastSibling();
                        }
                    }
                    else
                    {
                        danmakuView.Hide();
                    }
                };
            danmakuFetchFailureHandler =
                error =>
                {
                    if (requestVersion != danmakuRequestVersion)
                    {
                        return;
                    }

                    ModBehaviour.DevLog("[WishFountain] 弹幕读取未展示: " + (error ?? "unknown"));
                    if (!open || danmakuView == null || (cachedDanmakuContents != null && cachedDanmakuContents.Count > 0))
                    {
                        return;
                    }

                    danmakuView.Hide();
                };
            WishFountainService.RequestRecentWishes(danmakuFetchSuccessHandler, danmakuFetchFailureHandler);
        }

        private void CancelDanmakuFetch()
        {
            danmakuRequestVersion++;
            WishFountainService.CancelRecentWishesRequest(danmakuFetchSuccessHandler, danmakuFetchFailureHandler);
            danmakuFetchSuccessHandler = null;
            danmakuFetchFailureHandler = null;
        }

        private void HideDanmaku()
        {
            if (danmakuView != null)
            {
                danmakuView.Hide();
            }
        }

        private void HideImmediately()
        {
            CancelDanmakuFetch();
            HideDanmaku();
            if (fadeGroup != null)
            {
                fadeGroup.SkipHide();
            }
        }

        private void ScheduleDanmakuWarmup()
        {
            EnsureDanmakuLayer();
            if (danmakuWarmupScheduled || danmakuView == null || ModBehaviour.Instance == null)
            {
                return;
            }

            danmakuWarmupScheduled = true;
            danmakuView.WarmupPoolImmediate();
            danmakuWarmupCoroutine = ModBehaviour.Instance.StartCoroutine(WarmupDanmakuPoolAfterDelay());
        }

        private IEnumerator WarmupDanmakuPoolAfterDelay()
        {
            yield return null;
            yield return null;

            if (danmakuView != null)
            {
                yield return danmakuView.WarmupPoolIncrementally();
            }

            danmakuWarmupCoroutine = null;
        }

        private IEnumerator FocusInputFieldNextFrame()
        {
            yield return null;

            if (inputField == null || !open)
            {
                yield break;
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(inputField.gameObject);
            }

            inputField.ActivateInputField();
            if (string.IsNullOrEmpty(inputField.text))
            {
                inputField.MoveTextStart(false);
            }
        }

        private void MaybeShowCloseReminder()
        {
            if (sending || successDisplayed || submittedThisSession)
            {
                return;
            }

            WishFountainService.ShowWishCloseReminderBubble();
        }

        private void OnInputValueChanged(string _)
        {
            if (!sending)
            {
                hasExplicitStatus = false;
            }
            RefreshUIState();
        }

        private void OnAnonymousToggleChanged(bool _)
        {
            RefreshUIState();
        }

        private void NotifyClosedAfterSuccessfulWish()
        {
            string standardized = WishFountainService.StandardizeText(inputField != null ? inputField.text : "");
            WishFountainService.TryStartWishRewardAnimationAfterSuccessfulSend(standardized);
        }

        private void OnConfirmButtonClicked()
        {
            if (sending || successDisplayed)
            {
                return;
            }

            string standardized = WishFountainService.StandardizeText(inputField != null ? inputField.text : "");
            string errorMsg;
            if (!WishFountainService.ValidateWishText(standardized, out errorMsg))
            {
                SetStatus(errorMsg, BossRushUIColors.DangerText, true);
                return;
            }

            sending = true;
            hasExplicitStatus = true;
            SetStatus(L10n.T("正在将心愿送往星空…", "Sending your wish to the stars…"), BossRushUIColors.Accent, true);
            RefreshUIState();

            StartCoroutine(WishFountainService.SendWish(
                standardized,
                anonymousToggle == null || anonymousToggle.isOn,
                () =>
                {
                    sending = false;
                    successDisplayed = true;
                    submittedThisSession = true;
                    SetStatus(L10n.T("心愿送出去了", "Wish sent"), BossRushUIColors.SuccessText, true);
                    RefreshUIState();
                    BeginSuccessBeat();   // 停 0.6 秒让「送出去了」这一拍被看到，再淡出并开始抽奖（UD-21）
                },
                error =>
                {
                    sending = false;
                    successDisplayed = false;
                    SetStatus(error, BossRushUIColors.DangerText, true);
                    RefreshUIState();
                    StartCoroutine(FocusInputFieldNextFrame());
                }));
        }

        private void OnCancelButtonClicked()
        {
            Close();
        }

        private void RefreshLocalizedTexts()
        {
            if (titleText != null)
            {
                titleText.text = L10n.T("写下你的心愿", "Write Your Wish");
            }

            if (hintText != null)
            {
                hintText.text = L10n.T(
                    "许愿对抽奖有加成哦，另外有想要实现的功能也可以写下来，我可以看到~",
                    "Wishing boosts your draw. Want a feature added? Write that too, I read these~");
            }

            if (placeholderText != null)
            {
                placeholderText.text = L10n.T(
                    "在这里写下你的心愿……",
                    "Write your wish here...");
            }

            if (anonymousToggleLabelText != null)
            {
                anonymousToggleLabelText.text = L10n.T("匿名许愿（隐藏我的名字）", "Anonymous wish (hide my name)");
            }

            if (cancelButtonText != null)
            {
                cancelButtonText.text = successDisplayed
                    ? L10n.T("关闭", "Close")
                    : L10n.T("取消", "Cancel");
            }
        }

        private void RefreshUIState()
        {
            if (inputField == null)
            {
                return;
            }

            int charCount = !string.IsNullOrEmpty(inputField.text) ? inputField.text.Length : 0;

            if (countText != null)
            {
                countText.text = L10n.T(
                    "已输入 " + charCount + " / " + WishFountainService.MAX_CHARS + " 字",
                    "Typed " + charCount + " / " + WishFountainService.MAX_CHARS + " chars");
                // 还没开始写时不先亮红（A-11）：要求写在状态行，计数只在写了但不够时才标红
                countText.color = charCount > 0 && charCount < WishFountainService.MIN_CHARS
                    ? BossRushUIColors.DangerText
                    : BossRushUIColors.TextSecondary;
            }

            bool inCooldown = WishFountainService.IsInCooldown();
            bool canSend = !sending
                && !successDisplayed
                && !inCooldown
                && charCount >= WishFountainService.MIN_CHARS;

            if (inputField != null)
            {
                inputField.interactable = !sending && !successDisplayed;
            }

            RefreshInputFieldVisualState();

            if (inputScrollbar != null)
            {
                inputScrollbar.interactable = !sending && !successDisplayed;
            }

            if (anonymousToggle != null)
            {
                anonymousToggle.interactable = !sending && !successDisplayed;
            }

            if (confirmButton != null)
            {
                confirmButton.interactable = canSend;
            }

            if (cancelButton != null)
            {
                cancelButton.interactable = !sending || successDisplayed;
            }

            if (cancelButtonText != null)
            {
                cancelButtonText.text = successDisplayed
                    ? L10n.T("关闭", "Close")
                    : L10n.T("取消", "Cancel");
            }

            if (confirmButtonText != null)
            {
                if (successDisplayed)
                {
                    confirmButtonText.text = L10n.T("已提交", "Submitted");
                }
                else if (inCooldown)
                {
                    int remain = WishFountainService.GetCooldownRemaining();
                    confirmButtonText.text = L10n.T("冷却中 (" + remain + "s)", "Cooldown (" + remain + "s)");
                }
                else
                {
                    confirmButtonText.text = L10n.T("许愿", "Make a Wish");
                }
            }

            if (!hasExplicitStatus)
            {
                if (inCooldown)
                {
                    int remain = WishFountainService.GetCooldownRemaining();
                    SetStatus(
                        L10n.T("请 " + remain + " 秒后再试", "Please wait " + remain + " seconds"),
                        BossRushUIColors.WarningText,
                        false);
                }
                else if (charCount > 0 && charCount < WishFountainService.MIN_CHARS)
                {
                    SetStatus(
                        L10n.T("最少输入 " + WishFountainService.MIN_CHARS + " 个字符哦", "At least " + WishFountainService.MIN_CHARS + " characters required"),
                        BossRushUIColors.WarningText,
                        false);
                }
                else if (charCount == 0)
                {
                    // 0 字时「许愿」按不动，原因要写出来（A-11）
                    SetStatus(
                        string.Format(L10n.T("写下心愿就能许愿（至少 {0} 个字）", "Write your wish to send it (at least {0} characters)"),
                            WishFountainService.MIN_CHARS),
                        BossRushUIColors.TextSecondary,
                        false);
                }
                else
                {
                    SetStatus(" ", BossRushUIColors.TextSecondary, false);
                }
            }

            if (statusText != null)
            {
                statusText.color = statusColor;
            }

            lastCooldownState = inCooldown;
            cooldownStateInitialized = true;
            lastCooldownRemaining = inCooldown ? WishFountainService.GetCooldownRemaining() : -1;
        }

        private void RefreshInputFieldVisualState()
        {
            ApplyInputVisualState(IsInputFieldFocused());   // 配色与描边环见 WishFountainUI_Feel.cs（UD-17 / UD-19）
        }

        private bool IsInputFieldFocused()
        {
            if (inputField == null || !open || !inputField.interactable)
            {
                return false;
            }

            if (inputField.isFocused)
            {
                return true;
            }

            GameObject current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return current == inputField.gameObject
                || (inputField.textViewport != null && current == inputField.textViewport.gameObject);
        }

        private void SetStatus(string text, Color color, bool explicitStatus)
        {
            hasExplicitStatus = explicitStatus;
            statusColor = color;
            if (statusText != null)
            {
                statusText.text = text;
                statusText.color = color;
            }
            ApplyStatusRail(text, color);
        }

        private static void ConfigureFadeGroup(GameObject root, FadeGroup fadeGroup)
        {
            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            CanvasGroupFade canvasFade = root.GetComponent<CanvasGroupFade>();
            ConfigureCanvasGroupFade(canvasFade, canvasGroup);

            FieldInfo fadeElementsField = typeof(FadeGroup).GetField("fadeElements", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fadeElementsField != null)
            {
                fadeElementsField.SetValue(fadeGroup, new List<FadeElement> { canvasFade });
            }
        }

        private static void ConfigureHostCanvas(Transform parent, Canvas hostCanvas)
        {
            if (hostCanvas == null)
            {
                return;
            }

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
                ? Mathf.Max(HOST_TOPMOST_SORTING_ORDER, parentCanvas.sortingOrder + 20)
                : HOST_TOPMOST_SORTING_ORDER;
        }

        private static void ConfigureCanvasGroupFade(CanvasGroupFade canvasFade, CanvasGroup canvasGroup)
        {
            if (canvasFade == null || canvasGroup == null)
            {
                return;
            }

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
            if (target == null)
            {
                return;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }

        private static Toggle CreateToggle(RectTransform parent, TMP_FontAsset font, out TextMeshProUGUI label)
        {
            GameObject root = CreateUIObject("AnonymousToggle", parent, typeof(RectTransform), typeof(Toggle));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            SetPreferredWidth(rootRect, 430f);
            SetPreferredHeight(rootRect, BODY_LINE_HEIGHT);

            GameObject background = CreateUIObject("Background", rootRect, typeof(Image));
            RectTransform bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.5f);
            bgRect.anchorMax = new Vector2(0f, 0.5f);
            bgRect.pivot = new Vector2(0f, 0.5f);
            bgRect.sizeDelta = new Vector2(24f, 24f);

            Image bgImage = background.GetComponent<Image>();

            label = CreateText("Label", rootRect, font, BODY_FONT_SIZE, FontStyles.Normal, TextAlignmentOptions.Left);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(38f, 0f);
            labelRect.offsetMax = Vector2.zero;
            label.color = BossRushUIColors.TextPrimary;

            Toggle toggle = root.GetComponent<Toggle>();
            StyleAnonymousToggle(toggle, bgImage);   // 圆角框 + 三态色 + 程序化对勾 + 官方音效（UD-19 / UD-20）
            toggle.isOn = false;
            return toggle;
        }

        private static Button CreateButton(RectTransform parent, TMP_FontAsset font, out TextMeshProUGUI label, float width, float height, bool primary)
        {
            GameObject root = CreateUIObject("Button", parent, typeof(Image), typeof(Button));
            RectTransform rect = root.GetComponent<RectTransform>();
            SetPreferredWidth(rect, width);
            SetPreferredHeight(rect, height);

            BossRushUI.ApplyPanelSkin(root.GetComponent<Image>(), 8, BossRushUISkinPart.Button);
            Button button = root.GetComponent<Button>();
            label = CreateText("Text", rect, font, BUTTON_FONT_SIZE, FontStyles.Bold, TextAlignmentOptions.Center);
            StretchRect(label.rectTransform);

            // 全 Mod 按钮口径：主操作 AccentFill、其余次级；三态、音效、按下回弹、投影斜面都由共享层给，
            // 标签色由 ApplyButtonColors 按底色算（只认名为 "Text" 的子物体，所以标签要先建）。
            if (primary)
            {
                ZombieModeUIHelper.SetButtonBaseColor(button, BossRushUIColors.AccentFill);
            }
            else
            {
                BossRushUIKit.StyleSecondaryButton(button);
            }
            return button;
        }

        private static TextMeshProUGUI CreateText(string name, RectTransform parent, TMP_FontAsset font, float fontSize, FontStyles style, TextAlignmentOptions alignment)
        {
            GameObject go = CreateUIObject(name, parent, typeof(TextMeshProUGUI));
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            ConfigureTMPText(text, font, fontSize, alignment);
            text.fontStyle = style;
            return text;
        }

        private static void ConfigureTMPText(TextMeshProUGUI text, TMP_FontAsset font, float fontSize, TextAlignmentOptions alignment)
        {
            if (font != null)
            {
                text.font = font;
            }
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = BossRushUIColors.TextPrimary;
            text.text = string.Empty;
            text.raycastTarget = false;
        }

        private static GameObject CreateUIObject(string name, RectTransform parent, params Type[] components)
        {
            List<Type> finalTypes = new List<Type>();
            bool hasRectTransform = false;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == typeof(RectTransform))
                {
                    hasRectTransform = true;
                }
                finalTypes.Add(components[i]);
            }

            if (!hasRectTransform)
            {
                finalTypes.Insert(0, typeof(RectTransform));
            }

            GameObject go = new GameObject(name, finalTypes.ToArray());
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetPreferredHeight(RectTransform rect, float height)
        {
            LayoutElement element = rect.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = rect.gameObject.AddComponent<LayoutElement>();
            }
            element.preferredHeight = height;
        }

        private static void SetPreferredWidth(RectTransform rect, float width)
        {
            LayoutElement element = rect.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = rect.gameObject.AddComponent<LayoutElement>();
            }
            element.preferredWidth = width;
        }
    }

}
