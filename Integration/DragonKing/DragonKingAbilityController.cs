// ============================================================================
// DragonKingAbilityController.cs - 龙王Boss能力控制器
// ============================================================================
// 模块说明：
//   管理龙王Boss的AI状态机、攻击序列和阶段转换
//   基于泰拉瑞亚光之女皇的AI框架设计
//   实现7种攻击技能和两阶段战斗机制
//
// 攻击技能列表：
//   1. PrismaticBolts    - 棱彩弹：Boss周围生成8个弹幕，延迟后追踪玩家
//   2. PrismaticBolts2   - 棱彩弹2：螺旋发射追踪弹幕
//   3. Dash              - 冲刺攻击：蓄力后冲向玩家，留下岩浆轨迹，二阶段有二段冲刺
//   4. SunDance          - 太阳舞：传送到玩家附近，发射24方向旋转弹幕+光束伤害
//   5. EverlastingRainbow- 永恒彩虹：13颗星环绕Boss螺旋扩散后收缩
//   6. EtherealLance     - 以太长矛：显示警告线后从两端发射长矛
//   7. EtherealLance2    - 以太长矛2：切屏剑阵，4波各16条线同时画出
//
// 阶段机制：
//   - Phase1：血量>50%，按Phase1Sequence序列释放技能
//   - Phase2：血量≤50%触发转换，攻击间隔缩短，使用Phase2Sequence
//   - 转换时Boss消失并传送到玩家上方，显示狂暴提示
//
// 辅助组件：
//   - DragonKingCollisionDetector：碰撞伤害检测
//   - BossAIController：AI暂停/恢复控制
//   - SunBeamDamageTrigger：太阳舞光束伤害触发器
//   - DragonKingLavaZone：冲刺残影岩浆伤害区域
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.Utilities;
using Duckov.UI.DialogueBubbles;
using BossRush.Common.Effects;

namespace BossRush
{
    /// <summary>
    /// Boss阶段枚举
    /// </summary>
    public enum DragonKingPhase
    {
        Phase1,         // 一阶段
        Phase2,         // 二阶段
        Transitioning,  // 阶段转换中
        Dead            // 死亡
    }

    /// <summary>
    /// 龙王 Boss 能力控制器
    /// </summary>
    public partial class DragonKingAbilityController : MonoBehaviour
    {
        // ========== 状态变量 ==========

        /// <summary>
        /// 当前阶段
        /// </summary>
        public DragonKingPhase CurrentPhase { get; private set; } = DragonKingPhase.Phase1;

        /// <summary>
        /// 当前攻击序列索引
        /// </summary>
        private int currentAttackIndex = 0;

        /// <summary>
        /// 是否已触发二阶段转换
        /// </summary>
        private bool phase2Triggered = false;

        /// <summary>
        /// Boss角色引用
        /// </summary>
        private CharacterMainControl bossCharacter;
        private Transform bossTransform;

        private Transform BossTransform
        {
            get
            {
                if (bossTransform == null)
                {
                    bossTransform = bossCharacter != null ? bossCharacter.transform : null;
                }

                return bossTransform;
            }
        }

        /// <summary>
        /// Boss的Health组件
        /// </summary>
        private Health bossHealth;
        private int attackDesyncSeed = 0;

        /// <summary>
        /// 玩家角色引用
        /// </summary>
        private CharacterMainControl playerCharacter;

        /// <summary>
        /// AI 控制辅助类（统一的暂停/恢复接口）
        /// </summary>
        private BossAIController aiController;

        /// <summary>
        /// 攻击循环协程
        /// </summary>
        private Coroutine attackLoopCoroutine;

        /// <summary>
        /// 当前攻击协程
        /// </summary>
        private Coroutine currentAttackCoroutine;

        /// <summary>
        /// 活跃的攻击特效列表（用于清理）
        /// 预分配容量以避免动态扩容
        /// </summary>
        private List<GameObject> activeEffects = new List<GameObject>(64);

        /// <summary>
        /// 活跃的弹幕集合（用于清理）
        /// 使用 HashSet 提供 O(1) Contains/Add/Remove
        /// </summary>
        private HashSet<GameObject> activeProjectiles = new HashSet<GameObject>();

        /// <summary>
        /// 活跃的警告线列表（用于清理）
        /// 预分配容量以避免动态扩容
        /// </summary>
        private List<GameObject> activeWarningLines = new List<GameObject>(64);

        /// <summary>
        /// 碰撞检测器
        /// </summary>
        private DragonKingCollisionDetector collisionDetector;

        /// <summary>
        /// 上次碰撞伤害时间
        /// </summary>
        private float lastCollisionDamageTime = 0f;

        /// <summary>
        /// 悬浮偏移方向（左上角或右上角）
        /// </summary>
        private int hoverSide = 1; // 1=右上角, -1=左上角

        // ========== 太阳舞弹幕相关 ==========

        /// <summary>
        /// 缓存的武器子弹预制体（用于太阳舞弹幕）
        /// </summary>
        private Projectile cachedWeaponBullet = null;

        /// <summary>
        /// 是否已缓存武器子弹
        /// </summary>
        private bool weaponBulletCached = false;

        /// <summary>
        /// 缓存的反射字段：Projectile.hitLayers
        /// </summary>
        private System.Reflection.FieldInfo cachedHitLayersField = null;

        /// <summary>
        /// 缓存的穿墙 LayerMask（只检测伤害接收层）
        /// </summary>
        private LayerMask piercingLayerMask = new LayerMask();

        /// <summary>
        /// 太阳舞弹幕发射协程引用
        /// </summary>
        private Coroutine sunDanceBarrageCoroutine = null;

        /// <summary>
        /// 太阳舞是否正在进行
        /// </summary>
        private bool isSunDanceActive = false;

        /// <summary>
        /// 缓存的武器射击音效键
        /// </summary>
        private string cachedWeaponShootKey = null;

        /// <summary>
        /// 缓存的完整武器射击事件名，避免热路径重复字符串拼接
        /// </summary>
        private string cachedWeaponShootEventName = null;

        /// <summary>
        /// 缓存的武器子弹速度
        /// </summary>
        private float cachedWeaponBulletSpeed = 30f;
        private float lastWeaponShootSoundTime = float.NegativeInfinity;

        // ========== 反射缓存（避免重复反射） ==========

        private delegate void AudioPostDelegate(string eventName, GameObject target);

        /// <summary>
        /// 缓存的AudioManager.Post委托
        /// </summary>
        private static AudioPostDelegate cachedAudioPostDelegate = null;

        /// <summary>
        /// 是否已尝试解析AudioManager.Post
        /// </summary>
        private static bool cachedAudioPostResolved = false;

        /// <summary>
        /// 跨龙皇共享的音效节流时间表，避免同一帧堆出多次相同事件
        /// </summary>
        private static Dictionary<string, float> sharedSoundThrottleTimestamps = new Dictionary<string, float>(16);


        /// <summary>
        /// 太阳舞期间Boss锁定位置（用于弹幕发射位置）
        /// </summary>
        private Vector3 sunDanceLockPosition;

        // ========== 孩儿护我系统 ==========

        /// <summary>
        /// 是否已触发孩儿护我
        /// </summary>
        private bool childProtectionTriggered = false;

        /// <summary>
        /// 是否处于孩儿护我阶段
        /// </summary>
        private bool isInChildProtection = false;

        /// <summary>
        /// 召唤的龙裔遗族引用
        /// </summary>
        private CharacterMainControl spawnedDescendant = null;

        /// <summary>
        /// 飞行平台（防止下落）
        /// </summary>
        private GameObject flightPlatform = null;

        /// <summary>
        /// 锁定的最低Y坐标（悬停高度）
        /// </summary>
        private float lockedMinY = float.MinValue;

        /// <summary>
        /// 孩儿护我协程引用
        /// </summary>
        private Coroutine childProtectionCoroutine = null;

        /// <summary>
        /// 孩儿护我阶段棱彩弹发射协程引用
        /// </summary>
        private Coroutine childProtectionBoltCoroutine = null;

        /// <summary>
        /// 飞升云雾特效引用
        /// </summary>
        private FlightCloudEffect flightCloudEffect = null;

        /// <summary>
        /// 三阶段起飞前的位置（用于死亡时掉落物生成）
        /// </summary>
        private Vector3 preFlyPosition = Vector3.zero;

        // ========== 自定义射击系统（替代原版AI射击） ==========

        /// <summary>
        /// 是否正在自定义射击
        /// </summary>
        private bool isCustomShootingActive = false;

        /// <summary>
        /// 自定义射击协程引用
        /// </summary>
        private Coroutine customShootingCoroutine = null;

        // ========== 资源管理：Material缓存（防止内存泄漏） ==========

        /// <summary>
        /// 缓存的通用材质（用于特效，避免重复创建）
        /// </summary>
        private static Material cachedInternalColoredMaterial = null;

        /// <summary>
        /// 缓存的白色材质（用于冲刺蓄力粒子）
        /// </summary>
        private static Material cachedWhiteParticleMaterial = null;

        /// <summary>
        /// 缓存的黄色材质（用于太阳舞预警圆圈）
        /// </summary>
        private static Material cachedYellowWarningMaterial = null;

        /// <summary>
        /// 获取或创建共享的Internal-Colored材质
        /// </summary>
        private static Material GetSharedInternalColoredMaterial()
        {
            if (cachedInternalColoredMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    cachedInternalColoredMaterial = new Material(shader);
                }
            }
            return cachedInternalColoredMaterial;
        }

        /// <summary>
        /// 获取或创建共享的白色粒子材质
        /// </summary>
        private static Material GetSharedWhiteParticleMaterial()
        {
            if (cachedWhiteParticleMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    cachedWhiteParticleMaterial = new Material(shader);
                    cachedWhiteParticleMaterial.color = new Color(1f, 1f, 1f, 1f);
                }
            }
            return cachedWhiteParticleMaterial;
        }

        /// <summary>
        /// 获取或创建共享的黄色警告材质
        /// </summary>
        private static Material GetSharedYellowWarningMaterial()
        {
            if (cachedYellowWarningMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    cachedYellowWarningMaterial = new Material(shader);
                    cachedYellowWarningMaterial.color = new Color(1f, 0.9f, 0f, 1f);
                }
            }
            return cachedYellowWarningMaterial;
        }

        /// <summary>
        /// 清理静态材质缓存（场景切换时调用）
        /// </summary>
        public static void ClearStaticMaterialCache()
        {
            if (cachedInternalColoredMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedInternalColoredMaterial);
                cachedInternalColoredMaterial = null;
            }
            if (cachedWhiteParticleMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedWhiteParticleMaterial);
                cachedWhiteParticleMaterial = null;
            }
            if (cachedYellowWarningMaterial != null)
            {
                UnityEngine.Object.Destroy(cachedYellowWarningMaterial);
                cachedYellowWarningMaterial = null;
            }
        }

        /// <summary>
        /// 动态创建的Material列表（用于销毁时清理）
        /// </summary>
        private List<Material> dynamicMaterials = new List<Material>();

        // ========== 协程管理：弹幕协程列表（统一管理） ==========

        /// <summary>
        /// 活跃的弹幕追踪协程列表（用于统一停止）
        /// 预分配容量以避免动态扩容
        /// </summary>
        private List<Coroutine> activeProjectileCoroutines = new List<Coroutine>(128);

        private struct TrackingProjectileState
        {
            public GameObject projectile;
            public Rigidbody rigidbody;
            public float startTime;
            public float lifetime;
            public float trackingDuration;
            public float speed;
            public float trackingStrength;
            public Vector3 currentVelocity;
            public bool trackingEnded;
        }

        private struct LanceProjectileState
        {
            public GameObject lance;
            public Vector3 direction;
            public float speed;
            public float maxDistance;
            public float traveled;
        }

        /// <summary>
        /// 统一批处理的追踪弹幕状态
        /// </summary>
        private List<TrackingProjectileState> activeTrackingProjectiles = new List<TrackingProjectileState>(128);

        /// <summary>
        /// 统一批处理的长矛移动状态
        /// </summary>
        private List<LanceProjectileState> activeLances = new List<LanceProjectileState>(128);

        // ========== 性能优化：WaitForSeconds缓存 ==========

        private static readonly WaitForSeconds wait01s = new WaitForSeconds(0.1f);
        private static readonly WaitForSeconds wait02s = new WaitForSeconds(0.2f);
        private static readonly WaitForSeconds wait05s = new WaitForSeconds(0.5f);
        private static readonly WaitForSeconds wait1s = new WaitForSeconds(1f);
        private static readonly WaitForSeconds wait15s = new WaitForSeconds(1.5f);
        private static readonly WaitForSeconds wait2s = new WaitForSeconds(2f);
        private static readonly WaitForSeconds wait25s = new WaitForSeconds(2.5f);
        private static readonly WaitForSeconds wait3s = new WaitForSeconds(3f);
        private static readonly WaitForSeconds wait5s = new WaitForSeconds(5f);
        private static readonly WaitForSeconds wait8s = new WaitForSeconds(8f);

        // ========== 性能优化：Gradient缓存（避免每次攻击重复创建） ==========

        /// <summary>
        /// 缓存的彩虹渐变（用于以太长矛警告线）
        /// </summary>
        private static Gradient cachedRainbowGradient = null;
        private static int nextAttackDesyncSeed = 0;

        private const float PLAYER_AIM_SAMPLE_INTERVAL = 0.05f;
        private const float ATTACK_LOOP_DESYNC_STEP = 0.18f;
        private const int ATTACK_LOOP_DESYNC_BUCKET_COUNT = 5;
        private const int WARNING_CIRCLE_SEGMENTS = 64;
        private const float WARNING_CIRCLE_START_RADIUS = 20f;
        private const float WARNING_CIRCLE_END_RADIUS = 0f;
        private const int WARNING_LINE_PREWARM_COUNT = 64;
        private const int WARNING_CIRCLE_PREWARM_COUNT = 6;
        private const int DASH_CHARGE_PREWARM_COUNT = 3;
        private const int DASH_COUNTDOWN_RING_PREWARM_COUNT = 3;
        private const float GLOBAL_WEAPON_SOUND_INTERVAL = 0.015f;
        private const float GLOBAL_BOLT_SPAWN2_SOUND_INTERVAL = 0.03f;
        private const float PLAYER_HITBOX_BOUNDS_REFRESH_INTERVAL = 1f;

        private static CharacterMainControl sharedTrackedTarget = null;
        private static Rigidbody sharedTrackedTargetRigidbody = null;
        private static Vector3 sharedTrackedTargetAimPosition = Vector3.zero;
        private static Vector3 sharedTrackedTargetVelocity = Vector3.zero;
        private static float lastSharedTargetSampleTime = float.NegativeInfinity;
        private static int lastSharedTargetSampleFrame = -1;
        private static bool hasSharedTrackedTargetSnapshot = false;
        private static CharacterMainControl sharedTargetHitboxBoundsOwner = null;
        private static Transform sharedTargetHitboxBoundsRoot = null;
        private static Bounds sharedTargetHitboxBounds = default(Bounds);
        private static float lastSharedTargetHitboxBoundsRefreshTime = float.NegativeInfinity;
        private static bool hasSharedTargetHitboxBounds = false;
        private static Gradient cachedTransparentRainbowGradient = null;
        private static Vector3[] cachedWarningCircleUnitPoints = null;
        private static readonly Collider[] sharedTeleportValidationBuffer = new Collider[24];
        private static Stack<GameObject> sharedWarningLinePool = new Stack<GameObject>(96);
        private static Stack<GameObject> sharedWarningCirclePool = new Stack<GameObject>(8);
        private static Stack<GameObject> sharedDashChargeEffectPool = new Stack<GameObject>(8);
        private static Stack<GameObject> sharedDashCountdownRingPool = new Stack<GameObject>(8);
        private static int sharedWarningLineCreatedCount = 0;
        private static int sharedWarningCircleCreatedCount = 0;
        private static int sharedDashChargeEffectCreatedCount = 0;
        private static int sharedDashCountdownRingCreatedCount = 0;

        /// <summary>
        /// 获取或创建共享的彩虹渐变
        /// </summary>
        private static Gradient GetSharedRainbowGradient()
        {
            if (cachedRainbowGradient == null)
            {
                cachedRainbowGradient = new Gradient();
                cachedRainbowGradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(new Color(1f, 1f, 0f, 1f), 0f),      // 亮黄
                        new GradientColorKey(new Color(1f, 0.5f, 0f, 1f), 0.15f), // 橙
                        new GradientColorKey(new Color(1f, 0f, 0f, 1f), 0.3f),    // 红
                        new GradientColorKey(new Color(0.5f, 0f, 1f, 1f), 0.45f), // 紫
                        new GradientColorKey(new Color(0f, 0f, 1f, 1f), 0.6f),    // 蓝
                        new GradientColorKey(new Color(0f, 1f, 1f, 1f), 0.75f),   // 青
                        new GradientColorKey(new Color(0f, 1f, 0f, 1f), 0.9f),    // 绿
                        new GradientColorKey(new Color(1f, 1f, 0f, 1f), 1f)       // 亮黄
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0.95f, 0.15f),
                        new GradientAlphaKey(0.8f, 0.35f),
                        new GradientAlphaKey(0.6f, 0.55f),
                        new GradientAlphaKey(0.4f, 0.75f),
                        new GradientAlphaKey(0.3f, 1f)
                    }
                );
            }
            return cachedRainbowGradient;
        }

        private static Gradient GetTransparentRainbowGradient()
        {
            if (cachedTransparentRainbowGradient == null)
            {
                Gradient baseGradient = GetSharedRainbowGradient();
                cachedTransparentRainbowGradient = new Gradient();
                cachedTransparentRainbowGradient.SetKeys(
                    baseGradient.colorKeys,
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0f, 0.15f),
                        new GradientAlphaKey(0f, 0.35f),
                        new GradientAlphaKey(0f, 0.55f),
                        new GradientAlphaKey(0f, 0.75f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
            }
            return cachedTransparentRainbowGradient;
        }

        private static Vector3[] GetWarningCircleUnitPoints()
        {
            if (cachedWarningCircleUnitPoints == null || cachedWarningCircleUnitPoints.Length != WARNING_CIRCLE_SEGMENTS)
            {
                cachedWarningCircleUnitPoints = new Vector3[WARNING_CIRCLE_SEGMENTS];
                for (int i = 0; i < WARNING_CIRCLE_SEGMENTS; i++)
                {
                    float angle = (float)i / WARNING_CIRCLE_SEGMENTS * 360f * Mathf.Deg2Rad;
                    cachedWarningCircleUnitPoints[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }
            }

            return cachedWarningCircleUnitPoints;
        }

        private static void DestroySharedObjectPool(Stack<GameObject> pool)
        {
            if (pool == null) return;

            while (pool.Count > 0)
            {
                GameObject pooledObject = pool.Pop();
                if (pooledObject != null)
                {
                    UnityEngine.Object.Destroy(pooledObject);
                }
            }
        }

        // ========== 静态缓存 ==========

        /// <summary>
        /// 清理静态缓存
        /// </summary>
        public static void ClearStaticCache()
        {
            ClearStaticMaterialCache();
            cachedRainbowGradient = null;
            cachedTransparentRainbowGradient = null;
            cachedWarningCircleUnitPoints = null;
            nextAttackDesyncSeed = 0;
            cachedAudioPostDelegate = null;
            cachedAudioPostResolved = false;
            sharedSoundThrottleTimestamps.Clear();
            sharedTrackedTarget = null;
            sharedTrackedTargetRigidbody = null;
            sharedTrackedTargetAimPosition = Vector3.zero;
            sharedTrackedTargetVelocity = Vector3.zero;
            lastSharedTargetSampleTime = float.NegativeInfinity;
            lastSharedTargetSampleFrame = -1;
            hasSharedTrackedTargetSnapshot = false;
            DestroySharedObjectPool(sharedWarningLinePool);
            DestroySharedObjectPool(sharedWarningCirclePool);
            DestroySharedObjectPool(sharedDashChargeEffectPool);
            DestroySharedObjectPool(sharedDashCountdownRingPool);
            sharedWarningLineCreatedCount = 0;
            sharedWarningCircleCreatedCount = 0;
            sharedDashChargeEffectCreatedCount = 0;
            sharedDashCountdownRingCreatedCount = 0;
        }

        private static void WarmSharedVisualPools()
        {
            WarmSharedWarningLinePool(WARNING_LINE_PREWARM_COUNT);
            WarmSharedWarningCirclePool(WARNING_CIRCLE_PREWARM_COUNT);
            WarmSharedDashChargePool(DASH_CHARGE_PREWARM_COUNT);
            WarmSharedDashCountdownRingPool(DASH_COUNTDOWN_RING_PREWARM_COUNT);
            DragonKingShockwaveEffect.WarmSharedPool(3);
        }

        private static void WarmSharedWarningLinePool(int desiredCount)
        {
            while (sharedWarningLineCreatedCount < desiredCount)
            {
                ReturnWarningLine(CreateWarningLineObject());
            }
        }

        private static void WarmSharedWarningCirclePool(int desiredCount)
        {
            while (sharedWarningCircleCreatedCount < desiredCount)
            {
                ReturnWarningCircle(CreateWarningCircleObject());
            }
        }

        private static void WarmSharedDashChargePool(int desiredCount)
        {
            while (sharedDashChargeEffectCreatedCount < desiredCount)
            {
                ReturnDashChargeParticles(CreateDashChargeParticlesObject());
            }
        }

        private static void WarmSharedDashCountdownRingPool(int desiredCount)
        {
            while (sharedDashCountdownRingCreatedCount < desiredCount)
            {
                ReturnDashCountdownRing(CreateDashCountdownRingObject());
            }
        }

        // ========== LayerMask缓存（避免重复计算） ==========

        /// <summary>
        /// 缓存的地面层LayerMask（用于位置验证）
        /// </summary>
        private static int cachedGroundLayer = -1;

        /// <summary>
        /// 缓存的地面层障碍物检测LayerMask
        /// </summary>
        private static int cachedGroundLayerMask = 0;

        /// <summary>
        /// 获取地面层LayerMask（缓存后避免重复调用NameToLayer）
        /// </summary>
        private static int GetGroundLayer()
        {
            if (cachedGroundLayer == -1)
            {
                cachedGroundLayer = LayerMask.NameToLayer("Ground");
            }
            return cachedGroundLayer;
        }

        /// <summary>
        /// 获取地面层障碍物检测LayerMask（缓存后避免重复计算）
        /// </summary>
        private static int GetGroundObstacleLayerMask()
        {
            if (cachedGroundLayerMask == 0)
            {
                int groundLayer = GetGroundLayer();
                cachedGroundLayerMask = ~(1 << groundLayer);
            }
            return cachedGroundLayerMask;
        }

        // ========== 公开属性 ==========

        /// <summary>
        /// 获取当前攻击序列
        /// </summary>
        public DragonKingAttackType[] CurrentSequence
        {
            get
            {
                return CurrentPhase == DragonKingPhase.Phase2
                    ? DragonKingConfig.Phase2Sequence
                    : DragonKingConfig.Phase1Sequence;
            }
        }

        /// <summary>
        /// 获取当前攻击间隔
        /// </summary>
        public float CurrentAttackInterval
        {
            get
            {
                return CurrentPhase == DragonKingPhase.Phase2
                    ? DragonKingConfig.Phase2AttackInterval
                    : DragonKingConfig.Phase1AttackInterval;
            }
        }

        // ========== 初始化 ==========

        /// <summary>
        /// 初始化能力控制器
        /// </summary>
        public void Initialize(CharacterMainControl character)
        {
            bossCharacter = character;
            bossTransform = bossCharacter != null ? bossCharacter.transform : null;

            if (bossCharacter == null)
            {
                ModBehaviour.DevLog("[DragonKing] [ERROR] Initialize: bossCharacter is null");
                return;
            }

            // 获取Health组件
            bossHealth = bossCharacter.Health;
            if (bossHealth != null)
            {
                bossHealth.OnHurtEvent.AddListener(OnBossHurt);
            }

            // 初始化AI控制辅助类
            aiController = new BossAIController(bossCharacter, "DragonKing");

            // 获取玩家引用
            UpdatePlayerReference();

            // 加载AssetBundle资源
            LoadAssets();
            WarmSharedVisualPools();

            // 初始化碰撞检测器
            InitializeCollisionDetector();

            // 随机选择悬浮方向（左上角或右上角）
            hoverSide = UnityEngine.Random.value > 0.5f ? 1 : -1;
            attackDesyncSeed = ReserveAttackDesyncSeed();
            currentAttackIndex = GetSequenceStartIndex(DragonKingConfig.Phase1Sequence);

            // 缓存武器子弹预制体（用于自定义射击和太阳舞弹幕）
            CacheWeaponBullet();

            // 移除龙王武器，由我们自己控制射击
            RemoveDragonKingWeapon();

            // 启动自定义射击（每秒10发朝玩家方向）
            StartCustomShooting();

            // 启动攻击循环
            attackLoopCoroutine = StartCoroutine(AttackLoop());

            // 播放登场音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_Spawn);

            ModBehaviour.DevLog("[DragonKing] 能力控制器初始化完成");
        }

        /// <summary>
        /// 缓存龙王武器的子弹预制体、射击音效和子弹速度
        /// </summary>
        private void CacheWeaponBullet()
        {
            if (weaponBulletCached) return;
            weaponBulletCached = true;

            try
            {
                if (bossCharacter == null) return;

                // 方法1：从角色当前手持的枪获取
                var gun = bossCharacter.GetGun();
                if (gun != null)
                {
                    // 缓存子弹速度
                    cachedWeaponBulletSpeed = gun.BulletSpeed;

                    // 通过反射获取GunItemSetting
                    var gunSettingProp = gun.GetType().GetProperty("GunItemSetting");
                    if (gunSettingProp != null)
                    {
                        var gunSetting = gunSettingProp.GetValue(gun) as ItemSetting_Gun;
                        if (gunSetting != null && gunSetting.bulletPfb != null)
                        {
                            cachedWeaponBullet = gunSetting.bulletPfb;
                            cachedWeaponShootKey = gunSetting.shootKey;
                            UpdateWeaponShootEventName();
                            ModBehaviour.DevLog($"[DragonKing] 从手持武器缓存: 子弹={cachedWeaponBullet.name}, 音效={cachedWeaponShootKey}, 速度={cachedWeaponBulletSpeed}");
                            CacheSunDancePiercingData();
                            return;
                        }
                    }

                    // 尝试直接从gun.Item获取
                    if (gun.Item != null)
                    {
                        var itemGunSetting = gun.Item.GetComponent<ItemSetting_Gun>();
                        if (itemGunSetting != null && itemGunSetting.bulletPfb != null)
                        {
                            cachedWeaponBullet = itemGunSetting.bulletPfb;
                            cachedWeaponShootKey = itemGunSetting.shootKey;
                            UpdateWeaponShootEventName();
                            ModBehaviour.DevLog($"[DragonKing] 从武器Item缓存: 子弹={cachedWeaponBullet.name}, 音效={cachedWeaponShootKey}, 速度={cachedWeaponBulletSpeed}");
                            CacheSunDancePiercingData();
                            return;
                        }
                    }
                }

                // 方法2：从主武器槽位获取
                var primSlot = bossCharacter.PrimWeaponSlot();
                if (primSlot != null && primSlot.Content != null)
                {
                    var weaponItem = primSlot.Content;
                    var itemGunSetting = weaponItem.GetComponent<ItemSetting_Gun>();
                    if (itemGunSetting != null && itemGunSetting.bulletPfb != null)
                    {
                        cachedWeaponBullet = itemGunSetting.bulletPfb;
                        cachedWeaponShootKey = itemGunSetting.shootKey;
                        UpdateWeaponShootEventName();
                        // 尝试从Item获取子弹速度
                        float speed = weaponItem.GetStatValue("BulletSpeed".GetHashCode());
                        if (speed > 0) cachedWeaponBulletSpeed = speed;
                        ModBehaviour.DevLog($"[DragonKing] 从主武器槽位缓存: 子弹={cachedWeaponBullet.name}, 武器={weaponItem.name}, 音效={cachedWeaponShootKey}, 速度={cachedWeaponBulletSpeed}");
                        CacheSunDancePiercingData();
                        return;
                    }
                }

                // 方法3：不使用默认子弹，如果没有找到就记录警告
                if (cachedWeaponBullet == null)
                {
                    ModBehaviour.DevLog("[DragonKing] [WARNING] 未能缓存子弹预制体，太阳舞弹幕将不可用");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 缓存武器子弹失败: {e.Message}");
            }
        }

        private void UpdateWeaponShootEventName()
        {
            if (string.IsNullOrEmpty(cachedWeaponShootKey))
            {
                cachedWeaponShootEventName = null;
                return;
            }

            cachedWeaponShootEventName = "SFX/Combat/Gun/Shoot/" + cachedWeaponShootKey.ToLowerInvariant();
        }

        /// <summary>
        /// 缓存太阳舞穿墙所需的反射字段和LayerMask值（只调用一次）
        /// 因为Projectile.Init()会重置hitLayers，所以必须在每发子弹Init后设置
        /// 但为了性能，反射字段只查找一次
        /// </summary>
        private void CacheSunDancePiercingData()
        {
            if (cachedHitLayersField != null) return; // 已缓存

            try
            {
                // 缓存反射字段
                var projectileType = typeof(Projectile);
                cachedHitLayersField = projectileType.GetField("hitLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                // 直接使用GameplayDataSettings的LayerMask（本身就是LayerMask类型）
                piercingLayerMask = GameplayDataSettings.Layers.damageReceiverLayerMask;

                ModBehaviour.DevLog("[DragonKing] 太阳舞穿墙数据已缓存");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 缓存穿墙数据失败: {e.Message}");
            }
        }

        /// <summary>
        /// 加载AssetBundle资源
        /// </summary>
        private void LoadAssets()
        {
            try
            {
                string modBasePath = System.IO.Path.GetDirectoryName(GetType().Assembly.Location);
                DragonKingAssetManager.LoadAssetBundleSync(modBasePath);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 加载资源失败: {e.Message}");
            }
        }

        /// <summary>
        /// 初始化碰撞检测器
        /// </summary>
        private void InitializeCollisionDetector()
        {
            try
            {
                if (bossCharacter == null) return;

                // 创建碰撞检测器子对象
                GameObject detectorObj = new GameObject("DragonKing_CollisionDetector");
                detectorObj.transform.SetParent(bossCharacter.transform);
                detectorObj.transform.localPosition = Vector3.zero;

                // 添加碰撞检测器组件
                collisionDetector = detectorObj.AddComponent<DragonKingCollisionDetector>();
                collisionDetector.Initialize(this);

                ModBehaviour.DevLog($"[DragonKing] 碰撞检测器初始化完成，半径={DragonKingConfig.CollisionRadius}");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 初始化碰撞检测器失败: {e.Message}");
            }
        }

        /// <summary>
        /// 上次更新玩家引用的时间
        /// </summary>
        private float lastPlayerRefUpdateTime = 0f;

        /// <summary>
        /// 玩家引用更新间隔（秒）- 不需要每帧更新
        /// </summary>
        private const float PLAYER_REF_UPDATE_INTERVAL = 0.1f;

        /// <summary>
        /// 更新玩家引用（带节流，避免每帧调用）
        /// Mode E 下从原版AI的 searchedEnemy 获取攻击目标，而非固定锁定玩家
        /// </summary>
        private void UpdatePlayerReference()
        {
            // 性能优化：节流更新，每0.1秒更新一次
            if (Time.time - lastPlayerRefUpdateTime < PLAYER_REF_UPDATE_INTERVAL && playerCharacter != null)
            {
                return;
            }

            lastPlayerRefUpdateTime = Time.time;

            try
            {
                var inst = ModBehaviour.Instance;
                if (inst != null && inst.IsModeEActive)
                {
                    // 【Mode E】从原版AI的 searchedEnemy 获取攻击目标
                    var ai = aiController != null ? aiController.GetAI() : null;
                    if (ai != null && ai.searchedEnemy != null)
                    {
                        // searchedEnemy 是 DamageReceiver，通过 Health 获取 CharacterMainControl
                        var targetHealth = ai.searchedEnemy.health;
                        if (targetHealth != null)
                        {
                            var targetChar = targetHealth.TryGetCharacter();
                            if (targetChar != null)
                            {
                                playerCharacter = targetChar;
                                return;
                            }
                        }
                    }
                    // Mode E 下无仇恨目标时清空 playerCharacter，防止锁定玩家
                    playerCharacter = null;
                }
                else
                {
                    // 非 Mode E：始终锁定玩家（原有逻辑）
                    playerCharacter = CharacterMainControl.Main;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] UpdatePlayerReference异常: {e.Message}");
            }
        }

        /// <summary>
        /// 检查玩家是否已死亡或不可用（用于协程中快速判断，避免对已死亡玩家继续攻击）
        /// </summary>
        private static int ReserveAttackDesyncSeed()
        {
            int seed = nextAttackDesyncSeed;
            nextAttackDesyncSeed = (nextAttackDesyncSeed + 1) % 1024;
            return seed;
        }

        private int GetSequenceStartIndex(DragonKingAttackType[] sequence)
        {
            if (sequence == null || sequence.Length == 0)
            {
                return 0;
            }

            return attackDesyncSeed % sequence.Length;
        }

        private float GetAttackLoopStartupDelay()
        {
            return (attackDesyncSeed % ATTACK_LOOP_DESYNC_BUCKET_COUNT) * ATTACK_LOOP_DESYNC_STEP;
        }

        private bool TryRefreshSharedTargetSnapshot(CharacterMainControl targetCharacter, bool forceRefresh = false)
        {
            if (targetCharacter == null || targetCharacter.Health == null || targetCharacter.Health.IsDead)
            {
                if (sharedTrackedTarget != targetCharacter)
                {
                    sharedTrackedTarget = targetCharacter;
                    sharedTrackedTargetRigidbody = null;
                }

                hasSharedTrackedTargetSnapshot = false;
                sharedTrackedTargetAimPosition = Vector3.zero;
                sharedTrackedTargetVelocity = Vector3.zero;
                return false;
            }

            bool targetChanged = sharedTrackedTarget != targetCharacter;
            bool shouldRefresh = targetChanged || !hasSharedTrackedTargetSnapshot;

            if (forceRefresh)
            {
                shouldRefresh = shouldRefresh || lastSharedTargetSampleFrame != Time.frameCount;
            }
            else
            {
                shouldRefresh = shouldRefresh || Time.time - lastSharedTargetSampleTime >= PLAYER_AIM_SAMPLE_INTERVAL;
            }

            if (shouldRefresh)
            {
                sharedTrackedTarget = targetCharacter;
                if (sharedTrackedTargetRigidbody == null || targetChanged || sharedTrackedTargetRigidbody.gameObject != targetCharacter.gameObject)
                {
                    sharedTrackedTargetRigidbody = targetCharacter.GetComponent<Rigidbody>();
                }

                sharedTrackedTargetAimPosition = targetCharacter.transform.position + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;
                sharedTrackedTargetVelocity = sharedTrackedTargetRigidbody != null ? sharedTrackedTargetRigidbody.velocity : Vector3.zero;
                lastSharedTargetSampleTime = Time.time;
                lastSharedTargetSampleFrame = Time.frameCount;
                hasSharedTrackedTargetSnapshot = true;
            }

            return hasSharedTrackedTargetSnapshot;
        }

        private bool TryGetPlayerAimPosition(out Vector3 targetPosition, bool forceRefresh = false)
        {
            UpdatePlayerReference();

            if (TryRefreshSharedTargetSnapshot(playerCharacter, forceRefresh))
            {
                targetPosition = sharedTrackedTargetAimPosition;
                return true;
            }

            targetPosition = Vector3.zero;
            return false;
        }

        private bool TryGetPlayerSnapshot(out CharacterMainControl targetCharacter, out Vector3 targetPosition, bool forceRefresh = false)
        {
            targetCharacter = null;
            targetPosition = Vector3.zero;

            UpdatePlayerReference();
            targetCharacter = playerCharacter;
            if (targetCharacter == null || targetCharacter.Health == null || targetCharacter.Health.IsDead)
            {
                return false;
            }

            if (!TryRefreshSharedTargetSnapshot(targetCharacter, forceRefresh))
            {
                return false;
            }

            targetPosition = sharedTrackedTargetAimPosition;
            return true;
        }

        private static bool TryGetSharedTargetHitboxBounds(CharacterMainControl targetCharacter, out Bounds hitboxBounds)
        {
            hitboxBounds = default(Bounds);
            if (targetCharacter == null || targetCharacter.gameObject == null)
            {
                return false;
            }

            Transform targetRoot = targetCharacter.transform.root;
            bool needsRefresh =
                sharedTargetHitboxBoundsOwner != targetCharacter ||
                sharedTargetHitboxBoundsRoot != targetRoot ||
                !hasSharedTargetHitboxBounds ||
                Time.time - lastSharedTargetHitboxBoundsRefreshTime >= PLAYER_HITBOX_BOUNDS_REFRESH_INTERVAL;

            if (needsRefresh)
            {
                sharedTargetHitboxBoundsOwner = targetCharacter;
                sharedTargetHitboxBoundsRoot = targetRoot;
                hasSharedTargetHitboxBounds = TryBuildTargetHitboxBounds(targetRoot, out sharedTargetHitboxBounds);
                lastSharedTargetHitboxBoundsRefreshTime = Time.time;
            }

            hitboxBounds = sharedTargetHitboxBounds;
            return hasSharedTargetHitboxBounds;
        }

        private static bool TryBuildTargetHitboxBounds(Transform targetRoot, out Bounds hitboxBounds)
        {
            hitboxBounds = default(Bounds);
            if (targetRoot == null)
            {
                return false;
            }

            Collider[] colliders = targetRoot.GetComponentsInChildren<Collider>(true);
            bool hasBounds = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    hitboxBounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    hitboxBounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        private static bool TryPassSharedSoundThrottle(string throttleKey, float minInterval)
        {
            if (string.IsNullOrEmpty(throttleKey) || minInterval <= 0f)
            {
                return true;
            }

            float now = Time.time;
            float lastTime;
            if (sharedSoundThrottleTimestamps.TryGetValue(throttleKey, out lastTime) && now - lastTime < minInterval)
            {
                return false;
            }

            sharedSoundThrottleTimestamps[throttleKey] = now;
            return true;
        }

        private void PlaySharedDragonSound(string filePath, string throttleKey, float minInterval)
        {
            if (!TryPassSharedSoundThrottle(throttleKey, minInterval))
            {
                return;
            }

            ModBehaviour.Instance?.PlaySoundEffect(filePath);
        }

        private bool IsPlayerDead()
        {
            return playerCharacter == null || playerCharacter.Health == null || playerCharacter.Health.IsDead;
        }

        /// <summary>
        /// 【Mode E 专用】检查原版AI是否有仇恨目标
        /// Mode E 下龙皇的自定义射击和技能释放前必须检查此条件，
        /// 只有原版AI搜索到敌人时才允许攻击，避免无仇恨目标时远距离锁定其他阵营Boss
        /// 非 Mode E 模式下始终返回 true（不影响原有逻辑）
        /// </summary>
        private bool HasValidTargetForModeE()
        {
            var inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsModeEActive)
                return true; // 非 Mode E，不限制

            // 检查原版AI的 searchedEnemy
            var ai = aiController != null ? aiController.GetAI() : null;
            if (ai == null)
                return false; // AI不可用，不攻击

            return ai.searchedEnemy != null;
        }

        // ========== 生命周期 ==========

        /// <summary>
        /// Boss死亡时调用
        /// </summary>
        public void OnBossDeath()
        {
            // 播放死亡音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_Death);

            CurrentPhase = DragonKingPhase.Dead;

            // 停止自定义射击
            isCustomShootingActive = false;
            if (customShootingCoroutine != null)
            {
                StopCoroutine(customShootingCoroutine);
                customShootingCoroutine = null;
            }

            // 停止太阳舞弹幕
            isSunDanceActive = false;
            if (sunDanceBarrageCoroutine != null)
            {
                StopCoroutine(sunDanceBarrageCoroutine);
                sunDanceBarrageCoroutine = null;
            }

            // 清理孩儿护我状态
            CleanupChildProtection();

            // 停止所有协程
            StopAllCoroutines();

            // 清理所有活跃特效
            CleanupAllEffects();

            ModBehaviour.DevLog("[DragonKing] Boss死亡，清理完成");
        }

        /// <summary>
        /// 玩家死亡时调用 - 立即停止所有攻击协程和清理弹幕
        /// 避免对已死亡/不活跃的玩家对象持续操作导致掉帧
        /// 采用一次性清理而非每帧检查，零持续开销，低端机友好
        /// </summary>
        public void OnPlayerDeath()
        {
            ModBehaviour.DevLog("[DragonKing] 检测到玩家死亡，停止所有攻击");

            // 停止自定义射击
            isCustomShootingActive = false;
            if (customShootingCoroutine != null)
            {
                StopCoroutine(customShootingCoroutine);
                customShootingCoroutine = null;
            }

            // 停止太阳舞弹幕
            isSunDanceActive = false;
            if (sunDanceBarrageCoroutine != null)
            {
                StopCoroutine(sunDanceBarrageCoroutine);
                sunDanceBarrageCoroutine = null;
            }

            // 停止所有协程（包括AttackLoop、MoveLance、TrackingProjectileCore等）
            StopAllCoroutines();

            // 清理所有活跃特效和弹幕
            CleanupAllEffects();

            // 恢复AI行为（防止Boss卡在暂停状态）
            ResumeBossMovementAndShooting();

            ModBehaviour.DevLog("[DragonKing] 玩家死亡清理完成");
        }

        /// <summary>
        /// 组件销毁时调用（防止协程和内存泄漏）
        /// 合并了所有清理逻辑，确保资源正确释放
        /// </summary>
        private void OnDestroy()
        {
            // 取消订阅事件（防止内存泄漏）
            if (bossHealth != null)
            {
                bossHealth.OnHurtEvent.RemoveListener(OnBossHurt);
            }

            // 停止自定义射击
            isCustomShootingActive = false;
            customShootingCoroutine = null;

            // 停止太阳舞弹幕
            isSunDanceActive = false;
            sunDanceBarrageCoroutine = null;

            // 清理孩儿护我状态
            CleanupChildProtection();

            // 停止所有协程（防止协程泄漏）
            StopAllCoroutines();

            // 停止所有弹幕追踪协程
            StopAllProjectileCoroutines();

            // 清理AI控制器
            if (aiController != null)
            {
                aiController.Cleanup();
                aiController = null;
            }

            // 清理碰撞检测器
            if (collisionDetector != null)
            {
                Destroy(collisionDetector.gameObject);
                collisionDetector = null;
            }

            // 清理所有活跃特效
            CleanupAllEffects();

            // 清理所有动态创建的Material（防止内存泄漏）
            CleanupDynamicMaterials();

            // 清理引用
            bossCharacter = null;
            bossTransform = null;
            bossHealth = null;
            playerCharacter = null;
            cachedWeaponBullet = null;
            cachedWeaponShootEventName = null;

            ModBehaviour.DevLog("[DragonKing] 组件销毁，资源清理完成");
        }

        /// <summary>
        /// 清理所有活跃特效和弹幕
        /// </summary>
        private void CleanupAllEffects()
        {
            // 停止所有弹幕追踪协程
            StopAllProjectileCoroutines();

            // 清理特效（包含动态创建的 Material）
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                GameObject effect = activeEffects[i];
                activeEffects.RemoveAt(i);
                if (effect != null)
                {
                    // 释放特效前先清理实例化材质
                    CleanupMaterialsInObject(effect);
                    ReleaseActiveEffect(effect);
                }
            }

            // 清理自定义弹幕（非BulletPool管理的）
            foreach (var projectile in activeProjectiles)
            {
                if (projectile != null)
                {
                    DragonKingAssetManager.ReleaseEffect(projectile);
                }
            }
            activeProjectiles.Clear();

            // 清理警告线
            for (int i = activeWarningLines.Count - 1; i >= 0; i--)
            {
                GameObject line = activeWarningLines[i];
                activeWarningLines.RemoveAt(i);
                if (line != null)
                {
                    CleanupMaterialsInObject(line);
                    ReturnWarningLine(line);
                }
            }

            // 清理动态创建的Material
            CleanupDynamicMaterials();
        }

        /// <summary>
        /// 停止所有弹幕追踪协程
        /// </summary>
        private void StopAllProjectileCoroutines()
        {
            for (int i = activeTrackingProjectiles.Count - 1; i >= 0; i--)
            {
                GameObject projectile = activeTrackingProjectiles[i].projectile;
                if (projectile != null)
                {
                    ReleaseManagedProjectile(projectile);
                }
            }

            for (int i = activeLances.Count - 1; i >= 0; i--)
            {
                GameObject lance = activeLances[i].lance;
                if (lance != null)
                {
                    ReleaseManagedProjectile(lance);
                }
            }

            activeTrackingProjectiles.Clear();
            activeLances.Clear();

            foreach (var coroutine in activeProjectileCoroutines)
            {
                if (coroutine != null)
                {
                    StopCoroutine(coroutine);
                }
            }
            activeProjectileCoroutines.Clear();
        }

        private void Update()
        {
            UpdateTrackingProjectiles();
            UpdateActiveLances();
        }

        private void RegisterTrackingProjectile(GameObject projectile, float lifetime, float trackingDuration = -1f)
        {
            if (projectile == null) return;

            if (trackingDuration < 0f)
            {
                trackingDuration = DragonKingConfig.PrismaticBoltTrackingDuration;
            }

            float speed = DragonKingConfig.PrismaticBoltSpeed;
            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            Vector3 currentVelocity = projectile.transform.forward * speed;
            if (rb != null)
            {
                rb.velocity = currentVelocity;
            }

            TrackManagedProjectile(projectile);
            activeTrackingProjectiles.Add(new TrackingProjectileState
            {
                projectile = projectile,
                rigidbody = rb,
                startTime = Time.time,
                lifetime = lifetime,
                trackingDuration = trackingDuration,
                speed = speed,
                trackingStrength = DragonKingConfig.PrismaticBoltTrackingStrength,
                currentVelocity = currentVelocity,
                trackingEnded = false
            });
        }

        private void UpdateTrackingProjectiles()
        {
            if (activeTrackingProjectiles.Count == 0) return;

            Vector3 targetPos;
            bool hasTargetSnapshot = TryGetPlayerSnapshot(out _, out targetPos);

            for (int i = activeTrackingProjectiles.Count - 1; i >= 0; i--)
            {
                if (i >= activeTrackingProjectiles.Count)
                {
                    continue;
                }

                TrackingProjectileState state = activeTrackingProjectiles[i];
                GameObject projectile = state.projectile;

                if (projectile == null || !projectile.activeInHierarchy)
                {
                    activeTrackingProjectiles.RemoveAt(i);
                    continue;
                }

                float elapsed = Time.time - state.startTime;
                if (elapsed >= state.lifetime)
                {
                    ReleaseManagedProjectile(projectile);
                    activeTrackingProjectiles.RemoveAt(i);
                    continue;
                }

                Vector3 currentPos = projectile.transform.position;
                if (elapsed < state.trackingDuration && !state.trackingEnded)
                {
                    if (hasTargetSnapshot)
                    {
                        Vector3 dirToTarget = (targetPos - currentPos).normalized;
                        if (state.rigidbody == null && state.currentVelocity.sqrMagnitude < 0.01f)
                        {
                            state.currentVelocity = dirToTarget * state.speed;
                        }
                        else
                        {
                            Vector3 desiredVelocity = dirToTarget * state.speed;
                            state.currentVelocity = Vector3.Lerp(
                                state.currentVelocity,
                                desiredVelocity,
                                state.trackingStrength * Time.deltaTime * 5f);
                        }
                    }
                }
                else if (!state.trackingEnded)
                {
                    state.trackingEnded = true;
                    float currentVelocitySqr = state.currentVelocity.sqrMagnitude;
                    if (currentVelocitySqr > 0.01f)
                    {
                        state.currentVelocity *= state.speed / Mathf.Sqrt(currentVelocitySqr);
                    }
                }

                if (state.rigidbody != null)
                {
                    state.rigidbody.velocity = state.currentVelocity;
                }
                else
                {
                    projectile.transform.position += state.currentVelocity * Time.deltaTime;
                }

                if (state.currentVelocity.sqrMagnitude > 0.01f)
                {
                    projectile.transform.rotation = Quaternion.LookRotation(state.currentVelocity);
                }

                if (CheckProjectileHit(currentPos, DragonKingConfig.PrismaticBoltDamage, hasTargetSnapshot, targetPos))
                {
                    ReleaseManagedProjectile(projectile);
                    if (i < activeTrackingProjectiles.Count)
                    {
                        activeTrackingProjectiles.RemoveAt(i);
                    }
                    continue;
                }

                activeTrackingProjectiles[i] = state;
            }
        }

        private void RegisterLanceProjectile(GameObject lance, Vector3 direction, float speed, float maxDistance)
        {
            if (lance == null) return;

            TrackManagedProjectile(lance);
            activeLances.Add(new LanceProjectileState
            {
                lance = lance,
                direction = direction,
                speed = speed,
                maxDistance = maxDistance,
                traveled = 0f
            });
        }

        private void UpdateActiveLances()
        {
            if (activeLances.Count == 0) return;

            CharacterMainControl targetCharacter;
            Vector3 targetPos;
            bool hasTargetSnapshot = TryGetPlayerSnapshot(out targetCharacter, out targetPos);

            for (int i = activeLances.Count - 1; i >= 0; i--)
            {
                if (i >= activeLances.Count)
                {
                    continue;
                }

                LanceProjectileState state = activeLances[i];
                GameObject lance = state.lance;

                if (lance == null || !lance.activeInHierarchy)
                {
                    activeLances.RemoveAt(i);
                    continue;
                }

                float moveDelta = state.speed * Time.deltaTime;
                lance.transform.position += state.direction * moveDelta;
                state.traveled += moveDelta;

                if (CheckLanceHit(lance, hasTargetSnapshot, targetCharacter, targetPos) || state.traveled >= state.maxDistance)
                {
                    ReleaseManagedProjectile(lance);
                    if (i < activeLances.Count)
                    {
                        activeLances.RemoveAt(i);
                    }
                    continue;
                }

                activeLances[i] = state;
            }
        }

        private void ReleaseManagedProjectile(GameObject projectile)
        {
            if (projectile == null) return;

            activeProjectiles.Remove(projectile);
            DragonKingAssetManager.ReleaseEffect(projectile);
        }

        private void TrackManagedProjectile(GameObject projectile)
        {
            if (projectile == null) return;
            activeProjectiles.Add(projectile);
        }



        /// <summary>
        /// 清理GameObject中的所有动态Material
        /// </summary>
        private void CleanupMaterialsInObject(GameObject obj)
        {
            if (obj == null) return;

            try
            {
                // 使用 sharedMaterial 避免在清理阶段意外实例化新材质
                var renderers = obj.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;

                    Material material = renderer.sharedMaterial;
                    if (material != null && material.name.Contains("(Instance)"))
                    {
                        Destroy(material);
                    }
                }

                // 清理 LineRenderer 的材质
                var lineRenderers = obj.GetComponentsInChildren<LineRenderer>(true);
                foreach (var lr in lineRenderers)
                {
                    if (lr == null) continue;

                    Material material = lr.sharedMaterial;
                    if (material != null && material.name.Contains("(Instance)"))
                    {
                        Destroy(material);
                    }
                }

                // 清理 ParticleSystemRenderer 的材质
                var psRenderers = obj.GetComponentsInChildren<ParticleSystemRenderer>(true);
                foreach (var psr in psRenderers)
                {
                    if (psr == null) continue;

                    Material material = psr.sharedMaterial;
                    if (material != null && material.name.Contains("(Instance)"))
                    {
                        Destroy(material);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 清理Material失败: {e.Message}");
            }
        }

        /// <summary>
        /// 清理动态创建的Material列表
        /// </summary>
        private void CleanupDynamicMaterials()
        {
            foreach (var mat in dynamicMaterials)
            {
                if (mat != null)
                {
                    Destroy(mat);
                }
            }
            dynamicMaterials.Clear();
        }

        private void TrackActiveEffect(GameObject effect)
        {
            if (effect == null) return;
            if (activeEffects.Contains(effect)) return;

            DragonKingPooledEffect pooledEffect = effect.GetComponent<DragonKingPooledEffect>();
            if (pooledEffect != null)
            {
                pooledEffect.SetOwnerReleaseTracker(UntrackActiveEffect);
            }

            activeEffects.Add(effect);
        }

        private void UntrackActiveEffect(GameObject effect)
        {
            if (effect == null) return;
            activeEffects.Remove(effect);
        }

        private void ReleaseActiveEffect(GameObject effect)
        {
            if (effect == null) return;

            if (effect.name == "WarningCircle")
            {
                ReturnWarningCircle(effect);
                return;
            }

            if (effect.name == "DashChargeParticles")
            {
                ReturnDashChargeParticles(effect);
                return;
            }

            if (effect.name == "DashCountdownRing")
            {
                ReturnDashCountdownRing(effect);
                return;
            }

            DragonKingAssetManager.ReleaseEffect(effect);
        }

        private static GameObject CreateWarningLineObject()
        {
            GameObject lineObj = new GameObject("EtherealLanceWarningLine");
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            Material mat = GetSharedInternalColoredMaterial();
            if (mat != null)
            {
                lr.sharedMaterial = mat;
            }

            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.sortingOrder = 100;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 2;
            sharedWarningLineCreatedCount++;
            return lineObj;
        }

        private static GameObject RentWarningLine()
        {
            while (sharedWarningLinePool.Count > 0)
            {
                GameObject pooledLine = sharedWarningLinePool.Pop();
                if (pooledLine != null)
                {
                    pooledLine.SetActive(true);
                    return pooledLine;
                }
            }

            GameObject lineObj = CreateWarningLineObject();
            lineObj.SetActive(true);
            return lineObj;
        }

        private static void ReturnWarningLine(GameObject line)
        {
            if (line == null) return;

            line.transform.SetParent(null, false);
            line.SetActive(false);
            sharedWarningLinePool.Push(line);
        }

        private static GameObject CreateWarningCircleObject()
        {
            GameObject circleObj = new GameObject("WarningCircle");
            LineRenderer lineRenderer = circleObj.AddComponent<LineRenderer>();
            Material mat = GetSharedYellowWarningMaterial();
            if (mat != null)
            {
                lineRenderer.sharedMaterial = mat;
            }

            lineRenderer.startWidth = 0.15f;
            lineRenderer.endWidth = 0.15f;
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = WARNING_CIRCLE_SEGMENTS;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            Vector3[] unitPoints = GetWarningCircleUnitPoints();
            for (int i = 0; i < unitPoints.Length; i++)
            {
                lineRenderer.SetPosition(i, unitPoints[i] * WARNING_CIRCLE_START_RADIUS);
            }

            circleObj.AddComponent<WarningCircleAnimation>();
            sharedWarningCircleCreatedCount++;
            return circleObj;
        }

        private static GameObject RentWarningCircle()
        {
            while (sharedWarningCirclePool.Count > 0)
            {
                GameObject pooledCircle = sharedWarningCirclePool.Pop();
                if (pooledCircle != null)
                {
                    pooledCircle.SetActive(true);
                    return pooledCircle;
                }
            }

            GameObject circleObj = CreateWarningCircleObject();
            circleObj.SetActive(true);
            return circleObj;
        }

        private static void ReturnWarningCircle(GameObject circleObj)
        {
            if (circleObj == null) return;

            circleObj.transform.SetParent(null, false);
            circleObj.transform.localScale = Vector3.one;
            circleObj.SetActive(false);
            sharedWarningCirclePool.Push(circleObj);
        }

        private static GameObject CreateDashChargeParticlesObject()
        {
            GameObject particleObj = new GameObject("DashChargeParticles");

            ParticleSystem ps = particleObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = DragonKingConfig.DashChargeTime;
            main.loop = false;
            main.startLifetime = 0.5f;
            main.startSpeed = -5f;
            main.startSize = 0.2f;
            main.startColor = new Color(1f, 1f, 1f, 1f);
            main.maxParticles = 100;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 50f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 2.5f;
            shape.radiusThickness = 0f;

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                Material particleMat = GetSharedWhiteParticleMaterial();
                if (particleMat != null)
                {
                    renderer.sharedMaterial = particleMat;
                }
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(0.9f, 0.95f, 1f), 0f),
                    new GradientColorKey(new Color(1f, 1f, 1f), 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            Light light = particleObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.9f, 0.95f, 1f);
            light.intensity = 3f;
            light.range = 4f;

            LightFlicker flicker = particleObj.AddComponent<LightFlicker>();
            flicker.baseIntensity = 3f;
            flicker.flickerAmount = 1.5f;
            flicker.flickerSpeed = 10f;

            sharedDashChargeEffectCreatedCount++;
            return particleObj;
        }

        private GameObject RentDashChargeParticles(Transform parent)
        {
            GameObject particleObj = null;
            while (sharedDashChargeEffectPool.Count > 0 && particleObj == null)
            {
                particleObj = sharedDashChargeEffectPool.Pop();
            }

            if (particleObj == null)
            {
                particleObj = CreateDashChargeParticlesObject();
            }

            particleObj.transform.SetParent(parent, false);
            particleObj.transform.localPosition = Vector3.up * 1f;
            particleObj.transform.localRotation = Quaternion.identity;
            particleObj.SetActive(true);

            ParticleSystem ps = particleObj.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Clear(true);
                ps.Play(true);
            }

            Light light = particleObj.GetComponent<Light>();
            if (light != null)
            {
                light.enabled = true;
            }

            return particleObj;
        }

        private static void ReturnDashChargeParticles(GameObject particleObj)
        {
            if (particleObj == null) return;

            ParticleSystem ps = particleObj.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            Light light = particleObj.GetComponent<Light>();
            if (light != null)
            {
                light.enabled = false;
            }

            particleObj.transform.SetParent(null, false);
            particleObj.SetActive(false);
            sharedDashChargeEffectPool.Push(particleObj);
        }

        private static GameObject CreateDashCountdownRingObject()
        {
            GameObject ringObj = new GameObject("DashCountdownRing");
            LineRenderer lineRenderer = ringObj.AddComponent<LineRenderer>();

            Material mat = GetSharedInternalColoredMaterial();
            if (mat != null)
            {
                Color ringColor = new Color(1f, 1f, 1f, 0.9f);
                lineRenderer.sharedMaterial = mat;
                lineRenderer.startColor = ringColor;
                lineRenderer.endColor = ringColor;
            }

            lineRenderer.startWidth = 0.25f;
            lineRenderer.endWidth = 0.25f;
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = WARNING_CIRCLE_SEGMENTS;

            Vector3[] unitPoints = GetWarningCircleUnitPoints();
            for (int i = 0; i < unitPoints.Length; i++)
            {
                lineRenderer.SetPosition(i, unitPoints[i] * 2f);
            }

            RingShrinkAnimation shrink = ringObj.AddComponent<RingShrinkAnimation>();
            shrink.duration = DragonKingConfig.DashCountdownRingTime;
            shrink.startScale = 1f;
            shrink.endScale = 0f;

            Light light = ringObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.9f, 0.95f, 1f);
            light.intensity = 2f;
            light.range = 3f;

            sharedDashCountdownRingCreatedCount++;
            return ringObj;
        }

        private GameObject RentDashCountdownRing(Vector3 position, float duration)
        {
            GameObject ringObj = null;
            while (sharedDashCountdownRingPool.Count > 0 && ringObj == null)
            {
                ringObj = sharedDashCountdownRingPool.Pop();
            }

            if (ringObj == null)
            {
                ringObj = CreateDashCountdownRingObject();
            }

            ringObj.transform.position = position + Vector3.up * 0.05f;
            ringObj.transform.rotation = Quaternion.identity;
            ringObj.transform.localScale = Vector3.one;
            ringObj.SetActive(true);

            RingShrinkAnimation shrink = ringObj.GetComponent<RingShrinkAnimation>();
            if (shrink != null)
            {
                shrink.ResetAnimation(duration, 1f, 0f);
            }

            Light light = ringObj.GetComponent<Light>();
            if (light != null)
            {
                light.enabled = true;
            }

            return ringObj;
        }

        private static void ReturnDashCountdownRing(GameObject ringObj)
        {
            if (ringObj == null) return;

            Light light = ringObj.GetComponent<Light>();
            if (light != null)
            {
                light.enabled = false;
            }

            ringObj.transform.SetParent(null, false);
            ringObj.transform.localScale = Vector3.one;
            ringObj.SetActive(false);
            sharedDashCountdownRingPool.Push(ringObj);
        }

    }
}
