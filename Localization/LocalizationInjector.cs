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
    public static partial class LocalizationInjector
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

        // ============================================================================
        // 护士NPC本地化数据
        // ============================================================================
        private const string NURSE_NAME_CN = "羽织";
        private const string NURSE_NAME_EN = "Yu Zhi";
        private const string NURSE_CHAT_CN = "聊天";
        private const string NURSE_CHAT_EN = "Chat";
        private const string NURSE_HEAL_CN = "治疗";
        private const string NURSE_HEAL_EN = "Heal";

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

        // ============================================================================
        // 护士羽织5级故事对话（大对话系统）- 羽织的诅咒
        // ============================================================================
        private static readonly string[][] NURSE_STORY_LEVEL5_DIALOGUES = new string[][]
        {
            new string[] { "...你已经到5级了，有些话我不能再瞒着你。", "...You've reached level 5. There are things I can't hide from you anymore." },
            new string[] { "我原本只是一只普通的鸭子，被他们抓进实验室后，硬生生长出了人腿。", "I was once just an ordinary duck. After they captured me for experiments, they forced human legs onto me." },
            new string[] { "那场实验成功了，却也留下了诅咒。", "The experiment succeeded, but it also left a curse behind." },
            new string[] { "你看到我大腿上的紫色裂纹了吗？它们每天都在扩散、灼痛。", "Do you see the purple cracks on my thighs? They spread and burn every single day." },
            new string[] { "我每天都要忍着这种痛苦，假装自己只是个普通护士。", "I endure that pain every day and pretend I'm just an ordinary nurse." },
            new string[] { "谢谢你愿意听我说这些...这份信任，只给你。", "Thank you for listening... this trust is for you alone." }
        };

        // 快递员随机对话（中英文对照）
        // 包含：全部mod内容的角色口吻提示——游戏模式、Boss、装备、物品、地图、NPC、系统、日常世界观
        private static readonly string[][] COURIER_DIALOGUES = new string[][]
        {
            // ============ 快递业务相关 ============
            new string[] { "补给到了……先把伞可乐灌了，灵魂别掉地上。", "Supplies arrived... drink your Umbrella Cola first, don't let your soul drop." },
            new string[] { "有啥要寄存尽管来，我这人信得过，童叟无欺。", "Need to store something? Come find me. I've never lost a package." },
            new string[] { "有时候送的快也很重要，直接就把钱拿过来，概不赊账！", "Sometimes speed matters, just hand over the money, no credit!" },
            new string[] { "你要是能活到下一波，我给你盖个章，再塞你一瓶'有糖的'——有灵魂那种。", "If you survive the next wave, I'll stamp your card and slip you a 'sugared' one - the kind with soul." },
            new string[] { "我这把年纪了还在跑单，外星水熊虫母舰来了都得排队签收。", "At my age still running deliveries. Even alien tardigrade motherships have to queue for pickup." },
            new string[] { "签收方式：按爪印、按羽毛、或者交一块蓝色方块当押金。", "Sign for delivery: paw print, feather, or leave a blue cube as deposit." },

            // ============ 焚天龙皇Boss相关 ============
            new string[] { "深处那头龙皇……我送快递的时候撞见过一次，那气势，整个天都压下来了。", "The Dragon King in the depths... I bumped into it once on a delivery run. The pressure it gives off feels like the sky is collapsing." },
            new string[] { "龙皇的鳞片那是真值钱，不过你得先有命把它剥下来。", "Dragon King scales are worth a fortune, but you need to be alive to peel them off." },
            new string[] { "龙皇发怒那次，我正跑在路上，满天通红差点把快递连人一起烧成灰。", "When the Dragon King raged last time, I was mid-delivery. The whole sky turned red, almost burned me and the packages to ash." },
            new string[] { "龙皇掉的东西都好——飞行图腾、龙王之冕、龙王鳞铠、逆鳞……但凡拿到一样都够吹一年。", "Everything the Dragon King drops is top-tier—Flight Totem, Dragon Crown, Scale Armor, Reverse Scale... just one piece and you can brag about it for a year." },
            new string[] { "别小看龙皇，那家伙变了好几个阶段，每一阶段都够你喝一壶。别冲上去就莽。", "Don't underestimate the Dragon King. It has multiple phases and each one hits like a truck. Don't just charge in blindly." },

            // ============ 火龙相关 ============
            new string[] { "看到那只到处乱创的火龙了吗？离远点，烧坏了快递我找你赔。", "See that fire dragon causing chaos everywhere? Stay away from it. If it burns my deliveries, you're paying." },
            new string[] { "火龙怕毒，这我是亲眼见过的。哪天你想对付它，记得备点带毒的家伙。", "Fire dragons fear poison. I've seen it with my own eyes. If you ever want to take it on, bring something toxic." },
            new string[] { "你知道火龙也怕冰吗？我有一次都把它打坠机了哈哈哈哈", "Did you know fire dragons also fear ice? I once made it crash land hahaha" },
            new string[] { "这该死的火龙把我的快递都创飞了", "That damn fire dragon knocked all my deliveries flying" },
            new string[] { "那头火龙在叽里咕噜的时候最好跑远点", "When that fire dragon starts gurgling, you better run far away" },
            new string[] { "离火龙太近可是会被炸的哦", "Get too close to the fire dragon and you'll get blown up" },

            // ============ 叮当NPC相关 ============
            new string[] { "叮当那小子别看长得怪，手艺活是真没话说，重铸装备找它准没错。", "Don't let Dingdang's looks fool you. Its craftsmanship is impeccable—if you need gear reforged, it's the one to find." },
            new string[] { "叮当那家伙总笑嘻嘻的，我跑了这么多年单，总觉得那笑容背后有事儿。", "Dingdang's always grinning. After all my years on the road, I can tell there's something behind that smile." },
            new string[] { "想找叮当重铸就去呗，顺便带点小礼物，说不定能讨个好价。", "Want Dingdang to reforge your gear? Bring a little gift along, might get yourself a better deal." },
            new string[] { "叮当画的那些涂鸦……还别说，有几幅我看着挺有意思的。", "Those graffiti Dingdang draws... gotta say, a few of them are actually pretty interesting." },

            // ============ 护士NPC相关 ============
            new string[] { "羽织那边的药材单我刚送到，她记账比我还细。", "I just delivered Yu Zhi's medicine order. She keeps records even more carefully than I do." },
            new string[] { "你要是受了伤就先去找羽织，别硬扛。她嘴上凶，手上可稳。", "If you're hurt, go see Yu Zhi first. She sounds strict, but her hands are steady." },
            new string[] { "她腿上的紫色裂纹不是装饰，是旧实验留下的。别盯着看，她会不高兴。", "Those purple cracks on her legs are no decoration. Left by old experiments. Don't stare—she'll get mad." },
            new string[] { "别在羽织面前逞强，她最烦不把命当回事的人。", "Don't act tough in front of Yu Zhi. She hates people who don't value their own lives." },

            // ============ 好感度系统相关（用角色口吻传递信息） ============
            new string[] { "叮当和羽织这两个家伙都挺记人情的，你对他们好，他们迟早会还。", "Dingdang and Yu Zhi both remember who treats them well. Be good to them, and they'll return the favor eventually." },
            new string[] { "送礼也有讲究，送对了人家记你一辈子的好，送错了……嘿嘿，自求多福。", "Gift-giving is an art. Get it right and they'll remember your kindness forever. Get it wrong... heh, good luck." },
            new string[] { "听说好感度拉满了会有大事发生？具体的我也不清楚，你可以自己去试试。", "Heard something big happens at max affinity? I don't know the details. Go find out yourself." },

            // ============ 重铸系统相关 ============
            new string[] { "装备属性不满意？叮当那能重铸。不过别忘了带够钱，投入越多出来的东西越好。", "Not happy with your gear stats? Dingdang can reforge them. Just bring enough cash—more investment, better results." },
            new string[] { "重铸要花钱，但比起在战场上碰运气，花钱买个靠谱强多了。", "Reforging costs money, but compared to hoping for a lucky drop on the battlefield, paying for certainty is way better." },
            new string[] { "叮当的重铸工坊最近生意不错，我老给它送材料过去。", "Dingdang's reforge workshop has been busy lately. I keep delivering materials there." },

            // ============ 飞行图腾相关 ============
            new string[] { "飞行图腾？那玩意儿能让你飞起来，送快递要是有这个就好了……", "Flight Totem? That thing lets you fly. Wish I had one for deliveries..." },
            new string[] { "飞行图腾好用是好用，就是别飞太高，摔下来可不是闹着玩的。", "Flight totem's great and all, just don't fly too high. Falling's no joke." },
            new string[] { "有了飞行图腾，躲Boss技能就方便多了。你有机会搞到一个的话，一定要试试。", "With the flight totem, dodging boss skills is way easier. If you ever get the chance, definitely try one out." },

            // ============ 成就系统相关 ============
            new string[] { "成就勋章你收了多少了？上次我看你那个勋章盒还空着大半呢。", "How many achievement medals have you collected? Last time I checked, your medal case was still mostly empty." },
            new string[] { "听说集齐所有成就会有什么好东西，我也没见过，你要是集齐了给我看看。", "Heard there's something good for collecting all achievements. Never seen it myself. Show me if you get them all." },
            new string[] { "有些成就挺难拿的，不过话说回来，要是不难那也没意思了。", "Some achievements are tough to get, but then again, if it were easy it wouldn't be fun." },

            // ============ 新物品相关 ============
            new string[] { "砖石和钻石？那是召唤叮当用的。砖石它会生气，钻石它会开心。你看着办。", "Brickstone and diamonds? Those summon Dingdang. Brickstone makes it angry, diamonds make it happy. Your call." },
            new string[] { "冷淬液是好东西，能把装备上你看中的属性锁住，重铸的时候就不怕洗掉了。", "Cold Quench Fluid is great stuff—locks the stats you like on your gear, so reforging won't wash them away." },
            new string[] { "叮当涂鸦是那小家伙自己画的，收藏起来还挺有意思。", "Dingdang Graffiti is that little guy's own artwork. Fun to collect." },

            // ============ 世界观/日常 ============
            new string[] { "这破地方路况是真差，不过跑久了也就习惯了。", "Roads here are terrible, but you get used to it after a while." },
            new string[] { "别跟我讲热血，我只认单号、撤离路线，以及'有糖才有灵魂'。", "Don't talk passion to me. I only care about order numbers, evacuation routes, and 'sugar means soul'." },
            new string[] { "星球都快崩了还要准点，KPI不讲情面啊。", "Planet's about to collapse and we still need to be on time. KPIs show no mercy." },
            new string[] { "我不怕Boss，我怕紫毒把快递标签腐蚀了——到时候谁也别想对账。", "I'm not afraid of bosses. I'm afraid the purple poison will corrode the delivery labels—then no one can reconcile accounts." },

            // ============ 标准BossRush模式 ============
            new string[] { "船票你买了吧？拿着它选张图就能进竞技场。路牌上有难度选项，别上来就找刺激。", "Got your ticket? Pick a map and you're in. The signpost has difficulty options—don't go looking for trouble on day one." },
            new string[] { "竞技场里的路牌可不止是摆设——弹药、维修、清箱子、回出生点，全在它周围。", "The signpost in the arena isn't just decoration—ammo, repairs, cleanup, respawn point, all right there." },
            new string[] { "打完了记得去中央领通关奖励箱，那可是你拼死拼活换来的。", "Don't forget to grab the victory chest in the center after clearing all waves. That's what you bled for." },
            new string[] { "打完Boss走撤离点出去，别在里面发呆，又不是旅游景点。", "Use the extraction point when you're done. It's not a tourist attraction." },
            new string[] { "前二十个波次不会出四骑士、龙裔遗族和焚天龙皇这种狠角色，给你热身的时间。别浪费了。", "First twenty waves won't spawn the Four Horsemen, Dragon Descendant, or the Dragon King. That's your warm-up—don't waste it." },

            // ============ 无间炼狱 ============
            new string[] { "无间炼狱没有终点，Boss每波都变强，撑得越久赚得越多。真正的亡命徒才玩这个。", "Infinite Hell has no finish line. Bosses get stronger every wave. The longer you last, the more you earn. Only madmen play this." },
            new string[] { "我听说有人撑到了一百波。一百波的奖励是皇冠加一千万现金。每多一百波翻一倍。疯子。", "Heard someone made it to wave 100. Reward was a crown plus ten million cash. Doubles every hundred waves. Lunatic." },
            new string[] { "无间炼狱里不掉箱子，全换成现金，还会自动飞过来。这方面倒是挺省心的。", "Infinite Hell drops cash instead of loot boxes. Flies right to you too. Convenient, at least." },
            new string[] { "无间炼狱每五波送一件好东西，每百波给大奖。活得够久你就是首富。", "Infinite Hell gives quality loot every 5 waves and a jackpot every 100. Survive long enough and you'll be the richest duck around." },
            new string[] { "无间炼狱里路牌上会显示你攒了多少钱，那数字看着是挺爽，但你得活着带走才行。", "The signpost in Infinite Hell shows your cash pool. Nice number to look at, but you gotta stay alive to keep it." },

            // ============ 白手起家 ============
            new string[] { "白手起家啊，啥都不带就进去，装备全靠抢。说实话这种活法我挺佩服的。", "Rags to Riches—go in with nothing, gear up from what you kill. Gotta respect that kind of hustle." },
            new string[] { "白手起家前五波没有Boss，好好利用这段时间从小兵身上扒装备，后面才扛得住。", "No bosses in the first five waves of Rags to Riches. Scrape gear off the grunts while you can—you'll need it later." },
            new string[] { "白手起家越往后敌人装备越好，有时候你打死的小兵掉的比你全身都强。讽刺吧？", "In Rags to Riches, later enemies carry better gear than you. Sometimes the grunt you just killed drops something nicer than everything you're wearing. Ironic, huh?" },
            new string[] { "白手起家开局装备全是随机的，拿到什么用什么。别纠结完美开局了。", "Rags to Riches gives you random starting gear. Work with what you get. Don't obsess over a perfect start." },

            // ============ 划地为营 ============
            new string[] { "划地为营是真正的群魔乱舞——几拨Boss分了阵营互相打，你选一边站就完事了。", "Zone Defense is pure chaos—bosses split into factions and fight each other. Just pick a side." },
            new string[] { "划地为营里有个神秘商人，啥都卖，就是贵。不过弹药用完了去他那能救命。", "Zone Defense has a mystery merchant. Sells everything, but pricey. Good for emergency ammo though." },
            new string[] { "你要是觉得自己够硬，就带爷的营旗——全场都是你的敌人，没有任何友军。真·独狼。", "If you think you're tough enough, bring the Lone Wolf flag. Everyone's your enemy. No backup. True lone wolf." },
            new string[] { "划地为营里可以召唤煤球帮你打架，那小家伙打起架来还挺凶的。", "You can summon Meiqiu in Zone Defense to help you fight. That little guy's fiercer than he looks." },
            new string[] { "划地为营里有挑衅烟雾弹和混沌引爆器这些东西，用了能让不同阵营的Boss互相消耗，坐山观虎斗。", "Zone Defense has smokes and detonators that spawn bosses everywhere. Let the factions tear each other apart while you watch." },
            new string[] { "划地为营里Boss每死一个，剩下的就会变强一点。别磨叽，速战速决。", "In Zone Defense, surviving bosses get stronger each time one dies. Don't drag it out." },

            // ============ 血猎追击 ============
            new string[] { "血猎追击是最狠的模式。进去之后一直在流血，只有杀Boss才能回血。", "Blood Hunt is the hardest mode. You bleed constantly. Only killing bosses stops it." },
            new string[] { "血猎追击分四个阶段，越往后流得越快，最后还得跑到撤离点才能活下来。", "Blood Hunt has four stages. Bleeding gets faster each phase. You have to reach the extraction point to survive." },
            new string[] { "血猎追击里可以用工事——掩体、路障、铁丝网——提前摆好了能保命。", "Blood Hunt lets you deploy fortifications—barricades, wire, barriers. Set them up early, they'll save your life." },
            new string[] { "血猎追击的Boss身上有悬赏印记，杀了它印记归你，最后成功撤离每个印记换一件好装备。", "Bosses in Blood Hunt carry bounty marks. Kill them, take the marks. Each mark becomes quality gear if you extract." },
            new string[] { "血猎追击开场的三分钟准备阶段很重要，先搜刮装备再打几个Boss回口血。", "Those 3 minutes of prep time in Blood Hunt are crucial. Scavenge gear, kill a few bosses, heal up. Don't waste them." },

            // ============ 龙裔遗族Boss ============
            new string[] { "龙裔遗族比龙皇弱一档，但也不是吃素的。掉的东西够你用很长一段时间。", "Dragon Descendant is weaker than the King, but still no pushover. Its drops will last you a good while." },
            new string[] { "赤龙首和焰鳞甲都是龙裔遗族掉的，过渡期穿这两件挺管用。", "Red Dragon Head and Flame Scale Armor drop from the Dragon Descendant. Solid gear for the mid-game." },
            new string[] { "先打龙裔遗族攒一身龙裔套装，再去挑战龙皇。这是我给每个新人的建议。", "My advice to every newcomer: farm the Dragon Descendant first, gear up, then challenge the King." },

            // ============ 龙裔套装 ============
            new string[] { "龙裔套装和龙王套装都能把火焰伤害转成回血，被烧了反而越打越精神。", "Both Dragon sets convert fire damage into healing. Get burned, get stronger. Kind of poetic." },
            new string[] { "龙裔套装能冲刺，双击方向键就出去了，三米距离够躲大部分技能。", "Dragon set lets you dash. Double-tap a direction, three meters. Enough to dodge most attacks." },

            // ============ 龙王套装 ============
            new string[] { "龙王套装冲刺的时候地上会冒岩浆，穿着它跑酷又帅又危险。", "Dragon King set leaves lava trails when you dash. Stylish and dangerous." },
            new string[] { "龙王套装能连着冲两下，六米远，比龙裔套装灵活一倍。有钱就冲它去。", "Dragon King set has a double-dash—six meters. Twice as nimble as the regular set. Save up for it." },

            // ============ 逆鳞 ============
            new string[] { "逆鳞这东西就是保命符——快死的时候自动触发，回一半血还往四面八方打棱彩弹。不过用一次就碎了。", "Reverse Scale is a lifesaver—triggers when you're about to die, heals half your HP and fires prismatic shots everywhere. But it shatters after one use." },
            new string[] { "打龙皇之前我建议你带个逆鳞。多一条命总不是坏事。", "I recommend bringing a Reverse Scale before fighting the Dragon King. An extra life never hurts." },

            // ============ 焚皇断界戟 ============
            new string[] { "焚皇断界戟，龙皇掉的那把大戟。使好了伤害爆炸，不过近战打Boss得盯紧走位。", "The Halberd drops from the Dragon King. Massive damage if you use it right, but melee means you gotta watch your positioning." },

            // ============ 龙息 ============
            new string[] { "龙息是龙裔遗族掉的一把枪，掉率大概一成。算是稀罕货，拿到了别乱卖。", "Dragon Breath drops from the Dragon Descendant—about a 10% chance. Rare stuff. Don't sell it if you get one." },

            // ============ 霜之哀伤 ============
            new string[] { "霜之哀伤那把冰剑你见过没？打Blue Boss有概率出，右键能召唤五个亡灵小弟帮你打架。", "Seen Frostmourne? That ice sword drops from Blue Bosses sometimes. Right-click summons five undead minions to fight for you." },
            new string[] { "霜之哀伤自带冰属性和寒冷防护，去雪地地图带上它，不吃亏。", "Frostmourne has ice damage and cold protection built in. Bring it to snow maps, you won't regret it." },

            // ============ 焚天龙铳 ============
            new string[] { "焚天龙铳那是龙皇自己扛的枪，咱们拿不到。不过看到它开枪的时候——躲远点。", "The Dragon Cannon belongs to the Dragon King. We can't get it. But when you see it firing—get out of the way." },

            // ============ 地图 ============
            new string[] { "九张图随你选。新手去DEMO终极挑战，场地平视野好，适合练手。", "Nine maps to choose from. Beginners should try DEMO Ultimate Challenge—flat, open, good for learning." },
            new string[] { "零度挑战是雪地地图，进去会发防寒装备。不然你还没见到Boss就先冻死了。", "Zero Challenge is a snow map. They issue cold protection gear at the start. Otherwise you'd freeze before seeing a boss." },
            new string[] { "J-Lab实验室那张图……我不喜欢送那边的单。总觉得背后有东西在看我。", "The J-Lab Laboratory map... I don't like delivering there. Always feels like something's watching me from behind." },
            new string[] { "迷宫那张图视野差，拐角多，打Boss的时候容易被偷袭。不建议新手去。", "The Maze map has terrible visibility. Too many corners. Easy to get ambushed by a boss. Not for beginners." },
            new string[] { "农场镇场地开阔刷怪点又多，打Boss和划地为营都不错。", "Farm Town is open with lots of spawn points. Good for boss fights and Zone Defense alike." },

            // ============ 死亡亡魂 ============
            new string[] { "你死过的地方，下次回去会在原地冒出一个你的亡魂——带着你死时的装备，用你自己的招式打你。", "Where you die, a wraith of you appears next time—wearing your gear, using your own moves against you." },
            new string[] { "死的时候身上带的东西越贵，下次遇到的亡魂就越猛。出门别把全部家当都带上。", "The more expensive the gear you die with, the stronger your wraith becomes. Don't carry your life savings into battle." },
            new string[] { "打赢自己的亡魂记录就清了，不会再来了。直到你下次再死。", "Beat your own wraith and the record clears. Won't come back—until you die again, that is." },

            // ============ 许愿台 ============
            new string[] { "基地里那个布满灰尘的许愿台看到了吗？在上面写心愿就行。不过别写垃圾话，真的有人会看。", "Seen that dusty wish fountain in the base? Write your wishes there. Just don't write garbage—someone actually reads them." },

            // ============ 婚姻系统 ============
            new string[] { "叮当和羽织好感度拉满了可以结婚，送钻石戒指就行。不过一次只能娶一个。", "Max out affinity with Dingdang or Yu Zhi and you can propose with a diamond ring. One spouse at a time though." },
            new string[] { "结婚之后他们每天都会送东西——叮当送冷淬液，羽织送安神滴剂。过日子嘛。", "After marriage they give you daily gifts—Dingdang gives Cold Quench Fluid, Yu Zhi gives Calming Drops. Domestic bliss." },
            new string[] { "结了婚就别朝三暮四的。把戒指送别人会被抓包，扣好感度不说还得挨骂。", "Don't cheat after marrying. Send a ring to someone else and you'll get caught—lose affinity AND get scolded." },
            new string[] { "离婚也可以，不过好感度直接归零，还得重新追。想清楚再动手。", "Divorce is an option, but affinity resets to zero and you start over. Think before you act." },
            new string[] { "结了婚可以让配偶跟着你到处跑，不过好感度要是掉下去了人家会自己回家。", "Married spouses can follow you around. But if affinity drops too low, they'll head home on their own." },

            // ============ 安神滴剂 ============
            new string[] { "安神滴剂是羽织亲手调的，用了能清掉身上的负面效果。打完Boss一身debuff的时候来一瓶。", "Calming Drops are Yu Zhi's own brew. Clears debuffs. Chug one after a boss fight leaves you covered in status effects." },

            // ============ 平安护身符 ============
            new string[] { "羽织那有个平安护身符，好感度到了她会给。放在背包里就行，快死的时候有概率满血复活。", "Yu Zhi gives out Peace Charms at high affinity. Keep it in your bag—it might fully revive you when you're about to die." },

            // ============ 钻石戒指 ============
            new string[] { "钻石戒指在叮当那买，送NPC加五百好感度。好感度满的时候送出去还能求婚。一物两用。", "Buy diamond rings from Dingdang. +500 affinity as a gift, or use it to propose when affinity is maxed. Two birds, one ring." },

            // ============ 快递牌 ============
            new string[] { "快递牌你买了吗？用了能把身上所有东西一键寄回家，关键时刻能保住全部家当。", "Got an Express Token? Use it to ship everything you're carrying back home instantly. Saves your loot when things go south." },

            // ============ 扫箱令 ============
            new string[] { "划地为营和血猎追击里每打死二十个Boss会给你扫箱令，用了我帮你把场上的箱子全收了。", "Every 20 boss kills in Zone Defense or Blood Hunt earns you a Loot Sweep Token. Use it and I'll clean up all the boxes on the field." },

            // ============ 荒野号角 ============
            new string[] { "荒野号角能召唤坐骑，跑图省力不少。配了狼模型的话坐骑还会变成狼。", "The Wild Horn summons a mount. Much easier to get around. With the wolf model enabled, it becomes a wolf instead." },

            // ============ Boss筛选器 ============
            new string[] { "按Ctrl+F10能打开Boss筛选器，不想见到的Boss直接禁掉。跟快递一样，不想送的件就拒单。", "Ctrl+F10 opens the Boss Filter. Ban any boss you don't want to see. Just like declining a delivery—don't want it, don't take it." },

            // ============ 配置选项 ============
            new string[] { "很多设置是可以改的——波次间隔、每波Boss数量、Boss血量倍率。嫌太难或太简单就调一调。", "Lots of settings are configurable—wave intervals, boss count per wave, HP multipliers. Adjust if it's too hard or too easy." },

            // ============ 掉落/战利品 ============
            new string[] { "打Boss越快，掉的东西品质越高。所以别磨叽，能速杀就速杀。", "Kill bosses faster, get better loot. Don't dawdle—speed is rewarded." },
            new string[] { "在BossRush里死了不会掉身上的东西，放心去打。死了也就是丢点面子。", "You don't drop your gear when you die in BossRush. Go fight without worry. You only lose face." },
            new string[] { "战利品箱有时候会挡子弹，要是箱子堆太多了得注意别把自己卡住。", "Loot boxes can block bullets sometimes. When they pile up, watch out you don't box yourself in." },

            // ============ 成就系统补充 ============
            new string[] { "按L键能看成就面板，有些成就奖金很高——比如无伤杀龙皇给五十万。", "Press L to check achievements. Some pay really well—500,000 for flawlessing the Dragon King." },
            new string[] { "成就勋章在商人那免费领，不拿白不拿。", "Achievement medals are free at the merchant. Don't leave them sitting there." },

            // ============ 营旗/血猎收发器 ============
            new string[] { "营旗有六种加一个随机的，不同的旗子进不同的阵营。想清楚再买。", "Six camp flags plus a random one—different flags, different factions. Think before you buy." },
            new string[] { "血猎收发器是血猎追击的入场钥匙，和船票一起带才能进去。别光带一个。", "Blood Hunt Transceiver is the key to Blood Hunt mode. Bring it with a ticket. Don't show up with just one." },

            // ============ 入场优先级 ============
            new string[] { "同时带了营旗、收发器和船票的话，系统会优先让你进划地为营。想玩别的就别带营旗。", "If you carry a flag, transceiver, and ticket at the same time, the system sends you to Zone Defense first. Leave the flag behind if you want something else." },

            // ============ 龙皇掉落细节 ============
            new string[] { "龙皇掉的东西里，腾云驾雾图腾一成半概率，逆鳞三成五，其他的就看你造化了。", "Of the Dragon King's drops: Flight Totem's about 15%, Reverse Scale 35%. The rest is up to luck." },

            // ============ 更多日常/世界观 ============
            new string[] { "当快递员最怕的不是Boss，是送到了没人签收。你知道老板催单多凶吗？", "The scariest thing about being a courier isn't bosses—it's delivering to nobody. You know how hard the boss pushes?" },
            new string[] { "有些路线我跑了好几百遍了，闭着眼都能到。但每次Boss出来还是得绕路。", "I've run some routes hundreds of times. Could do them blindfolded. But bosses always make me detour." },
            new string[] { "我以前也想过当冒险家来着，后来算了一下快递赚得更多。", "I used to dream of being an adventurer. Then I did the math—courier pays better." },
            new string[] { "今天风有点大，快递差点被吹跑了。你们在里面打得热火朝天，我在外面追包裹。", "Windy today. Almost lost a package. You're in there fighting for your life, I'm out here chasing boxes." }
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
                InjectDiamondLocalization();
                InjectDiamondRingLocalization();  // 钻石戒指本地化
                InjectCalmingDropsLocalization();
                InjectPeaceCharmLocalization();
                InjectZombieModeLocalization();
                InjectDragonDescendantLocalization();
                InjectCommonNPCLocalization();
                InjectCourierNPCLocalization();
                InjectGoblinNPCLocalization();
                InjectNurseNPCLocalization();
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
        /// 注入丧尸模式本地化
        /// </summary>
        public static void InjectZombieModeLocalization()
        {
            ZombieTideInvitationConfig.InjectLocalization();
            ZombieTideBeaconConfig.InjectLocalization();
            InjectZombieModeString("BossRush_ZombieTideInvitation", "尸潮邀请函", "Zombie Tide Invitation");
            InjectZombieModeString("BossRush_ZombieTideInvitation_Desc", "进入末日丧尸模式的入场凭证。撤离失败时不退还。", "Required to enter Zombie Mode. Not refunded on failure.");
            InjectZombieModeString("BossRush_ZombieTideBeacon", "尸潮信标", "Zombie Tide Beacon");
            InjectZombieModeString("BossRush_ZombieTideBeacon_Desc", "在准备倒计时阶段使用，立即开始下一波。本局工具，不能带出。", "Use during preparation countdown to start the next wave immediately. Run-only tool.");
            InjectZombieModeString("BossRush_ZombieMode", "末日丧尸模式", "Zombie Mode");
            InjectZombieModeString("BossRush_ZombieMode_WaitingStarterChoice", "选择初始流派", "Choose starter class");
            InjectZombieModeString("BossRush_ZombieMode_Active", "尸潮来袭", "Zombie tide incoming");
            InjectZombieModeString("BossRush_ZombieMode_Exiting", "正在撤离", "Extracting");
            InjectZombieModeString("BossRush_ZombieMode_EntryName", "末日丧尸", "Zombie Mode");
            InjectZombieModeString("BossRush_ZombieMode_InvitationUseDesc", "使用：选择末日丧尸模式地图", "Use: choose a Zombie Mode map");
            InjectZombieModeString("BossRush_ZombieMode_EntryDesc", "在原版地图中迎接无限尸潮，每 5 波 Boss 节点提供撤离机会。", "Face an endless zombie tide on classic maps. Extract every 5 waves at a Boss node.");
            InjectZombieModeString("BossRush_ZombieMode_MapEntryPrefix", "末日丧尸 - {0}", "Zombie Mode - {0}");
            InjectZombieModeString("BossRush_ZombieMode_NoInvitation", "需要 1 张尸潮邀请函。", "Requires 1 Zombie Tide Invitation.");
            InjectZombieModeString("BossRush_ZombieMode_NoMaps", "没有可用的末日丧尸模式地图。", "No Zombie Mode maps are available.");
            InjectZombieModeString("BossRush_ZombieMode_OpenMapFailed", "无法打开地图选择界面。", "Could not open the map selection UI.");
            InjectZombieModeString("BossRush_ZombieMode_NotInitialized", "末日丧尸模式尚未初始化。", "Zombie Mode is not initialized.");
            InjectZombieModeString("BossRush_ZombieMode_OtherModeActive", "已有 BossRush 类模式正在进行。", "A BossRush-like mode is already in progress.");
            InjectZombieModeString("BossRush_ZombieMode_CashPrompt_Title", "投入现金兑换初始净化点数", "Convert Cash to Initial Purification Points");
            InjectZombieModeString("BossRush_ZombieMode_CashPrompt_Body", "兑换比例：100 现金 = 1 局内净化点数。失败/死亡全部损失；撤离时剩余净化点按点数结算为现金奖励。", "Rate: 100 cash = 1 run-only Purification Point. All lost on failure or death; remaining points are settled as cash rewards on extraction.");
            InjectZombieModeString("BossRush_ZombieMode_CashPrompt_AmountLabel", "投入金额", "Investment Amount");
            InjectZombieModeString("BossRush_ZombieMode_CashPrompt_Confirm", "确认", "Confirm");
            InjectZombieModeString("BossRush_ZombieMode_CashPrompt_SkipZero", "跳过（投入 0）", "Skip (Invest 0)");
            InjectZombieModeString("BossRush_ZombieMode_CashPrompt_Cancel", "返回", "Back");
            InjectZombieModeString("BossRush_ZombieMode_CashPrompt_NotEnough", "现金不足。", "Not enough cash.");
            InjectZombieModeString("BossRush_ZombieMode_CashPrompt_Balance", "余额", "Balance");
            InjectZombieModeString("BossRush_ZombieMode_CashPrompt_Preview", "投入 {0:n0} 现金 → 初始净化点数 {1:n0}", "Invest {0:n0} cash → {1:n0} initial purification points");
            InjectZombieModeString("BossRush_ZombieMode_Starter_Title", "选择初始流派", "Choose Starter Class");
            InjectZombieModeString("BossRush_ZombieMode_Starter_Subtitle", "进入第一波准备期前必须选择，无法重选。", "Required before the first preparation. Cannot be changed.");
            InjectZombieModeString("BossRush_ZombieMode_Starter_Melee", "近战求生", "Melee Survivor");
            InjectZombieModeString("BossRush_ZombieMode_Starter_Melee_Desc", "随机近战武器 + 医疗补给 + 食物饮料。含护甲头盔耳机。", "Random melee weapon + medical supplies + food/drink. Includes armor, helmet, headset.");
            InjectZombieModeString("BossRush_ZombieMode_Starter_Gunner", "枪械突围", "Gunner");
            InjectZombieModeString("BossRush_ZombieMode_Starter_Gunner_Desc", "随机枪械 + 一组同口径弹药 + 少量医疗食物。含护甲头盔耳机。", "Random gun + a stack of matched ammo + some medical/food. Includes armor, helmet, headset.");
            InjectZombieModeString("BossRush_ZombieMode_Starter_Select", "选择", "Select");
            InjectZombieModeString("BossRush_ZombieMode_Starter_Confirmed", "已锁定开局：{0}", "Starter locked: {0}");
            InjectZombieModeString("BossRush_ZombieMode_Hud_Wave", "第 {0} 波", "Wave {0}");
            InjectZombieModeString("BossRush_ZombieMode_Hud_Pollution", "污染 {0} ({1})", "Pollution {0} ({1})");
            InjectZombieModeString("BossRush_ZombieMode_Hud_PollutionTier_Base", "基础", "Base");
            InjectZombieModeString("BossRush_ZombieMode_Hud_PollutionTier_I", "I 阶", "Tier I");
            InjectZombieModeString("BossRush_ZombieMode_Hud_PollutionTier_II", "II 阶", "Tier II");
            InjectZombieModeString("BossRush_ZombieMode_Hud_PollutionTier_III", "III 阶", "Tier III");
            InjectZombieModeString("BossRush_ZombieMode_Hud_PollutionTier_IV", "IV 阶", "Tier IV");
            InjectZombieModeString("BossRush_ZombieMode_Hud_PollutionTier_Critical", "高危", "Critical");
            InjectZombieModeString("BossRush_ZombieMode_Hud_KillProgress", "击杀 {0}/{1}", "Kills {0}/{1}");
            InjectZombieModeString("BossRush_ZombieMode_Hud_BossProgress", "Boss {0}/{1}", "Boss {0}/{1}");
            InjectZombieModeString("BossRush_ZombieMode_Hud_PurificationPoints", "净化点数 {0}", "Purification {0}");
            InjectZombieModeString("BossRush_ZombieMode_Hud_NextBoss", "距下次 Boss 节点 {0} 波", "{0} waves to next Boss");
            InjectZombieModeString("BossRush_ZombieMode_Hud_NextBossNow", "下一波即为 Boss 节点", "Next wave is a Boss node");
            InjectZombieModeString("BossRush_ZombieMode_Hud_PreparationTimer", "倒计时 {0}s", "Countdown {0}s");
            InjectZombieModeString("BossRush_ZombieMode_Hud_StageBattle", "战斗中", "Combat");
            InjectZombieModeString("BossRush_ZombieMode_Hud_StageSettling", "结算中", "Settling");
            InjectZombieModeString("BossRush_ZombieMode_Hud_StageRewardSelection", "奖励选择", "Reward Selection");
            InjectZombieModeString("BossRush_ZombieMode_Hud_StagePreparation", "准备期", "Preparation");
            InjectZombieModeString("BossRush_ZombieMode_Hud_StageExtractionOpportunity", "撤离机会", "Extraction Opportunity");
            InjectZombieModeString("BossRush_ZombieMode_Hud_SafeZone_Inside", "在安全区内", "Inside Safe Zone");
            InjectZombieModeString("BossRush_ZombieMode_Hud_SafeZone_Outside", "离开安全区", "Outside Safe Zone");
            InjectZombieModeString("BossRush_ZombieMode_Hud_SafeZone_StealthOk", "保护：未破隐", "Stealth: Intact");
            InjectZombieModeString("BossRush_ZombieMode_Hud_SafeZone_StealthBroken", "保护：已破坏", "Stealth: Broken");
            InjectZombieModeString("BossRush_ZombieMode_Map_SafeZone", "安全区", "Safe Zone");
            InjectZombieModeString("BossRush_ZombieMode_Hud_RefreshAvailable", "免费刷新 {0}", "Free Refresh {0}");
            InjectZombieModeString("BossRush_ZombieMode_Hud_BeaconReady", "信标可用", "Beacon Ready");
            InjectZombieModeString("BossRush_ZombieMode_Hud_BeaconUnavailable", "信标不可用", "Beacon Unavailable");
            InjectZombieModeString("BossRush_ZombieMode_Hud_ExtractionOpenHint", "撤离点已开放 - 仅本准备期有效", "Extraction Open - This Preparation Only");
            InjectZombieModeString("BossRush_ZombieMode_Banner_Started", "<color=green>末日丧尸模式开始</color>", "<color=green>Zombie Mode Started</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_PreparationStarted", "末日丧尸模式：准备阶段开始。", "Zombie Mode: preparation started.");
            InjectZombieModeString("BossRush_ZombieMode_Banner_PreparationNextWave", "下一波尸潮即将到来。", "Next zombie wave is coming.");
            InjectZombieModeString("BossRush_ZombieMode_Banner_WaveIncoming", "第 <color=yellow>{0}</color> 波尸潮来袭！", "Zombie wave <color=yellow>{0}</color> incoming!");
            InjectZombieModeString("BossRush_ZombieMode_Banner_WaveCleared", "第 <color=yellow>{0}</color> 波已肃清。", "Wave <color=yellow>{0}</color> cleared.");
            InjectZombieModeString("BossRush_ZombieMode_Banner_Failed", "<color=red>末日丧尸模式失败。</color>", "<color=red>Zombie Mode failed.</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_BossWaveStart", "<color=red>Boss 节点 - 第 {0} 波</color>", "<color=red>Boss Node - Wave {0}</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_BossWaveCleared", "<color=yellow>Boss 节点完成</color>", "<color=yellow>Boss Node Cleared</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_PollutionUp", "<color=#bb55ff>污染上升至 {0}（{1}）</color>", "<color=#bb55ff>Pollution rose to {0} ({1})</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_ExtractionOpen", "<color=#22aaff>撤离点已开放</color>", "<color=#22aaff>Extraction Open</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_StealthBroken", "<color=#ff7733>安全区保护已破坏</color>", "<color=#ff7733>Safe Zone Stealth Broken</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_PerformanceProtect", "<color=gray>[Dev] 性能保护已启用</color>", "<color=gray>[Dev] Performance protect</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_RepairPackReceived", "<color=#88dd44>已领取工事补给包</color>", "<color=#88dd44>Fortification Pack Received</color>");
            InjectZombieModeString("BossRush_ZombieMode_Extraction_Title", "撤离机会", "Extraction Opportunity");
            InjectZombieModeString("BossRush_ZombieMode_Extraction_ExtractNow", "立即撤离", "Extract Now");
            InjectZombieModeString("BossRush_ZombieMode_Extraction_Continue", "继续战斗", "Keep Fighting");
            InjectZombieModeString("BossRush_ZombieMode_Notify_BeaconNotPreparation", "尸潮信标只能在准备倒计时阶段使用。", "Zombie Tide Beacon only works during preparation countdown.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_BeaconNotZombieMode", "尸潮信标只能在末日丧尸模式中使用。", "Zombie Tide Beacon only works in Zombie Mode.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_BeaconExtractionLocked", "撤离读条进行中，无法使用信标。", "Extraction is in progress; beacon unavailable.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ExtractionBeaconLocked", "信标读条进行中，无法开始撤离。", "Beacon is channeling; extraction unavailable.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_RefreshNoPoints", "净化点数不足以刷新。", "Not enough Purification Points to refresh.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_NpcServiceNoPoints", "净化点数不足。", "Not enough Purification Points.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_RefundedInvitation", "末日丧尸模式未正式开始，已返还尸潮邀请函。", "Zombie Mode did not start; Zombie Tide Invitation refunded.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_RefundedCash", "已退还投入的现金。", "Refunded invested cash.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_StorageFull", "仓储格已满，随身物品已转入仓库收件箱。", "Storage grid is full; carried items were sent to the storage inbox.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_HasBoundItems", "随身物品会在入场后转入仓库。", "Carried items are moved to storage after entry.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_NoSpawnPoints", "该地图暂无可用丧尸刷怪点。", "No spawn points available on this map.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_BackpackFullDropped", "背包已满，奖励掉落在脚下。", "Backpack full; reward dropped at your feet.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_PreparationEnding", "准备期最后 5 秒。", "5 seconds left in preparation.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_BeaconChannelInterrupted", "信标读条已中断。", "Beacon channel interrupted.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_BeaconChannelComplete", "信标读条完成，开始下一波。", "Beacon channel complete. Starting next wave.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_BeaconChannelStarted", "尸潮信标已启动，3 秒后开波。", "Zombie beacon activated. Wave starts in 3 seconds.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ExtractionCash", "剩余净化点数结算为 {0} 现金。", "Remaining Purification Points settled as {0} cash.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_RewardGranted", "获得 {0} 点净化点。", "Gained {0} Purification Points.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_RewardDeliveryFailed", "奖励发放失败，请重试。", "Reward delivery failed. Please try again.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_RewardFallbackPurification", "奖励物品不可用，已改为净化点 +{0}。", "Reward item unavailable; converted to Purification +{0}.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_AttributeMaxHealth", "最大生命强化已累计至 +{0}%。", "Max health bonus is now +{0}%.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_AttributeBonus", "{0} 已累计至 +{1}%。", "{0} is now +{1}%.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ContractPollutionCost", "契约完成：污染 +{0}，净化点 -{1}。", "Pact complete: Pollution +{0}, Purification -{1}.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ContractGearCost", "契约完成：污染 +{0}，净化点 -{1}，获得装备。", "Pact complete: Pollution +{0}, Purification -{1}, gear granted.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ContractHugePurificationCost", "契约完成：污染 +{0}，净化点 -{1}，保险 +30%。", "Pact complete: Pollution +{0}, Purification -{1}, Insurance +30%.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ContractInsuranceCost", "契约完成：污染 +{0}，净化点 -{1}。", "Pact complete: Pollution +{0}, Purification -{1}.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ContractDevilBargainCost", "契约完成：污染 +{0}，净化点 -{1}，保险 +25%。", "Pact complete: Pollution +{0}, Purification -{1}, Insurance +25%.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ContractCursedReloadCost", "契约完成：污染 +{0}，净化点 -{1}，换弹速度提升。", "Pact complete: Pollution +{0}, Purification -{1}, reload speed boosted.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ContractBloodPriceCost", "契约完成：污染 +{0}，净化点 -{1}，已回血。", "Pact complete: Pollution +{0}, Purification -{1}, health restored.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_ContractCursePoolCost", "契约完成：污染 +{0}，净化点 -{1}，随机奖励。", "Pact complete: Pollution +{0}, Purification -{1}, random reward.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_InsuranceKeepOne", "保险生效：失败时随机保留 {0}% 随身物品。", "Insurance active: keep {0}% carried items on failure.");
            InjectZombieModeString("BossRush_ZombieMode_Banner_ExtractionCountdown", "撤离倒计时 <color=yellow>{0}</color> 秒，保持存活！", "Extraction in <color=yellow>{0}</color>s. Stay alive!");
            InjectZombieModeString("BossRush_ZombieMode_Reason_InvitationMissing", "缺少尸潮邀请函。", "Missing Zombie Tide Invitation.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_NotEnoughCash", "现金不足。", "Not enough cash.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_NoEffectiveSpawnPoints", "无有效刷怪点。", "No effective spawn points.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_StorageFull", "仓储格已满，已使用仓库收件箱。", "Storage grid is full; storage inbox was used.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_BlockedTaskOrBoundItems", "随身物品会转入仓库。", "Carried items are moved to storage.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_AnotherBossRushLikeModeActive", "已有 BossRush 类模式进行中。", "Another BossRush-like mode is active.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_InvitationConsumeFailed", "邀请函消耗失败。", "Invitation consume failed.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_CashWithdrawFailed", "现金扣款失败。", "Cash withdrawal failed.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_InventoryTransferFailed", "物品转移失败。", "Inventory transfer failed.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_MapLoadFailed", "地图加载失败。", "Map load failed.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_MapIsolationFailed", "地图隔离失败。", "Map isolation failed.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_SpawnPointCollectionFailed", "刷怪点收集失败。", "Spawn point collection failed.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_BeaconGrantFailed", "尸潮信标发放失败。", "Beacon grant failed.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_InitializationFailed", "模式初始化失败。", "Mode initialization failed.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_StarterChoiceUiClosed", "初始选择 UI 异常关闭。", "Starter choice UI closed unexpectedly.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_StarterChoiceTimedOut", "初始选择超时。", "Starter choice timed out.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_StarterLoadoutFailed", "初始装备发放失败。", "Starter loadout failed.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_PlayerDeath", "玩家死亡。", "Player died.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_ManualExit", "手动退出。", "Manual exit.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_SceneSwitched", "场景已切换。", "Scene switched.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_UnexpectedSceneUnload", "场景被异常卸载。", "Scene unloaded unexpectedly.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_SuccessfulExtraction", "撤离成功。", "Extraction successful.");
            InjectZombieModeString("BossRush_ZombieMode_Reason_Unknown", "未知原因。", "Unknown reason.");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Title_Normal", "第 {0} 波 奖励选择", "Wave {0} Rewards");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Title_Boss", "<color=red>Boss 节点 - 第 {0} 波</color>", "<color=red>Boss Node - Wave {0}</color>");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Info", "净化点: {0}    免费刷新: {1}    付费刷新: {2}", "Purification: {0}    Free Refreshes: {1}    Paid Refresh: {2}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_PointsHeader", "净化点数 {0}", "Purification {0}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_PickButton", "选择", "Pick");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RefreshFree", "免费刷新", "Free Refresh");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RefreshPaid", "付费刷新 -{0}", "Paid Refresh -{0}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RefreshHalfPriced", "下次半价", "Next Half Price");
            InjectZombieModeString("BossRush_ZombieMode_Reward_PurificationPoints", "净化点 +{0}", "Purification +{0}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Heal", "生命回满", "Refill Health");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RandomSupply", "随机补给物品", "Random Supply Item");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RandomHighQualityItem", "随机高品质物品", "Random High-Quality Item");
            InjectZombieModeString("BossRush_ZombieMode_Reward_StarterReroll", "按流派补武器", "Weapon Refill for Loadout");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RandomMeleeWeapon", "随机近战武器", "Random Melee Weapon");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RandomGunWithAmmo", "随机枪械 + 弹药", "Random Gun + Ammo");
            InjectZombieModeString("BossRush_ZombieMode_Reward_AmmoSupply", "弹药补给", "Ammo Supply");
            InjectZombieModeString("BossRush_ZombieMode_Reward_MedicalSupply", "医疗补给", "Medical Supply");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ArmorOrHelmet", "护甲或头盔", "Armor or Helmet");
            InjectZombieModeString("BossRush_ZombieMode_Reward_CurrentNodeFreeRefresh", "本节点免费刷新 +1", "Current Node Free Refresh +1");
            InjectZombieModeString("BossRush_ZombieMode_Reward_NextNodeFreeRefresh", "下节点免费刷新 +1", "Next Node Free Refresh +1");
            InjectZombieModeString("BossRush_ZombieMode_Reward_HalfPricePaidRefresh", "下次付费刷新半价", "Half-Price Paid Refresh");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Attribute_MaxHealth", "最大生命 +10%（本局）", "Max Health +10% (run)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Attribute_MoveSpeed", "移动速度 +5%（本局）", "Move Speed +5% (run)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Attribute_MeleeDamage", "近战伤害 +12%（本局）", "Melee Damage +12% (run)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Attribute_RangedDamage", "远程伤害 +10%（本局）", "Ranged Damage +10% (run)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Attribute_ReloadSpeed", "换弹速度 +10%（本局）", "Reload Speed +10% (run)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Attribute_DamageReduction", "受伤减免 +5%（本局）", "Damage Reduction +5% (run)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_AttributeName_MaxHealth", "最大生命", "Max Health");
            InjectZombieModeString("BossRush_ZombieMode_Reward_AttributeName_MoveSpeed", "移动速度", "Move Speed");
            InjectZombieModeString("BossRush_ZombieMode_Reward_AttributeName_MeleeDamage", "近战伤害", "Melee Damage");
            InjectZombieModeString("BossRush_ZombieMode_Reward_AttributeName_RangedDamage", "远程伤害", "Ranged Damage");
            InjectZombieModeString("BossRush_ZombieMode_Reward_AttributeName_ReloadSpeed", "换弹速度", "Reload Speed");
            InjectZombieModeString("BossRush_ZombieMode_Reward_AttributeName_DamageReduction", "受伤减免", "Damage Reduction");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TempMerchant", "补给终端：下次购买保底高品质", "Supply Terminal: Next Purchase High Quality");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TempNurse", "医疗终端：可花净化点治疗", "Medical Terminal: Spend Purification to Heal");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TempGoblinNpc", "召唤叮当：可花净化点重铸", "Summon Dingdang: Reforge with Purification");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TempNurseNpc", "召唤羽织：可花净化点治疗", "Summon Yuzhi: Heal with Purification");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TempCourierNpc", "召唤阿稳：可花净化点使用服务", "Summon Awen: Services with Purification");
            InjectZombieModeString("BossRush_ZombieMode_Reward_FortificationPack", "给掩体/路障/铁丝网/维修喷剂", "Cover/Roadblock/Wire/Repair Spray Pack");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ContractPollutionDeal", "污染 +1/+2，净化点 -80/-150", "Pollution +1/+2, Purification -80/-150");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ContractGearDeal", "污染 +2/+3，净化点 -60/-120，给高阶枪械和护甲/头盔", "Pollution +2/+3, Purification -60/-120, High-Tier Gun and Armor/Helmet");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ContractHugePurification", "污染 +3，净化点 -200，保险 +30%", "Pollution +3, Purification -200, Insurance +30%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ContractInsurance", "污染 +2，净化点 -80，失败指定保留+随机20%", "Pollution +2, Purification -80, Keep Chosen Item + Random 20% on Failure");
            InjectZombieModeString("BossRush_ZombieMode_Reward_InsuranceKeepOne", "失败时保留指定物品，并随机保留10%", "On Failure: Keep Chosen Item and Random 10%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_InsuranceRandom10", "失败时随机保留 10% 物品", "On Failure: Keep Random 10% of Items");
            InjectZombieModeString("BossRush_ZombieMode_Reward_InsuranceRandom20", "失败时随机保留 20% 物品", "On Failure: Keep Random 20% of Items");
            InjectZombieModeString("BossRush_ZombieMode_Reward_InsuranceNearFull", "污染 +5，失败时随机保留 80% 物品", "Pollution +5, On Failure Keep Random 80% of Items");
            InjectZombieModeString("BossRush_ZombieMode_Reward_MapEventHighValueAirdrop", "立即获得高品质枪/近战/护甲补给", "Immediately Gain High-Quality Gun/Melee/Armor Supply");
            InjectZombieModeString("BossRush_ZombieMode_Reward_MapEventEliteSquad", "下波额外刷 3 个精英敌人", "Next Wave Spawns 3 Extra Elite Enemies");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Tradeoff_MoveSpeed", "代价：移动速度 -{0}%", "Cost: Move Speed -{0}%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Tradeoff_GunDamage", "代价：枪械伤害 -{0}%", "Cost: Gun Damage -{0}%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Tradeoff_ReloadSpeed", "代价：换弹速度 -{0}%", "Cost: Reload Speed -{0}%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Tradeoff_DamageTaken", "代价：承受伤害 +{0}%", "Cost: Damage Taken +{0}%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Tradeoff_Pollution", "代价：污染 +{0}", "Cost: Pollution +{0}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Tradeoff_MaxHealth", "代价：最大生命 -{0}%", "Cost: Max Health -{0}%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Tradeoff_Purification", "代价：净化点 -{0}", "Cost: Purification -{0}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectilePenetration", "子弹穿透 +1（代价：换弹速度 -6%）", "Bullet Penetration +1 (Cost: Reload Speed -6%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileBurn", "命中有概率点燃：燃烧概率 +35%，最高75%（代价：枪械伤害 -4%）", "Chance to ignite on hit: Burn Chance +35%, Max 75% (Cost: Gun Damage -4%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileCold", "命中有概率减速：冰霜概率 +25%，最高60%（代价：换弹速度 -5%）", "Chance to slow on hit: Frost Chance +25%, Max 60% (Cost: Reload Speed -5%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectilePoison", "命中有概率中毒：毒化概率 +35%，最高75%（代价：最大生命 -5%）", "Chance to poison on hit: Poison Chance +35%, Max 75% (Cost: Max Health -5%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileArmorBreak", "穿甲 +25%，破甲 +10%（代价：承受伤害 +6%）", "Armor Pierce +25%, Armor Break +10% (Cost: Damage Taken +6%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_MutatorCritFocus", "暴击率 +15%，最多45%（代价：换弹速度 -8%）", "Crit Rate +15%, Max 45% (Cost: Reload Speed -8%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TriggerLifesteal", "命中回血 I：10% 概率恢复 1 生命（代价：移动速度 -11%）", "Hit Heal I: 10% chance to restore 1 HP (Cost: Move Speed -11%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TriggerLifestealMedium", "命中回血 II：20% 概率恢复 1 生命（代价：移动速度 -22%）", "Hit Heal II: 20% chance to restore 1 HP (Cost: Move Speed -22%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TriggerLifestealLarge", "命中回血 III：30% 概率恢复 1 生命（代价：移动速度 -33%）", "Hit Heal III: 30% chance to restore 1 HP (Cost: Move Speed -33%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TriggerCritBurst", "暴击时爆炸，本次伤害30%起（代价：承受伤害 +8%）", "Crits Explode for 30%+ Hit Damage (Cost: Damage Taken +8%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TriggerPurificationSiphon", "击杀额外掉净化星（代价：污染 +1）", "Kills Drop Extra Purification Stars (Cost: Pollution +1)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TriggerSecondWind", "击杀回血，每层 2（代价：最大生命 -6%）", "Heal 2 per Stack on Kill (Cost: Max Health -6%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_TriggerDoomPulse", "累计击杀触发 3 次爆炸（代价：承受伤害 +10%）", "Kill Streak Triggers 3 Explosions (Cost: Damage Taken +10%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_MutatorBulletTime", "生命低于25%触发 1 秒子弹时间（代价：承受伤害 +12%）", "Below 25% HP Triggers 1s Bullet Time (Cost: Damage Taken +12%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_MutatorGuardianShield", "满血时物理伤害 -25%（代价：枪械伤害 -5%）", "At Full HP, Physical Damage -25% (Cost: Gun Damage -5%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_MutatorQuickReload", "换弹速度 +25%（代价：枪械伤害 -5%）", "Reload Speed +25% (Cost: Gun Damage -5%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_MutatorDashBoost", "翻滚速度 +25%（代价：枪械伤害 -5%）", "Dash Speed +25% (Cost: Gun Damage -5%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_BattlefieldAmmoRain", "每45秒给 60发弹药（代价：净化点 -120）", "Gain 60 Ammo Every 45s (Cost: Purification -120)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ContractDevilBargain", "污染 +3/+4，净化点 -120/-200，保险 +25%", "Pollution +3/+4, Purification -120/-200, Insurance +25%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ContractCursedReload", "污染 +2/+3，净化点 -60/-100，换弹速度 +35%/+45%", "Pollution +2/+3, Purification -60/-100, Reload Speed +35%/+45%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ContractBloodPrice", "污染 +2/+3，净化点 -50/-80，立即回血30%/45%", "Pollution +2/+3, Purification -50/-80, Heal 30%/45% Now");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ContractCursePool", "污染 +3/+4，净化点 -100/-150，随机获得保底/保险+装备", "Pollution +3/+4, Purification -100/-150, Random Guarantee or Insurance+Gear");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileTrident", "当前枪至少3发散射，单颗伤害分摊（代价：换弹速度 -7%）", "Current gun fires at least 3 spread shots, damage split per pellet (Cost: Reload Speed -7%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileShotgunSpray", "当前枪至少5发散射，单颗伤害分摊（代价：枪械伤害 -5%）", "Current gun fires at least 5 spread shots, damage split per pellet (Cost: Gun Damage -5%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileStasis", "命中普通敌人减速65% 1秒（代价：移动速度 -5%）", "Hit Normal Enemies: Slow 65% for 1s (Cost: Move Speed -5%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileRicochet", "命中后追加一发追向附近敌人的子弹，命中后弹向附近敌人（代价：换弹速度 -6%）", "On hit, add a shot that seeks and ricochets to a nearby enemy (Cost: Reload Speed -6%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileFork", "命中后分裂：命中后分出两发支援弹（2发斜向子弹，代价：枪械伤害 -5%）", "On hit, split into two support rounds (2 angled shots, Cost: Gun Damage -5%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileReturn", "命中后回射玩家方向：命中后向你飞回支援弹（代价：最大生命 -5%）", "On hit, fire a support round back toward you (Cost: Max Health -5%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileHelix", "子弹螺旋飞行（代价：移动速度 -6%）", "Bullets Fly in a Helix (Cost: Move Speed -6%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_ProjectileTrail", "子弹沿途造成小范围伤害（代价：承受伤害 +6%）", "Bullets deal small area damage along the path (Cost: Damage Taken +6%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_BattlefieldPurgeAura", "每3秒造成身边范围伤害（代价：污染 +1）", "Area Damage Around You Every 3s (Cost: Pollution +1)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_BattlefieldCurseTrap", "每18秒在前方延迟爆炸（代价：最大生命 -8%）", "Delayed Blast Ahead Every 18s (Cost: Max Health -8%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_BattlefieldBlackHole", "每12秒定期在前方生成牵引场（前方牵引黑洞，代价：净化点 -180）", "Every 12s, create a pull field ahead (black hole, Cost: Purification -180)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_BattlefieldGravityDrag", "每16秒定期把小怪往前方拉（前方弱牵引区，代价：移动速度 -7%）", "Every 16s, pull small enemies ahead (weak pull zone, Cost: Move Speed -7%)");
            InjectZombieModeString("BossRush_ZombieMode_Reward_FreeQuota", "免费 {0}/{1}", "Free {0}/{1}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_NextPaidPrice", "付费下次价 {0}", "Next paid price {0}");
            InjectZombieModeString("BossRush_ZombieMode_Npc_TempMerchant", "补给终端已部署", "Supply Terminal deployed");
            InjectZombieModeString("BossRush_ZombieMode_Npc_TempNurse", "医疗终端已部署", "Medical Terminal deployed");
            InjectZombieModeString("BossRush_ZombieMode_Npc_TempGoblinNpc", "叮当已抵达安全区", "Dingdang reached the safe zone");
            InjectZombieModeString("BossRush_ZombieMode_Npc_TempNurseNpcReal", "羽织已抵达安全区", "Yuzhi reached the safe zone");
            InjectZombieModeString("BossRush_ZombieMode_Npc_TempCourierNpc", "阿稳已抵达安全区", "Awen reached the safe zone");
            InjectZombieModeString("BossRush_ZombieMode_Notify_TempMerchantGuarantee", "补给终端高品质保底已就绪", "Supply Terminal high-quality guarantee is ready");
            InjectZombieModeString("BossRush_ZombieMode_Npc_InteractMerchant", "按 {0} 使用补给终端", "Press {0} to use Supply Terminal");
            InjectZombieModeString("BossRush_ZombieMode_Npc_InteractNurse", "按 {0} 使用医疗终端", "Press {0} to use Medical Terminal");
            InjectZombieModeString("BossRush_ZombieMode_Npc_ServicePrice", "价格 {0}", "Price {0}");
            InjectZombieModeString("BossRush_ZombieMode_Npc_ServiceRemaining", "剩余 {0}", "Left {0}");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Close", "关闭", "Close");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_RandomAmmo", "随机弹药", "Random Ammo");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_RandomMedical", "随机医疗品", "Random Medical");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_RandomFood", "随机食物", "Random Food");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_RandomDrink", "随机饮料", "Random Drink");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_RandomMelee", "随机近战武器", "Random Melee Weapon");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_RandomGun", "随机枪械", "Random Gun");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_RandomArmor", "随机护甲", "Random Armor");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_RandomHelmet", "随机头盔", "Random Helmet");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Gun", "丧尸模式枪械", "Zombie Mode Guns");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Melee", "丧尸模式近战", "Zombie Mode Melee");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Accessory", "丧尸模式配件", "Zombie Mode Accessories");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Bullet", "丧尸模式子弹", "Zombie Mode Ammo");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Helmat", "丧尸模式头盔", "Zombie Mode Helmets");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Armor", "丧尸模式护甲", "Zombie Mode Armor");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Backpack", "丧尸模式背包", "Zombie Mode Backpacks");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Totem", "丧尸模式图腾", "Zombie Mode Totems");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Mask", "丧尸模式面具/耳机", "Zombie Mode Masks and Headsets");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Medical", "丧尸模式医疗品", "Zombie Mode Medical");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Food", "丧尸模式食物", "Zombie Mode Food");
            InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_Bait", "丧尸模式诱饵", "Zombie Mode Bait");
            InjectZombieModeString("BossRush_ZombieMode_Npc_NurseService_HealHalf", "治疗：恢复缺失生命 50%", "Heal: Restore 50% Missing HP");
            InjectZombieModeString("BossRush_ZombieMode_Npc_NurseService_HealFull", "完全治疗", "Full Heal");
            InjectZombieModeString("BossRush_ZombieMode_Npc_NurseService_Detox", "解毒", "Detox");
            InjectZombieModeString("BossRush_ZombieMode_Npc_NurseService_StopBleed", "止血", "Stop Bleeding");
            InjectZombieModeString("BossRush_ZombieMode_Npc_NurseService_FirstAid", "急救", "First Aid");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Attribute", "属性", "Attribute");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Equipment", "装备", "Equipment");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Economy", "经济", "Economy");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Npc", "NPC 服务", "NPC");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Fortification", "工事", "Fortification");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Curse", "契约", "Pact");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Insurance", "保险", "Insurance");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Event", "事件", "Event");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_MapEvent", "事件", "Event");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_ProjectileMod", "弹道", "Projectile");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Trigger", "触发", "Trigger");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Mutator", "变异", "Mutator");
            InjectZombieModeString("BossRush_ZombieMode_RewardCat_Battlefield", "战场", "Battlefield");
            InjectZombieModeString("BossRush_ZombieMode_Boss_Titan", "巨坦", "Titan");
            InjectZombieModeString("BossRush_ZombieMode_Boss_Hunter", "极速追猎", "Hunter");
            InjectZombieModeString("BossRush_ZombieMode_Boss_Splitter", "分裂尸群", "Splitter");
            InjectZombieModeString("BossRush_ZombieMode_Boss_Shielder", "护盾统御", "Shielder");
            InjectZombieModeString("BossRush_ZombieMode_Boss_Corruptor", "腐蚀地面", "Corruptor");
            InjectZombieModeString("BossRush_ZombieMode_BossSkill_TitanShockwave", "巨坦震荡波", "Titan Shockwave");
            InjectZombieModeString("BossRush_ZombieMode_BossSkill_TitanFortify", "巨坦硬化", "Titan Fortify");
            InjectZombieModeString("BossRush_ZombieMode_BossSkill_HunterDash", "追猎冲刺", "Hunter Dash");
            InjectZombieModeString("BossRush_ZombieMode_BossSkill_SplitterSummon", "尸群分裂", "Splitter Swarm");
            InjectZombieModeString("BossRush_ZombieMode_BossSkill_ShielderSelfShield", "护盾自保", "Self Shield");
            InjectZombieModeString("BossRush_ZombieMode_BossSkill_ShielderGroupShield", "群体护盾", "Group Shield");
            InjectZombieModeString("BossRush_ZombieMode_BossSkill_CorruptorZone", "腐蚀领域", "Corruption Zone");
            InjectZombieModeString("BossRush_ZombieMode_Elite", "精英丧尸", "Elite Zombie");
            InjectZombieModeString("BossRush_ZombieMode_Special_Sprinter", "冲刺丧尸", "Sprinter Zombie");
            InjectZombieModeString("BossRush_ZombieMode_Special_Exploder", "自爆丧尸", "Exploder Zombie");
            InjectZombieModeString("BossRush_ZombieMode_Special_Plague", "毒疫丧尸", "Plague Zombie");
            InjectZombieModeString("BossRush_ZombieMode_Special_Summoner", "召唤丧尸", "Summoner Zombie");
            InjectZombieModeString("BossRush_ZombieMode_Special_Harasser", "骚扰丧尸", "Harasser Zombie");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Swift", "迅捷", "Swift");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Frenzied", "狂暴", "Frenzied");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Tough", "厚皮", "Tough");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Stalwart", "刚硬", "Stalwart");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Regenerating", "再生", "Regenerating");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Burst", "爆裂", "Burst");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Plague", "毒疫", "Plague");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Commander", "号令", "Commander");
            InjectZombieModeString("BossRush_ZombieMode_Affix_ToxicAura", "污染光环", "Toxic Aura");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Splitting", "分裂", "Splitting");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Shielded", "护盾", "Shielded");
            InjectZombieModeString("BossRush_ZombieMode_Affix_Adaptive", "反制", "Adaptive");
            InjectZombieModeString("BossRush_ZombieMode_Settle_SuccessTitle", "<color=#22aaff>撤离成功</color>", "<color=#22aaff>Extraction Successful</color>");
            InjectZombieModeString("BossRush_ZombieMode_Settle_PointsToCash", "剩余净化点数 {0} → 现金 {0}", "Remaining Purification {0} → Cash {0}");
            InjectZombieModeString("BossRush_ZombieMode_Settle_FailTitle", "<color=#ff5544>本局失败</color>", "<color=#ff5544>Run Failed</color>");
            InjectZombieModeString("BossRush_ZombieMode_Settle_FailReason", "失败原因：{0}", "Reason: {0}");
            InjectZombieModeString("BossRush_ZombieMode_Settle_PointsLost", "净化点数已全部清空。", "All Purification Points lost.");
            InjectZombieModeString("BossRush_ZombieMode_Settle_InsuranceSaved", "保险保留 {0} 件物品。", "Insurance saved {0} items.");
        }

        private static void InjectZombieModeString(string key, string zh, string en)
        {
            LocalizationHelper.InjectLocalization(key, L10n.T(zh, en));
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
    }
}
