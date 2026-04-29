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
    public static class OriginalConfirmDialogueAdapter
    {
        private const string CanvasName = "BossRush_SweepConfirmCanvas";
        private const int SortingOrder = 10;
        private const float BackgroundAlpha = 0.45f;
        private const float PanelWidth = 680f;
        private const float PanelHeight = 300f;
        private const float TitleBarHeight = 50f;
        private const float FooterHeight = 78f;
        private const float ButtonWidth = 160f;
        private const float ButtonHeight = 40f;
        private const int ActiveViewCloseWaitFrames = 8;

        private static GameObject canvasRoot = null;
        private static GameObject panelRoot = null;
        private static TextMeshProUGUI titleText = null;
        private static TextMeshProUGUI messageText = null;
        private static Button confirmButton = null;
        private static Button cancelButton = null;
        private static TextMeshProUGUI confirmButtonText = null;
        private static TextMeshProUGUI cancelButtonText = null;
        private static SweepConfirmRuntime runtime = null;
        private static UniTaskCompletionSource<bool> pendingCompletion = null;
        private static bool inputClaimed = false;
        private static bool isVisible = false;
        private static float previousTimeScale = 1f;

        public static async UniTask<OriginalConfirmDialogueResult> Execute(
            string message,
            string confirmText,
            string cancelText)
        {
            try
            {
                await WaitForPreviousViewCleanup();

                if (!EnsureUiCreated())
                {
                    return OriginalConfirmDialogueResult.Failure(L10n.T(
                        "扫箱确认框初始化失败，当前操作已取消。",
                        "Sweep confirmation UI could not be created. The operation was cancelled."));
                }

                if (pendingCompletion != null)
                {
                    return OriginalConfirmDialogueResult.Failure(L10n.T(
                        "扫箱确认框忙碌中，请稍后再试。",
                        "Sweep confirmation UI is busy. Try again in a moment."));
                }

                pendingCompletion = new UniTaskCompletionSource<bool>();
                ShowDialog(
                    L10n.T("扫箱确认", "Sweep Confirmation"),
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
                    "扫箱确认框执行失败，当前操作已取消。",
                    "Sweep confirmation UI failed. The operation was cancelled."));
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
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                canvasRoot.AddComponent<GraphicRaycaster>();
                runtime = canvasRoot.AddComponent<SweepConfirmRuntime>();
                UnityEngine.Object.DontDestroyOnLoad(canvasRoot);

                GameObject background = new GameObject("Background");
                background.transform.SetParent(canvasRoot.transform, false);
                Image backgroundImage = background.AddComponent<Image>();
                backgroundImage.color = new Color(0f, 0f, 0f, BackgroundAlpha);
                backgroundImage.raycastTarget = true;
                RectTransform backgroundRect = background.GetComponent<RectTransform>();
                backgroundRect.anchorMin = Vector2.zero;
                backgroundRect.anchorMax = Vector2.one;
                backgroundRect.offsetMin = Vector2.zero;
                backgroundRect.offsetMax = Vector2.zero;

                panelRoot = new GameObject("Panel");
                panelRoot.transform.SetParent(canvasRoot.transform, false);
                Image panelImage = panelRoot.AddComponent<Image>();
                panelImage.color = new Color(0.12f, 0.12f, 0.12f, 0.94f);
                RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

                CreateTitleBar(panelRoot.transform, buttonPrefab);
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

        private static void CreateTitleBar(Transform parent, Button buttonPrefab)
        {
            GameObject titleBar = new GameObject("TitleBar");
            titleBar.transform.SetParent(parent, false);
            Image titleBarImage = titleBar.AddComponent<Image>();
            titleBarImage.color = new Color(0.24f, 0.19f, 0.13f, 0.98f);
            RectTransform titleBarRect = titleBar.GetComponent<RectTransform>();
            titleBarRect.anchorMin = new Vector2(0f, 1f);
            titleBarRect.anchorMax = new Vector2(1f, 1f);
            titleBarRect.pivot = new Vector2(0.5f, 1f);
            titleBarRect.anchoredPosition = Vector2.zero;
            titleBarRect.sizeDelta = new Vector2(0f, TitleBarHeight);

            GameObject titleTextObj = new GameObject("TitleText");
            titleTextObj.transform.SetParent(titleBar.transform, false);
            titleText = titleTextObj.AddComponent<TextMeshProUGUI>();
            titleText.fontSize = 24f;
            titleText.color = Color.white;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.raycastTarget = false;
            RectTransform titleTextRect = titleTextObj.GetComponent<RectTransform>();
            titleTextRect.anchorMin = new Vector2(0f, 0f);
            titleTextRect.anchorMax = new Vector2(1f, 1f);
            titleTextRect.offsetMin = new Vector2(56f, 0f);
            titleTextRect.offsetMax = new Vector2(-56f, 0f);

            Button closeButton = UnityEngine.Object.Instantiate(buttonPrefab, titleBar.transform);
            RectTransform closeButtonRect = closeButton.GetComponent<RectTransform>();
            closeButtonRect.anchorMin = new Vector2(1f, 0.5f);
            closeButtonRect.anchorMax = new Vector2(1f, 0.5f);
            closeButtonRect.pivot = new Vector2(1f, 0.5f);
            closeButtonRect.anchoredPosition = new Vector2(-10f, 0f);
            closeButtonRect.sizeDelta = new Vector2(36f, 36f);

            TextMeshProUGUI closeButtonLabel = closeButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (closeButtonLabel != null)
            {
                closeButtonLabel.text = "X";
                closeButtonLabel.fontSize = 20f;
            }

            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => Resolve(false));
        }

        private static void CreateMessageArea(Transform parent)
        {
            GameObject messageObj = new GameObject("Message");
            messageObj.transform.SetParent(parent, false);
            messageText = messageObj.AddComponent<TextMeshProUGUI>();
            messageText.fontSize = 21f;
            messageText.color = Color.white;
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
            HorizontalLayoutGroup footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 18f;
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
            ApplyButtonText(cancelButtonText, cancelText ?? L10n.T("取消", "Cancel"));

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            canvasRoot.SetActive(true);
            isVisible = true;

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

        private static void HideDialog()
        {
            isVisible = false;

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
            if (confirmButton == null || EventSystem.current == null)
            {
                return;
            }

            try
            {
                EventSystem.current.SetSelectedGameObject(confirmButton.gameObject);
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
            if (canvasRoot != null)
            {
                UnityEngine.Object.Destroy(canvasRoot);
            }

            canvasRoot = null;
            panelRoot = null;
            titleText = null;
            messageText = null;
            confirmButton = null;
            cancelButton = null;
            confirmButtonText = null;
            cancelButtonText = null;
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
                titleText = null;
                messageText = null;
                confirmButton = null;
                cancelButton = null;
                confirmButtonText = null;
                cancelButtonText = null;
                runtime = null;
                inputClaimed = false;
                isVisible = false;
                previousTimeScale = 1f;
            }
        }
    }
}
