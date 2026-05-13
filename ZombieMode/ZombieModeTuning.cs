using System.Collections.Generic;

namespace BossRush
{
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
        public const string GunCritRateGain = "GunCritRateGain";
        public const string ReloadSpeedGain = "ReloadSpeedGain";
        public const string DashSpeed = "DashSpeed";
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
            public const float TemporaryNpcProtectionTickIntervalSeconds = 0.25f;
            public const float BossStuckTimeoutSeconds = 45f;
            public const int DropCleanupWaveAge = 3;
            public const float DropCleanupAgeSeconds = 300f;
            public const float FarDistance = 60f;
            public const float StarMagnetRadius = 30f;
            public const float StarPickupDistance = 0.3f;
        }

        private static class Spawn
        {
            public const float BossSpreadMinDistance = 8f;
            public const float NavMeshVirtualSpawnRadius = 10f;
            public const float NavMeshSampleRadius = 8f;
            public const float DuplicateDistance = 4f;
            public const float MinPlayerDistance = 12f;
            public const float NavMeshLiftOffset = 0.05f;
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
            public const float HarasserSlowRadius = 3.5f;
            public const float HarasserSlowPercent = 0.50f;
            public const float HarasserSlowDurationSeconds = 2f;
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
        public const float TemporaryNpcProtectionTickIntervalSeconds = Performance.TemporaryNpcProtectionTickIntervalSeconds;
        public const float BossStuckTimeoutSeconds = Performance.BossStuckTimeoutSeconds;
        public const int DropCleanupWaveAge = Performance.DropCleanupWaveAge;
        public const float DropCleanupAgeSeconds = Performance.DropCleanupAgeSeconds;
        public const float StarMagnetRadius = Performance.StarMagnetRadius;
        public const float StarPickupDistance = Performance.StarPickupDistance;

        // Spawn
        public const float BossSpreadMinDistance = Spawn.BossSpreadMinDistance;
        public const float NavMeshVirtualSpawnRadius = Spawn.NavMeshVirtualSpawnRadius;
        public const float SpawnPointNavMeshSampleRadius = Spawn.NavMeshSampleRadius;
        public const float SpawnPointDuplicateDistance = Spawn.DuplicateDistance;
        public const float SpawnPointMinPlayerDistance = Spawn.MinPlayerDistance;
        public const float NavMeshLiftOffset = Spawn.NavMeshLiftOffset;
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
        public const float HarasserSlowRadius = Combat.HarasserSlowRadius;
        public const float HarasserSlowPercent = Combat.HarasserSlowPercent;
        public const float HarasserSlowDurationSeconds = Combat.HarasserSlowDurationSeconds;

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

        /// <summary>
        /// 判断当前是否处于"丧尸模式运行中"状态（入场流程完成后、清理完成前）
        /// </summary>
        public static bool IsRunActive(ZombieModeLifecyclePhase lifecycle)
        {
            return lifecycle == ZombieModeLifecyclePhase.WaitingStarterChoice ||
                   lifecycle == ZombieModeLifecyclePhase.Active ||
                   lifecycle == ZombieModeLifecyclePhase.Exiting;
        }

        /// <summary>
        /// 判断当前是否处于结算流程（结算/奖励选择）
        /// </summary>
        public static bool IsSettling(ZombieModeCombatPhase combat)
        {
            return combat == ZombieModeCombatPhase.Settling ||
                   combat == ZombieModeCombatPhase.RewardSelection;
        }
    }
}
