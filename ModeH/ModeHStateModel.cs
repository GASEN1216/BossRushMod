using System;
using System.Collections.Generic;

namespace BossRush
{
    #region 枚举（全部显式整数，Unknown=0，只能在末尾追加）

    /// <summary>
    /// Mode H 唯一持久状态机（设计提案 §18.2、§20.1 v1 冻结）。
    /// 读到未知整数一律映射 Unknown 并对对应 key 设置写入保护。
    /// </summary>
    public enum ModeHLifecycle
    {
        /// <summary>未知/写保护</summary>
        Unknown = 0,
        /// <summary>无活动赛季</summary>
        None = 1,
        /// <summary>已冻结一次进入意图，尚未创建 Season</summary>
        EntryIntent = 2,
        /// <summary>等待目标场景、点位与静态候选缓存</summary>
        SceneLoading = 3,
        /// <summary>正式入口内逐 stable key 运行生产兼容性认证</summary>
        ProductionCertifying = 4,
        /// <summary>五席试棚展示</summary>
        Drafting = 5,
        /// <summary>已写入合同与落选回响去向</summary>
        RosterLocked = 6,
        /// <summary>本场敌军计划公布与一次免费侦察</summary>
        MatchBrief = 7,
        /// <summary>调整先发/接力、虚拟 kit、口令与下注</summary>
        LoadoutEditing = 8,
        /// <summary>公开报价预览</summary>
        OddsPreview = 9,
        /// <summary>整备、口令、下注、赔率与 seed 已锁定</summary>
        LoadoutLocked = 10,
        /// <summary>分帧生成本场角色</summary>
        MatchSpawning = 11,
        /// <summary>战斗中</summary>
        MatchFighting = 12,
        /// <summary>ERROR 已提交倒地/终局事实，等待恢复屏障</summary>
        ErrorRecoveryPending = 13,
        /// <summary>先发倒地且接力可用</summary>
        RelayPending = 14,
        /// <summary>一次性提交全部结算事实</summary>
        MatchSettling = 15,
        /// <summary>奖励选择、归档与路由</summary>
        Intermission = 16,
        /// <summary>第 2/4 场后的唯一败者市场</summary>
        TransferWindow = 17,
        /// <summary>第 6 场胜利后的名人堂写入</summary>
        HallOfFame = 18,
        /// <summary>赛季结束</summary>
        SeasonEnded = 19,
        /// <summary>技术恢复中</summary>
        Recovering = 20,
        /// <summary>持久挂起，等待人工/环境恢复</summary>
        Suspended = 21,
        /// <summary>真实资产路径：journal 预备完成</summary>
        StakePrepared = 22
    }

    /// <summary>
    /// 只从 lifecycle 派生的比赛相位（§20.1）。禁止另建跳转表或写入第二个权威状态。
    /// </summary>
    public enum ModeHMatchPhase
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>非比赛相位</summary>
        None = 1,
        /// <summary>看盘</summary>
        Brief = 2,
        /// <summary>整备编辑</summary>
        Editing = 3,
        /// <summary>已锁盘</summary>
        Locked = 4,
        /// <summary>生成中</summary>
        Spawning = 5,
        /// <summary>战斗中</summary>
        Fighting = 6,
        /// <summary>等待接力</summary>
        RelayPending = 7,
        /// <summary>结算中</summary>
        Settling = 8,
        /// <summary>恢复相位</summary>
        Recovery = 9,
        /// <summary>幕间</summary>
        Intermission = 10
    }

    /// <summary>合同选手唯一角色状态（§20.1）。</summary>
    public enum ModeHParticipantStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>可用</summary>
        Available = 1,
        /// <summary>带伤</summary>
        Injured = 2,
        /// <summary>赛季退役</summary>
        Retired = 3,
        /// <summary>市场窗口被替换</summary>
        Released = 4,
        /// <summary>撕票移除</summary>
        Removed = 5
    }

    /// <summary>战报状态（§20.1）。不保存未结算报告状态。</summary>
    public enum ModeHMatchReportStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>已结算，等待归档</summary>
        SettledPendingArchive = 1,
        /// <summary>已归档</summary>
        Archived = 2
    }

    /// <summary>虚拟奖励 operation 状态（§20.1，单向）。</summary>
    public enum ModeHSeasonRewardOperationStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>已提供候选，等待玩家选择</summary>
        Offered = 1,
        /// <summary>已应用</summary>
        Applied = 2,
        /// <summary>已归档</summary>
        Archived = 3
    }

    /// <summary>虚拟奖励类型（§20.1）。</summary>
    public enum ModeHRewardKind
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>无奖励（败场）</summary>
        None = 1,
        /// <summary>解锁一件虚拟 kit</summary>
        UnlockKit = 2,
        /// <summary>候选池耗尽的自动名声</summary>
        FameDisplay = 3
    }

    /// <summary>转会 offer 状态（§20.1）。禁止用单个 accepted 布尔混淆三态。</summary>
    public enum ModeHOfferStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>等待玩家选择</summary>
        Pending = 1,
        /// <summary>已接受</summary>
        Accepted = 2,
        /// <summary>已拒绝</summary>
        Rejected = 3,
        /// <summary>已过期</summary>
        Expired = 4
    }

    /// <summary>逐 stable key 生产认证结论（§20.1）。</summary>
    public enum ModeHCertificationStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>通过，可进入生产池</summary>
        Passed = 1,
        /// <summary>拒绝</summary>
        Rejected = 2
    }

    /// <summary>
    /// 行为兼容状态（§17.6.4）。v1 冻结前三项；PartiallyVerified 按“只能在末尾追加”规则取 4。
    /// 伤病与战痕不使用 PartiallyVerified：任一分量不可用时整条不进抽池。
    /// </summary>
    public enum ModeHCommandCompatibilityStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>全部 effect 通过</summary>
        VerifiedBehavior = 1,
        /// <summary>控制点存在但全部 effect 未通过：只进诊断页与赛后报告</summary>
        ReportOnly = 2,
        /// <summary>控制点缺失：完全隐藏，不得抽取</summary>
        Unavailable = 3,
        /// <summary>部分 effect 通过：可选，但文案只描述已通过部分</summary>
        PartiallyVerified = 4
    }

    /// <summary>名人堂跨 key 幂等命令状态（§20.1）。</summary>
    public enum ModeHHallOfFameCommandStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>待写入名人堂</summary>
        Pending = 1,
        /// <summary>已完成</summary>
        Completed = 2
    }

    /// <summary>退出原因（§20.1）。</summary>
    public enum ModeHExitReason
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>玩家地图返回</summary>
        UserMapReturn = 1,
        /// <summary>场景 generation 失配</summary>
        SceneGenerationMismatch = 2,
        /// <summary>Mod 销毁</summary>
        ModDestroyed = 3,
        /// <summary>技术中止</summary>
        TechnicalAbort = 4,
        /// <summary>赛季完成</summary>
        SeasonComplete = 5,
        /// <summary>入口不可用</summary>
        Unavailable = 6
    }

    /// <summary>
    /// 入口可用性结果（§20.1 的 ModeHAvailability 枚举）。
    /// 命名加 Status 后缀以避开 §25.1 的静态类 <see cref="ModeHAvailability"/>；两者语义分工不变：
    /// 类负责计算与 fail-closed 原因，枚举只表达三态结果，且不持久化。
    /// </summary>
    public enum ModeHAvailabilityStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>可进入</summary>
        Available = 1,
        /// <summary>不可进入</summary>
        Unavailable = 2
    }

    /// <summary>比赛终局归属（§17.4）。</summary>
    public enum ModeHMatchOutcome
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>玩家胜利</summary>
        PlayerVictory = 1,
        /// <summary>玩家失败</summary>
        PlayerDefeat = 2
    }

    /// <summary>真实资产路径：逐项 receipt 状态（§20.1 冻结）。</summary>
    public enum ModeHStakeReceiptStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>已计划</summary>
        Planned = 1,
        /// <summary>已执行</summary>
        Applied = 2,
        /// <summary>已核对</summary>
        Verified = 3,
        /// <summary>已拒绝</summary>
        Rejected = 4,
        /// <summary>等待回读</summary>
        Pending = 5,
        /// <summary>需要人工介入</summary>
        ManualIntervention = 6
    }

    /// <summary>真实资产路径：结算类别（§20.1 冻结）。</summary>
    public enum ModeHSettlementKind
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>尚未进入 committed phase</summary>
        None = 1,
        /// <summary>正常比赛结果</summary>
        MatchResult = 2,
        /// <summary>技术中止后的完整返还</summary>
        AbortReturn = 3
    }

    /// <summary>真实资产路径：奖励父 operation 状态（§20.1 冻结）。</summary>
    public enum ModeHRewardOperationStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>已计划</summary>
        Planned = 1,
        /// <summary>已提交</summary>
        Committed = 2,
        /// <summary>等待逐项结算</summary>
        SettlementPending = 3,
        /// <summary>已结算</summary>
        Settled = 4,
        /// <summary>需要人工介入</summary>
        ManualIntervention = 5
    }

    /// <summary>真实资产路径：退款父 operation 状态（§20.1 冻结）。</summary>
    public enum ModeHAbortReturnOperationStatus
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>已提交</summary>
        Committed = 1,
        /// <summary>等待逐项结算</summary>
        SettlementPending = 2,
        /// <summary>已结算</summary>
        Settled = 3,
        /// <summary>需要人工介入</summary>
        ManualIntervention = 4
    }

    /// <summary>
    /// 真实资产路径：journal 阶段（§22.2 冻结，单向）。
    /// phase 是唯一终态来源，不另设可漂移的 terminal 布尔。
    /// </summary>
    public enum ModeHStakePhase
    {
        /// <summary>未知</summary>
        Unknown = 0,
        /// <summary>无活动事务</summary>
        None = 1,
        /// <summary>已写入押品与最坏损失候选</summary>
        Prepared = 2,
        /// <summary>escrow 快照已持久</summary>
        EscrowSnapshotDurable = 3,
        /// <summary>escrow 已从运行时 inventory 脱离并持久</summary>
        EscrowRemovedDurable = 4,
        /// <summary>计划、赔率、装备快照与 journal 均不可变</summary>
        MatchLocked = 5,
        /// <summary>已写入唯一 result token 与父 reward operation</summary>
        ResultCommitted = 6,
        /// <summary>逐项 receipt 或库存回调未完成</summary>
        SettlementPending = 7,
        /// <summary>正常比赛结果终态</summary>
        Terminal = 8,
        /// <summary>已证明 escrow 从未移除的取消终态</summary>
        CancelledTerminal = 9,
        /// <summary>技术中止后的完整返还已提交</summary>
        AbortReturnCommitted = 10,
        /// <summary>完整返还终态</summary>
        RefundedTerminal = 11,
        /// <summary>证据冲突，需人工介入</summary>
        ManualIntervention = 12
    }

    #endregion

    #region 稳定 ID 常量（§17.5、§17.6.3、§17.4）

    /// <summary>
    /// Mode H 冻结稳定 ID。JSON、DTO、UI、guard 共用同一份字面量，禁止各处散写。
    /// </summary>
    public static class ModeHStableIds
    {
        /// <summary>五种公开原型（§17.5 原型矩阵只认这五个 ID）。</summary>
        public const string ArchetypeAssault = "assault";
        /// <summary>远程压制</summary>
        public const string ArchetypeRanged = "ranged";
        /// <summary>重装坚守</summary>
        public const string ArchetypeTank = "tank";
        /// <summary>消耗/召唤</summary>
        public const string ArchetypeSustain = "sustain";
        /// <summary>残局收割</summary>
        public const string ArchetypeFinisher = "finisher";

        /// <summary>五种公开原型的规范顺序。</summary>
        public static readonly string[] AllArchetypes = new string[]
        {
            ArchetypeAssault,
            ArchetypeFinisher,
            ArchetypeRanged,
            ArchetypeSustain,
            ArchetypeTank
        };

        /// <summary>八条通用口令（§17.6.3 冻结）。</summary>
        public const string CommandSteady = "steady";
        /// <summary>压上</summary>
        public const string CommandPress = "press";
        /// <summary>回到中间</summary>
        public const string CommandCenter = "center";
        /// <summary>清掉旁边</summary>
        public const string CommandSpread = "spread";
        /// <summary>收割</summary>
        public const string CommandFinish = "finish";
        /// <summary>留一手</summary>
        public const string CommandHold = "hold";
        /// <summary>护替补</summary>
        public const string CommandGuard = "guard";
        /// <summary>拼了</summary>
        public const string CommandAllIn = "all_in";

        /// <summary>八条通用口令的规范顺序（ordinal 升序）。</summary>
        public static readonly string[] AllCommonCommands = new string[]
        {
            CommandAllIn,
            CommandCenter,
            CommandFinish,
            CommandGuard,
            CommandHold,
            CommandPress,
            CommandSpread,
            CommandSteady
        };

        /// <summary>五类招牌口令模板（§17.6.3）。</summary>
        public static readonly string[] AllSignatureCommands = new string[]
        {
            "anchor",
            "handoff",
            "last_mag",
            "together",
            "weakness"
        };

        /// <summary>六种固有底色（§17.6.3）。</summary>
        public const string TemperamentAggressive = "aggressive";
        /// <summary>谨慎</summary>
        public const string TemperamentCautious = "cautious";
        /// <summary>猎手</summary>
        public const string TemperamentHunter = "hunter";
        /// <summary>坚守</summary>
        public const string TemperamentBulwark = "bulwark";
        /// <summary>诡术</summary>
        public const string TemperamentTrickster = "trickster";
        /// <summary>群性</summary>
        public const string TemperamentPack = "pack";

        /// <summary>六种底色的规范顺序。</summary>
        public static readonly string[] AllTemperaments = new string[]
        {
            TemperamentAggressive,
            TemperamentBulwark,
            TemperamentCautious,
            TemperamentHunter,
            TemperamentPack,
            TemperamentTrickster
        };

        /// <summary>
        /// 稳定型底色（§17.2 要求五席至少两名）。莽攻与诡术因行为方差较大不计入。
        /// </summary>
        public static readonly string[] StableTemperaments = new string[]
        {
            TemperamentBulwark,
            TemperamentCautious,
            TemperamentHunter
        };

        /// <summary>八种普通怪癖（§17.6.3）。</summary>
        public static readonly string[] AllQuirks = new string[]
        {
            "center_keeper",
            "clutch",
            "protect_sub",
            "reload_first",
            "revenge",
            "skill_saver",
            "slow_start",
            "soft_target"
        };

        /// <summary>公开异常：三种胆怯 + ERROR（§17.6.4、§17.6.5）。</summary>
        public const string AnomalyCowardBlood = "blood";
        /// <summary>惧众胆怯</summary>
        public const string AnomalyCowardCrowd = "crowd";
        /// <summary>畏强胆怯</summary>
        public const string AnomalyCowardStrong = "strong";
        /// <summary>控制权异常</summary>
        public const string AnomalyError = "error";

        /// <summary>四种公开异常的规范顺序。</summary>
        public static readonly string[] AllAnomalies = new string[]
        {
            AnomalyCowardBlood,
            AnomalyCowardCrowd,
            AnomalyError,
            AnomalyCowardStrong
        };

        /// <summary>五条伤病（§17.4 冻结）。</summary>
        public static readonly string[] AllInjuries = new string[]
        {
            "armor",
            "hand",
            "leg",
            "old_wound",
            "spirit"
        };

        /// <summary>八条战痕（§17.4 冻结）。</summary>
        public static readonly string[] AllScars = new string[]
        {
            "bell_dependence",
            "blood_rush",
            "broken_shield_charge",
            "center_keeper",
            "crowd_favorite",
            "longshot_memory",
            "relay_expert",
            "skill_saver_scar"
        };

        /// <summary>落选回响三路去向（§20.1）。</summary>
        public const string EchoDestinationReturnEnemy = "return_enemy";
        /// <summary>候签，进入第 2 场后的败者市场</summary>
        public const string EchoDestinationTransferCandidate = "transfer_candidate";
        /// <summary>撕票，彻底离开本赛季</summary>
        public const string EchoDestinationRemoved = "removed";

        /// <summary>特殊击杀标签（§17.4）。</summary>
        public const string SpecialKillHighThreatCore = "high_threat_core";
        /// <summary>接力者击倒最终敌人</summary>
        public const string SpecialKillRelayFinisher = "relay_finisher";
        /// <summary>残血击倒最终敌人</summary>
        public const string SpecialKillLastStand = "last_stand";

        /// <summary>四类侦察（§17.5）。</summary>
        public static readonly string[] AllReconChoices = new string[]
        {
            "hidden_quirk",
            "member_order",
            "second_equipment",
            "current_injury"
        };

        /// <summary>虚拟 kit 允许的装备槽（§17.7）。</summary>
        public static readonly string[] AllowedKitSlots = new string[]
        {
            "Armor",
            "Helmat",
            "MeleeWeapon",
            "PrimaryWeapon",
            "SecondaryWeapon"
        };

        /// <summary>六种看台表演模式（§17.6.5）。</summary>
        public static readonly string[] AllStandInPatterns = new string[]
        {
            "anchor_stand",
            "erratic_dart",
            "gate_pace",
            "rail_charge",
            "slow_circle",
            "wall_hug"
        };
    }

    #endregion

    #region 派生映射与校验

    /// <summary>
    /// lifecycle -> matchPhase 的唯一映射（§20.1 冻结），以及稳定 ID / roster 不变式校验。
    /// </summary>
    public static class ModeHStateModel
    {
        /// <summary>把持久 lifecycle 映射为派生比赛相位。禁止另建第二个权威状态。</summary>
        public static ModeHMatchPhase GetMatchPhase(ModeHLifecycle lifecycle)
        {
            switch (lifecycle)
            {
                case ModeHLifecycle.None:
                case ModeHLifecycle.EntryIntent:
                case ModeHLifecycle.SceneLoading:
                case ModeHLifecycle.ProductionCertifying:
                case ModeHLifecycle.Drafting:
                case ModeHLifecycle.RosterLocked:
                    return ModeHMatchPhase.None;
                case ModeHLifecycle.MatchBrief:
                    return ModeHMatchPhase.Brief;
                case ModeHLifecycle.LoadoutEditing:
                case ModeHLifecycle.OddsPreview:
                    return ModeHMatchPhase.Editing;
                case ModeHLifecycle.LoadoutLocked:
                case ModeHLifecycle.StakePrepared:
                    return ModeHMatchPhase.Locked;
                case ModeHLifecycle.MatchSpawning:
                    return ModeHMatchPhase.Spawning;
                case ModeHLifecycle.MatchFighting:
                    return ModeHMatchPhase.Fighting;
                case ModeHLifecycle.RelayPending:
                    return ModeHMatchPhase.RelayPending;
                case ModeHLifecycle.MatchSettling:
                    return ModeHMatchPhase.Settling;
                case ModeHLifecycle.ErrorRecoveryPending:
                case ModeHLifecycle.Recovering:
                case ModeHLifecycle.Suspended:
                    return ModeHMatchPhase.Recovery;
                case ModeHLifecycle.Intermission:
                case ModeHLifecycle.TransferWindow:
                    return ModeHMatchPhase.Intermission;
                case ModeHLifecycle.HallOfFame:
                case ModeHLifecycle.SeasonEnded:
                    return ModeHMatchPhase.None;
                default:
                    return ModeHMatchPhase.Unknown;
            }
        }

        /// <summary>读到未知整数一律映射 Unknown（§20.1 写保护前提）。</summary>
        public static ModeHLifecycle ToLifecycle(int raw)
        {
            if (raw < (int)ModeHLifecycle.None || raw > (int)ModeHLifecycle.StakePrepared)
            {
                return ModeHLifecycle.Unknown;
            }
            return (ModeHLifecycle)raw;
        }

        /// <summary>读到未知整数一律映射 Unknown。</summary>
        public static ModeHParticipantStatus ToParticipantStatus(int raw)
        {
            if (raw < (int)ModeHParticipantStatus.Available || raw > (int)ModeHParticipantStatus.Removed)
            {
                return ModeHParticipantStatus.Unknown;
            }
            return (ModeHParticipantStatus)raw;
        }

        /// <summary>读到未知整数一律映射 Unknown。</summary>
        public static ModeHCommandCompatibilityStatus ToCompatibilityStatus(int raw)
        {
            if (raw < (int)ModeHCommandCompatibilityStatus.VerifiedBehavior
                || raw > (int)ModeHCommandCompatibilityStatus.PartiallyVerified)
            {
                return ModeHCommandCompatibilityStatus.Unknown;
            }
            return (ModeHCommandCompatibilityStatus)raw;
        }

        /// <summary>读到未知整数一律映射 Unknown。</summary>
        public static ModeHStakePhase ToStakePhase(int raw)
        {
            if (raw < (int)ModeHStakePhase.None || raw > (int)ModeHStakePhase.ManualIntervention)
            {
                return ModeHStakePhase.Unknown;
            }
            return (ModeHStakePhase)raw;
        }

        /// <summary>journal 是否处于终态（§22.1 IsSlotConsistent 依赖）。</summary>
        public static bool IsStakePhaseTerminal(ModeHStakePhase phase)
        {
            return phase == ModeHStakePhase.Terminal
                || phase == ModeHStakePhase.CancelledTerminal
                || phase == ModeHStakePhase.RefundedTerminal;
        }

        /// <summary>该状态是否属于“已创建 Season”的活动赛季状态。</summary>
        public static bool IsSeasonPersistedLifecycle(ModeHLifecycle lifecycle)
        {
            return lifecycle != ModeHLifecycle.Unknown
                && lifecycle != ModeHLifecycle.None
                && lifecycle != ModeHLifecycle.EntryIntent
                && lifecycle != ModeHLifecycle.SceneLoading
                && lifecycle != ModeHLifecycle.ProductionCertifying;
        }

        /// <summary>可用于上场的合同选手状态。</summary>
        public static bool IsLiveContractStatus(ModeHParticipantStatus status)
        {
            return status == ModeHParticipantStatus.Available || status == ModeHParticipantStatus.Injured;
        }

        /// <summary>
        /// roster 不变式（§17.3）：先发必须是存活合同选手；接力者只能是另一名存活合同选手或空；
        /// 存活数为 1 时强制 matchRelay 为空。
        /// </summary>
        public static bool ValidateRosterInvariant(
            string matchStarterProfileId,
            string matchRelayProfileId,
            ICollection<string> liveContractProfileIds,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (liveContractProfileIds == null || liveContractProfileIds.Count == 0)
            {
                failureReasonId = "no_live_contract";
                return false;
            }
            if (string.IsNullOrEmpty(matchStarterProfileId) || !liveContractProfileIds.Contains(matchStarterProfileId))
            {
                failureReasonId = "starter_not_live_contract";
                return false;
            }
            bool hasRelay = !string.IsNullOrEmpty(matchRelayProfileId);
            if (hasRelay)
            {
                if (!liveContractProfileIds.Contains(matchRelayProfileId))
                {
                    failureReasonId = "relay_not_live_contract";
                    return false;
                }
                if (string.Equals(matchRelayProfileId, matchStarterProfileId, StringComparison.Ordinal))
                {
                    failureReasonId = "relay_duplicates_starter";
                    return false;
                }
            }
            if (liveContractProfileIds.Count == 1 && hasRelay)
            {
                failureReasonId = "relay_must_be_empty_for_single_roster";
                return false;
            }
            return true;
        }

        /// <summary>稳定 ID 合法性：非空、无空白、只允许 [a-z0-9_.-]，长度 1..64。</summary>
        public static bool IsValidStableId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>effectId 命名固定为 &lt;commandId&gt;.&lt;controlPointId&gt;（§17.6.4）。</summary>
        public static string ComposeEffectId(string commandId, string controlPointId)
        {
            if (string.IsNullOrEmpty(commandId) || string.IsNullOrEmpty(controlPointId)) return null;
            return commandId + "." + controlPointId;
        }

        /// <summary>公开分差 -> 赔率档位（§17.5 冻结阈值）。</summary>
        public static int ResolveOddsTier(int publicEdge)
        {
            if (publicEdge >= ModeHConfig.OddsThresholdX1MinEdge) return 1;
            if (publicEdge >= ModeHConfig.OddsThresholdX2MinEdge) return 2;
            if (publicEdge >= ModeHConfig.OddsThresholdX3MinEdge) return 3;
            if (publicEdge >= ModeHConfig.OddsThresholdX4MinEdge) return 4;
            return 5;
        }
    }

    #endregion
}
