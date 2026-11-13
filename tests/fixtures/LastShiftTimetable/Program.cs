using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BossRush;

// 末班编组站班次表执行回归：直接编译生产 LastShiftYard/LastShiftTimetable.cs（纯规则，无 Unity），
// 按第七轮 owner 拍板的流程逐条断言：首班等人、看完示范才给道岔、教学停车、锁线、事件与条件出口、回访、撞击判定。
internal static class Program
{
    private static int failures;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
        if (!ok) failures++;
    }

    // 南护栏观察位（three.js 坐标）与远离它的一点。
    private const double RailX = LastShiftTimetable.WatchX, RailZ = LastShiftTimetable.WatchZ + 2, AwayX = 40, AwayZ = -40;

    /// <summary>按调用方（LastShiftTrains）的口径推进：车头扫过推车时记推车被撞。</summary>
    private static void Run(LastShiftTimetable t, double seconds, double px, double pz, double step = 0.05)
    {
        for (double s = 0; s < seconds; s += step)
        {
            t.Tick(step, px, pz);
            if (t.Training && !t.CartHit && t.Phase == LastShiftPhase.Arrival && t.LeadZ <= LastShiftTimetable.CartZ) t.MarkCartHit();
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length == 1) return WriteTrainNavigation(args[0]);
        FirstTrainWaitsForPlayer();
        FirstTrainFallsBack();
        DemoThenSwitch();
        ExpressSkipsDwell();
        ConditionalExits();
        ReturnVisit();
        Determinism();
        EventOdds();
        FrontHitAndEnvelope();
        ParkedTrainNavigation();
        FastCycle();
        LastTrainClosesYard();
        PremiumPerTrain();
        Console.WriteLine(failures == 0 ? "LastShiftTimetable: ALL PASS" : "LastShiftTimetable: " + failures + " FAIL");
        return failures == 0 ? 0 : 1;
    }

    private static int WriteTrainNavigation(string path)
    {
        var parked = new[] { LastShiftTimetable.ParkedTrain('W', false), LastShiftTimetable.ParkedTrain('E', false),
            LastShiftTimetable.ParkedTrain('W', true), LastShiftTimetable.ParkedTrain('E', true) };
        foreach (string row in File.ReadLines(path))
        {
            double[] v = Array.ConvertAll(row.Split(','), x => double.Parse(x, CultureInfo.InvariantCulture));
            for (int i = 0; i < parked.Length; i++)
            {
                char line = i % 2 == 0 ? 'W' : 'E';
                bool blocked = LastShiftTimetable.TrainBlocksTriangle(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], line, parked[i]);
                Console.Write(blocked ? '1' : '0');
            }
            Console.WriteLine();
        }
        return 0;
    }

    private static void FirstTrainWaitsForPlayer()
    {
        var t = new LastShiftTimetable(17, false);
        Check(t.Training && t.Phase == LastShiftPhase.Approach, "第一趟从鸣笛预告开始、处于教学");
        Run(t, 40, AwayX, AwayZ);
        Check(t.Phase == LastShiftPhase.Approach && t.PhaseTime < 0.01, "玩家不在南护栏后：首班 40 秒仍不发车");
        Run(t, 7.5, RailX, RailZ);
        Check(t.Phase == LastShiftPhase.Approach, "首班鸣笛预告不到 8 秒还没进站");
        Run(t, 1.0, RailX, RailZ);
        Check(t.Phase == LastShiftPhase.Arrival, "站到南护栏后：首班 8 秒鸣笛后进站（之后每班 20 秒）");
    }

    private static void FirstTrainFallsBack()
    {
        var t = new LastShiftTimetable(17, false);
        // 60 秒是第七轮 owner 采纳的数，按字面钉住，不读生产常量。
        Run(t, 59, AwayX, AwayZ);
        Check(!t.DemoForced, "离开护栏 59 秒：首班仍在等人");
        Run(t, 1.5, AwayX, AwayZ);
        Check(t.DemoForced, "离开护栏累计 60 秒后改为照常发车（不卡死）");
        Run(t, LastShiftTimetable.Approach + 0.5, AwayX, AwayZ);
        Check(t.Phase == LastShiftPhase.Arrival, "兜底后首班照常进站");
    }

    private static void DemoThenSwitch()
    {
        var t = new LastShiftTimetable(5, false);
        string reason;
        Check(!t.TrySwitch('E', out reason) && reason == "demo", "看示范之前道岔不给扳（demo）");
        Run(t, LastShiftTimetable.Approach + 0.5, RailX, RailZ);
        Run(t, LastShiftTimetable.Arrival, RailX, RailZ);
        Check(t.CartHit, "首班进站撞飞示范推车");
        Check(t.Phase == LastShiftPhase.Arrival && t.DispatchWait, "首班停在岔心前等调度");
        Check(!t.HasAssignedPlatform, "首班等待扳岔时还不能派押运长到台");
        Check(Math.Abs(t.PhaseTime - LastShiftTimetable.TrainingHold) < 1e-6, "停车点就是教学停车时刻");
        Check(t.TrySwitch('E', out reason), "示范后在停车点能扳到东台");
        Check(t.LockedLine == 'E' && !t.DispatchWait, "扳岔后锁到东线、不再等");
        Check(!t.HasAssignedPlatform, "教学扳岔窗口尚未关闭时不提前派押运长");
        Run(t, 0.5, AwayX, AwayZ);
        Check(t.HasAssignedPlatform && t.LineUsed == 'E' && t.Phase == LastShiftPhase.Arrival, "首班东台锁定后、停靠前即可派押运长");
        Run(t, LastShiftTimetable.Arrival, AwayX, AwayZ);
        Check(t.Phase == LastShiftPhase.Dwell && t.LineUsed == 'E', "列车开到东台停靠");
        Check(t.PhaseLength(LastShiftPhase.Dwell) == 90, "首班停靠 90 秒，够第一次来的人绕过车尾去东台签收");
        Check(!t.TrySwitch('W', out reason) && reason == "locked", "停靠中锁线，不能改道");
    }

    private static LastShiftTimetable FindReturn(LastShiftEvent wanted, LastShiftPhase phase, out int seed)
    {
        for (seed = 1; seed < 100000; seed++)
        {
            var t = new LastShiftTimetable(seed, true);
            if (t.Event == wanted && t.Phase == phase) return t;
        }
        throw new InvalidOperationException("找不到种子");
    }

    private static void ExpressSkipsDwell()
    {
        int seed;
        var t = FindReturn(LastShiftEvent.Express, LastShiftPhase.Approach, out seed);
        Check(t.PhaseLength(LastShiftPhase.Arrival) == LastShiftTimetable.ExpressArrival
            && t.PhaseLength(LastShiftPhase.Depart) == LastShiftTimetable.ExpressDepart, "直通快车进出站 13 / 10 秒");
        var seen = new List<LastShiftPhase>();
        t.PhaseChanged += (a, b) => seen.Add(b);
        Run(t, 60, AwayX, AwayZ);
        Check(seen.Contains(LastShiftPhase.Arrival) && seen.Contains(LastShiftPhase.Depart) && !seen.Contains(LastShiftPhase.Dwell),
            "直通快车不停靠：进站后直接出站");
        var slow = FindReturn(LastShiftEvent.Slow, LastShiftPhase.Approach, out seed);
        Check(slow.PhaseLength(LastShiftPhase.Dwell) == LastShiftTimetable.SlowDwell, "慢车停靠 58 秒");
    }

    private static void ConditionalExits()
    {
        int seed;
        var t = FindReturn(LastShiftEvent.Normal, LastShiftPhase.Approach, out seed);
        string reason;
        t.TrySwitch('W', out reason);
        Check(t.ExitOpen("north") && !t.ExitOpen("west") && !t.ExitOpen("east") && !t.ExitOpen("bell"),
            "鸣笛预告调往西线：北公路开；西便门从预告起就算占线（原型 exitOpen.west），东门与接客亭关");
        Run(t, t.SecondsLeft + 0.2, AwayX, AwayZ);
        Check(t.Phase == LastShiftPhase.Arrival && !t.ExitOpen("west"), "列车走西线进站：西线检修便门关");
        t.RingBell();
        Run(t, LastShiftTimetable.BellWait - 1, AwayX, AwayZ);
        Check(!t.ExitOpen("bell"), "拉铃 13 秒：轨道车未到");
        Run(t, 1.5, AwayX, AwayZ);
        Check(t.ExitOpen("bell"), "拉铃 14 秒后接客亭开");
        double wait = 0;
        while (t.Phase != LastShiftPhase.Clear && wait < 400) { Run(t, 0.5, AwayX, AwayZ); wait += 0.5; }
        Check(t.Phase == LastShiftPhase.Clear && t.ExitOpen("west"), "西线出清（线路净空）：西线检修便门开");

        var e = FindReturn(LastShiftEvent.Normal, LastShiftPhase.Clear, out seed);
        e.TrySwitch('E', out reason);
        double guard = 0;
        while (e.Phase != LastShiftPhase.Dwell && guard < 400) { Run(e, 0.5, AwayX, AwayZ); guard += 0.5; }
        Check(e.Phase == LastShiftPhase.Dwell && e.LineUsed == 'E', "东台停靠");
        Run(e, LastShiftTimetable.EastUnloadSeconds - 1, AwayX, AwayZ);
        Check(!e.ExitOpen("east"), "东台停靠 11 秒：押运门未开");
        Run(e, 1.5, AwayX, AwayZ);
        Check(e.ExitOpen("east"), "东台卸完（12 秒）押运门开");
        Check(e.ExitOpen("west"), "列车在东线：西线检修便门开");
    }

    private static void ReturnVisit()
    {
        var t = new LastShiftTimetable(99, true);
        string reason;
        Check(!t.Training && t.CartHit && t.DispatchChosen, "回访跳过首班教学");
        Check(t.Phase == LastShiftPhase.Clear || t.Phase == LastShiftPhase.Approach, "回访从净空或预告的随机时刻开局");
        Check(t.TrySwitch(t.Line == 'W' ? 'E' : 'W', out reason), "回访开局就能扳道岔");
        int seed;
        var normal = FindReturn(LastShiftEvent.Normal, LastShiftPhase.Approach, out seed);
        char wanted = normal.Line == 'W' ? 'E' : 'W';
        Check(normal.TrySwitch(wanted, out reason) && !normal.HasAssignedPlatform, "回访预告中道岔可改，未正式派岗");
        Run(normal, normal.SecondsLeft + 0.2, AwayX, AwayZ);
        Check(normal.HasAssignedPlatform && normal.LineUsed == wanted && normal.Phase == LastShiftPhase.Arrival, "回访按实际进站线路派岗");
    }

    private static void Determinism()
    {
        var a = new LastShiftTimetable(424242, true);
        var b = new LastShiftTimetable(424242, true);
        Check(a.Event == b.Event && a.Premium == b.Premium && a.Line == b.Line && a.Phase == b.Phase && a.PhaseTime == b.PhaseTime, "同一种子开局完全一致");
        Check(a.Stream("raid#manifest").Next(3) == b.Stream("raid#manifest").Next(3), "按标签派生的随机流与调用顺序无关");
        Check(a.RollEvent(7) == b.RollEvent(7), "第 n 班事件只由种子和班次决定");
    }

    private static void EventOdds()
    {
        var counts = new int[4];
        const int n = 40000;
        for (int seed = 0; seed < n; seed++) counts[(int)new LastShiftTimetable(seed, false).RollEvent(2)]++;
        double[] expect = { 0.40, 0.20, 0.25, 0.15 };
        bool ok = true;
        for (int i = 0; i < 4; i++) ok &= Math.Abs(counts[i] / (double)n - expect[i]) < 0.02;
        Check(ok, "班次事件概率接近 40/20/25/15（" + string.Join("/", Array.ConvertAll(counts, c => (c * 100.0 / n).ToString("0.0"))) + "）");
    }

    private static void FrontHitAndEnvelope()
    {
        int seed;
        var t = FindReturn(LastShiftEvent.Normal, LastShiftPhase.Approach, out seed);
        string reason;
        t.TrySwitch('W', out reason);
        Run(t, t.SecondsLeft + 0.2, AwayX, AwayZ);
        Run(t, 8, AwayX, AwayZ);
        Check(t.Phase == LastShiftPhase.Arrival, "进站中");
        double old = t.LeadZ;
        t.Tick(0.05, AwayX, AwayZ);
        LastShiftCar engine = t.Cars()[0];
        double noseZ = engine.Z - engine.Depth * 0.5 - 0.1;
        double noseX = LastShiftTimetable.RailX(noseZ, t.LineUsed);
        Check(t.FrontHit(old, noseX, noseZ, 0), "站在车头正前方：判为正面撞击");
        Check(!t.FrontHit(old, noseX + 5, noseZ, 0), "站在轨道侧面 5 m：不撞");
        Check(!t.FrontHit(old, noseX, noseZ, 5.2), "在司机室 / 跨线桥高处：不撞");
        Check(!t.FrontHit(old, engine.X, engine.Z + 3, 0), "车身中段侧擦不算正面撞击");
        Check(LastShiftTimetable.InEnvelope(LastShiftTimetable.RailX(-30, 'E'), -30, 'E')
            && !LastShiftTimetable.InEnvelope(LastShiftTimetable.RailX(-30, 'E') + 8, -30, 'E'), "车体包络只罩股道两侧 3 m");
        Check(t.Cars().Count == 4, "普通货列四节");
        var heavy = FindReturn(LastShiftEvent.Heavy, LastShiftPhase.Approach, out seed);
        heavy.TrySwitch('E', out reason);
        Run(heavy, heavy.SecondsLeft + 1, AwayX, AwayZ);
        Check(heavy.Cars().Count == 5, "重载货列多挂一节（五节）");
    }

    private static void ParkedTrainNavigation()
    {
        foreach (char line in new[] { 'W', 'E' })
        {
            var shortTrain = LastShiftTimetable.ParkedTrain(line, false);
            var longTrain = LastShiftTimetable.ParkedTrain(line, true);
            double x = LastShiftTimetable.RailX(-38, line);
            Check(Blocks(shortTrain, line, x, -38) && Blocks(longTrain, line, x, -38), line + "：停车封车头与实体盒");
            Check(!Blocks(shortTrain, line, x, -45) && !Blocks(longTrain, line, x, -45)
                && Blocks(null, line, x, -45), line + "：仅在停车时恢复车头外的通道");
            Check(!Blocks(shortTrain, line, x, -38, 5.2), line + "：车顶以上的桥面仍通行");
            double tailX = LastShiftTimetable.RailX(-2.8, line);
            Check(!Blocks(shortTrain, line, tailX, -2.8) && Blocks(longTrain, line, tailX, -2.8), line + "：重载多一节，相应封闭车尾");
            Check(LastShiftTimetable.TrainBlocksTriangle(x, 0, -38, x + 9, 0, -37, x + 9, 0, -39, line, shortTrain),
                line + "：重心在车外但伸进实体车厢的长三角仍要封");
        }
    }

    // ---------------- 2026-11-13 快节奏改版 ----------------
    private static void FastCycle()
    {
        double cycle = LastShiftTimetable.Approach + LastShiftTimetable.Arrival + LastShiftTimetable.Dwell + LastShiftTimetable.Depart + LastShiftTimetable.Clear;
        // 118 秒是 owner「快节奏的多趟火车」拍板后的数，按字面钉住；之前是 180 秒。
        Check(Math.Abs(cycle - 118) < 1e-9, "一班 118 秒（预告 15 + 进站 22 + 停靠 40 + 出站 16 + 净空 25）");
        Check(LastShiftTimetable.LastTrip == 8, "第 8 班是末班车");
        Check(new LastShiftTimetable(3, true).RollEvent(LastShiftTimetable.LastTrip) == LastShiftEvent.Heavy, "末班车总是重载、总要停靠");
    }

    private static void LastTrainClosesYard()
    {
        int seed;
        var t = FindReturn(LastShiftEvent.Normal, LastShiftPhase.Approach, out seed);
        string reason;
        t.TrySwitch('E', out reason);
        bool earlyStep = false, stepAtDwell = false, stepAtDepart = false, lastDocked = false;
        double run = 0;
        while (!t.YardClosed && run < 2000)
        {
            Run(t, 0.25, AwayX, AwayZ);
            run += 0.25;
            bool open = t.ExitOpen("last");
            if (open && !t.IsLastTrain) earlyStep = true;
            if (t.IsLastTrain && t.Phase == LastShiftPhase.Dwell) { lastDocked = true; stepAtDwell |= open; }
            if (t.IsLastTrain && t.Phase == LastShiftPhase.Depart) stepAtDepart |= open;
            LastShiftCar van;
            if (t.IsLastTrain && t.Phase == LastShiftPhase.Dwell && t.TryVan(out van) && van.Kind != LastShiftCarKind.Van) earlyStep = true;
        }
        Check(t.YardClosed && t.Trip == LastShiftTimetable.LastTrip, "末班车出站后封站（第 " + t.Trip + " 班，" + run.ToString("0") + " 秒）");
        // 全是慢车也不过 8 × (118 + 18)；正常种子约 15 分钟。
        Check(run <= 8 * (118 + 18), "一趟八班在 18 分钟内走完（本种子 " + (run / 60).ToString("0.0") + " 分钟）");
        Check(!earlyStep, "末班车之前押运车厢尾梯不开");
        Check(lastDocked && stepAtDwell && stepAtDepart, "末班车停靠与出站时尾梯开着");
        Check(t.Phase == LastShiftPhase.Clear && t.Cars().Count == 0 && !t.TrainOnLine, "封站后股道空着");
        Check(!t.TrySwitch('W', out reason) && reason == "closed", "封站后道岔不给扳（closed）");
        t.RingBell();
        Run(t, 120, AwayX, AwayZ);
        Check(t.YardClosed && t.Trip == LastShiftTimetable.LastTrip && t.Phase == LastShiftPhase.Clear, "封站后不再来车");
        Check(t.ExitOpen("north") && !t.ExitOpen("west") && !t.ExitOpen("east") && !t.ExitOpen("bell") && !t.ExitOpen("last"),
            "封站后只剩北公路（便门、押运门、接客亭、尾梯都关）");
        Check(t.TrainsLeft == 0 && t.SecondsLeft == 0, "封站后班次读数归零");
    }

    private static void PremiumPerTrain()
    {
        int west = 0, changed = 0;
        const int n = 4000;
        for (int seed = 0; seed < n; seed++)
        {
            var t = new LastShiftTimetable(seed, true);
            if (t.PremiumFor(3) == 'W') west++;
            if (t.PremiumFor(3) != t.PremiumFor(4)) changed++;
        }
        Check(Math.Abs(west / (double)n - .5) < .03, "加价台每班西东各半（" + (west * 100.0 / n).ToString("0.0") + "%）");
        Check(Math.Abs(changed / (double)n - .5) < .03, "相邻两班的加价台互不相干（" + (changed * 100.0 / n).ToString("0.0") + "% 换边）");
        int s;
        var u = FindReturn(LastShiftEvent.Normal, LastShiftPhase.Approach, out s);
        Check(u.Premium == u.PremiumFor(u.Trip) && !u.PremiumRevealed, "预告时加价签还没贴（进站才揭晓）");
        Run(u, u.SecondsLeft + 0.2, AwayX, AwayZ);
        Check(u.Phase == LastShiftPhase.Arrival && u.PremiumRevealed, "进站那一刻揭晓加价台");
        var c = FindReturn(LastShiftEvent.Normal, LastShiftPhase.Clear, out s);
        Check(c.UpcomingPremium == c.PremiumFor(c.Trip + 1) && c.UpcomingEvent == c.RollEvent(c.Trip + 1), "净空里说的「下一班」是还没来的那一班");
    }

    private static bool Blocks(LastShiftParkedTrain parked, char line, double x, double z, double y = 0)
    {
        return LastShiftTimetable.TrainBlocksTriangle(x, y, z, x + 0.05, y, z, x, y, z + 0.05, line, parked);
    }
}
