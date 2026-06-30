// ============================================================================
// ThunderRingRuntime.cs - 雷电戒指运行时逻辑
// ============================================================================
// 模块说明：
//   监听 Health.OnHurt 事件，实现双重机制：
//   1. 玩家受击时叠加电能层数
//   2. 玩家攻击敌人时，若满层则释放雷电伤害
//   性能优化：不装备雷电戒指时回调立即返回
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>
    /// 雷电戒指运行时 - 受击蓄雷 + 攻击释放
    /// </summary>
    public static class ThunderRingRuntime
    {
        private static bool isSubscribed;

        // 电能状态
        private static int currentCharges;
        private static float lastChargeTime;
        private static float lastChargeCooldownTime;
        private static int cachedEquipCheckFrame = -1;
        private static CharacterMainControl cachedEquipCheckPlayer;
        private static bool cachedEquipCheckResult;

        /// <summary>
        /// 订阅伤害事件
        /// </summary>
        public static void Subscribe()
        {
            if (isSubscribed) return;

            Health.OnHurt += OnHurt;
            Health.OnDead += OnAnyDead;
            isSubscribed = true;
            ModBehaviour.DevLog(ThunderRingConfig.LogPrefix + " 运行时已订阅");
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
            ResetStaticCaches();
            ModBehaviour.DevLog(ThunderRingConfig.LogPrefix + " 运行时已取消订阅");
        }

        /// <summary>
        /// 重置状态
        /// </summary>
        public static void ResetStaticCaches()
        {
            currentCharges = 0;
            lastChargeTime = 0f;
            lastChargeCooldownTime = 0f;
            cachedEquipCheckFrame = -1;
            cachedEquipCheckPlayer = null;
            cachedEquipCheckResult = false;
        }

        /// <summary>
        /// 玩家死亡时重置电能层数。
        /// 避免在 Mode E/F 等支持局内复活的模式下，玩家复活后仍然带着上次的雷能。
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
            if (targetHealth == null) return;

            bool isPlayerHurt = targetHealth.IsMainCharacterHealth;
            bool hasAttacker = damageInfo.fromCharacter != null;
            if (!isPlayerHurt && !hasAttacker) return;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;

            // 情况1：玩家受击 -> 蓄雷
            if (isPlayerHurt && player.Health == targetHealth)
            {
                HandlePlayerHurt(player, damageInfo);
                return;
            }

            // 情况2：玩家攻击敌人 -> 尝试释放
            if (hasAttacker && damageInfo.fromCharacter == player)
            {
                HandlePlayerAttack(player, targetHealth, damageInfo);
            }
        }

        /// <summary>
        /// 玩家受击：叠加电能
        /// </summary>
        private static void HandlePlayerHurt(CharacterMainControl player, DamageInfo damageInfo)
        {
            // 检查是否装备了雷电戒指
            if (!IsEquippingThunderRing(player)) return;

            // 冷却检查
            if (Time.time - lastChargeCooldownTime < ThunderRingConfig.ChargeCooldown) return;

            // 检查电能是否过期
            if (currentCharges > 0 && Time.time - lastChargeTime > ThunderRingConfig.ChargeDuration)
            {
                currentCharges = 0;
            }

            // 已满层不再叠加
            if (currentCharges >= ThunderRingConfig.MaxCharges) return;

            // 叠加
            currentCharges++;
            lastChargeTime = Time.time;
            lastChargeCooldownTime = Time.time;

            // 满层提示
            if (currentCharges >= ThunderRingConfig.MaxCharges)
            {
                ShowChargeBubble(player);
            }
        }

        /// <summary>
        /// 玩家攻击：满层时释放雷电
        /// </summary>
        private static void HandlePlayerAttack(CharacterMainControl player, Health targetHealth, DamageInfo damageInfo)
        {
            if (currentCharges <= 0) return;

            // 检查电能是否过期
            if (Time.time - lastChargeTime > ThunderRingConfig.ChargeDuration)
            {
                currentCharges = 0;
                return;
            }

            // 未满层不释放
            if (currentCharges < ThunderRingConfig.MaxCharges) return;

            // 检查是否装备了雷电戒指
            if (!IsEquippingThunderRing(player))
            {
                currentCharges = 0;
                lastChargeTime = 0f;
                return;
            }

            // 释放雷电伤害
            try
            {
                // 先消耗层数再造成伤害：targetHealth.Hurt 会同步回调 OnHurt，
                // 而该伤害的 fromCharacter 仍是玩家，会再次进入 HandlePlayerAttack。
                // 若放在 Hurt 之后清零，会因 currentCharges 仍满层而无限递归
                // （普通敌人被秒杀、高血量 Boss 直接爆栈）。提前清零后，
                // 重入时第 149 行 currentCharges <= 0 守卫会立即返回。
                currentCharges = 0;
                lastChargeTime = 0f;

                DamageInfo thunderDamage = new DamageInfo(player);
                thunderDamage.damageValue = ThunderRingConfig.ReleaseDamage;
                thunderDamage.fromWeaponItemID = 0; // 设为0避免释放伤害再次触发蓄雷
                thunderDamage.AddElementFactor(ElementTypes.electricity, 1f);
                thunderDamage.damagePoint = targetHealth.transform.position;
                thunderDamage.damageType = DamageTypes.normal;

                targetHealth.Hurt(thunderDamage);

                // 显示释放气泡
                ShowReleaseBubble(player);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ThunderRingConfig.LogPrefix + " 雷电释放异常: " + e.Message);
            }
        }

        /// <summary>
        /// 检查玩家是否装备了雷电戒指（图腾槽位）
        /// </summary>
        private static bool IsEquippingThunderRing(CharacterMainControl player)
        {
            if (player == null) return false;

            int frame = Time.frameCount;
            if (cachedEquipCheckFrame == frame && cachedEquipCheckPlayer == player)
            {
                return cachedEquipCheckResult;
            }

            bool isEquipped = false;
            try
            {
                Item characterItem = player.CharacterItem;
                if (characterItem != null && characterItem.Slots != null)
                {
                    // 遍历所有槽位，检查以 "Totem" 开头的槽位
                    foreach (ItemStatsSystem.Items.Slot slot in characterItem.Slots)
                    {
                        if (slot == null || slot.Content == null) continue;
                        if (!slot.Key.StartsWith("Totem")) continue;

                        if (slot.Content.TypeID == NewWeaponIds.ThunderRingTypeId)
                        {
                            isEquipped = true;
                            break;
                        }
                    }
                }
            }
            catch  { /* best-effort fallback intentionally ignored */ }

            cachedEquipCheckFrame = frame;
            cachedEquipCheckPlayer = player;
            cachedEquipCheckResult = isEquipped;
            return isEquipped;
        }

        private static void ShowChargeBubble(CharacterMainControl player)
        {
            if (player == null || player.transform == null) return;

            try
            {
                string text = L10n.T(
                    "<color=#FFD54F>⚡ 雷能已满！</color>",
                    "<color=#FFD54F>⚡ Fully charged!</color>");

                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    text,
                    player.transform,
                    1.5f,
                    false,
                    false,
                    -1f,
                    1.5f);
            }
            catch  { /* best-effort fallback intentionally ignored */ }
        }

        private static void ShowReleaseBubble(CharacterMainControl player)
        {
            if (player == null || player.transform == null) return;

            try
            {
                string text = L10n.T(
                    "<color=#FFD54F>⚡ 雷霆释放！</color>",
                    "<color=#FFD54F>⚡ Thunder Release!</color>");

                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    text,
                    player.transform,
                    1.5f,
                    false,
                    false,
                    -1f,
                    1.5f);
            }
            catch  { /* best-effort fallback intentionally ignored */ }
        }
    }
}
