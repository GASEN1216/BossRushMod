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
            Console.WriteLine("SilenceBellRules: FAIL " + e.Message);
            return 1;
        }
        Console.WriteLine("SilenceBellRules: PASS (" + assertions + " assertions, production rules)");
        return 0;
    }

    private static void Run()
    {
        bool broke;

        // 数值与来路
        Check(SilenceBellConfig.Capacity == 80f && SilenceBellConfig.RechargeDelaySeconds == 5f
            && SilenceBellConfig.BrokenDelaySeconds == 9f && SilenceBellConfig.RechargePerSecond == 20f,
            "ward 80, recharge after 5 s, 9 s after shattering, 20 per second");
        Check(SilenceBellConfig.VendorNpcId == "sky_bellkeeper" && SilenceBellConfig.ShellCost == 30
            && SilenceBellConfig.ItemQuality == 5, "sold by the bell keeper for 30 seashells, quality 5");

        // 新建即满罩
        var rules = new SilenceBellRules();
        Check(rules.IsFull && rules.Shield == 80f && !rules.Broken, "a fresh ward starts full");

        // 部分吸收：先吃护罩，打穿的部分交给血量
        float absorbed = rules.Absorb(30f, 10f, out broke);
        Check(absorbed == 30f && rules.Shield == 50f && !broke, "a 30 hit is fully absorbed");
        Check(rules.RechargeAt == 15f, "a hit pushes recharge to 5 s later");
        Check(!rules.Tick(14.9f, 0.1f) && rules.Shield == 50f, "no refill before the 5 s delay");
        Check(!rules.IsRecharging(14.9f) && rules.IsRecharging(15f), "recharging starts exactly at the delay");

        // 回充 20/s，单帧最多 0.25 s
        rules.Tick(15f, 0.5f);
        Check(Math.Abs(rules.Shield - 55f) < 1e-4, "a long frame refills at most 0.25 s worth (5 points), got " + rules.Shield);
        for (int i = 0; i < 4; i++) rules.Tick(15.5f + i * 0.25f, 0.25f);
        Check(Math.Abs(rules.Shield - 75f) < 1e-4, "one second of ticks refills 20 points, got " + rules.Shield);
        bool refilled = false;
        for (int i = 0; i < 8 && !refilled; i++) refilled = rules.Tick(16.5f + i * 0.1f, 0.1f);
        Check(refilled && rules.IsFull && rules.Shield == 80f, "the refill tick reports full exactly once and clamps at 80");
        Check(!rules.Tick(20f, 0.1f), "a full ward does not report refilling again");

        // 打穿：挡下剩余护罩，标记碎裂，回充顺延 9 秒
        rules.Absorb(70f, 30f, out broke);
        absorbed = rules.Absorb(25f, 31f, out broke);
        Check(absorbed == 10f && rules.Shield == 0f && broke && rules.Broken, "a 25 hit on 10 left absorbs 10 and shatters");
        Check(rules.RechargeAt == 40f, "shattering delays recharge for 9 s (31 + 9)");

        // 碎着挨打：不再挡，但按 5 秒顺延（取晚）
        absorbed = rules.Absorb(15f, 33f, out broke);
        Check(absorbed == 0f && !broke && rules.RechargeAt == 40f, "a hit while shattered absorbs nothing; 33 + 5 is earlier than 40");
        rules.Absorb(15f, 37f, out broke);
        Check(rules.RechargeAt == 42f, "a later hit while shattered pushes recharge to 37 + 5");
        Check(!rules.Tick(41.9f, 0.1f) && rules.Broken, "still quiet before 42");
        rules.Tick(42f, 0.1f);
        Check(!rules.Broken && rules.Shield > 0f, "the first refill tick clears the shattered state");

        // 整条时间线：满罩挨一记 80 打碎，之后不挨打，9 秒后开始回，再 4 秒回满 → 第 13 秒满
        var timeline = new SilenceBellRules();
        timeline.Absorb(120f, 100f, out broke);
        Check(broke && timeline.Shield == 0f, "an 80+ hit shatters a full ward and the excess passes through");
        float fullAt = -1f;
        for (float t = 100f; t <= 130f; t += 0.05f)
        {
            if (timeline.Tick(t, 0.05f)) { fullAt = t; break; }
        }
        Check(Math.Abs(fullAt - 113f) < 0.06f, "an untouched shattered ward is full again 13 s later, got " + fullAt);

        // 非法输入不改状态
        var guard = new SilenceBellRules();
        Check(guard.Absorb(float.NaN, 1f, out broke) == 0f && guard.Absorb(-5f, 1f, out broke) == 0f
            && guard.Absorb(10f, float.PositiveInfinity, out broke) == 0f && guard.IsFull && guard.RechargeAt == 0f,
            "NaN, negative damage and invalid time are ignored");
        Check(!guard.Tick(float.NaN, 0.1f) && !guard.Tick(1f, float.NaN) && !guard.Tick(1f, -1f), "invalid ticks are ignored");

        // 换场景 / 死亡：满罩、清掉计时
        guard.Absorb(80f, 5f, out broke);
        guard.ResetFull();
        Check(guard.IsFull && !guard.Broken && guard.RechargeAt == 0f, "reset refills and clears the recharge clock");
        Check(Math.Abs(guard.Fraction - 1f) < 1e-6, "fraction is 1 when full");
    }
}
