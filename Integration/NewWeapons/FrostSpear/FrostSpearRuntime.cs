// ============================================================================
// FrostSpearRuntime.cs - 冰霜长矛命中表现
// ============================================================================
// 模块说明：
//   冰霜长矛的减速本身由 ItemSetting_MeleeWeapon 上的官方 Cold buff 提供，不需要运行时逻辑。
//   本类只负责命中反馈：在被刺中的目标脚下点一圈霜环，让「每次刺击都会留下寒霜印记」
//   这句描述在屏幕上真的看得见。不改伤害、不加 buff、不碰玩法结算。
//
// 性能（AGENTS.md 4.12）：
//   OnHurt 第一行就用 fromWeaponItemID != TypeId 早返，非本武器造成的伤害零开销
//   （与毒蛇匕首同款写法）。同一目标 0.35 秒内只点一次，避免高攻速下刷屏。
//   状态只有一张小字典，退订与切场景时清空。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>冰霜长矛命中表现（纯视觉）</summary>
    public static class FrostSpearRuntime
    {
        /// <summary>同一目标的霜环最小间隔，避免高攻速刷屏</summary>
        private const float PerTargetFxCooldown = 0.35f;
        /// <summary>字典清理间隔</summary>
        private const float CleanupInterval = 5f;

        private static bool isSubscribed;
        private static readonly Dictionary<int, float> lastFxTime = new Dictionary<int, float>();
        private static readonly List<int> removalScratch = new List<int>();
        private static float lastCleanupTime;

        public static void Subscribe()
        {
            if (isSubscribed) return;

            Health.OnHurt += OnHurt;
            isSubscribed = true;
            ModBehaviour.DevLog(FrostSpearConfig.LogPrefix + " 运行时已订阅");
        }

        public static void Unsubscribe()
        {
            if (!isSubscribed) return;

            Health.OnHurt -= OnHurt;
            isSubscribed = false;
            lastFxTime.Clear();
            removalScratch.Clear();
            ModBehaviour.DevLog(FrostSpearConfig.LogPrefix + " 运行时已取消订阅");
        }

        public static void ResetStaticCaches()
        {
            lastFxTime.Clear();
            removalScratch.Clear();
            lastCleanupTime = 0f;
        }

        private static void OnHurt(Health targetHealth, DamageInfo damageInfo)
        {
            // 早返：非冰霜长矛造成的伤害零开销
            if (damageInfo.fromWeaponItemID != NewWeaponIds.FrostSpearTypeId) return;
            if (targetHealth == null) return;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || damageInfo.fromCharacter != player) return;

            int targetId = targetHealth.GetInstanceID();
            float now = Time.time;

            if (now - lastCleanupTime > CleanupInterval)
            {
                CleanupExpired(now);
                lastCleanupTime = now;
            }

            float previous;
            if (lastFxTime.TryGetValue(targetId, out previous) && now - previous < PerTargetFxCooldown)
            {
                return;
            }
            lastFxTime[targetId] = now;

            try
            {
                Transform targetTransform = targetHealth.transform;
                if (targetTransform == null) return;

                // 小霜环，比毒爆发/雷击小一圈：这是每次命中都会出现的常驻反馈，不能喧宾夺主
                NewWeaponFx.PlayBurst(
                    targetTransform.position, NewWeaponPalette.FrostCore, 1.1f, 0.28f, 0, withLight: false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(FrostSpearConfig.LogPrefix + " 命中特效异常: " + e.Message);
            }
        }

        private static void CleanupExpired(float now)
        {
            removalScratch.Clear();
            foreach (KeyValuePair<int, float> entry in lastFxTime)
            {
                if (now - entry.Value > CleanupInterval)
                {
                    removalScratch.Add(entry.Key);
                }
            }

            for (int i = 0; i < removalScratch.Count; i++)
            {
                lastFxTime.Remove(removalScratch[i]);
            }
            removalScratch.Clear();
        }
    }
}
