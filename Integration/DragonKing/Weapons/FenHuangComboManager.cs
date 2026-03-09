// ============================================================================
// FenHuangComboManager.cs - 焚皇断界戟三段连招管理器
// ============================================================================
// 模块说明：
//   1. MonoBehaviour 组件：管理三段连招状态 (comboStep / 超时重置)
//   2. Harmony Postfix Patch：在 CA_Attack.OnStart 后注入连招计数
//   3. Harmony Postfix Patch：在 ItemAgent_MeleeWeapon.CheckCollidersInRange 后
//      根据 comboStep 施加不同效果（击退 / 灼烧 / 叠印记）
//
//   性能注意：
//   - 所有 Patch 方法首先检查武器 TypeID，非焚皇断界戟时直接跳过（零额外开销）
//   - 不会影响其他近战武器
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 焚皇断界戟三段连招状态管理器 (MonoBehaviour)
    /// </summary>
    public class FenHuangComboManager : MonoBehaviour
    {
        // ========== 单例 ==========

        /// <summary>
        /// 全局实例（挂在常驻 GameObject 上）
        /// </summary>
        public static FenHuangComboManager Instance { get; private set; }

        // ========== 连招状态 ==========

        /// <summary>
        /// 当前连招段数：0=横扫, 1=上挑, 2=重劈
        /// </summary>
        public int ComboStep { get; private set; }

        /// <summary>
        /// 上次攻击时间
        /// </summary>
        private float lastAttackTime = -999f;

        // ========== 生命周期 ==========

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void Update()
        {
            // 连招超时重置
            if (ComboStep > 0 && Time.time - lastAttackTime > FenHuangHalberdConfig.ComboWindowTime)
            {
                ComboStep = 0;
            }

            // 定期清理过期印记
            DragonFlameMarkTracker.CleanupExpired();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            DragonFlameMarkTracker.ClearAll();
        }

        // ========== 连招推进 ==========

        /// <summary>
        /// 推进到下一段连招（由 Harmony Patch 在 CA_Attack.OnStart 后调用）
        /// </summary>
        public void AdvanceCombo()
        {
            lastAttackTime = Time.time;
            // combo 在 OnStart 时记录当前段，然后推进
            // 下一次攻击会使用推进后的段
            ComboStep = (ComboStep + 1) % 3;
        }

        /// <summary>
        /// 获取当前攻击段数（在 AdvanceCombo 之前调用以获取当前生效的段）
        /// </summary>
        public int GetCurrentAttackStep()
        {
            return ComboStep;
        }

        /// <summary>
        /// 重置连招
        /// </summary>
        public void ResetCombo()
        {
            ComboStep = 0;
            lastAttackTime = -999f;
        }

        // ========== 场景切换清理 ==========

        /// <summary>
        /// 场景切换时调用
        /// </summary>
        public void OnSceneChanged()
        {
            ResetCombo();
            DragonFlameMarkTracker.ClearAll();
        }
    }

    // ========================================================================
    // Harmony Patches
    // ========================================================================

    /// <summary>
    /// Patch CA_Attack.OnStart：
    /// 焚皇断界戟攻击时推进连招段数
    /// 性能：非焚皇断界戟时第一个 if 就返回，零开销
    /// </summary>
    [HarmonyPatch(typeof(CA_Attack), "OnStart")]
    public static class FenHuangComboAttackPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CA_Attack __instance, bool __result)
        {
            // 攻击未成功启动则跳过
            if (!__result) return;

            // 获取 combo 管理器
            var combo = FenHuangComboManager.Instance;
            if (combo == null) return;

            try
            {
                // 获取 characterController（CA_Attack 继承自 CharacterActionBase）
                var character = __instance.characterController;
                if (character == null) return;

                // 获取当前近战武器
                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee == null || melee.Item == null) return;

                // ★ 关键检查：只对焚皇断界戟生效
                if (melee.Item.TypeID != FenHuangHalberdIds.WeaponTypeId) return;

                // 推进连招
                combo.AdvanceCombo();
            }
            catch
            {
                // 静默失败，不影响游戏
            }
        }
    }

    /// <summary>
    /// Patch ItemAgent_MeleeWeapon.CheckAndDealDamage：
    /// 焚皇断界戟命中后根据连招段数施加附加效果
    /// 性能：非焚皇断界戟时第一个 if 就返回，零开销
    /// </summary>
    [HarmonyPatch(typeof(ItemAgent_MeleeWeapon), "CheckAndDealDamage")]
    public static class FenHuangComboDamagePatch
    {
        // 静态碰撞检测缓冲区（避免每次攻击 new Collider[]，减少 GC）
        private static readonly Collider[] hitBuffer = new Collider[12];

        [HarmonyPostfix]
        public static void Postfix(ItemAgent_MeleeWeapon __instance)
        {
            var combo = FenHuangComboManager.Instance;
            if (combo == null) return;

            try
            {
                // 获取 Item
                Item item = __instance.Item;
                if (item == null) return;

                // ★ 关键检查：只对焚皇断界戟生效
                if (item.TypeID != FenHuangHalberdIds.WeaponTypeId) return;

                // 获取角色
                CharacterMainControl holder = __instance.Holder;
                if (holder == null) return;

                // 获取当前段数（AdvanceCombo 已在 OnStart 里调用，当前 ComboStep 已经+1）
                // 所以实际生效的段是 (ComboStep - 1 + 3) % 3
                int effectiveStep = (combo.ComboStep - 1 + 3) % 3;

                // 在攻击范围内寻找被命中的敌人，施加额外效果
                ApplyComboEffects(holder, __instance, effectiveStep);
            }
            catch
            {
                // 静默失败
            }
        }

        /// <summary>
        /// 根据连招段数对范围内敌人施加额外效果
        /// </summary>
        private static void ApplyComboEffects(CharacterMainControl holder, ItemAgent_MeleeWeapon melee, int step)
        {
            // 获取攻击范围参数
            float range;
            float angle;

            switch (step)
            {
                case 0: // 横扫
                    range = FenHuangHalberdConfig.Combo1Range;
                    angle = FenHuangHalberdConfig.Combo1Angle;
                    break;
                case 1: // 上挑
                    range = FenHuangHalberdConfig.Combo2Range;
                    angle = FenHuangHalberdConfig.Combo2Angle;
                    break;
                case 2: // 重劈
                    range = FenHuangHalberdConfig.Combo3Range;
                    angle = FenHuangHalberdConfig.Combo3Angle;
                    break;
                default:
                    return;
            }

            // 在范围内查找敌人（复用静态缓冲区，避免 GC）
            int hitCount = Physics.OverlapSphereNonAlloc(
                holder.transform.position,
                range + 0.1f,
                hitBuffer,
                Duckov.Utilities.GameplayDataSettings.Layers.damageReceiverLayerMask
            );

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = hitBuffer[i];
                if (col == null) continue;

                DamageReceiver receiver = col.GetComponent<DamageReceiver>();
                if (receiver == null) continue;

                // 跳过友方
                if (holder.Team == receiver.Team && holder.Team != Teams.all) continue;

                // 检查角度
                Vector3 toTarget = col.transform.position - holder.transform.position;
                toTarget.y = 0f;
                float dist = toTarget.magnitude;
                toTarget.Normalize();

                float angleToTarget = Vector3.Angle(toTarget, holder.CurrentAimDirection);
                if (angleToTarget > angle * 0.5f && dist > 0.5f) continue;

                // 检查是否存活
                Health health = receiver.health;
                if (health != null)
                {
                    CharacterMainControl targetChar = health.TryGetCharacter();
                    if (targetChar == holder || (targetChar != null && targetChar.Dashing)) continue;
                    if (health.IsDead) continue;
                }

                // ★ 叠加龙焰印记（所有段都叠）
                DragonFlameMarkTracker.AddMark(receiver);

                // 根据段数施加额外效果
                switch (step)
                {
                    case 1: // 上挑：击退
                        ApplyKnockback(col.transform, holder, FenHuangHalberdConfig.Combo2KnockbackDistance);
                        break;

                    case 2: // 重劈：施加原版灼烧 Buff
                        ApplyBurnBuff(receiver, holder);
                        break;
                }
            }
        }

        /// <summary>
        /// 施加击退效果
        /// </summary>
        private static void ApplyKnockback(Transform target, CharacterMainControl holder, float distance)
        {
            try
            {
                // 计算击退方向
                Vector3 knockDir = target.position - holder.transform.position;
                knockDir.y = 0f;
                knockDir.Normalize();

                // 获取目标角色并移动
                CharacterMainControl targetChar = target.GetComponentInParent<CharacterMainControl>();
                if (targetChar != null)
                {
                    // 使用 SetForceMoveVelocity 实现短距离击退
                    targetChar.SetForceMoveVelocity(knockDir * distance * 5f);
                    // 0.2 秒后停止
                    if (FenHuangComboManager.Instance != null)
                    {
                        FenHuangComboManager.Instance.StartCoroutine(
                            StopKnockbackCoroutine(targetChar, 0.2f)
                        );
                    }
                }
            }
            catch
            {
                // 静默失败
            }
        }

        /// <summary>
        /// 击退停止协程
        /// </summary>
        private static System.Collections.IEnumerator StopKnockbackCoroutine(CharacterMainControl target, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (target != null)
            {
                target.SetForceMoveVelocity(Vector3.zero);
            }
        }

        /// <summary>
        /// 施加原版灼烧 Buff
        /// </summary>
        private static void ApplyBurnBuff(DamageReceiver receiver, CharacterMainControl fromCharacter)
        {
            try
            {
                var burnBuff = Duckov.Utilities.GameplayDataSettings.Buffs.Burn;
                if (burnBuff != null)
                {
                    receiver.AddBuff(burnBuff, fromCharacter);
                }
            }
            catch
            {
                // 静默失败
            }
        }
    }
}
