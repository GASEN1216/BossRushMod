// ============================================================================
// ZombieModeUIHelper.cs - 丧尸模式 UI 通用工具
// ============================================================================
// 模块说明：
//   为丧尸模式所有运行时 UI 视图提供统一的 TMP 字体获取、文本创建
//   和 CanvasScaler 配置，避免各视图分别使用 UI.Text + Arial 导致
//   中文无法显示以及风格不统一。
//
// 主要功能：
//   - GetGameFont: 获取游戏 TMP 字体资产（带缓存）
//   - CreateTMPText: 创建 TextMeshProUGUI 文本组件
//   - ConfigureCanvasScaler: 统一 CanvasScaler 参考分辨率
// ============================================================================

using System;
using System.Reflection;
using Duckov.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// 丧尸模式 UI 通用工具类
    /// </summary>
    internal static class ZombieModeUIHelper
    {
        private static TMP_FontAsset _cachedFont;
        private static int _modalInputLeaseCount;
        private static float _modalPreviousTimeScale = 1f;
        private static bool _modalPreviousCursorVisible;
        private static CursorLockMode _modalPreviousCursorLockState;

        internal static bool IsModalInputPaused
        {
            get { return _modalInputLeaseCount > 0; }
        }

        internal sealed class ModalInputLease
        {
            internal readonly GameObject InputToken;
            internal readonly string OwnerLabel;
            internal bool InputClaimed;
            private bool active;

            internal ModalInputLease(GameObject inputToken, string ownerLabel)
            {
                InputToken = inputToken;
                OwnerLabel = string.IsNullOrEmpty(ownerLabel) ? "ZombieModeModal" : ownerLabel;
                active = true;
            }

            internal void Release()
            {
                if (!active)
                {
                    return;
                }

                active = false;
                ZombieModeUIHelper.ReleaseModalInput(this);
            }
        }

        internal static ModalInputLease ClaimModalInput(GameObject inputToken, string ownerLabel)
        {
            ModalInputLease lease = new ModalInputLease(inputToken, ownerLabel);
            if (_modalInputLeaseCount == 0)
            {
                _modalPreviousTimeScale = Time.timeScale;
                _modalPreviousCursorVisible = Cursor.visible;
                _modalPreviousCursorLockState = Cursor.lockState;

                Time.timeScale = 0f;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            _modalInputLeaseCount++;

            try
            {
                if (inputToken != null)
                {
                    InputManager.DisableInput(inputToken);
                    lease.InputClaimed = true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] " + lease.OwnerLabel + " 输入占用失败: " + e.Message);
            }

            return lease;
        }

        internal static void EnforceModalInputPause()
        {
            if (_modalInputLeaseCount <= 0)
            {
                return;
            }

            Time.timeScale = 0f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private static void ReleaseModalInput(ModalInputLease lease)
        {
            if (lease == null)
            {
                return;
            }

            if (lease.InputClaimed && lease.InputToken != null)
            {
                try
                {
                    InputManager.ActiveInput(lease.InputToken);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ZombieMode] " + lease.OwnerLabel + " 输入释放失败: " + e.Message);
                }
            }

            lease.InputClaimed = false;
            _modalInputLeaseCount = Mathf.Max(0, _modalInputLeaseCount - 1);
            if (_modalInputLeaseCount > 0)
            {
                return;
            }

            Time.timeScale = _modalPreviousTimeScale;
            Cursor.visible = _modalPreviousCursorVisible;
            Cursor.lockState = _modalPreviousCursorLockState;
        }

        /// <summary>
        /// 获取游戏 TMP 字体资产（带缓存），与 HealthBar 名字显示使用相同字体。
        /// </summary>
        internal static TMP_FontAsset GetGameFont()
        {
            if (_cachedFont != null)
            {
                return _cachedFont;
            }

            // 优先从 TMP 全局默认字体获取
            try
            {
                if (TMP_Settings.defaultFontAsset != null)
                {
                    _cachedFont = TMP_Settings.defaultFontAsset;
                    return _cachedFont;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] UIHelper TMP_Settings 读取失败: " + e.Message);
            }

            // 回退：从 HealthBar prefab 的 nameText 获取
            try
            {
                Duckov.UI.HealthBarManager manager = Duckov.UI.HealthBarManager.Instance;
                if (manager != null && manager.healthBarPrefab != null)
                {
                    FieldInfo nameTextField = BossRush.Common.Utils.ReflectionCache.GetField(
                        typeof(Duckov.UI.HealthBar),
                        "nameText",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (nameTextField != null)
                    {
                        TMP_Text nameText = nameTextField.GetValue(manager.healthBarPrefab) as TMP_Text;
                        if (nameText != null && nameText.font != null)
                        {
                            _cachedFont = nameText.font;
                            return _cachedFont;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] UIHelper HealthBar 字体读取失败: " + e.Message);
            }

            // 回退：从场景中任意已有的 TMP_Text 获取
            try
            {
                TMP_Text existing = UnityEngine.Object.FindObjectOfType<TMP_Text>();
                if (existing != null && existing.font != null)
                {
                    _cachedFont = existing.font;
                    return _cachedFont;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] UIHelper TMP_Text 扫描失败: " + e.Message);
            }

            // 最后回退：从已加载的资源中查找
            try
            {
                TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0)
                {
                    _cachedFont = fonts[0];
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] UIHelper 字体资源回退失败: " + e.Message);
            }

            return _cachedFont;
        }

        /// <summary>
        /// 创建 TextMeshProUGUI 文本组件（统一替代 UI.Text + Arial）
        /// </summary>
        internal static TextMeshProUGUI CreateTMPText(
            GameObject obj,
            string text,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color)
        {
            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = GetGameFont();
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontSizeMax = fontSize;
            tmp.fontSizeMin = Mathf.Max(10f, fontSize * 0.65f);
            tmp.enableAutoSizing = true;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.margin = new Vector4(4f, 2f, 4f, 2f);
            return tmp;
        }

        internal static GameObject CreateRect(string name, Transform parent, Vector2 anchor, Vector2 size)
        {
            return CreateRect(name, parent, anchor, anchor, Vector2.zero, size, new Vector2(0.5f, 0.5f));
        }

        internal static GameObject CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            Vector2 pivot)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
            return obj;
        }

        internal static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Vector2 position,
            Vector2 size,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            return CreateTMPText(obj, text, fontSize, alignment, color);
        }

        internal static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject obj = CreateRect(name, parent, anchorMin, anchorMax, anchoredPosition, sizeDelta, new Vector2(0.5f, 0.5f));
            return CreateTMPText(obj, text, fontSize, alignment, color);
        }

        internal static Button CreateButton(
            string name,
            Transform parent,
            string text,
            Vector2 anchor,
            Vector2 position,
            Vector2 size,
            Color backgroundColor,
            float fontSize,
            Vector2 textSize,
            UnityAction onClick,
            bool interactable)
        {
            GameObject obj = CreateRect(name, parent, anchor, size);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            image.color = backgroundColor;
            Button button = obj.AddComponent<Button>();
            button.interactable = interactable;
            CreateText("Text", obj.transform, text, fontSize, Vector2.zero, textSize, TextAlignmentOptions.Center, Color.white);
            if (interactable && onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        /// <summary>
        /// 配置 CanvasScaler 为统一参考分辨率
        /// </summary>
        internal static void ConfigureCanvasScaler(CanvasScaler scaler)
        {
            if (scaler == null)
            {
                return;
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        /// <summary>
        /// 创建水平分隔线
        /// </summary>
        internal static GameObject CreateSeparator(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            float height,
            Color color)
        {
            GameObject obj = CreateRect(name, parent, anchorMin, anchorMax, anchoredPosition, new Vector2(0f, height), new Vector2(0.5f, 0.5f));
            Image img = obj.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return obj;
        }

        /// <summary>
        /// 创建高亮信息条（带背景色的文本行）
        /// </summary>
        internal static TextMeshProUGUI CreateHighlightBar(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            TextAlignmentOptions alignment,
            Color textColor,
            Color backgroundColor)
        {
            GameObject bar = CreateRect(name + "_Bar", parent, anchorMin, anchorMax, anchoredPosition, sizeDelta, new Vector2(0.5f, 0.5f));
            Image barImage = bar.AddComponent<Image>();
            barImage.color = backgroundColor;
            barImage.raycastTarget = false;

            GameObject textObject = CreateRect(name + "_Text", bar.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return CreateTMPText(textObject, text, fontSize, alignment, textColor);
        }
    }
}
