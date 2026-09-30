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
            check(SkyIslandMapMarkers.SideTargets(data).Contains("POI_E"), "optional Windeater survives full quest delivery");
            check(SkyIslandMapMarkers.SideTargets(data).Contains("Search_S1"), "unfinished garden remains an optional destination");
        }
        L10n.IsChinese = true;
        GuardGuidance(check);
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
