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
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// 丧尸模式 UI 通用工具类
    /// </summary>
    internal static class ZombieModeUIHelper
    {
        private static TMP_FontAsset _cachedFont;

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
            catch { }

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
            catch { }

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
            catch { }

            // 最后回退：从已加载的资源中查找
            try
            {
                TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0)
                {
                    _cachedFont = fonts[0];
                }
            }
            catch { }

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
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;
            return tmp;
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
    }
}
