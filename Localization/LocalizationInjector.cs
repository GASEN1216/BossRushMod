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
        // 快递员NPC本地化数据
        // ============================================================================
        private const string COURIER_NAME_CN = "阿稳";
        private const string COURIER_NAME_EN = "Awen";
        private const string COURIER_SERVICE_CN = "快递服务";
        private const string COURIER_SERVICE_EN = "Courier Service";
        private const string COURIER_SERVICE_UNAVAILABLE_CN = "快递服务暂未开放，敬请期待！";
        private const string COURIER_SERVICE_UNAVAILABLE_EN = "Courier service coming soon!";
        private const string COURIER_FLEE_CN = "离我远点小子！弄坏了可是要赔的";
        private const string COURIER_FLEE_EN = "Stay away kid! You break it, you pay for it!";
        private const string COURIER_CHEER_CN = "加油小子！我赌了不少钱呢";
        private const string COURIER_CHEER_EN = "Go get 'em kid! I bet a lot on you!";
        private const string COURIER_VICTORY_CN = "哈哈哈哈哈...赚大发了";
        private const string COURIER_VICTORY_EN = "Hahaha... I'm rich!";
        
        // 快递员随机对话（中英文对照）
        private static readonly string[][] COURIER_DIALOGUES = new string[][]
        {
            new string[] { "补给到了……先把伞可乐灌了，灵魂别掉地上。", "Supplies arrived... drink your Umbrella Cola first, don't let your soul drop." },
            new string[] { "这地方路况真差，比拎着XO钥匙去洗脚房还折磨。", "The roads here are terrible, worse than carrying XO keys to a foot spa." },
            new string[] { "别盯着我背包看，都是J-Lab登记过的，少一件杰夫要开会。", "Stop staring at my backpack, everything's registered with J-Lab. Jeff will call a meeting if anything's missing." },
            new string[] { "Boss也得排队，先去祭坛交羽毛，图腾按流程发。", "Even bosses have to queue. Go to the altar with feathers first, totems are distributed by procedure." },
            new string[] { "哎，这里谁点了'急件'？紫色空间能量都溢出来了。", "Hey, who ordered 'express delivery' here? Purple space energy is overflowing." },
            new string[] { "星球都快崩了还要准点，J-Lab的KPI不讲情面。", "The planet's about to collapse and we still need to be on time. J-Lab's KPIs show no mercy." },
            new string[] { "你要是能活到下一波，我给你盖个章，再塞你一瓶'有糖的'——有灵魂那种。", "If you survive the next wave, I'll stamp your card and slip you a 'sugared' one - the kind with soul." },
            new string[] { "别吵，听见没？那边在打碟……蓝皮人可能又在看热闹。", "Quiet, hear that? Someone's DJing over there... the blue guys are probably watching again." },
            new string[] { "我这把年纪了还在跑单，外星水熊虫母舰来了都得排队签收。", "At my age still running deliveries. Even alien tardigrade motherships have to queue for pickup." },
            new string[] { "箱子里是什么？浓缩浆质、绷带，还有一张'无糖可乐慎用'的说明。", "What's in the box? Concentrated plasma, bandages, and a 'use sugar-free cola with caution' note." },
            new string[] { "你要投诉？可以，去找蓝皮人，他一个响指就能把你的工单传送走。", "Want to complain? Sure, find the blue guy. One snap and he'll teleport your ticket away." },
            new string[] { "路线规划又被风暴改了……行，绕开机器蜘蛛，走那条最紫的。", "Route changed by the storm again... fine, avoid the mech spiders, take the most purple path." },
            new string[] { "我不怕Boss，我怕紫毒把快递标签腐蚀了——到时候谁也别想对账。", "I'm not afraid of bosses. I'm afraid the purple poison will corrode the delivery labels - then no one can reconcile accounts." },
            new string[] { "签收方式：按爪印、按羽毛、或者交一块蓝色方块当押金。", "Sign for delivery: paw print, feather, or leave a blue cube as deposit." },
            new string[] { "别跟我讲热血，我只认单号、撤离路线，以及'有糖才有灵魂'。", "Don't talk passion to me. I only care about order numbers, evacuation routes, and 'sugar means soul'." },
            new string[] { "看到那只到处乱创的火龙了吗？", "See that fire dragon causing chaos everywhere?" },
            new string[] { "嗯...火龙怕毒，哪天毒死它", "Hmm... fire dragons fear poison. Maybe poison it someday." },
            new string[] { "你知道火龙也怕冰吗？我有一次都把它打坠机了哈哈哈哈", "Did you know fire dragons also fear ice? I once made it crash land hahaha" },
            new string[] { "这该死的火龙把我的快递都创飞了", "That damn fire dragon knocked all my deliveries flying" },
            new string[] { "那头火龙在叽里咕噜的时候最好跑远点", "When that fire dragon starts gurgling, you better run far away" }
        };
        
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
                InjectCourierNPCLocalization();
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
        /// 注入快递员NPC本地化
        /// </summary>
        public static void InjectCourierNPCLocalization()
        {
            // 快递员名称
            string courierName = L10n.T(COURIER_NAME_CN, COURIER_NAME_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierName", courierName);
            LocalizationHelper.InjectLocalization(COURIER_NAME_CN, courierName);
            
            // 快递服务选项
            string courierService = L10n.T(COURIER_SERVICE_CN, COURIER_SERVICE_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierService", courierService);
            LocalizationHelper.InjectLocalization(COURIER_SERVICE_CN, courierService);
            
            // 快递服务暂未开放提示
            string serviceUnavailable = L10n.T(COURIER_SERVICE_UNAVAILABLE_CN, COURIER_SERVICE_UNAVAILABLE_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierServiceUnavailable", serviceUnavailable);
            
            // 快递员气泡对话
            string fleeDialogue = L10n.T(COURIER_FLEE_CN, COURIER_FLEE_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierFlee", fleeDialogue);
            
            string cheerDialogue = L10n.T(COURIER_CHEER_CN, COURIER_CHEER_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierCheer", cheerDialogue);
            
            string victoryDialogue = L10n.T(COURIER_VICTORY_CN, COURIER_VICTORY_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierVictory", victoryDialogue);
            
            // 快递员随机对话（使用索引键）
            for (int i = 0; i < COURIER_DIALOGUES.Length; i++)
            {
                string dialogue = L10n.T(COURIER_DIALOGUES[i][0], COURIER_DIALOGUES[i][1]);
                LocalizationHelper.InjectLocalization("BossRush_CourierDialogue_" + i, dialogue);
            }
            
            ModBehaviour.DevLog("[LocalizationInjector] 快递员NPC本地化注入完成");
        }
        
        /// <summary>
        /// 获取快递员随机对话数量
        /// </summary>
        public static int GetCourierDialogueCount()
        {
            return COURIER_DIALOGUES.Length;
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
                
                // 快递员NPC
                { "BossRush_CourierService", L10n.T("快递服务", "Courier Service") },
                
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
