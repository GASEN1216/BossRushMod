using System;

namespace BossRush
{
    /// <summary>同一枚炸弹只由当前携带者传出；接力不刷新引信，取消不退还触发冷却。</summary>
    internal sealed class PassTheBombRules
    {
        private readonly float _fuseSeconds;
        private readonly float _cooldownSeconds;

        internal bool IsArmed { get; private set; }
        internal int CarrierId { get; private set; }
        internal float DetonateAt { get; private set; }
        internal float NextTriggerAt { get; private set; }

        internal PassTheBombRules(float fuseSeconds, float cooldownSeconds)
        {
            if (!IsFinite(fuseSeconds) || fuseSeconds <= 0f)
                throw new ArgumentOutOfRangeException("fuseSeconds");
            if (!IsFinite(cooldownSeconds) || cooldownSeconds <= 0f)
                throw new ArgumentOutOfRangeException("cooldownSeconds");
            _fuseSeconds = fuseSeconds;
            _cooldownSeconds = cooldownSeconds;
        }

        internal bool TryArm(int victimId, float now)
        {
            if (IsArmed || victimId == 0 || !IsValidTime(now) || now < NextTriggerAt)
                return false;

            float detonateAt = now + _fuseSeconds;
            float nextTriggerAt = now + _cooldownSeconds;
            // 有限输入仍可能相加溢出，或因 float 精度不能前进；失败必须保留原状态。
            if (!IsFinite(detonateAt) || detonateAt <= now
                || !IsFinite(nextTriggerAt) || nextTriggerAt <= now)
                return false;

            CarrierId = victimId;
            DetonateAt = detonateAt;
            NextTriggerAt = nextTriggerAt;
            IsArmed = true;
            return true;
        }

        internal bool TryTransfer(int attackerId, int victimId, float now)
        {
            if (!IsArmed || !IsValidTime(now) || now >= DetonateAt
                || attackerId != CarrierId || victimId == 0 || victimId == CarrierId)
                return false;

            CarrierId = victimId;
            return true;
        }

        internal bool IsDue(float now)
        {
            return IsArmed && IsValidTime(now) && now >= DetonateAt;
        }

        internal float Remaining(float now)
        {
            if (!IsArmed || !IsValidTime(now)) return 0f;
            return Math.Max(0f, DetonateAt - now);
        }

        internal void Complete()
        {
            IsArmed = false;
            CarrierId = 0;
            DetonateAt = 0f;
        }

        internal void ResetSession()
        {
            Complete();
            NextTriggerAt = 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsValidTime(float now)
        {
            return IsFinite(now) && now >= 0f;
        }
    }
}
