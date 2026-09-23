// ============================================================================
// PetNestTuning.cs - 遗种巢数值常量单点（实施计划 步骤 1）
// ============================================================================
// 归位依据 AGENTS.md 4.8「Config 三层归位」第 2 层：玩法强耦合常量放模块配置类。
// 只有入口总开关 petNestEnabled 走 Config/Config.cs + ModConfig（第 1 层），
// 其余数值一律在这里，owner 审定后单点改。
//
// 【全表数值均为草案，待 owner 审定】
//   来源：docs/设计提案/2026-08-28_养崽系统创意脑暴.md 附录 A §A.3。
//   每项都标了调参方向；改动只需改本文件，不影响任何结构。
// ============================================================================

namespace BossRush
{
    /// <summary>遗种巢数值常量。无状态、无逻辑，只有 const / static readonly。</summary>
    internal static class PetNestTuning
    {
        #region 掉落双轨（草案）

        /// <summary>遗种蛋直掉概率。调高 = 欧轨更甜，图鉴推进更快。</summary>
        internal const float EggDropChance = 0.04f;

        /// <summary>
        /// 遗魂掉落量除数：souls = round(MaxHealth / 该值)，至少 1。
        /// 镜像官方 SoulCollector 的 MaxHealth/15 公式口径，**不复用**官方灵魂方块 1165。
        /// </summary>
        internal const float SoulDropHealthDivisor = 15f;

        /// <summary>单次击杀遗魂下限。</summary>
        internal const int MinSoulDropPerKill = 1;

        /// <summary>凝蛋阈值：同血脉攒够该数量遗魂可定向凝成一枚蛋。调低 = 保底更快。</summary>
        internal const int SoulsPerCondensedEgg = 240;

        #endregion

        #region 孵化 roll（草案）

        /// <summary>
        /// 异色概率。纯收集荣誉，不给数值。
        /// owner 2026-09-20：「异色极为稀有」——从 1.5% 压到 0.4%（约 250 枚蛋出一只），
        /// 同时新增更常见的炫彩档位补上「开蛋有惊喜」的密度。
        /// </summary>
        internal const float ShinyChance = 0.004f;

        /// <summary>
        /// 炫彩概率（任意两色搭配，45 种）。比异色常见得多，但仍是少数派：
        /// 每 10 枚蛋里约 1 只带色，既有收集面又不会让普通崽显得寒酸。
        /// 与异色**独立** roll：同时命中就是异色的炫彩崽。
        /// </summary>
        internal const float ChromaChance = 0.10f;

        /// <summary>
        /// 炫彩保底：连续孵出这么多枚都不是炫彩，这一枚必定是炫彩（颜色仍随机）。
        /// owner 2026-09-22：「开了十几个蛋了都没有炫彩/异色，你应该弄个保底机制吧」。
        /// 10% 的自然概率不变，保底只兜住运气最差的那一段；实际平均约 6.5 枚一只。
        /// </summary>
        internal const int ChromaPityHatches = 10;

        /// <summary>
        /// 异色保底：连续这么多枚都不是异色，这一枚必定是异色。
        /// 自然概率 0.4% 不变，「极为稀有」的定位不动，只保证肝到头一定有；实际平均约 80 枚一只。
        /// </summary>
        internal const int ShinyPityHatches = 100;

        /// <summary>出身天赋条数（孵化即锁定）。</summary>
        internal const int TalentRollCount = 2;

        /// <summary>性格条数（孵化即锁定）。</summary>
        internal const int PersonalityRollCount = 1;

        #endregion

        #region 巢与存档体积（草案）

        /// <summary>巢初始容量。</summary>
        internal const int DefaultNestCapacity = 12;

        /// <summary>里程碑单次提升的容量。</summary>
        internal const int NestCapacityMilestoneStep = 4;

        /// <summary>巢容量上限。</summary>
        internal const int MaxNestCapacity = 24;

        /// <summary>
        /// 巢容量里程碑：图鉴解锁血脉数达到各阈值时各 +NestCapacityMilestoneStep。
        /// 与驯养成就同一口径（PetNestMuseumStats.UnlockedLineageCount），
        /// 12 → 16 → 20 → 24 正好用满 MaxNestCapacity。
        /// </summary>
        internal static readonly int[] NestCapacityMilestoneLineageCounts = { 10, 20, 30 };

        /// <summary>放生一只崽返还的同血脉遗魂数（凝一枚蛋需 SoulsPerCondensedEgg）。</summary>
        internal const int ReleaseSoulRefund = 60;

        #endregion

        #region 养成（等级与经验）

        /// <summary>崽的等级上限。到顶即「成年体」，不再成长。</summary>
        internal const int PetMaxLevel = 10;

        /// <summary>每级所需经验（线性曲线，不做阶梯）。</summary>
        internal const int PetExpPerLevel = 100;

        /// <summary>带崽进局并活着回巢的经验（重伤退场也算：战痕已经是惩罚了）。</summary>
        internal const int PetExpHomecoming = 10;

        /// <summary>随从每次击杀的经验。</summary>
        internal const int PetExpPerCompanionKill = 2;

        /// <summary>单局击杀经验上限，避免刷小怪把等级冲满。</summary>
        internal const int PetExpCompanionKillRunCap = 30;

        /// <summary>每多少级给玩家 +1 格捡漏背包（PetCapcity）。</summary>
        internal const int PetLevelsPerCapacityBonus = 3;

        /// <summary>
        /// 每级额外生命（小数口径，0.06 = +6%；作用在角色 Item 的 MaxHealth 上）。
        ///
        /// 在此之前，Lv1 与 Lv10 的崽进局后属性一模一样——等级的唯一回报是每 3 级 +1 格
        /// 捡漏背包，而那还要先借到官方宠物席位才生效。养 20 局看不出差别，
        /// "养成"这条主循环等于不存在。
        /// 复用 PetNestCompanionSpawner.ApplyPetModifiers 的同一条 Modifier 管线，
        /// 不新增系统、不占存档字段。置 0 即回到旧行为。
        /// </summary>
        internal const float PetLevelMaxHealthBonusPerLevel = 0.06f;

        /// <summary>
        /// 每级额外伤害（小数口径，作用在 GunDamageMultiplier / MeleeDamageMultiplier 上）。
        /// Lv10 = +45%，即基准 0.06 提到约 0.087，仍然远低于玩家，
        /// 守住设计稿「锦上添花不改天换地」。置 0 即回到旧行为。
        /// </summary>
        internal const float PetLevelDamageBonusPerLevel = 0.05f;

        /// <summary>每崽战痕上限。溢出后最旧的合并进 mergedOldScarCount，防存档膨胀。</summary>
        internal const int MaxScarsPerPet = 8;

        /// <summary>纪念碑上限。溢出后最旧的转为"碑林"计数。</summary>
        internal const int MaxMemorialEntries = 64;

        #endregion

        #region 随从进局（草案）

        /// <summary>
        /// 随从 DPS 目标占比。伤害归一系数由此反推：
        /// "锦上添花不改天换地"，价值在牵制、补刀和陪伴。
        /// </summary>
        internal const float CompanionDpsShareTarget = 0.06f;

        /// <summary>幼体视觉缩放基准（只作用于 modelRoot，不动碰撞体）。</summary>
        internal const float DefaultCubModelScale = 0.4f;

        /// <summary>
        /// 战痕永久 Modifier 单条数值。
        ///
        /// **单位是小数不是百分数**：官方 ModifierType.PercentageAdd 走的是
        /// `result *= Mathf.Max(0f, 1f + 累加值)`（鸭科夫源码/ItemStatsSystem/Stat.cs:263），
        /// 本仓同口径（Common/Stats/RuntimeStatModifierTracker.cs:31「0.30 = +30%」）。
        /// 写成 -2f 会变成 -200% 而不是 -2%，展示层用 FormatModifierPercent 补 ×100。
        /// </summary>
        internal const float ScarModifierFraction = -0.02f;

        /// <summary>同一 stat 的战痕减益叠加封顶（小数口径，见上）。</summary>
        internal const float ScarModifierCapFraction = -0.10f;

        /// <summary>重伤退场前的短无敌时长（秒），避免钳血后同帧被连击打死。</summary>
        internal const float DownedInvincibleSeconds = 1.5f;

        /// <summary>崽名字符上限（存档体积与 UI 宽度双重考虑）。</summary>
        internal const int MaxPetNameLength = 12;

        /// <summary>随从进局提供的额外背包格子（挂在玩家 PetCapcity 上）。</summary>
        internal const int CompanionPetCapacityBonus = 4;

        #endregion

        #region 天灾远征（草案）

        // owner 2026-09-20 拍板的时长梯度：10 / 30 / 60 分钟（现实时间）。
        // 旧值是 2 / 4 / 8 小时，一天只能跑两轮，节奏太慢。
        /// <summary>平安档时长（小时，现实时间）= 10 分钟。</summary>
        internal const double ExpeditionHoursSafe = 10d / 60d;
        /// <summary>风浪档时长（小时，现实时间）= 30 分钟。</summary>
        internal const double ExpeditionHoursRough = 30d / 60d;
        /// <summary>亡命档时长（小时，现实时间）= 60 分钟。</summary>
        internal const double ExpeditionHoursDesperate = 60d / 60d;

        /// <summary>平安档死亡率。绝对安全，最多空手。</summary>
        internal const float DeathRateSafe = 0f;
        /// <summary>风浪档死亡率。**出发前必须明示**。</summary>
        internal const float DeathRateRough = 0.06f;
        /// <summary>亡命档死亡率（原 0.22f，降低以使期望收益为正）。**出发前必须明示**。</summary>
        internal const float DeathRateDesperate = 0.12f;

        /// <summary>平安档基础成功率。</summary>
        internal const float SuccessRateSafe = 0.55f;
        /// <summary>风浪档基础成功率。</summary>
        internal const float SuccessRateRough = 0.7f;
        /// <summary>亡命档基础成功率。</summary>
        internal const float SuccessRateDesperate = 0.8f;

        /// <summary>血脉元素与目的地匹配时的成功率加成（绝对百分点）。</summary>
        internal const float ElementAffinityBonus = 0.12f;

        /// <summary>
        /// 发奖尝试次数上限。**只用于诊断退避与日志**：连续这么多次仍未发全时打一条
        /// [ERROR]，欠账照旧保留到成功为止，绝不因为次数到顶就置 rewardsGranted
        /// （`tests/GameplayReliabilityPersistenceGuard.py` 正是断言这一点）。
        /// 每次回基地至多消耗两次（模块扫一次 + 翻牌前补发一次）。
        /// </summary>
        internal const int MaxRewardGrantAttempts = 6;

        /// <summary>风浪档负伤概率（未阵亡时）。</summary>
        internal const float InjuryRateRough = 0.25f;
        /// <summary>亡命档负伤概率（未阵亡时）。</summary>
        internal const float InjuryRateDesperate = 0.45f;

        /// <summary>平安档远征经验（原全档统一 25，现改为梯度以补偿高难度风险）。</summary>
        internal const int PetExpExpeditionSurviveSafe = 15;
        /// <summary>风浪档远征经验。</summary>
        internal const int PetExpExpeditionSurviveRough = 30;
        /// <summary>亡命档远征经验（60 exp 可部分补偿 12% 死亡风险下的期望损失）。</summary>
        internal const int PetExpExpeditionSurviveDesperate = 60;

        /// <summary>平安档远征成功带回的同血脉遗魂。</summary>
        internal const int ExpeditionSoulRewardSafe = 15;
        /// <summary>风浪档远征成功带回的同血脉遗魂。</summary>
        internal const int ExpeditionSoulRewardRough = 40;
        /// <summary>亡命档远征成功带回的同血脉遗魂。</summary>
        internal const int ExpeditionSoulRewardDesperate = 90;

        /// <summary>亡命档成功后额外掉一枚遗种蛋的概率。</summary>
        internal const float ExpeditionRelicEggChanceDesperate = 0.2f;

        /// <summary>
        /// 远征战利品的品质与件数（0 件 = 该档只给现金与遗魂）。
        ///
        /// 候选来自 Common/Loot/BossRushQualityItemPool（官方全表按品质随机 + 掉落黑名单，
        /// 与《鸭科夫日报》签到奖励同一口径、同一份缓存），因此不新增也不猜物品 ID。
        /// 平安档保持「最多空手」的定位不给物；想关掉整张表把件数置 0 即可。
        /// </summary>
        internal const int ExpeditionLootQualitySafe = 0;
        internal const int ExpeditionLootCountSafe = 0;
        internal const int ExpeditionLootQualityRough = 3;
        internal const int ExpeditionLootCountRough = 1;
        internal const int ExpeditionLootQualityDesperate = 4;
        internal const int ExpeditionLootCountDesperate = 1;

        #endregion

        #region 基地展示与性能

        /// <summary>基地闲逛崽上限。分帧生成，超出不显示。</summary>
        internal const int MaxBaseIdleCompanions = 3;

        /// <summary>基地闲逛崽分帧生成间隔（秒）。</summary>
        internal const float BaseIdleSpawnIntervalSeconds = 0.35f;

        /// <summary>HUD 刷新节流间隔（秒，4Hz）。</summary>
        internal const float HudRefreshIntervalSeconds = 0.25f;

        #endregion

        #region 存档 key（v1 冻结，只增不改）

        /// <summary>巢：崽列表 / 出战席位 / 遗魂账本 / 巢容量。</summary>
        internal const string NestStorageKey = "BossRush_PetNest_Nest_v1";

        /// <summary>远征：进行中 + 已结算未翻牌。</summary>
        internal const string ExpeditionStorageKey = "BossRush_PetNest_Expedition_v1";

        /// <summary>博物馆：图鉴统计 / 纪念碑 / 异色收集。</summary>
        internal const string MuseumStorageKey = "BossRush_PetNest_Museum_v1";

        /// <summary>v2 单包权威键；三个 v1 key 永久保留为只读迁移输入。</summary>
        internal const string BundleStorageKey = "BossRush_PetNest_Bundle_v2";

        internal const int BundleSchemaVersion = 2;

        /// <summary>当前 schema 版本。高版本 fail-closed 只读，不覆盖。</summary>
        internal const int CurrentSchemaVersion = 1;

        #endregion

        #region 本地化与身份前缀

        /// <summary>本地化 key 前缀（唯一入口 Localization/PetNestLocalization.cs）。</summary>
        internal const string LocalizationPrefix = "BossRush_PetNest_";

        /// <summary>遗种蛋物品上记录血脉的 KV 键（随 ItemTreeData 持久化）。</summary>
        internal const string EggLineageVariableKey = "PetNest_Lineage";

        /// <summary>模块名（运行时模块 host 日志与 owner label）。</summary>
        internal const string ModuleName = "PetNest";

        #endregion

        #region 远征目的地 id（稳定标识，禁止改名）

        /// <summary>风暴海域：雷暴，electricity。</summary>
        internal const string DestinationStormSea = "storm_sea";
        /// <summary>酸雨废墟：毒雨，poison。</summary>
        internal const string DestinationAcidRuins = "acid_ruins";
        /// <summary>极寒荒原：暴雪，ice。</summary>
        internal const string DestinationFrozenWaste = "frozen_waste";

        #endregion

        #region 性格 id（稳定标识，禁止改名）

        /// <summary>莽撞：贴身缠斗。</summary>
        internal const string PersonalityReckless = "reckless";
        /// <summary>谨慎：拉开距离。</summary>
        internal const string PersonalityCautious = "cautious";
        /// <summary>懒散：攻击欲低但背包大。</summary>
        internal const string PersonalityLazy = "lazy";
        /// <summary>忠诚：贴着主人不乱跑。</summary>
        internal const string PersonalityLoyal = "loyal";

        /// <summary>全部性格 id（roll 用）。</summary>
        internal static readonly string[] AllPersonalityIds =
        {
            PersonalityReckless,
            PersonalityCautious,
            PersonalityLazy,
            PersonalityLoyal,
        };

        #endregion
    }
}
