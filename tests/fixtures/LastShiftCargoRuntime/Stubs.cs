using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using ItemStatsSystem;
using UnityEngine;

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;

        public static bool operator ==(Object a, Object b)
        {
            bool aNull = ReferenceEquals(a, null) || a.Destroyed;
            bool bNull = ReferenceEquals(b, null) || b.Destroyed;
            return aNull || bNull ? aNull && bNull : ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }

        internal static T Instantiate<T>(T prefab, Transform parent) where T : Component
        {
            var copy = new GameObject(prefab.gameObject.name);
            copy.transform.SetParent(parent, false);
            return (T)(Component)copy.AddComponent<InteractableLootbox>();
        }

        internal static void Destroy(Object target)
        {
            if (target == null) return;
            if (target is GameObject host) host.DestroyWithChildren();
            else target.Destroyed = true;
        }

        internal static void DestroyImmediate(Object target) { Destroy(target); }
    }

    internal class Component : Object
    {
        internal GameObject gameObject;
        internal Transform transform { get { return gameObject.transform; } }
        internal string name { get { return gameObject.name; } set { gameObject.name = value; } }
        internal T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        internal T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            return gameObject.GetComponentsInChildren<T>(includeInactive).ToArray();
        }
    }

    internal sealed class GameObject : Object
    {
        internal static Action<GameObject> Activating;
        internal string name;
        internal readonly Transform transform;
        internal bool activeSelf = true;
        internal bool activeInHierarchy { get { return !Destroyed && activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); } }
        private readonly List<Component> components = new List<Component>();

        internal GameObject(string name)
        {
            this.name = name;
            transform = AddComponent<Transform>();
        }

        internal T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }

        internal T GetComponent<T>() where T : Component { return components.OfType<T>().FirstOrDefault(c => c != null); }

        internal IEnumerable<T> GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            if (!includeInactive && !activeInHierarchy) yield break;
            foreach (T component in components.OfType<T>().Where(c => c != null)) yield return component;
            foreach (Transform child in transform.children)
                foreach (T component in child.gameObject.GetComponentsInChildren<T>(includeInactive)) yield return component;
        }

        internal void SetActive(bool value)
        {
            bool wasActive = activeInHierarchy;
            activeSelf = value;
            NotifyActivation(wasActive);
        }

        internal void NotifyActivation(bool wasActive)
        {
            if (!wasActive && activeInHierarchy) Activating?.Invoke(this);
        }

        internal void DestroyWithChildren()
        {
            foreach (Transform child in transform.children.ToArray()) Destroy(child.gameObject);
            if (transform.parent != null) transform.parent.children.Remove(transform);
            foreach (Component component in components) component.Destroyed = true;
            Destroyed = true;
        }
    }

    internal sealed class Transform : Component
    {
        internal Transform parent;
        internal readonly List<Transform> children = new List<Transform>();
        internal Vector3 position;
        internal Vector3 eulerAngles { get { return default(Vector3); } }
        internal Quaternion rotation;

        internal void SetParent(Transform next, bool worldPositionStays)
        {
            bool wasActive = gameObject.activeInHierarchy;
            if (parent != null) parent.children.Remove(this);
            parent = next;
            if (parent != null) parent.children.Add(this);
            gameObject.NotifyActivation(wasActive);
        }

        internal bool IsChildOf(Transform root) { return this == root || (parent != null && parent.IsChildOf(root)); }
    }

    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal float sqrMagnitude { get { return x * x + y * y + z * z; } }
        internal static Vector3 up { get { return new Vector3(0, 1, 0); } }
        internal static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float scale) { return new Vector3(a.x * scale, a.y * scale, a.z * scale); }
    }

    internal struct Quaternion { internal static Quaternion Euler(float x, float y, float z) { return default(Quaternion); } }
    internal sealed class Renderer : Component { internal bool enabled = true; }
    internal class Collider : Component { }
    internal sealed class SphereCollider : Collider { internal float radius; internal bool isTrigger; }
    internal struct RaycastHit { internal Transform transform; internal Vector3 point; }
    internal enum QueryTriggerInteraction { Ignore }
    internal static class Physics
    {
        internal static bool Raycast(Vector3 from, Vector3 direction, out RaycastHit hit, float distance, int mask, QueryTriggerInteraction query)
        { hit = new RaycastHit { transform = null, point = from }; return false; }
        internal static bool CheckCapsule(Vector3 bottom, Vector3 top, float radius, int mask, QueryTriggerInteraction query) { return false; }
    }
    internal static class Mathf
    {
        internal const float Deg2Rad = (float)(Math.PI / 180);
        internal static float Sin(float x) { return (float)Math.Sin(x); }
        internal static float Cos(float x) { return (float)Math.Cos(x); }
        internal static int Clamp(int value, int min, int max) { return Math.Min(Math.Max(value, min), max); }
        internal static int Max(int a, int b) { return Math.Max(a, b); }
    }
    internal static class Debug
    {
        internal static readonly List<string> Messages = new List<string>();
        internal static void Log(string text) { Messages.Add(text); }
        internal static void LogWarning(string text) { Messages.Add(text); }
    }
    internal static class Time { internal static float time; }
}

namespace ItemStatsSystem
{
    internal sealed class Item : Component
    {
        internal int TypeID, Quality, StackCount = 1;
        internal string DisplayName { get { return "Item " + TypeID; } }
        internal bool Stackable { get { return TypeID / 10000 == (int)SkyIslandLootCategory.Ammo + 1; } }
        internal int MaxStackCount { get { return 30; } }
        internal bool Inspected = true;
        internal bool IsBeingDestroyed { get { return Destroyed; } }
        internal Inventory InInventory;
        internal object PluggedIntoSlot { get { return null; } }
        internal readonly HashSet<string> Tags = new HashSet<string>();
        internal List<Item> GetAllChildren(bool includeInventory, bool includeSlots)
        {
            Inventory inventory = GetComponent<Inventory>();
            return includeInventory && inventory != null ? inventory.Content.Where(item => item != null).ToList() : new List<Item>();
        }
        internal void DestroyTree() { UnityEngine.Object.Destroy(gameObject); }
    }

    internal static class ItemAssetsCollection
    {
        internal static readonly List<Item> Instances = new List<Item>();
        internal static readonly List<int> Attempts = new List<int>();
        internal static Func<Item, Item> Instantiating;
        private static readonly Dictionary<int, Item> prefabs = new Dictionary<int, Item>();

        internal static Item GetPrefab(int id)
        {
            if (!prefabs.TryGetValue(id, out Item prefab))
            {
                prefab = NewItem(id, id / 100 % 100);
                prefabs.Add(id, prefab);
            }
            return prefab;
        }

        internal static Item InstantiateSync(int id)
        {
            Attempts.Add(id);
            Item item = NewItem(id, GetPrefab(id).Quality);
            Instances.Add(item);
            return Instantiating == null ? item : Instantiating(item);
        }

        private static Item NewItem(int id, int quality)
        {
            var item = new GameObject("Item " + id).AddComponent<Item>();
            item.TypeID = id;
            item.Quality = quality;
            return item;
        }
    }

    internal sealed class Inventory : Component
    {
        internal static Func<Inventory, Item, bool> Adding;
        internal static Action<Inventory, Item> Added;
        internal bool NeedInspection;
        internal readonly List<Item> Content = new List<Item>();
        private int capacity;

        internal void SetCapacity(int value) { capacity = value; }
        internal int GetItemCount() { return Content.Count(item => item != null); }
        internal Item GetItemAt(int index) { return index < Content.Count ? Content[index] : null; }

        internal bool AddItem(Item item)
        {
            if ((Adding != null && !Adding(this, item)) || Content.Count >= capacity) return false;
            Content.Add(item);
            item.transform.SetParent(transform, true);
            item.InInventory = this;
            Added?.Invoke(this, item);
            return true;
        }

        internal void RemoveAt(int index)
        {
            Item item = Content[index];
            Content.RemoveAt(index);
            item.InInventory = null;
            item.transform.SetParent(null, true);
        }

        internal void RestoreAt(Item item, int index)
        {
            Content.Insert(index, item);
            item.InInventory = this;
            item.transform.SetParent(transform, true);
        }

        internal void DestroyAllContent()
        {
            foreach (Item item in Content) item.DestroyTree();
            Content.Clear();
        }
    }
}

namespace BossRush
{
    internal sealed class InteractableLootbox : Component
    {
        internal static event Action<InteractableLootbox> OnStartLoot;
        internal static int SubscriberCount { get { return OnStartLoot == null ? 0 : OnStartLoot.GetInvocationList().Length; } }
        internal Inventory Inventory { get; set; }
        internal bool needInspect, interactableGroup;
        internal Vector3 interactMarkerOffset;
#pragma warning disable 0169 // 官方字段由真实建箱方法反射写入。
        private string displayNameKey;
#pragma warning restore 0169
        internal List<InteractableLootbox> GetInteractableList() { return new List<InteractableLootbox> { this }; }
        internal void Open() { if (gameObject.activeInHierarchy) OnStartLoot?.Invoke(this); }
    }

    internal sealed class LootBoxLoader : Component { }
    internal sealed class Carriable : Component { }
    internal sealed class InteractableCarriable : Component { internal Collider interactCollider { get { return GetComponent<Collider>(); } } }
    internal static class InteractableLootboxInventoryHelper
    {
        internal static bool EnsureLocalInventory(InteractableLootbox loot, int fallbackCapacity)
        {
            loot.Inventory = loot.gameObject.AddComponent<Inventory>();
            return true;
        }
    }

    internal static class GameplayDataSettings
    {
        internal sealed class PrefabsData { internal InteractableLootbox LootBoxPrefab; }
        internal static readonly PrefabsData Prefabs = new PrefabsData();
        internal sealed class LayersData { internal int wallLayerMask { get { return 0; } } }
        internal static readonly LayersData Layers = new LayersData();
    }

    internal sealed class LastShiftWorld
    {
        internal readonly GameObject Root = new GameObject("Raid");
        internal readonly List<Transform> LootMarkers = new List<Transform>();
        internal readonly Dictionary<string, Transform> Interacts = new Dictionary<string, Transform>();
        internal static Vector3 CrateMarkerOffset(string kind) { return Vector3.up; }
    }

    internal sealed class LastShiftDeviceInteractable : Component
    {
        private Func<bool> available;
        private Action activate;
        internal void Bind(string id, float seconds, Func<string> label, Func<bool> available, Action action)
        {
            this.available = available;
            activate = action;
        }
        internal bool Available { get { return gameObject.activeInHierarchy && available(); } }
        internal void Press() { if (Available) activate(); }
    }

    internal static class NPCInteractionGroupHelper
    {
        internal static T GetOrCreateStandaloneInteractable<T>(Transform root, string name, Vector3 offset, Action<T> setup) where T : Component, new()
        {
            var host = new GameObject(name);
            host.transform.SetParent(root, false);
            host.AddComponent<SphereCollider>();
            T component = host.AddComponent<T>();
            setup(component);
            return component;
        }
    }

    internal sealed class CharacterMainControl : Component
    {
        internal static CharacterMainControl Main;
        internal Item CharacterItem;
    }
    internal static class LastShiftRaidScene { internal const string LogTag = "[LastShift]"; }
    internal static class LastShiftText
    {
        internal static string PlatformName(char side) { return side.ToString(); }
        internal static string LineName(char side) { return side.ToString(); }
        internal static string SignedLine(int index, string topItem) { return "signed " + index + " " + topItem; }
        internal static string CargoReclaimedLine { get { return "reclaimed"; } }
    }
    internal static class BossRushItemIds { internal const int LastShiftStamp = 500117; }
    internal static class TidegateInventory
    {
        internal sealed class Reservation : IDisposable
        {
            private readonly Item item;
            private readonly Inventory inventory;
            private readonly int index;
            private bool committed;

            private Reservation(Item item, Inventory inventory, int index)
            { this.item = item; this.inventory = inventory; this.index = index; }

            internal static bool TryCreate(Item item, bool wholeStack, out Reservation reservation)
            {
                if (wholeStack || item.StackCount != 1) throw new InvalidOperationException("expected a one-stamp reservation");
                reservation = null;
                Inventory inventory = item.InInventory;
                int index = inventory == null ? -1 : inventory.Content.IndexOf(item);
                if (index < 0) return false;
                inventory.RemoveAt(index);
                reservation = new Reservation(item, inventory, index);
                return true;
            }

            internal void Commit() { committed = true; item.DestroyTree(); }
            public void Dispose() { if (!committed) inventory.RestoreAt(item, index); }
        }
    }
    internal static class TidegateLoot
    {
        internal sealed class PrizeCatalog
        {
            private readonly Dictionary<SkyIslandLootCategory, int[]> stock = new Dictionary<SkyIslandLootCategory, int[]>();

            internal int Pick(SkyIslandLootCategory category, int preferred, int floor, int ceiling, Random random, SkyIslandLootCategory fallback)
            {
                int[] band = Get(category).Where(id => ItemAssetsCollection.GetPrefab(id).Quality == preferred).ToArray();
                if (band.Length == 0) band = Get(fallback).Where(id => ItemAssetsCollection.GetPrefab(id).Quality == preferred).ToArray();
                return band.Length == 0 ? 0 : band[random.Next(band.Length)];
            }

            private int[] Get(SkyIslandLootCategory category)
            {
                if (!stock.TryGetValue(category, out int[] ids))
                {
                    ids = SkyIslandLootPools.GetCategoryBand(category, 4, 8);
                    stock.Add(category, ids);
                }
                return ids;
            }
        }
    }

    internal static class SkyIslandLootPools
    {
        internal static bool EmptyPrizes, EmptyGoods;
        internal static int MissingPrizeGrade;
        internal static bool IsCategoryCached(SkyIslandLootCategory category, int min, int max) { return true; }
        internal static int[] GetCategoryBand(SkyIslandLootCategory category, int min, int max)
        {
            bool prize = min >= 4;
            if (prize ? EmptyPrizes : EmptyGoods) return Array.Empty<int>();
            return Enumerable.Range(min, max - min + 1)
                .Where(grade => !prize || grade != MissingPrizeGrade)
                .SelectMany(grade => Enumerable.Range(1, 8).Select(index => ((int)category + 1) * 10000 + grade * 100 + index))
                .ToArray();
        }
        internal static int[] Get(SkyIslandLootTier tier) { return Array.Empty<int>(); }
        internal static int Pick(SkyIslandLootTier tier, bool guaranteed, Random random) { return 0; }
        internal static int PickCategory(SkyIslandLootCategory category, SkyIslandLootTier tier, Random random) { return 0; }
    }
    internal struct SkyIslandYield { internal int TypeId { get { return 0; } } internal int Count { get { return 0; } } }
    internal static class SkyIslandItemRules
    {
        internal static int IslandExtraFor(SkyIslandLootTier tier, double roll) { return 0; }
        internal static int IslandStapleFor(SkyIslandLootTier tier, double choice, double roll, out int count) { count = 0; return 0; }
    }
    internal static class SkyIslandBossRules { internal static double HunterIslandExtraRoll(double roll, int pieces) { return roll; } }
    internal static class SkyIslandBossGearWorn { internal static int RootweavePieces { get { return 0; } } }
}

namespace BossRush
{
    internal static class L10n { internal static string T(string zh, string en) { return en; } }
    internal static class LocalizationHelper { internal static void InjectLocalization(string key, string value) { } }
}

namespace BossRush.Utils { }
namespace Duckov.Utilities { }
