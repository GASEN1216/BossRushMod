// ============================================================================
// DragonDescendantAbilities.cs - 龙裔遗族Boss能力控制器
// ============================================================================
// 模块说明：
//   管理龙裔遗族Boss的所有特殊能力：
//   - 火箭弹发射（每10发子弹触发）
//   - 燃烧弹投掷（根据血量选择目标）
//   - 复活机制（首次濒死时触发）
//   - 狂暴状态（复活后的追逐和撞击）
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    /// <summary>
    /// 龙裔遗族Boss能力控制器
    /// </summary>
    public class DragonDescendantAbilityController : MonoBehaviour
    {
        // ========== 状态变量 ==========
        
        /// <summary>
        /// 子弹计数器
        /// </summary>
        private int bulletCounter = 0;
        
        /// <summary>
        /// 是否已复活
        /// </summary>
        private bool hasResurrected = false;
        
        /// <summary>
        /// 是否处于狂暴状态
        /// </summary>
        private bool isEnraged = false;
        
        /// <summary>
        /// 是否处于无敌状态（复活期间）
        /// </summary>
        private bool isInvulnerable = false;
        
        /// <summary>
        /// 是否正在执行复活序列
        /// </summary>
        private bool isResurrecting = false;
        
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
        /// 燃烧弹计时器协程
        /// </summary>
        private Coroutine grenadeTimerCoroutine;
        
        /// <summary>
        /// 追逐协程
        /// </summary>
        private Coroutine chaseCoroutine;
        
        /// <summary>
        /// 碰撞检测器
        /// </summary>
        private SphereCollider collisionTrigger;
        
        /// <summary>
        /// 上次碰撞时间（防止连续触发）
        /// </summary>
        private float lastCollisionTime = 0f;
        
        // ========== 性能优化：预制体缓存 ==========
        
        /// <summary>
        /// 缓存的燃烧弹预制体（启动时查找一次）
        /// </summary>
        private static Grenade cachedGrenadePrefab = null;
        
        /// <summary>
        /// 是否已搜索过燃烧弹预制体
        /// </summary>
        private static bool grenadeSearched = false;
        
        /// <summary>
        /// 缓存的子弹预制体（启动时查找一次）
        /// </summary>
        private static Projectile cachedBulletPrefab = null;
        
        /// <summary>
        /// 是否已搜索过子弹预制体
        /// </summary>
        private static bool bulletSearched = false;
        
        /// <summary>
        /// 清理静态缓存（场景切换时调用，防止持有已销毁对象引用）
        /// </summary>
        public static void ClearStaticCache()
        {
            cachedGrenadePrefab = null;
            grenadeSearched = false;
            cachedBulletPrefab = null;
            bulletSearched = false;
            
            // 清理WaitForSeconds缓存
            cachedGrenadeInterval = null;
            cachedGrenadeIntervalValue = -1f;
        }
        
        /// <summary>
        /// 二阶段使用的原始武器完整属性
        /// </summary>
        private ModBehaviour.OriginalWeaponData originalWeaponData = null;
        
        /// <summary>
        /// 碰撞冷却时间（使用配置常量）
        /// </summary>
        private const float COLLISION_COOLDOWN = DragonDescendantConfig.CollisionCooldown;
        
        // ========== 性能优化：WaitForSeconds缓存 ==========
        // 避免每次协程中创建新的WaitForSeconds对象产生GC
        
        private static readonly WaitForSeconds wait01s = new WaitForSeconds(0.1f);
        private static readonly WaitForSeconds wait02s = new WaitForSeconds(0.2f);
        private static readonly WaitForSeconds wait05s = new WaitForSeconds(0.5f);
        private static readonly WaitForSeconds wait1s = new WaitForSeconds(1f);
        private static readonly WaitForSeconds wait3s = new WaitForSeconds(3f);
        private static readonly WaitForSeconds wait10s = new WaitForSeconds(10f);
        private static WaitForSeconds cachedGrenadeInterval = null;
        private static float cachedGrenadeIntervalValue = -1f;
        
        // ========== 冰属性伤害减速机制 ==========
        
        /// <summary>
        /// 累计冰属性伤害
        /// </summary>
        private float accumulatedIceDamage = 0f;
        
        // ========== 性能优化：AudioManager反射缓存 ==========
        
        /// <summary>
        /// 缓存的AudioManager类型
        /// </summary>
        private static System.Type cachedAudioManagerType = null;
        
        /// <summary>
        /// 缓存的Post方法
        /// </summary>
        private static MethodInfo cachedAudioPostMethod = null;
        
        /// <summary>
        /// 缓存的PostCustomSFX方法
        /// </summary>
        private static MethodInfo cachedAudioPostCustomSFXMethod = null;
        
        /// <summary>
        /// 是否已缓存AudioManager反射
        /// </summary>
        private static bool audioManagerReflectionCached = false;
        
        /// <summary>
        /// 是否处于冰冻减速状态
        /// </summary>
        private bool isIceSlowed = false;
        
        /// <summary>
        /// 冰冻减速协程
        /// </summary>
        private Coroutine iceSlowdownCoroutine;
        
        // ========== 公开属性 ==========
        
        /// <summary>
        /// 是否处于狂暴状态
        /// </summary>
        public bool IsEnraged { get { return isEnraged; } }
        
        /// <summary>
        /// 是否处于无敌状态
        /// </summary>
        public bool IsInvulnerable { get { return isInvulnerable; } }

        // ========== Mode E 阵营感知 ==========

        /// <summary>
        /// 检查玩家是否为友方（Mode E 同阵营时不攻击玩家）
        /// </summary>
        private bool IsPlayerAlly()
        {
            if (bossCharacter == null) return false;
            var inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsModeEActive) return false;
            // 同阵营 = 友方
            return bossCharacter.Team == inst.ModeEPlayerFaction;
        }

        /// <summary>
        /// Mode E 脱战距离检查：玩家超过配置距离时停止追逐/攻击
        /// 仅在 Mode E 激活且玩家非友方时生效
        /// </summary>
        private bool IsPlayerOutOfLeashRange()
        {
            if (bossCharacter == null || playerCharacter == null) return false;
            var inst = ModBehaviour.Instance;
            if (inst == null || !inst.IsModeEActive) return false;
            float dist = Vector3.Distance(bossCharacter.transform.position, playerCharacter.transform.position);
            return dist > DragonDescendantConfig.LeashDistance;
        }
        
        // ========== 初始化 ==========
        
        /// <summary>
        /// 初始化能力控制器
        /// </summary>
        /// <param name="character">Boss角色</param>
        /// <param name="weaponData">原始武器完整属性（二阶段使用）</param>
        public void Initialize(CharacterMainControl character, ModBehaviour.OriginalWeaponData weaponData = null)
        {
            bossCharacter = character;
            
            // 保存原始武器完整属性（二阶段使用）
            originalWeaponData = weaponData;
            
            if (bossCharacter == null)
            {
                ModBehaviour.DevLog("[DragonDescendant] [ERROR] Initialize: bossCharacter is null");
                return;
            }
            
            // 获取Health组件
            bossHealth = bossCharacter.Health;
            if (bossHealth != null)
            {
                // 订阅伤害事件以实现无敌和复活机制（使用实例事件）
                bossHealth.OnHurtEvent.AddListener(OnBossHurt);
            }
            
            // 获取玩家引用
            try
            {
                playerCharacter = CharacterMainControl.Main;
            }
            catch { }
            
            // 订阅射击事件
            SubscribeToShootEvent();
            
            // 启动燃烧弹计时器
            grenadeTimerCoroutine = StartCoroutine(GrenadeTimerCoroutine());
            
            // 性能优化：预缓存预制体（只在首次初始化时执行）
            PreCachePrefabs();
            
            ModBehaviour.DevLog("[DragonDescendant] 能力控制器初始化完成, 原始武器数据: " + 
                (originalWeaponData != null ? 
                    "子弹=" + (originalWeaponData.bulletPrefab != null ? originalWeaponData.bulletPrefab.name : "null") +
                    ", 射速=" + originalWeaponData.shootSpeed +
                    ", 音效=" + originalWeaponData.shootKey
                    : "null"));
        }
        
        /// <summary>
        /// 预缓存所有需要的预制体（性能优化）
        /// 在初始化时一次性查找，避免战斗中频繁调用
        /// </summary>
        private void PreCachePrefabs()
        {
            // 缓存燃烧弹预制体
            if (!grenadeSearched)
            {
                grenadeSearched = true;
                try
                {
                    // 使用Resources.FindObjectsOfTypeAll查找所有Grenade预制体
                    // 注意：此方法仅在Boss初始化时调用一次，不影响战斗性能
                    var grenades = Resources.FindObjectsOfTypeAll<Grenade>();
                    ModBehaviour.DevLog("[DragonDescendant] 搜索到 " + grenades.Length + " 个Grenade预制体");
                    
                    foreach (var grenade in grenades)
                    {
                        if (grenade == null) continue;
                        
                        // 查找火焰类型的手雷
                        if (grenade.fxType == ExplosionFxTypes.fire || 
                            grenade.name.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0 || 
                            grenade.name.IndexOf("incendiary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            grenade.name.Contains("燃烧") ||
                            grenade.name.Contains("Fire") ||
                            grenade.name.Contains("Burn"))
                        {
                            cachedGrenadePrefab = grenade;
                            ModBehaviour.DevLog("[DragonDescendant] 已缓存燃烧弹预制体: " + grenade.name + " (fxType=" + grenade.fxType + ")");
                            break;
                        }
                        
                        // 记录第一个找到的手雷作为后备
                        if (cachedGrenadePrefab == null)
                        {
                            cachedGrenadePrefab = grenade;
                        }
                    }
                    
                    if (cachedGrenadePrefab != null && cachedGrenadePrefab.fxType != ExplosionFxTypes.fire)
                    {
                        ModBehaviour.DevLog("[DragonDescendant] 使用默认手雷预制体: " + cachedGrenadePrefab.name);
                    }
                    else if (cachedGrenadePrefab == null)
                    {
                        ModBehaviour.DevLog("[DragonDescendant] [WARNING] 未找到任何Grenade预制体，将使用火焰爆炸作为后备");
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] 缓存燃烧弹预制体失败: " + e.Message);
                }
            }
            
            // 缓存子弹预制体
            if (!bulletSearched)
            {
                bulletSearched = true;
                try
                {
                    // 优先从Boss的枪获取
                    if (bossCharacter != null)
                    {
                        var gun = bossCharacter.GetGun();
                        if (gun != null)
                        {
                            var gunSettingField = gun.GetType().GetProperty("GunItemSetting");
                            if (gunSettingField != null)
                            {
                                var gunSetting = gunSettingField.GetValue(gun) as ItemSetting_Gun;
                                if (gunSetting != null && gunSetting.bulletPfb != null)
                                {
                                    cachedBulletPrefab = gunSetting.bulletPfb;
                                    ModBehaviour.DevLog("[DragonDescendant] 已缓存Boss枪械子弹预制体");
                                }
                            }
                        }
                    }
                    
                    // 后备：如果没有找到子弹，记录警告
                    if (cachedBulletPrefab == null)
                    {
                        ModBehaviour.DevLog("[DragonDescendant] [WARNING] 未能缓存子弹预制体");
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] 缓存子弹预制体失败: " + e.Message);
                }
            }
        }

        
        /// <summary>
        /// 订阅射击事件
        /// </summary>
        private void SubscribeToShootEvent()
        {
            try
            {
                // 尝试获取Boss的武器并订阅射击事件
                if (bossCharacter != null && bossCharacter.CharacterItem != null)
                {
                    // 通过反射获取当前武器的射击事件
                    // 由于游戏API可能不直接暴露，我们使用Update轮询检测
                    ModBehaviour.DevLog("[DragonDescendant] 使用Update轮询检测射击");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 订阅射击事件失败: " + e.Message);
            }
        }
        
        // ========== 生命周期 ==========
        
        private void OnDestroy()
        {
            // 取消订阅事件（使用实例事件）
            if (bossHealth != null)
            {
                bossHealth.OnHurtEvent.RemoveListener(OnBossHurt);
            }
            
            // [内存优化] 停止所有协程，包括嵌套的子协程
            // StopAllCoroutines会停止该MonoBehaviour上的所有协程
            StopAllCoroutines();
            
            // 清理协程引用
            grenadeTimerCoroutine = null;
            chaseCoroutine = null;
            iceSlowdownCoroutine = null;

            // 清理其他引用，帮助GC
            bossCharacter = null;
            bossHealth = null;
            playerCharacter = null;
            cachedBossGun = null;
            if (aiController != null) aiController.Cleanup();
            aiController = null;
            originalWeaponData = null;
        }
        
        // ========== 火箭弹逻辑 ==========
        
        /// <summary>
        /// 记录上一帧的弹药数量（用于检测射击）
        /// </summary>
        private int lastBulletInMag = -1;
        
        /// <summary>
        /// 缓存的Boss枪械引用（避免每帧调用GetGun）
        /// </summary>
        private ItemAgent_Gun cachedBossGun = null;
        
        private void Update()
        {
            // [性能优化] 快速返回路径：Boss不存在时直接返回
            if (bossCharacter == null) return;
            
            // 复活期间完全停止AI行为
            if (isResurrecting || aiPaused)
            {
                // 强制停止移动和射击
                bossCharacter.SetMoveInput(Vector2.zero);
                bossCharacter.SetRunInput(false);
                bossCharacter.Trigger(false, false, false);
                if (aiController?.GetAI() != null)
                {
                    aiController?.GetAI().StopMove();
                }
                return; // 不执行其他逻辑
            }
            
            // 检测射击（通过弹药变化）- 只在非狂暴状态下检测
            // [性能说明] 这里使用轮询是因为游戏原版没有暴露射击事件API
            // 已优化：枪械引用每0.5秒刷新一次，减少GetGun()调用
            if (!isEnraged)
            {
                DetectShooting();
            }
            
            // 狂暴状态下的追逐逻辑
            if (isEnraged && playerCharacter != null)
            {
                UpdateChase();
                
                // 狂暴状态下持续停止Boss自身射击（使用直接生成子弹代替）
                bossCharacter.Trigger(false, false, false);
            }
        }
        
        /// <summary>
        /// 射击检测帧计数器（每N帧检测一次）
        /// </summary>
        private int shootDetectFrameCounter = 0;
        
        /// <summary>
        /// 射击检测间隔帧数（每5帧检测一次，约83ms@60fps，低端机友好）
        /// </summary>
        private const int SHOOT_DETECT_FRAME_INTERVAL = 5;
        
        /// <summary>
        /// 检测Boss是否射击
        /// [性能优化] 使用帧计数器替代Time.time比较，减少每帧开销
        /// [设计说明] 由于游戏原版没有暴露射击事件API，只能通过轮询弹药变化来检测
        /// </summary>
        private void DetectShooting()
        {
            // [性能优化] 使用帧计数器，避免每帧调用Time.time
            shootDetectFrameCounter++;
            if (shootDetectFrameCounter < SHOOT_DETECT_FRAME_INTERVAL) return;
            shootDetectFrameCounter = 0;
            
            // 每30帧（约0.5秒@60fps）重新获取枪械引用
            if (cachedBossGun == null)
            {
                cachedBossGun = bossCharacter.GetGun();
                if (cachedBossGun != null)
                {
                    lastBulletInMag = cachedBossGun.BulletCount;
                }
                return;
            }
            
            int currentBullet = cachedBossGun.BulletCount;
            
            if (lastBulletInMag >= 0 && currentBullet < lastBulletInMag)
            {
                // 检测到射击
                int shotsFired = lastBulletInMag - currentBullet;
                OnBossShoot(shotsFired);
            }
            
            lastBulletInMag = currentBullet;
        }
        
        /// <summary>
        /// Boss射击回调
        /// </summary>
        private void OnBossShoot(int shotsFired = 1)
        {
            bulletCounter += shotsFired;
            
            // 每10发子弹发射一次火箭弹
            while (bulletCounter >= DragonDescendantConfig.BulletsPerRocket)
            {
                bulletCounter -= DragonDescendantConfig.BulletsPerRocket;
                LaunchRocket();
            }
        }
        
        /// <summary>
        /// 发射火箭弹 - 简化版：只有玩家距离Boss小于2m时才在玩家位置爆炸
        /// </summary>
        private void LaunchRocket()
        {
            try
            {
                if (bossCharacter == null || playerCharacter == null) return;
                
                // Mode E 同阵营不攻击玩家
                if (IsPlayerAlly()) return;
                
                // Mode E 脱战距离检查
                if (IsPlayerOutOfLeashRange()) return;
                Vector3 explosionPos = playerCharacter.transform.position;
                
                // 检查玩家是否在Boss 2米范围内，只有在范围内才爆炸
                float distToBoss = Vector3.Distance(explosionPos, bossCharacter.transform.position);
                bool shouldExplode = distToBoss <= DragonDescendantConfig.RocketBossDamageRadius;
                
                ModBehaviour.DevLog("[DragonDescendant] 火箭弹检测: 玩家位置: " + explosionPos + 
                    ", Boss距离: " + distToBoss + "m, 是否爆炸: " + shouldExplode);
                
                // 只有玩家在Boss附近时才创建爆炸
                if (!shouldExplode) return;
                
                // 创建爆炸
                if (LevelManager.Instance != null && LevelManager.Instance.ExplosionManager != null)
                {
                    DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                    dmgInfo.damageValue = DragonDescendantConfig.RocketExplosionDamage;
                    dmgInfo.isExplosion = true;
                    dmgInfo.AddElementFactor(ElementTypes.fire, 1f);
                    
                    // 注意：原版ExplosionManager只处理normal和flash类型的特效
                    LevelManager.Instance.ExplosionManager.CreateExplosion(
                        explosionPos, 
                        DragonDescendantConfig.RocketExplosionRadius,
                        dmgInfo, 
                        ExplosionFxTypes.normal, 
                        1f, 
                        true
                    );
                    
                    ModBehaviour.DevLog("[DragonDescendant] 火箭弹爆炸创建成功，范围: " + DragonDescendantConfig.RocketExplosionRadius);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 发射火箭弹失败: " + e.Message);
            }
        }

        
        // ========== 燃烧弹逻辑 ==========
        
        /// <summary>
        /// 燃烧弹计时器协程
        /// [性能优化] 使用缓存的WaitForSeconds避免GC
        /// </summary>
        private IEnumerator GrenadeTimerCoroutine()
        {
            while (true)
            {
                // 根据状态选择间隔
                float interval = isEnraged 
                    ? DragonDescendantConfig.EnragedGrenadeInterval 
                    : DragonDescendantConfig.NormalGrenadeInterval;
                
                // [性能优化] 使用缓存的WaitForSeconds
                yield return GetCachedWaitForSeconds(interval);
                
                // 复活期间不投掷
                if (isResurrecting) continue;
                
                // 投掷燃烧弹
                ThrowIncendiaryGrenade();
            }
        }
        
        /// <summary>
        /// 获取缓存的WaitForSeconds对象
        /// [性能优化] 避免每次创建新对象产生GC
        /// </summary>
        private WaitForSeconds GetCachedWaitForSeconds(float seconds)
        {
            // 常用时间使用预缓存对象
            if (Mathf.Approximately(seconds, 0.1f)) return wait01s;
            if (Mathf.Approximately(seconds, 0.2f)) return wait02s;
            if (Mathf.Approximately(seconds, 0.5f)) return wait05s;
            if (Mathf.Approximately(seconds, 1f)) return wait1s;
            if (Mathf.Approximately(seconds, 3f)) return wait3s;
            if (Mathf.Approximately(seconds, 10f)) return wait10s;
            
            // 燃烧弹间隔使用动态缓存
            if (!Mathf.Approximately(cachedGrenadeIntervalValue, seconds))
            {
                cachedGrenadeIntervalValue = seconds;
                cachedGrenadeInterval = new WaitForSeconds(seconds);
            }
            return cachedGrenadeInterval;
        }
        
        /// <summary>
        /// 投掷燃烧弹 - 始终投向玩家脚下
        /// </summary>
        private void ThrowIncendiaryGrenade()
        {
            try
            {
                if (bossCharacter == null) return;
                
                // 更新玩家引用
                if (playerCharacter == null)
                {
                    try { playerCharacter = CharacterMainControl.Main; } catch { }
                }
                
                if (playerCharacter == null) return;
                
                // Mode E 同阵营不攻击玩家
                if (IsPlayerAlly()) return;
                
                // Mode E 脱战距离检查
                if (IsPlayerOutOfLeashRange()) return;
                
                // 始终投向玩家脚下（不再区分血量阶段）
                Vector3 targetPos = playerCharacter.transform.position;
                
                // 创建燃烧弹
                CreateIncendiaryGrenade(targetPos);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 投掷燃烧弹失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 创建燃烧弹
        /// </summary>
        private void CreateIncendiaryGrenade(Vector3 targetPos)
        {
            try
            {
                Vector3 startPos = bossCharacter.transform.position + Vector3.up * 1.5f;
                
                // 查找燃烧弹预制体
                Grenade grenadePrefab = FindIncendiaryGrenadePrefab();
                
                if (grenadePrefab != null)
                {
                    // 使用预制体创建
                    Grenade grenade = UnityEngine.Object.Instantiate(grenadePrefab, startPos, Quaternion.identity);
                    
                    // 设置伤害信息
                    DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                    dmgInfo.damageValue = 30f;
                    dmgInfo.AddElementFactor(ElementTypes.fire, 1f);
                    grenade.damageInfo = dmgInfo;
                    
                    // 计算投掷速度
                    Vector3 velocity = CalculateThrowVelocity(startPos, targetPos, 8f);
                    grenade.Launch(startPos, velocity, bossCharacter, false);
                    
                    ModBehaviour.DevLog("[DragonDescendant] 投掷燃烧弹到: " + targetPos);
                }
                else
                {
                    // 没有预制体，直接在目标位置创建火焰爆炸
                    StartCoroutine(DelayedFireExplosion(targetPos, 1f));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 创建燃烧弹失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 查找燃烧弹预制体（使用缓存）
        /// </summary>
        private Grenade FindIncendiaryGrenadePrefab()
        {
            // 直接返回缓存的预制体（已在Initialize时预缓存）
            return cachedGrenadePrefab;
        }
        
        /// <summary>
        /// 计算投掷速度
        /// </summary>
        private Vector3 CalculateThrowVelocity(Vector3 start, Vector3 target, float verticalSpeed)
        {
            float gravity = Physics.gravity.magnitude;
            if (gravity <= 0f) gravity = 9.81f;
            
            float timeUp = verticalSpeed / gravity;
            float heightDiff = start.y - target.y;
            float timeDown = Mathf.Sqrt(2f * Mathf.Abs(timeUp * verticalSpeed * 0.5f + heightDiff) / gravity);
            float totalTime = timeUp + timeDown;
            
            if (totalTime <= 0f) totalTime = 0.001f;
            
            Vector3 horizontalStart = new Vector3(start.x, 0f, start.z);
            Vector3 horizontalTarget = new Vector3(target.x, 0f, target.z);
            float horizontalDistance = Vector3.Distance(horizontalStart, horizontalTarget);
            
            Vector3 horizontalDir = (horizontalTarget - horizontalStart).normalized;
            float horizontalSpeed = horizontalDistance / totalTime;
            
            return horizontalDir * horizontalSpeed + Vector3.up * verticalSpeed;
        }
        
        /// <summary>
        /// 延迟火焰爆炸（作为燃烧弹的后备方案）
        /// [性能优化] 使用缓存的WaitForSeconds
        /// </summary>
        private IEnumerator DelayedFireExplosion(Vector3 position, float delay)
        {
            yield return GetCachedWaitForSeconds(delay);
            
            try
            {
                if (LevelManager.Instance != null && LevelManager.Instance.ExplosionManager != null)
                {
                    DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                    dmgInfo.damageValue = 30f;
                    dmgInfo.AddElementFactor(ElementTypes.fire, 1f);
                    
                    LevelManager.Instance.ExplosionManager.CreateExplosion(
                        position, 
                        2.5f, 
                        dmgInfo, 
                        ExplosionFxTypes.fire, 
                        0.5f, 
                        true
                    );
                }
            }
            catch { }
        }

        
        // ========== 复活机制 ==========
        
        /// <summary>
        /// Boss受伤回调
        /// </summary>
        private void OnBossHurt(DamageInfo damageInfo)
        {
            // 如果使用原版无敌机制，这里不需要手动恢复血量
            // 原版Health.Hurt()会在invincible=true时直接返回false
            // 但我们仍保留isInvulnerable标记用于额外保护
            if (isInvulnerable && bossHealth != null)
            {
                // 双重保护：如果原版无敌机制失效，手动恢复血量
                if (!bossHealth.Invincible)
                {
                    bossHealth.SetHealth(bossHealth.CurrentHealth + damageInfo.finalDamage);
                }
                return;
            }
            
            // 狂暴状态下检测冰属性伤害
            if (isEnraged && !isIceSlowed)
            {
                CheckIceDamage(damageInfo);
            }
            
            // 检查是否需要触发复活
            if (!hasResurrected && !isResurrecting && bossHealth != null)
            {
                if (bossHealth.CurrentHealth <= 1f)
                {
                    // 触发复活
                    StartCoroutine(ResurrectionSequence());
                }
            }
        }
        
        /// <summary>
        /// 检测并累加冰属性伤害
        /// [性能优化] 移除字符串拼接日志，减少GC分配
        /// </summary>
        private void CheckIceDamage(DamageInfo damageInfo)
        {
            try
            {
                if (damageInfo.elementFactors == null || damageInfo.elementFactors.Count == 0) return;
                if (bossHealth == null) return;
                
                float totalFinalDamage = damageInfo.finalDamage;
                if (totalFinalDamage <= 0f) return;
                
                // 计算冰属性伤害占比
                float iceFactor = 0f;
                float totalFactor = 0f;
                var factors = damageInfo.elementFactors;
                int count = factors.Count;
                
                for (int i = 0; i < count; i++)
                {
                    var ef = factors[i];
                    if (ef.factor > 0f)
                    {
                        totalFactor += ef.factor;
                        if (ef.elementType == ElementTypes.ice)
                        {
                            iceFactor += ef.factor;
                        }
                    }
                }
                
                // 没有冰属性伤害则跳过
                if (iceFactor <= 0f || totalFactor <= 0f) return;
                
                // 计算实际冰属性伤害
                float iceRatio = iceFactor / totalFactor;
                float actualIceDamage = totalFinalDamage * iceRatio;
                
                // 累加冰属性伤害
                accumulatedIceDamage += actualIceDamage;
                
                // [性能优化] 只在达到阈值时输出日志，避免频繁字符串拼接
                float threshold = bossHealth.MaxHealth * 0.1f;
                if (accumulatedIceDamage >= threshold)
                {
                    ModBehaviour.DevLog("[DragonDescendant] 冰属性伤害达到阈值，触发减速");
                    TriggerIceSlowdown();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [ERROR] CheckIceDamage 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取当前血量百分比
        /// </summary>
        private float GetHealthPercent()
        {
            if (bossHealth == null) return 1f;
            return bossHealth.CurrentHealth / bossHealth.MaxHealth;
        }
        
        /// <summary>
        /// 复活序列
        /// </summary>
        private IEnumerator ResurrectionSequence()
        {
            if (hasResurrected || isResurrecting) yield break;
            
            isResurrecting = true;
            isInvulnerable = true;
            
            ModBehaviour.DevLog("[DragonDescendant] 开始复活序列");
            
            // 使用原版API设置无敌状态
            if (bossHealth != null)
            {
                bossHealth.SetInvincible(true);
                bossHealth.SetHealth(1f);
            }
            
            // 暂停AI行动（对话期间Boss不动）
            PauseAI();
            
            // 显示对话气泡
            yield return StartCoroutine(ShowResurrectionDialogue());
            
            // 对话结束后，向八个方向投掷燃烧弹
            ThrowIncendiaryGrenadesInEightDirections();
            
            // 恢复AI行动
            ResumeAI();
            
            // 恢复血量到50%
            if (bossHealth != null)
            {
                float targetHealth = bossHealth.MaxHealth * DragonDescendantConfig.ResurrectionHealthPercent;
                bossHealth.SetHealth(targetHealth);
                ModBehaviour.DevLog("[DragonDescendant] 血量恢复到: " + targetHealth);
                
                // 关闭原版无敌状态
                bossHealth.SetInvincible(false);
            }
            
            // 结束无敌
            isInvulnerable = false;
            isResurrecting = false;
            hasResurrected = true;
            
            // 播放第二阶段音效
            PlaySecondPhaseSound();
            
            // 进入狂暴状态
            EnterEnragedState();
        }
        
        /// <summary>
        /// AI是否被暂停
        /// </summary>
        private bool aiPaused => aiController != null && aiController.IsPaused;

        /// <summary>
        /// 暂停AI行动（复活对话期间）
        /// 使用统一的AI控制辅助类
        /// </summary>
        private void PauseAI()
        {
            // 确保AI控制器已初始化
            if (aiController == null && bossCharacter != null)
            {
                aiController = new BossAIController(bossCharacter, "DragonDescendant");
            }

            if (aiController != null)
            {
                aiController.Pause();
            }
        }

        /// <summary>
        /// 恢复AI行动
        /// </summary>
        private void ResumeAI()
        {
            if (aiController != null)
            {
                aiController.Resume(playerCharacter);
            }
        }
        
        /// <summary>
        /// 向八个方向投掷燃烧弹（进入第二阶段时）
        /// 投掷距离为Boss到玩家的距离
        /// </summary>
        private void ThrowIncendiaryGrenadesInEightDirections()
        {
            try
            {
                if (bossCharacter == null) return;
                
                // 更新玩家引用
                if (playerCharacter == null)
                {
                    try { playerCharacter = CharacterMainControl.Main; } catch { }
                }
                
                Vector3 bossPos = bossCharacter.transform.position;
                
                // 投掷距离为Boss到玩家的距离
                float throwDistance = 5f; // 默认距离
                if (playerCharacter != null)
                {
                    throwDistance = Vector3.Distance(bossPos, playerCharacter.transform.position);
                }
                
                // 八个方向：N, NE, E, SE, S, SW, W, NW
                Vector3[] directions = new Vector3[]
                {
                    Vector3.forward,                                    // N
                    (Vector3.forward + Vector3.right).normalized,       // NE
                    Vector3.right,                                      // E
                    (Vector3.back + Vector3.right).normalized,          // SE
                    Vector3.back,                                       // S
                    (Vector3.back + Vector3.left).normalized,           // SW
                    Vector3.left,                                       // W
                    (Vector3.forward + Vector3.left).normalized         // NW
                };
                
                ModBehaviour.DevLog("[DragonDescendant] 向八个方向投掷燃烧弹");
                
                foreach (Vector3 dir in directions)
                {
                    Vector3 targetPos = bossPos + dir * throwDistance;
                    CreateIncendiaryGrenade(targetPos);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 八方向燃烧弹投掷失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 显示复活对话气泡（简化版：只显示两次）
        /// </summary>
        private IEnumerator ShowResurrectionDialogue()
        {
            string dialogue = DragonDescendantConfig.ResurrectionDialogue;
            
            // 第一次：显示 "我..."（悬念效果）
            ShowDialogueBubble("我...", 2f);
            yield return wait1s;
            
            // 第二次：显示完整对话
            ShowDialogueBubble(dialogue, 2f);
            yield return wait1s;
        }
        
        /// <summary>
        /// 显示对话气泡（通用方法，消除重复代码）
        /// </summary>
        /// <param name="text">对话文本</param>
        /// <param name="duration">显示时长</param>
        private void ShowDialogueBubble(string text, float duration)
        {
            try
            {
                if (bossCharacter == null) return;
                
                float yOffset = DragonDescendantConfig.DialogueBubbleYOffset;
                try
                {
                    if (bossCharacter.characterModel != null && bossCharacter.characterModel.HelmatSocket != null)
                    {
                        yOffset = Vector3.Distance(bossCharacter.transform.position, 
                            bossCharacter.characterModel.HelmatSocket.position) + 0.5f;
                    }
                }
                catch { }
                
                // 使用DialogueBubblesManager显示
                UniTaskExtensions.Forget(DialogueBubblesManager.Show(
                    text, 
                    bossCharacter.transform, 
                    yOffset, 
                    false, 
                    false, 
                    -1f, 
                    duration
                ));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 显示对话气泡失败: " + e.Message);
            }
        }
        
        // ========== 狂暴状态 ==========

        /// <summary>
        /// AI控制辅助类（统一的暂停/恢复接口）
        /// </summary>
        private BossAIController aiController;
        
        /// <summary>
        /// 追逐更新间隔（与原版TraceTarget一致：0.15秒）
        /// </summary>
        #pragma warning disable CS0414
        private float chaseUpdateInterval = 0.15f;
        #pragma warning restore CS0414

        /// <summary>
        /// 进入狂暴状态
        /// </summary>
        private void EnterEnragedState()
        {
            if (isEnraged) return;

            isEnraged = true;
            ModBehaviour.DevLog("[DragonDescendant] 进入狂暴状态");

            // 初始化AI控制辅助类
            if (bossCharacter != null && aiController == null)
            {
                aiController = new BossAIController(bossCharacter, "DragonDescendant");
            }

            // 应用二阶段伤害倍率
            ApplyPhase2DamageMultiplier();
            
            // 禁用Boss自身的射击行为（二阶段使用直接生成子弹）
            DisableShooting();
            
            // 扩大套装发光范围
            ExpandGlowRadius();
            
            // 不再在这里应用速度加成，由ChasePlayerCoroutine动态控制
            // ApplyChaseSpeedBoost(); // 已移除
            
            // 设置碰撞检测
            SetupCollisionDetection();
            
            // 设置跑步状态（让Boss跑步追逐玩家）
            if (bossCharacter != null)
            {
                bossCharacter.SetRunInput(true);
            }
            
            // 启动二阶段行为循环协程
            chaseCoroutine = StartCoroutine(ChasePlayerCoroutine());
        }
        
        /// <summary>
        /// 应用二阶段伤害倍率
        /// </summary>
        private void ApplyPhase2DamageMultiplier()
        {
            try
            {
                if (bossCharacter == null || bossCharacter.CharacterItem == null) return;
                
                var item = bossCharacter.CharacterItem;
                
                // 设置枪械伤害倍率
                var gunDmgStat = item.GetStat("GunDamageMultiplier");
                if (gunDmgStat != null)
                {
                    gunDmgStat.BaseValue = DragonDescendantConfig.Phase2DamageMultiplier;
                }
                
                // 设置近战伤害倍率
                var meleeDmgStat = item.GetStat("MeleeDamageMultiplier");
                if (meleeDmgStat != null)
                {
                    meleeDmgStat.BaseValue = DragonDescendantConfig.Phase2DamageMultiplier;
                }
                
                ModBehaviour.DevLog("[DragonDescendant] 二阶段伤害倍率已应用: " + DragonDescendantConfig.Phase2DamageMultiplier);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 应用二阶段伤害倍率失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 禁用射击行为
        /// 完全停止Boss的射击能力
        /// </summary>
        private void DisableShooting()
        {
            try
            {
                if (bossCharacter == null) return;

                // 使用原版API收起武器
                var ai = aiController?.GetAI();
                if (ai != null)
                {
                    ai.PutBackWeapon();
                    ai.defaultWeaponOut = false;
                }
                
                // 停止射击输入
                bossCharacter.Trigger(false, false, false);
                
                ModBehaviour.DevLog("[DragonDescendant] 已收起武器并停止射击");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 禁用射击失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 应用追逐速度加成
        /// </summary>
        private void ApplyChaseSpeedBoost()
        {
            try
            {
                if (bossCharacter == null || bossCharacter.CharacterItem == null) return;
                
                var speedStat = bossCharacter.CharacterItem.GetStat("MoveSpeed");
                if (speedStat != null)
                {
                    speedStat.BaseValue *= DragonDescendantConfig.ChaseSpeedMultiplier;
                    ModBehaviour.DevLog("[DragonDescendant] 移动速度提升到: " + speedStat.Value);
                }
                
                // 设置高Moveability值，免疫子弹减速效果
                var moveabilityStat = bossCharacter.CharacterItem.GetStat("Moveability".GetHashCode());
                if (moveabilityStat != null)
                {
                    moveabilityStat.BaseValue = 10f; // 设置很高的基础值，即使被减速也不会低于1
                    ModBehaviour.DevLog("[DragonDescendant] 设置Moveability免疫减速");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 应用速度加成失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 扩大套装发光范围
        /// </summary>
        private void ExpandGlowRadius()
        {
            try
            {
                // 查找并扩大龙套装的发光效果
                float newRadius = DragonDescendantConfig.EnragedGlowRadius;
                
                // 尝试找到DarkRoomFade组件并调整范围
                var darkRoomFade = bossCharacter.GetComponentInChildren<DarkRoomFade>();
                if (darkRoomFade != null)
                {
                    // 通过反射设置范围
                    var rangeField = typeof(DarkRoomFade).GetField("range", 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (rangeField != null)
                    {
                        rangeField.SetValue(darkRoomFade, newRadius);
                    }
                }
                
                ModBehaviour.DevLog("[DragonDescendant] 扩大发光范围到: " + newRadius);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 扩大发光范围失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 追逐玩家协程 - 二阶段行为循环
        /// 循环：停止射击 -> 冲刺 -> 扇形射击 -> 重复
        /// </summary>
        private IEnumerator ChasePlayerCoroutine()
        {
            ModBehaviour.DevLog("[DragonDescendant] 开始二阶段行为循环");
            
            while (isEnraged && bossCharacter != null)
            {
                // 更新玩家引用
                if (playerCharacter == null)
                {
                    try { playerCharacter = CharacterMainControl.Main; } catch { }
                }
                
                if (playerCharacter == null)
                {
                    // [性能优化] 使用缓存的WaitForSeconds
                    yield return wait05s;
                    continue;
                }
                
                // Mode E 同阵营不追逐玩家，等待直到阵营变化
                if (IsPlayerAlly())
                {
                    yield return wait05s;
                    continue;
                }
                
                // Mode E 脱战距离检查：超出距离时跳过本轮攻击循环
                if (IsPlayerOutOfLeashRange())
                {
                    // 清除AI仇恨，让龙裔自然寻找其他敌人
                    var leashAi = aiController?.GetAI();
                    if (leashAi != null)
                    {
                        leashAi.searchedEnemy = null;
                        leashAi.noticed = false;
                    }
                    yield return wait05s;
                    continue;
                }
                
                // ========== 阶段1：停止并射击10发直线子弹 ==========
                ModBehaviour.DevLog("[DragonDescendant] 阶段1：停止射击");
                SetMoveability(1f);
                StopMovement();
                
                // 获取朝向玩家的方向
                Vector3 dirToPlayer = GetDirectionToPlayer();
                
                // 射击10发直线子弹
                yield return StartCoroutine(FireLinearBullets(dirToPlayer, 10, 0.1f));
                
                // ========== 阶段2：高速冲刺0.5秒 ==========
                ModBehaviour.DevLog("[DragonDescendant] 阶段2：高速冲刺");
                SetMoveability(10f);
                
                float chargeTime = 0f;
                while (chargeTime < 0.5f && isEnraged && bossCharacter != null)
                {
                    // 更新玩家引用
                    if (playerCharacter == null)
                    {
                        try { playerCharacter = CharacterMainControl.Main; } catch { }
                    }

                    var ai = aiController?.GetAI();
                    if (playerCharacter != null && ai != null)
                    {
                        // Mode E 同阵营时不朝玩家冲刺
                        if (!IsPlayerAlly())
                        {
                            // 朝玩家方向冲刺
                            Vector3 targetPos = playerCharacter.transform.position;
                            ai.MoveToPos(targetPos);

                            if (playerCharacter.mainDamageReceiver != null)
                            {
                                ai.searchedEnemy = playerCharacter.mainDamageReceiver;
                                ai.SetTarget(playerCharacter.mainDamageReceiver.transform);
                                ai.noticed = true;
                            }

                            bossCharacter.SetRunInput(true);
                        }
                    }
                    
                    chargeTime += Time.deltaTime;
                    yield return null;
                }
                
                // ========== 阶段3：停止并射击扇形子弹3秒 ==========
                ModBehaviour.DevLog("[DragonDescendant] 阶段3：扇形射击");
                SetMoveability(1f);
                StopMovement();
                
                // 更新朝向玩家的方向
                dirToPlayer = GetDirectionToPlayer();
                
                // 射击扇形子弹3秒（30发，来回扫射，60度扇形角度）
                yield return StartCoroutine(FireFanBulletsSweep(dirToPlayer, 30, 3f, 60f));
                
                // [性能优化] 使用缓存的WaitForSeconds
                yield return wait02s;
            }
        }
        
        /// <summary>
        /// 设置Moveability值
        /// </summary>
        private void SetMoveability(float value)
        {
            try
            {
                if (bossCharacter == null || bossCharacter.CharacterItem == null) return;
                
                var moveabilityStat = bossCharacter.CharacterItem.GetStat("Moveability".GetHashCode());
                if (moveabilityStat != null)
                {
                    moveabilityStat.BaseValue = value;
                    ModBehaviour.DevLog("[DragonDescendant] Moveability设为: " + value);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 设置Moveability失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 停止移动
        /// </summary>
        private void StopMovement()
        {
            try
            {
                if (bossCharacter != null)
                {
                    bossCharacter.SetMoveInput(Vector2.zero);
                    bossCharacter.SetRunInput(false);
                }
                if (aiController?.GetAI() != null)
                {
                    aiController?.GetAI().StopMove();
                }
            }
            catch { }
        }
        
        /// <summary>
        /// 获取朝向玩家的方向（水平面）
        /// </summary>
        private Vector3 GetDirectionToPlayer()
        {
            if (bossCharacter == null || playerCharacter == null)
                return Vector3.forward;
            
            Vector3 dir = playerCharacter.transform.position - bossCharacter.transform.position;
            dir.y = 0f;
            return dir.normalized;
        }
        
        /// <summary>
        /// 发射直线子弹
        /// 直接使用BulletPool生成子弹
        /// </summary>
        /// <param name="direction">发射方向</param>
        /// <param name="count">子弹数量</param>
        /// <param name="interval">每发间隔（秒）</param>
        private IEnumerator FireLinearBullets(Vector3 direction, int count, float interval)
        {
            ModBehaviour.DevLog("[DragonDescendant] FireLinearBullets开始: count=" + count + ", isEnraged=" + isEnraged);
            
            // [性能优化] 使用缓存的WaitForSeconds
            WaitForSeconds waitInterval = GetCachedWaitForSeconds(interval);
            
            for (int i = 0; i < count; i++)
            {
                if (!isEnraged || bossCharacter == null)
                {
                    ModBehaviour.DevLog("[DragonDescendant] FireLinearBullets中断: isEnraged=" + isEnraged);
                    yield break;
                }
                
                // 更新方向（追踪玩家）
                direction = GetDirectionToPlayer();
                
                // 直接生成子弹
                SpawnBulletDirect(direction);
                
                yield return waitInterval;
            }
            
            ModBehaviour.DevLog("[DragonDescendant] FireLinearBullets完成");
        }
        
        /// <summary>
        /// 发射扇形子弹
        /// 直接使用BulletPool生成子弹，实时追踪玩家方向
        /// </summary>
        /// <param name="baseDirection">基础方向（初始朝向玩家，会实时更新）</param>
        /// <param name="bulletCount">子弹总数</param>
        /// <param name="duration">持续时间（秒）</param>
        /// <param name="totalAngle">扇形总角度</param>
        private IEnumerator FireFanBullets(Vector3 baseDirection, int bulletCount, float duration, float totalAngle)
        {
            if (bulletCount <= 0) yield break;
            
            // 扇形子弹间隔更小，射击更快（1秒内射完30发）
            float interval = 1f / bulletCount;
            float angleStep = totalAngle / (bulletCount - 1);
            
            // [性能优化] 预计算WaitForSeconds
            WaitForSeconds waitInterval = GetCachedWaitForSeconds(interval);
            
            for (int i = 0; i < bulletCount; i++)
            {
                if (!isEnraged || bossCharacter == null) yield break;
                
                // 实时更新基础方向（追踪玩家）
                baseDirection = GetDirectionToPlayer();
                
                // 计算当前子弹的偏转角度
                // 从 -totalAngle/2 到 +totalAngle/2
                float angle = -totalAngle * 0.5f + angleStep * i;
                
                // 使用Quaternion旋转基础方向
                Vector3 bulletDir = Quaternion.Euler(0f, angle, 0f) * baseDirection;
                
                // 直接生成子弹（不通过Boss的枪）
                SpawnBulletDirect(bulletDir);
                
                yield return waitInterval;
            }
        }
        
        /// <summary>
        /// 发射扇形子弹（来回扫射版本）
        /// 子弹从左到右再从右到左来回扫射，给玩家躲避空间
        /// </summary>
        /// <param name="baseDirection">基础方向（朝向玩家）</param>
        /// <param name="bulletCount">子弹总数</param>
        /// <param name="duration">持续时间（秒）</param>
        /// <param name="totalAngle">扇形总角度</param>
        private IEnumerator FireFanBulletsSweep(Vector3 baseDirection, int bulletCount, float duration, float totalAngle)
        {
            ModBehaviour.DevLog("[DragonDescendant] FireFanBulletsSweep开始: bulletCount=" + bulletCount + 
                ", duration=" + duration + ", isEnraged=" + isEnraged);
            
            if (bulletCount <= 0) yield break;
            
            // 计算每发子弹的间隔时间（3秒24发）
            float interval = duration / bulletCount;
            
            // [性能优化] 预计算WaitForSeconds
            WaitForSeconds waitInterval = GetCachedWaitForSeconds(interval);
            
            for (int i = 0; i < bulletCount; i++)
            {
                if (!isEnraged || bossCharacter == null)
                {
                    ModBehaviour.DevLog("[DragonDescendant] FireFanBulletsSweep中断: isEnraged=" + isEnraged + 
                        ", bossCharacter=" + (bossCharacter != null));
                    yield break;
                }
                
                // 实时更新基础方向（追踪玩家）
                baseDirection = GetDirectionToPlayer();
                
                // 使用正弦函数计算当前角度，实现来回扫射
                // t从0到1，sin(t * PI)从0到1再到0，实现一个来回
                float t = (float)i / (bulletCount - 1);
                float sweepProgress = Mathf.Sin(t * Mathf.PI); // 0 -> 1 -> 0
                
                // 角度从 -totalAngle/2 到 +totalAngle/2 再回到 -totalAngle/2
                float angle = -totalAngle * 0.5f + totalAngle * sweepProgress;
                
                // 使用Quaternion旋转基础方向
                Vector3 bulletDir = Quaternion.Euler(0f, angle, 0f) * baseDirection;
                
                // 直接生成子弹
                SpawnBulletDirect(bulletDir);
                
                yield return waitInterval;
            }
            
            ModBehaviour.DevLog("[DragonDescendant] FireFanBulletsSweep完成");
        }
        
        /// <summary>
        /// 直接生成子弹（使用BulletPool）
        /// 参考原版ItemAgent_Gun.ShootOneBullet
        /// 二阶段使用原始武器完整属性（子弹、射速、音效等）
        /// </summary>
        private void SpawnBulletDirect(Vector3 direction)
        {
            try
            {
                if (bossCharacter == null) return;
                if (LevelManager.Instance == null || LevelManager.Instance.BulletPool == null)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] BulletPool不可用");
                    return;
                }
                
                // 使用原始武器属性
                float bulletSpeed = 30f;
                float bulletDistance = 50f;
                float damage = 15f;
                Projectile bulletPrefab = null;
                string shootKey = "Default";
                GameObject muzzleFxPrefab = null;
                
                // 优先使用原始武器数据
                if (originalWeaponData != null)
                {
                    bulletPrefab = originalWeaponData.bulletPrefab;
                    bulletSpeed = originalWeaponData.bulletSpeed > 0 ? originalWeaponData.bulletSpeed : 30f;
                    bulletDistance = originalWeaponData.bulletDistance > 0 ? originalWeaponData.bulletDistance : 50f;
                    damage = originalWeaponData.damage > 0 ? originalWeaponData.damage : 15f;
                    shootKey = !string.IsNullOrEmpty(originalWeaponData.shootKey) ? originalWeaponData.shootKey : "Default";
                    muzzleFxPrefab = originalWeaponData.muzzleFxPrefab;
                }
                
                // 回退到缓存的子弹预制体
                if (bulletPrefab == null)
                {
                    bulletPrefab = cachedBulletPrefab;
                }
                
                // 最后尝试从缓存的子弹预制体获取
                if (bulletPrefab == null && cachedBulletPrefab != null)
                {
                    bulletPrefab = cachedBulletPrefab;
                }
                    
                if (bulletPrefab == null)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] 无法获取任何子弹预制体");
                    return;
                }
                
                // 计算发射位置（Boss胸口位置）
                Vector3 muzzlePos = bossCharacter.transform.position + Vector3.up * 1.2f;
                
                // 播放开枪音效
                PlayShootSound(shootKey);
                
                // 生成枪口特效
                SpawnMuzzleFlash(muzzlePos, direction, muzzleFxPrefab);
                
                // 从BulletPool获取子弹
                Projectile bullet = LevelManager.Instance.BulletPool.GetABullet(bulletPrefab);
                if (bullet == null)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] BulletPool返回null");
                    return;
                }
                
                // 设置子弹位置和方向
                bullet.transform.position = muzzlePos;
                bullet.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                
                // 创建ProjectileContext
                ProjectileContext ctx = default(ProjectileContext);
                ctx.direction = direction.normalized;
                ctx.speed = bulletSpeed;
                ctx.distance = bulletDistance;
                ctx.halfDamageDistance = bulletDistance * 0.5f;
                ctx.damage = damage;
                ctx.penetrate = 0;
                ctx.critRate = 0f;
                ctx.critDamageFactor = DragonDescendantConfig.Phase2CritDamageFactor;
                ctx.armorPiercing = 0f;
                ctx.armorBreak = 0f;
                ctx.fromCharacter = bossCharacter;
                ctx.team = bossCharacter.Team;
                ctx.element_Fire = 1f; // 火属性子弹
                ctx.firstFrameCheck = false;
                
                // 使用Init方法初始化子弹
                bullet.Init(ctx);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 直接生成子弹失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 播放开枪音效（使用缓存的反射）
        /// </summary>
        private void PlayShootSound(string shootKey)
        {
            try
            {
                if (bossCharacter == null || string.IsNullOrEmpty(shootKey)) return;
                
                // 缓存AudioManager反射（只执行一次）
                if (!audioManagerReflectionCached)
                {
                    CacheAudioManagerReflection();
                }
                
                if (cachedAudioPostMethod == null) return;
                
                // 提取纯 shootKey（去除可能存在的路径前缀）
                string pureKey = shootKey;
                
                // 如果包含完整路径，提取最后的 key 部分
                int lastSlash = shootKey.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash < shootKey.Length - 1)
                {
                    pureKey = shootKey.Substring(lastSlash + 1);
                }
                
                // 去除可能的 event: 前缀
                if (pureKey.StartsWith("event:"))
                {
                    pureKey = pureKey.Substring(6);
                    if (pureKey.Length > 0 && pureKey[0] == '/')
                    {
                        pureKey = pureKey.Substring(1);
                    }
                }
                
                // 构建音效路径（使用原版格式，不带 event:/ 前缀）
                string eventName = "SFX/Combat/Gun/Shoot/" + pureKey.ToLower();
                
                // 使用缓存的方法调用
                cachedAudioPostMethod.Invoke(null, new object[] { eventName, bossCharacter.gameObject });
            }
            catch (Exception e)
            {
                // 音效播放失败不影响游戏逻辑
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 播放开枪音效失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 缓存AudioManager的反射信息（只执行一次）
        /// </summary>
        private static void CacheAudioManagerReflection()
        {
            if (audioManagerReflectionCached) return;
            
            try
            {
                cachedAudioManagerType = System.Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                if (cachedAudioManagerType != null)
                {
                    cachedAudioPostMethod = cachedAudioManagerType.GetMethod("Post", 
                        new System.Type[] { typeof(string), typeof(GameObject) });
                    cachedAudioPostCustomSFXMethod = cachedAudioManagerType.GetMethod("PostCustomSFX", 
                        BindingFlags.Public | BindingFlags.Static);
                }
            }
            catch { }
            
            audioManagerReflectionCached = true;
        }
        
        /// <summary>
        /// 生成枪口特效
        /// </summary>
        private void SpawnMuzzleFlash(Vector3 position, Vector3 direction, GameObject muzzleFxPrefab)
        {
            try
            {
                if (muzzleFxPrefab == null) return;
                
                // 实例化枪口特效
                GameObject fx = UnityEngine.Object.Instantiate(muzzleFxPrefab, position, Quaternion.LookRotation(direction));
                
                // 自动销毁（2秒后）
                UnityEngine.Object.Destroy(fx, 2f);
            }
            catch (Exception e)
            {
                // 特效生成失败不影响游戏逻辑
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 生成枪口特效失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 设置Boss的瞄准方向
        /// 参考原版RotateAim.cs
        /// </summary>
        private void SetAimDirection(Vector3 direction)
        {
            try
            {
                if (bossCharacter == null) return;
                
                // 设置瞄准点为Boss位置 + 方向 * 远距离
                Vector3 aimPoint = bossCharacter.transform.position + direction * 100f;
                bossCharacter.SetAimPoint(aimPoint);
            }
            catch { }
        }
        
        /// <summary>
        /// 发射一发子弹
        /// 使用原版的Trigger方法触发射击
        /// </summary>
        private void FireBullet(Vector3 direction)
        {
            try
            {
                if (bossCharacter == null) return;

                // 确保武器已拿出
                var ai = aiController?.GetAI();
                if (ai != null)
                {
                    ai.defaultWeaponOut = true;
                }

                // 设置瞄准方向
                SetAimDirection(direction);

                // 触发射击（triggerInput=true, triggerThisFrame=true）
                bossCharacter.Trigger(true, true, false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 发射子弹失败: " + e.Message);
            }
        }

        
        // ========== 碰撞击退 ==========
        
        /// <summary>
        /// 设置碰撞检测
        /// </summary>
        private void SetupCollisionDetection()
        {
            try
            {
                // 添加触发器用于检测与玩家的碰撞
                if (collisionTrigger == null)
                {
                    GameObject triggerObj = new GameObject("DragonDescendant_CollisionTrigger");
                    triggerObj.transform.SetParent(bossCharacter.transform);
                    triggerObj.transform.localPosition = Vector3.up * 1f;
                    
                    collisionTrigger = triggerObj.AddComponent<SphereCollider>();
                    collisionTrigger.radius = DragonDescendantConfig.CollisionTriggerRadius;
                    collisionTrigger.isTrigger = true;
                    
                    // 添加碰撞检测脚本
                    var detector = triggerObj.AddComponent<DragonDescendantCollisionDetector>();
                    detector.Initialize(this);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 设置碰撞检测失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 更新追逐逻辑（在Update中调用，作为协程的补充）
        /// </summary>
        private void UpdateChase()
        {
            // 协程已经处理追逐逻辑，这里只做简单的状态维护
            var ai = aiController?.GetAI();
            if (!isEnraged || bossCharacter == null || ai == null) return;

            // Mode E 同阵营不追逐玩家
            if (IsPlayerAlly()) return;

            // Mode E 脱战距离检查：超出距离时清除仇恨，让AI自然寻敌
            if (IsPlayerOutOfLeashRange())
            {
                ai.searchedEnemy = null;
                ai.noticed = false;
                return;
            }

            // 确保AI保持追逐状态
            if (playerCharacter != null && playerCharacter.mainDamageReceiver != null)
            {
                ai.searchedEnemy = playerCharacter.mainDamageReceiver;
                ai.noticed = true;

                // 确保保持跑步状态
                bossCharacter.SetRunInput(true);
            }
        }
        
        /// <summary>
        /// 处理与玩家的碰撞
        /// </summary>
        public void OnCollisionWithPlayer(CharacterMainControl player)
        {
            if (!isEnraged) return;
            if (player == null) return;
            
            // Mode E 同阵营不对玩家造成碰撞伤害
            if (IsPlayerAlly()) return;
            
            // 检查冷却
            if (Time.time - lastCollisionTime < COLLISION_COOLDOWN) return;
            lastCollisionTime = Time.time;
            
            ModBehaviour.DevLog("[DragonDescendant] 撞击玩家");
            
            // 播放撞击音效
            PlayCollisionSound();
            
            // 应用击退
            ApplyKnockback(player);
            
            // 造成伤害
            ApplyCollisionDamage(player);
        }
        
        /// <summary>
        /// 播放第二阶段音效（进入狂暴状态时）
        /// </summary>
        private void PlaySecondPhaseSound()
        {
            PlayCustomSound("dragonToSecond.mp3", bossCharacter != null ? bossCharacter.gameObject : null);
        }
        
        /// <summary>
        /// 播放撞击音效
        /// </summary>
        private void PlayCollisionSound()
        {
            PlayCustomSound("hurt.mp3", playerCharacter != null ? playerCharacter.gameObject : null);
        }
        
        /// <summary>
        /// 播放自定义音效（通用方法，使用缓存的反射）
        /// </summary>
        /// <param name="fileName">音效文件名</param>
        /// <param name="target">播放目标GameObject</param>
        private void PlayCustomSound(string fileName, GameObject target)
        {
            try
            {
                // 缓存AudioManager反射（只执行一次）
                if (!audioManagerReflectionCached)
                {
                    CacheAudioManagerReflection();
                }
                
                if (cachedAudioPostCustomSFXMethod == null) return;
                
                // 获取mod基础路径
                string baseDir = null;
                try
                {
                    baseDir = System.IO.Path.GetDirectoryName(GetType().Assembly.Location);
                }
                catch { }
                
                if (string.IsNullOrEmpty(baseDir)) return;
                
                // 查找音效文件
                string filePath = null;
                string candidate1 = System.IO.Path.Combine(baseDir, "Assets", fileName);
                string candidate2 = System.IO.Path.Combine(baseDir, fileName);
                
                if (System.IO.File.Exists(candidate1))
                {
                    filePath = candidate1;
                }
                else if (System.IO.File.Exists(candidate2))
                {
                    filePath = candidate2;
                }
                
                if (string.IsNullOrEmpty(filePath))
                {
                    ModBehaviour.DevLog("[DragonDescendant] 未找到音效文件: " + fileName);
                    return;
                }
                
                // 使用缓存的方法调用
                object[] args = new object[] { filePath, target, false };
                cachedAudioPostCustomSFXMethod.Invoke(null, args);
                ModBehaviour.DevLog("[DragonDescendant] 播放音效: " + filePath);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 播放音效失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 应用击退效果
        /// 使用原版物理系统和角色控制
        /// </summary>
        private void ApplyKnockback(CharacterMainControl player)
        {
            try
            {
                if (player == null || bossCharacter == null) return;
                
                // 计算击退方向（从Boss指向玩家）
                Vector3 knockbackDir = (player.transform.position - bossCharacter.transform.position).normalized;
                knockbackDir.y = DragonDescendantConfig.KnockbackYComponent; // 稍微向上
                knockbackDir.Normalize();
                
                // 应用击退力
                float force = DragonDescendantConfig.KnockbackForce;
                
                // 优先使用原版的SetForceMoveVelocity方法（如果存在）
                try
                {
                    player.SetForceMoveVelocity(knockbackDir * force);
                    ModBehaviour.DevLog("[DragonDescendant] 使用SetForceMoveVelocity应用击退");
                }
                catch
                {
                    // 后备方案：尝试通过Rigidbody应用力
                    var rb = player.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.AddForce(knockbackDir * force, ForceMode.Impulse);
                        ModBehaviour.DevLog("[DragonDescendant] 使用Rigidbody应用击退");
                    }
                    else
                    {
                        // 最后方案：直接移动位置
                        player.transform.position += knockbackDir * (force * 0.1f);
                        ModBehaviour.DevLog("[DragonDescendant] 直接移动位置应用击退");
                    }
                }
                
                ModBehaviour.DevLog("[DragonDescendant] 应用击退: 方向=" + knockbackDir + ", 力=" + force);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 应用击退失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 应用碰撞伤害
        /// 使用原版伤害系统
        /// </summary>
        private void ApplyCollisionDamage(CharacterMainControl player)
        {
            try
            {
                if (player == null) return;
                
                // 创建伤害信息
                DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                dmgInfo.damageValue = DragonDescendantConfig.CollisionDamage;
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
                    ModBehaviour.DevLog("[DragonDescendant] 通过mainDamageReceiver造成碰撞伤害: " + DragonDescendantConfig.CollisionDamage);
                }
                // 后备：直接使用Health组件
                else if (player.Health != null)
                {
                    player.Health.Hurt(dmgInfo);
                    damageApplied = true;
                    ModBehaviour.DevLog("[DragonDescendant] 通过Health造成碰撞伤害: " + DragonDescendantConfig.CollisionDamage);
                }
                
                if (!damageApplied)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] 无法应用碰撞伤害 - 玩家没有有效的伤害接收器");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 应用碰撞伤害失败: " + e.Message);
            }
        }
        
        // ========== 冰属性减速机制 ==========
        
        /// <summary>
        /// 触发冰冻减速效果
        /// </summary>
        private void TriggerIceSlowdown()
        {
            if (isIceSlowed) return;
            
            isIceSlowed = true;
            ModBehaviour.DevLog("[DragonDescendant] 触发冰冻减速效果");
            
            try
            {
                // 将Moveability设为1f（减速）
                if (bossCharacter != null && bossCharacter.CharacterItem != null)
                {
                    var moveabilityStat = bossCharacter.CharacterItem.GetStat("Moveability".GetHashCode());
                    if (moveabilityStat != null)
                    {
                        moveabilityStat.BaseValue = 1f;
                        ModBehaviour.DevLog("[DragonDescendant] Moveability设为1f");
                    }
                }
                
                // 显示对话气泡
                ShowIceSlowdownDialogue();
                
                // 启动10秒后恢复的协程
                if (iceSlowdownCoroutine != null)
                {
                    StopCoroutine(iceSlowdownCoroutine);
                }
                iceSlowdownCoroutine = StartCoroutine(IceSlowdownRecoveryCoroutine());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [ERROR] TriggerIceSlowdown 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 显示冰冻减速对话气泡
        /// </summary>
        private void ShowIceSlowdownDialogue()
        {
            ShowDialogueBubble("此等极寒之力也被你征服了吗，可恶...", 3f);
            ModBehaviour.DevLog("[DragonDescendant] 显示冰冻减速对话");
        }
        
        /// <summary>
        /// 冰冻减速恢复协程（10秒后恢复）
        /// </summary>
        private IEnumerator IceSlowdownRecoveryCoroutine()
        {
            // [性能优化] 使用缓存的WaitForSeconds
            yield return wait10s;
            
            ModBehaviour.DevLog("[DragonDescendant] 冰冻减速效果结束");
            
            try
            {
                // 恢复Moveability为10f
                if (bossCharacter != null && bossCharacter.CharacterItem != null)
                {
                    var moveabilityStat = bossCharacter.CharacterItem.GetStat("Moveability".GetHashCode());
                    if (moveabilityStat != null)
                    {
                        moveabilityStat.BaseValue = 10f;
                        ModBehaviour.DevLog("[DragonDescendant] Moveability恢复为10f");
                    }
                }
                
                // 显示恢复对话气泡
                ShowIceRecoveryDialogue();
                
                // 重置累计冰伤
                accumulatedIceDamage = 0f;
                isIceSlowed = false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [ERROR] IceSlowdownRecoveryCoroutine 出错: " + e.Message);
                isIceSlowed = false;
            }
        }
        
        /// <summary>
        /// 显示冰冻恢复对话气泡
        /// </summary>
        private void ShowIceRecoveryDialogue()
        {
            ShowDialogueBubble("哈哈哈用完了吗？轮到我了！", 3f);
            ModBehaviour.DevLog("[DragonDescendant] 显示冰冻恢复对话");
        }
    }
    
    /// <summary>
    /// 碰撞检测器组件
    /// 用于检测Boss与玩家的碰撞
    /// [性能优化] 使用OnTriggerEnter+协程替代OnTriggerStay，减少物理帧开销
    /// </summary>
    public class DragonDescendantCollisionDetector : MonoBehaviour
    {
        private DragonDescendantAbilityController controller;
        
        /// <summary>
        /// 上次碰撞时间（用于冷却检查）
        /// </summary>
        private float lastCollisionCheckTime = 0f;
        
        /// <summary>
        /// 碰撞检查冷却时间（与controller中的COLLISION_COOLDOWN一致）
        /// </summary>
        private const float CHECK_COOLDOWN = 0.5f;
        
        /// <summary>
        /// 当前在触发器内的玩家
        /// </summary>
        private CharacterMainControl playerInTrigger = null;
        
        /// <summary>
        /// 持续碰撞检测协程
        /// </summary>
        private Coroutine stayCheckCoroutine = null;
        
        /// <summary>
        /// 缓存的WaitForSeconds
        /// </summary>
        private static readonly WaitForSeconds waitCheckInterval = new WaitForSeconds(CHECK_COOLDOWN);
        
        public void Initialize(DragonDescendantAbilityController ctrl)
        {
            controller = ctrl;
            
            // 设置碰撞层级（确保能检测到玩家）
            gameObject.layer = LayerMask.NameToLayer("Default");
        }
        
        private void OnTriggerEnter(Collider other)
        {
            CharacterMainControl character = GetPlayerFromCollider(other);
            if (character != null && character.IsMainCharacter)
            {
                playerInTrigger = character;
                
                // 立即检测一次
                TryTriggerCollision();
                
                // 启动持续检测协程
                if (stayCheckCoroutine == null)
                {
                    stayCheckCoroutine = StartCoroutine(StayCheckCoroutine());
                }
            }
        }
        
        private void OnTriggerExit(Collider other)
        {
            CharacterMainControl character = GetPlayerFromCollider(other);
            if (character != null && character == playerInTrigger)
            {
                playerInTrigger = null;
                
                // 停止持续检测协程
                if (stayCheckCoroutine != null)
                {
                    StopCoroutine(stayCheckCoroutine);
                    stayCheckCoroutine = null;
                }
            }
        }
        
        /// <summary>
        /// 持续碰撞检测协程（替代OnTriggerStay）
        /// </summary>
        private IEnumerator StayCheckCoroutine()
        {
            while (playerInTrigger != null)
            {
                yield return waitCheckInterval;
                TryTriggerCollision();
            }
            stayCheckCoroutine = null;
        }
        
        /// <summary>
        /// 尝试触发碰撞（带冷却检查）
        /// </summary>
        private void TryTriggerCollision()
        {
            if (controller == null || playerInTrigger == null) return;
            if (Time.time - lastCollisionCheckTime < CHECK_COOLDOWN) return;
            
            lastCollisionCheckTime = Time.time;
            controller.OnCollisionWithPlayer(playerInTrigger);
        }
        
        /// <summary>
        /// 从碰撞器获取玩家角色
        /// </summary>
        private CharacterMainControl GetPlayerFromCollider(Collider other)
        {
            if (other == null) return null;
            
            // 方式1：从碰撞器的父级查找
            CharacterMainControl character = other.GetComponentInParent<CharacterMainControl>();
            
            // 方式2：从碰撞器本身查找
            if (character == null)
            {
                character = other.GetComponent<CharacterMainControl>();
            }
            
            // 方式3：检查DamageReceiver
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
        
        private void OnDestroy()
        {
            if (stayCheckCoroutine != null)
            {
                StopCoroutine(stayCheckCoroutine);
                stayCheckCoroutine = null;
            }
            playerInTrigger = null;
            controller = null;
        }
    }
}
