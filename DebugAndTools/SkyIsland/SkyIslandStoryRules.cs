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
        StormSlain = 32768,
        /// <summary>Jeff 已把零号区的异常坠落线索交给玩家。</summary>
        PreludeAccepted = 65536,
        /// <summary>玩家在零号区击败断风游猎并读出了失落航向仪。</summary>
        PreludeInstrumentRecovered = 131072,
        /// <summary>玩家把航向仪坐标交回 Jeff，基地船点正式开放晴岚航线。</summary>
        RouteUnlocked = 262144,
        /// <summary>SCHEMA+：岛上主线三条官方任务的接取 / 交付事实（各两位）。旧档读出 0 = 从未接过；官方 history 每次都被剥掉，读档只靠这两位重建投影。</summary>
        BeaconQuestAccepted = 524288,
        BeaconQuestDelivered = 1048576,
        BellCourtQuestAccepted = 2097152,
        BellCourtQuestDelivered = 4194304,
        HomecomingQuestAccepted = 8388608,
        HomecomingQuestDelivered = 16777216
    }

    internal enum SkyIslandStoryAction
    {
        RepairWindBeacon, RepairStarLamp, FindPlantingRecord, FindOldLetter,
        FindRouteChart, RepairTelescope, DeliverPlantingRecord,
        OpenShortcutK1, OpenShortcutK2, OpenShortcutK3,
        ReconcileZheling, ZhelingDefeated, ReconcileBellKeeper, BellKeeperDefeated, RingHomecomingBell,
        StormSlain, AcceptPrelude, RecoverPreludeInstrument, UnlockRoute,
        AcceptBeaconQuest, DeliverBeaconQuest, AcceptBellCourtQuest, DeliverBellCourtQuest,
        AcceptHomecomingQuest, DeliverHomecomingQuest
    }

    [Serializable]
    internal sealed class SkyIslandStoryData
    {
        public int schemaVersion;
        public int flags;
        public int visitedRegions;
        /// <summary>SCHEMA+：旧版岛上任务回填只执行一次。旧 JSON 缺省 false；正常解锁航线或接取任务时置 true。</summary>
        public bool islandQuestMigrationComplete;
        public string[] clearedEncounters;
        public string[] discoveredNotes;
        /// <summary>
        /// 运行时注入、不进存档：主角此刻穿着镜中客的镜纹甲（头目 R4）。折翎认得那身纹路，和解可以不带旧信与航路图。
        /// 由局内 owner 按穿戴采样写（SkyIslandFieldcraftBossGear）；默认 false，隔离回归不写就等于没穿。
        /// </summary>
        [NonSerialized] internal bool wearsMirrorArmor;

        internal bool Has(SkyIslandStoryFlag value) { return (flags & (int)value) == (int)value; }
        internal bool BothBeacons { get { return Has(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp); } }
        internal bool ZhelingResolved { get { return Has(SkyIslandStoryFlag.ZhelingReconciled) || Has(SkyIslandStoryFlag.ZhelingDefeated); } }
        internal bool BellKeeperResolved { get { return Has(SkyIslandStoryFlag.BellKeeperReconciled) || Has(SkyIslandStoryFlag.BellKeeperDefeated); } }
        internal bool StormResolved { get { return Has(SkyIslandStoryFlag.StormSlain); } }
        internal bool SkyIslandRouteUnlocked { get { return Has(SkyIslandStoryFlag.RouteUnlocked); } }
        internal bool EncounterCleared(string id) { return Array.IndexOf(clearedEncounters ?? new string[0], id) >= 0; }
        internal SkyIslandStoryData Copy()
        {
            return new SkyIslandStoryData { schemaVersion = schemaVersion, flags = flags, visitedRegions = visitedRegions,
                clearedEncounters = (string[])(clearedEncounters ?? new string[0]).Clone(),
                discoveredNotes = (string[])(discoveredNotes ?? new string[0]).Clone(), wearsMirrorArmor = wearsMirrorArmor,
                islandQuestMigrationComplete = islandQuestMigrationComplete };
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
        internal const int LegacyKnownFlags = 65535;
        internal const int KnownFlags = 33554431;
        internal static SkyIslandStoryData CreateDefault()
        {
            return new SkyIslandStoryData { schemaVersion = SchemaVersion, flags = 0,
                clearedEncounters = new string[0], discoveredNotes = new string[0] };
        }

        /// <summary>
        /// 入口门发布前已经玩过天空岛的槽位必须继续可进。只认实际群岛事实；一个完全空的新槽不会被迁移。
        /// 旧旗标、到访、清场或手记任一存在都说明该槽以前已经完成过一次正式进入。
        /// </summary>
        internal static bool HasLegacyIslandProgress(SkyIslandStoryData data)
        {
            return data != null && (((data.flags & LegacyKnownFlags) != 0) || data.visitedRegions != 0 ||
                (data.clearedEncounters != null && data.clearedEncounters.Length > 0) ||
                (data.discoveredNotes != null && data.discoveredNotes.Length > 0));
        }

        internal static bool TryGrantLegacyRoute(SkyIslandStoryData source, out SkyIslandStoryData candidate)
        {
            candidate = null;
            if (source == null || source.SkyIslandRouteUnlocked || !HasLegacyIslandProgress(source)) return false;
            candidate = source.Copy();
            candidate.flags |= (int)(SkyIslandStoryFlag.PreludeAccepted |
                SkyIslandStoryFlag.PreludeInstrumentRecovered | SkyIslandStoryFlag.RouteUnlocked);
            return true;
        }

        /// <summary>岛上主线任务的接取 + 交付两位。</summary>
        internal static readonly SkyIslandStoryFlag[][] IslandQuestFlags =
        {
            new[] { SkyIslandStoryFlag.BeaconQuestAccepted, SkyIslandStoryFlag.BeaconQuestDelivered },
            new[] { SkyIslandStoryFlag.BellCourtQuestAccepted, SkyIslandStoryFlag.BellCourtQuestDelivered },
            new[] { SkyIslandStoryFlag.HomecomingQuestAccepted, SkyIslandStoryFlag.HomecomingQuestDelivered }
        };

        /// <summary>
        /// 官方任务上线前已经打通过的槽不该被再问一遍「去点灯吧」：按既有事实把对应任务补成已接取 + 已交付。
        /// 只对已解锁航线的槽做（岛上任务只能在岛上接）；一个什么都没做过的槽不动。
        /// </summary>
        internal static bool TryBackfillIslandQuests(SkyIslandStoryData source, out SkyIslandStoryData candidate)
        {
            candidate = null;
            if (source == null || !source.SkyIslandRouteUnlocked || source.islandQuestMigrationComplete) return false;
            // 已有任务位说明玩家正在使用官方任务链。无论目标是否完成，都不能替玩家交付。
            bool hasQuestProgress = false;
            foreach (SkyIslandStoryFlag[] pair in IslandQuestFlags)
                if (source.Has(pair[0]) || source.Has(pair[1])) hasQuestProgress = true;
            int grant = 0;
            if (!hasQuestProgress)
            {
                if (source.BothBeacons) grant |= (int)(SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered);
                if (source.BothBeacons && source.BellKeeperResolved) grant |= (int)(SkyIslandStoryFlag.BellCourtQuestAccepted | SkyIslandStoryFlag.BellCourtQuestDelivered);
                if (source.Has(SkyIslandStoryFlag.Ending)) grant |= (int)(SkyIslandStoryFlag.HomecomingQuestAccepted | SkyIslandStoryFlag.HomecomingQuestDelivered);
            }
            candidate = source.Copy();
            candidate.flags |= grant;
            // 没有可回填的目标也要记完成；否则下一次出击产生的新事实还会被误当成旧档事实。
            candidate.islandQuestMigrationComplete = true;
            return true;
        }

        internal static bool TryApply(SkyIslandStoryData source, SkyIslandStoryAction action,
            out SkyIslandStoryData candidate, out string message)
        {
            candidate = null;
            if (source == null)
            { message = L10n.T("群岛记录尚未载入。", "The archipelago record has not loaded yet."); return false; }
            SkyIslandStoryFlag flag;
            string required;
            if (!Describe(source, action, out flag, out required, out message)) return false;
            if (source.Has(flag))
            { message = L10n.T("这段群岛见闻已经完成。", "That part of the archipelago story is already complete."); return false; }
            if (required != null) { message = required; return false; }
            candidate = source.Copy();
            candidate.flags |= (int)flag;
            if (action == SkyIslandStoryAction.UnlockRoute || action == SkyIslandStoryAction.AcceptBeaconQuest ||
                action == SkyIslandStoryAction.AcceptBellCourtQuest || action == SkyIslandStoryAction.AcceptHomecomingQuest)
                candidate.islandQuestMigrationComplete = true;
            return true;
        }

        /// <summary>
        /// 这一步**此刻能不能做**。判据与 <see cref="TryApply"/> 共用同一个 <see cref="Describe"/>，
        /// 所以「选项挂不挂得出来」与「点了会不会被拒」永远不会分叉。
        ///
        /// 【为什么需要它】旧写法是选项一律先挂上、前置判断全丢进回调，于是新档走进归航钟庭
        /// 就看得见「敲响归航钟」，点一下回一句「先恢复两端航标，并解决钟守的阻拦」；
        /// 交还种植记录之后「把种植记录留给晴禾」还挂在那儿，点了回「这段群岛见闻已经完成」。
        /// 玩家看到的是一屏点不动的按钮。
        /// </summary>
        /// <param name="blocker">
        /// 前置没满足时给出「还差什么」；**已经做完时是 null**——那不是引导，是噪声。
        /// 调用方（<c>SkyIslandWorldStory.AddIf</c>）把它收进正文，而不是留一个点不动的按钮。
        /// </param>
        internal static bool CanApply(SkyIslandStoryData source, SkyIslandStoryAction action,
            out string blocker)
        {
            blocker = null;
            if (source == null) return false;
            SkyIslandStoryFlag flag;
            string required, message;
            if (!Describe(source, action, out flag, out required, out message)) return false;
            if (source.Has(flag)) return false;
            // 走了互斥分支（战胜折翎 / 钟守之后的和解）：这一步永远不会再有，同样不给 blocker——
            // 否则「钟守已经放下了阻拦」会作为「下一步」永久挂在正文里（2026-09-14 审核 F-28）。
            if (Foreclosed(source, action)) return false;
            if (required != null) { blocker = required; return false; }
            return true;
        }

        /// <summary>具名对手已经了结（和解或战胜）之后，另一条互斥分支上的动作永久关闭。</summary>
        private static bool Foreclosed(SkyIslandStoryData source, SkyIslandStoryAction action)
        {
            // 写成 if 而不是 switch：守卫按 `case SkyIslandStoryAction.X:` 在 Describe 里切分支，这里不能再出现同样的记号。
            if (action == SkyIslandStoryAction.ReconcileZheling || action == SkyIslandStoryAction.ZhelingDefeated)
                return source.ZhelingResolved;
            if (action == SkyIslandStoryAction.ReconcileBellKeeper || action == SkyIslandStoryAction.BellKeeperDefeated)
                return source.BellKeeperResolved;
            return false;
        }

        /// <summary>
        /// 秘境物证点（S1–S4）收录时写哪个剧情旗标。它们走剧情动作（<see cref="TrySearchAction"/>）、不进 `discoveredNotes`，
        /// 手记与官方图鉴镜像据此判「已收录」。旗标取自同一份 <see cref="Describe"/>，不另写一张对应表。
        /// </summary>
        internal static bool TrySearchEvidenceFlag(string marker, out SkyIslandStoryFlag flag)
        {
            flag = SkyIslandStoryFlag.None;
            SkyIslandStoryAction action;
            if (!TrySearchAction(marker, out action)) return false;
            string required, message;
            return Describe(CreateDefault(), action, out flag, out required, out message) && flag != SkyIslandStoryFlag.None;
        }

        /// <summary>引风（噬风·回响）烧掉几块晴岚风晶。回响的奖励表与会话的预留读的都是它。</summary>
        internal const int StormEchoWindcrystalCost = 1;

        /// <summary>
        /// 噬风·回响**此刻能不能引**。「装置上挂不挂『引风』」与「点下去会不会被拒」只认这一份（口径同 <see cref="CanApply"/>）。
        /// 五项全要：敲过钟、打过噬风、这一趟还没引过、噬风之核在背包里、晴岚风晶够烧。
        /// 回响按本趟计、不写存档，所以不进 <see cref="Describe"/> 的旗标表。纯函数，隔离回归逐项翻转核对。
        /// </summary>
        /// <param name="blocker">
        /// 前置没满足时「还差什么」。还没打过噬风（首战那一项还挂着）与这一趟已经引过时是 null——那不是引导，是噪声。
        /// </param>
        internal static bool CanSummonStormEcho(SkyIslandStoryData data, bool usedThisRaid, bool coreCarried, int windcrystals,
            out string blocker)
        {
            blocker = null;
            if (data == null || !data.StormResolved) return false;
            if (!data.Has(SkyIslandStoryFlag.Ending))
            {
                blocker = L10n.T("栈道上那阵风散了。等归航钟响过，带着噬风之核、烧一块晴岚风晶，还能在这里把它的回响引回来。",
                    "The wind on the boardwalk is gone. Once the Homecoming Bell has rung, bring the Windeater Core and burn a Qinglan Windcrystal here to call its echo back.");
                return false;
            }
            if (usedThisRaid) return false;
            if (!coreCarried)
            {
                blocker = L10n.T("引风要把噬风之核带在背包里（不会用掉）。它若还在基地仓库，下次上岛带上。",
                    "Calling the wind needs the Windeater Core in your pack (it is not used up). If it is still in base storage, bring it next trip.");
                return false;
            }
            if (windcrystals < StormEchoWindcrystalCost)
            {
                blocker = L10n.T("引风要烧一块晴岚风晶：五片风晶碎片在浮舟的渡口工台熔成一块。",
                    "Calling the wind burns a Qinglan Windcrystal: five windcrystal shards fuse into one at Fuzhou's dock workbench.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 一个剧情动作的三件事：写哪个标志位、此刻缺什么前置（<paramref name="required"/>，
        /// null = 不缺）、做成了回什么话。**<see cref="TryApply"/> 与 <see cref="CanApply"/>
        /// 都只认这一份**——这是「门」的单一事实来源。
        /// 返回 false 表示这个 action 根本不认识。
        /// </summary>
        private static bool Describe(SkyIslandStoryData source, SkyIslandStoryAction action,
            out SkyIslandStoryFlag flag, out string required, out string message)
        {
            flag = 0;
            required = null;
            switch (action)
            {
                case SkyIslandStoryAction.AcceptPrelude:
                    flag = SkyIslandStoryFlag.PreludeAccepted;
                    message = L10n.T("Jeff：零号区落下了一具不属于地面的航向仪。把守它的家伙也不像本地拾荒者。去把仪器里的坐标读回来。",
                        "Jeff: A navigation instrument fell into Ground Zero, and its guard is no local scavenger. Bring back the coordinates stored inside it."); break;
                case SkyIslandStoryAction.RecoverPreludeInstrument:
                    flag = SkyIslandStoryFlag.PreludeInstrumentRecovered;
                    if (!source.Has(SkyIslandStoryFlag.PreludeAccepted))
                        required = L10n.T("先向基地的 Jeff 询问异常坠落线索。",
                            "Ask Jeff at the base about the strange wreckage first.");
                    message = L10n.T("航向仪仍在工作。残缺记录反复指向云层之上的『晴岚群岛』；把坐标交给 Jeff。",
                        "The instrument still works. Its damaged log points again and again to the Qinglan Archipelago above the clouds. Take the coordinates to Jeff."); break;
                case SkyIslandStoryAction.UnlockRoute:
                    flag = SkyIslandStoryFlag.RouteUnlocked;
                    if (!source.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered))
                        required = L10n.T("先在零号区找到失落的航向仪。",
                            "Find the lost navigation instrument in Ground Zero first.");
                    message = L10n.T("Jeff 校准了坐标，并让基地船工把晴岚航线写进航路表。天空岛旅程现在可以从船点出发。",
                        "Jeff calibrates the coordinates and has the base crew add Qinglan to the route table. The Sky Islands journey can now depart from the boat."); break;
                case SkyIslandStoryAction.AcceptBeaconQuest:
                    flag = SkyIslandStoryFlag.BeaconQuestAccepted;
                    if (!source.SkyIslandRouteUnlocked)
                        required = L10n.T("先向 Jeff 交付坐标，开放晴岚航线。", "Deliver the coordinates to Jeff to open the Qinglan route first.");
                    message = L10n.T("已接下航路委托：确认两端航标都已修复，再提交记录。",
                        "Route commission accepted: verify both beacons are repaired, then submit the record."); break;
                case SkyIslandStoryAction.DeliverBeaconQuest:
                    flag = SkyIslandStoryFlag.BeaconQuestDelivered;
                    if (!source.Has(SkyIslandStoryFlag.BeaconQuestAccepted))
                        required = L10n.T("先找苇白接下航标任务；她不在时，用风铃集委托板的「航路任务」。", "Take the beacon quest from Weibai first, or use Route quests at the Windchime Market board when she is away.");
                    else if (!source.BothBeacons)
                        required = L10n.T("风标与星灯都亮起来再回来复命。", "Light both the wind beacon and the star lamp, then report back.");
                    message = L10n.T("两端航标的修复记录已交付，航路名册添上了两道标记。可以到码头找浮舟接下钟庭之争。",
                        "Both beacon repairs are recorded in the route roster. Fuzhou at the dock can now offer The Bell Court Standoff."); break;
                case SkyIslandStoryAction.AcceptBellCourtQuest:
                    flag = SkyIslandStoryFlag.BellCourtQuestAccepted;
                    if (!source.Has(SkyIslandStoryFlag.BeaconQuestDelivered))
                        required = L10n.T("先向苇白交付两端航标任务；她不在时，用风铃集委托板的「航路任务」。", "Turn in the beacon quest to Weibai first, or use Route quests at the Windchime Market board when she is away.");
                    message = L10n.T("浮舟：灯都亮了，钟守还是不肯让钟响。去归航钟庭，说服他，或者击停守钟装置，再回来告诉我。",
                        "Fuzhou: The lamps burn, yet the Bell Keeper still won't let the bell ring. Go to the Bell Court, talk him down or stop his bell engine, then report back to me."); break;
                case SkyIslandStoryAction.DeliverBellCourtQuest:
                    flag = SkyIslandStoryFlag.BellCourtQuestDelivered;
                    if (!source.Has(SkyIslandStoryFlag.BellCourtQuestAccepted))
                        required = L10n.T("先在码头接下浮舟的委托。", "Take Fuzhou's commission at the dock first.");
                    else if (!source.BellKeeperResolved)
                        required = L10n.T("说服钟守或击停守钟装置之后，再回码头复命。", "Talk the Bell Keeper down or stop his bell engine, then report back at the dock.");
                    message = source.Has(SkyIslandStoryFlag.Ending)
                        ? L10n.T("浮舟解开缆绳：『钟声听见了。下一艘归航船，可以靠岸了。』",
                            "Fuzhou unties the mooring line: 'I heard the bell. The next ship home can dock now.'")
                        : L10n.T("浮舟把船头掉向钟庭：『那就等钟声。』",
                            "Fuzhou turns the bow toward the Bell Court: 'Then we wait for the bell.'"); break;
                case SkyIslandStoryAction.AcceptHomecomingQuest:
                    flag = SkyIslandStoryFlag.HomecomingQuestAccepted;
                    if (!source.BothBeacons || !source.BellKeeperResolved)
                        required = L10n.T("先恢复两端航标，并解决钟守的阻拦。", "Restore both beacons and settle the Bell Keeper's objection first.");
                    message = L10n.T("钟守：去敲响归航钟。这一次，是为归来的人。",
                        "The Bell Keeper: Ring the Homecoming Bell. This time, for the ones coming home."); break;
                case SkyIslandStoryAction.DeliverHomecomingQuest:
                    flag = SkyIslandStoryFlag.HomecomingQuestDelivered;
                    if (!source.Has(SkyIslandStoryFlag.HomecomingQuestAccepted))
                        required = L10n.T("先在归航钟庭接下钟守的托付。", "Take the Bell Keeper's charge at the Bell Court first.");
                    else if (!source.Has(SkyIslandStoryFlag.Ending))
                        required = L10n.T("敲响归航钟之后再回来复命。", "Ring the Homecoming Bell, then report back.");
                    message = L10n.T("钟守望着云海：『听见了。他们都听见了。』",
                        "The Bell Keeper looks out over the cloud sea: 'They heard it. All of them.'"); break;
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
                        required = L10n.T("先在蛙鸣池找到晴禾的种植记录。",
                            "Find Qinghe's planting record at Frogsong Pool first.");
                    message = L10n.T("种植记录已交还，菜畦挂起了新的风车。归航菜可以在菜畦领取。",
                        "The planting record is returned and a new pinwheel turns over the garden. A homecoming meal is available there."); break;
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
                    // 穿着镜中客的镜纹甲（运行时字段，不进存档）：折翎认得那身纹路，不带旧信与航路图也肯谈。
                    else if (!source.wearsMirrorArmor && !source.Has(SkyIslandStoryFlag.OldLetter | SkyIslandStoryFlag.RouteChart))
                        required = L10n.T("折翎：拿到倒挂邮亭的旧信和听雨洞的航路图，我们再谈。你也可以离开，或明确挑战我。",
                            "Zheling: Bring the old letter from the Upturned Post Hut and the route chart from the Rainlisten Grotto, and then we talk. You may also walk away, or challenge me outright.");
                    message = L10n.T("折翎放下武器：『我守住了路，却把回家的人也挡在外面。让我把它修好。』",
                        "Zheling lowers his weapon: 'I held the road, and shut out the people coming home along with it. Let me put that right.'"); break;
                case SkyIslandStoryAction.ZhelingDefeated:
                    flag = SkyIslandStoryFlag.ZhelingDefeated;
                    if (source.ZhelingResolved)
                        required = L10n.T("折翎的结果已经记下。", "Zheling's outcome is already on record.");
                    message = CombatOutcome(SkyIslandStoryFlag.ZhelingDefeated); break;
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
                        required = L10n.T("钟守不信航路安全：带齐旧信、航路图、观星镜并了结折翎（和解或战胜都算），或者解决栈道上那阵风。",
                            "The Bell Keeper doubts the lanes are safe. Bring the old letter, the route chart and a repaired telescope and settle Zheling (reconciling or defeating both count) — or deal with the wind on the boardwalk.");
                    message = L10n.T("钟守：『这一次，钟声不是催他们出航，是告诉他们有人等着归来。』守钟装置停了。",
                        "The Bell Keeper: 'This time the bell is not sending them out. It is telling them someone is waiting for them to come home.' The bell engine falls still."); break;
                case SkyIslandStoryAction.BellKeeperDefeated:
                    flag = SkyIslandStoryFlag.BellKeeperDefeated;
                    if (!source.BothBeacons)
                        required = L10n.T("先恢复两端航标，再面对钟守。",
                            "Restore both beacons before facing the Bell Keeper.");
                    else if (source.BellKeeperResolved)
                        required = L10n.T("钟守的结果已经记下。", "The Bell Keeper's outcome is already on record.");
                    message = CombatOutcome(SkyIslandStoryFlag.BellKeeperDefeated); break;
                case SkyIslandStoryAction.StormSlain:
                    flag = SkyIslandStoryFlag.StormSlain;
                    if (!source.BothBeacons)
                        required = L10n.T("两端航标都亮起来，它才会循着光过来。",
                            "It only comes for the light once both beacons burn.");
                    message = CombatOutcome(SkyIslandStoryFlag.StormSlain); break;
                case SkyIslandStoryAction.RingHomecomingBell:
                    flag = SkyIslandStoryFlag.Ending;
                    if (!source.BothBeacons || !source.BellKeeperResolved)
                        required = L10n.T("先恢复两端航标，并解决钟守的阻拦。",
                            "Restore both beacons and settle the Bell Keeper's objection first.");
                    message = L10n.T("归航钟响了。风铃集的灯沿云海依次亮起，浮舟把空船系在码头，留给下一位旅人。晴岚群岛仍欢迎你回来。",
                        "The Homecoming Bell rings. The lights of Windchime Market come up one by one along the cloud sea, and Fuzhou ties the empty boat at the dock for the next traveller. Qinglan will always welcome you back."); break;
                default: message = L10n.T("未知的群岛操作。", "Unknown archipelago action."); return false;
            }
            return true;
        }

        /// <summary>
        /// 在战斗里了结的三件事（战胜折翎、战胜守钟装置、击败噬风）的剧情回话，文案唯一来源；其它旗标返回 null。
        ///
        /// 这三个结果不是在面板里点出来的：会话在清场或噬风倒下时直接 TryApply，回话没有面板可写，以前被整句丢掉，
        /// 玩家只看到一句「航路已清理 · 折翎」。「航路交给你」的旧腰牌、钟守松口的那句，以及「噬风散了、钟守少了一个理由」
        /// ——噬风这条说服路线在场上唯一的提示——全都没人看得到。现在 TryApply 的对应分支取这里，
        /// <c>SkyIslandWorldStory.Tick</c> 在这些旗标新增时把它读成字幕。
        /// </summary>
        internal static string CombatOutcome(SkyIslandStoryFlag flag)
        {
            switch (flag)
            {
                case SkyIslandStoryFlag.ZhelingDefeated:
                    return L10n.T("折翎停下战斗，将旧腰牌留在路旁：『航路交给你。』镜水寺的路已开放。",
                        "Zheling breaks off the fight and leaves his old badge by the road: 'The route is yours now.' The Mirrorwater Temple road is open.");
                case SkyIslandStoryFlag.BellKeeperDefeated:
                    return L10n.T("失控的守钟装置停下。钟守望向亮着的航标：『那就让钟声，为归来的人响一次。』",
                        "The runaway bell engine stops. The Bell Keeper looks out at the lit beacons: 'Then let the bell ring once, for the ones coming home.'");
                case SkyIslandStoryFlag.StormSlain:
                    return L10n.T("噬风散了。折翎说的『封路』和钟守说的『不安全』，从今天起都少了一个理由。",
                        "The Windeater is gone. Zheling's 'close the lanes' and the Bell Keeper's 'it is not safe' each lost a reason today.");
                default: return null;
            }
        }

        /// <summary>需要在场上读出回话的战斗结果旗标（<see cref="CombatOutcome"/> 有文案的那三个），按这个顺序读。</summary>
        internal static readonly SkyIslandStoryFlag[] CombatOutcomeFlags =
        {
            SkyIslandStoryFlag.ZhelingDefeated, SkyIslandStoryFlag.BellKeeperDefeated, SkyIslandStoryFlag.StormSlain
        };

        /// <summary>
        /// 失败提示的对外文案：中文界面是「前缀 + 异常原文」，英文界面只给前缀并指向日志。
        ///
        /// 异常原文按「维护语言中文」写给维护者（部署损坏、官方契约变更、加载超时才会出现），
        /// 旧写法把它直接拼进提示条，英文玩家看到的是一句读不懂的中文——与 CR-2026-09-09-011 同一类缺口，
        /// 而 F3 的英文完整性用例只扫正常文案、扫不到这条路径。调用方负责把原文写进日志。
        /// </summary>
        internal static string WithDetail(string prefix, string detail)
        {
            if (L10n.IsChinese) return prefix + detail;
            return prefix + " (details in Player.log)";
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
            string questStep = SkyIslandOfficialQuestTable.NextContactObjective(data);
            if (questStep != null) return questStep;
            if (data.Has(SkyIslandStoryFlag.Ending))
                // 布局 v2：结局时两端航标必然已亮，四处出口全开（码头、钟庭、两处航标广场）。
                return L10n.T("归航钟已响 · 自由重访、补齐支线 · 码头、钟庭或航标广场返航",
                    "The bell has rung · revisit freely and finish the side paths · extract at the dock, Bell Court or a beacon plaza") +
                    (data.StormResolved
                        ? L10n.T(" · 栈道可引风：噬风·回响（每趟一次）", " · call the Windeater's echo on the boardwalk (once per raid)")
                        : L10n.T(" · 「噬风」仍在鸣风栈道", " · the Windeater is still on Windsong Boardwalk"));
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
