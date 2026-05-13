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
    public partial class DragonKingAbilityController
    {
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
                GameObject bolt = DragonKingAssetManager.AcquireSharedEffect(
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
                    TrackManagedProjectile(bolt);

                    // 播放棱彩弹1生成音效（每个弹幕都播放）
                }
            }

            // 延迟后开始追踪
            if (bolts.Count > 0)
            {
                ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_BoltSpawn);
            }

            yield return wait05s;

            // 恢复AI移动
            ResumeBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 棱彩弹：AI已恢复");

            // 启动追踪协程（添加到协程管理列表）
            foreach (var bolt in bolts)
            {
                if (bolt != null)
                {
                    RegisterTrackingProjectile(bolt, DragonKingConfig.PrismaticBoltLifetime);
                }
            }

            // 等待弹幕生命周期结束（使用缓存，PrismaticBoltLifetime=5f）
            yield return wait5s;
        }

        /// <summary>
        /// 检测弹幕是否命中玩家
        /// 性能优化：使用sqrMagnitude避免开方运算
        /// </summary>
        private bool CheckProjectileHit(Vector3 position, float damage)
        {
            Vector3 targetPos;
            if (playerCharacter == null || IsPlayerDead() || !TryGetPlayerAimPosition(out targetPos)) return false;
            return CheckProjectileHit(position, damage, true, targetPos);
        }

        private bool CheckProjectileHit(Vector3 position, float damage, bool hasTargetPosition, Vector3 targetPos)
        {
            if (!hasTargetPosition || playerCharacter == null || IsPlayerDead()) return false;

            // 性能优化：使用sqrMagnitude避免开方运算
            // 使用配置中的常量
            float hitRadius = DragonKingConfig.ProjectileHitRadius;
            float hitRadiusSqr = hitRadius * hitRadius;
            Vector3 diff = position - targetPos;

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
            // 玩家已死亡时跳过伤害，避免对不活跃对象操作
            if (IsPlayerDead()) return;

            try
            {
                DamageInfo dmgInfo = new DamageInfo(bossCharacter);
                dmgInfo.damageValue = damage;
                // 设置伤害点位置（玩家身体中心），这样伤害数字才能正确显示
                // 使用配置中的常量
                dmgInfo.damagePoint = playerCharacter.transform.position + Vector3.up * DragonKingConfig.DamagePointHeightOffset;
                dmgInfo.damageNormal = Vector3.up;
                // 添加火元素伤害，让原版受伤系统显示伤害数字
                dmgInfo.AddElementFactor(ElementTypes.fire, 1f);
                playerCharacter.Health.Hurt(dmgInfo);

                // 检测目标是否被这次伤害杀死
                if (playerCharacter.Health.IsDead)
                {
                    var inst = ModBehaviour.Instance;
                    if (inst != null && inst.IsModeEActive)
                    {
                        // 【Mode E】仇恨目标被击杀，清空引用等待AI搜索下一个目标
                        // 不调用 OnPlayerDeath，避免 StopAllCoroutines 永久终止攻击循环
                        playerCharacter = null;
                        ModBehaviour.DevLog("[DragonKing] [Mode E] 仇恨目标已击杀，等待AI搜索下一个目标");
                    }
                    else
                    {
                        // 非 Mode E：玩家死亡，一次性清理所有攻击
                        OnPlayerDeath();
                    }
                    return;
                }

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
            ModBehaviour.DevLog("[DragonKing] 棱彩弹：AI已暂停，Boss停留原地");

            // 记录Boss当前位置，确保释放期间不移动
            Vector3 lockedPosition = bossCharacter.transform.position;

            float duration = DragonKingConfig.SpiralFireDuration;
            float angleIncrement = DragonKingConfig.SpiralAngleIncrement;
            float scale = DragonKingConfig.PrismaticBoltScale;

            float currentAngle = 0f;
            float startTime = Time.time;

            while (Time.time - startTime < duration && bossCharacter != null)
            {
                // 强制保持Boss位置不变
                bossCharacter.transform.position = lockedPosition;

                // 从Boss身体中心发射（加上高度偏移）
                Vector3 spawnPos = lockedPosition + Vector3.up * DragonKingConfig.BossChestHeightOffset;

                // 计算发射方向（螺旋旋转）
                Vector3 fireDir = Quaternion.Euler(0f, currentAngle, 0f) * Vector3.forward;

                // 使用Unity预制体创建棱彩弹
                GameObject bolt = DragonKingAssetManager.AcquireSharedEffect(
                    DragonKingConfig.PrismaticBoltPrefab,
                    spawnPos,
                    Quaternion.LookRotation(fireDir)
                );

                if (bolt != null)
                {
                    // 设置缩放
                    bolt.transform.localScale = Vector3.one * scale;

                    TrackManagedProjectile(bolt);
                    // 使用棱彩弹2专用的追踪时间（2秒），添加到协程管理列表
                    RegisterTrackingProjectile(
                        bolt,
                        DragonKingConfig.PrismaticBoltLifetime,
                        DragonKingConfig.PrismaticBolt2TrackingDuration);

                    PlaySharedDragonSound(
                        DragonKingConfig.Sound_BoltSpawn2,
                        DragonKingConfig.Sound_BoltSpawn2,
                        GLOBAL_BOLT_SPAWN2_SOUND_INTERVAL);
                }

                currentAngle += angleIncrement;
                yield return wait01s; // interval = SpiralFireInterval = 0.1f
            }

            // 恢复AI移动
            ResumeBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 棱彩弹2结束，AI已恢复");
        }


        // ========== 冲刺攻击 ==========

        /// <summary>
        /// 执行冲刺攻击
        /// 使用 AI 移动而非直接修改位置
        /// </summary>
        private IEnumerator ExecuteDash()
        {
            ModBehaviour.DevLog("[DragonKing] 执行冲刺攻击");

            if (bossCharacter == null || playerCharacter == null) yield break;

            // 获取起始位置和目标位置
            Vector3 startPos = bossCharacter.transform.position;
            UpdatePlayerReference();
            // 【Mode E】UpdatePlayerReference 可能将 playerCharacter 置空，需要空检查
            if (playerCharacter == null) yield break;
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

            // 停止 Boss 移动，准备蓄力
            StopBossMovementAndShooting();

            // ========== 蓄力阶段 ==========
            float chargeTime = DragonKingConfig.DashChargeTime;
            float countdownStartTime = chargeTime - DragonKingConfig.DashCountdownRingTime;
            // 在冲刺前 0.3 秒锁定玩家位置作为第一段冲刺落点
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
            Vector3 lockedTargetPos1 = targetPos;  // 第一段冲刺目标（0.3 秒前）
            Vector3 lockedTargetPos2 = targetPos;  // 第二段冲刺方向参考（0.1 秒前）
            bool target1Locked = false;
            bool target2Locked = false;

            float elapsed = 0f;
            while (elapsed < chargeTime && bossCharacter != null)
            {
                // 在最后 0.3 秒创建倒计时光圈
                if (elapsed >= countdownStartTime && countdownRing == null)
                {
                    countdownRing = CreateCountdownRing(bossCharacter.transform.position, DragonKingConfig.DashCountdownRingTime);
                }

                UpdatePlayerReference();
                if (playerCharacter != null)
                {
                    // 在冲刺前 0.3 秒锁定第一段目标位置
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

                    // 锁定前持续跟踪玩家，锁定后转向锁定位置
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
            if (chargeParticles != null)
            {
                UntrackActiveEffect(chargeParticles);
                ReturnDashChargeParticles(chargeParticles);
                chargeParticles = null;
            }

            if (countdownRing != null)
            {
                UntrackActiveEffect(countdownRing);
                ReturnDashCountdownRing(countdownRing);
                countdownRing = null;
            }

            if (bossCharacter == null)
            {
                yield break;
            }

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

            // ========== 第一段冲刺：直接移动到锁定的目标位置 ==========
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
            // 限制最大冲刺时间，防止距离过远时冲刺过长
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

            // ========== 第二段冲刺：仅在二阶段时执行 ==========
            // 使用SetForceMoveVelocity向第二个锁定位置冲刺
            if (bossCharacter != null && CurrentPhase == DragonKingPhase.Phase2)
            {
                // 计算第二段冲刺方向（从当前位置到 0.1 秒前锁定的位置）
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
                        float speedMultiplier = 1f - (secondDashElapsed / secondDashDuration) * 0.5f; // 速度从 1.0 衰减到 0.5
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
        /// 创建粒子聚拢特效
        /// </summary>
        private GameObject CreateChargeParticles(Transform parent)
        {
            try
            {
                GameObject particleObj = RentDashChargeParticles(parent);
                TrackActiveEffect(particleObj);
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
        /// 创建倒计时缩小光圈
        /// </summary>
        private GameObject CreateCountdownRing(Vector3 position, float duration)
        {
            try
            {
                GameObject ringObj = RentDashCountdownRing(position, duration);
                TrackActiveEffect(ringObj);
                ModBehaviour.DevLog($"[DragonKing] 创建倒计时光圈，持续时间={duration}");
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
            var effect = DragonKingAssetManager.AcquireSharedEffect(
                DragonKingConfig.DashTrailPrefab,
                position,
                bossCharacter != null ? bossCharacter.transform.rotation : Quaternion.identity
            );

            if (effect != null)
            {
                TrackActiveEffect(effect);

                // 添加岩浆伤害区域组件
                var lavaZone = effect.GetComponent<DragonKingLavaZone>();
                if (lavaZone == null)
                {
                    lavaZone = effect.AddComponent<DragonKingLavaZone>();
                }
                lavaZone.Initialize(
                    DragonKingConfig.LavaDamage,
                    DragonKingConfig.LavaDamageInterval,
                    DragonKingConfig.LavaDuration,
                    DragonKingConfig.LavaRadius,
                    bossCharacter
                );

                // 使用对象池自带的延时回收，避免每个岩浆区都跑独立 Update 计时。
                DragonKingAssetManager.ReleaseEffectAfter(effect, DragonKingConfig.LavaDuration);
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

            // 使用缓存的LayerMask（避免重复计算）
            int obstacleLayerMask = GetGroundObstacleLayerMask();

            // 使用 NonAlloc 版本避免每次传送校验都分配 Collider 数组
            int hitCount = Physics.OverlapCapsuleNonAlloc(
                position,
                position + Vector3.up * checkHeight,
                checkRadius,
                sharedTeleportValidationBuffer,
                obstacleLayerMask,
                QueryTriggerInteraction.Collide
            );

            // 结果数量触顶时保守处理，避免遗漏障碍物
            if (hitCount >= sharedTeleportValidationBuffer.Length)
            {
                return false;
            }

            // 如果检测到碰撞体（除了角色自己的碰撞体），说明位置无效
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = sharedTeleportValidationBuffer[i];
                if (col == null)
                {
                    continue;
                }

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

            // 如果尝试多次后仍找不到有效位置，返回玩家位置作为备用
            ModBehaviour.DevLog("[DragonKing] 警告: 未能找到理想传送位置，使用玩家位置");
            return centerPos;
        }

        /// <summary>
        /// 执行太阳舞攻击
        /// 龙王传送到玩家附近，传送后收枪并停止 AI，发射旋转弹幕
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
                TrackActiveEffect(warningCircle);
            }

            // 播放太阳舞警告音效
            ModBehaviour.Instance?.PlaySoundEffect(DragonKingConfig.Sound_SunWarning);

            yield return wait15s; // 使用缓存的WaitForSeconds(1.5f)

            // 回收预警圆圈
            if (warningCircle != null)
            {
                UntrackActiveEffect(warningCircle);
                ReturnWarningCircle(warningCircle);
            }

            // Boss传送到目标位置
            bossCharacter.transform.position = targetPos;

            // 记录锁定位置（用于弹幕发射位置）
            sunDanceLockPosition = targetPos;

            // 不再调用 PutBackWeapon，射击继续进行

            // 暂停AI行为，让Boss停留原地
            StopBossMovementAndShooting();
            ModBehaviour.DevLog("[DragonKing] 太阳舞：传送完成，AI 已暂停");

            // 标记太阳舞开始
            isSunDanceActive = true;

            // 生成光束组（放在 Boss 身体中间高度）
            Vector3 beamPos = targetPos + Vector3.up * DragonKingConfig.BossChestHeightOffset; // Boss身体中间高度
            GameObject beamGroup = DragonKingAssetManager.AcquireSharedEffect(
                DragonKingConfig.SunBeamGroupPrefab,
                beamPos,
                Quaternion.identity
            );

            if (beamGroup != null)
            {
                TrackActiveEffect(beamGroup);

                // 给所有 Edge 子对象配置伤害触发器
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
            ModBehaviour.DevLog("[DragonKing] 太阳舞结束，AI 已恢复");

            // 清理光束
            if (beamGroup != null)
            {
                UntrackActiveEffect(beamGroup);
                DragonKingAssetManager.ReleaseEffect(beamGroup);
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
        /// Mode E 下不传玩家引用，避免将玩家设为 searchedEnemy
        /// </summary>
        private void ResumeBossMovementAndShooting()
        {
            if (aiController != null)
            {
                var inst = ModBehaviour.Instance;
                if (inst != null && inst.IsModeEActive)
                {
                    // 【Mode E】不传目标，让原版AI自行通过行为树搜索敌人
                    aiController.Resume(null);
                }
                else
                {
                    aiController.Resume(playerCharacter);
                }
            }
        }
    }
}
