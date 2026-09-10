using System;

namespace BossRush
{
    /// <summary>航务委托的三个方向：打、搜、走。覆盖玩家在群岛上会做的全部基础动作。</summary>
    internal enum SkyIslandBountyKind
    {
        None = 0,
        /// <summary>清理航路：完成 N 组遭遇。</summary>
        Threats = 1,
        /// <summary>补给回收：搜刮 N 处物资点。</summary>
        Salvage = 2,
        /// <summary>巡岛：踏足 N 个区域。</summary>
        Survey = 3
    }

    /// <summary>
    /// COMPAT：苇白的「航务委托」——群岛的可重复目标。纯逻辑、无 Unity 依赖（除文案走 `L10n.T`
    /// 外不碰任何引擎类型），隔离回归直接链接本文件执行，只需一个 `L10n` 替身。
    ///
    /// 口径：
    /// - **按出击计**，不进存档。委托是让单次出击有明确短目标的装置，不是跨局养成；
    ///   这样也不必为纯消耗性内容扩 `BossRush_SkyIsland_Story_v1` 的 schema。
    /// - 进度用「接单时的基线」相减，先打后接不会白算，接单后离线也不会漏算。
    /// - 每完成一单，下一单目标 +1、第三单起奖励升到星工遗存档：重复可做但不是无痛刷。
    /// - 单次出击最多 <see cref="MaxRounds"/> 单，避免「目标只线性增长、奖励恒定保底顶档」被刷。
    ///
    /// **本类不知道世界里还剩多少活可做**：清场与巡岛的计数源都是持久存档事实
    /// （遭遇清一次就永久记下、区域走过就永久记下），老档上可能一件都不剩。
    /// 因此「这一单接了还能不能完成」必须由调用方在派单前用可完成量门控
    /// （见 `SkyIslandSession.AvailableBountyProgress`），本类只提供 <see cref="TryAbandon"/>
    /// 作为兜底：任何残留的不可完成情形，玩家都能自己退掉，不会把委托槽卡死一整局。
    /// </summary>
    internal sealed class SkyIslandBounty
    {
        internal const int BaseThreatTarget = 3;
        internal const int BaseSalvageTarget = 4;
        internal const int BaseSurveyTarget = 4;
        /// <summary>第几单起奖励升档。</summary>
        internal const int StarworksFromRound = 3;
        /// <summary>单次出击可完成的委托上限。</summary>
        internal const int MaxRounds = 3;

        private int clearedTotal, salvagedTotal, surveyedTotal;
        private int baseline;
        private SkyIslandBountyKind active;
        private int target;
        private int completedRounds;

        internal SkyIslandBountyKind Active { get { return active; } }
        internal int Target { get { return target; } }
        internal int CompletedRounds { get { return completedRounds; } }
        internal bool HasActive { get { return active != SkyIslandBountyKind.None; } }

        internal void ReportEncounterCleared() { clearedTotal++; }
        internal void ReportScavenged() { salvagedTotal++; }
        internal void ReportRegionVisited() { surveyedTotal++; }

        internal int Counter(SkyIslandBountyKind kind)
        {
            if (kind == SkyIslandBountyKind.Threats) return clearedTotal;
            if (kind == SkyIslandBountyKind.Salvage) return salvagedTotal;
            if (kind == SkyIslandBountyKind.Survey) return surveyedTotal;
            return 0;
        }

        /// <summary>本轮目标数：基础值 + 已完成单数，重复接单会逐渐变重。</summary>
        internal int TargetFor(SkyIslandBountyKind kind)
        {
            if (kind == SkyIslandBountyKind.Threats) return BaseThreatTarget + completedRounds;
            if (kind == SkyIslandBountyKind.Salvage) return BaseSalvageTarget + completedRounds;
            if (kind == SkyIslandBountyKind.Survey) return BaseSurveyTarget + completedRounds;
            return 0;
        }

        internal int Progress
        {
            get
            {
                if (!HasActive) return 0;
                int value = Counter(active) - baseline;
                return value < 0 ? 0 : (value > target ? target : value);
            }
        }

        internal bool IsComplete { get { return HasActive && Progress >= target; } }

        /// <summary>本局是否还能再接单（已交满 <see cref="MaxRounds"/> 就收摊）。</summary>
        internal bool CanAcceptMore { get { return completedRounds < MaxRounds; } }

        internal bool TryAccept(SkyIslandBountyKind kind, out string message)
        {
            if (kind == SkyIslandBountyKind.None)
            { message = L10n.T("这不是一份有效的委托。", "That is not a valid contract."); return false; }
            if (HasActive)
            {
                message = L10n.T("苇白：手头这一单还没交呢 —— ", "Weibai: You still owe me on this one — ") + Describe();
                return false;
            }
            if (!CanAcceptMore)
            {
                message = L10n.T("苇白：今天的活都派完啦，剩下的留给下一趟。",
                    "Weibai: That is all the work I have today. The rest can wait for your next trip.");
                return false;
            }
            active = kind;
            target = TargetFor(kind);
            // 基线取当前计数：接单前已经做过的量不计入本单，也不会倒扣。
            baseline = Counter(kind);
            message = L10n.T("苇白：那就拜托了 —— ", "Weibai: Much obliged, then — ") + Describe();
            return true;
        }

        /// <summary>本轮谢礼档次：第三单起升到星工遗存。交单前后都可查询。</summary>
        internal SkyIslandLootTier PendingReward
        {
            get
            {
                return completedRounds + 1 >= StarworksFromRound
                    ? SkyIslandLootTier.Starworks : SkyIslandLootTier.Voyage;
            }
        }

        /// <summary>
        /// 交单。**先送达再消费**：`deliver` 返回 false 时委托原样保留，
        /// 不能出现「单没了、谢礼也没有」——原设计明确要求奖励送达与状态推进分开
        /// （`docs/制作教程/天空岛大地图_场景设计与制作教程.md` §11）。
        /// </summary>
        internal bool TryClaim(Func<SkyIslandLootTier, bool> deliver, out SkyIslandLootTier reward, out string message)
        {
            reward = PendingReward;
            if (!HasActive)
            {
                message = L10n.T("苇白：现在没有你手上的委托，先挑一单吧。",
                    "Weibai: You are not carrying a contract. Pick one up first.");
                return false;
            }
            if (!IsComplete)
            {
                message = L10n.T("苇白：还差一点 —— ", "Weibai: Not quite there yet — ") + Describe();
                return false;
            }
            if (deliver != null && !deliver(reward))
            {
                message = L10n.T("苇白：谢礼一时放不下，委托先给你留着，稍后再来。",
                    "Weibai: There is nowhere to set the payout down. The contract stays yours; come back in a moment.");
                return false;
            }
            completedRounds++;
            active = SkyIslandBountyKind.None;
            target = 0;
            baseline = 0;
            message = L10n.T("苇白：辛苦了。这些是集市能凑出来的谢礼，收下吧。",
                "Weibai: Thank you. This is what the market could put together — take it.");
            return true;
        }

        /// <summary>
        /// 退单。不扣 <c>completedRounds</c>、不给谢礼，只把委托槽让出来。
        ///
        /// 这是「接了才发现做不完」的唯一出口：清场与巡岛的计数源都是持久存档事实，
        /// 老档上可能一件都不剩，没有退单就等于把本局委托槽永久卡死。
        /// </summary>
        internal bool TryAbandon(out string message)
        {
            if (!HasActive)
            {
                message = L10n.T("苇白：你手上没有委托，不用退。",
                    "Weibai: You are not carrying a contract, so there is nothing to drop.");
                return false;
            }
            message = L10n.T("苇白：不勉强。这一单先撤了，想做别的随时来。",
                "Weibai: No pressure. I have withdrawn it — come back if you fancy something else.");
            active = SkyIslandBountyKind.None;
            target = 0;
            baseline = 0;
            return true;
        }

        internal string Describe()
        {
            if (!HasActive) return L10n.T("当前没有进行中的委托。", "No contract in progress.");
            return Name(active) + " " + Progress + "/" + target;
        }

        /// <summary>
        /// 委托名的**唯一**取用点。此前 `NameEn` 写好了却零调用、调用方一律直接用 `NameCn`，
        /// 于是英文玩家在派单、进度与 HUD 上看到的全是中文（CR-2026-09-09-011）。
        /// </summary>
        internal static string Name(SkyIslandBountyKind kind) { return L10n.T(NameCn(kind), NameEn(kind)); }

        internal static string NameCn(SkyIslandBountyKind kind)
        {
            if (kind == SkyIslandBountyKind.Threats) return "清理航路威胁";
            if (kind == SkyIslandBountyKind.Salvage) return "回收沿线补给";
            if (kind == SkyIslandBountyKind.Survey) return "巡视群岛区域";
            return "无委托";
        }

        internal static string NameEn(SkyIslandBountyKind kind)
        {
            if (kind == SkyIslandBountyKind.Threats) return "Clear the lanes";
            if (kind == SkyIslandBountyKind.Salvage) return "Recover supplies";
            if (kind == SkyIslandBountyKind.Survey) return "Survey the isles";
            return "No contract";
        }

        internal static SkyIslandBountyKind[] AllKinds
        {
            get
            {
                return new[]
                {
                    SkyIslandBountyKind.Threats, SkyIslandBountyKind.Salvage, SkyIslandBountyKind.Survey
                };
            }
        }
    }
}
