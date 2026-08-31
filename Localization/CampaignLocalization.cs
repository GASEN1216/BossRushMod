// ============================================================================
// CampaignLocalization.cs - 鸭王征程本地化的唯一 source of truth
// ============================================================================
// 形态照 Localization/DailyReportLocalization.cs。接线点是
// Integration/BossRushIntegration_StartAndScene.cs 的 InjectLocalization_Extra_Integration()。
//
// 【术语：用「鸭王」不用「鸭皇」】
//   ModeH 已经立了「黑市**鸭王**杯」，本战役讲的正是那场赛事名人堂里的事，
//   两者必须同名，否则玩家会以为是两个不相干的东西。
//  （「鸭皇图鉴」是 Boss 图鉴，另一个域，不冲突。）
//
// 【剧情锚在 ModeH 的真实机制上，不另造设定】
//   名人堂只有 32 席、第 33 个进来最底下那个就被挤掉——这是 ModeH 已实现的规则
//  （见设计提案 §17.8 与 ModeHHallOfFamePersistence）。整条线索链就长在这条规则上：
//   冠军不是被谁抹掉的，是**排队排出去的**。他之后做的每件事都是在找一个
//   「不会被挤掉的名字」，最后他找到了——代价是不再当选手。
//
// 【范围】这里只注入**官方系统会主动去查表**的 key：
//   建筑名/描述（官方查 "Building_" + id）、交互提示名、
//   官方笔记图鉴条目（官方查 "Note_{key}_Title" / "_Content"）。
//   面板按钮、章节标题、目标描述由 UI 侧内联 L10n.T 双语给出，不进注入表。
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
            map["BossRush_Campaign_FinalBoss_Interact"] = L10n.T("按住召唤石", "Hold the altar stone");
            map["BossRush_Campaign_FinalBoss_Name"] = L10n.T("冠军之影", "Shadow of the Champion");
            map["BossRush_Campaign_Broker_Name"] = L10n.T("中间人", "The Broker");
            LocalizationHelper.InjectLocalizations(map);

            InjectBuildingKeys();
            InjectClueKeys();
        }

        /// <summary>
        /// 建筑名与描述。官方按 "Building_" + id 硬编码查表，
        /// 缺这两条会在建造 UI 里显示 *Building_bossrush_campaign_board*。
        /// 建筑注入器在创建 prefab 之前会先调它，顺序不能颠倒。
        /// </summary>
        public static void InjectBuildingKeys()
        {
            string buildingKey = "Building_" + CampaignTuning.BoardBuildingId;

            Dictionary<string, string> map = new Dictionary<string, string>();
            map[buildingKey] = L10n.T("征程公告板", "Campaign Board");
            map[buildingKey + "_Desc"] = L10n.T(
                "钉满悬赏纸的旧木板。黑市那边托人带话：鸭王杯名人堂的碑上少了一个名字，"
                + "有人肯出钱把它找回来。中间人说这活儿不难，就是得多跑几趟。",
                "An old board plastered with bounty notices. Word from the black market: "
                + "a name has gone missing from the Duck Cup Hall of Fame plaque, and someone "
                + "is paying to get it back. The Broker says the job is simple. Just a lot of legwork.");

            LocalizationHelper.InjectLocalizations(map);
        }

        /// <summary>
        /// 线索条目在官方笔记图鉴里的标题与正文。
        /// 官方按 "Note_{key}_Title" / "Note_{key}_Content" 查表，缺了会显示裸 key。
        /// key 前缀 CampaignTuning.NoteKeyPrefix 与线索 ID 一起构成冻结契约。
        ///
        /// 每条的写法固定为「物证 + 一个精确到荒诞的细节 + 一句旁人证词」，
        /// 证词轮流交给三位既有 NPC（阿稳/叮当/羽织），让线索链同时把 mod 的老角色串进来。
        /// </summary>
        public static void InjectClueKeys()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();

            AddClue(map, "clue_ch1",
                "证物一：两张碑拓",
                "同一块碑，隔了一季拓的两张。两张都是三十二个名字，一个不多一个不少。"
                + "把它们对齐了看：顶上多出一个新名字，最底下那个不见了。\n"
                + "阿稳：「这活儿我送过。名人堂就三十二格，来了第三十三个，最下面那位自己挪窝。"
                + "签收单上写的是『满员退件』——跟包裹一个待遇。」",
                "Evidence I: Two Rubbings",
                "Two rubbings of the same plaque, taken a season apart. Both hold exactly thirty-two "
                + "names — no more, no fewer. Line them up: a new name at the top, and the bottom one gone.\n"
                + "Awen: \"I delivered this one. Hall of Fame has thirty-two slots. Number thirty-three "
                + "walks in, the man at the bottom walks out. The paperwork says 'returned: capacity reached.' "
                + "Same wording we use for parcels.\"");

            AddClue(map, "clue_ch2",
                "证物二：当票",
                "一整套冠军战甲的当票，赎期过了三年没人来赎。签名栏只有一个歪歪扭扭的鸭掌印。"
                + "当铺老板记得他当天说的话：「留着也没人认了。」\n"
                + "叮当：「那套甲是叮当重铸的，词条叮当闭着眼都认得出来！他当掉它……"
                + "是想从头再打一遍，重新排进那三十二格里。叮当才没有觉得可惜呢，哼。」",
                "Evidence II: A Pawn Ticket",
                "A ticket for a full set of champion's armor. Three years past redemption, never claimed. "
                + "The signature box holds only a crooked webbed footprint. The pawnbroker remembers what he "
                + "said that day: \"Nobody recognizes it any more anyway.\"\n"
                + "Dingdang: \"Dingdang reforged that set! Dingdang could name its affixes with both eyes shut. "
                + "He pawned it because he wanted to climb back into those thirty-two slots from nothing. "
                + "Dingdang is NOT sad about it. Hmph.\"");

            AddClue(map, "clue_ch3",
                "证物三：半张阵营旗",
                "旗子从中间被割开，只剩靠旗杆那半边。撕口很齐，是刀口，不是扯的。"
                + "三个不同阵营的头目都说他是自己人，三个都说不出他叫什么。\n"
                + "阿稳：「查无此人不是没记录，是记录里那一行被人腾出来了。"
                + "他挨个阵营挂旗，就为了让谁把他名字重新写下来。没人写。」",
                "Evidence III: Half a Faction Banner",
                "Cut down the middle; only the half nearest the pole survives. The edge is clean — a blade, "
                + "not a tear. Three faction bosses each claim he was one of theirs. None of the three can "
                + "say his name.\n"
                + "Awen: \"It's not that there's no record. It's that the line where his record sat got "
                + "cleared for someone else. He flew every banner he could find, hoping somebody would write "
                + "his name back down. Nobody did.\"");

            AddClue(map, "clue_ch4",
                "证物四：一张没发出去的悬赏令",
                "目标栏写着他自己的名字，落款也是他自己。赏金数额被涂改了十一次，"
                + "最后那个数字大得没有任何人会去接。\n"
                + "中间人：「他不是想死。悬赏令是这行里唯一一种『必须把名字写清楚』的纸。"
                + "没人肯写他的名字，他就自己写，写在唯一写了准数的地方。」",
                "Evidence IV: A Bounty Notice, Never Posted",
                "The target field bears his own name. So does the signature. The sum has been scratched out "
                + "and rewritten eleven times; the final figure is large enough that nobody would ever take "
                + "the contract.\n"
                + "The Broker: \"He wasn't looking to die. A bounty notice is the one piece of paper in this "
                + "trade that *has* to spell your name out. Nobody would write it for him, so he wrote it "
                + "himself, in the one place the number has to be exact.\"");

            AddClue(map, "clue_ch5",
                "证物五：疫区来信",
                "信纸被污染烧出一圈焦边。前半页字迹还工整，后半页整个散了架，"
                + "反复写着同一句：「我还认得自己吗」。数了数，十七遍。第十八遍写到一半没了。\n"
                + "羽织：「污染改写的是『你是什么』，不是『你叫什么』。他大概以为改一个就能改另一个。"
                + "……我不是在替他说话。我只是见过太多这样的病历。」",
                "Evidence V: A Letter from the Quarantine",
                "The paper is ringed with burn marks from contamination. The first half is steady; the second "
                + "falls apart entirely, repeating one line over and over: \"Do I still recognize myself?\" "
                + "Seventeen times. The eighteenth stops halfway.\n"
                + "Yuzhi: \"Contamination rewrites *what* you are, not *who* you're called. He must have "
                + "thought changing one would change the other. ...I'm not defending him. I've just read "
                + "too many charts that end like this.\"");

            AddClue(map, "clue_ch6",
                "结案：冠军之影",
                "名人堂三十二格，进一个挤一个。Boss 图鉴不挤人——写进去的，一条都不会掉。"
                + "他没失踪，他换了一本册子。\n"
                + "中间人：「他现在有个位置了，永久的。代价是那个位置上不写名字，只画一道影子。」\n"
                + "碑上那一行现在归别人了。没人去刮。",
                "Case Closed: Shadow of the Champion",
                "The Hall of Fame holds thirty-two; one in, one out. The bestiary evicts nobody — once you're "
                + "written in, you stay. He never vanished. He just changed which book he was in.\n"
                + "The Broker: \"He has a permanent slot now. The price is that the slot doesn't carry a name. "
                + "Just a silhouette.\"\n"
                + "That line on the plaque belongs to someone else now. Nobody scrapes it off.");

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
