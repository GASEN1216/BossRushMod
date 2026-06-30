using System;
using System.Collections.Generic;
using BossRush;

internal static class PhantomWitchPerformancePolicyTests
{
    private sealed class FakeRenderer
    {
        public FakeMaterial sharedMaterial { get; set; }
    }

    private sealed class FakeMaterial
    {
        private readonly HashSet<string> properties;

        public FakeMaterial(params string[] propertyNames)
        {
            properties = new HashSet<string>(propertyNames);
        }

        public bool HasProperty(string propertyName)
        {
            return properties.Contains(propertyName);
        }
    }

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

    private static void TestSupportsAlphaModulation()
    {
        AssertFalse(
            "SupportsAlphaModulation rejects null list",
            PhantomWitchPerformancePolicy.SupportsAlphaModulation(null));

        AssertFalse(
            "SupportsAlphaModulation rejects empty list",
            PhantomWitchPerformancePolicy.SupportsAlphaModulation(new object[0]));

        AssertFalse(
            "SupportsAlphaModulation rejects unsupported renderer and material",
            PhantomWitchPerformancePolicy.SupportsAlphaModulation(new object[]
            {
                new object(),
                null,
                new FakeRenderer { sharedMaterial = new FakeMaterial("_MainTex") }
            }));

        PhantomWitchPerformancePolicy.ResetReflectionCachesForTests();
        AssertTrue(
            "SupportsAlphaModulation accepts _TintColor",
            PhantomWitchPerformancePolicy.SupportsAlphaModulation(new object[]
            {
                new FakeRenderer { sharedMaterial = new FakeMaterial("_TintColor") }
            }));

        int cachedEntryCount = PhantomWitchPerformancePolicy.CachedReflectionEntryCountForTests;
        AssertTrue(
            "SupportsAlphaModulation populates reflection caches",
            cachedEntryCount >= 2);

        AssertTrue(
            "SupportsAlphaModulation reuses cached reflection entries",
            PhantomWitchPerformancePolicy.SupportsAlphaModulation(new object[]
            {
                new FakeRenderer { sharedMaterial = new FakeMaterial("_BaseColor") }
            }));

        AssertEqual(
            "SupportsAlphaModulation cache entry count is stable for reused types",
            PhantomWitchPerformancePolicy.CachedReflectionEntryCountForTests,
            cachedEntryCount);
    }

    public static int Main()
    {
        TestResolveFxDetailLevel();
        TestShouldSkipEffect();
        TestThresholdNormalization();
        TestSupportsAlphaModulation();
        Console.WriteLine("PhantomWitchPerformancePolicyTests: PASS");
        return 0;
    }
}
