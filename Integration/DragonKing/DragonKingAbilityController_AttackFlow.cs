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
        // ========== 攻击主循环 ==========

        /// <summary>
        /// 攻击主循环协程
        /// </summary>
        private IEnumerator AttackLoop()
        {
            // 等待初始化完成
            yield return wait1s;

            float startupDelay = GetAttackLoopStartupDelay();
            if (startupDelay > 0f)
            {
                yield return new WaitForSeconds(startupDelay);
            }

            ModBehaviour.DevLog("[DragonKing] 攻击循环开始");

            // 调试模式提示
            if (DragonKingConfig.DebugMode)
            {
                ModBehaviour.DevLog($"[DragonKing] [DEBUG] 调试模式已启用，将只重复释放技能 {DragonKingConfig.DebugAttackType}");
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

                // 【Mode E】无仇恨目标时跳过本轮，等待原版 AI 搜索到敌人
                // 必须放在 playerCharacter 空检查之前，否则 Mode E 下临时无目标会触发 OnPlayerDeath 永久退出循环
                if (!HasValidTargetForModeE())
                {
                    yield return wait05s;
                    continue;
                }

                if (playerCharacter == null || playerCharacter.Health == null || playerCharacter.Health.IsDead)
                {
                    var inst = ModBehaviour.Instance;
                    if (inst != null && inst.IsModeEActive)
                    {
                        // 【Mode E】仇恨目标死亡或丢失，清空引用等待 AI 搜索下一个目标
                        // 不调用 OnPlayerDeath，避免 StopAllCoroutines 永久终止攻击循环
                        playerCharacter = null;
                        yield return wait05s;
                        continue;
                    }
                    else
                    {
                        // 非 Mode E：玩家死亡，一次性清理所有攻击协程和弹幕，然后退出循环
                        OnPlayerDeath();
                        yield break;
                    }
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
                    ModBehaviour.DevLog($"[DragonKing] [DEBUG] 执行调试技能 {attackType}");
                }
                else
                {
                    // 正常模式：按序列执行
                    var sequence = CurrentSequence;
                    attackType = sequence[currentAttackIndex];
                    ModBehaviour.DevLog($"[DragonKing] 执行攻击: {attackType} (索引: {currentAttackIndex})");
                }

                // 执行攻击
                currentAttackCoroutine = StartCoroutine(ExecuteAttack(attackType));
                yield return currentAttackCoroutine;
                currentAttackCoroutine = null;

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
            // 停止 AI 和射击
            StopBossMovementAndShooting();

            // 停止太阳舞弹幕
            isSunDanceActive = false;
            if (sunDanceBarrageCoroutine != null)
            {
                StopCoroutine(sunDanceBarrageCoroutine);
                sunDanceBarrageCoroutine = null;
            }

            // 不再调用 PutBackWeapon，射击循环会自动检测 Transitioning 阶段并暂停

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

            // 播放传送特效（消失位置）
            SpawnTeleportEffect(bossCharacter.transform.position);

            // 播放阶段转换特效（消失位置）
            SpawnPhaseTransitionEffect(bossCharacter.transform.position);

            // 等待转换时间
            yield return wait1s;

            // 计算玩家附近的地面位置（2.5D 游戏，传送到地面而非玩家头上）
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

            // 等待波纹完成（3 个波，间隔 0.5s，约 2-3 秒完成）
            yield return wait25s;

            // 不再调用 TakeOutWeapon，射击循环会自动检测 Phase2 并恢复射击

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
            currentAttackIndex = GetSequenceStartIndex(DragonKingConfig.Phase2Sequence);

            // 进入二阶段
            CurrentPhase = DragonKingPhase.Phase2;

            // 重新启动攻击循环，确保二阶段攻击能够正常执行
            // 原因：二阶段转换期间停止了 currentAttackCoroutine，可能导致 AttackLoop 状态不一致
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
            var effect = DragonKingAssetManager.AcquireSharedEffect(
                DragonKingConfig.PhaseTransitionPrefab,
                position,
                Quaternion.identity
            );

            if (effect != null)
            {
                TrackActiveEffect(effect);
                DragonKingAssetManager.ReleaseEffectAfter(effect, 3f);
                ModBehaviour.DevLog("[DragonKing] 阶段转换特效已生成");
            }
        }

        /// <summary>
        /// 设置 Boss 可见性
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
            var effect = DragonKingAssetManager.AcquireSharedEffect(
                DragonKingConfig.TeleportFXPrefab,
                position,
                Quaternion.identity
            );

            if (effect != null)
            {
                TrackActiveEffect(effect);
                DragonKingAssetManager.ReleaseEffectAfter(effect, 2f);
            }
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

                // 【Mode E】无仇恨目标时跳过射击，等待原版AI搜索到敌人
                if (!HasValidTargetForModeE())
                {
                    yield return wait01s;
                    continue;
                }

                // 只有玩家存活时才射击
                Vector3 playerPos;
                if (playerCharacter != null
                    && playerCharacter.Health != null
                    && !playerCharacter.Health.IsDead
                    && TryGetPlayerAimPosition(out playerPos, true))
                {
                    // 根据阶段决定射弹量和偏移范围（使用配置）
                    bool isPhase2 = CurrentPhase == DragonKingPhase.Phase2;
                    int bulletCount = isPhase2 ? DragonKingConfig.Phase2BulletCount : DragonKingConfig.Phase1BulletCount;
                    float offsetRange = isPhase2 ? DragonKingConfig.Phase2OffsetRange : DragonKingConfig.Phase1OffsetRange;

                    Vector3 bossPos = bossCharacter.transform.position + Vector3.up * DragonKingConfig.BossChestHeightOffset; // 胸口位置

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

            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 发射自定义子弹失败: {e.Message}");
            }
        }


    }
}
