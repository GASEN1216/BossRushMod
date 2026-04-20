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
        AssertEqual("Guarantee Q7", LegacyBossLootProbabilityModel.RollGuaranteeQuality(0.9950), 7);
        AssertEqual("Guarantee Q8", LegacyBossLootProbabilityModel.RollGuaranteeQuality(0.9995), 8);
    }

    public static int Main()
    {
        TestBaseProbabilities();
        TestMaxProbabilities();
        TestGuaranteeQualityRolls();
        Console.WriteLine("LegacyBossLootProbabilityTests: PASS");
        return 0;
    }
}
