// ============================================================================
// DragonDescendantAbilities_CollisionAndIce.cs - collision and ice slowdown handling
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
