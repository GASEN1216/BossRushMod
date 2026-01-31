// ============================================================================
// ReforgeSystem.cs - 重铸系统核心逻辑
// ============================================================================
// 模块说明：
//   管理装备重铸的核心算法，包括：
//   - 概率p同时影响：是否修改 + 幅度大小
//   - 基础概率由稀有度和物品价值决定（基准r=8,v=10000时p=0.20）
//   - 金钱增益：基于物品价值的倍数（1倍+10%，10倍+30%，100倍+100%）
//   - 幅度抽取：u^expo（p越大expo越小，幅度越大）
//   - 正负号由概率p决定（p越大越保持原符号，增强效果）
//   - 属性范围：预制体值≤1时用0~1，>1时用0~100%预制体值
//   - 整数属性保持整数
//   - 每次重铸保证至少有一个属性被修改
//   - 基础重铸费用为物品价值的1/10
//   - 保底机制：连续失败后提升成功率
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
        /// 每次重铸的最大改变量（绝对值0~1，用于小数值属性）
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
        /// 小数值属性阈值：预制体值≤此值时使用0~1范围，>此值时使用百分比范围
        /// </summary>
        private const float SMALL_VALUE_THRESHOLD = 1.0f;
        
        // ============================================================================
        // 保底机制常量
        // ============================================================================
        
        /// <summary>
        /// 保底机制：每次失败增加的概率加成
        /// </summary>
        private const float PITY_BONUS_PER_FAIL = 0.05f;
        
        /// <summary>
        /// 保底机制：最大累计加成（100%保底）
        /// </summary>
        private const float PITY_MAX_BONUS = 0.80f;
        
        /// <summary>
        /// 保底机制：物品保底计数器（使用物品InstanceID作为key）
        /// </summary>
        private static readonly Dictionary<int, int> _pityCounters = new Dictionary<int, int>();
        
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
        /// 计算金钱增益：基于物品价值的倍数
        /// 1倍物品价值 → +10%
        /// 10倍物品价值 → +30%
        /// 100倍物品价值 → +100%
        /// </summary>
        /// <param name="money">投入的金钱</param>
        /// <param name="itemValue">物品价值（用于计算动态阈值）</param>
        public static float MoneyBonus(int money, float itemValue)
        {
            if (money <= 0) return 0f;
            if (itemValue <= 0) itemValue = BASE_ITEM_VALUE;
            
            float m = (float)money;
            float B;
            
            // 动态阈值：基于物品价值的倍数
            float tier1 = itemValue;        // 1倍物品价值
            float tier2 = itemValue * 10f;  // 10倍物品价值
            float tier3 = itemValue * 100f; // 100倍物品价值
            
            // 计算log阈值（用于分段计算）
            float logTier1 = Mathf.Log10(tier1);
            float logTier2 = Mathf.Log10(tier2);
            float logTier3 = Mathf.Log10(tier3);
            
            if (m <= tier1)
            {
                // 0 ~ 1倍物品价值：线性 0 → 0.10
                B = 0.10f * (m / tier1);
            }
            else if (m <= tier2)
            {
                // 1倍 ~ 10倍物品价值：log分段 0.10 → 0.30
                float logM = Mathf.Log10(m);
                B = 0.10f + 0.20f * (logM - logTier1) / (logTier2 - logTier1);
            }
            else if (m <= tier3)
            {
                // 10倍 ~ 100倍物品价值：log分段 0.30 → 1.00
                float logM = Mathf.Log10(m);
                B = 0.30f + 0.70f * (logM - logTier2) / (logTier3 - logTier2);
            }
            else
            {
                // 超过100倍物品价值：上限1.00
                B = 1.00f;
            }
            
            return Mathf.Clamp01(B);
        }
        
        /// <summary>
        /// 计算基础重铸费用（物品价值的1/10，最低100）
        /// </summary>
        public static int GetBaseCost(Item item)
        {
            if (item == null) return MIN_REFORGE_COST;
            float itemValue = GetItemValue(item);
            int baseCost = Mathf.RoundToInt(itemValue / 10f);
            return Mathf.Max(MIN_REFORGE_COST, baseCost);
        }
        
        /// <summary>
        /// 计算基础重铸费用（基于物品价值）
        /// </summary>
        public static int GetBaseCost(float itemValue)
        {
            int baseCost = Mathf.RoundToInt(itemValue / 10f);
            return Mathf.Max(MIN_REFORGE_COST, baseCost);
        }
        
        /// <summary>
        /// 计算最终概率p（用于判定是否修改 + 幅度抽取）
        /// p = clamp(0, 1, p_item + B(m, itemValue))
        /// </summary>
        public static float FinalProbability(int rarity, float itemValue, int moneyInvested)
        {
            float pItem = BASE_PROBABILITY * RarityFactor(rarity) * ValueFactor(itemValue);
            float p = pItem + MoneyBonus(moneyInvested, itemValue);
            return Mathf.Clamp01(p);
        }
        
        /// <summary>
        /// 计算带保底的最终概率（用于实际重铸判定）
        /// </summary>
        public static float FinalProbabilityWithPity(int rarity, float itemValue, int moneyInvested, int itemInstanceId)
        {
            float baseP = FinalProbability(rarity, itemValue, moneyInvested);
            float pityBonus = GetPityBonus(itemInstanceId);
            return Mathf.Clamp01(baseP + pityBonus);
        }
        
        /// <summary>
        /// 获取物品的保底加成
        /// </summary>
        public static float GetPityBonus(int itemInstanceId)
        {
            if (_pityCounters.TryGetValue(itemInstanceId, out int failCount))
            {
                return Mathf.Min(failCount * PITY_BONUS_PER_FAIL, PITY_MAX_BONUS);
            }
            return 0f;
        }
        
        /// <summary>
        /// 获取物品的保底计数
        /// </summary>
        public static int GetPityCount(int itemInstanceId)
        {
            if (_pityCounters.TryGetValue(itemInstanceId, out int count))
            {
                return count;
            }
            return 0;
        }
        
        /// <summary>
        /// 增加保底计数（重铸结果不理想时调用）
        /// </summary>
        private static void IncrementPityCounter(int itemInstanceId)
        {
            if (_pityCounters.ContainsKey(itemInstanceId))
            {
                _pityCounters[itemInstanceId]++;
            }
            else
            {
                _pityCounters[itemInstanceId] = 1;
            }
            ModBehaviour.DevLog("[ReforgeSystem] 保底计数增加: " + _pityCounters[itemInstanceId]);
        }
        
        /// <summary>
        /// 重置保底计数（重铸结果理想时调用）
        /// </summary>
        private static void ResetPityCounter(int itemInstanceId)
        {
            if (_pityCounters.ContainsKey(itemInstanceId))
            {
                _pityCounters.Remove(itemInstanceId);
                ModBehaviour.DevLog("[ReforgeSystem] 保底计数已重置");
            }
        }
        
        /// <summary>
        /// 判断属性是否为整数类型（基于预制体值）
        /// </summary>
        private static bool IsIntegerProperty(float prefabValue)
        {
            // 如果预制体值与其四舍五入值相等，则认为是整数属性
            return Mathf.Approximately(prefabValue, Mathf.Round(prefabValue));
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
            float bonus = MoneyBonus(moneyInvested, itemValue);
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
        /// 1. 检查基础费用（物品价值的1/10）
        /// 2. 每个属性独立判定是否修改（rand < p + 保底加成）
        /// 3. 保证至少有一个属性被修改
        /// 4. 触发后用 u^expo 抽幅度
        /// 5. 时间+玩家ID决定正负号
        /// 6. 属性范围：预制体值≤1时用0~1，>1时用0~100%预制体值
        /// 7. 整数属性保持整数
        /// 8. 保底机制：连续失败后提升成功率
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
            
            // 计算基础费用（物品价值的1/10）
            float itemValue = GetItemValue(item);
            int baseCost = GetBaseCost(itemValue);
            
            if (moneyInvested < baseCost)
            {
                result.ErrorMessage = string.Format("投入金额不足（最低 {0}）", baseCost);
                return result;
            }
            
            try
            {
                // 获取物品属性
                int rarity = item.Quality;
                int itemId = item.GetInstanceID();
                
                // 计算原始概率（不含保底，用于幅度计算）
                float baseP = FinalProbability(rarity, itemValue, moneyInvested);
                // 计算带保底的概率（用于属性选中判定）
                float pWithPity = FinalProbabilityWithPity(rarity, itemValue, moneyInvested, itemId);
                float pityBonus = GetPityBonus(itemId);
                
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
                
                // 3. 收集Variables（跳过Count、ReforgeCount和重铸数据RF_前缀）
                if (item.Variables != null)
                {
                    foreach (var variable in item.Variables)
                    {
                        // 跳过系统变量
                        if (variable.Key == "Count" || variable.Key == "ReforgeCount") continue;
                        // 跳过重铸数据（RF_MOD_、RF_STAT_、RF_VAR_ 前缀）
                        if (variable.Key.StartsWith("RF_")) continue;
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
                
                // 记录哪些属性被选中修改
                List<int> selectedIndices = new List<int>();
                
                // 对每个属性独立判定是否修改（使用带保底的概率）
                for (int i = 0; i < allProperties.Count; i++)
                {
                    // Step 1: 判定是否修改（rand < pWithPity，保底只影响选中概率）
                    if (random.NextDouble() < pWithPity)
                    {
                        selectedIndices.Add(i);
                    }
                }
                
                // 保证至少有一个属性被修改
                if (selectedIndices.Count == 0 && allProperties.Count > 0)
                {
                    // 随机选择一个属性强制修改
                    int forcedIndex = random.Next(allProperties.Count);
                    selectedIndices.Add(forcedIndex);
                    ModBehaviour.DevLog("[ReforgeSystem] 所有属性都未命中，强制选择属性: " + allProperties[forcedIndex].Key);
                }
                
                // 对选中的属性执行修改
                foreach (int i in selectedIndices)
                {
                    ReforgeableProperty prop = allProperties[i];
                    
                    // 获取预制体原始值（用于范围限制和整数判定）
                    float prefabValue = GetPrefabPropertyValue(prefab, prop.Key, prop.Type, prop.Value);
                    bool isInteger = IsIntegerProperty(prefabValue);
                    bool isSmallValue = Mathf.Abs(prefabValue) <= SMALL_VALUE_THRESHOLD;
                    
                    // Step 2: 抽幅度（使用原始概率baseP，保底不影响幅度）
                    float mag01 = RollMagnitude(baseP, random);
                    
                    // Step 3: 决定正负号（使用原始概率baseP，保底不影响方向）
                    float originalValue = prop.Value;
                    int sign = RollSign(baseP, originalValue);
                    
                    // Step 4: 计算delta和新值
                    float delta, newValue;
                    float minValue, maxValue;
                    
                    if (Mathf.Approximately(prefabValue, 0f))
                    {
                        // 预制体值为0：使用固定范围
                        minValue = -ZERO_VALUE_RANGE;
                        maxValue = ZERO_VALUE_RANGE;
                        delta = sign * mag01 * ZERO_VALUE_RANGE;
                        newValue = originalValue + delta;
                        newValue = Mathf.Clamp(newValue, minValue, maxValue);
                    }
                    else if (isSmallValue)
                    {
                        // 小数值属性（预制体值≤1）：使用0~1绝对范围
                        delta = sign * mag01 * MAX_DELTA_ABSOLUTE;
                        newValue = originalValue + delta;
                        
                        if (prefabValue > 0)
                        {
                            // 正数小值：范围0~2（预制体值的0%~200%）
                            minValue = 0f;
                            maxValue = prefabValue * 2f;
                        }
                        else
                        {
                            // 负数小值：范围-2~0
                            minValue = prefabValue * 2f;
                            maxValue = 0f;
                        }
                        newValue = Mathf.Clamp(newValue, minValue, maxValue);
                    }
                    else
                    {
                        // 大数值属性（预制体值>1）：使用百分比范围（0~100%预制体值）
                        float maxDelta = Mathf.Abs(prefabValue) * MAX_VALUE_OFFSET_PERCENT;
                        delta = sign * mag01 * maxDelta;
                        newValue = originalValue + delta;
                        
                        if (prefabValue > 0)
                        {
                            // 正数属性：范围为预制体值的0%~200%
                            minValue = 0f;
                            maxValue = prefabValue * (1f + MAX_VALUE_OFFSET_PERCENT);
                        }
                        else
                        {
                            // 负数属性：范围反转
                            minValue = prefabValue * (1f + MAX_VALUE_OFFSET_PERCENT);
                            maxValue = 0f;
                        }
                        newValue = Mathf.Clamp(newValue, minValue, maxValue);
                    }
                    
                    // Step 5: 整数属性保持整数
                    if (isInteger)
                    {
                        newValue = Mathf.Round(newValue);
                    }
                    else
                    {
                        newValue = (float)System.Math.Round(newValue, 2);
                    }
                    
                    // 记录修改信息
                    ModifiedStatInfo statInfo = new ModifiedStatInfo();
                    statInfo.StatKey = prop.Key;
                    statInfo.OldValue = originalValue;
                    statInfo.NewValue = newValue;
                    statInfo.AdjustmentFactor = newValue - originalValue;
                    result.ModifiedStats.Add(statInfo);
                    
                    // 应用修改并保存重铸数据
                    ApplyPropertyChange(prop, newValue, item, prefab);
                }
                
                // 保底机制判定：
                // 检查是否有显著变化（相对于预制体值的30%以上变化）
                bool hasSignificantChange = false;
                foreach (var stat in result.ModifiedStats)
                {
                    // 获取预制体值用于计算相对变化
                    float prefabVal = GetPrefabPropertyValue(prefab, stat.StatKey, PropertyType.Modifier, stat.OldValue);
                    // 也尝试从Stats和Variables获取
                    if (Mathf.Approximately(prefabVal, stat.OldValue))
                    {
                        prefabVal = GetPrefabPropertyValue(prefab, stat.StatKey, PropertyType.Stat, stat.OldValue);
                    }
                    if (Mathf.Approximately(prefabVal, stat.OldValue))
                    {
                        prefabVal = GetPrefabPropertyValue(prefab, stat.StatKey, PropertyType.Variable, stat.OldValue);
                    }
                    
                    // 计算相对变化幅度
                    float relativeChange;
                    if (Mathf.Abs(prefabVal) < 0.001f)
                    {
                        // 预制体值接近0，使用绝对值判断
                        relativeChange = Mathf.Abs(stat.AdjustmentFactor);
                    }
                    else
                    {
                        // 相对于预制体值的百分比变化
                        relativeChange = Mathf.Abs(stat.AdjustmentFactor / prefabVal);
                    }
                    
                    // 30%以上的相对变化视为显著
                    if (relativeChange > 0.3f)
                    {
                        hasSignificantChange = true;
                        break;
                    }
                }
                
                if (hasSignificantChange)
                {
                    ResetPityCounter(itemId);
                    ModBehaviour.DevLog("[ReforgeSystem] 重铸幅度显著（>30%），保底计数重置");
                }
                else
                {
                    IncrementPityCounter(itemId);
                    ModBehaviour.DevLog("[ReforgeSystem] 重铸幅度较小（<30%），保底计数+1");
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
                result.FinalProbability = pWithPity;
                result.PityBonus = pityBonus;
                
                ModBehaviour.DevLog(string.Format("[ReforgeSystem] 重铸成功！选中概率={0:P0}(含保底{1:P0})，幅度概率={2:P0}，调整了{3}/{4}个属性", 
                    pWithPity, pityBonus, baseP, result.ModifiedStats.Count, allProperties.Count));
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
        public float PityBonus;  // 保底加成
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
