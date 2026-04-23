// ============================================================================
// PhantomWitchConfig.cs - 幽灵女巫Boss配置
// ============================================================================
// 模块说明：
//   定义幽灵女巫Boss的所有可配置参数
//   近战核心：闪现贴身 + 诅咒范围技 + 镰刀重斩 + 二阶段召唤
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 幽灵女巫Boss阶段枚举
    /// </summary>
    public enum PhantomWitchPhase
    {
        Phase1,
        Phase2,
        Phase3,
        Transitioning,
        Dead
    }

    /// <summary>
    /// 幽灵女巫战术包类型
    /// </summary>
    public enum PhantomWitchAttackPackageType
    {
        FlankPressure,
        MidrangeRequiem,
        WraithTrailObserve,
        MidrangeDouble,
        CurseTrap,
        ShortDriftPressure,
        LastStandSummon,
        MinionRetreat
    }

    /// <summary>
    /// 幽灵女巫残局双幽灵职责
    /// </summary>
    public enum PhantomWitchMinionRole
    {
        Sustain,
        Harass
    }

    /// <summary>
    /// 幽灵女巫隐身形态
    /// </summary>
    public enum PhantomWitchStealthMode
    {
        TrueStealthTransition,
        SemiStealthWindup,
        Visible
    }

    /// <summary>
    /// 幽灵女巫Boss配置参数
    /// </summary>
    public static class PhantomWitchConfig
    {
        // ========== Boss基础属性 ==========

        public const float BaseHealth = 1000f;
        public const float DamageMultiplier = 1.1f;
        /// <summary>
        /// BossRush 中幽灵女巫模型整体缩放倍率（2f = 放大 1 倍）。
        /// 放大后碰撞/近战范围随 transform 一起变大，让 Boss 更具威慑感。
        /// </summary>
        public const float BossModelScale = 2f;
        public const string BossNameKey = "boss_phantomwitch";
        public const string BossNameCN = "幽灵女巫";
        public const string BossNameEN = "Phantom Witch";
        public const string ScytheNameCN = "噬魂挽歌";
        public const string ScytheNameEN = "Soulreaper's Requiem";

        // 预留正式镰刀 TypeID；当前版本缺资源时回退到断界戟占位。
        public const int ReservedScytheTypeId = 500044;
        public const int PlaceholderScytheTypeId = FenHuangHalberdIds.WeaponTypeId;

        // ========== 阶段参数 ==========

        public const float Phase2HealthThreshold = 0.6f;
        public const float Phase3HealthThreshold = 0.25f;
        public const float Phase1PackageInterval = 1.2f;
        public const float Phase2PackageInterval = 0.85f;
        public const float Phase3PackageInterval = 1.10f;
        public const float Phase1StealthRatioTarget = 0.38f;
        public const float Phase2StealthRatioTarget = 0.32f;
        public const float Phase3StealthRatioTarget = 0.18f;
        public const float StealthRatioTolerance = 0.05f;
        public const float TrueStealthMaxDuration = 1.1f;
        public const float MinionCensusInterval = 3.0f;

        // ========== 传送参数 ==========

        public const float BlinkMinDistance = 1f;
        public const float BlinkMaxDistance = 4f;
        public const float BlinkRecovery = 0f;
        public const float BlinkHideDuration = 0.3f;
        public const float NavMeshSampleRadius = 2f;
        public const float NavMeshFallbackRadius = 5f;
        public const float BlinkFallbackDistance = 6f;
        public const float BlinkTrackedOffsetDistance = 2.2f;
        public const float BlinkTrackedTelegraphDuration = 2f;
        public const float BlinkTrackedFlashLeadDuration = 0.1f;

        // ========== 诅咒范围技参数 ==========

        public const float CurseAuraWindup = 0.45f;
        public const float CurseAuraRecovery = 0.1f;
        public const float CurseAuraRadius = 3.5f;
        public const float CurseAuraDamage = 12f;

        // ========== 镰刀横扫参数 ==========

        public const float ScytheSweepWindup = 0.35f;
        public const float ScytheSweepRecovery = 0.1f;
        public const float ScytheSweepDamage = 18f;
        public const float ScytheSweepRadius = 3.1f;
        public const float ScytheSweepHalfAngle = 85f;
        public const float ScytheSweepForwardOffset = 1.15f;

        // ========== 镰刀重斩参数 ==========

        public const float HeavyScytheSlashWindup = 0.5f;
        public const float HeavyScytheSlashRecovery = 0.1f;
        public const float HeavyScytheSlashDamage = 30f;
        public const float HeavyScytheSlashRadius = 3.6f;
        public const float HeavyScytheSlashHalfAngle = 65f;
        public const float HeavyScytheSlashForwardOffset = 1.35f;
        public const float HeavySlashBlinkMinDistance = 1.8f;
        public const float HeavySlashBlinkMaxDistance = 3.6f;

        // ========== Boss 诅咒领域参数（镰刀右键技能） ==========

        public const float BossCurseRealmWindup = 0.5f;
        public const float BossCurseRealmRecovery = 0.1f;
        public const float BossCurseRealmRadius = 4.5f;
        public const float BossCurseRealmDuration = 4f;
        public const float BossCurseRealmDamagePerTick = 15f;
        public const float BossCurseRealmDamageInterval = 0.5f;
        public const float BossCurseRealmTeleportMinDist = 1.4f;
        public const float BossCurseRealmTeleportMaxDist = 2.5f;
        public const float CurseRealmWarningDuration = 1.05f;
        public const float CurseRealmWarningMinRadius = 0.4f;
        public const float CurseRealmPhase3RadiusScale = 0.8f;
        public const float CurseRealmPhase3DurationScale = 0.75f;
        public const bool PhaseTransitionClearsActiveRealm = true;

        // ========== 召唤参数 ==========

        public const int MaxMinions = 2;
        public const int PairFillSpawnsPerPackage = 2;
        public const float SummonWindup = 1.0f;
        public const float SummonRecovery = 0.1f;
        public const float MinionHealth = 150f;
        public const float MinionHealPerSecond = 15f;
        public const float MinionSpawnDistance = 3f;
        public const float MinionForceTraceDistance = 500f;
        public const float MinionPairFrameGap = 0.034f;
        public const float SustainMinionHealRate = 6.0f;
        public const float SustainProximityBonusMultiplier = 1.5f;
        public const float SustainProximityRadius = 6.0f;
        public const float HarassMinionPressureRadius = 3.2f;
        public const float HarassMinionPressureInterval = 2.4f;

        // ========== Boss 专属中距招式参数 ==========

        public const float RequiemArcWindup = 0.55f;
        public const float RequiemArcRange = 4.8f;
        public const float RequiemArcDamage = 16f;
        public const float WraithWindupDuration = 0.45f;
        public const float WraithTrailDelay = 0.30f;
        public const float WraithWindupMinGate = 0.40f;
        public const float WraithTrailDamage = 18f;
        public const float WraithWindupOutlineRadius = 3.0f;
        public const float WraithBodySinkDepth = 0.06f;

        // ========== 模型复用 ==========

        public const string BasePresetNameKey = "Cname_Ghost";
        public const string MinionPresetNameKey = "Cname_Ghost";
        public const string FallbackPresetNameKey = "Cname_Boss_Red";

        // ========== 代码特效配置 ==========

        public static readonly UnityEngine.Color EffectColor = new UnityEngine.Color(0.6f, 0.1f, 0.9f, 0.8f);
        public const float EffectEmissionMultiplier = 2f;
        public const float EffectDefaultDuration = 1.5f;

        // ========== Boss 特效配色 ==========

        public static readonly UnityEngine.Color TeleportRingColor = new UnityEngine.Color(0.68f, 0.25f, 1.00f, 0.85f);
        public static readonly UnityEngine.Color CurseAuraGroundColor = new UnityEngine.Color(0.40f, 0.10f, 0.65f, 0.45f);
        public static readonly UnityEngine.Color CurseAuraRingColor = new UnityEngine.Color(0.65f, 0.25f, 0.95f, 0.80f);
        public static readonly UnityEngine.Color SweepArcColor = new UnityEngine.Color(0.85f, 0.45f, 1.00f, 0.85f);
        public static readonly UnityEngine.Color HeavySlashColor = new UnityEngine.Color(0.90f, 0.50f, 1.00f, 0.90f);
        public static readonly UnityEngine.Color SummonCircleColor = new UnityEngine.Color(0.58f, 0.18f, 0.95f, 0.80f);
        public static readonly UnityEngine.Color SummonPentagramColor = new UnityEngine.Color(0.75f, 0.35f, 1.00f, 0.65f);
        public static readonly UnityEngine.Color PhaseTransitionColor = new UnityEngine.Color(0.80f, 0.35f, 1.00f, 0.85f);
        public static readonly UnityEngine.Color DamageHitFlashColor = new UnityEngine.Color(0.95f, 0.75f, 1.00f, 0.80f);
        public static readonly UnityEngine.Color FxParticlePurple = new UnityEngine.Color(0.75f, 0.40f, 1.00f, 0.85f);
        public static readonly UnityEngine.Color RuneMarkWhite = new UnityEngine.Color(1.00f, 0.92f, 1.00f, 0.80f);

        // ========== 幽灵女巫重做色板 ==========

        public static readonly UnityEngine.Color VioletVoidCore = new UnityEngine.Color(0.24f, 0.10f, 0.36f, 1f);
        public static readonly UnityEngine.Color VioletVoidMid = new UnityEngine.Color(0.42f, 0.23f, 0.62f, 1f);
        public static readonly UnityEngine.Color VioletVoidVeil = new UnityEngine.Color(0.57f, 0.44f, 0.72f, 1f);
        public static readonly UnityEngine.Color VioletVoidDust = new UnityEngine.Color(0.79f, 0.73f, 0.85f, 1f);
        public static readonly UnityEngine.Color SilverAshCore = new UnityEngine.Color(0.90f, 0.89f, 0.85f, 1f);
        public static readonly UnityEngine.Color SilverAshMid = new UnityEngine.Color(0.72f, 0.71f, 0.67f, 1f);
        public static readonly UnityEngine.Color SilverAshVeil = new UnityEngine.Color(0.56f, 0.55f, 0.52f, 1f);
        public static readonly UnityEngine.Color SilverAshDust = new UnityEngine.Color(0.36f, 0.35f, 0.33f, 1f);
        public static readonly UnityEngine.Color BloodRoseCore = new UnityEngine.Color(0.48f, 0.12f, 0.25f, 1f);
        public static readonly UnityEngine.Color BloodRoseMid = new UnityEngine.Color(0.71f, 0.28f, 0.46f, 1f);
        public static readonly UnityEngine.Color BloodRoseVeil = new UnityEngine.Color(0.85f, 0.61f, 0.71f, 1f);
        public static readonly UnityEngine.Color GhostBreathCore = new UnityEngine.Color(0.83f, 0.91f, 0.94f, 1f);
        public static readonly UnityEngine.Color GhostBreathMid = new UnityEngine.Color(0.62f, 0.76f, 0.82f, 1f);
        public static readonly UnityEngine.Color GhostBreathVeil = new UnityEngine.Color(0.42f, 0.56f, 0.63f, 1f);

        // ========== 待机气息系统 ==========

        public const float AmbientHaloBreathPeriod = 1.2f;
        public const float AmbientHaloAlphaMin = 0.05f;
        public const float AmbientHaloAlphaMax = 0.15f;
        public const float AmbientHaloAlphaCloseBonus = 0.10f;
        public const float AmbientHaloPhase2Bonus = 0.05f;
        public const float AmbientHeartbeatPulseDuration = 0.15f;
        public const float AmbientHeartbeatMinInterval = 3f;
        public const float AmbientHeartbeatMaxInterval = 5f;
        public const float AmbientHeartbeatLowHealthMinInterval = 1f;
        public const float AmbientHeartbeatLowHealthMaxInterval = 2f;
        public const float AmbientRuneFlashMinInterval = 8f;
        public const float AmbientRuneFlashMaxInterval = 14f;

        // ========== Boss 特效参数 ==========

        public const int FxRingSegments = 48;
        public const int FxArcSegments = 16;
        public const int FxSmallRingSegments = 32;
        public const int FxHitRingSegments = 24;
        public const float TeleportFxDuration = 0.5f;
        public const float TeleportShrinkRadius = 1.2f;
        public const float TeleportExpandRadius = 1.5f;
        public const float BlinkTrackedMarkerFxDuration = 0.9f;
        public const float CurseAuraFxDuration = 1.3f;
        public const float SweepFxDuration = 0.3f;
        public const float HeavySlashFxDuration = 0.8f;
        public const float SummonCircleFxDuration = 2.5f;
        public const float SummonCircleRadius = 2.5f;
        public const float SummonPentagramRadius = 1.8f;
        public const float MinionSpawnFxDuration = 0.8f;
        public const float MinionSpawnFxRadius = 0.6f;
        public const float DamageHitFxDuration = 0.25f;
        public const float PhaseTransitionFxDuration = 2.5f;
        public const float PhaseTransitionRadius = 8f;
        public const float PhaseTransitionInnerRadius = 5f;
        public const float DeathFxDuration = 1.8f;
        public const float DeathFxRadius = 2.8f;
        public const int FxReducedRingSegments = 24;
        public const int FxReducedArcSegments = 10;
        public const int FxReducedSmallRingSegments = 18;
        public const int FxReducedHitRingSegments = 16;
        public const int FxMinimalRingSegments = 16;
        public const int FxMinimalArcSegments = 6;
        public const int FxMinimalSmallRingSegments = 12;
        public const int FxMinimalHitRingSegments = 12;
        public const int FxReducedActiveRootThreshold = 6;
        public const int FxMinimalActiveRootThreshold = 10;

        // ========== 诅咒Buff运行时构建参数 ==========

        public const int CurseBuffID = 500043;
        public const float CurseBuffDuration = 5f;
        public const int CurseBuffMaxLayers = 3;
        public const float CurseSlowPerLayer = -0.3f;

        // ========== 战术包序列 ==========

        public static readonly PhantomWitchAttackPackageType[] Phase1Packages = new PhantomWitchAttackPackageType[]
        {
            PhantomWitchAttackPackageType.FlankPressure,
            PhantomWitchAttackPackageType.MidrangeRequiem,
            PhantomWitchAttackPackageType.WraithTrailObserve
        };

        public static readonly PhantomWitchAttackPackageType[] Phase2Packages = new PhantomWitchAttackPackageType[]
        {
            PhantomWitchAttackPackageType.FlankPressure,
            PhantomWitchAttackPackageType.MidrangeDouble,
            PhantomWitchAttackPackageType.CurseTrap,
            PhantomWitchAttackPackageType.FlankPressure
        };

        public static readonly PhantomWitchAttackPackageType[] Phase3Packages = new PhantomWitchAttackPackageType[]
        {
            PhantomWitchAttackPackageType.ShortDriftPressure,
            PhantomWitchAttackPackageType.LastStandSummon,
            PhantomWitchAttackPackageType.CurseTrap,
            PhantomWitchAttackPackageType.MinionRetreat
        };

        // ========== 阶段切换提示 ==========

        public const string Phase2MessageCN = "幽灵女巫的诅咒扩散开来，镰刀变得更加狂暴！";
        public const string Phase2MessageEN = "The Phantom Witch's curse spreads outward, and her scythe grows more violent!";
        public const string Phase3MessageCN = "残喘之影——她已无法满场游弋，却仍不肯离去。";
        public const string Phase3MessageEN = "Dwindling Wraith - she can no longer roam, yet refuses to leave.";
        public const string SpawnMessageCN = "幽灵女巫出现了！";
        public const string SpawnMessageEN = "The Phantom Witch has appeared!";
        public const string DefeatedMessageCN = "幽灵女巫被击败了！";
        public const string DefeatedMessageEN = "The Phantom Witch has been defeated!";
        public const bool TelemetryEnabled = true;
    }
}
