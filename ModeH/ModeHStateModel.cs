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

    #region 持久化 DTO（[Serializable]，禁字段初始化器）

    /// <summary>逐 effect 或逐条目的行为状态快照（§17.6.4）。</summary>
    [Serializable]
    public sealed class ModeHBehaviorStatusDto
    {
        /// <summary>条目 ID：口令为 commandId，effect 为 &lt;commandId&gt;.&lt;controlPointId&gt;，伤病/战痕为其稳定 ID。</summary>
        public string entryId;
        /// <summary>条目类别：command / effect / injury / scar / anomaly。</summary>
        public string entryKind;
        /// <summary>ModeHCommandCompatibilityStatus 的整数值。</summary>
        public int status;
    }

    /// <summary>单条口令在某 stable key 上的认证结论（§20.1）。</summary>
    [Serializable]
    public sealed class ModeHCommandCertificationStatusDto
    {
        /// <summary>口令稳定 ID。</summary>
        public string commandId;
        /// <summary>ModeHCommandCompatibilityStatus 的整数值。</summary>
        public int status;
        /// <summary>逐 effect 结论（按 entryId ordinal 排序）。</summary>
        public List<ModeHBehaviorStatusDto> effectStatuses;
    }

    /// <summary>逐 stable key 的生产认证记录（§20.1）。只保存纯诊断数据。</summary>
    [Serializable]
    public sealed class ModeHPresetCertificationRecordDto
    {
        /// <summary>官方预设稳定 key。</summary>
        public string stableKey;
        /// <summary>ModeHCertificationStatus 的整数值。</summary>
        public int status;
        /// <summary>稳定排序的失败原因 ID 集合。</summary>
        public List<string> failureReasonIds;
        /// <summary>按 commandId 排序的口令结论。</summary>
        public List<ModeHCommandCertificationStatusDto> commandStatuses;
        /// <summary>创建 await 窗口的时间线摘要。</summary>
        public string spawnTimelineDigest;
        /// <summary>窗口内伤害事件数。</summary>
        public int damageEventCount;
        /// <summary>窗口内死亡事件数。</summary>
        public int deathEventCount;
        /// <summary>窗口内额外掉落事件数。</summary>
        public int extraDropEventCount;
        /// <summary>窗口内旧模式 tracking 事件数。</summary>
        public int legacyTrackingEventCount;
        /// <summary>峰值非预期活动实例数。</summary>
        public int peakUnexpectedActiveCount;
        /// <summary>本条耗时（毫秒）。</summary>
        public int durationMs;
    }

    /// <summary>当前 runtime 生产认证报告（§20.1）。</summary>
    [Serializable]
    public sealed class ModeHProductionCertificationDto
    {
        /// <summary>认证记录 schema 版本。</summary>
        public int certificationSchemaVersion;
        /// <summary>当前加载的 Assembly-CSharp.dll 字节摘要。</summary>
        public string gameBuildSignature;
        /// <summary>当前加载的 BossRush.dll 字节摘要。</summary>
        public string modBuildSignature;
        /// <summary>内容目录摘要。</summary>
        public string contentCatalogSignature;
        /// <summary>完成时间（UTC，"O" 格式）。</summary>
        public string completedUtc;
        /// <summary>按 stableKey 排序的逐 key 记录。</summary>
        public List<ModeHPresetCertificationRecordDto> records;
        /// <summary>通过的 stable key 集合。</summary>
        public List<string> passedStableKeys;
        /// <summary>全池均可用的通用口令 ID 集合。</summary>
        public List<string> commonVerifiedCommandIds;
        /// <summary>整体是否通过门槛。</summary>
        public bool overallPassed;
    }

    /// <summary>
    /// 四签名生产认证缓存（§17.2）。存放于 HallOfFame envelope，跨赛季存在，不随 Season 清空。
    /// </summary>
    [Serializable]
    public sealed class ModeHCertificationCacheDto
    {
        /// <summary>缓存键：game 构建签名。</summary>
        public string gameBuildSignature;
        /// <summary>缓存键：mod 构建签名。</summary>
        public string modBuildSignature;
        /// <summary>缓存键：内容目录签名。</summary>
        public string contentCatalogSignature;
        /// <summary>缓存键：存档槽 generation。</summary>
        public int slotGeneration;
        /// <summary>缓存的完整报告。</summary>
        public ModeHProductionCertificationDto snapshot;
        /// <summary>报告摘要，用于检测缓存被篡改。</summary>
        public string snapshotDigest;
    }

    /// <summary>合同选手档案（§20.1）。角色状态只在 profile，不在其它 DTO 复制。</summary>
    [Serializable]
    public sealed class ModeHProfileDto
    {
        /// <summary>本季全局唯一 profile ID。</summary>
        public string profileId;
        /// <summary>官方预设稳定 key。</summary>
        public string stableKey;
        /// <summary>显示名本地化 key。</summary>
        public string displayNameKey;
        /// <summary>公开原型 ID。</summary>
        public string archetypeId;
        /// <summary>固有底色 ID。</summary>
        public string temperamentId;
        /// <summary>普通怪癖 ID（与 anomalyId 互斥，可空）。</summary>
        public string quirkId;
        /// <summary>公开异常 ID（与 quirkId 互斥，可空）。</summary>
        public string anomalyId;
        /// <summary>招牌口令 ID。</summary>
        public string signatureCommandId;
        /// <summary>试棚传闻本地化 key。</summary>
        public string rumorKey;
        /// <summary>看台表演模式 ID（ERROR 互换时使用）。</summary>
        public string standInPatternId;
        /// <summary>ModeHParticipantStatus 的整数值。</summary>
        public int status;
        /// <summary>当前伤病 ID（空表示无伤）。</summary>
        public string injuryId;
        /// <summary>已获得战痕（最多三条）。</summary>
        public List<string> scarIds;
        /// <summary>稳定名声（只用于展示）。</summary>
        public int fameDisplayCount;
        /// <summary>实际踏入擂台的场次数。</summary>
        public int enteredMatchCount;
        /// <summary>按 stable key 固定快照的逐条行为状态。</summary>
        public List<ModeHBehaviorStatusDto> behaviorStatuses;
    }

    /// <summary>合同角色（§20.1）。合同只由这两个 ID 决定。</summary>
    [Serializable]
    public sealed class ModeHContractDto
    {
        /// <summary>赛季核心主将合同 profile ID。</summary>
        public string contractMainProfileId;
        /// <summary>当前替补合同 profile ID（空字符串表示空槽）。</summary>
        public string contractSubProfileId;
    }

    /// <summary>落选回响去向（§20.1）。三个落选 profile 各有且只有一条。</summary>
    [Serializable]
    public sealed class ModeHEchoAssignmentDto
    {
        /// <summary>落选 profile ID。</summary>
        public string profileId;
        /// <summary>return_enemy / transfer_candidate / removed。</summary>
        public string destinationId;
        /// <summary>计划登场场次（0 表示不适用）。</summary>
        public int scheduledMatchIndex;
        /// <summary>是否已消费。</summary>
        public bool resolved;
    }

    /// <summary>本场角色（§20.1）。starter/relay 只由两个 ID 决定。</summary>
    [Serializable]
    public sealed class ModeHMatchRosterDto
    {
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>本场先发 profile ID。</summary>
        public string matchStarterProfileId;
        /// <summary>本场唯一接力者 profile ID（空表示 Empty）。</summary>
        public string matchRelayProfileId;
        /// <summary>先发虚拟 kit ID（排序集合）。</summary>
        public List<string> starterKitIds;
        /// <summary>接力者虚拟 kit ID（排序集合）。</summary>
        public List<string> relayKitIds;
        /// <summary>整备摘要。</summary>
        public string loadoutDigest;
        /// <summary>当前场上 profile ID。</summary>
        public string activeProfileId;
        /// <summary>本场实际踏入擂台的 profile ID 集合。</summary>
        public List<string> enteredProfileIds;
        /// <summary>接力是否已消费。</summary>
        public bool relayConsumed;
    }

    /// <summary>虚拟整备套装定义（§20.1）。运行时从 JSON 读取，Season 只保存 kit ID。</summary>
    [Serializable]
    public sealed class ModeHLoadoutKitDto
    {
        /// <summary>套装稳定 ID。</summary>
        public string kitId;
        /// <summary>是否为新赛季默认解锁的 starter kit。</summary>
        public bool isStarterKit;
        /// <summary>starter 展示顺序。</summary>
        public int starterOrder;
        /// <summary>兼容的 profile ID 集合（空表示不限）。</summary>
        public List<string> compatibleProfileIds;
        /// <summary>兼容的原型 ID 集合。</summary>
        public List<string> compatibleArchetypeIds;
        /// <summary>替换的装备槽。</summary>
        public string replaceSlot;
        /// <summary>官方物品 typeId。</summary>
        public int typeId;
        /// <summary>原版 ItemMetaData.quality（1..8），唯一品质事实源。</summary>
        public int gameQuality;
        /// <summary>兼容弹药 typeId（0 表示不适用）。</summary>
        public int ammoTypeId;
        /// <summary>冻结弹药数量。</summary>
        public int ammoCount;
        /// <summary>公开克制标签。</summary>
        public List<string> publicTags;
        /// <summary>本条目内容签名。</summary>
        public string contentSignature;
    }

    /// <summary>敌军公开摘要（§17.5）。侦察结果写入后才显示首次报价。</summary>
    [Serializable]
    public sealed class ModeHPublicSummaryDto
    {
        /// <summary>公开人数区间下限。</summary>
        public int enemyCountMin;
        /// <summary>公开人数区间上限。</summary>
        public int enemyCountMax;
        /// <summary>主要战斗身份。</summary>
        public string primaryArchetypeId;
        /// <summary>进场节奏提示 ID。</summary>
        public string entryScriptId;
        /// <summary>擂台条件 ID。</summary>
        public string conditionId;
        /// <summary>是否有已公开的高威胁核心。</summary>
        public bool hasHighThreatCore;
        /// <summary>已公开的协同类别（heal/summon/control）。</summary>
        public List<string> synergyTags;
        /// <summary>已公开的带伤敌人数量。</summary>
        public int visibleWoundedEnemyCount;
        /// <summary>已公开的敌方异常 ID 集合。</summary>
        public List<string> visibleAnomalyIds;
        /// <summary>核心免疫/弱点的可公开部分。</summary>
        public List<string> coreTraitTags;
        /// <summary>侦察揭示的补充文本 key。</summary>
        public string reconRevealKey;
    }

    /// <summary>本场敌军计划（§20.1）。计划先于玩家整备冻结。</summary>
    [Serializable]
    public sealed class ModeHMatchPlanDto
    {
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>计划稳定 ID。</summary>
        public string planId;
        /// <summary>候选序号（审计失败时递增）。</summary>
        public int planCandidateIndex;
        /// <summary>技术重试序号。</summary>
        public int technicalRetrySequence;
        /// <summary>编制骨架 ID。</summary>
        public string skeletonId;
        /// <summary>进场剧本 ID。</summary>
        public string entryScriptId;
        /// <summary>擂台条件 ID。</summary>
        public string conditionId;
        /// <summary>敌军 stable key（保序，隐藏顺序）。</summary>
        public List<string> enemyStableKeys;
        /// <summary>每个敌军所属入场批次（与 enemyStableKeys 同序）。</summary>
        public List<int> enemyBatchIndices;
        /// <summary>公开摘要。</summary>
        public ModeHPublicSummaryDto publicSummary;
        /// <summary>玩家选择的侦察类别（空表示未侦察）。</summary>
        public string reconChoiceId;
        /// <summary>侦察结果文本 key。</summary>
        public string reconResult;
        /// <summary>本场总威胁预算。</summary>
        public int threatBudget;
        /// <summary>计划派生 seed。</summary>
        public long planSeed;
        /// <summary>第 4 场市场资格：最近击败的特殊敌军是否合格。</summary>
        public bool specialEnemyEligible;
        /// <summary>特殊敌军来源标签。</summary>
        public string specialEnemySourceTag;
        /// <summary>计划摘要，锁盘后只读。</summary>
        public string planDigest;
    }

    /// <summary>锁盘快照（§20.1）。LoadoutLocked 后整场只读。</summary>
    [Serializable]
    public sealed class ModeHLoadoutLockDto
    {
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>先发 profile ID。</summary>
        public string matchStarterProfileId;
        /// <summary>接力者 profile ID（可空）。</summary>
        public string matchRelayProfileId;
        /// <summary>先发 kit ID（排序集合）。</summary>
        public List<string> starterKitIds;
        /// <summary>接力者 kit ID（排序集合）。</summary>
        public List<string> relayKitIds;
        /// <summary>本场锁定的口令 ID。</summary>
        public string commandId;
        /// <summary>锁定赔率（1..5，净赔率）。</summary>
        public int lockedOdds;
        /// <summary>本场保留的虚拟下注额。</summary>
        public int reservedVirtualStake;
        /// <summary>计划 ID。</summary>
        public string planId;
        /// <summary>计划摘要。</summary>
        public string planDigest;
        /// <summary>整备摘要。</summary>
        public string loadoutDigest;
        /// <summary>锁定时的状态序号。</summary>
        public int lockedStateSequence;
        /// <summary>本场是否选择了真实押品（真实资产路径）。</summary>
        public bool realStakeSelected;
    }

    /// <summary>单次倒地事件（§20.1 战报子项）。</summary>
    [Serializable]
    public sealed class ModeHInjuryEventDto
    {
        /// <summary>倒地选手 profile ID。</summary>
        public string profileId;
        /// <summary>本次记录的伤病 ID（空表示已带伤直接退役）。</summary>
        public string injuryId;
        /// <summary>是否因本次倒地退役。</summary>
        public bool retired;
        /// <summary>事件序号。</summary>
        public int eventSequence;
        /// <summary>唯一倒地 token。</summary>
        public string downToken;
    }

    /// <summary>本场战报（§20.1）。完整虚拟奖励 operation 只保存在 Season 集合。</summary>
    [Serializable]
    public sealed class ModeHMatchReportDto
    {
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>唯一比赛结果身份。</summary>
        public string resultToken;
        /// <summary>ModeHMatchReportStatus 的整数值。</summary>
        public int reportStatus;
        /// <summary>ModeHMatchOutcome 的整数值。</summary>
        public int winner;
        /// <summary>是否 180 秒超时判负。</summary>
        public bool timeout;
        /// <summary>触发的胆怯类型（空表示未触发）。</summary>
        public string cowardiceType;
        /// <summary>本场是否触发 ERROR。</summary>
        public bool errorTriggered;
        /// <summary>本场实际登场的 profile ID 集合。</summary>
        public List<string> entrantIds;
        /// <summary>本场倒地事件。</summary>
        public List<ModeHInjuryEventDto> injuryEvents;
        /// <summary>战痕候选 ID（空表示无候选）。</summary>
        public string scarOfferId;
        /// <summary>锁定赔率。</summary>
        public int lockedOdds;
        /// <summary>下注前余额。</summary>
        public int virtualStakeBalanceBefore;
        /// <summary>下注额。</summary>
        public int virtualStakeAmount;
        /// <summary>胜利总返还（gross）。</summary>
        public int grossVirtualPayout;
        /// <summary>结算后余额。</summary>
        public int virtualStakeBalanceAfter;
        /// <summary>本场虚拟奖励 operation ID（必填）。</summary>
        public string seasonRewardOperationId;
        /// <summary>真实押品事务 ID（未选真实押品时为空）。</summary>
        public string stakeTxId;
        /// <summary>真实资产奖励 operation 镜像 ID（未选真实押品时为空）。</summary>
        public string stakeRewardOperationMirror;
        /// <summary>最终被击败敌军的资格快照（第 4 场市场来源）。</summary>
        public string finalDefeatedProfileSnapshot;
        /// <summary>特殊敌军是否合格。</summary>
        public bool specialEnemyEligible;
        /// <summary>特殊敌军来源标签。</summary>
        public string specialEnemySourceTag;
        /// <summary>本场消耗的拍铃与口令记录。</summary>
        public string consumedCommandId;
        /// <summary>拍铃是否被使用。</summary>
        public bool bellConsumed;
        /// <summary>本场耗时（秒）。</summary>
        public float elapsedSeconds;
    }

    /// <summary>转会 offer（§20.1）。</summary>
    [Serializable]
    public sealed class ModeHOfferDto
    {
        /// <summary>市场窗口所属场次。</summary>
        public int windowMatchIndex;
        /// <summary>offer 稳定 ID。</summary>
        public string offerId;
        /// <summary>被提供的 profile ID。</summary>
        public string profileId;
        /// <summary>来源：echo_transfer_candidate / defeated_special_enemy。</summary>
        public string source;
        /// <summary>过期状态序号。</summary>
        public int expiresAtStateSequence;
        /// <summary>ModeHOfferStatus 的整数值。</summary>
        public int status;
    }

    /// <summary>虚拟奖励 operation（§20.1）。不含 item、receipt 或库存字段。</summary>
    [Serializable]
    public sealed class ModeHSeasonRewardOperationDto
    {
        /// <summary>operation 稳定 ID。</summary>
        public string operationId;
        /// <summary>非空事件 token。</summary>
        public string eventTokenId;
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>比赛结果 token。</summary>
        public string resultToken;
        /// <summary>ModeHRewardKind 的整数值。</summary>
        public int rewardKind;
        /// <summary>稳定候选 kit ID（已冻结展示顺序，不二次重排）。</summary>
        public List<string> candidateKitIds;
        /// <summary>玩家选择的 kit ID（未选择为空）。</summary>
        public string selectedRewardKitId;
        /// <summary>名声目标 profile ID（FameDisplay 时使用）。</summary>
        public string rewardProfileId;
        /// <summary>锁定赔率。</summary>
        public int lockedOdds;
        /// <summary>下注额。</summary>
        public int virtualStakeAmount;
        /// <summary>胜利总返还。</summary>
        public int grossVirtualPayout;
        /// <summary>净收益。</summary>
        public int netVirtualProfit;
        /// <summary>ModeHSeasonRewardOperationStatus 的整数值。</summary>
        public int status;
    }

    /// <summary>赛前快照（§20.1）。开战前技术中止按该快照返还本场保留筹码。</summary>
    [Serializable]
    public sealed class ModeHPreMatchSnapshotDto
    {
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>主将合同快照。</summary>
        public ModeHProfileDto contractMainProfileSnapshot;
        /// <summary>替补合同快照（可空）。</summary>
        public ModeHProfileDto contractSubProfileSnapshot;
        /// <summary>先发 profile ID。</summary>
        public string matchStarterProfileId;
        /// <summary>接力者 profile ID。</summary>
        public string matchRelayProfileId;
        /// <summary>先发 kit ID。</summary>
        public List<string> starterKitIds;
        /// <summary>接力者 kit ID。</summary>
        public List<string> relayKitIds;
        /// <summary>整备摘要。</summary>
        public string loadoutDigest;
        /// <summary>锁定口令。</summary>
        public string commandId;
        /// <summary>锁定赔率。</summary>
        public int lockedOdds;
        /// <summary>保留前余额。</summary>
        public int virtualStakeBalanceBeforeReservation;
        /// <summary>本场保留额。</summary>
        public int reservedVirtualStake;
        /// <summary>公开摘要摘要值。</summary>
        public string publicSummaryDigest;
        /// <summary>捕获时状态序号。</summary>
        public int capturedStateSequence;
    }

    /// <summary>战场快照中的单名敌军（§20.1）。位置只存标量。</summary>
    [Serializable]
    public sealed class ModeHBattleEnemyStateDto
    {
        /// <summary>计划槽位序号。</summary>
        public int planSlotIndex;
        /// <summary>官方预设稳定 key。</summary>
        public string stableKey;
        /// <summary>当前生命比例。</summary>
        public float healthFraction;
        /// <summary>位置 X。</summary>
        public float positionX;
        /// <summary>位置 Y。</summary>
        public float positionY;
        /// <summary>位置 Z。</summary>
        public float positionZ;
        /// <summary>朝向 Y。</summary>
        public float rotationY;
    }

    /// <summary>战场快照中的我方上场者（§20.1）。</summary>
    [Serializable]
    public sealed class ModeHBattleEntrantStateDto
    {
        /// <summary>profile ID。</summary>
        public string profileId;
        /// <summary>当前生命比例。</summary>
        public float healthFraction;
        /// <summary>位置 X。</summary>
        public float positionX;
        /// <summary>位置 Y。</summary>
        public float positionY;
        /// <summary>位置 Z。</summary>
        public float positionZ;
        /// <summary>朝向 Y。</summary>
        public float rotationY;
        /// <summary>是否已是接力者。</summary>
        public bool isRelay;
    }

    /// <summary>战痕窗口剩余时间（§20.1）。</summary>
    [Serializable]
    public sealed class ModeHScarWindowStateDto
    {
        /// <summary>战痕 ID。</summary>
        public string scarId;
        /// <summary>窗口剩余秒数。</summary>
        public float remainingSeconds;
    }

    /// <summary>
    /// 战场快照（§17.4、§20.1）。作为 Season 可空根字段 currentBattleSnapshot 参与同一 canonical digest。
    /// 禁止 Unity 引用、InstanceID 与委托。
    /// </summary>
    [Serializable]
    public sealed class ModeHBattleSnapshotDto
    {
        /// <summary>schema 版本。</summary>
        public int schemaVersion;
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>技术重试序号。</summary>
        public int technicalRetrySequence;
        /// <summary>快照序号（每次采集递增）。</summary>
        public int snapshotSequence;
        /// <summary>快照摘要。</summary>
        public string snapshotDigest;
        /// <summary>本场已进行秒数。</summary>
        public float elapsedSeconds;
        /// <summary>当前入场批次序号。</summary>
        public int entryBatchIndex;
        /// <summary>尚未入场批次的 stable key（按计划顺序）。</summary>
        public List<string> pendingBatchStableKeys;
        /// <summary>场上敌军状态。</summary>
        public List<ModeHBattleEnemyStateDto> activeEnemies;
        /// <summary>我方上场者状态。</summary>
        public ModeHBattleEntrantStateDto entrant;
        /// <summary>拍铃是否已消费。</summary>
        public bool bellConsumed;
        /// <summary>当前生效口令 ID（空表示无）。</summary>
        public string activeCommandId;
        /// <summary>口令窗口剩余秒数。</summary>
        public float commandWindowRemainingSeconds;
        /// <summary>战痕窗口状态。</summary>
        public List<ModeHScarWindowStateDto> scarWindowStates;
        /// <summary>已完成的胆怯判定类型集合。</summary>
        public List<string> cowardCheckDone;
        /// <summary>ERROR 判定是否已消耗。</summary>
        public bool errorCheckDone;
        /// <summary>ERROR 互换是否生效中。</summary>
        public bool errorSwapActive;
        /// <summary>被互换的 profile ID。</summary>
        public string errorSwapProfileId;
        /// <summary>看台表演模式 ID。</summary>
        public string standInPatternId;
        /// <summary>已提交事件 token 集合，保证不被重放。</summary>
        public List<string> appliedEventTokenIds;
    }

    /// <summary>赛季运行状态（§20.1）。runtime owner token 只存在内存，不得持久化。</summary>
    [Serializable]
    public sealed class ModeHRunStateDto
    {
        /// <summary>schema 版本。</summary>
        public int schemaVersion;
        /// <summary>本次赛季运行 ID。</summary>
        public string runId;
        /// <summary>赛季随机种子。</summary>
        public long runSeed;
        /// <summary>ModeHLifecycle 的整数值。</summary>
        public int lifecycle;
        /// <summary>状态序号（每次合法转换递增）。</summary>
        public int stateSequence;
        /// <summary>目标场景名。</summary>
        public string sceneName;
        /// <summary>场景 generation。</summary>
        public int sceneGeneration;
        /// <summary>当前比赛编号（0 表示尚未开赛）。</summary>
        public int matchIndex;
        /// <summary>当前阶段 deadline（UTC "O"，空表示无）。</summary>
        public string phaseDeadlineUtc;
        /// <summary>同场技术重试序号。</summary>
        public int technicalRetrySequence;
        /// <summary>赛前快照摘要。</summary>
        public string preMatchSnapshotHash;
        /// <summary>故障源状态（ModeHLifecycle 整数值，0 表示无）。</summary>
        public int recoveryOriginalLifecycle;
        /// <summary>恢复目标状态（ModeHLifecycle 整数值，0 表示无）。</summary>
        public int recoveryResumeTarget;
    }

    /// <summary>名人堂跨 key 幂等命令（§20.1）。</summary>
    [Serializable]
    public sealed class ModeHHallOfFameCommandDto
    {
        /// <summary>名人堂记录稳定 ID。</summary>
        public string hallOfFameId;
        /// <summary>完整记录快照。</summary>
        public ModeHHallOfFameRecordDto recordSnapshot;
        /// <summary>记录摘要。</summary>
        public string recordDigest;
        /// <summary>ModeHHallOfFameCommandStatus 的整数值。</summary>
        public int status;
    }

    /// <summary>名人堂镜像（§20.1）。只读展示，不提供数值继承。</summary>
    [Serializable]
    public sealed class ModeHHallOfFameRecordDto
    {
        /// <summary>记录稳定 ID。</summary>
        public string hallOfFameId;
        /// <summary>schema 版本。</summary>
        public int schemaVersion;
        /// <summary>赛季版本。</summary>
        public int seasonVersion;
        /// <summary>冠军 profile 快照。</summary>
        public ModeHProfileDto championProfileSnapshot;
        /// <summary>外号本地化 key。</summary>
        public string aliasKey;
        /// <summary>原型 ID。</summary>
        public string archetypeId;
        /// <summary>底色 ID。</summary>
        public string temperamentId;
        /// <summary>怪癖 ID。</summary>
        public string quirkId;
        /// <summary>异常 ID。</summary>
        public string anomalyId;
        /// <summary>招牌口令 ID。</summary>
        public string signatureCommandId;
        /// <summary>最多三条战痕。</summary>
        public List<string> scarIds;
        /// <summary>六场战报引用。</summary>
        public List<string> matchReportIds;
        /// <summary>历任替补 profile ID。</summary>
        public List<string> substituteHistory;
        /// <summary>最高赔率胜利。</summary>
        public int maxOddsWin;
        /// <summary>最高虚拟筹码胜利。</summary>
        public int maxVirtualStakeWin;
        /// <summary>赛季结束虚拟筹码。</summary>
        public int finalVirtualStakeCredits;
        /// <summary>最高真实抵押胜利（未开启真实押品时为 0）。</summary>
        public int maxRealStakeWin;
        /// <summary>创建时间（UTC "O"）。</summary>
        public string createdUtc;
        /// <summary>来源 game 构建签名。</summary>
        public string gameBuildSignature;
        /// <summary>来源 mod 构建签名。</summary>
        public string modBuildSignature;
    }

    /// <summary>名人堂 envelope（§20.3）。跨赛季存在，同时承载生产认证缓存。</summary>
    [Serializable]
    public sealed class ModeHHallOfFameEnvelopeDto
    {
        /// <summary>schema 版本。</summary>
        public int schemaVersion;
        /// <summary>签名算法版本。</summary>
        public int signatureAlgorithmVersion;
        /// <summary>mod 构建签名。</summary>
        public string modBuildSignature;
        /// <summary>game 构建签名。</summary>
        public string gameBuildSignature;
        /// <summary>内容目录签名。</summary>
        public string contentCatalogSignature;
        /// <summary>envelope 摘要（排除自身字段后计算）。</summary>
        public string payloadDigest;
        /// <summary>名人堂记录（最多 32 条，按 hallOfFameId 去重）。</summary>
        public List<ModeHHallOfFameRecordDto> records;
        /// <summary>四签名生产认证缓存（可空）。</summary>
        public ModeHCertificationCacheDto productionCertificationCache;
    }

    #endregion

    #region 真实资产路径 DTO（§20.1、§22）

    /// <summary>根物品树快照（真实资产路径）。</summary>
    [Serializable]
    public sealed class ModeHItemTreeSnapshotDto
    {
        /// <summary>原仓库槽位。</summary>
        public int sourcePosition;
        /// <summary>语义摘要（树内 instance id 已规范化为局部序号）。</summary>
        public string semanticTreeDigest;
        /// <summary>规范化后的树载荷。</summary>
        public string normalizedTreePayload;
        /// <summary>原版 ItemMetaData.quality（1..8）。</summary>
        public int gameQuality;
        /// <summary>操作前出现次数。</summary>
        public int preCount;
        /// <summary>操作后出现次数。</summary>
        public int postCount;
    }

    /// <summary>逐项 receipt（真实资产路径）。</summary>
    [Serializable]
    public sealed class ModeHStakeReceiptDto
    {
        /// <summary>子操作唯一 ID。</summary>
        public string operationId;
        /// <summary>父操作 ID（奖励/清算子项必填）。</summary>
        public string parentOperationId;
        /// <summary>子项类别：Escrow / Loss / Reward / Return。</summary>
        public string kind;
        /// <summary>事件 token（奖励/清算子项必填）。</summary>
        public string eventTokenId;
        /// <summary>期望的操作前库存摘要。</summary>
        public string expectedBeforeDigest;
        /// <summary>期望的操作后库存摘要。</summary>
        public string expectedAfterDigest;
        /// <summary>ModeHStakeReceiptStatus 的整数值。</summary>
        public int status;
        /// <summary>receipt 摘要。</summary>
        public string receiptDigest;
    }

    /// <summary>单件真实物品结果（真实资产路径）。</summary>
    [Serializable]
    public sealed class ModeHRewardItemResultDto
    {
        /// <summary>子操作 ID。</summary>
        public string operationId;
        /// <summary>结果类别：Granted / Lost / Returned。</summary>
        public string resultKind;
        /// <summary>官方物品 typeId。</summary>
        public int typeId;
        /// <summary>原版品质。</summary>
        public int gameQuality;
        /// <summary>物品树快照。</summary>
        public ModeHItemTreeSnapshotDto itemSnapshot;
    }

    /// <summary>真实资产奖励父 operation（§20.1）。</summary>
    [Serializable]
    public sealed class ModeHRewardOperationDto
    {
        /// <summary>父操作唯一 ID。</summary>
        public string operationId;
        /// <summary>非空事件 token。</summary>
        public string eventTokenId;
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>比赛结果 token。</summary>
        public string resultToken;
        /// <summary>ModeHSettlementKind 的整数值（固定 MatchResult）。</summary>
        public int settlementKind;
        /// <summary>计划摘要。</summary>
        public string planDigest;
        /// <summary>逐件结果。</summary>
        public List<ModeHRewardItemResultDto> itemResults;
        /// <summary>ModeHRewardOperationStatus 的整数值。</summary>
        public int status;
        /// <summary>逐项 receipt。</summary>
        public List<ModeHStakeReceiptDto> receipts;
    }

    /// <summary>真实资产退款父 operation（§20.1）。</summary>
    [Serializable]
    public sealed class ModeHAbortReturnOperationDto
    {
        /// <summary>退款父操作唯一 ID。</summary>
        public string operationId;
        /// <summary>退款 token。</summary>
        public string abortReturnToken;
        /// <summary>专用退款事件 token（非空）。</summary>
        public string eventTokenId;
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>ModeHSettlementKind 的整数值（固定 AbortReturn）。</summary>
        public int settlementKind;
        /// <summary>计划摘要。</summary>
        public string planDigest;
        /// <summary>逐件结果。</summary>
        public List<ModeHRewardItemResultDto> itemResults;
        /// <summary>ModeHAbortReturnOperationStatus 的整数值。</summary>
        public int status;
        /// <summary>逐项 receipt。</summary>
        public List<ModeHStakeReceiptDto> receipts;
    }

    /// <summary>真实仓库抵押 journal（§20.1、§22.2）。单 slot 单 active journal。</summary>
    [Serializable]
    public sealed class ModeHStakeJournalDto
    {
        /// <summary>schema 版本。</summary>
        public int schemaVersion;
        /// <summary>签名算法版本。</summary>
        public int signatureAlgorithmVersion;
        /// <summary>mod 构建签名。</summary>
        public string modBuildSignature;
        /// <summary>envelope 摘要。</summary>
        public string payloadDigest;
        /// <summary>事务身份。</summary>
        public string txId;
        /// <summary>存档槽 ID。</summary>
        public int slotId;
        /// <summary>存档槽 generation。</summary>
        public int slotGeneration;
        /// <summary>赛季运行 ID。</summary>
        public string runId;
        /// <summary>比赛编号。</summary>
        public int matchIndex;
        /// <summary>ModeHStakePhase 的整数值。</summary>
        public int phase;
        /// <summary>阶段序号（单向递增）。</summary>
        public int phaseSequence;
        /// <summary>ModeHSettlementKind 的整数值。</summary>
        public int settlementKind;
        /// <summary>操作前库存摘要。</summary>
        public string inventoryPreDigest;
        /// <summary>操作后库存摘要。</summary>
        public string inventoryPostDigest;
        /// <summary>escrow 根物品。</summary>
        public List<ModeHItemTreeSnapshotDto> escrowItems;
        /// <summary>预冻结损失根物品。</summary>
        public List<ModeHItemTreeSnapshotDto> lossItems;
        /// <summary>奖励根物品。</summary>
        public List<ModeHItemTreeSnapshotDto> rewardItems;
        /// <summary>奖励父 operation（ResultCommitted 必填）。</summary>
        public ModeHRewardOperationDto rewardOperation;
        /// <summary>退款父 operation（AbortReturnCommitted 必填）。</summary>
        public ModeHAbortReturnOperationDto abortReturnOperation;
        /// <summary>比赛结果 token。</summary>
        public string resultToken;
        /// <summary>退款 token。</summary>
        public string abortReturnToken;
        /// <summary>逐项 receipt。</summary>
        public List<ModeHStakeReceiptDto> receipts;
    }

    #endregion

    #region Season 根 DTO

    /// <summary>
    /// Mode H 完整赛季 payload（§20.1）。
    /// 每次状态变化都以一个完整对象原子写入 BossRush_ModeH_Season_v1，
    /// 禁止把 report、profile、roster 或虚拟奖励拆成多次 Save。
    /// </summary>
    [Serializable]
    public sealed class ModeHSeasonDto
    {
        /// <summary>schema 版本。</summary>
        public int schemaVersion;
        /// <summary>签名算法版本。</summary>
        public int signatureAlgorithmVersion;
        /// <summary>mod 构建签名。</summary>
        public string modBuildSignature;
        /// <summary>game 构建签名。</summary>
        public string gameBuildSignature;
        /// <summary>内容目录签名。</summary>
        public string contentCatalogSignature;
        /// <summary>envelope 摘要（排除自身字段后计算）。</summary>
        public string payloadDigest;
        /// <summary>存档槽 generation。</summary>
        public int slotGeneration;
        /// <summary>随首份 Season 一次写入的认证快照。</summary>
        public ModeHProductionCertificationDto productionCertificationSnapshot;
        /// <summary>赛季运行状态。</summary>
        public ModeHRunStateDto runState;
        /// <summary>五席候选 profile ID（展示顺序）。</summary>
        public List<string> draftCandidateProfileIds;
        /// <summary>落选回响去向。</summary>
        public List<ModeHEchoAssignmentDto> echoAssignments;
        /// <summary>本季全部 profile（按 profileId 排序）。</summary>
        public List<ModeHProfileDto> profiles;
        /// <summary>合同角色。</summary>
        public ModeHContractDto contract;
        /// <summary>本场角色。</summary>
        public ModeHMatchRosterDto matchRoster;
        /// <summary>当前敌军计划（可空）。</summary>
        public ModeHMatchPlanDto currentMatchPlan;
        /// <summary>当前锁盘（可空）。</summary>
        public ModeHLoadoutLockDto currentLoadoutLock;
        /// <summary>赛前快照（可空）。</summary>
        public ModeHPreMatchSnapshotDto preMatchSnapshot;
        /// <summary>当前转会 offer（可空）。</summary>
        public ModeHOfferDto currentOffer;
        /// <summary>战场快照（可空）。</summary>
        public ModeHBattleSnapshotDto currentBattleSnapshot;
        /// <summary>虚拟筹码余额。</summary>
        public int virtualStakeCredits;
        /// <summary>本场保留的虚拟下注额。</summary>
        public int reservedVirtualStake;
        /// <summary>已解锁虚拟 kit ID 集合。</summary>
        public List<string> unlockedKitIds;
        /// <summary>虚拟奖励 operation 集合（按 operationId 排序）。</summary>
        public List<ModeHSeasonRewardOperationDto> seasonRewardOperations;
        /// <summary>战报集合（按 matchIndex 排序）。</summary>
        public List<ModeHMatchReportDto> matchReports;
        /// <summary>已应用事件 token 集合。</summary>
        public List<string> appliedEventTokenIds;
        /// <summary>名人堂 pending command（可空）。</summary>
        public ModeHHallOfFameCommandDto hallOfFameCommand;
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
