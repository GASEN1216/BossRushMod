using System;

namespace BossRush
{
    /// <summary>潮闸镇的玩家可见地名与撤离点名。中英双语，取用时按当前语言解析。</summary>
    internal static class TidegateText
    {
        // 场景包里 POI 标记的名字（`POI_<中文名>`）与英文对照。名字来自 three.js 原型 `v2/js/layout.js` 的 POIS。
        private static readonly string[][] Places =
        {
            new[] { "公路检查站", "Highway Checkpoint" }, new[] { "第三水厂", "Waterworks No. 3" }, new[] { "老镇", "Old Town" },
            new[] { "广场", "Town Square" }, new[] { "镇区泵站", "Town Pump Station" }, new[] { "码头", "Docks" },
            new[] { "栈桥", "Ferry Pier" }, new[] { "滩涂", "Mudflats" }, new[] { "沉船", "Shipwreck" },
            new[] { "挖沙船", "Sand Dredger" }, new[] { "泻湖", "Lagoon" }, new[] { "储蓄所", "Savings Bank" },
            new[] { "大闸", "Tide Gate" }, new[] { "芦苇荡", "Reed Marsh" }, new[] { "灯塔岬", "Lighthouse Cape" },
            new[] { "潮位站", "Tide Gauge Station" }, new[] { "暗渠", "Drain Culvert" },
        };

        internal static string PlaceName(string markerName)
        {
            string name = markerName != null && markerName.StartsWith("POI_", StringComparison.Ordinal) ? markerName.Substring(4) : markerName;
            foreach (string[] place in Places)
                if (string.Equals(place[0], name, StringComparison.Ordinal)) return L10n.T(place[0], place[1]);
            return name ?? string.Empty;
        }

        internal static string ExitName(string id)
        {
            switch (id)
            {
                case "road": return L10n.T("公路检查站", "Highway Checkpoint");
                case "ferry": return L10n.T("栈桥渡口", "Ferry Pier");
                case "drain": return L10n.T("排水暗渠", "Drain Culvert");
                case "lh": return L10n.T("灯塔码头", "Lighthouse Jetty");
                default: return id ?? string.Empty;
            }
        }

        /// <summary>地图标签的本地化键（官方 SimplePointOfInterest 把 displayName 当键查表，传成品字会显示带星号的键）。</summary>
        internal const string MapEnemyKey = "BossRush_TidegateMap_Enemy";
        internal const string MapBossKey = "BossRush_TidegateMap_Boss";

        internal static void InjectMapLabels()
        {
            LocalizationHelper.InjectLocalization(MapEnemyKey, L10n.T("敌人", "Enemy"));
            LocalizationHelper.InjectLocalization(MapBossKey, L10n.T("沙老大", "Sand Boss"));
            foreach (string id in new[] { "road", "ferry", "drain", "lh" })
                LocalizationHelper.InjectLocalization(ExitMapKey(id), ExitName(id) + " · " + ExitRule(id));
        }

        internal static string ExitMapKey(string id) { return "BossRush_TidegateMap_Exit_" + id; }

        /// <summary>撤离点什么时候能走（地图标签与 Wiki 同一口径）。</summary>
        internal static string ExitRule(string id)
        {
            switch (id)
            {
                case "road": return L10n.T("随时", "always open");
                case "ferry": return L10n.T("高潮来船", "boat at high tide");
                case "drain": return L10n.T("退潮露出", "open at low tide");
                case "lh": return L10n.T("点亮灯塔后来船", "boat after the lighthouse is lit");
                default: return string.Empty;
            }
        }

        internal static string LetterTitle { get { return L10n.T("闸伯的信", "Old Zha's Letter"); } }

        internal static string LetterBody
        {
            get
            {
                return L10n.T(
                    "送信的鸭子：\n" +
                    "我是潮闸镇第三水厂的老闸工，镇上都叫我闸伯。\n" +
                    "镇子空了以后，大闸一直关着。泻湖越涨越高，储蓄所整栋泡在水底下。\n" +
                    "淘沙帮那伙人天天下滩挖东西，还把潮位站阀门的手轮拆了，扔在他们的挖沙船上。\n" +
                    "所长生前欠我一块怀表，锁在储蓄所的保险柜里。表盘上那圈潮位刻度，是我一刀一刀刻的。\n" +
                    "你要是肯跑一趟：我的竹筏停在你们地堡下水道东头，水道最北边，挂着马灯。\n" +
                    "三个阀门都拧开，大闸就提得起来。等退了潮，金库就露出来了。\n" +
                    "记着，背的东西太沉就别下深水，会沉底。\n" +
                    "——闸伯",
                    "To whoever carries this:\n" +
                    "I kept the gates at Tidegate's No. 3 Waterworks. Folks call me Old Zha.\n" +
                    "Since the town emptied, the tide gate has stayed shut. The lagoon keeps rising and the savings bank sits under it.\n" +
                    "The sand gang digs the flats every day. They pulled the handwheel off the tide gauge valve and dumped it on their dredger.\n" +
                    "The bank manager owed me a pocket watch. It's locked in his safe. I cut the tide marks on that dial myself.\n" +
                    "If you'll make the trip: my raft is tied up at the east end of your bunker's sewer, right at the north end of the canal. Look for the lantern.\n" +
                    "Open all three valves and the gate will lift. Wait for low tide and the vault comes up.\n" +
                    "And mind your load. Carry too much into deep water and you'll sink.\n" +
                    "— Old Zha");
            }
        }

        internal static string EndingTitle { get { return L10n.T("潮汐怀表", "The Tide Pocket Watch"); } }

        internal static string EndingBody
        {
            get
            {
                return L10n.T(
                    "闸伯把表贴在耳朵边听了很久，没说话。\n" +
                    "「还走着。」他用袖子擦了擦表盘，「刻度也没歪。」\n" +
                    "「所长那个人，借东西从来不还，倒是给锁得好好的。」\n" +
                    "「行了。往后你要下潮闸镇，就坐我的筏子。缺什么防水的，找我。」",
                    "Old Zha holds the watch to his ear for a long time and says nothing.\n" +
                    "\"Still ticking.\" He wipes the dial on his sleeve. \"Marks are still straight.\"\n" +
                    "\"That manager never gave back anything he borrowed. Kept it locked up nice, though.\"\n" +
                    "\"All right. Whenever you head down to Tidegate, take my raft. Need anything waterproof, come to me.\"");
            }
        }

        // 四页闸工日志：出处是场景包里固定的四个箱柜（TidegateLoot.PageIndexOf），顺序就是读起来的顺序。
        private static readonly string[][] PageTexts =
        {
            new[] { "闸工日志（一）· 大闸", "Gatekeeper's Log (I) · The Tide Gate",
                "大闸三处阀门：第三水厂泵房、镇区泵站、潮位站。三处全开，闸门才肯提。少一处，提到一半就卡死。\n开了闸，泻湖就跟着外海走：外头退潮它也退，外头涨潮它也涨。",
                "The tide gate has three valves: the waterworks pump house, the town pump station and the tide gauge station. All three open, or the gate jams halfway.\nOnce it's up, the lagoon follows the sea out: it drops when the tide goes out and rises when it comes back." },
            new[] { "闸工日志（二）· 泵站", "Gatekeeper's Log (II) · The Pump Station",
                "镇区泵站的阀门又锈了，拧它得使出全身力气，三口气才转得开。\n涨潮的时候别在滩上磨蹭。水是从排水暗渠那头先漫上来的，漫过脚脖子就该往高处走了。",
                "The town pump station valve has rusted again. It takes everything you've got, three long pulls.\nDon't linger on the flats when the tide turns. The water comes up through the drain culvert first. Once it's over your ankles, head for high ground." },
            new[] { "闸工日志（三）· 潮位站", "Gatekeeper's Log (III) · The Tide Gauge",
                "潮位站的手轮叫淘沙帮拆走了。他们巴不得泻湖一直满着：水底下的金库没人够得着，退潮时滩上的东西全归他们挖。\n手轮就扔在挖沙船船舱的储物柜里。挖沙船跳板低，涨了潮就淹，得趁退潮过去。",
                "The sand gang took the tide gauge handwheel. They want the lagoon kept full: nobody reaches the vault under it, and everything on the flats at low tide is theirs to dig.\nThe wheel is in the locker in the dredger's cabin. The dredger's gangway sits low and floods at high tide. Go when the water's out." },
            new[] { "闸工日志（四）· 所长", "Gatekeeper's Log (IV) · The Manager",
                "（夹在所长保险柜里的一页，不是闸伯的字）\n老闸的怀表我先替他收着。他那圈潮位刻得比潮汐表还准，放在外头早叫人摸走了。\n等哪天大闸再提起来，我亲手还他。",
                "(A page tucked in the manager's safe. Not Old Zha's handwriting.)\nI'm keeping Zha's watch safe for him. Those tide marks of his beat any tide table. Left out, it'd have been lifted long ago.\nThe day the gate goes up again, I'll hand it back to him myself." },
        };

        internal static string PageTitle(int index)
        {
            return index >= 0 && index < PageTexts.Length ? L10n.T(PageTexts[index][0], PageTexts[index][1]) : string.Empty;
        }

        internal static string PageBody(int index)
        {
            return index >= 0 && index < PageTexts.Length ? L10n.T(PageTexts[index][2], PageTexts[index][3]) : string.Empty;
        }

        internal static string PhaseName(TidegateTidePhase phase)
        {
            switch (phase)
            {
                case TidegateTidePhase.Ebb: return L10n.T("退潮", "Ebbing");
                case TidegateTidePhase.Low: return L10n.T("低潮", "Low tide");
                case TidegateTidePhase.Flood: return L10n.T("涨潮", "Flooding");
                default: return L10n.T("高潮", "High tide");
            }
        }
    }
}
