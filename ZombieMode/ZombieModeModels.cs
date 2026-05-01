using System;
using System.Collections.Generic;
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

    public static class ZombieModeTuning
    {
        // ──────────────────────────────────────────────────────────
        // 节奏 / 倒计时（影响：玩家从准备到下一波的节奏感）
        // ──────────────────────────────────────────────────────────
        public const float PreparationCountdownSeconds = 30f;
        public const float BeaconChannelDurationSeconds = 3f;
        public const float ExtractionCountdownSeconds = 15f;
        public const float PeriodicSpawnIntervalSeconds = 30f;
        public const float SettlementMaxWaitSeconds = 3.5f;
        public const float BannerDurationSeconds = 2.5f;

        // ──────────────────────────────────────────────────────────
        // 安全区（影响：每波准备阶段的破隐节奏与可见性闪烁）
        // ──────────────────────────────────────────────────────────
        public const float SafeZoneTickIntervalSeconds = 0.2f;
        public const float SafeZoneFlashStartSeconds = 5f;
        public const float SafeZoneFlashCycleSeconds = 0.5f;
        public const float SafeZoneRadius = 8f;
        public const float SafeZoneCenterPlayerRange = 30f;
        public const float NavMeshSafeZoneRadius = 5f;

        // ──────────────────────────────────────────────────────────
        // 性能 / 回收（影响：高密度敌人时的 fps 稳定性）
        // ──────────────────────────────────────────────────────────
        public const float PerformanceEvalIntervalSeconds = 0.5f;
        public const float BossStuckTimeoutSeconds = 45f;
        public const int DropCleanupWaveAge = 3;
        public const float DropCleanupAgeSeconds = 300f;
        public const float PerformanceFarDistance = 60f;
        public const float StarMagnetRadius = 30f;
        public const float StarPickupDistance = 0.3f;
        public const int PerfTierWatch = 150;
        public const int PerfTierSoft = 250;
        public const int PerfTierExtreme = 350;
        public const int PerfTierHysteresis = 10;
        public const int MaxRecyclePerEval = 8;
        public const float PerfSoftSpawnMultiplier = 0.7f;
        public const float PerfExtremeSpawnMultiplier = 0.4f;
        public const float PerfExtremeSpawnFarSkipDistance = 50f;

        // ──────────────────────────────────────────────────────────
        // 刷怪点几何（影响：丧尸生成位置离玩家的距离与可达性）
        // ──────────────────────────────────────────────────────────
        public const float BossSpreadMinDistance = 8f;
        public const float NavMeshVirtualSpawnRadius = 10f;
        public const float SpawnPointNavMeshSampleRadius = 8f;
        public const float SpawnPointDuplicateDistance = 4f;
        public const float SpawnPointMinPlayerDistance = 12f;
        public const float NavMeshLiftOffset = 0.05f;

        // ──────────────────────────────────────────────────────────
        // 净化点数 / 奖励（影响：经济曲线与 reroll 体验）
        // ──────────────────────────────────────────────────────────
        public const int CashToPurificationRatio = 100;
        public const int FreeRefreshCapPerNode = 3;
        public static readonly int[] PaidRefreshCosts = { 100, 200, 350, 550, 800 };
        public const int InstantPurificationNormalBase = 150;
        public const int InstantPurificationBossBase = 400;
        public const float PurificationPollutionScalePerStep = 0.10f;
        public const int PurificationPollutionStep = 10;
        public const float PurificationPollutionScaleMax = 1.5f;
        public const int BossLootCrateBaseAtWave5 = 4;
        public const int BossLootCrateGrowthEvery5Waves = 1;
        public const int StarterMaxQuality = 5;
        public const int StarterGunnerExtraAmmoCount = 1000;

        // ──────────────────────────────────────────────────────────
        // 普通敌人 / 精英 / 特殊（影响：单只丧尸难度倍率）
        // ──────────────────────────────────────────────────────────
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

        // ──────────────────────────────────────────────────────────
        // 词缀 / 死亡爆裂 / 自适应抗性（影响：精英战斗节奏）
        // ──────────────────────────────────────────────────────────
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

        // ──────────────────────────────────────────────────────────
        // Boss 技能（影响：5 种 Boss 战的循环节奏与威胁度）
        // 5 类共享倍率统一在这里；各 kind 独占字段紧跟其后。
        // ──────────────────────────────────────────────────────────

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

        // 撤离区域几何
        public const float ExtractionAreaTriggerRadius = 2.0f;
        public const float ExtractionAreaLeaveRadius = 2.5f;
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
    }

    public sealed class ZombieModeTitanState : ZombieModeBossSkillState
    {
        public float NextShockwaveTime;
        public float NextDamageReductionTime;
        public bool DamageReductionActive;
        public float DamageReductionEndTime;

        public override void Reset(float now, float bossScale)
        {
            NextShockwaveTime = now + UnityEngine.Random.Range(2f, 5f);
            NextDamageReductionTime = now + UnityEngine.Random.Range(8f, 12f);
        }
    }

    public sealed class ZombieModeHunterState : ZombieModeBossSkillState
    {
        public float NextDashTime;
        public bool FrenzyActive;
        public float FrenzyEndTime;
        public float FrenzyOriginalScale = 1f;

        public override void Reset(float now, float bossScale)
        {
            NextDashTime = now + UnityEngine.Random.Range(2f, 4f);
            FrenzyOriginalScale = bossScale;
        }
    }

    public sealed class ZombieModeSplitterState : ZombieModeBossSkillState
    {
        public float NextSummonTime;
        public bool FirstSplitTriggered;
        public bool SecondSplitTriggered;

        public override void Reset(float now, float bossScale)
        {
            NextSummonTime = now + UnityEngine.Random.Range(3f, 6f);
        }
    }

    public sealed class ZombieModeShielderState : ZombieModeBossSkillState
    {
        public float NextSelfShieldTime;
        public float NextGroupShieldTime;

        public override void Reset(float now, float bossScale)
        {
            NextSelfShieldTime = now + UnityEngine.Random.Range(4f, 8f);
            NextGroupShieldTime = now + UnityEngine.Random.Range(10f, 14f);
        }
    }

    public sealed class ZombieModeCorruptorState : ZombieModeBossSkillState
    {
        public float NextZoneTime;
        public float NextPoisonPathTime;

        public override void Reset(float now, float bossScale)
        {
            NextZoneTime = now + UnityEngine.Random.Range(2f, 5f);
            NextPoisonPathTime = now + UnityEngine.Random.Range(1f, 3f);
        }
    }

    public sealed class ZombieModeBossInstance
    {
        public CharacterMainControl Character;
        public ZombieModeBossKind Kind;
        public bool Alive;
        public bool LootSettled;
        public bool PointsSettled;
        public Vector3 LastKnownPosition;
        public float LastReachableTime;
        public float LastHurtTime;
        public bool RuntimeRegistered;
        public float NextSkillTime;
        public int SkillSequence;

        // per-kind 技能状态（按 Kind 实例化对应子类）
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
        public float PreparationTimer;
        public int LivingZombieCount;
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
        public readonly ZombieModeInsuranceState InsuranceState = new ZombieModeInsuranceState();
        public ZombieModePendingMapEventType PendingMapEvent = ZombieModePendingMapEventType.None;
        public int PendingEliteSquadCount;
        public readonly List<ZombieModeTemporaryNpc> TemporaryNpcs = new List<ZombieModeTemporaryNpc>();
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
            PreparationTimer = 0f;
            LivingZombieCount = 0;
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
            InsuranceState.Reset();
            PendingMapEvent = ZombieModePendingMapEventType.None;
            PendingEliteSquadCount = 0;
            TemporaryNpcs.Clear();
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
            TemporaryNpcs.Clear();
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
            PreparationTimer = 0f;
            LivingZombieCount = 0;
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
