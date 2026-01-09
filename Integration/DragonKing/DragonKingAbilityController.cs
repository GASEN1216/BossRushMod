// ============================================================================
// DragonKingAbilityController.cs - 龙王Boss能力控制器
// ============================================================================
// 模块说明：
//   管理龙王Boss的AI状态机、攻击序列和阶段转换
//   基于泰拉瑞亚光之女皇的AI框架设计
//   实现7种攻击技能和两阶段战斗机制
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;

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
        /// </summary>
        private List<GameObject> activeEffects = new List<GameObject>();
        
        /// <summary>
        /// 活跃的弹幕列表（用于清理）
        /// </summary>
        private List<GameObject> activeProjectiles = new List<GameObject>();
        
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
        
        /// <summary>
        /// 太阳舞期间Boss锁定位置（用于弹幕发射位置）
        /// </summary>
        private Vector3 sunDanceLockPosition;
        
        // ========== 性能优化：WaitForSeconds缓存 ==========
        
        private static readonly WaitForSeconds wait01s = new WaitForSeconds(0.1f);
        private static readonly WaitForSeconds wait02s = new WaitForSeconds(0.2f);
        private static readonly WaitForSeconds wait05s = new WaitForSeconds(0.5f);
        private static readonly WaitForSeconds wait1s = new WaitForSeconds(1f);
        private static readonly WaitForSeconds wait15s = new WaitForSeconds(1.5f);
        private static readonly WaitForSeconds wait2s = new WaitForSeconds(2f);
        
        // ========== 静态缓存 ==========
        
        /// <summary>
        /// 清理静态缓存
        /// </summary>
        public static void ClearStaticCache()
        {
            // 目前没有需要清理的静态缓存
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
            
            // 缓存武器子弹预制体（用于太阳舞弹幕）
            CacheWeaponBullet();
            
            // 启动攻击循环
            attackLoopCoroutine = StartCoroutine(AttackLoop());
            
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
                            ModBehaviour.DevLog("[DragonKing] 从手持武器缓存: 子弹=" + cachedWeaponBullet.name + ", 音效=" + cachedWeaponShootKey + ", 速度=" + cachedWeaponBulletSpeed);
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
                            ModBehaviour.DevLog("[DragonKing] 从武器Item缓存: 子弹=" + cachedWeaponBullet.name + ", 音效=" + cachedWeaponShootKey + ", 速度=" + cachedWeaponBulletSpeed);
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
                        ModBehaviour.DevLog("[DragonKing] 从主武器槽位缓存: 子弹=" + cachedWeaponBullet.name + ", 音效=" + cachedWeaponShootKey + ", 速度=" + cachedWeaponBulletSpeed);
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
                ModBehaviour.DevLog("[DragonKing] [WARNING] 缓存武器子弹失败: " + e.Message);
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
                ModBehaviour.DevLog("[DragonKing] [WARNING] 加载资源失败: " + e.Message);
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
                
                ModBehaviour.DevLog("[DragonKing] 碰撞检测器初始化完成，半径=" + DragonKingConfig.CollisionRadius);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKing] [WARNING] 初始化碰撞检测器失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 悬浮跟随循环协程
        /// 在非攻击状态时，Boss会持续跟随玩家保持在其上方偏移位置
        /// </summary>
        private IEnumerator HoverFollowLoop()
        {
            while (CurrentPhase != DragonKingPhase.Dead && bossCharacter != null)
            {
                // 只在非攻击状态时进行悬浮跟随
                if (!isAttacking && CurrentPhase != DragonKingPhase.Transitioning)
                {
                    UpdatePlayerReference();
                    if (playerCharacter != null)
                    {
                        // 计算目标悬浮位置（玩家上方偏移）
                        Vector3 playerPos = playerCharacter.transform.position;
                        float height = (DragonKingConfig.RepositionHeightMin + DragonKingConfig.RepositionHeightMax) * 0.5f;
                        float offsetAngle = DragonKingConfig.HoverOffsetAngle * hoverSide;
                        Vector3 horizontalOffset = Quaternion.Euler(0f, offsetAngle, 0f) * Vector3.forward * 2f;
                        Vector3 targetPos = playerPos + Vector3.up * height + horizontalOffset;
                        
                        // 平滑移动到目标位置
                        Vector3 currentPos = bossCharacter.transform.position;
                        float distance = Vector3.Distance(currentPos, targetPos);
                        
                        if (distance > 0.5f)
                        {
                            Vector3 newPos = Vector3.MoveTowards(currentPos, targetPos, DragonKingConfig.HoverFollowSpeed * Time.deltaTime);
                            bossCharacter.transform.position = newPos;
                        }
                        
                        // 始终面向玩家
                        FacePlayer();
                    }
                }
                
                yield return null;
            }
        }
        
        /// <summary>
        /// 更新玩家引用
        /// </summary>
        private void UpdatePlayerReference()
        {
            try
            {
                playerCharacter = CharacterMainControl.Main;
            }
            catch { }
        }
        
        // ========== 生命周期 ==========
        
        private void OnDestroy()
        {
            // 取消订阅事件
            if (bossHealth != null)
            {
                bossHealth.OnHurtEvent.RemoveListener(OnBossHurt);
            }
            
            // 停止太阳舞弹幕
            isSunDanceActive = false;
            sunDanceBarrageCoroutine = null;
            
            // 停止所有协程
            StopAllCoroutines();
            
            // 清理碰撞检测器
            if (collisionDetector != null)
            {
                Destroy(collisionDetector.gameObject);
                collisionDetector = null;
            }
            
            // 清理所有活跃特效
            CleanupAllEffects();
            
            // 清理引用
            bossCharacter = null;
            bossHealth = null;
            playerCharacter = null;
            if (aiController != null) aiController.Cleanup();
            aiController = null;
            cachedWeaponBullet = null;
        }

        /// <summary>
        /// Boss死亡时调用
        /// </summary>
        public void OnBossDeath()
        {
            CurrentPhase = DragonKingPhase.Dead;
            
            // 停止太阳舞弹幕
            isSunDanceActive = false;
            if (sunDanceBarrageCoroutine != null)
            {
                StopCoroutine(sunDanceBarrageCoroutine);
                sunDanceBarrageCoroutine = null;
            }
            
            // 停止所有协程
            StopAllCoroutines();
            
            // 清理所有活跃特效
            CleanupAllEffects();
            
            ModBehaviour.DevLog("[DragonKing] Boss死亡，清理完成");
        }
        
        /// <summary>
        /// 清理所有活跃特效和弹幕
        /// </summary>
        private void CleanupAllEffects()
        {
            // 清理特效
            foreach (var effect in activeEffects)
            {
                if (effect != null)
                {
                    Destroy(effect);
                }
            }
            activeEffects.Clear();
            
            // 清理弹幕
            foreach (var projectile in activeProjectiles)
            {
                if (projectile != null)
                {
                    Destroy(projectile);
                }
            }
            activeProjectiles.Clear();
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
                ModBehaviour.DevLog("[DragonKing] [DEBUG] 调试模式已启用，将只重复释放技能: " + DragonKingConfig.DebugAttackType);
            }
            
            while (CurrentPhase != DragonKingPhase.Dead && bossCharacter != null)
            {
                // 阶段转换中暂停攻击
                if (CurrentPhase == DragonKingPhase.Transitioning)
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
                    ModBehaviour.DevLog("[DragonKing] [DEBUG] 执行调试技能: " + attackType);
                }
                else
                {
                    // 正常模式：按序列执行
                    var sequence = CurrentSequence;
                    attackType = sequence[currentAttackIndex];
                    ModBehaviour.DevLog("[DragonKing] 执行攻击: " + attackType + " (索引: " + currentAttackIndex + ")");
                }
                
                // 执行攻击
                isAttacking = true;
                yield return StartCoroutine(ExecuteAttack(attackType));
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
        /// </summary>
        private WaitForSeconds GetAttackIntervalWait()
        {
            return CurrentPhase == DragonKingPhase.Phase2 ? wait15s : wait2s;
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
            
            CurrentPhase = DragonKingPhase.Transitioning;
            
            // 停止当前攻击
            if (currentAttackCoroutine != null)
            {
                StopCoroutine(currentAttackCoroutine);
                currentAttackCoroutine = null;
            }
            
            // 清理当前攻击的特效
            CleanupAllEffects();
            
            // Boss消失（隐藏模型）
            if (bossCharacter != null)
            {
                SetBossVisible(false);
            }
            
            // 播放传送特效
            SpawnTeleportEffect(bossCharacter.transform.position);
            
            // 等待转换时间
            yield return wait1s;
            
            // 计算玩家预判位置
            UpdatePlayerReference();
            Vector3 targetPos = CalculatePredictedPosition();
            
            // 传送到玩家上方
            if (bossCharacter != null)
            {
                bossCharacter.transform.position = targetPos;
            }
            
            // 播放出现特效
            SpawnTeleportEffect(targetPos);
            
            // Boss出现
            if (bossCharacter != null)
            {
                SetBossVisible(true);
            }
            
            // 显示消息
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.ShowMessage(L10n.DragonKingEnraged);
            }
            
            // 重置攻击序列索引
            currentAttackIndex = 0;
            
            // 进入二阶段
            CurrentPhase = DragonKingPhase.Phase2;
            
            ModBehaviour.DevLog("[DragonKing] 二阶段转换完成");
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
                ModBehaviour.DevLog("[DragonKing] [WARNING] 设置可见性失败: " + e.Message);
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
            catch { }
            
            // 预判1秒后的位置
            Vector3 predictedPos = playerPos + playerVelocity * 1f;
            
            // 在预判位置上方
            float height = UnityEngine.Random.Range(
                DragonKingConfig.RepositionHeightMin, 
                DragonKingConfig.RepositionHeightMax
            );
            
            return predictedPos + Vector3.up * height;
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
            // 阶段转换中无敌
            if (CurrentPhase == DragonKingPhase.Transitioning && bossHealth != null)
            {
                bossHealth.SetHealth(bossHealth.CurrentHealth + damageInfo.finalDamage);
            }
        }
        
        // ========== 攻击执行 ==========
        
        /// <summary>
        /// 执行攻击
        /// </summary>
        private IEnumerator ExecuteAttack(DragonKingAttackType attackType)
        {
            // 太阳舞特殊处理：不在开始时收枪，而是在传送后收枪
            if (attackType != DragonKingAttackType.SunDance)
            {
                // 其他技能：释放技能前收枪
                PutBackWeapon();
            }
            
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
                    ModBehaviour.DevLog("[DragonKing] [WARNING] 未知攻击类型: " + attackType);
                    yield return wait1s;
                    break;
            }
            
            // 技能结束后拿枪
            TakeOutWeapon();
        }
        
        /// <summary>
        /// 收枪（释放技能时调用）
        /// </summary>
        private void PutBackWeapon()
        {
            try
            {
                if (bossCharacter == null) return;
                
                // 让Boss放下手中的武器
                if (bossCharacter.CurrentHoldItemAgent != null)
                {
                    bossCharacter.ChangeHoldItem(null);
                    ModBehaviour.DevLog("[DragonKing] 释放技能，已收枪");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKing] [WARNING] 收枪失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 拿枪（技能结束后调用）
        /// </summary>
        private void TakeOutWeapon()
        {
            try
            {
                if (bossCharacter == null) return;
                
                // 让Boss拿出武器
                bool success = bossCharacter.SwitchToFirstAvailableWeapon();
                if (success)
                {
                    ModBehaviour.DevLog("[DragonKing] 技能结束，已拿枪");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKing] [WARNING] 拿枪失败: " + e.Message);
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
            
            Vector3 bossPos = bossCharacter.transform.position;
            int boltCount = DragonKingConfig.PrismaticBoltCount;
            float angleStep = 360f / boltCount;
            float scale = DragonKingConfig.PrismaticBoltScale;
            
            // 生成弹幕 - 使用Unity预制体
            List<GameObject> bolts = new List<GameObject>();
            for (int i = 0; i < boltCount; i++)
            {
                float angle = i * angleStep;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2f;
                Vector3 spawnPos = bossPos + offset + Vector3.up * 1f;
                
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
                    
                    bolts.Add(bolt);
                    activeProjectiles.Add(bolt);
                }
            }
            
            // 延迟后开始追踪
            yield return wait05s;
            
            // 启动追踪协程
            foreach (var bolt in bolts)
            {
                if (bolt != null)
                {
                    StartCoroutine(TrackingProjectile(bolt, DragonKingConfig.PrismaticBoltLifetime));
                }
            }
            
            // 等待弹幕生命周期结束
            yield return new WaitForSeconds(DragonKingConfig.PrismaticBoltLifetime);
        }
        
        /// <summary>
        /// 追踪弹幕协程
        /// 前2秒追踪玩家，之后直线飞行
        /// </summary>
        private IEnumerator TrackingProjectile(GameObject projectile, float lifetime)
        {
            float startTime = Time.time;
            float speed = DragonKingConfig.PrismaticBoltSpeed;
            float trackingStrength = DragonKingConfig.PrismaticBoltTrackingStrength;
            float trackingDuration = DragonKingConfig.PrismaticBoltTrackingDuration;
            
            Vector3 currentVelocity = Vector3.zero;
            bool trackingEnded = false;
            
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
                        Vector3 targetPos = playerCharacter.transform.position + Vector3.up * 1f;
                        Vector3 dirToTarget = (targetPos - currentPos).normalized;
                        
                        // 不完美追踪：混合当前方向和目标方向
                        if (currentVelocity.sqrMagnitude < 0.01f)
                        {
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
                
                // 移动弹幕
                projectile.transform.position += currentVelocity * Time.deltaTime;
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
        /// 检测弹幕是否命中玩家
        /// </summary>
        private bool CheckProjectileHit(Vector3 position, float damage)
        {
            if (playerCharacter == null) return false;
            
            float hitRadius = 0.5f;
            float distance = Vector3.Distance(position, playerCharacter.transform.position + Vector3.up * 1f);
            
            if (distance < hitRadius)
            {
                ApplyDamageToPlayer(damage);
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
                ModBehaviour.DevLog("[DragonKing] 对玩家造成伤害: " + damage);
                
                DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                dmgInfo.damageValue = damage;
                // 添加火元素伤害，让原版受伤系统显示伤害数字
                dmgInfo.AddElementFactor(ElementTypes.fire, 1f);
                playerCharacter.Health.Hurt(dmgInfo);
                
                // 播放玩家受伤音效
                PlayHurtSound();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKing] [WARNING] 造成伤害失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 播放玩家受伤音效
        /// </summary>
        private void PlayHurtSound()
        {
            try
            {
                // 获取Mod目录
                string modBasePath = System.IO.Path.GetDirectoryName(GetType().Assembly.Location);
                
                // 优先查找Assets目录（标准资源位置）
                string assetsPath = System.IO.Path.Combine(modBasePath, "Assets", "hurt.mp3");
                string hurtSoundPath = System.IO.File.Exists(assetsPath) ? assetsPath 
                    : System.IO.Path.Combine(modBasePath, "hurt.mp3");
                
                if (System.IO.File.Exists(hurtSoundPath) && ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.PlaySoundEffect(hurtSoundPath);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKing] [WARNING] 播放受伤音效失败: " + e.Message);
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
            
            float duration = DragonKingConfig.SpiralFireDuration;
            float interval = DragonKingConfig.SpiralFireInterval;
            float angleIncrement = DragonKingConfig.SpiralAngleIncrement;
            float scale = DragonKingConfig.PrismaticBoltScale;
            
            float currentAngle = 0f;
            float startTime = Time.time;
            int boltIndex = 0;
            
            while (Time.time - startTime < duration && bossCharacter != null)
            {
                Vector3 bossPos = bossCharacter.transform.position;
                Vector3 offset = Quaternion.Euler(0f, currentAngle, 0f) * Vector3.forward * 2f;
                Vector3 spawnPos = bossPos + offset + Vector3.up * 1f;
                
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
                    
                    activeProjectiles.Add(bolt);
                    StartCoroutine(TrackingProjectile(bolt, DragonKingConfig.PrismaticBoltLifetime));
                }
                
                currentAngle += angleIncrement;
                boltIndex++;
                yield return wait01s;
            }
        }
        
        // ========== 冲刺攻击 ==========
        
        /// <summary>
        /// 执行冲刺攻击
        /// </summary>
        private IEnumerator ExecuteDash()
        {
            ModBehaviour.DevLog("[DragonKing] 执行冲刺攻击");
            
            if (bossCharacter == null || playerCharacter == null) yield break;
            
            // 从当前位置开始，不再瞬移
            Vector3 startPos = bossCharacter.transform.position;
            
            // 获取玩家当前位置作为冲刺目标
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
            
            // 播放蓄力特效
            SpawnDashTrailEffect(startPos);
            
            // 蓄力阶段 - Boss原地蓄力，给玩家反应时间
            yield return wait05s;
            
            // 设置无敌
            if (bossHealth != null)
            {
                bossHealth.SetInvincible(true);
            }
            
            // 冲刺阶段 - 高速冲向目标位置
            float dashSpeed = DragonKingConfig.DashSpeed;
            float maxDashDistance = 15f; // 最大冲刺距离
            float traveledDistance = 0f;
            bool hitPlayer = false;
            
            // 持续生成残影特效
            float lastTrailTime = 0f;
            float trailInterval = 0.1f;
            
            while (traveledDistance < maxDashDistance && bossCharacter != null && !hitPlayer)
            {
                float moveDistance = dashSpeed * Time.deltaTime;
                bossCharacter.transform.position += dashDir * moveDistance;
                traveledDistance += moveDistance;
                
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
                    ModBehaviour.DevLog("[DragonKing] 冲刺命中玩家！");
                }
                
                yield return null;
            }
            
            // 取消无敌
            if (bossHealth != null)
            {
                bossHealth.SetInvincible(false);
            }
            
            // 冲刺结束后随机切换悬浮方向
            RandomizeHoverSide();
            
            ModBehaviour.DevLog("[DragonKing] 冲刺结束，移动距离=" + traveledDistance.ToString("F1"));
        }
        
        /// <summary>
        /// 生成冲刺残影特效
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
                Destroy(effect, 2f);
            }
        }
        
        /// <summary>
        /// 检测冲刺碰撞
        /// </summary>
        private bool CheckDashCollision()
        {
            if (bossCharacter == null || playerCharacter == null) return false;
            
            float collisionRadius = 1.5f;
            float distance = Vector3.Distance(
                bossCharacter.transform.position, 
                playerCharacter.transform.position
            );
            
            return distance < collisionRadius;
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

            // 获取障碍物层Mask（排除Ground层，因为我们只想检测障碍物）
            int groundLayer = LayerMask.NameToLayer("Ground");
            int obstacleLayerMask = ~(1 << groundLayer);

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
                ModBehaviour.DevLog("[DragonKing] 位置验证失败: 检测到障碍物 " + col.name);
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
            yield return new WaitForSeconds(chargeTime);
            
            // 销毁预警圆圈
            if (warningCircle != null)
            {
                Destroy(warningCircle);
            }
            
            // Boss传送到目标位置
            bossCharacter.transform.position = targetPos;
            
            // 记录锁定位置（用于弹幕发射位置）
            sunDanceLockPosition = targetPos;
            
            // 传送后才收枪（龙王停留原地释放弹幕）
            PutBackWeapon();
            
            // 暂停AI行为，让Boss停留原地（包括禁止射击）
            // 使用与龙裔遗族相同的方式停止移动
            StopBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 太阳舞：传送完成，已收枪，AI已暂停");
            
            // 标记太阳舞开始
            isSunDanceActive = true;
            
            // 生成光束组（放在Boss身体中间高度）
            Vector3 beamPos = targetPos + Vector3.up * 1.2f; // Boss身体中间高度
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
            
            // 等待技能持续时间
            float duration = DragonKingConfig.SunDanceDuration;
            yield return new WaitForSeconds(duration);
            
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
        /// 同时向24个方向（每15°一个）发射子弹，每0.1s整体旋转5°
        /// </summary>
        private IEnumerator SunDanceBarrageLoop()
        {
            ModBehaviour.DevLog("[DragonKing] 太阳舞弹幕开始");
            
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
            float rotationPerTick = 5f;           // 每0.1s旋转5°
            float angleStep = 15f;                // 每15°一个方向
            int directionCount = 24;              // 360°/15° = 24个方向
            int tickCount = 0;
            
            ModBehaviour.DevLog("[DragonKing] 弹幕初始方向: " + initialDir + ", 方向数: " + directionCount);
            
            while (isSunDanceActive && bossCharacter != null)
            {
                // 同时向24个方向发射子弹
                for (int i = 0; i < directionCount; i++)
                {
                    // 计算当前方向（基于初始方向 + 整体旋转 + 方向索引偏移）
                    float angle = currentRotation + (i * angleStep);
                    Vector3 fireDir = Quaternion.Euler(0f, angle, 0f) * initialDir;
                    
                    // 发射子弹
                    SpawnSunDanceTrackingBullet(fireDir);
                }
                
                tickCount++;
                
                // 每5次（0.5秒）输出一次日志
                if (tickCount % 5 == 0)
                {
                    ModBehaviour.DevLog("[DragonKing] 弹幕tick " + tickCount + ", 旋转角度: " + currentRotation + "°, 已发射 " + (tickCount * directionCount) + " 发");
                }
                
                // 整体旋转5°
                currentRotation += rotationPerTick;
                if (currentRotation >= 360f) currentRotation -= 360f;
                
                // 等待下一次发射（0.1秒）
                yield return wait01s;
            }
            
            ModBehaviour.DevLog("[DragonKing] 太阳舞弹幕结束，共发射 " + (tickCount * directionCount) + " 发");
        }
        
        /// <summary>
        /// 发射太阳舞子弹
        /// 使用龙王武器的子弹预制体和原武器子弹速度
        /// 初始不追踪，只有在玩家3m范围内才追踪1秒
        /// </summary>
        private void SpawnSunDanceTrackingBullet(Vector3 direction)
        {
            try
            {
                if (bossCharacter == null)
                {
                    ModBehaviour.DevLog("[DragonKing] [WARNING] bossCharacter为null，无法发射子弹");
                    return;
                }
                if (LevelManager.Instance == null || LevelManager.Instance.BulletPool == null)
                {
                    ModBehaviour.DevLog("[DragonKing] [WARNING] BulletPool不可用");
                    return;
                }
                
                // 确保有子弹预制体
                if (cachedWeaponBullet == null)
                {
                    CacheWeaponBullet();
                    if (cachedWeaponBullet == null)
                    {
                        ModBehaviour.DevLog("[DragonKing] [WARNING] 无法获取子弹预制体");
                        return;
                    }
                }
                
                // 使用原武器的子弹速度的一半（太阳舞弹幕速度降低）
                float bulletSpeed = cachedWeaponBulletSpeed * 0.4f;
                
                // 计算发射位置（Boss胸口位置，使用锁定位置而非当前位置）
                Vector3 muzzlePos = sunDanceLockPosition + Vector3.up * 1.2f;
                
                // 播放射击音效
                PlayWeaponShootSound();
                
                // 从BulletPool获取子弹
                Projectile bullet = LevelManager.Instance.BulletPool.GetABullet(cachedWeaponBullet);
                if (bullet == null)
                {
                    ModBehaviour.DevLog("[DragonKing] [WARNING] BulletPool返回null");
                    return;
                }
                
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
                
                // 检查玩家是否在3m范围内，只有在范围内才追踪1秒
                bool shouldTrack = false;
                UpdatePlayerReference();
                if (playerCharacter != null)
                {
                    float distanceToPlayer = Vector3.Distance(muzzlePos, playerCharacter.transform.position);
                    shouldTrack = distanceToPlayer <= 3f;
                }
                
                if (shouldTrack)
                {
                    // 玩家在3m范围内，启用追踪（追踪1秒，traceAbility控制追踪强度）
                    ctx.traceTarget = playerCharacter;
                    ctx.traceAbility = 1f; // 追踪强度（1秒内完成追踪）
                }
                else
                {
                    // 玩家不在3m范围内，不追踪
                    ctx.traceTarget = null;
                    ctx.traceAbility = 0f;
                }
                
                // 使用Init方法初始化子弹
                bullet.Init(ctx);
                
                // 记录到活跃弹幕列表
                activeProjectiles.Add(bullet.gameObject);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonKing] [WARNING] 发射太阳舞子弹失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 播放武器射击音效
        /// </summary>
        private void PlayWeaponShootSound()
        {
            try
            {
                if (bossCharacter == null || string.IsNullOrEmpty(cachedWeaponShootKey)) return;
                
                // 提取纯 shootKey（去除可能存在的路径前缀）
                string pureKey = cachedWeaponShootKey;
                
                // 如果包含完整路径，提取最后的 key 部分
                int lastSlash = cachedWeaponShootKey.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash < cachedWeaponShootKey.Length - 1)
                {
                    pureKey = cachedWeaponShootKey.Substring(lastSlash + 1);
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
                
                // 构建音效路径
                string eventName = "SFX/Combat/Gun/Shoot/" + pureKey.ToLower();
                
                // 使用反射调用AudioManager
                var audioManagerType = typeof(LevelManager).Assembly.GetType("Duckov.AudioManager");
                if (audioManagerType != null)
                {
                    var postMethod = audioManagerType.GetMethod("Post", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null,
                        new System.Type[] { typeof(string), typeof(GameObject) },
                        null);
                    
                    if (postMethod != null)
                    {
                        postMethod.Invoke(null, new object[] { eventName, bossCharacter.gameObject });
                    }
                }
            }
            catch (Exception e)
            {
                // 音效播放失败不影响游戏逻辑
                ModBehaviour.DevLog("[DragonKing] [WARNING] 播放射击音效失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 创建预警圆圈（实心圆+透明遮罩层逐渐缩小）
        /// </summary>
        private GameObject CreateWarningCircle(Vector3 position, float chargeTime)
        {
            // 创建圆圈容器
            GameObject circleObj = new GameObject("WarningCircle");
            circleObj.transform.position = position + Vector3.up * 0.05f; // 稍微抬高避免z-fighting
            
            float radius = 0.75f; // 原来3f的四分之一
            
            // 创建实心圆底层（金黄色）
            GameObject solidCircle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            solidCircle.name = "SolidCircle";
            solidCircle.transform.SetParent(circleObj.transform);
            solidCircle.transform.localPosition = Vector3.zero;
            solidCircle.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f); // 扁平圆柱
            
            // 移除碰撞体
            var collider = solidCircle.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            
            // 设置实心圆材质（金黄色半透明）
            var solidRenderer = solidCircle.GetComponent<Renderer>();
            solidRenderer.material = new Material(Shader.Find("Sprites/Default"));
            solidRenderer.material.color = new Color(1f, 0.7f, 0f, 0.6f);
            
            // 创建透明遮罩层（黑色，会逐渐缩小）
            GameObject maskCircle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            maskCircle.name = "MaskCircle";
            maskCircle.transform.SetParent(circleObj.transform);
            maskCircle.transform.localPosition = Vector3.up * 0.01f; // 稍微高于实心圆
            maskCircle.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
            
            // 移除碰撞体
            var maskCollider = maskCircle.GetComponent<Collider>();
            if (maskCollider != null) Destroy(maskCollider);
            
            // 设置遮罩材质（深色半透明）
            var maskRenderer = maskCircle.GetComponent<Renderer>();
            maskRenderer.material = new Material(Shader.Find("Sprites/Default"));
            maskRenderer.material.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            
            // 启动遮罩缩小动画
            StartCoroutine(AnimateMaskShrink(maskCircle.transform, chargeTime));
            
            return circleObj;
        }
        
        /// <summary>
        /// 遮罩层缩小动画
        /// </summary>
        private IEnumerator AnimateMaskShrink(Transform maskTransform, float chargeTime)
        {
            if (maskTransform == null) yield break;
            
            Vector3 initialScale = maskTransform.localScale;
            float elapsed = 0f;
            
            while (elapsed < chargeTime && maskTransform != null)
            {
                float t = elapsed / chargeTime;
                
                // 遮罩层逐渐缩小到0
                float scale = Mathf.Lerp(1f, 0f, t);
                maskTransform.localScale = new Vector3(initialScale.x * scale, initialScale.y, initialScale.z * scale);
                
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
            
            ModBehaviour.DevLog("[DragonKing] 太阳舞伤害触发器已设置，数量: " + count);
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
            
            // 生成星星
            List<GameObject> stars = new List<GameObject>();
            List<float> starAngles = new List<float>();
            
            float angleStep = 360f / starCount;
            for (int i = 0; i < starCount; i++)
            {
                float angle = i * angleStep;
                Vector3 spawnPos = centerPos + Vector3.up * 1f;
                
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
                    
                    stars[i].transform.position = centerPos + offset;
                    
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
        /// 执行以太长矛攻击
        /// 根据玩家移动状态生成长矛
        /// </summary>
        private IEnumerator ExecuteEtherealLance()
        {
            ModBehaviour.DevLog("[DragonKing] 执行以太长矛攻击");
            
            if (bossCharacter == null || playerCharacter == null) yield break;
            
            int lanceCount = DragonKingConfig.EtherealLanceCount;
            float warningDuration = DragonKingConfig.EtherealLanceWarningDuration;
            float lanceSpeed = DragonKingConfig.EtherealLanceSpeed;
            
            // 检测玩家移动方向
            Vector3 playerPos = playerCharacter.transform.position;
            Vector3 playerVelocity = GetPlayerVelocity();
            bool isMoving = playerVelocity.sqrMagnitude > 0.1f;
            
            // 计算长矛生成位置
            List<Vector3> spawnPositions = new List<Vector3>();
            List<Vector3> targetDirections = new List<Vector3>();
            
            if (isMoving)
            {
                // 玩家移动：在移动路径后方生成
                Vector3 behindDir = -playerVelocity.normalized;
                float spawnDistance = 8f;
                
                for (int i = 0; i < lanceCount; i++)
                {
                    float offsetAngle = (i - lanceCount / 2f) * 15f;
                    Vector3 offset = Quaternion.Euler(0f, offsetAngle, 0f) * behindDir * spawnDistance;
                    Vector3 spawnPos = playerPos + offset + Vector3.up * 2f;
                    
                    spawnPositions.Add(spawnPos);
                    targetDirections.Add((playerPos - spawnPos).normalized);
                }
            }
            else
            {
                // 玩家静止：在周围生成
                float angleStep = 360f / lanceCount;
                float spawnDistance = 6f;
                
                for (int i = 0; i < lanceCount; i++)
                {
                    float angle = i * angleStep;
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * spawnDistance;
                    Vector3 spawnPos = playerPos + offset + Vector3.up * 2f;
                    
                    spawnPositions.Add(spawnPos);
                    targetDirections.Add((playerPos - spawnPos).normalized);
                }
            }
            
            // 生成长矛（预警状态）
            List<GameObject> lances = new List<GameObject>();
            for (int i = 0; i < spawnPositions.Count; i++)
            {
                GameObject lance = DragonKingAssetManager.InstantiateEffect(
                    DragonKingConfig.EtherealLancePrefab,
                    spawnPositions[i],
                    Quaternion.LookRotation(targetDirections[i])
                );
                
                if (lance != null)
                {
                    lances.Add(lance);
                    activeProjectiles.Add(lance);
                }
            }
            
            // 预警时间
            yield return wait1s;
            
            // 发射长矛
            float launchTime = Time.time;
            float maxFlightTime = 3f;
            
            while (Time.time - launchTime < maxFlightTime)
            {
                bool anyActive = false;
                
                for (int i = 0; i < lances.Count; i++)
                {
                    if (lances[i] == null) continue;
                    anyActive = true;
                    
                    // 移动长矛
                    lances[i].transform.position += targetDirections[i] * lanceSpeed * Time.deltaTime;
                    
                    // 检测命中
                    if (CheckProjectileHit(lances[i].transform.position, DragonKingConfig.EtherealLanceDamage))
                    {
                        activeProjectiles.Remove(lances[i]);
                        Destroy(lances[i]);
                        lances[i] = null;
                    }
                }
                
                if (!anyActive) break;
                yield return null;
            }
            
            // 清理剩余长矛
            foreach (var lance in lances)
            {
                if (lance != null)
                {
                    activeProjectiles.Remove(lance);
                    Destroy(lance);
                }
            }
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
            catch { }
            
            return Vector3.zero;
        }
        
        // ========== 以太长矛2攻击（切屏） ==========
        
        /// <summary>
        /// 执行以太长矛2攻击（切屏剑阵）
        /// 4波长矛从不同方向切过屏幕
        /// </summary>
        private IEnumerator ExecuteEtherealLance2()
        {
            ModBehaviour.DevLog("[DragonKing] 执行以太长矛2攻击（切屏）");
            
            if (bossCharacter == null || playerCharacter == null) yield break;
            
            int waveCount = DragonKingConfig.ScreenLanceWaveCount;
            int lancesPerWave = DragonKingConfig.ScreenLancePerWave;
            float waveInterval = DragonKingConfig.ScreenLanceWaveInterval;
            float lanceSpeed = DragonKingConfig.EtherealLanceSpeed;
            
            Vector3 playerPos = playerCharacter.transform.position;
            
            // 4波方向：左到右、右到左、左上到右下、右上到左下
            Vector3[] waveDirections = new Vector3[]
            {
                Vector3.right,
                Vector3.left,
                (Vector3.right + Vector3.back).normalized,
                (Vector3.left + Vector3.back).normalized
            };
            
            Vector3[] waveStartOffsets = new Vector3[]
            {
                Vector3.left * 15f,
                Vector3.right * 15f,
                (Vector3.left + Vector3.forward) * 10f,
                (Vector3.right + Vector3.forward) * 10f
            };
            
            for (int wave = 0; wave < waveCount; wave++)
            {
                Vector3 waveDir = waveDirections[wave];
                Vector3 startOffset = waveStartOffsets[wave];
                
                // 生成这一波的长矛
                List<GameObject> waveLances = new List<GameObject>();
                
                for (int i = 0; i < lancesPerWave; i++)
                {
                    // 沿垂直于移动方向的轴分布
                    Vector3 perpendicular = Vector3.Cross(waveDir, Vector3.up).normalized;
                    float spread = (i - lancesPerWave / 2f) * 2f;
                    Vector3 spawnPos = playerPos + startOffset + perpendicular * spread + Vector3.up * 2f;
                    
                    GameObject lance = DragonKingAssetManager.InstantiateEffect(
                        DragonKingConfig.EtherealLancePrefab,
                        spawnPos,
                        Quaternion.LookRotation(waveDir)
                    );
                    
                    if (lance != null)
                    {
                        waveLances.Add(lance);
                        activeProjectiles.Add(lance);
                    }
                }
                
                // 预警
                yield return wait1s;
                
                // 发射这一波
                StartCoroutine(LaunchLanceWave(waveLances, waveDir, lanceSpeed));
                
                // 波间隔
                yield return wait05s;
            }
            
            // 等待最后一波完成
            yield return wait2s;
        }
        
        /// <summary>
        /// 发射一波长矛
        /// </summary>
        private IEnumerator LaunchLanceWave(List<GameObject> lances, Vector3 direction, float speed)
        {
            float maxFlightTime = 3f;
            float startTime = Time.time;
            
            while (Time.time - startTime < maxFlightTime)
            {
                bool anyActive = false;
                
                foreach (var lance in lances)
                {
                    if (lance == null) continue;
                    anyActive = true;
                    
                    lance.transform.position += direction * speed * Time.deltaTime;
                    
                    if (CheckProjectileHit(lance.transform.position, DragonKingConfig.EtherealLanceDamage))
                    {
                        activeProjectiles.Remove(lance);
                        Destroy(lance);
                    }
                }
                
                if (!anyActive) break;
                yield return null;
            }
            
            // 清理
            foreach (var lance in lances)
            {
                if (lance != null)
                {
                    activeProjectiles.Remove(lance);
                    Destroy(lance);
                }
            }
        }
        
        // ========== 碰撞伤害处理 ==========
        
        /// <summary>
        /// 碰撞检测器回调 - 当玩家进入碰撞范围时调用
        /// </summary>
        public void OnCollisionWithPlayer(CharacterMainControl player)
        {
            if (player == null || bossCharacter == null) return;
            if (CurrentPhase == DragonKingPhase.Dead) return;
            
            // 检查冷却时间
            if (Time.time - lastCollisionDamageTime < DragonKingConfig.CollisionCooldown) return;
            
            lastCollisionDamageTime = Time.time;
            
            // 应用碰撞伤害
            ApplyCollisionDamage(player);
            
            // 应用击退
            ApplyKnockback(player);
            
            ModBehaviour.DevLog("[DragonKing] 碰撞伤害触发，伤害=" + DragonKingConfig.CollisionDamage);
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
                ModBehaviour.DevLog("[DragonKing] [WARNING] 应用碰撞伤害失败: " + e.Message);
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
                ModBehaviour.DevLog("[DragonKing] [WARNING] 应用击退失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 切换悬浮方向（在某些攻击后随机切换）
        /// </summary>
        public void RandomizeHoverSide()
        {
            hoverSide = UnityEngine.Random.value > 0.5f ? 1 : -1;
        }
    }
    
    // ========== 碰撞检测器组件 ==========
    
    /// <summary>
    /// 龙王碰撞检测器组件
    /// 用于检测Boss与玩家的碰撞并触发伤害
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
        
        /// <summary>
        /// 初始化碰撞检测器
        /// </summary>
        public void Initialize(DragonKingAbilityController ctrl)
        {
            controller = ctrl;
            
            // 设置碰撞层级
            gameObject.layer = LayerMask.NameToLayer("Default");
            
            ModBehaviour.DevLog("[DragonKing] 碰撞检测器组件初始化完成");
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
        /// 持续碰撞检测协程（替代OnTriggerStay以优化性能）
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
            ModBehaviour.DevLog("[DragonKing] SunBeam OnTriggerEnter: " + other.name + ", tag=" + other.tag);
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

            // 获取所有NodeCanvas行为树（使用反射来避免直接依赖）
            if (cachedAI != null)
            {
                var behaviourTreeType = Type.GetType("NodeCanvas.BehaviourTrees.BehaviourTree, NodeCanvas");
                if (behaviourTreeType != null)
                {
                    // 通过反射获取行为树字段
                    var fields = cachedAI.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        if (field.FieldType == behaviourTreeType)
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
                            // 通过反射禁用行为
                            var enabledProp = behaviour.GetType().GetProperty("enabled");
                            if (enabledProp != null && enabledProp.CanWrite)
                            {
                                enabledProp.SetValue(behaviour, false);
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
                    cachedAI.defaultWeaponOut = true;

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
                            // 通过反射启用行为
                            var enabledProp = behaviour.GetType().GetProperty("enabled");
                            if (enabledProp != null && enabledProp.CanWrite)
                            {
                                enabledProp.SetValue(behaviour, true);
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
}
