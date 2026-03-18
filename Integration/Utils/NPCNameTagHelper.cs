// ============================================================================
// NPCNameTagHelper.cs - NPC 名字标签统一辅助
// ============================================================================
// 模块说明：
//   统一 NPC 名字显示逻辑。
//   旧方案：创建世界空间 TextMeshPro 头顶字。
//   新方案：复制原版 HealthBar UI，并只保留原版名字组件。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using Duckov.UI.Animations;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BossRush.Utils
{
    public static class NPCNameTagHelper
    {
        private const float DEFAULT_FONT_SIZE = 4f;
        private const int DEFAULT_SORTING_ORDER = 100;
        private const float DEFAULT_SCREEN_Y_OFFSET = 0.01f;
        private const float ORIGINAL_HEALTH_BAR_NAME_HEIGHT_ADJUSTMENT = -0.5f;
        private const float ORIGINAL_HEALTH_BAR_NAME_FONT_SCALE = 1.2f;

        private static readonly BindingFlags PrivateInstanceFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo NameTextField = typeof(HealthBar).GetField("nameText", PrivateInstanceFlags);
        private static readonly FieldInfo BackgroundField = typeof(HealthBar).GetField("background", PrivateInstanceFlags);
        private static readonly FieldInfo FillField = typeof(HealthBar).GetField("fill", PrivateInstanceFlags);
        private static readonly FieldInfo FollowFillField = typeof(HealthBar).GetField("followFill", PrivateInstanceFlags);
        private static readonly FieldInfo LevelIconField = typeof(HealthBar).GetField("levelIcon", PrivateInstanceFlags);
        private static readonly FieldInfo DeathIndicatorField = typeof(HealthBar).GetField("deathIndicator", PrivateInstanceFlags);
        private static readonly FieldInfo HurtBlinkField = typeof(HealthBar).GetField("hurtBlink", PrivateInstanceFlags);
        private static readonly FieldInfo DamageBarTemplateField = typeof(HealthBar).GetField("damageBarTemplate", PrivateInstanceFlags);

        private static Camera _cachedCamera;
        private static float _nextCameraRefreshTime;

        private static TMP_FontAsset _cachedGameFont;

        private sealed class OriginalHealthBarEntry
        {
            public Transform Target;
            public string DisplayName;
            public float Height;
            public bool HideBarVisuals;
            public string LogPrefix;
            public float NextEnsureInstanceTime;
            public bool LoggedMissingHealthBarManager;
            public GameObject RootObject;
            public RectTransform RootRectTransform;
            public TextMeshProUGUI NameText;
        }

        private static readonly Dictionary<int, OriginalHealthBarEntry> OriginalHealthBarEntriesByTransformId
            = new Dictionary<int, OriginalHealthBarEntry>();

        /// <summary>
        /// 使用原版 HealthBar 的名字组件显示 NPC 名字。
        /// 内部只复制原版 UI 样式，不再补 Health 代理。
        /// </summary>
        public static bool RegisterOriginalHealthBarName(
            Transform parent,
            string displayName,
            float height,
            string logPrefix,
            bool hideBarVisuals = true)
        {
            if (parent == null)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 注册原版名字显示失败: parent 为空");
                return false;
            }

            try
            {
                int transformId = parent.GetInstanceID();
                OriginalHealthBarEntry entry;
                if (!OriginalHealthBarEntriesByTransformId.TryGetValue(transformId, out entry) || entry == null)
                {
                    entry = new OriginalHealthBarEntry();
                    OriginalHealthBarEntriesByTransformId[transformId] = entry;
                }

                entry.Target = parent;
                entry.DisplayName = displayName ?? string.Empty;
                entry.Height = Mathf.Max(0.1f, height);
                entry.HideBarVisuals = hideBarVisuals;
                entry.LogPrefix = logPrefix ?? "[NPCNameTagHelper]";
                entry.NextEnsureInstanceTime = 0f;
                entry.LoggedMissingHealthBarManager = false;

                EnsureOriginalHealthBarInstance(entry);
                RefreshOriginalHealthBarName(parent);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 注册原版名字显示失败: " + e);
                return false;
            }
        }

        /// <summary>
        /// 刷新原版名字组件显示，内部带节流，可安全在 Update/LateUpdate 中调用。
        /// </summary>
        public static void RefreshOriginalHealthBarName(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            int transformId = parent.GetInstanceID();
            OriginalHealthBarEntry entry;
            if (!OriginalHealthBarEntriesByTransformId.TryGetValue(transformId, out entry) || entry == null)
            {
                return;
            }

            entry.Target = parent;
            if (!EnsureOriginalHealthBarInstance(entry))
            {
                return;
            }

            UpdateOriginalHealthBarInstance(entry);
        }

        /// <summary>
        /// 注销原版名字显示注册。
        /// </summary>
        public static void UnregisterOriginalHealthBarName(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            int transformId = parent.GetInstanceID();
            OriginalHealthBarEntry entry;
            if (!OriginalHealthBarEntriesByTransformId.TryGetValue(transformId, out entry) || entry == null)
            {
                return;
            }

            OriginalHealthBarEntriesByTransformId.Remove(transformId);
            DestroyOriginalHealthBarInstance(entry);
        }

        /// <summary>
        /// 创建统一样式的头顶名字标签，自动复用原版 HealthBar 的字体。
        /// 旧逻辑保留给未迁移代码，新的自定义 NPC 应优先使用 RegisterOriginalHealthBarName。
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

        public static void UpdateNameTagRotation(GameObject nameTagObject)
        {
            if (nameTagObject == null) return;

            RefreshCachedCamera();
            if (_cachedCamera == null) return;
            nameTagObject.transform.rotation = _cachedCamera.transform.rotation;
        }

        private static void RefreshCachedCamera()
        {
            float now = Time.unscaledTime;
            if (_cachedCamera == null || now >= _nextCameraRefreshTime)
            {
                _cachedCamera = Camera.main;
                _nextCameraRefreshTime = now + 1f;
            }
        }

        private static bool EnsureOriginalHealthBarInstance(OriginalHealthBarEntry entry)
        {
            if (entry == null || entry.Target == null)
            {
                return false;
            }

            if (entry.RootObject != null && entry.NameText != null)
            {
                return true;
            }

            if (entry.RootObject != null || entry.NameText != null)
            {
                DestroyOriginalHealthBarInstance(entry);
            }

            float now = Time.unscaledTime;
            if (now < entry.NextEnsureInstanceTime)
            {
                return false;
            }

            entry.NextEnsureInstanceTime = now + 1f;

            HealthBarManager manager = HealthBarManager.Instance;
            if (manager == null || manager.healthBarPrefab == null)
            {
                if (!entry.LoggedMissingHealthBarManager)
                {
                    entry.LoggedMissingHealthBarManager = true;
                    ModBehaviour.DevLog(entry.LogPrefix + " [WARNING] 复制原版名字组件失败: HealthBarManager 或 prefab 不可用");
                }

                return false;
            }

            entry.LoggedMissingHealthBarManager = false;

            try
            {
                GameObject instance = UnityEngine.Object.Instantiate(manager.healthBarPrefab.gameObject);
                instance.name = "NPCOriginalHealthBarName_" + entry.Target.name;
                instance.transform.SetParent(manager.transform, false);

                RectTransform rootRectTransform = instance.transform as RectTransform;
                if (rootRectTransform != null)
                {
                    rootRectTransform.localScale = Vector3.one;
                    rootRectTransform.localRotation = Quaternion.identity;
                }

                HealthBar healthBar = instance.GetComponent<HealthBar>();
                TextMeshProUGUI nameText = GetHealthBarField<TextMeshProUGUI>(healthBar, NameTextField);
                GameObject background = GetHealthBarField<GameObject>(healthBar, BackgroundField);
                Image fill = GetHealthBarField<Image>(healthBar, FillField);
                Image followFill = GetHealthBarField<Image>(healthBar, FollowFillField);
                Image levelIcon = GetHealthBarField<Image>(healthBar, LevelIconField);
                GameObject deathIndicator = GetHealthBarField<GameObject>(healthBar, DeathIndicatorField);
                Image hurtBlink = GetHealthBarField<Image>(healthBar, HurtBlinkField);
                Component damageBarTemplate = GetHealthBarField<Component>(healthBar, DamageBarTemplateField);

                DisableFadeBehaviours(instance);
                ApplyOriginalHealthBarNameVisuals(entry, nameText, background, fill, followFill, levelIcon, deathIndicator, hurtBlink, damageBarTemplate);

                if (healthBar != null)
                {
                    healthBar.enabled = false;
                }

                entry.RootObject = instance;
                entry.RootRectTransform = rootRectTransform;
                entry.NameText = nameText;

                if (entry.NameText == null)
                {
                    ModBehaviour.DevLog(entry.LogPrefix + " [WARNING] 复制原版名字组件失败: 未找到 nameText");
                    DestroyOriginalHealthBarInstance(entry);
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(entry.LogPrefix + " [WARNING] 复制原版名字组件失败: " + e);
                DestroyOriginalHealthBarInstance(entry);
                return false;
            }
        }

        private static void DestroyOriginalHealthBarInstance(OriginalHealthBarEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.RootObject != null)
            {
                UnityEngine.Object.Destroy(entry.RootObject);
            }

            entry.RootObject = null;
            entry.RootRectTransform = null;
            entry.NameText = null;
        }

        private static void UpdateOriginalHealthBarInstance(OriginalHealthBarEntry entry)
        {
            if (entry == null || entry.Target == null || entry.RootObject == null)
            {
                return;
            }

            if (entry.NameText != null)
            {
                if (!entry.NameText.gameObject.activeSelf)
                {
                    entry.NameText.gameObject.SetActive(true);
                }

                if (!string.Equals(entry.NameText.text, entry.DisplayName, StringComparison.Ordinal))
                {
                    entry.NameText.text = entry.DisplayName;
                }
            }

            RefreshCachedCamera();
            bool shouldShow = ShouldShowOriginalHealthBarName(entry.Target);
            if (entry.RootObject.activeSelf != shouldShow)
            {
                entry.RootObject.SetActive(shouldShow);
            }

            if (!shouldShow || _cachedCamera == null)
            {
                return;
            }

            Vector3 screenPosition = _cachedCamera.WorldToScreenPoint(
                entry.Target.position + Vector3.up * GetAdjustedOriginalHealthBarNameHeight(entry.Height));
            screenPosition.y += DEFAULT_SCREEN_Y_OFFSET * Screen.height;

            if (entry.RootRectTransform != null)
            {
                entry.RootRectTransform.position = screenPosition;
            }
            else
            {
                entry.RootObject.transform.position = screenPosition;
            }
        }

        private static bool ShouldShowOriginalHealthBarName(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (_cachedCamera == null)
            {
                return false;
            }

            Vector3 direction = target.position - _cachedCamera.transform.position;
            return Vector3.Dot(direction, _cachedCamera.transform.forward) > 0f;
        }

        private static void DisableFadeBehaviours(GameObject rootObject)
        {
            if (rootObject == null)
            {
                return;
            }

            FadeGroup[] fadeGroups = rootObject.GetComponentsInChildren<FadeGroup>(true);
            for (int i = 0; i < fadeGroups.Length; i++)
            {
                FadeGroup fadeGroup = fadeGroups[i];
                if (fadeGroup == null)
                {
                    continue;
                }

                fadeGroup.enabled = false;
            }

            CanvasGroup[] canvasGroups = rootObject.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < canvasGroups.Length; i++)
            {
                CanvasGroup canvasGroup = canvasGroups[i];
                if (canvasGroup == null)
                {
                    continue;
                }

                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }
        }

        private static void ApplyOriginalHealthBarNameVisuals(
            OriginalHealthBarEntry entry,
            TextMeshProUGUI nameText,
            GameObject background,
            Image fill,
            Image followFill,
            Image levelIcon,
            GameObject deathIndicator,
            Image hurtBlink,
            Component damageBarTemplate)
        {
            if (entry == null)
            {
                return;
            }

            if (nameText != null)
            {
                if (!nameText.gameObject.activeSelf)
                {
                    nameText.gameObject.SetActive(true);
                }

                if (!string.Equals(nameText.text, entry.DisplayName, StringComparison.Ordinal))
                {
                    nameText.text = entry.DisplayName;
                }

                if (nameText.enableAutoSizing)
                {
                    nameText.fontSizeMin *= ORIGINAL_HEALTH_BAR_NAME_FONT_SCALE;
                    nameText.fontSizeMax *= ORIGINAL_HEALTH_BAR_NAME_FONT_SCALE;
                }
                else if (nameText.fontSize > 0f)
                {
                    nameText.fontSize *= ORIGINAL_HEALTH_BAR_NAME_FONT_SCALE;
                }

                nameText.raycastTarget = false;
            }

            if (!entry.HideBarVisuals)
            {
                return;
            }

            if (background != null && background.activeSelf)
            {
                background.SetActive(false);
            }

            if (fill != null && fill.gameObject.activeSelf)
            {
                fill.gameObject.SetActive(false);
            }

            if (followFill != null && followFill.gameObject.activeSelf)
            {
                followFill.gameObject.SetActive(false);
            }

            if (levelIcon != null && levelIcon.gameObject.activeSelf)
            {
                levelIcon.gameObject.SetActive(false);
            }
            if (deathIndicator != null && deathIndicator.activeSelf)
            {
                deathIndicator.SetActive(false);
            }

            if (hurtBlink != null && hurtBlink.gameObject.activeSelf)
            {
                hurtBlink.gameObject.SetActive(false);
            }

            if (damageBarTemplate != null && damageBarTemplate.gameObject.activeSelf)
            {
                damageBarTemplate.gameObject.SetActive(false);
            }
        }

        private static T GetHealthBarField<T>(HealthBar healthBar, FieldInfo fieldInfo) where T : class
        {
            if (healthBar == null || fieldInfo == null)
            {
                return null;
            }

            return fieldInfo.GetValue(healthBar) as T;
        }

        private static float GetAdjustedOriginalHealthBarNameHeight(float originalHeight)
        {
            return Mathf.Max(0.1f, originalHeight + ORIGINAL_HEALTH_BAR_NAME_HEIGHT_ADJUSTMENT);
        }
    }
}
