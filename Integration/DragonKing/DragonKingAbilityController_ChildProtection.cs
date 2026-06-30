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
                Transform bossRoot = BossTransform;
                if (bossRoot == null) return;

                string dialogue = L10n.T(
                    DragonKingConfig.ChildProtectionDialogueCN,
                    DragonKingConfig.ChildProtectionDialogueEN
                );

                // 使用DialogueBubblesManager显示气泡
                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    dialogue,
                    bossRoot,
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
            Transform bossRoot = BossTransform;
            if (bossRoot == null) yield break;

            // 记录起飞前的位置（用于死亡时掉落物生成）
            preFlyPosition = bossRoot.position;
            ModBehaviour.DevLog($"[DragonKing] 记录起飞前位置: {preFlyPosition}");

            ModBehaviour.DevLog($"[DragonKing] 开始飞升到 {targetHeight} 米高度");

            // 创建飞行平台（防止下落）
            CreateFlightPlatform();

            // 创建云雾特效
            CreateFlightCloudEffect();

            float startY = bossRoot.position.y;
            float targetY = startY + targetHeight;
            lockedMinY = startY;

            // 使用加速度机制上升
            float currentUpwardSpeed = 0f;
            float maxUpwardSpeed = DragonKingConfig.ChildProtectionFlySpeed;
            float accelerationTime = 0.3f;
            float upwardAcceleration = maxUpwardSpeed / accelerationTime;

            while (bossCharacter != null && bossRoot != null)
            {
                float currentY = bossRoot.position.y;

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
                Vector3 pos = bossRoot.position;
                pos.y += currentUpwardSpeed * Time.deltaTime;
                bossRoot.position = pos;

                // 更新锁定高度
                if (pos.y > lockedMinY)
                {
                    lockedMinY = pos.y;
                }

                // 更新飞行平台位置
                UpdateFlightPlatformPosition();

                yield return null;
            }

            float? currentHeight = bossCharacter != null && bossRoot != null ? bossRoot.position.y : (float?)null;
            ModBehaviour.DevLog($"[DragonKing] 飞升完成，当前高度: {currentHeight}");
        }

        /// <summary>
        /// 创建飞行平台（防止下落）
        /// [修复] 改为Trigger模式，不产生物理碰撞，避免其他Boss被抬升
        /// 龙皇的悬浮由LateUpdate中的Y坐标锁定逻辑保证
        /// </summary>
        private void CreateFlightPlatform()
        {
            if (flightPlatform != null) return;

            flightPlatform = new GameObject("DragonKing_FlightPlatform");
            flightPlatform.hideFlags = HideFlags.HideInHierarchy;

            // [修复] 不再添加碰撞器，改用代码直接锁定Y坐标
            // 这样其他Boss不会被飞行平台抬升

            UpdateFlightPlatformPosition();

            ModBehaviour.DevLog("[DragonKing] 创建飞行平台（无物理碰撞模式）");
        }

        /// <summary>
        /// 更新飞行平台位置
        /// </summary>
        private void UpdateFlightPlatformPosition()
        {
            Transform bossRoot = BossTransform;
            if (flightPlatform == null || bossRoot == null) return;

            Vector3 bossPos = bossRoot.position;
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
        /// LateUpdate - 在孩儿护我阶段持续锁定龙皇Y坐标
        /// [修复] 替代物理飞行平台，确保只有龙皇被锁定在高空，其他Boss不受影响
        /// </summary>
        private void LateUpdate()
        {
            // 只在孩儿护我阶段且有有效锁定高度时执行
            if (!isInChildProtection || lockedMinY <= float.MinValue + 1f) return;
            Transform bossRoot = BossTransform;
            if (bossRoot == null) return;

            // 锁定龙皇Y坐标，防止下落
            Vector3 pos = bossRoot.position;
            if (pos.y < lockedMinY - 0.01f)
            {
                pos.y = lockedMinY;
                bossRoot.position = pos;
            }
        }

        /// <summary>
        /// 创建飞升云雾特效
        /// </summary>
        private void CreateFlightCloudEffect()
        {
            Transform bossRoot = BossTransform;
            if (flightCloudEffect != null || bossRoot == null) return;

            try
            {
                flightCloudEffect = RingParticleEffect.Create<FlightCloudEffect>(
                    bossRoot,
                    bossRoot.position
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

            WaitForSeconds waitInterval = Mathf.Approximately(DragonKingConfig.ChildProtectionBoltInterval, 3f)
                ? wait3s
                : new WaitForSeconds(DragonKingConfig.ChildProtectionBoltInterval);

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
                Transform bossRoot = BossTransform;
                if (bossRoot == null) return;

                // 获取玩家位置
                Vector3 playerPos;
                if (!TryGetPlayerAimPosition(out playerPos, true)) return;

                Vector3 bossPos = bossRoot.position + Vector3.up * DragonKingConfig.BossChestHeightOffset;
                Vector3 direction = (playerPos - bossPos).normalized;

                // 创建棱彩弹
                GameObject bolt = DragonKingAssetManager.AcquireSharedEffect(
                    DragonKingConfig.PrismaticBoltPrefab,
                    bossPos,
                    Quaternion.LookRotation(direction)
                );

                if (bolt != null)
                {
                    // 设置缩放
                    bolt.transform.localScale = Vector3.one * DragonKingConfig.PrismaticBoltScale;

                    // 启动追踪协程（使用统一的追踪弹幕方法）
                    RegisterTrackingProjectile(bolt, DragonKingConfig.PrismaticBoltLifetime);

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
            Transform bossRoot = BossTransform;
            if (bossRoot != null)
            {
                return bossRoot.position + Vector3.forward * 5f + Vector3.down * DragonKingConfig.ChildProtectionFlyHeight;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// 异步生成龙裔遗族（辅助方法，用于协程中调用异步方法）
        /// 孩儿护我阶段召唤的龙裔不加入波次追踪系统
        /// </summary>
        private async void SpawnDescendantAsync(Vector3 position, System.Action<CharacterMainControl> callback)
        {
            try
            {
                CharacterMainControl result = null;
                if (ModBehaviour.Instance != null)
                {
                    // 传入 isChildProtectionSummon: true，避免龙裔被加入波次追踪系统
                    result = await ModBehaviour.Instance.SpawnDragonDescendant(position, isChildProtectionSummon: true);
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
            Transform bossRoot = BossTransform;
            if (bossRoot == null || bossHealth == null) return;

            ModBehaviour.DevLog("[DragonKing] 执行联动死亡");

            // 移除无敌状态
            bossHealth.SetInvincible(false);
            isInChildProtection = false;

            // 清理飞行平台
            DestroyFlightPlatform();

            // 将龙王传送回起飞前的位置（确保掉落物生成在地面）
            if (preFlyPosition != Vector3.zero)
            {
                bossRoot.position = preFlyPosition;
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
}
