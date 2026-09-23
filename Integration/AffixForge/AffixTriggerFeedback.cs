// ============================================================================
// AffixTriggerFeedback.cs - 词缀触发的可见反馈：汲血飘字、灌能电弧、荆棘反震环
// ============================================================================
// 为什么有这一层（2026-09-23 特效审美审查 VA-24）：
//   汲血、灌能、荆棘触发时玩家什么都看不到——回血没有数字，追加电击只有官方伤害数字，
//   反弹伤害打在攻击者身上也没有任何表示，词缀像是没生效。
//   这里只做表现：由 AffixRuntimeService_Effects 在效果结算之后各调一行，不参与伤害、判定与冷却。
//
// 口径：
//   - 飘字走官方 FX.PopText，颜色用 BossRushUIColors.SuccessText（不用荧光纯绿）；
//   - 电弧 / 反震环复用 NewWeaponFx 的共享电弧池与爆发环（不开灯），不另起材质；
//   - 节流防刷屏：词缀本身有 0.25–0.4 秒冷却，但几件装备可以叠同一条词缀，这里再按类型限一次频率；
//     汲血的回血量在节流窗口内累加，一秒最多弹一次「+N」。
//   - 只在词缀真的触发时调用（词缀只在装备穿戴 / 手持时激活，AGENTS 4.12），没有逐帧工作；
//     状态只有几个时间戳与累加量，按游戏时间（Time.time）走，暂停时不推进。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>词缀触发反馈。全部 no-throw：表现失败不影响词缀结算。</summary>
    internal static class AffixTriggerFeedback
    {
        private const float LifestealPopInterval = 1f;
        /// <summary>累加的回血超过这么久没弹出就作废（离开战斗后不再补弹旧数字）。</summary>
        private const float LifestealPendingExpire = 3f;
        private const float LifestealPopHeight = 2f;
        private const float LifestealPopSize = 1f;

        private const float OverchargeInterval = 0.15f;
        private const float OverchargeArcLength = 0.4f;
        private const float OverchargeArcWidth = 0.03f;
        private const float OverchargeArcLife = 0.08f;

        private const float ThornsInterval = 0.25f;
        private const float ThornsRadius = 0.6f;
        private const float ThornsLife = 0.25f;
        private static readonly Color ThornsColor = new Color(0.62f, 0.16f, 0.14f, 0.85f);

        private static float _nextLifestealPopAt;
        private static float _lastLifestealAt = -100f;
        private static float _pendingHeal;
        private static float _nextOverchargeAt;
        private static float _nextThornsAt;

        /// <summary>汲血回了 healed 点血（实际回复量，满血时为 0）：累加，一秒最多弹一次「+N」。</summary>
        internal static void OnLifesteal(CharacterMainControl main, float healed)
        {
            try
            {
                if (main == null || healed <= 0f) return;
                float now = Time.time;
                if (now - _lastLifestealAt > LifestealPendingExpire) _pendingHeal = 0f;
                _lastLifestealAt = now;
                _pendingHeal += healed;
                if (now < _nextLifestealPopAt || _pendingHeal < 0.5f) return;

                _nextLifestealPopAt = now + LifestealPopInterval;
                string text = "+" + Mathf.RoundToInt(_pendingHeal).ToString();
                _pendingHeal = 0f;
                if (FX.PopText.instance == null) return;
                FX.PopText.Pop(text, main.transform.position + Vector3.up * LifestealPopHeight,
                    BossRushUIColors.SuccessText, LifestealPopSize, null);
            }
            catch (Exception)
            {
                // 纯表现，命中热路径上不刷日志
            }
        }

        /// <summary>灌能追加电击：在命中点画一道 0.4 m 的短电弧（没有命中点时退到受击者胸口）。</summary>
        internal static void OnOvercharge(Health victim, Vector3 hitPoint)
        {
            try
            {
                if (victim == null) return;
                float now = Time.time;
                if (now < _nextOverchargeAt) return;
                _nextOverchargeAt = now + OverchargeInterval;

                Vector3 center = hitPoint;
                if (center.sqrMagnitude < 0.0001f) center = victim.transform.position + Vector3.up;
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 half = new Vector3(Mathf.Cos(angle), UnityEngine.Random.Range(-0.35f, 0.35f), Mathf.Sin(angle))
                    .normalized * (OverchargeArcLength * 0.5f);
                NewWeaponFx.PlayArc(center - half, center + half, NewWeaponPalette.ThunderCore,
                    OverchargeArcWidth, OverchargeArcLife);
            }
            catch (Exception)
            {
                // 纯表现，命中热路径上不刷日志
            }
        }

        /// <summary>荆棘反弹：在攻击者脚下放一个半径 0.6 m 的暗红反震环（不开灯）。</summary>
        internal static void OnThorns(CharacterMainControl attacker)
        {
            try
            {
                if (attacker == null) return;
                float now = Time.time;
                if (now < _nextThornsAt) return;
                _nextThornsAt = now + ThornsInterval;
                NewWeaponFx.PlayBurst(attacker.transform.position + Vector3.up * 0.1f, ThornsColor,
                    ThornsRadius, ThornsLife, 0, false);
            }
            catch (Exception)
            {
                // 纯表现，受击热路径上不刷日志
            }
        }
    }
}
