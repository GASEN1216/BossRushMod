using System;
using System.Collections;
using System.Collections.Generic;
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
        private static readonly string[] AlphaMaterialPropertyNames = { "_Color", "_TintColor", "_BaseColor" };
        private static readonly Dictionary<Type, PropertyInfo> SharedMaterialPropertyCache = new Dictionary<Type, PropertyInfo>();
        private static readonly Dictionary<Type, MethodInfo> HasPropertyMethodCache = new Dictionary<Type, MethodInfo>();

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

            object[] hasPropertyArgs = new object[1];
            for (int i = 0; i < renderers.Count; i++)
            {
                object renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                PropertyInfo sharedMaterialProperty = GetSharedMaterialProperty(renderer.GetType());
                if (sharedMaterialProperty == null)
                {
                    continue;
                }

                object material = sharedMaterialProperty.GetValue(renderer, null);
                if (material == null)
                {
                    continue;
                }

                MethodInfo hasPropertyMethod = GetHasPropertyMethod(material.GetType());
                if (hasPropertyMethod == null)
                {
                    continue;
                }

                for (int propertyIndex = 0; propertyIndex < AlphaMaterialPropertyNames.Length; propertyIndex++)
                {
                    hasPropertyArgs[0] = AlphaMaterialPropertyNames[propertyIndex];
                    if ((bool)hasPropertyMethod.Invoke(material, hasPropertyArgs))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static PropertyInfo GetSharedMaterialProperty(Type rendererType)
        {
            PropertyInfo property;
            if (!SharedMaterialPropertyCache.TryGetValue(rendererType, out property))
            {
                property = rendererType.GetProperty("sharedMaterial", BindingFlags.Public | BindingFlags.Instance);
                SharedMaterialPropertyCache[rendererType] = property;
            }

            return property;
        }

        private static MethodInfo GetHasPropertyMethod(Type materialType)
        {
            MethodInfo method;
            if (!HasPropertyMethodCache.TryGetValue(materialType, out method))
            {
                method = materialType.GetMethod("HasProperty", new[] { typeof(string) });
                HasPropertyMethodCache[materialType] = method;
            }

            return method;
        }

        internal static void ResetStaticCaches()
        {
            SharedMaterialPropertyCache.Clear();
            HasPropertyMethodCache.Clear();
        }

        internal static void ResetReflectionCachesForTests()
        {
            ResetStaticCaches();
        }

        internal static int CachedReflectionEntryCountForTests
        {
            get
            {
                return SharedMaterialPropertyCache.Count + HasPropertyMethodCache.Count;
            }
        }
    }
}
