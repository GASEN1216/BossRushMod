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
            TidegateLoot.InjectCargoLabels();
            LocalizationHelper.InjectLocalization(MapEnemyKey, L10n.T("敌人", "Enemy"));
            LocalizationHelper.InjectLocalization(MapBossKey, L10n.T("沙老大·铅锤", "Leadline, the Dredge Boss"));
            LocalizationHelper.InjectLocalization(TidegateLeadUsage.PinKey, L10n.T("响铊：埋货", "Sounding lead: buried cache"));
            foreach (string id in new[] { "road", "ferry", "drain", "lh" })
            {
                LocalizationHelper.InjectLocalization(ExitMapKey(id), ExitName(id) + " · " + ExitRule(id));
                foreach (TidegateExitState state in Enum.GetValues(typeof(TidegateExitState)))
                    if (state != TidegateExitState.Unknown)
                        LocalizationHelper.InjectLocalization(ExitStateKey(id, state), ExitName(id) + " · " + ExitStatus(state));
            }
        }

        internal static string ExitMapKey(string id) { return "BossRush_TidegateMap_Exit_" + id; }
        internal static string ExitStateKey(string id, TidegateExitState state) { return ExitMapKey(id) + "_" + state.ToString(); }

        private static string ExitStatus(TidegateExitState state)
        {
            switch (state)
            {
                case TidegateExitState.Open: return L10n.T("可撤离", "Open now");
                case TidegateExitState.LightLoad: return L10n.T("可抓缆 · 轻装", "Catch the line · light load");
                case TidegateExitState.LowWater: return L10n.T("水低，船够不着", "Too shallow for the ferry");
                case TidegateExitState.Submerged: return L10n.T("出口被淹", "Outlet submerged");
                case TidegateExitState.Unlit: return L10n.T("去灯塔叫船", "Signal from the lighthouse");
                case TidegateExitState.Inbound: return L10n.T("船正在赶来", "Boat inbound");
                default: return "";
            }
        }

        /// <summary>撤离点什么时候能走（地图标签与 Wiki 同一口径）。</summary>
        internal static string ExitRule(string id)
        {
            switch (id)
            {
                case "road": return L10n.T("随时", "always open");
                case "ferry": return L10n.T("高潮靠岸；涨退潮轻装可抓缆", "docks at high tide; catch the line on a rising or falling tide if light");
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
                    "所长临走前欠我一块怀表。表盘上那些潮位，是我一刀一刀刻的。他那天把柜子里的钱分了，偏偏把我的表锁进去了。你说图什么？\n" +
                    "我的交班记录有两份：一份在第三水厂控制室的保险箱，一份在镇区泵站的值班桌。去哪个都行；你要是认得闸，也用不着绕路。\n" +
                    "你要是肯跑一趟，沿地堡下水道的西侧走道往北。走到水箱前，认那盏马灯，我的竹筏就停在旁边。\n" +
                    "三个阀门都拧开，大闸就提得起来。等退了潮，金库就露出来了。\n" +
                    "记着，背的东西太沉就别下深水，会沉底。\n" +
                    "——闸伯",
                    "To whoever carries this:\n" +
                    "I kept the gates at Tidegate's No. 3 Waterworks. Folks call me Old Zha.\n" +
                    "Since the town emptied, the tide gate has stayed shut. The lagoon keeps rising and the savings bank sits under it.\n" +
                    "The manager left owing me a pocket watch. I cut the tide marks on that dial myself. His last day there, he gave away the money in the safe, then locked my watch inside. What do you make of that?\n" +
                    "I left shift notes in the safe in the No. 3 Waterworks control room and on the duty desk at the town pump station. Either will do. If you know the gates already, no need for the detour.\n" +
                    "If you'll make the trip, take the west walkway north through the bunker sewer. At the water tanks, look for the lantern on my raft.\n" +
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
                    "他把表翻过来。背壳里垫着一角钞票，刚好把松了的表芯卡紧。「怪不得一路颠回来，还走着。」\n" +
                    "「行了。往后你要下潮闸镇，就坐我的筏子。缺什么防水的，找我。」",
                    "Old Zha holds the watch to his ear for a long time and says nothing.\n" +
                    "\"Still ticking.\" He wipes the dial on his sleeve. \"Marks are still straight.\"\n" +
                    "He turns it over. A corner of a banknote, folded inside the case, holds the loose movement in place. \"So that's how it kept ticking all the way home.\"\n" +
                    "\"All right. Whenever you head down to Tidegate, take my raft. Need anything waterproof, come to me.\"");
            }
        }

        // 页码只标装订顺序。四个固定箱柜（TidegateLoot.PageIndexOf）可独立发现；每页提到下一处，第 IV 页指回第 I 页。
        private static readonly string[][] PageTexts =
        {
            new[] { "闸工日志（一）· 大闸", "Gatekeeper's Log (I) · The Tide Gate",
                "大闸三处阀门：第三水厂泵房、镇区泵站、潮位站。少一处，闸提到半截就卡死。全开，泻湖才跟着外海涨落。闸下水往里抽的时候，连空桶都会被拖走；想过对岸，走坝顶。\n镇区泵站的伙计送来半截拧弯的扳手。我问他阀门怎么开的，他指指自己的值班桌：「法子写那儿了。」",
                "Three valves: the waterworks pump house, the town pump station and the tide gauge station. Miss one and the gate jams halfway. Open all three and the lagoon rises and falls with the sea. When the intake draws, it takes even an empty barrel. Cross along the dam.\nThe pump station hand brought me half a bent spanner. I asked how he'd got the valve open. He pointed at his duty desk. “Wrote it down for you.”" },
            new[] { "闸工日志（二）· 泵站", "Gatekeeper's Log (II) · The Pump Station",
                "镇区泵站的阀门又锈了。别松手，憋三口气让它转过死点。涨潮时水从排水暗渠那头先漫上来，过脚脖子就往高处走。\n昨夜潮位站报不出数。我叫值夜的把看见的事写下来，放进站里的记录柜，别在无线电里说。",
                "Pump valve's rusted again. Hold on through three breaths to get it past the stiff spot. The flood comes up the drain culvert first. Once it's over your ankles, head for high ground.\nNo reading from the tide gauge last night. I told the night hand to put what he'd seen in the station records cabinet. Best keep it off the radio." },
            new[] { "闸工日志（三）· 潮位站", "Gatekeeper's Log (III) · The Tide Gauge",
                "看清了。淘沙帮把手轮塞进挖沙船船舱的储物柜。泻湖不退，谁也摸不到金库，他们照旧独占滩涂。挖沙船跳板低，得趁退潮上船。\n所长借这张纸写了几句话，又撕走半页，说要和老闸的表一起锁在储蓄所金库的保险柜里。",
                "Saw the gang stash the handwheel in the dredger's cabin locker. Keep the lagoon full and nobody can reach the vault; the flats are theirs. The gangway floods. Board at low tide.\nThe bank manager borrowed this sheet and tore off the half he'd written on. Said it was going in his safe in the bank vault, with Zha's watch." },
            new[] { "闸工日志（四）· 所长", "Gatekeeper's Log (IV) · The Manager",
                "（这半页不是闸伯的字）\n老闸的怀表替我领过三趟夜班的路。他说表要进水就扔了，可惜那圈潮位刻得比潮汐表还准。我把它修好了。\n还闸钥匙时，他把怎么提闸写下来，锁在第三水厂控制室的保险箱里。哪天闸再提起来，我亲手还他。",
                "(The handwriting on this torn half isn't Old Zha's.)\nHis watch got me home on three night shifts. He said to throw it out if it took on water. A waste: those marks beat any tide table. I've fixed it.\nWhen Zha handed in his keys, he locked his gate instructions in the No. 3 Waterworks control room safe. The day the gate goes up again, I'll hand him his watch myself." },
        };

        internal static string PageTitle(int index)
        {
            return index >= 0 && index < PageTexts.Length ? L10n.T(PageTexts[index][0], PageTexts[index][1]) : string.Empty;
        }

        internal static string PageBody(int index)
        {
            return index >= 0 && index < PageTexts.Length ? L10n.T(PageTexts[index][2], PageTexts[index][3]) : string.Empty;
        }

        // 已读页留下的去向才能进提示；先试刚翻到的那一页，若目标已读，再查旧页。
        internal static int NextJournalLead(int pages, int recent = 0)
        {
            int count = PageTexts.Length;
            if (recent < 0 || recent >= count) recent = 0;
            for (int i = 0; i < count; i++)
            {
                int source = (recent + i) % count, target = (source + 1) % count;
                if ((pages & (1 << source)) != 0 && (pages & (1 << target)) == 0) return target;
            }
            return -1;
        }

        internal static string JournalLeadPlace(int page)
        {
            switch (page)
            {
                case 0: return L10n.T("第三水厂控制室的保险箱", "the No. 3 Waterworks control room safe");
                case 1: return L10n.T("镇区泵站的值班桌", "the town pump station duty desk");
                case 2: return L10n.T("潮位站的记录柜", "the tide gauge station records cabinet");
                case 3: return L10n.T("开闸后，低潮去储蓄所找所长的保险柜", "the bank manager's safe, at low tide after opening the gate");
                default: return string.Empty;
            }
        }

        internal static string OptionalJournalObjective(int pages)
        {
            if (pages == 0) return L10n.T("可选：闸伯的交班记录留在第三水厂控制室和镇区泵站，任选一处调查",
                "Optional: Look for Zha’s shift notes in the No. 3 Waterworks control room or at the town pump station");
            int next = NextJournalLead(pages);
            return next >= 0 ? L10n.T("可选 · 日志中提到：", "Optional · From your notes: ") + JournalLeadPlace(next)
                : L10n.T("四页日志已收齐。按潮水搜刮，从开放的出口撤离", "All four pages found. Loot with the tide and leave through an open exit");
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
