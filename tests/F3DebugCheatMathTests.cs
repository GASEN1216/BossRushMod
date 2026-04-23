using System;
using BossRush;

internal static class F3DebugCheatMathTests
{
    private static void AssertEqual(string name, float actual, float expected)
    {
        if (Math.Abs(actual - expected) > 0.0001f)
        {
            throw new Exception(name + " expected " + expected + " but got " + actual);
        }
    }

    private static void TestMultiplierDelta()
    {
        AssertEqual("2x health delta", F3DebugCheatMath.ComputeMultiplierAdditiveDelta(100f, 2f), 100f);
        AssertEqual("0.5x health delta", F3DebugCheatMath.ComputeMultiplierAdditiveDelta(100f, 0.5f), -50f);
    }

    private static void TestAbsoluteOverrideDelta()
    {
        AssertEqual("armor up", F3DebugCheatMath.ComputeAbsoluteAdditiveDelta(3f, 7f), 4f);
        AssertEqual("armor down", F3DebugCheatMath.ComputeAbsoluteAdditiveDelta(7f, 0f), -7f);
    }

    private static void TestClampMinMultiplier()
    {
        AssertEqual("clamp negative multiplier", F3DebugCheatMath.SanitizeMultiplier(-5f), 0f);
    }

    public static int Main()
    {
        TestMultiplierDelta();
        TestAbsoluteOverrideDelta();
        TestClampMinMultiplier();
        Console.WriteLine("F3DebugCheatMathTests: PASS");
        return 0;
    }
}
