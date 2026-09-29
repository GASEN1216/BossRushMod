using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>Jeff 一次性入门任务；六章和天空岛主线保持各自原有任务身份。</summary>
    internal static class CampaignGuideTable
    {
        internal const int QuestIdBase = 590200;
        internal const int FirstQuestId = 590201;
        internal const int LastQuestId = 590214;
        internal const string ModeG = "mode_g";
        internal const string ModeH = "mode_h";
        internal const string PetNest = "pet_nest";
        internal const string RandomEvents = "random_events";
        internal const string SkyIslandGear = "sky_island_gear";
        internal const string ModeD = "mode_d";
        internal const string ModeE = "mode_e";
        internal const string ModeF = "mode_f";
        internal const string Zombie = "zombie";
        internal const string Garden = "garden";
        internal const string Trophy = "trophy";
        internal const string AffixForge = "affix_forge";
        internal const string Reforge = "reforge";
        internal const string DailyReport = "daily_report";

        internal sealed class Definition
        {
            internal string Id;
            internal int QuestId;
            internal string NameKey;
            internal string DescriptionKey;
            internal string NameCN;
            internal string NameEN;
            internal string HintCN;
            internal string HintEN;
        }

        // 文案是杰夫当面跟玩家说的话（2026-09-25 owner：去掉说明书口吻）。条件照实写在话里：带什么、做到哪一步算数。
        // 表内顺序就是发放顺序（NextOfferableId）；QuestId 按 order 固定，不随顺序调整改号。
        // 2026-09-27 起每条交付有奖励（CampaignRewardTable），奖励是下一条要用的入场物品；
        // 文案里「上回给你的……」只写在前一条一定不会被跳过的引导上。
        private static readonly Definition[] _definitions = new Definition[]
        {
            Make(ModeG, 1, "ModeG", "带上你的老伙计", "Bring Your Own Gear",
                "这回穿你自己的家当上场。船票一张，再揣上那块旧竞技场的石片，上一个攥着它的人没赢。落地先挑一份契约，头一波打响就行。回来跟我说说，第一道回声是什么动静。",
                "Wear your own kit this time. One ticket, plus that stone chip from the old arena. The last one who held it didn't win. Pick a contract when you land; once the first wave kicks off, that's enough. Then tell me what the first echo sounded like."),
            Make(ModeH, 2, "ModeH", "这回坐在看台上", "Your Turn in the Stands",
                "换个位置，坐看台上看别人挨揍。码头那边有场黑市鸭王杯，一张船票就能进。两个斗士先比比身上的家伙和底子，再想押不押。押出去的，输了就归人家，头一回别押太狠。看完一场，回来告诉我谁赢了。",
                "Swap seats for once and watch someone else take the hits. There's a Black Market Duck Cup off the dock; one ticket gets you in. Size up both fighters, their kit and their build, before you decide on a bet. Whatever you stake is theirs if you lose, so go light the first time. Watch one match, then tell me who won."),
            Make(PetNest, 3, "PetNest", "给小家伙留个窝", "Room for a Cub",
                "上回塞给你那枚蛋，还温着吧？别卖。回基地搭个遗种巢，把它放进去。等巢里钻出一只崽，或者你带它出过一趟门，领来给我瞧瞧。那些大家伙倒下时偶尔还会留一枚，捡的时候手轻点。",
                "That egg I gave you, still warm? Don't sell it. Build a Pet Nest at base and settle it in. Once a cub crawls out, or you've taken one out on a run, bring it by so I can see. The big ones sometimes leave another when they drop, so pick it up gently."),
            Make(RandomEvents, 4, "RandomEvents", "战场不照剧本走", "Expect a Surprise",
                "竞技场不总照剧本走，打着打着场上会冒出点岔子。去打一场寻常的，碰上一回就回来。岔子来了先看一眼场边的告示，是规矩临时改了，不是你眼花。疫区那边的乱子自成一套，那个不算。",
                "The arena doesn't always stick to the script. Mid-fight, something can go sideways. Play an ordinary run and come back once it happens. When it does, read the notice first; the rules changed on the fly, your eyes are fine. The quarantine zone has its own brand of chaos, and that doesn't count."),
            Make(SkyIslandGear, 5, "SkyIslandGear", "带一件云上的纪念", "Something from the Clouds",
                "航线开了，云上那座岛该好好逛逛。岛上有几个占着地盘的狠角色，撂倒一个，把它身上的行头扒一件回来，背着穿着都行。光上去转一圈可不算。顺手从矿脉上敲五片铜皮子给我，船底该补了。",
                "The route's open, so the island in the clouds deserves a proper look. A few hard cases up there have staked out ground. Put one down and bring back a piece of what it was wearing, packed or worn. Just sightseeing doesn't count. While you're at it, chip five bits of old brass off the veins for me. The hull needs patching."),
            Make(ModeD, 6, "ModeD", "空手也能开张", "Start with Empty Hands",
                "这回看看你白手起家的本事。身上、包里，连小家伙的包都腾空，只揣一张船票从码头走，别的信物和旗子一样别带。进去以后捡着什么用什么，攒成下一波的本钱。",
                "Let's see what you can do from nothing. Empty your gear, your pack, even the little one's bag. Take just a ticket from the dock and leave every token and flag behind. Once you're in, use whatever you pick up and turn it into the stake for the next wave."),
            Make(ModeE, 7, "ModeE", "先认清自己人", "Choose Your Side",
                "上回给你那面旗带上。想换一边，基地商人那儿还有别家的。身上的装备卸了再走，扛着哪家的旗进去，就是哪家的人。开枪前先看清对面挂的是谁家的旗，别把自己人撂倒。",
                "Take that flag I gave you. If you'd rather switch sides, the base vendor has other colors. Strip your gear before you go; whoever's flag you carry in, you're one of theirs. Check whose colors the other guy's flying before you shoot. Don't drop your own people."),
            Make(ModeF, 8, "ModeF", "借来的时间", "Borrowed Time",
                "装备卸了，揣上船票和我给你那个改装定位器，旗子留家里。它一响，你身上就开始漏血，只有撂倒头目才补得回来。别站着发呆，你这点时间是借来的。",
                "Strip your gear, take the ticket and that modded locator I gave you, and leave the flag at home. Once it pings, you start bleeding out, and only dropping a boss buys the time back. Don't stand around. You're on borrowed time."),
            Make(Zombie, 9, "Zombie", "听见尸潮了吗", "Hear the Horde",
                "上回那封红底请柬，拆了就是，拆完商人那儿还有。进去之前，身上的东西会先寄回仓库，丢不了。想好你打算怎么活下去，真打起来再回来找我。开头要不要往里砸钱随你，不砸也打得下去。",
                "Open that red invitation I gave you; the vendor has more. Your stuff gets shipped back to storage before you go in, so nothing's lost. Decide how you plan to stay alive, get stuck in, then come find me. Putting money in up front is your call. You can hold out without it."),
            Make(Garden, 10, "Garden", "把种子种下去", "A Place for Seeds",
                "那块地批下来了。拎一把铲子、九坨粑粑去后山工地，交了钱就动工。种子我塞你包里了，建好就种，等它熟。收的菜先往你包里塞，塞不下进仓库，再满就搁马蜂那儿，自己去取。地弄好了告诉我一声。",
                "The plot's approved. Take a shovel and nine poop to the backyard site, pay, and they start digging. I put seeds in your pack; plant them once it's up and let them ripen. Harvest goes in your pack, then storage, then waits at Package Pickup if both are full. Tell me when the plot's done."),
            Make(Trophy, 11, "Trophy", "留一件给自己看", "Keep a Trophy",
                "地方有了，战利品别老压在箱底。挑一件从头目身上扒下来的，挂上展示架或者套到假人身上，弄好了跟我说一声。看着自己打回来的东西，人扛得住更多，摆一件是一件。",
                "You've got the space now, so stop burying trophies in a crate. Pick something you took off a boss, hang it on a display rack or put it on a mannequin, and tell me when it's up. Looking at what you won keeps you standing longer, and every piece counts."),
            Make(AffixForge, 12, "AffixForge", "给装备一点脾气", "Give Gear Some Character",
                "哥布林那边能往装备里敲点脾气进去，上回给你那两颗熔石就是干这个的。挑件吃得住的，敲出一条来，背着穿着都行，带回来给我看。光留个空位不算。他收钱不手软，先问价。",
                "The goblin can hammer a bit of temper into your gear. Those two forge stones I gave you are for that. Pick something that'll take it, get one trait on there and bring it back, packed or worn. An empty slot doesn't count. He charges plenty, so ask the price first."),
            Make(Reforge, 13, "Reforge", "旧装备也能再试试", "Give Old Gear a Chance",
                "压箱底的旧货先别急着卖。拿一件去哥布林那儿回回炉，先问清价钱。上回给你那瓶冰手的淬液，能先把你看得上的那一条护住。回炉有好有坏，这回就是试试手，别跟它较劲。",
                "Don't sell the old gear at the bottom of the crate yet. Take a piece to the goblin to be reworked, and ask the price first. That ice-cold quench I gave you can protect the one trait you like before he starts. Reworking can go either way. This time you're just trying it, so don't fight it for a perfect roll."),
            Make(DailyReport, 14, "DailyReport", "看看今天的报纸", "Read the Daily Paper",
                "基地门口立个邮箱，报纸每天有人送。翻开在回执上签个名，顺手看看今天悬赏谁、你最近打得怎么样。签完过来一趟。",
                "Put up a mailbox at base and the paper turns up every day. Open it, sign the receipt, and see who's got a bounty today and how you've been doing. Come by once you've signed."),
        };

        private static Definition Make(string id, int order, string suffix, string nameCN, string nameEN, string cn, string en)
        {
            return new Definition { Id = id, QuestId = QuestIdBase + order,
                NameKey = "BossRush_Campaign_Guide_" + suffix + "_Name",
                DescriptionKey = "BossRush_Campaign_Guide_" + suffix + "_Description",
                NameCN = nameCN, NameEN = nameEN, HintCN = cn, HintEN = en };
        }

        internal static IList<Definition> Definitions { get { return _definitions; } }
        internal static Definition Find(string id)
        {
            for (int i = 0; i < _definitions.Length; i++)
                if (string.Equals(_definitions[i].Id, id, StringComparison.Ordinal)) return _definitions[i];
            return null;
        }
        internal static bool IsCompleted(string id) { return CampaignPersistence.IsGuideCompleted(id); }
        internal static bool IsAccepted(string id) { return CampaignPersistence.IsGuideAccepted(id); }
        internal static bool IsExperienced(string id) { return CampaignPersistence.IsGuideExperienced(id); }
        internal static bool InBase()
        {
            return LevelManager.Instance != null && LevelManager.Instance.IsBaseLevel && !SceneLoader.IsSceneLoading;
        }
        internal static string Describe(Definition definition, bool done)
        {
            if (definition == null) return string.Empty;
            // 目标行只写玩家要做的动作（2026-09-29 owner：不重复任务描述，不再附「去试一次」之类的旁白）；
            // 判据与 CampaignGuideFacts 一一对应，交付仍在基地找杰夫。
            string objective;
            switch (definition.Id)
            {
                case ModeG: objective = L10n.T("用船票和宿命回响信物开始一局宿命回响", "Start a Fate Echo run with a ticket and a Fate Echo Relic"); break;
                case ModeH: objective = L10n.T("在黑市鸭王杯看完一场比赛", "Watch one Black Market Duck Cup match"); break;
                case PetNest: objective = L10n.T("在基地搭建遗种巢，孵出一只崽（或带崽出击一次）", "Build a Pet Nest at base and hatch a cub (or take one on a raid)"); break;
                case RandomEvents: objective = L10n.T("在普通 BossRush 中遇到一次随机事件", "Run into a random event in ordinary BossRush"); break;
                case SkyIslandGear: objective = L10n.T("击败天空岛 Boss，把一件它的专属装备带回基地", "Beat a Sky Island boss and bring one of its gear pieces back to base"); break;
                case ModeD: objective = L10n.T("清空装备，只带 BossRush 船票开始一局白手起家", "Empty your gear and start a From Scratch run with only a BossRush ticket"); break;
                case ModeE: objective = L10n.T("卸下装备，带营旗开始一局划地为营", "Take off your gear and start a Faction War run with a faction flag"); break;
                case ModeF: objective = L10n.T("卸下装备，带船票和血猎收发器开始一局血猎追击", "Take off your gear and start a Blood Hunt run with a ticket and a Bloodhunt Transponder"); break;
                case Zombie: objective = L10n.T("用尸潮邀请函进图，选好开局流派", "Enter with a Zombie Tide Invitation and choose a starting build"); break;
                case Garden: objective = L10n.T("在后山工地建成菜地", "Build the garden at the backyard site"); break;
                case Trophy: objective = L10n.T("把一件 Boss 战利品摆上展示架或假人", "Put a Boss trophy on a display rack or mannequin"); break;
                case AffixForge: objective = L10n.T("在哥布林处锻出一条词缀，带着该装备回基地", "Forge an affix at the goblin and bring that gear back to base"); break;
                case Reforge: objective = L10n.T("在哥布林处重铸一件装备，带着它回基地", "Reforge a piece of gear at the goblin and bring it back to base"); break;
                case DailyReport: objective = L10n.T("在基地邮箱打开日报并签到", "Open the daily paper at the base mailbox and sign in"); break;
                default: return string.Empty;
            }
            return objective + (done ? L10n.T("（已完成）", " (done)") : string.Empty);
        }

        /// <summary>
        /// 引导一条接一条（2026-09-25 owner：不要一进基地十四条全挂出来）：
        /// 手上还有接了没交付的就不挂新的；否则挂表里第一条没交付、且前置已满足的。
        /// 前置没到的（菜地、陈列要先推进鸭王征程）先跳过、排到前置满足那天，不会卡住后面的。
        /// 旧档里已经同时接下的几条照常保留，全部交付后才接着往下发。纯函数：只读引导存档与传入的前置判据。
        /// </summary>
        internal static string NextOfferableId(Func<string, bool> prerequisiteMet)
        {
            for (int i = 0; i < _definitions.Length; i++)
            {
                string id = _definitions[i].Id;
                if (IsAccepted(id) && !IsCompleted(id)) return null;
            }
            for (int i = 0; i < _definitions.Length; i++)
            {
                string id = _definitions[i].Id;
                if (IsCompleted(id)) continue;
                if (prerequisiteMet != null && !prerequisiteMet(id)) continue;
                return id;
            }
            return null;
        }
    }
}
