using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BossRush.Patches.ItemStatsSystem;
using ItemStatsSystem;

namespace BossRush.Utils { }

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;

        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull && rightNull || !leftNull && !rightNull && ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
        public static Item Instantiate(Item prefab)
        {
            if (prefab == null) throw new ArgumentException("The object to instantiate is null.");
            return prefab.Clone();
        }
        public static void Destroy(GameObject target)
        {
            target.Destroyed = true;
            target.Item.Destroyed = true;
        }
    }

    internal sealed class GameObject : Object
    {
        internal readonly Item Item;
        internal GameObject(Item item) { Item = item; }
    }

    public static class Debug
    {
        public static void LogWarning(object message) { }
    }
}

namespace UnityEngine.SceneManagement
{
    internal readonly struct Scene
    {
        private readonly SceneLoadState state;
        internal Scene(SceneLoadState state) { this.state = state; }
        public bool isLoaded { get { return state != null && state.Loaded; } }
    }

    internal sealed class SceneLoadState
    {
        internal bool Loaded = true;
    }

    internal static class SceneManager
    {
        private static SceneLoadState activeScene;
        internal static Scene GetActiveScene() { return new Scene(activeScene); }
        internal static void ChangeActiveScene(bool unloadPrevious)
        {
            if (unloadPrevious && activeScene != null) activeScene.Loaded = false;
            activeScene = new SceneLoadState();
        }
    }
}

namespace Cysharp.Threading.Tasks
{
    internal static class UniTask
    {
        private static TaskCompletionSource<bool> frame;
        internal static bool WaitingForFrame { get { return frame != null; } }
        internal static UniTask<T> FromResult<T>(T result) { return new UniTask<T>(Task.FromResult(result)); }
        internal static Task NextFrame()
        {
            if (frame == null) frame = new TaskCompletionSource<bool>();
            return frame.Task;
        }
        internal static void AdvanceFrame()
        {
            TaskCompletionSource<bool> wait = frame;
            frame = null;
            wait.SetResult(true);
        }
    }

    [AsyncMethodBuilder(typeof(UniTaskBuilder<>))]
    internal readonly struct UniTask<T>
    {
        private readonly Task<T> task;
        internal UniTask(Task<T> task) { this.task = task; }
        public TaskAwaiter<T> GetAwaiter() { return task.GetAwaiter(); }
    }

    internal struct UniTaskBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> builder;
        public static UniTaskBuilder<T> Create() { return new UniTaskBuilder<T> { builder = AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T>(builder.Task); } }
        public void SetException(Exception error) { builder.SetException(error); }
        public void SetResult(T result) { builder.SetResult(result); }
        public void SetStateMachine(IAsyncStateMachine machine) { builder.SetStateMachine(machine); }
        public void Start<TStateMachine>(ref TStateMachine machine) where TStateMachine : IAsyncStateMachine { builder.Start(ref machine); }
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine machine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { builder.AwaitOnCompleted(ref awaiter, ref machine); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine machine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref awaiter, ref machine); }
    }
}

namespace HarmonyLib
{
    internal sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name, Type[] arguments) { } }
    internal sealed class HarmonyPrefix : Attribute { }
    internal sealed class HarmonyPostfix : Attribute { }
    internal sealed class Harmony
    {
        public string Id { get; }
        public ClassProcessor CreateClassProcessor(Type type) { throw new NotSupportedException(); }
        public static Patches GetPatchInfo(MethodBase method) { throw new NotSupportedException(); }
    }
    internal sealed class ClassProcessor { public void Patch() { throw new NotSupportedException(); } }
    internal sealed class Patches
    {
        public Patch[] Prefixes { get; }
        public Patch[] Postfixes { get; }
    }
    internal sealed class Patch
    {
        public string owner { get; }
        public MethodInfo PatchMethod { get; }
    }
    internal static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] arguments) { throw new NotSupportedException(); }
    }
}

namespace ItemStatsSystem
{
    using Cysharp.Threading.Tasks;

    internal sealed class Item : UnityEngine.Object
    {
        public int TypeID;
        public object Icon { get; }
        public string name { get; set; }
        public Item ParentObject { get; set; }
        public UnityEngine.GameObject gameObject { get; }
        public bool AlreadyInitialized, ThrowOnInitialize, IsFallback, HasHandheldAgent;
        public ItemAgent MeleeModel, EquipmentModel;
        public int InitializeCalls;
        public readonly Agents AgentUtilities = new Agents();
        public readonly HashSet<string> Tags = new HashSet<string>();
        public readonly Dictionary<string, Slot> Slots = new Dictionary<string, Slot>();
        public readonly Dictionary<string, int> Variables = new Dictionary<string, int>();

        internal Item() { gameObject = new UnityEngine.GameObject(this); }

        public void Initialize()
        {
            InitializeCalls++;
            if (ThrowOnInitialize) throw new InvalidOperationException("host initialization failed");
            if (AlreadyInitialized) return;
            AlreadyInitialized = true;
            AgentUtilities.Initialize(this);
        }

        public Item Clone()
        {
            var item = new Item
            {
                TypeID = TypeID, HasHandheldAgent = HasHandheldAgent, name = name,
                MeleeModel = MeleeModel, EquipmentModel = EquipmentModel
            };
            item.Tags.UnionWith(Tags);
            foreach (var slot in Slots) item.Slots.Add(slot.Key, new Slot(slot.Value.RequiredTag));
            return item;
        }
    }

    internal sealed class ItemAgent : UnityEngine.Object { }

    internal sealed class Slot
    {
        public readonly string RequiredTag;
        public Item Content;
        public Slot(string requiredTag) { RequiredTag = requiredTag; }
        public bool Plug(Item item)
        {
            if (item == null || (RequiredTag != null && !item.Tags.Contains(RequiredTag))) return false;
            Content = item;
            return true;
        }
    }

    internal sealed class Agents
    {
        public Item Master;
        public int BindCalls;
        public void Initialize(Item item) { Master = item; BindCalls++; }
        public Item CreateAgent() { return Master != null && Master.HasHandheldAgent ? Master : null; }
    }

    internal sealed class Inventory
    {
        public readonly Dictionary<int, Item> Items = new Dictionary<int, Item>();
    }

    internal static class ItemAssetsCollection
    {
        internal sealed class DynamicEntry { public Item prefab; }
        public static readonly Dictionary<int, Item> Dynamic = new Dictionary<int, Item>();
        public static readonly Dictionary<int, Item> Official = new Dictionary<int, Item>();
        public static bool CollectionAvailable;
        public static int DeclinedPublishType;
        public static int Fallbacks;
        public static Item LastFallback;

        public static bool AddDynamicEntry(Item item)
        {
            if (!CollectionAvailable || DeclinedPublishType == item.TypeID) return false;
            Dynamic[item.TypeID] = item;
            return true;
        }

        public static bool TryGetDynamicEntry(int typeID, out DynamicEntry entry)
        {
            Item prefab;
            entry = Dynamic.TryGetValue(typeID, out prefab) ? new DynamicEntry { prefab = prefab } : null;
            return entry != null;
        }

        public static Item GetPrefab(int typeID)
        {
            Hooks.Invoke(typeof(ItemAssetsCollectionGetPrefabDynamicRegistrationPatch), "Prefix", typeID);
            Item prefab;
            if (Dynamic.TryGetValue(typeID, out prefab)) return prefab;
            return Official.TryGetValue(typeID, out prefab) ? prefab : null;
        }

        public static UniTask<Item> BeginInstantiateAsync(int typeID)
        {
            var outer = typeof(ItemAssetsCollectionInstantiateAsyncDynamicRegistrationPatch);
            object[] outerPrefix = { typeID, null };
            Hooks.Invoke(outer, "Prefix", outerPrefix);
            // The official static async path returns null without entering Local when its collection is unavailable.
            UniTask<Item> original = CollectionAvailable ? InstantiateLocal(typeID) : UniTask.FromResult<Item>(null);
            object[] outerArgs = { typeID, outerPrefix[1], original };
            Hooks.Invoke(outer, "Postfix", outerArgs);
            return (UniTask<Item>)outerArgs[2];
        }

        private static UniTask<Item> InstantiateLocal(int typeID)
        {
            var local = typeof(ItemAssetsCollectionInstantiateAsyncLocalDynamicRegistrationPatch);
            object[] localPrefix = { typeID, default(UniTask<Item>) };
            var localOperation = (bool)Hooks.Invoke(local, "Prefix", localPrefix)
                ? UniTask.FromResult(InstantiateOriginal(typeID, false))
                : (UniTask<Item>)localPrefix[1];
            object[] localArgs = { typeID, localOperation };
            Hooks.Invoke(local, "Postfix", localArgs);
            return (UniTask<Item>)localArgs[1];
        }

        public static Item InstantiateSync(int typeID)
        {
            var patch = typeof(ItemAssetsCollectionInstantiateSyncDynamicRegistrationPatch);
            object[] args = { typeID, null };
            Item item = (bool)Hooks.Invoke(patch, "Prefix", args) ? InstantiateOriginal(typeID, true) : (Item)args[1];
            Hooks.Invoke(patch, "Postfix", item);
            return item;
        }

        private static Item InstantiateOriginal(int typeID, bool sync)
        {
            Item prefab;
            if (Dynamic.TryGetValue(typeID, out prefab)) return UnityEngine.Object.Instantiate(prefab);
            if (Official.TryGetValue(typeID, out prefab))
            {
                if (prefab != null) return UnityEngine.Object.Instantiate(prefab);
            }
            else if (sync) throw new NullReferenceException("The original sync path reads prefab from a missing item entry.");
            return InstantiateFallbackItem(typeID);
        }

        public static Item InstantiateFallbackItem(int typeID)
        {
            object[] args = { typeID, null };
            if (!(bool)Hooks.Invoke(typeof(ItemAssetsCollectionInstantiateFallbackDynamicRegistrationPatch), "Prefix", args))
                return (Item)args[1];
            Fallbacks++;
            return LastFallback = new Item { TypeID = typeID, IsFallback = true, name = "FallbackItem_" + typeID };
        }
    }
}

namespace ItemStatsSystem.Data
{
    internal sealed class ItemTreeData
    {
        internal sealed class DataEntry
        {
            public int InstanceId, typeID;
            public readonly Dictionary<string, int> Variables = new Dictionary<string, int>();
            public readonly Dictionary<string, int> SlotContents = new Dictionary<string, int>();
        }

        public int RootId;
        public readonly List<DataEntry> entries = new List<DataEntry>();

        public Item Instantiate()
        {
            return InstantiateAsync().GetAwaiter().GetResult();
        }

        public async Task<Item> InstantiateAsync()
        {
            Hooks.Invoke(typeof(ItemTreeDataInstantiateAsyncDynamicRegistrationPatch), "Prefix", this);
            var created = new Dictionary<int, Item>();
            foreach (DataEntry entry in entries)
            {
                Item item = await ItemAssetsCollection.BeginInstantiateAsync(entry.typeID);
                if (item == null) continue;
                foreach (var data in entry.Variables) item.Variables[data.Key] = data.Value;
                created.Add(entry.InstanceId, item);
            }
            foreach (DataEntry entry in entries)
            {
                Item parent;
                if (!created.TryGetValue(entry.InstanceId, out parent)) continue;
                foreach (var link in entry.SlotContents)
                {
                    Slot slot;
                    if (!parent.Slots.TryGetValue(link.Key, out slot))
                        parent.Slots.Add(link.Key, slot = new Slot(null));
                    Item child;
                    if (created.TryGetValue(link.Value, out child))
                        slot.Plug(child); // The official loader discards the Plug return value.
                }
            }
            Item root;
            return created.TryGetValue(RootId, out root) ? root : null;
        }

        public static ItemTreeData FromItem(Item root)
        {
            var data = new ItemTreeData();
            data.RootId = data.AddItem(root);
            return data;
        }

        private int AddItem(Item item)
        {
            var entry = new DataEntry { InstanceId = entries.Count + 1, typeID = item.TypeID };
            entries.Add(entry);
            foreach (var value in item.Variables) entry.Variables.Add(value.Key, value.Value);
            foreach (var slot in item.Slots)
                if (slot.Value.Content != null) entry.SlotContents.Add(slot.Key, AddItem(slot.Value.Content));
            return entry.InstanceId;
        }
    }

    internal sealed class InventoryData
    {
        internal sealed class Entry
        {
            public int Position;
            public ItemTreeData itemTreeData;
        }

        public readonly List<Entry> entries = new List<Entry>();

        public static InventoryData FromInventory(Inventory inventory)
        {
            var data = new InventoryData();
            foreach (var slot in inventory.Items)
                data.entries.Add(new Entry { Position = slot.Key, itemTreeData = ItemTreeData.FromItem(slot.Value) });
            return data;
        }

        public void LoadIntoInventory(Inventory inventory)
        {
            LoadIntoInventoryAsync(inventory).GetAwaiter().GetResult();
        }

        public async Task LoadIntoInventoryAsync(Inventory inventory)
        {
            Hooks.Invoke(typeof(InventoryDataLoadIntoInventoryDynamicRegistrationPatch), "Prefix", this);
            foreach (Entry entry in entries)
            {
                Item item = await entry.itemTreeData.InstantiateAsync();
                if (item != null) inventory.Items.Add(entry.Position, item);
            }
        }
    }
}

internal static class Hooks
{
    internal static object Invoke(Type patch, string name, params object[] args)
    {
        return patch.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    }
}

namespace BossRush
{
    internal sealed class ModBehaviour
    {
        internal static ModBehaviour Instance;
        internal static readonly List<string> CriticalMessages = new List<string>();
        internal static int Errors { get { return CriticalMessages.Count; } }
        internal static int ConfiguratorRegistrations;
        internal static bool ConfiguratorsReady;
        internal bool EnsureItemContentConfiguratorsRegisteredForDynamicRegistry()
        {
            ConfiguratorRegistrations++;
            ConfiguratorsReady = true;
            return true;
        }
        internal bool EnsureBossRushTicketItemRegisteredForDynamicRegistry() { return false; }
        internal bool EnsureBirthdayCakeItemRegisteredForDynamicRegistry() { return false; }
        internal bool EnsureAdventureJournalItemRegisteredForDynamicRegistry() { return false; }
        internal static void DevLog(string message) { }
        internal static void CriticalLog(string key, string message) { CriticalMessages.Add(message); }
        internal static string GetModPath() { return "fixture"; }
    }

    // 生产 BossRushEquipmentRescue / BossRushStartupGuard 依赖关卡、存档与 UniTask，夹具只记补丁层的调用。
    internal static class BossRushEquipmentRescue
    {
        internal static int UnresolvedLoadNotes;
        internal static void NoteUnresolvedLoad() { UnresolvedLoadNotes++; }
    }

    internal static class BossRushStartupGuard
    {
        internal static int Verified = -1, Total = -1;
        internal static void NoteCriticalPatches(int verified, int total) { Verified = verified; Total = total; }
    }

    internal static class ResourceBundleLoader
    {
        internal static IDisposable BeginSynchronousRestore() { return new Restore(); }
        private sealed class Restore : IDisposable { public void Dispose() { } }
    }

    internal static class ModItemIcons
    {
        internal static int BrokenIcon;
        internal static void ApplyBeforePublish(Item item)
        {
            if (item.TypeID == BrokenIcon) throw new InvalidOperationException("bad icon resource");
        }
    }

    internal static class EquipmentFactory
    {
        internal static FactoryBundleRegistration Registration;
        internal static Item Weapon, Armor;
        internal static int Loads, ConfigurationRuns;
        internal static string SplitBundle, SplitModelName;
        internal static bool SplitBundleAvailable;
        internal static ItemAgent SplitModel;
        internal static int SplitBundleLoads;

        internal static void Reset()
        {
            Registration = new FactoryBundleRegistration();
            Weapon = new Item { TypeID = DragonKingBossGunConfig.WeaponTypeId };
            Armor = new Item { TypeID = DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID };
            Loads = 0;
            ConfigurationRuns = 0;
            SplitBundle = null;
            SplitModelName = null;
            SplitBundleAvailable = false;
            SplitModel = null;
            SplitBundleLoads = 0;
        }

        internal static void LoadBundle(string name)
        {
            if (name == SplitBundle)
            {
                SplitBundleLoads++;
                if (SplitBundleAvailable && SplitModel == null) SplitModel = new ItemAgent();
                return;
            }
            if (name != "dragonking_equipment") throw new InvalidOperationException(name);
            Loads++;
            foreach (Item prefab in new[] { Weapon, Armor })
            {
                if (Registration.IsPublished(prefab)) continue;
                if (!Registration.IsConfigured(prefab))
                {
                    ConfigurationRuns++;
                    // DragonKingBossGunConfig supplies these fields. The asset adapter leaves them unset.
                    if (prefab == Weapon && ModBehaviour.ConfiguratorsReady)
                    {
                        prefab.Tags.Add("Gun");
                        prefab.Slots.Add("Gem", new Slot("Gem"));
                        prefab.HasHandheldAgent = true;
                    }
                }
                Registration.TryPublish(prefab, name);
            }
        }

        internal static bool TryGetLoadedModel(string baseName, out ItemAgent model)
        {
            model = baseName == SplitModelName ? SplitModel : null;
            return model != null;
        }

        internal static bool HasBoundMeleeModel(Item item, ItemAgent model) { return item.MeleeModel == model; }
        internal static bool HasBoundEquipmentModel(Item item, ItemAgent model) { return item.EquipmentModel == model; }
    }

    internal static class ItemFactory
    {
        internal static int Loads, SplitTypeId, SplitConfigurations;
        internal static bool SplitModelIsMelee;
        internal static Item ConfiguredSplitPrefab;

        internal static void Reset()
        {
            Loads = 0;
            SplitTypeId = 0;
            SplitConfigurations = 0;
            SplitModelIsMelee = false;
            ConfiguredSplitPrefab = null;
        }

        internal static void LoadBundle(string name)
        {
            Loads++;
            if (name != ColdQuenchFluidConfig.BUNDLE_NAME) throw new InvalidOperationException(name);
            if (ModBehaviour.ConfiguratorsReady)
                BossRushDynamicItemRegistry.Publish(new Item { TypeID = ColdQuenchFluidConfig.TYPE_ID });
        }

        internal static void ConfigureItemPrefab(Item item)
        {
            SplitConfigurations++;
            ConfiguredSplitPrefab = item;
            ItemAgent model;
            if (!ModBehaviour.ConfiguratorsReady || item.TypeID != SplitTypeId
                || !EquipmentFactory.TryGetLoadedModel(EquipmentFactory.SplitModelName, out model)) return;
            if (SplitModelIsMelee) item.MeleeModel = model;
            else item.EquipmentModel = model;
        }
    }

    internal static partial class AwenLootSweepTokenConfig { internal static bool EnsureRuntimeRegistration() { return false; } }
    internal static partial class ZombieTideInvitationConfig { internal static bool EnsureRuntimeFallbackRegistrationShell() { return false; } }
    internal static partial class ZombieTideBeaconConfig { internal static bool EnsureRuntimeFallbackRegistrationShell() { return false; } }
    internal static partial class PortableSafeZoneDeviceConfig { internal static bool EnsureRuntimeFallbackRegistrationShell() { return false; } }
    internal static partial class SkyIslandNavInstrumentConfig { internal static bool EnsureRuntimeFallbackRegistrationShell() { return false; } }
    internal static class RelicEggConfig { internal static bool EnsureRuntimeFallbackRegistrationShell() { return false; } }
    internal static class AffixForgeStoneConfig { internal static bool EnsureRuntimeFallbackRegistrationShell() { return false; } }
    internal static class CodexBookConfig { internal static bool EnsureRuntimeFallbackRegistrationShell() { return false; } }
    internal static class BackMountainItems { internal static bool EnsureRuntimeRegistration(int id) { return false; } }
    internal static class SkyIslandItems { internal static bool EnsureRuntimeRegistration(int id) { return false; } }
    internal static class TidegateItems { internal static bool EnsureRuntimeRegistration(int id) { return false; } }
    internal static class LastShiftItems { internal static bool EnsureRuntimeRegistration(int id) { return false; } }
    internal static class SkyIslandBossGearConfig { internal static bool EnsureRegistered(int id) { return false; } }
    internal static class CollectibleItems { internal static bool EnsureRuntimeRegistration(int id) { return false; } }
    internal static class CollectionBookItem { internal static bool EnsureRuntimeRegistration() { return false; } }
    internal static class TemperingSolventConfig { internal static bool EnsureRuntimeRegistration() { return false; } }
    internal static class AstralStaffWeaponConfig { internal static bool EnsureRuntimeRegistration() { return false; } }
    internal static class NewWeaponPlaceholderRegistry { internal static bool EnsureRegistered(int id) { return false; } }
    internal static class SetBonusPlaceholderRegistry { internal static bool EnsureRegistered(int id) { return false; } }
}
