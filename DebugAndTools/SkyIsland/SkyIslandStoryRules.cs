using System;
using System.Collections.Generic;

namespace BossRush
{
    // COMPAT / SCHEMA+：只记录叙事事实；枚举位与存档 key 发布后冻结。
    [Flags]
    internal enum SkyIslandStoryFlag
    {
        None = 0, WindBeacon = 1, StarLamp = 2, PlantingRecord = 4,
        OldLetter = 8, RouteChart = 16, Telescope = 32, PlantingDelivered = 64,
        ShortcutK1 = 128, ShortcutK2 = 256, ShortcutK3 = 512,
        ZhelingReconciled = 1024, ZhelingDefeated = 2048,
        BellKeeperReconciled = 4096, BellKeeperDefeated = 8192, Ending = 16384,
        /// <summary>SCHEMA+：新增位，旧档读出来为 0，语义即「尚未挑战噬风」。</summary>
        StormSlain = 32768
    }

    internal enum SkyIslandStoryAction
    {
        RepairWindBeacon, RepairStarLamp, FindPlantingRecord, FindOldLetter,
        FindRouteChart, RepairTelescope, DeliverPlantingRecord,
        OpenShortcutK1, OpenShortcutK2, OpenShortcutK3,
        ReconcileZheling, ZhelingDefeated, ReconcileBellKeeper, BellKeeperDefeated, RingHomecomingBell,
        StormSlain
    }

    [Serializable]
    internal sealed class SkyIslandStoryData
    {
        public int schemaVersion;
        public int flags;
        public int visitedRegions;
        public string[] clearedEncounters;
        public string[] discoveredNotes;

        internal bool Has(SkyIslandStoryFlag value) { return (flags & (int)value) == (int)value; }
        internal bool BothBeacons { get { return Has(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp); } }
        internal bool ZhelingResolved { get { return Has(SkyIslandStoryFlag.ZhelingReconciled) || Has(SkyIslandStoryFlag.ZhelingDefeated); } }
        internal bool BellKeeperResolved { get { return Has(SkyIslandStoryFlag.BellKeeperReconciled) || Has(SkyIslandStoryFlag.BellKeeperDefeated); } }
        internal bool StormResolved { get { return Has(SkyIslandStoryFlag.StormSlain); } }
        internal bool EncounterCleared(string id) { return Array.IndexOf(clearedEncounters ?? new string[0], id) >= 0; }
        internal SkyIslandStoryData Copy()
        {
            return new SkyIslandStoryData { schemaVersion = schemaVersion, flags = flags, visitedRegions = visitedRegions,
                clearedEncounters = (string[])(clearedEncounters ?? new string[0]).Clone(),
                discoveredNotes = (string[])(discoveredNotes ?? new string[0]).Clone() };
        }
    }

    /// <summary>
    /// 无 Unity 依赖的剧情规则（文案走 `L10n.T`，隔离回归给个最小替身即可）。
    /// 先创建候选状态，存档接受后才公布；战斗开始不写永久敌对。
    /// </summary>
    internal static class SkyIslandStoryRules
    {
        internal const int SchemaVersion = 1;
        internal const string StorageKey = "BossRush_SkyIsland_Story_v1";
        /// <summary>已登记位的并集。新增位必须同时更新这里，否则解码会拒绝整份存档。</summary>
        internal const int KnownFlags = 65535;
        internal static SkyIslandStoryData CreateDefault()
        {
            return new SkyIslandStoryData { schemaVersion = SchemaVersion, flags = 0,
                clearedEncounters = new string[0], discoveredNotes = new string[0] };
        }

        internal static bool TryApply(SkyIslandStoryData source, SkyIslandStoryAction action,
            out SkyIslandStoryData candidate, out string message)
        {
            candidate = null;
            if (source == null)
            { message = L10n.T("群岛记录尚未载入。", "The archipelago record has not loaded yet."); return false; }
            SkyIslandStoryFlag flag;
            string required = null;
            switch (action)
            {
                case SkyIslandStoryAction.RepairWindBeacon:
                    flag = SkyIslandStoryFlag.WindBeacon;
                    if (!source.EncounterCleared("D") || !source.EncounterCleared("D_02"))
                        required = L10n.T("先清除悬根林风标与林间道路两处威胁。",
                            "Clear both threats first: the Hanging Root Wood beacon and the woodland path.");
                    message = L10n.T("风标重新转向云海。风铃集的西侧风铃亮了，回程绳桥可以重新系牢。",
                        "The wind beacon turns back toward the cloud sea. The west chimes of Windchime Market are lit, and the rope bridge home can be lashed tight again."); break;
                case SkyIslandStoryAction.RepairStarLamp:
                    flag = SkyIslandStoryFlag.StarLamp;
                    if (!source.EncounterCleared("G") || !source.EncounterCleared("G_02"))
                        required = L10n.T("先清除残星工坊星灯与检修通道两处威胁。",
                            "Clear both threats first: the Fallen Star Workshop lamp and the maintenance passage.");
                    message = L10n.T("星灯亮起，旧穹顶映出归航的方向。风铃集的东侧风铃有了回应。",
                        "The star lamp comes up and the old dome throws back the way home. The east chimes of Windchime Market answer it."); break;
                case SkyIslandStoryAction.FindPlantingRecord:
                    flag = SkyIslandStoryFlag.PlantingRecord;
                    message = L10n.T("找到了种植记录：风灾之后，晴禾仍为每一位归来的人留了一畦菜。可带回风铃集。",
                        "The planting record: after the storm, Qinghe still kept a bed of greens for everyone who came home. It can go back to Windchime Market."); break;
                case SkyIslandStoryAction.FindOldLetter:
                    flag = SkyIslandStoryFlag.OldLetter;
                    message = L10n.T("旧信没有寄出：『封路那天，我们看见了岸上的灯。请别让它熄灭。』可与航路图一同交给折翎。",
                        "The letter was never sent: 'On the day the lanes closed we saw the light on shore. Please do not let it go out.' It can go to Zheling together with the route chart."); break;
                case SkyIslandStoryAction.FindRouteChart:
                    flag = SkyIslandStoryFlag.RouteChart;
                    message = L10n.T("航路图标着避风航道。封路并非唯一选择，只是当年没人同时看清两端航标。",
                        "The route chart marks a sheltered lane. Closing the lanes was never the only option — nobody back then could see both beacons at once."); break;
                case SkyIslandStoryAction.RepairTelescope:
                    flag = SkyIslandStoryFlag.Telescope;
                    message = L10n.T("观星镜重新对准群岛。星灯回应了你的校准，归航钟庭多了一条安全航路的证据。",
                        "The telescope is trained on the archipelago again. The star lamp answers your calibration, and the Bell Court gains one more proof that the lanes are safe."); break;
                case SkyIslandStoryAction.DeliverPlantingRecord:
                    flag = SkyIslandStoryFlag.PlantingDelivered;
                    if (!source.Has(SkyIslandStoryFlag.PlantingRecord))
                        required = L10n.T("晴禾：种植记录落在蛙鸣池了，看到的话替我带回来吧。",
                            "Qinghe: I left the planting record at Frogsong Pool. Bring it back for me if you see it.");
                    message = L10n.T("晴禾：原来大家都还记得。最后一畦就种给下一艘归航船吧。菜畦挂起了新的风车。",
                        "Qinghe: So everyone still remembers. Then the last bed goes to the next ship home. A new pinwheel goes up over the garden."); break;
                case SkyIslandStoryAction.OpenShortcutK1:
                    flag = SkyIslandStoryFlag.ShortcutK1;
                    if (!source.Has(SkyIslandStoryFlag.WindBeacon))
                        required = L10n.T("风标恢复后才能安全系牢悬根林回程绳桥。",
                            "The Hanging Root Wood rope bridge can only be lashed safely once the wind beacon is back.");
                    message = L10n.T("悬根林至风铃集的回程绳桥已系牢。",
                        "The rope bridge from Hanging Root Wood back to Windchime Market is lashed tight."); break;
                case SkyIslandStoryAction.OpenShortcutK2:
                    flag = SkyIslandStoryFlag.ShortcutK2;
                    if (!source.Has(SkyIslandStoryFlag.StarLamp))
                        required = L10n.T("星灯恢复后才能辨清残星工坊回程桥的锚点。",
                            "The anchors of the Fallen Star Workshop return bridge are only readable once the star lamp is back.");
                    message = L10n.T("残星工坊至风铃集的回程桥已修复。",
                        "The return bridge from Fallen Star Workshop to Windchime Market is repaired."); break;
                case SkyIslandStoryAction.OpenShortcutK3:
                    flag = SkyIslandStoryFlag.ShortcutK3;
                    if (!source.BothBeacons)
                        required = L10n.T("先恢复东西两端航标。", "Restore both the east and west beacons first.");
                    message = L10n.T("鸣风栈道的双航标门开启，直达风铃集的回程路已恢复。",
                        "The twin-beacon gate on Windsong Boardwalk opens; the direct way back to Windchime Market is restored."); break;
                case SkyIslandStoryAction.ReconcileZheling:
                    flag = SkyIslandStoryFlag.ZhelingReconciled;
                    if (source.ZhelingResolved)
                        required = L10n.T("折翎的选择已经记下；镜水寺的道路保持开放。",
                            "Zheling's choice is already on record; the Mirrorwater Temple road stays open.");
                    else if (!source.Has(SkyIslandStoryFlag.OldLetter | SkyIslandStoryFlag.RouteChart))
                        required = L10n.T("折翎：拿到倒挂邮亭的旧信和听雨洞的航路图，我们再谈。你也可以离开，或明确挑战我。",
                            "Zheling: Bring the old letter from the Upturned Post Hut and the route chart from the Rainlisten Grotto, and then we talk. You may also walk away, or challenge me outright.");
                    message = L10n.T("折翎放下武器：『我守住了路，却把回家的人也挡在外面。让我把它修好。』",
                        "Zheling lowers his weapon: 'I held the road, and shut out the people coming home along with it. Let me put that right.'"); break;
                case SkyIslandStoryAction.ZhelingDefeated:
                    flag = SkyIslandStoryFlag.ZhelingDefeated;
                    if (source.ZhelingResolved)
                        required = L10n.T("折翎的结果已经记下。", "Zheling's outcome is already on record.");
                    message = L10n.T("折翎停下战斗，将旧腰牌留在路旁：『航路交给你。』镜水寺的路已开放。",
                        "Zheling breaks off the fight and leaves his old badge by the road: 'The route is yours now.' The Mirrorwater Temple road is open."); break;
                case SkyIslandStoryAction.ReconcileBellKeeper:
                    flag = SkyIslandStoryFlag.BellKeeperReconciled;
                    if (!source.BothBeacons)
                        required = L10n.T("归航钟需要风标与星灯同时回应。",
                            "The Homecoming Bell needs the wind beacon and the star lamp answering together.");
                    else if (source.BellKeeperResolved)
                        required = L10n.T("钟守已经放下了阻拦。", "The Bell Keeper has already stood down.");
                    // 原路线要求四件物证同时齐备，门槛过高；击败噬风是第五条、也是最直接的一条理由：
                    // 让他不肯敲钟的那阵风，已经不在云海上了。
                    //
                    // 折翎那一项认「已了结」而不是只认和解：同一份内容表里 ZhelingPass 门就是
                    // `ZhelingReconciled | ZhelingDefeated` 任一即开（`SkyIslandContent.CreateFallback`），
                    // 唯独这句只认和解——选择挑战折翎的玩家会被永久关在物证路线之外，
                    // 而失败提示还在要求他去「与折翎和解」，那是一个再也不可能达成的条件
                    // （`ReconcileZheling` 对已了结的折翎直接拒绝）。战胜同样解除了封路，
                    // 他留下的旧腰牌写着『航路交给你』，作为「航路已经通了」的物证成立。
                    else if (!(source.Has(SkyIslandStoryFlag.OldLetter | SkyIslandStoryFlag.RouteChart | SkyIslandStoryFlag.Telescope)
                        && source.ZhelingResolved) && !source.StormResolved)
                        required = L10n.T("钟守仍不相信航路安全：需要旧信、航路图、修复的观星镜，以及折翎那一关有个了结（和解或战胜都算）；或者，去把鸣风栈道上那阵风解决掉。也可以挑战失控的守钟装置。",
                            "The Bell Keeper still does not believe the lanes are safe. Bring the old letter, the route chart and a repaired telescope, and settle Zheling one way or the other (reconciling and defeating both count) — or simply deal with the wind out on Windsong Boardwalk. Challenging the runaway bell engine is also an option.");
                    message = L10n.T("钟守：『这一次，钟声不是催他们出航，是告诉他们有人等着归来。』守钟装置停了。",
                        "The Bell Keeper: 'This time the bell is not sending them out. It is telling them someone is waiting for them to come home.' The bell engine falls still."); break;
                case SkyIslandStoryAction.BellKeeperDefeated:
                    flag = SkyIslandStoryFlag.BellKeeperDefeated;
                    if (!source.BothBeacons)
                        required = L10n.T("先恢复两端航标，再面对钟守。",
                            "Restore both beacons before facing the Bell Keeper.");
                    else if (source.BellKeeperResolved)
                        required = L10n.T("钟守的结果已经记下。", "The Bell Keeper's outcome is already on record.");
                    message = L10n.T("失控的守钟装置停下。钟守望向亮着的航标：『那就让钟声，为归来的人响一次。』",
                        "The runaway bell engine stops. The Bell Keeper looks out at the lit beacons: 'Then let the bell ring once, for the ones coming home.'"); break;
                case SkyIslandStoryAction.StormSlain:
                    flag = SkyIslandStoryFlag.StormSlain;
                    if (!source.BothBeacons)
                        required = L10n.T("两端航标都亮起来，它才会循着光过来。",
                            "It only comes for the light once both beacons burn.");
                    message = L10n.T("噬风散了。折翎说的『封路』和钟守说的『不安全』，从今天起都少了一个理由。",
                        "The Windeater is gone. Zheling's 'close the lanes' and the Bell Keeper's 'it is not safe' each lost a reason today."); break;
                case SkyIslandStoryAction.RingHomecomingBell:
                    flag = SkyIslandStoryFlag.Ending;
                    if (!source.BothBeacons || !source.BellKeeperResolved)
                        required = L10n.T("先恢复两端航标，并解决钟守的阻拦。",
                            "Restore both beacons and settle the Bell Keeper's objection first.");
                    message = L10n.T("归航钟响了。风铃集的灯沿云海依次亮起，浮舟把空船系在码头，留给下一位旅人。晴岚群岛仍欢迎你回来。",
                        "The Homecoming Bell rings. The lights of Windchime Market come up one by one along the cloud sea, and Fuzhou ties the empty boat at the dock for the next traveller. Qinglan will always welcome you back."); break;
                default: message = L10n.T("未知的群岛操作。", "Unknown archipelago action."); return false;
            }
            if (source.Has(flag))
            { message = L10n.T("这段群岛见闻已经完成。", "That part of the archipelago is already done."); return false; }
            if (required != null) { message = required; return false; }
            candidate = source.Copy();
            candidate.flags |= (int)flag;
            return true;
        }

        internal static bool TrySearchAction(string marker, out SkyIslandStoryAction action)
        {
            switch (marker)
            {
                case "Search_S1": action = SkyIslandStoryAction.FindPlantingRecord; return true;
                case "Search_S2": action = SkyIslandStoryAction.FindOldLetter; return true;
                case "Search_S3": action = SkyIslandStoryAction.FindRouteChart; return true;
                case "Search_S4": action = SkyIslandStoryAction.RepairTelescope; return true;
                default: action = default(SkyIslandStoryAction); return false;
            }
        }

        internal static string Objective(SkyIslandStoryData data)
        {
            if (data.Has(SkyIslandStoryFlag.Ending))
                return L10n.T("归航钟已响 · 自由重访群岛、补齐支线 · 码头或钟庭返航",
                    "The bell has rung · revisit freely and finish the side paths · extract at the dock or the Bell Court") +
                    (data.StormResolved ? "" : L10n.T(" · 「噬风」仍在鸣风栈道",
                        " · the Windeater is still on Windsong Boardwalk"));
            if (!data.BothBeacons)
                return L10n.T("恢复两端航标：", "Restore both beacons: ") +
                    (data.Has(SkyIslandStoryFlag.WindBeacon)
                        ? L10n.T("风标已亮", "wind beacon lit")
                        : L10n.T("悬根林风标", "Hanging Root Wood beacon")) + " / " +
                    (data.Has(SkyIslandStoryFlag.StarLamp)
                        ? L10n.T("星灯已亮", "star lamp lit")
                        : L10n.T("残星工坊星灯", "Fallen Star Workshop lamp"));
            if (!data.BellKeeperResolved)
                return L10n.T("双航标已亮 · 经鸣风栈道前往归航钟庭 · 和解或战胜钟守",
                    "Both beacons lit · cross Windsong Boardwalk to the Bell Court · reconcile with or defeat the Bell Keeper") +
                    (data.StormResolved ? "" : L10n.T(" · 栈道上可挑战「噬风」",
                        " · the Windeater can be challenged on the boardwalk"));
            return L10n.T("钟守已放行 · 敲响归航钟，完成这次旅程",
                "The Bell Keeper stands aside · ring the Homecoming Bell to finish the journey");
        }
    }
}
