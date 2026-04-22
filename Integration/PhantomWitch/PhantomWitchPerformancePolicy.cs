using System;
using System.Collections;
using System.Reflection;

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
            int reducedThreshold,
            int minimalThreshold)
        {
            int safeReducedThreshold = Math.Max(0, reducedThreshold);
            int safeMinimalThreshold = Math.Max(safeReducedThreshold, minimalThreshold);

            if (activeRootCount >= safeMinimalThreshold)
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

        internal static bool SupportsAlphaModulation(IList renderers)
        {
            if (renderers == null || renderers.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                object renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                PropertyInfo sharedMaterialProperty = renderer.GetType().GetProperty("sharedMaterial", BindingFlags.Public | BindingFlags.Instance);
                if (sharedMaterialProperty == null)
                {
                    continue;
                }

                object material = sharedMaterialProperty.GetValue(renderer, null);
                if (material == null)
                {
                    continue;
                }

                MethodInfo hasPropertyMethod = material.GetType().GetMethod("HasProperty", new[] { typeof(string) });
                if (hasPropertyMethod == null)
                {
                    continue;
                }

                if ((bool)hasPropertyMethod.Invoke(material, new object[] { "_Color" }) ||
                    (bool)hasPropertyMethod.Invoke(material, new object[] { "_TintColor" }) ||
                    (bool)hasPropertyMethod.Invoke(material, new object[] { "_BaseColor" }))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
