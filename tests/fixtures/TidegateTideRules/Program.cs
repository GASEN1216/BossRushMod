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

        // 换相前提示（CR-2026-11-05-017）：官方倍速 60（1 游戏小时 = 60 现实秒）。低潮在 2.5 h 结束、高潮在 4.5 h 结束。
        const double scale = 60.0;
        Check(Math.Abs(TidegateTideRules.RealSecondsUntilNextPhase(2.5 - 10.0 / 60.0, scale) - 10.0) < 1e-6, "real seconds follow the clock scale");
        Check(Math.Abs(TidegateTideRules.RealSecondsUntilNextPhase(0.2, 0.0) - 0.3 * 3600.0) < 1e-6, "a stopped clock scale never divides by zero");
        Check(TidegateTideRules.NoticeAt(2.5 - 20.0 / 60.0, scale) == TidegateTideNotice.None, "no flood alarm 20 s before the flood");
        Check(TidegateTideRules.NoticeAt(2.5 - 14.0 / 60.0, scale) == TidegateTideNotice.FloodSoon, "flood alarm inside the last 15 s of low tide");
        Check(TidegateTideRules.NoticeAt(2.5 + 1.0 / 60.0, scale) == TidegateTideNotice.None, "no alarm once the flood has started");
        Check(TidegateTideRules.NoticeAt(4.5 - 13.0 / 60.0, scale) == TidegateTideNotice.None, "ferry stays quiet 13 s before the ebb");
        Check(TidegateTideRules.NoticeAt(4.5 - 11.0 / 60.0, scale) == TidegateTideNotice.FerryLeaving, "ferry warning inside the last 12 s of high tide");
        Check(TidegateTideRules.NoticeAt(0.25, scale) == TidegateTideNotice.None, "nothing to announce while ebbing");
        Check(TidegateTideRules.NoticeAt(double.NaN, scale) == TidegateTideNotice.None, "no clock, no alarm");
        int floodAlarms = 0;
        TidegateTideNotice shown = TidegateTideNotice.None;
        TidegateTidePhase seen = TidegateTideRules.PhaseAt(0.0);
        for (int i = 0; i <= 2 * 4500; i++)
        {
            // 两整轮、每 0.001 h（0.06 现实秒）问一次，会话按「换相清零、同一提示只报一次」记：每一轮恰好一次涨潮警报。
            double hours = i * 0.001;
            TidegateTidePhase phase = TidegateTideRules.PhaseAt(hours);
            if (phase != seen) { seen = phase; shown = TidegateTideNotice.None; }
            TidegateTideNotice notice = TidegateTideRules.NoticeAt(hours, scale);
            if (notice == TidegateTideNotice.None || notice == shown) continue;
            shown = notice;
            if (notice == TidegateTideNotice.FloodSoon) floodAlarms++;
        }
        Check(floodAlarms == 2, "exactly one flood alarm per tide cycle");

        // 错过高潮、重装等下一班、抓缆后来回跨潮：直接驱动生产撤离口使用的缆绳状态。
        var line = new TidegateFerryLine();
        Check(line.CanBoard(TidegateTidePhase.High), "a laden traveler still boards at high tide without touching the line");
        Check(!line.TryCatch(TidegateTidePhase.Low, true, true) && !line.CanBoard(TidegateTidePhase.Low), "a late swimmer cannot board at low tide");
        Check(!line.TryCatch(TidegateTidePhase.Ebb, false, true) && !line.CanBoard(TidegateTidePhase.Ebb), "heavy cargo cannot board the receding ferry");
        Check(!line.TryCatch(TidegateTidePhase.Ebb, true, false), "a swimmer cannot catch the line away from the pier");
        Check(line.TryCatch(TidegateTidePhase.Ebb, true, true) && line.CanBoard(TidegateTidePhase.Ebb), "light traveler explicitly catches the line");
        Check(!line.DropIfNeeded(true, true) && line.CanBoard(TidegateTidePhase.Low), "a swimmer already boarding finishes even as ebb ends");
        Check(line.DropIfNeeded(false, true) && !line.CanBoard(TidegateTidePhase.Low), "stepping away releases the reservation");
        Check(!line.CanBoard(TidegateTidePhase.Flood) && line.TryCatch(TidegateTidePhase.Flood, true, true), "next rising tide needs a fresh interaction");
        Check(line.DropIfNeeded(true, false) && !line.CanBoard(TidegateTidePhase.Flood), "picking up a heavy pack interrupts early boarding");
        Check(line.CanBoard(TidegateTidePhase.High), "ordinary high tide ferry still accepts the heavy pack");

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

        const float bed = -1.36f;
        Check(!TidegateIntakeHold.Flowing(false, lagoon, drained, -1.5f, bed, 1f, false), "closed gate cannot suck in a swimmer");
        Check(TidegateIntakeHold.Flowing(true, lagoon, drained, -1.5f, bed, 1f, false), "actual modeled discharge can activate the intake");
        Check(!TidegateIntakeHold.Flowing(true, drained, drained, -1.5f, bed, 1f, true), "slack water cancels the active intake");
        Check(!TidegateIntakeHold.Flowing(true, -0.4f, -0.3f, -0.3f, bed, 1f, true), "rising water cannot pull toward the outlet");
        Check(!TidegateIntakeHold.Flowing(true, -0.95f, -1.1f, -1.5f, bed, 1f, true), "exposed mud is never lethal");
        Check(TidegateIntakeHold.InCore(true, 0f, -0.25f, 0f, bed), "swimmer inside the marked pool can be warned");
        Check(!TidegateIntakeHold.InCore(true, 0f, 1.6f, 0.62f, bed), "the dry dam deck is above the intake");
        Check(!TidegateIntakeHold.InCore(true, 1.75f*1.75f, -0.25f, 0f, bed), "the marked perimeter itself is safe");
        var intake = new TidegateIntakeHold();
        Check(!intake.Step(true, 20f) && intake.Held == 0f, "entry always shows the full warning");
        for (int i = 0; i < 31; i++) Check(!intake.Step(true, 0.125f), "no death before the four seconds expire");
        Check(!intake.Step(true, 0f) && !intake.Step(false, 1f), "pause and escape at the last instant do not kill");
        Check(!intake.Step(true, 1f) && !intake.Step(true, 10f) && intake.Held <= 0.2f, "one long frame cannot use up the warning");
        intake.Step(false, 0f);
        intake.Step(true, 0.125f);
        for (int i = 0; i < 31; i++) Check(!intake.Step(true, 0.125f), "reentry starts a fresh full warning");
        Check(intake.Step(true, 0.125f) && !intake.Step(true, 0.125f), "one fatal event at four seconds per uninterrupted exposure");
        // 已经排过一次以后，不能变成只去初次开闸的一趟才可能遇见的危险；用真实潮位走过完整的下一次退潮。
        float lake2 = TidegateTideRules.HighLevel, activeRun = 0f, longest = 0f; bool rushing = false;
        for (int i = 1; i <= 30*16; i++)
        {
            float sea = TidegateTideRules.LevelAt(i / (16.0 * 60.0)), before = lake2;
            lake2 = TidegateTideRules.StepLagoon(before, sea, true, 1f/16f);
            rushing = TidegateIntakeHold.Flowing(true, before, lake2, sea, bed, 1f/16f, rushing);
            activeRun = rushing ? activeRun + 1f/16f : 0f;
            longest = Math.Max(longest, activeRun);
        }
        Check(longest > TidegateIntakeHold.WarningSeconds, "a normal subsequent ebb gives a real, avoidable discharge window");

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
