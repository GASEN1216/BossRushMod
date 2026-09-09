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

    /// <summary>无 Unity 依赖的剧情规则。先创建候选状态，存档接受后才公布；战斗开始不写永久敌对。</summary>
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
            if (source == null) { message = "群岛记录尚未载入。"; return false; }
            SkyIslandStoryFlag flag;
            string required = null;
            switch (action)
            {
                case SkyIslandStoryAction.RepairWindBeacon:
                    flag = SkyIslandStoryFlag.WindBeacon;
                    if (!source.EncounterCleared("D") || !source.EncounterCleared("D_02")) required = "先清除悬根林风标与林间道路两处威胁。";
                    message = "风标重新转向云海。风铃集的西侧风铃亮了，回程绳桥可以重新系牢。"; break;
                case SkyIslandStoryAction.RepairStarLamp:
                    flag = SkyIslandStoryFlag.StarLamp;
                    if (!source.EncounterCleared("G") || !source.EncounterCleared("G_02")) required = "先清除残星工坊星灯与检修通道两处威胁。";
                    message = "星灯亮起，旧穹顶映出归航的方向。风铃集的东侧风铃有了回应。"; break;
                case SkyIslandStoryAction.FindPlantingRecord:
                    flag = SkyIslandStoryFlag.PlantingRecord; message = "找到了种植记录：风灾之后，晴禾仍为每一位归来的人留了一畦菜。可带回风铃集。"; break;
                case SkyIslandStoryAction.FindOldLetter:
                    flag = SkyIslandStoryFlag.OldLetter; message = "旧信没有寄出：『封路那天，我们看见了岸上的灯。请别让它熄灭。』可与航路图一同交给折翎。"; break;
                case SkyIslandStoryAction.FindRouteChart:
                    flag = SkyIslandStoryFlag.RouteChart; message = "航路图标着避风航道。封路并非唯一选择，只是当年没人同时看清两端航标。"; break;
                case SkyIslandStoryAction.RepairTelescope:
                    flag = SkyIslandStoryFlag.Telescope; message = "观星镜重新对准群岛。星灯回应了你的校准，归航钟庭多了一条安全航路的证据。"; break;
                case SkyIslandStoryAction.DeliverPlantingRecord:
                    flag = SkyIslandStoryFlag.PlantingDelivered;
                    if (!source.Has(SkyIslandStoryFlag.PlantingRecord)) required = "晴禾：种植记录落在蛙鸣池了，看到的话替我带回来吧。";
                    message = "晴禾：原来大家都还记得。最后一畦就种给下一艘归航船吧。菜畦挂起了新的风车。"; break;
                case SkyIslandStoryAction.OpenShortcutK1:
                    flag = SkyIslandStoryFlag.ShortcutK1;
                    if (!source.Has(SkyIslandStoryFlag.WindBeacon)) required = "风标恢复后才能安全系牢悬根林回程绳桥。";
                    message = "悬根林至风铃集的回程绳桥已系牢。"; break;
                case SkyIslandStoryAction.OpenShortcutK2:
                    flag = SkyIslandStoryFlag.ShortcutK2;
                    if (!source.Has(SkyIslandStoryFlag.StarLamp)) required = "星灯恢复后才能辨清残星工坊回程桥的锚点。";
                    message = "残星工坊至风铃集的回程桥已修复。"; break;
                case SkyIslandStoryAction.OpenShortcutK3:
                    flag = SkyIslandStoryFlag.ShortcutK3;
                    if (!source.BothBeacons) required = "先恢复东西两端航标。";
                    message = "鸣风栈道的双航标门开启，直达风铃集的回程路已恢复。"; break;
                case SkyIslandStoryAction.ReconcileZheling:
                    flag = SkyIslandStoryFlag.ZhelingReconciled;
                    if (source.ZhelingResolved) required = "折翎的选择已经记下；镜水寺的道路保持开放。";
                    else if (!source.Has(SkyIslandStoryFlag.OldLetter | SkyIslandStoryFlag.RouteChart)) required = "折翎：拿到倒挂邮亭的旧信和听雨洞的航路图，我们再谈。你也可以离开，或明确挑战我。";
                    message = "折翎放下武器：『我守住了路，却把回家的人也挡在外面。让我把它修好。』"; break;
                case SkyIslandStoryAction.ZhelingDefeated:
                    flag = SkyIslandStoryFlag.ZhelingDefeated;
                    if (source.ZhelingResolved) required = "折翎的结果已经记下。";
                    message = "折翎停下战斗，将旧腰牌留在路旁：『航路交给你。』镜水寺的路已开放。"; break;
                case SkyIslandStoryAction.ReconcileBellKeeper:
                    flag = SkyIslandStoryFlag.BellKeeperReconciled;
                    if (!source.BothBeacons) required = "归航钟需要风标与星灯同时回应。";
                    else if (source.BellKeeperResolved) required = "钟守已经放下了阻拦。";
                    // 原路线要求四件物证同时齐备，门槛过高；击败噬风是第五条、也是最直接的一条理由：
                    // 让他不肯敲钟的那阵风，已经不在云海上了。
                    else if (!source.Has(SkyIslandStoryFlag.OldLetter | SkyIslandStoryFlag.RouteChart | SkyIslandStoryFlag.Telescope | SkyIslandStoryFlag.ZhelingReconciled)
                        && !source.StormResolved)
                        required = "钟守仍不相信航路安全：需要旧信、航路图、修复的观星镜与折翎的和解；或者，去把鸣风栈道上那阵风解决掉。也可以挑战失控的守钟装置。";
                    message = "钟守：『这一次，钟声不是催他们出航，是告诉他们有人等着归来。』守钟装置停了。"; break;
                case SkyIslandStoryAction.BellKeeperDefeated:
                    flag = SkyIslandStoryFlag.BellKeeperDefeated;
                    if (!source.BothBeacons) required = "先恢复两端航标，再面对钟守。";
                    else if (source.BellKeeperResolved) required = "钟守的结果已经记下。";
                    message = "失控的守钟装置停下。钟守望向亮着的航标：『那就让钟声，为归来的人响一次。』"; break;
                case SkyIslandStoryAction.StormSlain:
                    flag = SkyIslandStoryFlag.StormSlain;
                    if (!source.BothBeacons) required = "两端航标都亮起来，它才会循着光过来。";
                    message = "噬风散了。折翎说的『封路』和钟守说的『不安全』，从今天起都少了一个理由。"; break;
                case SkyIslandStoryAction.RingHomecomingBell:
                    flag = SkyIslandStoryFlag.Ending;
                    if (!source.BothBeacons || !source.BellKeeperResolved) required = "先恢复两端航标，并解决钟守的阻拦。";
                    message = "归航钟响了。风铃集的灯沿云海依次亮起，浮舟把空船系在码头，留给下一位旅人。晴岚群岛仍欢迎你回来。"; break;
                default: message = "未知的群岛操作。"; return false;
            }
            if (source.Has(flag)) { message = "这段群岛见闻已经完成。"; return false; }
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
            if (data.Has(SkyIslandStoryFlag.Ending)) return "归航钟已响 · 自由重访群岛、补齐支线 · 码头或钟庭返航" +
                (data.StormResolved ? "" : " · 「噬风」仍在鸣风栈道");
            if (!data.BothBeacons) return "恢复两端航标：" + (data.Has(SkyIslandStoryFlag.WindBeacon) ? "风标已亮" : "悬根林风标") + " / " + (data.Has(SkyIslandStoryFlag.StarLamp) ? "星灯已亮" : "残星工坊星灯");
            if (!data.BellKeeperResolved) return "双航标已亮 · 经鸣风栈道前往归航钟庭 · 和解或战胜钟守" +
                (data.StormResolved ? "" : " · 栈道上可挑战「噬风」");
            return "钟守已放行 · 敲响归航钟，完成这次旅程";
        }
    }
}
