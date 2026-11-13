using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool x = ReferenceEquals(a, null) || a.Destroyed, y = ReferenceEquals(b, null) || b.Destroyed;
            return x || y ? x == y : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            GameObject gameObject = value as GameObject;
            if (!ReferenceEquals(gameObject, null)) foreach (Object component in gameObject.Components) Destroy(component);
        }
    }
    public sealed class GameObject : Object { public readonly List<Object> Components = new List<Object>(); }
    public static class Time { public static int frameCount; public static float unscaledTime; public static float time { get { return unscaledTime; } } }
    public static class Debug { public static void Log(string text) { } public static void LogWarning(string text) { } }
}

public sealed class LevelManager { public static LevelManager Instance; public bool IsBaseLevel; }
public sealed class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main;
    public Item CharacterItem;
    public CharacterHealth Health;
}
public sealed class CharacterHealth { public float CurrentHealth = 100f; }
public sealed class GameClock { public static GameClock Instance; public static long Day; }

namespace Saves
{
    public static class SavesSystem
    {
        public static int CurrentSlot;
        public static string CurrentFilePath { get { return "fixture"; } }
        private static bool saving;
        public static bool IsSaving { get { return saving; } }
        public static string FailWrite, FailReadback, PersistentFailWrite, PersistentFailLoad;
        public static Action AfterWriteFailure;
        private static string pendingReadback;
        public static bool FailPhysical;
        public static Dictionary<string, object> Cache = new Dictionary<string, object>(), Disk = new Dictionary<string, object>();
        public static readonly List<Dictionary<string, object>> History = new List<Dictionary<string, object>>();
        public static readonly List<string> Operations = new List<string>();
        public static event Action OnCollectSaveData, OnSetFile, OnSaveDeleted;
        public static bool KeyExisits(string key) { return Cache.ContainsKey(key); }
        public static T Load<T>(string key)
        {
            if (PersistentFailLoad == key) throw new IOException("persistent read");
            if (key == pendingReadback) { pendingReadback = null; throw new IOException("readback"); }
            return Cache.ContainsKey(key) ? (T)Clone(Cache[key]) : default(T);
        }
        public static void Save<T>(string key, T value)
        {
            Operations.Add(key);
            if (PersistentFailWrite == key) throw new IOException("persistent write");
            if (FailWrite == key) { FailWrite = null; AfterWriteFailure?.Invoke(); throw new IOException("write"); }
            Cache[key] = Clone(value);
            if (FailReadback == key) { FailReadback = null; pendingReadback = key; }
        }
        public static void SaveFile(bool timestamp)
        {
            saving = true;
            if (FailPhysical) { FailPhysical = false; throw new IOException("physical"); }
            Disk = Cache.ToDictionary(pair => pair.Key, pair => Clone(pair.Value));
            History.Add(Disk);
            saving = false;
        }
        private static object Clone(object value)
        {
            ItemTreeData tree = value as ItemTreeData;
            if (tree != null) return tree.Copy();
            InventoryData inventory = value as InventoryData;
            if (inventory != null) return inventory.Copy();
            List<ItemTreeData> trees = value as List<ItemTreeData>;
            if (trees != null) return trees.Select(t => t == null ? null : t.Copy()).ToList();
            return value;
        }
        public static void Collect() { OnCollectSaveData?.Invoke(); }
        public static void SetSlot(int slot, bool notify)
        { CurrentSlot = slot; Cache.Clear(); Disk.Clear(); if (notify) OnSetFile?.Invoke(); }
        public static void Reset()
        {
            CurrentSlot = 1; saving = FailPhysical = false; FailWrite = FailReadback = PersistentFailWrite = PersistentFailLoad = pendingReadback = null;
            AfterWriteFailure = null;
            Cache.Clear(); Disk.Clear(); History.Clear(); Operations.Clear();
            OnCollectSaveData = OnSetFile = OnSaveDeleted = null;
        }
    }
}
public static class ES3
{
    public enum Location { Cache = 4 }
    public static void DeleteKey(string key, ES3Settings settings)
    { if (settings.location != Location.Cache) throw new Exception("must only mutate cache"); Saves.SavesSystem.Cache.Remove(key); }
}
public sealed class ES3Settings { public readonly ES3.Location location; public ES3Settings(string path, params Enum[] options) { location = (ES3.Location)options[0]; } }

namespace ItemStatsSystem.Items
{
    public sealed class Slot
    {
        public Item Content;
        public bool Plug(Item item, out Item displaced)
        { displaced = Content; item.Detach(); Content = item; item.PluggedIntoSlot = this; return true; }
    }
}
namespace ItemStatsSystem
{
    public sealed class Item : UnityEngine.Object
    {
        private static int nextId = 1;
        public static int SaveCalls;
        public readonly int Id = nextId++;
        public int TypeID, StackCount = 1;
        public bool Stackable;
        public Inventory Inventory, InInventory;
        public Slot PluggedIntoSlot;
        public readonly List<Slot> Slots = new List<Slot>();
        public bool IsBeingDestroyed { get { return Destroyed; } }
        public int GetInstanceID() { return Id; }
        public void Detach()
        {
            InInventory?.BeforeRemove?.Invoke(this);
            if (InInventory != null) { Inventory owner = InInventory; int at = owner.GetIndex(this); owner.Content[at] = null; InInventory = null; owner.AfterRemove?.Invoke(this); }
            if (PluggedIntoSlot != null) { PluggedIntoSlot.Content = null; PluggedIntoSlot = null; }
        }
        public void DestroyTree()
        { foreach (Item child in GetAllChildren(true, true)) UnityEngine.Object.Destroy(child); UnityEngine.Object.Destroy(this); }
        public List<Item> GetAllChildren(bool recursive, bool excludeSelf)
        {
            var children = new List<Item>();
            if (!excludeSelf) children.Add(this);
            IEnumerable<Item> direct = (Inventory == null ? Enumerable.Empty<Item>() : Inventory.Content).Concat(Slots.Select(s => s.Content));
            foreach (Item item in direct) if (item != null) { children.Add(item); if (recursive) children.AddRange(item.GetAllChildren(true, true)); }
            return children;
        }
        public void Save(string key) { SaveCalls++; Saves.SavesSystem.Save("Item/" + key, ItemTreeData.FromItem(this)); }
    }
    public sealed class Inventory
    {
        public readonly List<Item> Content = new List<Item>();
        public int Capacity = 30;
        public Action<Item> AfterRemove;
        public Action<Item> BeforeRemove;
        public int GetIndex(Item item) { return Content.IndexOf(item); }
        public Item GetItemAt(int index) { return index >= 0 && index < Content.Count ? Content[index] : null; }
        public bool AddAt(Item item, int index)
        {
            if (index < 0 || index >= Capacity || GetItemAt(index) != null) return false;
            item.Detach(); while (Content.Count <= index) Content.Add(null);
            Content[index] = item; item.InInventory = this; return true;
        }
        public bool AddItem(Item item)
        { for (int i = 0; i < Capacity; i++) if (GetItemAt(i) == null) return AddAt(item, i); return false; }
        public void Save(string key) { Saves.SavesSystem.Save("Inventory/" + key, InventoryData.FromInventory(this)); }
    }
    public static class ItemAssetsCollection
    {
        public static int MissingType;
        public static Item GetPrefab(int id) { return id == MissingType ? null : new Item { TypeID = id }; }
        public static Item InstantiateSync(int id) { return id == MissingType ? null : new Item { TypeID = id }; }
    }
}
namespace ItemStatsSystem.Data
{
    public sealed class InventoryData
    {
        public int capacity;
        public List<int> lockedIndexes = new List<int>();
        public List<Entry> entries = new List<Entry>();
        public sealed class Entry { public int inventoryPosition; public ItemTreeData itemTreeData; }
        public static InventoryData FromInventory(Inventory inventory)
        {
            var data = new InventoryData { capacity = inventory.Capacity };
            for (int i = 0; i < inventory.Content.Count; i++) if (inventory.Content[i] != null)
                data.entries.Add(new Entry { inventoryPosition = i, itemTreeData = ItemTreeData.FromItem(inventory.Content[i]) });
            return data;
        }
        public InventoryData Copy()
        { return new InventoryData { capacity = capacity, lockedIndexes = new List<int>(lockedIndexes),
            entries = entries.Select(e => new Entry { inventoryPosition = e.inventoryPosition, itemTreeData = e.itemTreeData.Copy() }).ToList() }; }
    }
    public sealed class ItemTreeData
    {
        public int rootInstanceID;
        public List<DataEntry> entries = new List<DataEntry>();
        public int RootTypeID { get { DataEntry entry = entries.Find(e => e.instanceID == rootInstanceID); return entry == null ? 0 : entry.typeID; } }
        public sealed class DataEntry
        {
            public int instanceID, typeID;
            public List<string> variables = new List<string>();
            public List<int> inventorySortLocks = new List<int>();
            public List<InventoryDataEntry> inventory = new List<InventoryDataEntry>();
            public List<SlotInstanceIDPair> slotContents = new List<SlotInstanceIDPair>();
        }
        public sealed class InventoryDataEntry { public int position, instanceID; public InventoryDataEntry(int position, int id) { this.position = position; instanceID = id; } }
        public sealed class SlotInstanceIDPair { public string slot; public int instanceID; public SlotInstanceIDPair(string slot, int id) { this.slot = slot; instanceID = id; } }
        public static ItemTreeData FromItem(Item item)
        {
            var tree = new ItemTreeData { rootInstanceID = item.Id };
            foreach (Item child in item.GetAllChildren(true, false))
            {
                var entry = new DataEntry { instanceID = child.Id, typeID = child.TypeID };
                if (child.Inventory != null) for (int i = 0; i < child.Inventory.Content.Count; i++)
                    if (child.Inventory.Content[i] != null) entry.inventory.Add(new InventoryDataEntry(i, child.Inventory.Content[i].Id));
                foreach (Slot slot in child.Slots) if (slot.Content != null) entry.slotContents.Add(new SlotInstanceIDPair("slot", slot.Content.Id));
                tree.entries.Add(entry);
            }
            return tree;
        }
        public ItemTreeData Copy()
        {
            return new ItemTreeData { rootInstanceID = rootInstanceID, entries = entries.Select(e => new DataEntry {
                instanceID = e.instanceID, typeID = e.typeID, variables = new List<string>(e.variables), inventorySortLocks = new List<int>(e.inventorySortLocks),
                inventory = e.inventory.Select(i => new InventoryDataEntry(i.position, i.instanceID)).ToList(),
                slotContents = e.slotContents.Select(s => new SlotInstanceIDPair(s.slot, s.instanceID)).ToList() }).ToList() };
        }
    }
}
public sealed class PlayerStorage
{
    public static PlayerStorage Instance;
    private readonly Inventory inventory = new Inventory();
    public static Inventory Inventory { get { return Instance == null ? null : Instance.inventory; } }
    public static bool Loading;
    public static List<ItemTreeData> IncomingItemBuffer { get { return PlayerStorageBuffer.Buffer; } }
    public bool HasInitialized() { return true; }
    public static void Push(Item item, bool direct)
    {
        if (!direct && Inventory != null && Inventory.AddItem(item)) return;
        IncomingItemBuffer.Add(ItemTreeData.FromItem(item)); item.Detach(); item.DestroyTree();
    }
}
public sealed class PlayerStorageBuffer
{
    public static PlayerStorageBuffer Instance;
    public static List<ItemTreeData> Buffer = new List<ItemTreeData>();
    public static void SaveBuffer() { Saves.SavesSystem.Save("PlayerStorage_Buffer", Buffer); }
}
public static class ItemUtilities
{
    public static bool RejectDelivery;
    public static Action AfterSend;
    public static void SendToPlayer(Item item, bool dontMerge, bool toStorage)
    {
        if (RejectDelivery) return;
        if (!CharacterMainControl.Main.CharacterItem.Inventory.AddItem(item) && toStorage) PlayerStorage.Push(item, false);
        AfterSend?.Invoke();
    }
}
namespace Duckov.NoteIndexs
{
    public sealed class Note { public string key; public object image; public bool hide; }
    public sealed class NoteIndex
    {
        public static NoteIndex Instance;
        public readonly List<Note> Notes = new List<Note>();
        public readonly HashSet<string> UnlockedNotes = new HashSet<string>();
        public static Action<string> onNoteStatusChanged;
        public static void SetNoteDynamic(Note note) { }
        public static bool GetNoteUnlocked(string key) { return Instance.UnlockedNotes.Contains(key); }
        public static void SetNoteUnlocked(string key) { Instance.UnlockedNotes.Add(key); onNoteStatusChanged?.Invoke(key); }
    }
}
namespace BossRush
{
    public static class ModBehaviour { public static void DevLog(string text) { } }
    public static class L10n { public static string T(string zh, string en) { return en; } }
    public static class TidegateText
    {
        public static string LetterTitle = "letter", LetterBody = "letter body", EndingTitle = "ending", EndingBody = "ending body";
        public static string PageTitle(int index) { return "page"; } public static string PageBody(int index) { return "page body"; }
    }
    public static class TidegateRaidScene { public const string LogTag = "[Tidegate]"; }
    public static class LocalizationHelper { public static void InjectLocalizations(Dictionary<string, string> map) { } }
    public static class BossRushConfirmDialog
    {
        public sealed class Options { public string Title, Body, Warning, ConfirmLabel, CancelLabel; public bool Danger, SingleAction; public object Illustration; public Action OnConfirm; }
        public static void Show(Options options) { }
    }
    public static class TidegateJournalArt { public static object Get(string id) { return null; } public static void ResetStaticCaches() { } }
    public static class CourierService
    {
        public static int QuickDeliverItems(Item[] items, string banner, bool notify, out int fallback)
        { fallback = 0; foreach (Item item in items) PlayerStorage.Push(item, true); PlayerStorageBuffer.SaveBuffer(); return items.Length; }
    }
}
