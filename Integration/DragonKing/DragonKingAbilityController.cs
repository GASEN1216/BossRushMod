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
    /// 龙王Boss能力控制器
    /// </summary>
    public class DragonKingAbilityController : MonoBehaviour
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
        /// 是否正在执行攻击
        /// </summary>
        private bool isAttacking = false;
        
        /// <summary>
        /// Boss角色引用
        /// </summary>
        private CharacterMainControl bossCharacter;
        
        /// <summary>
        /// Boss的Health组件
        /// </summary>
        private Health bossHealth;
        
        /// <summary>
        /// 玩家角色引用
        /// </summary>
        private CharacterMainControl playerCharacter;

        /// <summary>
        /// AI控制辅助类（统一的暂停/恢复接口）
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
        /// 活跃的弹幕列表（用于清理）
        /// 预分配容量以避免动态扩容
        /// </summary>
        private List<GameObject> activeProjectiles = new List<GameObject>(128);

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
        /// 缓存的穿墙LayerMask（只检测伤害接收层）
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
        /// 缓存的武器子弹速度
        /// </summary>
        private float cachedWeaponBulletSpeed = 30f;

        // ========== 反射缓存（避免重复反射） ==========

        /// <summary>
        /// 缓存的AudioManager.Post方法
        /// </summary>
        private static System.Reflection.MethodInfo cachedAudioPostMethod = null;


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

        /// <summary>
        /// BulletPool管理的子弹列表（不应手动销毁，只需移除引用）
        /// 预分配容量以避免动态扩容
        /// </summary>
        private List<GameObject> bulletPoolProjectiles = new List<GameObject>(256);
        
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
        
        // ========== 静态缓存 ==========
        
        /// <summary>
        /// 清理静态缓存
        /// </summary>
        public static void ClearStaticCache()
        {
            ClearStaticMaterialCache();
            cachedRainbowGradient = null;
        }

        // ========== 字符串常量缓存（减少GC） ==========

        private static readonly string LOG_PREFIX = "[DragonKing] ";
        private static readonly string LOG_WARNING = "[WARNING] ";
        private static readonly string LOG_ERROR = "[ERROR] ";

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
            
            // 初始化碰撞检测器
            InitializeCollisionDetector();
            
            // 随机选择悬浮方向（左上角或右上角）
            hoverSide = UnityEngine.Random.value > 0.5f ? 1 : -1;
            
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
                
                // 添加球形碰撞器（触发器模式）
                SphereCollider collider = detectorObj.AddComponent<SphereCollider>();
                collider.radius = DragonKingConfig.CollisionRadius;
                collider.isTrigger = true;
                
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
        
        // [已移除] 悬浮跟随机制 - 该功能未被使用且可能导致位置异常
        
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
                playerCharacter = CharacterMainControl.Main;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] UpdatePlayerReference异常: {e.Message}");
            }
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
            bossHealth = null;
            playerCharacter = null;
            cachedWeaponBullet = null;

            ModBehaviour.DevLog("[DragonKing] 组件销毁，资源清理完成");
        }
        
        /// <summary>
        /// 清理所有活跃特效和弹幕
        /// </summary>
        private void CleanupAllEffects()
        {
            // 停止所有弹幕追踪协程
            StopAllProjectileCoroutines();
            
            // 清理特效（包含动态创建的Material）
            foreach (var effect in activeEffects)
            {
                if (effect != null)
                {
                    // 销毁特效前先清理其Material
                    CleanupMaterialsInObject(effect);
                    Destroy(effect);
                }
            }
            activeEffects.Clear();
            
            // 清理自定义弹幕（非BulletPool管理的）
            foreach (var projectile in activeProjectiles)
            {
                if (projectile != null)
                {
                    Destroy(projectile);
                }
            }
            activeProjectiles.Clear();
            
            // 清理BulletPool弹幕引用（不销毁，由BulletPool管理）
            bulletPoolProjectiles.Clear();
            
            // 清理警告线
            foreach (var line in activeWarningLines)
            {
                if (line != null)
                {
                    CleanupMaterialsInObject(line);
                    Destroy(line);
                }
            }
            activeWarningLines.Clear();
            
            // 清理动态创建的Material
            CleanupDynamicMaterials();
        }
        
        /// <summary>
        /// 停止所有弹幕追踪协程
        /// </summary>
        private void StopAllProjectileCoroutines()
        {
            foreach (var coroutine in activeProjectileCoroutines)
            {
                if (coroutine != null)
                {
                    StopCoroutine(coroutine);
                }
            }
            activeProjectileCoroutines.Clear();
        }

        /// <summary>
        /// 安全地启动并追踪协程（防止协程泄漏）
        /// </summary>
        private Coroutine StartTrackedCoroutine(IEnumerator routine)
        {
            if (routine == null) return null;
            var coroutine = StartCoroutine(routine);
            activeProjectileCoroutines.Add(coroutine);
            return coroutine;
        }
        
        /// <summary>
        /// 清理GameObject中的所有动态Material
        /// </summary>
        private void CleanupMaterialsInObject(GameObject obj)
        {
            if (obj == null) return;
            
            try
            {
                // 清理Renderer的材质
                var renderers = obj.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (renderer != null && renderer.material != null)
                    {
                        // 只销毁实例化的材质（名称包含"(Instance)"）
                        if (renderer.material.name.Contains("(Instance)"))
                        {
                            Destroy(renderer.material);
                        }
                    }
                }
                
                // 清理LineRenderer的材质
                var lineRenderers = obj.GetComponentsInChildren<LineRenderer>(true);
                foreach (var lr in lineRenderers)
                {
                    if (lr != null && lr.material != null)
                    {
                        if (lr.material.name.Contains("(Instance)"))
                        {
                            Destroy(lr.material);
                        }
                    }
                }
                
                // 清理ParticleSystemRenderer的材质
                var psRenderers = obj.GetComponentsInChildren<ParticleSystemRenderer>(true);
                foreach (var psr in psRenderers)
                {
                    if (psr != null && psr.material != null)
                    {
                        if (psr.material.name.Contains("(Instance)"))
                        {
                            Destroy(psr.material);
                        }
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

        // ========== 攻击主循环 ==========
        
        /// <summary>
        /// 攻击主循环协程
        /// </summary>
        private IEnumerator AttackLoop()
        {
            // 等待初始化完成
            yield return wait1s;
            
            ModBehaviour.DevLog("[DragonKing] 攻击循环开始");
            
            // 调试模式提示
            if (DragonKingConfig.DebugMode)
            {
                ModBehaviour.DevLog($"[DragonKing] [DEBUG] 调试模式已启用，将只重复释放技能: {DragonKingConfig.DebugAttackType}");
            }
            
            while (CurrentPhase != DragonKingPhase.Dead && bossCharacter != null)
            {
                // 阶段转换中或孩儿护我期间暂停攻击
                if (CurrentPhase == DragonKingPhase.Transitioning || isInChildProtection)
                {
                    yield return wait05s;
                    continue;
                }
                
                // 更新玩家引用
                UpdatePlayerReference();
                
                if (playerCharacter == null || playerCharacter.Health == null || playerCharacter.Health.IsDead)
                {
                    yield return wait1s;
                    continue;
                }
                
                // 调试模式下跳过阶段转换检查
                if (!DragonKingConfig.DebugMode)
                {
                    // 检查阶段转换
                    CheckPhaseTransition();
                }
                
                // 获取当前攻击类型
                DragonKingAttackType attackType;
                
                if (DragonKingConfig.DebugMode)
                {
                    // 调试模式：只使用指定的技能
                    attackType = DragonKingConfig.DebugAttackType;
                    ModBehaviour.DevLog($"[DragonKing] [DEBUG] 执行调试技能: {attackType}");
                }
                else
                {
                    // 正常模式：按序列执行
                    var sequence = CurrentSequence;
                    attackType = sequence[currentAttackIndex];
                    ModBehaviour.DevLog($"[DragonKing] 执行攻击: {attackType} (索引: {currentAttackIndex})");
                }
                
                // 执行攻击
                isAttacking = true;
                currentAttackCoroutine = StartCoroutine(ExecuteAttack(attackType));
                yield return currentAttackCoroutine;
                currentAttackCoroutine = null;
                isAttacking = false;
                
                // 调试模式下不推进序列
                if (!DragonKingConfig.DebugMode)
                {
                    // 推进攻击序列
                    var sequence = CurrentSequence;
                    currentAttackIndex = (currentAttackIndex + 1) % sequence.Length;
                }
                
                // 攻击间隔
                yield return GetAttackIntervalWait();
            }
            
            ModBehaviour.DevLog("[DragonKing] 攻击循环结束");
        }
        
        /// <summary>
        /// 获取攻击间隔等待对象
        /// 使用配置文件中的间隔值，而非硬编码
        /// 使用缓存的WaitForSeconds对象，避免每帧分配GC
        /// </summary>
        private WaitForSeconds GetAttackIntervalWait()
        {
            // 使用配置文件中定义的攻击间隔对应的缓存对象
            // Phase1AttackInterval = 1.0s, Phase2AttackInterval = 0.5s
            return CurrentPhase == DragonKingPhase.Phase2 ? wait05s : wait1s;
        }
        
        // ========== 阶段转换 ==========
        
        /// <summary>
        /// 检查是否需要触发阶段转换
        /// </summary>
        private void CheckPhaseTransition()
        {
            if (phase2Triggered) return;
            if (bossHealth == null) return;
            
            float healthPercent = bossHealth.CurrentHealth / bossHealth.MaxHealth;
            
            if (healthPercent <= DragonKingConfig.Phase2HealthThreshold)
            {
                phase2Triggered = true;
                StartCoroutine(TriggerPhase2Transition());
            }
        }
        
        /// <summary>
        /// 触发二阶段转换
        /// </summary>
        private IEnumerator TriggerPhase2Transition()
        {
            ModBehaviour.DevLog("[DragonKing] 触发二阶段转换");
            
            // 播放阶段转换音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_Phase2);

            CurrentPhase = DragonKingPhase.Transitioning;

            // 【第一步】立即禁用Boss，阻止所有行为（必须最先执行）
            if (bossCharacter != null)
            {
                bossCharacter.enabled = false;

                // 立即清除手持物品
                if (bossCharacter.CurrentHoldItemAgent != null)
                {
                    bossCharacter.ChangeHoldItem(null);
                }
            }

            // 停止当前攻击
            if (currentAttackCoroutine != null)
            {
                StopCoroutine(currentAttackCoroutine);
                currentAttackCoroutine = null;
            }
            isAttacking = false;

            // 停止AI和射击
            StopBossMovementAndShooting();

            // 停止太阳舞弹幕
            isSunDanceActive = false;
            if (sunDanceBarrageCoroutine != null)
            {
                StopCoroutine(sunDanceBarrageCoroutine);
                sunDanceBarrageCoroutine = null;
            }

            // 【修改】不再调用PutBackWeapon，射击循环会自动检测Transitioning阶段并暂停

            // 清理当前攻击的特效
            CleanupAllEffects();

            // 禁用碰撞检测器
            if (collisionDetector != null)
            {
                collisionDetector.enabled = false;
            }

            // Boss消失（隐藏模型）
            if (bossCharacter != null)
            {
                SetBossVisible(false);
            }

            // 播放传送特效（消失）
            SpawnTeleportEffect(bossCharacter.transform.position);

            // 播放阶段转换特效（消失位置）
            SpawnPhaseTransitionEffect(bossCharacter.transform.position);

            // 等待转换时间
            yield return wait1s;

            // 计算玩家附近的地面位置（2.5D游戏，传送到地面而非玩家头上）
            UpdatePlayerReference();
            Vector3 targetPos = FindGroundPositionNearPlayer();

            // 传送到地面位置
            if (bossCharacter != null)
            {
                bossCharacter.transform.position = targetPos;
            }

            // 播放出现特效
            SpawnTeleportEffect(targetPos);

            // 播放阶段转换特效（出现位置）
            SpawnPhaseTransitionEffect(targetPos);

            // Boss出现（但暂不恢复AI，等波纹释放完）
            if (bossCharacter != null)
            {
                SetBossVisible(true);
            }

            // 重新启用碰撞检测器
            if (collisionDetector != null)
            {
                collisionDetector.enabled = true;
            }

            // 播放冲击波效果（音浪扩散），等待所有波纹释放完成
            var shockwave = DragonKingShockwaveEffect.PlayAt(targetPos);

            // 等待波纹完成（3个波，间隔0.5s，约2-3秒完成）
            yield return wait25s;

            // 【修改】不再调用TakeOutWeapon，射击循环会自动检测Phase2并恢复射击

            // 先恢复CharacterMainControl组件
            if (bossCharacter != null)
            {
                bossCharacter.enabled = true;
            }

            // 恢复AI
            ResumeBossMovementAndShooting();

            // 显示消息
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.ShowMessage(L10n.DragonKingEnraged);
            }

            // 重置攻击序列索引
            currentAttackIndex = 0;

            // 进入二阶段
            CurrentPhase = DragonKingPhase.Phase2;

            // 【修复】重新启动攻击循环，确保二阶段攻击能够正常执行
            // 原因：二阶段转换期间停止了currentAttackCoroutine，可能导致AttackLoop状态不一致
            if (attackLoopCoroutine != null)
            {
                StopCoroutine(attackLoopCoroutine);
            }
            attackLoopCoroutine = StartCoroutine(AttackLoop());
            ModBehaviour.DevLog("[DragonKing] 已重新启动攻击循环");

            ModBehaviour.DevLog("[DragonKing] 二阶段转换完成");
        }
        
        /// <summary>
        /// 生成阶段转换特效
        /// </summary>
        private void SpawnPhaseTransitionEffect(Vector3 position)
        {
            var effect = DragonKingAssetManager.InstantiateEffect(
                DragonKingConfig.PhaseTransitionPrefab, 
                position, 
                Quaternion.identity
            );
            
            if (effect != null)
            {
                activeEffects.Add(effect);
                UnityEngine.Object.Destroy(effect, 3f);
                ModBehaviour.DevLog("[DragonKing] 阶段转换特效已生成");
            }
        }
        
        /// <summary>
        /// 设置Boss可见性
        /// </summary>
        private void SetBossVisible(bool visible)
        {
            if (bossCharacter == null) return;
            
            try
            {
                // 隐藏/显示角色模型
                var renderers = bossCharacter.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers)
                {
                    renderer.enabled = visible;
                }
                
                // 设置无敌状态（消失时无敌）
                if (bossHealth != null)
                {
                    bossHealth.SetInvincible(!visible);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 设置可见性失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 生成传送特效
        /// </summary>
        private void SpawnTeleportEffect(Vector3 position)
        {
            var effect = DragonKingAssetManager.InstantiateEffect(
                DragonKingConfig.TeleportFXPrefab, 
                position, 
                Quaternion.identity
            );
            
            if (effect != null)
            {
                activeEffects.Add(effect);
                Destroy(effect, 2f);
            }
        }
        
        /// <summary>
        /// 计算玩家预判位置（用于传送）
        /// </summary>
        private Vector3 CalculatePredictedPosition()
        {
            if (playerCharacter == null)
            {
                return bossCharacter != null ? bossCharacter.transform.position : Vector3.zero;
            }
            
            Vector3 playerPos = playerCharacter.transform.position;
            
            // 预判玩家移动方向
            Vector3 playerVelocity = Vector3.zero;
            try
            {
                var rb = playerCharacter.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    playerVelocity = rb.velocity;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 获取玩家速度异常: {e.Message}");
            }

            // 预判1秒后的位置
            Vector3 predictedPos = playerPos + playerVelocity * 1f;
            
            // 在预判位置上方
            float height = UnityEngine.Random.Range(
                DragonKingConfig.RepositionHeightMin, 
                DragonKingConfig.RepositionHeightMax
            );
            
            return predictedPos + Vector3.up * height;
        }

        /// <summary>
        /// 计算阶段转换传送目标位置（玩家附近的有效地面位置）
        /// 【修复】使用FindValidTeleportPosition进行位置验证，防止掉出地图
        /// </summary>
        private Vector3 FindGroundPositionNearPlayer()
        {
            if (playerCharacter == null)
            {
                return bossCharacter != null ? bossCharacter.transform.position : Vector3.zero;
            }

            // 使用FindValidTeleportPosition找到玩家附近的有效位置
            // 距离范围：2-4米，确保不会太近也不会太远
            Vector3 playerPos = playerCharacter.transform.position;
            Vector3 targetPos = FindValidTeleportPosition(playerPos, 2f, 4f, 15);
            
            // 如果找不到有效位置，使用玩家位置
            if (targetPos == playerPos)
            {
                ModBehaviour.DevLog("[DragonKing] [WARNING] 二阶段转换：未找到有效传送位置，使用玩家位置");
            }
            
            // 确保使用地面高度（向下发射射线检测地面）
            Vector3 origin = targetPos + Vector3.up * 2f;
            int groundLayer = GetGroundLayer();
            int groundLayerMask = 1 << groundLayer;

            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, 10f, groundLayerMask))
            {
                targetPos.y = hit.point.y;
                ModBehaviour.DevLog($"[DragonKing] 二阶段转换：检测到地面高度 {hit.point.y}");
            }
            else
            {
                // 没检测到地面，使用玩家高度
                targetPos.y = playerPos.y;
                ModBehaviour.DevLog("[DragonKing] [WARNING] 二阶段转换：未检测到地面，使用玩家高度");
            }

            return targetPos;
        }
        
        // ========== 换位机制 ==========
        
        /// <summary>
        /// 换位压制机制
        /// </summary>
        private IEnumerator Reposition()
        {
            if (bossCharacter == null || playerCharacter == null) yield break;
            
            // 计算目标位置（玩家上方3-5米）
            Vector3 playerPos = playerCharacter.transform.position;
            float height = UnityEngine.Random.Range(
                DragonKingConfig.RepositionHeightMin, 
                DragonKingConfig.RepositionHeightMax
            );
            Vector3 targetPos = playerPos + Vector3.up * height;
            
            // 快速移动到目标位置
            float moveSpeed = DragonKingConfig.RepositionSpeed;
            float startTime = Time.time;
            float maxDuration = 2f; // 最大移动时间
            
            while (bossCharacter != null && Time.time - startTime < maxDuration)
            {
                Vector3 currentPos = bossCharacter.transform.position;
                float distance = Vector3.Distance(currentPos, targetPos);
                
                if (distance < 0.5f) break;
                
                // 平滑移动
                Vector3 newPos = Vector3.MoveTowards(currentPos, targetPos, moveSpeed * Time.deltaTime);
                bossCharacter.transform.position = newPos;
                
                // 面向玩家
                FacePlayer();
                
                yield return null;
            }
            
            // 最终面向玩家
            FacePlayer();
        }
        
        /// <summary>
        /// 面向玩家
        /// </summary>
        private void FacePlayer()
        {
            if (bossCharacter == null || playerCharacter == null) return;
            
            Vector3 dirToPlayer = playerCharacter.transform.position - bossCharacter.transform.position;
            dirToPlayer.y = 0f;
            
            if (dirToPlayer.sqrMagnitude > 0.01f)
            {
                bossCharacter.transform.rotation = Quaternion.LookRotation(dirToPlayer);
            }
        }
        
        // ========== 伤害回调 ==========
        
        /// <summary>
        /// Boss受伤回调
        /// </summary>
        private void OnBossHurt(DamageInfo damageInfo)
        {
            // 孩儿护我阶段无敌（双重保护）
            if (isInChildProtection && bossHealth != null)
            {
                bossHealth.SetHealth(bossHealth.CurrentHealth + damageInfo.finalDamage);
                return;
            }
            
            // 阶段转换中无敌
            if (CurrentPhase == DragonKingPhase.Transitioning && bossHealth != null)
            {
                bossHealth.SetHealth(bossHealth.CurrentHealth + damageInfo.finalDamage);
                return;
            }
            
            // 检查是否触发孩儿护我（血量降至1HP）
            CheckChildProtection();

            // 立即检查阶段转换（确保半血时立即转阶段，而不是等当前攻击结束）
            CheckPhaseTransition();
        }
        
        /// <summary>
        /// 检查是否触发孩儿护我
        /// </summary>
        private void CheckChildProtection()
        {
            // 已触发过则跳过（幂等性）
            if (childProtectionTriggered) return;
            
            // 检查血量是否降至阈值
            if (bossHealth == null) return;
            if (bossHealth.CurrentHealth > DragonKingConfig.ChildProtectionHealthThreshold) return;
            
            // 触发孩儿护我
            childProtectionTriggered = true;
            childProtectionCoroutine = StartCoroutine(ChildProtectionSequence());
        }
        
        // ========== 攻击执行 ==========
        
        /// <summary>
        /// 执行攻击
        /// </summary>
        private IEnumerator ExecuteAttack(DragonKingAttackType attackType)
        {
            // 【修改】不再在技能开始时停止射击，只有转阶段时才停止
            // Boss在释放技能时继续射击
            
            switch (attackType)
            {
                case DragonKingAttackType.PrismaticBolts:
                    yield return StartCoroutine(ExecutePrismaticBolts());
                    break;
                    
                case DragonKingAttackType.PrismaticBolts2:
                    yield return StartCoroutine(ExecutePrismaticBolts2());
                    break;
                    
                case DragonKingAttackType.Dash:
                    yield return StartCoroutine(ExecuteDash());
                    break;
                    
                case DragonKingAttackType.SunDance:
                    yield return StartCoroutine(ExecuteSunDance());
                    break;
                    
                case DragonKingAttackType.EverlastingRainbow:
                    yield return StartCoroutine(ExecuteEverlastingRainbow());
                    break;
                    
                case DragonKingAttackType.EtherealLance:
                    yield return StartCoroutine(ExecuteEtherealLance());
                    break;
                    
                case DragonKingAttackType.EtherealLance2:
                    yield return StartCoroutine(ExecuteEtherealLance2());
                    break;
                    
                default:
                    ModBehaviour.DevLog($"[DragonKing] [WARNING] 未知攻击类型: {attackType}");
                    yield return wait1s;
                    break;
            }
            
            // 【修改】不再在技能结束时恢复射击，射击始终保持运行
            // 只有转阶段时才控制射击的停止和恢复
        }
        
        /// <summary>
        /// 收枪（释放技能时调用）- 现在改为停止自定义射击
        /// </summary>
        private void PutBackWeapon()
        {
            // 停止自定义射击
            StopCustomShooting();
            ModBehaviour.DevLog("[DragonKing] 释放技能，已停止射击");
        }
        
        /// <summary>
        /// 拿枪（技能结束后调用）- 现在改为开始自定义射击
        /// </summary>
        private void TakeOutWeapon()
        {
            // 开始自定义射击
            StartCustomShooting();
            ModBehaviour.DevLog("[DragonKing] 技能结束，已恢复射击");
        }
        
        // ========== 自定义射击系统 ==========
        
        /// <summary>
        /// 移除龙王身上的武器（彻底销毁，让AI无法开枪）
        /// 注意：必须在CacheWeaponBullet之后调用，确保子弹预制体已缓存
        /// </summary>
        private void RemoveDragonKingWeapon()
        {
            try
            {
                if (bossCharacter == null) return;
                
                // 清除手持物品（收枪）
                if (bossCharacter.CurrentHoldItemAgent != null)
                {
                    bossCharacter.ChangeHoldItem(null);
                }
                
                // 禁止AI拿枪
                var ai = bossCharacter.GetComponentInChildren<AICharacterController>();
                if (ai != null)
                {
                    ai.defaultWeaponOut = false;
                }
                
                // 彻底销毁主武器槽位的武器（子弹预制体已在CacheWeaponBullet中缓存）
                var primSlot = bossCharacter.PrimWeaponSlot();
                if (primSlot != null && primSlot.Content != null)
                {
                    var weapon = primSlot.Content;
                    UnityEngine.Object.Destroy(weapon.gameObject);
                    ModBehaviour.DevLog("[DragonKing] 已销毁主武器");
                }
                
                // 销毁副武器槽位的武器
                var secSlot = bossCharacter.SecWeaponSlot();
                if (secSlot != null && secSlot.Content != null)
                {
                    var weapon = secSlot.Content;
                    UnityEngine.Object.Destroy(weapon.gameObject);
                    ModBehaviour.DevLog("[DragonKing] 已销毁副武器");
                }
                
                ModBehaviour.DevLog("[DragonKing] 已移除龙王所有武器");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 移除武器失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 开始自定义射击（每秒10发朝玩家方向）
        /// </summary>
        private void StartCustomShooting()
        {
            if (isCustomShootingActive) return;
            
            isCustomShootingActive = true;
            customShootingCoroutine = StartCoroutine(CustomShootingLoop());
            ModBehaviour.DevLog("[DragonKing] 自定义射击已启动");
        }
        
        /// <summary>
        /// 停止自定义射击
        /// </summary>
        private void StopCustomShooting()
        {
            if (!isCustomShootingActive) return;
            
            isCustomShootingActive = false;
            if (customShootingCoroutine != null)
            {
                StopCoroutine(customShootingCoroutine);
                customShootingCoroutine = null;
            }
            ModBehaviour.DevLog("[DragonKing] 自定义射击已停止");
        }
        
        /// <summary>
        /// 自定义射击循环
        /// 一阶段：每0.1秒发射1颗子弹，偏移范围2m
        /// 二阶段：每0.1秒发射2颗子弹，偏移范围4m
        /// </summary>
        private IEnumerator CustomShootingLoop()
        {
            ModBehaviour.DevLog("[DragonKing] 自定义射击循环开始");
            
            while (isCustomShootingActive && bossCharacter != null && CurrentPhase != DragonKingPhase.Dead)
            {
                // 转阶段或孩儿护我期间暂停射击但不退出循环
                if (CurrentPhase == DragonKingPhase.Transitioning || isInChildProtection)
                {
                    yield return wait01s;
                    continue;
                }
                
                // 更新玩家引用
                UpdatePlayerReference();
                
                // 只有玩家存活时才射击
                if (playerCharacter != null && playerCharacter.Health != null && !playerCharacter.Health.IsDead)
                {
                    // 根据阶段决定射弹量和偏移范围（使用配置）
                    bool isPhase2 = CurrentPhase == DragonKingPhase.Phase2;
                    int bulletCount = isPhase2 ? DragonKingConfig.Phase2BulletCount : DragonKingConfig.Phase1BulletCount;
                    float offsetRange = isPhase2 ? DragonKingConfig.Phase2OffsetRange : DragonKingConfig.Phase1OffsetRange;
                    
                    Vector3 bossPos = bossCharacter.transform.position + Vector3.up * DragonKingConfig.BossChestHeightOffset; // 胸口位置
                    Vector3 playerPos = playerCharacter.transform.position + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset; // 玩家中心
                    
                    // 发射指定数量的子弹
                    for (int i = 0; i < bulletCount; i++)
                    {
                        // 添加随机偏移
                        Vector3 randomOffset = new Vector3(
                            UnityEngine.Random.Range(-offsetRange, offsetRange),
                            0f,
                            UnityEngine.Random.Range(-offsetRange, offsetRange)
                        );
                        Vector3 targetPos = playerPos + randomOffset;
                        Vector3 direction = (targetPos - bossPos).normalized;
                        
                        // 发射子弹
                        SpawnCustomBullet(direction);
                    }
                }
                
                // 每0.1秒发射一次
                yield return wait01s;
            }
            
            ModBehaviour.DevLog("[DragonKing] 自定义射击循环结束");
        }
        
        /// <summary>
        /// 发射自定义子弹（朝玩家方向）
        /// </summary>
        private void SpawnCustomBullet(Vector3 direction)
        {
            try
            {
                if (bossCharacter == null) return;
                if (LevelManager.Instance == null || LevelManager.Instance.BulletPool == null) return;

                // 检查子弹预制体是否已缓存（初始化时已缓存，运行时不再重复缓存）
                if (cachedWeaponBullet == null) return;
                
                // 使用原武器的子弹速度
                float bulletSpeed = cachedWeaponBulletSpeed;
                
                // 计算发射位置（Boss胸口位置）
                Vector3 muzzlePos = bossCharacter.transform.position + Vector3.up * DragonKingConfig.BossChestHeightOffset;
                
                // 播放射击音效（与龙裔一致，每发都播放）
                PlayWeaponShootSound();
                
                // 从BulletPool获取子弹
                Projectile bullet = LevelManager.Instance.BulletPool.GetABullet(cachedWeaponBullet);
                if (bullet == null) return;
                
                // 设置子弹位置和方向
                bullet.transform.position = muzzlePos;
                bullet.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                
                // 创建ProjectileContext（使用配置参数）
                ProjectileContext ctx = default(ProjectileContext);
                ctx.direction = direction.normalized;
                ctx.speed = bulletSpeed;
                ctx.distance = DragonKingConfig.CustomBulletDistance;
                ctx.halfDamageDistance = DragonKingConfig.CustomBulletHalfDamageDistance;
                ctx.damage = DragonKingConfig.CustomBulletDamage;
                ctx.penetrate = 0;
                ctx.critRate = DragonKingConfig.CustomBulletCritRate;
                ctx.critDamageFactor = DragonKingConfig.CustomBulletCritDamageFactor;
                ctx.armorPiercing = 0f;
                ctx.armorBreak = 0f;
                ctx.fromCharacter = bossCharacter;
                ctx.team = bossCharacter.Team;
                ctx.firstFrameCheck = false;
                
                // 不追踪，直线飞行
                ctx.traceTarget = null;
                ctx.traceAbility = 0f;
                
                // 初始化子弹
                bullet.Init(ctx);
                
                // 记录到BulletPool弹幕列表（不手动销毁，由BulletPool管理）
                bulletPoolProjectiles.Add(bullet.gameObject);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 发射自定义子弹失败: {e.Message}");
            }
        }

        
        // ========== 棱彩弹攻击 ==========
        
        /// <summary>
        /// 执行棱彩弹攻击
        /// 在Boss周围生成8个彩虹弹幕，延迟后追踪玩家
        /// </summary>
        private IEnumerator ExecutePrismaticBolts()
        {
            ModBehaviour.DevLog("[DragonKing] 执行棱彩弹攻击");

            if (bossCharacter == null) yield break;

            // 暂停AI移动，Boss停留原地释放技能
            StopBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 棱彩弹：AI已暂停，Boss停留原地");

            // 记录Boss当前位置
            Vector3 bossPos = bossCharacter.transform.position;
            int boltCount = DragonKingConfig.PrismaticBoltCount;
            float angleStep = 360f / boltCount;
            float scale = DragonKingConfig.PrismaticBoltScale;

            // 生成弹幕 - 使用Unity预制体
            // 预分配List容量
            List<GameObject> bolts = new List<GameObject>(boltCount);
            for (int i = 0; i < boltCount; i++)
            {
                float angle = i * angleStep;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 1f;
                Vector3 spawnPos = bossPos + offset + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;

                // 使用Unity预制体创建棱彩弹
                GameObject bolt = DragonKingAssetManager.InstantiateEffect(
                    DragonKingConfig.PrismaticBoltPrefab,
                    spawnPos,
                    Quaternion.LookRotation(offset.normalized)
                );

                if (bolt != null)
                {
                    // 设置缩放
                    bolt.transform.localScale = Vector3.one * scale;

                    // 设置刚体速度（预制体中已配置Rigidbody组件）
                    var rb = bolt.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.velocity = offset.normalized * DragonKingConfig.PrismaticBoltSpeed;
                    }

                    bolts.Add(bolt);
                    activeProjectiles.Add(bolt);
                    
                    // 播放棱彩弹1生成音效（每个弹幕都播放）
                    ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_BoltSpawn);
                }
            }

            // 延迟后开始追踪
            yield return wait05s;

            // 恢复AI移动
            ResumeBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 棱彩弹：AI已恢复");

            // 启动追踪协程（添加到协程管理列表）
            foreach (var bolt in bolts)
            {
                if (bolt != null)
                {
                    StartTrackedCoroutine(TrackingProjectile(bolt, DragonKingConfig.PrismaticBoltLifetime));
                }
            }

            // 等待弹幕生命周期结束（使用缓存，PrismaticBoltLifetime=5f）
            yield return wait5s;
        }
        
        /// <summary>
        /// 统一的追踪弹幕协程（合并了4个重复协程）
        /// 前N秒追踪玩家，之后直线飞行
        /// 自动检测是否有刚体，选择合适的移动方式
        /// </summary>
        /// <param name="projectile">弹幕对象</param>
        /// <param name="lifetime">生命周期（秒）</param>
        /// <param name="trackingDuration">追踪持续时间（秒），默认使用配置值</param>
        private IEnumerator TrackingProjectileCore(GameObject projectile, float lifetime, float trackingDuration = -1f)
        {
            // 使用默认追踪时间（如果未指定）
            if (trackingDuration < 0f)
            {
                trackingDuration = DragonKingConfig.PrismaticBoltTrackingDuration;
            }
            
            float startTime = Time.time;
            float speed = DragonKingConfig.PrismaticBoltSpeed;
            float trackingStrength = DragonKingConfig.PrismaticBoltTrackingStrength;
            float targetHeightOffset = DragonKingConfig.PlayerTargetHeightOffset;

            // 获取刚体组件（决定移动方式）
            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            bool useRigidbody = (rb != null);

            Vector3 currentVelocity = projectile.transform.forward * speed;
            bool trackingEnded = false;

            // 如果有刚体，设置初始速度
            if (useRigidbody)
            {
                rb.velocity = currentVelocity;
            }

            while (projectile != null && Time.time - startTime < lifetime)
            {
                float elapsed = Time.time - startTime;
                Vector3 currentPos = projectile.transform.position;

                // 判断是否还在追踪阶段
                if (elapsed < trackingDuration && !trackingEnded)
                {
                    // 追踪阶段：追踪玩家
                    UpdatePlayerReference();
                    if (playerCharacter != null)
                    {
                        Vector3 targetPos = playerCharacter.transform.position + Vector3.up * targetHeightOffset;
                        Vector3 dirToTarget = (targetPos - currentPos).normalized;

                        // 不完美追踪：混合当前方向和目标方向
                        if (!useRigidbody && currentVelocity.sqrMagnitude < 0.01f)
                        {
                            // 非刚体模式下，如果速度为0则直接设置
                            currentVelocity = dirToTarget * speed;
                        }
                        else
                        {
                            Vector3 desiredVelocity = dirToTarget * speed;
                            currentVelocity = Vector3.Lerp(currentVelocity, desiredVelocity, trackingStrength * Time.deltaTime * 5f);
                        }
                    }
                }
                else if (!trackingEnded)
                {
                    // 追踪结束，锁定当前方向
                    trackingEnded = true;
                    if (currentVelocity.sqrMagnitude > 0.01f)
                    {
                        currentVelocity = currentVelocity.normalized * speed;
                    }
                    ModBehaviour.DevLog("[DragonKing] 棱彩弹追踪结束，转为直线飞行");
                }
                // 直线飞行阶段：保持当前速度方向不变

                // 根据是否有刚体选择移动方式
                if (useRigidbody)
                {
                    rb.velocity = currentVelocity;
                }
                else
                {
                    projectile.transform.position += currentVelocity * Time.deltaTime;
                }

                // 设置朝向
                if (currentVelocity.sqrMagnitude > 0.01f)
                {
                    projectile.transform.rotation = Quaternion.LookRotation(currentVelocity);
                }

                // 检测碰撞
                if (CheckProjectileHit(currentPos, DragonKingConfig.PrismaticBoltDamage))
                {
                    Destroy(projectile);
                    yield break;
                }

                yield return null;
            }

            // 销毁弹幕
            if (projectile != null)
            {
                activeProjectiles.Remove(projectile);
                Destroy(projectile);
            }
        }
        
        /// <summary>
        /// 追踪弹幕协程（兼容旧接口）
        /// </summary>
        private IEnumerator TrackingProjectile(GameObject projectile, float lifetime)
        {
            return TrackingProjectileCore(projectile, lifetime);
        }
        
        /// <summary>
        /// 检测弹幕是否命中玩家
        /// 性能优化：使用sqrMagnitude避免开方运算
        /// </summary>
        private bool CheckProjectileHit(Vector3 position, float damage)
        {
            if (playerCharacter == null) return false;
            
            // 性能优化：使用sqrMagnitude避免开方运算
            // 使用配置中的常量
            float hitRadius = DragonKingConfig.ProjectileHitRadius;
            float hitRadiusSqr = hitRadius * hitRadius;
            float targetHeightOffset = DragonKingConfig.PlayerTargetHeightOffset;
            Vector3 diff = position - (playerCharacter.transform.position + Vector3.up * targetHeightOffset);
            
            if (diff.sqrMagnitude < hitRadiusSqr)
            {
                ApplyDamageToPlayer(damage);
                
                // 播放棱彩弹命中音效
                ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_BoltHit);
                
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// 对玩家造成伤害
        /// </summary>
        private void ApplyDamageToPlayer(float damage)
        {
            if (playerCharacter == null || playerCharacter.Health == null) return;
            
            try
            {
                ModBehaviour.DevLog($"[DragonKing] 对玩家造成伤害: {damage}");
                
                DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                dmgInfo.damageValue = damage;
                // 设置伤害点位置（玩家身体中心），这样伤害数字才能正确显示
                // 使用配置中的常量
                dmgInfo.damagePoint = playerCharacter.transform.position + Vector3.up * DragonKingConfig.DamagePointHeightOffset;
                dmgInfo.damageNormal = Vector3.up;
                // 添加火元素伤害，让原版受伤系统显示伤害数字
                dmgInfo.AddElementFactor(ElementTypes.fire, 1f);
                playerCharacter.Health.Hurt(dmgInfo);
                
                // 播放玩家受伤音效
                PlayHurtSound();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 造成伤害失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 缓存的受伤音效路径
        /// </summary>
        private static string cachedHurtSoundPath = null;
        
        /// <summary>
        /// 是否已检查过受伤音效路径
        /// </summary>
        private static bool hurtSoundPathChecked = false;
        
        /// <summary>
        /// 播放玩家受伤音效（使用缓存路径避免重复文件检查）
        /// </summary>
        private void PlayHurtSound()
        {
            try
            {
                // 只在首次调用时检查路径
                if (!hurtSoundPathChecked)
                {
                    hurtSoundPathChecked = true;
                    string modBasePath = System.IO.Path.GetDirectoryName(GetType().Assembly.Location);
                    
                    // 优先查找Assets目录（标准资源位置）
                    string assetsPath = System.IO.Path.Combine(modBasePath, "Assets", "hurt.mp3");
                    if (System.IO.File.Exists(assetsPath))
                    {
                        cachedHurtSoundPath = assetsPath;
                    }
                    else
                    {
                        string rootPath = System.IO.Path.Combine(modBasePath, "hurt.mp3");
                        if (System.IO.File.Exists(rootPath))
                        {
                            cachedHurtSoundPath = rootPath;
                        }
                    }
                }
                
                if (cachedHurtSoundPath != null && ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.PlaySoundEffect(cachedHurtSoundPath);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 播放受伤音效失败: {e.Message}");
            }
        }
        
        // ========== 棱彩弹2攻击（螺旋） ==========
        
        /// <summary>
        /// 执行棱彩弹2攻击（螺旋发射）
        /// </summary>
        private IEnumerator ExecutePrismaticBolts2()
        {
            ModBehaviour.DevLog("[DragonKing] 执行棱彩弹2攻击（螺旋）");
            
            if (bossCharacter == null) yield break;
            
            // 暂停AI移动，Boss停留原地释放技能
            StopBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 棱彩弹2：AI已暂停，Boss停留原地");
            
            // 记录Boss当前位置，确保释放期间不移动
            Vector3 lockedPosition = bossCharacter.transform.position;
            
            float duration = DragonKingConfig.SpiralFireDuration;
            float angleIncrement = DragonKingConfig.SpiralAngleIncrement;
            float scale = DragonKingConfig.PrismaticBoltScale;
            
            float currentAngle = 0f;
            float startTime = Time.time;
            int boltIndex = 0;
            
            while (Time.time - startTime < duration && bossCharacter != null)
            {
                // 强制保持Boss位置不变
                bossCharacter.transform.position = lockedPosition;
                
                // 从Boss身体中心发射（加上高度偏移）
                Vector3 spawnPos = lockedPosition + Vector3.up * DragonKingConfig.BossChestHeightOffset;
                
                // 计算发射方向（螺旋旋转）
                Vector3 fireDir = Quaternion.Euler(0f, currentAngle, 0f) * Vector3.forward;
                
                // 使用Unity预制体创建棱彩弹
                GameObject bolt = DragonKingAssetManager.InstantiateEffect(
                    DragonKingConfig.PrismaticBoltPrefab,
                    spawnPos,
                    Quaternion.LookRotation(fireDir)
                );

                if (bolt != null)
                {
                    // 设置缩放
                    bolt.transform.localScale = Vector3.one * scale;

                    activeProjectiles.Add(bolt);
                    // 使用棱彩弹2专用的追踪时间（2秒），添加到协程管理列表
                    StartTrackedCoroutine(TrackingProjectileWithDuration(bolt, DragonKingConfig.PrismaticBoltLifetime, DragonKingConfig.PrismaticBolt2TrackingDuration));
                    
                    // 播放棱彩弹2生成音效（每个弹幕都播放）
                    ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_BoltSpawn2);
                }
                
                currentAngle += angleIncrement;
                boltIndex++;
                yield return wait01s; // interval = SpiralFireInterval = 0.1f
            }
            
            // 恢复AI移动
            ResumeBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 棱彩弹2结束，AI已恢复");
        }
        
        /// <summary>
        /// 带自定义追踪时间的追踪弹幕协程（兼容旧接口）
        /// </summary>
        private IEnumerator TrackingProjectileWithDuration(GameObject projectile, float lifetime, float customTrackingDuration)
        {
            return TrackingProjectileCore(projectile, lifetime, customTrackingDuration);
        }
        
        // ========== 冲刺攻击 ==========
        
        /// <summary>
        /// 执行冲刺攻击 - 参考龙裔遗族的实现方式
        /// 使用AI移动而非直接修改位置
        /// </summary>
        private IEnumerator ExecuteDash()
        {
            ModBehaviour.DevLog("[DragonKing] 执行冲刺攻击");
            
            if (bossCharacter == null || playerCharacter == null) yield break;
            
            // 获取起始位置和目标位置
            Vector3 startPos = bossCharacter.transform.position;
            UpdatePlayerReference();
            Vector3 targetPos = playerCharacter.transform.position;
            
            // 计算冲刺方向（水平方向）
            Vector3 dashDir = (targetPos - startPos);
            dashDir.y = 0f;
            dashDir = dashDir.normalized;
            
            // 面向冲刺方向
            if (dashDir.sqrMagnitude > 0.01f)
            {
                bossCharacter.transform.rotation = Quaternion.LookRotation(dashDir);
            }
            
            // 停止Boss移动，准备蓄力
            StopBossMovementAndShooting();
            
            // ========== 蓄力阶段 ==========
            float chargeTime = DragonKingConfig.DashChargeTime;
            float countdownStartTime = chargeTime - DragonKingConfig.DashCountdownRingTime;
            // 在冲刺前0.3秒锁定玩家位置作为第一段冲刺落点
            float lockTargetTime1 = chargeTime - 0.3f;
            // 在冲刺前0.1秒锁定玩家位置作为第二段冲刺方向
            float lockTargetTime2 = chargeTime - 0.1f;
            
            // 播放冲刺蓄力音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_DashCharge);
            
            // 创建粒子聚拢特效
            GameObject chargeParticles = CreateChargeParticles(bossCharacter.transform);
            
            // 倒计时光圈（延迟创建）
            GameObject countdownRing = null;
            
            // 记录锁定的目标位置
            Vector3 lockedTargetPos1 = targetPos;  // 第一段冲刺目标（0.3秒前）
            Vector3 lockedTargetPos2 = targetPos;  // 第二段冲刺方向参考（0.1秒前）
            bool target1Locked = false;
            bool target2Locked = false;
            
            float elapsed = 0f;
            while (elapsed < chargeTime && bossCharacter != null)
            {
                // 在最后0.3秒创建倒计时光圈
                if (elapsed >= countdownStartTime && countdownRing == null)
                {
                    countdownRing = CreateCountdownRing(bossCharacter.transform.position, DragonKingConfig.DashCountdownRingTime);
                }
                
                UpdatePlayerReference();
                if (playerCharacter != null)
                {
                    // 在冲刺前0.3秒锁定第一段目标位置
                    if (!target1Locked && elapsed >= lockTargetTime1)
                    {
                        lockedTargetPos1 = playerCharacter.transform.position;
                        target1Locked = true;
                        ModBehaviour.DevLog($"[DragonKing] 第一段冲刺目标位置已锁定: {lockedTargetPos1}");
                    }
                    
                    // 在冲刺前0.1秒锁定第二段目标位置
                    if (!target2Locked && elapsed >= lockTargetTime2)
                    {
                        lockedTargetPos2 = playerCharacter.transform.position;
                        target2Locked = true;
                        ModBehaviour.DevLog($"[DragonKing] 第二段冲刺目标位置已锁定: {lockedTargetPos2}");
                    }
                    
                    // 计算朝向目标的方向（锁定前跟踪玩家，锁定后朝向锁定位置）
                    Vector3 lookTarget = target1Locked ? lockedTargetPos1 : playerCharacter.transform.position;
                    dashDir = (lookTarget - bossCharacter.transform.position);
                    dashDir.y = 0f;
                    if (dashDir.sqrMagnitude > 0.01f)
                    {
                        dashDir = dashDir.normalized;
                        bossCharacter.transform.rotation = Quaternion.Slerp(
                            bossCharacter.transform.rotation,
                            Quaternion.LookRotation(dashDir),
                            Time.deltaTime * 5f
                        );
                    }
                }
                
                elapsed += Time.deltaTime;
                yield return null;
            }
            
            // 清理蓄力特效
            if (chargeParticles != null) Destroy(chargeParticles);
            
            // 记录最终冲刺方向和目标位置
            Vector3 finalTargetPos = lockedTargetPos1; // 使用第一段锁定的目标位置
            // 使用玩家高度（地面），而不是Boss当前高度，确保冲刺到地面而非空中
            finalTargetPos.y = lockedTargetPos1.y;
            Vector3 startDashPos = bossCharacter.transform.position;
            
            // 重新计算最终冲刺方向（从当前位置到目标位置）
            Vector3 finalDashDir = (finalTargetPos - startDashPos);
            finalDashDir.y = 0f;
            float totalDistance = finalDashDir.magnitude;
            if (totalDistance > 0.1f)
            {
                finalDashDir = finalDashDir.normalized;
            }
            else
            {
                // 距离太近，不需要冲刺
                ModBehaviour.DevLog("[DragonKing] 目标距离太近，跳过冲刺");
                ResumeBossMovementAndShooting();
                yield break;
            }
            
            // ========== 第一段冲刺 - 直接移动到锁定的目标位置 ==========
            ModBehaviour.DevLog($"[DragonKing] 开始第一段冲刺！起点={startDashPos} 目标={finalTargetPos} 距离={totalDistance}");
            
            // 播放冲刺爆发音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_DashBurst);
            
            // 强制转向冲刺方向
            bossCharacter.transform.rotation = Quaternion.LookRotation(finalDashDir);
            if (bossCharacter.movementControl != null)
            {
                bossCharacter.movementControl.ForceTurnTo(finalDashDir);
            }
            
            // 计算冲刺时间（根据距离和速度）
            float dashSpeed = DragonKingConfig.DashSpeed;
            float dashDuration = totalDistance / dashSpeed;
            // 限制最大冲刺时间，防止距离过远时冲刺太久
            dashDuration = Mathf.Min(dashDuration, 2f);
            
            float dashElapsed = 0f;
            bool hitPlayer = false;
            
            // 残影特效计时
            float lastTrailTime = 0f;
            float trailInterval = 0.08f;
            
            while (dashElapsed < dashDuration && bossCharacter != null && !hitPlayer)
            {
                // 使用插值直接移动到目标位置
                float t = dashElapsed / dashDuration;
                // 使用缓出曲线（开始快，结束慢）
                float easedT = 1f - Mathf.Pow(1f - t, 2f);
                
                Vector3 newPos = Vector3.Lerp(startDashPos, finalTargetPos, easedT);
                bossCharacter.transform.position = newPos;
                
                // 生成残影特效
                if (Time.time - lastTrailTime >= trailInterval)
                {
                    lastTrailTime = Time.time;
                    SpawnDashTrailEffect(bossCharacter.transform.position);
                }
                
                // 检测碰撞
                if (CheckDashCollision())
                {
                    ApplyDamageToPlayer(DragonKingConfig.DashDamage);
                    hitPlayer = true;
                    ModBehaviour.DevLog("[DragonKing] 第一段冲刺命中玩家！");
                }
                
                dashElapsed += Time.deltaTime;
                yield return null;
            }
            
            // 确保最终到达目标位置
            if (bossCharacter != null && !hitPlayer)
            {
                bossCharacter.transform.position = finalTargetPos;
            }
            
            // ========== 第二段冲刺 - 仅在二阶段时执行 ==========
            // 使用SetForceMoveVelocity向第二个锁定位置冲刺
            if (bossCharacter != null && CurrentPhase == DragonKingPhase.Phase2)
            {
                // 计算第二段冲刺方向（从当前位置到0.1秒前锁定的位置）
                Vector3 secondTargetPos = lockedTargetPos2;
                secondTargetPos.y = bossCharacter.transform.position.y;
                Vector3 secondDashDir = (secondTargetPos - bossCharacter.transform.position);
                secondDashDir.y = 0f;
                float secondDistance = secondDashDir.magnitude;
                
                if (secondDistance > 0.5f)
                {
                    secondDashDir = secondDashDir.normalized;
                    ModBehaviour.DevLog($"[DragonKing] 开始第二段冲刺！方向={secondDashDir} 距离={secondDistance}");
                    
                    // 强制转向第二段冲刺方向
                    bossCharacter.transform.rotation = Quaternion.LookRotation(secondDashDir);
                    if (bossCharacter.movementControl != null)
                    {
                        bossCharacter.movementControl.ForceTurnTo(secondDashDir);
                    }
                    
                    // 第二段冲刺持续时间（固定0.3秒的短冲刺）
                    float secondDashDuration = 0.3f;
                    float secondDashElapsed = 0f;
                    
                    while (secondDashElapsed < secondDashDuration && bossCharacter != null)
                    {
                        // 使用原版SetForceMoveVelocity进行冲刺
                        float speedMultiplier = 1f - (secondDashElapsed / secondDashDuration) * 0.5f; // 速度从1.0衰减到0.5
                        bossCharacter.SetForceMoveVelocity(dashSpeed * speedMultiplier * secondDashDir);
                        
                        // 生成残影特效
                        if (Time.time - lastTrailTime >= trailInterval)
                        {
                            lastTrailTime = Time.time;
                            SpawnDashTrailEffect(bossCharacter.transform.position);
                        }
                        
                        // 检测碰撞
                        if (CheckDashCollision())
                        {
                            ApplyDamageToPlayer(DragonKingConfig.DashDamage);
                            ModBehaviour.DevLog("[DragonKing] 第二段冲刺命中玩家！");
                            break;
                        }
                        
                        secondDashElapsed += Time.deltaTime;
                        yield return null;
                    }
                    
                    // 停止强制移动
                    if (bossCharacter != null)
                    {
                        bossCharacter.SetForceMoveVelocity(Vector3.zero);
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[DragonKing] 第二段冲刺距离太近，跳过");
                }
            }
            
            // ========== 冲刺结束 ==========
            
            // 恢复AI行动
            ResumeBossMovementAndShooting();
            
            // 冲刺结束后随机切换悬浮方向
            RandomizeHoverSide();
            
            ModBehaviour.DevLog("[DragonKing] 冲刺结束");
        }
        
        /// <summary>
        /// 创建粒子聚拢特效 - 粒子从四周向Boss身上聚拢
        /// </summary>
        private GameObject CreateChargeParticles(Transform parent)
        {
            try
            {
                GameObject particleObj = new GameObject("DashChargeParticles");
                particleObj.transform.SetParent(parent);
                particleObj.transform.localPosition = Vector3.up * 1f; // 稍微抬高
                
                // 创建粒子系统
                var ps = particleObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.duration = DragonKingConfig.DashChargeTime;
                main.loop = false;
                main.startLifetime = 0.5f;
                main.startSpeed = -5f; // 负速度 = 向中心聚拢
                main.startSize = 0.2f;
                main.startColor = new Color(1f, 1f, 1f, 1f); // 白色
                main.maxParticles = 100;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                
                // 发射器设置 - 从球形表面向内发射（范围缩小）
                var emission = ps.emission;
                emission.rateOverTime = 50f;
                
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 2.5f; // 缩小范围
                shape.radiusThickness = 0f; // 只从表面发射
                
                // 关键：设置粒子渲染器的材质
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    // 使用共享材质（白色粒子）
                    Material particleMat = GetSharedWhiteParticleMaterial();
                    if (particleMat != null)
                    {
                        renderer.material = particleMat;
                    }
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                }
                
                // 颜色渐变（白色系，带淡蓝色调）
                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] { 
                        new GradientColorKey(new Color(0.9f, 0.95f, 1f), 0f), // 淡蓝白色
                        new GradientColorKey(new Color(1f, 1f, 1f), 1f) // 纯白色
                    },
                    new GradientAlphaKey[] { 
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                colorOverLifetime.color = gradient;
                
                // 大小渐变（从大到小）
                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
                
                // 添加光源增强视觉效果（白色光）
                Light light = particleObj.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.9f, 0.95f, 1f); // 淡蓝白色光
                light.intensity = 3f;
                light.range = 4f;
                
                // 添加光源闪烁脚本
                var flicker = particleObj.AddComponent<LightFlicker>();
                flicker.baseIntensity = 3f;
                flicker.flickerAmount = 1.5f;
                flicker.flickerSpeed = 10f;
                
                ps.Play();
                
                activeEffects.Add(particleObj);
                ModBehaviour.DevLog("[DragonKing] 创建粒子聚拢特效");
                
                return particleObj;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 创建粒子聚拢特效失败: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 创建倒计时缩小光圈 - 使用 LineRenderer 实现更可靠的显示
        /// </summary>
        private GameObject CreateCountdownRing(Vector3 position, float duration)
        {
            try
            {
                GameObject ringObj = new GameObject("DashCountdownRing");
                ringObj.transform.position = position + Vector3.up * 0.05f; // 稍微抬高避免z-fighting

                // 使用 LineRenderer 绘制圆环（更可靠）
                var lineRenderer = ringObj.AddComponent<LineRenderer>();

                // 使用共享材质（避免重复创建）
                Material mat = GetSharedInternalColoredMaterial();
                if (mat != null)
                {
                    Color ringColor = new Color(1f, 1f, 1f, 0.9f); // 白色
                    lineRenderer.material = mat;
                    lineRenderer.startColor = ringColor;
                    lineRenderer.endColor = ringColor;
                }
                
                // 设置线条属性
                lineRenderer.startWidth = 0.25f;
                lineRenderer.endWidth = 0.25f;
                lineRenderer.useWorldSpace = false;
                lineRenderer.loop = true;
                
                // 生成圆形顶点
                int segments = 64;
                float radius = 2f; // 缩小光圈范围（原来是3）
                lineRenderer.positionCount = segments;
                
                for (int i = 0; i < segments; i++)
                {
                    float angle = (float)i / segments * 360f * Mathf.Deg2Rad;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;
                    lineRenderer.SetPosition(i, new Vector3(x, 0f, z));
                }
                
                // 添加缩小动画脚本
                var shrink = ringObj.AddComponent<RingShrinkAnimation>();
                shrink.duration = duration;
                shrink.startScale = 1f;
                shrink.endScale = 0f;
                
                // 添加光源增强效果（白色光）
                Light light = ringObj.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.9f, 0.95f, 1f); // 淡蓝白色光
                light.intensity = 2f;
                light.range = 3f;
                
                activeEffects.Add(ringObj);
                ModBehaviour.DevLog($"[DragonKing] 创建倒计时光圈，持续时间={duration}");
                
                // 自动销毁
                Destroy(ringObj, duration + 0.1f);
                
                return ringObj;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 创建倒计时光圈失败: {e.Message}");
                return null;
            }
        }
        

        /// <summary>
        /// 生成冲刺残影特效（带岩浆伤害区域）
        /// </summary>
        private void SpawnDashTrailEffect(Vector3 position)
        {
            var effect = DragonKingAssetManager.InstantiateEffect(
                DragonKingConfig.DashTrailPrefab,
                position,
                bossCharacter != null ? bossCharacter.transform.rotation : Quaternion.identity
            );
            
            if (effect != null)
            {
                activeEffects.Add(effect);
                
                // 添加岩浆伤害区域组件
                var lavaZone = effect.AddComponent<DragonKingLavaZone>();
                lavaZone.Initialize(
                    DragonKingConfig.LavaDamage,
                    DragonKingConfig.LavaDamageInterval,
                    DragonKingConfig.LavaDuration,
                    DragonKingConfig.LavaRadius,
                    bossCharacter
                );
                
                // 特效持续时间与岩浆区域一致
                Destroy(effect, DragonKingConfig.LavaDuration);
            }
        }
        
        /// <summary>
        /// 检测冲刺碰撞
        /// 性能优化：使用sqrMagnitude避免开方运算
        /// </summary>
        private bool CheckDashCollision()
        {
            if (bossCharacter == null || playerCharacter == null) return false;
            
            // 性能优化：使用sqrMagnitude避免开方运算
            const float collisionRadius = 1.5f;
            const float collisionRadiusSqr = collisionRadius * collisionRadius; // 2.25f
            Vector3 diff = bossCharacter.transform.position - playerCharacter.transform.position;
            
            return diff.sqrMagnitude < collisionRadiusSqr;
        }
        
        // ========== 太阳舞攻击 ==========

        /// <summary>
        /// 验证位置是否有效（不在空心物体内，玩家和敌怪都能走到）
        /// 使用物理碰撞检测确保位置没有障碍物
        /// </summary>
        private bool IsPositionValid(Vector3 position)
        {
            // 检测参数：使用角色大小进行碰撞检测
            float checkRadius = 0.5f;  // 角色碰撞半径
            float checkHeight = 2f;    // 角色高度
            Vector3 checkCenter = position + Vector3.up * (checkHeight / 2f);

            // 使用缓存的LayerMask（避免重复计算）
            int obstacleLayerMask = GetGroundObstacleLayerMask();

            // 使用OverlapCapsule检测该位置是否有障碍物
            // Capsule从脚部延伸到头顶，如果与任何障碍物碰撞则位置无效
            Collider[] hitColliders = Physics.OverlapCapsule(
                position,
                position + Vector3.up * checkHeight,
                checkRadius,
                obstacleLayerMask
            );

            // 如果检测到碰撞体（除了角色自己的碰撞体），说明位置无效
            foreach (var col in hitColliders)
            {
                // 忽略玩家和Boss自己的碰撞体
                if (col.GetComponentInParent<CharacterMainControl>() != null)
                    continue;

                // 忽略触发器（触发器通常不是物理障碍）
                if (col.isTrigger)
                    continue;

                // 发现其他碰撞体，位置无效
                return false;
            }

            return true;
        }

        /// <summary>
        /// 在玩家附近找到一个有效的传送位置
        /// 尝试多次直到找到不在空心物体内部的位置
        /// </summary>
        private Vector3 FindValidTeleportPosition(Vector3 centerPos, float minDistance, float maxDistance, int maxAttempts = 10)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                // 生成随机偏移
                Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * maxDistance;
                // 确保最小距离
                if (randomOffset.magnitude < minDistance)
                {
                    randomOffset = randomOffset.normalized * minDistance;
                }

                Vector3 candidatePos = centerPos + new Vector3(randomOffset.x, 0f, randomOffset.y);
                candidatePos.y = centerPos.y; // 确保同一平面

                // 验证位置
                if (IsPositionValid(candidatePos))
                {
                    ModBehaviour.DevLog("[DragonKing] 找到有效传送位置，尝试次数: " + (attempt + 1));
                    return candidatePos;
                }
            }

            // 如果尝试多次后仍找不到有效位置，返回玩家位置作为备选
            ModBehaviour.DevLog("[DragonKing] 警告: 未能找到理想传送位置，使用玩家位置");
            return centerPos;
        }

        /// <summary>
        /// 执行太阳舞攻击
        /// 龙王传送到玩家附近，传送后收枪并停止AI，发射旋转弹幕
        /// </summary>
        private IEnumerator ExecuteSunDance()
        {
            ModBehaviour.DevLog("[DragonKing] 执行太阳舞攻击");

            if (bossCharacter == null || playerCharacter == null) yield break;
            
            // 计算目标位置（玩家同一平面，偏移一定距离）
            // 使用位置验证确保不会落在空心物体内
            Vector3 playerPos = playerCharacter.transform.position;
            Vector3 targetPos = FindValidTeleportPosition(playerPos, 2f, 3f);
            
            // 显示预警圆圈并等待充能（此时Boss还没收枪，可以继续射击）
            float chargeTime = 1.5f; // 充能时间
            GameObject warningCircle = CreateWarningCircle(targetPos, chargeTime);
            if (warningCircle != null)
            {
                activeEffects.Add(warningCircle); // 添加到全局清理列表
            }
            
            // 播放太阳舞警告音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_SunWarning);
            
            yield return wait15s; // 使用缓存的WaitForSeconds(1.5f)
            
            // 销毁预警圆圈
            if (warningCircle != null)
            {
                activeEffects.Remove(warningCircle); // 从全局清理列表移除
                Destroy(warningCircle);
            }
            
            // Boss传送到目标位置
            bossCharacter.transform.position = targetPos;
            
            // 记录锁定位置（用于弹幕发射位置）
            sunDanceLockPosition = targetPos;
            
            // 【修改】不再调用PutBackWeapon，射击继续进行
            
            // 暂停AI行为，让Boss停留原地
            StopBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 太阳舞：传送完成，AI已暂停");
            
            // 标记太阳舞开始
            isSunDanceActive = true;
            
            // 生成光束组（放在Boss身体中间高度）
            Vector3 beamPos = targetPos + Vector3.up * DragonKingConfig.BossChestHeightOffset; // Boss身体中间高度
            GameObject beamGroup = DragonKingAssetManager.InstantiateEffect(
                DragonKingConfig.SunBeamGroupPrefab,
                beamPos,
                Quaternion.identity
            );
            
            if (beamGroup != null)
            {
                activeEffects.Add(beamGroup);
                
                // 给所有Edges子对象添加伤害触发器
                SetupSunBeamDamageTriggers(beamGroup);
            }
            
            // 启动旋转弹幕发射协程
            sunDanceBarrageCoroutine = StartCoroutine(SunDanceBarrageLoop());
            
            // 等待技能持续时间（使用缓存，SunDanceDuration=5f）
            yield return wait5s;
            
            // 标记太阳舞结束
            isSunDanceActive = false;
            
            // 停止弹幕发射
            if (sunDanceBarrageCoroutine != null)
            {
                StopCoroutine(sunDanceBarrageCoroutine);
                sunDanceBarrageCoroutine = null;
            }
            
            // 恢复AI行为
            ResumeBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 太阳舞结束，AI已恢复");
            
            // 清理光束
            if (beamGroup != null)
            {
                activeEffects.Remove(beamGroup);
                Destroy(beamGroup);
            }
        }

        /// <summary>
        /// 停止Boss移动和射击（使用统一的AI控制辅助类）
        /// </summary>
        private void StopBossMovementAndShooting()
        {
            if (aiController != null)
            {
                aiController.Pause();
            }
        }

        /// <summary>
        /// 恢复Boss移动和射击
        /// </summary>
        private void ResumeBossMovementAndShooting()
        {
            if (aiController != null)
            {
                aiController.Resume(playerCharacter);
            }
        }
        
        /// <summary>
        /// 太阳舞旋转弹幕发射循环
        /// 同时向24个方向（每15°一个）发射子弹，每0.2s整体旋转5°
        /// </summary>
        private IEnumerator SunDanceBarrageLoop()
        {
            // 初始方向：指向玩家
            UpdatePlayerReference();
            Vector3 initialDir = Vector3.forward;
            if (playerCharacter != null && bossCharacter != null)
            {
                initialDir = (playerCharacter.transform.position - bossCharacter.transform.position).normalized;
                initialDir.y = 0f;
                if (initialDir.sqrMagnitude < 0.01f) initialDir = Vector3.forward;
                initialDir = initialDir.normalized;
            }

            float currentRotation = 0f;           // 当前整体旋转角度
            // 使用配置文件中的弹幕参数
            float rotationPerTick = DragonKingConfig.SunDanceBarrageRotationPerTick;
            float angleStep = DragonKingConfig.SunDanceBarrageAngleStep;
            int directionCount = DragonKingConfig.SunDanceBarrageDirectionCount;
            int tickCount = 0;

            while (isSunDanceActive && bossCharacter != null)
            {
                // 同时向指定数量方向发射子弹
                for (int i = 0; i < directionCount; i++)
                {
                    // 计算当前方向（基于初始方向 + 整体旋转 + 方向索引偏移）
                    float angle = currentRotation + (i * angleStep);
                    Vector3 fireDir = Quaternion.Euler(0f, angle, 0f) * initialDir;

                    // 发射子弹
                    SpawnSunDanceTrackingBullet(fireDir);
                }

                tickCount++;

                // 整体旋转
                currentRotation += rotationPerTick;
                if (currentRotation >= 360f) currentRotation -= 360f;

                // 等待下一次发射（使用配置的tick间隔）
                yield return wait02s;
            }
        }
        
        /// <summary>
        /// 发射太阳舞子弹
        /// 使用龙王武器的子弹预制体和原武器子弹速度
        /// 直线飞行，不追踪
        /// </summary>
        private void SpawnSunDanceTrackingBullet(Vector3 direction)
        {
            // 快速路径检查
            if (bossCharacter == null || cachedWeaponBullet == null)
                return;
            if (LevelManager.Instance == null || LevelManager.Instance.BulletPool == null)
                return;

            Projectile bullet = null;

            try
            {
                // 使用原武器的子弹速度乘以配置的倍率（太阳舞弹幕速度降低）
                float bulletSpeed = cachedWeaponBulletSpeed * DragonKingConfig.SunDanceBulletSpeedMultiplier;

                // 计算发射位置（Boss胸口位置，使用锁定位置而非当前位置）
                Vector3 muzzlePos = sunDanceLockPosition + Vector3.up * DragonKingConfig.BossChestHeightOffset;

                // 播放射击音效（内部已做节流）
                PlayWeaponShootSound();

                // 从BulletPool获取子弹
                bullet = LevelManager.Instance.BulletPool.GetABullet(cachedWeaponBullet);
                if (bullet == null)
                    return;

                // 设置子弹位置和方向（不改变大小，保持原预制体大小）
                bullet.transform.position = muzzlePos;
                bullet.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

                // 创建ProjectileContext
                ProjectileContext ctx = default(ProjectileContext);
                ctx.direction = direction.normalized;
                ctx.speed = bulletSpeed;
                ctx.distance = 50f;
                ctx.halfDamageDistance = 25f;
                ctx.damage = DragonKingConfig.SunDanceDamagePerTick;
                ctx.penetrate = 0;
                ctx.critRate = 0f;
                ctx.critDamageFactor = 1.5f;
                ctx.armorPiercing = 0f;
                ctx.armorBreak = 0f;
                ctx.fromCharacter = bossCharacter;
                ctx.team = bossCharacter.Team;
                ctx.element_Fire = 1f; // 火属性子弹
                ctx.firstFrameCheck = false;

                // 不追踪，直线飞行
                ctx.traceTarget = null;
                ctx.traceAbility = 0f;

                // 使用Init方法初始化子弹
                bullet.Init(ctx);

                // 记录到BulletPool弹幕列表（不手动销毁，由BulletPool管理）
                bulletPoolProjectiles.Add(bullet.gameObject);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 发射太阳舞子弹失败: {e.Message}");
            }

            // Init后会重置hitLayers，必须重新设置穿墙属性
            // 将反射调用移到try-catch外部，减少异常处理开销
            // 使用缓存的反射字段，性能优化（只查找一次字段）
            if (bullet != null && cachedHitLayersField != null)
            {
                try
                {
                    cachedHitLayersField.SetValue(bullet, piercingLayerMask);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog($"[DragonKing] [WARNING] 设置穿墙LayerMask异常: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 播放武器射击音效
        /// </summary>
        private void PlayWeaponShootSound()
        {
            try
            {
                if (bossCharacter == null || string.IsNullOrEmpty(cachedWeaponShootKey))
                {
                    return;
                }

                // 每发子弹都播放音效（与龙裔一致，不做节流）
                string eventName = $"SFX/Combat/Gun/Shoot/{cachedWeaponShootKey.ToLower()}";

                // 使用缓存的反射方法（只反射一次）
                if (CacheAudioPostMethod() && cachedAudioPostMethod != null)
                {
                    cachedAudioPostMethod.Invoke(null, new object[] { eventName, bossCharacter.gameObject });
                }
            }
            catch
            {
                // 音效播放失败不影响游戏逻辑
            }
        }

        /// <summary>
        /// 缓存AudioManager.Post方法（只反射一次）
        /// </summary>
        private static bool CacheAudioPostMethod()
        {
            if (cachedAudioPostMethod != null) return true;

            try
            {
                var audioManagerType = typeof(LevelManager).Assembly.GetType("Duckov.AudioManager");
                if (audioManagerType != null)
                {
                    cachedAudioPostMethod = audioManagerType.GetMethod("Post",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null,
                        new System.Type[] { typeof(string), typeof(GameObject) },
                        null);
                    return cachedAudioPostMethod != null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 缓存AudioManager.Post异常: {e.Message}");
            }

            return false;
        }
        
        /// <summary>
        /// 创建预警圆圈（实心圆+透明遮罩层逐渐缩小）
        /// </summary>
        private GameObject CreateWarningCircle(Vector3 position, float chargeTime)
        {
            // 创建圆环容器
            GameObject circleObj = new GameObject("WarningCircle");
            circleObj.transform.position = position + Vector3.up * 0.05f; // 稍微抬高避免z-fighting

            float startRadius = 20f; // 起始半径20米
            float endRadius = 0f;    // 最终缩小到0

            // 使用LineRenderer绘制细圆环
            var lineRenderer = circleObj.AddComponent<LineRenderer>();

            // 使用共享材质（黄色）
            Material mat = GetSharedYellowWarningMaterial();
            if (mat != null)
            {
                lineRenderer.material = mat;
            }
            
            // 设置线条属性（细线）
            lineRenderer.startWidth = 0.15f;
            lineRenderer.endWidth = 0.15f;
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            
            // 生成圆形顶点
            int segments = 64;
            lineRenderer.positionCount = segments;
            
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * 360f * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * startRadius;
                float z = Mathf.Sin(angle) * startRadius;
                lineRenderer.SetPosition(i, new Vector3(x, 0f, z));
            }
            
            // 启动圆环缩小动画
            StartCoroutine(AnimateRingShrink(circleObj, lineRenderer, startRadius, endRadius, chargeTime));
            
            return circleObj;
        }
        
        /// <summary>
        /// 圆环缩小动画
        /// </summary>
        private IEnumerator AnimateRingShrink(GameObject circleObj, LineRenderer lineRenderer, float startRadius, float endRadius, float chargeTime)
        {
            if (lineRenderer == null) yield break;
            
            int segments = lineRenderer.positionCount;
            float elapsed = 0f;
            
            while (elapsed < chargeTime && circleObj != null && lineRenderer != null)
            {
                float t = elapsed / chargeTime;
                
                // 使用缓入曲线，让缩小越来越快（更有紧迫感）
                float easedT = t * t;
                float currentRadius = Mathf.Lerp(startRadius, endRadius, easedT);
                
                // 更新圆环顶点位置
                for (int i = 0; i < segments; i++)
                {
                    float angle = (float)i / segments * 360f * Mathf.Deg2Rad;
                    float x = Mathf.Cos(angle) * currentRadius;
                    float z = Mathf.Sin(angle) * currentRadius;
                    lineRenderer.SetPosition(i, new Vector3(x, 0f, z));
                }
                
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        
        /// <summary>
        /// 给太阳舞光束的Edges添加伤害触发器
        /// </summary>
        private void SetupSunBeamDamageTriggers(GameObject beamGroup)
        {
            // 查找所有名为Edge的子对象（Unity里已配置好Collider和Rigidbody）
            var allTransforms = beamGroup.GetComponentsInChildren<Transform>(true);
            int count = 0;
            
            foreach (var t in allTransforms)
            {
                // 查找名称包含"Edge"的对象
                if (t.name.StartsWith("Edge"))
                {
                    // 添加伤害触发器脚本
                    var trigger = t.gameObject.AddComponent<SunBeamDamageTrigger>();
                    trigger.Initialize(this);
                    count++;
                }
            }
            
            ModBehaviour.DevLog($"[DragonKing] 太阳舞伤害触发器已设置，数量: {count}");
        }
        
        /// <summary>
        /// 太阳舞光束造成伤害（由触发器调用）
        /// </summary>
        public void ApplySunBeamDamage()
        {
            ApplyDamageToPlayer(DragonKingConfig.SunDanceDamagePerTick);
        }

        
        // ========== 永恒彩虹攻击 ==========
        
        /// <summary>
        /// 执行永恒彩虹攻击
        /// 生成13颗星环，螺旋扩散后收缩
        /// </summary>
        private IEnumerator ExecuteEverlastingRainbow()
        {
            ModBehaviour.DevLog("[DragonKing] 执行永恒彩虹攻击");

            if (bossCharacter == null) yield break;

            Vector3 centerPos = bossCharacter.transform.position;
            int starCount = DragonKingConfig.RainbowStarCount;
            float maxRadius = DragonKingConfig.RainbowMaxRadius;
            float rotationSpeed = DragonKingConfig.RainbowRotationSpeed;
            float duration = DragonKingConfig.RainbowDuration;
            
            // 播放永恒彩虹生成音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_RainbowSpawn);

            // 生成星星 - 预分配List容量
            List<GameObject> stars = new List<GameObject>(starCount);
            List<float> starAngles = new List<float>(starCount);
            
            float angleStep = 360f / starCount;
            for (int i = 0; i < starCount; i++)
            {
                float angle = i * angleStep;
                Vector3 spawnPos = centerPos + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;
                
                GameObject star = DragonKingAssetManager.InstantiateEffect(
                    DragonKingConfig.RainbowStarPrefab,
                    spawnPos,
                    Quaternion.identity
                );
                
                if (star != null)
                {
                    stars.Add(star);
                    starAngles.Add(angle);
                    activeProjectiles.Add(star);
                }
            }
            
            // 螺旋运动
            float startTime = Time.time;
            float halfDuration = duration * 0.5f;
            
            while (Time.time - startTime < duration && bossCharacter != null)
            {
                float elapsed = Time.time - startTime;
                float progress = elapsed / duration;
                
                // 实时获取龙王位置作为中心点（跟随龙王移动）
                Vector3 currentCenter = bossCharacter.transform.position;
                
                // 计算当前半径（先扩散后收缩）
                float currentRadius;
                if (elapsed < halfDuration)
                {
                    // 扩散阶段
                    currentRadius = maxRadius * (elapsed / halfDuration);
                }
                else
                {
                    // 收缩阶段
                    currentRadius = maxRadius * (1f - (elapsed - halfDuration) / halfDuration);
                }
                
                // 更新每颗星星的位置
                for (int i = 0; i < stars.Count; i++)
                {
                    if (stars[i] == null) continue;
                    
                    // 顺时针旋转
                    starAngles[i] += rotationSpeed * Time.deltaTime;
                    
                    float angle = starAngles[i] * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(
                        Mathf.Cos(angle) * currentRadius,
                        1f,
                        Mathf.Sin(angle) * currentRadius
                    );
                    
                    // 使用实时中心点
                    stars[i].transform.position = currentCenter + offset;
                    
                    // 检测伤害
                    if (CheckProjectileHit(stars[i].transform.position, DragonKingConfig.RainbowTrailDamage))
                    {
                        // 不销毁星星，只造成伤害
                    }
                }
                
                yield return null;
            }
            
            // 清理星星
            foreach (var star in stars)
            {
                if (star != null)
                {
                    activeProjectiles.Remove(star);
                    Destroy(star);
                }
            }
        }
        
        // ========== 以太长矛攻击 ==========
        
        /// <summary>
        /// 执行以太长矛攻击 - 横向贯穿版本
        /// 从屏幕两边每0.1s画一条横穿玩家脚下的50米长线
        /// 警告1秒后射出长矛，每10条暂停0.5秒，共3波
        /// </summary>
        private IEnumerator ExecuteEtherealLance()
        {
            ModBehaviour.DevLog("[DragonKing] 执行以太长矛攻击（横向贯穿）");

            if (bossCharacter == null || playerCharacter == null) yield break;

            int waves = 3;              // 3波
            int linesPerWave = 10;      // 每波10条线
            float lineLength = 50f;     // 线长50米
            // 注：lineInterval=0.1f对应wait01s, warningTime=1f对应wait1s, wavePause=0.5f对应wait05s

            // 使用缓存的彩虹渐变（避免每次攻击重复创建）
            Gradient rainbowGradient = GetSharedRainbowGradient();

            // 执行3波攻击
            for (int wave = 0; wave < waves; wave++)
            {
                // 预分配List容量
                List<GameObject> warningLines = new List<GameObject>(linesPerWave);

                // 每波创建10条警告线（每0.1秒一条）
                for (int i = 0; i < linesPerWave; i++)
                {
                    // 动态获取玩家当前位置（高度设为身体中间）
                    Vector3 currentPos = playerCharacter.transform.position + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;

                    // 每条线都在玩家脚下，旋转5°
                    float rotation = i * 5f;

                    // 创建警告线（中心在玩家脚下，带旋转）
                    GameObject line = CreateHorizontalWarningLine(currentPos, lineLength, rainbowGradient, rotation);
                    if (line != null)
                    {
                        warningLines.Add(line);
                        activeWarningLines.Add(line); // 添加到全局清理列表
                        
                        // 播放长矛警告音效（每条线都播放）
                        ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_LanceWarning);
                    }

                    yield return wait01s; // lineInterval = 0.1f
                }

                // 等待警告显示（已经过了1秒，等待剩余时间）
                // 注：linesPerWave * lineInterval = 10 * 0.1 = 1.0s，与warningTime相等，无需额外等待

                // 射出所有长矛
                foreach (var line in warningLines)
                {
                    if (line != null)
                    {
                        // 播放长矛发射音效（每条长矛都播放）
                        ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_LanceFire);
                        
                        FireLanceFromWarningLine(line);
                        activeWarningLines.Remove(line); // 从全局清理列表移除
                        Destroy(line); // 移除警告线
                    }
                }
                warningLines.Clear();

                // 波次间暂停（最后一波不暂停）
                if (wave < waves - 1)
                {
                    yield return wait05s; // wavePause = 0.5f
                }
            }

            ModBehaviour.DevLog("[DragonKing] 以太长矛攻击完成");
        }

        /// <summary>
        ///创建警告线（50米长，可旋转，支持初始透明度）
        /// </summary>
        private GameObject CreateHorizontalWarningLine(Vector3 center, float length, Gradient gradient, float rotationY, float initialAlpha = 1f)
        {
            try
            {
                GameObject lineObj = new GameObject("EtherealLanceWarningLine");
                lineObj.transform.position = center;

                LineRenderer lr = lineObj.AddComponent<LineRenderer>();

                // 使用共享材质
                Material mat = GetSharedInternalColoredMaterial();
                if (mat != null)
                {
                    lr.material = mat;
                }

                // 设置线条属性
                lr.startWidth = 0.05f;
                lr.endWidth = 0.05f;
                lr.numCornerVertices = 0;
                lr.numCapVertices = 0;
                lr.sortingOrder = 100;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;

                // 设置彩虹渐变（应用初始透明度）
                if (initialAlpha < 1f)
                {
                    // 创建带初始透明度的渐变副本
                    Gradient fadedGradient = new Gradient();
                    fadedGradient.SetKeys(
                        gradient.colorKeys,
                        new GradientAlphaKey[] {
                            new GradientAlphaKey(initialAlpha, 0f),
                            new GradientAlphaKey(initialAlpha * 0.95f, 0.15f),
                            new GradientAlphaKey(initialAlpha * 0.8f, 0.35f),
                            new GradientAlphaKey(initialAlpha * 0.6f, 0.55f),
                            new GradientAlphaKey(initialAlpha * 0.4f, 0.75f),
                            new GradientAlphaKey(initialAlpha * 0.3f, 1f)
                        }
                    );
                    lr.colorGradient = fadedGradient;
                }
                else
                {
                    lr.colorGradient = gradient;
                }

                // 计算旋转后的方向
                Vector3 forwardDir = Quaternion.Euler(0, rotationY, 0) * Vector3.forward;
                Vector3 backDir = Quaternion.Euler(0, rotationY, 0) * Vector3.back;

                // 设置线条顶点（50米长，沿旋转后的方向）
                lr.positionCount = 2;
                lr.SetPosition(0, center + backDir * (length / 2f));
                lr.SetPosition(1, center + forwardDir * (length / 2f));

                // 存储旋转角度到lineObj的transform中，供发射长矛时使用
                lineObj.transform.rotation = Quaternion.Euler(0, rotationY, 0);

                return lineObj;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] 创建警告线失败: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 警告线淡入效果协程
        /// 优化：复用Gradient和GradientAlphaKey数组，减少GC压力
        /// </summary>
        private IEnumerator FadeInWarningLines(List<GameObject> lines, float duration)
        {
            float elapsed = 0f;

            // 预先缓存所有LineRenderer（避免每帧GetComponent）
            var renderers = new List<LineRenderer>(lines.Count);
            foreach (var line in lines)
            {
                if (line != null)
                {
                    var lr = line.GetComponent<LineRenderer>();
                    if (lr != null) renderers.Add(lr);
                }
            }

            // 预分配Gradient和GradientAlphaKey数组（避免每帧创建）
            Gradient fadeGradient = new Gradient();
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[6];
            alphaKeys[0] = new GradientAlphaKey(0f, 0f);
            alphaKeys[1] = new GradientAlphaKey(0f, 0.15f);
            alphaKeys[2] = new GradientAlphaKey(0f, 0.35f);
            alphaKeys[3] = new GradientAlphaKey(0f, 0.55f);
            alphaKeys[4] = new GradientAlphaKey(0f, 0.75f);
            alphaKeys[5] = new GradientAlphaKey(0f, 1f);
            
            // 缓存颜色键（只需获取一次）
            GradientColorKey[] colorKeys = null;
            if (renderers.Count > 0 && renderers[0] != null)
            {
                colorKeys = renderers[0].colorGradient.colorKeys;
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Clamp01(elapsed / duration);

                // 更新预分配的alpha值（避免每帧创建新数组）
                alphaKeys[0].alpha = alpha;
                alphaKeys[1].alpha = alpha * 0.95f;
                alphaKeys[2].alpha = alpha * 0.8f;
                alphaKeys[3].alpha = alpha * 0.6f;
                alphaKeys[4].alpha = alpha * 0.4f;
                alphaKeys[5].alpha = alpha * 0.3f;

                // 更新所有线条的透明度
                foreach (var lr in renderers)
                {
                    if (lr == null) continue;

                    // 复用Gradient对象，只更新alpha键
                    if (colorKeys == null) colorKeys = lr.colorGradient.colorKeys;
                    fadeGradient.SetKeys(colorKeys, alphaKeys);
                    lr.colorGradient = fadeGradient;
                }

                yield return null;
            }
        }

        /// <summary>
        /// 从警告线位置射出实际长矛（双向发射）
        /// </summary>
        private void FireLanceFromWarningLine(GameObject warningLine)
        {
            FireLanceFromWarningLine(warningLine, true, true);
        }

        /// <summary>
        /// 从警告线位置射出实际长矛（可指定方向）
        /// </summary>
        /// <param name="warningLine">警告线对象</param>
        /// <param name="fireFromFront">是否从前端发射（向后射）</param>
        /// <param name="fireFromBack">是否从后端发射（向前射）</param>
        private void FireLanceFromWarningLine(GameObject warningLine, bool fireFromFront, bool fireFromBack)
        {
            if (warningLine == null) return;

            Vector3 linePos = warningLine.transform.position;
            Quaternion lineRotation = warningLine.transform.rotation;

            // 获取旋转后的前后方向
            Vector3 forwardDir = lineRotation * Vector3.forward;
            Vector3 backDir = lineRotation * Vector3.back;

            // 从指定侧生成长矛射向中间
            float spawnDistance = 30f;  // 从30米外射入
            float lanceSpeed = 40f;     // 长矛速度

            // 后方长矛（向前射）
            if (fireFromBack)
            {
                GameObject backLance = DragonKingAssetManager.InstantiateEffect(
                    DragonKingConfig.EtherealLancePrefab,
                    linePos + backDir * spawnDistance,
                    Quaternion.LookRotation(forwardDir)
                );

                if (backLance != null)
                {
                    // 激活 Blade 子物体（使长矛可见）
                    ActivateLanceBlade(backLance);
                    activeProjectiles.Add(backLance);
                    StartCoroutine(MoveLance(backLance, forwardDir, lanceSpeed, spawnDistance * 2f));
                }
            }

            // 前方长矛（向后射）
            if (fireFromFront)
            {
                GameObject forwardLance = DragonKingAssetManager.InstantiateEffect(
                    DragonKingConfig.EtherealLancePrefab,
                    linePos + forwardDir * spawnDistance,
                    Quaternion.LookRotation(backDir)
                );

                if (forwardLance != null)
                {
                    // 激活 Blade 子物体（使长矛可见）
                    ActivateLanceBlade(forwardLance);
                    activeProjectiles.Add(forwardLance);
                    StartCoroutine(MoveLance(forwardLance, backDir, lanceSpeed, spawnDistance * 2f));
                }
            }
        }

        /// <summary>
        /// 激活长矛的 Blade 子物体
        /// </summary>
        private void ActivateLanceBlade(GameObject lance)
        {
            if (lance == null) return;

            // 查找 Blade 子物体并激活
            Transform blade = lance.transform.Find("Blade");
            if (blade != null)
            {
                blade.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// 移动长矛并检测命中
        /// </summary>
        private IEnumerator MoveLance(GameObject lance, Vector3 direction, float speed, float maxDistance)
        {
            float traveled = 0f;

            while (lance != null && traveled < maxDistance)
            {
                float moveDelta = speed * Time.deltaTime;
                lance.transform.position += direction * moveDelta;
                traveled += moveDelta;

                // 检测命中（使用长矛专用的更大检测范围）
                if (CheckLanceHit(lance))
                {
                    activeProjectiles.Remove(lance);
                    Destroy(lance);
                    yield break;
                }

                yield return null;
            }

            // 清理
            if (lance != null)
            {
                activeProjectiles.Remove(lance);
                Destroy(lance);
            }
        }

        /// <summary>
        /// 检测长矛是否命中玩家（使用射线检测）
        /// </summary>
        private bool CheckLanceHit(GameObject lance)
        {
            if (playerCharacter == null || lance == null) return false;

            Vector3 lancePos = lance.transform.position;
            Vector3 playerPos = playerCharacter.transform.position + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;

            // 使用射线检测长矛到玩家的路径
            Vector3 direction = (playerPos - lancePos).normalized;
            float distanceToPlayer = Vector3.Distance(lancePos, playerPos);

            // 长矛长度约2米，检测前方是否有玩家
            float lanceLength = 2f;
            RaycastHit hit;

            if (Physics.Raycast(lancePos, direction, out hit, lanceLength))
            {
                // 检测是否击中玩家
                if (hit.collider.gameObject == playerCharacter.gameObject ||
                    hit.collider.transform.IsChildOf(playerCharacter.transform))
                {
                    ApplyDamageToPlayer(DragonKingConfig.EtherealLanceDamage);
                    return true;
                }
            }

            // 备用检测：近距离检测（2米内）
            if (distanceToPlayer < 2f)
            {
                ApplyDamageToPlayer(DragonKingConfig.EtherealLanceDamage);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取玩家速度
        /// </summary>
        private Vector3 GetPlayerVelocity()
        {
            if (playerCharacter == null) return Vector3.zero;
            
            try
            {
                var rb = playerCharacter.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    return rb.velocity;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 获取玩家刚体速度异常: {e.Message}");
            }

            return Vector3.zero;
        }
        
        // ========== 以太长矛2攻击（切屏） ==========
        
        /// <summary>
        /// 执行以太长矛2攻击（切屏剑阵）
        /// 4波长矛从不同方向切过屏幕
        /// </summary>
        private IEnumerator ExecuteEtherealLance2()
        {
            ModBehaviour.DevLog("[DragonKing] 执行以太长矛2攻击（同时画线）");

            if (bossCharacter == null || playerCharacter == null) yield break;

            int waveCount = DragonKingConfig.ScreenLanceWaveCount;
            int linesPerWave = 16;        // 每波16条线（同时画出）
            float lineLength = 50f;       // 线长50米
            // 注：warningTime=0.5f对应wait05s, wavePause=0.5f对应wait05s

            // 使用缓存的彩虹渐变（避免每次攻击重复创建）
            Gradient rainbowGradient = GetSharedRainbowGradient();

            // 执行4波攻击
            for (int wave = 0; wave < waveCount; wave++)
            {
                // 预分配List容量
                List<GameObject> warningLines = new List<GameObject>(linesPerWave);

                // 模拟玩家移动轨迹来生成线位置
                Vector3 basePos = playerCharacter.transform.position + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;
                Vector3 playerForward = playerCharacter.transform.forward;
                playerForward.y = 0;
                playerForward = playerForward.normalized;

                // 同时创建所有警告线（每条旋转5度），初始透明
                for (int i = 0; i < linesPerWave; i++)
                {
                    // 计算每条线的位置（内联计算，无需额外数组）
                    Vector3 linePos;
                    if (i < 8)
                    {
                        // 前8条：从0到2m，均匀分布
                        float t = (i + 1) / 8f;
                        linePos = basePos + playerForward * (t * 2f);
                    }
                    else
                    {
                        // 后8条：从2m缩减到1m
                        float t = (i - 7) / 8f;
                        linePos = basePos + playerForward * (2f - t * 1f);
                    }

                    float rotation = i * 5f;
                    GameObject line = CreateHorizontalWarningLine(linePos, lineLength, rainbowGradient, rotation, 0f);
                    if (line != null)
                    {
                        warningLines.Add(line);
                        activeWarningLines.Add(line); // 添加到全局清理列表
                    }
                }

                // 启动淡入效果（0.3秒淡入）
                float fadeInDuration = 0.3f;
                StartCoroutine(FadeInWarningLines(warningLines, fadeInDuration));

                // 等待警告显示（warningTime = 0.5f）
                yield return wait05s;

                // 射出长矛（前两波从前端射，后两波从后端射）
                bool fireFromFront = (wave < 2);  // 波次0,1从前端射
                bool fireFromBack = (wave >= 2);  // 波次2,3从后端射
                
                foreach (var line in warningLines)
                {
                    if (line != null)
                    {
                        FireLanceFromWarningLine(line, fireFromFront, fireFromBack);
                        activeWarningLines.Remove(line); // 从全局清理列表移除
                        Destroy(line);
                    }
                }
                warningLines.Clear();

                // 波次间暂停（最后一波不暂停，wavePause = 0.5f）
                if (wave < waveCount - 1)
                {
                    yield return wait05s;
                }
            }

            ModBehaviour.DevLog("[DragonKing] 以太长矛2攻击完成");
        }
        

        // ========== 碰撞伤害处理 ==========
        
        /// <summary>
        /// 碰撞检测器回调 - 当玩家进入碰撞范围时调用
        /// </summary>
        public void OnCollisionWithPlayer(CharacterMainControl player)
        {
            if (player == null || bossCharacter == null) return;
            if (CurrentPhase == DragonKingPhase.Dead) return;

            // 阶段转换时不触发碰撞伤害
            if (CurrentPhase == DragonKingPhase.Transitioning) return;

            // 碰撞检测器被禁用时不触发
            if (collisionDetector != null && !collisionDetector.enabled) return;

            // 检查冷却时间
            if (Time.time - lastCollisionDamageTime < DragonKingConfig.CollisionCooldown) return;
            
            lastCollisionDamageTime = Time.time;
            
            // 播放碰撞音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_Hit);
            
            // 应用碰撞伤害
            ApplyCollisionDamage(player);
            
            // 应用击退
            ApplyKnockback(player);
            
            ModBehaviour.DevLog($"[DragonKing] 碰撞伤害触发，伤害={DragonKingConfig.CollisionDamage}");
        }
        
        /// <summary>
        /// 应用碰撞伤害
        /// </summary>
        private void ApplyCollisionDamage(CharacterMainControl player)
        {
            try
            {
                if (player == null) return;
                
                // 创建伤害信息
                DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                dmgInfo.damageValue = DragonKingConfig.CollisionDamage;
                dmgInfo.damageType = DamageTypes.normal;
                
                // 计算伤害方向（从Boss指向玩家）
                if (bossCharacter != null)
                {
                    dmgInfo.damageNormal = (player.transform.position - bossCharacter.transform.position).normalized;
                }
                
                // 使用原版伤害系统
                bool damageApplied = false;
                
                // 优先使用mainDamageReceiver
                if (player.mainDamageReceiver != null)
                {
                    player.mainDamageReceiver.Hurt(dmgInfo);
                    damageApplied = true;
                }
                // 后备：直接使用Health组件
                else if (player.Health != null)
                {
                    player.Health.Hurt(dmgInfo);
                    damageApplied = true;
                }
                
                if (damageApplied)
                {
                    // 播放受伤音效
                    PlayHurtSound();
                }
                else
                {
                    ModBehaviour.DevLog("[DragonKing] [WARNING] 无法应用碰撞伤害 - 玩家没有有效的伤害接收器");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 应用碰撞伤害失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 应用击退效果
        /// </summary>
        private void ApplyKnockback(CharacterMainControl player)
        {
            try
            {
                if (player == null || bossCharacter == null) return;
                
                // 计算击退方向（从Boss指向玩家）
                Vector3 knockbackDir = (player.transform.position - bossCharacter.transform.position).normalized;
                knockbackDir.y = 0.3f; // 稍微向上
                knockbackDir = knockbackDir.normalized;
                
                // 应用击退力
                float force = 8f;
                var rb = player.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.AddForce(knockbackDir * force, ForceMode.Impulse);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 应用击退失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 切换悬浮方向（在某些攻击后随机切换）
        /// </summary>
        public void RandomizeHoverSide()
        {
            hoverSide = UnityEngine.Random.value > 0.5f ? 1 : -1;
        }
        
        // ========== 孩儿护我系统方法 ==========
        
        /// <summary>
        /// 孩儿护我序列协程
        /// </summary>
        private IEnumerator ChildProtectionSequence()
        {
            ModBehaviour.DevLog("[DragonKing] 触发孩儿护我机制");
            
            isInChildProtection = true;
            
            // 1. 锁血并设置无敌
            if (bossHealth != null)
            {
                bossHealth.SetHealth(DragonKingConfig.ChildProtectionHealthThreshold);
                bossHealth.SetInvincible(true);
            }
            
            // 2. 【关键】立即禁用角色组件，彻底阻止所有行为（参考二阶段转换）
            if (bossCharacter != null)
            {
                bossCharacter.enabled = false;
                
                // 清除手持物品
                if (bossCharacter.CurrentHoldItemAgent != null)
                {
                    bossCharacter.ChangeHoldItem(null);
                }
            }
            
            // 3. 停止所有攻击和射击
            isAttacking = false;
            
            // 停止自定义射击循环
            StopCustomShooting();
            
            // 停止当前攻击协程
            if (currentAttackCoroutine != null)
            {
                StopCoroutine(currentAttackCoroutine);
                currentAttackCoroutine = null;
            }
            
            // 停止攻击循环
            if (attackLoopCoroutine != null)
            {
                StopCoroutine(attackLoopCoroutine);
                attackLoopCoroutine = null;
            }
            
            // 停止太阳舞弹幕（如果正在进行）
            isSunDanceActive = false;
            if (sunDanceBarrageCoroutine != null)
            {
                StopCoroutine(sunDanceBarrageCoroutine);
                sunDanceBarrageCoroutine = null;
            }
            
            // 清理当前攻击的特效
            CleanupAllEffects();
            
            // 4. 暂停AI（禁用AI控制器、路径控制、行为树）
            StopBossMovementAndShooting();
            
            // 5. 禁用碰撞检测器
            if (collisionDetector != null)
            {
                collisionDetector.enabled = false;
            }
            
            // 6. 停止角色移动输入（双重保险）
            if (bossCharacter != null)
            {
                bossCharacter.SetMoveInput(Vector2.zero);
                bossCharacter.SetRunInput(false);
                bossCharacter.Trigger(false, false, false);
            }
            
            // 7. 显示龙王对话气泡
            ShowDragonKingDialogue();
            
            yield return wait1s;
            
            // 8. 飞升到指定高度
            yield return StartCoroutine(FlyToHeight(DragonKingConfig.ChildProtectionFlyHeight));
            
            // 8.5 【关键】飞升完成后，从底层禁用移动系统
            // 使用原版Movement.MovementEnabled属性，从根源阻断所有移动
            if (bossCharacter != null)
            {
                // 禁用Movement组件（阻断UpdateMovement中的所有移动逻辑）
                if (bossCharacter.movementControl != null)
                {
                    bossCharacter.movementControl.MovementEnabled = false;
                }
                
                // 禁用Seeker组件（阻止A*寻路异步回调设置新路径）
                var seeker = bossCharacter.GetComponentInChildren<Pathfinding.Seeker>();
                if (seeker != null)
                {
                    seeker.CancelCurrentPathRequest();
                    seeker.enabled = false;
                }
            }
            
            yield return wait05s;
            
            // 9. 召唤龙裔遗族
            yield return StartCoroutine(SpawnDescendantForProtection());
            
            // 10. 等待龙裔遗族死亡（由OnDescendantDeath回调处理）
            // 协程在此结束，后续由回调处理
            ModBehaviour.DevLog("[DragonKing] 孩儿护我序列完成，等待龙裔遗族死亡");
        }
        
        /// <summary>
        /// 显示龙王对话气泡
        /// </summary>
        private void ShowDragonKingDialogue()
        {
            try
            {
                if (bossCharacter == null) return;
                
                string dialogue = L10n.T(
                    DragonKingConfig.ChildProtectionDialogueCN,
                    DragonKingConfig.ChildProtectionDialogueEN
                );
                
                // 使用DialogueBubblesManager显示气泡
                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    dialogue,
                    bossCharacter.transform,
                    DragonKingConfig.DialogueBubbleYOffset,
                    false,
                    false,
                    -1f,
                    DragonKingConfig.DialogueDuration
                );
                
                ModBehaviour.DevLog("[DragonKing] 显示对话气泡: " + dialogue);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 显示对话气泡失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 飞升到指定高度（复用腾云驾雾机制）
        /// </summary>
        private IEnumerator FlyToHeight(float targetHeight)
        {
            if (bossCharacter == null) yield break;
            
            // 记录起飞前的位置（用于死亡时掉落物生成）
            preFlyPosition = bossCharacter.transform.position;
            ModBehaviour.DevLog($"[DragonKing] 记录起飞前位置: {preFlyPosition}");
            
            ModBehaviour.DevLog($"[DragonKing] 开始飞升到 {targetHeight} 米高度");
            
            // 创建飞行平台（防止下落）
            CreateFlightPlatform();
            
            // 创建云雾特效
            CreateFlightCloudEffect();
            
            float startY = bossCharacter.transform.position.y;
            float targetY = startY + targetHeight;
            lockedMinY = startY;
            
            // 使用加速度机制上升
            float currentUpwardSpeed = 0f;
            float maxUpwardSpeed = DragonKingConfig.ChildProtectionFlySpeed;
            float accelerationTime = 0.3f;
            float upwardAcceleration = maxUpwardSpeed / accelerationTime;
            
            while (bossCharacter != null)
            {
                float currentY = bossCharacter.transform.position.y;
                
                // 到达目标高度
                if (currentY >= targetY)
                {
                    lockedMinY = targetY;
                    break;
                }
                
                // 加速度机制
                currentUpwardSpeed = Mathf.Min(
                    currentUpwardSpeed + upwardAcceleration * Time.deltaTime,
                    maxUpwardSpeed
                );
                
                // 直接修改位置实现上升（简化实现）
                Vector3 pos = bossCharacter.transform.position;
                pos.y += currentUpwardSpeed * Time.deltaTime;
                bossCharacter.transform.position = pos;
                
                // 更新锁定高度
                if (pos.y > lockedMinY)
                {
                    lockedMinY = pos.y;
                }
                
                // 更新飞行平台位置
                UpdateFlightPlatformPosition();
                
                yield return null;
            }
            
            ModBehaviour.DevLog($"[DragonKing] 飞升完成，当前高度: {bossCharacter?.transform.position.y}");
        }
        
        /// <summary>
        /// 创建飞行平台（防止下落）
        /// </summary>
        private void CreateFlightPlatform()
        {
            if (flightPlatform != null) return;
            
            flightPlatform = new GameObject("DragonKing_FlightPlatform");
            flightPlatform.hideFlags = HideFlags.HideInHierarchy;
            
            var boxCollider = flightPlatform.AddComponent<BoxCollider>();
            boxCollider.isTrigger = false;
            boxCollider.center = Vector3.zero;
            boxCollider.size = new Vector3(5f, 0.1f, 5f);
            
            // 设置层级为Ground
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
            {
                flightPlatform.layer = groundLayer;
            }
            
            UpdateFlightPlatformPosition();
            
            ModBehaviour.DevLog("[DragonKing] 创建飞行平台");
        }
        
        /// <summary>
        /// 更新飞行平台位置
        /// </summary>
        private void UpdateFlightPlatformPosition()
        {
            if (flightPlatform == null || bossCharacter == null) return;
            
            Vector3 bossPos = bossCharacter.transform.position;
            flightPlatform.transform.position = new Vector3(bossPos.x, bossPos.y - 0.06f, bossPos.z);
        }
        
        /// <summary>
        /// 销毁飞行平台
        /// </summary>
        private void DestroyFlightPlatform()
        {
            if (flightPlatform != null)
            {
                Destroy(flightPlatform);
                flightPlatform = null;
                ModBehaviour.DevLog("[DragonKing] 销毁飞行平台");
            }
        }
        
        /// <summary>
        /// 创建飞升云雾特效
        /// </summary>
        private void CreateFlightCloudEffect()
        {
            if (flightCloudEffect != null || bossCharacter == null) return;
            
            try
            {
                flightCloudEffect = RingParticleEffect.Create<FlightCloudEffect>(
                    bossCharacter.transform,
                    bossCharacter.transform.position
                );
                ModBehaviour.DevLog("[DragonKing] 创建飞升云雾特效");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 创建云雾特效失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 销毁飞升云雾特效
        /// </summary>
        private void DestroyFlightCloudEffect()
        {
            if (flightCloudEffect != null)
            {
                flightCloudEffect.StopEffect();
                flightCloudEffect = null;
                ModBehaviour.DevLog("[DragonKing] 销毁飞升云雾特效");
            }
        }
        
        /// <summary>
        /// 召唤龙裔遗族保护龙王
        /// </summary>
        private IEnumerator SpawnDescendantForProtection()
        {
            ModBehaviour.DevLog("[DragonKing] 开始召唤龙裔遗族");
            
            // 获取随机刷怪点
            Vector3 spawnPosition = GetRandomSpawnPoint();
            
            // 使用标志位等待异步生成完成
            bool spawnCompleted = false;
            CharacterMainControl spawnResult = null;
            
            // 启动异步生成任务
            SpawnDescendantAsync(spawnPosition, (result) => {
                spawnResult = result;
                spawnCompleted = true;
            });
            
            // 等待生成完成（最多等待10秒）
            float waitTime = 0f;
            while (!spawnCompleted && waitTime < 10f)
            {
                waitTime += Time.deltaTime;
                yield return null;
            }
            
            spawnedDescendant = spawnResult;
            
            if (spawnedDescendant == null)
            {
                ModBehaviour.DevLog("[DragonKing] [WARNING] 龙裔遗族生成失败，龙王直接死亡");
                TriggerLinkedDeath();
                yield break;
            }
            
            // 降低龙裔遗族属性（50%）
            ApplyDescendantStatReduction(spawnedDescendant);
            
            // 显示龙裔遗族对话气泡
            ShowDescendantDialogue();
            
            // 订阅龙裔遗族死亡事件
            if (spawnedDescendant.Health != null)
            {
                spawnedDescendant.Health.OnDeadEvent.AddListener(OnDescendantDeath);
                ModBehaviour.DevLog("[DragonKing] 已订阅龙裔遗族死亡事件");
            }
            
            // 启动孩儿护我阶段的棱彩弹发射协程
            childProtectionBoltCoroutine = StartCoroutine(ChildProtectionBoltLoop());
        }
        
        /// <summary>
        /// 降低召唤的龙裔遗族属性（第三阶段专用）
        /// </summary>
        private void ApplyDescendantStatReduction(CharacterMainControl descendant)
        {
            try
            {
                if (descendant == null || descendant.CharacterItem == null) return;
                
                float multiplier = DragonKingConfig.ChildProtectionDescendantStatMultiplier;
                var item = descendant.CharacterItem;
                
                // 降低血量
                var healthStat = item.GetStat("MaxHealth");
                if (healthStat != null)
                {
                    float newHealth = healthStat.BaseValue * multiplier;
                    healthStat.BaseValue = newHealth;
                    
                    // 同步设置当前血量
                    if (descendant.Health != null)
                    {
                        descendant.Health.SetHealth(newHealth);
                    }
                    
                    ModBehaviour.DevLog($"[DragonKing] 龙裔遗族血量降低至: {newHealth}");
                }
                
                // 降低伤害倍率
                var gunDmgStat = item.GetStat("GunDamageMultiplier");
                if (gunDmgStat != null)
                {
                    gunDmgStat.BaseValue *= multiplier;
                    ModBehaviour.DevLog($"[DragonKing] 龙裔遗族枪械伤害倍率降低至: {gunDmgStat.BaseValue}");
                }
                
                var meleeDmgStat = item.GetStat("MeleeDamageMultiplier");
                if (meleeDmgStat != null)
                {
                    meleeDmgStat.BaseValue *= multiplier;
                    ModBehaviour.DevLog($"[DragonKing] 龙裔遗族近战伤害倍率降低至: {meleeDmgStat.BaseValue}");
                }
                
                ModBehaviour.DevLog($"[DragonKing] 龙裔遗族属性已降低 {(1 - multiplier) * 100}%");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 降低龙裔遗族属性失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 孩儿护我阶段棱彩弹发射循环
        /// </summary>
        private IEnumerator ChildProtectionBoltLoop()
        {
            ModBehaviour.DevLog("[DragonKing] 开始孩儿护我阶段棱彩弹发射循环");
            
            WaitForSeconds waitInterval = new WaitForSeconds(DragonKingConfig.ChildProtectionBoltInterval);
            
            while (isInChildProtection && bossCharacter != null && bossHealth != null && !bossHealth.IsDead)
            {
                yield return waitInterval;
                
                // 再次检查状态（等待期间可能已结束）
                if (!isInChildProtection || bossCharacter == null || bossHealth == null || bossHealth.IsDead) break;
                
                // 发射单个棱彩弹
                FireChildProtectionBolt();
            }
            
            ModBehaviour.DevLog("[DragonKing] 孩儿护我阶段棱彩弹发射循环结束");
        }
        
        /// <summary>
        /// 发射孩儿护我阶段的棱彩弹
        /// </summary>
        private void FireChildProtectionBolt()
        {
            try
            {
                if (bossCharacter == null) return;
                
                // 获取玩家位置
                UpdatePlayerReference();
                if (playerCharacter == null) return;
                
                Vector3 bossPos = bossCharacter.transform.position + Vector3.up * DragonKingConfig.BossChestHeightOffset;
                Vector3 playerPos = playerCharacter.transform.position + Vector3.up * DragonKingConfig.PlayerTargetHeightOffset;
                Vector3 direction = (playerPos - bossPos).normalized;
                
                // 创建棱彩弹
                GameObject bolt = DragonKingAssetManager.InstantiateEffect(
                    DragonKingConfig.PrismaticBoltPrefab,
                    bossPos,
                    Quaternion.LookRotation(direction)
                );
                
                if (bolt != null)
                {
                    // 设置缩放
                    bolt.transform.localScale = Vector3.one * DragonKingConfig.PrismaticBoltScale;
                    
                    // 启动追踪协程（使用统一的追踪弹幕方法）
                    StartTrackedCoroutine(TrackingProjectile(bolt, DragonKingConfig.PrismaticBoltLifetime));
                    
                    // 播放音效
                    ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_BoltSpawn);
                    
                    ModBehaviour.DevLog("[DragonKing] 孩儿护我阶段发射棱彩弹");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 孩儿护我阶段发射棱彩弹失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 获取随机刷怪点
        /// </summary>
        private Vector3 GetRandomSpawnPoint()
        {
            try
            {
                Vector3[] spawnPoints = ModBehaviour.Instance?.GetCurrentSceneSpawnPoints();
                if (spawnPoints != null && spawnPoints.Length > 0)
                {
                    int index = UnityEngine.Random.Range(0, spawnPoints.Length);
                    return spawnPoints[index];
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 获取刷怪点失败: {e.Message}");
            }
            
            // 后备方案：使用龙王位置附近
            if (bossCharacter != null)
            {
                return bossCharacter.transform.position + Vector3.forward * 5f + Vector3.down * DragonKingConfig.ChildProtectionFlyHeight;
            }
            
            return Vector3.zero;
        }
        
        /// <summary>
        /// 异步生成龙裔遗族（辅助方法，用于协程中调用异步方法）
        /// </summary>
        private async void SpawnDescendantAsync(Vector3 position, System.Action<CharacterMainControl> callback)
        {
            try
            {
                CharacterMainControl result = null;
                if (ModBehaviour.Instance != null)
                {
                    result = await ModBehaviour.Instance.SpawnDragonDescendant(position);
                }
                callback?.Invoke(result);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 异步生成龙裔遗族失败: {e.Message}");
                callback?.Invoke(null);
            }
        }
        
        /// <summary>
        /// 显示龙裔遗族对话气泡
        /// </summary>
        private void ShowDescendantDialogue()
        {
            try
            {
                if (spawnedDescendant == null) return;
                
                string dialogue = L10n.T(
                    DragonKingConfig.DescendantDialogueCN,
                    DragonKingConfig.DescendantDialogueEN
                );
                
                // 使用DialogueBubblesManager显示气泡
                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    dialogue,
                    spawnedDescendant.transform,
                    DragonDescendantConfig.DialogueBubbleYOffset,
                    false,
                    false,
                    -1f,
                    DragonKingConfig.DialogueDuration
                );
                
                ModBehaviour.DevLog("[DragonKing] 龙裔遗族显示对话气泡: " + dialogue);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 龙裔遗族显示对话气泡失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 龙裔遗族死亡回调
        /// </summary>
        private void OnDescendantDeath(DamageInfo damageInfo)
        {
            ModBehaviour.DevLog("[DragonKing] 龙裔遗族死亡，触发龙王联动死亡");
            TriggerLinkedDeath();
        }
        
        /// <summary>
        /// 触发联动死亡
        /// </summary>
        private void TriggerLinkedDeath()
        {
            if (bossCharacter == null || bossHealth == null) return;
            
            ModBehaviour.DevLog("[DragonKing] 执行联动死亡");
            
            // 移除无敌状态
            bossHealth.SetInvincible(false);
            isInChildProtection = false;
            
            // 清理飞行平台
            DestroyFlightPlatform();
            
            // 将龙王传送回起飞前的位置（确保掉落物生成在地面）
            if (preFlyPosition != Vector3.zero)
            {
                bossCharacter.transform.position = preFlyPosition;
                ModBehaviour.DevLog($"[DragonKing] 已将龙王传送回起飞前位置: {preFlyPosition}");
            }
            
            // 设置血量为0触发死亡
            bossHealth.SetHealth(0f);
            
            // 创建伤害信息触发死亡事件
            DamageInfo deathDamage = new DamageInfo(null);
            deathDamage.damageValue = 1f;
            bossHealth.Hurt(deathDamage);
        }
        
        /// <summary>
        /// 清理孩儿护我状态
        /// </summary>
        private void CleanupChildProtection()
        {
            // 取消龙裔遗族死亡事件订阅
            if (spawnedDescendant != null && spawnedDescendant.Health != null)
            {
                spawnedDescendant.Health.OnDeadEvent.RemoveListener(OnDescendantDeath);
            }
            spawnedDescendant = null;
            
            // 停止孩儿护我协程
            if (childProtectionCoroutine != null)
            {
                StopCoroutine(childProtectionCoroutine);
                childProtectionCoroutine = null;
            }
            
            // 停止孩儿护我阶段棱彩弹发射协程
            if (childProtectionBoltCoroutine != null)
            {
                StopCoroutine(childProtectionBoltCoroutine);
                childProtectionBoltCoroutine = null;
            }
            
            // 销毁飞行平台
            DestroyFlightPlatform();
            
            // 销毁云雾特效
            DestroyFlightCloudEffect();
            
            // 恢复移动系统（清理时恢复，确保不影响其他逻辑）
            if (bossCharacter != null)
            {
                if (bossCharacter.movementControl != null)
                {
                    bossCharacter.movementControl.MovementEnabled = true;
                }
                
                var seeker = bossCharacter.GetComponentInChildren<Pathfinding.Seeker>();
                if (seeker != null)
                {
                    seeker.enabled = true;
                }
            }
            
            // 重置状态
            childProtectionTriggered = false;
            isInChildProtection = false;
            lockedMinY = float.MinValue;
            preFlyPosition = Vector3.zero;
            
            ModBehaviour.DevLog("[DragonKing] 孩儿护我状态已清理");
        }
    }
    
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
        
        /// <summary>
        /// 缓存的玩家引用
        /// </summary>
        private CharacterMainControl cachedPlayer = null;
        
        /// <summary>
        /// 初始化碰撞检测器
        /// </summary>
        public void Initialize(DragonKingAbilityController ctrl)
        {
            controller = ctrl;
            collisionRadius = DragonKingConfig.CollisionRadius;
            
            // 禁用SphereCollider的触发器功能，改用距离检测
            var sphereCollider = GetComponent<SphereCollider>();
            if (sphereCollider != null)
            {
                sphereCollider.enabled = false;
            }
            
            ModBehaviour.DevLog("[DragonKing] 碰撞检测器组件初始化完成（使用距离检测模式）");
        }
        
        void Update()
        {
            // 每帧检测距离，替代触发器事件
            if (controller == null) return;
            if (!enabled) return;
            
            // 获取玩家引用
            if (cachedPlayer == null || !cachedPlayer.gameObject.activeInHierarchy)
            {
                cachedPlayer = CharacterMainControl.Main;
            }
            
            if (cachedPlayer == null) return;
            
            // 计算与玩家的距离
            float distance = Vector3.Distance(transform.position, cachedPlayer.transform.position);
            
            // 如果在碰撞范围内，尝试触发碰撞
            if (distance <= collisionRadius)
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
    
    // ========== Billboard效果组件 ==========
    
    /// <summary>
    /// Billboard效果组件 - 让物体始终面向摄像机
    /// 用于让四角星形状的棱彩弹始终正面朝向玩家
    /// </summary>
    public class BillboardEffect : MonoBehaviour
    {
        /// <summary>
        /// 缓存的摄像机Transform
        /// </summary>
        private Transform cachedCameraTransform;
        
        /// <summary>
        /// 上次更新摄像机引用的时间
        /// </summary>
        private float lastCameraUpdateTime = 0f;
        
        /// <summary>
        /// 摄像机引用更新间隔（秒）
        /// </summary>
        private const float CAMERA_UPDATE_INTERVAL = 1f;
        
        void Start()
        {
            UpdateCameraReference();
        }
        
        void LateUpdate()
        {
            // 定期更新摄像机引用（防止摄像机切换）
            if (Time.time - lastCameraUpdateTime > CAMERA_UPDATE_INTERVAL)
            {
                UpdateCameraReference();
            }
            
            // 让物体面向摄像机
            if (cachedCameraTransform != null)
            {
                // 只旋转Y轴，保持物体水平
                Vector3 lookDir = cachedCameraTransform.position - transform.position;
                lookDir.y = 0f;
                
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(-lookDir);
                }
            }
        }
        
        /// <summary>
        /// 更新摄像机引用
        /// </summary>
        private void UpdateCameraReference()
        {
            lastCameraUpdateTime = Time.time;
            
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                cachedCameraTransform = mainCam.transform;
            }
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
        
        public void Initialize(DragonKingAbilityController ctrl)
        {
            controller = ctrl;
        }
        
        void OnTriggerEnter(Collider other)
        {
            ModBehaviour.DevLog($"[DragonKing] SunBeam OnTriggerEnter: {other.name}, tag={other.tag}");
            ProcessCollision(other);
        }
        
        void OnTriggerStay(Collider other)
        {
            ProcessCollision(other);
        }
        
        private void ProcessCollision(Collider other)
        {
            // 检查是否是玩家
            if (controller == null) return;
            if (Time.time - lastDamageTime < DAMAGE_INTERVAL) return;
            
            // 检查碰撞对象是否是玩家
            var character = other.GetComponentInParent<CharacterMainControl>();
            if (character != null && character == CharacterMainControl.Main)
            {
                lastDamageTime = Time.time;
                controller.ApplySunBeamDamage();
                ModBehaviour.DevLog("[DragonKing] SunBeam 造成伤害!");
            }
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

                    // 收起武器（停止射击行为）
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
                        var behaviour = tree.GetComponent("NodeCanvas.Framework.Behaviour");
                        if (behaviour != null)
                        {
                            // 使用缓存的反射属性
                            if (cachedBehaviourEnabledProperty != null && cachedBehaviourEnabledProperty.CanWrite)
                            {
                                cachedBehaviourEnabledProperty.SetValue(behaviour, false);
                            }
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
                    // 【修复】不再设置defaultWeaponOut=true，龙王使用自定义射击系统
                    // cachedAI.defaultWeaponOut = true;

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
                        var behaviour = tree.GetComponent("NodeCanvas.Framework.Behaviour");
                        if (behaviour != null)
                        {
                            // 使用缓存的反射属性
                            if (cachedBehaviourEnabledProperty != null && cachedBehaviourEnabledProperty.CanWrite)
                            {
                                cachedBehaviourEnabledProperty.SetValue(behaviour, true);
                            }
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
        /// <summary>
        /// 动画持续时间
        /// </summary>
        public float duration = 1f;
        
        /// <summary>
        /// 起始缩放
        /// </summary>
        public float startScale = 1f;
        
        /// <summary>
        /// 结束缩放
        /// </summary>
        public float endScale = 0f;
        
        private float elapsed = 0f;
        private Vector3 initialScale;
        private LineRenderer lineRenderer;
        private Material material;
        private Light ringLight;
        
        void Start()
        {
            initialScale = transform.localScale;
            if (initialScale == Vector3.zero)
            {
                initialScale = Vector3.one;
            }
            
            // 获取 LineRenderer
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                material = lineRenderer.material;
            }
            
            // 获取光源
            ringLight = GetComponent<Light>();
        }
        
        void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            
            // 使用缓动函数让缩小更有紧迫感（先慢后快）
            float easedT = t * t;
            
            // 缩放
            float currentScale = Mathf.Lerp(startScale, endScale, easedT);
            transform.localScale = initialScale * currentScale;
            
            // 颜色变化（白色系，越接近结束越亮）
            Color baseColor = new Color(0.9f, 0.95f, 1f, 0.9f); // 淡蓝白色
            Color brightColor = new Color(1f, 1f, 1f, 1f); // 纯白色
            Color currentColor = Color.Lerp(baseColor, brightColor, t);
            
            // 更新 LineRenderer 颜色
            if (lineRenderer != null)
            {
                lineRenderer.startColor = currentColor;
                lineRenderer.endColor = currentColor;
                
                // 线条宽度也随之变化（越接近结束越粗）
                float width = Mathf.Lerp(0.3f, 0.5f, t);
                lineRenderer.startWidth = width;
                lineRenderer.endWidth = width;
            }
            
            // 更新材质颜色
            if (material != null)
            {
                material.color = currentColor;
            }
            
            // 更新光源强度
            if (ringLight != null)
            {
                ringLight.intensity = Mathf.Lerp(2f, 5f, t);
                ringLight.color = currentColor;
            }
        }
    }
    
    /// <summary>
    /// 龙王岩浆伤害区域组件
    /// 玩家进入时造成火焰伤害并施加点燃Buff
    /// </summary>
    public class DragonKingLavaZone : MonoBehaviour
    {
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
        
        /// <summary>
        /// 球形碰撞器
        /// </summary>
        private SphereCollider sphereCollider;
        
        /// <summary>
        /// 初始化岩浆区域
        /// </summary>
        public void Initialize(float dmg, float interval, float dur, float rad, CharacterMainControl boss)
        {
            damage = dmg;
            damageInterval = interval;
            duration = dur;
            radius = rad;
            bossCharacter = boss;
            createTime = Time.time;
            
            // 缓存点燃Buff
            if (cachedBurnBuff == null)
            {
                cachedBurnBuff = GameplayDataSettings.Buffs.Burn;
            }
            
            // 添加球形触发器
            sphereCollider = gameObject.AddComponent<SphereCollider>();
            sphereCollider.radius = radius;
            sphereCollider.isTrigger = true;
            
            // 设置层级
            gameObject.layer = LayerMask.NameToLayer("Default");
        }
        
        void Update()
        {
            // 检查是否超时
            if (Time.time - createTime > duration)
            {
                Destroy(gameObject);
            }
        }
        
        void OnTriggerStay(Collider other)
        {
            // 检查伤害间隔
            if (Time.time - lastDamageTime < damageInterval) return;
            
            // 获取玩家角色
            CharacterMainControl player = GetPlayerFromCollider(other);
            if (player == null || !player.IsMainCharacter) return;
            
            // 造成伤害
            lastDamageTime = Time.time;
            ApplyLavaDamage(player);
        }
        
        /// <summary>
        /// 对玩家造成岩浆伤害
        /// </summary>
        private void ApplyLavaDamage(CharacterMainControl player)
        {
            if (player == null || player.Health == null) return;
            
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
        
        /// <summary>
        /// 从碰撞器获取玩家角色
        /// </summary>
        private CharacterMainControl GetPlayerFromCollider(Collider other)
        {
            if (other == null) return null;
            
            // 从碰撞器的父级查找
            CharacterMainControl character = other.GetComponentInParent<CharacterMainControl>();
            
            // 从碰撞器本身查找
            if (character == null)
            {
                character = other.GetComponent<CharacterMainControl>();
            }
            
            // 检查DamageReceiver
            if (character == null)
            {
                var damageReceiver = other.GetComponent<DamageReceiver>();
                if (damageReceiver != null && damageReceiver.health != null)
                {
                    character = damageReceiver.health.TryGetCharacter();
                }
            }
            
            return character;
        }
    }
}
