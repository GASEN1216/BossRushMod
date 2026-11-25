using System;

namespace BossRush
{
    /// <summary>
    /// 止语钟罩的护罩状态，无宿主依赖（执行回归 SilenceBellRules 直接编译本文件）。
    /// 每次敌方命中都把回充起点顺延到「此刻 + 5 秒」；打碎那一下再顺延到「此刻 + 9 秒」，两者取晚。
    /// 回充按帧累加、只在佩戴期间驱动，所以卸下时护罩停在原样，不会靠穿脱瞬间补满。
    /// </summary>
    internal sealed class SilenceBellRules
    {
        internal float Shield { get; private set; }
        internal bool Broken { get; private set; }
        internal float RechargeAt { get; private set; }

        internal SilenceBellRules() { ResetFull(); }

        internal bool IsFull { get { return Shield >= SilenceBellConfig.Capacity; } }
        internal float Fraction { get { return Math.Max(0f, Math.Min(1f, Shield / SilenceBellConfig.Capacity)); } }

        internal bool IsRecharging(float now)
        {
            return IsValidTime(now) && !IsFull && now >= RechargeAt;
        }

        /// <summary>新场景、换主角、死亡：满罩，不带上一张图的回充计时。</summary>
        internal void ResetFull()
        {
            Shield = SilenceBellConfig.Capacity;
            Broken = false;
            RechargeAt = 0f;
        }

        /// <summary>
        /// 吃一次敌方伤害，返回挡下的量。护罩已空时也顺延回充计时（挨着打就回不起来），但返回 0。
        /// </summary>
        internal float Absorb(float damage, float now, out bool broke)
        {
            broke = false;
            if (!IsFinite(damage) || damage <= 0f || !IsValidTime(now)) return 0f;
            Delay(now + SilenceBellConfig.RechargeDelaySeconds);
            if (!(Shield > 0f)) return 0f;
            float absorbed = Math.Min(Shield, damage);
            Shield -= absorbed;
            if (Shield <= 0f)
            {
                Shield = 0f;
                Broken = true;
                broke = true;
                Delay(now + SilenceBellConfig.BrokenDelaySeconds);
            }
            return absorbed;
        }

        /// <summary>按帧回充；这一帧恰好补满时返回 true（放「叮」）。</summary>
        internal bool Tick(float now, float deltaTime)
        {
            if (!IsValidTime(now) || !IsFinite(deltaTime) || deltaTime <= 0f || IsFull || now < RechargeAt) return false;
            float step = SilenceBellConfig.RechargePerSecond * Math.Min(deltaTime, SilenceBellConfig.MaxTickSeconds);
            Shield = Math.Min(SilenceBellConfig.Capacity, Shield + step);
            if (Shield > 0f) Broken = false;
            return IsFull;
        }

        private void Delay(float until)
        {
            if (IsFinite(until) && until > RechargeAt) RechargeAt = until;
        }

        internal static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool IsValidTime(float now) { return IsFinite(now) && now >= 0f; }
    }
}
