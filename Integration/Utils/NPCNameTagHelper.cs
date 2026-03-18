// ============================================================================
// NPCNameTagHelper.cs - NPC 名字标签统一辅助
// ============================================================================
// 模块说明：
//   统一 NPC 名字显示逻辑。
//   旧方案：创建世界空间 TextMeshPro 头顶字。
//   新方案：优先复用游戏原版 HealthBar 的 nameText，仅显示原版名字组件。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Duckov.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

namespace BossRush.Utils
{
    public static class NPCNameTagHelper
    {
        private const float DEFAULT_FONT_SIZE = 4f;
        private const int DEFAULT_SORTING_ORDER = 100;
        private const int DEFAULT_PROXY_MAX_HEALTH = 1;
        private const float DEFAULT_PROXY_CURRENT_HEALTH = 1f;

        private static readonly BindingFlags PrivateInstanceFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo DefaultMaxHealthField = typeof(Health).GetField("defaultMaxHealth", PrivateInstanceFlags);
        private static readonly FieldInfo CurrentHealthField = typeof(Health).GetField("_currentHealth", PrivateInstanceFlags);
        private static readonly FieldInfo DisplayOffsetField = typeof(HealthBar).GetField("displayOffset", PrivateInstanceFlags);

        private static Camera _cachedCamera;
        private static float _nextCameraRefreshTime;

        private static TMP_FontAsset _cachedGameFont;

        private sealed class OriginalHealthBarEntry
        {
            public Health Health;
            public string DisplayName;
            public float Height;
            public bool HideBarVisuals;
            public float NextEnsureVisibleTime;
        }

        private static readonly Dictionary<int, OriginalHealthBarEntry> OriginalHealthBarEntriesByHealthId
            = new Dictionary<int, OriginalHealthBarEntry>();
        private static readonly Dictionary<int, int> TransformToHealthId
            = new Dictionary<int, int>();

        /// <summary>
        /// 使用原版 HealthBar 的名字组件显示 NPC 名字。
        /// 对没有 CharacterMainControl 的功能型 NPC，会补一个最小 Health 代理。
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
                Health health = EnsureOriginalHealthBarProxy(parent.gameObject, Mathf.Max(0.1f, height), logPrefix);
                if (health == null)
                {
                    return false;
                }

                OriginalHealthBarEntry entry = new OriginalHealthBarEntry
                {
                    Health = health,
                    DisplayName = displayName ?? string.Empty,
                    Height = Mathf.Max(0.1f, height),
                    HideBarVisuals = hideBarVisuals,
                    NextEnsureVisibleTime = 0f
                };

                int healthId = health.GetInstanceID();
                OriginalHealthBarEntriesByHealthId[healthId] = entry;
                TransformToHealthId[parent.GetInstanceID()] = healthId;

                health.showHealthBar = true;
                health.RequestHealthBar();
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

            int healthId;
            if (!TransformToHealthId.TryGetValue(parent.GetInstanceID(), out healthId))
            {
                return;
            }

            OriginalHealthBarEntry entry;
            if (!OriginalHealthBarEntriesByHealthId.TryGetValue(healthId, out entry) || entry == null || entry.Health == null)
            {
                CleanupStaleOriginalHealthBarEntry(parent.GetInstanceID(), healthId);
                return;
            }

            float now = Time.unscaledTime;
            if (now < entry.NextEnsureVisibleTime)
            {
                return;
            }

            entry.NextEnsureVisibleTime = now + 0.5f;
            entry.Health.showHealthBar = true;
            entry.Health.RequestHealthBar();
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
            int healthId;
            if (!TransformToHealthId.TryGetValue(transformId, out healthId))
            {
                return;
            }

            TransformToHealthId.Remove(transformId);

            OriginalHealthBarEntry entry;
            if (!OriginalHealthBarEntriesByHealthId.TryGetValue(healthId, out entry))
            {
                return;
            }

            OriginalHealthBarEntriesByHealthId.Remove(healthId);

            HealthBar activeHealthBar = FindActiveHealthBar(entry.Health);
            if (activeHealthBar != null)
            {
                ForceRefreshCharacterIcon(activeHealthBar);
            }
        }

        internal static bool TryGetOriginalHealthBarEntry(
            Health health,
            out string displayName,
            out float height,
            out bool hideBarVisuals)
        {
            displayName = null;
            height = 0f;
            hideBarVisuals = false;

            if (health == null)
            {
                return false;
            }

            OriginalHealthBarEntry entry;
            int healthId = health.GetInstanceID();
            if (!OriginalHealthBarEntriesByHealthId.TryGetValue(healthId, out entry) || entry == null || entry.Health == null)
            {
                return false;
            }

            displayName = entry.DisplayName ?? string.Empty;
            height = entry.Height;
            hideBarVisuals = entry.HideBarVisuals;
            return true;
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

            float now = Time.unscaledTime;
            if (_cachedCamera == null || now >= _nextCameraRefreshTime)
            {
                _cachedCamera = Camera.main;
                _nextCameraRefreshTime = now + 1f;
            }

            if (_cachedCamera == null) return;
            nameTagObject.transform.rotation = _cachedCamera.transform.rotation;
        }

        private static Health EnsureOriginalHealthBarProxy(GameObject target, float height, string logPrefix)
        {
            if (target == null)
            {
                return null;
            }

            Health health = target.GetComponent<Health>();
            if (health == null)
            {
                health = target.AddComponent<Health>();
                ModBehaviour.DevLog(logPrefix + " 已添加 Health 代理以复用原版名字组件");
            }

            if (health.OnHealthChange == null)
            {
                health.OnHealthChange = new UnityEvent<Health>();
            }

            if (health.OnMaxHealthChange == null)
            {
                health.OnMaxHealthChange = new UnityEvent<Health>();
            }

            if (health.OnHurtEvent == null)
            {
                health.OnHurtEvent = new UnityEvent<DamageInfo>();
            }

            if (health.OnDeadEvent == null)
            {
                health.OnDeadEvent = new UnityEvent<DamageInfo>();
            }

            health.autoInit = false;
            health.showHealthBar = true;
            health.hasSoul = false;
            health.team = Teams.all;
            health.healthBarHeight = height;
            health.CanDieIfNotRaidMap = false;

            if (health.TryGetCharacter() == null)
            {
                if (DefaultMaxHealthField != null)
                {
                    DefaultMaxHealthField.SetValue(health, DEFAULT_PROXY_MAX_HEALTH);
                }

                if (CurrentHealthField != null)
                {
                    float currentHealth = 0f;
                    object boxedCurrentHealth = CurrentHealthField.GetValue(health);
                    if (boxedCurrentHealth is float)
                    {
                        currentHealth = (float)boxedCurrentHealth;
                    }

                    if (currentHealth <= 0f)
                    {
                        CurrentHealthField.SetValue(health, DEFAULT_PROXY_CURRENT_HEALTH);
                    }
                }
                else if (health.CurrentHealth <= 0f)
                {
                    health.CurrentHealth = DEFAULT_PROXY_CURRENT_HEALTH;
                }

                health.SetInvincible(true);
            }

            return health;
        }

        private static void CleanupStaleOriginalHealthBarEntry(int transformId, int healthId)
        {
            TransformToHealthId.Remove(transformId);
            OriginalHealthBarEntriesByHealthId.Remove(healthId);
        }

        private static HealthBar FindActiveHealthBar(Health health)
        {
            if (health == null)
            {
                return null;
            }

            HealthBar[] healthBars = UnityEngine.Object.FindObjectsOfType<HealthBar>();
            for (int i = 0; i < healthBars.Length; i++)
            {
                HealthBar healthBar = healthBars[i];
                if (healthBar != null && healthBar.target == health)
                {
                    return healthBar;
                }
            }

            return null;
        }

        private static void ForceRefreshCharacterIcon(HealthBar healthBar)
        {
            try
            {
                var refreshCharacterIcon = AccessTools.Method(typeof(HealthBar), "RefreshCharacterIcon");
                if (healthBar != null && refreshCharacterIcon != null)
                {
                    refreshCharacterIcon.Invoke(healthBar, null);
                }
            }
            catch { }
        }

        internal static void ApplyOriginalHealthBarStyle(
            HealthBar healthBar,
            TextMeshProUGUI nameText,
            GameObject background,
            Image fill,
            Image followFill,
            Image levelIcon)
        {
            if (healthBar == null || healthBar.target == null)
            {
                return;
            }

            string displayName;
            float height;
            bool hideBarVisuals;
            if (!TryGetOriginalHealthBarEntry(healthBar.target, out displayName, out height, out hideBarVisuals))
            {
                return;
            }

            if (DisplayOffsetField != null)
            {
                DisplayOffsetField.SetValue(healthBar, Vector3.up * height);
            }

            if (nameText != null)
            {
                if (!nameText.gameObject.activeSelf)
                {
                    nameText.gameObject.SetActive(true);
                }

                if (!string.Equals(nameText.text, displayName, StringComparison.Ordinal))
                {
                    nameText.text = displayName;
                }
            }

            if (!hideBarVisuals)
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
        }
    }

    [HarmonyPatch(typeof(HealthBar), "Refresh")]
    internal static class NPCOriginalHealthBarRefreshPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(HealthBar __instance, Image ___fill, Image ___followFill)
        {
            if (__instance == null || __instance.target == null)
            {
                return true;
            }

            string displayName;
            float height;
            bool hideBarVisuals;
            if (!NPCNameTagHelper.TryGetOriginalHealthBarEntry(__instance.target, out displayName, out height, out hideBarVisuals))
            {
                return true;
            }

            if (___fill != null)
            {
                ___fill.fillAmount = 1f;
            }

            if (___followFill != null)
            {
                ___followFill.fillAmount = 1f;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(HealthBar), "ShowDamageBar")]
    internal static class NPCOriginalHealthBarDamageBarPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(HealthBar __instance)
        {
            if (__instance == null || __instance.target == null)
            {
                return true;
            }

            string displayName;
            float height;
            bool hideBarVisuals;
            return !NPCNameTagHelper.TryGetOriginalHealthBarEntry(__instance.target, out displayName, out height, out hideBarVisuals);
        }
    }

    [HarmonyPatch(typeof(HealthBar), "RefreshOffset")]
    internal static class NPCOriginalHealthBarOffsetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HealthBar __instance)
        {
            NPCNameTagHelper.ApplyOriginalHealthBarStyle(__instance, null, null, null, null, null);
        }
    }

    [HarmonyPatch(typeof(HealthBar), "Setup")]
    internal static class NPCOriginalHealthBarSetupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            HealthBar __instance,
            TextMeshProUGUI ___nameText,
            GameObject ___background,
            Image ___fill,
            Image ___followFill,
            Image ___levelIcon)
        {
            NPCNameTagHelper.ApplyOriginalHealthBarStyle(__instance, ___nameText, ___background, ___fill, ___followFill, ___levelIcon);
        }
    }

    [HarmonyPatch(typeof(HealthBar), "RefreshCharacterIcon")]
    internal static class NPCOriginalHealthBarNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            HealthBar __instance,
            TextMeshProUGUI ___nameText,
            Image ___levelIcon)
        {
            NPCNameTagHelper.ApplyOriginalHealthBarStyle(__instance, ___nameText, null, null, null, ___levelIcon);
        }
    }
}
