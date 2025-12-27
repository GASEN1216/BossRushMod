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
        
        /// <summary>
        /// 碰撞冷却时间
        /// </summary>
        private const float COLLISION_COOLDOWN = 0.5f;
        
        // ========== 冰属性伤害减速机制 ==========
        
        /// <summary>
        /// 累计冰属性伤害
        /// </summary>
        private float accumulatedIceDamage = 0f;
        
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
        
        // ========== 初始化 ==========
        
        /// <summary>
        /// 初始化能力控制器
        /// </summary>
        public void Initialize(CharacterMainControl character)
        {
            bossCharacter = character;
            
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
            
            ModBehaviour.DevLog("[DragonDescendant] 能力控制器初始化完成");
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
            
            // 停止协程
            if (grenadeTimerCoroutine != null)
            {
                StopCoroutine(grenadeTimerCoroutine);
            }
            if (chaseCoroutine != null)
            {
                StopCoroutine(chaseCoroutine);
            }
            if (iceSlowdownCoroutine != null)
            {
                StopCoroutine(iceSlowdownCoroutine);
            }
        }
        
        // ========== 火箭弹逻辑 ==========
        
        /// <summary>
        /// 记录上一帧的弹药数量（用于检测射击）
        /// </summary>
        private int lastBulletInMag = -1;
        
        private void Update()
        {
            // 复活期间完全停止AI行为
            if (isResurrecting || aiPaused)
            {
                // 强制停止移动和射击
                if (bossCharacter != null)
                {
                    bossCharacter.SetMoveInput(Vector2.zero);
                    bossCharacter.SetRunInput(false);
                    bossCharacter.Trigger(false, false, false);
                }
                if (cachedAI != null)
                {
                    cachedAI.StopMove();
                }
                return; // 不执行其他逻辑
            }
            
            // 检测射击（通过弹药变化）- 只在非狂暴状态下检测
            if (!isEnraged && bossCharacter != null)
            {
                DetectShooting();
            }
            
            // 狂暴状态下的追逐逻辑
            if (isEnraged && bossCharacter != null && playerCharacter != null)
            {
                UpdateChase();
                
                // 狂暴状态下持续停止Boss自身射击（使用直接生成子弹代替）
                bossCharacter.Trigger(false, false, false);
            }
        }
        
        /// <summary>
        /// 检测Boss是否射击
        /// </summary>
        private void DetectShooting()
        {
            try
            {
                // 使用GetGun方法获取当前武器
                var gun = bossCharacter.GetGun();
                if (gun == null) return;
                
                int currentBullet = gun.BulletCount;
                
                if (lastBulletInMag >= 0 && currentBullet < lastBulletInMag)
                {
                    // 检测到射击
                    int shotsFired = lastBulletInMag - currentBullet;
                    OnBossShoot(shotsFired);
                }
                
                lastBulletInMag = currentBullet;
            }
            catch { }
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
                
                // 获取玩家位置作为爆炸点
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
        /// </summary>
        private IEnumerator GrenadeTimerCoroutine()
        {
            while (true)
            {
                // 根据状态选择间隔
                float interval = isEnraged 
                    ? DragonDescendantConfig.EnragedGrenadeInterval 
                    : DragonDescendantConfig.NormalGrenadeInterval;
                
                yield return new WaitForSeconds(interval);
                
                // 复活期间不投掷
                if (isResurrecting) continue;
                
                // 投掷燃烧弹
                ThrowIncendiaryGrenade();
            }
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
        /// 查找燃烧弹预制体
        /// </summary>
        private Grenade FindIncendiaryGrenadePrefab()
        {
            try
            {
                // 尝试从游戏资源中查找燃烧弹
                var allGrenades = Resources.FindObjectsOfTypeAll<Grenade>();
                foreach (var g in allGrenades)
                {
                    if (g == null) continue;
                    // 查找火焰类型的手雷
                    if (g.fxType == ExplosionFxTypes.fire || 
                        g.name.ToLower().Contains("fire") || 
                        g.name.ToLower().Contains("incendiary") ||
                        g.name.Contains("燃烧"))
                    {
                        return g;
                    }
                }
                
                // 如果没找到火焰类型，返回任意手雷
                if (allGrenades.Length > 0)
                {
                    return allGrenades[0];
                }
            }
            catch { }
            
            return null;
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
        /// </summary>
        private IEnumerator DelayedFireExplosion(Vector3 position, float delay)
        {
            yield return new WaitForSeconds(delay);
            
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
                
                ModBehaviour.DevLog("[DragonDescendant] 冰属性伤害累计: " + accumulatedIceDamage.ToString("F1") + 
                    " / " + (bossHealth.MaxHealth * 0.1f).ToString("F1"));
                
                // 检查是否达到阈值（最大生命值的10%）
                float threshold = bossHealth.MaxHealth * 0.1f;
                if (accumulatedIceDamage >= threshold)
                {
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
        private bool aiPaused = false;
        
        /// <summary>
        /// 暂停AI行动（复活对话期间）
        /// 完全停止Boss的移动、射击和AI决策
        /// </summary>
        private void PauseAI()
        {
            try
            {
                if (bossCharacter == null) return;
                
                aiPaused = true;
                
                // 缓存AI控制器
                if (cachedAI == null)
                {
                    cachedAI = bossCharacter.GetComponentInChildren<AICharacterController>();
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
                    cachedAI.noticed = false;
                    
                    // 禁用AI控制器
                    cachedAI.enabled = false;
                    
                    ModBehaviour.DevLog("[DragonDescendant] AI已暂停 - 停止移动和射击");
                }
                
                // 停止角色移动输入
                bossCharacter.SetMoveInput(Vector2.zero);
                bossCharacter.SetRunInput(false);
                
                // 停止射击（调用原版Trigger方法）
                bossCharacter.Trigger(false, false, false);
                
                ModBehaviour.DevLog("[DragonDescendant] Boss完全暂停");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 暂停AI失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 恢复AI行动
        /// </summary>
        private void ResumeAI()
        {
            try
            {
                aiPaused = false;
                
                if (cachedAI != null)
                {
                    // 重新启用AI控制器
                    cachedAI.enabled = true;
                    
                    // 恢复目标追踪
                    if (playerCharacter != null && playerCharacter.mainDamageReceiver != null)
                    {
                        cachedAI.searchedEnemy = playerCharacter.mainDamageReceiver;
                        cachedAI.noticed = true;
                    }
                    
                    ModBehaviour.DevLog("[DragonDescendant] AI已恢复");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 恢复AI失败: " + e.Message);
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
        /// 显示复活对话气泡
        /// </summary>
        private IEnumerator ShowResurrectionDialogue()
        {
            string dialogue = DragonDescendantConfig.ResurrectionDialogue;
            float charInterval = DragonDescendantConfig.DialogueCharInterval;
            
            // 逐字显示
            string displayText = "";
            foreach (char c in dialogue)
            {
                displayText += c;
                
                // 显示气泡
                try
                {
                    if (bossCharacter != null)
                    {
                        float yOffset = 2.5f;
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
                            displayText, 
                            bossCharacter.transform, 
                            yOffset, 
                            false, 
                            false, 
                            -1f, 
                            charInterval + 0.5f
                        ));
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] 显示对话气泡失败: " + e.Message);
                }
                
                yield return new WaitForSeconds(charInterval);
            }
            
            // 等待最后一个字符显示完成
            yield return new WaitForSeconds(0.5f);
        }
        
        // ========== 狂暴状态 ==========
        
        /// <summary>
        /// AI控制器引用（缓存）
        /// </summary>
        private AICharacterController cachedAI;
        
        /// <summary>
        /// 追逐更新间隔（与原版TraceTarget一致：0.15秒）
        /// </summary>
        private float chaseUpdateInterval = 0.15f;
        
        /// <summary>
        /// 进入狂暴状态
        /// </summary>
        private void EnterEnragedState()
        {
            if (isEnraged) return;
            
            isEnraged = true;
            ModBehaviour.DevLog("[DragonDescendant] 进入狂暴状态");
            
            // 缓存AI控制器
            if (bossCharacter != null)
            {
                cachedAI = bossCharacter.GetComponentInChildren<AICharacterController>();
            }
            
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
        /// 禁用射击行为
        /// 完全停止Boss的射击能力
        /// </summary>
        private void DisableShooting()
        {
            try
            {
                if (bossCharacter == null) return;
                
                // 使用原版API收起武器
                if (cachedAI != null)
                {
                    cachedAI.PutBackWeapon();
                    cachedAI.defaultWeaponOut = false;
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
                    yield return new WaitForSeconds(0.5f);
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
                    
                    if (playerCharacter != null && cachedAI != null)
                    {
                        // 朝玩家方向冲刺
                        Vector3 targetPos = playerCharacter.transform.position;
                        cachedAI.MoveToPos(targetPos);
                        
                        if (playerCharacter.mainDamageReceiver != null)
                        {
                            cachedAI.searchedEnemy = playerCharacter.mainDamageReceiver;
                            cachedAI.SetTarget(playerCharacter.mainDamageReceiver.transform);
                            cachedAI.noticed = true;
                        }
                        
                        bossCharacter.SetRunInput(true);
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
                
                // 短暂等待后进入下一个循环
                yield return new WaitForSeconds(0.2f);
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
                if (cachedAI != null)
                {
                    cachedAI.StopMove();
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
            for (int i = 0; i < count; i++)
            {
                if (!isEnraged || bossCharacter == null) yield break;
                
                // 更新方向（追踪玩家）
                direction = GetDirectionToPlayer();
                
                // 直接生成子弹
                SpawnBulletDirect(direction);
                
                yield return new WaitForSeconds(interval);
            }
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
                
                yield return new WaitForSeconds(interval);
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
            if (bulletCount <= 0) yield break;
            
            // 计算每发子弹的间隔时间（3秒24发）
            float interval = duration / bulletCount;
            
            for (int i = 0; i < bulletCount; i++)
            {
                if (!isEnraged || bossCharacter == null) yield break;
                
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
                
                yield return new WaitForSeconds(interval);
            }
        }
        
        /// <summary>
        /// 直接生成子弹（使用BulletPool）
        /// 参考原版ItemAgent_Gun.ShootOneBullet
        /// </summary>
        private void SpawnBulletDirect(Vector3 direction)
        {
            try
            {
                if (bossCharacter == null) return;
                if (LevelManager.Instance == null || LevelManager.Instance.BulletPool == null) return;
                
                // 获取Boss的枪
                var gun = bossCharacter.GetGun();
                Projectile bulletPrefab = null;
                float bulletSpeed = 30f;
                float bulletDistance = 50f;
                float damage = 15f;
                
                // 尝试从Boss的枪获取子弹预制体和属性
                if (gun != null)
                {
                    // 通过反射获取GunItemSetting
                    var gunSettingField = gun.GetType().GetProperty("GunItemSetting");
                    if (gunSettingField != null)
                    {
                        var gunSetting = gunSettingField.GetValue(gun) as ItemSetting_Gun;
                        if (gunSetting != null && gunSetting.bulletPfb != null)
                        {
                            bulletPrefab = gunSetting.bulletPfb;
                        }
                    }
                    
                    // 获取子弹属性
                    bulletSpeed = gun.BulletSpeed;
                    bulletDistance = gun.BulletDistance;
                    damage = gun.Damage;
                }
                
                // 如果没有找到预制体，尝试从Resources获取默认子弹
                if (bulletPrefab == null)
                {
                    // 尝试从场景中找到任意子弹预制体
                    var existingBullets = Resources.FindObjectsOfTypeAll<Projectile>();
                    if (existingBullets != null && existingBullets.Length > 0)
                    {
                        bulletPrefab = existingBullets[0];
                    }
                }
                
                if (bulletPrefab == null)
                {
                    ModBehaviour.DevLog("[DragonDescendant] [WARNING] 未找到子弹预制体");
                    return;
                }
                
                // 计算发射位置（Boss胸口位置）
                Vector3 muzzlePos = bossCharacter.transform.position + Vector3.up * 1.2f;
                
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
                ctx.critDamageFactor = 1.5f;
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
                if (cachedAI != null)
                {
                    cachedAI.defaultWeaponOut = true;
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
                    collisionTrigger.radius = 1.5f;
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
            if (!isEnraged || bossCharacter == null || cachedAI == null) return;
            
            // 确保AI保持追逐状态
            if (playerCharacter != null && playerCharacter.mainDamageReceiver != null)
            {
                cachedAI.searchedEnemy = playerCharacter.mainDamageReceiver;
                cachedAI.noticed = true;
                
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
            try
            {
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
                string candidate1 = System.IO.Path.Combine(baseDir, "Assets", "dragonToSecond.mp3");
                string candidate2 = System.IO.Path.Combine(baseDir, "dragonToSecond.mp3");
                
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
                    ModBehaviour.DevLog("[DragonDescendant] 未找到第二阶段音效文件: dragonToSecond.mp3");
                    return;
                }
                
                // 获取播放目标（Boss自身）
                GameObject target = bossCharacter != null ? bossCharacter.gameObject : null;
                
                // 使用反射调用AudioManager.PostCustomSFX
                System.Type audioManagerType = System.Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                if (audioManagerType != null)
                {
                    var method = audioManagerType.GetMethod("PostCustomSFX", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (method != null)
                    {
                        object[] args = new object[] { filePath, target, false };
                        method.Invoke(null, args);
                        ModBehaviour.DevLog("[DragonDescendant] 播放第二阶段音效: " + filePath);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] 播放第二阶段音效失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 播放撞击音效
        /// </summary>
        private void PlayCollisionSound()
        {
            try
            {
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
                string candidate1 = System.IO.Path.Combine(baseDir, "Assets", "hurt.mp3");
                string candidate2 = System.IO.Path.Combine(baseDir, "hurt.mp3");
                
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
                    ModBehaviour.DevLog("[DragonDescendant] 未找到撞击音效文件: hurt.mp3");
                    return;
                }
                
                // 获取播放目标（玩家）
                GameObject target = null;
                if (playerCharacter != null)
                {
                    target = playerCharacter.gameObject;
                }
                
                // 使用反射调用AudioManager.PostCustomSFX
                System.Type audioManagerType = System.Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                if (audioManagerType != null)
                {
                    var method = audioManagerType.GetMethod("PostCustomSFX", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (method != null)
                    {
                        object[] args = new object[] { filePath, target, false };
                        method.Invoke(null, args);
                        ModBehaviour.DevLog("[DragonDescendant] 播放撞击音效: " + filePath);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 播放撞击音效失败: " + e.Message);
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
                knockbackDir.y = 0.3f; // 稍微向上
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
            try
            {
                if (bossCharacter == null) return;
                
                string dialogue = "此等极寒之力也被你征服了吗，可恶...";
                float yOffset = 2.5f;
                
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
                    dialogue, 
                    bossCharacter.transform, 
                    yOffset, 
                    false, 
                    false, 
                    -1f, 
                    3f
                ));
                
                ModBehaviour.DevLog("[DragonDescendant] 显示冰冻减速对话: " + dialogue);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 显示冰冻减速对话失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 冰冻减速恢复协程（10秒后恢复）
        /// </summary>
        private IEnumerator IceSlowdownRecoveryCoroutine()
        {
            // 等待10秒
            yield return new WaitForSeconds(10f);
            
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
            try
            {
                if (bossCharacter == null) return;
                
                string dialogue = "哈哈哈用完了吗？轮到我了！";
                float yOffset = 2.5f;
                
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
                    dialogue, 
                    bossCharacter.transform, 
                    yOffset, 
                    false, 
                    false, 
                    -1f, 
                    3f
                ));
                
                ModBehaviour.DevLog("[DragonDescendant] 显示冰冻恢复对话: " + dialogue);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DragonDescendant] [WARNING] 显示冰冻恢复对话失败: " + e.Message);
            }
        }
    }
    
    /// <summary>
    /// 碰撞检测器组件
    /// 用于检测Boss与玩家的碰撞
    /// </summary>
    public class DragonDescendantCollisionDetector : MonoBehaviour
    {
        private DragonDescendantAbilityController controller;
        
        public void Initialize(DragonDescendantAbilityController ctrl)
        {
            controller = ctrl;
            
            // 设置碰撞层级（确保能检测到玩家）
            gameObject.layer = LayerMask.NameToLayer("Default");
        }
        
        private void OnTriggerEnter(Collider other)
        {
            CheckCollision(other);
        }
        
        private void OnTriggerStay(Collider other)
        {
            // 持续碰撞也触发（但有冷却）
            CheckCollision(other);
        }
        
        private void CheckCollision(Collider other)
        {
            if (controller == null) return;
            if (other == null) return;
            
            // 检查是否是玩家（多种方式检测）
            CharacterMainControl character = null;
            
            // 方式1：从碰撞器的父级查找
            character = other.GetComponentInParent<CharacterMainControl>();
            
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
            
            // 确认是主角色
            if (character != null && character.IsMainCharacter)
            {
                controller.OnCollisionWithPlayer(character);
            }
        }
    }
}
