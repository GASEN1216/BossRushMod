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
        protected ModBehaviour owner;
        internal List<EnemyPresetInfo> Catalog
        {
            get { return owner != null ? owner.Pool : null; }
            set { owner.Pool = value; }
        }
        public abstract void OnAwake(ModBehaviour owner);
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

    internal abstract class RuntimeStartHarness
    {
        public abstract void OnStart();
    }

    internal sealed partial class BossFilterRuntimeModule : RuntimeStartHarness
    {
        private readonly ModBehaviour owner;
        private bool bossPoolFilterInitialized, showBossPoolWindow, _filteredPresetsCacheDirty = true;
        private readonly Dictionary<string, bool> bossEnabledStates = new Dictionary<string, bool>();
        private readonly Dictionary<string, float> bossInfiniteHellFactors = new Dictionary<string, float>();
        private List<EnemyPresetInfo> _filteredPresetsCache;
        internal BossFilterRuntimeModule(ModBehaviour owner) { this.owner = owner; }
        internal bool Initialized { get { return bossPoolFilterInitialized; } }
        internal float FactorForTest(string key) { return bossInfiniteHellFactors[key]; }
        private void DestroyBossPoolUI() { }
    }

    internal sealed class CatalogObserver
    {
        private readonly ModBehaviour owner;
        internal List<EnemyPresetInfo> LastPool;
        internal int Refreshes;
        internal CatalogObserver(ModBehaviour owner) { this.owner = owner; }
        internal void NotifyEnemyPresetsRefreshed()
        {
            LastPool = new List<EnemyPresetInfo>(owner.GetFilteredEnemyPresets());
            Refreshes++;
        }
    }

    internal sealed partial class ModBehaviour
    {
        internal readonly BossFilterRuntimeModule Filter;
        internal readonly CatalogObserver PetNestRuntime, CodexRuntime;
        internal readonly List<string> DisabledBosses = new List<string>();
        internal readonly Dictionary<string, float> SavedFactors = new Dictionary<string, float>();
        internal readonly List<PresetCatalogHarness> Attached = new List<PresetCatalogHarness>();
        internal ModBehaviour()
        {
            Filter = new BossFilterRuntimeModule(this);
            PetNestRuntime = new CatalogObserver(this);
            CodexRuntime = new CatalogObserver(this);
        }
        internal List<EnemyPresetInfo> BossFilterEnemyPresets { get { return Pool; } }
        internal List<string> GetBossFilterDisabledBosses() { return DisabledBosses; }
        internal Dictionary<string, float> GetBossFilterSavedFactors() { return SavedFactors; }
        internal void AttachDragonKingRuntimeModule(DragonKingRuntimeModule module) { Attached.Add(module); }
        internal void AttachDragonDescendantRuntimeModule(DragonDescendantRuntimeModule module) { Attached.Add(module); }
        internal void AttachPhantomWitchRuntimeModule(PhantomWitchRuntimeModule module) { Attached.Add(module); }
        internal void ResetBossPoolFilterStateForArena() { Filter.ResetBossPoolFilterStateForEnemyPresetRefresh(); }
        internal void InitializeBossPoolFilterForArena() { Filter.InitializeBossPoolFilter(); }
        internal static void DevLog(string message) { }
    }

    internal static partial class Program
    {
        private static void CheckAwakePresetRecovery()
        {
            var owner = new ModBehaviour();
            owner.Pool.Add(new EnemyPresetInfo { name = "official_enabled" });
            owner.Pool.Add(new EnemyPresetInfo { name = "official_disabled" });
            owner.InitializeBossPoolFilterForArena();
            WavesArenaRuntimeModule.EnemyPresetsInitialized = true;

            var king = new DragonKingRuntimeModule();
            var descendant = new DragonDescendantRuntimeModule();
            var witch = new PhantomWitchRuntimeModule();
            // Runtime host creates module objects before callbacks, but their owner is still null.
            king.RegisterDragonKingPreset();
            descendant.RegisterDragonDescendantPreset();
            witch.RegisterPhantomWitchPreset();
            Check(owner.Pool.Count == 2, "prewarm cannot register custom bosses before owner binding");
            PresetCatalogHarness[] modules = { king, descendant, witch };
            foreach (PresetCatalogHarness module in modules)
            {
                int before = owner.Pool.Count;
                module.OnAwake(owner);
                Check(owner.Attached.Contains(module) && owner.Pool.Count == before + 1,
                    "Awake binds the owner and repairs the already initialized pool");
                Check(!owner.Filter.Initialized,
                    "Awake repair invalidates the filter until real configuration arrives");
                Check(owner.PetNestRuntime.LastPool.Count == owner.Pool.Count
                    && owner.CodexRuntime.LastPool.Count == owner.Pool.Count,
                    "Awake repair republishes each appended boss to both catalogs");
                int refreshes = owner.PetNestRuntime.Refreshes;
                module.OnAwake(owner);
                Check(owner.Pool.Count == before + 1 && owner.PetNestRuntime.Refreshes == refreshes,
                    "repeated Awake neither duplicates bosses nor resets configured state");
            }
            // ModBehaviour.Start loads config in StartIntegrationRuntime before host.OnStart.
            owner.DisabledBosses.Add("official_disabled");
            owner.DisabledBosses.Add(PhantomWitchConfig.BossNameKey);
            owner.SavedFactors[DragonKingConfig.BossNameKey] = 0.2f;
            owner.Filter.OnStart();
            Check(owner.Filter.Initialized && !owner.Filter.IsBossEnabled("official_disabled"),
                "Start repairs the prewarmed filter after real configuration is loaded");
            Check(!owner.CodexRuntime.LastPool.Exists(preset => preset.name == "official_disabled")
                && !owner.PetNestRuntime.LastPool.Exists(preset => preset.name == "official_disabled"),
                "both catalog notifications finish with the configured filtered pool");
            Check(owner.GetFilteredEnemyPresets().Count == 3
                && !owner.Filter.IsBossEnabled(PhantomWitchConfig.BossNameKey),
                "late custom entries also respect their saved disabled setting");
            Check(owner.Filter.FactorForTest(DragonKingConfig.BossNameKey) == 0.2f,
                "late registration preserves saved Infinite Hell factors");

            // A complete prewarmed pool also needs to replace its default Awake configuration.
            var completeOwner = new ModBehaviour();
            completeOwner.Pool.Add(new EnemyPresetInfo { name = "official_disabled" });
            completeOwner.InitializeBossPoolFilterForArena();
            completeOwner.DisabledBosses.Add("official_disabled");
            completeOwner.Filter.OnStart();
            Check(completeOwner.GetFilteredEnemyPresets().Count == 0,
                "Start reloads saved exclusions even when Awake had already initialized the filter");

            WavesArenaRuntimeModule.EnemyPresetsInitialized = false;
            var coldOwner = new ModBehaviour();
            var coldKing = new DragonKingRuntimeModule();
            var coldDescendant = new DragonDescendantRuntimeModule();
            var coldWitch = new PhantomWitchRuntimeModule();
            coldKing.OnAwake(null);
            coldDescendant.OnAwake(null);
            coldWitch.OnAwake(null);
            coldKing.OnAwake(coldOwner);
            coldDescendant.OnAwake(coldOwner);
            coldWitch.OnAwake(coldOwner);
            coldOwner.Filter.OnStart();
            Check(coldOwner.Pool.Count == 0 && coldOwner.PetNestRuntime.Refreshes == 0,
                "cold Awake does not create a partial catalog or trigger a premature scan");
            coldKing.RegisterDragonKingPreset();
            coldDescendant.RegisterDragonDescendantPreset();
            coldWitch.RegisterPhantomWitchPreset();
            Check(coldOwner.Pool.Count == 3, "normal later initialization retains all custom bosses");
            Console.WriteLine("Custom boss Awake order and configured catalog recovery: PASS");
        }

        private static void CheckPresetRecovery()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var king = new DragonKingRuntimeModule();
                var descendant = new DragonDescendantRuntimeModule();
                var witch = new PhantomWitchRuntimeModule();
                king.OnAwake(new ModBehaviour());
                descendant.OnAwake(new ModBehaviour());
                witch.OnAwake(new ModBehaviour());
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
