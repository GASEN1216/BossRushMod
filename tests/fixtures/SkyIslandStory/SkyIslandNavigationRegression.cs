using System;
using System.Linq;
using BossRush;

internal static class SkyIslandNavigationRegression
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (bool chinese in new[] { true, false })
        foreach (bool windFirst in new[] { true, false })
        foreach (bool peaceful in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            var data = new SkyIslandStoryData
            {
                flags = (int)(SkyIslandStoryFlag.PreludeAccepted | SkyIslandStoryFlag.PreludeInstrumentRecovered
                    | SkyIslandStoryFlag.RouteUnlocked),
                clearedEncounters = new[] { "D", "D_02", "G", "G_02" }
            };
            Targets(data, check, "Search_B");
            Apply(ref data, SkyIslandStoryAction.AcceptBeaconQuest, check);
            Targets(data, check, "Search_D", "Search_G");
            Apply(ref data, windFirst ? SkyIslandStoryAction.RepairWindBeacon : SkyIslandStoryAction.RepairStarLamp, check);
            Targets(data, check, windFirst ? "Search_G" : "Search_D");
            Apply(ref data, windFirst ? SkyIslandStoryAction.RepairStarLamp : SkyIslandStoryAction.RepairWindBeacon, check);
            Targets(data, check, "Search_B");
            Apply(ref data, SkyIslandStoryAction.DeliverBeaconQuest, check);
            Targets(data, check, "Search_A");
            Apply(ref data, SkyIslandStoryAction.AcceptBellCourtQuest, check);
            Targets(data, check, "Search_H");
            // 玩家反馈 2026-10-16：目标卡让去双航标门打噬风，地图圈却在 31 米外的风眼，木牌找不到。
            check(SkyIslandMapMarkers.SideTargets(data).Contains("Search_E") && !SkyIslandMapMarkers.SideTargets(data).Contains("POI_E"),
                "both beacons lit: map circles the twin-beacon gate sign, not the wind eye");
            if (peaceful)
            {
                Apply(ref data, SkyIslandStoryAction.FindOldLetter, check);
                Apply(ref data, SkyIslandStoryAction.FindRouteChart, check);
                Apply(ref data, SkyIslandStoryAction.RepairTelescope, check);
                Apply(ref data, SkyIslandStoryAction.ReconcileZheling, check);
            }
            Apply(ref data, peaceful ? SkyIslandStoryAction.ReconcileBellKeeper : SkyIslandStoryAction.BellKeeperDefeated, check);
            Targets(data, check, "Search_H");
            Apply(ref data, SkyIslandStoryAction.AcceptHomecomingQuest, check);
            Targets(data, check, "Search_H");
            Apply(ref data, SkyIslandStoryAction.RingHomecomingBell, check);
            // 结局不等于任务已交：先在面前交钟守，再顺路回码头。
            Targets(data, check, "Search_H");
            Apply(ref data, SkyIslandStoryAction.DeliverHomecomingQuest, check);
            Targets(data, check, "Search_A");
            Apply(ref data, SkyIslandStoryAction.DeliverBellCourtQuest, check);
            Targets(data, check);
            check(SkyIslandMapMarkers.SideTargets(data).Contains("Search_E") && !SkyIslandMapMarkers.SideTargets(data).Contains("POI_E"),
                "optional Windeater survives full quest delivery and circles the twin-beacon gate sign");
            check(SkyIslandMapMarkers.SideTargets(data).Contains("Search_S1"), "unfinished garden remains an optional destination");
        }
        L10n.IsChinese = true;
        GuardGuidance(check);
        PointerTargets(check);
    }

    /// <summary>
    /// 2026-10-16 指引复核（docs/reports/reviews/2026-10-16-天空岛任务指引指向复核.md）：
    /// 每条指引都要指向地图圈里真有的那个交互点——居民不在时写兜底装置、敲钟写「钟守留言」而不是 55 米外的钟架、
    /// D 组守卫写在风标东边、只拿到旧信不叫人去见折翎。
    /// </summary>
    private static void PointerTargets(Action<bool, string> check)
    {
        Saves.SavesSystem.Switch(101016);
        var story = new SkyIslandStoryService(); story.Open();
        int route = (int)(SkyIslandStoryFlag.PreludeAccepted | SkyIslandStoryFlag.PreludeInstrumentRecovered | SkyIslandStoryFlag.RouteUnlocked);
        int[] givers = { SkyIslandOfficialQuestTable.WeibaiGiverId, SkyIslandOfficialQuestTable.FuzhouGiverId, SkyIslandOfficialQuestTable.BellKeeperGiverId };
        foreach (bool chinese in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            // ① 居民不在岛上：圈与罗盘的字写兜底装置（与它走近时的交互名同词），不再叫人去找不在的人。
            string[] device = chinese ? new[] { "委托", "渡口整备", "钟守留言" } : new[] { "board", "dock refit", "bell keeper's message" };
            for (int i = 0; i < givers.Length; i++)
            {
                string present = SkyIslandMapMarkers.ContactTask(givers[i], true);
                string absent = SkyIslandMapMarkers.ContactTask(givers[i], false);
                string marker = SkyIslandOfficialQuestTable.FallbackMarkerOfGiver(givers[i]);
                check(present != null && absent != null && present != absent, "contact label differs when the giver is away: " + givers[i]);
                check(present.Contains(chinese ? "找" : "find "), "giver on the isles: the label says to find them: " + givers[i]);
                check(!absent.Contains(chinese ? "找" : "find ") && absent.ToLowerInvariant().Contains(device[i]),
                    "giver away: the label names the fallback device, not the missing person: " + givers[i]);
                check(SkyIslandPointText.Name(marker).ToLowerInvariant().Contains(device[i]),
                    "the fallback label uses the same word as the device's own interaction name: " + marker);
                check(chinese || (!Cjk(present) && !Cjk(absent)), "contact labels have an English half: " + givers[i]);
            }
            SkyIslandOfficialQuestDefinition bellCourt, homecoming;
            SkyIslandOfficialQuestTable.TryGet(SkyIslandOfficialQuestTable.BellCourtQuestId, out bellCourt);
            SkyIslandOfficialQuestTable.TryGet(SkyIslandOfficialQuestTable.HomecomingQuestId, out homecoming);
            check(bellCourt.Contact().ToLowerInvariant().Contains(device[1]) && homecoming.Contact().ToLowerInvariant().Contains(device[2]),
                "the HUD contact line names the fallback device for Fuzhou and the Bell Keeper, like Weibai's");

            // ② 敲钟：看得见的大钟挂在钟庭北头，交互点 Search_H 在西南角、相隔 55 米。指引要指圈里的「钟守留言」。
            var data = new SkyIslandStoryData
            {
                flags = route | (int)(SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered
                    | SkyIslandStoryFlag.BellCourtQuestAccepted | SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp
                    | SkyIslandStoryFlag.BellKeeperDefeated),
                clearedEncounters = new string[0], discoveredNotes = new string[0]
            };
            SkyIslandStoryData accepted; string reply;
            check(SkyIslandStoryRules.TryApply(data, SkyIslandStoryAction.AcceptHomecomingQuest, out accepted, out reply)
                && !reply.Contains(chinese ? "钟架" : "frame") && reply.Contains(chinese ? "钟守留言" : "Bell Keeper's message"),
                "taking The Homecoming Bell points at the circled message point, not the bell frame");
            Targets(accepted, check, "Search_H");
            string objective = SkyIslandStoryRules.Objective(accepted);
            check(!objective.Contains(chinese ? "钟架" : "frame") && objective.Contains(chinese ? "钟守留言" : "Bell Keeper's message")
                && objective.Contains(chinese ? "敲响归航钟" : "Ring the Homecoming Bell"),
                "bell objective names the circled interaction and the exact button");
            string task = homecoming.Tasks[0].Description(accepted);
            check(!task.Contains(chinese ? "归航钟跟前" : "Walk up to the Homecoming Bell") && task.Contains(chinese ? "钟守留言" : "Bell Keeper's message"),
                "quest log line points at the circled interaction, not the bell");
            check(!SkyIslandPointText.Brief("Search_H").Contains(chinese ? "这就是归航钟" : "This is the Homecoming Bell"),
                "the message point does not claim to be the bell itself");
            story.Current.flags = route;
            string home = story.DescribeNpc("sky_bellkeeper", true, false);
            check(!home.Contains(chinese ? "找我" : "come to the Bell Court") && home.Contains(chinese ? "钟守留言" : "Bell Keeper's message"),
                "Bell Keeper at home does not send the player to find him on the isles");

            // ③ D 组守卫在风标交互点以东 51 米：拒绝话与清场字幕写「风标东边」。
            var guards = new SkyIslandStoryData
            {
                flags = route | (int)SkyIslandStoryFlag.BeaconQuestAccepted,
                clearedEncounters = new[] { "D_02" }, discoveredNotes = new string[0]
            };
            string blocker;
            SkyIslandStoryRules.CanApply(guards, SkyIslandStoryAction.RepairWindBeacon, out blocker);
            string caption = SkyIslandStoryRules.GuardProgressCaption(guards, "D_02");
            foreach (string line in new[] { blocker, caption })
                check(line != null && line.Contains(chinese ? "风标东边" : "east of the wind beacon")
                    && !line.Contains(chinese ? "风标跟前" : "right by the wind beacon"), "the D guard group is placed east of the beacon: " + line);
            Targets(guards, check, "EnemySpawn_D", "EnemySpawn_G", "Search_G_02");

            // ④ 眠苔：只拿到旧信不叫人去见折翎（要旧信和航路图两样）；折翎了结后不再说他在等、不再劝人穿甲去谈。
            story.Current.flags = route | (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.OldLetter);
            story.Current.discoveredNotes = new string[0];
            string miantai = story.DescribeNpc("sky_miantai");
            check(miantai.Contains(chinese ? "听雨洞" : "Rainlisten Grotto") && !miantai.Contains(chinese ? "折翎在等" : "take them to Zheling"),
                "letter only: Miantai sends the player for the chart first");
            story.Current.flags |= (int)SkyIslandStoryFlag.RouteChart;
            check(story.DescribeNpc("sky_miantai").Contains(chinese ? "折翎在等" : "take them to Zheling"), "letter and chart: Miantai sends the player to Zheling");
            story.Current.flags |= (int)SkyIslandStoryFlag.ZhelingReconciled;
            story.Current.discoveredNotes = new[] { SkyIslandBossRules.PiperNote, SkyIslandBossRules.MirrorNote };
            miantai = story.DescribeNpc("sky_miantai");
            check(!miantai.Contains(chinese ? "镜水寺" : "Mirrorwater Temple") && !miantai.Contains(chinese ? "去见折翎" : "Zheling will talk"),
                "Zheling settled: Miantai no longer points at a talk that can't happen");
            story.Current.flags &= ~(int)SkyIslandStoryFlag.ZhelingReconciled;
            check(story.DescribeNpc("sky_miantai").Contains(chinese ? "去见折翎" : "Zheling will talk"),
                "Zheling unsettled: the mirror-plate hint is still there");
            story.Current.discoveredNotes = new string[0];

            // ⑤ 玩家反馈 2026-10-17「双航标门在哪里，风晶哪里烧」：结局后打过噬风，引风也在双航标门（Search_E）点，
            //    K3 开了也要圈它；HUD、钟守、手记、钟守的拒绝话都说出「栈道南头」。
            var echo = new SkyIslandStoryData
            {
                flags = route | (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp | SkyIslandStoryFlag.ShortcutK3
                    | SkyIslandStoryFlag.StormSlain | SkyIslandStoryFlag.BellKeeperDefeated | SkyIslandStoryFlag.Ending
                    | SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered
                    | SkyIslandStoryFlag.BellCourtQuestAccepted | SkyIslandStoryFlag.BellCourtQuestDelivered
                    | SkyIslandStoryFlag.HomecomingQuestAccepted | SkyIslandStoryFlag.HomecomingQuestDelivered),
                clearedEncounters = new string[0], discoveredNotes = new string[0]
            };
            check(SkyIslandMapMarkers.SideTargets(echo).Contains("Search_E"), "after the ending the echo gate stays circled even with K3 open");
            echo.flags &= ~(int)SkyIslandStoryFlag.Ending;
            check(!SkyIslandMapMarkers.SideTargets(echo).Contains("Search_E"), "before the bell rings a slain Windeater and an open K3 leave nothing to do at the gate");
            echo.flags |= (int)SkyIslandStoryFlag.Ending;
            string south = chinese ? "栈道南头的双航标门" : "south end of the boardwalk";
            check(SkyIslandStoryRules.Objective(echo).Contains(south) && SkyIslandStoryRules.Objective(echo).Contains(chinese ? "晴岚风晶" : "Qinglan Windcrystal"),
                "ending objective says where the echo is called and what to bring");
            story.Current.flags = echo.flags;
            string keeper = story.DescribeNpc("sky_bellkeeper");
            check(keeper.Contains(chinese ? "栈道南头" : "south end of the boardwalk") && keeper.Contains(chinese ? "双航标门" : "twin-beacon gate")
                && keeper.Contains(chinese ? "它会回来找你" : "it will come looking for you"),
                "the Bell Keeper says where to burn the windcrystal");
            check(SkyIslandJournal.Uses().Contains(chinese ? "南头的双航标门烧一块" : "burn one at the twin-beacon gate")
                && SkyIslandJournal.Uses().Contains(chinese ? "南头的双航标门「引风」" : "bring it to the twin-beacon gate"),
                "journal uses page names the gate for the windcrystal and the core");
            var peace = new SkyIslandStoryData
            {
                flags = route | (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp),
                clearedEncounters = new string[0], discoveredNotes = new string[0]
            };
            SkyIslandStoryRules.CanApply(peace, SkyIslandStoryAction.ReconcileBellKeeper, out blocker);
            check(blocker != null && blocker.Contains(chinese ? "栈道南头的双航标门" : "south end of the boardwalk"),
                "the Bell Keeper's refusal says which end of the boardwalk the gate is on");
            story.Current.flags = route;
        }
        L10n.IsChinese = true;
        story.Close();
    }

    private static bool Cjk(string text)
    {
        foreach (char c in text) if (c >= 0x4E00 && c <= 0x9FFF) return true;
        return false;
    }

    /// <summary>
    /// 2026-09-29 引导复核：修灯要先清两伙守卫，其中一伙离灯五六十米。地图、目标卡、面板拒绝与清场字幕
    /// 必须一路指到「还没清的那一伙」，清一伙就往前挪一步，不能只圈灯、只说「先清守卫」。
    /// </summary>
    private static void GuardGuidance(Action<bool, string> check)
    {
        foreach (bool chinese in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            var data = new SkyIslandStoryData
            {
                flags = (int)(SkyIslandStoryFlag.PreludeAccepted | SkyIslandStoryFlag.PreludeInstrumentRecovered
                    | SkyIslandStoryFlag.RouteUnlocked | SkyIslandStoryFlag.BeaconQuestAccepted),
                clearedEncounters = new string[0], discoveredNotes = new string[0]
            };
            Targets(data, check, "EnemySpawn_D", "Search_D_02", "EnemySpawn_G", "Search_G_02");
            string objective = SkyIslandStoryRules.Objective(data);
            check(objective.Contains("0/2") && !objective.Contains(chinese ? "（先清守卫）" : "(clear the guards first)"),
                "objective counts cleared guard groups instead of a vague 'clear the guards first'");
            string blocker;
            check(!SkyIslandStoryRules.CanApply(data, SkyIslandStoryAction.RepairWindBeacon, out blocker) && blocker != null &&
                blocker.Contains("2") && !blocker.Contains(chinese ? "林间道路" : "woodland path"),
                "beacon refusal names how many groups are left, not a place that doesn't exist on the map");
            check(SkyIslandStoryRules.GuardProgressCaption(data, "C") == null && SkyIslandStoryRules.GuardProgressCaption(data, "D") != null,
                "only beacon guard groups get a progress caption");

            data.clearedEncounters = new[] { "D" };
            Targets(data, check, "Search_D_02", "EnemySpawn_G", "Search_G_02");
            check(SkyIslandStoryRules.Objective(data).Contains("1/2"), "one cleared group advances the objective count");
            string caption = SkyIslandStoryRules.GuardProgressCaption(data, "D");
            check(caption != null && caption.Contains(chinese ? "药臼" : "mortar"), "after the first group the caption points at the other one");
            check(SkyIslandStoryRules.CanApply(data, SkyIslandStoryAction.RepairWindBeacon, out blocker) == false &&
                blocker.Contains(chinese ? "药臼" : "mortar"), "refusal points at the remaining group");

            data.clearedEncounters = new[] { "D", "D_02" };
            Targets(data, check, "Search_D", "EnemySpawn_G", "Search_G_02");
            caption = SkyIslandStoryRules.GuardProgressCaption(data, "D_02");
            check(caption != null && caption.Contains(chinese ? "可以过去修了" : "Go fix it"), "all guards down: the caption says go fix the lamp");
            check(SkyIslandStoryRules.CanApply(data, SkyIslandStoryAction.RepairWindBeacon, out blocker), "cleared guards unlock the repair");

            SkyIslandStoryData next; string message;
            check(SkyIslandStoryRules.TryApply(data, SkyIslandStoryAction.RepairWindBeacon, out next, out message) &&
                message.Contains(chinese ? "星灯" : "star lamp"), "fixing one lamp says which one is still left");
            data = next;
            check(SkyIslandStoryRules.GuardProgressCaption(data, "D") == null, "a fixed lamp stops talking about its guards");
            Targets(data, check, "EnemySpawn_G", "Search_G_02");

            // 钟庭：目标卡把最快的那条路（打停装置）和讲和的前置都写出来。
            data.flags |= (int)(SkyIslandStoryFlag.StarLamp | SkyIslandStoryFlag.BeaconQuestDelivered | SkyIslandStoryFlag.BellCourtQuestAccepted);
            objective = SkyIslandStoryRules.Objective(data);
            check(objective.Contains(chinese ? "守钟装置" : "bell engine") && objective.Contains(chinese ? "噬风" : "Windeater"),
                "bell court objective lists the fight option and the Windeater route to peace");
            // 2026-09-30：每条路都要写到「在哪儿、点哪一项」，按钮名与面板一致。
            check(objective.Contains(chinese ? "双航标门" : "twin-beacon gate") && objective.Contains(chinese ? "直面云海里的那阵风" : "Face the wind")
                && objective.Contains(chinese ? "挑战守钟装置" : "Challenge the bell engine"),
                "bell court objective says where each route starts and which option to pick");
            check(objective.IndexOf('…') < 0 && objective.IndexOf('。') < 0,
                "objective has no sentence breaks (Weibai used to read it aloud and the dialogue splits on them)");
            data.flags |= (int)SkyIslandStoryFlag.StormSlain;
            check(SkyIslandStoryRules.Objective(data).Contains(chinese ? "证据够了" : "you have the proof"),
                "after the Windeater the objective says peace is available");
            check(SkyIslandStoryRules.CombatOutcome(SkyIslandStoryFlag.StormSlain).Contains(chinese ? "钟庭" : "Bell Court"),
                "the Windeater caption says where to go next");
        }
        L10n.IsChinese = true;
    }

    private static void Apply(ref SkyIslandStoryData data, SkyIslandStoryAction action, Action<bool, string> check)
    {
        SkyIslandStoryData next; string message;
        check(SkyIslandStoryRules.TryApply(data, action, out next, out message), "navigation journey: " + action + " " + message);
        data = SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(next));
        check(data != null, "navigation state survives save/reload: " + action);
    }

    private static void Targets(SkyIslandStoryData data, Action<bool, string> check, params string[] expected)
    {
        string[] actual = SkyIslandMapMarkers.ObjectiveTargets(data).ToArray();
        check(actual.SequenceEqual(expected), "map/compass destination: expected " + string.Join(",", expected) + "; actual " + string.Join(",", actual));
        var contact = SkyIslandOfficialQuestTable.NextContactQuest(data);
        if (contact == null) return;
        string hud = SkyIslandStoryRules.Objective(data);
        check(hud.Contains(contact.Contact()) && hud.Contains(contact.Name()), "HUD and destination use the same pending contact");
    }
}
