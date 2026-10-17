using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BossRush;

/// <summary>
/// 天空岛剧情指引全状态矩阵（2026-10-17 全面核查，docs/reports/reviews/2026-10-17-天空岛剧情指引全面核查.md）。
///
/// 前两轮是按一条条玩家反馈补断言：双航标门圈错、敲钟指向钟架、居民不在还叫人找他……每补一条，同类的下一条照样漏。
/// 这里换个做法：从航线刚解锁的新档出发，用生产的 <see cref="SkyIslandStoryRules.TryApply"/> / 任务表门控把**所有可达剧情状态**
/// 广度优先走一遍（航标守卫逐伙清、两灯先后、三条任务接交、物证、折翎和解 / 战胜、噬风、K3、钟守和解 / 战胜、敲钟），
/// 每个状态 × 中英 × 三位给予者在岛上 / 都不在，核对同一组不变式：
///
/// ① 主线没走完时地图一定圈着东西；圈里此刻一定有能点的事（圈心对应的交互点上挂得出选项、或那伙守卫还没清）。
/// ② 只照着地图圈做（每步点圈里第一件能做的事），从任何可达状态出发都能走到三条任务交完——没有死胡同、没有原地打转。
/// ③ 圈上的字与风标罗盘的字永远不是泛称（每个可能被圈的标记都有 TargetTask / ContactTask / SideLabel）。
/// ④ 指引里「」/ '' 引着的选项名、交互名、任务名，必须在面板按钮、交互名或任务名里找得到原词（按钮名由 run.py 逐字从生产文件抽取）。
/// ⑤ 英文界面不出现中文；中文每条都非空。
/// ⑥ 给予者不在岛上时，叫人「找他 / 跟他说」的句子必须同时说出他的兜底装置。
///
/// 设 SKY_GUIDANCE_DUMP=&lt;文件&gt; 时把每种不同的指引组合写成一份可读的状态矩阵（报告的数据来源）。
/// </summary>
internal static class SkyIslandGuidanceMatrixRegression
{
    private const int RouteFlags = (int)(SkyIslandStoryFlag.PreludeAccepted | SkyIslandStoryFlag.PreludeInstrumentRecovered
        | SkyIslandStoryFlag.RouteUnlocked);
    private static readonly string[] Guards = { "D", "D_02", "G", "G_02" };

    /// <summary>参与枚举的剧情动作。种植记录、K1、K2 与主线指引互不影响，单独在结局态翻转核对（否则状态数乘 12）。</summary>
    private static readonly SkyIslandStoryAction[] Actions =
    {
        SkyIslandStoryAction.AcceptBeaconQuest, SkyIslandStoryAction.RepairWindBeacon, SkyIslandStoryAction.RepairStarLamp,
        SkyIslandStoryAction.DeliverBeaconQuest, SkyIslandStoryAction.AcceptBellCourtQuest,
        SkyIslandStoryAction.FindOldLetter, SkyIslandStoryAction.FindRouteChart, SkyIslandStoryAction.RepairTelescope,
        SkyIslandStoryAction.ReconcileZheling, SkyIslandStoryAction.ZhelingDefeated, SkyIslandStoryAction.StormSlain,
        SkyIslandStoryAction.OpenShortcutK3, SkyIslandStoryAction.ReconcileBellKeeper, SkyIslandStoryAction.BellKeeperDefeated,
        SkyIslandStoryAction.AcceptHomecomingQuest, SkyIslandStoryAction.RingHomecomingBell,
        SkyIslandStoryAction.DeliverHomecomingQuest, SkyIslandStoryAction.DeliverBellCourtQuest
    };

    private static readonly string[] Residents = { "sky_qinghe", "sky_weibai", "sky_fuzhou", "sky_miantai", "sky_zheling", "sky_bellkeeper" };

    internal static void Run(Action<bool, string> check)
    {
        List<SkyIslandStoryData> states = Enumerate();
        check(states.Count > 1000, "guidance matrix enumerates the reachable story states: " + states.Count);
        string dumpPath = Environment.GetEnvironmentVariable("SKY_GUIDANCE_DUMP");
        var dump = new Dictionary<string, List<string>>();
        var signatures = new Dictionary<string, int>();
        var checkedTexts = new HashSet<string>();
        var failures = new Dictionary<string, int>();
        Action<bool, string> once = (ok, message) =>
        {
            // 同一类失败在几万个状态里会重复成千上万次：每条只报第一次，后面计数。
            if (ok) return;
            int seen;
            failures.TryGetValue(message, out seen);
            failures[message] = seen + 1;
        };

        foreach (SkyIslandStoryData data in states)
        {
            once(MainComplete(data) || SkyIslandMapMarkers.ObjectiveTargets(data).Any(),
                "main story unfinished but the map circles nothing: " + Describe(data));
            foreach (string target in SkyIslandMapMarkers.ObjectiveTargets(data))
            {
                SkyIslandStoryData next; string what;
                once(TryGuided(data, target, out next, out what), "map circles " + target + " but nothing there can be done now: " + Describe(data));
            }
            foreach (string target in SkyIslandMapMarkers.SideTargets(data))
            {
                once(SideActionable(data, target), "side circle " + target + " has nothing to do: " + Describe(data));
                // 紫圈与罗盘的字也不许是泛称：双航标门走 SideLabel 的三种写法，其余走 SideTask。
                once(target == "Search_E" || SkyIslandMapMarkers.SideTask(target) != SkyIslandMapMarkers.SideTask(null),
                    "side circle / compass falls back to the generic 'unfinished side path' at " + target);
            }
            string stuck = FollowGuidance(data);
            once(stuck == null, "following only the map circles gets stuck: " + stuck + " from " + Describe(data));
            // 面板导语不叫人去做已经做完的事：风标、星灯、折翎、观星镜做完后换成「做完了」那一句。
            once(!data.Has(SkyIslandStoryFlag.WindBeacon) || SkyIslandPointText.BriefFor("Search_D", data) != SkyIslandPointText.Brief("Search_D"),
                "wind beacon fixed but its panel still says clear the guards and fix it: " + Describe(data));
            once(!data.Has(SkyIslandStoryFlag.StarLamp) || SkyIslandPointText.BriefFor("Search_G", data) != SkyIslandPointText.Brief("Search_G"),
                "star lamp fixed but its panel still says clear the guards and fix it: " + Describe(data));
            once(!data.ZhelingResolved || SkyIslandPointText.BriefFor("Search_F", data) != SkyIslandPointText.Brief("Search_F"),
                "Zheling settled but his panel still offers talk or fight: " + Describe(data));
            once(!data.Has(SkyIslandStoryFlag.Telescope) || SkyIslandPointText.BriefFor("Search_S4", data) != SkyIslandPointText.Brief("Search_S4"),
                "telescope fixed but its panel still says clear the gang and fix it: " + Describe(data));
            // 官方完成按钮被拦下时有自己的原因：接了、还没做完的任务说还差哪一步（不落到核心的泛称）。
            foreach (SkyIslandOfficialQuestDefinition quest in SkyIslandOfficialQuestTable.Island)
            {
                bool deliverable = SkyIslandOfficialQuestTable.CanDeliver(quest, Context(data));
                string reason = SkyIslandOfficialQuestTable.DeliverBlocked(quest, Context(data));
                once(deliverable == (reason == null) || !data.Has(quest.AcceptedFlag) || data.Has(quest.DeliveredFlag),
                    "deliver refusal and the official complete gate disagree for " + quest.QuestId + ": " + Describe(data));
            }
        }
        DeliverOffIsland(once);

        Saves.SavesSystem.Switch(101017);
        var story = new SkyIslandStoryService(); story.Open();
        var compass = new SkyIslandGateCompassHarness { story = story };
        foreach (bool chinese in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            HashSet<string> labels = KnownLabels();
            foreach (SkyIslandStoryData data in states)
            foreach (int away in new[] { -1, 0, 1, 2 })
            {
                // 三位给予者逐个缺席（婚后留家 / 这趟没生成）：缺席的那位只能在基地说话，其余居民照常在岛上。
                Load(story, data);
                var texts = new List<KeyValuePair<string, string>>();
                Collect(story, compass, data, away, texts);
                foreach (KeyValuePair<string, string> text in texts)
                {
                    if (text.Value == null) continue;
                    string memo = (chinese ? "zh|" : "en|") + text.Value;
                    if (checkedTexts.Add(memo)) CheckText(text.Key, text.Value, chinese, labels, once);
                    if (away >= 0) CheckAbsent(text.Key, text.Value, chinese, Givers[away], once);
                }
                foreach (string target in SkyIslandMapMarkers.ObjectiveTargets(data))
                {
                    var contact = SkyIslandOfficialQuestTable.NextContactQuest(data);
                    string label = contact != null ? SkyIslandMapMarkers.ContactTask(contact.GiverId, !Away(contact.GiverId, away)) : SkyIslandMapMarkers.TargetTask(target);
                    once(label != null, "map circle / compass falls back to the generic 'current objective' at " + target + ": " + Describe(data));
                }
                if (dumpPath != null && away < 0) AddDump(dump, signatures, data, false, chinese, texts);
                else if (dumpPath != null) AddUnique(chinese, true, texts);
            }
        }
        L10n.IsChinese = true;
        story.Close();
        // 全部跑完再一次性报：一个缺口往往牵连几千个状态，先看清有几类，再逐类修。
        string report = string.Join("\n", failures.Select(pair => "(x" + pair.Value + ") " + pair.Key));
        if (dumpPath != null) WriteDump(dumpPath, dump, signatures, states.Count, report);
        check(failures.Count == 0, "guidance matrix: " + failures.Count + " kind(s) of failure\n" + report);
    }

    /// <summary>岛上任务离岛后点完成、存档写不进去时：说清原因，不落到核心那句「做完了，回去找交代你这件事的人」。</summary>
    private static void DeliverOffIsland(Action<bool, string> once)
    {
        foreach (bool chinese in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            var data = SkyIslandStoryRules.CreateDefault();
            data.flags = RouteFlags | (int)(SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp);
            SkyIslandOfficialQuestDefinition beacons;
            SkyIslandOfficialQuestTable.TryGet(SkyIslandOfficialQuestTable.BeaconQuestId, out beacons);
            var context = Context(data);
            once(SkyIslandOfficialQuestTable.DeliverBlocked(beacons, context) == null, "deliverable beacon quest has no refusal");
            context.OnIsland = false;
            string away = SkyIslandOfficialQuestTable.DeliverBlocked(beacons, context);
            once(away != null && away.Contains(chinese ? "晴岚群岛" : "Qinglan"), "off-island refusal says where to hand it in: " + away);
            context.OnIsland = true;
            context.CanWrite = false;
            string busy = SkyIslandOfficialQuestTable.DeliverBlocked(beacons, context);
            once(busy != null && busy.Contains(chinese ? "过一会儿" : "in a moment"), "save barrier refusal says to retry: " + busy);
        }
        L10n.IsChinese = true;
    }

    // ------------------------------------------------------------------ 枚举

    private static SkyIslandOfficialQuestContext Context(SkyIslandStoryData data)
    {
        return new SkyIslandOfficialQuestContext { Data = data, StoryReady = true, CanWrite = true, OnIsland = true };
    }

    private static SkyIslandOfficialQuestDefinition QuestOf(SkyIslandStoryAction action, out bool accept)
    {
        foreach (SkyIslandOfficialQuestDefinition quest in SkyIslandOfficialQuestTable.Island)
        {
            if (quest.AcceptAction == action) { accept = true; return quest; }
            if (quest.DeliverAction == action) { accept = false; return quest; }
        }
        accept = false;
        return null;
    }

    /// <summary>与玩家点按钮同一道门：任务接交走任务表的 CanOffer / CanDeliver，其余走 TryApply。</summary>
    private static bool Step(SkyIslandStoryData data, SkyIslandStoryAction action, out SkyIslandStoryData next)
    {
        next = null;
        bool accept;
        SkyIslandOfficialQuestDefinition quest = QuestOf(action, out accept);
        if (quest != null && !(accept ? SkyIslandOfficialQuestTable.CanOffer(quest, Context(data)) : SkyIslandOfficialQuestTable.CanDeliver(quest, Context(data))))
            return false;
        string message;
        return SkyIslandStoryRules.TryApply(data, action, out next, out message);
    }

    private static string LampOfGuard(string guard) { return guard.StartsWith("D") ? "wind" : "star"; }

    private static bool CanClear(SkyIslandStoryData data, string guard)
    {
        if (data.EncounterCleared(guard)) return false;
        return !data.Has(LampOfGuard(guard) == "wind" ? SkyIslandStoryFlag.WindBeacon : SkyIslandStoryFlag.StarLamp);
    }

    private static SkyIslandStoryData Cleared(SkyIslandStoryData data, string guard)
    {
        SkyIslandStoryData next = data.Copy();
        next.clearedEncounters = data.clearedEncounters.Concat(new[] { guard }).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return next;
    }

    private static string Key(SkyIslandStoryData data) { return data.flags + ":" + string.Join(",", data.clearedEncounters); }

    private static List<SkyIslandStoryData> Enumerate()
    {
        var start = SkyIslandStoryRules.CreateDefault();
        start.flags = RouteFlags;
        var seen = new HashSet<string> { Key(start) };
        var queue = new Queue<SkyIslandStoryData>();
        var all = new List<SkyIslandStoryData>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            SkyIslandStoryData data = queue.Dequeue();
            all.Add(data);
            var nexts = new List<SkyIslandStoryData>();
            foreach (SkyIslandStoryAction action in Actions)
            {
                SkyIslandStoryData next;
                if (Step(data, action, out next)) nexts.Add(next);
            }
            foreach (string guard in Guards) if (CanClear(data, guard)) nexts.Add(Cleared(data, guard));
            foreach (SkyIslandStoryData next in nexts) if (seen.Add(Key(next))) queue.Enqueue(next);
        }
        return all;
    }

    private static bool MainComplete(SkyIslandStoryData data)
    {
        return data.Has(SkyIslandStoryFlag.BeaconQuestDelivered | SkyIslandStoryFlag.BellCourtQuestDelivered | SkyIslandStoryFlag.HomecomingQuestDelivered);
    }

    // ------------------------------------------------------------------ 圈里能做什么

    /// <summary>圈心标记上（或那位给予者身上，二者挂同一组任务与选项）此刻能做的剧情动作，按玩家最可能点的顺序。</summary>
    private static IEnumerable<SkyIslandStoryAction> ActionsAt(string marker)
    {
        foreach (SkyIslandOfficialQuestDefinition quest in SkyIslandOfficialQuestTable.Island)
            if (SkyIslandOfficialQuestTable.FallbackMarkerOfGiver(quest.GiverId) == marker)
            {
                yield return quest.DeliverAction;
                yield return quest.AcceptAction;
            }
        switch (marker)
        {
            case "Search_D": yield return SkyIslandStoryAction.RepairWindBeacon; yield return SkyIslandStoryAction.OpenShortcutK1; break;
            case "Search_G": yield return SkyIslandStoryAction.RepairStarLamp; yield return SkyIslandStoryAction.OpenShortcutK2; break;
            case "Search_E": yield return SkyIslandStoryAction.StormSlain; yield return SkyIslandStoryAction.OpenShortcutK3; break;
            case "Search_H":
                yield return SkyIslandStoryAction.ReconcileBellKeeper; yield return SkyIslandStoryAction.RingHomecomingBell;
                yield return SkyIslandStoryAction.BellKeeperDefeated; break;
            case "Search_F": yield return SkyIslandStoryAction.ReconcileZheling; yield return SkyIslandStoryAction.ZhelingDefeated; break;
            case "Search_B": yield return SkyIslandStoryAction.DeliverPlantingRecord; break;
        }
        SkyIslandStoryAction evidence;
        if (SkyIslandStoryRules.TrySearchAction(marker, out evidence)) yield return evidence;
    }

    private static bool TryGuided(SkyIslandStoryData data, string marker, out SkyIslandStoryData next, out string what)
    {
        foreach (string guard in Guards)
            if (SkyIslandStoryRules.GuardMarker(guard) == marker)
            {
                what = "clear " + guard;
                next = Cleared(data, guard);
                return !data.EncounterCleared(guard);
            }
        foreach (SkyIslandStoryAction action in ActionsAt(marker))
            if (Step(data, action, out next)) { what = action.ToString(); return true; }
        next = null;
        what = null;
        return false;
    }

    private static bool SideActionable(SkyIslandStoryData data, string marker)
    {
        SkyIslandStoryData next; string what;
        if (TryGuided(data, marker, out next, out what)) return true;
        // 双航标门结局后只因「引风」被圈：引风按本趟计、不写存档，判据是 CanSummonStormEcho（带核、带风晶、本趟没引过）。
        string blocker;
        return marker == "Search_E" && SkyIslandStoryRules.CanSummonStormEcho(data, false, true, SkyIslandStoryRules.StormEchoWindcrystalCost, out blocker);
    }

    /// <summary>每步只点地图圈里第一件能做的事；走到三条任务都交完返回 null，否则返回卡住的原因。</summary>
    private static string FollowGuidance(SkyIslandStoryData data)
    {
        for (int step = 0; step < 40; step++)
        {
            if (MainComplete(data)) return null;
            bool moved = false;
            foreach (string target in SkyIslandMapMarkers.ObjectiveTargets(data))
            {
                SkyIslandStoryData next; string what;
                if (!TryGuided(data, target, out next, out what)) continue;
                data = next;
                moved = true;
                break;
            }
            if (!moved) return "no circled action at " + Describe(data);
        }
        return "no end after 40 steps at " + Describe(data);
    }

    // ------------------------------------------------------------------ 文本

    private static void Load(SkyIslandStoryService story, SkyIslandStoryData data)
    {
        // CurrentObjective 按快照引用 + 旗标缓存：原地改清场表时先拨一次旗标，逼它重算。
        story.Current.flags = ~data.flags;
        string ignored = story.CurrentObjective;
        story.Current.flags = data.flags;
        story.Current.clearedEncounters = data.clearedEncounters;
        story.Current.discoveredNotes = data.discoveredNotes;
        story.Current.wearsMirrorArmor = false;
    }

    private static readonly string[] Markers =
    {
        "Search_A", "Search_B", "Search_C", "Search_D", "Search_D_02", "Search_E", "Search_F", "Search_F_02", "Search_G",
        "Search_G_02", "Search_H", "Search_S1", "Search_S2", "Search_S3", "Search_S4"
    };

    private static readonly int[] GiverIds =
    {
        SkyIslandOfficialQuestTable.WeibaiGiverId, SkyIslandOfficialQuestTable.FuzhouGiverId, SkyIslandOfficialQuestTable.BellKeeperGiverId
    };

    private static bool Away(int giverId, int away) { return away >= 0 && GiverIds[away] == giverId; }

    private static void Collect(SkyIslandStoryService story, SkyIslandGateCompassHarness compass, SkyIslandStoryData data, int away,
        List<KeyValuePair<string, string>> texts)
    {
        Action<string, string> add = (key, value) => texts.Add(new KeyValuePair<string, string>(key, value));
        add("目标卡", SkyIslandStoryRules.Objective(data));
        foreach (SkyIslandOfficialQuestDefinition quest in SkyIslandOfficialQuestTable.Island)
        {
            add("任务名 " + quest.QuestId, quest.Name());
            add("任务说明 " + quest.QuestId, quest.Description());
            add("任务联系人 " + quest.QuestId, quest.Contact());
            foreach (SkyIslandOfficialQuestTaskDefinition task in quest.Tasks)
            {
                add("任务目标行 " + quest.QuestId + "." + task.TaskId, task.Description(data));
                if (task.ExtraHint != null) add("任务提示 " + quest.QuestId + "." + task.TaskId, task.ExtraHint(data));
            }
        }
        foreach (SkyIslandStoryAction action in Enum.GetValues(typeof(SkyIslandStoryAction)))
        {
            string blocker;
            SkyIslandStoryRules.CanApply(data, action, out blocker);
            add("还差什么 " + action, blocker);
            SkyIslandStoryData next; string message;
            if (SkyIslandStoryRules.TryApply(data, action, out next, out message)) add("回话 " + action, message);
        }
        foreach (string guard in Guards) add("清场字幕 " + guard, SkyIslandStoryRules.GuardProgressCaption(data, guard));
        foreach (SkyIslandStoryFlag flag in SkyIslandStoryRules.CombatOutcomeFlags) add("战斗字幕 " + flag, SkyIslandStoryRules.CombatOutcome(flag));
        foreach (string gate in new[] { "K1", "K2", "K3", "BellCourt", "ZhelingPass" })
            add("门牌 " + gate, SkyIslandGateNoticeHarness.Notice(gate, false, data));
        foreach (string marker in Markers)
        {
            add("交互名 " + marker, SkyIslandPointText.Name(marker));
            add("导语 " + marker, SkyIslandPointText.BriefFor(marker, data));
        }
        foreach (SkyIslandOfficialQuestDefinition quest in SkyIslandOfficialQuestTable.Island)
            add("交付被拦 " + quest.QuestId, SkyIslandOfficialQuestTable.DeliverBlocked(quest, Context(data)));
        var contact = SkyIslandOfficialQuestTable.NextContactQuest(data);
        foreach (string target in SkyIslandMapMarkers.ObjectiveTargets(data))
            add("地图圈 " + target, SkyIslandMapMarkers.ObjectiveLabel(contact != null
                ? SkyIslandMapMarkers.ContactTask(contact.GiverId, !Away(contact.GiverId, away)) : SkyIslandMapMarkers.TargetTask(target)));
        foreach (string target in SkyIslandMapMarkers.SideTargets(data))
            add("支线圈 " + target, SkyIslandMapMarkers.SideLabel(target, data));
        add("罗盘 双航标门", compass.GateCompassTask());
        string echo;
        foreach (bool core in new[] { false, true })
        foreach (int crystals in new[] { 0, 1 })
        {
            SkyIslandStoryRules.CanSummonStormEcho(data, false, core, crystals, out echo);
            add("引风还差什么", echo);
        }
        foreach (string id in Residents)
        {
            if (Away(SkyIslandOfficialQuestTable.GiverIdOfResident(id), away)) add("居民在家 " + id, story.DescribeNpc(id, true, false, id == "sky_weibai"));
            else add("居民台词 " + id, story.DescribeNpc(id, false, true, Away(SkyIslandOfficialQuestTable.WeibaiGiverId, away)));
        }
    }

    private static readonly Regex QuoteZh = new Regex("「([^「」]+)」");
    private static readonly Regex QuoteEn = new Regex("(?<=^|[\\s(])'([A-Za-z][^']*?(?:'s [^']*?)?)'(?=[\\s.,;:)!?]|$)");

    /// <summary>引号里允许出现、但不是按钮或交互名的词：任务名、地名与人物 / 物品名。</summary>
    private static HashSet<string> KnownLabels()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Action<string> add = label =>
        {
            if (string.IsNullOrEmpty(label)) return;
            known.Add(label.Trim());
            foreach (string part in label.Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries)) known.Add(part.Trim());
        };
        foreach (string[] pair in SkyIslandGuidanceLabels.Buttons) add(L10n.T(pair[0], pair[1]));
        foreach (string marker in Markers) add(SkyIslandPointText.Name(marker));
        foreach (SkyIslandOfficialQuestDefinition quest in SkyIslandOfficialQuestTable.Island) add(quest.Name());
        add(SkyIslandStormEchoRules.ChoiceLabel);
        add(L10n.T("航路任务", "Route quests"));
        foreach (string region in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "S1", "S2", "S3", "S4" })
            add(SkyIslandRegionLabelHarness.RegionLabel(region));
        // 纪念物牌子（SkyIslandWorldStory.RebuildFeedback）：钟守的引风台词点名「风眼已散」那块牌子。
        add(L10n.T("风眼已散 · 航路重开", "The eye is gone · the lanes reopen"));
        // 剧情对手与物件的名字，不是选项名。
        add(L10n.T("噬风", "Windeater"));
        add(L10n.T("引风", "Call the wind"));
        add(L10n.T("鸭王征程", "Duck King's Campaign"));
        return known;
    }

    private static bool Cjk(string text)
    {
        foreach (char c in text) if (c >= 0x4E00 && c <= 0x9FFF) return true;
        return false;
    }

    private static void CheckText(string source, string text, bool chinese, HashSet<string> labels, Action<bool, string> once)
    {
        if (!chinese) once(!Cjk(text), "English text contains Chinese [" + source + "]: " + text);
        else once(text.Trim().Length > 0, "empty Chinese text [" + source + "]");
        foreach (Match match in (chinese ? QuoteZh : QuoteEn).Matches(text))
        {
            string quoted = match.Groups[1].Value.Trim();
            // 英文单引号也用来引人说的话（中文那边是『』）：冒号或「某人的」后面、或带句读的，是引语不是名字。
            string before = text.Substring(0, match.Index);
            if (!chinese && (before.EndsWith(": ") || before.EndsWith("'s ") || quoted.IndexOfAny(new[] { '.', '!', '?', ',' }) >= 0)) continue;
            bool ok = labels.Contains(quoted) || labels.Any(label => label.StartsWith(quoted + " ", StringComparison.OrdinalIgnoreCase));
            once(ok, "quoted name is not a real button / interaction / quest name [" + source + "]: " + quoted + " in: " + text);
        }
    }

    private static readonly string[][] Givers =
    {
        // 名字（中 / 英）、叫人去找他的说法、兜底装置的词（中 / 英，与 ContactTask 和装置交互名同词）。
        new[] { "苇白", "Weibai", "委托板", "board" },
        new[] { "浮舟", "Fuzhou", "渡口整备", "dock refit" },
        new[] { "钟守", "Bell Keeper", "钟守留言", "bell keeper's message" },
    };

    private static void CheckAbsent(string source, string text, bool chinese, string[] giver, Action<bool, string> once)
    {
        // 不在岛上的人的在家台词本来就该说「找我」以外的话，由 SkyIslandNavigationRegression 单独钉；这里只看岛上读得到的字。
        if (source.StartsWith("居民在家") || source.StartsWith("任务名") || source.StartsWith("交互名")) return;
        {
            string name = chinese ? giver[0] : giver[1];
            string device = chinese ? giver[2] : giver[3];
            string[] asks = chinese
                ? new[] { "找" + name, "跟" + name + "说", "回码头找" + name, "去码头找" + name }
                : new[] { "find " + name, "tell " + name, "see " + name, "Find " + name, "Tell " + name, "to " + name + " at", "back to " + name };
            bool asked = asks.Any(ask => text.IndexOf(ask, StringComparison.Ordinal) >= 0
                || text.IndexOf(ask.Replace(name, "the " + name), StringComparison.Ordinal) >= 0);
            if (!asked) return;
            once(text.IndexOf(device, StringComparison.OrdinalIgnoreCase) >= 0,
                "giver away but the text still sends the player to find " + giver[1] + " without naming the fallback [" + source + "]: " + text);
        }
    }

    // ------------------------------------------------------------------ 矩阵输出

    private static string Describe(SkyIslandStoryData data)
    {
        var parts = new List<string>();
        foreach (SkyIslandStoryFlag flag in Enum.GetValues(typeof(SkyIslandStoryFlag)))
            if (flag != SkyIslandStoryFlag.None && (RouteFlags & (int)flag) == 0 && data.Has(flag)) parts.Add(flag.ToString());
        if (data.clearedEncounters.Length > 0) parts.Add("cleared[" + string.Join(",", data.clearedEncounters) + "]");
        return parts.Count == 0 ? "fresh island save" : string.Join(" ", parts);
    }

    private static readonly string[] DumpKeys = { "目标卡", "地图圈", "支线圈", "任务目标行", "任务提示" };

    private static void AddDump(Dictionary<string, List<string>> dump, Dictionary<string, int> signatures, SkyIslandStoryData data, bool away,
        bool chinese, List<KeyValuePair<string, string>> texts)
    {
        AddUnique(chinese, away, texts);
        var lines = new List<string>();
        foreach (KeyValuePair<string, string> text in texts)
            if (text.Value != null && DumpKeys.Any(key => text.Key.StartsWith(key)))
                lines.Add("  - " + text.Key + "：" + text.Value.Replace("\n", " ⏎ "));
        string signature = (chinese ? "zh" : "en") + "|" + (away ? "away" : "here") + "|" + string.Join("\n", lines);
        int count;
        if (signatures.TryGetValue(signature, out count)) { signatures[signature] = count + 1; return; }
        signatures[signature] = 1;
        string stage = (chinese ? "中文" : "English") + (away ? " · 三位给予者都不在岛上" : " · 居民都在");
        List<string> section;
        if (!dump.TryGetValue(stage, out section)) dump[stage] = section = new List<string>();
        section.Add("### " + Describe(data) + "\n" + string.Join("\n", lines) + "\n@@" + signature.GetHashCode());
    }

    private static void WriteDump(string path, Dictionary<string, List<string>> dump, Dictionary<string, int> signatures, int states, string report)
    {
        var text = new StringBuilder("# 天空岛剧情指引状态矩阵（生成）\n\n可达剧情状态 " + states + " 个；下列为去重后的不同指引组合（每组只列第一个代表状态）。\n");
        text.Append("\n## 不变式失败\n\n").Append(string.IsNullOrEmpty(report) ? "（无）" : report).Append('\n');
        foreach (KeyValuePair<string, List<string>> section in dump)
        {
            text.Append("\n## ").Append(section.Key).Append("（").Append(section.Value.Count).Append(" 组）\n\n");
            foreach (string entry in section.Value) text.Append(Regex.Replace(entry, "\n@@-?\\d+$", string.Empty)).Append("\n\n");
        }
        text.Append("\n# 各来源去重原文\n");
        foreach (KeyValuePair<string, List<string>> bucket in uniqueTexts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append("\n## ").Append(bucket.Key).Append("（").Append(bucket.Value.Count).Append(" 条）\n\n");
            foreach (string value in bucket.Value) text.Append("- ").Append(value).Append('\n');
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private static readonly Dictionary<string, List<string>> uniqueTexts = new Dictionary<string, List<string>>();
    private static readonly HashSet<string> uniqueSeen = new HashSet<string>();

    /// <summary>第二部分：每个来源去重后的全部原文（台词、回话、拒绝话、门牌……），逐条读一遍就覆盖了所有状态。</summary>
    private static void AddUnique(bool chinese, bool away, List<KeyValuePair<string, string>> texts)
    {
        foreach (KeyValuePair<string, string> text in texts)
        {
            if (text.Value == null) continue;
            string bucket = (chinese ? "中文 · " : "English · ") + text.Key;
            string line = (away && text.Key.StartsWith("居民") ? "[有给予者不在] " : string.Empty) + text.Value.Replace("\n", " ⏎ ");
            if (!uniqueSeen.Add(bucket + "\u0001" + line)) continue;
            List<string> values;
            if (!uniqueTexts.TryGetValue(bucket, out values)) uniqueTexts[bucket] = values = new List<string>();
            values.Add(line);
        }
    }
}
