// ============================================================================
// WishFountainUI.cs - 布满了灰尘的星愿许愿台运行时 View 面板
// ============================================================================
// 模块说明：
//   使用 Duckov 原版 View / FadeGroup 体系运行时创建许愿面板：
//   - 固定高度的多行 TMP_InputField，超长内容通过垂直滚动条浏览
//   - 文本框聚焦高亮、固定提示文案、字数统计与状态提示
//   - 匿名勾选默认关闭，发送成功后走原版 NotificationText 大横幅
//   - 发送成功后 3 秒自动关闭，失败时保留输入内容供重试
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
    public class WishFountainView : View
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
        private Outline inputContainerOutline;
        private Scrollbar inputScrollbar;

        private bool sending;
        private bool successDisplayed;
        private bool submittedThisSession;
        private bool hasExplicitStatus;
        private Color statusColor = new Color(1f, 0.75f, 0.35f);
        private Coroutine autoCloseCoroutine;

        private RectTransform panelRectTransform;
        private LayoutElement inputContainerLayoutElement;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private int lastCooldownRemaining = -1;
        private bool lastCooldownState;
        private bool cooldownStateInitialized;
        private bool lastInputFocusState;
        private bool inputFocusStateInitialized;

        private const float BASE_PANEL_WIDTH = 820f;
        private const float MIN_PANEL_WIDTH = 400f;
        private const float PANEL_WIDTH_RATIO = 0.55f;
        private const float MAX_HEIGHT_RATIO = 0.85f;
        private const float BASE_INPUT_HEIGHT = 186f;
        private const float MIN_INPUT_HEIGHT = 100f;
        private const float FIXED_CONTENT_HEIGHT = 466f;

        public static WishFountainView CreateRuntime(Transform parent)
        {
            if (parent == null)
            {
                return null;
            }

            GameObject host = new GameObject("BossRush_WishFountainViewHost", typeof(RectTransform));
            host.transform.SetParent(parent, false);

            RectTransform hostRect = host.GetComponent<RectTransform>();
            StretchRect(hostRect);

            GameObject root = new GameObject(
                "WishFountainView",
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
            overlay.color = new Color(0f, 0f, 0f, 0.58f);
            overlay.raycastTarget = true;

            FadeGroup fadeGroup = root.GetComponent<FadeGroup>();
            fadeGroup.manageGameObjectActive = true;

            ConfigureFadeGroup(root, fadeGroup);

            WishFountainView view = root.AddComponent<WishFountainView>();
            view.fadeGroup = fadeGroup;
            view.BuildLayout(rootRect);

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

            base.OnDestroy();
        }

        public void ResetAndOpen()
        {
            if (autoCloseCoroutine != null)
            {
                StopCoroutine(autoCloseCoroutine);
                autoCloseCoroutine = null;
            }

            sending = false;
            successDisplayed = false;
            submittedThisSession = false;
            hasExplicitStatus = false;
            statusColor = new Color(1f, 0.75f, 0.35f);
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
                StartCoroutine(FocusInputFieldNextFrame());
                return;
            }

            Open();
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            transform.SetAsLastSibling();

            if (fadeGroup != null)
            {
                fadeGroup.Show();
            }

            AdjustPanelForResolution();
            StartCoroutine(FocusInputFieldNextFrame());
        }

        protected override void OnClose()
        {
            base.OnClose();

            if (fadeGroup != null)
            {
                fadeGroup.SkipHide();
            }

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
            TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;

            GameObject panel = CreateUIObject("Panel", rootRect, typeof(Image), typeof(VerticalLayoutGroup), typeof(Shadow), typeof(ContentSizeFitter));
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
            panelImage.color = new Color(0.07f, 0.1f, 0.17f, 0.98f);

            Shadow panelShadow = panel.GetComponent<Shadow>();
            panelShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            panelShadow.effectDistance = new Vector2(0f, -14f);

            VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(32, 32, 24, 34);
            panelLayout.spacing = 16f;
            panelLayout.childControlHeight = true;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childForceExpandWidth = true;

            GameObject accentLine = CreateUIObject("AccentLine", panelRect, typeof(Image));
            RectTransform accentLineRect = accentLine.GetComponent<RectTransform>();
            SetPreferredHeight(accentLineRect, 4f);
            Image accentLineImage = accentLine.GetComponent<Image>();
            accentLineImage.color = new Color(0.44f, 0.78f, 1f, 0.95f);
            accentLineImage.raycastTarget = false;

            GameObject headerBlock = CreateUIObject("HeaderBlock", panelRect, typeof(VerticalLayoutGroup));
            RectTransform headerRect = headerBlock.GetComponent<RectTransform>();
            VerticalLayoutGroup headerLayout = headerBlock.GetComponent<VerticalLayoutGroup>();
            headerLayout.spacing = 8f;
            headerLayout.childControlHeight = true;
            headerLayout.childControlWidth = true;
            headerLayout.childForceExpandHeight = false;
            headerLayout.childForceExpandWidth = true;


            titleText = CreateText("Title", headerRect, defaultFont, 34, FontStyles.Bold, TextAlignmentOptions.Center);
            SetPreferredHeight(titleText.rectTransform, 38f);

            hintText = CreateText("Hint", headerRect, defaultFont, 18, FontStyles.Normal, TextAlignmentOptions.Center);
            hintText.enableWordWrapping = true;
            hintText.color = new Color(0.74f, 0.8f, 0.9f, 0.95f);
            SetPreferredHeight(hintText.rectTransform, 28f);

            GameObject contentCard = CreateUIObject("ContentCard", panelRect, typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform contentCardRect = contentCard.GetComponent<RectTransform>();
            Image contentCardImage = contentCard.GetComponent<Image>();
            contentCardImage.color = new Color(0.09f, 0.13f, 0.22f, 0.98f);
            VerticalLayoutGroup contentCardLayout = contentCard.GetComponent<VerticalLayoutGroup>();
            contentCardLayout.padding = new RectOffset(20, 20, 16, 16);
            contentCardLayout.spacing = 12f;
            contentCardLayout.childControlHeight = true;
            contentCardLayout.childControlWidth = true;
            contentCardLayout.childForceExpandHeight = false;
            contentCardLayout.childForceExpandWidth = true;

            GameObject inputHeaderRow = CreateUIObject("InputHeaderRow", contentCardRect, typeof(HorizontalLayoutGroup));
            RectTransform inputHeaderRowRect = inputHeaderRow.GetComponent<RectTransform>();
            SetPreferredHeight(inputHeaderRowRect, 22f);
            HorizontalLayoutGroup inputHeaderLayout = inputHeaderRow.GetComponent<HorizontalLayoutGroup>();
            inputHeaderLayout.spacing = 8f;
            inputHeaderLayout.childControlWidth = true;
            inputHeaderLayout.childControlHeight = true;
            inputHeaderLayout.childForceExpandHeight = false;
            inputHeaderLayout.childForceExpandWidth = false;
            inputHeaderLayout.childAlignment = TextAnchor.MiddleLeft;

            TextMeshProUGUI inputCaption = CreateText("InputCaption", inputHeaderRowRect, defaultFont, 16, FontStyles.Bold, TextAlignmentOptions.Left);
            inputCaption.text = L10n.T("心愿内容", "Wish Content");
            inputCaption.color = new Color(0.8f, 0.86f, 0.95f, 0.95f);
            SetPreferredWidth(inputCaption.rectTransform, 110f);
            SetPreferredHeight(inputCaption.rectTransform, 20f);

            GameObject inputHeaderSpacer = CreateUIObject("InputHeaderSpacer", inputHeaderRowRect, typeof(LayoutElement));
            LayoutElement inputHeaderSpacerElement = inputHeaderSpacer.GetComponent<LayoutElement>();
            inputHeaderSpacerElement.flexibleWidth = 1f;

            inputFocusHintText = CreateText("InputFocusHint", inputHeaderRowRect, defaultFont, 14, FontStyles.Normal, TextAlignmentOptions.Right);
            inputFocusHintText.color = new Color(0.62f, 0.7f, 0.8f, 0.96f);
            SetPreferredWidth(inputFocusHintText.rectTransform, 250f);
            SetPreferredHeight(inputFocusHintText.rectTransform, 18f);

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
            inputContainerImage.color = new Color(0.04f, 0.06f, 0.11f, 1f);
            inputContainerOutline = inputContainer.AddComponent<Outline>();
            inputContainerOutline.effectColor = new Color(0.16f, 0.24f, 0.34f, 0.95f);
            inputContainerOutline.effectDistance = new Vector2(1f, -1f);
            inputContainerOutline.useGraphicAlpha = false;

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
            ConfigureTMPText(inputText, defaultFont, 18, TextAlignmentOptions.TopLeft);
            inputText.enableWordWrapping = true;
            inputText.overflowMode = TextOverflowModes.Overflow;

            GameObject placeholderGO = CreateUIObject("Placeholder", inputViewportRect, typeof(TextMeshProUGUI));
            RectTransform placeholderRect = placeholderGO.GetComponent<RectTransform>();
            StretchRect(placeholderRect);
            placeholderText = placeholderGO.GetComponent<TextMeshProUGUI>();
            ConfigureTMPText(placeholderText, defaultFont, 18, TextAlignmentOptions.TopLeft);
            placeholderText.enableWordWrapping = true;
            placeholderText.color = new Color(0.55f, 0.63f, 0.72f, 0.82f);

            inputField = inputContainer.AddComponent<TMP_InputField>();
            inputField.textViewport = inputViewportRect;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            inputField.characterLimit = WishFountainService.MAX_CHARS;
            inputField.richText = false;
            inputField.scrollSensitivity = 20f;
            inputField.pointSize = 20f;
            inputField.customCaretColor = true;
            inputField.caretColor = new Color(0.78f, 0.94f, 1f, 1f);
            inputField.caretWidth = 3;
            inputField.selectionColor = new Color(0.26f, 0.57f, 0.9f, 0.38f);
            inputField.transition = Selectable.Transition.None;

            inputField.targetGraphic = inputContainerImage;
            ColorBlock cb = inputField.colors;
            cb.normalColor = inputContainerImage.color;
            cb.highlightedColor = new Color(0.08f, 0.12f, 0.21f, 1f);
            cb.selectedColor = new Color(0.12f, 0.17f, 0.3f, 1f);
            cb.pressedColor = new Color(0.05f, 0.08f, 0.14f, 1f);
            cb.colorMultiplier = 1f;
            inputField.colors = cb;

            GameObject scrollbarGO = CreateUIObject("VerticalScrollbar", inputContainerRect, typeof(Image), typeof(Scrollbar));
            RectTransform scrollbarRect = scrollbarGO.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-18f, 10f);
            scrollbarRect.offsetMax = new Vector2(-8f, -10f);

            Image scrollbarTrackImage = scrollbarGO.GetComponent<Image>();
            scrollbarTrackImage.color = new Color(0.11f, 0.16f, 0.23f, 0.95f);

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
            handleImage.color = new Color(0.48f, 0.82f, 1f, 0.92f);

            inputScrollbar.targetGraphic = handleImage;
            inputScrollbar.handleRect = handleRect;

            ColorBlock scrollbarColors = inputScrollbar.colors;
            scrollbarColors.normalColor = handleImage.color;
            scrollbarColors.highlightedColor = new Color(0.62f, 0.9f, 1f, 0.96f);
            scrollbarColors.pressedColor = new Color(0.34f, 0.68f, 0.92f, 1f);
            scrollbarColors.selectedColor = scrollbarColors.highlightedColor;
            scrollbarColors.disabledColor = new Color(0.22f, 0.28f, 0.34f, 0.9f);
            scrollbarColors.colorMultiplier = 1f;
            inputScrollbar.colors = scrollbarColors;

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

            countText = CreateText("CountText", metaRow.GetComponent<RectTransform>(), defaultFont, 16, FontStyles.Normal, TextAlignmentOptions.Right);
            SetPreferredWidth(countText.rectTransform, 170f);
            SetPreferredHeight(countText.rectTransform, 28f);

            GameObject statusCard = CreateUIObject("StatusCard", contentCardRect, typeof(Image));
            RectTransform statusCardRect = statusCard.GetComponent<RectTransform>();
            SetPreferredHeight(statusCardRect, 48f);
            Image statusCardImage = statusCard.GetComponent<Image>();
            statusCardImage.color = new Color(0.12f, 0.15f, 0.23f, 0.95f);

            statusText = CreateText("StatusText", statusCardRect, defaultFont, 17, FontStyles.Normal, TextAlignmentOptions.Left);
            statusText.enableWordWrapping = true;
            statusText.rectTransform.anchorMin = Vector2.zero;
            statusText.rectTransform.anchorMax = Vector2.one;
            statusText.rectTransform.offsetMin = new Vector2(14f, 8f);
            statusText.rectTransform.offsetMax = new Vector2(-14f, -8f);

            GameObject actionBar = CreateUIObject("ActionBar", panelRect, typeof(Image), typeof(VerticalLayoutGroup));
            RectTransform actionBarRect = actionBar.GetComponent<RectTransform>();
            Image actionBarImage = actionBar.GetComponent<Image>();
            actionBarImage.color = new Color(0.08f, 0.11f, 0.18f, 0.98f);
            VerticalLayoutGroup actionBarLayout = actionBar.GetComponent<VerticalLayoutGroup>();
            actionBarLayout.padding = new RectOffset(18, 18, 12, 10);
            actionBarLayout.spacing = 8f;
            actionBarLayout.childControlHeight = true;
            actionBarLayout.childControlWidth = true;
            actionBarLayout.childForceExpandHeight = false;
            actionBarLayout.childForceExpandWidth = true;

            GameObject divider = CreateUIObject("Divider", actionBarRect, typeof(Image));
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            SetPreferredHeight(dividerRect, 2f);
            Image dividerImage = divider.GetComponent<Image>();
            dividerImage.color = new Color(0.26f, 0.44f, 0.64f, 0.9f);
            dividerImage.raycastTarget = false;

            GameObject buttonRow = CreateUIObject("ButtonRow", actionBarRect, typeof(HorizontalLayoutGroup));
            HorizontalLayoutGroup buttonLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
            buttonLayout.childControlWidth = true;
            buttonLayout.childControlHeight = true;
            buttonLayout.childForceExpandHeight = false;
            buttonLayout.childForceExpandWidth = false;
            buttonLayout.spacing = 16f;
            buttonLayout.childAlignment = TextAnchor.MiddleCenter;

            confirmButton = CreateButton(buttonRow.GetComponent<RectTransform>(), defaultFont, out confirmButtonText, 248f, 46f, true);
            cancelButton = CreateButton(buttonRow.GetComponent<RectTransform>(), defaultFont, out cancelButtonText, 180f, 46f, false);
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

        private void HideImmediately()
        {
            if (fadeGroup != null)
            {
                fadeGroup.SkipHide();
            }
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
                SetStatus(errorMsg, new Color(1f, 0.45f, 0.45f), true);
                return;
            }

            sending = true;
            hasExplicitStatus = true;
            SetStatus(L10n.T("正在将心愿送往星空…", "Sending your wish to the stars…"), new Color(0.9f, 0.9f, 0.95f), true);
            RefreshUIState();

            StartCoroutine(WishFountainService.SendWish(
                standardized,
                anonymousToggle == null || anonymousToggle.isOn,
                () =>
                {
                    sending = false;
                    successDisplayed = true;
                    submittedThisSession = true;
                    SetStatus(L10n.T("心愿已被星空收纳", "Your wish has been received by the stars"), new Color(0.75f, 0.95f, 0.75f), true);
                    RefreshUIState();
                    Close();
                    NotifyClosedAfterSuccessfulWish();
                },
                error =>
                {
                    sending = false;
                    successDisplayed = false;
                    SetStatus(error, new Color(1f, 0.45f, 0.45f), true);
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
                    "Feel free to write down any features you'd like to implement, and I'll see them~");
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
                countText.color = charCount < WishFountainService.MIN_CHARS
                    ? new Color(1f, 0.55f, 0.55f)
                    : new Color(0.85f, 0.85f, 0.88f);
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
                        new Color(1f, 0.75f, 0.35f),
                        false);
                }
                else if (charCount > 0 && charCount < WishFountainService.MIN_CHARS)
                {
                    SetStatus(
                        L10n.T("最少输入 " + WishFountainService.MIN_CHARS + " 个字符哦", "At least " + WishFountainService.MIN_CHARS + " characters required"),
                        new Color(1f, 0.75f, 0.35f),
                        false);
                }
                else
                {
                    SetStatus(" ", new Color(0.8f, 0.8f, 0.85f), false);
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
            bool inputFocused = IsInputFieldFocused();

            if (inputContainerImage != null)
            {
                if (successDisplayed)
                {
                    inputContainerImage.color = new Color(0.05f, 0.09f, 0.12f, 1f);
                }
                else if (sending)
                {
                    inputContainerImage.color = new Color(0.05f, 0.08f, 0.14f, 1f);
                }
                else if (inputFocused)
                {
                    inputContainerImage.color = new Color(0.08f, 0.12f, 0.21f, 1f);
                }
                else
                {
                    inputContainerImage.color = new Color(0.04f, 0.06f, 0.11f, 1f);
                }
            }

            if (inputContainerOutline != null)
            {
                if (successDisplayed)
                {
                    inputContainerOutline.effectColor = new Color(0.38f, 0.74f, 0.6f, 0.95f);
                    inputContainerOutline.effectDistance = new Vector2(2f, -2f);
                }
                else if (sending)
                {
                    inputContainerOutline.effectColor = new Color(0.35f, 0.62f, 0.87f, 0.92f);
                    inputContainerOutline.effectDistance = new Vector2(2f, -2f);
                }
                else if (inputFocused)
                {
                    inputContainerOutline.effectColor = new Color(0.48f, 0.82f, 1f, 0.96f);
                    inputContainerOutline.effectDistance = new Vector2(2f, -2f);
                }
                else
                {
                    inputContainerOutline.effectColor = new Color(0.16f, 0.24f, 0.34f, 0.95f);
                    inputContainerOutline.effectDistance = new Vector2(1f, -1f);
                }
            }

            if (placeholderText != null && !successDisplayed)
            {
                placeholderText.color = inputFocused
                    ? new Color(0.72f, 0.83f, 0.95f, 0.88f)
                    : new Color(0.55f, 0.63f, 0.72f, 0.82f);
            }

            if (inputFocusHintText != null)
            {
                inputFocusHintText.text = L10n.T(
                    "请不要输入无效/垃圾内容哦~",
                    "Please don't enter invalid or spam content~");
                inputFocusHintText.color = new Color(0.9f, 0.82f, 0.58f, 0.98f);
            }
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

        private void ShowOriginalSuccessBanner()
        {
            try
            {
                string message = L10n.T(
                    "愿这点<color=#9FE6FF>星光</color>，照亮你心中的<color=#F3C65F>乌托邦</color>~",
                    "May this <color=#9FE6FF>starlight</color> illuminate the <color=#F3C65F>utopia</color> in your heart~");

                MethodInfo[] methods = typeof(NotificationText).GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || method.Name != "Push")
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters == null || parameters.Length != 2)
                    {
                        continue;
                    }

                    if (parameters[0].ParameterType != typeof(string))
                    {
                        continue;
                    }

                    if (parameters[1].ParameterType == typeof(float))
                    {
                        method.Invoke(null, new object[] { message, 3f });
                        return;
                    }

                    if (parameters[1].ParameterType == typeof(int))
                    {
                        method.Invoke(null, new object[] { message, 3 });
                        return;
                    }
                }

                NotificationText.Push(message);
            }
            catch
            {
            }
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
            SetPreferredHeight(rootRect, 28f);

            GameObject background = CreateUIObject("Background", rootRect, typeof(Image));
            RectTransform bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.5f);
            bgRect.anchorMax = new Vector2(0f, 0.5f);
            bgRect.pivot = new Vector2(0f, 0.5f);
            bgRect.sizeDelta = new Vector2(24f, 24f);

            Image bgImage = background.GetComponent<Image>();
            bgImage.color = new Color(0.11f, 0.16f, 0.25f, 1f);

            GameObject checkmark = CreateUIObject("Checkmark", bgRect, typeof(TextMeshProUGUI));
            RectTransform checkRect = checkmark.GetComponent<RectTransform>();
            StretchRect(checkRect);
            TextMeshProUGUI checkmarkText = checkmark.GetComponent<TextMeshProUGUI>();
            ConfigureTMPText(checkmarkText, font, 18, TextAlignmentOptions.Center);
            checkmarkText.fontStyle = FontStyles.Bold;
            checkmarkText.text = "✓";
            checkmarkText.color = new Color(0.99f, 0.82f, 0.33f, 1f);
            checkmarkText.raycastTarget = false;

            label = CreateText("Label", rootRect, font, 18, FontStyles.Normal, TextAlignmentOptions.Left);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(38f, 0f);
            labelRect.offsetMax = Vector2.zero;
            label.color = new Color(0.9f, 0.93f, 0.98f, 1f);

            Toggle toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = bgImage;
            toggle.graphic = checkmarkText;
            toggle.isOn = false;
            return toggle;
        }

        private static Button CreateButton(RectTransform parent, TMP_FontAsset font, out TextMeshProUGUI label, float width, float height, bool primary)
        {
            GameObject root = CreateUIObject("Button", parent, typeof(Image), typeof(Button));
            RectTransform rect = root.GetComponent<RectTransform>();
            SetPreferredWidth(rect, width);
            SetPreferredHeight(rect, height);

            Image image = root.GetComponent<Image>();
            image.color = primary
                ? new Color(0.22f, 0.53f, 0.76f, 1f)
                : new Color(0.16f, 0.19f, 0.27f, 1f);

            Button button = root.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = primary
                ? new Color(0.28f, 0.61f, 0.86f, 1f)
                : new Color(0.22f, 0.26f, 0.35f, 1f);
            colors.pressedColor = primary
                ? new Color(0.16f, 0.44f, 0.66f, 1f)
                : new Color(0.11f, 0.14f, 0.2f, 1f);
            colors.disabledColor = new Color(0.11f, 0.13f, 0.17f, 0.9f);
            button.colors = colors;

            label = CreateText("Text", rect, font, 20, FontStyles.Bold, TextAlignmentOptions.Center);
            StretchRect(label.rectTransform);
            label.color = primary
                ? new Color(0.98f, 0.99f, 1f, 1f)
                : new Color(0.91f, 0.93f, 0.99f, 1f);
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
            text.color = new Color(0.95f, 0.95f, 0.97f, 1f);
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

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private WishFountainView wishFountainView;

        public void OpenWishFountainUI()
        {
            EnsureWishFountainView();
            if (wishFountainView == null)
            {
                DevLog("[WishFountain] 无法创建原版风格许愿面板");
                return;
            }

            wishFountainView.ResetAndOpen();
            DevLog("[WishFountain] 许愿 View 已打开");
        }

        private void EnsureWishFountainView()
        {
            if (wishFountainView != null)
            {
                return;
            }

            Transform parent = GameplayUIManager.Instance != null ? GameplayUIManager.Instance.transform : null;
            if (parent == null)
            {
                DevLog("[WishFountain] GameplayUIManager 不存在，无法创建许愿面板");
                return;
            }

            wishFountainView = WishFountainView.CreateRuntime(parent);
        }
    }
}
