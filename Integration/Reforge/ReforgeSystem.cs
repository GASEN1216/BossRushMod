// ============================================================================
// ReforgeSystem.cs - 重铸系统核心逻辑
// ============================================================================
// 模块说明：
//   管理装备重铸的核心算法，包括：
//   - 概率p同时影响：是否修改 + 幅度大小
//   - 基础概率由稀有度和物品价值决定（基准r=8,v=10000时p=0.20）
//   - 金钱增益：基于物品价值的倍数（10倍+10%，100倍+30%，1000倍+100%）
//   - 幅度抽取：u^expo（p越大expo越小，幅度越大）
//   - 正负号由倾向滑块决定（0=必定负，1=必定正，0.5=各50%）
//   - 每次从预制体原值重抽：一般属性 ±50%，原值为0时 ±0.25，护甲 ±1（GetPropertyValueBounds）
//   - 幅度落进顶档（≥ BOUND_SNAP_MAGNITUDE）直接取到上下限，显示 Max/Min
//   - 整数属性（|值|≥2）保持整数
//   - 每次重铸改动全部未固定的可重铸属性（概率p只决定幅度，不决定改不改）
//   - 基础重铸费用为物品价值的1/100（最低 MIN_REFORGE_COST，再按哥布林好感度打折）
//   - 没有保底机制：不记录连续失败次数，也不随失败提升概率
// ============================================================================

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
    /// <summary>
    /// 重铸系统核心逻辑
    /// 性能优化版本：使用委托缓存替代反射调用
    /// </summary>
    public static partial class ReforgeSystem
    {
        // ============================================================================
        // 缓存的反射 FieldInfo（性能优化）
        // ============================================================================
        private static FieldInfo _modifierValueField;
        private static FieldInfo _statBaseValueField;

        // ============================================================================
        // 委托缓存（性能优化：比反射快约10倍）
        // ============================================================================
        private static Action<ModifierDescription, float> _setModifierValue;
        private static Action<Stat, float> _setStatValue;
        private static bool _delegatesInitialized = false;

        /// <summary>
        /// 初始化委托缓存（只执行一次）
        /// </summary>
        private static void InitializeSetterDelegates()
        {
            if (_delegatesInitialized) return;

            try
            {
                // 初始化 ModifierDescription.value 的 setter 委托
                var modField = typeof(ModifierDescription).GetField("value",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (modField != null)
                {
                    var param1 = Expression.Parameter(typeof(ModifierDescription), "mod");
                    var param2 = Expression.Parameter(typeof(float), "value");
                    var fieldAccess = Expression.Field(param1, modField);
                    var assign = Expression.Assign(fieldAccess, param2);
                    _setModifierValue = Expression.Lambda<Action<ModifierDescription, float>>(
                        assign, param1, param2).Compile();
                    _modifierValueField = modField;
                }

                // 初始化 Stat.baseValue 的 setter 委托
                var statField = typeof(Stat).GetField("baseValue",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (statField != null)
                {
                    var param1 = Expression.Parameter(typeof(Stat), "stat");
                    var param2 = Expression.Parameter(typeof(float), "value");
                    var fieldAccess = Expression.Field(param1, statField);
                    var assign = Expression.Assign(fieldAccess, param2);
                    _setStatValue = Expression.Lambda<Action<Stat, float>>(
                        assign, param1, param2).Compile();
                    _statBaseValueField = statField;
                }

                _delegatesInitialized = true;
                ModBehaviour.DevLog("[ReforgeSystem] 委托缓存初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeSystem] 委托缓存初始化失败，回退到反射: " + e.Message);
                // 回退到反射方式
                _modifierValueField = typeof(ModifierDescription).GetField("value",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _statBaseValueField = typeof(Stat).GetField("baseValue",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _delegatesInitialized = true;
            }
        }

        private static FieldInfo ModifierValueField
        {
            get
            {
                InitializeSetterDelegates();
                return _modifierValueField;
            }
        }

        private static FieldInfo StatBaseValueField
        {
            get
            {
                InitializeSetterDelegates();
                return _statBaseValueField;
            }
        }

        // ============================================================================
        // 兼容性
        // ============================================================================

        /// <summary>
        /// 判断 Modifier 是否为预制体中原生存在的属性。
        /// 仅做按 Key 的快速判断，无法区分同 Key 的重复 Modifier。
        /// 兼容其他 Mod 时，应优先使用带 item 参数的重载。
        /// </summary>
        public static bool IsNativeModifier(Item prefab, ModifierDescription mod)
        {
            if (prefab == null || prefab.Modifiers == null || mod == null) return false;
            foreach (var prefabMod in prefab.Modifiers)
            {
                if (prefabMod.Key == mod.Key) return true;
            }
            return false;
        }

        /// <summary>
        /// 判断当前 Modifier 是否属于物品预制体自带的那一份原生 Modifier。
        /// 当其他 Mod 追加了同 Key 的额外 Modifier 时，只把前 N 个（N = prefab 中该 Key 的数量）
        /// 视为原生项，避免把外部词缀错当成 BossRush 可重铸/可持久化的基础属性。
        /// </summary>
        public static bool IsNativeModifier(Item item, Item prefab, ModifierDescription mod)
        {
            if (mod == null)
            {
                return false;
            }

            if (item == null || item.Modifiers == null)
            {
                return IsNativeModifier(prefab, mod);
            }

            if (prefab == null || prefab.Modifiers == null)
            {
                return false;
            }

            int allowedCount = 0;
            foreach (var prefabMod in prefab.Modifiers)
            {
                if (prefabMod != null && prefabMod.Key == mod.Key)
                {
                    allowedCount++;
                }
            }

            if (allowedCount <= 0)
            {
                return false;
            }

            int seenCount = 0;
            foreach (var itemMod in item.Modifiers)
            {
                if (itemMod == null || itemMod.Key != mod.Key)
                {
                    continue;
                }

                seenCount++;
                if (ReferenceEquals(itemMod, mod))
                {
                    return seenCount <= allowedCount;
                }
            }

            return false;
        }

        // ============================================================================
        // 常量配置
        // ============================================================================

        /// <summary>
        /// 最小重铸费用
        /// </summary>
        public const int MIN_REFORGE_COST = 100;

        /// <summary>
        /// 基础概率：初始50%
        /// </summary>
        public const float BASE_PROBABILITY = 0.50f;

        /// <summary>
        /// 基准物品价值
        /// </summary>
        private const float BASE_ITEM_VALUE = 10000f;

        /// <summary>
        /// 金钱增益分段阈值（基于物品价值的倍数）
        /// 原始曲线阈值为 0.1 / 1 / 10。
        /// 按需求将整条曲线整体放大 100 倍，变为 10 / 100 / 1000，
        /// 保持形状不变，只放大达到同一加成所需的投入金额。
        /// </summary>
        public const float MONEY_BONUS_TIER1_MULTIPLIER = 10f;
        public const float MONEY_BONUS_TIER2_MULTIPLIER = 100f;
        public const float MONEY_BONUS_TIER3_MULTIPLIER = 1000f;

        /// <summary>
        /// 护甲类（FORCE_ABSOLUTE_RANGE_KEYS）每次重铸的最大绝对改变量：预制体值 ±1 档
        /// </summary>
        private const float MAX_DELTA_ABSOLUTE = 1.0f;

        /// <summary>
        /// 重铸相对预制体值的最大偏移（±50%），既是单次幅度也是最终上下限（预制体值的 50%~150%）。
        /// 2026-11-25 由 ±100% / 30%~200% 下调：玩家反馈重铸后全属性顶到 2 倍过强。
        /// </summary>
        private const float MAX_VALUE_OFFSET_PERCENT = 0.5f;

        /// <summary>
        /// 当原值为0时，使用的固定绝对范围（防止0值永远不变）；2026-11-25 由 ±0.5 下调
        /// </summary>
        private const float ZERO_VALUE_RANGE = 0.25f;

        /// <summary>
        /// 幅度指数范围：p=0时expo=3.5，p=1时expo=0.6
        /// </summary>
        private const float EXPO_MAX = 3.5f;
        private const float EXPO_MIN = 0.6f;

        /// <summary>
        /// 幅度顶档：抽到的幅度不低于此值时直接取到上下限（Max/Min）。
        /// 2026-11-25 改为从预制体原值重抽后，上限正好是连续分布的端点：小数属性抽中概率约万分之一，
        /// 三位小数的上限（如 0.35 的 0.525）舍入到两位后更是永远够不着，Max 标记与金色揭晓等于失效。
        /// 顶档 5% 在默认投入下每条约 2.5%、满投入约 8%（再乘倾向），只多给最多 2.5% 预制体值。
        /// </summary>
        private const float BOUND_SNAP_MAGNITUDE = 0.95f;

        /// <summary>
        /// UI判定属性是否已贴近上下限时使用的容差
        /// </summary>
        private const float BOUNDARY_EPSILON = 0.001f;

        /// <summary>
        /// 按绝对值 ±MAX_DELTA_ABSOLUTE（±1 档、不低于 0）取范围的属性Key列表（护甲值）
        /// </summary>
        private static readonly HashSet<string> FORCE_ABSOLUTE_RANGE_KEYS = new HashSet<string>
        {
            "HeadArmor",  // 头盔护甲值
            "BodyArmor"   // 身体护甲值
        };

        /// <summary>
        /// 不参与重铸的内部时序类 Stat。
        /// 这些字段会直接影响战斗判定窗口，暴露到重铸池后容易让武器行为失真。
        /// </summary>
        private static readonly HashSet<string> UNSUPPORTED_REFORGE_STAT_KEYS = new HashSet<string>
        {
            "DealDamageTime",
            "ShotCount",
            "ShotAngle"
        };

        /// <summary>
        /// 不参与重铸的 Variable 键。
        /// 先保留扩展点，后续如有新的运行时字段可直接加入。
        /// </summary>
        private static readonly HashSet<string> UNSUPPORTED_REFORGE_VARIABLE_KEYS = new HashSet<string>();

        // ============================================================================

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
        /// 10倍物品价值 → +10%
        /// 100倍物品价值 → +30%
        /// 1000倍物品价值 → +100%
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
            float tier1 = itemValue * MONEY_BONUS_TIER1_MULTIPLIER;  // 10倍物品价值
            float tier2 = itemValue * MONEY_BONUS_TIER2_MULTIPLIER;  // 100倍物品价值
            float tier3 = itemValue * MONEY_BONUS_TIER3_MULTIPLIER;  // 1000倍物品价值

            // 计算log阈值（用于分段计算）
            float logTier1 = Mathf.Log10(tier1);
            float logTier2 = Mathf.Log10(tier2);
            float logTier3 = Mathf.Log10(tier3);

            if (m <= tier1)
            {
                // 0 ~ 10倍物品价值：线性 0 → 0.10
                B = 0.10f * (m / tier1);
            }
            else if (m <= tier2)
            {
                // 10倍 ~ 100倍物品价值：log分段 0.10 → 0.30
                float logM = Mathf.Log10(m);
                B = 0.10f + 0.20f * (logM - logTier1) / (logTier2 - logTier1);
            }
            else if (m <= tier3)
            {
                // 100倍 ~ 1000倍物品价值：log分段 0.30 → 1.00
                float logM = Mathf.Log10(m);
                B = 0.30f + 0.70f * (logM - logTier2) / (logTier3 - logTier2);
            }
            else
            {
                // 超过1000倍物品价值：上限1.00
                B = 1.00f;
            }

            return Mathf.Clamp01(B);
        }

        /// <summary>
        /// 计算基础重铸费用（物品价值的1/100，最低100）
        /// </summary>
        public static int GetBaseCost(Item item)
        {
            if (item == null) return MIN_REFORGE_COST;
            float itemValue = GetItemValue(item);
            int baseCost = Mathf.RoundToInt(itemValue / 100f);
            return Mathf.Max(MIN_REFORGE_COST, baseCost);
        }

        /// <summary>
        /// 计算基础重铸费用（基于物品价值，1/100）
        /// </summary>
        public static int GetBaseCost(float itemValue)
        {
            int baseCost = Mathf.RoundToInt(itemValue / 100f);
            return Mathf.Max(MIN_REFORGE_COST, baseCost);
        }

        /// <summary>
        /// 计算带哥布林好感度折扣的重铸费用
        /// </summary>
        public static int GetDiscountedCost(Item item)
        {
            int baseCost = GetBaseCost(item);
            float discount = AffinityManager.GetDiscount(GoblinAffinityConfig.NPC_ID);
            int discountedCost = Mathf.RoundToInt(baseCost * (1f - discount));
            return Mathf.Max(MIN_REFORGE_COST, discountedCost);
        }

        /// <summary>
        /// 获取当前哥布林好感度折扣率（用于UI显示）
        /// </summary>
        public static float GetCurrentDiscount()
        {
            return AffinityManager.GetDiscount(GoblinAffinityConfig.NPC_ID);
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
        /// 判断属性是否为整数类型（基于预制体值）
        /// </summary>
        private static bool IsIntegerProperty(float prefabValue)
        {
            // |值|<2 按小数处理：1.0 这类倍率若按整数取整，只会在 0 和 2 之间跳
            if (Mathf.Abs(prefabValue) < 2f) return false;
            return Mathf.Approximately(prefabValue, Mathf.Round(prefabValue));
        }

        /// <summary>
        /// 计算指定属性在重铸系统中的允许范围。
        /// UI与实际重铸逻辑共用这套边界，避免 Max/Min 显示和真实结果不一致。
        /// </summary>
        public static void GetPropertyValueBounds(string key, float prefabValue, out float minValue, out float maxValue)
        {
            if (Mathf.Approximately(prefabValue, 0f))
            {
                minValue = -ZERO_VALUE_RANGE;
                maxValue = ZERO_VALUE_RANGE;
                return;
            }

            if (!string.IsNullOrEmpty(key) && FORCE_ABSOLUTE_RANGE_KEYS.Contains(key))
            {
                minValue = Mathf.Max(0f, prefabValue - MAX_DELTA_ABSOLUTE);
                maxValue = prefabValue + MAX_DELTA_ABSOLUTE;
            }
            else
            {
                float span = Mathf.Abs(prefabValue) * MAX_VALUE_OFFSET_PERCENT;
                minValue = prefabValue - span;
                maxValue = prefabValue + span;
            }

            // 整数属性的边界取到整数，保证 Max/Min 标记与实际能抽到的值一致
            if (IsIntegerProperty(prefabValue))
            {
                minValue = Mathf.Ceil(minValue - BOUNDARY_EPSILON);
                maxValue = Mathf.Floor(maxValue + BOUNDARY_EPSILON);
            }
        }

        /// <summary>
        /// 把属性值夹进当前重铸上下限。读档恢复旧 RF_ 数据时使用：
        /// 下调前刷到 200% 的旧装备按新上限生效，存档里的原始差值不改写。
        /// </summary>
        public static float ClampToReforgeBounds(string key, float prefabValue, float value)
        {
            float minValue, maxValue;
            GetPropertyValueBounds(key, prefabValue, out minValue, out maxValue);
            return Mathf.Clamp(value, minValue, maxValue);
        }

        /// <summary>
        /// 当前值是否已经达到重铸允许的最大值。
        /// </summary>
        public static bool IsValueAtUpperBound(string key, float prefabValue, float currentValue)
        {
            float minValue, maxValue;
            GetPropertyValueBounds(key, prefabValue, out minValue, out maxValue);
            return currentValue >= maxValue - BOUNDARY_EPSILON;
        }

        /// <summary>
        /// 当前值是否已经达到重铸允许的最小值。
        /// </summary>
        public static bool IsValueAtLowerBound(string key, float prefabValue, float currentValue)
        {
            float minValue, maxValue;
            GetPropertyValueBounds(key, prefabValue, out minValue, out maxValue);
            return currentValue <= minValue + BOUNDARY_EPSILON;
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
        /// 决定绝对正负号（基于tendencyChance: 0为必定负，1为必定正，0.5为各50%）
        /// tendencyChance通过滑动条进行设置，范围为0.0f - 1.0f。
        /// 使用 UnityEngine.Random 确保真正随机，不依赖种子
        /// </summary>
        public static int RollSign(float tendencyChance)
        {
            if (UnityEngine.Random.value < tendencyChance)
            {
                return 1;  // 生成正值
            }
            else
            {
                return -1; // 生成负值
            }
        }

        // ============================================================================
        // 公共接口
        // ============================================================================

        /// <summary>
        /// 判断 Variable 是否为系统跟踪字段。
        /// </summary>
        public static bool IsRuntimeTrackingVariableKey(string key)
        {
            return string.IsNullOrEmpty(key) ||
                   key == "Count" ||
                   key == "ReforgeCount" ||
                   key.StartsWith("RF_", StringComparison.Ordinal) ||
                   // 词缀锻造把词缀身份写在 AFX_ 前缀的物品 KV 上。这里是唯一咽喉点：
                   // 挡住之后，词缀 KV 既不会进重铸 roll 池，也不会被属性锁定 UI 列出，
                   // 更不会被 RF_ 差值同步覆盖——两套系统靠前缀双向互斥。
                   key.StartsWith("AFX_", StringComparison.Ordinal);
        }

        /// <summary>
        /// 判断指定属性键是否允许进入重铸池。
        /// </summary>
        public static bool IsPropertySupportedForReforge(string key, PropertyType type)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            switch (type)
            {
                case PropertyType.Modifier:
                    return true;
                case PropertyType.Stat:
                    return !UNSUPPORTED_REFORGE_STAT_KEYS.Contains(key);
                case PropertyType.Variable:
                    return !IsRuntimeTrackingVariableKey(key) &&
                           !UNSUPPORTED_REFORGE_VARIABLE_KEYS.Contains(key);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 判断 Modifier 是否可参与重铸。
        /// </summary>
        public static bool IsModifierEligibleForReforge(Item prefab, ModifierDescription mod)
        {
            return mod != null &&
                   mod.Display &&
                   IsNativeModifier(prefab, mod) &&
                   IsPropertySupportedForReforge(mod.Key, PropertyType.Modifier);
        }

        /// <summary>
        /// 判断 Modifier 是否可参与重铸。
        /// 对同 Key 的重复 Modifier 使用更保守的原生判定，避免误伤其他 Mod 的动态词缀。
        /// </summary>
        public static bool IsModifierEligibleForReforge(Item item, Item prefab, ModifierDescription mod)
        {
            return mod != null &&
                   mod.Display &&
                   IsNativeModifier(item, prefab, mod) &&
                   IsPropertySupportedForReforge(mod.Key, PropertyType.Modifier);
        }

        /// <summary>
        /// 判断 Stat 是否可参与重铸。
        /// </summary>
        public static bool IsStatEligibleForReforge(Stat stat)
        {
            return stat != null &&
                   stat.Display &&
                   IsPropertySupportedForReforge(stat.Key, PropertyType.Stat);
        }

        /// <summary>
        /// 判断 Variable 是否可参与重铸。
        /// </summary>
        public static bool IsVariableEligibleForReforge(CustomData variable)
        {
            return variable != null &&
                   variable.Display &&
                   variable.DataType == CustomDataType.Float &&
                   IsPropertySupportedForReforge(variable.Key, PropertyType.Variable);
        }

        /// <summary>
        /// 检查物品是否可以重铸
        /// 可重铸条件：只要有可修改的属性（Display=true的Modifiers/Stats/Variables）就可以重铸
        /// </summary>
        public static bool CanReforge(Item item)
        {
            if (item == null) return false;

            // 只检查Display=true的属性，与UI显示保持一致
            bool hasDisplayModifiers = HasDisplayableModifiers(item);
            bool hasDisplayStats = HasDisplayableStats(item);
            bool hasDisplayVariables = HasDisplayableVariables(item);

            return hasDisplayModifiers || hasDisplayStats || hasDisplayVariables;
        }

        /// <summary>
        /// 检查是否至少有一项未固定的可重铸属性，供按钮与执行入口共同使用。
        /// </summary>
        public static bool CanExecuteReforge(Item item)
        {
            if (item == null) return false;
            Item prefab = GetItemPrefab(item);
            if (item.Modifiers != null)
                foreach (var mod in item.Modifiers)
                    if (IsModifierEligibleForReforge(item, prefab, mod) && !PropertyLockSystem.IsPropertyLocked(item, mod.Key, PropertyType.Modifier)) return true;
            if (item.Stats != null)
                foreach (var stat in item.Stats)
                    if (IsStatEligibleForReforge(stat) && !PropertyLockSystem.IsPropertyLocked(item, stat.Key, PropertyType.Stat)) return true;
            if (item.Variables != null)
                foreach (var variable in item.Variables)
                    if (IsVariableEligibleForReforge(variable) && !PropertyLockSystem.IsPropertyLocked(item, variable.Key, PropertyType.Variable)) return true;
            return false;
        }

        /// <summary>检查物品是否有可显示的原生 Modifier。</summary>
        private static bool HasDisplayableModifiers(Item item)
        {
            if (item == null || item.Modifiers == null) return false;
            Item prefab = GetItemPrefab(item);

            foreach (var mod in item.Modifiers)
            {
                if (IsModifierEligibleForReforge(item, prefab, mod)) return true;
            }
            return false;
        }

        /// <summary>
        /// 检查物品是否有可显示的Stats（Display=true）
        /// </summary>
        private static bool HasDisplayableStats(Item item)
        {
            if (item == null || item.Stats == null) return false;

            foreach (var stat in item.Stats)
            {
                if (IsStatEligibleForReforge(stat)) return true;
            }
            return false;
        }

        /// <summary>
        /// 检查物品是否有可显示的Variables（Display=true，排除系统变量）
        /// </summary>
        private static bool HasDisplayableVariables(Item item)
        {
            if (item == null || item.Variables == null) return false;

            foreach (var variable in item.Variables)
            {
                if (IsVariableEligibleForReforge(variable))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 获取物品的可重铸属性数量（只计算Display=true的属性）
        /// </summary>
        public static int GetReforgeablePropertyCount(Item item)
        {
            if (item == null) return 0;

            int count = 0;

            // 只计算Display=true的Modifiers（排除非预制体原生的）
            if (item.Modifiers != null)
            {
                Item prefab = GetItemPrefab(item);
                foreach (var mod in item.Modifiers)
                {
                    if (IsModifierEligibleForReforge(item, prefab, mod)) count++;
                }
            }

            // 只计算Display=true的Stats
            if (item.Stats != null)
            {
                foreach (var stat in item.Stats)
                {
                    if (IsStatEligibleForReforge(stat)) count++;
                }
            }

            // 只计算Display=true的Variables（排除系统变量）
            if (item.Variables != null)
            {
                foreach (var variable in item.Variables)
                {
                    if (IsVariableEligibleForReforge(variable)) count++;
                }
            }

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
        /// 获取物品价值。
        /// 优先读取物品自身 Value；对运行时补配的自定义武器，若 Value 缺失则回退到
        /// GetTotalRawValue() 和已补配的 prefab 值，避免重铸 UI/费用读取到默认值。
        /// </summary>
        public static float GetItemValue(Item item)
        {
            if (item == null) return BASE_ITEM_VALUE;

            try
            {
                if (CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(item.TypeID))
                {
                    CustomItemRuntimeStateHelper.RestoreRuntimeState(item);
                }
            }
            catch { }

            try
            {
                if (item.Value > 0)
                {
                    return item.Value;
                }
            }
            catch { }

            try
            {
                int rawValue = item.GetTotalRawValue();
                if (rawValue > 0)
                {
                    return rawValue;
                }
            }
            catch { }

            try
            {
                Item prefab = GetItemPrefab(item);
                if (prefab != null)
                {
                    if (prefab.Value > 0)
                    {
                        return prefab.Value;
                    }

                    int prefabRawValue = prefab.GetTotalRawValue();
                    if (prefabRawValue > 0)
                    {
                        return prefabRawValue;
                    }
                }
            }
            catch { }

            return BASE_ITEM_VALUE;
        }

        /// <summary>
        /// 执行重铸 - 新概率系统
        /// 1. 检查基础费用（物品价值的1/100，含哥布林折扣）
        /// 2. 全部未固定的可重铸属性都会被修改
        /// 3. （无保底：不按连续失败调整概率）
        /// 4. 触发后用 u^expo 抽幅度
        /// 5. 收益方向由 tendencyChance 控制（0~1），再按属性极性换算数值正负号
        /// 6. 每次都以预制体原值为基准重新抽，不在当前值上累加；范围为预制体值 ±50%
        ///    （原值为0时 ±0.25，护甲 ±1），想留住好结果要用冷淬液固定
        /// 7. 整数属性（|值|≥2）保持整数；幅度落进顶档直接取到上下限（Max/Min）
        /// 8. 投入金额只通过 FinalProbability 影响幅度
        /// </summary>
        public static ReforgeResult Reforge(Item item, int moneyInvested, string userId, float tendencyChance = 0.5f)
        {
            ReforgeResult result = new ReforgeResult();
            result.Success = false;
            result.ModifiedStats = new List<ModifiedStatInfo>();

            if (!CanExecuteReforge(item))
            {
                // 能重铸但全被冷淬液固定：说清缺什么、怎么解；本来就没有可重铸属性的另给一句
                result.ErrorMessage = CanReforge(item)
                    ? L10n.T("这件物品能重铸的属性都已固定，没有可改的了。用回火液解开一项再来。", "Every reforgeable stat on this item is locked, so there's nothing to change. Unlock one with Tempering Solvent first.")
                    : L10n.T("这件物品无法重铸。", "This item can't be reforged.");
                return result;
            }

            // 计算基础费用（物品价值的1/100，应用哥布林好感度折扣）
            float itemValue = GetItemValue(item);
            int baseCost = GetDiscountedCost(item);

            if (moneyInvested < baseCost)
            {
                result.ErrorMessage = string.Format(L10n.T("投入金额不足：这件至少要投 {0}。", "Investment is too low: this item needs at least {0}."), baseCost);
                return result;
            }

            try
            {
                // 获取物品属性
                int rarity = item.Quality;
                int itemId = item.GetInstanceID();

                // 计算概率p（用于幅度计算）
                float baseP = FinalProbability(rarity, itemValue, moneyInvested);

                // 创建随机种子
                int seed = GenerateRandomSeed(userId, itemId);
                System.Random random = new System.Random(seed);

                // 获取预制体（用于范围限制）
                Item prefab = GetItemPrefab(item);

                // 收集所有可调整的属性（只收集 Display=true 的属性，与UI显示保持一致）
                List<ReforgeableProperty> allProperties = new List<ReforgeableProperty>();

                // 1. 收集Modifiers（只收集Display=true的，跳过非预制体原生属性和已固定的属性）
                if (item.Modifiers != null)
                {
                    foreach (ModifierDescription mod in item.Modifiers)
                    {
                        if (!IsModifierEligibleForReforge(item, prefab, mod))
                        {
                            if (mod != null && mod.Display && !IsNativeModifier(item, prefab, mod))
                            {
                                ModBehaviour.DevLog("[ReforgeSystem] 跳过非原生属性: " + mod.Key);
                            }
                            continue;
                        }

                        // 跳过已固定的属性（冷淬液功能）
                        if (PropertyLockSystem.IsPropertyLocked(item, mod.Key, PropertyType.Modifier))
                        {
                            ModBehaviour.DevLog("[ReforgeSystem] 跳过已固定属性: " + mod.Key + " (Modifier)");
                            continue;
                        }

                        allProperties.Add(new ReforgeableProperty
                        {
                            Key = mod.Key,
                            Value = mod.Value,
                            Type = PropertyType.Modifier,
                            Source = mod
                        });
                    }
                }

                // 2. 收集Stats（只收集Display=true的，跳过已固定的属性）
                if (item.Stats != null)
                {
                    foreach (var stat in item.Stats)
                    {
                        if (!IsStatEligibleForReforge(stat)) continue;

                        // 跳过已固定的属性（冷淬液功能）
                        if (PropertyLockSystem.IsPropertyLocked(item, stat.Key, PropertyType.Stat))
                        {
                            ModBehaviour.DevLog("[ReforgeSystem] 跳过已固定属性: " + stat.Key + " (Stat)");
                            continue;
                        }

                        allProperties.Add(new ReforgeableProperty
                        {
                            Key = stat.Key,
                            Value = stat.BaseValue,
                            Type = PropertyType.Stat,
                            Source = stat
                        });
                    }
                }

                // 3. 收集Variables（只收集Display=true的，跳过系统变量和已固定的属性）
                if (item.Variables != null)
                {
                    foreach (var variable in item.Variables)
                    {
                        if (!IsVariableEligibleForReforge(variable)) continue;

                        // 跳过已固定的属性（冷淬液功能）
                        if (PropertyLockSystem.IsPropertyLocked(item, variable.Key, PropertyType.Variable))
                        {
                            ModBehaviour.DevLog("[ReforgeSystem] 跳过已固定属性: " + variable.Key + " (Variable)");
                            continue;
                        }

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
                    result.ErrorMessage = L10n.T("该物品没有可调整的属性", "This item has no adjustable properties.");
                    return result;
                }

                // 记录哪些属性被选中修改
                List<int> selectedIndices = new List<int>();

                // 保证全部词条必定都会被重铸
                for (int i = 0; i < allProperties.Count; i++)
                {
                    selectedIndices.Add(i);
                }

                // 对选中的属性执行修改
                foreach (int i in selectedIndices)
                {
                    ReforgeableProperty prop = allProperties[i];

                    // 获取预制体原始值（用于范围限制、整数判定和重抽基准）
                    float prefabValue = GetPrefabPropertyValue(prefab, prop.Key, prop.Type, prop.Value);
                    bool isInteger = IsIntegerProperty(prefabValue);

                    // Step 2: 抽幅度（使用概率baseP）
                    float mag01 = RollMagnitude(baseP, random);

                    // Step 3: 先抽收益方向，再按官方属性极性转换成数值方向（后坐力越低越好）
                    float originalValue = prop.Value;
                    int sign = RollSign(tendencyChance) * GetBeneficialValueDirection(prop.Key);

                    // Step 4: 以预制体原值为基准重新抽（不在当前值上累加），幅度上限即边界半宽
                    float minValue, maxValue;
                    GetPropertyValueBounds(prop.Key, prefabValue, out minValue, out maxValue);
                    float rollRange;
                    if (Mathf.Approximately(prefabValue, 0f))
                    {
                        rollRange = ZERO_VALUE_RANGE;
                    }
                    else if (FORCE_ABSOLUTE_RANGE_KEYS.Contains(prop.Key))
                    {
                        rollRange = MAX_DELTA_ABSOLUTE;
                    }
                    else
                    {
                        rollRange = MAX_VALUE_OFFSET_PERCENT * Mathf.Abs(prefabValue);
                    }
                    float newValue;
                    if (mag01 >= BOUND_SNAP_MAGNITUDE)
                    {
                        // Step 5a: 幅度落进顶档直接取到上下限（Max/Min）。整数边界已在 GetPropertyValueBounds 取整；
                        // 小数边界不再舍入——0.525 舍到两位会掉到上限以下。已在该边界上时保持不变，不强行推开
                        newValue = sign > 0 ? maxValue : minValue;
                    }
                    else
                    {
                        newValue = prefabValue + sign * mag01 * rollRange;

                        // Step 5: 整数属性保持整数，且保证数值必定有变化
                        if (isInteger)
                        {
                            newValue = Mathf.Round(newValue);
                            // 如果四舍五入后没有变化，则根据sign强行偏移1个单位
                            if (Mathf.Approximately(newValue, originalValue))
                            {
                                newValue = originalValue + (sign != 0 ? sign : (random.Next(2) == 0 ? 1 : -1));
                            }
                        }
                        else
                        {
                            newValue = (float)System.Math.Round(newValue, 2);
                            // 如果舍入后没有变化，则强行偏移一个最小单位0.01
                            if (Mathf.Approximately(newValue, originalValue))
                            {
                                newValue = originalValue + (sign != 0 ? sign * 0.01f : (random.Next(2) == 0 ? 0.01f : -0.01f));
                                newValue = (float)System.Math.Round(newValue, 2);
                            }
                        }
                    }

                    // Step 6: 优先级要小于控制变化数值最大值的逻辑，即在最后进行Clamp截断
                    newValue = Mathf.Clamp(newValue, minValue, maxValue);

                    // 记录修改信息
                    ModifiedStatInfo statInfo = new ModifiedStatInfo();
                    statInfo.StatKey = prop.Key;
                    statInfo.OldValue = originalValue;
                    statInfo.NewValue = newValue;
                    statInfo.AdjustmentFactor = newValue - originalValue;
                    result.ModifiedStats.Add(statInfo);

                    // 应用修改并保存重铸数据
                    ApplyPropertyChange(prop, newValue, item, prefab);
                    float appliedValue = prop.Type == PropertyType.Modifier ? ((ModifierDescription)prop.Source).Value
                        : prop.Type == PropertyType.Stat ? ((Stat)prop.Source).BaseValue
                        : ((CustomData)prop.Source).GetFloat();
                    result.HasAppliedChanges |= !Mathf.Approximately(appliedValue, originalValue);
                }

                // 标记物品已恢复，防止下面 ReapplyModifiers 触发 Harmony Prefix 时重复恢复
                ReforgeDataPersistence.MarkAsRestored(item);
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
                result.FinalProbability = baseP;

                ModBehaviour.DevLog(string.Format("[ReforgeSystem] 重铸完成！幅度系数={0:P0}，调整了{1}/{2}个属性",
                    baseP, result.ModifiedStats.Count, allProperties.Count));
            }
            catch (Exception e)
            {
                result.ErrorMessage = L10n.T("重铸过程出错: ", "Reforge failed: ") + e.Message;
                ModBehaviour.DevLog("[ReforgeSystem] 错误: " + e.Message + "\n" + e.StackTrace);
            }

            return result;
        }
    }
}
