using System;

namespace BossRush
{
    internal enum TidegateTidePhase { Ebb, Low, Flood, High }

    /// <summary>
    /// 潮闸镇的潮汐规则（纯函数，不读 Unity 状态，离线可测）。潮位挂官方游戏时钟：一轮 4.5 个游戏小时
    /// （官方 `clockTimeScale` 60 时约 4.5 现实分钟），退潮 0.5 h → 低潮 2 h → 涨潮 0.5 h → 高潮 1.5 h，
    /// 与 three.js 原型的 26 / 120 / 30 / 100 秒同比例。进图时刻不同，潮位就不同，同一张图每趟路线不一样。
    /// 水位单位是世界 y（米），与场景包、着色器全局量 `_TidegateTide` / `_TidegateLagoon` 同一口径。
    /// </summary>
    internal static class TidegateTideRules
    {
        internal const double CycleHours = 4.5;
        internal const double EbbHours = 0.5, LowHours = 2.0, FloodHours = 0.5;
        internal const float LowLevel = -1.5f, HighLevel = 0.45f;
        /// <summary>大闸关着时泻湖的水位：比外海高潮还高，金库所在的储蓄所整栋泡在水下。</summary>
        internal const float LagoonFull = 0.62f;
        /// <summary>大闸提起后泻湖放水的速度（米 / 现实秒），放到和外海齐平后跟着外海涨落。</summary>
        internal const float LagoonDrainRate = 0.085f;
        /// <summary>湿痕线回落速度（米 / 现实秒）：水退了，地面还湿一阵。</summary>
        internal const float WetLineFallRate = 0.02f;
        /// <summary>深于这个水深算深水：泡在深水里的箱子不能搜，敌人的导航三角形也按它判淹没。</summary>
        internal const float DeepWater = 0.5f;
        /// <summary>排水暗渠出口露出水面的判据（米）：水深低于它才能从暗渠撤离。</summary>
        internal const float DrainExposedDepth = 0.25f;
        /// <summary>官方一天的游戏秒数（反编译源 `GameClock.SecondsPerDay`，是 86300 不是 86400）。</summary>
        internal const double OfficialSecondsPerDay = 86300.0;

        /// <summary>
        /// 潮汐用的跨天连续时钟（游戏小时）：天数 × 86300 + 当天秒数。官方 `TimeOfDay` 每天归零，24 又不是 4.5 的整数倍，
        /// 直接拿它算，午夜会从低潮一步跳回高潮起点（滩涂上的人当场被淹，也没有涨潮过程）；`GameClock.Now` 按 24 小时折天，
        /// 跨天也会跳 100 秒。读不到时钟时传 NaN，原样返回 NaN。
        /// </summary>
        internal static double ElapsedHours(long day, double secondsOfDay)
        {
            if (double.IsNaN(secondsOfDay) || double.IsInfinity(secondsOfDay)) return double.NaN;
            return (Math.Max(0L, day) * OfficialSecondsPerDay + secondsOfDay) / 3600.0;
        }

        internal static double CycleOffset(double clockHours)
        {
            if (double.IsNaN(clockHours) || double.IsInfinity(clockHours)) return 0.0;
            double t = clockHours % CycleHours;
            return t < 0 ? t + CycleHours : t;
        }

        internal static TidegateTidePhase PhaseAt(double clockHours)
        {
            double t = CycleOffset(clockHours);
            if (t < EbbHours) return TidegateTidePhase.Ebb;
            if (t < EbbHours + LowHours) return TidegateTidePhase.Low;
            if (t < EbbHours + LowHours + FloodHours) return TidegateTidePhase.Flood;
            return TidegateTidePhase.High;
        }

        /// <summary>外海潮位。涨落两段用 smoothstep，平潮段恒定。时钟不可用（NaN）时按高潮算。</summary>
        internal static float LevelAt(double clockHours)
        {
            if (double.IsNaN(clockHours)) return HighLevel;
            double t = CycleOffset(clockHours);
            if (t < EbbHours) return Lerp(HighLevel, LowLevel, Smooth(t / EbbHours));
            if (t < EbbHours + LowHours) return LowLevel;
            if (t < EbbHours + LowHours + FloodHours) return Lerp(LowLevel, HighLevel, Smooth((t - EbbHours - LowHours) / FloodHours));
            return HighLevel;
        }

        /// <summary>到下一次换相还有几个游戏小时（HUD 提示「多久涨潮」用）。</summary>
        internal static double HoursUntilNextPhase(double clockHours)
        {
            double t = CycleOffset(clockHours);
            double[] edges = { EbbHours, EbbHours + LowHours, EbbHours + LowHours + FloodHours, CycleHours };
            foreach (double edge in edges) if (t < edge) return edge - t;
            return CycleHours - t;
        }

        /// <summary>泻湖水位推进一帧：大闸关着恒为满湖；提起后按放水速度降到外海潮位，之后贴着外海走。</summary>
        internal static float StepLagoon(float lagoon, float tide, bool sluiceOpen, float realDeltaSeconds)
        {
            if (!sluiceOpen) return LagoonFull;
            if (lagoon <= tide) return tide;
            return Math.Max(tide, lagoon - LagoonDrainRate * Math.Max(0f, realDeltaSeconds));
        }

        /// <summary>湿痕线：水涨立刻跟上，水退慢慢回落。</summary>
        internal static float StepWetLine(float wetLine, float level, float realDeltaSeconds)
        {
            if (level >= wetLine) return level;
            return Math.Max(level, wetLine - WetLineFallRate * Math.Max(0f, realDeltaSeconds));
        }

        internal static float Depth(float waterLevel, float groundY) { return waterLevel - groundY; }

        internal static bool IsDeep(float waterLevel, float groundY) { return waterLevel - groundY > DeepWater; }

        private static float Smooth(double x)
        {
            float t = (float)Math.Max(0.0, Math.Min(1.0, x));
            return t * t * (3f - 2f * t);
        }

        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
    }
}
