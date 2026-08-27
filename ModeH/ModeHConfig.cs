using System;

namespace BossRush
{
    /// <summary>
    /// Mode H（百战留痕：黑市鸭王杯）玩法强耦合常量。
    ///
    /// 归位依据 AGENTS.md 4.8 与设计提案 §24.1：
    /// - 本类只放与代码逻辑强耦合、不允许玩家运行时调整的冻结常量；
    /// - 唯一可写入口开关是 BossRushConfig.modeHEnabled（§24.1），本类禁止声明任何可写 Enabled；
    /// - Boss 档案、敌军计划组合表、口令倍率、战痕、赔率权重属于 Assets/Data/ModeH/*.json。
    ///
    /// 冻结来源：设计提案 §17.1-§17.8、§19.3、§22.2、§24.2、§29.1。
    /// </summary>
    public static class ModeHConfig
    {
        #region 模式身份

        /// <summary>内部模式名（日志、owner label、遥测前缀统一使用）。</summary>
        public const string ModeName = "ModeH";

        /// <summary>本地化 key 前缀（§23.2 冻结）。</summary>
        public const string LocalizationKeyPrefix = "BossRush_ModeH_";

        #endregion

        #region 赛季与比赛（§17.4）

        /// <summary>三幕六战，第 6 场即冠军赛，不追加第 7 场。</summary>
        public const int SeasonMatchCount = 6;

        /// <summary>比赛编号下界（1..6）。</summary>
        public const int FirstMatchIndex = 1;

        /// <summary>单场最长 180 秒，到时判玩家失败。</summary>
        public const float MatchDurationSeconds = 180f;

        /// <summary>开放败者市场的场次（第 2、4 场结算后各一次）。</summary>
        public const int FirstTransferWindowMatchIndex = 2;

        /// <summary>第二次败者市场窗口。</summary>
        public const int SecondTransferWindowMatchIndex = 4;

        /// <summary>落选回响“回场签”固定作为第 5 场敌军核心登场。</summary>
        public const int EchoReturnMatchIndex = 5;

        /// <summary>同场自动重试上限；连续两次仍失败转 Suspended，绝不判负。</summary>
        public const int MaxAutomaticTechnicalRetriesPerMatch = 2;

        #endregion

        #region 试棚与合同（§17.2、§17.3）

        /// <summary>每季一次五席试棚。</summary>
        public const int DraftCandidateCount = 5;

        /// <summary>五席中最多一名稀有异常。</summary>
        public const int MaxAnomalyCandidatesInDraft = 1;

        /// <summary>五席中至少两名稳定型底色。</summary>
        public const int MinStableTemperamentCandidatesInDraft = 2;

        /// <summary>签约后落选三人各翻一张去向牌。</summary>
        public const int EchoAssignmentCount = 3;

        #endregion

        #region 生产目录与认证（§17.2）

        /// <summary>签名生产目录下限：5 个候选席 + 至少 3 个敌军/回响备选。</summary>
        public const int MinProductionCandidateCount = 8;

        /// <summary>签名生产目录上限：把首次正式自检限制在 180 秒内。</summary>
        public const int MaxProductionCandidateCount = 12;

        /// <summary>逐 stable key 认证上限。</summary>
        public const float CertificationPerKeyTimeoutSeconds = 15f;

        /// <summary>全池认证上限（12 key × 15 秒）。</summary>
        public const float CertificationPoolTimeoutSeconds = 180f;

        /// <summary>每个生产 key 至少需要可用的通用口令条数（VerifiedBehavior 或 PartiallyVerified）。</summary>
        public const int MinUsableCommonCommandsPerKey = 3;

        /// <summary>每个生产 key 至少需要可用的伤病条数。</summary>
        public const int MinUsableInjuriesPerKey = 3;

        /// <summary>五席必须覆盖的公开原型数量。</summary>
        public const int RequiredArchetypeCoverage = 5;

        #endregion

        #region 生成与同屏上限（§19.3、§24.2）

        /// <summary>擂台上最多同时存在的敌军临时实例。</summary>
        public const int MaxConcurrentEnemyInstances = 3;

        /// <summary>擂台上最多同时存在的参赛选手临时实例（我方常规同时在场始终为一）。</summary>
        public const int MaxConcurrentFighterInstances = 1;

        /// <summary>每帧最多创建一个角色。</summary>
        public const int MaxSpawnPerFrame = 1;

        /// <summary>敌军计划候选连续失败上限，超过即 TechnicalAbort。</summary>
        public const int MaxPlanCandidateAttempts = 8;

        #endregion

        #region 威胁走廊（§17.5 冻结表）

        /// <summary>第 1..6 场总威胁预算（索引 0 对应第 1 场）。</summary>
        public static readonly int[] MatchThreatBudgets = new int[] { 100, 115, 130, 145, 165, 190 };

        /// <summary>第 1..6 场同时在场上限（索引 0 对应第 1 场）。</summary>
        public static readonly int[] MatchSimultaneousEnemyCaps = new int[] { 2, 2, 3, 3, 3, 3 };

        /// <summary>多单位行动数量系数：1 + 0.20 * (count - 1)。</summary>
        public const float ThreatActionCountCoefficient = 0.20f;

        /// <summary>组合协同溢价上限（10%）。</summary>
        public const float ThreatSynergyPremiumCap = 0.10f;

        /// <summary>计划允许超出预算的相对容差（5%）。</summary>
        public const float ThreatBudgetTolerance = 0.05f;

        #endregion

        #region 赔率与虚拟筹码（§17.5 冻结）

        /// <summary>公开分差阈值：publicEdge 大于等于 20 为 x1。</summary>
        public const int OddsThresholdX1MinEdge = 20;

        /// <summary>公开分差阈值：publicEdge 在 [5, 19] 为 x2。</summary>
        public const int OddsThresholdX2MinEdge = 5;

        /// <summary>公开分差阈值：publicEdge 在 [-9, 4] 为 x3。</summary>
        public const int OddsThresholdX3MinEdge = -9;

        /// <summary>公开分差阈值：publicEdge 在 [-24, -10] 为 x4；小于等于 -25 为 x5。</summary>
        public const int OddsThresholdX4MinEdge = -24;

        /// <summary>赔率下界（净赔率，x1 即一赔一）。</summary>
        public const int MinOdds = 1;

        /// <summary>赔率上界。</summary>
        public const int MaxOdds = 5;

        /// <summary>每季初始虚拟筹码。</summary>
        public const int InitialVirtualStakeCredits = 6;

        /// <summary>虚拟筹码余额上限（clamp）。</summary>
        public const int MaxVirtualStakeCredits = 30;

        /// <summary>每场下注额上限（实际上限还受当前余额约束）。</summary>
        public const int MaxVirtualStakePerMatch = 2;

        /// <summary>奖励候选数上限（1 + min(2, floor(max(0, net) / 2))）。</summary>
        public const int MaxRewardCandidateCount = 3;

        /// <summary>奖励候选数公式的净收益除数。</summary>
        public const int RewardCandidateNetDivisor = 2;

        /// <summary>触发战痕候选的最低冷门赔率（x3+）。</summary>
        public const int ScarOfferMinOdds = 3;

        #endregion

        #region 口令（§17.6.1 冻结实现模型）

        /// <summary>口令窗口时长。</summary>
        public const float CommandWindowSeconds = 6f;

        /// <summary>
        /// 窗口内字段调制重申间隔。必须严格高于行为树 TraceTarget 的 0.15 秒重发频率，
        /// 否则一次性写值会在口令唯一有意义的时刻被抹掉（§17.6.1）。
        /// </summary>
        public const float CommandReassertIntervalSeconds = 0.1f;

        /// <summary>每场唯一一次拍铃。</summary>
        public const int BellUsesPerMatch = 1;

        #endregion

        #region 异常：胆怯与 ERROR（§17.6.4、§17.6.5）

        /// <summary>见血胆怯：首次进入 35% 最大生命以下判定一次。</summary>
        public const float CowardBloodHealthFraction = 0.35f;

        /// <summary>见血胆怯基础逃跑概率。</summary>
        public const float CowardBloodBaseChance = 0.25f;

        /// <summary>惧众胆怯：敌方同时在场首次达到 3。</summary>
        public const int CowardCrowdEnemyThreshold = 3;

        /// <summary>惧众胆怯基础逃跑概率。</summary>
        public const float CowardCrowdBaseChance = 0.30f;

        /// <summary>畏强胆怯：高威胁核心首次进入并存活 5 秒。</summary>
        public const float CowardStrongCoreSurvivalSeconds = 5f;

        /// <summary>畏强胆怯基础逃跑概率。</summary>
        public const float CowardStrongBaseChance = 0.20f;

        /// <summary>提前拍铃“稳住”后的逃跑概率乘数（Mode H 自结算，恒生效）。</summary>
        public const float CowardSteadyMitigationMultiplier = 0.75f;

        /// <summary>ERROR 单次判定概率。</summary>
        public const float ErrorTriggerChance = 0.08f;

        /// <summary>ERROR 控制切换 deadline，不是可重复抽签窗口。</summary>
        public const float ErrorControlSwitchDeadlineSeconds = 2f;

        /// <summary>看台表演半径（以 modeHSpectatorPos 为圆心）。</summary>
        public const float StandInRadiusMeters = 3.0f;

        /// <summary>看台表演重设目标点间隔。</summary>
        public const float StandInRepathIntervalSeconds = 0.5f;

        /// <summary>看台表演越界倍数：超过半径乘该值立即拉回圆心。</summary>
        public const float StandInLeashMultiplier = 1.5f;

        /// <summary>看台表演连续失败上限，达到即停止表演并让身体静止（不升级为技术中止）。</summary>
        public const int StandInMaxConsecutiveFailures = 3;

        #endregion

        #region 战场快照（§17.4）

        /// <summary>周期采集间隔；另外三类采集时点为批次入场完成、拍铃提交后、倒地/接力提交后。</summary>
        public const float BattleSnapshotIntervalSeconds = 10f;

        #endregion

        #region 伤病、战痕与名声（§17.4）

        /// <summary>单名选手最多保留的战痕条数。</summary>
        public const int MaxScarsPerProfile = 3;

        /// <summary>每场最多提供一条战痕候选。</summary>
        public const int MaxScarOffersPerMatch = 1;

        /// <summary>拒绝战痕换取的稳定名声增量。</summary>
        public const int ScarDeclineFameGain = 1;

        /// <summary>名声展示上限。</summary>
        public const int MaxFameDisplayCount = 99;

        /// <summary>old_wound 伤病的触发生命比例。</summary>
        public const float OldWoundTriggerHealthFraction = 0.35f;

        /// <summary>spirit 伤病触发所需的敌方同时在场数量。</summary>
        public const int SpiritInjuryEnemyThreshold = 2;

        /// <summary>spirit 伤病对口令调制幅度的乘数。</summary>
        public const float SpiritInjuryCommandScale = 0.85f;

        /// <summary>specialKillTag last_stand 的自身生命比例阈值。</summary>
        public const float LastStandHealthFraction = 0.20f;

        /// <summary>战痕窗口使用的“敌人危险血量”比例。</summary>
        public const float EnemyLowHealthFraction = 0.35f;

        #endregion

        #region 整备（§17.7）

        /// <summary>每名选手最多选择的 kit 数量（槽位互不冲突）。</summary>
        public const int MaxKitsPerFighter = 4;

        /// <summary>每个生产原型至少需要的 starter kit 数量（槽位不冲突）。</summary>
        public const int MinStarterKitsPerArchetype = 2;

        /// <summary>原版物品品质下界（ItemMetaData.quality，Q1-Q8），唯一事实源。</summary>
        public const int MinGameQuality = 1;

        /// <summary>原版物品品质上界。</summary>
        public const int MaxGameQuality = 8;

        #endregion

        #region 名人堂（§17.8）

        /// <summary>名人堂镜像最多保留条数，插入第 33 条时按 (createdUtc, hallOfFameId) 删除最旧一条。</summary>
        public const int MaxHallOfFameRecords = 32;

        #endregion

        #region 存档键与 schema（§20.3）

        /// <summary>完整赛季 payload。</summary>
        public const string SeasonStorageKey = "BossRush_ModeH_Season_v1";

        /// <summary>名人堂 + 生产认证缓存 envelope。</summary>
        public const string HallOfFameStorageKey = "BossRush_ModeH_HallOfFame_v1";

        /// <summary>真实资产 journal（真实资产路径；无押品时不创建 active 记录）。</summary>
        public const string StakeJournalStorageKey = "BossRush_ModeH_StakeJournal_v1";

        /// <summary>当前 payload schema 版本（v1）。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>签名算法版本（SHA-256 + 规范 JSON，§20.2）。</summary>
        public const int CurrentSignatureAlgorithmVersion = 1;

        /// <summary>生产认证记录 schema 版本。</summary>
        public const int CurrentCertificationSchemaVersion = 1;

        #endregion

        #region 数据文件（§23.2）

        /// <summary>Assets/Data 下的 Mode H 子目录名。</summary>
        public const string DataSubDirectoryName = "ModeH";

        /// <summary>Boss 档案与生产目录。</summary>
        public const string BossProfilesFileName = "BossProfiles.json";

        /// <summary>口令定义与控制点倍率。</summary>
        public const string CommandsFileName = "Commands.json";

        /// <summary>逐 effect 兼容矩阵。</summary>
        public const string CommandCompatibilityFileName = "CommandCompatibility.json";

        /// <summary>虚拟整备套装。</summary>
        public const string LoadoutKitsFileName = "LoadoutKits.json";

        /// <summary>敌军三层剧本与威胁评分。</summary>
        public const string ThreatPlansFileName = "ThreatPlans.json";

        /// <summary>战痕定义。</summary>
        public const string ScarsFileName = "Scars.json";

        /// <summary>赔率权重表。</summary>
        public const string OddsWeightsFileName = "OddsWeights.json";

        /// <summary>
        /// contentCatalogSignature 的规范相对路径集合（按 ordinal 升序参与摘要，§20.2）。
        /// </summary>
        public static readonly string[] RequiredDataFileNames = new string[]
        {
            BossProfilesFileName,
            CommandCompatibilityFileName,
            CommandsFileName,
            LoadoutKitsFileName,
            OddsWeightsFileName,
            ScarsFileName,
            ThreatPlansFileName
        };

        #endregion

        #region 性能预算（§24.2）

        /// <summary>HUD 刷新节流上限（最多 4 Hz）。</summary>
        public const float HudRefreshIntervalSeconds = 0.25f;

        /// <summary>进入后预热与点位校验的目标完成时间。</summary>
        public const float WarmupTargetSeconds = 1f;

        /// <summary>诊断/异常日志限频间隔。</summary>
        public const float DiagnosticLogIntervalSeconds = 5f;

        #endregion

        #region 只读派生

        /// <summary>取指定场次（1..6）的总威胁预算；越界 fail-closed 返回 0。</summary>
        public static int GetThreatBudget(int matchIndex)
        {
            if (matchIndex < FirstMatchIndex || matchIndex > SeasonMatchCount) return 0;
            return MatchThreatBudgets[matchIndex - FirstMatchIndex];
        }

        /// <summary>取指定场次（1..6）的同时在场上限；越界 fail-closed 返回 0。</summary>
        public static int GetSimultaneousEnemyCap(int matchIndex)
        {
            if (matchIndex < FirstMatchIndex || matchIndex > SeasonMatchCount) return 0;
            return MatchSimultaneousEnemyCaps[matchIndex - FirstMatchIndex];
        }

        /// <summary>该场次结算后是否开启一次败者市场。</summary>
        public static bool IsTransferWindowMatch(int matchIndex)
        {
            return matchIndex == FirstTransferWindowMatchIndex || matchIndex == SecondTransferWindowMatchIndex;
        }

        #endregion
    }
}
