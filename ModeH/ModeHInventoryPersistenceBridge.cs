using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Mode H 仓库持久化桥（设计提案 §22.3、§25.1）。
    ///
    /// **只由 `ModeHWarehouseStakeJournal` 调用**：它是 Mode H 唯一的真实仓库写入者，
    /// 本类只是它的原语层，自身不决定任何事务语义。
    ///
    /// 冻结契约：
    /// - 等待当前 slot 的 `PlayerStorage` 初始化完成后才允许读写；
    /// - `AddAt` **不是**安全恢复原语：调用前必须在同一 owner/slot generation 内
    ///   确认目标格为空，调用后核对返回树摘要与最终 inventory digest；
    ///   格子被占用时改用已确认空位或保持 pending，**绝不覆盖已有物品**；
    /// - 禁止调用 Courier/Deposit 的“先删源再回调”流程；
    /// - 不把 `PlayerStorage` 的内存缓存当作提交成功依据——每一步都读回核对。
    /// </summary>
    internal static class ModeHInventoryPersistenceBridge
    {
        #region 就绪与读取

        /// <summary>当前 slot 的 `PlayerStorage` 是否已初始化并可访问。</summary>
        public static bool IsStorageReady(out string failureReasonId)
        {
            failureReasonId = null;
            try
            {
                if (PlayerStorage.Instance == null)
                {
                    failureReasonId = "storage_instance_missing";
                    return false;
                }
                if (PlayerStorage.Loading)
                {
                    failureReasonId = "storage_loading";
                    return false;
                }
                if (!PlayerStorage.Instance.HasInitialized())
                {
                    failureReasonId = "storage_not_initialized";
                    return false;
                }
                if (PlayerStorage.Inventory == null)
                {
                    failureReasonId = "storage_inventory_missing";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "storage_probe_exception:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>取当前仓库 inventory。未就绪返回 null。</summary>
        public static Inventory TryGetInventory(out string failureReasonId)
        {
            if (!IsStorageReady(out failureReasonId)) return null;
            try { return PlayerStorage.Inventory; }
            catch (Exception e)
            {
                failureReasonId = "storage_inventory_exception:" + e.GetType().Name;
                return null;
            }
        }

        /// <summary>
        /// 计算整份仓库的规范摘要。这是 journal 的 pre/post image 唯一来源，
        /// 每次写屏障前后都要重算并读回核对。
        /// </summary>
        public static bool TryComputeInventoryDigest(out string digest, out string failureReasonId)
        {
            digest = null;
            Inventory inventory = TryGetInventory(out failureReasonId);
            if (inventory == null) return false;

            ModeHJsonValue root = ModeHJsonValue.NewObject();
            ModeHJsonValue slots = ModeHJsonValue.NewArray();
            try
            {
                List<Item> content = inventory.Content;
                if (content == null)
                {
                    failureReasonId = "storage_content_missing";
                    return false;
                }
                for (int i = 0; i < content.Count; i++)
                {
                    Item item = content[i];
                    if (item == null) continue;
                    string reason;
                    ModeHItemTreeSnapshotDto snapshot =
                        ModeHItemTreeNormalizer.TryCapture(item, i, 1, out reason);
                    if (snapshot == null)
                    {
                        failureReasonId = "storage_digest_item_failed:" + reason;
                        return false;
                    }
                    ModeHJsonValue node = ModeHJsonValue.NewObject();
                    node.AddProperty("position", ModeHJsonValue.NewInteger(i));
                    node.AddProperty("digest", ModeHJsonValue.NewString(snapshot.semanticTreeDigest));
                    slots.Items.Add(node);
                }
            }
            catch (Exception e)
            {
                failureReasonId = "storage_digest_exception:" + e.GetType().Name;
                return false;
            }
            root.AddProperty("slots", slots);

            string error;
            if (!ModeHCanonicalDigest.TryComputeValueDigest(root, out digest, out error))
            {
                failureReasonId = "storage_digest_failed:" + error;
                return false;
            }
            return true;
        }

        /// <summary>统计某个语义摘要在仓库中的出现次数（防“同 TypeID 顶替”）。</summary>
        public static int CountOccurrences(string semanticTreeDigest)
        {
            string reason;
            Inventory inventory = TryGetInventory(out reason);
            if (inventory == null || string.IsNullOrEmpty(semanticTreeDigest)) return 0;
            int count = 0;
            try
            {
                List<Item> content = inventory.Content;
                if (content == null) return 0;
                for (int i = 0; i < content.Count; i++)
                {
                    Item item = content[i];
                    if (item == null) continue;
                    ModeHItemTreeSnapshotDto snapshot =
                        ModeHItemTreeNormalizer.TryCapture(item, i, 1, out reason);
                    if (snapshot == null) continue;
                    if (string.Equals(snapshot.semanticTreeDigest, semanticTreeDigest,
                            StringComparison.Ordinal))
                    {
                        count++;
                    }
                }
            }
            catch (Exception)
            {
                // 读取失败时返回 0，由调用方按出现次数不符 fail-closed
                return 0;
            }
            return count;
        }

        /// <summary>
        /// 押品选择器用的只读查询：列出前 `maxPositions` 格里**非空**的槽位号。
        ///
        /// 放在本类而不是调用方，是因为 ModeHIsolationGuard 冻结了「只有 journal 与
        /// 本桥可以引用 Inventory / PlayerStorage」。选择器只需要槽位号与展示名，
        /// 不需要（也不应该）拿到 Item 引用——Item 引用跨场景就失效了。
        ///
        /// 只扫窗口内的格子：本方法不做树规范化，因此本身很便宜，
        /// 但调用方随后对每格做的 TryCapture 不便宜，窗口由调用方决定。
        /// </summary>
        public static List<int> ListOccupiedPositions(int maxPositions)
        {
            List<int> result = new List<int>();
            if (maxPositions <= 0) return result;

            string reason;
            Inventory inventory = TryGetInventory(out reason);
            if (inventory == null) return result;

            try
            {
                int capacity = inventory.Capacity;
                int limit = capacity < maxPositions ? capacity : maxPositions;
                for (int i = 0; i < limit; i++)
                {
                    if (inventory.GetItemAt(i) == null) continue;
                    result.Add(i);
                }
            }
            catch (Exception)
            {
                // 读取失败按"没有可押物品"处理，不抛给 UI
            }
            return result;
        }

        /// <summary>
        /// 一格物品的展示名与品质（押品选择器按钮文案用）。
        /// 格子为空或读不出时返回 false，调用方回落槽位号。
        /// </summary>
        public static bool TryDescribePosition(int position, out string displayName, out int quality)
        {
            displayName = null;
            quality = 0;

            string reason;
            Inventory inventory = TryGetInventory(out reason);
            if (inventory == null) return false;

            try
            {
                Item item = inventory.GetItemAt(position);
                if (item == null) return false;

                try { displayName = item.DisplayName; }
                catch (Exception)
                {
                    displayName = null;
                }
                try { quality = item.Quality; }
                catch (Exception)
                {
                    quality = 0;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 为一个槽位构造规范化快照并补齐真实出现次数（押品选择与锁盘用）。
        /// 空格 / 读不出品质 / 出现次数解析失败一律返回 null（fail-closed：
        /// 无法证明"同品质返还"的物品不允许押）。
        /// </summary>
        public static ModeHItemTreeSnapshotDto TryCaptureAt(int position, out string failureReasonId)
        {
            failureReasonId = null;
            Inventory inventory = TryGetInventory(out failureReasonId);
            if (inventory == null) return null;

            Item item;
            try { item = inventory.GetItemAt(position); }
            catch (Exception e)
            {
                failureReasonId = "stake_slot_read_failed:" + e.GetType().Name;
                return null;
            }
            if (item == null)
            {
                failureReasonId = "stake_slot_empty";
                return null;
            }

            ModeHItemTreeSnapshotDto snapshot =
                ModeHItemTreeNormalizer.TryCapture(item, position, 1, out failureReasonId);
            if (snapshot == null) return null;

            // preCount 必须是真实出现次数：TryDetachAt 会拿它做前置核对
            snapshot.preCount = CountOccurrences(snapshot.semanticTreeDigest);
            if (snapshot.preCount <= 0)
            {
                failureReasonId = "stake_occurrence_unresolved";
                return null;
            }
            return snapshot;
        }

        #endregion

        #region 写入原语

        /// <summary>
        /// 按位置摘取一个根物品（escrow 移入）。取出前核对摘要与出现次数；
        /// 任一不符即拒绝，不做“差不多就行”的近似匹配。
        /// </summary>
        public static Item TryDetachAt(
            ModeHItemTreeSnapshotDto expected, out string failureReasonId)
        {
            failureReasonId = null;
            Inventory inventory = TryGetInventory(out failureReasonId);
            if (inventory == null || expected == null)
            {
                if (expected == null) failureReasonId = "detach_expected_missing";
                return null;
            }

            Item candidate;
            try { candidate = inventory.GetItemAt(expected.sourcePosition); }
            catch (Exception e)
            {
                failureReasonId = "detach_read_exception:" + e.GetType().Name;
                return null;
            }
            if (candidate == null)
            {
                failureReasonId = "detach_slot_empty";
                return null;
            }

            int occurrences = CountOccurrences(expected.semanticTreeDigest);
            if (!ModeHItemTreeNormalizer.Matches(expected, candidate, occurrences, out failureReasonId))
            {
                return null;
            }

            Item removed;
            bool ok;
            try { ok = inventory.RemoveAt(expected.sourcePosition, out removed); }
            catch (Exception e)
            {
                failureReasonId = "detach_remove_exception:" + e.GetType().Name;
                return null;
            }
            if (!ok || removed == null)
            {
                failureReasonId = "detach_remove_failed";
                return null;
            }
            return removed;
        }

        /// <summary>
        /// 把一个根物品放回指定空位（escrow 返还 / 奖励发放）。
        /// 调用前确认目标格为空；被占用时**不覆盖**，改由调用方取已确认空位或保持 pending。
        /// </summary>
        public static bool TryAddAtEmpty(
            Item item, int targetPosition, out string failureReasonId)
        {
            failureReasonId = null;
            Inventory inventory = TryGetInventory(out failureReasonId);
            if (inventory == null) return false;
            if (item == null)
            {
                failureReasonId = "add_item_missing";
                return false;
            }

            try
            {
                if (targetPosition < 0 || targetPosition >= inventory.Capacity)
                {
                    failureReasonId = "add_position_out_of_range";
                    return false;
                }
                if (inventory.GetItemAt(targetPosition) != null)
                {
                    // 绝不覆盖已有物品
                    failureReasonId = "add_position_occupied";
                    return false;
                }
            }
            catch (Exception e)
            {
                failureReasonId = "add_probe_exception:" + e.GetType().Name;
                return false;
            }

            bool ok;
            try { ok = inventory.AddAt(item, targetPosition); }
            catch (Exception e)
            {
                failureReasonId = "add_exception:" + e.GetType().Name;
                return false;
            }
            if (!ok)
            {
                failureReasonId = "add_rejected";
                return false;
            }

            // 读回核对：AddAt 返回 true 不等于落位成功
            try
            {
                if (!ReferenceEquals(inventory.GetItemAt(targetPosition), item))
                {
                    failureReasonId = "add_readback_mismatch";
                    return false;
                }
            }
            catch (Exception e)
            {
                failureReasonId = "add_readback_exception:" + e.GetType().Name;
                return false;
            }
            return true;
        }

        /// <summary>取第一个确认为空的位置；没有空位返回 -1（由调用方保持 pending）。</summary>
        public static int FindConfirmedEmptyPosition()
        {
            string reason;
            Inventory inventory = TryGetInventory(out reason);
            if (inventory == null) return -1;
            try
            {
                int capacity = inventory.Capacity;
                for (int i = 0; i < capacity; i++)
                {
                    if (inventory.GetItemAt(i) == null) return i;
                }
            }
            catch (Exception)
            {
                // 读取失败按“没有可用空位”处理，保持 pending
                return -1;
            }
            return -1;
        }

        #endregion
    }
}
