// ============================================================================
// DragonSetConfig.cs - 龙套装配置
// ============================================================================
// 模块说明：
//   龙套装（龙头、龙甲）的属性配置和初始化逻辑
//   - 护甲值、耐久度、元素抗性等属性
//   - 本地化注入
// 
// 属性键名参考（来自游戏源码 Health.cs / CharacterMainControl.cs）：
//   - HeadArmor / BodyArmor：头/身护甲值
//   - ElementFactor_Physics：物理伤害倍率（<1减伤，>1增伤）
//   - ElementFactor_Fire：火元素伤害倍率
//   - ElementFactor_Electricity：电元素伤害倍率
//   - ElementFactor_Poison：毒元素伤害倍率
//   - StormProtection：风暴防护
//   - ViewAngle：视野角度
//   - GunCritDamageGain：枪械暴击伤害加成（爆头伤害）
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 龙套装配置管理
    /// </summary>
    public static class DragonSetConfig
    {
        // ========== 龙套装物品基础名（用于匹配 AssetBundle 中的 Prefab）==========
        private const string DRAGON_HELM_BASE = "dargon_Helmet";
        private const string DRAGON_ARMOR_BASE = "dargon_Armor";

        // ========== 龙头配置 ==========
        private const int DRAGON_HELM_ARMOR = 7;           // 头盔护甲值（满上限）
        private const float DRAGON_HELM_DURABILITY = 200f; // 耐久度
        
        // 龙头属性
        private const float DRAGON_HELM_PHYSICS_RESIST = -0.1f;   // 物理减伤10%
        private const float DRAGON_HELM_STORM_PROTECTION = 1f;    // 风暴防护+1
        private const float DRAGON_HELM_COLD_PROTECTION = 1f;     // 寒冷防护+1
        private const float DRAGON_HELM_CRIT_DAMAGE_GAIN = 0.1f;  // 枪械爆头伤害+10%
        private const float DRAGON_HELM_FIRE_FACTOR = -0.15f;     // 火承伤倍率-15%
        private const float DRAGON_HELM_ELECTRIC_FACTOR = -0.15f; // 电承伤倍率-15%
        private const float DRAGON_HELM_POISON_FACTOR = 0.3f;     // 毒承伤倍率+30%
        private const float DRAGON_HELM_VIEW_ANGLE = -0.2f;       // 视野角度-20%

        // ========== 龙甲配置 ==========
        private const int DRAGON_ARMOR_ARMOR = 7;          // 护甲值（满上限）
        private const float DRAGON_ARMOR_DURABILITY = 200f; // 耐久度
        
        // 龙甲属性
        private const float DRAGON_ARMOR_PHYSICS_RESIST = -0.2f;   // 物理减伤20%
        private const float DRAGON_ARMOR_STORM_PROTECTION = 1f;    // 风暴防护+1
        private const float DRAGON_ARMOR_COLD_PROTECTION = 1f;     // 寒冷防护+1
        private const float DRAGON_ARMOR_FIRE_FACTOR = -0.2f;      // 火承伤倍率-20%
        private const float DRAGON_ARMOR_ELECTRIC_FACTOR = -0.2f;  // 电承伤倍率-20%
        private const float DRAGON_ARMOR_POISON_FACTOR = 0.4f;     // 毒承伤倍率+40%

        /// <summary>
        /// 尝试配置龙套装（自动识别是否为龙套装物品）
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;

            bool isDragonHelm = baseName.Equals(DRAGON_HELM_BASE, StringComparison.OrdinalIgnoreCase);
            bool isDragonArmor = baseName.Equals(DRAGON_ARMOR_BASE, StringComparison.OrdinalIgnoreCase);

            if (isDragonHelm)
            {
                ConfigureEquipment(item, true);
                return true;
            }
            else if (isDragonArmor)
            {
                ConfigureEquipment(item, false);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 配置龙套装装备属性
        /// </summary>
        public static void ConfigureEquipment(Item item, bool isHelm)
        {
            if (item == null) return;

            try
            {
                if (isHelm)
                {
                    ConfigureDragonHelm(item);
                }
                else
                {
                    ConfigureDragonArmor(item);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonSetConfig] [ERROR] ConfigureEquipment 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 配置赤龙首属性
        /// </summary>
        private static void ConfigureDragonHelm(Item item)
        {
            ModBehaviour.DevLog("[DragonSetConfig] 配置赤龙首属性...");

            // 设置护甲值（满护甲上限）
            EquipmentHelper.AddModifierToItem(item, "HeadArmor", ModifierType.Add, DRAGON_HELM_ARMOR, true);

            // 设置耐久度
            EquipmentHelper.SetItemConstant(item, "MaxDurability", DRAGON_HELM_DURABILITY);
            item.Durability = DRAGON_HELM_DURABILITY;

            // 风暴防护+1
            EquipmentHelper.AddModifierToItem(item, "StormProtection", ModifierType.Add, DRAGON_HELM_STORM_PROTECTION, true);

            // 寒冷防护+1
            EquipmentHelper.AddModifierToItem(item, "ColdProtection", ModifierType.Add, DRAGON_HELM_COLD_PROTECTION, true);

            // 枪械爆头伤害+10%
            EquipmentHelper.AddModifierToItem(item, "GunCritDamageGain", ModifierType.PercentageAdd, DRAGON_HELM_CRIT_DAMAGE_GAIN, true);

            // 物理减伤10%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Physics", ModifierType.PercentageAdd, DRAGON_HELM_PHYSICS_RESIST, true);

            // 火承伤倍率-15%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Fire", ModifierType.PercentageAdd, DRAGON_HELM_FIRE_FACTOR, true);

            // 电承伤倍率-15%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Electricity", ModifierType.PercentageAdd, DRAGON_HELM_ELECTRIC_FACTOR, true);

            // 毒承伤倍率+30%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Poison", ModifierType.PercentageAdd, DRAGON_HELM_POISON_FACTOR, true);

            // 视野角度-20%
            EquipmentHelper.AddModifierToItem(item, "ViewAngle", ModifierType.PercentageAdd, DRAGON_HELM_VIEW_ANGLE, true);

            // 添加可维修标签
            EquipmentHelper.AddRepairableTag(item);

            // 注入本地化
            EquipmentLocalization.InjectDragonHelmLocalization(item.TypeID);

            ModBehaviour.DevLog("[DragonSetConfig] 赤龙首配置完成");
        }

        /// <summary>
        /// 配置焰鳞甲属性
        /// </summary>
        private static void ConfigureDragonArmor(Item item)
        {
            ModBehaviour.DevLog("[DragonSetConfig] 配置焰鳞甲属性...");

            // 设置护甲值（满护甲上限）
            EquipmentHelper.AddModifierToItem(item, "BodyArmor", ModifierType.Add, DRAGON_ARMOR_ARMOR, true);

            // 设置耐久度
            EquipmentHelper.SetItemConstant(item, "MaxDurability", DRAGON_ARMOR_DURABILITY);
            item.Durability = DRAGON_ARMOR_DURABILITY;

            // 风暴防护+1
            EquipmentHelper.AddModifierToItem(item, "StormProtection", ModifierType.Add, DRAGON_ARMOR_STORM_PROTECTION, true);

            // 寒冷防护+1
            EquipmentHelper.AddModifierToItem(item, "ColdProtection", ModifierType.Add, DRAGON_ARMOR_COLD_PROTECTION, true);

            // 物理减伤20%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Physics", ModifierType.PercentageAdd, DRAGON_ARMOR_PHYSICS_RESIST, true);

            // 火承伤倍率-20%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Fire", ModifierType.PercentageAdd, DRAGON_ARMOR_FIRE_FACTOR, true);

            // 电承伤倍率-20%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Electricity", ModifierType.PercentageAdd, DRAGON_ARMOR_ELECTRIC_FACTOR, true);

            // 毒承伤倍率+40%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Poison", ModifierType.PercentageAdd, DRAGON_ARMOR_POISON_FACTOR, true);

            // 添加可维修标签
            EquipmentHelper.AddRepairableTag(item);

            // 注入本地化
            EquipmentLocalization.InjectDragonArmorLocalization(item.TypeID);

            ModBehaviour.DevLog("[DragonSetConfig] 焰鳞甲配置完成");
        }
    }
}
