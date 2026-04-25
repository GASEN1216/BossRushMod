// ============================================================================
// ConfirmDialogUI.cs - 通用确认对话框UI组件
// ============================================================================
// 模块说明：
//   通用的确认对话框UI组件，支持复用
//   使用静态单例模式，避免重复创建UI对象
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BossRush.Utils;

namespace BossRush.UI
{
    /// <summary>
    /// 通用确认对话框UI组件
    /// 单例模式，支持复用
    /// </summary>
    public static class ConfirmDialogUI
    {
        // ============================================================================
        // UI元素
        // ============================================================================
        
        private static GameObject dialogRoot;
        private static TextMeshProUGUI messageText;
        private static Button confirmButton;
        private static Button cancelButton;
        private static TextMeshProUGUI confirmButtonText;
        private static TextMeshProUGUI cancelButtonText;
        private static RectTransform panelRect;
        private static RectTransform messageRect;
        private static RectTransform buttonContainerRect;
        
        // ============================================================================
        // 回调
        // ============================================================================
        
        private static Action onConfirm;
        private static Action onCancel;
        
        // ============================================================================
        // 公共接口
        // ============================================================================
        
        /// <summary>
        /// 显示确认对话框
        /// </summary>
        /// <param name="message">消息文本（支持富文本）</param>
        /// <param name="onConfirmAction">确认回调</param>
        /// <param name="onCancelAction">取消回调</param>
        /// <param name="confirmText">确认按钮文本（可选）</param>
        /// <param name="cancelText">取消按钮文本（可选）</param>
        public static void Show(
            string message,
            Action onConfirmAction,
            Action onCancelAction,
            string confirmText = null,
            string cancelText = null)
        {
            onConfirm = onConfirmAction;
            onCancel = onCancelAction;
            
            // 如果对话框已存在，直接更新内容并显示
            if (dialogRoot != null)
            {
                UpdateDialogContent(message, confirmText, cancelText);
                ApplyResponsiveLayout();
                dialogRoot.SetActive(true);
                return;
            }
            
            // 创建新的对话框
            CreateDialog(message, confirmText, cancelText);
        }
        
        /// <summary>
        /// 隐藏确认对话框
        /// </summary>
        public static void Hide()
        {
            if (dialogRoot != null)
            {
                dialogRoot.SetActive(false);
            }
            onConfirm = null;
            onCancel = null;
        }
        
        /// <summary>
        /// 检查对话框是否显示中
        /// </summary>
        public static bool IsVisible
        {
            get { return dialogRoot != null && dialogRoot.activeSelf; }
        }
        
        // ============================================================================
        // 私有方法
        // ============================================================================
        
        /// <summary>
        /// 更新对话框内容
        /// </summary>
        private static void UpdateDialogContent(string message, string confirmText, string cancelText)
        {
            if (messageText != null)
            {
                messageText.text = message;
            }
            
            if (confirmButtonText != null && !string.IsNullOrEmpty(confirmText))
            {
                confirmButtonText.text = confirmText;
            }
            
            if (cancelButtonText != null && !string.IsNullOrEmpty(cancelText))
            {
                cancelButtonText.text = cancelText;
            }

            ApplyResponsiveLayout();
        }
        
        /// <summary>
        /// 创建对话框
        /// </summary>
        private static void CreateDialog(string message, string confirmText, string cancelText)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                Canvas canvas = FindMainCanvas();
                if (canvas == null)
                {
                    ModBehaviour.DevLog("[ConfirmDialogUI] 无法找到Canvas");
                    return;
                }
                
                // 创建对话框根对象
                dialogRoot = new GameObject("ConfirmDialogUI");
                dialogRoot.transform.SetParent(canvas.transform, false);
                
                // 添加背景遮罩
                Image bgMask = dialogRoot.AddComponent<Image>();
                bgMask.color = new Color(0, 0, 0, 0.6f);
                bgMask.raycastTarget = true;
                
                RectTransform rootRect = dialogRoot.GetComponent<RectTransform>();
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;
                
                // 创建面板
                GameObject panel = new GameObject("Panel");
                panel.transform.SetParent(dialogRoot.transform, false);
                
                Image panelBg = panel.AddComponent<Image>();
                panelBg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
                
                panelRect = panel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                
                // 创建消息文本
                GameObject textObj = new GameObject("Message");
                textObj.transform.SetParent(panel.transform, false);
                
                messageText = textObj.AddComponent<TextMeshProUGUI>();
                messageText.text = message;
                messageText.fontSize = 20;
                messageText.alignment = TextAlignmentOptions.Center;
                messageText.color = Color.white;
                messageText.richText = true;  // 支持富文本
                messageText.enableAutoSizing = true;
                messageText.fontSizeMin = 15f;
                messageText.fontSizeMax = 24f;
                messageText.enableWordWrapping = true;
                messageText.overflowMode = TextOverflowModes.Ellipsis;
                messageText.lineSpacing = 6f;
                
                messageRect = textObj.GetComponent<RectTransform>();
                messageRect.anchorMin = new Vector2(0f, 0f);
                messageRect.anchorMax = new Vector2(1f, 1f);
                
                // 创建按钮容器
                GameObject buttonContainer = new GameObject("Buttons");
                buttonContainer.transform.SetParent(panel.transform, false);
                
                buttonContainerRect = buttonContainer.AddComponent<RectTransform>();
                buttonContainerRect.anchorMin = new Vector2(0f, 0f);
                buttonContainerRect.anchorMax = new Vector2(1f, 0f);
                buttonContainerRect.pivot = new Vector2(0.5f, 0f);
                
                // 默认按钮文本
                string defaultConfirmText = confirmText ?? L10n.T("确认", "Confirm");
                string defaultCancelText = cancelText ?? L10n.T("取消", "Cancel");
                
                // 创建确认按钮
                CreateButton(buttonContainer, "ConfirmButton", defaultConfirmText,
                    new Color(0.2f, 0.6f, 0.2f, 1f), new Vector2(0f, 0f), new Vector2(0.47f, 1f),
                    OnConfirmClicked, out confirmButton, out confirmButtonText);
                
                // 创建取消按钮
                CreateButton(buttonContainer, "CancelButton", defaultCancelText,
                    new Color(0.6f, 0.2f, 0.2f, 1f), new Vector2(0.53f, 0f), new Vector2(1f, 1f),
                    OnCancelClicked, out cancelButton, out cancelButtonText);

                ApplyResponsiveLayout();
                
                ModBehaviour.DevLog("[ConfirmDialogUI] 确认对话框创建成功");
            }, "ConfirmDialogUI.CreateDialog");
        }

        private static void ApplyResponsiveLayout()
        {
            if (panelRect == null || messageRect == null || buttonContainerRect == null)
            {
                return;
            }

            float screenWidth = Mathf.Max(1280f, Screen.width);
            float screenHeight = Mathf.Max(720f, Screen.height);

            float width = Mathf.Clamp(screenWidth * 0.34f, 440f, 760f);
            float height = Mathf.Clamp(screenHeight * 0.30f, 230f, 360f);
            float horizontalPadding = Mathf.Clamp(width * 0.07f, 26f, 44f);
            float topPadding = Mathf.Clamp(height * 0.08f, 18f, 28f);
            float bottomPadding = Mathf.Clamp(height * 0.08f, 18f, 28f);
            float buttonHeight = Mathf.Clamp(height * 0.24f, 54f, 72f);
            float textBottomInset = bottomPadding + buttonHeight + 16f;

            panelRect.sizeDelta = new Vector2(width, height);

            messageRect.offsetMin = new Vector2(horizontalPadding, textBottomInset);
            messageRect.offsetMax = new Vector2(-horizontalPadding, -topPadding);

            buttonContainerRect.anchoredPosition = new Vector2(0f, bottomPadding);
            buttonContainerRect.sizeDelta = new Vector2(-horizontalPadding * 2f, buttonHeight);

            if (messageText != null)
            {
                messageText.margin = new Vector4(2f, 2f, 2f, 2f);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        }
        
        /// <summary>
        /// 创建按钮
        /// </summary>
        private static void CreateButton(GameObject parent, string name, string text, Color bgColor,
            Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick, 
            out Button button, out TextMeshProUGUI buttonText)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent.transform, false);
            
            Image btnBg = btnObj.AddComponent<Image>();
            btnBg.color = bgColor;
            
            button = btnObj.AddComponent<Button>();
            button.onClick.AddListener(onClick);
            
            RectTransform btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.anchorMin = anchorMin;
            btnRect.anchorMax = anchorMax;
            btnRect.offsetMin = Vector2.zero;
            btnRect.offsetMax = Vector2.zero;
            
            // 按钮文字
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            
            buttonText = textObj.AddComponent<TextMeshProUGUI>();
            buttonText.text = text;
            buttonText.fontSize = 16;
            buttonText.alignment = TextAlignmentOptions.Center;
            buttonText.color = Color.white;
            buttonText.enableAutoSizing = true;
            buttonText.fontSizeMin = 12f;
            buttonText.fontSizeMax = 18f;
            buttonText.enableWordWrapping = false;
            buttonText.overflowMode = TextOverflowModes.Ellipsis;
            
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }
        
        /// <summary>
        /// 查找主Canvas
        /// </summary>
        private static Canvas FindMainCanvas()
        {
            Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            foreach (var canvas in canvases)
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    return canvas;
                }
            }
            return canvases.Length > 0 ? canvases[0] : null;
        }
        
        /// <summary>
        /// 确认按钮点击回调
        /// </summary>
        private static void OnConfirmClicked()
        {
            onConfirm?.Invoke();
        }
        
        /// <summary>
        /// 取消按钮点击回调
        /// </summary>
        private static void OnCancelClicked()
        {
            onCancel?.Invoke();
        }
    }
}
