using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Duckov.Utilities;

namespace BossRush
{
    public static partial class ReforgeSystem
    {
        /// <summary>
        /// 获取物品的重铸计数
        /// </summary>
        private static int GetReforgeCount(Item item)
        {
            if (item == null || item.Variables == null) return 0;
            try
            {
                foreach (var v in item.Variables)
                {
                    if (v.Key == "ReforgeCount")
                    {
                        return (int)v.GetFloat();
                    }
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// 增加物品的重铸计数
        /// </summary>
        private static void IncrementReforgeCount(Item item)
        {
            if (item == null) return;
            try
            {
                int count = GetReforgeCount(item) + 1;
                // 尝试设置或添加ReforgeCount变量
                if (item.Variables != null)
                {
                    foreach (var v in item.Variables)
                    {
                        if (v.Key == "ReforgeCount")
                        {
                            v.SetFloat(count);
                            return;
                        }
                    }
                }
                // 首次重铸时需要允许创建变量，否则原版会直接打 Error 并丢失计数。
                item.SetFloat("ReforgeCount", count, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeSystem] 更新重铸计数失败: " + e.Message);
            }
        }

        // 注意：PropertyType 枚举已移至 PropertyLockSystem.cs 中定义为公开枚举

        // 可重铸属性结构
        private class ReforgeableProperty
        {
            public string Key;
            public float Value;
            public PropertyType Type;
            public object Source;
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
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab != null)
                {
                    CustomItemRuntimeStateHelper.EnsureCustomItemConfigured(prefab);
                }
                return prefab;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeSystem] 获取预制体失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 获取预制体对应属性的原始值
        /// </summary>
        private static float GetPrefabPropertyValue(Item prefab, string key, PropertyType type, float fallback)
        {
            if (prefab == null) return fallback;

            try
            {
                switch (type)
                {
                    case PropertyType.Modifier:
                        if (prefab.Modifiers != null)
                        {
                            foreach (var mod in prefab.Modifiers)
                            {
                                if (mod.Key == key) return mod.Value;
                            }
                        }
                        break;
                    case PropertyType.Stat:
                        if (prefab.Stats != null)
                        {
                            foreach (var stat in prefab.Stats)
                            {
                                if (stat.Key == key) return stat.BaseValue;
                            }
                        }
                        break;
                    case PropertyType.Variable:
                        if (prefab.Variables != null)
                        {
                            foreach (var variable in prefab.Variables)
                            {
                                if (variable.Key == key)
                                {
                                    try { return variable.GetFloat(); } catch { }
                                }
                            }
                        }
                        break;
                }
            }
            catch { }

            return fallback;
        }

        /// <summary>
        /// 应用属性修改并保存重铸数据
        /// </summary>
        private static void ApplyPropertyChange(ReforgeableProperty prop, float newValue, Item item, Item prefab)
        {
            try
            {
                float prefabValue = GetPrefabPropertyValue(prefab, prop.Key, prop.Type, prop.Value);

                switch (prop.Type)
                {
                    case PropertyType.Modifier:
                        ApplyModifierValueChange(prop.Source as ModifierDescription, newValue);
                        // 保存重铸数据到 Variables（用于场景切换后恢复）
                        ReforgeDataPersistence.SaveReforgeData(item, prop.Key, prefabValue, newValue);
                        break;
                    case PropertyType.Stat:
                        ApplyStatValueChange(prop.Source as Stat, newValue);
                        // 保存 Stat 重铸数据到 Variables（修复：枪械/近战武器场景切换后属性丢失）
                        ReforgeDataPersistence.SaveReforgeDataStat(item, prop.Key, prefabValue, newValue);
                        break;
                    case PropertyType.Variable:
                        ApplyVariableValueChange(prop.Source as CustomData, newValue);
                        // 保存 Variable 重铸数据到 Variables
                        ReforgeDataPersistence.SaveReforgeDataVariable(item, prop.Key, prefabValue, newValue);
                        break;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeSystem] 应用属性修改失败: " + prop.Key + " - " + e.Message);
            }
        }

        /// <summary>
        /// 修改Stat的基础值（使用委托缓存优化性能）
        /// </summary>
        private static void ApplyStatValueChange(Stat stat, float newValue)
        {
            if (stat == null) return;
            try
            {
                // 优先使用委托（性能更好）
                if (_setStatValue != null)
                {
                    _setStatValue(stat, newValue);
                }
                else
                {
                    // 回退到反射
                    var field = StatBaseValueField;
                    if (field != null)
                    {
                        field.SetValue(stat, newValue);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeSystem] 修改Stat失败: " + e.Message);
            }
        }

        /// <summary>
        /// 修改Variable的值
        /// </summary>
        private static void ApplyVariableValueChange(CustomData variable, float newValue)
        {
            if (variable == null) return;
            try
            {
                // CustomData使用SetFloat()设置值
                variable.SetFloat(newValue);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeSystem] 修改Variable失败: " + e.Message);
            }
        }

        // ============================================================================
        // 私有方法
        // ============================================================================

        /// <summary>
        /// 生成随机种子（基于时间 + 用户ID + 物品ID）
        /// </summary>
        private static int GenerateRandomSeed(string userId, int itemId)
        {
            // 使用更细粒度的时间与运行时种子，避免同一秒内连续重铸复现相同随机序列。
            int timeSeed = unchecked((int)DateTime.UtcNow.Ticks);
            int runtimeSeed = Environment.TickCount;
            int userSeed = string.IsNullOrEmpty(userId) ? 0 : userId.GetHashCode();

            return timeSeed ^ runtimeSeed ^ userSeed ^ itemId;
        }

        /// <summary>
        /// 通过委托或反射修改ModifierDescription的value字段（性能优化版本）
        /// </summary>
        private static void ApplyModifierValueChange(ModifierDescription mod, float newValue)
        {
            try
            {
                // 优先使用委托（性能更好，约10倍提升）
                if (_setModifierValue != null)
                {
                    _setModifierValue(mod, newValue);
                }
                else
                {
                    // 回退到反射
                    var field = ModifierValueField;
                    if (field != null)
                    {
                        field.SetValue(mod, newValue);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeSystem] 修改属性值失败: " + e.Message);
            }
        }

        /// <summary>
        /// 公开的修改 Modifier 值方法（供持久化系统 ReforgeDataPersistence 使用）
        /// </summary>
        public static void ApplyModifierValueChangePublic(ModifierDescription mod, float newValue)
        {
            ApplyModifierValueChange(mod, newValue);
        }

        /// <summary>
        /// 公开的修改 Stat 值方法（供持久化系统 ReforgeDataPersistence 使用）
        /// </summary>
        public static void ApplyStatValueChangePublic(Stat stat, float newValue)
        {
            ApplyStatValueChange(stat, newValue);
        }

    }

    /// <summary>
    /// 重铸结果
    /// </summary>
    public class ReforgeResult
    {
        public bool Success;
        public string ErrorMessage;
        public int TotalCost;
        public float FinalProbability;
        public List<ModifiedStatInfo> ModifiedStats;
    }

    /// <summary>
    /// 修改的属性信息
    /// </summary>
    public class ModifiedStatInfo
    {
        public string StatKey;
        public float OldValue;
        public float NewValue;
        public float AdjustmentFactor;

        public string GetChangeDescription()
        {
            float change = NewValue - OldValue;
            string changeStr = change >= 0 ? "+" + change.ToString("F2") : change.ToString("F2");
            return StatKey + ": " + OldValue.ToString("F2") + " -> " + NewValue.ToString("F2") + " (" + changeStr + ")";
        }
    }
}
