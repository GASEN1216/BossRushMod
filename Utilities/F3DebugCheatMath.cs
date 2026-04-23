namespace BossRush
{
    internal static class F3DebugCheatMath
    {
        public static float ComputeMultiplierAdditiveDelta(float baseValue, float multiplier)
        {
            return baseValue * (SanitizeMultiplier(multiplier) - 1f);
        }

        public static float ComputeAbsoluteAdditiveDelta(float currentValue, float targetValue)
        {
            return targetValue - currentValue;
        }

        public static float SanitizeMultiplier(float multiplier)
        {
            return multiplier < 0f ? 0f : multiplier;
        }
    }
}
