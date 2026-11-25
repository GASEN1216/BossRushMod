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
            Console.WriteLine("BossMapBeaconRules: FAIL " + e.Message);
            return 1;
        }
        Console.WriteLine("BossMapBeaconRules: PASS (" + assertions + " assertions, production rules)");
        return 0;
    }

    private static void Run()
    {
        // 三档半径 35 → 20 → 10，越界夹到两端
        Check(BossMapBeaconRules.RadiusOf(0) == 35f && BossMapBeaconRules.RadiusOf(1) == 20f && BossMapBeaconRules.RadiusOf(2) == 10f,
            "stage radii are 35 / 20 / 10 m");
        Check(BossMapBeaconRules.RadiusOf(-3) == 35f && BossMapBeaconRules.RadiusOf(9) == 10f, "out-of-range stages clamp to the ends");

        // 偏移上限：35 米那档 15 米；每档都小于半径，Boss 始终在圈里
        Check(Math.Abs(BossMapBeaconRules.MaxOffset(0) - 15f) < 1e-4, "35 m circle drifts at most 15 m");
        for (int stage = 0; stage <= BossMapBeaconRules.LastStage; stage++)
        {
            Check(BossMapBeaconRules.MaxOffset(stage) < BossMapBeaconRules.RadiusOf(stage) * 0.5f,
                "boss stays well inside the circle at stage " + stage);
        }

        // 偏移取点：落在上限圆盘内；u1=0 落在 Boss 身上；u1→1 落在边上
        Random random = new Random(20261124);
        for (int i = 0; i < 2000; i++)
        {
            int stage = i % 3;
            float x;
            float z;
            BossMapBeaconRules.Offset(stage, random.NextDouble(), random.NextDouble(), out x, out z);
            Check(Math.Sqrt(x * x + z * z) <= BossMapBeaconRules.MaxOffset(stage) + 1e-3, "offset stays within the drift disc");
        }
        float ox;
        float oz;
        BossMapBeaconRules.Offset(0, 0.0, 0.3, out ox, out oz);
        Check(ox == 0f && oz == 0f, "u1 = 0 centres on the boss");
        BossMapBeaconRules.Offset(0, 1.0, 0.0, out ox, out oz);
        Check(Math.Abs(ox - 15f) < 1e-3 && Math.Abs(oz) < 1e-3, "u1 = 1, u2 = 0 lands on the +x rim");

        // 收紧：安静满 60 秒收一档，最多收到 10 米那档
        Check(BossMapBeaconRules.StageAfterQuiet(0, 59.9f) == 0, "59.9 quiet seconds do not tighten");
        Check(BossMapBeaconRules.StageAfterQuiet(0, 60f) == 1, "60 quiet seconds tighten 35 -> 20");
        Check(BossMapBeaconRules.StageAfterQuiet(1, 60f) == 2, "another 60 seconds tighten 20 -> 10");
        Check(BossMapBeaconRules.StageAfterQuiet(2, 600f) == 2, "10 m is the last stage");

        // 显形：走进 40 米内立刻显形；或者 10 米那档又安静 60 秒；其余不显形
        Check(BossMapBeaconRules.ShouldReveal(0, 0f, 40f), "within 40 m reveals at once");
        Check(!BossMapBeaconRules.ShouldReveal(0, 0f, 40.5f), "40.5 m does not reveal");
        Check(!BossMapBeaconRules.ShouldReveal(1, 600f, 200f), "a 20 m circle does not reveal on quiet alone");
        Check(!BossMapBeaconRules.ShouldReveal(2, 59f, 200f), "the 10 m circle holds for its own 60 seconds");
        Check(BossMapBeaconRules.ShouldReveal(2, 60f, 200f), "the 10 m circle reveals after 60 more quiet seconds");

        // 整条时间线：一直不接战，60 秒到 20 米、120 秒到 10 米、180 秒显形
        int s = 0;
        float since = 0f;
        float revealAt = -1f;
        for (float t = 0f; t <= 400f; t += 0.5f)
        {
            if (BossMapBeaconRules.ShouldReveal(s, t - since, 500f)) { revealAt = t; break; }
            int next = BossMapBeaconRules.StageAfterQuiet(s, t - since);
            if (next != s) { s = next; since = t; }
        }
        Check(Math.Abs(revealAt - 180f) < 0.01f, "an untouched boss reveals after 180 seconds, got " + revealAt);

        // 光柱：上一道散尽前不放第二道
        Check(BossMapBeaconRules.CanPlayPillar(10f, 0f), "first pillar plays");
        Check(!BossMapBeaconRules.CanPlayPillar(10f, 10f + BossMapBeaconRules.PillarSeconds - 0.01f), "second pillar waits");
        Check(BossMapBeaconRules.CanPlayPillar(11.5f, 11.5f), "pillar plays again once the last one is done");

        Check(BossMapBeaconRules.MaxBeacons == 8 && BossMapBeaconRules.RecenterSeconds == 8f && BossMapBeaconRules.RevealedScale == 1.3f,
            "cap 8 beacons, recentre every 8 s, revealed scale 1.3");
    }
}
