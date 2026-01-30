// ============================================================================
// ReforgeSystem.cs - 重铸系统核心逻辑
// ============================================================================
// 模块说明：
//   管理装备重铸的核心算法，包括：
//   - 概率p同时影响：是否修改 + 幅度大小
//   - 基础概率由稀有度和物品价值决定（基准r=8,v=10000时p=0.20）
//   - 金钱增益：10万+10%，100万+30%，1000万+100%
//   - 幅度抽取：u^expo（p越大expo越小，幅度越大）
//   - 正负号由概率p决定（p越大越保持原符号，增强效果）
//   - 百分比制，最大±100%原值
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 重铸系统核心逻辑
    /// </summary>
    public static class ReforgeSystem
    {
        // ============================================================================
        // 缓存的反射 FieldInfo（性能优化）
        // ============================================================================
        private static System.Reflection.FieldInfo _modifierValueField;
        private static System.Reflection.FieldInfo _statBaseValueField;
        
        private static System.Reflection.FieldInfo ModifierValueField
        {
            get
            {
                if (_modifierValueField == null)
                {
                    _modifierValueField = typeof(ModifierDescription).GetField("value", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                return _modifierValueField;
            }
        }
        
        private static System.Reflection.FieldInfo StatBaseValueField
        {
            get
            {
                if (_statBaseValueField == null)
                {
                    _statBaseValueField = typeof(Stat).GetField("baseValue", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                return _statBaseValueField;
            }
        }
        
        // ============================================================================
        // 常量配置
        // ============================================================================
        
        /// <summary>
        /// 最小重铸费用
        /// </summary>
        public const int MIN_REFORGE_COST = 100;
        
        /// <summary>
        /// 基准概率常量（当稀有度=8，物品价值=10000时）
        /// </summary>
        private const float BASE_PROBABILITY = 0.20f;
        
        /// <summary>
        /// 基准物品价值
        /// </summary>
        private const float BASE_ITEM_VALUE = 10000f;
        
        /// <summary>
        /// 每次重铸的最大改变量（绝对值0~1）
        /// </summary>
        private const float MAX_DELTA_ABSOLUTE = 1.0f;
        
        /// <summary>
        /// 属性值相对于原值的最大偏移比例（±100%，即最终值在0~200%原值范围内）
        /// </summary>
        private const float MAX_VALUE_OFFSET_PERCENT = 1.0f;
        
        /// <summary>
        /// 当原值为0时，使用的固定绝对范围（防止0值永远不变）
        /// </summary>
        private const float ZERO_VALUE_RANGE = 0.5f;
        
        /// <summary>
        /// 幅度指数范围：p=0时expo=3.5，p=1时expo=0.6
        /// </summary>
        private const float EXPO_MAX = 3.5f;
        private const float EXPO_MIN = 0.6f;
        
        /// <summary>
        /// 可重铸的物品Tag（装备、武器、图腾）
        /// </summary>
        public static readonly string[] ReforgeableTags = new string[]
        {
            "Armor",
            "Helmet",       // 头盔
            "Helmat",       // 兼容旧版拼写
            "FaceMask",     // 面罩
            "Backpack",     // 背包
            "Headset",      // 耳机
            "Weapon",       // 武器（通用）
            "Gun",          // 枪械
            "MeleeWeapon",  // 近战武器
            "Melee",        // 近战（兼容）
            "Totem"         // 图腾
        };
        
        // ============================================================================
        // 概率计算函数
        // ============================================================================
        
        /// <summary>
        /// 计算稀有度因子：r=1→0.5，r=8→1.0
        /// </summary>
        public static float RarityFactor(int rarity)
        {
            int r = Mathf.Clamp(rarity, 1, 8);
            return 0.5f + 0.5f * (r - 1) / 7f;
        }
        
        /// <summary>
        /// 计算物品价值因子：v=10000→1.0，带上下限clamp(0.6, 1.4)
        /// </summary>
        public static float ValueFactor(float value)
        {
            if (value <= 0) value = 1f;
            float raw = Mathf.Pow(value / BASE_ITEM_VALUE, 0.25f);
            return Mathf.Clamp(raw, 0.6f, 1.4f);
        }
        
        /// <summary>
        /// 计算金钱增益：10万+10%，100万+30%，1000万+100%
        /// </summary>
        public static float MoneyBonus(int money)
        {
            if (money <= 0) return 0f;
            
            float m = (float)money;
            float B;
            
            if (m <= 1e5f)
            {
                // 0 ~ 10万：线性 0 → 0.10
                B = 0.10f * (m / 1e5f);
            }
            else if (m <= 1e6f)
            {
                // 10万 ~ 100万：log分段 0.10 → 0.30
                B = 0.10f + 0.20f * (Mathf.Log10(m) - 5f);
            }
            else if (m <= 1e7f)
            {
                // 100万 ~ 1000万：log分段 0.30 → 1.00
                B = 0.30f + 0.70f * (Mathf.Log10(m) - 6f);
            }
            else
            {
                // 超过1000万：上限1.00
                B = 1.00f;
            }
            
            return Mathf.Clamp01(B);
        }
        
        /// <summary>
        /// 计算最终概率p（用于判定是否修改 + 幅度抽取）
        /// p = clamp(0, 1, p_item + B(m))
        /// </summary>
        public static float FinalProbability(int rarity, float itemValue, int moneyInvested)
        {
            float pItem = BASE_PROBABILITY * RarityFactor(rarity) * ValueFactor(itemValue);
            float p = pItem + MoneyBonus(moneyInvested);
            return Mathf.Clamp01(p);
        }
        
        /// <summary>
        /// 抽取幅度值（0~1），p越大越容易抽到大数
        /// mag01 = u^expo，其中expo = 3.5 - 2.9*p
        /// </summary>
        public static float RollMagnitude(float p, System.Random random)
        {
            float u = (float)random.NextDouble();
            float expo = EXPO_MAX - (EXPO_MAX - EXPO_MIN) * p;  // p=0→3.5, p=1→0.6
            return Mathf.Pow(u, expo);
        }
        
        /// <summary>
        /// 决定正负号（基于概率p和原值符号）
        /// p越大越倾向于保持原符号（增强效果），p越小越可能反转符号
        /// 使用 UnityEngine.Random 确保真正随机，不依赖种子
        /// </summary>
        public static int RollSign(float p, float originalValue)
        {
            // 原值的符号：正数返回+1，负数返回-1，零返回+1
            int originalSign = originalValue >= 0 ? 1 : -1;
            
            // 保持原符号的概率 = 0.5 + 0.5 * p
            // p=0 → 50%保持原符号（纯随机）
            // p=1 → 100%保持原符号（必定增强）
            float keepSignProb = 0.5f + 0.5f * p;
            
            if (UnityEngine.Random.value < keepSignProb)
            {
                return originalSign;  // 保持原符号（增强效果）
            }
            else
            {
                return -originalSign; // 反转符号（削弱效果）
            }
        }
        
        // ============================================================================
        // 公共接口
        // ============================================================================
        
        /// <summary>
        /// 检查物品是否可以重铸
        /// 可重铸条件：只要有可修改的属性（Modifiers/Stats/Variables）就可以重铸
        /// </summary>
        public static bool CanReforge(Item item)
        {
            if (item == null) return false;
            
            // 取消Tag过滤，只要有可修改属性就可以重铸
            // 检查物品是否有任意属性（Modifiers 或 Stats 或 Variables）
            bool hasModifiers = item.Modifiers != null && item.Modifiers.Count > 0;
            bool hasStats = item.Stats != null && item.Stats.Count > 0;
            bool hasVariables = HasReforgeableVariables(item);
            
            return hasModifiers || hasStats || hasVariables;
        }
        
        /// <summary>
        /// 检查物品是否有可重铸的Variables（排除Count等不可修改项）
        /// </summary>
        private static bool HasReforgeableVariables(Item item)
        {
            if (item == null || item.Variables == null) return false;
            
            foreach (var variable in item.Variables)
            {
                // 跳过不可重铸的变量
                if (variable.Key == "Count") continue;
                
                // 只处理Float类型的变量
                if (variable.DataType == Duckov.Utilities.CustomDataType.Float)
                {
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// 获取物品的可重铸属性数量
        /// </summary>
        public static int GetReforgeablePropertyCount(Item item)
        {
            if (item == null) return 0;
            
            int count = 0;
            if (item.Modifiers != null) count += item.Modifiers.Count;
            if (item.Stats != null) count += item.Stats.Count;
            if (item.Variables != null) count += item.Variables.Count;
            
            return count;
        }
        
        /// <summary>
        /// 获取概率公式描述（用于UI显示）
        /// </summary>
        public static string GetProbabilityDescription(int rarity, float itemValue, int moneyInvested)
        {
            float fr = RarityFactor(rarity);
            float fv = ValueFactor(itemValue);
            float pItem = BASE_PROBABILITY * fr * fv;
            float bonus = MoneyBonus(moneyInvested);
            float p = FinalProbability(rarity, itemValue, moneyInvested);
            
            // 格式：基础(稀有度×价值) + 金钱增益 = 最终概率
            return string.Format("基础{0:P0}(r={1},v={2:F0}) + 金钱{3:P0} = {4:P0}", 
                pItem, rarity, itemValue, bonus, p);
        }
        
        /// <summary>
        /// 获取物品价值（尝试从物品获取）
        /// </summary>
        public static float GetItemValue(Item item)
        {
            if (item == null) return BASE_ITEM_VALUE;
            try
            {
                // 尝试获取物品价值
                return item.Value > 0 ? item.Value : BASE_ITEM_VALUE;
            }
            catch
            {
                return BASE_ITEM_VALUE;
            }
        }
        
        /// <summary>
        /// 执行重铸 - 新概率系统
        /// 1. 每个属性独立判定是否修改（rand < p）
        /// 2. 触发后用 u^expo 抽幅度
        /// 3. 时间+玩家ID决定正负号
        /// 4. 百分比制，最大±100%原值
        /// </summary>
        public static ReforgeResult Reforge(Item item, int moneyInvested, string userId)
        {
            ReforgeResult result = new ReforgeResult();
            result.Success = false;
            result.ModifiedStats = new List<ModifiedStatInfo>();
            
            if (!CanReforge(item))
            {
                result.ErrorMessage = "该物品无法重铸";
                return result;
            }
            
            if (moneyInvested < MIN_REFORGE_COST)
            {
                result.ErrorMessage = "投入金额不足（最低 " + MIN_REFORGE_COST + "）";
                return result;
            }
            
            try
            {
                // 获取物品属性
                int rarity = item.Quality;
                float itemValue = GetItemValue(item);
                int itemId = item.GetInstanceID();
                
                // 计算最终概率p
                float p = FinalProbability(rarity, itemValue, moneyInvested);
                
                // 创建随机种子
                int seed = GenerateRandomSeed(userId, itemId);
                System.Random random = new System.Random(seed);
                
                // 获取预制体（用于范围限制）
                Item prefab = GetItemPrefab(item);
                
                // 收集所有可调整的属性
                List<ReforgeableProperty> allProperties = new List<ReforgeableProperty>();
                
                // 1. 收集Modifiers
                if (item.Modifiers != null)
                {
                    foreach (ModifierDescription mod in item.Modifiers)
                    {
                        allProperties.Add(new ReforgeableProperty
                        {
                            Key = mod.Key,
                            Value = mod.Value,
                            Type = PropertyType.Modifier,
                            Source = mod
                        });
                    }
                }
                
                // 2. 收集Stats
                if (item.Stats != null)
                {
                    foreach (var stat in item.Stats)
                    {
                        allProperties.Add(new ReforgeableProperty
                        {
                            Key = stat.Key,
                            Value = stat.BaseValue,
                            Type = PropertyType.Stat,
                            Source = stat
                        });
                    }
                }
                
                // 3. 收集Variables（跳过Count和ReforgeCount）
                if (item.Variables != null)
                {
                    foreach (var variable in item.Variables)
                    {
                        if (variable.Key == "Count" || variable.Key == "ReforgeCount") continue;
                        float varValue = 0f;
                        try { varValue = variable.GetFloat(); } catch { continue; }
                        allProperties.Add(new ReforgeableProperty
                        {
                            Key = variable.Key,
                            Value = varValue,
                            Type = PropertyType.Variable,
                            Source = variable
                        });
                    }
                }
                
                if (allProperties.Count == 0)
                {
                    result.ErrorMessage = "该物品没有可调整的属性";
                    return result;
                }
                
                // 对每个属性独立判定是否修改
                for (int i = 0; i < allProperties.Count; i++)
                {
                    ReforgeableProperty prop = allProperties[i];
                    
                    // Step 1: 判定是否修改（rand < p）
                    if (random.NextDouble() >= p)
                    {
                        continue; // 不修改此属性
                    }
                    
                    // Step 2: 抽幅度（0~1，绝对值）
                    float mag01 = RollMagnitude(p, random);
                    
                    // Step 3: 决定正负号（基于概率p和原值符号，使用UnityEngine.Random）
                    float originalValue = prop.Value;
                    int sign = RollSign(p, originalValue);
                    
                    // Step 4: 计算delta（绝对值0~1）
                    float delta = sign * mag01 * MAX_DELTA_ABSOLUTE;
                    float newValue = originalValue + delta;
                    
                    // Step 5: 限制最终值范围（基于预制体原始值的±100%）
                    float prefabValue = GetPrefabPropertyValue(prefab, prop.Key, prop.Type, originalValue);
                    float minValue, maxValue;
                    
                    // 特殊处理：当预制体原值为0时，使用固定的绝对范围
                    if (Mathf.Approximately(prefabValue, 0f))
                    {
                        minValue = -ZERO_VALUE_RANGE;
                        maxValue = ZERO_VALUE_RANGE;
                        newValue = Mathf.Clamp(newValue, minValue, maxValue);
                    }
                    else if (prefabValue > 0)
                    {
                        // 正数属性：范围为预制体值的0%~200%
                        minValue = prefabValue * (1f - MAX_VALUE_OFFSET_PERCENT);
                        maxValue = prefabValue * (1f + MAX_VALUE_OFFSET_PERCENT);
                        newValue = Mathf.Clamp(newValue, minValue, maxValue);
                    }
                    else
                    {
                        // 负数属性：范围反转，预制体值的200%~0%
                        minValue = prefabValue * (1f + MAX_VALUE_OFFSET_PERCENT);
                        maxValue = prefabValue * (1f - MAX_VALUE_OFFSET_PERCENT);
                        newValue = Mathf.Clamp(newValue, minValue, maxValue);
                    }
                    newValue = (float)System.Math.Round(newValue, 2);
                    
                    // 记录修改信息
                    ModifiedStatInfo statInfo = new ModifiedStatInfo();
                    statInfo.StatKey = prop.Key;
                    statInfo.OldValue = originalValue;
                    statInfo.NewValue = newValue;
                    statInfo.AdjustmentFactor = delta;
                    result.ModifiedStats.Add(statInfo);
                    
                    // 应用修改并保存重铸数据
                    ApplyPropertyChange(prop, newValue, item, prefab);
                }
                
                // 更新重铸计数
                IncrementReforgeCount(item);
                
                // 触发物品属性更新
                try
                {
                    if (item.Modifiers != null)
                    {
                        item.Modifiers.ReapplyModifiers();
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ReforgeSystem] 重新应用修改器时出错: " + e.Message);
                }
                
                result.Success = true;
                result.TotalCost = moneyInvested;
                result.FinalProbability = p;
                
                ModBehaviour.DevLog(string.Format("[ReforgeSystem] 重铸成功！概率={0:P0}，调整了{1}/{2}个属性", 
                    p, result.ModifiedStats.Count, allProperties.Count));
            }
            catch (Exception e)
            {
                result.ErrorMessage = "重铸过程出错: " + e.Message;
                ModBehaviour.DevLog("[ReforgeSystem] 错误: " + e.Message + "\n" + e.StackTrace);
            }
            
            return result;
        }
        
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
                // 如果没有找到，尝试添加（如果支持的话）
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeSystem] 更新重铸计数失败: " + e.Message);
            }
        }
        
        // 属性类型枚举
        private enum PropertyType { Modifier, Stat, Variable }
        
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
                return ItemAssetsCollection.GetPrefab(typeId);
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
        /// 修改Stat的基础值
        /// </summary>
        private static void ApplyStatValueChange(Stat stat, float newValue)
        {
            if (stat == null) return;
            try
            {
                var field = StatBaseValueField;
                if (field != null)
                {
                    field.SetValue(stat, newValue);
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
            // 使用当前时间（秒级）+ 用户ID哈希 + 物品ID
            int timeSeed = (int)(DateTime.Now.Ticks / TimeSpan.TicksPerSecond);
            int userSeed = string.IsNullOrEmpty(userId) ? 0 : userId.GetHashCode();
            
            return timeSeed ^ userSeed ^ itemId;
        }
        
        /// <summary>
        /// 通过反射修改ModifierDescription的value字段
        /// </summary>
        private static void ApplyModifierValueChange(ModifierDescription mod, float newValue)
        {
            try
            {
                var field = ModifierValueField;
                if (field != null)
                {
                    field.SetValue(mod, newValue);
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
        
        /// <summary>
        /// 随机打乱列表
        /// </summary>
        private static void ShuffleList<T>(List<T> list, System.Random random)
        {
            int n = list.Count;
            for (int i = n - 1; i > 0; i--)
            {
                int j = random.Next(0, i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
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
