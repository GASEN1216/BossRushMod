using System;
using BossRush;

internal static class Program
{
    private static int checks;

    private static void Check(bool value, string name)
    {
        checks++;
        if (!value) throw new InvalidOperationException(name);
    }

    private static void Expect(SandstormChampionAttackPattern pattern, SandstormChampionAttackKind kind, int count)
    {
        SandstormChampionAttack actual = pattern.Next();
        Check(actual.Kind == kind && actual.Count == count,
            "expected " + kind + ":" + count + ", got " + actual.Kind + ":" + actual.Count);
    }

    private static void Cycle(SandstormChampionAttackPattern pattern, bool second)
    {
        Expect(pattern, SandstormChampionAttackKind.Dashes, second ? 3 : 5);
        Expect(pattern, second ? SandstormChampionAttackKind.SpiralBubbles : SandstormChampionAttackKind.BubbleBelch,
            second ? 31 : 21);
        Expect(pattern, SandstormChampionAttackKind.Dashes, second ? 3 : 5);
        Expect(pattern, second ? SandstormChampionAttackKind.HomingCycloneSeed : SandstormChampionAttackKind.TwinTornadoSeeds,
            second ? 1 : 2);
    }

    private static void Main()
    {
        var pattern = new SandstormChampionAttackPattern();
        Check(pattern.Phase == 1 && pattern.CompletedCycles == 0 && !pattern.Enraged, "fresh encounter");
        for (int i = 0; i < 12; i++)
        {
            Cycle(pattern, false);
            Check(pattern.CompletedCycles == i + 1, "one completed cycle per full phase-one sequence");
        }
        Check(!pattern.UpdateState(0.5f, false) && pattern.Phase == 1, "50 percent is still phase one");
        Expect(pattern, SandstormChampionAttackKind.Dashes, 5);
        Check(pattern.UpdateState(0.499f, false) && pattern.Phase == 2, "below 50 percent enters phase two");
        int before = pattern.CompletedCycles;
        for (int i = 0; i < 12; i++) Cycle(pattern, true);
        Check(pattern.CompletedCycles == before + 12, "phase-two full cycle count");
        Check(!pattern.UpdateState(0.15f, false) && pattern.Phase == 2, "15 percent is still phase two");
        Expect(pattern, SandstormChampionAttackKind.Dashes, 3);
        Check(pattern.UpdateState(0.149f, false) && pattern.Phase == 3, "below 15 percent enters final phase");
        before = pattern.CompletedCycles;
        for (int i = 0; i < 12; i++)
        {
            Expect(pattern, SandstormChampionAttackKind.TeleportDashes, 1);
            Expect(pattern, SandstormChampionAttackKind.TeleportDashes, 2);
            Expect(pattern, SandstormChampionAttackKind.TeleportDashes, 3);
            // owner 2026-10-08：末阶段每组冲锋后轮换双生沙卷、绕圈吐泡、大沙暴。
            if (i % 3 == 0) Expect(pattern, SandstormChampionAttackKind.TwinTornadoSeeds, 2);
            else if (i % 3 == 1) Expect(pattern, SandstormChampionAttackKind.SpiralBubbles, 31);
            else Expect(pattern, SandstormChampionAttackKind.HomingCycloneSeed, 1);
        }
        Check(pattern.CompletedCycles == before + 12, "final phase counts complete 1-2-3 + skill loops");
        Expect(pattern, SandstormChampionAttackKind.TeleportDashes, 1);
        Check(!pattern.UpdateState(1f, true) && pattern.Phase == 3, "healing cannot reverse phase");
        Expect(pattern, SandstormChampionAttackKind.TeleportDashes, 2);
        Check(!pattern.UpdateState(0.1f, false), "returning inside arena is not a new phase");
        Expect(pattern, SandstormChampionAttackKind.TeleportDashes, 3);

        var jumped = new SandstormChampionAttackPattern();
        Expect(jumped, SandstormChampionAttackKind.Dashes, 5);
        Check(jumped.UpdateState(0.01f, false) && jumped.Phase == 3, "burst damage may skip directly to final phase");
        Expect(jumped, SandstormChampionAttackKind.TeleportDashes, 1);
        Check(jumped.CompletedCycles == 0, "phase change does not award an unfinished cycle");

        var enraged = new SandstormChampionAttackPattern();
        Check(!enraged.UpdateState(0.9f, true) && enraged.Enraged, "enrage is distinct from health phase");
        for (int i = 0; i < 3; i++) Expect(enraged, SandstormChampionAttackKind.Dashes, 3);
        Expect(enraged, SandstormChampionAttackKind.HomingCycloneSeed, 1);
        Check(enraged.CompletedCycles == 1, "enrage cycle completes after cyclone seed");
        Expect(enraged, SandstormChampionAttackKind.Dashes, 3);
        Check(!enraged.UpdateState(0.8f, false) && !enraged.Enraged, "return from enrage");
        Cycle(enraged, false);
        Check(enraged.UpdateState(0.4f, true), "health phase still advances while enraged");
        for (int i = 0; i < 3; i++) Expect(enraged, SandstormChampionAttackKind.Dashes, 3);
        Expect(enraged, SandstormChampionAttackKind.HomingCycloneSeed, 1);
        Check(enraged.UpdateState(0.05f, true), "final phase outranks enraged cyclone sequence");
        for (int i = 1; i <= 3; i++) Expect(enraged, SandstormChampionAttackKind.TeleportDashes, i);
        var clean = new SandstormChampionAttackPattern();
        Cycle(clean, false);
        Check(clean.Phase == 1 && !clean.Enraged && clean.CompletedCycles == 1, "encounters do not share mutable state");
        Console.WriteLine("SandstormChampionPattern: PASS (" + checks + " production assertions)");
    }
}
