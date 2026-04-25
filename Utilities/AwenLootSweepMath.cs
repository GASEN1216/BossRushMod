using System;

namespace BossRush
{
    public static class AwenLootSweepMath
    {
        public const int CostPerLootbox = 10000;
        public const int MinContainerCapacity = 35;
        public const int MaxContainerCapacity = 512;

        public static int CalculateSweepCost(int lootboxCount)
        {
            return Math.Max(0, lootboxCount) * CostPerLootbox;
        }

        public static int CalculateContainerCapacity(int transferableRootItemCount)
        {
            int clamped = Math.Max(MinContainerCapacity, transferableRootItemCount);
            return Math.Min(MaxContainerCapacity, clamped);
        }

        public static int PickConsumedRootIndex(int rootItemCount, double roll)
        {
            if (rootItemCount <= 0)
            {
                return -1;
            }

            double clamped = Math.Max(0.0, Math.Min(0.999999, roll));
            return Math.Min(rootItemCount - 1, (int)(clamped * rootItemCount));
        }
    }
}
