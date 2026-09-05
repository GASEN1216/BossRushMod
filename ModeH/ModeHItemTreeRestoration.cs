using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace BossRush
{
    internal static partial class ModeHItemTreeNormalizer
    {
        // 扩展的是字符串中的自描述载荷，旧 journal DTO、摘要及七个 header 字段不变。
        internal static bool HasRestoreData(ModeHItemTreeSnapshotDto snapshot)
        {
            return snapshot != null && snapshot.normalizedTreePayload != null
                && snapshot.normalizedTreePayload.Contains("\"variableRecords\"");
        }

        private static void WriteRestoreData(BossRushJsonValue node, ItemTreeData.DataEntry entry)
        {
            BossRushJsonValue records = BossRushJsonValue.NewArray();
            List<CustomData> variables = new List<CustomData>(entry.variables);
            variables.Sort(delegate(CustomData a, CustomData b) { return string.CompareOrdinal(a.Key, b.Key); });
            foreach (CustomData data in variables)
            {
                BossRushJsonValue record = BossRushJsonValue.NewObject();
                record.AddProperty("key", BossRushJsonValue.NewString(data.Key));
                record.AddProperty("type", BossRushJsonValue.NewInteger((int)data.DataType));
                record.AddProperty("display", BossRushJsonValue.NewInteger(data.Display ? 1 : 0));
                record.AddProperty("raw", BossRushJsonValue.NewString(ToHex(data.GetRawCopied())));
                records.Items.Add(record);
            }
            node.AddProperty("variableRecords", records);
            BossRushJsonValue locks = BossRushJsonValue.NewArray();
            if (entry.inventorySortLocks != null)
                foreach (int index in entry.inventorySortLocks) locks.Items.Add(BossRushJsonValue.NewInteger(index));
            node.AddProperty("sortLocks", locks);
        }

        /// <summary>重建完整官方物品树数据，由官方缓冲区实例化；不按 TypeID 伪造押品。</summary>
        internal static ItemTreeData TryRestore(ModeHItemTreeSnapshotDto snapshot, out string error)
        {
            error = null;
            try
            {
                BossRushJsonValue root;
                if (snapshot == null || !ModeHCanonicalDigest.TryParse(snapshot.normalizedTreePayload, out root, out error))
                    return null;
                string digest;
                if (!ModeHCanonicalDigest.TryComputeValueDigest(root, out digest, out error)
                    || digest != snapshot.semanticTreeDigest) throw new InvalidOperationException("payload_digest");
                ItemTreeData tree = new ItemTreeData();
                tree.rootInstanceID = ReadInt(root, "rootLocalId");
                foreach (BossRushJsonValue node in root.GetProperty("nodes").Items)
                {
                    ItemTreeData.DataEntry entry = new ItemTreeData.DataEntry();
                    entry.instanceID = ReadInt(node, "localId");
                    entry.typeID = ReadInt(node, "typeId");
                    BossRushDynamicItemRegistry.EnsureRegistered(entry.typeID);
                    Item prefab = ItemAssetsCollection.GetPrefab(entry.typeID);
                    if (prefab == null) throw new InvalidOperationException("tree_prefab_missing:" + entry.typeID);
                    BossRushJsonValue records = node.GetProperty("variableRecords");
                    if (records != null)
                    {
                        foreach (BossRushJsonValue record in records.Items)
                        {
                            int kind = ReadInt(record, "type");
                            if (!Enum.IsDefined(typeof(CustomDataType), kind)) throw new InvalidOperationException("variable_type");
                            CustomData data = new CustomData(record.GetProperty("key").StringValue,
                                (CustomDataType)kind, FromHex(record.GetProperty("raw").StringValue));
                            data.Display = ReadInt(record, "display") != 0;
                            entry.variables.Add(data);
                        }
                    }
                    else
                    {
                        // 旧载荷没保存动态变量类型，只有 prefab 中可证实的类型可恢复，绝不猜测。
                        foreach (BossRushJsonValue value in node.GetProperty("variables").Items)
                        {
                            string line = value.StringValue;
                            int split = line.LastIndexOf('=');
                            if (split < 0 || prefab == null) throw new InvalidOperationException("legacy_variable");
                            string key = line.Substring(0, split);
                            CustomData template = prefab.Variables.GetEntry(key);
                            if (template == null) throw new InvalidOperationException("legacy_variable_type_missing:" + key);
                            CustomData data = new CustomData(key, template.DataType, FromHex(line.Substring(split + 1)));
                            data.Display = template.Display;
                            entry.variables.Add(data);
                        }
                    }
                    foreach (BossRushJsonValue slot in node.GetProperty("slots").Items)
                        entry.slotContents.Add(new ItemTreeData.SlotInstanceIDPair(
                            slot.GetProperty("slot").StringValue, ReadInt(slot, "localId")));
                    foreach (BossRushJsonValue item in node.GetProperty("inventory").Items)
                        entry.inventory.Add(new ItemTreeData.InventoryDataEntry(ReadInt(item, "position"), ReadInt(item, "localId")));
                    BossRushJsonValue locks = node.GetProperty("sortLocks");
                    if (locks != null)
                        foreach (BossRushJsonValue index in locks.Items) entry.inventorySortLocks.Add(checked((int)index.IntegerValue));
                    if (entry.StackCount != ReadInt(node, "stackCount")) throw new InvalidOperationException("stack_count");
                    tree.entries.Add(entry);
                }
                // 检查孤儿、重复引用、循环和断边，再重新规范化比对全部内容。
                HashSet<int> ids = new HashSet<int>();
                HashSet<int> children = new HashSet<int>();
                foreach (ItemTreeData.DataEntry entry in tree.entries)
                {
                    if (!ids.Add(entry.instanceID)) throw new InvalidOperationException("duplicate_node");
                    foreach (var slot in entry.slotContents)
                        if (!children.Add(slot.instanceID)) throw new InvalidOperationException("shared_node");
                    foreach (var item in entry.inventory)
                        if (!children.Add(item.instanceID)) throw new InvalidOperationException("shared_node");
                }
                if (children.Contains(tree.rootInstanceID) || children.Count != ids.Count - 1
                    || !children.IsSubsetOf(ids)) throw new InvalidOperationException("tree_edges");
                string rebuilt;
                if (!TryWriteNormalizedPayload(tree, out rebuilt, out error, HasRestoreData(snapshot))
                    || rebuilt != snapshot.normalizedTreePayload) throw new InvalidOperationException("tree_roundtrip");
                BossRushDynamicItemRegistry.EnsureRegistered(tree.RootTypeID);
                if (ItemAssetsCollection.GetMetaData(tree.RootTypeID).quality != snapshot.gameQuality)
                    throw new InvalidOperationException("tree_quality");
                return tree;
            }
            catch (Exception e) { error = "escrow_restore_failed:" + e.Message; return null; }
        }

        private static int ReadInt(BossRushJsonValue node, string key)
        {
            BossRushJsonValue value = node.GetProperty(key);
            if (value == null || value.Kind != BossRushJsonKind.Integer) throw new InvalidOperationException(key);
            return checked((int)value.IntegerValue);
        }

        private static byte[] FromHex(string hex)
        {
            if (hex == null || hex.Length % 2 != 0) throw new InvalidOperationException("hex");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }
}
