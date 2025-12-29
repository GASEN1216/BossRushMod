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
        
        #region 龙裔遗族Boss本地化数据
        
        // 龙裔遗族Boss名称（红色显示）
        private static readonly string DragonDescendantNameCN = "<color=red>龙裔遗族</color>";
        private static readonly string DragonDescendantNameEN = "<color=red>Dragon Descendant</color>";
        
        // 复活台词
        private static readonly string DragonDescendantResurrectionCN = "我...命不该绝！";
        private static readonly string DragonDescendantResurrectionEN = "I... shall not fall!";
        
        #endregion
        
        #region 龙息武器本地化数据
        
        // 龙息武器名称
        private static readonly string DragonBreathNameCN = "龙息";
        private static readonly string DragonBreathNameEN = "Dragon's Breath";
        
        // 龙息武器描述
        private static readonly string DragonBreathDescCN = "J-Lab实验室将赤龙的残骸与MCX相结合的完美艺术品。按下扳机的那一刻，你会明白\"生存\"和\"撤离\"之间还有第三个选项：把道路烤出来。";
        private static readonly string DragonBreathDescEN = "A masterpiece from J-Lab, fusing crimson dragon remains with the MCX. The moment you pull the trigger, you'll realize there's a third option between 'survive' and 'extract': burn your way out.";
        
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
                InjectDragonDescendantLocalization();
                InjectDragonBreathWeaponLocalization();
                ModBehaviour.DevLog("[EquipmentLocalization] 所有装备本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入装备本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入龙裔遗族Boss本地化
        /// </summary>
        public static void InjectDragonDescendantLocalization()
        {
            try
            {
                string displayName = L10n.T(DragonDescendantNameCN, DragonDescendantNameEN);
                
                // 注入Boss名称键
                LocalizationHelper.InjectLocalization("DragonDescendant", displayName);
                LocalizationHelper.InjectLocalization("龙裔遗族", displayName);
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙裔遗族本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙裔遗族本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取龙裔遗族显示名称
        /// </summary>
        public static string GetDragonDescendantName()
        {
            return L10n.T(DragonDescendantNameCN, DragonDescendantNameEN);
        }
        
        /// <summary>
        /// 获取龙裔遗族复活台词
        /// </summary>
        public static string GetDragonDescendantResurrectionDialogue()
        {
            return L10n.T(DragonDescendantResurrectionCN, DragonDescendantResurrectionEN);
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
                
                // 注入原始键（Item 的 displayName 字段值为 "龙头"）
                LocalizationHelper.InjectLocalization("龙头", displayName);
                LocalizationHelper.InjectLocalization("龙头_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                ModBehaviour.DevLog("[EquipmentLocalization] 赤龙首本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入赤龙首本地化失败: " + e.Message);
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
                
                // 注入原始键（Item 的 displayName 字段值为 "龙甲"）
                LocalizationHelper.InjectLocalization("龙甲", displayName);
                LocalizationHelper.InjectLocalization("龙甲_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                ModBehaviour.DevLog("[EquipmentLocalization] 焰鳞甲本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入焰鳞甲本地化失败: " + e.Message);
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
        
        #region 龙息武器本地化方法
        
        /// <summary>
        /// 注入龙息武器本地化
        /// </summary>
        public static void InjectDragonBreathWeaponLocalization()
        {
            try
            {
                string displayName = L10n.T(DragonBreathNameCN, DragonBreathNameEN);
                string description = L10n.T(DragonBreathDescCN, DragonBreathDescEN);
                
                // 注入原始键（Item 的 displayName 字段值为 "龙息"）
                LocalizationHelper.InjectLocalization("龙息", displayName);
                LocalizationHelper.InjectLocalization("龙息_Desc", description);
                
                // 注入 DragonBreathConfig 中定义的本地化键
                LocalizationHelper.InjectLocalization(DragonBreathConfig.LOC_KEY_WEAPON_NAME, displayName);
                LocalizationHelper.InjectLocalization(DragonBreathConfig.LOC_KEY_WEAPON_DESC, description);
                
                // 注入物品 ID 键（TypeID = 500005）
                int typeId = DragonBreathConfig.WEAPON_TYPE_ID;
                string itemKey = "Item_" + typeId;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙息武器本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙息武器本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取龙息武器显示名称
        /// </summary>
        public static string GetDragonBreathName()
        {
            return L10n.T(DragonBreathNameCN, DragonBreathNameEN);
        }
        
        /// <summary>
        /// 获取龙息武器描述
        /// </summary>
        public static string GetDragonBreathDescription()
        {
            return L10n.T(DragonBreathDescCN, DragonBreathDescEN);
        }
        
        #endregion
    }
}
