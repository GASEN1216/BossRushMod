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
    // ========== 碰撞检测器组件 ==========

    /// <summary>
    /// 龙王碰撞检测器组件
    /// 用于检测Boss与玩家的碰撞并触发伤害
    /// [修复] 使用距离检测替代触发器事件，避免多碰撞器导致的异常
    /// </summary>
    public class DragonKingCollisionDetector : MonoBehaviour
    {
        private DragonKingAbilityController controller;

        /// <summary>
        /// 上次碰撞检查时间（用于冷却）
        /// </summary>
        private float lastCollisionCheckTime = 0f;

        /// <summary>
        /// 碰撞检查冷却时间
        /// </summary>
        private const float CHECK_COOLDOWN = 0.5f;

        /// <summary>
        /// 碰撞检测半径
        /// </summary>
        private float collisionRadius = 1.5f;
        private float collisionRadiusSqr = 2.25f;
        private float lastDistanceCheckTime = 0f;
        private const float DISTANCE_CHECK_INTERVAL = 0.05f;

        /// <summary>
        /// 缓存的玩家引用
        /// </summary>
        private CharacterMainControl cachedPlayer = null;
        private Transform cachedPlayerTransform;
        private Transform cachedTransform;

        private Transform CachedTransform
        {
            get
            {
                if (cachedTransform == null)
                {
                    cachedTransform = transform;
                }

                return cachedTransform;
            }
        }

        /// <summary>
        /// 初始化碰撞检测器
        /// </summary>
        public void Initialize(DragonKingAbilityController ctrl)
        {
            controller = ctrl;
            collisionRadius = DragonKingConfig.CollisionRadius;
            collisionRadiusSqr = collisionRadius * collisionRadius;

            ModBehaviour.DevLog("[DragonKing] 碰撞检测器组件初始化完成（使用距离检测模式）");
        }

        void Update()
        {
            // 每帧检测距离，替代触发器事件
            if (controller == null) return;
            if (!enabled) return;
            if (Time.time - lastDistanceCheckTime < DISTANCE_CHECK_INTERVAL) return;

            lastDistanceCheckTime = Time.time;

            // 获取玩家引用
            if (cachedPlayer == null || !cachedPlayer.gameObject.activeInHierarchy)
            {
                cachedPlayer = CharacterMainControl.Main;
                if (cachedPlayer != null)
                {
                    cachedPlayerTransform = cachedPlayer.transform;
                }
                else
                {
                    cachedPlayerTransform = null;
                }
            }

            if (cachedPlayer == null) return;
            if (cachedPlayerTransform == null) return;

            // 计算与玩家的距离
            Vector3 diff = CachedTransform.position - cachedPlayerTransform.position;

            // 如果在碰撞范围内，尝试触发碰撞
            if (diff.sqrMagnitude <= collisionRadiusSqr)
            {
                TryTriggerCollision();
            }
        }

        /// <summary>
        /// 尝试触发碰撞（带冷却检查）
        /// </summary>
        private void TryTriggerCollision()
        {
            if (controller == null || cachedPlayer == null) return;
            if (Time.time - lastCollisionCheckTime < CHECK_COOLDOWN) return;

            lastCollisionCheckTime = Time.time;
            controller.OnCollisionWithPlayer(cachedPlayer);
        }

        private void OnDestroy()
        {
            cachedPlayer = null;
            controller = null;
        }
    }

    /// <summary>
    /// 太阳舞光束伤害触发器
    /// 挂载到Edges子对象上，玩家碰到时持续造成伤害
    /// </summary>
    public class SunBeamDamageTrigger : MonoBehaviour
    {
        private DragonKingAbilityController controller;
        private float lastDamageTime = 0f;
        private const float DAMAGE_INTERVAL = 0.2f; // 伤害间隔
        private CharacterMainControl cachedMainCharacter;
        private Transform cachedMainRoot;

        public void Initialize(DragonKingAbilityController ctrl)
        {
            controller = ctrl;
            lastDamageTime = 0f;
            CacheMainCharacter();
        }

        void OnTriggerEnter(Collider other)
        {
            ProcessCollision(other);
        }

        void OnTriggerStay(Collider other)
        {
            ProcessCollision(other);
        }

        private void ProcessCollision(Collider other)
        {
            if (controller == null || other == null) return;
            float currentTime = Time.time;
            if (currentTime - lastDamageTime < DAMAGE_INTERVAL) return;

            CacheMainCharacter();
            if (cachedMainCharacter == null || cachedMainCharacter.Health == null || cachedMainCharacter.Health.IsDead)
            {
                return;
            }

            if (cachedMainRoot != null && other.transform.root == cachedMainRoot)
            {
                lastDamageTime = currentTime;
                controller.ApplySunBeamDamage();
                return;
            }

            var character = other.GetComponentInParent<CharacterMainControl>();
            if (character != null && character == cachedMainCharacter && !character.Health.IsDead)
            {
                lastDamageTime = currentTime;
                controller.ApplySunBeamDamage();
            }
        }

        private void CacheMainCharacter()
        {
            if (cachedMainCharacter != null && cachedMainCharacter.gameObject != null && cachedMainCharacter.gameObject.activeInHierarchy)
            {
                return;
            }

            cachedMainCharacter = CharacterMainControl.Main;
            cachedMainRoot = cachedMainCharacter != null ? cachedMainCharacter.transform.root : null;
        }
    }

    /// <summary>
    /// 太阳舞光束触发器缓存
    /// 只在首个实例化时扫描一次 Edge，后续复用同一组触发器
    /// </summary>
    public class SunBeamTriggerCache : MonoBehaviour
    {
        private SunBeamDamageTrigger[] triggers = null;

        public void WarmCache()
        {
            EnsureTriggersBuilt();
        }

        public int Initialize(DragonKingAbilityController controller)
        {
            EnsureTriggersBuilt();

            if (triggers == null)
            {
                return 0;
            }

            for (int i = 0; i < triggers.Length; i++)
            {
                if (triggers[i] != null)
                {
                    triggers[i].Initialize(controller);
                }
            }

            return triggers.Length;
        }

        private void EnsureTriggersBuilt()
        {
            if (triggers != null && triggers.Length > 0)
            {
                return;
            }

            Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
            List<SunBeamDamageTrigger> builtTriggers = new List<SunBeamDamageTrigger>(allTransforms.Length);

            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform current = allTransforms[i];
                if (current == null || !current.name.StartsWith("Edge"))
                {
                    continue;
                }

                SunBeamDamageTrigger trigger = current.GetComponent<SunBeamDamageTrigger>();
                if (trigger == null)
                {
                    trigger = current.gameObject.AddComponent<SunBeamDamageTrigger>();
                }

                builtTriggers.Add(trigger);
            }

            triggers = builtTriggers.ToArray();
        }
    }

    /// <summary>
    /// Boss AI 通用控制辅助类
    /// 提供统一的AI暂停/恢复功能，通过禁用组件实现优雅停止
    /// </summary>
    public class BossAIController
    {
        // ========== 静态缓存（避免重复反射） ==========

        /// <summary>
        /// 缓存的NodeCanvas.BehaviourTree类型
        /// </summary>
        private static System.Type cachedBehaviourTreeType = null;

        /// <summary>
        /// 缓存的NodeCanvas.Framework.Behaviour类型的enabled属性
        /// </summary>
        private static System.Reflection.PropertyInfo cachedBehaviourEnabledProperty = null;

        // ========== 实例变量 ==========

        /// <summary>
        /// Boss角色引用
        /// </summary>
        private CharacterMainControl bossCharacter;

        /// <summary>
        /// AI控制器引用
        /// </summary>
        private AICharacterController cachedAI;

        /// <summary>
        /// AI路径控制引用（关键：禁用此组件可阻止每帧移动）
        /// </summary>
        private AI_PathControl cachedPathControl;

        /// <summary>
        /// NodeCanvas行为树引用集合（需要禁用以阻止行为树重新拿枪）
        /// </summary>
        private List<Component> behaviourTrees = new List<Component>();

        /// <summary>
        /// 是否已暂停
        /// </summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// Pause 前保存的 defaultWeaponOut 原始值，Resume 时恢复
        /// </summary>
        private bool savedDefaultWeaponOut;

        /// <summary>
        /// 日志前缀（用于区分不同Boss）
        /// </summary>
        private readonly string logPrefix;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="boss">Boss角色</param>
        /// <param name="bossName">Boss名称（用于日志）</param>
        public BossAIController(CharacterMainControl boss, string bossName = "Boss")
        {
            bossCharacter = boss;
            logPrefix = $"[{bossName}]";
            Initialize();
        }

        /// <summary>
        /// 初始化AI组件引用
        /// </summary>
        private void Initialize()
        {
            if (bossCharacter == null)
            {
                Log("[ERROR] Boss角色为空，无法初始化AI控制器");
                return;
            }

            // 获取AI控制器
            cachedAI = bossCharacter.GetComponentInChildren<AICharacterController>();

            // 获取AI路径控制组件（关键组件）
            cachedPathControl = bossCharacter.GetComponentInChildren<AI_PathControl>();

            // 获取所有NodeCanvas行为树（使用缓存的类型来避免重复反射）
            if (cachedAI != null)
            {
                CacheBehaviourTreeTypes();

                if (cachedBehaviourTreeType != null)
                {
                    // 通过反射获取行为树字段
                    var fields = cachedAI.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        if (field.FieldType == cachedBehaviourTreeType)
                        {
                            var tree = field.GetValue(cachedAI) as Component;
                            if (tree != null)
                            {
                                behaviourTrees.Add(tree);
                            }
                        }
                    }
                }
            }

            Log($"AI控制器初始化完成 - AI:{(cachedAI != null)}, PathControl:{(cachedPathControl != null)}, BehaviourTrees:{behaviourTrees.Count}");
        }

        /// <summary>
        /// 缓存NodeCanvas相关类型（只反射一次）
        /// </summary>
        private static void CacheBehaviourTreeTypes()
        {
            if (cachedBehaviourTreeType != null) return;

            try
            {
                cachedBehaviourTreeType = System.Type.GetType("NodeCanvas.BehaviourTrees.BehaviourTree, NodeCanvas");

                // 缓存Behaviour.enabled属性
                var behaviourType = System.Type.GetType("NodeCanvas.Framework.Behaviour, NodeCanvas");
                if (behaviourType != null)
                {
                    cachedBehaviourEnabledProperty = behaviourType.GetProperty("enabled");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 缓存Behaviour.enabled属性异常: {e.Message}");
            }
        }

        /// <summary>
        /// 暂停AI行动
        /// 优雅实现：禁用组件而非每帧强制停止
        /// </summary>
        public void Pause()
        {
            if (IsPaused) return;

            try
            {
                if (bossCharacter == null)
                {
                    Log("[WARNING] Boss角色为空，无法暂停AI");
                    return;
                }

                if (cachedAI != null)
                {
                    // 停止AI移动（清除寻路路径）
                    cachedAI.StopMove();

                    // 收起武器（停止射击行为），保存原始值以便 Resume 恢复
                    savedDefaultWeaponOut = cachedAI.defaultWeaponOut;
                    cachedAI.PutBackWeapon();
                    cachedAI.defaultWeaponOut = false;

                    // 清除目标
                    cachedAI.searchedEnemy = null;
                    cachedAI.aimTarget = null;
                    cachedAI.noticed = false;

                    // 禁用AI控制器
                    cachedAI.enabled = false;
                }

                // 关键：禁用AI_PathControl组件
                // 这会阻止其Update()每帧调用SetMoveInput()，无需额外Update监听
                if (cachedPathControl != null)
                {
                    cachedPathControl.enabled = false;
                }

                // 关键：禁用所有NodeCanvas行为树
                // 行为树可能会独立运行并让Boss重新拿枪，必须禁用
                foreach (var tree in behaviourTrees)
                {
                    if (tree != null)
                    {
                        if (cachedBehaviourEnabledProperty != null && cachedBehaviourEnabledProperty.CanWrite)
                        {
                            cachedBehaviourEnabledProperty.SetValue(tree, false);
                        }
                    }
                }

                // 停止角色移动输入
                bossCharacter.SetMoveInput(Vector2.zero);
                bossCharacter.SetRunInput(false);

                // 停止射击
                bossCharacter.Trigger(false, false, false);

                IsPaused = true;
                Log("AI已暂停");
            }
            catch (Exception e)
            {
                Log($"[WARNING] 暂停AI失败: {e.Message}");
            }
        }

        /// <summary>
        /// 恢复AI行动
        /// </summary>
        /// <param name="playerCharacter">玩家角色（用于恢复目标追踪）</param>
        public void Resume(CharacterMainControl playerCharacter = null)
        {
            if (!IsPaused) return;

            try
            {
                // 重新启用AI_PathControl组件
                if (cachedPathControl != null)
                {
                    cachedPathControl.enabled = true;
                }

                if (cachedAI != null)
                {
                    // 重新启用AI控制器
                    cachedAI.enabled = true;
                    // 恢复 Pause 前保存的 defaultWeaponOut 原始值
                    // 近战 Boss（幽灵女巫等）需要 true 才能在技能间隙正常攻击
                    // 远程 Boss（龙王等）原始值本身为 false，恢复后不受影响
                    cachedAI.defaultWeaponOut = savedDefaultWeaponOut;

                    // 恢复目标追踪
                    if (playerCharacter != null && playerCharacter.mainDamageReceiver != null)
                    {
                        cachedAI.searchedEnemy = playerCharacter.mainDamageReceiver;
                        cachedAI.noticed = true;
                    }
                }

                // 重新启用所有NodeCanvas行为树
                foreach (var tree in behaviourTrees)
                {
                    if (tree != null)
                    {
                        if (cachedBehaviourEnabledProperty != null && cachedBehaviourEnabledProperty.CanWrite)
                        {
                            cachedBehaviourEnabledProperty.SetValue(tree, true);
                        }
                    }
                }

                IsPaused = false;
                Log("AI已恢复");
            }
            catch (Exception e)
            {
                Log($"[WARNING] 恢复AI失败: {e.Message}");
            }
        }

        /// <summary>
        /// 清理引用
        /// </summary>
        public void Cleanup()
        {
            bossCharacter = null;
            cachedAI = null;
            cachedPathControl = null;
            IsPaused = false;
        }

        /// <summary>
        /// 获取AI控制器（供外部使用）
        /// </summary>
        public AICharacterController GetAI()
        {
            return cachedAI;
        }

        /// <summary>
        /// 输出日志
        /// </summary>
        private void Log(string message)
        {
            ModBehaviour.DevLog($"{logPrefix} {message}");
        }

        /// <summary>
        /// 确保AI组件已初始化（延迟初始化场景）
        /// </summary>
        public void EnsureInitialized()
        {
            if (cachedAI == null || cachedPathControl == null)
            {
                Initialize();
            }
        }
    }

    /// <summary>
    /// 预警圆圈缩放动画
    /// 使用组件自身 Update，避免每次施法都额外启动协程
    /// </summary>
    public class WarningCircleAnimation : MonoBehaviour
    {
        private const int SegmentCount = 64;
        private static Vector3[] cachedUnitPoints = null;
        private static readonly Vector3[] positionsBuffer = new Vector3[SegmentCount];

        private LineRenderer lineRenderer;
        private float duration = 1f;
        private float startRadius = 20f;
        private float endRadius = 0f;
        private float elapsed = 0f;
        private bool isAnimating = false;

        void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();
        }

        public void ResetAnimation(float durationSeconds, float fromRadius, float toRadius)
        {
            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }

            duration = Mathf.Max(0.01f, durationSeconds);
            startRadius = fromRadius;
            endRadius = toRadius;
            elapsed = 0f;
            isAnimating = true;

            if (lineRenderer != null)
            {
                lineRenderer.positionCount = SegmentCount;
                ApplyRadius(startRadius);
            }
        }

        void Update()
        {
            if (!isAnimating || lineRenderer == null)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = t * t;
            float currentRadius = Mathf.Lerp(startRadius, endRadius, easedT);
            ApplyRadius(currentRadius);

            if (t >= 1f)
            {
                isAnimating = false;
            }
        }

        private void ApplyRadius(float radius)
        {
            Vector3[] unitPoints = GetUnitPoints();
            for (int i = 0; i < unitPoints.Length; i++)
            {
                positionsBuffer[i] = unitPoints[i] * radius;
            }
            lineRenderer.SetPositions(positionsBuffer);
        }

        private static Vector3[] GetUnitPoints()
        {
            if (cachedUnitPoints == null || cachedUnitPoints.Length != SegmentCount)
            {
                cachedUnitPoints = new Vector3[SegmentCount];
                for (int i = 0; i < SegmentCount; i++)
                {
                    float angle = (float)i / SegmentCount * 360f * Mathf.Deg2Rad;
                    cachedUnitPoints[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }
            }

            return cachedUnitPoints;
        }
    }

    /// <summary>
    /// 光源闪烁效果组件
    /// </summary>
    public class LightFlicker : MonoBehaviour
    {
        /// <summary>
        /// 基础光照强度
        /// </summary>
        public float baseIntensity = 2f;

        /// <summary>
        /// 闪烁幅度
        /// </summary>
        public float flickerAmount = 1f;

        /// <summary>
        /// 闪烁速度
        /// </summary>
        public float flickerSpeed = 5f;

        private Light targetLight;
        private float randomOffset;

        void Start()
        {
            targetLight = GetComponent<Light>();
            randomOffset = UnityEngine.Random.Range(0f, 100f);
        }

        void Update()
        {
            if (targetLight != null)
            {
                // 使用多个正弦波叠加产生自然的闪烁效果
                float flicker = Mathf.Sin((Time.time + randomOffset) * flickerSpeed) * 0.5f +
                               Mathf.Sin((Time.time + randomOffset) * flickerSpeed * 1.7f) * 0.3f +
                               Mathf.Sin((Time.time + randomOffset) * flickerSpeed * 2.3f) * 0.2f;

                targetLight.intensity = baseIntensity + flicker * flickerAmount;
            }
        }
    }

    /// <summary>
    /// 圆环缩小动画组件
    /// </summary>
    public class RingShrinkAnimation : MonoBehaviour
    {
        public float duration = 1f;
        public float startScale = 1f;
        public float endScale = 0f;

        private float elapsed = 0f;
        private Vector3 initialScale;
        private LineRenderer lineRenderer;
        private Light ringLight;
        private bool referencesCached;

        private static readonly Color BaseColor = new Color(0.9f, 0.95f, 1f, 0.9f);
        private static readonly Color BrightColor = new Color(1f, 1f, 1f, 1f);

        void Awake()
        {
            CacheReferences();
        }

        public void ResetAnimation(float durationSeconds, float fromScale, float toScale)
        {
            if (!referencesCached) CacheReferences();
            duration = Mathf.Max(0.01f, durationSeconds);
            startScale = fromScale;
            endScale = toScale;
            elapsed = 0f;
            transform.localScale = initialScale * startScale;
        }

        void Update()
        {
            if (!referencesCached) CacheReferences();

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float easedT = t * t;

            float currentScale = Mathf.Lerp(startScale, endScale, easedT);
            transform.localScale = initialScale * currentScale;

            Color currentColor = Color.Lerp(BaseColor, BrightColor, t);

            if (lineRenderer != null)
            {
                lineRenderer.startColor = currentColor;
                lineRenderer.endColor = currentColor;

                float width = Mathf.Lerp(0.3f, 0.5f, t);
                lineRenderer.startWidth = width;
                lineRenderer.endWidth = width;
            }

            if (ringLight != null)
            {
                ringLight.intensity = Mathf.Lerp(2f, 5f, t);
                ringLight.color = currentColor;
            }
        }

        private void CacheReferences()
        {
            if (initialScale == Vector3.zero)
            {
                initialScale = transform.localScale;
                if (initialScale == Vector3.zero)
                {
                    initialScale = Vector3.one;
                }
            }

            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }

            if (ringLight == null)
            {
                ringLight = GetComponent<Light>();
            }

            referencesCached = true;
        }
    }

    /// <summary>
    /// 龙王岩浆伤害区域组件
    /// 玩家进入时造成火焰伤害并施加点燃Buff
    /// </summary>
    public class DragonKingLavaZone : MonoBehaviour
    {
        private const float PLAYER_COLLIDER_REFRESH_INTERVAL = 1f;
        private static readonly Collider[] emptyPlayerColliders = new Collider[0];
        private static readonly List<DragonKingLavaZone> activeZones = new List<DragonKingLavaZone>(48);

        /// <summary>
        /// 每次伤害值
        /// </summary>
        private float damage = 5f;

        /// <summary>
        /// 伤害间隔（秒）
        /// </summary>
        private float damageInterval = 0.5f;

        /// <summary>
        /// 持续时间（秒）
        /// </summary>
        private float duration = 3f;

        /// <summary>
        /// 检测半径
        /// </summary>
        private float radius = 1f;

        /// <summary>
        /// Boss角色引用（用于伤害来源）
        /// </summary>
        private CharacterMainControl bossCharacter;

        /// <summary>
        /// 上次伤害时间
        /// </summary>
        private float lastDamageTime = 0f;

        /// <summary>
        /// 创建时间
        /// </summary>
        private float createTime = 0f;

        /// <summary>
        /// 缓存的点燃Buff
        /// </summary>
        private static Duckov.Buffs.Buff cachedBurnBuff = null;
        private static CharacterMainControl cachedMainPlayer = null;
        private static Transform cachedMainPlayerRoot = null;
        private static Collider[] cachedMainPlayerColliders = emptyPlayerColliders;
        private static float lastPlayerColliderRefreshTime = float.NegativeInfinity;
        private static DragonKingLavaZoneUpdater updater = null;
        private bool isInitialized = false;

        /// <summary>
        /// 球形碰撞器
        /// </summary>
        private SphereCollider sphereCollider;

        /// <summary>
        /// 初始化岩浆区域
        /// </summary>
        public void PrepareForPooling()
        {
            EnsureUpdater();
            EnsureSphereCollider();
            ConfigureDisabledTrigger();
            RefreshMainPlayerCache(false);
            ResetRuntimeState();
        }

        public void Initialize(float dmg, float interval, float dur, float rad, CharacterMainControl boss)
        {
            damage = dmg;
            damageInterval = interval;
            duration = dur;
            radius = rad;
            bossCharacter = boss;
            createTime = Time.time;
            lastDamageTime = 0f;
            isInitialized = true;

            // 缓存点燃Buff
            if (cachedBurnBuff == null)
            {
                cachedBurnBuff = GameplayDataSettings.Buffs.Burn;
            }

            EnsureUpdater();
            RefreshMainPlayerCache(false);

            // 保留碰撞器组件供对象池复用，但关闭触发检测，改由集中式距离采样统一处理
            EnsureSphereCollider();
            sphereCollider.radius = radius;
            sphereCollider.isTrigger = true;
            sphereCollider.enabled = false;

            // 设置层级
            gameObject.layer = LayerMask.NameToLayer("Default");
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            RegisterZone(this);
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            UnregisterZone(this);
            ResetRuntimeState();
        }

        private void OnDestroy()
        {
            UnregisterZone(this);
        }

        private void TickActiveZone(CharacterMainControl player)
        {
            if (!isInitialized || !gameObject.activeInHierarchy)
            {
                return;
            }

            if (player == null || player.Health == null || player.Health.IsDead)
            {
                return;
            }

            if (Time.time - createTime > duration)
            {
                return;
            }

            if (Time.time - lastDamageTime < damageInterval)
            {
                return;
            }

            if (!IsMainPlayerInsideZone())
            {
                return;
            }

            lastDamageTime = Time.time;
            ApplyLavaDamage(player);
        }

        /// <summary>
        /// 对玩家造成岩浆伤害
        /// </summary>
        private void ApplyLavaDamage(CharacterMainControl player)
        {
            if (player == null || player.Health == null || player.Health.IsDead) return;

            try
            {
                // 创建伤害信息
                DamageInfo damageInfo = new DamageInfo(bossCharacter);
                damageInfo.damageValue = damage;
                damageInfo.damageType = DamageTypes.normal;
                damageInfo.damagePoint = player.transform.position;
                damageInfo.AddElementFactor(ElementTypes.fire, 1f);

                // 造成伤害
                player.Health.Hurt(damageInfo);

                // 施加点燃Buff
                if (cachedBurnBuff != null)
                {
                    player.AddBuff(cachedBurnBuff, bossCharacter, 0);
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] 岩浆伤害失败: {e.Message}");
            }
        }

        private bool IsMainPlayerInsideZone()
        {
            Vector3 zoneCenter = transform.position;
            float radiusSqr = radius * radius;
            Collider[] playerColliders = cachedMainPlayerColliders;

            for (int i = 0; i < playerColliders.Length; i++)
            {
                Collider collider = playerColliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 closestPoint = collider.ClosestPoint(zoneCenter);
                if ((closestPoint - zoneCenter).sqrMagnitude <= radiusSqr)
                {
                    return true;
                }
            }

            if (cachedMainPlayerRoot == null)
            {
                return false;
            }

            Vector3 toPlayer = cachedMainPlayerRoot.position - zoneCenter;
            return toPlayer.sqrMagnitude <= radiusSqr;
        }

        private static void UpdateActiveZones()
        {
            if (activeZones.Count == 0)
            {
                return;
            }

            CharacterMainControl player;
            if (!TryGetMainPlayer(out player))
            {
                return;
            }

            for (int i = activeZones.Count - 1; i >= 0; i--)
            {
                DragonKingLavaZone zone = activeZones[i];
                if (zone == null)
                {
                    activeZones.RemoveAt(i);
                    continue;
                }

                zone.TickActiveZone(player);
            }
        }

        private static bool TryGetMainPlayer(out CharacterMainControl player)
        {
            RefreshMainPlayerCache(false);
            player = cachedMainPlayer;
            return player != null && player.Health != null && !player.Health.IsDead;
        }

        private static void RefreshMainPlayerCache(bool forceRefresh)
        {
            CharacterMainControl mainPlayer = CharacterMainControl.Main;
            Transform mainPlayerRoot = mainPlayer != null ? mainPlayer.transform.root : null;
            bool needsRefresh =
                forceRefresh ||
                cachedMainPlayer != mainPlayer ||
                cachedMainPlayerRoot != mainPlayerRoot ||
                cachedMainPlayerColliders == null ||
                Time.time - lastPlayerColliderRefreshTime >= PLAYER_COLLIDER_REFRESH_INTERVAL;

            if (!needsRefresh)
            {
                return;
            }

            cachedMainPlayer = mainPlayer;
            cachedMainPlayerRoot = mainPlayerRoot;
            cachedMainPlayerColliders = emptyPlayerColliders;

            if (mainPlayerRoot != null)
            {
                Collider[] colliders = mainPlayerRoot.GetComponentsInChildren<Collider>(true);
                if (colliders != null && colliders.Length > 0)
                {
                    cachedMainPlayerColliders = colliders;
                }
            }

            lastPlayerColliderRefreshTime = Time.time;
        }

        private static void RegisterZone(DragonKingLavaZone zone)
        {
            if (zone == null)
            {
                return;
            }

            EnsureUpdater();
            if (!activeZones.Contains(zone))
            {
                activeZones.Add(zone);
            }
        }

        private static void UnregisterZone(DragonKingLavaZone zone)
        {
            if (zone == null)
            {
                return;
            }

            activeZones.Remove(zone);
        }

        private static void EnsureUpdater()
        {
            if (updater != null)
            {
                return;
            }

            GameObject updaterObject = new GameObject("DragonKing_LavaZoneUpdater");
            updaterObject.hideFlags = HideFlags.HideInHierarchy;
            updater = updaterObject.AddComponent<DragonKingLavaZoneUpdater>();
        }

        private void ResetRuntimeState()
        {
            isInitialized = false;
            bossCharacter = null;
            createTime = 0f;
            lastDamageTime = 0f;
        }

        private void EnsureSphereCollider()
        {
            if (sphereCollider == null)
            {
                sphereCollider = gameObject.GetComponent<SphereCollider>();
            }
            if (sphereCollider == null)
            {
                sphereCollider = gameObject.AddComponent<SphereCollider>();
            }

            ConfigureDisabledTrigger();
        }

        private void ConfigureDisabledTrigger()
        {
            if (sphereCollider == null)
            {
                return;
            }

            sphereCollider.isTrigger = true;
            sphereCollider.enabled = false;
        }

        private sealed class DragonKingLavaZoneUpdater : MonoBehaviour
        {
            private void Update()
            {
                DragonKingLavaZone.UpdateActiveZones();
            }

            private void OnDisable()
            {
                if (updater == this)
                {
                    updater = null;
                }
            }

            private void OnDestroy()
            {
                if (updater == this)
                {
                    updater = null;
                }
            }
        }
    }
}
