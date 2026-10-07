using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        internal string name;
        private static bool Missing(Object value) { return ReferenceEquals(value, null) || value.Destroyed; }
        public static bool operator ==(Object a, Object b) { return Missing(a) ? Missing(b) : !Missing(b) && ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        internal static void Destroy(Object value) { value.Destroyed = true; }
    }

    internal static class Resources
    {
        internal static CharacterRandomPreset[] Presets = new CharacterRandomPreset[0];
        internal static int Queries;
        internal static T[] FindObjectsOfTypeAll<T>() where T : Object { Queries++; return Presets as T[]; }
    }
}

internal sealed class CharacterRandomPreset : UnityEngine.Object
{
    internal string nameKey;
    internal float health = 100;
}

namespace BossRush
{
    internal enum Teams { wolf, scav }
    internal static class L10n { internal static string T(string cn, string en) { return cn; } }
    internal static class DragonKingConfig
    {
        internal const string BossNameKey = "king", BossNameCN = "king", BossNameEN = "king";
        internal const float BaseHealth = 200, DamageMultiplier = 1;
    }
    internal static class DragonDescendantConfig
    {
        internal const string BOSS_NAME_KEY = "descendant", BOSS_NAME_CN = "descendant", BOSS_NAME_EN = "descendant";
        internal const float BaseHealth = 150, DamageMultiplier = 1;
    }
    internal static class PhantomWitchConfig
    {
        internal const string BossNameKey = "witch", BossNameCN = "witch", BossNameEN = "witch";
        internal const string BasePresetNameKey = "Cname_Ghost", FallbackPresetNameKey = "Cname_Boss_Red";
        internal const float BaseHealth = 125, DamageMultiplier = 1;
    }

    internal abstract class PresetCatalogHarness
    {
        internal List<EnemyPresetInfo> Catalog = new List<EnemyPresetInfo>();
        protected bool HasArenaEnemyPresetCatalog { get { return Catalog != null; } }
        protected EnemyPresetInfo FindArenaEnemyPreset(string key)
        {
            if (Catalog != null) foreach (EnemyPresetInfo entry in Catalog) if (entry.name == key) return entry;
            return null;
        }
        protected void AddArenaEnemyPreset(EnemyPresetInfo value) { Catalog.Add(value); }
        protected static void DevLog(string message) { }
    }

    internal sealed partial class DragonKingRuntimeModule : PresetCatalogHarness
    { private static bool dragonKingRegistered; }
    internal sealed partial class DragonDescendantRuntimeModule : PresetCatalogHarness
    { private static bool dragonDescendantRegistered; }
    internal sealed partial class PhantomWitchRuntimeModule : PresetCatalogHarness
    {
        private static bool phantomWitchRegistered;
        private static CharacterRandomPreset cachedPhantomWitchBasePreset;
    }

    internal static partial class Program
    {
        private static void CheckPresetRecovery()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var king = new DragonKingRuntimeModule();
                var descendant = new DragonDescendantRuntimeModule();
                var witch = new PhantomWitchRuntimeModule();
                CheckCatalog(king, king.RegisterDragonKingPreset, DragonKingConfig.BossNameKey);
                CheckCatalog(descendant, descendant.RegisterDragonDescendantPreset, DragonDescendantConfig.BOSS_NAME_KEY);
                CheckCatalog(witch, witch.RegisterPhantomWitchPreset, PhantomWitchConfig.BossNameKey);
            }

            var module = new PhantomWitchRuntimeModule();
            Check(module.FindPhantomWitchBasePreset() == null, "witch lookup initially misses unloaded resources");
            var ghost = new CharacterRandomPreset { nameKey = PhantomWitchConfig.BasePresetNameKey, name = "ghost" };
            UnityEngine.Resources.Presets = new[] { ghost };
            Check(ReferenceEquals(module.FindPhantomWitchBasePreset(), ghost), "witch retries after a miss when resources arrive");
            int queries = UnityEngine.Resources.Queries;
            Check(ReferenceEquals(module.FindPhantomWitchBasePreset(), ghost) && UnityEngine.Resources.Queries == queries,
                "valid witch cache avoids another resource scan");

            UnityEngine.Object.Destroy(ghost);
            var replacement = new CharacterRandomPreset { nameKey = PhantomWitchConfig.BasePresetNameKey, name = "replacement" };
            UnityEngine.Resources.Presets = new[] { ghost, replacement };
            Check(ReferenceEquals(module.FindPhantomWitchBasePreset(), replacement),
                "destroyed Unity cache is skipped and replaced by a live resource");
            UnityEngine.Object.Destroy(replacement);
            var fallback = new CharacterRandomPreset { nameKey = PhantomWitchConfig.FallbackPresetNameKey, name = "fallback" };
            UnityEngine.Resources.Presets = new[] { fallback };
            Check(ReferenceEquals(module.FindPhantomWitchBasePreset(), fallback), "witch keeps the official fallback when no ghost is loaded");
            UnityEngine.Object.Destroy(fallback);
            UnityEngine.Resources.Presets = new CharacterRandomPreset[0];
            Check(module.FindPhantomWitchBasePreset() == null, "exhausted destroyed caches return null without reusing destroyed objects");
            Console.WriteLine("Custom boss preset catalog and witch cache recovery: PASS");
        }

        private static void CheckCatalog(PresetCatalogHarness module, Action register, string key)
        {
            register();
            Check(module.Catalog.Count == 1 && module.Catalog[0].name == key,
                "each new host registers its custom boss despite prior static flags");
            EnemyPresetInfo first = module.Catalog[0];
            register();
            Check(module.Catalog.Count == 1 && ReferenceEquals(module.Catalog[0], first), "existing preset registration stays idempotent");
            module.Catalog.Clear();
            register();
            Check(module.Catalog.Count == 1 && module.Catalog[0].name == key && !ReferenceEquals(module.Catalog[0], first),
                "cleared catalog receives its custom boss again");
            module.Catalog = null;
            register();
            module.Catalog = new List<EnemyPresetInfo>();
            register();
            Check(module.Catalog.Count == 1, "missing then restored catalog can still register");
        }
    }
}
