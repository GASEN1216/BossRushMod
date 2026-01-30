// ============================================================================
// ReforgeDataPersistence.cs - 重铸数据持久化（Harmony Patch）
// ============================================================================
// 功能：
//   1. 重铸时将属性差值保存到 Variables（以 "RF_" 为前缀）
//   2. 物品重新应用修改器前自动恢复重铸属性
//   3. Patch ModifierDescriptionCollection.ReapplyModifiers 的 Prefix
//
// 前缀规范：
//   - RF_MOD_xxx  : Modifier 类型属性
//   - RF_STAT_xxx : Stat 类型属性
//   - RF_VAR_xxx  : Variable 类型属性
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
        /// Modifier 重铸数据前缀
        /// </summary>
        public const string MODIFIER_PREFIX = "RF_MOD_";

        /// <summary>
        /// Stat 重铸数据前缀
        /// </summary>
        public const string STAT_PREFIX = "RF_STAT_";

        /// <summary>
        /// Variable 重铸数据前缀
        /// </summary>
        public const string VARIABLE_PREFIX = "RF_VAR_";

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
        /// 保存 Modifier 重铸数据到物品的 Variables
        /// </summary>
        public static void SaveReforgeData(Item item, string modifierKey, float prefabValue, float newValue)
        {
            SaveReforgeDataInternal(item, MODIFIER_PREFIX, modifierKey, prefabValue, newValue);
        }

        /// <summary>
        /// 保存 Stat 重铸数据到物品的 Variables
        /// </summary>
        public static void SaveReforgeDataStat(Item item, string statKey, float prefabValue, float newValue)
        {
            SaveReforgeDataInternal(item, STAT_PREFIX, statKey, prefabValue, newValue);
        }

        /// <summary>
        /// 保存 Variable 重铸数据到物品的 Variables
        /// </summary>
        public static void SaveReforgeDataVariable(Item item, string variableKey, float prefabValue, float newValue)
        {
            SaveReforgeDataInternal(item, VARIABLE_PREFIX, variableKey, prefabValue, newValue);
        }

        /// <summary>
        /// 内部方法：保存重铸数据到物品的 Variables
        /// </summary>
        private static void SaveReforgeDataInternal(Item item, string prefix, string propertyKey, float prefabValue, float newValue)
        {
            if (item == null || item.Variables == null) return;

            float delta = newValue - prefabValue;

            // 如果差值接近0，设置为0（表示无修改）
            if (Mathf.Abs(delta) < 0.001f)
            {
                delta = 0f;
            }

            string key = prefix + propertyKey;
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
                    ModBehaviour.DevLog($"[ReforgeData] 保存: {item.DisplayName}.{key} delta={delta:F2}");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[ReforgeData] 保存失败: {key} - {e.Message}");
            }
        }

        /// <summary>
        /// 尝试恢复物品的重铸数据（合并检查和恢复，只遍历一次）
        /// 支持 Modifier、Stat、Variable 三种属性类型
        /// </summary>
        /// <returns>是否有恢复操作</returns>
        public static bool TryRestoreReforgeData(Item item)
        {
            if (item == null || item.Variables == null) return false;

            // 快速检查：已恢复的物品直接跳过
            int itemId = item.GetInstanceID();
            if (_restoredItems.Contains(itemId)) return false;

            int restored = 0;

            // 只遍历一次 Variables
            foreach (var variable in item.Variables)
            {
                // 快速跳过非重铸数据（检查 RF_ 前缀）
                if (variable.Key == null || variable.Key.Length <= 3) continue;
                if (variable.Key[0] != 'R' || variable.Key[1] != 'F' || variable.Key[2] != '_') continue;

                try
                {
                    float delta = variable.GetFloat();
                    if (Mathf.Abs(delta) < 0.001f) continue;

                    // 判断属性类型并恢复
                    if (variable.Key.StartsWith(MODIFIER_PREFIX))
                    {
                        // RF_MOD_ 前缀 - 恢复 Modifier
                        string modifierKey = variable.Key.Substring(MODIFIER_PREFIX.Length);
                        if (TryRestoreModifier(item, modifierKey, delta))
                        {
                            restored++;
                        }
                    }
                    else if (variable.Key.StartsWith(STAT_PREFIX))
                    {
                        // RF_STAT_ 前缀 - 恢复 Stat
                        string statKey = variable.Key.Substring(STAT_PREFIX.Length);
                        if (TryRestoreStat(item, statKey, delta))
                        {
                            restored++;
                        }
                    }
                    else if (variable.Key.StartsWith(VARIABLE_PREFIX))
                    {
                        // RF_VAR_ 前缀 - 恢复 Variable
                        string varKey = variable.Key.Substring(VARIABLE_PREFIX.Length);
                        if (TryRestoreVariable(item, varKey, delta))
                        {
                            restored++;
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

        /// <summary>
        /// 恢复 Modifier 属性
        /// 使用覆盖模式：prefabValue + delta = 最终值
        /// </summary>
        private static bool TryRestoreModifier(Item item, string modifierKey, float delta)
        {
            if (item.Modifiers == null) return false;

            // 获取预制体原始值
            Item prefab = GetItemPrefab(item);
            float prefabValue = GetPrefabModifierValue(prefab, modifierKey);

            foreach (var mod in item.Modifiers)
            {
                if (mod.Key == modifierKey)
                {
                    // 覆盖模式：最终值 = 预制体原值 + delta
                    float newValue = prefabValue + delta;
                    float currentValue = mod.Value;
                    ReforgeSystem.ApplyModifierValueChangePublic(mod, newValue);
                    ModBehaviour.DevLog($"[ReforgeData] 恢复Modifier: {item.DisplayName}.{modifierKey} = prefab({prefabValue:F2}) + delta({delta:F2}) = {newValue:F2} (当前值:{currentValue:F2})");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 恢复 Stat 属性
        /// 使用覆盖模式：prefabValue + delta = 最终值
        /// </summary>
        private static bool TryRestoreStat(Item item, string statKey, float delta)
        {
            if (item.Stats == null) return false;

            // 获取预制体原始值
            Item prefab = GetItemPrefab(item);
            float prefabValue = GetPrefabStatValue(prefab, statKey);

            foreach (var stat in item.Stats)
            {
                if (stat.Key == statKey)
                {
                    // 覆盖模式：最终值 = 预制体原值 + delta
                    float newValue = prefabValue + delta;
                    float currentValue = stat.BaseValue;
                    ReforgeSystem.ApplyStatValueChangePublic(stat, newValue);
                    ModBehaviour.DevLog($"[ReforgeData] 恢复Stat: {item.DisplayName}.{statKey} = prefab({prefabValue:F2}) + delta({delta:F2}) = {newValue:F2} (当前值:{currentValue:F2})");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 恢复 Variable 属性
        /// 使用覆盖模式：prefabValue + delta = 最终值
        /// </summary>
        private static bool TryRestoreVariable(Item item, string varKey, float delta)
        {
            if (item.Variables == null) return false;

            // 获取预制体原始值
            Item prefab = GetItemPrefab(item);
            float prefabValue = GetPrefabVariableValue(prefab, varKey);

            foreach (var variable in item.Variables)
            {
                if (variable.Key == varKey)
                {
                    try
                    {
                        // 覆盖模式：最终值 = 预制体原值 + delta
                        float newValue = prefabValue + delta;
                        float currentValue = variable.GetFloat();
                        variable.SetFloat(newValue);
                        ModBehaviour.DevLog($"[ReforgeData] 恢复Variable: {item.DisplayName}.{varKey} = prefab({prefabValue:F2}) + delta({delta:F2}) = {newValue:F2} (当前值:{currentValue:F2})");
                        return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        /// <summary>
        /// 获取物品的预制体
        /// </summary>
        private static Item GetItemPrefab(Item item)
        {
            if (item == null) return null;
            try
            {
                int typeId = item.TypeID;
                if (typeId <= 0) return null;
                return ItemAssetsCollection.GetPrefab(typeId);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取预制体的 Modifier 值
        /// </summary>
        private static float GetPrefabModifierValue(Item prefab, string key)
        {
            if (prefab == null || prefab.Modifiers == null) return 0f;
            foreach (var mod in prefab.Modifiers)
            {
                if (mod.Key == key) return mod.Value;
            }
            return 0f;
        }

        /// <summary>
        /// 获取预制体的 Stat 值
        /// </summary>
        private static float GetPrefabStatValue(Item prefab, string key)
        {
            if (prefab == null || prefab.Stats == null) return 0f;
            foreach (var stat in prefab.Stats)
            {
                if (stat.Key == key) return stat.BaseValue;
            }
            return 0f;
        }

        /// <summary>
        /// 获取预制体的 Variable 值
        /// </summary>
        private static float GetPrefabVariableValue(Item prefab, string key)
        {
            if (prefab == null || prefab.Variables == null) return 0f;
            foreach (var variable in prefab.Variables)
            {
                if (variable.Key == key)
                {
                    try { return variable.GetFloat(); } catch { }
                }
            }
            return 0f;
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
                // 正确方式：ModifierDescriptionCollection 继承自 ItemComponent，直接使用 Master 属性
                Item item = __instance.Master;
                
                if (item == null) return;

                // 直接尝试恢复（内部会检查是否有RF_数据和是否已恢复）
                ReforgeDataPersistence.TryRestoreReforgeData(item);
            }
            catch (NullReferenceException)
            {
                // 物品可能已被销毁，静默处理
            }
            catch (InvalidOperationException)
            {
                // 集合可能正在被修改，静默处理
            }
        }
    }
}
