// ============================================================================
// PerformanceTierAdjuster.cs - 通用性能层级判定
// ============================================================================
// 模块说明：
//   根据当前层级 + 实时对象计数 + 各档阈值（含 hysteresis），决定下一层级。
//   原本 ZombieMode 内部的 EvaluateZombieModePerformanceTier 提取为通用工具，
//   方便 ModeD / ModeE 等模式在大量怪堆叠时也能拿到"层级化降低强度"的能力。
//
// 设计要点：
//   - 与具体业务（spawn multiplier、回收策略）解耦：本工具只回答"现在该处于哪一层"。
//     调用方拿到层级后，按自己的策略调节刷怪频率、回收远端、降低词缀触发等。
//   - hysteresis：避免在阈值边缘抖动。从高层级降到低层级需要数值额外下降 hysteresis。
//   - Tier 枚举的整数值与 ZombieModePerformanceTier 一致，可 (int) 互转。
// ============================================================================

namespace BossRush
{
    internal static class PerformanceTierAdjuster
    {
        internal enum Tier
        {
            Normal = 0,
            Watch = 1,
            SoftProtect = 2,
            ExtremeProtect = 3,
        }

        internal struct Thresholds
        {
            public int Watch;
            public int Soft;
            public int Extreme;
            public int Hysteresis;
        }

        internal static Tier Evaluate(Tier current, int count, Thresholds t)
        {
            int hyst = t.Hysteresis < 0 ? 0 : t.Hysteresis;

            if (count >= t.Extreme ||
                (current == Tier.ExtremeProtect && count > t.Extreme - hyst - 1))
            {
                return Tier.ExtremeProtect;
            }

            if (count >= t.Soft ||
                (current == Tier.SoftProtect && count > t.Soft - hyst - 1))
            {
                return Tier.SoftProtect;
            }

            if (count >= t.Watch ||
                (current == Tier.Watch && count > t.Watch - hyst - 1))
            {
                return Tier.Watch;
            }

            return Tier.Normal;
        }
    }
}
