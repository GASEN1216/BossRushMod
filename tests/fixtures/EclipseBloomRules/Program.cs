using System;
using BossRush;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(label);
    }

    private static void Near(float actual, float expected, float tolerance, string label)
    {
        Check(!float.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance,
            label + ": expected " + expected + ", actual " + actual);
    }

    private static void Until(EclipseBloomRules rules, Func<bool> condition, float healthRatio = 1f)
    {
        for (int frame = 0; frame < 1000 && !condition(); frame++) rules.Advance(0.05f, healthRatio);
        Check(condition(), "bounded progress reaches the requested attack stage");
    }

    private static int NextTelegraph(EclipseBloomRules rules, float healthRatio = 1f)
    {
        Until(rules, () => rules.IsResting, healthRatio);
        Until(rules, () => !rules.IsResting, healthRatio);
        Check(!rules.IsDamaging && rules.TelegraphProgress == 0f, "a fresh attack starts with a full independent telegraph");
        return rules.AttackMask;
    }

    private static void Main()
    {
        PhaseAndBreakRules();
        TelegraphFairnessAndClock();
        AttackPatternsAndRest();
        AimTracking();
        GeometryBoundariesAndMeshSamples();
        EnumerateBreakMasksAndRotatingSafeSectors();
        EnumerateBreakOrdersAndHealthTrajectories();
        InvalidInputsAndTerminalStop();
        Console.WriteLine("PASS: " + checks + " production-linked EclipseBloom assertions");
    }

    private static void PhaseAndBreakRules()
    {
        var rules = new EclipseBloomRules();
        Check(rules.Phase == 1 && rules.IntactMask == 15 && rules.AttackMask == 1, "new crown begins with four petals and one warning sector");
        Check(!rules.IsDamaging && !rules.IsCoreAttack && !rules.IsResting, "spawn is a harmless crown telegraph");
        rules.Advance(0.05f, 0.6001f);
        Check(rules.Phase == 1, "phase two does not begin above sixty percent");
        Until(rules, () => rules.IsDamaging);
        rules.Advance(0.05f, 0.60f);
        Check(rules.Phase == 2 && !rules.IsDamaging && rules.TelegraphProgress == 0f,
            "sixty percent interrupts old damage and begins a fresh phase two warning");
        rules.Advance(0.05f, 1f);
        Check(rules.Phase == 2, "healing cannot return to phase one");
        Check(rules.BreakPetal(2), "an intact petal can break");
        Check(!rules.BreakPetal(2) && rules.IntactMask == 11, "broken petals never resurrect or break twice");
        rules.Advance(0.05f, 0.2501f);
        Check(rules.Phase == 2, "phase three does not begin above twenty five percent");
        rules.Advance(0.05f, 0.25f);
        Check(rules.Phase == 3 && rules.IntactMask == 0 && rules.IsCoreAttack && !rules.IsDamaging,
            "twenty five percent retires all petals and starts the exposed core warning");
        Check(rules.TelegraphProgress == 0f, "phase three warning does not inherit previous elapsed time");
        rules.Advance(0.05f, 1f);
        Check(rules.Phase == 3 && rules.IntactMask == 0, "phase three remains irreversible after healing");
        for (int index = 0; index < 4; index++) Check(!rules.BreakPetal(index), "retired phase three petals cannot be damaged");

        var burst = new EclipseBloomRules();
        Until(burst, () => burst.IsDamaging);
        burst.Advance(100f, 0.1f);
        Check(burst.Phase == 3 && !burst.IsDamaging && burst.TelegraphProgress == 0f,
            "burst health loss can skip phase two without skipping the new warning");

        var brokenActive = new EclipseBloomRules();
        Until(brokenActive, () => brokenActive.IsDamaging);
        Check(brokenActive.BreakPetal(0), "currently firing petal breaks");
        Check(!brokenActive.IsDamaging && brokenActive.AttackMask == 0,
            "breaking the firing petal immediately clears its damage sector");

        var paired = new EclipseBloomRules();
        paired.Advance(0.05f, 0.5f);
        Until(paired, () => paired.IsDamaging, 0.5f);
        Check(paired.BreakPetal(0) && paired.IsDamaging && paired.AttackMask == 4,
            "breaking one firing petal preserves the other already warned opposing beam");
        float safeX, safeZ;
        EclipseBloomRules.GetSectorPoint(0, paired.RotationDegrees, 6f, 0f, out safeX, out safeZ);
        Check(!paired.IsDangerous(safeX, safeZ), "phase two petal break makes its rotating sector safe immediately");
        Check(paired.BreakPetal(2) && !paired.IsDamaging && paired.IsResting,
            "breaking both currently firing petals ends damage while unused petals survive");
    }

    private static void TelegraphFairnessAndClock()
    {
        foreach (float delta in new[] { 1f / 120f, 1f / 60f, 0.05f, 0.1f, 0.5f, 100f })
        {
            var rules = new EclipseBloomRules();
            float displayedWarning = 0f;
            for (int frame = 0; frame < 1000 && !rules.IsDamaging; frame++)
            {
                rules.Advance(delta, 1f);
                displayedWarning += rules.LastStepSeconds;
            }
            Check(rules.IsDamaging, "every positive frame interval eventually attacks");
            Check(displayedWarning >= EclipseBloomTuning.FirstPhaseTelegraphSeconds,
                "damage waits for a full simulated warning at every frame interval");
            Check(rules.LastStepSeconds <= EclipseBloomTuning.MaximumStepSeconds,
                "slow frames cannot advance the encounter beyond its fairness budget");

            Until(rules, () => rules.IsResting);
            while (rules.IsResting) rules.Advance(100f, 1f);
            Check(!rules.IsDamaging && rules.TelegraphProgress == 0f,
                "a slow frame ending recovery cannot also consume the next telegraph");
            rules.Advance(100f, 1f);
            Check(!rules.IsDamaging && rules.TelegraphProgress < 0.1f,
                "the first displayed frame of a newly selected attack remains harmless");
        }

        var clock = new EclipseBloomRules();
        clock.Advance(0.01f, 0.5f);
        for (int frame = 0; frame < 30; frame++) clock.Advance(0.1f, 0.5f);
        float rotation = clock.RotationDegrees;
        float elapsed = clock.ElapsedSeconds;
        float progress = clock.TelegraphProgress;
        int mask = clock.AttackMask;
        bool damage = clock.IsDamaging;
        for (int frame = 0; frame < 100; frame++) clock.Advance(0f, 0.1f);
        Near(clock.RotationDegrees, rotation, 0f, "pause freezes geometry rotation");
        Near(clock.ElapsedSeconds, elapsed, 0f, "pause freezes the visual battle clock");
        Near(clock.TelegraphProgress, progress, 0f, "pause freezes attack progress");
        Check(clock.Phase == 2 && clock.AttackMask == mask && clock.IsDamaging == damage && clock.LastStepSeconds == 0f,
            "pause does not progress phase, mask or damage state");
        clock.Advance(100f, 0.5f);
        Near(clock.ElapsedSeconds - elapsed, 0.1f, 0.00001f, "visual clock uses the actual limited simulation step");
        Near(clock.RotationDegrees - rotation, 0.9f, 0.0001f, "petal rotation uses the same limited simulation step");
    }

    private static void AttackPatternsAndRest()
    {
        var first = new EclipseBloomRules();
        int[] expected = { 1, 2, 4, 8, 1, 2, 4, 8 };
        Check(first.AttackMask == expected[0], "first phase starts at petal zero");
        for (int index = 1; index < expected.Length; index++)
            Check(NextTelegraph(first) == expected[index], "first phase visits each intact petal in order");

        var second = new EclipseBloomRules();
        second.Advance(0.01f, 0.5f);
        Check(second.AttackMask == 5, "second phase starts with opposing petal beams");
        Check(NextTelegraph(second, 0.5f) == 10, "second phase then uses the other opposing pair");
        Check(BitCount(NextTelegraph(second, 0.5f)) == 3, "second phase interleaves a three petal pattern");
        Check(second.RotationDegrees > 0f, "second phase slowly rotates its shared geometry");

        foreach (float health in new[] { 1f, 0.5f, 0.2f })
        {
            var core = new EclipseBloomRules();
            if (health < 1f) core.Advance(0.01f, health);
            if (health > 0.25f)
                for (int index = 0; index < 4; index++) core.BreakPetal(index);
            Check(core.IsCoreAttack && core.AttackMask == 5 && !core.IsDamaging && core.TelegraphProgress == 0f,
                "no petals and phase three both start a fresh opposing core warning");
            Until(core, () => core.IsDamaging, health);
            Until(core, () => core.IsResting, health);
            float restStart = core.ElapsedSeconds;
            Check(core.AttackMask == 0 && !core.IsDangerous(0f, 6f), "core recovery has no damage geometry");
            Until(core, () => !core.IsResting, health);
            Check(core.ElapsedSeconds - restStart >= EclipseBloomTuning.CoreRestSeconds - 0.0001f,
                "the exposed core offers a visible full rest interval");
            Check(core.AttackMask == 10 && !core.IsDamaging, "core alternates between two opposing sector pairs");
            Check(NextTelegraph(core, health) == 5, "core repeats its readable alternating pattern");
        }
    }

    private static void AimTracking()
    {
        var rules = new EclipseBloomRules();
        rules.Track(70f);
        float before = rules.RotationDegrees;
        rules.Advance(0.1f, 1f);
        Near(rules.RotationDegrees - before, EclipseBloomTuning.AimTurnDegreesPerSecond * 0.1f, 0.001f,
            "the warned petal turns toward the player at the bounded aim rate");
        Until(rules, () => rules.TelegraphProgress * EclipseBloomTuning.FirstPhaseTelegraphSeconds > EclipseBloomTuning.AimLockSeconds + 0.05f);
        Near(EclipseBloomRules.GetSectorCenterDegrees(0, rules.RotationDegrees), 70f, 0.01f, "the warned sector settles on the player bearing before the lock");
        float locked = rules.RotationDegrees;
        rules.Track(250f);
        Until(rules, () => rules.IsDamaging);
        Near(rules.RotationDegrees, locked, 0f, "after the lock the warning and its damage geometry never move");
        Check(rules.IsDangerous(6f * (float)Math.Sin(70 * Math.PI / 180), 6f * (float)Math.Cos(70 * Math.PI / 180)),
            "the locked sector damages exactly where it was warned");
        rules.Track(float.NaN);
        int serial = rules.AttackSerial;
        Until(rules, () => rules.IsResting);
        Until(rules, () => !rules.IsResting);
        Check(rules.AttackSerial == serial + 1, "each fresh telegraph advances the attack serial once");
    }

    private static void GeometryBoundariesAndMeshSamples()
    {
        Check(EclipseBloomTuning.PetalCount == 4 && EclipseBloomTuning.HalfAngleDegrees < 45f,
            "four petal geometry leaves angular gaps");
        foreach (float rotation in new[] { 0f, 0.1f, 45f, 89.9f, 179f, 270f, 359.9f, -360f, 720f })
        {
            for (int index = 0; index < 4; index++)
            {
                float x, z;
                foreach (float radius in new[] { EclipseBloomTuning.InnerRadius, 6f, EclipseBloomTuning.OuterRadius })
                {
                    foreach (float offset in new[] { -30f, -15f, 0f, 15f, 30f })
                    {
                        EclipseBloomRules.GetSectorPoint(index, rotation, radius, offset, out x, out z);
                        Check(EclipseBloomRules.ContainsSector(x, z, 1 << index, rotation),
                            "every visible sector mesh sample lies in the production damage geometry");
                        Check(!EclipseBloomRules.ContainsSector(x, z, 15 ^ (1 << index), rotation),
                            "a petal sample cannot leak into an unrelated petal sector");
                    }
                }
                foreach (float offset in new[] { -30.1f, 30.1f, 45f })
                {
                    EclipseBloomRules.GetSectorPoint(index, rotation, 6f, offset, out x, out z);
                    Check(!EclipseBloomRules.ContainsSector(x, z, 1 << index, rotation), "points outside sector angle remain safe");
                }
                foreach (float radius in new[] { 0f, EclipseBloomTuning.InnerRadius - 0.01f, 13.01f, 1000f })
                {
                    EclipseBloomRules.GetSectorPoint(index, rotation, radius, 0f, out x, out z);
                    Check(!EclipseBloomRules.ContainsSector(x, z, 15, rotation), "inner refuge and outside range remain safe");
                }
            }
        }
        Near(EclipseBloomRules.GetSectorCenterDegrees(0, 0), 0f, 0f, "sector zero faces positive local Z");
        Near(EclipseBloomRules.GetSectorCenterDegrees(1, 0), 90f, 0f, "sector one faces positive local X");
        Check(EclipseBloomRules.ContainsSector(0f, 6f, 1, 0f), "local Z point belongs to sector zero");
        Check(EclipseBloomRules.ContainsSector(6f, 0f, 2, 0f), "local X point belongs to sector one");
        Check(!EclipseBloomRules.ContainsSector(6f, 0f, 16, 0f), "high mask bits cannot create sectors");

        for (int mask = 0; mask < 16; mask++)
        {
            for (int angle = 0; angle < 360; angle += 3)
            {
                float x = (float)(Math.Sin(angle * Math.PI / 180) * 6);
                float z = (float)(Math.Cos(angle * Math.PI / 180) * 6);
                bool union = false;
                for (int index = 0; index < 4; index++)
                    if ((mask & (1 << index)) != 0) union |= EclipseBloomRules.ContainsSector(x, z, 1 << index, 17f);
                Check(EclipseBloomRules.ContainsSector(x, z, mask, 17f) == union,
                    "all sixteen masks equal the union of exactly their visible petal sectors");
            }
        }
    }

    private static void EnumerateBreakMasksAndRotatingSafeSectors()
    {
        foreach (float health in new[] { 1f, 0.5f })
        {
            for (int intact = 0; intact < 16; intact++)
            {
                var rules = new EclipseBloomRules();
                if (health < 1f) rules.Advance(0.01f, health);
                for (int index = 0; index < 4; index++)
                    if ((intact & (1 << index)) == 0) rules.BreakPetal(index);
                bool attacked = false;
                for (int frame = 0; frame < 900; frame++)
                {
                    rules.Advance(0.05f, health);
                    Check(rules.IntactMask == intact, "all sixteen petal masks remain permanent through repeated cycles");
                    Check(rules.TelegraphProgress >= 0f && rules.TelegraphProgress <= 1f, "warning progress remains bounded");
                    Check(rules.RotationDegrees >= 0f && rules.RotationDegrees < 360f, "rotation remains normalized");
                    if (rules.IsDamaging) attacked = true;
                    if (intact == 0)
                    {
                        Check(rules.IsCoreAttack, "destroying every petal still leaves a live core attack pattern");
                        continue;
                    }
                    Check(!rules.IsCoreAttack && (rules.AttackMask & ~intact) == 0,
                        "petal attacks never reactivate broken sectors");
                    for (int index = 0; index < 4; index++)
                    {
                        if ((intact & (1 << index)) != 0) continue;
                        float x, z;
                        EclipseBloomRules.GetSectorPoint(index, rules.RotationDegrees, 6f, 0f, out x, out z);
                        Check(!rules.IsDangerous(x, z), "broken petal sectors remain safe while the crown rotates");
                    }
                }
                Check(attacked, "every damageable petal state eventually produces an attack");
            }
        }
    }

    private static void EnumerateBreakOrdersAndHealthTrajectories()
    {
        int permutations = 0;
        for (int a = 0; a < 4; a++)
            for (int b = 0; b < 4; b++)
                for (int c = 0; c < 4; c++)
                    for (int d = 0; d < 4; d++)
                    {
                        if (a == b || a == c || a == d || b == c || b == d || c == d) continue;
                        permutations++;
                        var rules = new EclipseBloomRules();
                        foreach (int index in new[] { a, b, c, d })
                        {
                            Until(rules, () => rules.IsDamaging);
                            Check(rules.BreakPetal(index), "every permutation can destroy each remaining petal once");
                            Check(!rules.BreakPetal(index), "repeat petal hits are idempotent in every break order");
                        }
                        Check(rules.IntactMask == 0 && rules.IsCoreAttack && !rules.IsDamaging,
                            "every one of the twenty four break orders ends in a safe core telegraph");
                    }
        Check(permutations == 24, "all petal break orders were exercised");

        float[] samples = { 1f, 0.6001f, 0.60f, 0.2501f, 0.25f, 0f, -1f };
        foreach (float first in samples)
            foreach (float second in samples)
                foreach (float third in samples)
                {
                    var rules = new EclipseBloomRules();
                    int reached = 1;
                    foreach (float health in new[] { first, second, third })
                    {
                        int expected = health <= 0.25f ? 3 : health <= 0.60f ? 2 : 1;
                        reached = Math.Max(reached, expected);
                        int previous = rules.Phase;
                        rules.Advance(0.1f, health);
                        Check(rules.Phase == reached, "all sampled health trajectories only advance at exact thresholds");
                        if (reached > previous)
                            Check(!rules.IsDamaging && rules.TelegraphProgress == 0f,
                                "every phase increase cancels the old attack and resets warning time");
                        if (reached == 3) Check(rules.IntactMask == 0, "no health trajectory can restore retired petals");
                    }
                }
    }

    private static void InvalidInputsAndTerminalStop()
    {
        var rules = new EclipseBloomRules();
        foreach (int index in new[] { -1, 4, 32, int.MinValue, int.MaxValue })
            Check(!rules.BreakPetal(index) && rules.IntactMask == 15, "invalid petal index cannot corrupt the bit mask");
        foreach (float delta in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            rules.Advance(delta, 0f);
            Check(rules.Phase == 1 && rules.ElapsedSeconds == 0f && rules.TelegraphProgress == 0f && rules.LastStepSeconds == 0f,
                "invalid or paused delta cannot alter encounter state");
        }
        foreach (float health in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            rules.Advance(0.05f, health);
            Check(rules.Phase == 1, "invalid health data cannot force a phase transition");
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Check(!EclipseBloomRules.ContainsSector(invalid, 6f, 15, 0f), "non finite local X is safe");
            Check(!EclipseBloomRules.ContainsSector(0f, invalid, 15, 0f), "non finite local Z is safe");
            Check(!EclipseBloomRules.ContainsSector(0f, 6f, 15, invalid), "non finite geometry orientation is safe");
        }
        var independent = new EclipseBloomRules();
        Until(rules, () => rules.IsDamaging);
        Check(!independent.IsDamaging && independent.ElapsedSeconds == 0f && independent.IntactMask == 15,
            "two bosses own independent rule state");
        rules.Stop();
        float elapsed = rules.ElapsedSeconds;
        float rotation = rules.RotationDegrees;
        int intact = rules.IntactMask;
        rules.Stop();
        for (int frame = 0; frame < 100; frame++)
        {
            rules.Advance(100f, 0f);
            Check(!rules.BreakPetal(frame % 4), "stopped encounter rejects later petal callbacks");
            Check(rules.IsStopped && !rules.IsDamaging && !rules.IsResting && rules.AttackMask == 0,
                "stop is terminal and clears all damage geometry");
            Check(!rules.IsDangerous(0f, 6f) && rules.TelegraphProgress == 0f,
                "stopped encounter is harmless even on a previously firing point");
        }
        Check(rules.ElapsedSeconds == elapsed && rules.RotationDegrees == rotation && rules.IntactMask == intact,
            "stopped callbacks cannot change time geometry or petal ownership");
    }

    private static int BitCount(int mask)
    {
        int result = 0;
        for (int index = 0; index < 4; index++) if ((mask & (1 << index)) != 0) result++;
        return result;
    }
}
