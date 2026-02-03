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
        // Wiki Book 本地化数据
        // ============================================================================
        private const string WIKI_BOOK_NAME_CN = "冒险家日志";
        private const string WIKI_BOOK_NAME_EN = "Adventurer's Journal";
        private const string WIKI_BOOK_DESC_CN = "一本皱皱巴巴的冒险家日志，看得出阿稳已经翻了又翻、开了又开。";
        private const string WIKI_BOOK_DESC_EN = "A crumpled adventurer's journal. You can tell Awen has flipped through it again and again.";
        
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
        
        // 快递服务功能本地化数据
        private const string COURIER_CONTAINER_TITLE_CN = "阿稳速递";
        private const string COURIER_CONTAINER_TITLE_EN = "Awen Express";
        private const string COURIER_SERVICE_SEND_CN = "发送";
        private const string COURIER_SERVICE_SEND_EN = "Send";
        private const string COURIER_SERVICE_FEE_CN = "快递费: {0}";
        private const string COURIER_SERVICE_FEE_EN = "Fee: {0}";
        private const string COURIER_SERVICE_GOODBYE_CN = "欢迎下次光临，稳叔爱你哟~";
        private const string COURIER_SERVICE_GOODBYE_EN = "Come again, Uncle Wen loves you~";
        private const string COURIER_SERVICE_INSUFFICIENT_CN = "资金不足";
        private const string COURIER_SERVICE_INSUFFICIENT_EN = "Insufficient funds";
        private const string COURIER_SERVICE_EMPTY_CN = "请放入物品";
        private const string COURIER_SERVICE_EMPTY_EN = "Please add items";
        
        // ============================================================================
        // 寄存服务本地化数据
        // ============================================================================
        private const string STORAGE_SERVICE_CN = "寄存服务";
        private const string STORAGE_SERVICE_EN = "Storage Service";
        private const string STORAGE_CONTAINER_TITLE_CN = "阿稳寄存";
        private const string STORAGE_CONTAINER_TITLE_EN = "Awen Storage";
        private const string STORAGE_SERVICE_RETRIEVE_ALL_CN = "全部取出";
        private const string STORAGE_SERVICE_RETRIEVE_ALL_EN = "Retrieve All";
        private const string STORAGE_SERVICE_INSUFFICIENT_CN = "别乱碰，不然让你见识下稳叔的厉害";
        private const string STORAGE_SERVICE_INSUFFICIENT_EN = "Don't touch that, or I'll show you what Uncle Wen is capable of";
        private const string STORAGE_SERVICE_RETRIEVED_CN = "多存多优惠！小子！";
        private const string STORAGE_SERVICE_RETRIEVED_EN = "Store more, save more! Kid!";
        private const string STORAGE_SERVICE_EMPTY_CN = "空空如也";
        private const string STORAGE_SERVICE_EMPTY_EN = "Empty";
        
        // 阿稳寄存服务（新版）本地化数据
        private const string STORAGE_DEPOSIT_SERVICE_NAME_CN = "寄存服务";
        private const string STORAGE_DEPOSIT_SERVICE_NAME_EN = "Storage Deposit";
        private const string STORAGE_DEPOSIT_SHOP_NAME_CN = "阿稳寄存";
        private const string STORAGE_DEPOSIT_SHOP_NAME_EN = "Awen's Storage";
        private const string STORAGE_DEPOSIT_BUTTON_CN = "寄存";
        private const string STORAGE_DEPOSIT_BUTTON_EN = "Deposit";
        private const string STORAGE_DEPOSIT_DEPOSITED_CN = "物品已存入寄存柜";
        private const string STORAGE_DEPOSIT_DEPOSITED_EN = "Item deposited";
        private const string STORAGE_DEPOSIT_RETRIEVED_CN = "物品已取回";
        private const string STORAGE_DEPOSIT_RETRIEVED_EN = "Item retrieved";
        private const string STORAGE_DEPOSIT_INVENTORY_FULL_CN = "背包已满，无法取回";
        private const string STORAGE_DEPOSIT_INVENTORY_FULL_EN = "Inventory full";
        private const string STORAGE_DEPOSIT_FAREWELL_CN = "多存多优惠！小子！";
        private const string STORAGE_DEPOSIT_FAREWELL_EN = "Deposit more, get more discounts! Kid!";
        private const string STORAGE_DEPOSIT_RETRIEVE_ALL_CN = "全部取出";
        private const string STORAGE_DEPOSIT_RETRIEVE_ALL_EN = "Retrieve All";
        private const string STORAGE_DEPOSIT_ITEM_NOT_UNLOCKED_CN = "该物品未解锁，无法寄存";
        private const string STORAGE_DEPOSIT_ITEM_NOT_UNLOCKED_EN = "Item not unlocked, cannot deposit";
        private const string STORAGE_DEPOSIT_DISCARD_ALL_CN = "全部丢弃";
        private const string STORAGE_DEPOSIT_DISCARD_ALL_EN = "Discard All";
        private const string STORAGE_DEPOSIT_DISCARDED_CN = "已丢弃所有寄存物品";
        private const string STORAGE_DEPOSIT_DISCARDED_EN = "All deposited items discarded";
        
        // ============================================================================
        // 哥布林NPC本地化数据
        // ============================================================================
        private const string GOBLIN_NAME_CN = "叮当";
        private const string GOBLIN_NAME_EN = "Dingdang";
        private const string GOBLIN_TALK_CN = "交谈";
        private const string GOBLIN_TALK_EN = "Talk";
        private const string GOBLIN_GREETING_CN = "嘿嘿，有啥需要的？";
        private const string GOBLIN_GREETING_EN = "Hehe, need something?";
        
        // 重铸服务本地化数据
        private const string REFORGE_SERVICE_CN = "重铸服务";
        private const string REFORGE_SERVICE_EN = "Reforge Service";
        private const string REFORGE_TITLE_CN = "叮当的重铸工坊";
        private const string REFORGE_TITLE_EN = "Dingdang's Reforge Workshop";
        private const string REFORGE_DESC_CN = "选择一件装备进行重铸，投入更多金钱可以提高重铸品质。\n品质越高的装备，获得高属性的概率越大。";
        private const string REFORGE_DESC_EN = "Select an equipment to reforge. Invest more money for better quality.\nHigher quality equipment has better chances for high stats.";
        private const string REFORGE_NO_ITEM_SELECTED_CN = "请先选择一件装备";
        private const string REFORGE_NO_ITEM_SELECTED_EN = "Please select an equipment first";
        private const string REFORGE_SELECTED_CN = "已选择";
        private const string REFORGE_SELECTED_EN = "Selected";
        private const string REFORGE_MODIFIERS_CN = "属性数量";
        private const string REFORGE_MODIFIERS_EN = "Modifiers";
        private const string REFORGE_COST_CN = "投入金额";
        private const string REFORGE_COST_EN = "Cost";
        private const string REFORGE_BUTTON_CN = "重铸";
        private const string REFORGE_BUTTON_EN = "Reforge";
        private const string REFORGE_CLOSE_CN = "关闭";
        private const string REFORGE_CLOSE_EN = "Close";
        private const string REFORGE_SUCCESS_CN = "重铸成功";
        private const string REFORGE_SUCCESS_EN = "Reforge successful";
        private const string REFORGE_SELECT_FIRST_CN = "请先选择装备";
        private const string REFORGE_SELECT_FIRST_EN = "Please select equipment first";
        private const string REFORGE_NOT_ENOUGH_MONEY_CN = "金钱不足";
        private const string REFORGE_NOT_ENOUGH_MONEY_EN = "Not enough money";
        private const string REFORGE_NO_EQUIPMENT_CN = "没有可重铸的装备";
        private const string REFORGE_NO_EQUIPMENT_EN = "No reforgeable equipment";
        
        // ============================================================================
        // 快递员首次见面对话（大对话系统）
        // ============================================================================
        private static readonly string[][] COURIER_FIRST_MEET_DIALOGUES = new string[][]
        {
            new string[] { "哟，新来的？我是阿稳，这片区域的快递员。", "Hey, newbie? I'm Awen, the courier for this area." },
            new string[] { "别看我只是个送快递的，这地方的门道我可清楚得很。", "Don't let the delivery job fool you, I know all the ins and outs of this place." },
            new string[] { "这本书给你，里面记载了不少有用的情报。", "Here's a book for you, it contains a lot of useful intel." },
            new string[] { "有什么需要寄存的东西也可以找我，收费公道童叟无欺！", "If you need to store anything, come find me. Fair prices, no tricks!" }
        };
        
        // ============================================================================
        // 哥布林叮当5级故事对话（大对话系统）- 叮当的过去
        // ============================================================================
        private static readonly string[][] GOBLIN_STORY_LEVEL5_DIALOGUES = new string[][]
        {
            new string[] { "...你想知道叮当的故事吗？叮当从来没跟别人说过...", "...Do you want to know Dingdang's story? Dingdang has never told anyone..." },
            new string[] { "叮当是在J-Lab被'创造'出来的，他们叫叮当'智慧哥布林实验体007号'。", "Dingdang was 'created' in J-Lab, they called Dingdang 'Intelligent Goblin Test Subject 007'." },
            new string[] { "叮当的脸...这张永远在笑的脸，也是他们做的。他们说笑脸更容易被接受...", "Dingdang's face... this forever smiling face, they did this too. They said a smile is more acceptable..." },
            new string[] { "叮当想哭的时候...也哭不出来...你能理解吗？", "When Dingdang wants to cry... Dingdang can't... can you understand?" },
            new string[] { "后来叮当逃出来了，但那些哥布林说叮当是'怪胎'，他们欺负叮当...", "Later Dingdang escaped, but those goblins called Dingdang a 'freak', they bullied Dingdang..." },
            new string[] { "...谢谢你愿意听叮当说这些。你是第一个愿意听的人...", "...Thank you for listening. You're the first person willing to listen..." }
        };
        
        // ============================================================================
        // 哥布林叮当10级故事对话（大对话系统）- 叮当的心愿
        // ============================================================================
        private static readonly string[][] GOBLIN_STORY_LEVEL10_DIALOGUES = new string[][]
        {
            new string[] { "...叮当有件事想告诉你。", "...Dingdang has something to tell you." },
            new string[] { "在鸭科夫这个世界里，每个人都在为了生存而挣扎。叮当也是一样...", "In this world of Duckov, everyone struggles to survive. Dingdang is the same..." },
            new string[] { "叮当见过很多人，但他们都只是路过...只有你愿意停下来...", "Dingdang has met many people, but they all just passed by... only you were willing to stop..." },
            new string[] { "叮当一直在想，这个世界的'真相'是什么？为什么会有J-Lab？", "Dingdang always wonders, what is the 'truth' of this world? Why is there J-Lab?" },
            new string[] { "叮当有一个心愿...叮当希望有一天，能和你一起找到答案。", "Dingdang has a wish... Dingdang hopes that one day, we can find the answer together." },
            new string[] { "因为你是叮当...叮当最好的朋友。这次叮当的笑脸...是真心的。谢谢你。", "Because you are Dingdang's... Dingdang's best friend. This time Dingdang's smile... is genuine. Thank you." }
        };
        
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
            new string[] { "那头火龙在叽里咕噜的时候最好跑远点", "When that fire dragon starts gurgling, you better run far away" },
            new string[] { "离火龙太近可是会被炸的哦", "Get too close to the fire dragon and you'll get blown up" },
            new string[] { "有时候送的快也很重要，直接就把钱拿过来，概不赊账！", "Sometimes speed matters, just hand over the money, no credit!" }
        };
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 注入所有本地化（主入口）
        /// </summary>
        public static void InjectAll(int ticketTypeId = 0, int cakeTypeId = 0, int wikiBookTypeId = 0)
        {
            try
            {
                InjectTicketLocalization(ticketTypeId);
                InjectCakeLocalization(cakeTypeId);
                InjectWikiBookLocalization(wikiBookTypeId);
                InjectColdQuenchFluidLocalization();
                InjectBrickStoneLocalization();
                InjectDragonDescendantLocalization();
                InjectCourierNPCLocalization();
                InjectGoblinNPCLocalization();
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
        /// 注入 Wiki Book 本地化
        /// </summary>
        public static void InjectWikiBookLocalization(int typeId)
        {
            string displayName = L10n.T(WIKI_BOOK_NAME_CN, WIKI_BOOK_NAME_EN);
            string description = L10n.T(WIKI_BOOK_DESC_CN, WIKI_BOOK_DESC_EN);
            
            // 注入中英文键
            LocalizationHelper.InjectLocalization(WIKI_BOOK_NAME_CN, displayName);
            LocalizationHelper.InjectLocalization(WIKI_BOOK_NAME_EN, displayName);
            LocalizationHelper.InjectLocalization("BossRush_WikiBook", displayName);
            
            LocalizationHelper.InjectLocalization(WIKI_BOOK_NAME_CN + "_Desc", description);
            LocalizationHelper.InjectLocalization(WIKI_BOOK_NAME_EN + "_Desc", description);
            LocalizationHelper.InjectLocalization("BossRush_WikiBook_Desc", description);
            
            // 注入 Unity 预制体中使用的本地化键（冒险家日志）
            // 预制体 displayName 字段设置为 "冒险家日志"，游戏会用它作为本地化键查找
            LocalizationHelper.InjectLocalization("冒险家日志", displayName);
            LocalizationHelper.InjectLocalization("冒险家日志_Desc", description);
            
            // 注入物品 ID 键
            if (typeId > 0)
            {
                string itemKey = "Item_" + typeId;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
            }
            
            ModBehaviour.DevLog("[LocalizationInjector] Wiki Book 本地化注入完成");
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
            
            // 聊天选项
            string chatOption = L10n.T("聊天", "Chat");
            LocalizationHelper.InjectLocalization("BossRush_Chat", chatOption);
            
            // 赠送礼物选项
            string giftOption = L10n.T("赠送礼物", "Give Gift");
            LocalizationHelper.InjectLocalization("BossRush_GiveGift", giftOption);
            
            // 商店选项
            string shopOption = L10n.T("商店", "Shop");
            LocalizationHelper.InjectLocalization("BossRush_GoblinShop", shopOption);
            LocalizationHelper.InjectLocalization("BossRush_NPCShop", shopOption);  // 通用NPC商店交互键
            
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
    }
}
