// ============================================================================
// ReforgeDataPersistence.cs - 重铸数据持久化（Harmony Patch）
// ============================================================================
// 功能：
//   1. 重铸时将属性差值保存到 Variables（以 "RF_" 为前缀）
//   2. 物品重新应用修改器前自动恢复重铸属性
//   3. Patch ModifierDescriptionCollection.ReapplyModifiers 的 Prefix
//
// 兼容性：
//   - 使用 Prefix + void 返回，不阻断原方法，与其他mod兼容
//   - RF_ 前缀确保数据隔离，不会与其他mod的Variables冲突
// ============================================================================

using System;
using System.Collections.Generic;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 重铸数据持久化管理器
    /// </summary>
    public static class ReforgeDataPersistence
    {
        /// <summary>
        /// 重铸数据变量前缀
        /// </summary>
        public const string REFORGE_PREFIX = "RF_";

        /// <summary>
        /// 已恢复物品的追踪（防止重复恢复）
        /// 使用 Item 的 InstanceID 作为 key
        /// </summary>
        private static readonly HashSet<int> _restoredItems = new HashSet<int>();

        /// <summary>
        /// 清除恢复追踪
        /// </summary>
        public static void ClearRestoredTracking()
        {
            _restoredItems.Clear();
        }

        /// <summary>
        /// 标记物品已恢复（重铸时调用，避免立即被再次"恢复"）
        /// </summary>
        public static void MarkAsRestored(Item item)
        {
            if (item != null)
            {
                _restoredItems.Add(item.GetInstanceID());
            }
        }

        /// <summary>
        /// 保存重铸数据到物品的 Variables
        /// </summary>
        public static void SaveReforgeData(Item item, string modifierKey, float prefabValue, float newValue)
        {
            if (item == null || item.Variables == null) return;

            float delta = newValue - prefabValue;

            // 如果差值接近0，设置为0（表示无修改）
            if (Mathf.Abs(delta) < 0.001f)
            {
                delta = 0f;
            }

            string key = REFORGE_PREFIX + modifierKey;
            try
            {
                item.SetFloat(key, delta, true);

                // 设置为不显示（避免在物品详情中显示）
                var entry = item.Variables.GetEntry(key);
                if (entry != null)
                {
                    entry.Display = false;
                }

                if (Mathf.Abs(delta) > 0.001f)
                {
                    ModBehaviour.DevLog($"[ReforgeData] 保存: {item.DisplayName}.{modifierKey} delta={delta:F2}");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[ReforgeData] 保存失败: {key} - {e.Message}");
            }
        }

        /// <summary>
        /// 尝试恢复物品的重铸数据（合并检查和恢复，只遍历一次）
        /// </summary>
        /// <returns>是否有恢复操作</returns>
        public static bool TryRestoreReforgeData(Item item)
        {
            if (item == null || item.Variables == null || item.Modifiers == null) return false;

            // 快速检查：已恢复的物品直接跳过
            int itemId = item.GetInstanceID();
            if (_restoredItems.Contains(itemId)) return false;

            int restored = 0;

            // 只遍历一次 Variables
            foreach (var variable in item.Variables)
            {
                // 快速跳过非重铸数据
                if (variable.Key == null || variable.Key.Length <= 3) continue;
                if (variable.Key[0] != 'R' || variable.Key[1] != 'F' || variable.Key[2] != '_') continue;

                try
                {
                    float delta = variable.GetFloat();
                    if (Mathf.Abs(delta) < 0.001f) continue;

                    string modifierKey = variable.Key.Substring(3); // "RF_".Length = 3

                    // 查找对应的 Modifier
                    foreach (var mod in item.Modifiers)
                    {
                        if (mod.Key == modifierKey)
                        {
                            float currentValue = mod.Value;
                            float newValue = currentValue + delta;

                            // 使用反射修改值
                            ReforgeSystem.ApplyModifierValueChangePublic(mod, newValue);
                            restored++;

                            ModBehaviour.DevLog($"[ReforgeData] 恢复: {item.DisplayName}.{modifierKey} = {currentValue:F2} + {delta:F2} = {newValue:F2}");
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog($"[ReforgeData] 恢复失败: {variable.Key} - {e.Message}");
                }
            }

            if (restored > 0)
            {
                _restoredItems.Add(itemId);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Harmony Patch: 在 ModifierDescriptionCollection.ReapplyModifiers 之前恢复重铸数据
    ///
    /// 设计说明：
    /// - 使用 Prefix（前置钩子），在原方法执行前恢复属性
    /// - 返回 void，不影响原方法执行，与其他mod的Patch兼容
    /// - 使用 HashSet 去重，确保每个物品只恢复一次
    /// - 性能优化：快速字符检查替代 StartsWith，只遍历一次
    /// </summary>
    [HarmonyPatch(typeof(ModifierDescriptionCollection), "ReapplyModifiers")]
    public static class ModifiersReapplyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ModifierDescriptionCollection __instance)
        {
            // 快速空检查
            if (__instance == null) return;

            try
            {
                // 获取所属的 Item（ModifierDescriptionCollection 是 Item 的字段，不是组件）
                // 需要通过反射或遍历找到
                Item item = null;

                // 方法1：尝试作为组件获取
                var component = __instance as Component;
                if (component != null)
                {
                    item = component.GetComponentInParent<Item>();
                    if (item == null)
                    {
                        item = component.GetComponent<Item>();
                    }
                }

                if (item == null) return;

                // 直接尝试恢复（内部会检查是否有RF_数据和是否已恢复）
                ReforgeDataPersistence.TryRestoreReforgeData(item);
            }
            catch
            {
                // 静默处理，避免影响游戏
            }
        }
    }
}
