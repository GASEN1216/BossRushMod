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
