using System;
using BossRush;

internal static class Program
{
    private static int assertions;

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }

    private static int Main()
    {
        try { Run(); }
        catch (Exception e)
        {
            Console.WriteLine("TidegateTideRules: FAIL " + e.Message);
            return 1;
        }
        Console.WriteLine("TidegateTideRules: PASS (" + assertions + " assertions, production rules)");
        return 0;
    }

    private static void Run()
    {
        // 一轮 4.5 个游戏小时：退潮 0.5 → 低潮 2 → 涨潮 0.5 → 高潮 1.5，与原型 26/120/30/100 秒同比例。
        Check(Math.Abs(TidegateTideRules.EbbHours + TidegateTideRules.LowHours + TidegateTideRules.FloodHours + 1.5 - TidegateTideRules.CycleHours) < 1e-9,
            "four phases fill exactly one cycle");
        Check(TidegateTideRules.PhaseAt(0.1) == TidegateTidePhase.Ebb, "cycle starts ebbing");
        Check(TidegateTideRules.PhaseAt(1.0) == TidegateTidePhase.Low, "low tide after the ebb");
        Check(TidegateTideRules.PhaseAt(2.7) == TidegateTidePhase.Flood, "flooding after low tide");
        Check(TidegateTideRules.PhaseAt(3.5) == TidegateTidePhase.High, "high tide closes the cycle");
        Check(TidegateTideRules.PhaseAt(4.6) == TidegateTidePhase.Ebb, "cycle repeats on the game clock");
        Check(TidegateTideRules.PhaseAt(-0.4) == TidegateTidePhase.High, "negative clock wraps instead of throwing");

        Check(Math.Abs(TidegateTideRules.LevelAt(1.5) - TidegateTideRules.LowLevel) < 1e-6, "slack low water holds the low level");
        Check(Math.Abs(TidegateTideRules.LevelAt(4.0) - TidegateTideRules.HighLevel) < 1e-6, "slack high water holds the high level");
        Check(Math.Abs(TidegateTideRules.LevelAt(double.NaN) - TidegateTideRules.HighLevel) < 1e-6, "no game clock reads as high tide");
        // 连续：一整轮按 0.001 h 步进，相邻两步水位变化不超过 2 cm（没有跳变，换相处首尾相接）。
        float previous = TidegateTideRules.LevelAt(0);
        float minimum = float.MaxValue, maximum = float.MinValue;
        for (int i = 1; i <= 9000; i++)
        {
            float level = TidegateTideRules.LevelAt(i * 0.001);
            Check(Math.Abs(level - previous) < 0.02f, "tide level is continuous at " + (i * 0.001).ToString("F3") + "h");
            minimum = Math.Min(minimum, level);
            maximum = Math.Max(maximum, level);
            previous = level;
        }
        Check(Math.Abs(minimum - TidegateTideRules.LowLevel) < 1e-4 && Math.Abs(maximum - TidegateTideRules.HighLevel) < 1e-4,
            "the cycle reaches both slack levels");

        // 跨天：官方一天 86300 秒、天数进位时当天秒数归零。按官方时钟的进位方式走过三个午夜，潮位与相位都不能跳
        // （旧写法只读当天时刻，第一天午夜从低潮 -1.5 m 一步跳到高潮起点 +0.45 m）。
        Check(Math.Abs(TidegateTideRules.ElapsedHours(2, 1800.0) - (2 * 86300.0 + 1800.0) / 3600.0) < 1e-9, "elapsed clock counts official 86300 s days");
        Check(double.IsNaN(TidegateTideRules.ElapsedHours(0, double.NaN)), "missing clock stays NaN");
        long day = 0;
        double seconds = 80000.0;
        float last = TidegateTideRules.LevelAt(TidegateTideRules.ElapsedHours(day, seconds));
        TidegateTidePhase lastPhase = TidegateTideRules.PhaseAt(TidegateTideRules.ElapsedHours(day, seconds));
        int midnights = 0;
        for (int i = 0; i < 3 * 86300 / 6; i++)
        {
            seconds += 6.0;
            if (seconds > TidegateTideRules.OfficialSecondsPerDay) { day++; seconds -= TidegateTideRules.OfficialSecondsPerDay; midnights++; }
            double hours = TidegateTideRules.ElapsedHours(day, seconds);
            float level = TidegateTideRules.LevelAt(hours);
            TidegateTidePhase phase = TidegateTideRules.PhaseAt(hours);
            Check(Math.Abs(level - last) < 0.02f, "tide level is continuous across midnight (day " + day + ", " + seconds.ToString("F0") + " s)");
            Check(phase == lastPhase || (int)phase == ((int)lastPhase + 1) % 4, "tide phase only steps forward across midnight (day " + day + ")");
            last = level;
            lastPhase = phase;
        }
        Check(midnights == 3, "walked across three official midnights");

        double untilLow = TidegateTideRules.HoursUntilNextPhase(0.2);
        Check(Math.Abs(untilLow - 0.3) < 1e-9, "countdown to the next phase");
        Check(Math.Abs(TidegateTideRules.HoursUntilNextPhase(4.4) - 0.1) < 1e-9, "countdown wraps into the next cycle");

        // 泻湖：闸关着恒满；提起后以固定速度降到外海，之后贴着外海走（外海涨上来时泻湖跟着涨）。
        float lagoon = TidegateTideRules.LagoonFull;
        Check(TidegateTideRules.StepLagoon(lagoon, -1.5f, false, 10f) == TidegateTideRules.LagoonFull, "closed sluice keeps the lagoon full");
        float drained = TidegateTideRules.StepLagoon(lagoon, -1.5f, true, 1f);
        Check(Math.Abs(drained - (lagoon - TidegateTideRules.LagoonDrainRate)) < 1e-6, "open sluice drains at the fixed rate");
        float level2 = lagoon;
        for (int i = 0; i < 1000; i++) level2 = TidegateTideRules.StepLagoon(level2, -1.5f, true, 0.5f);
        Check(Math.Abs(level2 + 1.5f) < 1e-6, "drained lagoon settles at the sea level");
        Check(Math.Abs(TidegateTideRules.StepLagoon(-1.5f, 0.2f, true, 0.016f) - 0.2f) < 1e-6, "drained lagoon follows the rising sea");
        Check(Math.Abs(TidegateTideRules.StepLagoon(lagoon, -1.5f, true, -5f) - lagoon) < 1e-6, "negative time never refills");

        // 湿痕：水涨立刻跟上，水退按速度回落，不低于水面。
        Check(TidegateTideRules.StepWetLine(-1f, 0.3f, 0.016f) == 0.3f, "wet line follows a rising tide immediately");
        Check(Math.Abs(TidegateTideRules.StepWetLine(0.45f, -1.5f, 10f) - (0.45f - TidegateTideRules.WetLineFallRate * 10f)) < 1e-6,
            "wet line dries slowly after the ebb");
        Check(TidegateTideRules.StepWetLine(0.45f, -1.5f, 1000f) == -1.5f, "wet line never sinks under the water");

        // 深水：超过 0.5 m 才算；排水暗渠出口（原型地面 -1.12）只在低潮露出。
        Check(!TidegateTideRules.IsDeep(0.45f, 0f) && TidegateTideRules.IsDeep(0.45f, -0.1f), "deep water threshold is 0.5 m");
        Check(TidegateTideRules.Depth(TidegateTideRules.HighLevel, -1.12f) > TidegateTideRules.DrainExposedDepth, "drain is flooded at high tide");
        Check(TidegateTideRules.Depth(TidegateTideRules.LowLevel, -1.12f) < TidegateTideRules.DrainExposedDepth, "drain is exposed at low tide");
    }
}
