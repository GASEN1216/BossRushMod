using System;
using BossRush;

internal static class VictoryRewardShadowMathTests
{
    private static void AssertEqual(string name, float actual, float expected)
    {
        if (Math.Abs(actual - expected) > 0.0001f)
        {
            throw new Exception(name + " expected " + expected + " but got " + actual);
        }
    }

    private static void TestComputeFollowYAddsBaseOffset()
    {
        float y = VictoryRewardShadowMath.ComputeFollowY(4f, 0f);
        AssertEqual("follow y", y, 7.2f);
    }

    private static void TestComputeLandingYUsesGroundHeightWhenAvailable()
    {
        float y = VictoryRewardShadowMath.ComputeLandingY(9f, true, 1.5f);
        AssertEqual("landing grounded", y, 1.55f);
    }

    private static void TestComputeLandingYFallsBackToAnchorHeight()
    {
        float y = VictoryRewardShadowMath.ComputeLandingY(9f, false, 0f);
        AssertEqual("landing fallback", y, 9.05f);
    }

    private static void TestMoveTowardsYClampsToTarget()
    {
        float y = VictoryRewardShadowMath.MoveTowardsY(5f, 2f, 10f, 1f);
        AssertEqual("move towards clamp", y, 2f);
    }

    private static void TestMoveTowardsYUsesSpeedAndDeltaTime()
    {
        float y = VictoryRewardShadowMath.MoveTowardsY(5f, 2f, 1.4f, 1f);
        AssertEqual("move towards step", y, 3.6f);
    }

    public static int Main()
    {
        TestComputeFollowYAddsBaseOffset();
        TestComputeLandingYUsesGroundHeightWhenAvailable();
        TestComputeLandingYFallsBackToAnchorHeight();
        TestMoveTowardsYClampsToTarget();
        TestMoveTowardsYUsesSpeedAndDeltaTime();
        Console.WriteLine("VictoryRewardShadowMathTests: PASS");
        return 0;
    }
}
