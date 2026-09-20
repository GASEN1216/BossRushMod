using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 官方任务桥每次同步时采一次的运行时上下文。纯值：隔离回归直接构造，判据函数不碰 Unity。
    /// </summary>
    internal struct SkyIslandOfficialQuestContext
    {
        /// <summary>当前权威剧情事实；null = 故事未就绪。</summary>
        internal SkyIslandStoryData Data;
        internal bool StoryReady;
        internal bool CanWrite;
        /// <summary>岛上会话在跑（事实来自会话的故事门面）。</summary>
        internal bool OnIsland;
        /// <summary>只有序章用：人在基地、场景包已部署、没有别的模式在跑。</summary>
        internal bool InBaseHub;
        internal bool BundleDeployed;
        internal bool NoConflictingMode;
        internal int Slot;
    }

    /// <summary>官方 Task 的一条目标：判据与文案都只读剧情事实。</summary>
    internal sealed class SkyIslandOfficialQuestTaskDefinition
    {
        /// <summary>Quest 内唯一。</summary>
        internal int TaskId;
        internal Func<SkyIslandStoryData, bool> Done;
        /// <summary>官方任务日志里的目标行；未完成时与自绘面板的「还差什么」同一句。</summary>
        internal Func<SkyIslandStoryData, string> Description;
        /// <summary>可空：ExtraDescriptsions 的一行提示。</summary>
        internal Func<SkyIslandStoryData, string> ExtraHint;
    }

    internal delegate bool SkyIslandOfficialQuestCommit(out string message);

    /// <summary>
    /// 一条投影到官方 <c>Duckov.Quests</c> 的任务定义。Mod 分槽故事是权威：接取 / 交付各写一位旗标，
    /// 官方 active / history 只是从这两位重建出来的投影（读档时官方快照里没有我们的 ID）。
    /// </summary>
    internal sealed class SkyIslandOfficialQuestDefinition
    {
        internal int QuestId;
        /// <summary>官方 <c>QuestGiverID</c> 的整数值：Jeff = 1；岛上给予者用 5900–5949 保留区间。</summary>
        internal int GiverId;
        internal string ObjectName;
        internal string NameKey;
        internal string DescriptionKey;
        internal Func<string> Name;
        internal Func<string> Description;
        /// <summary>HUD 的接取 / 交付引导与任务表共用地点；进行中的探索目标仍由剧情规则描述。</summary>
        internal Func<string> Contact;
        /// <summary>
        /// 可选：官方任务详情页「所需物品」栏显示的交付物（0 = 不显示）。纯展示——
        /// 官方只把它画出来，收物品仍由本条任务自己的 Deliver 负责。
        /// </summary>
        internal int RequiredItemId;
        internal int RequiredItemCount;
        /// <summary>
        /// 可选：交付时发的金钱（0 = 无）。官方完成面板按 Reward_Money 照常显示，
        /// 但真正发放由桥在「未交付 → 已交付」那一拍做一次，读档重建投影不会再发第二次。
        /// </summary>
        internal int RewardMoney;
        internal SkyIslandStoryFlag AcceptedFlag;
        internal SkyIslandStoryFlag DeliveredFlag;
        internal SkyIslandStoryAction AcceptAction;
        internal SkyIslandStoryAction DeliverAction;
        internal SkyIslandOfficialQuestTaskDefinition[] Tasks;
        /// <summary>纯门：表里写，隔离回归穷举。</summary>
        internal Func<SkyIslandOfficialQuestContext, bool> Gate;
        /// <summary>可空：运行时才知道的门（序章的场景 / 包 / 模式）。</summary>
        internal Func<bool> RuntimeGate;
        /// <summary>可空：null 时桥走默认实现（<see cref="SkyIslandStoryService.TryApply"/> 对应动作）。</summary>
        internal SkyIslandOfficialQuestCommit Accept;
        internal SkyIslandOfficialQuestCommit Deliver;
    }

    /// <summary>
    /// 天空岛接进官方任务系统的唯一任务表（COMPAT / SCHEMA+）。
    ///
    /// - 序章 590001 由 <c>SkyIslandPreludeFlow</c> 登记（给予者官方 Jeff，交付要求回基地）。
    /// - 岛上三条 590011–590013 在这里定义，给予者是岛上居民（自定义 <c>QuestGiverID</c> 整数：官方 UI 不显示给予者名，
    ///   <c>Quest.Compare</c> 只做整数减法，<c>Quest.SaveData.questGiverID</c> 随整条记录从官方快照剥离，所以安全）。
    /// - 三条任务都只能在岛上接、在岛上交，官方日志里的目标行与自绘面板的「还差什么」取同一份 <see cref="SkyIslandStoryRules"/> 文案。
    /// - 整体回退开关：<see cref="Island"/> 返回空表即回到只有序章。
    ///
    /// 无 Unity / Duckov 依赖：<c>tests/fixtures/SkyIslandStory</c> 直接链接执行。
    /// </summary>
    internal static class SkyIslandOfficialQuestTable
    {
        internal const int PreludeQuestId = 590001;
        internal const int BeaconQuestId = 590011;
        internal const int BellCourtQuestId = 590012;
        internal const int HomecomingQuestId = 590013;

        /// <summary>(int)QuestGiverID.Jeff。</summary>
        internal const int JeffGiverId = 1;
        /// <summary>自定义给予者区间 5900–5949 归 BossRush；5904 晴禾 / 5905 折翎 / 5906 眠苔预留未用。</summary>
        internal const int WeibaiGiverId = 5901;
        internal const int FuzhouGiverId = 5902;
        internal const int BellKeeperGiverId = 5903;

        /// <summary>
        /// 岛上三条主线的交付奖金，按链条递进（序章是 5000，见 <c>SkyIslandPreludeFlow.DeliveryMoney</c>）。
        /// 口径参照岛上既有价码（渡口整备下限 60、满血苔药 480）与商店大件（新武器 20000、套装 30000）：
        /// 一条主线任务给得比一趟整备多得多，又不至于一条任务顶掉半件大装备。
        /// 发放与序章同一条：桥只在「未交付 → 已交付」那一拍发一次，老档回填不补发。
        /// </summary>
        internal const int BeaconQuestMoney = 3000;
        internal const int BellCourtQuestMoney = 5000;
        internal const int HomecomingQuestMoney = 8000;

        internal const string BeaconQuestNameKey = "BossRush_SkyIslandQuest_Beacons_Name";
        internal const string BeaconQuestDescriptionKey = "BossRush_SkyIslandQuest_Beacons_Description";
        internal const string BellCourtQuestNameKey = "BossRush_SkyIslandQuest_BellCourt_Name";
        internal const string BellCourtQuestDescriptionKey = "BossRush_SkyIslandQuest_BellCourt_Description";
        internal const string HomecomingQuestNameKey = "BossRush_SkyIslandQuest_Homecoming_Name";
        internal const string HomecomingQuestDescriptionKey = "BossRush_SkyIslandQuest_Homecoming_Description";

        private static readonly SkyIslandOfficialQuestDefinition[] island = BuildIsland();

        /// <summary>岛上三条任务，按链条顺序。</summary>
        internal static IList<SkyIslandOfficialQuestDefinition> Island { get { return island; } }

        /// <summary>居民 id → 给予者整数；不发任务的居民返回 0。</summary>
        internal static int GiverIdOfResident(string residentId)
        {
            switch (residentId)
            {
                case "sky_weibai": return WeibaiGiverId;
                case "sky_fuzhou": return FuzhouGiverId;
                case "sky_bellkeeper": return BellKeeperGiverId;
                default: return 0;
            }
        }

        /// <summary>给予者整数 → 居民缺席那一趟兜底的装置标记（委托板 / 渡口工台 / 钟庭装置）。</summary>
        internal static string FallbackMarkerOfGiver(int giverId)
        {
            switch (giverId)
            {
                case WeibaiGiverId: return "Search_B";
                case FuzhouGiverId: return "Search_A";
                case BellKeeperGiverId: return "Search_H";
                default: return null;
            }
        }

        /// <summary>给予者整数 → 兜底装置对应的居民 id（判「居民在不在岛上」用）。</summary>
        internal static string ResidentOfGiver(int giverId)
        {
            switch (giverId)
            {
                case WeibaiGiverId: return "sky_weibai";
                case FuzhouGiverId: return "sky_fuzhou";
                case BellKeeperGiverId: return "sky_bellkeeper";
                default: return null;
            }
        }

        internal static bool TryGet(int questId, out SkyIslandOfficialQuestDefinition definition)
        {
            for (int i = 0; i < island.Length; i++)
                if (island[i].QuestId == questId) { definition = island[i]; return true; }
            definition = null;
            return false;
        }

        /// <summary>此刻能不能出现在给予者的「可接取」页。与 <see cref="SkyIslandStoryRules.CanApply"/>(AcceptAction) 同源。</summary>
        internal static bool CanOffer(SkyIslandOfficialQuestDefinition definition, SkyIslandOfficialQuestContext context)
        {
            if (definition == null || !context.StoryReady || !context.CanWrite || context.Data == null) return false;
            if (context.Data.Has(definition.AcceptedFlag) || context.Data.Has(definition.DeliveredFlag)) return false;
            string blocker;
            if (!SkyIslandStoryRules.CanApply(context.Data, definition.AcceptAction, out blocker)) return false;
            if (definition.Gate != null && !definition.Gate(context)) return false;
            return definition.RuntimeGate == null || definition.RuntimeGate();
        }

        internal static bool TasksDone(SkyIslandOfficialQuestDefinition definition, SkyIslandStoryData data)
        {
            if (definition == null || data == null) return false;
            for (int i = 0; i < definition.Tasks.Length; i++)
                if (!definition.Tasks[i].Done(data)) return false;
            return true;
        }

        /// <summary>官方「完成任务」按钮此刻能不能真的交付：已接取、目标全完成、事实可写。</summary>
        internal static bool CanDeliver(SkyIslandOfficialQuestDefinition definition, SkyIslandOfficialQuestContext context)
        {
            if (definition == null || !context.StoryReady || !context.CanWrite || context.Data == null) return false;
            if (context.Data.Has(definition.DeliveredFlag) || !context.Data.Has(definition.AcceptedFlag)) return false;
            if (definition.Gate != null && !definition.Gate(context)) return false;
            return TasksDone(definition, context.Data);
        }

        /// <summary>只补任务接取和复命这两个旧 HUD 漏掉的步骤，不把支线、和解或战斗改成强制任务。</summary>
        internal static string NextContactObjective(SkyIslandStoryData data)
        {
            SkyIslandOfficialQuestDefinition quest = NextContactQuest(data);
            if (quest == null) return null;
            return quest.Contact() + L10n.T(" · 航路任务：", " · Route quests: ") +
                (data.Has(quest.AcceptedFlag) ? L10n.T("交付「", "complete ") : L10n.T("接取「", "accept ")) +
                quest.Name() + L10n.T("」", "");
        }

        /// <summary>HUD、地图与罗盘共用下一次接取/复命；探索阶段返回 null。</summary>
        internal static SkyIslandOfficialQuestDefinition NextContactQuest(SkyIslandStoryData data)
        {
            if (data == null || !data.SkyIslandRouteUnlocked) return null;
            for (int i = 0; i < island.Length; i++)
            {
                SkyIslandOfficialQuestDefinition quest = island[i];
                if (data.Has(quest.DeliveredFlag)) continue;
                bool accepted = data.Has(quest.AcceptedFlag);
                if (accepted && !TasksDone(quest, data)) return null;
                // 钟庭事件完成后先处理就在面前的归航钟，之后再提示回码头复命。
                if (quest.QuestId == BellCourtQuestId && accepted && !data.Has(SkyIslandStoryFlag.HomecomingQuestDelivered)) continue;
                return quest;
            }
            return null;
        }

        internal static void InjectLocalizations()
        {
            // 与任务标题一起由全局语言切换注入；不能只在居民生成时缓存一次中文。
            LocalizationHelper.InjectLocalization("BossRush_SkyIsland_QuestGiver", L10n.T("航路任务", "Route quests"));
            for (int i = 0; i < island.Length; i++)
            {
                LocalizationHelper.InjectLocalization(island[i].NameKey, island[i].Name());
                LocalizationHelper.InjectLocalization(island[i].DescriptionKey, island[i].Description());
            }
        }

        private static bool OnIslandWithRoute(SkyIslandOfficialQuestContext context)
        {
            return context.OnIsland && context.Data != null && context.Data.SkyIslandRouteUnlocked;
        }

        /// <summary>未完成时的目标行取自绘面板同一份「还差什么」；前置都满足、只差去做时用 <paramref name="fallback"/>。</summary>
        private static string Blocker(SkyIslandStoryData data, SkyIslandStoryAction action, string fallback)
        {
            string blocker;
            SkyIslandStoryRules.CanApply(data, action, out blocker);
            return string.IsNullOrEmpty(blocker) ? fallback : blocker;
        }

        private static SkyIslandOfficialQuestDefinition[] BuildIsland()
        {
            var beacons = new SkyIslandOfficialQuestDefinition
            {
                QuestId = BeaconQuestId, GiverId = WeibaiGiverId,
                ObjectName = "BossRush_SkyIsland_Quest_590011",
                NameKey = BeaconQuestNameKey, DescriptionKey = BeaconQuestDescriptionKey,
                Name = () => L10n.T("点亮两端航标", "Light Both Beacons"),
                Contact = () => L10n.T("苇白／风铃集委托板", "Weibai / Windchime Market board"),
                Description = () => L10n.T("苇白：两头的灯都灭着，船看不见岛。悬根林的风标要校，残星工坊的星灯要修。都亮起来，回来跟我说一声。我不在就找风铃集的委托板。",
                    "Weibai: Both lights are out, so the ships cannot see the isles. The wind beacon in Hanging Root Wood needs calibrating, the star lamp at Fallen Star Workshop needs repairs. Get them burning and come tell me. If I am away, the Windchime Market board will do."),
                AcceptedFlag = SkyIslandStoryFlag.BeaconQuestAccepted, DeliveredFlag = SkyIslandStoryFlag.BeaconQuestDelivered,
                AcceptAction = SkyIslandStoryAction.AcceptBeaconQuest, DeliverAction = SkyIslandStoryAction.DeliverBeaconQuest,
                RewardMoney = BeaconQuestMoney,
                Gate = OnIslandWithRoute,
                Tasks = new[]
                {
                    new SkyIslandOfficialQuestTaskDefinition
                    {
                        TaskId = 1, Done = data => data.Has(SkyIslandStoryFlag.WindBeacon),
                        Description = data => data.Has(SkyIslandStoryFlag.WindBeacon)
                            ? L10n.T("悬根林的风标校好了。", "The Hanging Root Wood wind beacon is calibrated.")
                            : Blocker(data, SkyIslandStoryAction.RepairWindBeacon,
                                L10n.T("到悬根林风标下的见闻点，把西边那座风标校准。", "Go to the record point under the Hanging Root Wood beacon and calibrate the west one.")),
                    },
                    new SkyIslandOfficialQuestTaskDefinition
                    {
                        TaskId = 2, Done = data => data.Has(SkyIslandStoryFlag.StarLamp),
                        Description = data => data.Has(SkyIslandStoryFlag.StarLamp)
                            ? L10n.T("残星工坊的星灯亮了。", "The Fallen Star Workshop star lamp is lit.")
                            : Blocker(data, SkyIslandStoryAction.RepairStarLamp,
                                L10n.T("到残星工坊星灯下的见闻点，把东边那盏星灯修好。", "Go to the record point under the Fallen Star Workshop lamp and repair the east one.")),
                        ExtraHint = data => L10n.T("两盏都亮起来，回去跟苇白说一声。她不在就用风铃集的委托板。", "Once both are burning, go tell Weibai. If she is away, use the Windchime Market board."),
                    },
                },
            };
            var bellCourt = new SkyIslandOfficialQuestDefinition
            {
                QuestId = BellCourtQuestId, GiverId = FuzhouGiverId,
                ObjectName = "BossRush_SkyIsland_Quest_590012",
                NameKey = BellCourtQuestNameKey, DescriptionKey = BellCourtQuestDescriptionKey,
                Name = () => L10n.T("钟庭之争", "The Bell Court Standoff"),
                Contact = () => L10n.T("码头 · 浮舟", "Dock · Fuzhou"),
                Description = () => L10n.T("浮舟：灯亮了，钟还是哑的。钟守不肯松口。你从鸣风栈道过去，跟他谈，谈不拢就把守钟装置打停。回来告诉我一声。",
                    "Fuzhou: The lights are burning, but the bell is still silent. The Bell Keeper will not budge. Cross Windsong Boardwalk and talk to him. If talking fails, stop that bell engine of his. Then come back and tell me."),
                AcceptedFlag = SkyIslandStoryFlag.BellCourtQuestAccepted, DeliveredFlag = SkyIslandStoryFlag.BellCourtQuestDelivered,
                AcceptAction = SkyIslandStoryAction.AcceptBellCourtQuest, DeliverAction = SkyIslandStoryAction.DeliverBellCourtQuest,
                RewardMoney = BellCourtQuestMoney,
                Gate = context => OnIslandWithRoute(context) && context.Data.Has(SkyIslandStoryFlag.BeaconQuestDelivered),
                Tasks = new[]
                {
                    new SkyIslandOfficialQuestTaskDefinition
                    {
                        TaskId = 1, Done = data => data.BellKeeperResolved,
                        Description = data => data.BellKeeperResolved
                            ? L10n.T("钟守松口了。", "The Bell Keeper has stood down.")
                            : L10n.T("去归航钟庭，跟钟守谈，或者把守钟装置打停。", "Go to the Bell Court. Talk the Bell Keeper down, or stop his bell engine."),
                        ExtraHint = data => data.BellKeeperResolved
                            ? (data.Has(SkyIslandStoryFlag.HomecomingQuestDelivered)
                                ? L10n.T("回码头跟浮舟说一声。", "Go back to the dock and tell Fuzhou.")
                                : L10n.T("钟就在眼前，先把钟守的「归航钟」办了，回程再跟浮舟说。", "The bell is right here. Finish the Bell Keeper's Homecoming Bell first, then tell Fuzhou on the way back."))
                            : Blocker(data, SkyIslandStoryAction.ReconcileBellKeeper,
                                L10n.T("航路安全的证据已经足够，可以直接与钟守谈谈。", "You have enough proof that the lanes are safe; you can talk to the Bell Keeper.")),
                    },
                },
            };
            var homecoming = new SkyIslandOfficialQuestDefinition
            {
                QuestId = HomecomingQuestId, GiverId = BellKeeperGiverId,
                ObjectName = "BossRush_SkyIsland_Quest_590013",
                NameKey = HomecomingQuestNameKey, DescriptionKey = HomecomingQuestDescriptionKey,
                Name = () => L10n.T("归航钟", "The Homecoming Bell"),
                Contact = () => L10n.T("归航钟庭 · 钟守", "Bell Court · Bell Keeper"),
                Description = () => L10n.T("钟守：这钟以前是催人出航的。这一次不是。敲吧，让还在外头的人知道有人等着。",
                    "The Bell Keeper: This bell used to send people out. Not this time. Ring it, so whoever is still out there knows someone is waiting."),
                AcceptedFlag = SkyIslandStoryFlag.HomecomingQuestAccepted, DeliveredFlag = SkyIslandStoryFlag.HomecomingQuestDelivered,
                AcceptAction = SkyIslandStoryAction.AcceptHomecomingQuest, DeliverAction = SkyIslandStoryAction.DeliverHomecomingQuest,
                RewardMoney = HomecomingQuestMoney,
                // 钟庭事件就地衔接敲钟；浮舟的复命可留在返航路上，避免钟庭→码头→钟庭的空跑。
                Gate = context => OnIslandWithRoute(context) && context.Data.BothBeacons && context.Data.BellKeeperResolved,
                Tasks = new[]
                {
                    new SkyIslandOfficialQuestTaskDefinition
                    {
                        TaskId = 1, Done = data => data.Has(SkyIslandStoryFlag.Ending),
                        Description = data => data.Has(SkyIslandStoryFlag.Ending)
                            ? L10n.T("钟响过了。", "The bell has rung.")
                            : Blocker(data, SkyIslandStoryAction.RingHomecomingBell,
                                L10n.T("到归航钟庭的见闻点，敲响归航钟。", "Go to the record point in the Bell Court and ring the Homecoming Bell.")),
                    },
                },
            };
            return new[] { beacons, bellCourt, homecoming };
        }
    }
}
