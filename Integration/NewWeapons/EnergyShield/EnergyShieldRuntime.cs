// ============================================================================
// EnergyShieldRuntime.cs - 能量盾运行时逻辑
// ============================================================================
// 模块说明：
//   监听 Health.OnHurt 事件，当玩家装备能量盾且正面受击时：
//   - 判定攻击方向是否在正面 ±60 度内
//   - 回复受到伤害的 30% 生命值
//   性能优化：不装备能量盾时回调立即返回
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;

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
            if (targetHealth == null || !targetHealth.IsMainCharacterHealth) return;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health != targetHealth) return;

            // 检查玩家是否装备了能量盾
            if (!IsEquippingEnergyShield(player)) return;

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
                float currentHp = player.Health.CurrentHealth;
                float maxHp = player.Health.MaxHealth;
                float newHp = Mathf.Min(currentHp + healAmount, maxHp);

                if (newHp > currentHp)
                {
                    player.Health.SetHealth(newHp);
                    lastTriggerTime = Time.time;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 回复生命值异常: " + e.Message);
            }
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

            // 玩家朝向
            Vector3 playerForward = player.transform.forward;
            playerForward.y = 0f;

            float playerForwardSqr = playerForward.sqrMagnitude;
            if (playerForwardSqr < 0.01f) return false;

            float dot = Vector3.Dot(playerForward, toAttacker);
            if (dot <= 0f) return false;

            return dot * dot >= playerForwardSqr * toAttackerSqr *
                EnergyShieldFrontalAngleCos * EnergyShieldFrontalAngleCos;
        }

        /// <summary>
        /// 检查玩家是否装备了能量盾（图腾槽位）
        /// </summary>
        private static bool IsEquippingEnergyShield(CharacterMainControl player)
        {
            if (player == null) return false;

            try
            {
                Item characterItem = player.CharacterItem;
                if (characterItem == null) return false;

                // 遍历所有槽位，检查以 "Totem" 开头的槽位
                if (characterItem.Slots != null)
                {
                    foreach (ItemStatsSystem.Items.Slot slot in characterItem.Slots)
                    {
                        if (slot == null || slot.Content == null) continue;
                        if (!slot.Key.StartsWith("Totem")) continue;

                        if (slot.Content.TypeID == NewWeaponIds.EnergyShieldTypeId)
                        {
                            return true;
                        }
                    }
                }
            }
            catch  { /* best-effort fallback intentionally ignored */ }

            return false;
        }
    }
}
