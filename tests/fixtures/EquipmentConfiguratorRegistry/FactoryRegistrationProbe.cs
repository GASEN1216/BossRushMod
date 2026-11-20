using System;
using System.Collections.Generic;
using System.IO;

namespace FactoryProbe
{
    internal static class FactoryRegistrationProbe
    {
        internal static void Run()
        {
            ModBehaviour.Root = Path.Combine(Path.GetTempPath(), "BossRushFactory-" + Guid.NewGuid().ToString("N"));
            try
            {
                VerifyFactory("Assets/Items", 500701, ItemFactory.LoadBundle, item =>
                    ItemFactory.RegisterProbeConfigurator(item.TypeID, value => value.Configurations++));
                VerifyFactory("Assets/Equipment", 500711, EquipmentFactory.LoadBundle, item => { });
                VerifyColdStart();
                VerifySplitWeapon();
            }
            finally { Directory.Delete(ModBehaviour.Root, true); }
        }

        private static void VerifyFactory(string directory, int firstId, Func<string, int> load, Action<Item> register)
        {
            const string bundleName = "pair";
            var first = new Item(firstId);
            var second = new Item(firstId + 1);
            register(first);
            register(second);
            var bundle = PrepareBundle(directory, bundleName, first, second);
            BossRushDynamicItemRegistry.Refuse = second.TypeID;
            Check(load(bundleName) == 1, "only a successful publication counts");
            Check(BossRushDynamicItemRegistry.Get(first.TypeID) == first && BossRushDynamicItemRegistry.Get(second.TypeID) == null,
                "partial success retains the first item");

            BossRushDynamicItemRegistry.Refuse = -1;
            BossRushDynamicItemRegistry.AcknowledgeWithoutRegistering = second.TypeID;
            Check(load(bundleName) == 0, "a successful return without an actual official entry does not count");
            BossRushDynamicItemRegistry.AcknowledgeWithoutRegistering = -1;
            Check(load(bundleName) == 1 && BossRushDynamicItemRegistry.Get(second.TypeID) == second, "the failed member is retried");
            Check(first.Configurations == 1 && second.Configurations == 1 && first.PublicationAttempts == 1 && second.PublicationAttempts == 3,
                "retry skips the published neighbor and reuses the configured prefab");
            Check(bundle.Opens == 1 && load(bundleName) == 0, "fully published bundle is reused");

            BossRushDynamicItemRegistry.RegisterForProbe(new Item(first.TypeID) { Destroyed = true });
            Check(load(bundleName) == 1 && BossRushDynamicItemRegistry.Get(first.TypeID) == first && first.Configurations == 1,
                "a Unity-null official entry republished the live factory prefab");
            first.Destroyed = true;
            var replacement = new Item(first.TypeID);
            bundle.Assets[0] = new GameObject(replacement);
            Check(load(bundleName) == 1 && BossRushDynamicItemRegistry.Get(first.TypeID) == replacement && replacement.Configurations == 1,
                "a reloaded source asset is configured and published");
            Check(bundle.Opens == 1 && second.PublicationAttempts == 3, "repair still shares the bundle and leaves the sibling untouched");
        }

        private static void VerifyColdStart()
        {
            const int typeId = 500721;
            const string bundleName = "cold_equipment";
            var item = new Item(typeId);
            var bundle = PrepareBundle("Assets/Equipment", bundleName, item);
            string selectedDirectory = ModBehaviour.Root;
            ModBehaviour.Root = Path.Combine(selectedDirectory, "previous-copy");
            Check(EquipmentFactory.LoadBundle(bundleName) == 0, "previous copy does not contain the requested bundle");
            ModBehaviour.Root = selectedDirectory;
            BossRushDynamicItemRegistry.SetEquipmentPlan(typeId, bundleName);
            ModBehaviour.Instance = new ModBehaviour();
            ModBehaviour.Instance.ConfigurationAllowed = false;
            Check(!BossRushDynamicItemRegistry.EnsureRegistered(typeId) && bundle.Opens == 0,
                "cold equipment does not publish before configuring the registry");
            ModBehaviour.Instance.ConfigurationAllowed = true;
            Check(BossRushDynamicItemRegistry.EnsureRegistered(typeId), "cold equipment succeeds on the next request");
            Check(ModBehaviour.Instance.ConfiguratorsInstalled && ResourceBundleLoader.LoadedInRestoreScope && item.Configurations == 1,
                "equipment-only plan installs configurators then opens the bundle in restore scope");
            Check(!ResourceBundleLoader.InRestoreScope, "restore scope exits after publication");
        }

        private static void VerifySplitWeapon()
        {
            const int typeId = 500731;
            const string itemBundle = "split_item";
            const string modelBundle = "split_model";
            const string modelName = "split_weapon";
            var source = new Item(typeId);
            var earlierInstance = new Item(typeId);
            ItemFactory.RegisterProbeConfigurator(typeId, item =>
            {
                item.Configurations++;
                EquipmentFactory.TryBindLoadedMeleeModel(item, modelName, modelName);
            });
            var itemAsset = PrepareBundle("Assets/Items", itemBundle, source);
            var model = new ItemAgent(modelName);
            var modelAsset = PrepareBundle("Assets/Equipment", modelBundle);
            modelAsset.Assets = new[] { model.gameObject };
            var unrelated = PrepareBundle("Assets/Equipment", "unrelated");
            BossRushDynamicItemRegistry.SetSplitPlan(typeId, modelBundle, itemBundle, modelName);
            Check(ItemFactory.LoadBundle(itemBundle) == 1 && modelAsset.Opens == 0 && source.AgentUtilities.GetPrefab("Handheld") == null,
                "deferred item publication can precede the model bundle");
            Check(BossRushDynamicItemRegistry.EnsureRegistered(typeId), "registered split weapon is completed before delivery");
            Check(EquipmentFactory.HasBoundMeleeModel(source, model) && source.Configurations == 2 && earlierInstance.Configurations == 0,
                "targeted repair binds the announced source and does not reconfigure any prior runtime instance");
            Check(BossRushDynamicItemRegistry.EnsureRegistered(typeId) && modelAsset.Opens == 1 && itemAsset.Opens == 1
                && unrelated.Opens == 0 && source.Configurations == 2, "ready source preserves both targeted bundle loads and skips reconfiguration");

            var previousHandheld = source.AgentUtilities.GetPrefab("Handheld");
            Check(!EquipmentFactory.HasBoundMeleeModel(earlierInstance, model)
                && EquipmentFactory.TryBindLoadedMeleeModel(earlierInstance, modelName, modelName), "same TypeID needs its own actual binding");
            Check(earlierInstance.AgentUtilities.GetPrefab("Handheld") == previousHandheld, "same live source shares one Handheld template");
            Check(EquipmentFactory.TryBindLoadedMeleeModel(source, modelName, modelName)
                && source.AgentUtilities.GetPrefab("Handheld") == previousHandheld, "same source with a live binding keeps its Handheld");
            previousHandheld.Destroyed = true;
            Check(EquipmentFactory.TryBindLoadedMeleeModel(source, modelName, modelName)
                && EquipmentFactory.HasBoundMeleeModel(source, model), "destroyed handheld is replaced");

            model.Destroyed = true;
            var freshModel = new ItemAgent(modelName);
            modelAsset.Assets[0] = freshModel.gameObject;
            Check(BossRushDynamicItemRegistry.EnsureRegistered(typeId) && EquipmentFactory.HasBoundMeleeModel(source, freshModel)
                && modelAsset.Opens == 1, "stale model is loaded from the held bundle and rebound");
        }

        private static AssetBundle PrepareBundle(string directory, string name, params Item[] items)
        {
            string path = Path.Combine(ModBehaviour.Root, directory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "probe");
            var bundle = new AssetBundle { Name = name, Assets = Array.ConvertAll(items, item => new GameObject(item)) };
            ResourceBundleLoader.Bundles.Add(path, bundle);
            return bundle;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    internal sealed class ModBehaviour
    {
        internal static string Root;
        internal static ModBehaviour Instance;
        internal bool ConfigurationAllowed;
        internal bool ConfiguratorsInstalled;
        internal static string GetModPath() => Root;
        internal static void DevLog(string message) { }
        internal static void CriticalLog(string key, string message) { }
        internal bool EnsureItemContentConfiguratorsRegisteredForDynamicRegistry()
        {
            ConfiguratorsInstalled = ConfigurationAllowed;
            return ConfiguratorsInstalled;
        }
    }

    internal sealed class Item
    {
        internal readonly int TypeID;
        internal readonly GameObject gameObject;
        internal readonly ItemAgentUtilities AgentUtilities = new ItemAgentUtilities();
        internal string name => gameObject.name;
        internal int Configurations;
        internal int PublicationAttempts;
        internal bool Destroyed;
        internal Item(int typeID) { TypeID = typeID; gameObject = new GameObject(this); }
        internal T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        internal void SetBool(string key, bool value, bool sendEvent) { }
        public static bool operator ==(Item a, Item b) => ReferenceEquals(a, b)
            || (ReferenceEquals(a, null) && b.Destroyed) || (ReferenceEquals(b, null) && a.Destroyed);
        public static bool operator !=(Item a, Item b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }

    internal sealed class GameObject
    {
        private readonly object component;
        private readonly Dictionary<Type, object> added = new Dictionary<Type, object>();
        internal readonly string name;
        internal GameObject(Item item) : this(item, "Test_" + item.TypeID) { }
        internal GameObject(object component, string name) { this.component = component; this.name = name; }
        internal T GetComponent<T>() where T : class => (component as T) ?? (added.TryGetValue(typeof(T), out object result) ? result as T : null);
        internal T AddComponent<T>() where T : class, new() { var value = new T(); added.Add(typeof(T), value); return value; }
    }

    internal sealed class ItemAgentUtilities
    {
        private readonly Dictionary<string, ItemAgent> prefabs = new Dictionary<string, ItemAgent>();
        internal ItemAgent GetPrefab(string key) => prefabs.TryGetValue(key, out ItemAgent result) ? result : null;
        internal void SetPrefab(string key, ItemAgent prefab) { prefabs[key] = prefab; }
    }

    internal sealed class AssetBundle
    {
        internal string Name;
        internal GameObject[] Assets;
        internal int Opens;
        internal void Unload(bool destroy) { }
    }

    internal static class ResourceBundleLoader
    {
        internal static readonly Dictionary<string, AssetBundle> Bundles = new Dictionary<string, AssetBundle>();
        internal static bool InRestoreScope;
        internal static bool LoadedInRestoreScope;
        internal static AssetBundle LoadFromFile(string path)
        {
            LoadedInRestoreScope = InRestoreScope;
            AssetBundle bundle = Bundles[path];
            bundle.Opens++;
            return bundle;
        }
        internal static AssetBundle FindLoaded(string name) => null;
        internal static T[] LoadAllAssets<T>(AssetBundle bundle) where T : class => Array.ConvertAll(bundle.Assets, asset => asset as T);
        internal static bool IsNamed(AssetBundle bundle, string name) => bundle.Name == name;
        internal static IDisposable BeginSynchronousRestore() => new RestoreScope();
        private sealed class RestoreScope : IDisposable
        {
            internal RestoreScope() { InRestoreScope = true; }
            public void Dispose() { InRestoreScope = false; }
        }
    }

    internal static partial class BossRushDynamicItemRegistry
    {
        private sealed class RegistrationPlan
        {
            internal string[] EquipmentBundles;
            internal string[] ItemBundles = new string[0];
            internal Func<bool> SpecialLoader = null;
            internal Func<int, bool> FallbackLoader = null;
            internal string SplitModelBaseName;
            internal bool SplitModelIsMelee;
        }

        private static readonly Dictionary<int, RegistrationPlan> Plans = new Dictionary<int, RegistrationPlan>();
        private static readonly HashSet<int> InProgress = new HashSet<int>();
        private static bool itemConfiguratorsRegistered;
        private static readonly Dictionary<int, Item> officialPrefabs = new Dictionary<int, Item>();
        internal static int Refuse = -1;
        internal static int AcknowledgeWithoutRegistering = -1;

        internal static void SetEquipmentPlan(int typeId, string bundle) => Plans.Add(typeId, new RegistrationPlan { EquipmentBundles = new[] { bundle } });
        internal static void SetSplitPlan(int typeId, string equipment, string item, string model) => Plans.Add(typeId, new RegistrationPlan
        {
            EquipmentBundles = new[] { equipment }, ItemBundles = new[] { item }, SplitModelBaseName = model, SplitModelIsMelee = true
        });
        internal static void RegisterForProbe(Item item) { officialPrefabs[item.TypeID] = item; }
        internal static Item Get(int typeId) => officialPrefabs.TryGetValue(typeId, out Item prefab) ? prefab : null;
        internal static Item GetRegisteredPrefabWithoutEnsuring(int typeId) => Get(typeId);
        internal static bool HasRegisteredPrefabWithoutEnsuring(int typeId) => Get(typeId) != null;
        internal static bool Publish(Item item)
        {
            item.PublicationAttempts++;
            if (item.TypeID == Refuse) return false;
            if (item.TypeID == AcknowledgeWithoutRegistering) return true;
            RegisterForProbe(item);
            return true;
        }
        private static void LoadEquipmentBundles(int typeId, string[] bundles)
        {
            if (!ModBehaviour.Instance.ConfiguratorsInstalled) throw new InvalidOperationException("configuration missing");
            foreach (string bundle in bundles) EquipmentFactory.LoadBundle(bundle);
        }
        private static void LoadItemBundles(int typeId, string[] bundles)
        {
            foreach (string bundle in bundles) ItemFactory.LoadBundle(bundle);
        }
        private static void TryRunLoader(int typeId, string label, Func<bool> loader) { }
        private static void TryRunLoader(int typeId, string label, Func<int, bool> loader) { }
        private static void LogFailureOnce(int typeId, string message) { }
    }

    internal static partial class ItemFactory
    {
        private const string ITEMS_PATH = "Assets/Items";
        private static readonly HashSet<string> loadedBundles = new HashSet<string>();
        private static readonly Dictionary<string, AssetBundle> loadedAssetBundles = new Dictionary<string, AssetBundle>();
        private static readonly Dictionary<string, FactoryBundleRegistration> bundleRegistrations = new Dictionary<string, FactoryBundleRegistration>();
        private static readonly Dictionary<int, Item> loadedItems = new Dictionary<int, Item>();
        private static readonly Dictionary<int, Action<Item>> itemConfigurators = new Dictionary<int, Action<Item>>();
        internal static void RegisterProbeConfigurator(int id, Action<Item> configure) { itemConfigurators[id] = configure; }
    }

    internal enum EquipmentType { Gun, MeleeWeapon, Helmet }
    internal sealed class Buff { public int ID; }
    internal sealed class Projectile { }
    internal sealed class ItemSetting_Gun { }
    internal sealed class ItemSetting_MeleeWeapon { }
    internal class ItemAgent
    {
        internal readonly GameObject gameObject;
        internal bool Destroyed;
        internal ItemAgent(string name) { gameObject = new GameObject(this, name); }
        public static bool operator ==(ItemAgent a, ItemAgent b) => ReferenceEquals(a, b)
            || (ReferenceEquals(a, null) && b.Destroyed) || (ReferenceEquals(b, null) && a.Destroyed);
        public static bool operator !=(ItemAgent a, ItemAgent b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }
    internal sealed class ItemAgent_MeleeWeapon : ItemAgent { public ItemAgent_MeleeWeapon() : base("Handheld") { } }
    internal static class EquipmentHelper { internal static void AddTagToItem(Item item, string tag) { } }

    internal static partial class EquipmentFactory
    {
        private const string EQUIPMENT_PATH = "Assets/Equipment";
        private const string HANDHELD_AGENT_KEY = "Handheld";
        private static readonly HashSet<string> loadedBundles = new HashSet<string>();
        private static readonly HashSet<AssetBundle> ownedBundles = new HashSet<AssetBundle>();
        private static readonly Dictionary<string, FactoryBundleRegistration> bundleRegistrations = new Dictionary<string, FactoryBundleRegistration>();
        private static readonly Dictionary<string, List<ItemAgent>> bundleModels = new Dictionary<string, List<ItemAgent>>();
        private static readonly Dictionary<GameObject, ItemAgent> modelWrappers = new Dictionary<GameObject, ItemAgent>();
        private static readonly HashSet<int> customMeleeWeaponTypeIds = new HashSet<int>();
        private static readonly Dictionary<string, Buff> loadedBuffs = new Dictionary<string, Buff>();
        private static readonly Dictionary<string, Projectile> loadedBullets = new Dictionary<string, Projectile>();
        private static readonly Dictionary<string, ItemAgent> loadedModelsByBaseName = new Dictionary<string, ItemAgent>();
        private static readonly Dictionary<int, Item> loadedGuns = new Dictionary<int, Item>();
        private static readonly Dictionary<int, ItemAgent> loadedModels = new Dictionary<int, ItemAgent>();
        private static readonly Dictionary<ItemAgent, ItemAgent> meleeModelsByHandheld = new Dictionary<ItemAgent, ItemAgent>();
        private static readonly Dictionary<int, ItemAgent_MeleeWeapon> meleeHandheldsByType = new Dictionary<int, ItemAgent_MeleeWeapon>();
        private static readonly List<Action<Item, string>> gunConfigurators = new List<Action<Item, string>>();
        private static readonly List<Action<Item, string>> equipmentConfigurators = new List<Action<Item, string>> { (item, name) => item.Configurations++ };
        private static string ExtractBaseName(string name) => name;
        private static EquipmentType? ParseEquipmentTypeFromName(string name) => EquipmentType.Helmet;
        private static ItemAgent CreateModelWrapper(GameObject go, string name) => null;
        private static void FixModelLayerAndShader(GameObject go) { }
        private static void EnsureModelSubVisuals(GameObject go) { }
        private static ItemAgent TryCreateEmbeddedMeleeModel(Item item, string name) => null;
        private static void InjectItemGraphicForEquipment(Item item, ItemAgent model, bool replace = false) { }
        private static void ProcessEquipmentItem(Item item, string name, EquipmentType type, ItemAgent model) { }
        private static void ApplyMeleeAgentDefaults(ItemAgent_MeleeWeapon target, ItemAgent_MeleeWeapon source) { }
        private static ItemAgent_MeleeWeapon CreateMeleeHandheldPrefab(Item item, ItemAgent model, ItemAgent_MeleeWeapon template, string name)
            => new ItemAgent_MeleeWeapon();
        private static void SetAgentUtilityPrefab(Item item, string key, ItemAgent model) { item.AgentUtilities.SetPrefab(key, model); }
        private static void ProcessGunItem(Item item, string name, ItemAgent model, Dictionary<string, ItemSetting_Gun> guns,
            Dictionary<string, Buff> buffs, Dictionary<string, Projectile> bullets) { }
        private static void ApplyRegisteredConfigurators(List<Action<Item, string>> configurators, Item item, string name)
        {
            foreach (var configure in configurators) configure(item, name);
        }
    }
}

namespace UnityEngine
{
    internal static class Object
    {
        internal static void DestroyImmediate(object value, bool allowDestroyingAssets) { }
        internal static void Destroy(FactoryProbe.GameObject value) { }
    }
}
