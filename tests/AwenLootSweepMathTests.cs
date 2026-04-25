using System;
using BossRush;

internal static class AwenLootSweepMathTests
{
    private static void AssertEqual(string name, int actual, int expected)
    {
        if (actual != expected)
        {
            throw new Exception(name + " expected " + expected + " but got " + actual);
        }
    }

    private static void TestCalculateSweepCost()
    {
        AssertEqual("zero boxes", AwenLootSweepMath.CalculateSweepCost(0), 0);
        AssertEqual("three boxes", AwenLootSweepMath.CalculateSweepCost(3), 30000);
    }

    private static void TestCalculateContainerCapacity()
    {
        AssertEqual("min capacity", AwenLootSweepMath.CalculateContainerCapacity(0), 35);
        AssertEqual("exact capacity", AwenLootSweepMath.CalculateContainerCapacity(40), 40);
        AssertEqual("clamped capacity", AwenLootSweepMath.CalculateContainerCapacity(999), 512);
    }

    private static void TestPickConsumedRootIndex()
    {
        AssertEqual("empty box", AwenLootSweepMath.PickConsumedRootIndex(0, 0.5), -1);
        AssertEqual("first root", AwenLootSweepMath.PickConsumedRootIndex(3, 0.0), 0);
        AssertEqual("last root", AwenLootSweepMath.PickConsumedRootIndex(3, 0.9999), 2);
    }

    public static int Main()
    {
        TestCalculateSweepCost();
        TestCalculateContainerCapacity();
        TestPickConsumedRootIndex();
        Console.WriteLine("AwenLootSweepMathTests: PASS");
        return 0;
    }
}
