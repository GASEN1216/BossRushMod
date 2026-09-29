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
                "这回让你带自己的家当上场。揣一张船票和一枚宿命回响信物，穿好你顺手的装备，从码头出发。落地先挑一份契约，九波挑战一开打就算你入门了。回来跟我讲讲，第一道回声是什么动静。",
                "This one lets you bring your own kit. Pocket a ticket and an Echo of Fate token, gear up the way you like and leave from the dock. Pick a contract when you land; once the nine-wave run starts, you're in. Then come tell me what the first echo sounded like."),
            Make(ModeH, 2, "ModeH", "这回坐在看台上", "Your Turn in the Stands",
                "想不想换个位置，坐看台上看别人打？去码头选黑市鸭王杯，一张船票就够。挑两个斗士，比比两边的装备和属性，再决定押不押。押出去的钱和东西输了就回不来，头一回悠着点。看完一场就回来找我。",
                "Fancy watching from the stands for once? Pick the Black Market Duck King Cup at the dock; one ticket is enough. Choose two fighters, size up both sides' gear and stats, then decide whether to bet. Anything you stake is gone if you lose, so go easy the first time. Watch one match, then come see me."),
            Make(PetNest, 3, "PetNest", "给小家伙留个窝", "Room for a Cub",
                "上回给你的那枚遗种蛋先别卖。回基地搭个遗种巢，把蛋放进去孵。等巢里有了一只崽，或者你带着它出了一趟门，就带来给我瞧瞧。打 Boss 的时候也留个心眼，有时还会再掉。",
                "Don't sell that relic egg I gave you. Build a Pet Nest at base and let it hatch. Once there's a cub in the nest, or you've taken one out on a raid, bring it by so I can have a look. Keep an eye on bosses too; now and then one drops another."),
            Make(RandomEvents, 4, "RandomEvents", "战场不照剧本走", "Expect a Surprise",
                "普通的 BossRush 打着打着，场上可能突然冒出点意外。去打一局，碰上一次随机事件就回来。事件来了先看一眼提示，那是规矩变了，不是游戏坏了。丧尸模式有它自己那一套，不算数。",
                "Ordinary BossRush runs can take a turn out of nowhere. Play one and come back once a random event kicks in. When it does, glance at the hint first; the rules just changed, nothing's broken. Zombie mode runs its own events and doesn't count."),
            Make(SkyIslandGear, 5, "SkyIslandGear", "带一件云上的纪念", "Something from the Clouds",
                "航线开了，云上那座岛该好好逛逛了。上岛打倒一个 Boss，拿一件它的专属装备，塞背包里或者穿着都行，带回基地给我看看。光上岛转一圈可不算。顺路从岛上的残铜矿脉敲五片残铜片捎给我，我拿去补船。",
                "The route is open, so the island in the clouds is worth a proper look. Beat one of the island bosses, take a piece of its exclusive gear and bring it back to base, in your pack or on your back. Just sightseeing doesn't count. While you're there, knock five pieces of brass scrap off the island's veins for me; I need them to patch the boat."),
            Make(ModeD, 6, "ModeD", "空手也能开张", "Start with Empty Hands",
                "这回考考你白手起家的本事。装备、背包、宠物包全清空，只带一张 BossRush 船票从码头出发，别的模式信物一样都别带。开局以后捡到什么用什么，把它们攒成下一波的本钱。",
                "Let's see what you can do from nothing. Empty your gear, backpack and pet bag, take only a BossRush ticket from the dock and leave every other mode token at home. Once it starts, use whatever you find and turn it into the stake for the next wave."),
            Make(ModeE, 7, "ModeE", "先认清自己人", "Choose Your Side",
                "上回给你的营旗带上，想换一边就去基地商人那儿再买一面。把身上的装备卸了再出发。旗子决定你站哪一边，划地为营一开局就算你来过了。开枪之前先看清对面是哪家的，别把自己人撂倒。",
                "Take the faction flag I gave you, or buy another from the base vendor if you'd rather switch sides. Take off your gear before you head out. The flag decides whose side you're on, and once Territory starts, that counts. Just look before you shoot; don't drop your own people."),
            Make(ModeF, 8, "ModeF", "借来的时间", "Borrowed Time",
                "卸下装备，带上我给你的船票和血猎收发器，营旗就别带了。血猎追击一开始，你的血会一直往下掉，只有干掉 Boss 才续得上，所以千万别站着发呆。开局就算你试过了。",
                "Take off your gear, grab the ticket and Blood Hunt receiver I gave you, and leave the faction flag behind. Once Blood Hunt starts your health keeps draining, and only killing bosses buys it back, so don't stand around. Starting the run is enough."),
            Make(Zombie, 9, "Zombie", "听见尸潮了吗", "Hear the Horde",
                "上回给你的尸潮邀请函用掉就行，用完了基地商人那儿还有卖，然后选地图。进场时身上的东西会先送回仓库，丢不了。挑好开局流派，真打起来以后回来找我。开局要不要投钱随你，不投也照样能玩。",
                "Use the Horde Invitation I gave you (the base vendor sells more), then pick a map. Your gear goes back to storage on the way in, so nothing gets lost. Choose a starting build, get stuck in, then come find me. Putting cash in at the start is up to you; it plays fine without."),
            Make(Garden, 10, "Garden", "把种子种下去", "A Place for Seeds",
                "菜地那块地批下来了。带一把铲子、九坨粑粑去后山工地，交了钱就能动工。起步种子已经放在你背包里，建好就种下去，等它熟。收成先进背包，装不下进仓库，仓库也满了就去马蜂自提点拿。菜地建成了回来告诉我。",
                "The garden plot is yours now. Take a shovel and nine poop to the backyard site and pay to start building. Your starter seeds are already in your backpack; plant them once it's built and wait for them to ripen. Harvest goes to your backpack first, then storage, and any overflow waits at Package Pickup. Come tell me once the garden is up."),
            Make(Trophy, 11, "Trophy", "留一件给自己看", "Keep a Trophy",
                "陈列的地方有了，别让战利品一直压在箱底。挑一件 Boss 战利品摆上官方展示架或者假人，摆好了回来跟我说一声。每摆出一件，你的生命上限还能涨一点。",
                "You've got somewhere to show things off now, so don't leave your trophies buried in a crate. Put a Boss trophy on an official display rack or a mannequin, then come tell me. Every one on display raises your max health a little."),
            Make(AffixForge, 12, "AffixForge", "给装备一点脾气", "Give Gear Some Character",
                "哥布林那儿能给装备锻词缀，熔石我上回给过你两颗。挑一件能锻的，锻出一条词缀来，放背包里或者穿在身上带回来给我看。空着的词缀槽不算。锻一次还要花钱，先看清价钱再动手。",
                "The goblin can forge affixes onto your gear, and I gave you two forge stones last time. Pick something that takes one, forge an affix onto it and bring it back, in your pack or on you. An empty slot doesn't count. It also costs money, so check the price before you start."),
            Make(Reforge, 13, "Reforge", "旧装备也能再试试", "Give Old Gear a Chance",
                "压箱底的旧装备先别急着卖。去哥布林的重铸页挑一件，看看属性和价钱，重铸一次，带着它回来找我。上回给你的冷淬液能先把一条满意的属性锁住。数值有涨有跌，这回就是让你试试手，用不着非洗到满值。",
                "Don't sell that old gear just yet. Open the goblin's reforge page, pick a piece, check its stats and the cost, and reforge it once. Bring it back to me afterward. The cold quench fluid I gave you can lock a stat you like first. Numbers can go up or down; this is just to try it, no need to chase a perfect roll."),
            Make(DailyReport, 14, "DailyReport", "看看今天的报纸", "Read the Daily Paper",
                "在基地建个邮箱，每天都有日报送上门。翻开签个到，顺手看看今天的悬赏和你最近的战绩。签完到就回来找我。",
                "Build a mailbox at base and the daily paper turns up every day. Open it and sign in, and have a look at today's bounty and your recent record while you're there. Come back once you've signed in."),
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
                case ModeG: objective = L10n.T("用船票和宿命回响信物开始一局宿命回响", "Start a Fate Echo run with a ticket and an Echo of Fate token"); break;
                case ModeH: objective = L10n.T("在黑市鸭王杯看完一场比赛", "Watch one Black Market Duck King Cup match"); break;
                case PetNest: objective = L10n.T("在基地搭建遗种巢，孵出一只崽（或带崽出击一次）", "Build a Pet Nest at base and hatch a cub (or take one on a raid)"); break;
                case RandomEvents: objective = L10n.T("在普通 BossRush 中遇到一次随机事件", "Run into a random event in ordinary BossRush"); break;
                case SkyIslandGear: objective = L10n.T("击败天空岛 Boss，把一件它的专属装备带回基地", "Beat a Sky Island boss and bring one of its gear pieces back to base"); break;
                case ModeD: objective = L10n.T("清空装备，只带 BossRush 船票开始一局白手起家", "Empty your gear and start a From Scratch run with only a BossRush ticket"); break;
                case ModeE: objective = L10n.T("卸下装备，带营旗开始一局划地为营", "Take off your gear and start a Territory run with a faction flag"); break;
                case ModeF: objective = L10n.T("卸下装备，带船票和血猎收发器开始一局血猎追击", "Take off your gear and start a Blood Hunt run with a ticket and a Blood Hunt receiver"); break;
                case Zombie: objective = L10n.T("用尸潮邀请函进图，选好开局流派", "Enter with a Horde Invitation and choose a starting build"); break;
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
