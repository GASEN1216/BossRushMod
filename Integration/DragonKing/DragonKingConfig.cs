// ============================================================================
// DragonKingConfig.cs - 龙王Boss配置
// ============================================================================
// 模块说明：
//   定义龙王Boss的所有可配置参数
//   基于泰拉瑞亚光之女皇的AI框架设计
// ============================================================================

using System.IO;

namespace BossRush
{
    /// <summary>
    /// 攻击类型枚举
    /// </summary>
    public enum DragonKingAttackType
    {
        PrismaticBolts,      // 棱彩弹
        PrismaticBolts2,     // 棱彩弹2（螺旋）
        Dash,                // 冲刺
        SunDance,            // 太阳舞
        EverlastingRainbow,  // 永恒彩虹
        EtherealLance,       // 以太长矛
        EtherealLance2       // 以太长矛2（切屏）
    }

    /// <summary>
    /// 龙王Boss配置参数
    /// </summary>
    public static class DragonKingConfig
    {
        // ========== 调试模式配置 ==========
        
        /// <summary>
        /// 是否启用调试模式（只重复释放指定技能）
        /// </summary>
        public static bool DebugMode = false;
        
        /// <summary>
        /// 调试模式下重复释放的技能类型
        /// </summary>
        public static DragonKingAttackType DebugAttackType = DragonKingAttackType.EtherealLance2;
        
        // ========== Boss基础属性 ==========
        
        /// <summary>
        /// 基础血量
        /// </summary>
        public const float BaseHealth = 5000f;
        
        /// <summary>
        /// 伤害倍率
        /// </summary>
        public const float DamageMultiplier = 0.5f;
        
        /// <summary>
        /// Boss名称本地化键
        /// </summary>
        public const string BossNameKey = "boss_dragonking";
        
        /// <summary>
        /// Boss中文名称
        /// </summary>
        public const string BossNameCN = "龙王";
        
        /// <summary>
        /// Boss英文名称
        /// </summary>
        public const string BossNameEN = "Dragon King";
        
        // ========== 阶段参数 ==========
        
        /// <summary>
        /// 二阶段血量阈值（50%）
        /// </summary>
        public const float Phase2HealthThreshold = 0.5f;
        
        /// <summary>
        /// 一阶段攻击间隔（秒）
        /// </summary>
        public const float Phase1AttackInterval = 1.0f;
        
        /// <summary>
        /// 二阶段攻击间隔（秒）
        /// </summary>
        public const float Phase2AttackInterval = 0.5f;
        
        /// <summary>
        /// 二阶段转换持续时间（秒）
        /// </summary>
        public const float Phase2TransitionDuration = 1.0f;
        
        // ========== 棱彩弹参数 ==========
        
        /// <summary>
        /// 棱彩弹数量
        /// </summary>
        public const int PrismaticBoltCount = 8;
        
        /// <summary>
        /// 棱彩弹延迟时间（秒）
        /// </summary>
        public const float PrismaticBoltDelay = 0.5f;
        
        /// <summary>
        /// 棱彩弹追踪强度（0-1，1为完美追踪）
        /// </summary>
        public const float PrismaticBoltTrackingStrength = 0.8f;
        
        /// <summary>
        /// 棱彩弹追踪持续时间（秒）- 超过此时间后直线飞行
        /// </summary>
        public const float PrismaticBoltTrackingDuration = 2.5f;
        
        /// <summary>
        /// 棱彩弹伤害
        /// </summary>
        public const float PrismaticBoltDamage = 15f;
        
        /// <summary>
        /// 棱彩弹生命周期（秒）
        /// </summary>
        public const float PrismaticBoltLifetime = 5f;
        
        /// <summary>
        /// 棱彩弹移动速度
        /// </summary>
        public const float PrismaticBoltSpeed = 10f;
        
        /// <summary>
        /// 棱彩弹缩放倍数（增大弹幕大小）
        /// </summary>
        public const float PrismaticBoltScale = 2.5f;
        
        // ========== 棱彩弹2参数（螺旋） ==========
        
        /// <summary>
        /// 螺旋发射间隔（秒）
        /// </summary>
        public const float SpiralFireInterval = 0.1f;
        
        /// <summary>
        /// 螺旋发射持续时间（秒）
        /// </summary>
        public const float SpiralFireDuration = 1f;
        
        /// <summary>
        /// 螺旋角度增量（度）
        /// </summary>
        public const float SpiralAngleIncrement = 15f;
        
        /// <summary>
        /// 棱彩弹2追踪持续时间（秒）- 超过此时间后直线飞行
        /// </summary>
        public const float PrismaticBolt2TrackingDuration = 2f;
        
        // ========== 冲刺参数 ==========
        
        /// <summary>
        /// 冲刺蓄力时间（秒）- 包含粒子聚拢和倒计时光圈
        /// </summary>
        public const float DashChargeTime = 0.8f;
        
        /// <summary>
        /// 倒计时光圈开始时间（蓄力结束前多少秒显示光圈）
        /// </summary>
        public const float DashCountdownRingTime = 0.3f;
        
        /// <summary>
        /// 冲刺速度（单位/秒）- 使用 SetForceMoveVelocity
        /// </summary>
        public const float DashSpeed = 25f;
        
        /// <summary>
        /// 冲刺持续时间（秒）
        /// </summary>
        public const float DashDuration = 0.6f;
        
        /// <summary>
        /// 冲刺伤害
        /// </summary>
        public const float DashDamage = 30f;
        
        /// <summary>
        /// 冲刺碰撞检测半径
        /// </summary>
        public const float DashCollisionRadius = 1.5f;
        
        // ========== 岩浆区域参数 ==========
        
        /// <summary>
        /// 岩浆区域伤害（每次触发）
        /// </summary>
        public const float LavaDamage = 5f;
        
        /// <summary>
        /// 岩浆区域伤害间隔（秒）
        /// </summary>
        public const float LavaDamageInterval = 0.5f;
        
        /// <summary>
        /// 岩浆区域持续时间（秒）
        /// </summary>
        public const float LavaDuration = 3f;
        
        /// <summary>
        /// 岩浆区域检测半径
        /// </summary>
        public const float LavaRadius = 1f;
        
        // ========== 太阳舞参数 ==========
        
        /// <summary>
        /// 太阳舞光束数量
        /// </summary>
        public const int SunDanceBeamCount = 6;
        
        /// <summary>
        /// 太阳舞旋转速度（度/秒）
        /// </summary>
        public const float SunDanceRotationSpeed = 30f;
        
        /// <summary>
        /// 太阳舞波数
        /// </summary>
        public const int SunDanceWaveCount = 3;
        
        /// <summary>
        /// 太阳舞波偏移角度（度）
        /// </summary>
        public const float SunDanceWaveOffset = 20f;
        
        /// <summary>
        /// 太阳舞每tick伤害
        /// </summary>
        public const float SunDanceDamagePerTick = 10f;
        
        /// <summary>
        /// 太阳舞持续时间（秒）
        /// </summary>
        public const float SunDanceDuration = 5f;

        /// <summary>
        /// 太阳舞伤害tick间隔（秒）
        /// </summary>
        public const float SunDanceTickInterval = 0.2f;

        /// <summary>
        /// 太阳舞弹幕方向数量（360度均分）
        /// </summary>
        public const int SunDanceBarrageDirectionCount = 24;

        /// <summary>
        /// 太阳舞弹幕方向间隔（度）
        /// </summary>
        public const float SunDanceBarrageAngleStep = 15f;

        /// <summary>
        /// 太阳舞弹幕每次旋转角度（度）
        /// </summary>
        public const float SunDanceBarrageRotationPerTick = 5f;

        /// <summary>
        /// 太阳舞弹幕速度倍率（相对于原武器速度）
        /// </summary>
        public const float SunDanceBulletSpeedMultiplier = 0.3f;

        // ========== 永恒彩虹参数 ==========
        
        /// <summary>
        /// 永恒彩虹星数量
        /// </summary>
        public const int RainbowStarCount = 13;
        
        /// <summary>
        /// 永恒彩虹轨迹伤害
        /// </summary>
        public const float RainbowTrailDamage = 5f;
        
        /// <summary>
        /// 永恒彩虹持续时间（秒）
        /// </summary>
        public const float RainbowDuration = 8f;
        
        /// <summary>
        /// 永恒彩虹最大扩散半径
        /// </summary>
        public const float RainbowMaxRadius = 10f;
        
        /// <summary>
        /// 永恒彩虹旋转速度（度/秒）
        /// </summary>
        public const float RainbowRotationSpeed = 45f;
        
        // ========== 以太长矛参数 ==========
        
        /// <summary>
        /// 以太长矛数量
        /// </summary>
        public const int EtherealLanceCount = 12;
        
        /// <summary>
        /// 以太长矛预警时间（秒）
        /// </summary>
        public const float EtherealLanceWarningDuration = 1.0f;
        
        /// <summary>
        /// 以太长矛速度（单位/秒）
        /// </summary>
        public const float EtherealLanceSpeed = 40f;
        
        /// <summary>
        /// 以太长矛伤害
        /// </summary>
        public const float EtherealLanceDamage = 25f;
        
        // ========== 以太长矛2参数（切屏） ==========
        
        /// <summary>
        /// 切屏剑阵波数
        /// </summary>
        public const int ScreenLanceWaveCount = 4;
        
        /// <summary>
        /// 每波长矛数量
        /// </summary>
        public const int ScreenLancePerWave = 16;
        
        /// <summary>
        /// 波间隔时间（秒）
        /// </summary>
        public const float ScreenLanceWaveInterval = 0.5f;
        
        // ========== 碰撞伤害参数 ==========
        
        /// <summary>
        /// 碰撞伤害值
        /// </summary>
        public const float CollisionDamage = 15f;
        
        /// <summary>
        /// 碰撞伤害冷却时间（秒）
        /// </summary>
        public const float CollisionCooldown = 0.5f;
        
        /// <summary>
        /// 碰撞检测半径
        /// </summary>
        public const float CollisionRadius = 1.5f;
        
        // ========== 位置偏移参数（提取的魔法数字） ==========
        
        /// <summary>
        /// Boss胸口高度偏移（弹幕发射位置）
        /// </summary>
        public const float BossChestHeightOffset = 1.2f;
        
        /// <summary>
        /// 玩家目标高度偏移（追踪目标位置）
        /// </summary>
        public const float PlayerTargetHeightOffset = 1f;
        
        /// <summary>
        /// 伤害点高度偏移（伤害数字显示位置）
        /// </summary>
        public const float DamagePointHeightOffset = 0.8f;
        
        /// <summary>
        /// 弹幕命中检测半径
        /// </summary>
        public const float ProjectileHitRadius = 1.05f;
        
        // ========== 换位参数 ==========
        
        /// <summary>
        /// 换位最小高度（米）
        /// </summary>
        public const float RepositionHeightMin = 3f;
        
        /// <summary>
        /// 换位最大高度（米）
        /// </summary>
        public const float RepositionHeightMax = 5f;
        
        /// <summary>
        /// 换位移动速度（单位/秒）
        /// </summary>
        public const float RepositionSpeed = 15f;
        
        /// <summary>
        /// 悬浮跟随速度（单位/秒）- 非攻击时跟随玩家的速度
        /// </summary>
        public const float HoverFollowSpeed = 8f;
        
        /// <summary>
        /// 悬浮偏移角度（度）- 相对于玩家的偏移方向
        /// </summary>
        public const float HoverOffsetAngle = 45f;

        // ========== 自定义射击参数 ==========

        /// <summary>
        /// 自定义子弹基础伤害
        /// </summary>
        public const float CustomBulletDamage = 15f;

        /// <summary>
        /// 自定义子弹飞行距离
        /// </summary>
        public const float CustomBulletDistance = 100f;

        /// <summary>
        /// 自定义子弹半伤害距离
        /// </summary>
        public const float CustomBulletHalfDamageDistance = 50f;

        /// <summary>
        /// 自定义子弹暴击率
        /// </summary>
        public const float CustomBulletCritRate = 0.1f;

        /// <summary>
        /// 自定义子弹暴击伤害倍率
        /// </summary>
        public const float CustomBulletCritDamageFactor = 1.5f;

        /// <summary>
        /// 一阶段每发子弹数量
        /// </summary>
        public const int Phase1BulletCount = 1;

        /// <summary>
        /// 一阶段射击偏移范围（米）
        /// </summary>
        public const float Phase1OffsetRange = 2f;

        /// <summary>
        /// 二阶段每发子弹数量
        /// </summary>
        public const int Phase2BulletCount = 2;

        /// <summary>
        /// 二阶段射击偏移范围（米）
        /// </summary>
        public const float Phase2OffsetRange = 4f;

        // ========== 资源路径 ==========
        
        /// <summary>
        /// AssetBundle文件路径（相对于Mod目录）
        /// </summary>
        public const string AssetBundlePath = "Assets/boss/dragonking";
        
        /// <summary>
        /// 棱彩弹预制体名称
        /// </summary>
        public const string PrismaticBoltPrefab = "PrismaticBolt";
        
        /// <summary>
        /// 太阳舞光束组预制体名称
        /// </summary>
        public const string SunBeamGroupPrefab = "SunBeamGroup";
        
        /// <summary>
        /// 永恒彩虹星预制体名称
        /// </summary>
        public const string RainbowStarPrefab = "RainbowStar";
        
        /// <summary>
        /// 以太长矛预制体名称
        /// </summary>
        public const string EtherealLancePrefab = "EtherealLance";
        
        /// <summary>
        /// 冲刺残影预制体名称
        /// </summary>
        public const string DashTrailPrefab = "DashTrail";
        
        /// <summary>
        /// 传送特效预制体名称
        /// </summary>
        public const string TeleportFXPrefab = "TeleportFX";
        
        /// <summary>
        /// 阶段转换特效预制体名称
        /// </summary>
        public const string PhaseTransitionPrefab = "PhaseTransition";
        
        // ========== 音效路径 ==========
        
        /// <summary>
        /// 音效文件基础路径
        /// </summary>
        public static readonly string SoundBasePath = Path.Combine(
            Path.GetDirectoryName(typeof(DragonKingConfig).Assembly.Location),
            "Assets", "Sounds", "DragonKing"
        );
        
        /// <summary>
        /// Boss登场音效
        /// </summary>
        public static readonly string Sound_Spawn = Path.Combine(SoundBasePath, "common_spawn.mp3");
        
        /// <summary>
        /// Boss死亡音效
        /// </summary>
        public static readonly string Sound_Death = Path.Combine(SoundBasePath, "common_death.mp3");
        
        /// <summary>
        /// 碰撞/受击音效
        /// </summary>
        public static readonly string Sound_Hit = Path.Combine(SoundBasePath, "common_hit.mp3");
        
        /// <summary>
        /// 二阶段转换音效
        /// </summary>
        public static readonly string Sound_Phase2 = Path.Combine(SoundBasePath, "phase2_transition.mp3");
        
        /// <summary>
        /// 冲刺蓄力音效
        /// </summary>
        public static readonly string Sound_DashCharge = Path.Combine(SoundBasePath, "dash_charge.mp3");
        
        /// <summary>
        /// 冲刺爆发音效
        /// </summary>
        public static readonly string Sound_DashBurst = Path.Combine(SoundBasePath, "dash_burst.mp3");
        
        /// <summary>
        /// 太阳舞警告音效
        /// </summary>
        public static readonly string Sound_SunWarning = Path.Combine(SoundBasePath, "sun_warning.mp3");
        
        /// <summary>
        /// 长矛警告音效
        /// </summary>
        public static readonly string Sound_LanceWarning = Path.Combine(SoundBasePath, "lance_warning.mp3");
        
        /// <summary>
        /// 长矛发射音效
        /// </summary>
        public static readonly string Sound_LanceFire = Path.Combine(SoundBasePath, "lance_fire.mp3");
        
        /// <summary>
        /// 棱彩弹1生成音效
        /// </summary>
        public static readonly string Sound_BoltSpawn = Path.Combine(SoundBasePath, "bolt_spawn.mp3");
        
        /// <summary>
        /// 棱彩弹2生成音效
        /// </summary>
        public static readonly string Sound_BoltSpawn2 = Path.Combine(SoundBasePath, "bolt_spawn2.mp3");
        
        /// <summary>
        /// 棱彩弹命中音效
        /// </summary>
        public static readonly string Sound_BoltHit = Path.Combine(SoundBasePath, "bolt_hit.mp3");
        
        /// <summary>
        /// 永恒彩虹生成音效
        /// </summary>
        public static readonly string Sound_RainbowSpawn = Path.Combine(SoundBasePath, "rainbow_spawn.mp3");
        
        // ========== 攻击序列 ==========
        
        /// <summary>
        /// 一阶段攻击序列
        /// </summary>
        public static readonly DragonKingAttackType[] Phase1Sequence = new DragonKingAttackType[]
        {
            DragonKingAttackType.PrismaticBolts,
            DragonKingAttackType.Dash,
            DragonKingAttackType.SunDance,
            DragonKingAttackType.Dash,
            DragonKingAttackType.EverlastingRainbow,
            DragonKingAttackType.PrismaticBolts,
            DragonKingAttackType.Dash,
            DragonKingAttackType.EtherealLance,
            DragonKingAttackType.Dash,
            DragonKingAttackType.EverlastingRainbow
        };
        
        /// <summary>
        /// 二阶段攻击序列
        /// </summary>
        public static readonly DragonKingAttackType[] Phase2Sequence = new DragonKingAttackType[]
        {
            DragonKingAttackType.EtherealLance2,
            DragonKingAttackType.PrismaticBolts,
            DragonKingAttackType.Dash,
            DragonKingAttackType.EverlastingRainbow,
            DragonKingAttackType.PrismaticBolts,
            DragonKingAttackType.SunDance,
            DragonKingAttackType.EtherealLance,
            DragonKingAttackType.Dash,
            DragonKingAttackType.PrismaticBolts2
        };
        
        // ========== 孩儿护我配置 ==========
        
        /// <summary>
        /// 孩儿护我触发血量阈值（HP）
        /// </summary>
        public const float ChildProtectionHealthThreshold = 1f;
        
        /// <summary>
        /// 飞升目标高度（米）
        /// </summary>
        public const float ChildProtectionFlyHeight = 5f;
        
        /// <summary>
        /// 飞升速度（米/秒）
        /// </summary>
        public const float ChildProtectionFlySpeed = 3f;
        
        /// <summary>
        /// 龙王对话内容（中文）
        /// </summary>
        public const string ChildProtectionDialogueCN = "孩儿护我！";
        
        /// <summary>
        /// 龙王对话内容（英文）
        /// </summary>
        public const string ChildProtectionDialogueEN = "My child, protect me!";
        
        /// <summary>
        /// 龙裔遗族对话内容（中文）
        /// </summary>
        public const string DescendantDialogueCN = "爹爹！";
        
        /// <summary>
        /// 龙裔遗族对话内容（英文）
        /// </summary>
        public const string DescendantDialogueEN = "Father!";
        
        /// <summary>
        /// 对话气泡显示时长（秒）
        /// </summary>
        public const float DialogueDuration = 2f;
        
        /// <summary>
        /// 对话气泡Y轴偏移（米）
        /// </summary>
        public const float DialogueBubbleYOffset = 2.5f;
    }
}
