// ============================================================================
// DragonDescendantAbilities_Phase2Combat.cs - phase-two chase and shooting
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
    public partial class DragonDescendantAbilityController : MonoBehaviour
    {
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
    }
}
