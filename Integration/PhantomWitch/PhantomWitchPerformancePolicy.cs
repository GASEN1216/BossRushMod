using System;

namespace BossRush
{
    internal enum PhantomWitchFxDetailLevel
    {
        Full,
        Reduced,
        Minimal
    }

    internal enum PhantomWitchFxEffectImportance
    {
        Critical,
        Standard,
        Optional
    }

    internal static class PhantomWitchPerformancePolicy
    {
        internal static PhantomWitchFxDetailLevel ResolveFxDetailLevel(
            int activeRootCount,
            bool isLowSpecHardware,
            int reducedThreshold,
            int minimalThreshold)
        {
            int safeReducedThreshold = Math.Max(0, reducedThreshold);
            int safeMinimalThreshold = Math.Max(safeReducedThreshold, minimalThreshold);

            if (activeRootCount >= safeMinimalThreshold)
            {
                return PhantomWitchFxDetailLevel.Minimal;
            }

            if (isLowSpecHardware)
            {
                return PhantomWitchFxDetailLevel.Minimal;
            }

            if (activeRootCount >= safeReducedThreshold)
            {
                return PhantomWitchFxDetailLevel.Reduced;
            }

            return PhantomWitchFxDetailLevel.Full;
        }

        internal static bool ShouldSkipEffect(
            PhantomWitchFxDetailLevel detailLevel,
            int activeRootCount,
            int reducedThreshold,
            int minimalThreshold,
            PhantomWitchFxEffectImportance importance)
        {
            if (importance == PhantomWitchFxEffectImportance.Critical)
            {
                return false;
            }

            int safeMinimalThreshold = Math.Max(Math.Max(0, reducedThreshold), minimalThreshold);
            if (importance == PhantomWitchFxEffectImportance.Optional)
            {
                return detailLevel == PhantomWitchFxDetailLevel.Minimal ||
                       activeRootCount >= safeMinimalThreshold;
            }

            return detailLevel == PhantomWitchFxDetailLevel.Minimal &&
                   activeRootCount >= safeMinimalThreshold;
        }
    }
}
