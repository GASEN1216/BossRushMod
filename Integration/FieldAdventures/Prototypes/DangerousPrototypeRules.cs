using System;

namespace BossRush
{
    /// <summary>
    /// 叮当的试用品。前两种数值同步加强，后三种 2026-10-24 新增；枚举只追加，不改既有数值。
    /// 每趟出击的试验台只放一种（由运行时按 <see cref="RandomKind"/> 抽），名称与说明见 <see cref="DangerousPrototypeCatalog"/>。
    /// </summary>
    internal enum DangerousPrototypeKind { JetCannon = 0, RicochetSun = 1, QuackRail = 2, Thunderbill = 3, GrandFinale = 4 }

    /// <summary>单次出击的借用与验收；与 Unity 分离，完成不等于已领奖。</summary>
    internal sealed class DangerousPrototypeRules
    {
        internal const float TrialSeconds = 150f;
        internal const int KindCount = 5;
        private int claimedMask;
        internal bool Active { get; private set; }
        internal bool Completed { get; private set; }
        internal DangerousPrototypeKind Kind { get; private set; }
        internal int Progress { get; private set; }
        internal float Remaining { get; private set; }
        internal float Charge { get; private set; }
        internal int Target { get { return TargetOf(Kind); } }
        internal float RechargeSeconds { get { return RechargeOf(Kind); } }

        internal static bool IsKnown(DangerousPrototypeKind kind) { return (int)kind >= 0 && (int)kind < KindCount; }

        /// <summary>验收目标：倒鸭一号与轨道炮数实际命中，折射太阳只数弹墙后的命中，雷霆数链上每一跳，烟花数每颗子弹的命中。</summary>
        internal static int TargetOf(DangerousPrototypeKind kind)
        {
            switch (kind)
            {
                case DangerousPrototypeKind.JetCannon: return 6;
                case DangerousPrototypeKind.RicochetSun: return 3;
                case DangerousPrototypeKind.QuackRail: return 6;
                case DangerousPrototypeKind.Thunderbill: return 12;
                case DangerousPrototypeKind.GrandFinale: return 10;
                default: return 1;
            }
        }

        internal static float RechargeOf(DangerousPrototypeKind kind)
        {
            switch (kind)
            {
                case DangerousPrototypeKind.JetCannon: return 2f;
                case DangerousPrototypeKind.RicochetSun: return 1.4f;
                case DangerousPrototypeKind.QuackRail: return 3.5f;
                case DangerousPrototypeKind.Thunderbill: return 1.2f;
                case DangerousPrototypeKind.GrandFinale: return 3f;
                default: return 2f;
            }
        }

        /// <summary>把 [0,1) 的随机数映射成一种试用品（每趟出击只抽一次）。</summary>
        internal static DangerousPrototypeKind RandomKind(float roll)
        {
            if (float.IsNaN(roll) || roll < 0f) roll = 0f;
            int index = Math.Min(KindCount - 1, (int)(roll * KindCount));
            return (DangerousPrototypeKind)index;
        }

        internal bool CanBorrow(DangerousPrototypeKind kind)
        {
            return IsKnown(kind) && !Active && !Completed && (claimedMask & (1 << (int)kind)) == 0;
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

        /// <summary>
        /// 单次命中伤害。bounces 对折射太阳是弹墙次数，对雷霆是链上第几跳（0 起），对烟花 0 = 主弹、1 = 子弹。
        /// </summary>
        internal static float Damage(DangerousPrototypeKind kind, int bounces)
        {
            switch (kind)
            {
                case DangerousPrototypeKind.JetCannon: return 140f;
                case DangerousPrototypeKind.RicochetSun: return bounces > 0 ? 160f + 25f * Math.Min(bounces - 1, 4) : 30f;
                case DangerousPrototypeKind.QuackRail: return 260f;
                case DangerousPrototypeKind.Thunderbill: return Math.Max(40f, 70f - 6f * bounces);
                case DangerousPrototypeKind.GrandFinale: return bounces > 0 ? 45f : 110f;
                default: return 0f;
            }
        }

        // 形状参数：运行时的射线、链、抛物线都读这里，离线规则测试也能核对。
        internal const float JetRange = 26f, JetRadius = 1.3f;
        internal const float RicochetRange = 60f;
        internal const int RicochetBounces = 5;
        internal const float RailRange = 70f, RailRadius = 0.7f;
        internal const float ThunderReach = 24f, ThunderHopRange = 9f;
        internal const int ThunderLinks = 7;
        internal const float FinaleMaxRange = 30f, FinaleFlightSeconds = 0.85f, FinaleMainRadius = 3.4f, FinaleBombletRadius = 2.6f;
        internal const int FinaleBomblets = 12;
    }
}
