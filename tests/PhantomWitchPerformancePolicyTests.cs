using System;
using BossRush;

internal static class PhantomWitchPerformancePolicyTests
{
    private static void AssertEqual<T>(string name, T actual, T expected)
    {
        if (!Equals(actual, expected))
        {
            throw new Exception(name + " expected " + expected + " but got " + actual);
        }
    }

    private static void AssertTrue(string name, bool value)
    {
        if (!value)
        {
            throw new Exception(name + " expected true but got false");
        }
    }

    private static void AssertFalse(string name, bool value)
    {
        if (value)
        {
            throw new Exception(name + " expected false but got true");
        }
    }

    private static void TestResolveFxDetailLevel()
    {
        AssertEqual(
            "ResolveFxDetailLevel normal",
            PhantomWitchPerformancePolicy.ResolveFxDetailLevel(0, 6, 10),
            PhantomWitchFxDetailLevel.Full);

        AssertEqual(
            "ResolveFxDetailLevel reduced threshold",
            PhantomWitchPerformancePolicy.ResolveFxDetailLevel(6, 6, 10),
            PhantomWitchFxDetailLevel.Reduced);

        AssertEqual(
            "ResolveFxDetailLevel minimal threshold",
            PhantomWitchPerformancePolicy.ResolveFxDetailLevel(10, 6, 10),
            PhantomWitchFxDetailLevel.Minimal);

        AssertEqual(
            "ResolveFxDetailLevel ignores machine class and follows active roots only",
            PhantomWitchPerformancePolicy.ResolveFxDetailLevel(0, 6, 10),
            PhantomWitchFxDetailLevel.Full);
    }

    private static void TestShouldSkipEffect()
    {
        AssertTrue(
            "Optional effect skipped on minimal",
            PhantomWitchPerformancePolicy.ShouldSkipEffect(
                PhantomWitchFxDetailLevel.Minimal,
                0,
                6,
                10,
                PhantomWitchFxEffectImportance.Optional));

        AssertFalse(
            "Optional effect kept on reduced",
            PhantomWitchPerformancePolicy.ShouldSkipEffect(
                PhantomWitchFxDetailLevel.Reduced,
                0,
                6,
                10,
                PhantomWitchFxEffectImportance.Optional));

        AssertFalse(
            "Critical effect kept on minimal",
            PhantomWitchPerformancePolicy.ShouldSkipEffect(
                PhantomWitchFxDetailLevel.Minimal,
                10,
                6,
                10,
                PhantomWitchFxEffectImportance.Critical));

        AssertTrue(
            "Standard effect skipped when minimal and saturated",
            PhantomWitchPerformancePolicy.ShouldSkipEffect(
                PhantomWitchFxDetailLevel.Minimal,
                10,
                6,
                10,
                PhantomWitchFxEffectImportance.Standard));

        AssertFalse(
            "Standard effect kept on minimal before saturation",
            PhantomWitchPerformancePolicy.ShouldSkipEffect(
                PhantomWitchFxDetailLevel.Minimal,
                5,
                6,
                10,
                PhantomWitchFxEffectImportance.Standard));
    }

    private static void TestThresholdNormalization()
    {
        AssertEqual(
            "ResolveFxDetailLevel normalizes negative thresholds",
            PhantomWitchPerformancePolicy.ResolveFxDetailLevel(0, -2, -1),
            PhantomWitchFxDetailLevel.Minimal);

        AssertTrue(
            "Optional effect skipped when active roots reach normalized minimal threshold",
            PhantomWitchPerformancePolicy.ShouldSkipEffect(
                PhantomWitchFxDetailLevel.Reduced,
                5,
                5,
                3,
                PhantomWitchFxEffectImportance.Optional));
    }

    public static int Main()
    {
        TestResolveFxDetailLevel();
        TestShouldSkipEffect();
        TestThresholdNormalization();
        Console.WriteLine("PhantomWitchPerformancePolicyTests: PASS");
        return 0;
    }
}
