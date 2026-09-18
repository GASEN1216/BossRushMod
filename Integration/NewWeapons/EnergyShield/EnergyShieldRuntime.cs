// ============================================================================
// EnergyShieldRuntime.cs - 能量盾运行时逻辑
// ============================================================================
// 模块说明：
//   监听 Health.OnHurt 事件，当玩家装备能量盾且正面受击时：
//   - 判定攻击方向是否在正面 ±60 度内
//   - 回复受到伤害的 30% 生命值
//
// 朝向口径（2026-09-18 修正，重要）：
//   官方角色的**根 Transform 从不旋转**，朝向写在子节点 modelRoot 上
//   （Movement.rotationRoot => CharacterMainControl.modelRoot，官方 Movement.cs:193/445/464），
//   对外暴露为 CharacterMainControl.CurrentAimDirection（官方 CharacterMainControl.cs:254）。
//   本文件此前用 player.transform.forward 做「正面」判据，等于拿一个恒定的世界方向去比——
//   实际效果是「只有从世界 +Z 方向打来才吸收」，与玩家实际面朝哪儿完全无关，
//   核心机制与它自称的弱点（侧背面不触发）都是失效的。现统一改用 CurrentAimDirection，
//   与仓库其它用到朝向的地方（焚皇断界戟、霜之哀伤、新武器挥砍拖尾）口径一致。
//
// 性能（AGENTS §4.12）：
//   「是否佩戴能量盾」走 NewWeaponEquipState 的 O(1) 缓存，不在受击回调里遍历图腾槽。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 能量盾运行时 - 正面受击回补机制
    /// </summary>
    public static class EnergyShieldRuntime
    {
        private static bool isSubscribed;
        private static float lastTriggerTime;
        private static readonly float EnergyShieldFrontalAngleCos =
            Mathf.Cos(EnergyShieldConfig.FrontalAngleThreshold * Mathf.Deg2Rad);

        /// <summary>
        /// 订阅伤害事件
        /// </summary>
        public static void Subscribe()
        {
            if (isSubscribed) return;

            Health.OnHurt += OnHurt;
            Health.OnDead += OnAnyDead;
            isSubscribed = true;
            ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 运行时已订阅");
        }

        /// <summary>
        /// 取消订阅
        /// </summary>
        public static void Unsubscribe()
        {
            if (!isSubscribed) return;

            Health.OnHurt -= OnHurt;
            Health.OnDead -= OnAnyDead;
            isSubscribed = false;
            ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 运行时已取消订阅");
        }

        /// <summary>
        /// 重置状态
        /// </summary>
        public static void ResetStaticCaches()
        {
            lastTriggerTime = 0f;
        }

        /// <summary>
        /// 玩家死亡时重置冷却。
        /// 与雷电戒指对称处理，确保复活后冷却从干净状态开始计算。
        /// </summary>
        private static void OnAnyDead(Health target, DamageInfo damageInfo)
        {
            if (target == null) return;
            if (!target.IsMainCharacterHealth) return;
            ResetStaticCaches();
        }

        /// <summary>
        /// Health.OnHurt 回调（签名：Action&lt;Health, DamageInfo&gt;）
        /// </summary>
        private static void OnHurt(Health targetHealth, DamageInfo damageInfo)
        {
            // 早期退出：只关心玩家受击
            // 官方 Health.Hurt 在致死流程里先发 OnDead、再发 OnHurt；不挡住这里会在死后回补生命，
            // 复活型模式可能因此带着一份「死后写入」的残余血量，且错误触发护盾表现。
            if (targetHealth == null || targetHealth.IsDead || !targetHealth.IsMainCharacterHealth) return;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health != targetHealth) return;

            // 检查玩家是否装备了能量盾（O(1) 读缓存，不遍历图腾槽）
            if (!NewWeaponEquipState.IsTotemEquipped(NewWeaponIds.EnergyShieldTypeId)) return;

            // 冷却检查
            if (Time.time - lastTriggerTime < EnergyShieldConfig.TriggerCooldown) return;

            // 方向判定：攻击来源是否在玩家正面
            if (!IsFrontalAttack(player, damageInfo)) return;

            // 计算回复量
            float healAmount = damageInfo.finalDamage * EnergyShieldConfig.FrontalAbsorptionRate;
            healAmount = Mathf.Min(healAmount, EnergyShieldConfig.MaxHealPerTrigger);

            if (healAmount < 0.5f) return;

            // 回复生命值（heal-back 模式：伤害已扣，加回部分）
            try
            {
                Health health = player.Health;
                float beforeHp = health.CurrentHealth;
                // 官方 AddHealth 内部就是 Min(MaxHealth, Current + v)，不需要自己再钳一次
                health.AddHealth(healAmount);

                if (health.CurrentHealth > beforeHp)
                {
                    lastTriggerTime = Time.time;

                    // 表现层：在玩家正前方亮一层护盾环 + 音效（配色取自描述文案的 #64B5F6）。
                    // 触发点本身已有 0.5 秒冷却，这里不会连闪。
                    Vector3 shieldFront = player.transform.position
                        + Vector3.up * 0.9f
                        + GetFacing(player) * 0.8f;
                    NewWeaponFx.PlayBurst(shieldFront, NewWeaponPalette.ShieldCore, 1.5f, 0.3f, 0);
                    NewWeaponFx.PlaySound(NewWeaponSfx.ShieldAbsorb);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 回复生命值异常: " + e.Message);
            }
        }

        /// <summary>
        /// 玩家面朝的水平单位向量。取官方 CurrentAimDirection（= modelRoot.forward），
        /// 不能用 transform.forward —— 根节点从不旋转。朝向退化时回退为 Vector3.forward。
        /// </summary>
        private static Vector3 GetFacing(CharacterMainControl player)
        {
            if (player == null) return Vector3.forward;

            Vector3 forward;
            try { forward = player.CurrentAimDirection; }
            catch { return Vector3.forward; }

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return Vector3.forward;
            return forward.normalized;
        }

        /// <summary>
        /// 判定攻击是否来自正面
        /// </summary>
        private static bool IsFrontalAttack(CharacterMainControl player, DamageInfo damageInfo)
        {
            // 获取攻击来源位置
            Vector3 attackOrigin;

            if (damageInfo.fromCharacter != null && damageInfo.fromCharacter.transform != null)
            {
                attackOrigin = damageInfo.fromCharacter.transform.position;
            }
            else if (damageInfo.damagePoint != Vector3.zero)
            {
                attackOrigin = damageInfo.damagePoint;
            }
            else
            {
                // 无法确定攻击方向，不触发
                return false;
            }

            // 计算攻击方向（从玩家看向攻击者）
            Vector3 playerPos = player.transform.position;
            Vector3 toAttacker = attackOrigin - playerPos;
            toAttacker.y = 0f;

            float toAttackerSqr = toAttacker.sqrMagnitude;
            if (toAttackerSqr < 0.01f) return false;

            // 玩家朝向：官方把朝向写在 modelRoot 上，CurrentAimDirection 是它的对外出口
            Vector3 playerForward = player.CurrentAimDirection;
            playerForward.y = 0f;

            float playerForwardSqr = playerForward.sqrMagnitude;
            if (playerForwardSqr < 0.01f) return false;

            float dot = Vector3.Dot(playerForward, toAttacker);
            if (dot <= 0f) return false;

            return dot * dot >= playerForwardSqr * toAttackerSqr *
                EnergyShieldFrontalAngleCos * EnergyShieldFrontalAngleCos;
        }
    }
}
