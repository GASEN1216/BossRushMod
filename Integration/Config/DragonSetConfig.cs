// ============================================================================
// DragonSetConfig.cs - 龙套装配置
// ============================================================================
// 模块说明：
//   龙套装（龙头、龙甲）的属性配置和初始化逻辑
//   - 护甲值、耐久度、物理减伤等属性
//   - 本地化注入
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
        private const int DRAGON_HELM_ARMOR = 7;           // 头盔护甲值
        private const float DRAGON_HELM_PHYSICS_RESIST = -0.2f;  // 物理减伤 20%
        private const float DRAGON_HELM_DURABILITY = 100f; // 耐久度
        
        // ========== 龙甲配置 ==========
        private const int DRAGON_ARMOR_ARMOR = 7;          // 护甲值
        private const float DRAGON_ARMOR_PHYSICS_RESIST = -0.2f; // 物理减伤 20%
        private const float DRAGON_ARMOR_DURABILITY = 100f; // 耐久度
        
        /// <summary>
        /// 尝试配置龙套装（自动识别是否为龙套装物品）
        /// </summary>
        /// <param name="item">装备物品</param>
        /// <param name="baseName">物品基础名（如 dargon_Helmet）</param>
        /// <returns>是否为龙套装物品</returns>
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
        /// <param name="item">装备物品</param>
        /// <param name="isHelm">是否为龙头（false 则为龙甲）</param>
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
                Debug.LogError("[DragonSetConfig] ConfigureEquipment 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 配置赤龙首属性
        /// </summary>
        private static void ConfigureDragonHelm(Item item)
        {
            Debug.Log("[DragonSetConfig] 配置赤龙首属性...");
            
            // 设置护甲值（使用公共辅助方法）
            EquipmentHelper.AddModifierToItem(item, "HeadArmor", ModifierType.Add, DRAGON_HELM_ARMOR, true);
            
            // 设置物理减伤
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Physics", ModifierType.PercentageAdd, DRAGON_HELM_PHYSICS_RESIST, true);
            
            // 设置耐久度
            EquipmentHelper.SetItemConstant(item, "MaxDurability", DRAGON_HELM_DURABILITY);
            item.Durability = DRAGON_HELM_DURABILITY;  // 初始化当前耐久度
            
            // 添加可维修标签
            EquipmentHelper.AddRepairableTag(item);
            
            // 注入本地化
            EquipmentLocalization.InjectDragonHelmLocalization(item.TypeID);
            
            Debug.Log("[DragonSetConfig] 赤龙首配置完成: HeadArmor=" + DRAGON_HELM_ARMOR + 
                ", PhysicsResist=" + (DRAGON_HELM_PHYSICS_RESIST * 100) + "%, Durability=" + DRAGON_HELM_DURABILITY);
        }
        
        /// <summary>
        /// 配置焰鳞甲属性
        /// </summary>
        private static void ConfigureDragonArmor(Item item)
        {
            Debug.Log("[DragonSetConfig] 配置焰鳞甲属性...");
            
            // 设置护甲值（使用公共辅助方法）
            EquipmentHelper.AddModifierToItem(item, "BodyArmor", ModifierType.Add, DRAGON_ARMOR_ARMOR, true);
            
            // 设置物理减伤
            EquipmentHelper.AddModifierToItem(item, "ElementFactor_Physics", ModifierType.PercentageAdd, DRAGON_ARMOR_PHYSICS_RESIST, true);
            
            // 设置耐久度
            EquipmentHelper.SetItemConstant(item, "MaxDurability", DRAGON_ARMOR_DURABILITY);
            item.Durability = DRAGON_ARMOR_DURABILITY;  // 初始化当前耐久度
            
            // 添加可维修标签
            EquipmentHelper.AddRepairableTag(item);
            
            // 注入本地化
            EquipmentLocalization.InjectDragonArmorLocalization(item.TypeID);
            
            Debug.Log("[DragonSetConfig] 焰鳞甲配置完成: BodyArmor=" + DRAGON_ARMOR_ARMOR + 
                ", PhysicsResist=" + (DRAGON_ARMOR_PHYSICS_RESIST * 100) + "%, Durability=" + DRAGON_ARMOR_DURABILITY);
        }
    }
}
