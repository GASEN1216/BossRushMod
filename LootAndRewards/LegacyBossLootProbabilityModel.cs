using System;

namespace BossRush
{
    internal struct LegacyBossLootQualityDistribution
    {
        public double Quality1;
        public double Quality2;
        public double Quality3;
        public double Quality4;
        public double Quality5;
        public double Quality6;
        public double Quality7;
        public double Quality8;

        public double TotalProbability
        {
            get
            {
                return Quality1 + Quality2 + Quality3 + Quality4 +
                    Quality5 + Quality6 + Quality7 + Quality8;
            }
        }

        public double GetProbabilityForQuality(int quality)
        {
            switch (quality)
            {
                case 1: return Quality1;
                case 2: return Quality2;
                case 3: return Quality3;
                case 4: return Quality4;
                case 5: return Quality5;
                case 6: return Quality6;
                case 7: return Quality7;
                case 8: return Quality8;
                default: return 0.0;
            }
        }
    }

    internal static class LegacyBossLootProbabilityModel
    {
        private const double LowQualityWeight1 = 0.1345;
        private const double LowQualityWeight2 = 0.3655;
        private const double LowQualityWeight3 = 0.3655;
        private const double LowQualityWeight4 = 0.1345;

        public static LegacyBossLootQualityDistribution BuildDistribution(double bonusFactor)
        {
            double t = Clamp01(bonusFactor);
            double quality8 = Lerp(0.0005, 0.0010, t);
            double quality7 = Lerp(0.0010, 0.0100, t);
            double quality6 = Lerp(0.0100, 0.0500, t);
            double quality5 = Lerp(0.0500, 0.1000, t);

            double remaining = 1.0 - (quality5 + quality6 + quality7 + quality8);
            if (remaining < 0.0)
            {
                remaining = 0.0;
            }

            return new LegacyBossLootQualityDistribution
            {
                Quality1 = remaining * LowQualityWeight1,
                Quality2 = remaining * LowQualityWeight2,
                Quality3 = remaining * LowQualityWeight3,
                Quality4 = remaining * LowQualityWeight4,
                Quality5 = quality5,
                Quality6 = quality6,
                Quality7 = quality7,
                Quality8 = quality8
            };
        }

        public static int RollGuaranteeQuality(double roll)
        {
            double clampedRoll = Clamp01(roll);
            if (clampedRoll < 0.99)
            {
                return 6;
            }

            if (clampedRoll < 0.999)
            {
                return 7;
            }

            return 8;
        }

        public static double Clamp01(double value)
        {
            if (value < 0.0)
            {
                return 0.0;
            }

            if (value > 1.0)
            {
                return 1.0;
            }

            return value;
        }

        public static double Lerp(double min, double max, double t)
        {
            return min + ((max - min) * Clamp01(t));
        }
    }
}
