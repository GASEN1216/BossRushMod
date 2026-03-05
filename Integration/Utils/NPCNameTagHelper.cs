// ============================================================================
// NPCNameTagHelper.cs - NPC 名字标签统一辅助
// ============================================================================
// 模块说明：
//   统一 NPC 名字标签的创建与朝向刷新逻辑，避免各 NPC 参数漂移。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush.Utils
{
    public static class NPCNameTagHelper
    {
        private const float DEFAULT_FONT_SIZE = 4f;
        private const int DEFAULT_SORTING_ORDER = 100;

        /// <summary>
        /// 创建统一样式的头顶名字标签
        /// </summary>
        public static bool CreateNameTag(
            Transform parent,
            string objectName,
            string displayName,
            float height,
            out GameObject nameTagObject,
            out TMPro.TextMeshPro nameTagText,
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

                nameTagText = nameTagObject.AddComponent<TMPro.TextMeshPro>();
                nameTagText.text = displayName ?? string.Empty;
                nameTagText.fontSize = DEFAULT_FONT_SIZE;
                nameTagText.alignment = TMPro.TextAlignmentOptions.Center;
                nameTagText.color = Color.white;
                nameTagText.faceColor = new Color32(255, 255, 255, 255);
                nameTagText.enableAutoSizing = false;
                nameTagText.sortingOrder = DEFAULT_SORTING_ORDER;
                nameTagText.richText = false;

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [WARNING] 创建名字标签失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 让名字标签始终面向相机
        /// </summary>
        public static void UpdateNameTagRotation(GameObject nameTagObject)
        {
            if (nameTagObject == null || Camera.main == null) return;
            nameTagObject.transform.rotation = Camera.main.transform.rotation;
        }
    }
}
