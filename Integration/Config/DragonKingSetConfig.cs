// ============================================================================
// DragonKingSetConfig.cs - 龙王套装配置
// ============================================================================
// 模块说明：
//   龙王专属套装（龙王之冕、龙王鳞铠）的属性配置和初始化逻辑
//   相比龙裔套装（赤龙首、焰鳞甲），龙王套装移除了负面效果：
//   - 移除毒元素承伤增加
//   - 移除视野角度减少
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 龙王套装配置管理
    /// </summary>
    public static class DragonKingSetConfig
    {
        // ========== 龙王套装物品基础名（用于匹配 AssetBundle 中的 Prefab）==========
        private const string DRAGON_KING_HELM_BASE = "dragonking_Helmet";
        private const string DRAGON_KING_ARMOR_BASE = "dragonking_Armor";

        // ========== 龙王之冕配置 ==========
        private const int DRAGON_KING_HELM_ARMOR = 7;           // 头盔护甲值（满上限）
        private const float DRAGON_KING_HELM_DURABILITY = 200f; // 耐久度
        
        // 龙王之冕属性（无负面效果）
        private const float DRAGON_KING_HELM_PHYSICS_RESIST = -0.15f;   // 物理减伤15%
        private const float DRAGON_KING_HELM_STORM_PROTECTION = 1f;    // 风暴防护+1
        private const float DRAGON_KING_HELM_COLD_PROTECTION = 1f;     // 寒冷防护+1
        private const float DRAGON_KING_HELM_CRIT_DAMAGE_GAIN = 0.15f;  // 枪械爆头伤害+15%
        private const float DRAGON_KING_HELM_FIRE_FACTOR = -0.2f;     // 火承伤倍率-20%
        private const float DRAGON_KING_HELM_ELECTRIC_FACTOR = -0.2f; // 电承伤倍率-20%
        // 移除：毒承伤倍率、视野角度减少

        // ========== 龙王鳞铠配置 ==========
        private const int DRAGON_KING_ARMOR_ARMOR = 7;          // 护甲值（满上限）
        private const float DRAGON_KING_ARMOR_DURABILITY = 200f; // 耐久度
        
        // 龙王鳞铠属性（无负面效果）
        private const float DRAGON_KING_ARMOR_PHYSICS_RESIST = -0.25f;   // 物理减伤25%
        private const float DRAGON_KING_ARMOR_STORM_PROTECTION = 1f;    // 风暴防护+1
        private const float DRAGON_KING_ARMOR_COLD_PROTECTION = 1f;     // 寒冷防护+1
        private const float DRAGON_KING_ARMOR_FIRE_FACTOR = -0.25f;      // 火承伤倍率-25%
        private const float DRAGON_KING_ARMOR_ELECTRIC_FACTOR = -0.25f;  // 电承伤倍率-25%
        // 移除：毒承伤倍率

        /// <summary>
        /// 尝试配置龙王套装（自动识别是否为龙王套装物品）
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;

            bool isDragonKingHelm = baseName.Equals(DRAGON_KING_HELM_BASE, StringComparison.OrdinalIgnoreCase);
            bool isDragonKingArmor = baseName.Equals(DRAGON_KING_ARMOR_BASE, StringComparison.OrdinalIgnoreCase);

            if (isDragonKingHelm)
            {
                ConfigureEquipment(item, true);
                return true;
            }
            else if (isDragonKingArmor)
            {
                ConfigureEquipment(item, false);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 配置龙王套装装备属性
        /// </summary>
        public static void ConfigureEquipment(Item item, bool isHelm)
        {
            if (item == null) return;

            try
            {
                if (isHelm)
                {
                    ConfigureDragonKingHelm(item);
                }
                else
                {
                    ConfigureDragonKingArmor(item);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKingSetConfig] [ERROR] ConfigureEquipment 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 配置龙王之冕属性
        /// </summary>
        private static void ConfigureDragonKingHelm(Item item)
        {
            ModBehaviour.DevLog("[DragonKingSetConfig] 配置龙王之冕属性...");

            // 设置护甲值（满护甲上限）
            EquipmentHelper.AddModifierToItem(item, "HeadArmor", ModifierType.Add, DRAGON_KING_HELM_ARMOR, true);

            // 设置耐久度
            EquipmentHelper.SetItemConstant(item, "MaxDurability", DRAGON_KING_HELM_DURABILITY);
            item.Durability = DRAGON_KING_HELM_DURABILITY;

            // 风暴防护+1
            EquipmentHelper.AddModifierToItem(item, "StormProtection", ModifierType.Add, DRAGON_KING_HELM_STORM_PROTECTION, true);

            // 寒冷防护+1
            EquipmentHelper.AddModifierToItem(item, "ColdProtection", ModifierType.Add, DRAGON_KING_HELM_COLD_PROTECTION, true);

            // 枪械爆头伤害+10%
            EquipmentHelper.AddModifierToItem(item, "GunCritDamageGain", ModifierType.PercentageAdd, DRAGON_KING_HELM_CRIT_DAMAGE_GAIN, true);

            // 物理减伤10%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Physics", ModifierType.PercentageAdd, DRAGON_KING_HELM_PHYSICS_RESIST, true);

            // 火承伤倍率-15%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Fire", ModifierType.PercentageAdd, DRAGON_KING_HELM_FIRE_FACTOR, true);

            // 电承伤倍率-15%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Electricity", ModifierType.PercentageAdd, DRAGON_KING_HELM_ELECTRIC_FACTOR, true);

            // 龙王套装：移除毒承伤和视野减少的负面效果

            // 添加可维修标签
            EquipmentHelper.AddRepairableTag(item);

            // 注入本地化
            EquipmentLocalization.InjectDragonKingHelmLocalization(item.TypeID);

            ModBehaviour.DevLog("[DragonKingSetConfig] 龙王之冕配置完成");
        }

        /// <summary>
        /// 配置龙王鳞铠属性
        /// </summary>
        private static void ConfigureDragonKingArmor(Item item)
        {
            ModBehaviour.DevLog("[DragonKingSetConfig] 配置龙王鳞铠属性...");

            // 设置护甲值（满护甲上限）
            EquipmentHelper.AddModifierToItem(item, "BodyArmor", ModifierType.Add, DRAGON_KING_ARMOR_ARMOR, true);

            // 设置耐久度
            EquipmentHelper.SetItemConstant(item, "MaxDurability", DRAGON_KING_ARMOR_DURABILITY);
            item.Durability = DRAGON_KING_ARMOR_DURABILITY;

            // 风暴防护+1
            EquipmentHelper.AddModifierToItem(item, "StormProtection", ModifierType.Add, DRAGON_KING_ARMOR_STORM_PROTECTION, true);

            // 寒冷防护+1
            EquipmentHelper.AddModifierToItem(item, "ColdProtection", ModifierType.Add, DRAGON_KING_ARMOR_COLD_PROTECTION, true);

            // 物理减伤20%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Physics", ModifierType.PercentageAdd, DRAGON_KING_ARMOR_PHYSICS_RESIST, true);

            // 火承伤倍率-20%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Fire", ModifierType.PercentageAdd, DRAGON_KING_ARMOR_FIRE_FACTOR, true);

            // 电承伤倍率-20%
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Electricity", ModifierType.PercentageAdd, DRAGON_KING_ARMOR_ELECTRIC_FACTOR, true);

            // 龙王套装：移除毒承伤的负面效果

            // 添加可维修标签
            EquipmentHelper.AddRepairableTag(item);

            // 注入本地化
            EquipmentLocalization.InjectDragonKingArmorLocalization(item.TypeID);

            ModBehaviour.DevLog("[DragonKingSetConfig] 龙王鳞铠配置完成");
        }
    }
}
