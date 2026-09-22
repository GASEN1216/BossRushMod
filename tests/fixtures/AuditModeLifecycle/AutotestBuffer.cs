using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace UnityEngine
{
    public class GameObject { public Item Item; }
    public static class Object { public static void Destroy(GameObject obj) { if (obj != null && obj.Item != null) obj.Item.Destroyed = true; } }
}
namespace ItemStatsSystem
{
    public sealed class Item
    {
        public int TypeID, StackCount = 1;
        public bool Stackable, Destroyed;
        public Inventory Inventory;
        public UnityEngine.GameObject gameObject;
        public Item(int type, int count = 1) { TypeID = type; StackCount = count; Stackable = count > 1; gameObject = new UnityEngine.GameObject { Item = this }; }
        public void Save(string key) { Saves.SavesSystem.AssetWrites++; }
    }
    public sealed class Inventory
    {
        public List<Item> Content = new List<Item>();
        public void RemoveItem(Item item) { Content.Remove(item); }
        public void Save(string key) { Saves.SavesSystem.AssetWrites++; }
    }
}
namespace ItemStatsSystem.Data
{
    public sealed class CustomData
    {
        public string Key = "Count";
        public int Value;
        public void SetInt(int value) { Value = value; }
    }
    public sealed class ItemTreeData
    {
        public int rootInstanceID;
        public List<DataEntry> entries = new List<DataEntry>();
        public DataEntry RootData { get { return entries.Find(e => e.instanceID == rootInstanceID); } }
        public int RootTypeID { get { return RootData == null ? 0 : RootData.typeID; } }
        public sealed class DataEntry
        {
            public int instanceID, typeID;
            public List<CustomData> variables = new List<CustomData>();
            public int StackCount { get { var count = variables.Find(v => v.Key == "Count"); return count == null ? 1 : count.Value; } }
        }
    }
}
public sealed class CharacterMainControl
{
    public static CharacterMainControl Main;
    public Item CharacterItem = new Item(0) { Inventory = new Inventory() };
}
public static class PlayerStorage { public static Inventory Inventory; }
public sealed class PlayerStorageBuffer
{
    public static PlayerStorageBuffer Instance;
    public static List<ItemTreeData> Buffer = new List<ItemTreeData>();
    public static bool FailSave;
    public static void SaveBuffer()
    {
        if (FailSave) throw new InvalidOperationException("buffer save failure");
        Saves.SavesSystem.AssetWrites++;
    }
}
namespace Saves
{
    public static class SavesSystem
    {
        public static int CurrentSlot = 1, AssetWrites, PhysicalWrites;
        public static bool IsSaving, FailPhysical;
        public static string Key = "original_snapshot", PhysicalKey = "original_snapshot";
        public static T Load<T>(string key) { return (T)(object)Key; }
        public static void Save<T>(string key, T value) { Key = (string)(object)value; }
        public static void SaveFile(bool ignored)
        {
            if (FailPhysical) throw new InvalidOperationException("physical failure after cached key changed");
            PhysicalKey = Key; PhysicalWrites++;
        }
    }
}
namespace BossRush
{
    public static class ModBehaviour { public static void DevLog(string text) { } }
    public partial class BufferRestoreProbe
    {
        private const string AutotestSnapshotKey = "snapshot";
        private sealed class AutotestSnapshot
        {
            public int Slot;
            public bool BufferCountsIncluded;
            public Dictionary<int, int> Items = new Dictionary<int, int>();
        }
        private static readonly int[] AutotestLedgerTypeIds = { 42 };
        private static bool AutotestWriteAllowed(out string reason) { reason = null; return true; }
        public string Detail;
        public bool Restore(int before, bool bufferIncluded = true, int slot = 1)
        {
            var snapshot = new AutotestSnapshot { Slot = slot, BufferCountsIncluded = bufferIncluded };
            snapshot.Items[42] = before;
            return TryReclaimAutotestItems(snapshot, out Detail) && ClearAutotestSnapshotKey();
        }
        public static int Count() { string where; return CountOwnedItems(42, out where); }
        public static string CountDetail() { string where; CountOwnedItems(42, out where); return where; }
    }
    internal static class Program
    {
        private static int checks;
        private static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
        private static ItemTreeData Tree(int id, int type = 42, int count = 1)
        {
            var tree = new ItemTreeData { rootInstanceID = id };
            tree.entries.Add(new ItemTreeData.DataEntry { instanceID = id, typeID = type });
            tree.RootData.variables.Add(new CustomData { Value = count });
            return tree;
        }
        private static void Reset()
        {
            CharacterMainControl.Main = new CharacterMainControl(); PlayerStorage.Inventory = new Inventory();
            PlayerStorageBuffer.Instance = new PlayerStorageBuffer(); PlayerStorageBuffer.Buffer.Clear(); PlayerStorageBuffer.FailSave = false;
            Saves.SavesSystem.Key = Saves.SavesSystem.PhysicalKey = "original_snapshot";
            Saves.SavesSystem.AssetWrites = Saves.SavesSystem.PhysicalWrites = 0;
            Saves.SavesSystem.IsSaving = Saves.SavesSystem.FailPhysical = false;
        }
        public static void Main()
        {
            var probe = new BufferRestoreProbe();
            Reset();
            var old = Tree(1, 42, 2); var unrelated = Tree(2, 99); var reward = Tree(3);
            PlayerStorageBuffer.Buffer.AddRange(new[] { old, unrelated, reward });
            Check(BufferRestoreProbe.Count() == 3, "readonly count includes official pending buffer");
            Check(probe.Restore(2) && PlayerStorageBuffer.Buffer.Count == 2
                && ReferenceEquals(PlayerStorageBuffer.Buffer[0], old) && ReferenceEquals(PlayerStorageBuffer.Buffer[1], unrelated),
                "full-storage buffer reward reclaimed while original and unrelated trees remain");
            Check(Saves.SavesSystem.PhysicalKey == "" && Saves.SavesSystem.AssetWrites == 3, "asset snapshots precede durable recovery-key clear");

            Reset(); old = Tree(1); reward = Tree(2, 42, 5); PlayerStorageBuffer.Buffer.AddRange(new[] { old, reward });
            Check(probe.Restore(3) && ReferenceEquals(PlayerStorageBuffer.Buffer[0], old)
                && reward.RootData.StackCount == 2, "partial buffered stack removal retains baseline quantity");

            Reset(); old = Tree(1); reward = Tree(2); PlayerStorageBuffer.Buffer.AddRange(new[] { old, reward });
            PlayerStorageBuffer.FailSave = true;
            Check(!probe.Restore(1) && Saves.SavesSystem.Key == "original_snapshot" && Saves.SavesSystem.PhysicalWrites == 0,
                "SaveBuffer failure preserves recovery key and skips physical clear");
            PlayerStorageBuffer.FailSave = false;
            Check(probe.Restore(1) && PlayerStorageBuffer.Buffer.Count == 1 && ReferenceEquals(PlayerStorageBuffer.Buffer[0], old),
                "retry after Buffer save failure is idempotent");

            Reset(); PlayerStorageBuffer.Buffer.Add(Tree(7)); Saves.SavesSystem.FailPhysical = true;
            Check(!probe.Restore(0) && Saves.SavesSystem.Key == "original_snapshot" && Saves.SavesSystem.PhysicalKey == "original_snapshot",
                "physical failure restores cached recovery key after clear changed cache");

            Reset(); old = Tree(1); PlayerStorageBuffer.Buffer.Add(old);
            Check(!probe.Restore(0, false) && ReferenceEquals(PlayerStorageBuffer.Buffer[0], old)
                && Saves.SavesSystem.Key == "original_snapshot", "legacy snapshot cannot delete uncounted original Buffer");
            Check(!probe.Restore(0, true, 2) && PlayerStorageBuffer.Buffer.Count == 1, "slot mismatch rejects before any mutation");
            Reset(); Check(probe.Restore(0, false), "legacy snapshot with empty Buffer remains recoverable");

            Reset(); old = Tree(1); old.entries = null; PlayerStorageBuffer.Buffer.Add(old);
            Check(!probe.Restore(0) && Saves.SavesSystem.Key == "original_snapshot", "malformed Buffer keeps recovery key without destructive recovery");
            Reset(); CharacterMainControl.Main = null;
            Check(BufferRestoreProbe.CountDetail().Contains("pack:error"), "missing player is an unreadable count rather than zero assets");
            CharacterMainControl.Main = new CharacterMainControl(); CharacterMainControl.Main.CharacterItem.Inventory = null;
            Check(BufferRestoreProbe.CountDetail().Contains("pack:error"), "missing inventory cannot authorize a zero baseline snapshot");
            Console.WriteLine("PASS AutotestBuffer " + checks + " assertions");
        }
    }
}
