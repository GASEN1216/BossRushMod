using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public static partial class LocalizationInjector
    {
        /// <summary>
        /// 注入快递员NPC本地化
        /// </summary>
        public static void InjectCourierNPCLocalization()
        {
            // 快递员名称
            string courierName = L10n.T(COURIER_NAME_CN, COURIER_NAME_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierName", courierName);
            LocalizationHelper.InjectLocalization("BossRush_CourierNPC", courierName);  // 快递员主交互选项名称
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

            // 快递容器标题（UI显示在最上面的名字）
            string courierContainerTitle = L10n.T(COURIER_CONTAINER_TITLE_CN, COURIER_CONTAINER_TITLE_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierService_ContainerTitle", courierContainerTitle);

            // 快递服务功能本地化
            string sendText = L10n.T(COURIER_SERVICE_SEND_CN, COURIER_SERVICE_SEND_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierService_Send", sendText);

            string feeText = L10n.T(COURIER_SERVICE_FEE_CN, COURIER_SERVICE_FEE_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierService_Fee", feeText);

            string goodbyeText = L10n.T(COURIER_SERVICE_GOODBYE_CN, COURIER_SERVICE_GOODBYE_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierService_Goodbye", goodbyeText);

            string insufficientText = L10n.T(COURIER_SERVICE_INSUFFICIENT_CN, COURIER_SERVICE_INSUFFICIENT_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierService_InsufficientFunds", insufficientText);

            string emptyText = L10n.T(COURIER_SERVICE_EMPTY_CN, COURIER_SERVICE_EMPTY_EN);
            LocalizationHelper.InjectLocalization("BossRush_CourierService_Empty", emptyText);

            // 快递员随机对话（使用索引键）
            for (int i = 0; i < COURIER_DIALOGUES.Length; i++)
            {
                string dialogue = L10n.T(COURIER_DIALOGUES[i][0], COURIER_DIALOGUES[i][1]);
                LocalizationHelper.InjectLocalization("BossRush_CourierDialogue_" + i, dialogue);
            }

            // 寄存服务本地化
            string storageService = L10n.T(STORAGE_SERVICE_CN, STORAGE_SERVICE_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageService", storageService);
            LocalizationHelper.InjectLocalization(STORAGE_SERVICE_CN, storageService);

            // 寄存容器标题（UI显示在最上面的名字）
            string storageContainerTitle = L10n.T(STORAGE_CONTAINER_TITLE_CN, STORAGE_CONTAINER_TITLE_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageService_ContainerTitle", storageContainerTitle);

            string retrieveAll = L10n.T(STORAGE_SERVICE_RETRIEVE_ALL_CN, STORAGE_SERVICE_RETRIEVE_ALL_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageService_RetrieveAll", retrieveAll);

            string storageInsufficient = L10n.T(STORAGE_SERVICE_INSUFFICIENT_CN, STORAGE_SERVICE_INSUFFICIENT_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageService_InsufficientFunds", storageInsufficient);

            string storageRetrieved = L10n.T(STORAGE_SERVICE_RETRIEVED_CN, STORAGE_SERVICE_RETRIEVED_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageService_Retrieved", storageRetrieved);

            string storageEmpty = L10n.T(STORAGE_SERVICE_EMPTY_CN, STORAGE_SERVICE_EMPTY_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageService_Empty", storageEmpty);

            // 阿稳寄存服务（新版）本地化
            string depositServiceName = L10n.T(STORAGE_DEPOSIT_SERVICE_NAME_CN, STORAGE_DEPOSIT_SERVICE_NAME_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_ServiceName", depositServiceName);

            string depositShopName = L10n.T(STORAGE_DEPOSIT_SHOP_NAME_CN, STORAGE_DEPOSIT_SHOP_NAME_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_ShopName", depositShopName);

            string depositDeposited = L10n.T(STORAGE_DEPOSIT_DEPOSITED_CN, STORAGE_DEPOSIT_DEPOSITED_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_Deposited", depositDeposited);

            string depositRetrieved = L10n.T(STORAGE_DEPOSIT_RETRIEVED_CN, STORAGE_DEPOSIT_RETRIEVED_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_Retrieved", depositRetrieved);

            string depositInventoryFull = L10n.T(STORAGE_DEPOSIT_INVENTORY_FULL_CN, STORAGE_DEPOSIT_INVENTORY_FULL_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_InventoryFull", depositInventoryFull);

            string depositFarewell = L10n.T(STORAGE_DEPOSIT_FAREWELL_CN, STORAGE_DEPOSIT_FAREWELL_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_Farewell", depositFarewell);

            // 寄存按钮文字
            string depositButton = L10n.T(STORAGE_DEPOSIT_BUTTON_CN, STORAGE_DEPOSIT_BUTTON_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_Button", depositButton);

            // "全部取出"按钮文字
            string retrieveAllButton = L10n.T(STORAGE_DEPOSIT_RETRIEVE_ALL_CN, STORAGE_DEPOSIT_RETRIEVE_ALL_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_RetrieveAll", retrieveAllButton);

            // "全部丢弃"按钮文字
            string discardAllButton = L10n.T(STORAGE_DEPOSIT_DISCARD_ALL_CN, STORAGE_DEPOSIT_DISCARD_ALL_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_DiscardAll", discardAllButton);

            // "已丢弃所有寄存物品"通知文字
            string discardedNotification = L10n.T(STORAGE_DEPOSIT_DISCARDED_CN, STORAGE_DEPOSIT_DISCARDED_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_Discarded", discardedNotification);

            // "物品未解锁"提示文字
            string itemNotUnlocked = L10n.T(STORAGE_DEPOSIT_ITEM_NOT_UNLOCKED_CN, STORAGE_DEPOSIT_ITEM_NOT_UNLOCKED_EN);
            LocalizationHelper.InjectLocalization("BossRush_StorageDeposit_ItemNotUnlocked", itemNotUnlocked);

            // 快递员首次见面对话（大对话系统使用）
            for (int i = 0; i < COURIER_FIRST_MEET_DIALOGUES.Length; i++)
            {
                string dialogue = L10n.T(COURIER_FIRST_MEET_DIALOGUES[i][0], COURIER_FIRST_MEET_DIALOGUES[i][1]);
                LocalizationHelper.InjectLocalization("BossRush_CourierFirstMeet_" + i, dialogue);
            }

            // "给你"气泡文字
            string giveText = L10n.T("给你", "Here you go");
            LocalizationHelper.InjectLocalization("BossRush_CourierGive", giveText);

            ModBehaviour.DevLog("[LocalizationInjector] 快递员NPC本地化注入完成");
        }

        /// <summary>
        /// 注入所有NPC共用交互本地化
        /// </summary>
        public static void InjectCommonNPCLocalization()
        {
            // 聊天选项
            string chatOption = L10n.T("聊天", "Chat");
            LocalizationHelper.InjectLocalization("BossRush_Chat", chatOption);
            // 兼容二次本地化：把最终显示文本也注册为自映射键，避免显示 *Chat* / *聊天*
            LocalizationHelper.InjectLocalization("聊天", "聊天");
            LocalizationHelper.InjectLocalization("Chat", "Chat");
            LocalizationHelper.InjectLocalization(chatOption, chatOption);

            // 赠送礼物选项
            string giftOption = L10n.T("赠送礼物", "Give Gift");
            LocalizationHelper.InjectLocalization("BossRush_GiveGift", giftOption);
            LocalizationHelper.InjectLocalization("赠送礼物", "赠送礼物");
            LocalizationHelper.InjectLocalization("Give Gift", "Give Gift");
            LocalizationHelper.InjectLocalization(giftOption, giftOption);

            // 通用商店选项
            string shopOption = L10n.T("商店", "Shop");
            LocalizationHelper.InjectLocalization("BossRush_NPCShop", shopOption);
            LocalizationHelper.InjectLocalization("商店", "商店");
            LocalizationHelper.InjectLocalization("Shop", "Shop");
            LocalizationHelper.InjectLocalization(shopOption, shopOption);

            ModBehaviour.DevLog("[LocalizationInjector] 通用NPC交互本地化注入完成");
        }

        /// <summary>
        /// 注入哥布林NPC本地化
        /// </summary>
        public static void InjectGoblinNPCLocalization()
        {
            // 哥布林名称
            string goblinName = L10n.T(GOBLIN_NAME_CN, GOBLIN_NAME_EN);
            LocalizationHelper.InjectLocalization("BossRush_GoblinName", goblinName);
            LocalizationHelper.InjectLocalization(GOBLIN_NAME_CN, goblinName);

            // 哥布林交谈选项
            string goblinTalk = L10n.T(GOBLIN_TALK_CN, GOBLIN_TALK_EN);
            LocalizationHelper.InjectLocalization("BossRush_GoblinTalk", goblinTalk);

            // 哥布林问候语
            string goblinGreeting = L10n.T(GOBLIN_GREETING_CN, GOBLIN_GREETING_EN);
            LocalizationHelper.InjectLocalization("BossRush_GoblinGreeting", goblinGreeting);

            // 重铸服务选项
            string reforgeService = L10n.T(REFORGE_SERVICE_CN, REFORGE_SERVICE_EN);
            LocalizationHelper.InjectLocalization("BossRush_Reforge", reforgeService);
            LocalizationHelper.InjectLocalization(REFORGE_SERVICE_CN, reforgeService);

            // 重铸UI标题
            string reforgeTitle = L10n.T(REFORGE_TITLE_CN, REFORGE_TITLE_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeTitle", reforgeTitle);

            // 重铸说明
            string reforgeDesc = L10n.T(REFORGE_DESC_CN, REFORGE_DESC_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeDesc", reforgeDesc);

            // 未选择物品提示
            string noItemSelected = L10n.T(REFORGE_NO_ITEM_SELECTED_CN, REFORGE_NO_ITEM_SELECTED_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeNoItemSelected", noItemSelected);

            // 已选择
            string selected = L10n.T(REFORGE_SELECTED_CN, REFORGE_SELECTED_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeSelected", selected);

            // 属性数量
            string modifiers = L10n.T(REFORGE_MODIFIERS_CN, REFORGE_MODIFIERS_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeModifiers", modifiers);

            // 投入金额
            string cost = L10n.T(REFORGE_COST_CN, REFORGE_COST_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeCost", cost);

            // 重铸按钮
            string reforgeButton = L10n.T(REFORGE_BUTTON_CN, REFORGE_BUTTON_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeButton", reforgeButton);

            // 关闭按钮
            string closeButton = L10n.T(REFORGE_CLOSE_CN, REFORGE_CLOSE_EN);
            LocalizationHelper.InjectLocalization("BossRush_Close", closeButton);

            // 重铸成功
            string success = L10n.T(REFORGE_SUCCESS_CN, REFORGE_SUCCESS_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeSuccess", success);

            // 请先选择装备
            string selectFirst = L10n.T(REFORGE_SELECT_FIRST_CN, REFORGE_SELECT_FIRST_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeSelectFirst", selectFirst);

            // 金钱不足
            string notEnoughMoney = L10n.T(REFORGE_NOT_ENOUGH_MONEY_CN, REFORGE_NOT_ENOUGH_MONEY_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeNotEnoughMoney", notEnoughMoney);

            // 没有可重铸的装备
            string noEquipment = L10n.T(REFORGE_NO_EQUIPMENT_CN, REFORGE_NO_EQUIPMENT_EN);
            LocalizationHelper.InjectLocalization("BossRush_ReforgeNoEquipment", noEquipment);

            // 选择物品（左栏标题）
            string selectItem = L10n.T("选择物品", "Select Item");
            LocalizationHelper.InjectLocalization("BossRush_ReforgeSelectItem", selectItem);

            // 概率标签
            string probability = L10n.T("重铸概率", "Reforge Probability");
            LocalizationHelper.InjectLocalization("BossRush_ReforgeProbability", probability);

            // 商店选项
            string shopOption = L10n.T("商店", "Shop");
            LocalizationHelper.InjectLocalization("BossRush_GoblinShop", shopOption);

            // 哥布林主交互选项（交互组名称）
            string goblinNPC = L10n.T("叮当", "Dingdang");
            LocalizationHelper.InjectLocalization("BossRush_GoblinNPC", goblinNPC);

            // 哥布林5级故事对话（大对话系统）
            for (int i = 0; i < GOBLIN_STORY_LEVEL5_DIALOGUES.Length; i++)
            {
                string dialogue = L10n.T(GOBLIN_STORY_LEVEL5_DIALOGUES[i][0], GOBLIN_STORY_LEVEL5_DIALOGUES[i][1]);
                LocalizationHelper.InjectLocalization("BossRush_GoblinStory5_" + i, dialogue);
            }

            // 哥布林10级故事对话（大对话系统）
            for (int i = 0; i < GOBLIN_STORY_LEVEL10_DIALOGUES.Length; i++)
            {
                string dialogue = L10n.T(GOBLIN_STORY_LEVEL10_DIALOGUES[i][0], GOBLIN_STORY_LEVEL10_DIALOGUES[i][1]);
                LocalizationHelper.InjectLocalization("BossRush_GoblinStory10_" + i, dialogue);
            }

            // ============================================================================
            // 礼物容器UI本地化（哥布林专属）
            // ============================================================================

            // 哥布林礼物容器标题
            string goblinGiftContainerTitle = L10n.T("赠送礼物给叮当", "Give Gift to Dingdang");
            LocalizationHelper.InjectLocalization("BossRush_GoblinGift_ContainerTitle", goblinGiftContainerTitle);

            // 哥布林礼物赠送按钮
            string goblinGiftButton = L10n.T("赠送", "Give");
            LocalizationHelper.InjectLocalization("BossRush_GoblinGift_GiftButton", goblinGiftButton);

            // 哥布林礼物空槽位提示
            string goblinGiftEmptySlot = L10n.T("放入礼物", "Place Gift");
            LocalizationHelper.InjectLocalization("BossRush_GoblinGift_EmptySlot", goblinGiftEmptySlot);

            // ============================================================================
            // 礼物容器UI本地化（通用默认值）
            // ============================================================================

            // 默认礼物容器标题
            string defaultGiftContainerTitle = L10n.T("赠送礼物", "Give Gift");
            LocalizationHelper.InjectLocalization("BossRush_GiftContainer_DefaultTitle", defaultGiftContainerTitle);

            // 默认礼物赠送按钮
            string defaultGiftButton = L10n.T("赠送", "Give");
            LocalizationHelper.InjectLocalization("BossRush_GiftContainer_DefaultButton", defaultGiftButton);

            // 默认礼物空槽位提示
            string defaultGiftEmptySlot = L10n.T("放入礼物", "Place Gift");
            LocalizationHelper.InjectLocalization("BossRush_GiftContainer_DefaultEmptySlot", defaultGiftEmptySlot);

            ModBehaviour.DevLog("[LocalizationInjector] 哥布林NPC本地化注入完成");
        }

        /// <summary>
        /// 注入护士NPC相关本地化
        /// </summary>
        public static void InjectNurseNPCLocalization()
        {
            // 护士名称（含大对话Actor名称键）
            string nurseName = L10n.T(NURSE_NAME_CN, NURSE_NAME_EN);
            LocalizationHelper.InjectLocalization("BossRush_NurseName", nurseName);
            LocalizationHelper.InjectLocalization("BossRush_Actor_nurse_npc_Name", nurseName);
            LocalizationHelper.InjectLocalization(NURSE_NAME_CN, nurseName);

            // 护士交互选项（聊天、治疗）
            string nurseChat = L10n.T(NURSE_CHAT_CN, NURSE_CHAT_EN);
            LocalizationHelper.InjectLocalization("BossRush_NurseChat", nurseChat);
            LocalizationHelper.InjectLocalization(nurseChat, nurseChat);

            string nurseHeal = L10n.T(NURSE_HEAL_CN, NURSE_HEAL_EN);
            LocalizationHelper.InjectLocalization("BossRush_NurseHeal", nurseHeal);
            LocalizationHelper.InjectLocalization(nurseHeal, nurseHeal);

            // 护士礼物容器UI本地化
            LocalizationHelper.InjectLocalization(
                "BossRush_NurseGift_ContainerTitle",
                L10n.T("赠送礼物给羽织", "Give Gift to Yu Zhi"));
            LocalizationHelper.InjectLocalization(
                "BossRush_NurseGift_GiftButton",
                L10n.T("赠送", "Give"));
            LocalizationHelper.InjectLocalization(
                "BossRush_NurseGift_EmptySlot",
                L10n.T("放入礼物", "Place Gift"));

            // 护士5级故事对话（大对话系统）
            for (int i = 0; i < NURSE_STORY_LEVEL5_DIALOGUES.Length; i++)
            {
                string dialogue = L10n.T(NURSE_STORY_LEVEL5_DIALOGUES[i][0], NURSE_STORY_LEVEL5_DIALOGUES[i][1]);
                LocalizationHelper.InjectLocalization("BossRush_NurseStory5_" + i, dialogue);
            }

            ModBehaviour.DevLog("[LocalizationInjector] 护士NPC本地化注入完成");
        }

        /// <summary>
        /// 获取快递员首次见面对话数量
        /// </summary>
        public static int GetCourierFirstMeetDialogueCount()
        {
            return COURIER_FIRST_MEET_DIALOGUES.Length;
        }

        /// <summary>
        /// 获取快递员首次见面对话的本地化键数组
        /// </summary>
        public static string[] GetCourierFirstMeetDialogueKeys()
        {
            string[] keys = new string[COURIER_FIRST_MEET_DIALOGUES.Length];
            for (int i = 0; i < COURIER_FIRST_MEET_DIALOGUES.Length; i++)
            {
                keys[i] = "BossRush_CourierFirstMeet_" + i;
            }
            return keys;
        }

        /// <summary>
        /// 获取快递员随机对话数量
        /// </summary>
        public static int GetCourierDialogueCount()
        {
            return COURIER_DIALOGUES.Length;
        }

        /// <summary>
        /// 获取哥布林5级故事对话的本地化键数组
        /// </summary>
        public static string[] GetGoblinStory5DialogueKeys()
        {
            string[] keys = new string[GOBLIN_STORY_LEVEL5_DIALOGUES.Length];
            for (int i = 0; i < GOBLIN_STORY_LEVEL5_DIALOGUES.Length; i++)
            {
                keys[i] = "BossRush_GoblinStory5_" + i;
            }
            return keys;
        }

        /// <summary>
        /// 获取哥布林10级故事对话的本地化键数组
        /// </summary>
        public static string[] GetGoblinStory10DialogueKeys()
        {
            string[] keys = new string[GOBLIN_STORY_LEVEL10_DIALOGUES.Length];
            for (int i = 0; i < GOBLIN_STORY_LEVEL10_DIALOGUES.Length; i++)
            {
                keys[i] = "BossRush_GoblinStory10_" + i;
            }
            return keys;
        }

        /// <summary>
        /// 获取护士5级故事对话的本地化键数组
        /// </summary>
        public static string[] GetNurseStory5DialogueKeys()
        {
            string[] keys = new string[NURSE_STORY_LEVEL5_DIALOGUES.Length];
            for (int i = 0; i < NURSE_STORY_LEVEL5_DIALOGUES.Length; i++)
            {
                keys[i] = "BossRush_NurseStory5_" + i;
            }
            return keys;
        }

        public static string[] GetNurseStory3DialogueKeys()
        {
            return BuildDynamicDialogueKeys(
                "BossRush_NurseStory3_",
                new string[][]
                {
                    new string[] { "别乱动，我先帮你把伤口处理好。", "Don't move. Let me patch your wound first." },
                    new string[] { "这些安神滴剂是我自己调的，晚上实在睡不着时再用。", "I mixed these calming drops myself. Use them only when you really can't sleep." },
                    new string[] { "别误会，我只是觉得像你这样的人，不该总是硬撑着。", "Don't get the wrong idea. I just think someone like you shouldn't keep forcing themself to endure everything." },
                    new string[] { "拿着吧，算是我这个医生的一点私心。", "Take them. Call it a small selfish favor from your doctor." }
                });
        }

        public static string[] GetNurseStory8DialogueKeys()
        {
            return BuildDynamicDialogueKeys(
                "BossRush_NurseStory8_",
                new string[][]
                {
                    new string[] { "最近外面越来越乱了，我总担心你会不会哪天回不来了。", "Things have been getting worse out there. I keep worrying that one day you might not come back." },
                    new string[] { "所以我做了这个平安护身符。它未必真能挡灾，但至少能让我安心一点。", "So I made this peace charm. It may not truly protect you, but it helps me breathe a little easier." },
                    new string[] { "如果你愿意的话，就把它带在身上。", "If you're willing, keep it with you." },
                    new string[] { "等你平安回来，再把今天发生的事讲给我听。", "When you come back safe, tell me everything that happened today." }
                });
        }

        public static string[] GetNurseStory10DialogueKeys()
        {
            return BuildDynamicDialogueKeys(
                "BossRush_NurseStory10_",
                new string[][]
                {
                    new string[] { "我以前一直以为，自己只需要留在这里，替别人处理伤口就够了。", "I used to think staying here and tending other people's wounds was all I needed to do." },
                    new string[] { "可是你一次次回来，让我开始认真去想‘以后’这种事。", "But every time you came back, I started thinking seriously about something called 'the future.'" },
                    new string[] { "我会担心你，会期待你，会在你站到门口时觉得整间医务室都亮了一点。", "I worry about you, wait for you, and every time you appear at the doorway, this whole clinic feels a little brighter." },
                    new string[] { "如果你愿意的话……以后也让我继续这样等你吧。", "If you're willing... let me keep waiting for you like this from now on." },
                    new string[] { "这一次，我说的不是医生对病人，而是羽织对你。", "This time, I'm not speaking as a doctor to a patient. I'm speaking as Yu Zhi to you." }
                });
        }

        private static string[] BuildDynamicDialogueKeys(string prefix, string[][] dialogues)
        {
            string[] keys = new string[dialogues.Length];
            for (int i = 0; i < dialogues.Length; i++)
            {
                string key = prefix + i;
                keys[i] = key;
                LocalizationHelper.InjectLocalization(key, L10n.T(dialogues[i][0], dialogues[i][1]));
            }

            return keys;
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

                // 垃圾桶交互
                { "BossRush_TrashCan", L10n.T("垃圾桶", "Trash Can") },

                // Mode D 选项
                { "BossRush_ModeD_NextWave", L10n.T("冲！（下一波）", "Charge! (Next Wave)") },

                // 弹药和维修
                { "BossRush_AmmoShop", L10n.T("加油站", "Ammo Shop") },
                { "BossRush_AmmoRefill", "加油站" },
                { "BossRush_Repair", L10n.T("维修", "Repair") },

                // 删除和下一波
                { "BossRush_Delete", L10n.T("删除", "Delete") },
                { "BossRush_NextWave", L10n.T("下一波", "Next Wave") },

                // 快递员NPC
                { "BossRush_CourierService", L10n.T("快递服务", "Courier Service") },
                { "BossRush_CourierPaidLootSweep", L10n.T("扫箱", "Sweep Loot") },
                { "BossRush_CourierPaidLootSweep_ResultTitle", L10n.T("阿稳代收箱", "Awen Sweep Crate") },
                { "BossRush_CourierPaidLootSweep_Partial", L10n.T("阿稳这趟只扫了一部分，先看结果箱。", "Awen only cleared part of the field. Check the result crate first.") },

                // Mode E 神秘商人分类商店
                { "BossRush_ModeE_Shop_Title", L10n.T("神秘商人", "Mysterious Merchant") },
                { "BossRush_ModeE_SummonPet", L10n.T("召唤煤球", "Summon Coalball") },
                { "BossRush_ModeE_Shop_Gun", L10n.T("枪械", "Guns") },
                { "BossRush_ModeE_Shop_Melee", L10n.T("近战武器", "Melee Weapons") },
                { "BossRush_ModeE_Shop_Accessory", L10n.T("配件模组", "Accessories & Mods") },
                { "BossRush_ModeE_Shop_Bullet", L10n.T("子弹", "Ammo") },
                { "BossRush_ModeE_Shop_Helmat", L10n.T("头盔", "Helmets") },
                { "BossRush_ModeE_Shop_Armor", L10n.T("护甲", "Armor") },
                { "BossRush_ModeE_Shop_Backpack", L10n.T("背包", "Backpacks") },
                { "BossRush_ModeE_Shop_Totem", L10n.T("图腾", "Totems") },
                { "BossRush_ModeE_Shop_Mask", L10n.T("面具/耳机", "Masks & Headsets") },
                { "BossRush_ModeE_Shop_Medical", L10n.T("医疗品", "Medical") },
                { "BossRush_ModeE_Shop_Food", L10n.T("食物", "Food") },
                { "BossRush_ModeE_Shop_Bait", L10n.T("诱饵", "Bait") },
                { "BossRush_ModeE_Shop_Other", L10n.T("其他", "Other") },

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

        /// <summary>
        /// 获取 Wiki Book 显示名称
        /// </summary>
        public static string GetWikiBookName()
        {
            return L10n.T(WIKI_BOOK_NAME_CN, WIKI_BOOK_NAME_EN);
        }

        /// <summary>
        /// 获取 Wiki Book 描述
        /// </summary>
        public static string GetWikiBookDescription()
        {
            return L10n.T(WIKI_BOOK_DESC_CN, WIKI_BOOK_DESC_EN);
        }

        /// <summary>
        /// 注入冷淬液物品本地化
        /// </summary>
        public static void InjectColdQuenchFluidLocalization()
        {
            string displayName = ColdQuenchFluidConfig.GetDisplayName();
            string description = ColdQuenchFluidConfig.GetDescription();

            // 注入中英文键
            LocalizationHelper.InjectLocalization(ColdQuenchFluidConfig.DISPLAY_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(ColdQuenchFluidConfig.DISPLAY_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization(ColdQuenchFluidConfig.LOC_KEY_DISPLAY, displayName);

            LocalizationHelper.InjectLocalization(ColdQuenchFluidConfig.DISPLAY_NAME_CN + "_Desc", description);
            LocalizationHelper.InjectLocalization(ColdQuenchFluidConfig.DISPLAY_NAME_EN + "_Desc", description);
            LocalizationHelper.InjectLocalization(ColdQuenchFluidConfig.LOC_KEY_DISPLAY + "_Desc", description);

            // 注入物品 ID 键
            string itemKey = "Item_" + ColdQuenchFluidConfig.TYPE_ID;
            LocalizationHelper.InjectLocalization(itemKey, displayName);
            LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

            // 注入标签本地化（格式：Tag_{name} 和 Tag_{name}_Desc）
            string tagDisplayName = L10n.T(ColdQuenchFluidConfig.TAG_DISPLAY_CN, ColdQuenchFluidConfig.TAG_DISPLAY_EN);
            string tagDescription = L10n.T(ColdQuenchFluidConfig.TAG_DESC_CN, ColdQuenchFluidConfig.TAG_DESC_EN);
            LocalizationHelper.InjectLocalization("Tag_" + ColdQuenchFluidConfig.TAG_NAME, tagDisplayName);
            LocalizationHelper.InjectLocalization("Tag_" + ColdQuenchFluidConfig.TAG_NAME + "_Desc", tagDescription);

            // 注入 UI 提示文本
            LocalizationHelper.InjectLocalization("BossRush_ColdQuench_LockHint", ColdQuenchFluidConfig.GetLockHint());
            LocalizationHelper.InjectLocalization("BossRush_ColdQuench_LockedHint", ColdQuenchFluidConfig.GetLockedHint());
            LocalizationHelper.InjectLocalization("BossRush_ColdQuench_NoFluidHint", ColdQuenchFluidConfig.GetNoFluidHint());
            LocalizationHelper.InjectLocalization("BossRush_ColdQuench_LockSuccess", ColdQuenchFluidConfig.GetLockSuccessHint());

            ModBehaviour.DevLog("[LocalizationInjector] 冷淬液本地化注入完成");
        }

        /// <summary>
        /// 注入砖石物品本地化
        /// </summary>
        public static void InjectBrickStoneLocalization()
        {
            string displayName = BrickStoneConfig.GetDisplayName();
            string description = BrickStoneConfig.GetDescription();

            // 注入中英文键
            LocalizationHelper.InjectLocalization(BrickStoneConfig.DISPLAY_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(BrickStoneConfig.DISPLAY_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization(BrickStoneConfig.LOC_KEY_DISPLAY, displayName);

            LocalizationHelper.InjectLocalization(BrickStoneConfig.DISPLAY_NAME_CN + "_Desc", description);
            LocalizationHelper.InjectLocalization(BrickStoneConfig.DISPLAY_NAME_EN + "_Desc", description);
            LocalizationHelper.InjectLocalization(BrickStoneConfig.LOC_KEY_DISPLAY + "_Desc", description);

            // 注入物品 ID 键
            string itemKey = "Item_" + BrickStoneConfig.TYPE_ID;
            LocalizationHelper.InjectLocalization(itemKey, displayName);
            LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

            // 注入无哥布林提示
            LocalizationHelper.InjectLocalization("BossRush_BrickStone_NoGoblin", BrickStoneConfig.GetNoGoblinHint());

            ModBehaviour.DevLog("[LocalizationInjector] 砖石本地化注入完成");
        }

        /// <summary>
        /// 注入钻石物品本地化
        /// </summary>
        public static void InjectDiamondLocalization()
        {
            string displayName = DiamondConfig.GetDisplayName();
            string description = DiamondConfig.GetDescription();

            // 注入中英文键
            LocalizationHelper.InjectLocalization(DiamondConfig.DISPLAY_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(DiamondConfig.DISPLAY_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization(DiamondConfig.LOC_KEY_DISPLAY, displayName);

            LocalizationHelper.InjectLocalization(DiamondConfig.DISPLAY_NAME_CN + "_Desc", description);
            LocalizationHelper.InjectLocalization(DiamondConfig.DISPLAY_NAME_EN + "_Desc", description);
            LocalizationHelper.InjectLocalization(DiamondConfig.LOC_KEY_DISPLAY + "_Desc", description);

            // 注入物品 ID 键
            string itemKey = "Item_" + DiamondConfig.TYPE_ID;
            LocalizationHelper.InjectLocalization(itemKey, displayName);
            LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

            // 注入无哥布林提示
            LocalizationHelper.InjectLocalization("BossRush_Diamond_NoGoblin", DiamondConfig.GetNoGoblinHint());

            ModBehaviour.DevLog("[LocalizationInjector] 钻石本地化注入完成");
        }

        /// <summary>
        /// 注入钻石戒指物品本地化
        /// </summary>
        public static void InjectDiamondRingLocalization()
        {
            string displayName = DiamondRingConfig.GetDisplayName();
            string description = DiamondRingConfig.GetDescription();

            // 注入中英文键
            LocalizationHelper.InjectLocalization(DiamondRingConfig.DISPLAY_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(DiamondRingConfig.DISPLAY_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization(DiamondRingConfig.LOC_KEY_DISPLAY, displayName);

            LocalizationHelper.InjectLocalization(DiamondRingConfig.DISPLAY_NAME_CN + "_Desc", description);
            LocalizationHelper.InjectLocalization(DiamondRingConfig.DISPLAY_NAME_EN + "_Desc", description);
            LocalizationHelper.InjectLocalization(DiamondRingConfig.LOC_KEY_DISPLAY + "_Desc", description);

            // 注入物品 ID 键（这是最重要的，游戏通过这个键查找物品名称）
            string itemKey = "Item_" + DiamondRingConfig.TYPE_ID;
            LocalizationHelper.InjectLocalization(itemKey, displayName);
            LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

            ModBehaviour.DevLog("[LocalizationInjector] 钻石戒指本地化注入完成");
        }

        /// <summary>
        /// 注入安神滴剂物品本地化
        /// </summary>
        public static void InjectCalmingDropsLocalization()
        {
            string displayName = CalmingDropsConfig.GetDisplayName();
            string description = CalmingDropsConfig.GetDescription();

            LocalizationHelper.InjectLocalization(CalmingDropsConfig.DISPLAY_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(CalmingDropsConfig.DISPLAY_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization(CalmingDropsConfig.LOC_KEY_DISPLAY, displayName);

            LocalizationHelper.InjectLocalization(CalmingDropsConfig.DISPLAY_NAME_CN + "_Desc", description);
            LocalizationHelper.InjectLocalization(CalmingDropsConfig.DISPLAY_NAME_EN + "_Desc", description);
            LocalizationHelper.InjectLocalization(CalmingDropsConfig.LOC_KEY_DISPLAY + "_Desc", description);

            string itemKey = "Item_" + CalmingDropsConfig.TYPE_ID;
            LocalizationHelper.InjectLocalization(itemKey, displayName);
            LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

            ModBehaviour.DevLog("[LocalizationInjector] 安神滴剂本地化注入完成");
        }

        /// <summary>
        /// 注入平安护身符物品本地化
        /// </summary>
        public static void InjectPeaceCharmLocalization()
        {
            string displayName = PeaceCharmConfig.GetDisplayName();
            string description = PeaceCharmConfig.GetDescription();

            LocalizationHelper.InjectLocalization(PeaceCharmConfig.DISPLAY_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(PeaceCharmConfig.DISPLAY_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization(PeaceCharmConfig.LOC_KEY_DISPLAY, displayName);

            LocalizationHelper.InjectLocalization(PeaceCharmConfig.DISPLAY_NAME_CN + "_Desc", description);
            LocalizationHelper.InjectLocalization(PeaceCharmConfig.DISPLAY_NAME_EN + "_Desc", description);
            LocalizationHelper.InjectLocalization(PeaceCharmConfig.LOC_KEY_DISPLAY + "_Desc", description);

            string itemKey = "Item_" + PeaceCharmConfig.TYPE_ID;
            LocalizationHelper.InjectLocalization(itemKey, displayName);
            LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);

            ModBehaviour.DevLog("[LocalizationInjector] 平安护身符本地化注入完成");
        }

        // ============================================================================
        // 婚礼教堂建筑本地化
        // ============================================================================

        // 婚礼教堂本地化数据
        private const string WEDDING_BUILDING_NAME_CN = "婚礼教堂";
        private const string WEDDING_BUILDING_NAME_EN = "Wedding Chapel";
        private const string WEDDING_BUILDING_DESC_CN = "一座温馨的小教堂，可以在这里举办婚礼仪式。";
        private const string WEDDING_BUILDING_DESC_EN = "A cozy little chapel where wedding ceremonies can be held.";

        /// <summary>
        /// 注入婚礼教堂建筑本地化
        /// 游戏建筑系统使用 "Building_{id}" 和 "Building_{id}_Desc" 作为本地化键
        /// </summary>
        public static void InjectWeddingBuildingLocalization()
        {
            string displayName = L10n.T(WEDDING_BUILDING_NAME_CN, WEDDING_BUILDING_NAME_EN);
            string description = L10n.T(WEDDING_BUILDING_DESC_CN, WEDDING_BUILDING_DESC_EN);

            // 注入建筑系统使用的标准键（Building_{id} 格式）
            LocalizationHelper.InjectLocalization("Building_wedding_chapel", displayName);
            LocalizationHelper.InjectLocalization("Building_wedding_chapel_Desc", description);

            ModBehaviour.DevLog("[LocalizationInjector] 婚礼教堂建筑本地化注入完成");
        }

        // ============================================================================
        // 布满了灰尘的星愿许愿台建筑本地化
        // ============================================================================

        private const string STARWISH_BUILDING_NAME_CN = "布满了灰尘的星愿许愿台";
        private const string STARWISH_BUILDING_NAME_EN = "Dust-Covered StarWish Fountain";
        private const string STARWISH_BUILDING_DESC_CN = "闭上眼，将心愿写入星光…";
        private const string STARWISH_BUILDING_DESC_EN = "Close your eyes, write your wish into the starlight...";
        private const string STARWISH_INTERACT_CN = "许愿";
        private const string STARWISH_INTERACT_EN = "Make a Wish";

        /// <summary>
        /// 注入布满了灰尘的星愿许愿台建筑本地化
        /// </summary>
        public static void InjectWishFountainLocalization()
        {
            string displayName = L10n.T(STARWISH_BUILDING_NAME_CN, STARWISH_BUILDING_NAME_EN);
            string description = L10n.T(STARWISH_BUILDING_DESC_CN, STARWISH_BUILDING_DESC_EN);
            string interact = L10n.T(STARWISH_INTERACT_CN, STARWISH_INTERACT_EN);

            LocalizationHelper.InjectLocalization("Building_starwish_fountain", displayName);
            LocalizationHelper.InjectLocalization("Building_starwish_fountain_Desc", description);
            LocalizationHelper.InjectLocalization("BossRush_StarWish_Interact", interact);
            LocalizationHelper.InjectLocalization(STARWISH_INTERACT_CN, interact);
            LocalizationHelper.InjectLocalization(interact, interact);

            ModBehaviour.DevLog("[LocalizationInjector] 布满了灰尘的星愿许愿台建筑本地化注入完成");
        }
    }
}
