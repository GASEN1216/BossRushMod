using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object obj) { return this == obj as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null)) return;
            obj.Destroyed = true;
            var go = obj as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object child in go.Components) Destroy(child);
        }
    }
    public class GameObject : Object
    {
        public readonly List<Object> Components = new List<Object>();
        public T GetComponent<T>() where T : Object { return Components.Find(x => x is T) as T; }
        public T AddComponent<T>() where T : Object, new() { var value = new T(); Components.Add(value); return value; }
    }
    public static class Debug { public static void LogWarning(string value) { } }
}
public sealed class InteractableLootbox : UnityEngine.Object
{
    public Inventory Inventory;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
}
public sealed class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main;
    public Item CharacterItem;
}
public static class PlayerStorage
{
    public static readonly List<ItemStatsSystem.Data.ItemTreeData> IncomingItemBuffer = new List<ItemStatsSystem.Data.ItemTreeData>();
}
namespace ItemStatsSystem.Data { public sealed class ItemTreeData { public int rootInstanceID; public int RootTypeID; } }
namespace ItemStatsSystem
{
    public sealed class Item : UnityEngine.Object
    {
        private static int nextId = 10;
        public int TypeID, Id = nextId++, MaxStackCount = 99;
        public bool IsBeingDestroyed { get { return Destroyed; } }
        public Inventory InInventory, Inventory;
        public object PluggedIntoSlot;
        public AgentUtilities AgentUtilities;
        public bool UseDurability;
        public float Durability, MaxDurability = 100;
        public object ParentObject { get { return (object)InInventory ?? PluggedIntoSlot ?? (AgentUtilities == null ? null : AgentUtilities.ActiveAgent); } }
        public bool Stackable { get { return MaxStackCount > 1; } }
        private int count = 1;
        public Action<Item> OnCount;
        public int StackCount
        {
            get { return Stackable ? count : 1; }
            set
            {
                count = Math.Min(value, MaxStackCount);
                // 官方先写 Count，随后广播，最后才处理零件数销毁。
                if (OnCount != null) OnCount(this);
                if (count < 1) DestroyTree();
            }
        }
        public int GetInstanceID() { return Id; }
        public void SetInt(string key, int value, bool create) { count = value; }
        public void DestroyTree() { if (Inventory != null) foreach (Item item in Inventory.Content) if (item != null) item.DestroyTree(); Destroy(this); }
        public void Drop(CharacterMainControl player, bool random) { AgentUtilities = new AgentUtilities { ActiveAgent = new object() }; }
        public void Combine(Item other)
        {
            int take = Math.Min(MaxStackCount - StackCount, other.StackCount);
            StackCount += take;
            other.StackCount -= take;
        }
    }
    public sealed class AgentUtilities { public object ActiveAgent; }
    public sealed class Inventory : UnityEngine.Object
    {
        public readonly List<Item> Content = new List<Item>();
        public int Capacity = 8, Growths;
        public bool Reject, FailGrowth;
        public Action<Item> AfterAdd, AfterRemove;
        public int GetFirstEmptyPosition(int start) { for (int i = start; i < Capacity; i++) if (GetItemAt(i) == null) return i; return -1; }
        public Item GetItemAt(int at) { return at >= 0 && at < Content.Count ? Content[at] : null; }
        public void SetCapacity(int value) { if (FailGrowth) throw new Exception("growth"); Capacity = value; Growths++; }
        public bool AddItem(Item item) { int at = GetFirstEmptyPosition(0); return at >= 0 && AddAt(item, at); }
        public bool AddAt(Item item, int at)
        {
            if (Reject || item == null || item.ParentObject != null || at >= Capacity) return false;
            while (Content.Count <= at) Content.Add(null);
            Content[at] = item; item.InInventory = this;
            if (AfterAdd != null) AfterAdd(item);
            return true;
        }
        public bool RemoveItem(Item item)
        {
            int at = Content.IndexOf(item);
            if (at < 0) return false;
            Content[at] = null; item.InInventory = null;
            while (Content.Count > 0 && Content[Content.Count - 1] == null) Content.RemoveAt(Content.Count - 1);
            if (AfterRemove != null) AfterRemove(item);
            return true;
        }
    }
    public static class ItemAssetsCollection
    {
        public static bool MissingPrefab;
        public static Item Last;
        public static Item GetPrefab(int typeId) { return MissingPrefab ? null : new Item { TypeID = typeId }; }
        public static Item InstantiateSync(int typeId) { return Last = new Item { TypeID = typeId }; }
    }
}
namespace BossRush
{
    public static class ModBehaviour { public static void DevLog(string value) { } }
    internal struct SkyIslandIngredient
    {
        internal int TypeId, Count;
        internal SkyIslandIngredient(int id, int count) { TypeId = id; Count = count; }
    }
    public static class ItemFactory
    {
        public static int GetItemCountInInventory(int id)
        {
            int count = 0;
            foreach (Item item in CharacterMainControl.Main.CharacterItem.Inventory.Content) if (item != null && item.TypeID == id) count += item.StackCount;
            return count;
        }
    }
    internal sealed class SkyIslandSession { internal bool IsReady = true; }
    internal sealed partial class SkyIslandFieldcraft
    {
        internal bool disposed;
        private bool inventoryBusy;
        internal readonly SkyIslandSession session = new SkyIslandSession();
        internal int CountInPack(int id) { return ItemFactory.GetItemCountInInventory(id); }
    }
    internal sealed class SkyIslandStoryService
    {
        internal object Current = new object();
        internal bool CanWrite = true;
        internal string SaveStatus = "read only";
        internal void LogTiming(string key, string value) { }
    }
    internal static class SkyIslandMosquitoRules
    {
        internal static bool FrogsComplete(object data) { return false; }
        internal const string SpawnNeedsNight = "night", FrogsAlreadyHome = "complete", SpawnAlreadyCarried = "carried", SpawnNeedsFiber = "fiber", SpawnTaken = "taken";
    }
    internal sealed partial class SkyIslandGnats
    {
        internal bool Usable { get { return !owner.disposed && owner.session.IsReady; } }
        internal bool NightNow = true;
        internal bool carryingSpawn;
        internal SkyIslandStoryService story = new SkyIslandStoryService();
        internal readonly SkyIslandFieldcraft owner = new SkyIslandFieldcraft();
    }
    public static class ItemUtilities
    {
        public static bool ThrowBefore, ThrowAfterOwnership, ThrowAfterBuffer;
        public static void SendToPlayer(Item item, bool drop = false, bool combine = true)
        {
            if (ThrowBefore) throw new InvalidOperationException("transfer unavailable");
            Inventory pack = CharacterMainControl.Main != null ? CharacterMainControl.Main.CharacterItem.Inventory : new Inventory();
            if (!pack.AddItem(item)) { if (drop) item.Drop(CharacterMainControl.Main, true); }
            if (ThrowAfterOwnership) throw new InvalidOperationException("notification failed");
        }
        public static void SendToPlayerStorage(Item item)
        {
            PlayerStorage.IncomingItemBuffer.Add(new ItemStatsSystem.Data.ItemTreeData { rootInstanceID = item.GetInstanceID(), RootTypeID = item.TypeID });
            if (ThrowAfterBuffer) throw new InvalidOperationException("buffer notification failed");
        }
    }
}
