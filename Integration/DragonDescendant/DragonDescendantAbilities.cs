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
    public partial class DragonDescendantAbilityController : MonoBehaviour
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
            float leashDistance = DragonDescendantConfig.LeashDistance;
            float leashDistanceSqr = leashDistance * leashDistance;
            Vector3 leashDelta = bossCharacter.transform.position - playerCharacter.transform.position;
            return leashDelta.sqrMagnitude > leashDistanceSqr;
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

    }
}
