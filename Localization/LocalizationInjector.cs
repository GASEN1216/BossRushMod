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
        private const string TICKET_DESC_CN = "进入 BossRush 的凭证。带上船票，在地图选择界面出发。\n空装入场可开启白手起家。死亡后掉落的物品会被清走，请看清所选模式的规则。";
        private const string TICKET_DESC_EN = "Entry ticket for BossRush. Bring it to map selection.\nEnter without gear for Rags to Riches. Items dropped on death are cleared; check your mode's rules.";

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
        private const string REFORGE_DESC_CN = "选一件装备重铸。投入越多，出好属性的机会越大。\n高品质装备更容易获得高属性。";
        private const string REFORGE_DESC_EN = "Choose gear to reforge. More money improves the odds.\nHigher-quality gear is more likely to roll high stats.";
        private const string REFORGE_NO_ITEM_SELECTED_CN = "请先选择一件装备";
        private const string REFORGE_NO_ITEM_SELECTED_EN = "Select a piece of gear first";
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
            new string[] { "新面孔？我是阿稳，跑这片的快递。", "New face? I'm Awen. I run deliveries around here." },
            new string[] { "哪条路能走，哪儿有麻烦，我多少知道些。", "I know the routes. And where the trouble is." },
            new string[] { "这本书拿着，装备和玩法都能查。", "Take this book. It covers the gear and the modes." },
            new string[] { "东西带不动就找我寄存，价钱写得明白。", "Need storage? Come find me. The prices are posted." }
        };

        // ============================================================================
        // 哥布林叮当5级故事对话（大对话系统）- 叮当的过去
        // ============================================================================
        private static readonly string[][] GOBLIN_STORY_LEVEL5_DIALOGUES = new string[][]
        {
            new string[] { "你真想听？叮当还没跟别人说过。", "You want to hear it? Dingdang hasn't told anyone." },
            new string[] { "叮当从J-Lab出来。那时他们只叫叮当007号。", "Dingdang came from J-Lab. They called Dingdang Number 007." },
            new string[] { "这张笑脸也是他们做的，说这样招人喜欢。", "They made this smile too. Said people would like it." },
            new string[] { "想哭的时候，脸还是这样。叮当改不了。", "Even when Dingdang wants to cry, the face stays like this." },
            new string[] { "逃出来以后，别的哥布林又叫叮当怪胎。", "After Dingdang escaped, the other goblins called Dingdang a freak." },
            new string[] { "你没笑叮当。那……下回叮当还跟你说。", "You didn't laugh. Maybe Dingdang will tell you more next time." }
        };

        // ============================================================================
        // 哥布林叮当10级故事对话（大对话系统）- 叮当的心愿
        // ============================================================================
        private static readonly string[][] GOBLIN_STORY_LEVEL10_DIALOGUES = new string[][]
        {
            new string[] { "锤子先放下。叮当有件事想说。", "Hammer down for a moment. Dingdang has something to say." },
            new string[] { "叮当还留着实验室的牌子，上面就一个号。", "Dingdang kept the lab tag. Just a number on it." },
            new string[] { "以前来的人只问价钱。你还会问叮当累不累。", "People used to ask only the price. You ask if Dingdang is tired." },
            new string[] { "J-Lab为什么要造出叮当？叮当还想弄明白。", "Why did J-Lab make Dingdang? Dingdang still wants to know." },
            new string[] { "哪天回去找答案，你能陪叮当吗？", "If Dingdang goes back for answers, will you come along?" },
            new string[] { "嗯，说定了。这回叮当是真的想笑。", "Then it's settled. This time Dingdang wants to smile." }
        };

        // ============================================================================
        // 护士羽织5级故事对话（大对话系统）- 羽织的诅咒
        // ============================================================================
        private static readonly string[][] NURSE_STORY_LEVEL5_DIALOGUES = new string[][]
        {
            new string[] { "坐一会儿吧。有件事，我想告诉你。", "Sit a while. There's something I want to tell you." },
            new string[] { "我以前也是普通鸭子。这双腿是实验室弄的。", "I used to be an ordinary duck. The lab gave me these legs." },
            new string[] { "他们说实验成功了，就把我赶下了手术台。", "They called it a success and sent me off the operating table." },
            new string[] { "腿上这些紫色裂纹，一直在扩散，也一直疼。", "These purple cracks keep spreading. They hurt all the time." },
            new string[] { "给别人包扎的时候，手可不能跟着抖。", "I can't let my hands shake while I'm dressing someone else's wound." },
            new string[] { "别替我担心。能说出来，已经好受些了。", "Don't worry about me. It helps to say it out loud." }
        };

        // 快递员随机对话（中英文对照）
        // 包含：全部mod内容的角色口吻提示——游戏模式、Boss、装备、物品、地图、NPC、系统、日常世界观
        private static readonly string[][] COURIER_DIALOGUES = new string[][]
        {
            // ============ 快递业务相关 ============
            new string[] { "补给到了。可乐给我留一瓶，要有糖的。", "Supplies are here. Save me a cola. With sugar." },
            new string[] { "东西放我这儿，腾出手来好赶路。", "Leave your things with me. Travel light." },
            new string[] { "先付钱，再发货。老板盯着账呢。", "Pay first, then I ship. The boss checks the books." },
            new string[] { "下一波也得活着回来，我可不送讣告。", "Come back alive next wave. I don't deliver obituaries." },
            new string[] { "签个字吧。爪印也行，别把单子戳穿了。", "Sign here. A paw print works. Just don't tear the slip." },

            // ============ 焚天龙皇Boss相关 ============
            new string[] { "我送货撞见过龙皇。那单差点成了最后一单。", "I met the Dragon King on a delivery. Nearly my last one." },
            new string[] { "龙皇的鳞片值钱，剥鳞片的活儿可别找我。", "Dragon King scales sell well. Don't ask me to peel them off." },
            new string[] { "龙皇一发火，半边天都红了。快递差点烤熟。", "The Dragon King lit up half the sky. Nearly roasted my parcels." },
            new string[] { "龙王之冕和龙王鳞铠？得去找龙皇拿。", "Want the Dragon King's Crown and Scale Mail? Ask the dragon." },
            new string[] { "龙皇会变招。别拿上一招的空当赌下一招。", "The Dragon King changes tactics. Keep watching it." },

            // ============ 火龙相关 ============
            new string[] { "离那头火龙远点。烧坏了包裹，你赔啊？", "Keep away from that fire dragon. You paying for burnt parcels?" },
            new string[] { "火龙怕毒。备点带毒的家伙再去。", "Fire dragons hate poison. Bring some." },
            new string[] { "火龙也怕冰。上回我亲眼看它栽下来了。", "Fire dragons hate ice too. Saw one crash myself." },
            new string[] { "该死的火龙，我的快递又飞了！", "That blasted dragon! There go my parcels!" },
            new string[] { "火龙开始嘀咕了？先跑再说。", "Dragon's muttering? Run first, ask later." },
            new string[] { "别贴火龙太近，炸一下够你受的。", "Don't hug the fire dragon. It explodes." },

            // ============ 叮当NPC相关 ============
            new string[] { "装备要重铸，找叮当。它的锤子比嘴靠谱。", "Need a reforge? See Dingdang. Good hammer, loud mouth." },
            new string[] { "叮当一直在笑。我倒没见过它歇口气。", "Dingdang's always smiling. Never seems to get a break." },
            new string[] { "找叮当办事，顺手带点它喜欢的礼物。", "Seeing Dingdang? Bring a gift it likes." },
            new string[] { "叮当又画涂鸦了。这回没画在我箱子上，谢天谢地。", "More graffiti from Dingdang. At least it's not on my crates this time." },

            // ============ 护士NPC相关 ============
            new string[] { "羽织的药材单刚送到。她记账比我还细。", "Yu Zhi's medicine order is in. She counts every last packet." },
            new string[] { "受伤就找羽织。她嘴上凶，手上稳。", "Hurt? See Yu Zhi. Sharp tongue, steady hands." },
            new string[] { "别老盯着羽织的腿看。送药又不用看腿。", "Quit staring at Yu Zhi's legs. You're here for medicine." },
            new string[] { "别在羽织面前逞强，她一眼就能看出你瘸了。", "Don't play tough with Yu Zhi. She can see you limping." },

            // ============ 好感度系统相关（用角色口吻传递信息） ============
            new string[] { "叮当和羽织都记人情。别只在用得着时才去。", "Dingdang and Yu Zhi remember kindness. Visit between jobs too." },
            new string[] { "送礼先打听喜好。别拿自己的口味替人做主。", "Ask what they like before buying gifts." },
            new string[] { "熟了再问私事。谁乐意跟陌生人掏心窝子？", "Get to know people before asking about their past." },

            // ============ 重铸系统相关 ============
            new string[] { "重铸多花钱，出好属性的机会大些。可不包出。", "More money improves your reforge odds. No guarantees." },
            new string[] { "重铸也看运气，留点路费再下锤。", "Reforging is a gamble. Keep some travel money." },
            new string[] { "叮当的工坊又缺材料了。我这腿就没闲过。", "Dingdang needs more supplies. My feet never get a rest." },

            // ============ 飞行图腾相关 ============
            new string[] { "飞行图腾能让人飞？借我送两单呗。", "A totem that lets you fly? Lend it to me for a few deliveries." },
            new string[] { "飞行图腾收起来之前，先看看脚下多高。", "Check how far down it is before putting that totem away." },
            new string[] { "有飞行图腾也得看路，天上可没护栏。", "Watch where you fly. No railings up there." },

            // ============ 成就系统相关 ============
            new string[] { "成就勋章收了几枚？让我开开眼。", "How many medals now? Let's see them." },
            new string[] { "成就奖励记得去领，别光顾着打下一场。", "Claim your achievement rewards before the next fight." },
            new string[] { "难拿的勋章慢慢来。我送急件也得认路。", "Take your time with the hard medals. Even express needs a route." },

            // ============ 新物品相关 ============
            new string[] { "砖石和钻石都能叫叮当来，可别送错了。", "Brick or diamond, both call Dingdang. Choose carefully." },
            new string[] { "喜欢的属性先用冷淬液锁住，再重铸。", "Lock your favorite stats with Cold Quench Fluid before reforging." },
            new string[] { "叮当的画你收着吧。别拿来垫箱子，它会急。", "Keep Dingdang's drawings. Don't use them to pack crates." },

            // ============ 世界观/日常 ============
            new string[] { "这路又坑又洼。今天的蛋怕是要送成蛋液。", "These potholes... I'll be delivering scrambled eggs." },
            new string[] { "我就认两样：单号，还有回家的路。", "Two things I keep track of: parcel numbers and the way home." },
            new string[] { "天都快塌了，老板还问我怎么迟到。", "Sky's falling. Boss still wants to know why I'm late." },
            new string[] { "离紫毒远点，标签泡烂了我还怎么对账？", "Keep that poison off my labels. I need to read those numbers." },

            // ============ 标准BossRush模式 ============
            new string[] { "带船票选张图，就能去竞技场。难度看路牌。", "Take a ticket and pick a map. Set the difficulty at the signpost." },
            new string[] { "竞技场要补弹、修甲，先去路牌附近看看。", "Need ammo or repairs in the arena? Check by the signpost." },
            new string[] { "通关先等一等，奖励箱还没落地呢。", "Cleared it? Wait for the reward crate to land." },
            new string[] { "打完记得走撤离点，别在场上瞎转悠。", "Done fighting? Use the extraction point." },
            new string[] { "前面的波次拿来热身，狠角色还在后头。", "Warm up in the early waves. The nasty ones come later." },

            // ============ 无间炼狱 ============
            new string[] { "无间炼狱没个头，撑不住就找机会撤。", "Infinite Hell keeps going. Leave before it gets the better of you." },
            new string[] { "无间炼狱百波有大奖。先活到那儿再惦记。", "Big reward at wave 100 in Infinite Hell. Get there alive first." },
            new string[] { "无间炼狱的战利品折成钱，省得我搬箱子。", "Infinite Hell turns loot into cash. Less hauling for me." },
            new string[] { "无间炼狱每五波有奖励，记着去看。", "Rewards every five waves in Infinite Hell. Keep an eye out." },
            new string[] { "无间炼狱攒了多少钱，路牌上能看。", "Check the signpost for your Infinite Hell cash pool." },

            // ============ 白手起家 ============
            new string[] { "白手起家得空着身子进，进去了再找装备。", "Go into Rags to Riches with nothing. Find your gear inside." },
            new string[] { "白手起家先搜小兵的装备，别急着往后冲。", "Loot the grunts in Rags to Riches. Gear up before pushing on." },
            new string[] { "后面的敌人穿得越来越好，扒下来就是你的。", "Later enemies wear better gear. Help yourself when they fall." },
            new string[] { "白手起家发什么就用什么，先站稳再挑。", "Use what Rags to Riches gives you. Be picky once you're safe." },

            // ============ 划地为营 ============
            new string[] { "划地为营先认旗子，别一上来就打自己人。", "Check the flags in Zone Defense. Don't shoot your own side." },
            new string[] { "划地为营缺补给？找神秘商人，记得带钱。", "Short on supplies in Zone Defense? Find the merchant. Bring cash." },
            new string[] { "带爷的营旗？那可没人跟你一伙了。", "Taking the Lone Wolf flag? Nobody's on your side." },
            new string[] { "划地为营能叫煤球帮忙。个头小，下手可不轻。", "Meiqiu can help in Zone Defense. Small, but hits hard." },
            new string[] { "挑衅烟雾弹能惹来Boss。扔之前先想好退路。", "Taunt Smoke brings bosses. Plan your exit before throwing it." },
            new string[] { "划地为营倒下的Boss越多，剩下的越难缠。", "Each fallen boss makes the survivors tougher in Zone Defense." },

            // ============ 血猎追击 ============
            new string[] { "血猎追击会掉血，杀Boss能续命。别停太久。", "Blood Hunt drains your health. Boss kills keep you going." },
            new string[] { "血猎追击越往后越难熬，记好撤离点在哪。", "Blood Hunt gets rougher over time. Know your extraction route." },
            new string[] { "血猎追击能摆工事，别等被围了才想起来。", "Set up your Blood Hunt defenses before you're surrounded." },
            new string[] { "血猎的悬赏印记得撤离后才换奖励。别贪。", "Extract to cash in your Blood Hunt bounty marks. Don't get greedy." },
            new string[] { "血猎开场先找装备，准备时间可不等人。", "Find gear early in Blood Hunt. Prep time won't wait." },

            // ============ 龙裔遗族Boss ============
            new string[] { "龙裔遗族没龙皇那么凶，也够你忙一阵的。", "The Dragon Descendant isn't the King. Still keeps you busy." },
            new string[] { "赤龙首和焰鳞甲，都能从龙裔遗族那儿拿。", "The Dragon Descendant drops the Crimson Helm and Flame Scale Armor." },
            new string[] { "先找龙裔遗族练练手，再惦记龙皇吧。", "Try the Dragon Descendant before taking on the King." },

            // ============ 龙裔套装 ============
            new string[] { "龙裔和龙王套装都能把火伤转成回血。得穿齐。", "Both dragon sets turn fire damage into healing. Wear the full set." },
            new string[] { "龙裔套穿齐，双击方向键就能冲出去。", "Wear the dragon set and double-tap a direction to dash." },

            // ============ 龙王套装 ============
            new string[] { "龙王套冲刺会留下岩浆，路过都烫脚。", "The Dragon King set leaves lava when you dash. Watch your feet." },
            new string[] { "龙王套先冲六米，还能再接三米。别冲过头。", "Dragon King set: a six-meter dash, then three more. Mind the edge." },

            // ============ 逆鳞 ============
            new string[] { "逆鳞能救急，用一次就碎。别拿它试着玩。", "Reverse Scale can save you once. Don't waste it testing your luck." },
            new string[] { "打龙皇前带个逆鳞，多少有个照应。", "Pack a Reverse Scale before facing the Dragon King." },

            // ============ 焚皇断界戟 ============
            new string[] { "龙皇那把焚皇断界戟，抡起来可别忘了躲招。", "Swinging the Dragon King's halberd? You still need to dodge." },

            // ============ 龙息 ============
            new string[] { "龙息能从龙裔遗族身上出，拿到了别乱卖。", "The Dragon Descendant can drop Dragon Breath. Keep it if you get one." },

            // ============ 霜之哀伤 ============
            new string[] { "霜之哀伤右键能叫亡灵帮忙，省点自己的力气。", "Right-click with Frostmourne to call undead help." },
            new string[] { "去雪地带霜之哀伤，能挡些寒气。", "Frostmourne helps with the cold on snow maps." },

            // ============ 焚天龙铳 ============
            new string[] { "龙皇抬起焚天龙铳的时候，我劝你先找掩体。", "When the Dragon King raises its cannon, find cover." },

            // ============ 地图 ============
            new string[] { "新手先去DEMO终极挑战，场地平，好看清路。", "Start with DEMO Ultimate Challenge. Flat ground, clear sightlines." },
            new string[] { "零度挑战是雪地，进场先检查防寒装备。", "Zero Challenge is snowy. Check your cold protection on arrival." },
            new string[] { "J-Lab的单我不爱送，总觉得背后有人看。", "I hate J-Lab deliveries. Always feels like someone's watching." },
            new string[] { "迷宫拐角多，别光盯着眼前那个Boss。", "Lots of corners in the Maze. Watch more than the boss in front." },
            new string[] { "农场镇地方宽，跑得开，也容易被远处盯上。", "Farm Town has room to run. Also room to get spotted." },

            // ============ 死亡亡魂 ============
            new string[] { "死过的地方可能有你的亡魂，连装备都像你。", "Your wraith may haunt where you died. Wears your gear, too." },
            new string[] { "亡魂穿着你的旧装备。自己的本事，自己当心。", "That wraith wears your old gear. You know what it can do." },
            new string[] { "打赢亡魂，这笔旧账就清了。下次别再欠。", "Beat your wraith and that debt is settled. Don't run up another." },

            // ============ 许愿台 ============
            new string[] { "有想说的，去基地许愿台留个条。有人看的。", "Got a wish? Leave a note at the base fountain. Someone reads them." },

            // ============ 婚姻系统 ============
            new string[] { "想求婚，先处好关系，再带钻石戒指去。", "Thinking of proposing? Build a bond, then bring a diamond ring." },
            new string[] { "婚后的礼物每天别忘了领。人家特意留的。", "Don't forget your spouse's daily gift. They saved it for you." },
            new string[] { "结了婚还乱送戒指？这单我可不替你解释。", "Married and giving rings away? I'm not explaining that one for you." },
            new string[] { "离婚会把好感清零，想好了再开口。", "Divorce resets affinity to zero. Think it through." },
            new string[] { "配偶能陪你出门，可别只把人家当帮手。", "Your spouse can travel with you. Treat them as more than backup." },

            // ============ 安神滴剂 ============
            new string[] { "安神滴剂能清负面状态，药包里留一瓶。", "Calming Drops clear debuffs. Keep a bottle in your medkit." },

            // ============ 平安护身符 ============
            new string[] { "羽织的平安护身符能救命，可不是每次都灵。", "Yu Zhi's Peace Charm can save you. Don't count on it every time." },

            // ============ 钻石戒指 ============
            new string[] { "叮当那儿卖钻石戒指。买好了，别又让我转交。", "Dingdang sells diamond rings. Deliver that one yourself." },

            // ============ 快递牌 ============
            new string[] { "快递牌能把东西寄回家，急用时翻翻背包。", "An Express Token ships your things home. Check your bag in a pinch." },

            // ============ 扫箱令 ============
            new string[] { "划地为营和血猎追击会奖扫箱令，留着叫我。", "Zone Defense and Blood Hunt award Sweep Tokens. Use one to call me." },

            // ============ 荒野号角 ============
            new string[] { "荒野号角能叫坐骑。跑远路，总比靠两条腿强。", "The Wild Horn calls a mount. Beats walking long distances." },

            // ============ Boss筛选器 ============
            new string[] { "Ctrl+F10开Boss筛选器，出发前先挑好对手。", "Ctrl+F10 opens the Boss Filter. Pick your opponents before leaving." },

            // ============ 配置选项 ============
            new string[] { "波次间隔和Boss强度能调，挑个自己打得动的。", "Adjust wave gaps and boss strength to suit you." },

            // ============ 掉落/战利品 ============
            new string[] { "箱子该拿就拿，别打完一转身把战利品忘了。", "Check the crates. Don't walk off and leave your loot behind." },
            new string[] { "各模式的死亡规矩不一样，进场前看清楚。", "Death rules vary by mode. Read them before entering." },
            new string[] { "箱子堆太多就清一清，别把自己的路堵死。", "Clear those crates before you block your own escape." },

            // ============ 成就系统补充 ============
            new string[] { "按L看成就，完成了记得领奖金。", "Press L for achievements. Claim the rewards you've earned." },
            new string[] { "成就勋章能在商人那免费领，别漏了。", "Get your free achievement medal from the merchant." },

            // ============ 营旗/血猎收发器 ============
            new string[] { "营旗认准颜色再带，别进了场才认错队伍。", "Check your flag before leaving. Know which side you're on." },
            new string[] { "玩血猎追击，船票和血猎收发器都得带上。", "For Blood Hunt, bring both a ticket and a transceiver." },

            // ============ 入场优先级 ============
            new string[] { "想玩哪种模式就带哪种凭证，别全塞包里。", "Pack the entry item for the mode you want. Don't bring the whole lot." },

            // ============ 龙皇掉落细节 ============
            new string[] { "龙皇能掉图腾和逆鳞，可别指望回回都有。", "The Dragon King can drop totems and Reverse Scales. Not every time." },

            // ============ 更多日常/世界观 ============
            new string[] { "最怕送到了没人签收，跑得再快也白搭。", "Worst delivery? Nobody there to sign. All that running for nothing." },
            new string[] { "老路线闭着眼都认得。可Boss不认我的路。", "I know the old routes by heart. Bosses keep changing them." },

            // ============ 末日丧尸模式（v2.2.0） ============
            new string[] { "基地商人卖尸潮邀请函，一张只能进一趟。", "The base merchant sells Zombie Tide Invitations. One per run." },
            new string[] { "尸潮净化点撤离能换钱，死了就没了。", "Extract to cash in your tide purification points. Die and lose them." },
            new string[] { "尸潮污染越高，丧尸越难打。别贪最后一波。", "More pollution, tougher zombies. Don't get greedy for one more wave." },
            new string[] { "带尸潮信标能跳过准备读秒，想好了再用。", "A Zombie Tide Beacon skips the prep countdown. Be ready." },

            // ============ 变异词条系统（v2.2.0） ============
            new string[] { "开场看看左边抽到的变异词条，别闷头冲。", "Check your mutators on the left before charging in." },
            new string[] { "词条细则把鼠标移上去看。便宜往往带着代价。", "Hover over a mutator for details. Read the catch." },
            new string[] { "变异词条抽几个能在配置里调，出发前看看。", "Set your mutator count in the config before leaving." },

            // ============ 新装备货源（P0 五把武器 2026-09 已批出库；霜雷两套装同批） ============
            new string[] { "毒蛇匕首那批新武器送到叮当那儿了，熟了去问。", "Dingdang has the new weapons, Viper Dagger included. Get to know it." },
            new string[] { "霜雷两套甲也在叮当那儿，好感六级才卖。", "Dingdang sells the frost and thunder armor sets at Affinity 6." },

            // 近期内容：入口、去处与用途各说一件，旧下标不变。
            new string[] { "宿命回响要带信物和船票，自己的装备也带齐。", "For Fate Echo, pack the relic, a ticket, and your own gear." },
            new string[] { "宿命回响那位宿敌记仇，下回还会来找你。", "Your Fate Echo nemesis holds a grudge. Expect another visit." },
            new string[] { "黑市鸭王杯让你当经理人，选将和配装都得操心。", "The Black Market Duck Cup puts you in charge of fighters and their gear." },
            new string[] { "百战留痕要看对阵再下注，别光听名字响。", "In the Duck Cup, check the matchup before placing your bet." },
            new string[] { "鸭王征程找基地里的杰夫接，六章都在他的任务页上。", "Jeff at base hands out the Duck King Campaign. All six chapters are on his quest page." },
            new string[] { "鸭王征程接了契约再去打，别白跑；做完回来找杰夫交。", "Take a campaign contract before heading out, and hand it in to Jeff when you're done." },
            new string[] { "听说有条去天空岛的航路，先找Jeff问问。", "Heard there's a route to Sky Island. Ask Jeff first." },
            new string[] { "Jeff让找的航向仪在零号区，拿到后回来交差。", "Jeff's missing instrument is in Ground Zero. Bring it back to him." },
            new string[] { "天空岛通航后，从基地船点走，不用挤竞技场。", "Once Sky Island opens, leave from the base boat." },
            new string[] { "天空岛想返航，回登云码头找系泊桩。", "Heading home from Sky Island? Use Cloudrise Dock's mooring post." },
            new string[] { "天空岛的浮舟修装备，眠苔治伤。别跑错门。", "On Sky Island, Fuzhou repairs gear and Miantai treats wounds." },
            new string[] { "岛上头目穿的装备能掉下来，看上哪件就盯紧谁。", "Island bosses can drop the gear they wear. Pick your target." },
            new string[] { "夜里上岛多带药，云蚋可不看你有没有空。", "Pack medicine for island nights. Gnats don't wait their turn." },
            new string[] { "Boss掉的遗种蛋别卖，带回基地遗种巢孵。", "Keep those Relic Eggs from bosses. Hatch them in a Relic Nest at base." },
            new string[] { "想添点新本事，去词缀锻造台看看。", "Want a new trick on your gear? Check the Affix Forge." },
            new string[] { "竞技场后山有地方整备，忙完一场再去转转。", "Check the arena's back mountain between fights. You can prepare there." },
            new string[] { "没见过的Boss，打完翻翻鸭皇图鉴。", "Met a new boss? Check the bestiary after the fight." },
            new string[] { "基地能看鸭科夫日报，送报的可比我轻松。", "Read the Duckov Daily at base. Easier job than hauling crates." },
            new string[] { "毒蛇匕首找典狱长，召唤法杖找大兴兴。", "The Warden can drop Viper Dagger; Big Xing, the Summoning Staff." },
            new string[] { "呆头鹅出能量盾，大冰冰出冰霜长矛。", "Goofy Goose can drop Energy Shield; Big Ice, Frost Spear." },
            new string[] { "雷电戒指找三枪哥。能不能拿到，还得看手气。", "Triple-Shot Man can drop Thunder Ring. If you're lucky." },
            new string[] { "新货的详细用法在书里，别听我一句就乱按。", "The book explains the new gear. Read it before pressing buttons." },

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
                InjectModeGLocalization();
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
        /// 物品本地化注入的公共形态：中文名键、英文名键、规范 BossRush_* 键各注一条显示名，
        /// 再按 "&lt;key&gt;_Desc" 注三条描述，最后按 typeId 注 "Item_&lt;id&gt;" 与 "Item_&lt;id&gt;_Desc"。
        ///
        /// 船票 / 生日蛋糕 / Wiki Book 此前各抄了一遍这段（三个方法结构逐行相同，
        /// 只差常量名和那一个规范 key 字面量）。收成一处后**注入的 key 与内容逐字不变**。
        ///
        /// 注意一个 grep 上的差别：`BossRush_Ticket_Desc` / `BossRush_BirthdayCake_Desc` /
        /// `BossRush_WikiBook_Desc` 这三个 key 此前是源码里的字面量，现在由
        /// `canonicalKey + "_Desc"` 在运行时拼出，源码里 grep 不到。
        /// 运行时注册的 key 完全一样，只是找它们要按 canonicalKey 找。
        /// </summary>
        private static void InjectItemLocalization(
            int typeId,
            string nameKeyCn,
            string nameKeyEn,
            string canonicalKey,
            string displayName,
            string description)
        {
            // 注入中英文键
            LocalizationHelper.InjectLocalization(nameKeyCn, displayName);
            LocalizationHelper.InjectLocalization(nameKeyEn, displayName);
            LocalizationHelper.InjectLocalization(canonicalKey, displayName);

            LocalizationHelper.InjectLocalization(nameKeyCn + "_Desc", description);
            LocalizationHelper.InjectLocalization(nameKeyEn + "_Desc", description);
            LocalizationHelper.InjectLocalization(canonicalKey + "_Desc", description);

            // 注入物品 ID 键
            if (typeId > 0)
            {
                string itemKey = "Item_" + typeId;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
            }
        }

        /// <summary>
        /// 注入船票本地化
        /// </summary>
        public static void InjectTicketLocalization(int typeId)
        {
            InjectItemLocalization(
                typeId,
                TICKET_NAME_CN,
                TICKET_NAME_EN,
                "BossRush_Ticket",
                L10n.T(TICKET_NAME_CN, TICKET_NAME_EN),
                L10n.T(TICKET_DESC_CN, TICKET_DESC_EN));
        }

        /// <summary>
        /// 注入生日蛋糕本地化
        /// </summary>
        public static void InjectCakeLocalization(int typeId)
        {
            InjectItemLocalization(
                typeId,
                CAKE_NAME_CN,
                CAKE_NAME_EN,
                "BossRush_BirthdayCake",
                L10n.T(CAKE_NAME_CN, CAKE_NAME_EN),
                L10n.T(CAKE_DESC_CN, CAKE_DESC_EN));
        }

        /// <summary>
        /// 注入 Wiki Book 本地化
        /// </summary>
        public static void InjectWikiBookLocalization(int typeId)
        {
            string displayName = L10n.T(WIKI_BOOK_NAME_CN, WIKI_BOOK_NAME_EN);
            string description = L10n.T(WIKI_BOOK_DESC_CN, WIKI_BOOK_DESC_EN);

            InjectItemLocalization(
                typeId, WIKI_BOOK_NAME_CN, WIKI_BOOK_NAME_EN, "BossRush_WikiBook",
                displayName, description);

            // 注入 Unity 预制体中使用的本地化键（冒险家日志）
            // 预制体 displayName 字段设置为 "冒险家日志"，游戏会用它作为本地化键查找
            LocalizationHelper.InjectLocalization("冒险家日志", displayName);
            LocalizationHelper.InjectLocalization("冒险家日志_Desc", description);

            ModBehaviour.DevLog("[LocalizationInjector] Wiki Book 本地化注入完成");
        }

        /// <summary>
        /// 注入丧尸模式本地化
        /// </summary>
        public static void InjectZombieModeLocalization()
        {
            ZombieTideInvitationConfig.InjectLocalization();
            ZombieTideBeaconConfig.InjectLocalization();
            PortableSafeZoneDeviceConfig.InjectLocalization();
            InjectZombieModeString("BossRush_ZombieTideInvitation", "尸潮邀请函", "Zombie Tide Invitation");
            InjectZombieModeString("BossRush_ZombieTideInvitation_Desc", "进入末日丧尸模式的入场凭证。撤离失败时不退还。", "Required to enter Zombie Mode. Not refunded on failure.");
            InjectZombieModeString("BossRush_ZombieTideBeacon", "尸潮信标", "Zombie Tide Beacon");
            InjectZombieModeString("BossRush_ZombieTideBeacon_Desc", "在准备倒计时阶段使用，立即开始下一波。本局工具，不能带出。", "Use during preparation countdown to start the next wave immediately. Run-only tool.");
            InjectZombieModeString("BossRush_PortableSafeZoneDevice", "便携安全区装置", "Portable Safe-Zone Device");
            // 描述只有一份事实来源：PortableSafeZoneDeviceConfig 的常量。
            // 这里在 InjectLocalization() 之后再注入同一个 key，写死文案会静默覆盖掉配置里的版本。
            InjectZombieModeString(
                "BossRush_PortableSafeZoneDevice_Desc",
                PortableSafeZoneDeviceConfig.DESCRIPTION_CN,
                PortableSafeZoneDeviceConfig.DESCRIPTION_EN);
            InjectZombieModeString("BossRush_ZombieMode_Notify_PortableSafeZoneNotZombieMode", "便携安全区装置只能在丧尸模式中使用。", "The Portable Safe-Zone Device can only be used in Zombie Mode.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_PortableSafeZoneUnavailable", "当前阶段无法部署安全区。", "The safe zone cannot be deployed during the current phase.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_PortableSafeZoneDeployed", "便携安全区已部署。", "Portable safe zone deployed.");
            InjectZombieModeString("BossRush_ZombieMode_Notify_RecycledBackpackJunk", "已回收 {0} 件背包废品，获得 {1} 净化点。", "Recycled {0} backpack junk items and gained {1} Purification Points.");
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
            InjectZombieModeString("BossRush_ZombieMode_Hud_Pressure", "场上尸潮 {0}/{1}（{2}）", "Tide Pressure {0}/{1} ({2})");
            InjectZombieModeString("BossRush_ZombieMode_Tide_Low", "低潮", "Low Tide");
            InjectZombieModeString("BossRush_ZombieMode_Tide_Rising", "涨潮", "Rising Tide");
            InjectZombieModeString("BossRush_ZombieMode_Tide_High", "高潮", "High Tide");
            InjectZombieModeString("BossRush_ZombieMode_Tide_Peak", "峰值", "Peak Tide");
            InjectZombieModeString("BossRush_ZombieMode_Tide_Boss", "Boss 潮", "Boss Tide");
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
            InjectZombieModeString("BossRush_ZombieMode_Hud_SafeZone_StealthOk", "安全区：有效", "Safe Zone: Active");
            InjectZombieModeString("BossRush_ZombieMode_Map_SafeZone", "安全区", "Safe Zone");
            InjectZombieModeString("BossRush_ZombieMode_Hud_RefreshAvailable", "免费刷新 {0}", "Free Refresh {0}");
            InjectZombieModeString("BossRush_ZombieMode_Hud_BeaconReady", "信标可用", "Beacon Ready");
            InjectZombieModeString("BossRush_ZombieMode_Hud_BeaconUnavailable", "信标不可用", "Beacon Unavailable");
            InjectZombieModeString("BossRush_ZombieMode_Hud_ExtractionOpenHint", "撤离点已开放 - 仅本准备期有效", "Extraction Open - This Preparation Only");
            InjectZombieModeString("BossRush_ZombieMode_Banner_Started", "<color=green>击杀目标 → 选择强化 → 第 5 波 Boss 后可撤离</color>", "<color=green>Kill targets → choose upgrades → extract after the Wave 5 Boss</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_PreparationStarted", "末日丧尸模式：准备阶段开始。", "Zombie Mode: preparation started.");
            InjectZombieModeString("BossRush_ZombieMode_Banner_PreparationNextWave", "下一波尸潮即将到来。", "Next zombie wave is coming.");
            InjectZombieModeString("BossRush_ZombieMode_Banner_WaveIncoming", "第 <color=yellow>{0}</color> 波尸潮来袭！", "Zombie wave <color=yellow>{0}</color> incoming!");
            InjectZombieModeString("BossRush_ZombieMode_Banner_WaveCleared", "第 <color=yellow>{0}</color> 波已肃清。", "Wave <color=yellow>{0}</color> cleared.");
            InjectZombieModeString("BossRush_ZombieMode_Banner_Failed", "<color=red>末日丧尸模式失败。</color>", "<color=red>Zombie Mode failed.</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_BossWaveStart", "<color=red>Boss 节点 - 第 {0} 波</color>", "<color=red>Boss Node - Wave {0}</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_BossWaveCleared", "<color=yellow>Boss 节点完成</color>", "<color=yellow>Boss Node Cleared</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_PollutionUp", "<color=#bb55ff>污染上升至 {0}（{1}）</color>", "<color=#bb55ff>Pollution rose to {0} ({1})</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_ExtractionOpen", "<color=#22aaff>撤离点已开放</color>", "<color=#22aaff>Extraction Open</color>");
            InjectZombieModeString("BossRush_ZombieMode_Banner_SafeZoneCancelled", "<color=#ff7733>安全区已取消</color>", "<color=#ff7733>Safe Zone Cancelled</color>");
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
            InjectZombieModeString("BossRush_ZombieMode_Reward_Title_Boss", "<color=red>Boss 第 {0} 波</color> | 净化收益 {1}% | 剩余 {2} 选", "<color=red>Boss W{0}</color> | Purification {1}% | {2} Pick(s) Left");
            InjectZombieModeString("BossRush_ZombieMode_Reward_Info", "净化点: {0}    免费刷新: {1}    付费刷新: {2}", "Purification: {0}    Free Refreshes: {1}    Paid Refresh: {2}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_NextWavePreview", "下一波 {0}：压力 {1} | 非 Boss 移速 {2}% | {3}", "Next Wave {0}: Pressure {1} | Non-Boss Speed {2}% | {3}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_NextBossPreview", "下一波 {0}：Boss 强度 {1} | 数量 {2} | 生命 {3}% | 伤害 {4}% | 支援 {5} | 净化收益 {6}%", "Next Wave {0}: Boss Tier {1} | Count {2} | HP {3}% | Damage {4}% | Support {5} | Purification {6}%");
            InjectZombieModeString("BossRush_ZombieMode_Reward_PointsHeader", "净化点数 {0}", "Purification {0}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_PickButton", "选择", "Pick");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RefreshFree", "免费刷新", "Free Refresh");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RefreshPaid", "付费刷新 -{0}", "Paid Refresh -{0}");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RestTitle", "休息时长：{0} 秒", "Rest: {0}s");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RestEdit", "修改", "Edit");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RestApply", "确定", "Apply");
            InjectZombieModeString("BossRush_ZombieMode_Reward_RestOption", "{0} 秒", "{0}s");
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
            InjectZombieModeString("BossRush_ZombieMode_Npc_MerchantSubtitle", "净化点 {0} · 分类补给", "Purification {0} · Categorized supplies");
            InjectZombieModeString("BossRush_ZombieMode_Npc_NurseSubtitle", "净化点 {0} · 治疗 / 解毒 / 止血", "Purification {0} · Heal / Detox / Stop bleeding");
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
            InjectZombieModeString("BossRush_ZombieMode_Reward_RecycleBackpackJunk", "将背包里的低品质普通废品换成净化点，按价值结算。武器、弹药、药品、食物、钥匙和特殊物品保留。", "Trade low-quality backpack junk for Purification Points based on value. Weapons, ammo, medicine, food, keys and special items stay.");
            InjectZombieModeString("BossRush_ZombieMode_Reward_PortableSafeZoneDevice", "获得一个可在战斗或准备阶段部署一次的便携安全区装置。", "Gain a Portable Safe-Zone Device that can deploy one safe zone during combat or preparation.");
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
            InjectZombieModeString("BossRush_ZombieMode_Special_OfficialExploder", "自爆丧尸", "Exploder Zombie");
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

            // Mode G 宿命回响本地化：InjectLocalization_Extra_Integration 未直接列出 Mode G 入口，
            // 在本方法（运行时注入链成员）末尾挂载，保证 key 随同一注入时机生效
            InjectModeGLocalization();
        }

        /// <summary>
        /// 注入 Mode G「宿命回响」本地化（九波三幕 / 三轴反制 / 宿敌追猎 / 结算面板等 UI 文本）。
        /// key 集合以 ModeG/ 代码实际引用为准；只新增 key，不改旧 key。
        /// </summary>
        public static void InjectModeGLocalization()
        {
            try
            {
                // 场内交互入口（ModeGInteractable 的 InteractName）
                InjectModeGString("BossRush_ModeG_Preview", "宿命回响", "Fate Echo");

                // 波次横幅 / 结算标题
                InjectModeGString("BossRush_ModeG_DefeatTitle", "败北", "DEFEAT");
                InjectModeGString("BossRush_ModeG_WaveWord", "第", "Wave");
                InjectModeGString("BossRush_ModeG_WaveOfNine", "/9 波", "/9");
                InjectModeGString("BossRush_ModeG_NewRecord", "新纪录", "New Record");

                // 三轴反制
                InjectModeGString("BossRush_ModeG_AxisDistance", "距离回声", "Distance Echo");
                InjectModeGString("BossRush_ModeG_AxisAmmo", "弹药点名", "Ammo Mark");
                InjectModeGString("BossRush_ModeG_AxisAttribute", "属性封锁", "Attribute Lock");
                InjectModeGString("BossRush_ModeG_AmmoBan", "弹药点名：禁止使用", "Ammo Mark: banned");
                InjectModeGString("BossRush_ModeG_AmmoFallback", "弹药", "Ammo");
                InjectModeGString("BossRush_ModeG_BanAttrPrefix", "上局你的", "Last run your");
                InjectModeGString("BossRush_ModeG_BanAttrMid", "贡献了", " contributed");
                InjectModeGString("BossRush_ModeG_BanAttrTail", "% 威胁", "% of the threat");
                InjectModeGString("BossRush_ModeG_Axis_Attempts", "次尝试", "attempts");
                InjectModeGString("BossRush_ModeG_Axis_Breaks", "次破解", "breaks");

                // 战斗 HUD（规格 §15：唯一反制目标 + 可验证双门槛进度）
                InjectModeGString("BossRush_ModeG_Hud_Counter", "反制:", "Counter:");
                InjectModeGString("BossRush_ModeG_Hud_FateProbe", "宿命试探", "Fate Probe");
                InjectModeGString("BossRush_ModeG_Hud_NoCounter", "本波无反制", "No counter this wave");
                InjectModeGString("BossRush_ModeG_Hud_Invalid", "本波挑战无效", "Objective void this wave");
                InjectModeGString("BossRush_ModeG_Hud_NoAmmoCandidate", "宿敌未学会新弹药", "Nemesis learned no new ammo");
                InjectModeGString("BossRush_ModeG_Hud_NeedClose", "需贴近 ≤8m", "Need ≤8m");
                InjectModeGString("BossRush_ModeG_Hud_NeedFar", "需拉开 ≥18m", "Need ≥18m");
                InjectModeGString("BossRush_ModeG_Hud_ContribWord", "总血贡献", "boss HP");
                InjectModeGString("BossRush_ModeG_Hud_WillBreak", "双门槛已达标 · 波次完成即破解", "Thresholds met · breaks on wave clear");
                InjectModeGString("BossRush_ModeG_Hud_BanPrefix", "禁用：", "Banned: ");
                InjectModeGString("BossRush_ModeG_Hud_BanClean", "未违禁", "clean");
                InjectModeGString("BossRush_ModeG_Hud_BanViolated", "已违禁", "VIOLATED");
                InjectModeGString("BossRush_ModeG_Hud_FamilyGun", "枪械", "Gun");
                InjectModeGString("BossRush_ModeG_Hud_FamilyMelee", "近战", "Melee");
                InjectModeGString("BossRush_ModeG_Hud_LockedSuffix", "最终伤害 x0.75", " final damage x0.75");
                InjectModeGString("BossRush_ModeG_Hud_NeedTerminal", "需该系终结", "needs finishing blow");
                InjectModeGString("BossRush_ModeG_Hud_Intermission", "休整 · 下一波", "Intermission · next wave");
                InjectModeGString("BossRush_ModeG_Hud_CalmGate", "停火中", "Hold fire");
                InjectModeGString("BossRush_ModeG_Hud_NextWave", "下一波", "Next");
                InjectModeGString("BossRush_ModeG_Hud_LastStand", "最后处决", "Last Stand");
                InjectModeGString("BossRush_ModeG_Hud_Seconds", " 秒", "s");
                InjectModeGString("BossRush_ModeG_Hud_Targets", "目标", "Targets");
                InjectModeGString("BossRush_ModeG_Hud_Nemesis", "宿敌", "Nemesis");

                // 入口确认页强制披露（规格 §3.1：死亡损失规则 + 高 Resolve 备装建议）
                InjectModeGString("BossRush_ModeG_Entry_DeathRule",
                    "死亡损失遵循当前地图规则（可能生成墓碑或掉落物品），本模式不提供额外保装。",
                    "Death losses follow this map's own rules (tomb or dropped items). This mode adds no gear insurance.");
                InjectModeGString("BossRush_ModeG_Entry_LoadoutHint",
                    "高 Resolve 建议准备三种弹药与近战备用；这些只影响可选 Resolve，不影响通关。",
                    "For high Resolve bring three ammo types plus a melee backup. These affect optional Resolve only, never the clear.");

                // 宿敌追猎
                InjectModeGString("BossRush_ModeG_NextNemesis", "下局宿敌", "Next nemesis:");
                InjectModeGString("BossRush_ModeG_RankWord", "Rank", "Rank");
                InjectModeGString("BossRush_ModeG_KillerWord", "击杀者", "Killer:");
                InjectModeGString("BossRush_ModeG_NemesisProtected", "宿敌记录受版本保护，未变更", "nemesis record version-protected, unchanged");
                InjectModeGString("BossRush_ModeG_NoNewNemesis", "未形成新宿敌", "No new nemesis formed");
                InjectModeGString("BossRush_ModeG_NemesisKills", "宿敌击败", "Nemesis kills");

                // 结算面板（Recap）
                InjectModeGString("BossRush_ModeG_Recap_VictoryTitle", "宿命已改写", "Fate Rewritten");
                InjectModeGString("BossRush_ModeG_Recap_DefeatTitle", "宿命未竟", "Fate Unfinished");
                InjectModeGString("BossRush_ModeG_Recap_Close", "关闭", "Close");
                InjectModeGString("BossRush_ModeG_Recap_Rewards", "奖励", "Rewards:");
                InjectModeGString("BossRush_ModeG_Recap_ItemsUnit", "件", "items");
                InjectModeGString("BossRush_ModeG_Recap_ResolveGap", "距下一档还差", "to next tier:");
                InjectModeGString("BossRush_ModeG_Recap_RewardMax", "已达最高档", "max tier reached");
                InjectModeGString("BossRush_ModeG_Recap_Contract", "本局契约", "Contract");
                InjectModeGString("BossRush_ModeG_Recap_ContractDone", "达成", "Fulfilled");
                InjectModeGString("BossRush_ModeG_Recap_ContractFailed", "未达成", "Failed");

                // 宿命契约印章
                InjectModeGString("BossRush_ModeG_Seal_NextLabel", "下一枚印章", "Next seal");
                InjectModeGString("BossRush_ModeG_Seal_EntryCondition", "完成本局所选宿命契约即可铭刻", "fulfill this run's chosen Fate Contract to press the seal");
                InjectModeGString("BossRush_ModeG_Seal_Streak", "契约连胜", "contract streak");

                // 宿敌图鉴
                InjectModeGString("BossRush_ModeG_Codex", "宿敌图鉴", "Nemesis codex");
                InjectModeGString("BossRush_ModeG_Codex_Complete", "图鉴集齐", "complete");
                InjectModeGString("BossRush_ModeG_Codex_Need", "还需", "need");
                InjectModeGString("BossRush_ModeG_Codex_More", "次宿敌击败解锁下一里程碑", "more nemesis kills for next milestone");

                // 个人记录
                InjectModeGString("BossRush_ModeG_TotalRuns", "总场次", "Runs");
                InjectModeGString("BossRush_ModeG_TotalVictories", "胜利", "Victories");
                InjectModeGString("BossRush_ModeG_BestWave", "最佳波次", "Best wave");

                ModBehaviour.DevLog("[LocalizationInjector] Mode G 本地化注入完成");
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[LocalizationInjector] Mode G 本地化注入失败: " + e.Message);
            }
        }

        private static void InjectModeGString(string key, string zh, string en)
        {
            LocalizationHelper.InjectLocalization(key, L10n.T(zh, en));
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
