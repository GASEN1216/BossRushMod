// ============================================================================
// CampaignLocalization.cs - 鸭王征程本地化的唯一 source of truth
// ============================================================================
// 形态照 Localization/DailyReportLocalization.cs。接线点是
// Integration/BossRushIntegration_StartAndScene.cs 的 InjectLocalization_Extra_Integration()。
//
// 【故事：《册子上的名字》（2026-09-22 换新）】
//   杰夫嫌竞技场按「流浪选手」结账，要把地堡的名字立到擂台报名册上。六章是账房要的
//   六行记录，每交一行杰夫把赏金换成基地里真能用的东西：菜地（ch1）、陈列加成（ch2）、
//   点唱机战歌（ch3）。上一章解锁的设施是下一章的目标（ch2 要建好菜地、ch3 要摆上一件战利品）。
//   冠军之影 = 擂台的守擂者，穿历任冠军留下的甲，册上只印一道剪影；要立名字先跟他打一场。
//   全线只依赖「杰夫 + 竞技场 + 基地建设」，不依赖任何别的 Mod 系统的知识。
//
// 【范围】这里只注入**官方系统会主动去查表**的 key：
//   官方任务标题 / 说明（Quest.DisplayNameRaw / DescriptionRaw）、建筑名 / 描述（官方查 "Building_" + id）、
//   交互提示名、官方笔记图鉴条目（官方查 "Note_{key}_Title" / "_Content"）。
//   目标行、飘字、对话由代码侧内联 L10n.T 双语给出，不进注入表。
//   说话人杰夫直接用官方键 Character_Jeff，不另注册名字。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>征程本地化键的注入入口。</summary>
    public static class CampaignLocalization
    {
        /// <summary>把全部征程键注入官方本地化表。</summary>
        public static void Inject()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            map["BossRush_Campaign_Board_Interact"] = L10n.T("看看公告板", "Check the board");
            map["BossRush_Campaign_FinalBoss_Interact"] = L10n.T("按住报名石", "Hold the sign-up stone");
            map["BossRush_Campaign_FinalBoss_Name"] = L10n.T("冠军之影", "Shadow of the Champion");
            LocalizationHelper.InjectLocalizations(map);

            InjectQuestKeys();
            InjectBuildingKeys();
            InjectClueKeys();
        }

        /// <summary>
        /// 官方任务页的标题与说明（六章，键 BossRush_Campaign_chN_Name / _Description）。
        /// 标题与章节表的 titleCN / titleEN 同一串；说明是杰夫在跟你说话，三四句，只用逗号句号。
        /// </summary>
        public static void InjectQuestKeys()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();

            AddQuest(map, "ch1",
                "报个名", "Sign Us Up",
                "竞技场的报名册上没有我们的名字，赏金按流浪选手结，亏得慌。你带上船票去标准竞技场打一局，路牌选标准那一档，开头两波别挨打，账房就看这两行。打完回基地找我，钱我付，后头那块菜地我也让人给你腾出来。",
                "Our name isn't in the arena's ledger, so they pay us like drifters. It stings. Take a ticket into the Standard Arena, pick a standard tier at the sign, and don't get hit in the first two waves. That's all the bookkeeper reads. Come find me at base after and I'll pay, and I'll have that plot out back cleared for you.");

            AddQuest(map, "ch2",
                "种地的选手", "The Fighter With a Garden",
                "菜地建起来了吗，没粮的选手账房不给写第二行。地弄好了就带船票空手进白手起家，身上和宠物背包都得空，打到第 5 波，顺手用发给你的刀砍够 5 个。回来结钱，我再教你一招，打回来的好东西摆出来能长命。",
                "Is the garden up yet? The bookkeeper won't write line two for a fighter with no food. Once it's planted, take a ticket into From Scratch with nothing on you, empty pet bag included, push to wave 5 and put down 5 with the knife we hand you. Come back for your money, and I'll show you a trick. The good stuff you win keeps you alive longer if you put it on display.");

            AddQuest(map, "ch3",
                "门面", "A Proper Front",
                "第三行要证明我们守得住一块地。带船票和营旗去划地为营，选个阵营，把敌方头目干掉 8 个，够数就能回。架子那边也别忘了，随便摆一件打回来的战利品，账房要派人来基地看门面。",
                "Line three says we can hold ground. Take a ticket and a faction banner into Faction War, pick a side, and drop 8 hostile bosses. That's enough, come home. Don't forget the rack either. Put one of your trophies up, the bookkeeper sends a man round to look the place over.");

            AddQuest(map, "ch4",
                "收钱走人", "Collect and Leave",
                "第四行要看我们收得了钱，还走得掉。带船票和血猎收发器裸装进血猎追击，把带悬赏印记的干掉 3 个，撤离点一开就走，别贪。这一章没有新东西给你，钱多给一点。",
                "Line four wants to see we can collect and still walk out. Ticket and Bloodhunt Transponder, no gear, into Blood Hunt. Kill 3 marked bounties and take the extraction the moment it opens, don't get greedy. No new toys this time, just more money.");

            AddQuest(map, "ch5",
                "没人肯去的那场", "The Match Nobody Takes",
                "最后一行是疫区那场，签过名的人没回来过，所以册子上一直空着。用尸潮邀请函出发，投多少现金你自己看，撑到第 5 波，Boss 打完撤离点就开。站上去走，回来这一行就满了。",
                "The last line is the quarantine match. Nobody who signed it ever came back, so the line stayed empty. Use a Zombie Tide Invitation, put in as much cash as you think it's worth, and hold to wave 5. The extraction opens once that Boss is down. Step on it and leave, and the page is full.");

            AddQuest(map, "ch6",
                "守擂的那个", "The One Holding the Ring",
                "册子翻到我们那一页了，就差你签。带装备和船票进竞技场，别带其它模式的信物，也别去点路牌。身边会立起一块报名石，按住它，守擂的那个就来，赢了名字就是我们的。",
                "Our page is open, all it needs is your name. Take gear and a ticket into the arena, leave the other modes' tokens at home, and don't touch the sign. A sign-up stone comes up beside you. Hold it, the one holding the ring answers, and if you win the name is ours.");

            LocalizationHelper.InjectLocalizations(map);
        }

        private static void AddQuest(Dictionary<string, string> map, string chapterId,
            string nameCN, string nameEN, string descriptionCN, string descriptionEN)
        {
            map[CampaignQuestTable.NameKey(chapterId)] = L10n.T(nameCN, nameEN);
            map[CampaignQuestTable.DescriptionKey(chapterId)] = L10n.T(descriptionCN, descriptionEN);
        }

        /// <summary>
        /// 建筑名与描述。官方按 "Building_" + id 硬编码查表，
        /// 缺这两条会在建造 UI 里显示 *Building_bossrush_campaign_board*。
        /// 公告板已退役（任务改由杰夫发放）：新档不再进建造菜单，老档已建的照常注册，所以键还要注入。
        /// </summary>
        public static void InjectBuildingKeys()
        {
            string buildingKey = "Building_" + CampaignTuning.BoardBuildingId;

            Dictionary<string, string> map = new Dictionary<string, string>();
            map[buildingKey] = L10n.T("征程公告板", "Campaign Board");
            map[buildingKey + "_Desc"] = L10n.T(
                "钉满旧悬赏纸的木板。杰夫把征程的活儿收回自己手里了，这块板子留着当个纪念，拆了也不影响进度。",
                "An old board plastered with bounty notices. Jeff runs the campaign himself now. Keep it as a souvenir or tear it down, either way your progress is safe.");

            LocalizationHelper.InjectLocalizations(map);
        }

        /// <summary>
        /// 线索条目在官方笔记图鉴里的标题与正文。
        /// 官方按 "Note_{key}_Title" / "Note_{key}_Content" 查表，缺了会显示裸 key。
        /// key 前缀 CampaignTuning.NoteKeyPrefix 与线索 ID（clue_ch1..6）一起构成冻结契约：换故事只换文案不换 key。
        ///
        /// 每条 = 报名册上的一行 + 一句杰夫的话。数字全线对得上：六行、十一个签过名的、八个头目、三个悬赏、第五波。
        /// </summary>
        public static void InjectClueKeys()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();

            AddClue(map, "clue_ch1",
                "报名册 第一行",
                "账房抄给我们的那一页，一共六行。第一行写着「标准场，通关，开头两波无损」。旁边是我们的名字，铅笔写的，字很小，擦得掉。\n"
                + "杰夫：「先用铅笔。六行填满才给上墨。」",
                "Ledger, Line One",
                "The page the bookkeeper copied out for us. Six lines. The first reads \"Standard Arena, cleared, no damage through wave two.\" Our name sits beside it in pencil, small enough to rub out.\n"
                + "Jeff: \"Pencil for now. They only ink it once all six are full.\"");

            AddClue(map, "clue_ch2",
                "报名册 第二行",
                "第二行写着「空手入场，第五波，近战五杀」。行末盖了个歪章，章上是一株菜苗，账房管这个叫「有粮」。\n"
                + "杰夫：「一个会自己种地的选手，账房看着都顺眼些。」",
                "Ledger, Line Two",
                "Line two reads \"entered with nothing, wave five, five melee kills.\" A crooked stamp sits at the end of the line with a seedling on it. The bookkeeper calls that \"has food.\"\n"
                + "Jeff: \"A fighter who grows his own. Even the bookkeeper likes the look of that.\"");

            AddClue(map, "clue_ch3",
                "报名册 第三行",
                "第三行写着「守住一块地，八个头目」。后面别着一张来人的字条，说我们基地的架子上摆着东西，「看得出来是自己打的」。\n"
                + "杰夫：「他没问价钱，也没问牌子。看的是有没有人真打过。」",
                "Ledger, Line Three",
                "Line three reads \"held ground, eight bosses.\" A visitor's note is pinned after it: the base has trophies up, and they \"clearly came off something he killed himself.\"\n"
                + "Jeff: \"He didn't ask what they cost or who made them. He was checking whether anyone here actually fights.\"");

            AddClue(map, "clue_ch4",
                "报名册 第四行",
                "第四行写着「三个悬赏，活着撤离」。赏金栏被账房改过一次，原来那个数字划掉了，改高了，旁边注着「入册价」。\n"
                + "杰夫：「名字还没上墨，价先涨了。册子就是这么个东西。」",
                "Ledger, Line Four",
                "Line four reads \"three bounties, extracted alive.\" The bookkeeper revised the fee once: the old figure struck out, a higher one written in, marked \"listed rate.\"\n"
                + "Jeff: \"Name's still in pencil and the rate already went up. That's what a ledger is for.\"");

            AddClue(map, "clue_ch5",
                "报名册 第五行",
                "第五行以前是空的。往前翻，这一行签过十一个名字，十一个后面都画着一道横线，没有回执。现在第十二个名字后面写着「第五波，撤离，回来了」。\n"
                + "杰夫：「你是第一个把这行填完的。别问我为什么留着这一页。」",
                "Ledger, Line Five",
                "Line five used to be empty. Flip back and eleven names have signed it, all eleven with a dash after them and no return receipt. The twelfth now reads \"wave five, extracted, came back.\"\n"
                + "Jeff: \"You're the first to finish that line. Don't ask why I kept the page.\"");

            AddClue(map, "clue_ch6",
                "报名册 我们的那一页",
                "六行填满，名字上了墨。最后一行是守擂那一场，对手栏印着一道影子，没有名字。历任冠军的甲还挂在擂台上，谁穿上谁就是那道影子。\n"
                + "杰夫：「这页以后归我们了。你想回来吃饭就回来吃饭，那套甲留给别人。」",
                "Ledger, Our Page",
                "Six lines full, the name inked in. The last line is the gatekeeping bout; the opponent column holds a silhouette and no name. The old champions' armor still hangs in the ring, and whoever puts it on becomes that silhouette.\n"
                + "Jeff: \"The page is ours now. Come home and eat whenever you like. Let somebody else wear the armor.\"");

            LocalizationHelper.InjectLocalizations(map);
        }

        private static void AddClue(
            Dictionary<string, string> map, string clueId,
            string titleCN, string contentCN, string titleEN, string contentEN)
        {
            string key = CampaignTuning.NoteKeyPrefix + clueId;
            map["Note_" + key + "_Title"] = L10n.T(titleCN, titleEN);
            map["Note_" + key + "_Content"] = L10n.T(contentCN, contentEN);
        }
    }
}
