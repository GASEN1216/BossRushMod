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
    /// 幽灵女巫攻击类型枚举
    /// </summary>
    public enum PhantomWitchAttackType
    {
        Blink,            // 短距传送
        CurseAura,        // 近距离诅咒光环
        ScytheSweep,      // 镰刀横扫
        HeavyScytheSlash, // 二阶段重斩
        SummonMinions,    // 召唤随从（Phase2专属）
        CurseRealm        // 诅咒领域（镰刀右键技能）
    }

    /// <summary>
    /// 幽灵女巫Boss阶段枚举
    /// </summary>
    public enum PhantomWitchPhase
    {
        Phase1,
        Phase2,
        Transitioning,
        Dead
    }

    /// <summary>
    /// 幽灵女巫Boss配置参数
    /// </summary>
    public static class PhantomWitchConfig
    {
        // ========== Boss基础属性 ==========

        public const float BaseHealth = 700f;
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

        public const float Phase2HealthThreshold = 0.5f;
        public const float Phase1AttackInterval = 1.2f;
        public const float Phase2AttackInterval = 0.8f;

        // ========== 传送参数 ==========

        public const float BlinkMinDistance = 1f;
        public const float BlinkMaxDistance = 4f;
        public const float BlinkRecovery = 0.1f;
        public const float BlinkHideDuration = 0.22f;
        public const float NavMeshSampleRadius = 3f;
        public const float NavMeshFallbackRadius = 5f;
        public const float BlinkFallbackDistance = 6f;

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

        // ========== 召唤参数 ==========

        public const int MaxMinions = 3;
        public const int SpawnPerSummon = 2;
        public const float SummonWindup = 1.0f;
        public const float SummonRecovery = 0.1f;
        public const float MinionHealth = 150f;
        public const float MinionHealPerSecond = 15f;
        public const float MinionSpawnDistance = 3f;
        public const float MinionForceTraceDistance = 500f;

        // ========== 模型复用 ==========

        public const string BasePresetNameKey = "Cname_Ghost";
        public const string MinionPresetNameKey = "Cname_Ghost";
        public const string FallbackPresetNameKey = "Cname_Boss_Red";

        // ========== 代码特效配置 ==========

        public static readonly UnityEngine.Color EffectColor = new UnityEngine.Color(0.6f, 0.1f, 0.9f, 0.8f);
        public const float EffectEmissionMultiplier = 2f;
        public const float EffectDefaultDuration = 1.5f;

        // ========== Boss 特效配色 ==========

        public static readonly UnityEngine.Color TeleportRingColor = new UnityEngine.Color(0.65f, 0.20f, 1.00f, 0.85f);
        public static readonly UnityEngine.Color CurseAuraGroundColor = new UnityEngine.Color(0.40f, 0.08f, 0.60f, 0.45f);
        public static readonly UnityEngine.Color CurseAuraRingColor = new UnityEngine.Color(0.60f, 0.18f, 0.95f, 0.80f);
        public static readonly UnityEngine.Color SweepArcColor = new UnityEngine.Color(0.80f, 0.40f, 1.00f, 0.90f);
        public static readonly UnityEngine.Color HeavySlashColor = new UnityEngine.Color(0.90f, 0.50f, 1.00f, 0.95f);
        public static readonly UnityEngine.Color SummonCircleColor = new UnityEngine.Color(0.55f, 0.15f, 0.90f, 0.80f);
        public static readonly UnityEngine.Color SummonPentagramColor = new UnityEngine.Color(0.70f, 0.30f, 1.00f, 0.55f);
        public static readonly UnityEngine.Color PhaseTransitionColor = new UnityEngine.Color(0.75f, 0.30f, 1.00f, 0.90f);
        public static readonly UnityEngine.Color DamageHitFlashColor = new UnityEngine.Color(0.95f, 0.70f, 1.00f, 0.80f);
        public static readonly UnityEngine.Color FxParticlePurple = new UnityEngine.Color(0.70f, 0.35f, 1.00f, 1.00f);
        public static readonly UnityEngine.Color RuneMarkWhite = new UnityEngine.Color(1.00f, 0.85f, 1.00f, 0.90f);

        // ========== Boss 特效参数 ==========

        public const int FxRingSegments = 48;
        public const int FxArcSegments = 16;
        public const int FxSmallRingSegments = 32;
        public const int FxHitRingSegments = 24;
        public const float TeleportFxDuration = 0.5f;
        public const float TeleportShrinkRadius = 1.2f;
        public const float TeleportExpandRadius = 1.5f;
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
        public const int FxLowSpecProcessorCount = 4;
        public const int FxLowSpecSystemMemoryMb = 8192;
        public const int FxLowSpecGraphicsMemoryMb = 1536;

        // ========== 诅咒Buff运行时构建参数 ==========

        public const int CurseBuffID = 500043;
        public const float CurseBuffDuration = 5f;
        public const int CurseBuffMaxLayers = 3;
        public const float CurseSlowPerLayer = -0.3f;

        // ========== 攻击序列 ==========

        public static readonly PhantomWitchAttackType[] Phase1Sequence = new PhantomWitchAttackType[]
        {
            PhantomWitchAttackType.Blink,
            PhantomWitchAttackType.CurseAura,
            PhantomWitchAttackType.ScytheSweep,
            PhantomWitchAttackType.Blink,
            PhantomWitchAttackType.CurseRealm
        };

        public static readonly PhantomWitchAttackType[] Phase2Sequence = new PhantomWitchAttackType[]
        {
            PhantomWitchAttackType.SummonMinions,
            PhantomWitchAttackType.Blink,
            PhantomWitchAttackType.HeavyScytheSlash,
            PhantomWitchAttackType.CurseRealm,
            PhantomWitchAttackType.ScytheSweep
        };

        // ========== 阶段切换提示 ==========

        public const string Phase2MessageCN = "幽灵女巫召唤了亡灵随从，镰刀变得更加狂暴！";
        public const string Phase2MessageEN = "The Phantom Witch summons undead minions, and her scythe grows more violent!";
        public const string SpawnMessageCN = "幽灵女巫出现了！";
        public const string SpawnMessageEN = "The Phantom Witch has appeared!";
        public const string DefeatedMessageCN = "幽灵女巫被击败了！";
        public const string DefeatedMessageEN = "The Phantom Witch has been defeated!";
    }
}
