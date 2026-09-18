// ============================================================================
// ThunderRingRuntime.cs - 雷电戒指运行时逻辑
// ============================================================================
// 模块说明：
//   监听 Health.OnHurt 事件，实现双重机制：
//   1. 玩家被攻击命中时叠加电能层数（持续伤害不计）
//   2. 玩家下一次真正打在敌人身上时，若满层则释放雷电伤害
//
// 归因（2026-09-18 收紧）：
//   释放侧原先只判「fromCharacter 是玩家」，于是荆棘反弹、殉爆、毒爆发、套装反震这些
//   自建伤害，以及打可破坏木箱、误伤自己的召唤物，都会把攒了 8 秒的满层电能白白吃掉——
//   玩家会看到「明明满层了，一开枪却没有电弧」。现在统一走 NewWeaponAttribution.IsPlayerDirectHit，
//   口径与词缀系统的 IsPlayerHitOnEnemy 一致。
//   蓄能侧同样排掉 isFromBuffOrEffect：否则站在火里 1.5 秒就能靠灼烧 DoT 叠满，
//   与「以肉换伤」的设计意图不符。
//
// 性能（AGENTS §4.12）：
//   「是否佩戴戒指」走 NewWeaponEquipState 的 O(1) 缓存，不在受击回调里遍历图腾槽。
// ============================================================================

using System;
using UnityEngine;

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
        }

        /// <summary>当前电能层数。Dev 诊断与 F3 验收用。</summary>
        internal static int CurrentCharges { get { return currentCharges; } }

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
            // 官方 Health.Hurt 在致死流程里先发 OnDead、再发 OnHurt；主角死后不再蓄雷。
            // 避免重置电能后又被本次致死受击写回一层，局内复活继承上一条命的电能。
            if (isPlayerHurt && targetHealth.IsDead) return;
            bool hasAttacker = damageInfo.fromCharacter != null;
            if (!isPlayerHurt && !hasAttacker) return;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;

            // 情况1：玩家受击 -> 蓄雷
            if (isPlayerHurt && player.Health == targetHealth)
            {
                HandlePlayerHurt(player, ref damageInfo);
                return;
            }

            // 情况2：玩家攻击敌人 -> 尝试释放
            if (hasAttacker && damageInfo.fromCharacter == player)
            {
                HandlePlayerAttack(player, targetHealth, ref damageInfo);
            }
        }

        /// <summary>
        /// 玩家受击：叠加电能。只认「被攻击命中」，持续伤害（灼烧 / 中毒等 buff 通道）不计。
        /// </summary>
        private static void HandlePlayerHurt(CharacterMainControl player, ref DamageInfo damageInfo)
        {
            // DoT 不蓄能：官方 buff 伤害走 Duckov.Effects.DamageAction，必然带 isFromBuffOrEffect
            if (damageInfo.isFromBuffOrEffect) return;

            // 检查是否装备了雷电戒指
            if (!NewWeaponEquipState.IsTotemEquipped(NewWeaponIds.ThunderRingTypeId)) return;

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

            // 满层提示。这是玩家唯一需要知道的状态变化（可以去换个大伤害武器再开枪），
            // 8 秒内最多一次，不会刷屏，因此保留气泡。
            if (currentCharges >= ThunderRingConfig.MaxCharges)
            {
                NewWeaponFx.ShowBubble(
                    player,
                    "<color=#FFD54F>⚡ 雷能已满！</color>",
                    "<color=#FFD54F>⚡ Fully charged!</color>");
            }
        }

        /// <summary>
        /// 玩家攻击：满层时释放雷电
        /// </summary>
        private static void HandlePlayerAttack(CharacterMainControl player, Health targetHealth, ref DamageInfo damageInfo)
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

            // 只有「玩家用武器真的打在敌人身上」才消耗满层：自建伤害、可破坏物件、
            // 自己的召唤物都不算，否则玩家会看到电能凭空消失。
            if (!NewWeaponAttribution.IsPlayerDirectHit(targetHealth, ref damageInfo, player)) return;

            // 检查是否装备了雷电戒指
            if (!NewWeaponEquipState.IsTotemEquipped(NewWeaponIds.ThunderRingTypeId))
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
                // 重入时开头的 currentCharges <= 0 守卫会立即返回。
                currentCharges = 0;
                lastChargeTime = 0f;

                // 释放伤害 = 固定底 + 触发这一击的实际伤害 × 比例。固定底保证前期与旧版一致，
                // 比例项让它跟着玩家的武器一起长（详见 ThunderRingConfig.ReleaseHitDamageRatio）。
                float releaseDamage = ThunderRingConfig.ReleaseDamage;
                if (damageInfo.finalDamage > 0f)
                {
                    releaseDamage += damageInfo.finalDamage * ThunderRingConfig.ReleaseHitDamageRatio;
                }
                if (releaseDamage <= 0f) return;

                Vector3 strikePoint = targetHealth.transform.position;

                DamageInfo thunderDamage = new DamageInfo(player);
                thunderDamage.damageValue = releaseDamage;
                thunderDamage.fromWeaponItemID = 0; // 设为0避免释放伤害再次触发蓄雷
                // 自建伤害必须显式标记为 buff/效果通道（AGENTS.md 4.x 自建伤害约定），
                // 与 ModeGWeaponScoringCompatibilityMatrix 里 ThunderRing_Release_DamageInfoMain_TypeId0 的登记口径一致。
                thunderDamage.isFromBuffOrEffect = true;
                thunderDamage.AddElementFactor(ElementTypes.electricity, 1f);
                thunderDamage.damagePoint = strikePoint;
                thunderDamage.damageType = DamageTypes.normal;

                targetHealth.Hurt(thunderDamage);

                // 表现层：从玩家胸口打一道电弧到目标 + 落点雷光 + 音效（配色取自描述文案的 #FFD54F）。
                // 不再弹「雷霆释放」气泡：电弧 + 落点雷光 + 放电声已经把这一下说清楚了，
                // 紧跟在「雷能已满」后面再弹一条只会连着糊两次。
                NewWeaponFx.PlayArc(
                    player.transform.position + Vector3.up * 1.1f,
                    strikePoint + Vector3.up * 0.8f,
                    NewWeaponPalette.ThunderCore, 0.12f, 0.22f);
                NewWeaponFx.PlayBurst(strikePoint, NewWeaponPalette.ThunderCore, 1.8f, 0.35f, 5);
                NewWeaponFx.PlaySound(NewWeaponSfx.ThunderRelease);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ThunderRingConfig.LogPrefix + " 雷电释放异常: " + e.Message);
            }
        }
    }
}
