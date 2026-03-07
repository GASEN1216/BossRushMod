// ============================================================================
// NPCNameTagHelper.cs - NPC 名字标签统一辅助
// ============================================================================
// 模块说明：
//   统一 NPC 名字标签的创建与朝向刷新逻辑，避免各 NPC 参数漂移。
//   复用原版 HealthBar 的字体，确保 NPC 名字与 Boss 名字显示风格一致。
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using TMPro;

namespace BossRush.Utils
{
    public static class NPCNameTagHelper
    {
        private const float DEFAULT_FONT_SIZE = 4f;
        private const int DEFAULT_SORTING_ORDER = 100;

        private static Camera _cachedCamera;
        private static float _nextCameraRefreshTime;

        private static TMP_FontAsset _cachedGameFont;

        /// <summary>
        /// 从原版 HealthBar prefab 的 nameText 上获取游戏字体，
        /// 与 Boss 名字显示使用完全相同的 TMP_FontAsset。
        /// </summary>
        private static TMP_FontAsset GetGameFont()
        {
            if (_cachedGameFont != null) return _cachedGameFont;

            try
            {
                // 优先从 HealthBarManager.Instance.healthBarPrefab 的 nameText 获取
                var manager = Duckov.UI.HealthBarManager.Instance;
                if (manager != null && manager.healthBarPrefab != null)
                {
                    var field = typeof(Duckov.UI.HealthBar).GetField("nameText",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var nameText = field.GetValue(manager.healthBarPrefab) as TMP_Text;
                        if (nameText != null && nameText.font != null)
                        {
                            _cachedGameFont = nameText.font;
                            ModBehaviour.DevLog("[NPCNameTagHelper] 从 HealthBar prefab 获取到游戏字体: " + _cachedGameFont.name);
                            return _cachedGameFont;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCNameTagHelper] 从 HealthBar 获取字体失败: " + e.Message);
            }

            try
            {
                // 回退：从场景中任意已有的 TMP_Text 获取
                var existingTMP = UnityEngine.Object.FindObjectOfType<TMP_Text>();
                if (existingTMP != null && existingTMP.font != null)
                {
                    _cachedGameFont = existingTMP.font;
                    ModBehaviour.DevLog("[NPCNameTagHelper] 从场景 TMP_Text 获取到字体: " + _cachedGameFont.name);
                    return _cachedGameFont;
                }

                // 最后回退：从已加载的资源中查找
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0)
                {
                    _cachedGameFont = fonts[0];
                    ModBehaviour.DevLog("[NPCNameTagHelper] 从 Resources 获取到字体: " + _cachedGameFont.name);
                    return _cachedGameFont;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCNameTagHelper] 获取游戏字体失败: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 创建统一样式的头顶名字标签，自动复用原版 HealthBar 的字体
        /// </summary>
        public static bool CreateNameTag(
            Transform parent,
            string objectName,
            string displayName,
            float height,
            out GameObject nameTagObject,
            out TextMeshPro nameTagText,
            string logPrefix)
        {
            nameTagObject = null;
            nameTagText = null;

            if (parent == null)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 创建名字标签失败: parent 为空");
                return false;
            }

            try
            {
                nameTagObject = new GameObject(objectName);
                nameTagObject.transform.SetParent(parent, false);
                nameTagObject.transform.localPosition = new Vector3(0f, height, 0f);

                nameTagText = nameTagObject.AddComponent<TextMeshPro>();
                nameTagText.text = displayName ?? string.Empty;
                nameTagText.fontSize = DEFAULT_FONT_SIZE;
                nameTagText.alignment = TextAlignmentOptions.Center;
                nameTagText.color = Color.white;
                nameTagText.faceColor = new Color32(255, 255, 255, 255);
                nameTagText.enableAutoSizing = false;
                nameTagText.sortingOrder = DEFAULT_SORTING_ORDER;
                nameTagText.richText = false;

                TMP_FontAsset gameFont = GetGameFont();
                if (gameFont != null)
                {
                    nameTagText.font = gameFont;
                    ModBehaviour.DevLog(logPrefix + " 名字标签已应用游戏原版字体: " + gameFont.name);
                }
                else
                {
                    ModBehaviour.DevLog(logPrefix + " [WARNING] 未获取到游戏字体，使用 TMP 默认字体");
                }

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 创建名字标签失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 让名字标签始终面向相机（内部缓存 Camera.main 避免每帧 FindWithTag 开销）
        /// </summary>
        public static void UpdateNameTagRotation(GameObject nameTagObject)
        {
            if (nameTagObject == null) return;

            float now = Time.unscaledTime;
            if (_cachedCamera == null || now >= _nextCameraRefreshTime)
            {
                _cachedCamera = Camera.main;
                _nextCameraRefreshTime = now + 1f;
            }

            if (_cachedCamera == null) return;
            nameTagObject.transform.rotation = _cachedCamera.transform.rotation;
        }
    }
}
