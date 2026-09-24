using System;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using Duckov.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    public struct OriginalConfirmDialogueResult
    {
        public readonly bool Completed;
        public readonly bool Confirmed;
        public readonly string FailureMessage;

        private OriginalConfirmDialogueResult(bool completed, bool confirmed, string failureMessage)
        {
            Completed = completed;
            Confirmed = confirmed;
            FailureMessage = failureMessage;
        }

        public static OriginalConfirmDialogueResult Success(bool confirmed)
        {
            return new OriginalConfirmDialogueResult(true, confirmed, string.Empty);
        }

        public static OriginalConfirmDialogueResult Failure(string failureMessage)
        {
            return new OriginalConfirmDialogueResult(false, false, failureMessage ?? string.Empty);
        }
    }

    // Historical name kept to avoid touching all call sites; the implementation now
    // uses the BossFilter-style reusable modal UI instead of cloning Duckov's full-screen prompt.
    //
    // 外观（2026-09-23 审美审查 UA-30）：遮罩、面板一律走 BossRushUIColors token，
    // 不再是中性灰面板 + 棕色直角标题条 + 字母「X」——标题直接写在面板上、下接分隔线，关闭走底部「取消」与 ESC。
    // 底部两颗按钮仍克隆官方按钮 prefab（官方外观 + 官方 hover / click 音效，与 BossFilter 同口径），不另染色：
    // 官方 prefab 自带 ButtonAnimation，再挂共享按钮手感会一次悬停响两声。
    // 第二个入口 ExecuteOverActiveView（UA-29）：给「寄存全部丢弃」这类发生在官方 View 里的不可逆操作用，
    // 不关当前 View、默认选中「取消」，并吃掉官方 UI 取消键，避免一按 ESC 连商店一起关掉。
    // 2026-09-24 UI 共识对照审查：标题由调用方写成问句（A-38，旧版写死「扫箱确认」）；不可逆（destructive）时确认键换成
    // 共享的 Danger 实心按钮、与「取消」拉开到 40（A-37，旧版两颗同色同宽、间距 18）——这是「进确认之后」的那颗，允许实心红。
    public static class OriginalConfirmDialogueAdapter
    {
        private const string CanvasName = "BossRush_SweepConfirmCanvas";
        private const int SortingOrder = BossRushUILayers.ModalConfirm;
        private const float PanelWidth = 680f;
        private const float PanelHeight = 300f;
        private const float TitleBarHeight = 56f;
        private const float FooterHeight = 78f;
        private const float ButtonWidth = 160f;
        private const float ButtonHeight = 40f;
        private const float FooterSpacing = 18f;
        /// <summary>不可逆时危险键与「取消」之间拉开的距离（UI 共识第 4 节：破坏性按钮与安全按钮拉开）。</summary>
        private const float DestructiveFooterSpacing = 40f;
        private const int ActiveViewCloseWaitFrames = 8;

        private static GameObject canvasRoot = null;
        private static GameObject panelRoot = null;
        private static Image backdropImage = null;
        private static TextMeshProUGUI titleText = null;
        private static TextMeshProUGUI messageText = null;
        private static Button confirmButton = null;
        private static Button cancelButton = null;
        private static TextMeshProUGUI confirmButtonText = null;
        private static TextMeshProUGUI cancelButtonText = null;
        /// <summary>不可逆时代替 confirmButton 出场的危险确认键（共享按钮、Danger 实心）。</summary>
        private static Button dangerConfirmButton = null;
        private static TextMeshProUGUI dangerConfirmButtonText = null;
        private static HorizontalLayoutGroup footerLayout = null;
        private static SweepConfirmRuntime runtime = null;
        private static UniTaskCompletionSource<bool> pendingCompletion = null;
        private static bool inputClaimed = false;
        private static bool isVisible = false;
        private static float previousTimeScale = 1f;
        private static bool cancelEarlySubscribed = false;
        private static bool defaultToCancel = false;

        /// <summary>
        /// 通用确认（会先关掉当前官方 View）。<paramref name="title"/> 写成问句（「花 ￥1,200 让阿稳扫 6 个箱子？」），
        /// 确认键写「动词 + 对象」，不用「确定」。
        /// </summary>
        public static UniTask<OriginalConfirmDialogueResult> Execute(
            string title,
            string message,
            string confirmText,
            string cancelText)
        {
            return ExecuteCore(title, message, confirmText, cancelText, false, false);
        }

        /// <summary>
        /// 在官方 View（商店、背包）打开着的时候弹确认：不关当前 View，确认 / 取消后回到原界面。
        /// <paramref name="destructive"/> 为真时默认选中「取消」，回车 / 手柄确认键不会误触不可逆操作。
        /// 调用方拿到结果后必须重新检查自己的状态（View 可能已经被关、服务可能已经结束）。
        /// </summary>
        public static UniTask<OriginalConfirmDialogueResult> ExecuteOverActiveView(
            string title,
            string message,
            string confirmText,
            string cancelText,
            bool destructive)
        {
            return ExecuteCore(title, message, confirmText, cancelText, true, destructive);
        }

        private static async UniTask<OriginalConfirmDialogueResult> ExecuteCore(
            string title,
            string message,
            string confirmText,
            string cancelText,
            bool keepActiveView,
            bool destructive)
        {
            try
            {
                if (!keepActiveView)
                {
                    await WaitForPreviousViewCleanup();
                }

                if (!EnsureUiCreated())
                {
                    return OriginalConfirmDialogueResult.Failure(L10n.T(
                        "确认框初始化失败，当前操作已取消。",
                        "Confirmation UI could not be created. The operation was cancelled."));
                }

                if (pendingCompletion != null)
                {
                    return OriginalConfirmDialogueResult.Failure(L10n.T(
                        "确认框忙碌中，请稍后再试。",
                        "Confirmation UI is busy. Try again in a moment."));
                }

                pendingCompletion = new UniTaskCompletionSource<bool>();
                defaultToCancel = destructive;
                ShowDialog(
                    title,
                    message,
                    confirmText,
                    cancelText);

                bool confirmed = await pendingCompletion.Task;
                return OriginalConfirmDialogueResult.Success(confirmed);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] [ERROR] Execute failed: " + e.Message);
                HideDialog();
                pendingCompletion = null;
                return OriginalConfirmDialogueResult.Failure(L10n.T(
                    "确认框执行失败，当前操作已取消。",
                    "Confirmation UI failed. The operation was cancelled."));
            }
        }

        private static async UniTask WaitForPreviousViewCleanup()
        {
            View activeView = View.ActiveView;
            if (activeView == null)
            {
                return;
            }

            try
            {
                ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] Closing active View before sweep confirm: " + activeView.GetType().Name);
                activeView.Close();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] [WARNING] Failed to close active View: " + e.Message);
            }

            for (int frame = 0; frame < ActiveViewCloseWaitFrames && View.ActiveView != null; frame++)
            {
                await UniTask.Yield();
            }
        }

        private static bool EnsureUiCreated()
        {
            if (canvasRoot != null && runtime != null && confirmButton != null && cancelButton != null && messageText != null)
            {
                return true;
            }

            try
            {
                DestroyUi();

                Button buttonPrefab = GameplayDataSettings.UIPrefabs.Button;
                if (buttonPrefab == null)
                {
                    ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] [ERROR] GameplayDataSettings.UIPrefabs.Button is null");
                    return false;
                }

                canvasRoot = new GameObject(CanvasName);
                Canvas canvas = canvasRoot.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;

                CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
                // screenMatchMode 原本显式写成 MatchWidthOrHeight，而这正是新建
                // CanvasScaler 的默认值，因此这里的收口不改变实际缩放行为。
                ZombieModeUIHelper.ConfigureCanvasScaler(scaler);

                canvasRoot.AddComponent<GraphicRaycaster>();
                runtime = canvasRoot.AddComponent<SweepConfirmRuntime>();
                UnityEngine.Object.DontDestroyOnLoad(canvasRoot);

                // 遮罩走共享 token（旧值是 0.45 的纯黑，与全 Mod 其他模态不是一套）。
                Image backgroundImage = BossRushUI.CreateBackdrop(canvasRoot.transform);
                backgroundImage.color = BossRushUIColors.Backdrop;
                backgroundImage.raycastTarget = true;
                backdropImage = backgroundImage;

                panelRoot = new GameObject("Panel");
                panelRoot.transform.SetParent(canvasRoot.transform, false);
                Image panelImage = panelRoot.AddComponent<Image>();
                // 先上色再套皮：投影按底色不透明度判定（BossRushUIDepth），反过来会按默认白色判。
                panelImage.color = BossRushUIColors.Surface;
                BossRushUI.ApplyFramedPanelSkin(panelImage, 14, BossRushUISkinPart.Panel);
                RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

                CreateTitle(panelRoot.transform);
                CreateMessageArea(panelRoot.transform);
                CreateFooter(panelRoot.transform, buttonPrefab);

                canvasRoot.SetActive(false);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] [ERROR] Create UI failed: " + e.Message);
                DestroyUi();
                return false;
            }
        }

        /// <summary>
        /// 标题直接写在面板上，下面接一条分隔线。旧写法是一条没有 sprite 的棕色直角标题条，
        /// 两个上角从圆角面板外面露出来，右上还有一颗写着字母「X」的关闭钮（审美审查 UA-30）；
        /// 关闭现在走底部「取消」与 ESC，不再重复一颗。
        /// </summary>
        private static void CreateTitle(Transform parent)
        {
            GameObject titleTextObj = new GameObject("TitleText");
            titleTextObj.transform.SetParent(parent, false);
            titleText = titleTextObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(titleText);
            titleText.fontSize = 26f;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = BossRushUIColors.TextPrimary;
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
            titleText.enableWordWrapping = false;
            titleText.overflowMode = TextOverflowModes.Ellipsis;
            titleText.raycastTarget = false;
            RectTransform titleTextRect = titleTextObj.GetComponent<RectTransform>();
            titleTextRect.anchorMin = new Vector2(0f, 1f);
            titleTextRect.anchorMax = new Vector2(1f, 1f);
            titleTextRect.pivot = new Vector2(0.5f, 1f);
            titleTextRect.offsetMin = new Vector2(28f, -TitleBarHeight);
            titleTextRect.offsetMax = new Vector2(-28f, 0f);

            GameObject rule = ZombieModeUIHelper.CreateSeparator(
                "TitleRule", parent, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -TitleBarHeight), 2f, BossRushUIColors.Divider);
            RectTransform ruleRect = rule.GetComponent<RectTransform>();
            ruleRect.sizeDelta = new Vector2(-40f, ruleRect.sizeDelta.y);
        }

        private static void CreateMessageArea(Transform parent)
        {
            GameObject messageObj = new GameObject("Message");
            messageObj.transform.SetParent(parent, false);
            messageText = messageObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(messageText);
            messageText.fontSize = 18f;
            messageText.color = BossRushUIColors.TextPrimary;
            messageText.alignment = TextAlignmentOptions.MidlineLeft;
            messageText.richText = true;
            messageText.enableWordWrapping = true;
            messageText.raycastTarget = false;
            messageText.lineSpacing = 8f;
            RectTransform messageRect = messageObj.GetComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0f, 0f);
            messageRect.anchorMax = new Vector2(1f, 1f);
            messageRect.offsetMin = new Vector2(28f, FooterHeight + 10f);
            messageRect.offsetMax = new Vector2(-28f, -TitleBarHeight - 14f);
        }

        private static void CreateFooter(Transform parent, Button buttonPrefab)
        {
            GameObject footer = new GameObject("Footer");
            footer.transform.SetParent(parent, false);
            footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = FooterSpacing;
            footerLayout.padding = new RectOffset(24, 24, 16, 16);
            footerLayout.childAlignment = TextAnchor.MiddleCenter;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childForceExpandHeight = false;
            RectTransform footerRect = footer.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = Vector2.zero;
            footerRect.sizeDelta = new Vector2(0f, FooterHeight);

            confirmButton = UnityEngine.Object.Instantiate(buttonPrefab, footer.transform);
            LayoutElement confirmLayout = confirmButton.gameObject.AddComponent<LayoutElement>();
            confirmLayout.preferredWidth = ButtonWidth;
            confirmLayout.preferredHeight = ButtonHeight;
            confirmButtonText = confirmButton.GetComponentInChildren<TextMeshProUGUI>(true);
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(() => Resolve(true));

            // 危险确认键（A-37）：官方按钮 prefab 的配色由它自己的动画组件管，不另染色；不可逆时换这颗共享 Danger 实心键。
            dangerConfirmButton = ZombieModeUIHelper.CreateButton("DangerConfirm", footer.transform, string.Empty,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ButtonWidth, ButtonHeight), BossRushUIColors.Danger,
                18f, new Vector2(ButtonWidth - 12f, ButtonHeight - 6f), () => Resolve(true), true);
            LayoutElement dangerLayout = dangerConfirmButton.gameObject.AddComponent<LayoutElement>();
            dangerLayout.preferredWidth = ButtonWidth;
            dangerLayout.preferredHeight = ButtonHeight;
            dangerConfirmButtonText = dangerConfirmButton.GetComponentInChildren<TextMeshProUGUI>(true);
            dangerConfirmButton.gameObject.SetActive(false);

            cancelButton = UnityEngine.Object.Instantiate(buttonPrefab, footer.transform);
            LayoutElement cancelLayout = cancelButton.gameObject.AddComponent<LayoutElement>();
            cancelLayout.preferredWidth = ButtonWidth;
            cancelLayout.preferredHeight = ButtonHeight;
            cancelButtonText = cancelButton.GetComponentInChildren<TextMeshProUGUI>(true);
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(() => Resolve(false));
        }

        private static void ShowDialog(string title, string message, string confirmText, string cancelText)
        {
            if (canvasRoot == null)
            {
                return;
            }

            if (titleText != null)
            {
                titleText.text = title ?? string.Empty;
            }

            if (messageText != null)
            {
                messageText.text = message ?? string.Empty;
            }

            ApplyButtonText(confirmButtonText, confirmText ?? L10n.T("确认", "Confirm"));
            ApplyButtonText(dangerConfirmButtonText, confirmText ?? L10n.T("确认", "Confirm"));
            ApplyButtonText(cancelButtonText, cancelText ?? L10n.T("取消", "Cancel"));
            // 不可逆：危险实心键在左、「取消」在右并拉开（A-37）；可逆：官方按钮、原间距
            bool destructive = defaultToCancel;
            if (confirmButton != null) confirmButton.gameObject.SetActive(!destructive);
            if (dangerConfirmButton != null) dangerConfirmButton.gameObject.SetActive(destructive);
            if (footerLayout != null) footerLayout.spacing = destructive ? DestructiveFooterSpacing : FooterSpacing;

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            canvasRoot.SetActive(true);
            isVisible = true;
            // 遮罩暗角淡入 + 面板淡入放大，与全 Mod 模态同一口径（旧写法一帧弹出）。画布是复用的，每次显示都重播。
            BossRushUIKit.StyleBackdrop(backdropImage);
            BossRushUI.PlayOpenAnimation(panelRoot);
            SubscribeCancelEarly();

            try
            {
                InputManager.DisableInput(canvasRoot);
                inputClaimed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] [WARNING] Failed to claim UI input: " + e.Message);
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            SelectConfirmButton();
        }

        /// <summary>
        /// 确认框开着时吃掉官方 UI 取消键（ESC / 手柄 B）：官方在 OnCancelEarly 之后才分发给 View 与暂停菜单，
        /// 不吃掉的话，按一下 ESC 会同时关掉确认框背后的商店（ExecuteOverActiveView），或者弹出暂停菜单。
        /// 只在显示期间订阅，HideDialog / 销毁时退订（AGENTS §4.6）。
        /// </summary>
        private static void SubscribeCancelEarly()
        {
            if (cancelEarlySubscribed)
            {
                return;
            }

            try
            {
                UIInputManager.OnCancelEarly += OnUICancelEarly;
                cancelEarlySubscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] [WARNING] Failed to hook UI cancel: " + e.Message);
            }
        }

        private static void UnsubscribeCancelEarly()
        {
            if (!cancelEarlySubscribed)
            {
                return;
            }

            cancelEarlySubscribed = false;
            try
            {
                UIInputManager.OnCancelEarly -= OnUICancelEarly;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] [WARNING] Failed to unhook UI cancel: " + e.Message);
            }
        }

        private static void OnUICancelEarly(UIInputEventData eventData)
        {
            if (!isVisible || eventData == null)
            {
                return;
            }

            eventData.Use();
            Resolve(false);
        }

        private static void HideDialog()
        {
            isVisible = false;
            UnsubscribeCancelEarly();

            if (inputClaimed && canvasRoot != null)
            {
                try
                {
                    InputManager.ActiveInput(canvasRoot);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] [WARNING] Failed to release UI input: " + e.Message);
                }
            }

            inputClaimed = false;

            if (canvasRoot != null)
            {
                canvasRoot.SetActive(false);
            }

            Time.timeScale = previousTimeScale;
        }

        private static void Resolve(bool confirmed)
        {
            UniTaskCompletionSource<bool> completion = pendingCompletion;
            if (completion == null)
            {
                HideDialog();
                return;
            }

            pendingCompletion = null;
            HideDialog();
            completion.TrySetResult(confirmed);
        }

        private static void SelectConfirmButton()
        {
            // 不可逆操作默认停在「取消」上：回车 / 手柄确认键不会一下就把东西删掉。
            Button target = defaultToCancel ? cancelButton : confirmButton;
            if (target == null || EventSystem.current == null)
            {
                return;
            }

            try
            {
                EventSystem.current.SetSelectedGameObject(target.gameObject);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[OriginalConfirmDialogueAdapter] [WARNING] Failed to select confirm button: " + e.Message);
            }
        }

        private static void ApplyButtonText(TextMeshProUGUI label, string text)
        {
            if (label == null)
            {
                return;
            }

            label.text = text ?? string.Empty;
            label.fontSize = 18f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = 20f;
            label.enableWordWrapping = false;
        }

        private static void DestroyUi()
        {
            UnsubscribeCancelEarly();
            if (canvasRoot != null)
            {
                UnityEngine.Object.Destroy(canvasRoot);
            }

            canvasRoot = null;
            panelRoot = null;
            backdropImage = null;
            titleText = null;
            messageText = null;
            confirmButton = null;
            cancelButton = null;
            confirmButtonText = null;
            cancelButtonText = null;
            dangerConfirmButton = null;
            dangerConfirmButtonText = null;
            footerLayout = null;
            runtime = null;
            pendingCompletion = null;
            inputClaimed = false;
            isVisible = false;
            previousTimeScale = 1f;
        }

        private sealed class SweepConfirmRuntime : MonoBehaviour
        {
            private void Update()
            {
                if (!isVisible)
                {
                    return;
                }

                // 官方 UI 取消键已由 OnUICancelEarly 处理并置 isVisible=false；这里兜底官方输入没接管到的情况。
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    Resolve(false);
                }
            }

            private void OnDestroy()
            {
                if (!object.ReferenceEquals(runtime, this))
                {
                    return;
                }

                UniTaskCompletionSource<bool> completion = pendingCompletion;
                pendingCompletion = null;
                HideDialog();
                completion?.TrySetResult(false);
                canvasRoot = null;
                panelRoot = null;
                backdropImage = null;
                titleText = null;
                messageText = null;
                confirmButton = null;
                cancelButton = null;
                confirmButtonText = null;
                cancelButtonText = null;
                dangerConfirmButton = null;
                dangerConfirmButtonText = null;
                footerLayout = null;
                runtime = null;
                inputClaimed = false;
                isVisible = false;
                previousTimeScale = 1f;
            }
        }
    }
}
