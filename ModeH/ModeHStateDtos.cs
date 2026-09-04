// Mode H 持久化 DTO（设计提案 §20.1）。
// 与 ModeHStateModel.cs 同属一个数据契约：那边放枚举、稳定 ID 与派生映射，
// 这里只放 [Serializable] DTO，拆分只为遵守仓库单文件 1200 行预算。
using System;
using System.Collections.Generic;

namespace BossRush
{
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
        /// <summary>
        /// 条目稳定 ID。**不只是口令**：伤病与战痕的条目 ID 也走这个字段
        /// （BuildCommandStatuses 三族同表写入，RestoreCertificationEffects 用
        /// GetBehaviorEffectIds 反查）。字段名保留 commandId 是因为它进 canonical digest，
        /// 改名会让所有已存赛季与名人堂信封的 VerifyDigest 失败。
        /// </summary>
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
}
