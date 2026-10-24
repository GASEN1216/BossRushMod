using System;

namespace BossRush
{
    internal enum DangerousPrototypeKind { JetCannon, RicochetSun }

    /// <summary>单次出击的借用与验收；与 Unity 分离，完成不等于已领奖。</summary>
    internal sealed class DangerousPrototypeRules
    {
        internal const float TrialSeconds = 150f;
        private int claimedMask;
        internal bool Active { get; private set; }
        internal bool Completed { get; private set; }
        internal DangerousPrototypeKind Kind { get; private set; }
        internal int Progress { get; private set; }
        internal float Remaining { get; private set; }
        internal float Charge { get; private set; }
        internal int Target { get { return Kind == DangerousPrototypeKind.JetCannon ? 6 : 3; } }
        internal float RechargeSeconds { get { return Kind == DangerousPrototypeKind.JetCannon ? 2.5f : 1.6f; } }

        internal bool CanBorrow(DangerousPrototypeKind kind)
        {
            return (kind == DangerousPrototypeKind.JetCannon || kind == DangerousPrototypeKind.RicochetSun)
                && !Active && !Completed && (claimedMask & (1 << (int)kind)) == 0;
        }
        internal bool Borrow(DangerousPrototypeKind kind)
        {
            if (!CanBorrow(kind)) return false;
            Kind = kind; Active = true; Completed = false; Progress = 0;
            Remaining = TrialSeconds; Charge = 1f;
            return true;
        }
        internal bool Tick(float elapsed, bool held)
        {
            if (!Active || float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed <= 0f) return false;
            Remaining = Math.Max(0f, Remaining - elapsed);
            if (held) Charge = Math.Min(1f, Charge + elapsed / RechargeSeconds);
            if (Remaining > 0f) return false;
            Cancel();
            return true;
        }
        internal bool TryFire()
        {
            if (!Active || Charge < 1f) return false;
            Charge = 0f;
            return true;
        }
        internal bool ObserveHit(bool reflected)
        {
            if (!Active || (Kind == DangerousPrototypeKind.RicochetSun && !reflected)) return false;
            Progress = Math.Min(Target, Progress + 1);
            if (Progress < Target) return false;
            Active = false; Completed = true;
            return true;
        }
        internal bool Claim()
        {
            if (!Completed) return false;
            claimedMask |= 1 << (int)Kind;
            Completed = false;
            return true;
        }
        internal void Cancel() { Active = false; Completed = false; Charge = 0f; Remaining = 0f; Progress = 0; }
        internal void ResetRaid() { Cancel(); claimedMask = 0; }
        internal static float Damage(DangerousPrototypeKind kind, int bounces)
        {
            return kind == DangerousPrototypeKind.JetCannon ? 85f : (bounces > 0 ? 110f : 22f);
        }
    }
}
