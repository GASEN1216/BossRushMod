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
            { message = L10n.T("群岛记录还没读出来。", "The archipelago record hasn't loaded yet."); return false; }
            SkyIslandStoryFlag flag;
            string required;
            if (!Describe(source, action, out flag, out required, out message)) return false;
            if (source.Has(flag))
            { message = L10n.T("这一步已经做完了。", "That step is already done."); return false; }
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
                blocker = L10n.T("栈道上那阵风散了。等归航钟响过，带上噬风之核、烧一块晴岚风晶，还能在这儿把它的回响引回来。",
                    "The wind on the boardwalk is gone. Once the Homecoming Bell has rung, bring the Windeater Core and burn a Qinglan Windcrystal here to call its echo back.");
                return false;
            }
            if (usedThisRaid) return false;
            if (!coreCarried)
            {
                blocker = L10n.T("引风要把噬风之核带在背包里（不会用掉）。核要是还在基地仓库，下趟上岛记得带上。",
                    "Calling the wind needs the Windeater Core in your pack (it isn't used up). If it's still in base storage, bring it next trip.");
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
                    message = L10n.T("Jeff：零号区掉下来一台航向仪，不是地面上的东西。守着它的家伙也不像本地拾荒者。帮我把航向仪带回来。",
                        "Jeff: A navigation instrument fell into Ground Zero, and it isn't from down here. Whatever guards it is no local scavenger either. Bring the instrument back to me."); break;
                case SkyIslandStoryAction.RecoverPreludeInstrument:
                    flag = SkyIslandStoryFlag.PreludeInstrumentRecovered;
                    if (!source.Has(SkyIslandStoryFlag.PreludeAccepted))
                        required = L10n.T("先回基地问问 Jeff 那台坠落的仪器。",
                            "Ask Jeff back at base about the fallen instrument first.");
                    message = L10n.T("航向仪还在走。记录缺了一半，翻来覆去只指着云上的晴岚群岛。把它带回去给 Jeff。",
                        "The instrument is still running. What is left of its log keeps pointing to Qinglan, up above the clouds. Take it back to Jeff."); break;
                case SkyIslandStoryAction.UnlockRoute:
                    flag = SkyIslandStoryFlag.RouteUnlocked;
                    if (!source.Has(SkyIslandStoryFlag.PreludeInstrumentRecovered))
                        required = L10n.T("先去零号区把那台航向仪带回来。",
                            "Bring the instrument back from Ground Zero first.");
                    message = L10n.T("Jeff 把坐标读完，让船工在航路表上添了一条晴岚。想上去看，就去船边。",
                        "Jeff finishes reading the coordinates and has the boat crew add Qinglan to the route table. If you want to see it, head down to the boat."); break;
                case SkyIslandStoryAction.AcceptBeaconQuest:
                    flag = SkyIslandStoryFlag.BeaconQuestAccepted;
                    if (!source.SkyIslandRouteUnlocked)
                        required = L10n.T("先把坐标交给 Jeff，晴岚的航线才开得了。", "Hand the coordinates to Jeff first; that opens the Qinglan route.");
                    message = L10n.T("活接下了：西边悬根林的风标、东边残星工坊的星灯，两盏都修好再回来找苇白。地图上圈出来了。",
                        "Job taken: fix the wind beacon out west in Hanging Root Wood and the star lamp out east at Fallen Star Workshop, then come back to Weibai. They're circled on the map."); break;
                case SkyIslandStoryAction.DeliverBeaconQuest:
                    flag = SkyIslandStoryFlag.BeaconQuestDelivered;
                    if (!source.Has(SkyIslandStoryFlag.BeaconQuestAccepted))
                        required = L10n.T("先去找苇白，把两端航标的活接下来。她要是不在，风铃集的委托板上也有。", "Find Weibai first and take on the beacon work. If she's away, the Windchime Market board has it too.");
                    else if (!source.BothBeacons)
                        required = L10n.T("风标和星灯都修好了再回来交差。", "Fix both the wind beacon and the star lamp, then come back.");
                    message = L10n.T("交差了。下一件事去码头找浮舟，他那儿有「钟庭之争」。",
                        "Handed in. Next, go to the dock and see Fuzhou about The Bell Court Standoff."); break;
                case SkyIslandStoryAction.AcceptBellCourtQuest:
                    flag = SkyIslandStoryFlag.BellCourtQuestAccepted;
                    if (!source.Has(SkyIslandStoryFlag.BeaconQuestDelivered))
                        required = L10n.T("先回去跟苇白交差。她不在，就到风铃集的委托板上交。", "Report back to Weibai first. If she's away, hand it in at the Windchime Market board.");
                    message = L10n.T("浮舟：灯亮了，钟守还是不让敲钟。过鸣风栈道去钟庭，跟他谈，谈不拢就把他那台守钟装置打停。办完回码头说一声。",
                        "Fuzhou: The lamps are lit, but the Bell Keeper still won't let anyone ring the bell. Cross Windsong Boardwalk to the Bell Court and talk to him. If that fails, stop his bell engine. Tell me at the dock when it's done."); break;
                case SkyIslandStoryAction.DeliverBellCourtQuest:
                    flag = SkyIslandStoryFlag.BellCourtQuestDelivered;
                    if (!source.Has(SkyIslandStoryFlag.BellCourtQuestAccepted))
                        required = L10n.T("先去码头，听浮舟怎么说。", "Go to the dock and hear what Fuzhou has to say first.");
                    else if (!source.BellKeeperResolved)
                        required = L10n.T("先去钟庭把钟守那关过了（谈妥，或者打停守钟装置），再回码头交差。", "Get past the Bell Keeper at the Bell Court first (talk him round or stop his bell engine), then come back to the dock.");
                    message = source.Has(SkyIslandStoryFlag.Ending)
                        ? L10n.T("浮舟：『钟声我听见了。这下回家的船能靠岸了。』",
                            "Fuzhou: 'I heard the bell. Ships coming home can dock now.'")
                        : L10n.T("浮舟：『行，那就等你把钟敲响。』",
                            "Fuzhou: 'All right. Now go ring that bell.'"); break;
                case SkyIslandStoryAction.AcceptHomecomingQuest:
                    flag = SkyIslandStoryFlag.HomecomingQuestAccepted;
                    if (!source.BothBeacons || !source.BellKeeperResolved)
                        required = L10n.T("先修好两盏灯，再过钟守那一关。", "Fix both lights and get past the Bell Keeper first.");
                    message = L10n.T("钟守在木牌上写：去敲钟吧，钟就挂在钟庭的钟架上。敲完回来跟我说一声。",
                        "The Bell Keeper writes on his slate: Go ring it. It hangs on the frame in the Bell Court. Come tell me when it's done."); break;
                case SkyIslandStoryAction.DeliverHomecomingQuest:
                    flag = SkyIslandStoryFlag.HomecomingQuestDelivered;
                    if (!source.Has(SkyIslandStoryFlag.HomecomingQuestAccepted))
                        required = L10n.T("先到钟庭，看看钟守留的字。", "Go to the Bell Court and read what the Bell Keeper left for you first.");
                    else if (!source.Has(SkyIslandStoryFlag.Ending))
                        required = L10n.T("先去把钟敲了，再回来跟我说。", "Ring the bell first, then come tell me.");
                    message = source.Has(SkyIslandStoryFlag.BellCourtQuestDelivered)
                        ? L10n.T("钟守在木牌上添了一行：『听见了。他们都听见了。』",
                            "The Bell Keeper adds a line to his slate: 'They heard it. All of them.'")
                        : L10n.T("钟守在木牌上添了一行：『听见了。他们都听见了。』最后一件事：回码头跟浮舟说一声。",
                            "The Bell Keeper adds a line to his slate: 'They heard it. All of them.' One last thing: tell Fuzhou at the dock."); break;
                case SkyIslandStoryAction.RepairWindBeacon:
                    flag = SkyIslandStoryFlag.WindBeacon;
                    if (!WindBeaconGuardsCleared(source))
                        required = GuardsBlocker(source, WindBeaconGuards, LampName(true));
                    message = source.Has(SkyIslandStoryFlag.StarLamp)
                        ? L10n.T("风标修好了，两盏灯都亮了。回风铃集找苇白交差。顺手把回程绳桥 K1 系上，下次回村近一点。",
                            "The wind beacon is fixed and both lights are up. Go back to Weibai at Windchime Market. Lash the K1 rope bridge on the way; it's a shortcut home.")
                        : L10n.T("风标修好了。还差东边残星工坊的星灯。顺手把回程绳桥 K1 系上，回村近一点。",
                            "The wind beacon is fixed. The star lamp out east at Fallen Star Workshop is still left. Lash the K1 rope bridge while you're here; it's a shortcut home."); break;
                case SkyIslandStoryAction.RepairStarLamp:
                    flag = SkyIslandStoryFlag.StarLamp;
                    if (!StarLampGuardsCleared(source))
                        required = GuardsBlocker(source, StarLampGuards, LampName(false));
                    message = source.Has(SkyIslandStoryFlag.WindBeacon)
                        ? L10n.T("星灯修好了，两盏灯都亮了。回风铃集找苇白交差。顺手把检修廊 K2 打开，下次回村近一点。",
                            "The star lamp is fixed and both lights are up. Go back to Weibai at Windchime Market. Open the K2 maintenance walk on the way; it's a shortcut home.")
                        : L10n.T("星灯修好了。还差西边悬根林的风标。顺手把检修廊 K2 打开，回村近一点。",
                            "The star lamp is fixed. The wind beacon out west in Hanging Root Wood is still left. Open the K2 maintenance walk while you're here; it's a shortcut home."); break;
                case SkyIslandStoryAction.FindPlantingRecord:
                    flag = SkyIslandStoryFlag.PlantingRecord;
                    message = L10n.T("找到晴禾的种植记录了。拿回风铃集交给晴禾，或者钉在委托板上。",
                        "Found Qinghe's planting record. Take it back to Qinghe at Windchime Market, or pin it on the board there."); break;
                case SkyIslandStoryAction.FindOldLetter:
                    flag = SkyIslandStoryFlag.OldLetter;
                    message = L10n.T("找到一封没寄出的旧信：『别让岛上的灯灭了。』折翎要的就是这个，再配上听雨洞的航路图，就能去镜水寺跟他谈。",
                        "Found an unsent letter: 'Don't let the island's lights go out.' This is what Zheling wants. Bring it with the chart from Rainlisten Grotto and he'll talk at Mirrorwater Temple."); break;
                case SkyIslandStoryAction.FindRouteChart:
                    flag = SkyIslandStoryFlag.RouteChart;
                    message = L10n.T("找到旧航路图了，上面画着一条能绕开风灾的路。折翎要的就是这个，再配上倒挂邮亭的旧信，就能去镜水寺跟他谈。",
                        "Found the old route chart. It shows a way around the storm. This is what Zheling wants. Bring it with the letter from the Upturned Post Hut and he'll talk at Mirrorwater Temple."); break;
                case SkyIslandStoryAction.RepairTelescope:
                    flag = SkyIslandStoryFlag.Telescope;
                    message = L10n.T("观星镜修好了。钟守要的证据又多了一件。",
                        "The telescope is fixed. That's one more piece of proof for the Bell Keeper."); break;
                case SkyIslandStoryAction.DeliverPlantingRecord:
                    flag = SkyIslandStoryFlag.PlantingDelivered;
                    if (!source.Has(SkyIslandStoryFlag.PlantingRecord))
                        required = L10n.T("先去蛙鸣池把晴禾的种植记录找回来。",
                            "Go find Qinghe's planting record at Frogsong Pool first.");
                    message = L10n.T("种植记录交回去了。以后每趟都能在青穗梯田的菜畦吃一顿归航菜。",
                        "The planting record is back. From now on you can get a homecoming meal at the Green Terraces garden every trip."); break;
                case SkyIslandStoryAction.OpenShortcutK1:
                    flag = SkyIslandStoryFlag.ShortcutK1;
                    if (!source.Has(SkyIslandStoryFlag.WindBeacon))
                        required = L10n.T("先把风标修好，才能系这座绳桥。",
                            "Fix the wind beacon first, then you can lash this rope bridge.");
                    message = L10n.T("绳桥系好了，从这儿回风铃集近多了。",
                        "The rope bridge is lashed. It's a much shorter way back to Windchime Market."); break;
                case SkyIslandStoryAction.OpenShortcutK2:
                    flag = SkyIslandStoryFlag.ShortcutK2;
                    if (!source.Has(SkyIslandStoryFlag.StarLamp))
                        required = L10n.T("先把星灯修好，才能打开检修廊。",
                            "Fix the star lamp first, then you can open the maintenance walk.");
                    message = L10n.T("检修廊打开了，从这儿回风铃集近多了。",
                        "The maintenance walk is open. It's a much shorter way back to Windchime Market."); break;
                case SkyIslandStoryAction.OpenShortcutK3:
                    flag = SkyIslandStoryFlag.ShortcutK3;
                    if (!source.BothBeacons)
                        required = L10n.T("先把风标和星灯都修好。", "Fix both the wind beacon and the star lamp first.");
                    message = L10n.T("中间那座旧桥放下来了，从栈道能直接走回风铃集。",
                        "The old centre bridge is down. You can walk straight from the boardwalk back to Windchime Market."); break;
                case SkyIslandStoryAction.ReconcileZheling:
                    flag = SkyIslandStoryFlag.ZhelingReconciled;
                    if (source.ZhelingResolved)
                        required = L10n.T("折翎这边已经有结果了，镜水寺的路一直开着。",
                            "Zheling has already made his choice, and the Mirrorwater Temple road stays open.");
                    // 穿着镜中客的镜纹甲（运行时字段，不进存档）：折翎认得那身纹路，不带旧信与航路图也肯谈。
                    else if (!source.wearsMirrorArmor && !source.Has(SkyIslandStoryFlag.OldLetter | SkyIslandStoryFlag.RouteChart))
                        required = L10n.T("折翎：拿倒挂邮亭的旧信和听雨洞的航路图来，我们再谈。不然就走，或者跟我打。",
                            "Zheling: Bring me the old letter from the Upturned Post Hut and the route chart from Rainlisten Grotto, then we talk. Or walk away. Or fight me.");
                    message = L10n.T("折翎把刀放下了：『是我把回家的人也挡在外面了。路我不拦了。』镜水寺的近路开了。",
                        "Zheling puts his blade down: 'I kept the people coming home out too. I won't block the road anymore.' The Mirrorwater Temple shortcut is open."); break;
                case SkyIslandStoryAction.ZhelingDefeated:
                    flag = SkyIslandStoryFlag.ZhelingDefeated;
                    if (source.ZhelingResolved)
                        required = L10n.T("折翎这边已经有结果了。", "That's already settled with Zheling.");
                    message = CombatOutcome(SkyIslandStoryFlag.ZhelingDefeated); break;
                case SkyIslandStoryAction.ReconcileBellKeeper:
                    flag = SkyIslandStoryFlag.BellKeeperReconciled;
                    if (!source.BothBeacons)
                        required = L10n.T("先把风标和星灯都修好，钟守才肯谈。",
                            "Fix both the wind beacon and the star lamp first; until then the Bell Keeper won't talk.");
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
                        required = L10n.T("钟守不信航路安全。想讲和，最省事的是去栈道的双航标门，把「噬风」引出来打掉；要不就带齐旧信、航路图，修好观星镜，再把折翎那边了结（和解或战胜都算）。不想等，直接打停守钟装置也行。",
                            "The Bell Keeper doubts the lanes are safe. To make peace, the easy way is to call out the Windeater at the boardwalk's twin-beacon gate and beat it. Or bring the old letter and the route chart, fix the telescope, and settle Zheling (talking or winning both count). Or skip all that and stop his bell engine.");
                    message = L10n.T("钟守在木牌上写：『行，这回敲钟是告诉外头的人，家里有人等。』守钟装置停了。接下来找钟守接「归航钟」，去敲钟。",
                        "The Bell Keeper writes: 'Fine. This time the bell tells them someone's waiting at home.' The bell engine stops. Next, take The Homecoming Bell from him and go ring it."); break;
                case SkyIslandStoryAction.BellKeeperDefeated:
                    flag = SkyIslandStoryFlag.BellKeeperDefeated;
                    if (!source.BothBeacons)
                        required = L10n.T("先把两盏灯修好，再来找钟守。",
                            "Fix both lights before you take on the Bell Keeper.");
                    else if (source.BellKeeperResolved)
                        required = L10n.T("钟守这边已经有结果了。", "That's already settled with the Bell Keeper.");
                    message = CombatOutcome(SkyIslandStoryFlag.BellKeeperDefeated); break;
                case SkyIslandStoryAction.StormSlain:
                    flag = SkyIslandStoryFlag.StormSlain;
                    if (!source.BothBeacons)
                        required = L10n.T("两盏灯都亮了，它才会顺着光找过来。",
                            "It only follows the light once both beacons are lit.");
                    message = CombatOutcome(SkyIslandStoryFlag.StormSlain); break;
                case SkyIslandStoryAction.RingHomecomingBell:
                    flag = SkyIslandStoryFlag.Ending;
                    if (!source.BothBeacons || !source.BellKeeperResolved)
                        required = L10n.T("先修好两盏灯，再过钟守那一关。", "Fix both lights and get past the Bell Keeper first.");
                    message = L10n.T("归航钟响了，风铃集的灯一盏盏亮起来。", "The Homecoming Bell rings and the lights of Windchime Market come on one by one.") +
                        (source.Has(SkyIslandStoryFlag.HomecomingQuestAccepted) && !source.Has(SkyIslandStoryFlag.HomecomingQuestDelivered)
                            ? L10n.T("先跟钟守说一声。", " Tell the Bell Keeper first.")
                            : source.Has(SkyIslandStoryFlag.BellCourtQuestAccepted) && !source.Has(SkyIslandStoryFlag.BellCourtQuestDelivered)
                                ? L10n.T("回码头找浮舟交差。", " Then report to Fuzhou at the dock.")
                                : string.Empty); break;
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
                    return L10n.T("折翎认输了，把旧腰牌放在路边，上面刻着：『航路交给你。』镜水寺的路已开放。他这趟先回去养伤，下次来还能找他。",
                        "Zheling gives up and sets his old badge by the road. It reads: 'The route is yours now.' The Mirrorwater Temple road is open. He's off to heal for this trip; you can find him again next time.");
                case SkyIslandStoryFlag.BellKeeperDefeated:
                    return L10n.T("守钟装置停了。钟守写：『那就让钟声，为归来的人响一次。』去找钟守接「归航钟」，然后敲钟。",
                        "The bell engine stops. The Bell Keeper writes: 'Then let the bell ring once, for the ones coming home.' Take The Homecoming Bell from him, then ring it.");
                case SkyIslandStoryFlag.StormSlain:
                    // 噬风可以在钟守那关之后才打（结局后也还挂着）：后半句按「他要是还拦着」写，两种时机都不会说错。
                    return L10n.T("噬风散了。折翎说的『封路』和钟守说的『不安全』，从今天起都少了一个理由。钟守要是还拦着不让敲钟，现在去钟庭跟他谈就行。",
                        "The Windeater is gone. Zheling's 'close the lanes' and the Bell Keeper's 'it is not safe' each lost a reason today. If the Bell Keeper is still blocking the bell, go to the Bell Court and talk to him now.");
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

        /// <summary>修风标的前置：风标守卫与林间道路两组都清过。目标卡与 RepairWindBeacon 的拒绝共用这一份判据。</summary>
        internal static bool WindBeaconGuardsCleared(SkyIslandStoryData source)
        {
            return source != null && source.EncounterCleared("D") && source.EncounterCleared("D_02");
        }

        /// <summary>修星灯的前置：星灯守卫与检修通道两组都清过。目标卡与 RepairStarLamp 的拒绝共用这一份判据。</summary>
        internal static bool StarLampGuardsCleared(SkyIslandStoryData source)
        {
            return source != null && source.EncounterCleared("G") && source.EncounterCleared("G_02");
        }

        /// <summary>
        /// 航标守卫：每座航标台要清两伙人，一伙守在灯旁、一伙在附近另一处（地图上圈出来的就是还没清的那几处）。
        /// 以前目标卡只写「（先清守卫）」，面板拒绝时说「林间道路」「检修通道」——岛上根本没有叫这个名字的地方，
        /// 玩家清完灯旁那一伙、按装置没反应，就不知道还差哪儿（2026-09-29 引导复核）。
        /// 标记与 <see cref="WindBeaconGuardsCleared"/> / <see cref="StarLampGuardsCleared"/> 同一份遭遇 id。
        /// </summary>
        internal static readonly string[] WindBeaconGuards = { "D", "D_02" };
        internal static readonly string[] StarLampGuards = { "G", "G_02" };

        /// <summary>遭遇 id → 刷怪锚点（地图圈与罗盘指向用）。与 World.json 的 marker 一致。</summary>
        internal static string GuardMarker(string encounterId)
        {
            switch (encounterId)
            {
                case "D": return "EnemySpawn_D";
                case "D_02": return "Search_D_02";
                case "G": return "EnemySpawn_G";
                case "G_02": return "Search_G_02";
                default: return null;
            }
        }

        /// <summary>这伙守卫在哪儿：玩家看得见的地标说法。</summary>
        private static string GuardPlace(string encounterId)
        {
            switch (encounterId)
            {
                case "D": return L10n.T("风标跟前", "right by the wind beacon");
                case "D_02": return L10n.T("北边眠苔的药臼那儿", "up north by Miantai's mortar");
                case "G": return L10n.T("星灯跟前", "right by the star lamp");
                default: return L10n.T("北边瞭台检修日志那儿", "up north by the overlook log");
            }
        }

        private static string LampName(bool wind)
        {
            return wind ? L10n.T("风标", "wind beacon") : L10n.T("星灯", "star lamp");
        }

        private static int GuardsCleared(SkyIslandStoryData data, string[] guards)
        {
            int count = 0;
            for (int i = 0; i < guards.Length; i++) if (data != null && data.EncounterCleared(guards[i])) count++;
            return count;
        }

        /// <summary>还没清的第一伙守卫在哪儿；都清了返回 null。</summary>
        private static string NextGuardPlace(SkyIslandStoryData data, string[] guards)
        {
            for (int i = 0; i < guards.Length; i++) if (!data.EncounterCleared(guards[i])) return GuardPlace(guards[i]);
            return null;
        }

        /// <summary>修灯的拒绝话：说清还差几伙、先去哪儿。每段都是成对的完整字面量（本地化守卫只认这种形状）。</summary>
        private static string GuardsBlocker(SkyIslandStoryData data, string[] guards, string what)
        {
            int left = guards.Length - GuardsCleared(data, guards);
            return L10n.T("还修不了", "Can't fix the ") + what + L10n.T("：附近还有 ", " yet: ") + left +
                L10n.T(" 伙人没清，先去", " group(s) nearby still to clear. Start ") + NextGuardPlace(data, guards) +
                L10n.T("把他们打掉。地图上圈出来了。", ". They're circled on the map.");
        }

        /// <summary>目标卡上一盏灯那一行：修好了 / 可以修了 / 还差几伙守卫。</summary>
        private static string BeaconLine(SkyIslandStoryData data, SkyIslandStoryFlag flag, string[] guards, string name)
        {
            if (data.Has(flag)) return name + L10n.T("：修好了", ": fixed");
            int cleared = GuardsCleared(data, guards);
            if (cleared >= guards.Length) return name + L10n.T("：守卫清完了，过去修", ": guards cleared, go fix it");
            return name + L10n.T("：先清附近两伙守卫（", ": clear the two guard groups first (") + cleared + "/" + guards.Length + ")";
        }

        /// <summary>
        /// 一伙守卫第一次被清掉时的字幕：这一伙算进了哪盏灯、还差哪儿。不是航标守卫、或那盏灯已经修好时返回 null。
        /// 会话在清场事实第一次落盘后调一次（<c>SkyIslandSession.OnEncounterCleared</c>）。
        /// </summary>
        internal static string GuardProgressCaption(SkyIslandStoryData data, string encounterId)
        {
            if (data == null) return null;
            bool wind = Array.IndexOf(WindBeaconGuards, encounterId) >= 0;
            bool star = Array.IndexOf(StarLampGuards, encounterId) >= 0;
            if (!wind && !star) return null;
            if (data.Has(wind ? SkyIslandStoryFlag.WindBeacon : SkyIslandStoryFlag.StarLamp)) return null;
            string[] guards = wind ? WindBeaconGuards : StarLampGuards;
            string next = NextGuardPlace(data, guards);
            if (next == null)
                return wind
                    ? L10n.T("风标附近的守卫清完了，可以过去修了", "The guards around the wind beacon are gone. Go fix it.")
                    : L10n.T("星灯附近的守卫清完了，可以过去修了", "The guards around the star lamp are gone. Go fix it.");
            return (wind
                    ? L10n.T("风标的守卫清了一伙，还差一伙在", "One guard group at the wind beacon is down. The other is ")
                    : L10n.T("星灯的守卫清了一伙，还差一伙在", "One guard group at the star lamp is down. The other is ")) + next;
        }

        /// <summary>
        /// 右上角目标卡。文案按「 · 」分行（<c>SkyIslandHud.ObjectiveLines</c>）：第一句是现在要干什么，后面每句一行补细节。
        /// 不写句号：居民台词会原样接上这一句（苇白），句号会把它拆成好几屏官方对话。
        /// </summary>
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
            {
                int lit = (data.Has(SkyIslandStoryFlag.WindBeacon) ? 1 : 0) + (data.Has(SkyIslandStoryFlag.StarLamp) ? 1 : 0);
                return L10n.T("恢复两端航标（", "Restore both beacons (") + lit + "/2)" + " · " +
                    BeaconLine(data, SkyIslandStoryFlag.WindBeacon, WindBeaconGuards, L10n.T("西边悬根林风标", "West: Hanging Root Wood beacon")) + " · " +
                    BeaconLine(data, SkyIslandStoryFlag.StarLamp, StarLampGuards, L10n.T("东边残星工坊星灯", "East: Fallen Star Workshop lamp"));
            }
            if (!data.BellKeeperResolved)
            {
                // 每条路都写到「在哪儿、点哪一项」：以前只说「打掉噬风」「打停装置」，玩家到了钟庭不知道从哪儿开打，
                // 噬风更是要站到栈道的双航标门跟前才引得出来（2026-09-30 引导复核）。选项名与面板按钮一字不差。
                // 卡片最多 7 行（含「目标更新」眉题），英文更长：只写最直接的两条路。凑物证那条长路写在钟守的拒绝话与任务说明里；
                // 「打了就不能讲和」由开打前的确认页说。
                string blocker;
                string peace = CanApply(data, SkyIslandStoryAction.ReconcileBellKeeper, out blocker)
                    ? L10n.T("想讲和：证据够了，找钟守选「与钟守和解」", "To make peace: you have the proof, pick reconcile")
                    // 走到这里说明噬风还在（打掉它就直接够格讲和，见 ReconcileBellKeeper 的判据）。
                    : L10n.T("想讲和：在双航标门选「直面云海里的那阵风」打噬风", "For peace: beat the Windeater via 'Face the wind' at the twin-beacon gate");
                return L10n.T("过鸣风栈道去归航钟庭找钟守", "Cross Windsong Boardwalk to the Bell Keeper") + " · " +
                    L10n.T("想打：找钟守选「挑战守钟装置」", "To fight: pick 'Challenge the bell engine'") + " · " + peace;
            }
            return L10n.T("钟守已放行 · 去钟庭的钟架敲响归航钟",
                "The Bell Keeper stands aside · ring the Homecoming Bell on the Bell Court frame");
        }
    }
}
