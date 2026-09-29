// 显式保存官方物品树：官方 SlotInstanceIDPair / InventoryDataEntry 没有 Serializable，
// 不能直接交给 JsonUtility，否则配件与嵌套容器的连接可能静默丢失。
using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem.Data;

namespace BossRush
{
    internal sealed class PetNestBackpackSnapshot
    {
        internal readonly List<PetNestBackpackEntry> Items = new List<PetNestBackpackEntry>();
        internal readonly List<int> Locks = new List<int>();

        internal string Encode()
        {
            BossRushJsonWriter writer = new BossRushJsonWriter();
            writer.BeginObject().Int("version", 1).BeginArray("items");
            foreach (PetNestBackpackEntry item in Items)
            {
                ValidateTree(item.Tree);
                writer.BeginObject().Int("position", item.Position).BeginObject("tree")
                    .Int("rootInstanceID", item.Tree.rootInstanceID).BeginArray("entries");
                foreach (ItemTreeData.DataEntry entry in item.Tree.entries)
                {
                    writer.BeginObject().Int("instanceID", entry.instanceID).Int("typeID", entry.typeID)
                        .BeginArray("variables");
                    foreach (CustomData variable in entry.variables)
                    {
                        writer.BeginObject().Str("key", variable.Key).Int("dataType", (int)variable.DataType)
                            .Bool("display", variable.Display).BeginArray("data");
                        foreach (byte value in variable.GetRawCopied()) writer.ItemInt(value);
                        writer.EndArray().EndObject();
                    }
                    writer.EndArray().BeginArray("slotContents");
                    foreach (ItemTreeData.SlotInstanceIDPair slot in entry.slotContents)
                        writer.BeginObject().Str("slot", slot.slot).Int("instanceID", slot.instanceID).EndObject();
                    writer.EndArray().BeginArray("inventory");
                    foreach (ItemTreeData.InventoryDataEntry child in entry.inventory)
                        writer.BeginObject().Int("position", child.position).Int("instanceID", child.instanceID).EndObject();
                    writer.EndArray().BeginArray("inventorySortLocks");
                    foreach (int index in entry.inventorySortLocks) writer.ItemInt(index);
                    writer.EndArray().EndObject();
                }
                writer.EndArray().EndObject().EndObject();
            }
            writer.EndArray().BeginArray("locks");
            foreach (int index in Locks) writer.ItemInt(index);
            return writer.EndArray().EndObject().ToString();
        }

        internal static PetNestBackpackSnapshot Decode(string json)
        {
            PetNestBackpackSnapshot result = new PetNestBackpackSnapshot();
            if (string.IsNullOrEmpty(json)) return result;
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
            if (ReadInt(root, "version") != 1) throw Invalid();
            HashSet<int> positions = new HashSet<int>();
            foreach (BossRushJsonValue item in ReadArray(root, "items"))
            {
                int position = ReadInt(item, "position");
                if (position < 0 || position == int.MaxValue || !positions.Add(position)) throw Invalid();
                BossRushJsonValue node = item.GetProperty("tree");
                ItemTreeData tree = new ItemTreeData { rootInstanceID = ReadInt(node, "rootInstanceID") };
                foreach (BossRushJsonValue data in ReadArray(node, "entries"))
                {
                    ItemTreeData.DataEntry entry = new ItemTreeData.DataEntry
                    {
                        instanceID = ReadInt(data, "instanceID"), typeID = ReadInt(data, "typeID")
                    };
                    foreach (BossRushJsonValue variable in ReadArray(data, "variables"))
                    {
                        int type = ReadInt(variable, "dataType");
                        if (!Enum.IsDefined(typeof(CustomDataType), type)) throw Invalid();
                        List<BossRushJsonValue> bytes = ReadArray(variable, "data");
                        byte[] raw = new byte[bytes.Count];
                        for (int i = 0; i < raw.Length; i++) raw[i] = checked((byte)ReadInt(bytes[i]));
                        CustomData value = new CustomData(ReadString(variable, "key"), (CustomDataType)type, raw);
                        value.Display = variable.GetBool("display", false);
                        entry.variables.Add(value);
                    }
                    foreach (BossRushJsonValue slot in ReadArray(data, "slotContents"))
                        entry.slotContents.Add(new ItemTreeData.SlotInstanceIDPair(ReadString(slot, "slot"), ReadInt(slot, "instanceID")));
                    foreach (BossRushJsonValue child in ReadArray(data, "inventory"))
                        entry.inventory.Add(new ItemTreeData.InventoryDataEntry(ReadInt(child, "position"), ReadInt(child, "instanceID")));
                    foreach (BossRushJsonValue index in ReadArray(data, "inventorySortLocks"))
                        entry.inventorySortLocks.Add(ReadInt(index));
                    tree.entries.Add(entry);
                }
                ValidateTree(tree);
                result.Items.Add(new PetNestBackpackEntry { Position = position, Tree = tree });
            }
            foreach (BossRushJsonValue index in ReadArray(root, "locks"))
            {
                int value = ReadInt(index);
                if (value < 0) throw Invalid();
                result.Locks.Add(value);
            }
            return result;
        }

        // 先证明每个节点都能从根到达且只出现一次，再允许实例化；坏数据保持原记录待恢复。
        internal static void ValidateTree(ItemTreeData tree)
        {
            if (tree == null || tree.entries == null || tree.entries.Count == 0) throw Invalid();
            Dictionary<int, ItemTreeData.DataEntry> nodes = new Dictionary<int, ItemTreeData.DataEntry>();
            foreach (ItemTreeData.DataEntry entry in tree.entries)
            {
                if (entry == null || entry.typeID <= 0 || nodes.ContainsKey(entry.instanceID)
                    || entry.variables == null || entry.slotContents == null || entry.inventory == null
                    || entry.inventorySortLocks == null) throw Invalid();
                nodes.Add(entry.instanceID, entry);
                // 官方集合允许同名项并按原顺序保存；原样保留，不在背包层自行去重。
                foreach (CustomData value in entry.variables)
                    if (value == null || value.Key == null) throw Invalid();
            }
            Stack<int> pending = new Stack<int>();
            HashSet<int> visited = new HashSet<int>();
            pending.Push(tree.rootInstanceID);
            while (pending.Count > 0)
            {
                int id = pending.Pop();
                ItemTreeData.DataEntry node;
                if (!visited.Add(id) || !nodes.TryGetValue(id, out node)) throw Invalid();
                HashSet<string> slots = new HashSet<string>(StringComparer.Ordinal);
                foreach (ItemTreeData.SlotInstanceIDPair slot in node.slotContents)
                {
                    if (slot == null || string.IsNullOrEmpty(slot.slot) || !slots.Add(slot.slot)) throw Invalid();
                    pending.Push(slot.instanceID);
                }
                HashSet<int> positions = new HashSet<int>();
                foreach (ItemTreeData.InventoryDataEntry item in node.inventory)
                {
                    if (item == null || item.position < 0 || !positions.Add(item.position)) throw Invalid();
                    pending.Push(item.instanceID);
                }
            }
            if (visited.Count != nodes.Count) throw Invalid();
        }

        private static int ReadInt(BossRushJsonValue node, string key) { return ReadInt(node == null ? null : node.GetProperty(key)); }
        private static int ReadInt(BossRushJsonValue value)
        {
            if (value == null || value.Kind != BossRushJsonKind.Integer) throw Invalid();
            return checked((int)value.IntegerValue);
        }
        private static string ReadString(BossRushJsonValue node, string key)
        {
            BossRushJsonValue value = node == null ? null : node.GetProperty(key);
            if (value == null || value.Kind != BossRushJsonKind.String) throw Invalid();
            return value.StringValue;
        }
        private static List<BossRushJsonValue> ReadArray(BossRushJsonValue node, string key)
        {
            BossRushJsonValue value = node == null ? null : node.GetProperty(key);
            if (value == null || value.Kind != BossRushJsonKind.Array || value.Items == null) throw Invalid();
            return value.Items;
        }
        private static InvalidOperationException Invalid() { return new InvalidOperationException("backpack_snapshot_invalid"); }
    }

    internal sealed class PetNestBackpackEntry
    {
        internal int Position;
        internal ItemTreeData Tree;
    }
}
