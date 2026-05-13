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
        TempGoblinNpc,
        TempNurseNpc,
        TempCourierNpc,
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
        MapEventEliteSquad,
        ProjectilePenetration,
        ProjectileBurn,
        ProjectileCold,
        ProjectilePoison,
        ProjectileArmorBreak,
        TriggerLifesteal,
        TriggerLifestealMedium,
        TriggerLifestealLarge,
        TriggerCritBurst,
        TriggerPurificationSiphon,
        TriggerSecondWind,
        TriggerDoomPulse,
        MutatorCritFocus,
        MutatorBulletTime,
        MutatorGuardianShield,
        MutatorQuickReload,
        MutatorDashBoost,
        BattlefieldAmmoRain,
        ContractDevilBargain,
        ContractCursedReload,
        ContractBloodPrice,
        ContractCursePool,
        ProjectileTrident,
        ProjectileShotgunSpray,
        ProjectileStasis,
        ProjectileRicochet,
        ProjectileFork,
        ProjectileReturn,
        ProjectileHelix,
        ProjectileTrail,
        BattlefieldPurgeAura,
        BattlefieldCurseTrap,
        BattlefieldBlackHole,
        BattlefieldGravityDrag
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
        MapEvent,
        ProjectileMod,
        Trigger,
        Mutator,
        Battlefield
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

    public enum ZombieModeBossKind
    {
        Titan,
        Hunter,
        Splitter,
        Shielder,
        Corruptor
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

    public sealed class ZombieModeOptionRuntimeState
    {
        public int ProjectilePenetrationStacks;
        public int ProjectileBurnStacks;
        public int ProjectileColdStacks;
        public int ProjectilePoisonStacks;
        public int ProjectileArmorBreakStacks;
        public int TriggerLifestealStacks;
        public int TriggerLifestealChancePercent;
        public int TriggerLifestealHealAmount;
        public int TriggerCritBurstStacks;
        public int TriggerPurificationSiphonStacks;
        public int TriggerSecondWindStacks;
        public int TriggerDoomPulseStacks;
        public int MutatorCritFocusStacks;
        public bool MutatorBulletTimeEnabled;
        public bool MutatorGuardianShieldEnabled;
        public int MutatorQuickReloadStacks;
        public int MutatorDashBoostStacks;
        public int BattlefieldAmmoRainStacks;
        public int ProjectileTridentStacks;
        public int ProjectileShotgunSprayStacks;
        public int ProjectileStasisStacks;
        public int ProjectileRicochetStacks;
        public int ProjectileForkStacks;
        public int ProjectileReturnStacks;
        public int ProjectileHelixStacks;
        public int ProjectileTrailStacks;
        public int BattlefieldPurgeAuraStacks;
        public int BattlefieldCurseTrapStacks;
        public int BattlefieldBlackHoleStacks;
        public int BattlefieldGravityDragStacks;
        public bool BattlefieldAreaRuntimeStarted;
        public bool BattlefieldGravityRuntimeStarted;
        public bool PlayerHealthListenerRegistered;
        public float LastBulletTimeTriggerTime = -999f;
        public float LastCritBurstTriggerTime = -999f;
        public float LastTrajectorySupportTriggerTime = -999f;
        public float LastProjectileTrailDamageTime = -999f;
        public bool GuardianShieldActive;
        public int DoomPulseKillCounter;
        public int ElementalShotCursor;
        public bool AmmoRainCoroutineStarted;
        public float OptionTradeoffMoveSpeedPenalty;
        public float OptionTradeoffGunDamagePenalty;
        public float OptionTradeoffReloadSpeedPenalty;
        public float OptionTradeoffDamageTakenPenalty;
        public float OptionTradeoffMaxHealthPenalty;
        public readonly List<ZombieModeAttributeModifierRecord> ModifierRecords = new List<ZombieModeAttributeModifierRecord>();
        public readonly List<ZombieModeAttributeModifierRecord> GuardianShieldRecords = new List<ZombieModeAttributeModifierRecord>();
        public readonly List<ZombieModeAttributeModifierRecord> ContractRuntimeModifierRecords = new List<ZombieModeAttributeModifierRecord>();

        public void Reset()
        {
            ProjectilePenetrationStacks = 0;
            ProjectileBurnStacks = 0;
            ProjectileColdStacks = 0;
            ProjectilePoisonStacks = 0;
            ProjectileArmorBreakStacks = 0;
            TriggerLifestealStacks = 0;
            TriggerLifestealChancePercent = 0;
            TriggerLifestealHealAmount = 0;
            TriggerCritBurstStacks = 0;
            TriggerPurificationSiphonStacks = 0;
            TriggerSecondWindStacks = 0;
            TriggerDoomPulseStacks = 0;
            MutatorCritFocusStacks = 0;
            MutatorBulletTimeEnabled = false;
            MutatorGuardianShieldEnabled = false;
            MutatorQuickReloadStacks = 0;
            MutatorDashBoostStacks = 0;
            BattlefieldAmmoRainStacks = 0;
            ProjectileTridentStacks = 0;
            ProjectileShotgunSprayStacks = 0;
            ProjectileStasisStacks = 0;
            ProjectileRicochetStacks = 0;
            ProjectileForkStacks = 0;
            ProjectileReturnStacks = 0;
            ProjectileHelixStacks = 0;
            ProjectileTrailStacks = 0;
            BattlefieldPurgeAuraStacks = 0;
            BattlefieldCurseTrapStacks = 0;
            BattlefieldBlackHoleStacks = 0;
            BattlefieldGravityDragStacks = 0;
            BattlefieldAreaRuntimeStarted = false;
            BattlefieldGravityRuntimeStarted = false;
            PlayerHealthListenerRegistered = false;
            LastBulletTimeTriggerTime = -999f;
            LastCritBurstTriggerTime = -999f;
            LastTrajectorySupportTriggerTime = -999f;
            LastProjectileTrailDamageTime = -999f;
            GuardianShieldActive = false;
            DoomPulseKillCounter = 0;
            ElementalShotCursor = 0;
            AmmoRainCoroutineStarted = false;
            OptionTradeoffMoveSpeedPenalty = 0f;
            OptionTradeoffGunDamagePenalty = 0f;
            OptionTradeoffReloadSpeedPenalty = 0f;
            OptionTradeoffDamageTakenPenalty = 0f;
            OptionTradeoffMaxHealthPenalty = 0f;
            ModifierRecords.Clear();
            GuardianShieldRecords.Clear();
            ContractRuntimeModifierRecords.Clear();
        }
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

    public sealed class ZombieModeTemporaryRealNpcRecord
    {
        public GameObject GameObject;
        public string NpcType = string.Empty;
        public int SpawnWave;
        public bool SafeZoneBound;
    }

    public sealed class ZombieModeTemporaryNpcProtectionMarker : MonoBehaviour
    {
        public int RunId;
        public string ServiceType = string.Empty;
    }

    public sealed class ZombieModeTemporaryRealNpcMarker : MonoBehaviour
    {
        public int RunId;
        public string NpcType = string.Empty;
        public bool UsesPurificationPayment = true;
    }

    public sealed class ZombieModeNpcServiceState
    {
        public bool BossNodeStock;
        public readonly List<int> MerchantStockRemaining = new List<int>();
        public readonly List<int> NurseUsesRemaining = new List<int>();
        public bool SafeZoneBound;
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
        public readonly List<ItemStatsSystem.Data.ItemTreeData> InventoryTransferredInboxItems = new List<ItemStatsSystem.Data.ItemTreeData>();
        public readonly List<string> BlockingMessages = new List<string>();

        public void Reset()
        {
            InvitationTemporarilyHeld = false;
            CashTemporarilyHeld = false;
            InventoryTransferStarted = false;
            EntryResourcesFinalized = false;
            CashWithheldAmount = 0L;
            InventoryTransferredItems.Clear();
            InventoryTransferredInboxItems.Clear();
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
        public ZombieModeRewardNode CurrentRewardNode;
        public int FreeRefreshesRemainingCurrentNode;
        public int PaidRefreshIndexCurrentNode;
        public int PendingFreeRefreshNextNode;
        public bool HalfPriceNextPaidRefresh;
        public readonly Dictionary<string, float> AttributeBonuses = new Dictionary<string, float>();
        public readonly List<ZombieModeAttributeModifierRecord> AttributeModifierRecords = new List<ZombieModeAttributeModifierRecord>();
        public bool AttributeModifierCleanupRegistered;
        public int GuaranteedMerchantPurchaseMinQuality;
        public bool GuaranteedMerchantPurchasePending;
        public readonly ZombieModeInsuranceState InsuranceState = new ZombieModeInsuranceState();
        public ZombieModePendingMapEventType PendingMapEvent = ZombieModePendingMapEventType.None;
        public int PendingEliteSquadCount;
        public readonly List<ZombieModeTemporaryNpc> TemporaryNpcs = new List<ZombieModeTemporaryNpc>();
        public readonly List<ZombieModeTemporaryRealNpcRecord> TemporaryRealNpcs = new List<ZombieModeTemporaryRealNpcRecord>();
        public float LastTemporaryNpcProtectionTickTime;
        public readonly List<ZombieModeDropCandidate> EntityDropCleanupCandidates = new List<ZombieModeDropCandidate>();
        public readonly List<ZombieModeBossDrop> BossDropEntries = new List<ZombieModeBossDrop>();
        public bool ContainersRefilled;
        public ZombieModeStarterLoadout StarterLoadout = ZombieModeStarterLoadout.None;
        public string StarterAmmoCaliber = string.Empty;
        public readonly List<ZombieModeSpawnPoint> SpawnPoints = new List<ZombieModeSpawnPoint>();
        public readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects = new List<ZombieModeRunOnlyRecord>();
        public readonly ZombieModeOptionRuntimeState OptionRuntime = new ZombieModeOptionRuntimeState();

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
            AttributeBonuses.Clear();
            AttributeModifierRecords.Clear();
            AttributeModifierCleanupRegistered = false;
            GuaranteedMerchantPurchaseMinQuality = 0;
            GuaranteedMerchantPurchasePending = false;
            InsuranceState.Reset();
            PendingMapEvent = ZombieModePendingMapEventType.None;
            PendingEliteSquadCount = 0;
            TemporaryNpcs.Clear();
            TemporaryRealNpcs.Clear();
            LastTemporaryNpcProtectionTickTime = 0f;
            EntityDropCleanupCandidates.Clear();
            BossDropEntries.Clear();
            ContainersRefilled = false;
            StarterLoadout = ZombieModeStarterLoadout.None;
            StarterAmmoCaliber = string.Empty;
            SpawnPoints.Clear();
            RunOnlyObjects.Clear();
            OptionRuntime.Reset();
        }

        public void ClearRuntime()
        {
            RunOnlyObjects.Clear();
            SpawnPoints.Clear();
            EffectiveSpawnPoints.Clear();
            PendingPurificationStars.Clear();
            CurrentWaveBossInstances.Clear();
            AttributeBonuses.Clear();
            AttributeModifierRecords.Clear();
            AttributeModifierCleanupRegistered = false;
            GuaranteedMerchantPurchaseMinQuality = 0;
            GuaranteedMerchantPurchasePending = false;
            TemporaryNpcs.Clear();
            TemporaryRealNpcs.Clear();
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
            ContainersRefilled = false;
            StarterLoadout = ZombieModeStarterLoadout.None;
            StarterAmmoCaliber = string.Empty;
            OptionRuntime.Reset();
        }

        [System.Diagnostics.Conditional("DEBUG")]
        internal void ValidatePhaseConsistency()
        {
            if (LifecyclePhase != ZombieModeLifecyclePhase.Active &&
                LifecyclePhase != ZombieModeLifecyclePhase.Exiting)
            {
                if (CombatPhase != ZombieModeCombatPhase.None &&
                    CombatPhase != ZombieModeCombatPhase.SuccessExit &&
                    CombatPhase != ZombieModeCombatPhase.FailedExit)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[ZombieMode] 状态不一致: LifecyclePhase={LifecyclePhase}, CombatPhase={CombatPhase}");
                }
            }
        }
    }
}
