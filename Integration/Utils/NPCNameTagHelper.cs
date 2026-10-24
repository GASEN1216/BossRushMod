// ============================================================================
// NPCNameTagHelper.cs - 自定义 NPC 姓名与官方对话绑定
// ============================================================================
// 原版基地友好 NPC 没有常驻头顶名牌，姓名由 DuckovDialogueActor / StockShop 显示。
// 保留旧登记 API 兼容已有调用；显式语言刷新只同步已有 actor，不再创建独立屏幕 UI。

using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace BossRush.Utils
{
    public static class NPCNameTagHelper
    {
        private const float DEFAULT_FONT_SIZE = 4f;
        private const int DEFAULT_SORTING_ORDER = 100;
        private static Camera _cachedCamera;
        private static Transform _cachedCameraTransform;
        private static float _nextCameraRefreshTime;

        private sealed class OriginalHealthBarEntry
        {
            public Transform Target;
            public string DisplayName;
            public string LogPrefix;
            public DuckovDialogueActor Actor;
            public bool PendingNameUpdate;
            public float NextActorLookupTime;
        }

        private static readonly Dictionary<int, OriginalHealthBarEntry> OriginalHealthBarEntriesByTransformId
            = new Dictionary<int, OriginalHealthBarEntry>();

        public static void ResetStaticCaches()
        {
            OriginalHealthBarEntriesByTransformId.Clear();
            _cachedCamera = null;
            _cachedCameraTransform = null;
            _nextCameraRefreshTime = 0f;
        }

        /// <summary>
        /// 兼容旧登记入口：初始姓名由官方对话 owner 设置，不创建头顶名牌或 Health 代理。
        /// height / hideBarVisuals 保留调用兼容，友好 NPC 的姓名遵循原版对话和商店 UI。
        /// </summary>
        public static bool RegisterOriginalHealthBarName(
            Transform parent,
            string displayName,
            float height,
            string logPrefix,
            bool hideBarVisuals = true)
        {
            if (parent == null) return false;
            int transformId = parent.GetInstanceID();
            OriginalHealthBarEntry entry;
            if (!OriginalHealthBarEntriesByTransformId.TryGetValue(transformId, out entry))
            {
                entry = new OriginalHealthBarEntry();
                OriginalHealthBarEntriesByTransformId[transformId] = entry;
            }
            entry.Target = parent;
            entry.DisplayName = displayName ?? string.Empty;
            entry.LogPrefix = logPrefix ?? "[NPCNameTagHelper]";
            RefreshOriginalHealthBarName(parent);
            return true;
        }

        /// <summary>刷新已有登记的官方姓名；不为已注销的 NPC 新建登记或 UI。</summary>
        internal static bool UpdateOriginalHealthBarDisplayName(Transform parent, string displayName)
        {
            if (parent == null) return false;
            OriginalHealthBarEntry entry;
            if (!OriginalHealthBarEntriesByTransformId.TryGetValue(parent.GetInstanceID(), out entry) || entry == null)
                return false;
            string next = displayName ?? string.Empty;
            if (string.Equals(entry.DisplayName, next, StringComparison.Ordinal)) return true;
            entry.DisplayName = next;
            entry.PendingNameUpdate = true;
            RefreshOriginalHealthBarName(parent);
            return true;
        }

        /// <summary>兼容控制器的刷新入口；仅提交显式姓名更新，不重写对话 owner 的初始姓名。</summary>
        public static void RefreshOriginalHealthBarName(Transform parent)
        {
            if (parent == null) return;
            OriginalHealthBarEntry entry;
            if (!OriginalHealthBarEntriesByTransformId.TryGetValue(parent.GetInstanceID(), out entry) || entry == null)
                return;
            TryApplyOfficialDialogueName(entry);
        }

        /// <summary>注销只释放本 helper 的登记；官方 actor 由原有对话 owner 管理。</summary>
        public static void UnregisterOriginalHealthBarName(Transform parent)
        {
            if (parent == null) return;
            OriginalHealthBarEntriesByTransformId.Remove(parent.GetInstanceID());
        }

        private static void TryApplyOfficialDialogueName(OriginalHealthBarEntry entry)
        {
            if (entry == null || entry.Target == null || !entry.PendingNameUpdate) return;
            try
            {
                if (entry.Actor == null)
                {
                    float now = Time.unscaledTime;
                    if (now < entry.NextActorLookupTime) return;
                    entry.NextActorLookupTime = now + 1f;
                    entry.Actor = entry.Target.GetComponent<DuckovDialogueActor>();
                    if (entry.Actor == null) return;
                }
                string nameKey = entry.Actor.NameKey;
                if (string.IsNullOrEmpty(nameKey) || !nameKey.StartsWith("BossRush_", StringComparison.Ordinal)) return;
                LocalizationHelper.InjectLocalization(nameKey, entry.DisplayName);
                entry.PendingNameUpdate = false;
            }
            catch
            {
                // 每帧调用的兼容入口不打日志；actor 可能在同帧销毁或仍在初始化。
            }
        }

        /// <summary>
        /// 创建统一样式的头顶名字标签，自动复用原版 HealthBar 的字体。
        /// 仅保留旧外部调用兼容；自定义友好 NPC 不调用此入口，姓名走官方对话或商店 UI。
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

                TMP_FontAsset gameFont = BossRush.ZombieModeUIHelper.GetGameFont();
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

        public static void UpdateNameTagRotation(GameObject nameTagObject)
        {
            if (nameTagObject == null) return;

            RefreshCachedCamera();
            if (_cachedCameraTransform == null) return;
            nameTagObject.transform.rotation = _cachedCameraTransform.rotation;
        }

        private static void RefreshCachedCamera()
        {
            float now = Time.unscaledTime;
            if (_cachedCamera == null || now >= _nextCameraRefreshTime)
            {
                _cachedCamera = Camera.main;
                _cachedCameraTransform = _cachedCamera != null ? _cachedCamera.transform : null;
                _nextCameraRefreshTime = now + 1f;
            }
        }

    }
}
