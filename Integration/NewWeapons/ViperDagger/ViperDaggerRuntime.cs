// ============================================================================
// ViperDaggerRuntime.cs - 毒蛇匕首运行时逻辑
// ============================================================================
// 模块说明：
//   监听 Health.OnHurt 事件，当玩家持有毒蛇匕首命中敌人时：
//   - 追踪每个敌人的毒素叠层数
//   - 叠满时触发爆发伤害
//   - 毒素有持续时间，超时自动清除
//   性能优化：不持有匕首时回调立即返回（零开销）
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;

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

            // 检查受击目标
            if (targetHealth == null || targetHealth.IsDead) return;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            if (damageInfo.fromCharacter != player) return;

            // 确认玩家持有毒蛇匕首
            if (!IsHoldingViperDagger(player)) return;

            // 获取目标实例ID
            int targetId = targetHealth.GetInstanceID();

            // 定期清理过期的毒素状态
            if (Time.time - lastCleanupTime > CleanupInterval)
            {
                CleanupExpiredStates();
                lastCleanupTime = Time.time;
            }

            // 叠加毒素
            ApplyPoisonStack(targetId, targetHealth, player);
        }

        /// <summary>
        /// 对目标叠加一层毒素
        /// </summary>
        private static void ApplyPoisonStack(int targetId, Health targetHealth, CharacterMainControl player)
        {
            PoisonState state;
            if (!poisonStates.TryGetValue(targetId, out state))
            {
                state = new PoisonState { layers = 0, lastApplyTime = 0f };
            }

            // 检查是否过期（超时则重置层数）
            if (state.layers > 0 && Time.time - state.lastApplyTime > ViperDaggerConfig.PoisonDuration)
            {
                state.layers = 0;
            }

            // 叠加
            state.layers = Mathf.Min(state.layers + 1, ViperDaggerConfig.MaxPoisonLayers);
            state.lastApplyTime = Time.time;
            poisonStates[targetId] = state;

            // 满层爆发
            if (state.layers >= ViperDaggerConfig.MaxPoisonLayers)
            {
                TriggerBurst(targetHealth, player);

                // 爆发后重置层数
                state.layers = 0;
                state.lastApplyTime = Time.time;
                poisonStates[targetId] = state;
            }
        }

        /// <summary>
        /// 满层爆发伤害
        /// </summary>
        private static void TriggerBurst(Health targetHealth, CharacterMainControl player)
        {
            try
            {
                if (player == null || targetHealth == null || targetHealth.IsDead) return;

                // 构造爆发伤害信息
                DamageInfo burstDamage = new DamageInfo(player);
                burstDamage.damageValue = ViperDaggerConfig.BurstDamageOnMaxStack;
                burstDamage.fromWeaponItemID = 0; // 设为0避免再次触发叠毒回调
                burstDamage.AddElementFactor(ElementTypes.poison, 1f);
                burstDamage.damagePoint = targetHealth.transform.position;
                burstDamage.damageType = DamageTypes.normal;

                targetHealth.Hurt(burstDamage);

                // 显示爆发气泡
                ShowBurstBubble(player);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ViperDaggerConfig.LogPrefix + " 爆发伤害异常: " + e.Message);
            }
        }

        /// <summary>
        /// 显示毒性爆发气泡
        /// </summary>
        private static void ShowBurstBubble(CharacterMainControl player)
        {
            if (player == null || player.transform == null) return;

            try
            {
                string text = L10n.T(
                    "<color=#7CFC00>毒性爆发！</color>",
                    "<color=#7CFC00>Toxic Burst!</color>");

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

        /// <summary>
        /// 检查玩家是否持有毒蛇匕首
        /// </summary>
        private static bool IsHoldingViperDagger(CharacterMainControl character)
        {
            if (character == null) return false;

            try
            {
                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee != null && melee.Item != null &&
                    melee.Item.TypeID == NewWeaponIds.ViperDaggerTypeId)
                {
                    return true;
                }

                DuckovItemAgent holdAgent = character.CurrentHoldItemAgent;
                if (holdAgent != null && holdAgent.Item != null &&
                    holdAgent.Item.TypeID == NewWeaponIds.ViperDaggerTypeId)
                {
                    return true;
                }
            }
            catch  { /* best-effort fallback intentionally ignored */ }

            return false;
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
