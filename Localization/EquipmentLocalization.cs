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
        
        #region 龙王套装本地化数据
        
        // 龙王之冕（龙王专属头盔）
        private static readonly string DragonKingHelmNameCN = "龙王之冕";
        private static readonly string DragonKingHelmNameEN = "Dragon King's Crown";
        private static readonly string DragonKingHelmDescCN = "真正的龙王遗冠，其上镌刻着远古龙族的王权印记。当你戴上它的那一刻，会感受到一股毁灭一切的力量在血脉中觉醒。唯有配得上这份力量的人，才能承受它的重量。";
        private static readonly string DragonKingHelmDescEN = "The true crown of the Dragon King, etched with the royal sigils of an ancient dragon lineage. The moment you don it, you feel an overwhelming power awaken in your veins. Only those worthy of this power can bear its weight.";
        
        // 龙王鳞铠（龙王专属护甲）
        private static readonly string DragonKingArmorNameCN = "龙王鳞铠";
        private static readonly string DragonKingArmorNameEN = "Dragon King's Scale Mail";
        private static readonly string DragonKingArmorDescCN = "由龙王心脏区域的核心鳞甲锻造而成，据说仍有微弱的心跳声在其中回响。与龙王之冕一同穿戴时会产生神秘的共鸣，获得龙王的庇护，但也将被烙上龙族的灵魂印记，永远无法逃离火焰的宿命。";
        private static readonly string DragonKingArmorDescEN = "Forged from the core scales near the Dragon King's heart, a faint heartbeat still echoes within. When worn with the Dragon King's Crown, a mysterious resonance occurs, granting the Dragon King's protection, but forever marking you with the dragon's soul, bound to the fate of flames.";
        
        #endregion
        
        #region 龙息武器本地化数据
        
        // 龙息武器名称
        private static readonly string DragonBreathNameCN = "龙息";
        private static readonly string DragonBreathNameEN = "Dragon's Breath";
        
        // 龙息武器描述
        private static readonly string DragonBreathDescCN = "J-Lab实验室将赤龙的残骸与MCX相结合的完美艺术品。按下扳机的那一刻，你会明白\"生存\"和\"撤离\"之间还有第三个选项：把道路烤出来。";
        private static readonly string DragonBreathDescEN = "A masterpiece from J-Lab, fusing crimson dragon remains with the MCX. The moment you pull the trigger, you'll realize there's a third option between 'survive' and 'extract': burn your way out.";
        
        #endregion
        
        #region 龙焰灼烧Buff本地化数据
        
        // 龙焰灼烧Buff名称
        private static readonly string DragonBurnNameCN = "龙焰灼烧";
        private static readonly string DragonBurnNameEN = "Dragon Burn";
        
        // 龙焰灼烧Buff描述
        private static readonly string DragonBurnDescCN = "每秒受到最大生命值0.1%+1点真实火焰伤害，最多叠加10层，持续10秒";
        private static readonly string DragonBurnDescEN = "Takes 0.1% max HP + 1 true fire damage per second per layer, stacks up to 10, lasts 10 seconds";
        
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
                InjectDragonKingSetLocalization();  // 龙王套装
                InjectDragonDescendantLocalization();
                InjectDragonBreathWeaponLocalization();
                InjectDragonBurnBuffLocalization();
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
        /// 注入龙王套装本地化（龙王之冕 + 龙王鳞铠）
        /// </summary>
        public static void InjectDragonKingSetLocalization()
        {
            InjectDragonKingHelmLocalization(0);
            InjectDragonKingArmorLocalization(0);
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
        
        /// <summary>
        /// 注入龙王之冕本地化
        /// </summary>
        /// <param name="typeId">物品 TypeID，用于注入 Item_xxx 键（0 表示不注入）</param>
        public static void InjectDragonKingHelmLocalization(int typeId)
        {
            try
            {
                string displayName = L10n.T(DragonKingHelmNameCN, DragonKingHelmNameEN);
                string description = L10n.T(DragonKingHelmDescCN, DragonKingHelmDescEN);
                
                // 注入原始键（Unity Prefab 中 displayName 字段值）
                LocalizationHelper.InjectLocalization("dragonking_Helmet_Item", displayName);
                LocalizationHelper.InjectLocalization("dragonking_Helmet_Item_Desc", description);
                // 也注入中文键以备用
                LocalizationHelper.InjectLocalization("龙王之冕", displayName);
                LocalizationHelper.InjectLocalization("龙王之冕_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙王之冕本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙王之冕本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入龙王鳞铠本地化
        /// </summary>
        /// <param name="typeId">物品 TypeID，用于注入 Item_xxx 键（0 表示不注入）</param>
        public static void InjectDragonKingArmorLocalization(int typeId)
        {
            try
            {
                string displayName = L10n.T(DragonKingArmorNameCN, DragonKingArmorNameEN);
                string description = L10n.T(DragonKingArmorDescCN, DragonKingArmorDescEN);
                
                // 注入原始键（Unity Prefab 中 displayName 字段值）
                LocalizationHelper.InjectLocalization("dragonking_Armor_Item", displayName);
                LocalizationHelper.InjectLocalization("dragonking_Armor_Item_Desc", description);
                // 也注入中文键以备用
                LocalizationHelper.InjectLocalization("龙王鳞铠", displayName);
                LocalizationHelper.InjectLocalization("龙王鳞铠_Desc", description);
                
                // 注入物品 ID 键
                if (typeId > 0)
                {
                    string itemKey = "Item_" + typeId;
                    LocalizationHelper.InjectLocalization(itemKey, displayName);
                    LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
                }
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙王鳞铠本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙王鳞铠本地化失败: " + e.Message);
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
        
        /// <summary>
        /// 获取龙王之冕显示名称
        /// </summary>
        public static string GetDragonKingHelmName()
        {
            return L10n.T(DragonKingHelmNameCN, DragonKingHelmNameEN);
        }
        
        /// <summary>
        /// 获取龙王之冕描述
        /// </summary>
        public static string GetDragonKingHelmDescription()
        {
            return L10n.T(DragonKingHelmDescCN, DragonKingHelmDescEN);
        }
        
        /// <summary>
        /// 获取龙王鳞铠显示名称
        /// </summary>
        public static string GetDragonKingArmorName()
        {
            return L10n.T(DragonKingArmorNameCN, DragonKingArmorNameEN);
        }
        
        /// <summary>
        /// 获取龙王鳞铠描述
        /// </summary>
        public static string GetDragonKingArmorDescription()
        {
            return L10n.T(DragonKingArmorDescCN, DragonKingArmorDescEN);
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
        
        #region 龙焰灼烧Buff本地化方法
        
        /// <summary>
        /// 注入龙焰灼烧Buff本地化
        /// </summary>
        public static void InjectDragonBurnBuffLocalization()
        {
            try
            {
                string displayName = L10n.T(DragonBurnNameCN, DragonBurnNameEN);
                string description = L10n.T(DragonBurnDescCN, DragonBurnDescEN);
                
                // 注入 DragonBreathConfig 中定义的本地化键
                LocalizationHelper.InjectLocalization(DragonBreathConfig.LOC_KEY_BUFF_NAME, displayName);
                LocalizationHelper.InjectLocalization(DragonBreathConfig.LOC_KEY_BUFF_DESC, description);
                
                // 注入原始键（Buff 的 displayName 字段值）
                LocalizationHelper.InjectLocalization("龙焰灼烧", displayName);
                LocalizationHelper.InjectLocalization("龙焰灼烧_Desc", description);
                
                // 注入 Buff ID 键（BuffID = 500006）
                int buffId = DragonBreathConfig.BUFF_ID;
                string buffKey = "Buff_" + buffId;
                LocalizationHelper.InjectLocalization(buffKey, displayName);
                LocalizationHelper.InjectLocalization(buffKey + "_Desc", description);
                
                ModBehaviour.DevLog("[EquipmentLocalization] 龙焰灼烧Buff本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EquipmentLocalization] 注入龙焰灼烧Buff本地化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取龙焰灼烧Buff显示名称
        /// </summary>
        public static string GetDragonBurnName()
        {
            return L10n.T(DragonBurnNameCN, DragonBurnNameEN);
        }
        
        /// <summary>
        /// 获取龙焰灼烧Buff描述
        /// </summary>
        public static string GetDragonBurnDescription()
        {
            return L10n.T(DragonBurnDescCN, DragonBurnDescEN);
        }
        
        #endregion
    }
}
