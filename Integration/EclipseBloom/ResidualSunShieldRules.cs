using System;

namespace BossRush
{
    /// <summary>无宿主依赖的单次护盾与单份充能。取消只清待用效果，保留已经提交的冷却。</summary>
    internal sealed class ResidualSunShieldRules
    {
        internal float GuardUntil { get; private set; }
        internal float ChargeUntil { get; private set; }
        internal float ChargeDamage { get; private set; }
        internal float NextTriggerAt { get; private set; }

        internal bool HasGuard(float now) { return IsValidTime(now) && GuardUntil > now; }
        internal bool HasCharge(float now) { return IsValidTime(now) && ChargeUntil > now && ChargeDamage > 0f; }

        internal bool TryStartGuard(float now)
        {
            if (!IsValidTime(now) || now < NextTriggerAt) return false;
            float guardUntil = now + ResidualSunShieldConfig.GuardSeconds;
            float next = now + ResidualSunShieldConfig.CooldownSeconds;
            if (!IsFinite(guardUntil) || guardUntil <= now || !IsFinite(next) || next <= now) return false;
            GuardUntil = guardUntil;
            NextTriggerAt = next;
            return true;
        }

        internal float TryBlock(float damage, float now)
        {
            if (!HasGuard(now) || !IsFinite(damage) || damage <= 0f) return 0f;
            float chargeUntil = now + ResidualSunShieldConfig.ChargeSeconds;
            if (!IsFinite(chargeUntil) || chargeUntil <= now) return 0f;
            float absorbed = Math.Min(damage * ResidualSunShieldConfig.AbsorptionRatio,
                ResidualSunShieldConfig.MaximumAbsorption);
            if (!(absorbed > 0f)) return 0f;
            GuardUntil = 0f;
            ChargeDamage = absorbed;
            ChargeUntil = chargeUntil;
            return absorbed;
        }

        internal float TryConsumeCharge(float damage, float now)
        {
            if (!HasCharge(now) || !IsFinite(damage) || damage <= 0f
                || !IsFinite(damage + ChargeDamage)) return 0f;
            float bonus = ChargeDamage;
            ChargeDamage = 0f;
            ChargeUntil = 0f;
            return bonus;
        }

        internal void CancelPending() { GuardUntil = 0f; ChargeUntil = 0f; ChargeDamage = 0f; }
        internal void ResetAll() { CancelPending(); NextTriggerAt = 0f; }

        internal static bool IsFrontal(float facingX, float facingZ, float deltaX, float deltaZ)
        {
            if (!IsFinite(facingX) || !IsFinite(facingZ) || !IsFinite(deltaX) || !IsFinite(deltaZ)) return false;
            // double 防止有限但极大的坐标在平方时变成 Infinity，从而误判方向。
            double facingSq = (double)facingX * facingX + (double)facingZ * facingZ;
            double deltaSq = (double)deltaX * deltaX + (double)deltaZ * deltaZ;
            double dot = (double)facingX * deltaX + (double)facingZ * deltaZ;
            return facingSq >= 0.0001 && deltaSq >= 0.0001 && dot > 0.0
                && dot * dot >= facingSq * deltaSq * ResidualSunShieldConfig.FrontalCosine
                    * ResidualSunShieldConfig.FrontalCosine;
        }

        internal static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool IsValidTime(float now) { return IsFinite(now) && now >= 0f; }
    }
}
