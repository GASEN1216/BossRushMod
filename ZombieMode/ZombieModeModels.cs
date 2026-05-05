using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using UnityEngine;

namespace BossRush
{
    public enum ZombieModeLifecyclePhase
    {
        None,
        SelectingMap,
        Prechecking,
        CommittingResources,
        LoadingMap,
        InitializingRun,
        WaitingStarterChoice,
        Active,
        Exiting
    }

    public enum ZombieModeCombatPhase
    {
        None,
        InitialPreparation,
        Combat,
        Settling,
        RewardSelection,
        Preparation,
        ExtractionOpportunity,
        SuccessExit,
        FailedExit
    }

    public enum ZombieModeFailureReason
    {
        None,
        InvitationMissing,
        NotEnoughCash,
        NoEffectiveSpawnPoints,
        StorageFull,
        BlockedTaskOrBoundItems,
        AnotherBossRushLikeModeActive,
        InvitationConsumeFailed,
        CashWithdrawFailed,
        InventoryTransferFailed,
        MapLoadFailed,
        MapIsolationFailed,
        SpawnPointCollectionFailed,
        BeaconGrantFailed,
        InitializationFailed,
        StarterChoiceUiClosed,
        StarterChoiceTimedOut,
        StarterLoadoutFailed,
        PlayerDeath,
        ManualExit,
        SceneSwitched,
        UnexpectedSceneUnload,
        SuccessfulExtraction,
        Unknown
    }

    public enum ZombieModeRunOnlyObjectKind
    {
        Unknown,
        Beacon,
        Enemy,
        Boss,
        PurificationPoint,
        RewardUi,
        SafeZone,
        ExtractionPoint,
        TemporaryNpc,
        Hud,
        MapIsolation,
        Coroutine,
        EventListener,
        Projectile,
        Fortification,
        MapEvent,
        Buff
    }

    public enum ZombieModeStarterLoadout
    {
        None,
        Melee,
        Gunner
    }

    public enum ZombieModeRewardType
    {
        PurificationPoints,
        Heal,
        RandomSupply,
        RandomHighQualityItem,
        StarterReroll,
        RandomMeleeWeapon,
        RandomGunWithAmmo,
        AmmoSupply,
        MedicalSupply,
        ArmorOrHelmet,
        CurrentNodeFreeRefresh,
        NextNodeFreeRefresh,
        HalfPricePaidRefresh,
        AttributeMaxHealth,
        AttributeMoveSpeed,
        AttributeMeleeDamage,
        AttributeRangedDamage,
        AttributeReloadSpeed,
        AttributeDamageReduction,
        TempMerchant,
        TempNurse,
        FortificationPack,
        ContractPollutionDeal,
        ContractGearDeal,
        ContractHugePurification,
        ContractInsurance,
        InsuranceKeepOne,
        InsuranceRandom10,
        InsuranceRandom20,
        InsuranceNearFull,
        MapEventHighValueAirdrop,
        MapEventEliteSquad
    }

    public enum ZombieModeRewardCategory
    {
        Attribute,
        Equipment,
        Economy,
        Npc,
        Fortification,
        Contract,
        Insurance,
        MapEvent
    }

    public enum ZombieModePendingMapEventType
    {
        None,
        HighValueAirdrop,
        EliteSquad
    }

    public enum ZombieModeEnemyKind
    {
        Normal,
        Special,
        Elite
    }

    public enum ZombieModeSpecialKind
    {
        None,
        Sprinter,
        Exploder,
        Plague,
        Summoner,
        Harasser
    }

    public enum ZombieModeEliteAffix
    {
        Swift,
        Frenzied,
        Tough,
        Stalwart,
        Regenerating,
        Burst,
        Plague,
        Commander,
        ToxicAura,
        Splitting,
        Shielded,
        Adaptive
    }

    public enum ZombieModePerformanceTier
    {
        Normal,
        Watch,
        SoftProtect,
        ExtremeProtect
    }

    public enum ZombieModeBossKind
    {
        Titan,
        Hunter,
        Splitter,
        Shielder,
        Corruptor
    }

    /// <summary>
    /// ZombieMode / Reward / Pollution 子系统使用的鸭科夫 stat 名常量集合。
    /// 这些字符串原本散落在 4 个文件，且 AttackSpeed 等被裸字符串硬编码——见 §2.3。
    /// 收口到此处后，新增 stat 时只需改一个地方。
    /// </summary>
    internal static class ZombieModeStatNames
    {
        // 基础属性（给 Hunter Frenzy / Player Slow / Reward Attribute 用）
        public const string MaxHealth = "MaxHealth";
        public const string MoveSpeed = "MoveSpeed";
        public const string WalkSpeed = "WalkSpeed";
        public const string RunSpeed = "RunSpeed";
        public const string AttackSpeed = "AttackSpeed";

        // 倍率类（给 Reward.Attribute 用，对应鸭科夫源码 stat 名）
        public const string MeleeDamageMultiplier = "MeleeDamageMultiplier";
        public const string GunDamageMultiplier = "GunDamageMultiplier";
        public const string ReloadSpeedMultiplier = "ReloadSpeedMultiplier";
        public const string ElementFactorPhysics = "ElementFactor_Physics";
    }

    /// <summary>
    /// Boss 入门数值表（生命/伤害/缩放/速度倍率 + 净化点值范围）。
    /// 见审查 §1.2：原本在 ApplyZombieModeBossTuning 内 5-case switch 硬编码，
    /// 调参时藏在控制器深处难找；新增 Boss 轴（如冷却减少）时也容易漏 case。
    /// </summary>
    internal struct BossKindTuning
    {
        public float HealthMultiplier;
        public float DamageMultiplier;
        public float ScaleMultiplier;
        public float SpeedMultiplier;
        public int PointMin;
        public int PointMax;
    }

    public static class ZombieModeTuning
    {
        // ZombieModeTuning 对外统一暴露顶层常量；按主题分组的嵌套静态类
        // 仅用于类内组织，避免调用侧长期维护两层公开入口。

        private static class Pacing
        {
            public const float PreparationCountdownSeconds = 30f;
            public const float BeaconChannelDurationSeconds = 3f;
            public const float ExtractionCountdownSeconds = 15f;
            public const float PeriodicSpawnIntervalSeconds = 1f;
            public const float SettlementMaxWaitSeconds = 3.5f;
            public const float BannerDurationSeconds = 2.5f;
        }

        private static class SafeZone
        {
            public const float TickIntervalSeconds = 0.2f;
            public const float FlashStartSeconds = 5f;
            public const float FlashCycleSeconds = 0.5f;
            public const float Radius = 8f;
            public const float CenterPlayerRange = 30f;
            public const float NavMeshRadius = 5f;
        }

        private static class Performance
        {
            public const float EvalIntervalSeconds = 0.5f;
            public const float TemporaryNpcProtectionTickIntervalSeconds = 0.25f;
            public const float BossStuckTimeoutSeconds = 45f;
            public const int DropCleanupWaveAge = 3;
            public const float DropCleanupAgeSeconds = 300f;
            public const float FarDistance = 60f;
            public const float StarMagnetRadius = 30f;
            public const float StarPickupDistance = 0.3f;
            public const int TierWatch = 150;
            public const int TierSoft = 250;
            public const int TierExtreme = 350;
            public const int TierHysteresis = 10;
            public const int MaxRecyclePerEval = 8;
            public const float SoftSpawnMultiplier = 0.7f;
            public const float ExtremeSpawnMultiplier = 0.4f;
            public const float ExtremeSpawnFarSkipDistance = 50f;
        }

        private static class Spawn
        {
            public const float BossSpreadMinDistance = 8f;
            public const float NavMeshVirtualSpawnRadius = 10f;
            public const float NavMeshSampleRadius = 8f;
            public const float DuplicateDistance = 4f;
            public const float MinPlayerDistance = 12f;
            public const float NavMeshLiftOffset = 0.05f;
            public const int MaxInitialWaveSpawnCount = 0;
            public const int MaxNormalZombieCount = 50;
            public const float NormalZombieForceTraceDistance = 500f;
        }

        private static class Reward
        {
            public const int CashToPurificationRatio = 100;
            public const int FreeRefreshCapPerNode = 3;
            public static readonly int[] PaidRefreshCosts = { 100, 200, 350, 550, 800 };
            public const int InstantPurificationNormalBase = 150;
            public const int InstantPurificationBossBase = 400;
            public const float PurificationPollutionScalePerStep = 0.10f;
            public const int PurificationPollutionStep = 10;
            public const float PurificationPollutionScaleMax = 1.5f;
            public const int StarterMaxQuality = 5;
            public const int StarterGunnerExtraAmmoCount = 1000;
        }

        private static class Combat
        {
            // 普通敌人 / 精英 / 特殊倍率
            public const float SpecialHealthMultiplier = 1.4f;
            public const float SpecialDamageMultiplier = 1.2f;
            public const float SpecialMoveSpeedMultiplier = 1.1f;
            public const float EliteHealthMultiplier = 2.5f;
            public const float EliteDamageMultiplier = 1.5f;
            public const float EliteMoveSpeedMultiplier = 1.1f;
            public const float EnhancedEliteHealthMultiplier = 3.2f;
            public const float EnhancedEliteDamageMultiplier = 1.7f;
            public const float EnhancedEliteMoveSpeedMultiplier = 1.3f;
            public const float PollutionHealthScalePerPoint = 0.05f;
            public const float PollutionDamageScalePerPoint = 0.04f;
            public const float StalwartRangedDamageMultiplier = 0.10f;
            public const int NormalPurificationMin = 3;
            public const int NormalPurificationMax = 8;
            public const int SpecialPurificationMin = 30;
            public const int SpecialPurificationMax = 60;
            public const int ElitePurificationMin = 80;
            public const int ElitePurificationMax = 150;

            // 词缀 / 死亡爆裂
            public const float ExploderDeathRadius = 4f;
            public const float ExploderDeathDamage = 80f;
            public const float BurstAffixDeathRadius = 4f;
            public const float BurstAffixDeathDamage = 40f;
            public const int SplittingAffixSpawnCount = 2;
            public const int SplitterBossDeathSpawnCount = 2;
            public const int SplitterBossDeathSpawnCountSoftProtect = 1;
            public const float SplitterBossDeathRadius = 4f;
            public const float SplitterBossDeathDamage = 45f;
            public const int AdaptiveAffixHitThreshold = 5;
            public const float AdaptiveAffixReductionPercent = 0.60f;
            public const float AdaptiveAffixDurationSeconds = 8f;
            public const float ShieldedAffixCooldownSeconds = 12f;
            public const float ShieldedAffixShieldPercent = 0.25f;
            public const float ShieldedAffixDurationSeconds = 5f;
            public const float CommanderAffixAuraRadius = 8f;
            public const float CommanderAffixMoveSpeedBonus = 0.20f;
            public const float CommanderAffixDamageBonus = 0.15f;
            public const float CommanderAuraTickIntervalSeconds = 0.5f;
            public const float ThreatTelegraphDelaySeconds = 0.9f;
            public const float BossTelegraphDelaySeconds = 1.2f;

            // 通用冷却 / 起手
            public const float SprinterCooldownSeconds = 8f;
            public const float PoisonCooldownSeconds = 12f;
            public const float SummonerCooldownSeconds = 15f;
            public const float HarasserCooldownSeconds = 4f;
            public const float ExploderCooldownSeconds = 9f;
            public const float EliteSkillCooldownSeconds = 12f;
            public const float BossSkillCooldownSeconds = 10f;
            public const float SprinterDashStartupSeconds = 0.5f;
            public const float SprinterDashDistance = 12f;
            public const float ExploderTriggerDistance = 2.5f;
            public const float ExploderDetonationDelaySeconds = 1.0f;
            public const float PlagueCloudRadius = 4f;
            public const float PlagueCloudDurationSeconds = 3f;
            public const float PlagueCloudDamagePerSecond = 8f;
            public const int SummonerSpawnCount = 2;
            public const float HarasserProjectileSpeed = 12f;
            public const float HarasserProjectileDamage = 25f;
            public const float HarasserProjectileLifetimeSeconds = 2f;
        }

        private static class Boss
        {
            // Titan
            public const float TitanShockwaveRadius = 6f;
            public const float TitanShockwaveDamage = 60f;
            public const float TitanShockwaveCooldownSeconds = 12f;
            public const float TitanShockwaveStartupSeconds = 1.0f;
            public const float TitanDamageReductionPercent = 0.40f;
            public const float TitanDamageReductionDurationSeconds = 4f;
            public const float TitanDamageReductionCooldownSeconds = 20f;
            public const float TitanDamageReductionStartupSeconds = 0.6f;

            // Hunter
            public const float HunterDashDistance = 15f;
            public const float HunterDashDamage = 40f;
            public const float HunterDashRadius = 3.5f;
            public const float HunterDashCooldownSeconds = 5f;
            public const float HunterDashStartupSeconds = 0.3f;
            public const float HunterFrenzyHpThreshold = 0.30f;
            public const float HunterFrenzyAttackSpeedBonus = 0.50f;
            public const float HunterFrenzyMoveSpeedBonus = 0.30f;
            public const float HunterFrenzyDurationSeconds = 15f;

            // Splitter
            public const int SplitterBossSummonCount = 4;
            public const float SplitterBossSummonScale = 0.7f;
            public const float SplitterBossSummonCooldownSeconds = 15f;
            public const float SplitterBossSummonStartupSeconds = 0.8f;
            public const float SplitterBossSplitFirstHpThreshold = 0.50f;
            public const float SplitterBossSplitSecondHpThreshold = 0.25f;
            public const int SplitterBossSplitCount = 2;
            public const float SplitterBossSplitChildScale = 0.5f;

            // Shielder
            public const float ShielderSelfShieldPercent = 0.35f;
            public const float ShielderSelfShieldDurationSeconds = 8f;
            public const float ShielderSelfShieldCooldownSeconds = 25f;
            public const float ShielderSelfShieldStartupSeconds = 0.5f;
            public const float ShielderGroupShieldPercent = 0.35f;
            public const float ShielderGroupShieldRadius = 8f;
            public const float ShielderGroupShieldDurationSeconds = 6f;
            public const float ShielderGroupShieldCooldownSeconds = 35f;
            public const float ShielderGroupShieldStartupSeconds = 1.0f;
            public const float ShielderAuraRadius = 6f;
            public const float ShielderAuraDamageReductionPercent = 0.15f;

            // Corruptor
            public const float CorruptorZoneRadius = 4f;
            public const float CorruptorZoneDurationSeconds = 8f;
            public const float CorruptorZoneDamagePerSecond = 6f;
            public const float CorruptorZoneSlowPercent = 0.20f;
            public const float CorruptorZoneCooldownSeconds = 12f;
            public const float CorruptorZoneStartupSeconds = 1.0f;
            public const float CorruptorPoisonPathWidth = 1.2f;
            public const float CorruptorPoisonPathDurationSeconds = 5f;
            public const float CorruptorPoisonPathDamagePerSecond = 4f;
            public const float CorruptorPoisonPathTickIntervalSeconds = 0.5f;
            public const float CorruptorDeathCloudRadius = 5f;
            public const float CorruptorDeathCloudDurationSeconds = 6f;
            public const float CorruptorDeathCloudDamagePerSecond = 5f;
            public const float CorruptorDeathCloudTickIntervalSeconds = 0.5f;
        }

        // Boss 入门数值表 — 见 BossKindTuning 注释。
        // 调用方走 ZombieModeTuning.GetBossKind(kind) 拿 struct 副本。
        private static readonly Dictionary<ZombieModeBossKind, BossKindTuning> BossKindTable
            = new Dictionary<ZombieModeBossKind, BossKindTuning>
            {
                {
                    ZombieModeBossKind.Titan,
                    new BossKindTuning
                    {
                        HealthMultiplier = 35f, DamageMultiplier = 1.8f,
                        ScaleMultiplier = 1.8f, SpeedMultiplier = 0.7f,
                        PointMin = 400, PointMax = 600
                    }
                },
                {
                    ZombieModeBossKind.Hunter,
                    new BossKindTuning
                    {
                        HealthMultiplier = 18f, DamageMultiplier = 1.4f,
                        ScaleMultiplier = 1.2f, SpeedMultiplier = 1.6f,
                        PointMin = 300, PointMax = 500
                    }
                },
                {
                    ZombieModeBossKind.Splitter,
                    new BossKindTuning
                    {
                        HealthMultiplier = 25f, DamageMultiplier = 1.1f,
                        ScaleMultiplier = 1.5f, SpeedMultiplier = 0.95f,
                        PointMin = 400, PointMax = 700
                    }
                },
                {
                    ZombieModeBossKind.Shielder,
                    new BossKindTuning
                    {
                        HealthMultiplier = 28f, DamageMultiplier = 1.3f,
                        ScaleMultiplier = 1.3f, SpeedMultiplier = 0.9f,
                        PointMin = 400, PointMax = 600
                    }
                },
                {
                    ZombieModeBossKind.Corruptor,
                    new BossKindTuning
                    {
                        HealthMultiplier = 26f, DamageMultiplier = 1.2f,
                        ScaleMultiplier = 1.4f, SpeedMultiplier = 1.0f,
                        PointMin = 400, PointMax = 650
                    }
                },
            };

        /// <summary>
        /// 取某 Boss kind 的入门数值。未知 kind 时返回稳健的默认（与原有 fallback 一致）。
        /// </summary>
        internal static BossKindTuning GetBossKind(ZombieModeBossKind kind)
        {
            BossKindTuning tuning;
            if (BossKindTable.TryGetValue(kind, out tuning))
            {
                return tuning;
            }
            return new BossKindTuning
            {
                HealthMultiplier = 2.2f,
                DamageMultiplier = 1.2f,
                ScaleMultiplier = 1.18f,
                SpeedMultiplier = 1f,
                PointMin = 300,
                PointMax = 800,
            };
        }

        private static class Extraction
        {
            public const float AreaTriggerRadius = 2.0f;
            public const float AreaLeaveRadius = 2.5f;
        }

        // ============================================================
        // 统一公开入口（映射到上述按主题分组常量）
        // ============================================================

        // Pacing
        public const float PreparationCountdownSeconds = Pacing.PreparationCountdownSeconds;
        public const float BeaconChannelDurationSeconds = Pacing.BeaconChannelDurationSeconds;
        public const float ExtractionCountdownSeconds = Pacing.ExtractionCountdownSeconds;
        public const float PeriodicSpawnIntervalSeconds = Pacing.PeriodicSpawnIntervalSeconds;
        public const float SettlementMaxWaitSeconds = Pacing.SettlementMaxWaitSeconds;
        public const float BannerDurationSeconds = Pacing.BannerDurationSeconds;

        // SafeZone
        public const float SafeZoneTickIntervalSeconds = SafeZone.TickIntervalSeconds;
        public const float SafeZoneFlashStartSeconds = SafeZone.FlashStartSeconds;
        public const float SafeZoneFlashCycleSeconds = SafeZone.FlashCycleSeconds;
        public const float SafeZoneRadius = SafeZone.Radius;
        public const float SafeZoneCenterPlayerRange = SafeZone.CenterPlayerRange;
        public const float NavMeshSafeZoneRadius = SafeZone.NavMeshRadius;

        // Performance
        public const float PerformanceEvalIntervalSeconds = Performance.EvalIntervalSeconds;
        public const float TemporaryNpcProtectionTickIntervalSeconds = Performance.TemporaryNpcProtectionTickIntervalSeconds;
        public const float BossStuckTimeoutSeconds = Performance.BossStuckTimeoutSeconds;
        public const int DropCleanupWaveAge = Performance.DropCleanupWaveAge;
        public const float DropCleanupAgeSeconds = Performance.DropCleanupAgeSeconds;
        public const float PerformanceFarDistance = Performance.FarDistance;
        public const float StarMagnetRadius = Performance.StarMagnetRadius;
        public const float StarPickupDistance = Performance.StarPickupDistance;
        public const int PerfTierWatch = Performance.TierWatch;
        public const int PerfTierSoft = Performance.TierSoft;
        public const int PerfTierExtreme = Performance.TierExtreme;
        public const int PerfTierHysteresis = Performance.TierHysteresis;
        public const int MaxRecyclePerEval = Performance.MaxRecyclePerEval;
        public const float PerfSoftSpawnMultiplier = Performance.SoftSpawnMultiplier;
        public const float PerfExtremeSpawnMultiplier = Performance.ExtremeSpawnMultiplier;
        public const float PerfExtremeSpawnFarSkipDistance = Performance.ExtremeSpawnFarSkipDistance;

        // Spawn
        public const float BossSpreadMinDistance = Spawn.BossSpreadMinDistance;
        public const float NavMeshVirtualSpawnRadius = Spawn.NavMeshVirtualSpawnRadius;
        public const float SpawnPointNavMeshSampleRadius = Spawn.NavMeshSampleRadius;
        public const float SpawnPointDuplicateDistance = Spawn.DuplicateDistance;
        public const float SpawnPointMinPlayerDistance = Spawn.MinPlayerDistance;
        public const float NavMeshLiftOffset = Spawn.NavMeshLiftOffset;
        public const int MaxInitialWaveSpawnCount = Spawn.MaxInitialWaveSpawnCount;
        public const int MaxNormalZombieCount = Spawn.MaxNormalZombieCount;
        public const float NormalZombieForceTraceDistance = Spawn.NormalZombieForceTraceDistance;

        // Reward
        public const int CashToPurificationRatio = Reward.CashToPurificationRatio;
        public const int FreeRefreshCapPerNode = Reward.FreeRefreshCapPerNode;
        public static readonly int[] PaidRefreshCosts = Reward.PaidRefreshCosts;
        public const int InstantPurificationNormalBase = Reward.InstantPurificationNormalBase;
        public const int InstantPurificationBossBase = Reward.InstantPurificationBossBase;
        public const float PurificationPollutionScalePerStep = Reward.PurificationPollutionScalePerStep;
        public const int PurificationPollutionStep = Reward.PurificationPollutionStep;
        public const float PurificationPollutionScaleMax = Reward.PurificationPollutionScaleMax;
        public const int StarterMaxQuality = Reward.StarterMaxQuality;
        public const int StarterGunnerExtraAmmoCount = Reward.StarterGunnerExtraAmmoCount;

        // Combat - 倍率
        public const float SpecialHealthMultiplier = Combat.SpecialHealthMultiplier;
        public const float SpecialDamageMultiplier = Combat.SpecialDamageMultiplier;
        public const float SpecialMoveSpeedMultiplier = Combat.SpecialMoveSpeedMultiplier;
        public const float EliteHealthMultiplier = Combat.EliteHealthMultiplier;
        public const float EliteDamageMultiplier = Combat.EliteDamageMultiplier;
        public const float EliteMoveSpeedMultiplier = Combat.EliteMoveSpeedMultiplier;
        public const float EnhancedEliteHealthMultiplier = Combat.EnhancedEliteHealthMultiplier;
        public const float EnhancedEliteDamageMultiplier = Combat.EnhancedEliteDamageMultiplier;
        public const float EnhancedEliteMoveSpeedMultiplier = Combat.EnhancedEliteMoveSpeedMultiplier;
        public const float PollutionHealthScalePerPoint = Combat.PollutionHealthScalePerPoint;
        public const float PollutionDamageScalePerPoint = Combat.PollutionDamageScalePerPoint;
        public const float StalwartRangedDamageMultiplier = Combat.StalwartRangedDamageMultiplier;
        public const int NormalPurificationMin = Combat.NormalPurificationMin;
        public const int NormalPurificationMax = Combat.NormalPurificationMax;
        public const int SpecialPurificationMin = Combat.SpecialPurificationMin;
        public const int SpecialPurificationMax = Combat.SpecialPurificationMax;
        public const int ElitePurificationMin = Combat.ElitePurificationMin;
        public const int ElitePurificationMax = Combat.ElitePurificationMax;

        // Combat - 词缀
        public const float ExploderDeathRadius = Combat.ExploderDeathRadius;
        public const float ExploderDeathDamage = Combat.ExploderDeathDamage;
        public const float BurstAffixDeathRadius = Combat.BurstAffixDeathRadius;
        public const float BurstAffixDeathDamage = Combat.BurstAffixDeathDamage;
        public const int SplittingAffixSpawnCount = Combat.SplittingAffixSpawnCount;
        public const int SplitterBossDeathSpawnCount = Combat.SplitterBossDeathSpawnCount;
        public const int SplitterBossDeathSpawnCountSoftProtect = Combat.SplitterBossDeathSpawnCountSoftProtect;
        public const float SplitterBossDeathRadius = Combat.SplitterBossDeathRadius;
        public const float SplitterBossDeathDamage = Combat.SplitterBossDeathDamage;
        public const int AdaptiveAffixHitThreshold = Combat.AdaptiveAffixHitThreshold;
        public const float AdaptiveAffixReductionPercent = Combat.AdaptiveAffixReductionPercent;
        public const float AdaptiveAffixDurationSeconds = Combat.AdaptiveAffixDurationSeconds;
        public const float ShieldedAffixCooldownSeconds = Combat.ShieldedAffixCooldownSeconds;
        public const float ShieldedAffixShieldPercent = Combat.ShieldedAffixShieldPercent;
        public const float ShieldedAffixDurationSeconds = Combat.ShieldedAffixDurationSeconds;
        public const float CommanderAffixAuraRadius = Combat.CommanderAffixAuraRadius;
        public const float CommanderAffixMoveSpeedBonus = Combat.CommanderAffixMoveSpeedBonus;
        public const float CommanderAffixDamageBonus = Combat.CommanderAffixDamageBonus;
        public const float CommanderAuraTickIntervalSeconds = Combat.CommanderAuraTickIntervalSeconds;
        public const float ThreatTelegraphDelaySeconds = Combat.ThreatTelegraphDelaySeconds;
        public const float BossTelegraphDelaySeconds = Combat.BossTelegraphDelaySeconds;

        // Combat - 冷却 / 起手
        public const float SprinterCooldownSeconds = Combat.SprinterCooldownSeconds;
        public const float PoisonCooldownSeconds = Combat.PoisonCooldownSeconds;
        public const float SummonerCooldownSeconds = Combat.SummonerCooldownSeconds;
        public const float HarasserCooldownSeconds = Combat.HarasserCooldownSeconds;
        public const float ExploderCooldownSeconds = Combat.ExploderCooldownSeconds;
        public const float EliteSkillCooldownSeconds = Combat.EliteSkillCooldownSeconds;
        public const float BossSkillCooldownSeconds = Combat.BossSkillCooldownSeconds;
        public const float SprinterDashStartupSeconds = Combat.SprinterDashStartupSeconds;
        public const float SprinterDashDistance = Combat.SprinterDashDistance;
        public const float ExploderTriggerDistance = Combat.ExploderTriggerDistance;
        public const float ExploderDetonationDelaySeconds = Combat.ExploderDetonationDelaySeconds;
        public const float PlagueCloudRadius = Combat.PlagueCloudRadius;
        public const float PlagueCloudDurationSeconds = Combat.PlagueCloudDurationSeconds;
        public const float PlagueCloudDamagePerSecond = Combat.PlagueCloudDamagePerSecond;
        public const int SummonerSpawnCount = Combat.SummonerSpawnCount;
        public const float HarasserProjectileSpeed = Combat.HarasserProjectileSpeed;
        public const float HarasserProjectileDamage = Combat.HarasserProjectileDamage;
        public const float HarasserProjectileLifetimeSeconds = Combat.HarasserProjectileLifetimeSeconds;

        // Boss - Titan
        public const float TitanShockwaveRadius = Boss.TitanShockwaveRadius;
        public const float TitanShockwaveDamage = Boss.TitanShockwaveDamage;
        public const float TitanShockwaveCooldownSeconds = Boss.TitanShockwaveCooldownSeconds;
        public const float TitanShockwaveStartupSeconds = Boss.TitanShockwaveStartupSeconds;
        public const float TitanDamageReductionPercent = Boss.TitanDamageReductionPercent;
        public const float TitanDamageReductionDurationSeconds = Boss.TitanDamageReductionDurationSeconds;
        public const float TitanDamageReductionCooldownSeconds = Boss.TitanDamageReductionCooldownSeconds;
        public const float TitanDamageReductionStartupSeconds = Boss.TitanDamageReductionStartupSeconds;

        // Boss - Hunter
        public const float HunterDashDistance = Boss.HunterDashDistance;
        public const float HunterDashDamage = Boss.HunterDashDamage;
        public const float HunterDashRadius = Boss.HunterDashRadius;
        public const float HunterDashCooldownSeconds = Boss.HunterDashCooldownSeconds;
        public const float HunterDashStartupSeconds = Boss.HunterDashStartupSeconds;
        public const float HunterFrenzyHpThreshold = Boss.HunterFrenzyHpThreshold;
        public const float HunterFrenzyAttackSpeedBonus = Boss.HunterFrenzyAttackSpeedBonus;
        public const float HunterFrenzyMoveSpeedBonus = Boss.HunterFrenzyMoveSpeedBonus;
        public const float HunterFrenzyDurationSeconds = Boss.HunterFrenzyDurationSeconds;

        // Boss - Splitter
        public const int SplitterBossSummonCount = Boss.SplitterBossSummonCount;
        public const float SplitterBossSummonScale = Boss.SplitterBossSummonScale;
        public const float SplitterBossSummonCooldownSeconds = Boss.SplitterBossSummonCooldownSeconds;
        public const float SplitterBossSummonStartupSeconds = Boss.SplitterBossSummonStartupSeconds;
        public const float SplitterBossSplitFirstHpThreshold = Boss.SplitterBossSplitFirstHpThreshold;
        public const float SplitterBossSplitSecondHpThreshold = Boss.SplitterBossSplitSecondHpThreshold;
        public const int SplitterBossSplitCount = Boss.SplitterBossSplitCount;
        public const float SplitterBossSplitChildScale = Boss.SplitterBossSplitChildScale;

        // Boss - Shielder
        public const float ShielderSelfShieldPercent = Boss.ShielderSelfShieldPercent;
        public const float ShielderSelfShieldDurationSeconds = Boss.ShielderSelfShieldDurationSeconds;
        public const float ShielderSelfShieldCooldownSeconds = Boss.ShielderSelfShieldCooldownSeconds;
        public const float ShielderSelfShieldStartupSeconds = Boss.ShielderSelfShieldStartupSeconds;
        public const float ShielderGroupShieldPercent = Boss.ShielderGroupShieldPercent;
        public const float ShielderGroupShieldRadius = Boss.ShielderGroupShieldRadius;
        public const float ShielderGroupShieldDurationSeconds = Boss.ShielderGroupShieldDurationSeconds;
        public const float ShielderGroupShieldCooldownSeconds = Boss.ShielderGroupShieldCooldownSeconds;
        public const float ShielderGroupShieldStartupSeconds = Boss.ShielderGroupShieldStartupSeconds;
        public const float ShielderAuraRadius = Boss.ShielderAuraRadius;
        public const float ShielderAuraDamageReductionPercent = Boss.ShielderAuraDamageReductionPercent;

        // Boss - Corruptor
        public const float CorruptorZoneRadius = Boss.CorruptorZoneRadius;
        public const float CorruptorZoneDurationSeconds = Boss.CorruptorZoneDurationSeconds;
        public const float CorruptorZoneDamagePerSecond = Boss.CorruptorZoneDamagePerSecond;
        public const float CorruptorZoneSlowPercent = Boss.CorruptorZoneSlowPercent;
        public const float CorruptorZoneCooldownSeconds = Boss.CorruptorZoneCooldownSeconds;
        public const float CorruptorZoneStartupSeconds = Boss.CorruptorZoneStartupSeconds;
        public const float CorruptorPoisonPathWidth = Boss.CorruptorPoisonPathWidth;
        public const float CorruptorPoisonPathDurationSeconds = Boss.CorruptorPoisonPathDurationSeconds;
        public const float CorruptorPoisonPathDamagePerSecond = Boss.CorruptorPoisonPathDamagePerSecond;
        public const float CorruptorPoisonPathTickIntervalSeconds = Boss.CorruptorPoisonPathTickIntervalSeconds;
        public const float CorruptorDeathCloudRadius = Boss.CorruptorDeathCloudRadius;
        public const float CorruptorDeathCloudDurationSeconds = Boss.CorruptorDeathCloudDurationSeconds;
        public const float CorruptorDeathCloudDamagePerSecond = Boss.CorruptorDeathCloudDamagePerSecond;
        public const float CorruptorDeathCloudTickIntervalSeconds = Boss.CorruptorDeathCloudTickIntervalSeconds;

        // Extraction
        public const float ExtractionAreaTriggerRadius = Extraction.AreaTriggerRadius;
        public const float ExtractionAreaLeaveRadius = Extraction.AreaLeaveRadius;
    }

    public static class ZombieModePhaseGuards
    {
        public static bool IsRunInProgress(ZombieModeLifecyclePhase phase)
        {
            return phase != ZombieModeLifecyclePhase.None;
        }

        public static bool IsBeforeActive(ZombieModeLifecyclePhase phase)
        {
            return phase == ZombieModeLifecyclePhase.SelectingMap ||
                   phase == ZombieModeLifecyclePhase.Prechecking ||
                   phase == ZombieModeLifecyclePhase.CommittingResources ||
                   phase == ZombieModeLifecyclePhase.LoadingMap ||
                   phase == ZombieModeLifecyclePhase.InitializingRun ||
                   phase == ZombieModeLifecyclePhase.WaitingStarterChoice;
        }

        public static bool IsActive(ZombieModeLifecyclePhase phase)
        {
            return phase == ZombieModeLifecyclePhase.Active;
        }

        public static bool IsCombatRunning(ZombieModeCombatPhase phase)
        {
            return phase == ZombieModeCombatPhase.Combat;
        }

        public static bool ShouldPauseModePressure(ZombieModeCombatPhase phase)
        {
            return phase == ZombieModeCombatPhase.Settling ||
                   phase == ZombieModeCombatPhase.RewardSelection ||
                   phase == ZombieModeCombatPhase.SuccessExit ||
                   phase == ZombieModeCombatPhase.FailedExit;
        }

        public static bool AllowsBeacon(ZombieModeCombatPhase phase)
        {
            return phase == ZombieModeCombatPhase.InitialPreparation ||
                   phase == ZombieModeCombatPhase.Preparation ||
                   phase == ZombieModeCombatPhase.ExtractionOpportunity;
        }

        public static bool AllowsExtraction(ZombieModeCombatPhase phase)
        {
            return phase == ZombieModeCombatPhase.Preparation ||
                   phase == ZombieModeCombatPhase.ExtractionOpportunity;
        }
    }

    public sealed class ZombieModeSpawnPoint
    {
        public Vector3 Position;
        public bool VirtualPoint;

        public ZombieModeSpawnPoint(Vector3 position, bool virtualPoint)
        {
            Position = position;
            VirtualPoint = virtualPoint;
        }
    }

    public sealed class ZombiePurificationStar
    {
        public Vector3 SpawnPosition;
        public int PointsValue;
        public bool Settled;
        public float SpawnTime;
        public int RunId;
        public GameObject Visual;
    }

    /// <summary>
    /// Boss 技能状态基类。每个 Boss kind 实例化一个对应子类，
    /// 把 5 种 Boss 的独占冷却字段从 ZombieModeBossInstance 拆出来。
    /// </summary>
    public abstract class ZombieModeBossSkillState
    {
        /// <summary>
        /// 在 Boss 出生时初始化所有 Next*Time 与 scale 缓存。
        /// </summary>
        public abstract void Reset(float now, float bossScale);

        /// <summary>
        /// 每帧由 TickZombieModeBossController 派发。子类内决定何时触发技能
        /// （比较 Next*Time），并调用 mod 上的 internal 实现方法（telegraph / 召唤 / 护盾）。
        /// 多态化后，BossController 主循环退化为 instance.SkillState.Tick(this, instance, now)。
        /// 默认 no-op；ZombieModeBossController 通过 partial 在自身定义 internal Tick 方法
        /// （TickZombieModeTitanState / HunterState / ...），子类 override 转发到对应方法。
        /// </summary>
        public virtual void Tick(ModBehaviour mod, ZombieModeBossInstance instance, float now) { }

        /// <summary>
        /// 主技能冷却（用于 UI / 守门用），未使用时可保留默认值。
        /// </summary>
        public virtual float CooldownSeconds => 0f;
    }

    public sealed class ZombieModeTitanState : ZombieModeBossSkillState
    {
        public float NextShockwaveTime;
        public float NextDamageReductionTime;
        public bool DamageReductionActive;
        public float DamageReductionEndTime;

        public override float CooldownSeconds => ZombieModeTuning.TitanShockwaveCooldownSeconds;

        public override void Reset(float now, float bossScale)
        {
            NextShockwaveTime = now + UnityEngine.Random.Range(2f, 5f);
            NextDamageReductionTime = now + UnityEngine.Random.Range(8f, 12f);
        }

        public override void Tick(ModBehaviour mod, ZombieModeBossInstance instance, float now)
        {
            if (mod != null) mod.TickZombieModeTitanState(this, instance, now);
        }
    }

    public sealed class ZombieModeHunterState : ZombieModeBossSkillState
    {
        public float NextDashTime;
        public bool FrenzyActive;
        public float FrenzyEndTime;
        public float FrenzyOriginalScale = 1f;
        public readonly List<ZombieModeAttributeModifierRecord> FrenzyModifierRecords = new List<ZombieModeAttributeModifierRecord>();

        public override float CooldownSeconds => ZombieModeTuning.HunterDashCooldownSeconds;

        public override void Reset(float now, float bossScale)
        {
            NextDashTime = now + UnityEngine.Random.Range(2f, 4f);
            FrenzyOriginalScale = bossScale;
            FrenzyModifierRecords.Clear();
        }

        public override void Tick(ModBehaviour mod, ZombieModeBossInstance instance, float now)
        {
            if (mod != null) mod.TickZombieModeHunterState(this, instance, now);
        }
    }

    public sealed class ZombieModeSplitterState : ZombieModeBossSkillState
    {
        public float NextSummonTime;
        public bool FirstSplitTriggered;
        public bool SecondSplitTriggered;

        public override float CooldownSeconds => ZombieModeTuning.SplitterBossSummonCooldownSeconds;

        public override void Reset(float now, float bossScale)
        {
            NextSummonTime = now + UnityEngine.Random.Range(3f, 6f);
        }

        public override void Tick(ModBehaviour mod, ZombieModeBossInstance instance, float now)
        {
            if (mod != null) mod.TickZombieModeSplitterState(this, instance, now);
        }
    }

    public sealed class ZombieModeShielderState : ZombieModeBossSkillState
    {
        public float NextSelfShieldTime;
        public float NextGroupShieldTime;

        public override float CooldownSeconds => ZombieModeTuning.ShielderSelfShieldCooldownSeconds;

        public override void Reset(float now, float bossScale)
        {
            NextSelfShieldTime = now + UnityEngine.Random.Range(4f, 8f);
            NextGroupShieldTime = now + UnityEngine.Random.Range(10f, 14f);
        }

        public override void Tick(ModBehaviour mod, ZombieModeBossInstance instance, float now)
        {
            if (mod != null) mod.TickZombieModeShielderState(this, instance, now);
        }
    }

    public sealed class ZombieModeCorruptorState : ZombieModeBossSkillState
    {
        public float NextZoneTime;
        public float NextPoisonPathTime;

        public override float CooldownSeconds => ZombieModeTuning.CorruptorZoneCooldownSeconds;

        public override void Reset(float now, float bossScale)
        {
            NextZoneTime = now + UnityEngine.Random.Range(2f, 5f);
            NextPoisonPathTime = now + UnityEngine.Random.Range(1f, 3f);
        }

        public override void Tick(ModBehaviour mod, ZombieModeBossInstance instance, float now)
        {
            if (mod != null) mod.TickZombieModeCorruptorState(this, instance, now);
        }
    }

    /// <summary>
    /// Boss 运行期生命周期 + 卡死检测追踪。
    /// 字段写入由 ZombieModeSpawner / TickZombieModeBossController / HandleZombieModeBossHurt 维护。
    /// 与 SkillState 的差别：本类记录"是否还活着 / 上次能动 / 上次受伤"等通用追踪字段；
    /// SkillState 记录 per-kind 的技能冷却（NextDashTime / FrenzyEndTime 等）。
    /// </summary>
    public sealed class ZombieModeBossLifecycleTrack
    {
        public bool Alive;
        public Vector3 LastKnownPosition;
        public float LastReachableTime;
        public float LastHurtTime;
    }

    public sealed class ZombieModeBossInstance
    {
        // 身份
        public CharacterMainControl Character;
        public ZombieModeBossKind Kind;
        public ZombieModeEnemyRuntimeMarker Marker;

        // 生命周期 + 卡死检测追踪（独立子对象，便于扩展新 Boss kind 时不必改 BossInstance）
        public readonly ZombieModeBossLifecycleTrack Lifecycle = new ZombieModeBossLifecycleTrack();

        // per-kind 技能状态（按 Kind 实例化对应子类，承载 NextDashTime / FrenzyEndTime 等）
        public ZombieModeBossSkillState SkillState;
    }

    public sealed class ZombieModeRewardNode
    {
        public int Wave;
        public bool BossNode;
        public readonly List<ZombieModeRewardType> Options = new List<ZombieModeRewardType>();
    }

    public sealed class ZombieModeRewardCatalogEntry
    {
        public ZombieModeRewardType RewardType;
        public ZombieModeRewardCategory Category;
        public int Weight;
    }

    public sealed class ZombieModeAttributeModifierRecord
    {
        public ItemStatsSystem.Item CharacterItem;
        public ItemStatsSystem.Stat Stat;
        public ItemStatsSystem.Stats.Modifier Modifier;
        public string StatName = string.Empty;
    }

    public sealed class ZombieModeInsuranceState
    {
        public float RandomKeepRatio;
        public ItemStatsSystem.Item SpecifiedKeepItem;

        public void Reset()
        {
            RandomKeepRatio = 0f;
            SpecifiedKeepItem = null;
        }
    }

    public sealed class ZombieModeTemporaryNpc
    {
        public GameObject GameObject;
        public string ServiceType = string.Empty;
        public int SpawnWave;
        public ZombieModeNpcServiceState ServiceState;
    }

    public sealed class ZombieModeTemporaryNpcProtectionMarker : MonoBehaviour
    {
        public int RunId;
        public string ServiceType = string.Empty;
    }

    public sealed class ZombieModeNpcServiceState
    {
        public bool BossNodeStock;
        public readonly List<int> MerchantStockRemaining = new List<int>();
        public readonly List<int> NurseUsesRemaining = new List<int>();
    }

    public sealed class ZombieModeDropCandidate
    {
        public GameObject GameObject;
        public int WaveAtSpawn;
        public float SpawnTime;
        public bool HighValue;
        public bool BossDrop;
    }

    public sealed class ZombieModeBossDrop
    {
        public GameObject GameObject;
        public int WaveAtSpawn;
        public ZombieModeBossKind BossKind;
    }

    public sealed class ZombieModeRunOnlyRecord
    {
        public int RunId;
        public ZombieModeRunOnlyObjectKind Kind;
        public UnityEngine.Object Target;
        public GameObject GameObject;
        public Action CleanupAction;

        public bool Cleanup(bool destroyGameObject)
        {
            bool handled = false;

            if (CleanupAction != null)
            {
                CleanupAction();
                handled = true;
            }

            if (destroyGameObject && GameObject != null)
            {
                UnityEngine.Object.Destroy(GameObject);
                handled = true;
            }

            Target = null;
            GameObject = null;
            CleanupAction = null;
            return handled;
        }
    }

    public sealed class ZombieModeEntryTransaction
    {
        public bool InvitationTemporarilyHeld;
        public bool CashTemporarilyHeld;
        public bool InventoryTransferStarted;
        public bool EntryResourcesFinalized;
        public long CashWithheldAmount;
        public readonly List<ItemStatsSystem.Item> InventoryTransferredItems = new List<ItemStatsSystem.Item>();
        public readonly List<string> BlockingMessages = new List<string>();

        public void Reset()
        {
            InvitationTemporarilyHeld = false;
            CashTemporarilyHeld = false;
            InventoryTransferStarted = false;
            EntryResourcesFinalized = false;
            CashWithheldAmount = 0L;
            InventoryTransferredItems.Clear();
            BlockingMessages.Clear();
        }
    }

    public sealed class ZombieModeMapProfile
    {
        public int SceneId = -1;
        public string SceneName = string.Empty;
        public string MainSceneName = string.Empty;
        public string DisplayName = string.Empty;
        public Vector3[] StaticSpawnPoints = new Vector3[0];
        public bool AllowVirtualSpawnPoints = true;
        public int[] DisabledExtractionAreaIds = new int[0];
        public Vector3[][] SafeZoneExclusionPolygons = new Vector3[0][];
        public Vector3[][] ExtractionExclusionPolygons = new Vector3[0][];
        public string[] RetainedNeutralWhitelistTypes = new string[0];
        public bool ContainerRefillEnabled = true;
        public Vector3? CustomSpawnPos;
    }

    public sealed class ZombieModeRunState
    {
        public int RunId;
        public int SceneBuildIndex = -1;
        public string SceneName = string.Empty;
        public ZombieModeMapProfile MapProfile;
        public ZombieModeLifecyclePhase LifecyclePhase = ZombieModeLifecyclePhase.None;
        public ZombieModeCombatPhase CombatPhase = ZombieModeCombatPhase.None;
        public bool ZombieModeResourcesCommitted;
        public bool EntryResourcesFinalized;
        public bool IsCleaningUp;
        public long PendingCashInvestment;
        public long ConfirmedCashInvested;
        public readonly List<ZombiePurificationStar> PendingPurificationStars = new List<ZombiePurificationStar>();
        public int CurrentWave;
        public int CurrentWaveKillTarget;
        public int CurrentWaveKills;
        public readonly List<ZombieModeBossInstance> CurrentWaveBossInstances = new List<ZombieModeBossInstance>();
        public int CurrentWaveBossesRemaining;
        public int PurificationPoints;
        public readonly List<ZombieModeSpawnPoint> EffectiveSpawnPoints = new List<ZombieModeSpawnPoint>();
        public float PeriodicSpawnTimer;
        public int NextSpawnPointIndex;
        public float PreparationTimer;
        public int LivingZombieCount;
        public int LivingNormalZombieCount;
        public int PendingNormalZombieSpawns;
        public ZombieModePerformanceTier PerformanceTier = ZombieModePerformanceTier.Normal;
        public readonly Queue<CharacterMainControl> PendingRecycleQueue = new Queue<CharacterMainControl>();
        public bool BeaconChanneling;
        public float BeaconChannelStartTime;
        public float BeaconChannelDuration = ZombieModeTuning.BeaconChannelDurationSeconds;
        public bool ExtractionChanneling;
        public bool ExtractionSuccessHandled;
        public CountDownArea ActiveExtractionArea;
        public Vector3 ActiveSafeZoneCenter;
        public float ActiveSafeZoneRadius;
        public bool ActiveSafeZoneActive;
        public bool SafeZoneStealthBroken;
        public bool PlayerInsideSafeZone;
        public bool SafeZoneThreatSuppressed;
        public float LastSafeZoneTickTime;
        public GameObject ActiveSafeZoneVisual;
        public SimplePointOfInterest ActiveSafeZoneMapPoi;
        public int PollutionFromNatural;
        public int PollutionFromContracts;
        public int TotalPollution
        {
            get { return PollutionFromNatural + PollutionFromContracts; }
        }
        public int PollutionTier
        {
            get { return Math.Min(TotalPollution / 5, 5); }
        }
        public readonly Dictionary<string, float> ContractAffixWeights = new Dictionary<string, float>();
        public ZombieModeRewardNode CurrentRewardNode;
        public int FreeRefreshesRemainingCurrentNode;
        public int PaidRefreshIndexCurrentNode;
        public int PendingFreeRefreshNextNode;
        public bool HalfPriceNextPaidRefresh;
        public readonly Dictionary<string, float> AttributeBonuses = new Dictionary<string, float>();
        public readonly List<ZombieModeAttributeModifierRecord> AttributeModifierRecords = new List<ZombieModeAttributeModifierRecord>();
        public bool AttributeModifierCleanupRegistered;
        public readonly ZombieModeInsuranceState InsuranceState = new ZombieModeInsuranceState();
        public ZombieModePendingMapEventType PendingMapEvent = ZombieModePendingMapEventType.None;
        public int PendingEliteSquadCount;
        public readonly List<ZombieModeTemporaryNpc> TemporaryNpcs = new List<ZombieModeTemporaryNpc>();
        public float LastTemporaryNpcProtectionTickTime;
        public readonly List<ZombieModeDropCandidate> EntityDropCleanupCandidates = new List<ZombieModeDropCandidate>();
        public readonly List<ZombieModeBossDrop> BossDropEntries = new List<ZombieModeBossDrop>();
        public float LastPerformanceEvalTime;
        public bool ContainersRefilled;
        public ZombieModeStarterLoadout StarterLoadout = ZombieModeStarterLoadout.None;
        public string StarterAmmoCaliber = string.Empty;
        public readonly List<ZombieModeSpawnPoint> SpawnPoints = new List<ZombieModeSpawnPoint>();
        public readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects = new List<ZombieModeRunOnlyRecord>();

        public void ResetForNewRun(int runId, int sceneBuildIndex, string sceneName)
        {
            RunId = runId;
            SceneBuildIndex = sceneBuildIndex;
            SceneName = sceneName ?? string.Empty;
            MapProfile = null;
            LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
            CombatPhase = ZombieModeCombatPhase.None;
            ZombieModeResourcesCommitted = false;
            EntryResourcesFinalized = false;
            IsCleaningUp = false;
            PendingCashInvestment = 0L;
            ConfirmedCashInvested = 0L;
            PendingPurificationStars.Clear();
            CurrentWave = 0;
            CurrentWaveKillTarget = 0;
            CurrentWaveKills = 0;
            CurrentWaveBossInstances.Clear();
            CurrentWaveBossesRemaining = 0;
            PurificationPoints = 0;
            EffectiveSpawnPoints.Clear();
            PeriodicSpawnTimer = 0f;
            NextSpawnPointIndex = 0;
            PreparationTimer = 0f;
            LivingZombieCount = 0;
            LivingNormalZombieCount = 0;
            PendingNormalZombieSpawns = 0;
            PerformanceTier = ZombieModePerformanceTier.Normal;
            PendingRecycleQueue.Clear();
            BeaconChanneling = false;
            BeaconChannelStartTime = 0f;
            BeaconChannelDuration = ZombieModeTuning.BeaconChannelDurationSeconds;
            ExtractionChanneling = false;
            ExtractionSuccessHandled = false;
            ActiveExtractionArea = null;
            ActiveSafeZoneCenter = Vector3.zero;
            ActiveSafeZoneRadius = 0f;
            ActiveSafeZoneActive = false;
            SafeZoneStealthBroken = false;
            PlayerInsideSafeZone = false;
            SafeZoneThreatSuppressed = false;
            LastSafeZoneTickTime = 0f;
            ActiveSafeZoneVisual = null;
            ActiveSafeZoneMapPoi = null;
            PollutionFromNatural = 0;
            PollutionFromContracts = 0;
            ContractAffixWeights.Clear();
            CurrentRewardNode = null;
            FreeRefreshesRemainingCurrentNode = 0;
            PaidRefreshIndexCurrentNode = 0;
            PendingFreeRefreshNextNode = 0;
            HalfPriceNextPaidRefresh = false;
            AttributeBonuses.Clear();
            AttributeModifierRecords.Clear();
            AttributeModifierCleanupRegistered = false;
            InsuranceState.Reset();
            PendingMapEvent = ZombieModePendingMapEventType.None;
            PendingEliteSquadCount = 0;
            TemporaryNpcs.Clear();
            LastTemporaryNpcProtectionTickTime = 0f;
            EntityDropCleanupCandidates.Clear();
            BossDropEntries.Clear();
            LastPerformanceEvalTime = 0f;
            ContainersRefilled = false;
            StarterLoadout = ZombieModeStarterLoadout.None;
            StarterAmmoCaliber = string.Empty;
            SpawnPoints.Clear();
            RunOnlyObjects.Clear();
        }

        public void ClearRuntime()
        {
            RunOnlyObjects.Clear();
            SpawnPoints.Clear();
            EffectiveSpawnPoints.Clear();
            PendingPurificationStars.Clear();
            CurrentWaveBossInstances.Clear();
            PendingRecycleQueue.Clear();
            ContractAffixWeights.Clear();
            AttributeBonuses.Clear();
            AttributeModifierRecords.Clear();
            AttributeModifierCleanupRegistered = false;
            TemporaryNpcs.Clear();
            LastTemporaryNpcProtectionTickTime = 0f;
            EntityDropCleanupCandidates.Clear();
            BossDropEntries.Clear();
            InsuranceState.Reset();
            SceneBuildIndex = -1;
            SceneName = string.Empty;
            MapProfile = null;
            CombatPhase = ZombieModeCombatPhase.None;
            ZombieModeResourcesCommitted = false;
            EntryResourcesFinalized = false;
            IsCleaningUp = false;
            PendingCashInvestment = 0L;
            ConfirmedCashInvested = 0L;
            CurrentWave = 0;
            CurrentWaveKillTarget = 0;
            CurrentWaveKills = 0;
            CurrentWaveBossesRemaining = 0;
            PurificationPoints = 0;
            PeriodicSpawnTimer = 0f;
            NextSpawnPointIndex = 0;
            PreparationTimer = 0f;
            LivingZombieCount = 0;
            LivingNormalZombieCount = 0;
            PendingNormalZombieSpawns = 0;
            PerformanceTier = ZombieModePerformanceTier.Normal;
            BeaconChanneling = false;
            BeaconChannelStartTime = 0f;
            BeaconChannelDuration = ZombieModeTuning.BeaconChannelDurationSeconds;
            ExtractionChanneling = false;
            ExtractionSuccessHandled = false;
            ActiveExtractionArea = null;
            ActiveSafeZoneCenter = Vector3.zero;
            ActiveSafeZoneRadius = 0f;
            ActiveSafeZoneActive = false;
            SafeZoneStealthBroken = false;
            PlayerInsideSafeZone = false;
            SafeZoneThreatSuppressed = false;
            LastSafeZoneTickTime = 0f;
            ActiveSafeZoneVisual = null;
            ActiveSafeZoneMapPoi = null;
            PollutionFromNatural = 0;
            PollutionFromContracts = 0;
            CurrentRewardNode = null;
            FreeRefreshesRemainingCurrentNode = 0;
            PaidRefreshIndexCurrentNode = 0;
            PendingFreeRefreshNextNode = 0;
            HalfPriceNextPaidRefresh = false;
            PendingMapEvent = ZombieModePendingMapEventType.None;
            PendingEliteSquadCount = 0;
            LastPerformanceEvalTime = 0f;
            ContainersRefilled = false;
            StarterLoadout = ZombieModeStarterLoadout.None;
            StarterAmmoCaliber = string.Empty;
        }
    }
}
