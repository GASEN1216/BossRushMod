// ============================================================================
// ViperDaggerRuntime.cs - 毒蛇匕首运行时逻辑
// ============================================================================
// 模块说明：
//   监听 Health.OnHurt 事件，当玩家持有毒蛇匕首命中敌人时：
//   - 追踪每个敌人的毒素叠层数与本轮累计实伤
//   - 叠满 5 层时触发爆发伤害（固定值 + 本轮累计实伤的一个比例），并清空层数
//   - 毒素有持续时间，超时自动清除
//
// 性能（AGENTS §4.12）：
//   OnHurt 第一行就用 fromWeaponItemID != TypeId 早返，非本武器造成的伤害零开销；
//   「是否真的手持匕首」走 NewWeaponEquipState 的 O(1) 缓存，不在受击回调里遍历槽位。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 毒蛇匕首运行时 - 叠毒与爆发机制
    /// </summary>
    public static class ViperDaggerRuntime
    {
        // 每个敌人的毒素状态
        private struct PoisonState
        {
            public int layers;
            public float lastApplyTime;
            /// <summary>本轮（当前这几层）匕首对该目标造成的实际伤害合计，用于让爆发随 build 成长。</summary>
            public float accumulatedDamage;
        }

        // 敌人实例ID -> 毒素状态
        private static readonly Dictionary<int, PoisonState> poisonStates = new Dictionary<int, PoisonState>();
        private static readonly List<int> poisonStateRemovalScratch = new List<int>(16);

        // 事件订阅状态
        private static bool isSubscribed;

        // 清理计时器（避免每帧遍历字典）
        private static float lastCleanupTime;
        private const float CleanupInterval = 3f;

        /// <summary>
        /// 订阅伤害事件（在武器系统初始化时调用）
        /// </summary>
        public static void Subscribe()
        {
            if (isSubscribed) return;

            Health.OnHurt += OnHurt;
            isSubscribed = true;
            ModBehaviour.DevLog(ViperDaggerConfig.LogPrefix + " 运行时已订阅");
        }

        /// <summary>
        /// 取消订阅（在 Mod 卸载时调用）
        /// </summary>
        public static void Unsubscribe()
        {
            if (!isSubscribed) return;

            Health.OnHurt -= OnHurt;
            isSubscribed = false;
            poisonStates.Clear();
            poisonStateRemovalScratch.Clear();
            ModBehaviour.DevLog(ViperDaggerConfig.LogPrefix + " 运行时已取消订阅");
        }

        /// <summary>
        /// 重置所有状态（场景切换时调用）
        /// </summary>
        public static void ResetStaticCaches()
        {
            poisonStates.Clear();
            poisonStateRemovalScratch.Clear();
            lastCleanupTime = 0f;
        }

        /// <summary>
        /// Health.OnHurt 回调（签名：Action&lt;Health, DamageInfo&gt;）
        /// </summary>
        private static void OnHurt(Health targetHealth, DamageInfo damageInfo)
        {
            // 早期退出：检查是否是玩家用毒蛇匕首造成的伤害
            if (damageInfo.fromWeaponItemID != NewWeaponIds.ViperDaggerTypeId) return;

            // 检查受击目标。致死那一击官方先发 OnDead 再发 OnHurt，IsDead 已为 true，
            // 在这里被挡掉——死人不该再吃一层毒。
            if (targetHealth == null || targetHealth.IsDead) return;

            // 队友与中立物件（可破坏箱、木桶）不叠毒：它们的 Health 也会进这条多播链
            if (!NewWeaponAttribution.IsHostileVictim(targetHealth)) return;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            if (damageInfo.fromCharacter != player) return;

            // 确认玩家手上拿的确实是毒蛇匕首（O(1) 读缓存，不遍历槽位）
            if (!NewWeaponEquipState.IsHolding(NewWeaponIds.ViperDaggerTypeId)) return;

            // 获取目标实例ID
            int targetId = targetHealth.GetInstanceID();

            // 定期清理过期的毒素状态
            if (Time.time - lastCleanupTime > CleanupInterval)
            {
                CleanupExpiredStates();
                lastCleanupTime = Time.time;
            }

            // 叠加毒素
            ApplyPoisonStack(targetId, targetHealth, player, damageInfo.finalDamage);
        }

        /// <summary>
        /// 对目标叠加一层毒素
        /// </summary>
        private static void ApplyPoisonStack(int targetId, Health targetHealth, CharacterMainControl player, float finalDamage)
        {
            PoisonState state;
            if (!poisonStates.TryGetValue(targetId, out state))
            {
                state = new PoisonState { layers = 0, lastApplyTime = 0f, accumulatedDamage = 0f };
            }

            // 检查是否过期（超时则重置层数与累计伤害）
            if (state.layers > 0 && Time.time - state.lastApplyTime > ViperDaggerConfig.PoisonDuration)
            {
                state.layers = 0;
                state.accumulatedDamage = 0f;
            }

            // 叠加
            state.layers = Mathf.Min(state.layers + 1, ViperDaggerConfig.MaxPoisonLayers);
            state.lastApplyTime = Time.time;
            if (finalDamage > 0f)
            {
                state.accumulatedDamage += finalDamage;
            }
            poisonStates[targetId] = state;

            // 满层爆发
            if (state.layers >= ViperDaggerConfig.MaxPoisonLayers)
            {
                TriggerBurst(targetHealth, player, state.accumulatedDamage);

                // 爆发后重置层数与累计伤害：下一轮从零开始重新滚
                state.layers = 0;
                state.accumulatedDamage = 0f;
                state.lastApplyTime = Time.time;
                poisonStates[targetId] = state;
            }
        }

        /// <summary>
        /// 满层爆发伤害。
        /// 伤害 = 固定底 <see cref="ViperDaggerConfig.BurstDamageOnMaxStack"/>
        ///      + 本轮 5 次命中对该目标累计实伤 × <see cref="ViperDaggerConfig.BurstAccumulatedDamageRatio"/>。
        /// 固定底保证前期与旧版一致，比例项让它在暴击 / 词缀 / 套装堆起来之后不会退化成零头。
        /// </summary>
        private static void TriggerBurst(Health targetHealth, CharacterMainControl player, float accumulatedDamage)
        {
            try
            {
                if (player == null || targetHealth == null || targetHealth.IsDead) return;

                float burstDamage = ViperDaggerConfig.BurstDamageOnMaxStack;
                if (accumulatedDamage > 0f)
                {
                    burstDamage += accumulatedDamage * ViperDaggerConfig.BurstAccumulatedDamageRatio;
                }
                if (burstDamage <= 0f) return;

                // 构造爆发伤害信息
                Vector3 burstPoint = targetHealth.transform.position;

                DamageInfo burstDamageInfo = new DamageInfo(player);
                burstDamageInfo.damageValue = burstDamage;
                burstDamageInfo.fromWeaponItemID = 0; // 设为0避免再次触发叠毒回调
                // 自建伤害必须显式标记为 buff/效果通道（AGENTS.md 4.x 自建伤害约定）：
                // 否则毒爆发击杀会被冰葬/引雷术等「只认 !isFromBuffOrEffect 的直接击杀」的系统当成直接击杀起链，
                // 也与 ModeGWeaponScoringCompatibilityMatrix 里 ViperDagger_PoisonBurst_BuffEffect 的登记口径不符。
                burstDamageInfo.isFromBuffOrEffect = true;
                burstDamageInfo.AddElementFactor(ElementTypes.poison, 1f);
                burstDamageInfo.damagePoint = burstPoint;
                burstDamageInfo.damageType = DamageTypes.normal;

                targetHealth.Hurt(burstDamageInfo);

                // 表现层：毒雾爆环 + 音效（配色取自描述文案的 #7CFC00）。
                // 不再弹对话气泡：满攻速下这条每 2 秒多就会刷一次，会长期糊在角色头顶。
                NewWeaponFx.PlayBurst(burstPoint, NewWeaponPalette.VenomCore, 2.2f, 0.45f, 6);
                NewWeaponFx.PlaySound(NewWeaponSfx.VenomBurst);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ViperDaggerConfig.LogPrefix + " 爆发伤害异常: " + e.Message);
            }
        }

        /// <summary>
        /// 清理过期的毒素状态
        /// </summary>
        private static void CleanupExpiredStates()
        {
            if (poisonStates.Count == 0) return;

            float now = Time.time;
            poisonStateRemovalScratch.Clear();

            foreach (var kvp in poisonStates)
            {
                if (now - kvp.Value.lastApplyTime > ViperDaggerConfig.PoisonDuration + 2f)
                {
                    poisonStateRemovalScratch.Add(kvp.Key);
                }
            }

            if (poisonStateRemovalScratch.Count > 0)
            {
                for (int i = 0; i < poisonStateRemovalScratch.Count; i++)
                {
                    poisonStates.Remove(poisonStateRemovalScratch[i]);
                }
            }

            poisonStateRemovalScratch.Clear();
        }
    }
}
