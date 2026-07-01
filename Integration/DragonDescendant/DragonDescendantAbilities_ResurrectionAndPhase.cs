// ============================================================================
// DragonDescendantAbilities_ResurrectionAndPhase.cs - resurrection and phase transition
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
        /// <summary>
        /// 在原版 Health.Hurt() 进入死亡分支前，为龙裔遗族的一次性复活保留触发窗口。
        /// </summary>
        internal bool ShouldClampLethalHealthDuringHurt()
        {
            return bossCharacter != null &&
                   bossHealth != null &&
                   !hasResurrected &&
                   !isResurrecting &&
                   !isInvulnerable;
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
    }
}
