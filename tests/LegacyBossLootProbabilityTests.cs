using System;
using BossRush;

internal static class LegacyBossLootProbabilityTests
{
    private static void AssertClose(string name, double actual, double expected, double tolerance)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new Exception(name + " expected " + expected + " but got " + actual);
        }
    }

    private static void AssertEqual(string name, int actual, int expected)
    {
        if (actual != expected)
        {
            throw new Exception(name + " expected " + expected + " but got " + actual);
        }
    }

    private static void TestBaseProbabilities()
    {
        LegacyBossLootQualityDistribution distribution =
            LegacyBossLootProbabilityModel.BuildDistribution(0.0);

        AssertClose("Q8 base", distribution.Quality8, 0.0005, 0.0000001);
        AssertClose("Q7 base", distribution.Quality7, 0.0010, 0.0000001);
        AssertClose("Q6 base", distribution.Quality6, 0.0100, 0.0000001);
        AssertClose("Q5 base", distribution.Quality5, 0.0500, 0.0000001);
        AssertClose("Total base", distribution.TotalProbability, 1.0, 0.0000001);
    }

    private static void TestMaxProbabilities()
    {
        LegacyBossLootQualityDistribution distribution =
            LegacyBossLootProbabilityModel.BuildDistribution(1.0);

        AssertClose("Q8 max", distribution.Quality8, 0.0010, 0.0000001);
        AssertClose("Q7 max", distribution.Quality7, 0.0100, 0.0000001);
        AssertClose("Q6 max", distribution.Quality6, 0.0500, 0.0000001);
        AssertClose("Q5 max", distribution.Quality5, 0.1000, 0.0000001);
        AssertClose("Total max", distribution.TotalProbability, 1.0, 0.0000001);
    }

    private static void TestGuaranteeQualityRolls()
    {
        AssertEqual("Guarantee Q6", LegacyBossLootProbabilityModel.RollGuaranteeQuality(0.5000), 6);
        AssertEqual("Guarantee boundary Q7", LegacyBossLootProbabilityModel.RollGuaranteeQuality(0.9900), 7);
        AssertEqual("Guarantee Q7", LegacyBossLootProbabilityModel.RollGuaranteeQuality(0.9950), 7);
        AssertEqual("Guarantee boundary Q8", LegacyBossLootProbabilityModel.RollGuaranteeQuality(0.9990), 8);
        AssertEqual("Guarantee Q8", LegacyBossLootProbabilityModel.RollGuaranteeQuality(0.9995), 8);
    }

    private static void TestClampAndLerp()
    {
        AssertClose("Clamp01 low", LegacyBossLootProbabilityModel.Clamp01(-1.0), 0.0, 0.0000001);
        AssertClose("Clamp01 high", LegacyBossLootProbabilityModel.Clamp01(2.0), 1.0, 0.0000001);
        AssertClose("Lerp clamps t", LegacyBossLootProbabilityModel.Lerp(10.0, 20.0, 2.0), 20.0, 0.0000001);
    }

    private static void TestOutOfRangeBonusFactorIsClamped()
    {
        LegacyBossLootQualityDistribution low =
            LegacyBossLootProbabilityModel.BuildDistribution(-10.0);
        LegacyBossLootQualityDistribution high =
            LegacyBossLootProbabilityModel.BuildDistribution(10.0);

        AssertClose("Out of range low clamps to base Q5", low.Quality5, 0.0500, 0.0000001);
        AssertClose("Out of range high clamps to max Q7", high.Quality7, 0.0100, 0.0000001);
    }

    private static void TestInvalidQualityLookup()
    {
        LegacyBossLootQualityDistribution distribution =
            LegacyBossLootProbabilityModel.BuildDistribution(0.5);

        AssertClose("Invalid low quality lookup", distribution.GetProbabilityForQuality(0), 0.0, 0.0000001);
        AssertClose("Invalid high quality lookup", distribution.GetProbabilityForQuality(9), 0.0, 0.0000001);
    }

    public static int Main()
    {
        TestBaseProbabilities();
        TestMaxProbabilities();
        TestGuaranteeQualityRolls();
        TestClampAndLerp();
        TestOutOfRangeBonusFactorIsClamped();
        TestInvalidQualityLookup();
        Console.WriteLine("LegacyBossLootProbabilityTests: PASS");
        return 0;
    }
}
