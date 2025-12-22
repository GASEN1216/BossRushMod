// ============================================================================
// EquipmentLocalization.cs - 装备本地化
// ============================================================================
// 模块说明：
//   统一管理所有自定义装备的本地化文本注入
//   - 龙套装（龙头、龙甲）
//   - 未来可扩展其他装备
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 装备本地化管理
    /// </summary>
    public static class EquipmentLocalization
    {
        #region 龙套装本地化数据
        
        // 赤龙首（原龙头）
        private static readonly string DragonHelmNameCN = "赤龙首";
        private static readonly string DragonHelmNameEN = "Crimson Dragon Helm";
        private static readonly string DragonHelmDescCN = "由旧时代火龙残骸锻造而成的头盔，鳞片依然散发着微弱的热量。据说与焰鳞甲一同穿戴时，会产生神秘的共鸣，获得火龙的祝福。";
        private static readonly string DragonHelmDescEN = "A helmet forged from the remains of an ancient fire dragon. The scales still emit faint warmth. Legend says wearing it with Flame Scale Armor creates a mysterious resonance, granting the fire dragon's blessing.";
        
        // 焰鳞甲（原龙甲）
        private static readonly string DragonArmorNameCN = "焰鳞甲";
        private static readonly string DragonArmorNameEN = "Flame Scale Armor";
        private static readonly string DragonArmorDescCN = "以旧时代火龙的胸甲残片为核心打造的护甲，触摸时能感受到沉睡的龙焰。与赤龙首一同穿戴时，会唤醒其中蕴含的远古力量。";
        private static readonly string DragonArmorDescEN = "Armor crafted around chest plate fragments of an ancient fire dragon. You can feel dormant dragon flames when touching it. Wearing it with Crimson Dragon Helm awakens the ancient power within.";
        
        #endregion
        
        #region 公共方法
        
        /// <summary>
        /// 注入所有装备本地化
        /// </summary>
        public static void InjectAllEquipmentLocalizations()
        {
            try
            {
                InjectDragonSetLocalization();
                Debug.Log("[EquipmentLocalization] 所有装备本地化注入完成");
            }
            catch (Exception e)
            {
                Debug.LogError("[EquipmentLocalization] 注入装备本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入龙套装本地化（龙头 + 龙甲）
        /// </summary>
        public static void InjectDragonSetLocalization()
        {
            InjectDragonHelmLocalization(0);
            InjectDragonArmorLocalization(0);
        }
        
        /// <summary>
        /// 注入龙头本地化
        /// </summary>
        /// <param name="typeId">物品 TypeID，用于注入 Item_xxx 键（0 表示不注入）</param>
        public static void InjectDragonHelmLocalization(int typeId)
        {
            try
            {
                string displayName = L10n.T(DragonHelmNameCN, DragonHelmNameEN);
                string description = L10n.T(DragonHelmDescCN, DragonHelmDescEN);
                
                // 注入中文键
                LocalizationHelper.InjectLocalization(DragonHelmNameCN, displayName);
                LocalizationHelper.InjectLocalization(DragonHelmNameCN + "_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                Debug.Log("[EquipmentLocalization] 赤龙首本地化注入完成");
            }
            catch (Exception e)
            {
                Debug.LogError("[EquipmentLocalization] 注入赤龙首本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入龙甲本地化
        /// </summary>
        /// <param name="typeId">物品 TypeID，用于注入 Item_xxx 键（0 表示不注入）</param>
        public static void InjectDragonArmorLocalization(int typeId)
        {
            try
            {
                string displayName = L10n.T(DragonArmorNameCN, DragonArmorNameEN);
                string description = L10n.T(DragonArmorDescCN, DragonArmorDescEN);
                
                // 注入中文键
                LocalizationHelper.InjectLocalization(DragonArmorNameCN, displayName);
                LocalizationHelper.InjectLocalization(DragonArmorNameCN + "_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                Debug.Log("[EquipmentLocalization] 焰鳞甲本地化注入完成");
            }
            catch (Exception e)
            {
                Debug.LogError("[EquipmentLocalization] 注入焰鳞甲本地化失败: " + e.Message);
            }
        }
        
        #endregion
        
        #region 辅助方法 - 获取本地化文本
        
        /// <summary>
        /// 获取赤龙首显示名称
        /// </summary>
        public static string GetDragonHelmName()
        {
            return L10n.T(DragonHelmNameCN, DragonHelmNameEN);
        }
        
        /// <summary>
        /// 获取赤龙首描述
        /// </summary>
        public static string GetDragonHelmDescription()
        {
            return L10n.T(DragonHelmDescCN, DragonHelmDescEN);
        }
        
        /// <summary>
        /// 获取焰鳞甲显示名称
        /// </summary>
        public static string GetDragonArmorName()
        {
            return L10n.T(DragonArmorNameCN, DragonArmorNameEN);
        }
        
        /// <summary>
        /// 获取焰鳞甲描述
        /// </summary>
        public static string GetDragonArmorDescription()
        {
            return L10n.T(DragonArmorDescCN, DragonArmorDescEN);
        }
        
        #endregion
    }
}
