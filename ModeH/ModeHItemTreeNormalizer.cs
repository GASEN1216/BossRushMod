using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace BossRush
{
    /// <summary>
    /// Mode H 物品树规范化（设计提案 §22.3、§25.1）。
    ///
    /// 冻结契约：
    /// - `sourceTree` 是**完整根物品树**，包含嵌套弹匣、附件和容器；
    /// - 写摘要前把树内 instance id 重映射为**确定性局部序号**，
    ///   因此同一棵树在不同运行时得到同一个 `semanticTreeDigest`；
    /// - `Item.GetInstanceID`、`TypeID` 与 `LockIndex` 都**不是**跨运行时所有权证明，
    ///   摘要只用于提交前置条件比对，不当作物品 GUID；
    /// - 出现次数（`preCount`/`postCount`）单独核对，防止“同 TypeID 顶替”。
    ///
    /// 本类不写库存、不实例化物品，只做只读快照与摘要。
    /// </summary>
    internal static class ModeHItemTreeNormalizer
    {
        #region 快照

        /// <summary>
        /// 为一个根物品构造规范化快照。失败返回 null。
        /// `inventoryPosition` 是它在仓库中的槽位，只作为提交前置条件之一。
        /// </summary>
        public static ModeHItemTreeSnapshotDto TryCapture(
            Item rootItem, int inventoryPosition, int occurrenceCount, out string failureReasonId)
        {
            failureReasonId = null;
            if (rootItem == null)
            {
                failureReasonId = "tree_root_null";
                return null;
            }

            ItemTreeData tree;
            try { tree = ItemTreeData.FromItem(rootItem); }
            catch (Exception e)
            {
                failureReasonId = "tree_capture_exception:" + e.GetType().Name;
                return null;
            }
            if (tree == null)
            {
                failureReasonId = "tree_capture_null";
                return null;
            }

            string normalized;
            if (!TryWriteNormalizedPayload(tree, out normalized, out failureReasonId)) return null;

            string digest, digestError;
            if (!ModeHCanonicalDigest.TryComputeSha256OfText(normalized, out digest, out digestError))
            {
                failureReasonId = "tree_digest_failed:" + digestError;
                return null;
            }

            int quality;
            try { quality = rootItem.Quality; }
            catch (Exception)
            {
                // 读不到品质即无法证明“同品质返还”，按 fail-closed 拒绝
                failureReasonId = "tree_quality_unreadable";
                return null;
            }
            if (quality < ModeHConfig.MinGameQuality || quality > ModeHConfig.MaxGameQuality)
            {
                failureReasonId = "tree_quality_out_of_range";
                return null;
            }

            ModeHItemTreeSnapshotDto dto = new ModeHItemTreeSnapshotDto();
            dto.sourcePosition = inventoryPosition;
            dto.semanticTreeDigest = digest;
            dto.normalizedTreePayload = normalized;
            dto.gameQuality = quality;
            dto.preCount = occurrenceCount;
            dto.postCount = 0;
            return dto;
        }

        #endregion

        #region 规范化

        /// <summary>
        /// 把 `ItemTreeData` 写成与运行时 instance id 无关的规范文本：
        /// 先按“根优先、随后按 (typeID, 结构) 稳定排序”给每个节点分配局部序号，
        /// 再按局部序号输出。任何 instance id 都不进入输出。
        /// </summary>
        public static bool TryWriteNormalizedPayload(
            ItemTreeData tree, out string payload, out string failureReasonId)
        {
            payload = null;
            failureReasonId = null;
            if (tree == null || tree.entries == null)
            {
                failureReasonId = "tree_entries_missing";
                return false;
            }

            Dictionary<int, ItemTreeData.DataEntry> byInstance =
                new Dictionary<int, ItemTreeData.DataEntry>();
            for (int i = 0; i < tree.entries.Count; i++)
            {
                ItemTreeData.DataEntry entry = tree.entries[i];
                if (entry == null) continue;
                byInstance[entry.instanceID] = entry;
            }
            if (!byInstance.ContainsKey(tree.rootInstanceID))
            {
                failureReasonId = "tree_root_entry_missing";
                return false;
            }

            // 确定性局部序号：从根开始的稳定深度优先遍历
            Dictionary<int, int> localIds = new Dictionary<int, int>();
            List<ItemTreeData.DataEntry> ordered = new List<ItemTreeData.DataEntry>();
            AssignLocalIds(tree.rootInstanceID, byInstance, localIds, ordered);

            ModeHJsonValue root = ModeHJsonValue.NewObject();
            root.AddProperty("rootLocalId", ModeHJsonValue.NewInteger(0));

            ModeHJsonValue nodes = ModeHJsonValue.NewArray();
            for (int i = 0; i < ordered.Count; i++)
            {
                ItemTreeData.DataEntry entry = ordered[i];
                ModeHJsonValue node = ModeHJsonValue.NewObject();
                node.AddProperty("localId", ModeHJsonValue.NewInteger(localIds[entry.instanceID]));
                node.AddProperty("typeId", ModeHJsonValue.NewInteger(entry.typeID));
                node.AddProperty("stackCount", ModeHJsonValue.NewInteger(entry.StackCount));
                node.AddProperty("slots", WriteSlots(entry, localIds));
                node.AddProperty("inventory", WriteInventory(entry, localIds));
                node.AddProperty("variables", WriteVariables(entry));
                nodes.Items.Add(node);
            }
            root.AddProperty("nodes", nodes);

            string canonical, canonicalError;
            if (!ModeHCanonicalDigest.TryWriteCanonical(root, out canonical, out canonicalError))
            {
                failureReasonId = "tree_canonical_failed:" + canonicalError;
                return false;
            }
            payload = canonical;
            return true;
        }

        /// <summary>稳定深度优先编号：槽位先（按 slot hash 升序），随后是 inventory（按位置升序）。</summary>
        private static void AssignLocalIds(
            int instanceId,
            Dictionary<int, ItemTreeData.DataEntry> byInstance,
            Dictionary<int, int> localIds,
            List<ItemTreeData.DataEntry> ordered)
        {
            ItemTreeData.DataEntry entry;
            if (!byInstance.TryGetValue(instanceId, out entry)) return;
            if (localIds.ContainsKey(instanceId)) return;

            localIds[instanceId] = localIds.Count;
            ordered.Add(entry);

            List<int> children = new List<int>();
            if (entry.slotContents != null)
            {
                List<ItemTreeData.SlotInstanceIDPair> pairs =
                    new List<ItemTreeData.SlotInstanceIDPair>(entry.slotContents);
                pairs.Sort(CompareSlotPair);
                for (int i = 0; i < pairs.Count; i++) children.Add(pairs[i].instanceID);
            }
            if (entry.inventory != null)
            {
                List<ItemTreeData.InventoryDataEntry> items =
                    new List<ItemTreeData.InventoryDataEntry>(entry.inventory);
                items.Sort(CompareInventoryEntry);
                for (int i = 0; i < items.Count; i++) children.Add(items[i].instanceID);
            }
            for (int i = 0; i < children.Count; i++)
            {
                AssignLocalIds(children[i], byInstance, localIds, ordered);
            }
        }

        private static int CompareSlotPair(
            ItemTreeData.SlotInstanceIDPair a, ItemTreeData.SlotInstanceIDPair b)
        {
            return string.CompareOrdinal(
                a.slot != null ? a.slot : string.Empty,
                b.slot != null ? b.slot : string.Empty);
        }

        private static int CompareInventoryEntry(
            ItemTreeData.InventoryDataEntry a, ItemTreeData.InventoryDataEntry b)
        {
            return a.position.CompareTo(b.position);
        }

        private static ModeHJsonValue WriteSlots(
            ItemTreeData.DataEntry entry, Dictionary<int, int> localIds)
        {
            ModeHJsonValue array = ModeHJsonValue.NewArray();
            if (entry.slotContents == null) return array;
            List<ItemTreeData.SlotInstanceIDPair> pairs =
                new List<ItemTreeData.SlotInstanceIDPair>(entry.slotContents);
            pairs.Sort(CompareSlotPair);
            for (int i = 0; i < pairs.Count; i++)
            {
                int local;
                if (!localIds.TryGetValue(pairs[i].instanceID, out local)) continue;
                ModeHJsonValue node = ModeHJsonValue.NewObject();
                node.AddProperty("slot", ModeHJsonValue.NewString(
                    pairs[i].slot != null ? pairs[i].slot : string.Empty));
                node.AddProperty("localId", ModeHJsonValue.NewInteger(local));
                array.Items.Add(node);
            }
            return array;
        }

        private static ModeHJsonValue WriteInventory(
            ItemTreeData.DataEntry entry, Dictionary<int, int> localIds)
        {
            ModeHJsonValue array = ModeHJsonValue.NewArray();
            if (entry.inventory == null) return array;
            List<ItemTreeData.InventoryDataEntry> items =
                new List<ItemTreeData.InventoryDataEntry>(entry.inventory);
            items.Sort(CompareInventoryEntry);
            for (int i = 0; i < items.Count; i++)
            {
                int local;
                if (!localIds.TryGetValue(items[i].instanceID, out local)) continue;
                ModeHJsonValue node = ModeHJsonValue.NewObject();
                node.AddProperty("position", ModeHJsonValue.NewInteger(items[i].position));
                node.AddProperty("localId", ModeHJsonValue.NewInteger(local));
                array.Items.Add(node);
            }
            return array;
        }

        /// <summary>
        /// 变量按 key ordinal 升序输出，值取原始字节的十六进制串：
        /// 绕开浮点文本化的表示漂移，同一份数据在任何运行时得到同一段文本。
        /// </summary>
        private static ModeHJsonValue WriteVariables(ItemTreeData.DataEntry entry)
        {
            ModeHJsonValue array = ModeHJsonValue.NewArray();
            if (entry.variables == null) return array;
            List<string> lines = new List<string>();
            for (int i = 0; i < entry.variables.Count; i++)
            {
                CustomData data = entry.variables[i];
                if (data == null) continue;
                string key;
                string raw;
                try
                {
                    key = data.Key != null ? data.Key : string.Empty;
                    raw = ToHex(data.GetRawCopied());
                }
                catch (Exception)
                {
                    // 读不到的变量按空值参与摘要：宁可摘要不匹配，也不静默跳过
                    key = string.Empty;
                    raw = string.Empty;
                }
                lines.Add(key + "=" + raw);
            }
            lines.Sort(StringComparer.Ordinal);
            for (int i = 0; i < lines.Count; i++)
            {
                array.Items.Add(ModeHJsonValue.NewString(lines[i]));
            }
            return array;
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int value = bytes[i];
                chars[i * 2] = HexDigits[value >> 4];
                chars[i * 2 + 1] = HexDigits[value & 0x0F];
            }
            return new string(chars);
        }

        private const string HexDigits = "0123456789abcdef";

        #endregion

        #region 比对

        /// <summary>
        /// 摘要与出现次数双重比对。任一不符即拒绝，绝不因为 TypeID 相同就认为是同一件。
        /// </summary>
        public static bool Matches(
            ModeHItemTreeSnapshotDto expected, Item candidate, int occurrenceCount,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (expected == null || candidate == null)
            {
                failureReasonId = "tree_match_input_missing";
                return false;
            }

            ModeHItemTreeSnapshotDto actual = TryCapture(
                candidate, expected.sourcePosition, occurrenceCount, out failureReasonId);
            if (actual == null) return false;

            if (!string.Equals(actual.semanticTreeDigest, expected.semanticTreeDigest,
                    StringComparison.Ordinal))
            {
                failureReasonId = "tree_digest_mismatch";
                return false;
            }
            if (actual.gameQuality != expected.gameQuality)
            {
                failureReasonId = "tree_quality_mismatch";
                return false;
            }
            if (occurrenceCount != expected.preCount)
            {
                failureReasonId = "tree_occurrence_mismatch";
                return false;
            }
            return true;
        }

        /// <summary>
        /// “同品质”只能指 `gameQuality` 完全相等（§17.5）。
        /// 不允许按赔率评分的封顶把 Q6/Q7/Q8 视为同品质。
        /// </summary>
        public static bool IsSameGameQuality(int a, int b)
        {
            return a == b
                && a >= ModeHConfig.MinGameQuality && a <= ModeHConfig.MaxGameQuality;
        }

        #endregion
    }
}
