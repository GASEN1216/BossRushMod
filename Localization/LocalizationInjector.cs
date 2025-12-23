// ============================================================================
// LocalizationInjector.cs - 统一本地化注入器
// ============================================================================
// 模块说明：
//   集中管理所有 BossRush 模组的本地化数据和注入逻辑
//   - 船票本地化
//   - 生日蛋糕本地化
//   - UI/路牌/难度选项本地化
//   - 地图名称本地化
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 统一本地化注入器
    /// </summary>
    public static class LocalizationInjector
    {
        // ============================================================================
        // 船票本地化数据
        // ============================================================================
        private const string TICKET_NAME_CN = "Boss Rush船票";
        private const string TICKET_NAME_EN = "Boss Rush Ticket";
        private const string TICKET_DESC_CN = "开启Boss Rush的凭证，九死一生，一旦倒在那，掉落的东西会被立马收走，一件不剩。但是裸体进入可白手起家！";
        private const string TICKET_DESC_EN = "A ticket to enter Boss Rush. High risk, high reward - if you fall, all your loot will be taken. Enter naked for Rags to Riches mode!";
        
        // ============================================================================
        // 生日蛋糕本地化数据
        // ============================================================================
        private const string CAKE_NAME_CN = "生日蛋糕";
        private const string CAKE_NAME_EN = "Birthday Cake";
        private const string CAKE_DESC_CN = "祝你永远开开心心快快乐乐！----来自小猪鲨的祝福";
        private const string CAKE_DESC_EN = "May you always be happy! ----Blessings from Little Pig Shark";
        
        // ============================================================================
        // 龙裔遗族Boss本地化数据
        // ============================================================================
        private const string DRAGON_DESCENDANT_NAME_CN = "龙裔遗族";
        private const string DRAGON_DESCENDANT_NAME_EN = "Dragon Descendant";
        private const string DRAGON_DESCENDANT_RESURRECTION_CN = "我...命不该绝！";
        private const string DRAGON_DESCENDANT_RESURRECTION_EN = "I...shall not perish!";
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 注入所有本地化（主入口）
        /// </summary>
        public static void InjectAll(int ticketTypeId = 0, int cakeTypeId = 0)
        {
            try
            {
                InjectTicketLocalization(ticketTypeId);
                InjectCakeLocalization(cakeTypeId);
                InjectDragonDescendantLocalization();
                InjectUILocalization();
                InjectMapNameLocalizations();
                EquipmentLocalization.InjectAllEquipmentLocalizations();
                ModBehaviour.DevLog("[LocalizationInjector] 所有本地化注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LocalizationInjector] 注入失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 注入船票本地化
        /// </summary>
        public static void InjectTicketLocalization(int typeId)
        {
            string displayName = L10n.T(TICKET_NAME_CN, TICKET_NAME_EN);
            string description = L10n.T(TICKET_DESC_CN, TICKET_DESC_EN);
            
            // 注入中英文键
            LocalizationHelper.InjectLocalization(TICKET_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(TICKET_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization("BossRush_Ticket", displayName);
            
            LocalizationHelper.InjectLocalization(TICKET_NAME_CN + "_Desc", description);
            LocalizationHelper.InjectLocalization(TICKET_NAME_EN + "_Desc", description);
            LocalizationHelper.InjectLocalization("BossRush_Ticket_Desc", description);
            
            // 注入物品 ID 键
            if (typeId > 0)
            {
                string itemKey = "Item_" + typeId;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
            }
        }
        
        /// <summary>
        /// 注入生日蛋糕本地化
        /// </summary>
        public static void InjectCakeLocalization(int typeId)
        {
            string displayName = L10n.T(CAKE_NAME_CN, CAKE_NAME_EN);
            string description = L10n.T(CAKE_DESC_CN, CAKE_DESC_EN);
            
            // 注入中英文键
            LocalizationHelper.InjectLocalization(CAKE_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(CAKE_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization("BossRush_BirthdayCake", displayName);
            
            LocalizationHelper.InjectLocalization(CAKE_NAME_CN + "_Desc", description);
            LocalizationHelper.InjectLocalization(CAKE_NAME_EN + "_Desc", description);
            LocalizationHelper.InjectLocalization("BossRush_BirthdayCake_Desc", description);
            
            // 注入物品 ID 键
            if (typeId > 0)
            {
                string itemKey = "Item_" + typeId;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
            }
        }
        
        /// <summary>
        /// 注入龙裔遗族Boss本地化
        /// </summary>
        public static void InjectDragonDescendantLocalization()
        {
            string displayName = L10n.T(DRAGON_DESCENDANT_NAME_CN, DRAGON_DESCENDANT_NAME_EN);
            string resurrection = L10n.T(DRAGON_DESCENDANT_RESURRECTION_CN, DRAGON_DESCENDANT_RESURRECTION_EN);
            
            // 注入Boss名称
            LocalizationHelper.InjectLocalization(DragonDescendantConfig.BOSS_NAME_KEY, displayName);
            LocalizationHelper.InjectLocalization(DRAGON_DESCENDANT_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(DRAGON_DESCENDANT_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization("Characters_DragonDescendant", displayName);
            
            // 注入复活对话
            LocalizationHelper.InjectLocalization("DragonDescendant_Resurrection", resurrection);
            LocalizationHelper.InjectLocalization(DRAGON_DESCENDANT_RESURRECTION_CN, resurrection);
            LocalizationHelper.InjectLocalization(DRAGON_DESCENDANT_RESURRECTION_EN, resurrection);
            
            ModBehaviour.DevLog("[LocalizationInjector] 龙裔遗族本地化注入完成");
        }

        /// <summary>
        /// 注入 UI 本地化（难度选项、路牌、传送等）
        /// </summary>
        public static void InjectUILocalization()
        {
            var localizations = new Dictionary<string, string>
            {
                // 基础键
                { "开始第一波", "开始第一波" },
                { "BossRush", "Boss Rush" },
                { "BossRush_StartFirstWave", L10n.T("开始第一波", "Start First Wave") },
                
                // 难度选项（带颜色）
                { "弹指可灭", "弹指可灭" },
                { "有点意思", "有点意思" },
                { "无间炼狱", "无间炼狱" },
                { "测试", "测试" },
                { "BossRush_Easy", L10n.T("<color=#00FF00>弹指可灭</color>", "<color=#00FF00>Easy Mode</color>") },
                { "BossRush_Hard", L10n.T("<color=#FFA500>有点意思</color>", "<color=#FFA500>Hard Mode</color>") },
                { "BossRush_InfiniteHell", L10n.T("<color=#FF0000>无间炼狱</color>", "<color=#FF0000>Infinite Hell</color>") },
                
                // 路牌状态
                { "BossRush_Sign_Cheer", L10n.T("<color=#FFD700>加油！！！</color>", "<color=#FFD700>Go! Go! Go!</color>") },
                { "BossRush_Sign_Entry", L10n.T("<color=#FFD700>哎哟~你干嘛~</color>", "<color=#FFD700>Hey~ What are you doing~</color>") },
                { "BossRush_Sign_NextWave", L10n.T("<color=#FFD700>冲！（下一波）</color>", "<color=#FFD700>Charge! (Next Wave)</color>") },
                { "BossRush_Sign_Victory", L10n.T("<color=#FFD700>君王凯旋归来，拿取属于王的荣耀！</color>", "<color=#FFD700>The King Returns Triumphant, Claim Your Glory!</color>") },
                
                // 搬运选项
                { "BossRush_Carry_Up", L10n.T("搬起", "Pick Up") },
                { "BossRush_Carry_Down", L10n.T("放下", "Put Down") },
                
                // 传送
                { "BossRush_Teleport", L10n.T("<color=#00BFFF>传送</color>", "<color=#00BFFF>Teleport</color>") },
                { "BossRush_ReturnToSpawn", L10n.T("按E键返回出生点！", "Press E to return to spawn!") },
                { "传送", "<color=#00BFFF>传送</color>" },
                { "BossRush_InitEntry", "<color=#FFD700>哎哟~你干嘛~</color>" },
                
                // 清理箱子选项
                { "BossRush_ClearAllLootboxes", L10n.T("清空所有箱子", "Clear All Lootboxes") },
                { "BossRush_ClearEmptyLootboxes", L10n.T("清空所有空箱子", "Clear Empty Lootboxes") },
                
                // Mode D 选项
                { "BossRush_ModeD_NextWave", L10n.T("冲！（下一波）", "Charge! (Next Wave)") },
                
                // 弹药和维修
                { "BossRush_AmmoShop", L10n.T("弹药商店", "Ammo Shop") },
                { "BossRush_AmmoRefill", "加油站" },
                { "BossRush_Repair", L10n.T("维修", "Repair") },
                
                // 删除和下一波
                { "BossRush_Delete", L10n.T("删除", "Delete") },
                { "BossRush_NextWave", L10n.T("下一波", "Next Wave") },
                
                // 旧版中文键（兼容）
                { "加油！", "<color=#FFD700>加油！</color>" },
                { "哎哟~你干嘛~", "<color=#FFD700>哎哟~你干嘛~</color>" },
                { "冲！（下一波）", "<color=#FFD700>冲！（下一波）</color>" }
            };
            
            LocalizationHelper.InjectLocalizations(localizations);
        }
        
        /// <summary>
        /// 注入地图名称本地化（官方未提供本地化的地图）
        /// </summary>
        public static void InjectMapNameLocalizations()
        {
            try
            {
                // 注入零度挑战的本地化（官方未提供）
                string zeroChallengeDisplayName = L10n.T("零度挑战", "Zero Challenge");
                LocalizationHelper.InjectLocalization("Level_ChallengeSnow", zeroChallengeDisplayName);
                ModBehaviour.DevLog("[LocalizationInjector] 已注入地图名称本地化: Level_ChallengeSnow -> " + zeroChallengeDisplayName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[LocalizationInjector] 注入地图名称本地化失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 获取本地化文本的辅助方法
        // ============================================================================
        
        /// <summary>
        /// 获取船票显示名称
        /// </summary>
        public static string GetTicketName()
        {
            return L10n.T(TICKET_NAME_CN, TICKET_NAME_EN);
        }
        
        /// <summary>
        /// 获取船票描述
        /// </summary>
        public static string GetTicketDescription()
        {
            return L10n.T(TICKET_DESC_CN, TICKET_DESC_EN);
        }
        
        /// <summary>
        /// 获取生日蛋糕显示名称
        /// </summary>
        public static string GetCakeName()
        {
            return L10n.T(CAKE_NAME_CN, CAKE_NAME_EN);
        }
        
        /// <summary>
        /// 获取生日蛋糕描述
        /// </summary>
        public static string GetCakeDescription()
        {
            return L10n.T(CAKE_DESC_CN, CAKE_DESC_EN);
        }
    }
}
