// ============================================================================
// ModeHWarehouseStakeJournalStorageBuffer.cs - 押品的仓库溢出缓冲区出口
// ============================================================================
// 为什么单独一个文件：
//   这三个 helper 原本写在 ModeHWarehouseStakeJournal.cs 里，但那个文件已经贴到
//   1200 行预算（LargeFileBudgetGuard）。按仓库既有先例原样提取成同一 partial 类的
//   第二个文件——Config/ConfigModConfigKeys.cs 当初就是为同一个预算从 Config.cs 拆出来的，
//   行为逐字不变。
//
// 为什么必须留在 journal 这个类里（而不是挪到 bridge）：
//   ModeHStakeJournalGuard 的 check_bridge 明令禁止 bridge 出现 PlayerStorage.Push /
//   IncomingItemBuffer——bridge 是「每一步都读回核对」的原语层，而 Push 是
//   「序列化进缓冲区并销毁原实例」的单向流程，正是那条禁令针对的形态。
//   journal 是 Mode H 唯一的真实仓库写入者（§22.3），这条出口属于它。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    internal static partial class ModeHWarehouseStakeJournal
    {
        #region 仓库溢出缓冲区出口

        internal static bool RefreshAssetCache(out string error)
        {
            error = null;
            if (_active == null) return true;
            if (!CollectAssetSnapshot(_active, out error)) return false;
            return ModeHStakeJournalPersistence.StageWrite(_active, out error);
        }

        internal static bool CollectAssetSnapshot(ModeHStakeJournalDto journal, out string error)
        {
            error = null;
            if (journal == null) { error = "journal_missing"; return false; }
            if (!ModeHInventoryPersistenceBridge.CollectSnapshot(journal.slotId, out error)) return false;
            try
            {
                if (PlayerStorageBuffer.Instance != null) PlayerStorageBuffer.SaveBuffer();
                return true;
            }
            catch (Exception e) { error = "buffer_collect_failed:" + e.GetType().Name; return false; }
        }

        /// <summary>
        /// 把无法放回仓库的托管物交给官方溢出缓冲区（玩家之后在仓库码头 StorageDock 取回）。
        ///
        /// **为什么必须有这条出路**：`_escrowItems` 是纯内存 List，`LoadPersisted` 与
        /// `ResetStaticCaches` 都会无条件清空它。仓库满时若只 `return false` 保持 pending，
        /// 玩家一旦退出游戏 / 切槽 / 删档，真实装备就永久消失——journal 里只剩摘要证据，
        /// 没有任何路径能把物品造回来（`ModeHRewardItemPool.TryInstantiate` 只认 typeId）。
        /// 官方自己的任务奖励发放走的就是这条缓冲区。
        ///
        /// 语义要点（对照官方 `PlayerStorage.Push`）：
        /// - `toBufferDirectly: true` 完全不碰 `PlayerStorage.Inventory`，也不做 stack 合并，
        ///   正是押品需要的语义——不会把玩家押进来的枪与仓库里的同型枪合并掉；
        /// - 调用成功后 item 已被 `Detach()` + `DestroyTree()`，**引用即死**，调用方必须同步
        ///   把它移出 `_escrowItems`，此后不得再 `AddAt` 或 `DestroyTree`；
        /// - `Push` 返回 void 不报成败，故照 `ModeGRewardTransaction` 的做法做**归属后验**：
        ///   比对缓冲区计数是否增长，增长即视为已交付，避免异常路径上重复写入。
        ///
        /// 顺序陷阱：`PlayerStorageBuffer.Awake()` 里的 `LoadBuffer()` 会**先清空**静态 list，
        /// 在它跑之前写入会被无声抹掉（与 `_escrowItems` 现在的问题同构），故先门控 Instance。
        /// </summary>
        private static bool TryHandOffToStorageBuffer(Item item, out string failureReasonId)
        {
            failureReasonId = null;
            if (item == null) return true;

            if (PlayerStorageBuffer.Instance == null)
            {
                failureReasonId = "buffer_not_initialized";
                return false;
            }

            List<ItemStatsSystem.Data.ItemTreeData> buffer = null;
            int countBefore = -1;
            try
            {
                buffer = PlayerStorage.IncomingItemBuffer;
                countBefore = buffer != null ? buffer.Count : -1;
            }
            catch (Exception e)
            {
                failureReasonId = "buffer_probe_exception:" + e.GetType().Name;
                return false;
            }

            try
            {
                PlayerStorage.Push(item, true);
                return true;
            }
            catch (Exception pushEx)
            {
                // Push 内部顺序是 通知 -> Buffer.Add -> Detach -> DestroyTree -> 回调。
                // 异常可能发生在 Buffer.Add 之后，此时物品其实已经交付，重复写入会变成复制。
                bool accepted = false;
                try
                {
                    accepted = buffer != null && countBefore >= 0 && buffer.Count > countBefore;
                }
                catch (Exception probeEx)
                {
                    ModBehaviour.DevLog(
                        "[ModeH] [WARNING] 复核缓冲区计数失败，按未交付处理: " + probeEx.Message);
                }
                if (accepted) return true;

                ModBehaviour.DevLog(
                    "[ModeH] [WARNING] 押品送入仓库缓冲区失败，改为直写: " + pushEx.Message);
            }

            // 真失败：直接写缓冲区表并销毁实例，形态照 ZombieModeInventoryTransfer。
            try
            {
                ItemStatsSystem.Data.ItemTreeData data =
                    ItemStatsSystem.Data.ItemTreeData.FromItem(item);
                if (data == null)
                {
                    failureReasonId = "buffer_serialize_null";
                    return false;
                }
                PlayerStorageBuffer.Buffer.Add(data);
                try
                {
                    item.DestroyTree();
                }
                catch (Exception destroyEx)
                {
                    // 已经写进缓冲区，销毁失败不影响交付，但要留痕。
                    ModBehaviour.DevLog(
                        "[ModeH] [WARNING] 押品实例销毁失败（已入缓冲区）: " + destroyEx.Message);
                }
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "buffer_add_exception:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 缓冲区写入后立即落盘。不手动落盘就要等下一次自动存档，中途退出即丢失。
        /// </summary>
        private static void FlushStorageBuffer()
        {
            try
            {
                PlayerStorageBuffer.SaveBuffer();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] [WARNING] 仓库缓冲区落盘失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清空内存 escrow 之前，把仍在托管的实物交给官方溢出缓冲区。
        /// 直接 `Clear()` 会让真实装备随引用一起消失——切槽、重启、删档都会走到这里。
        /// </summary>
        /// <param name="allowBufferHandoff">
        /// 是否允许把托管物交给官方溢出缓冲区。
        ///
        /// **换槽时必须传 false**：`ModeHProfilePersistence.HandleSetFile` 调 `LoadPersisted`
        /// 的时候 `PlayerStorage` 已经指向新槽（同一函数紧接着就读新槽），此时 Push
        /// 等于把旧档的装备搬进新档——那比静默丢失更糟，会变成跨档搬运手段。
        /// 旧槽的押品由该槽自己的 journal 记录，重新载入那个槽时经恢复壳处置。
        /// </param>
        private static void DrainEscrowToStorageBuffer(string contextId, bool allowBufferHandoff)
        {
            if (_escrowItems.Count == 0) return;
            allowBufferHandoff = allowBufferHandoff && _active != null
                && _active.slotId == Saves.SavesSystem.CurrentSlot;

            bool buffered = false;
            for (int i = _escrowItems.Count - 1; i >= 0; i--)
            {
                Item item = _escrowItems[i];
                _escrowItems.RemoveAt(i);
                if (item == null) continue;

                if (!allowBufferHandoff)
                {
                    ModBehaviour.CriticalLog(
                        "[ModeH] [ERROR] " + contextId
                        + " 时仍有托管押品；跨槽不做物理返还，已记入 journal 交恢复流程");
                    continue;
                }

                int escrowIndex = FindUnsettledEscrowIndex(item);
                string reason;
                if (TryHandOffToStorageBuffer(item, out reason))
                {
                    if (escrowIndex >= 0) AppendReceipt("escrow_return", escrowIndex, string.Empty, ModeHStakeReceiptStatus.Verified);
                    buffered = true;
                    continue;
                }
                ModBehaviour.CriticalLog(
                    "[ModeH] [ERROR] " + contextId
                    + " 时托管押品无法送入仓库缓冲区，物品可能丢失: " + (reason ?? "unknown"));
            }
            if (buffered)
            {
                string error;
                if (!PersistAssetProgress(out error)) ModBehaviour.CriticalLog("[ModeH] cleanup 返还待落盘: " + error);
            }
        }

        #endregion

        #region 托管表记账

        /// <summary>
        /// 整批逆序放回。放不回原仓库的项先送官方溢出缓冲区，再转人工介入。
        /// 旧写法把它们留在纯内存 `_escrowItems`，重启即永久丢失——那是第三处滞留点。
        /// </summary>
        private static void RollbackDetached(List<Item> removed)
        {
            bool buffered = false;
            for (int i = removed.Count - 1; i >= 0; i--)
            {
                int position = ModeHInventoryPersistenceBridge.FindConfirmedEmptyPosition();
                string reason;
                if (position < 0
                    || !ModeHInventoryPersistenceBridge.TryAddAtEmpty(removed[i], position, out reason))
                {
                    string bufferReason;
                    if (TryHandOffToStorageBuffer(removed[i], out bufferReason))
                    {
                        buffered = true;
                    }
                    else
                    {
                        _escrowItems.Add(removed[i]);
                    }
                    EnterManualIntervention("escrow_rollback_failed");
                }
            }
            if (buffered) FlushStorageBuffer();
            // 回滚实物后撤销移除凭据，下一次重试/官方收集必须写入同一回滚版本。
            if (_escrowItems.Count == 0)
            {
                _active.receipts.RemoveAll(r => r.operationId != null && r.operationId.Contains("|escrow_remove|"));
                _active.inventoryPostDigest = _active.inventoryPreDigest;
                string rollbackError;
                TryRestampDigest(out rollbackError);
                ModeHStakeJournalPersistence.StageWrite(_active, out rollbackError);
            }
        }

        private static bool PersistAssetProgress(out string error)
        {
            bool modern = _active.escrowItems.Count == 0 || ModeHItemTreeNormalizer.HasRestoreData(_active.escrowItems[0]);
            if (!ModeHInventoryPersistenceBridge.TryComputeInventoryDigest(out _active.inventoryPostDigest, out error, modern)) return false;
            return ModeHSaveFlushCoordinator.RequestStakeJournalWrite(_active, out error);
        }

        /// <summary>只恢复已 durable 移除、未返还/没收的原始索引；整批预检后交官方缓冲区。</summary>
        private static bool TryReturnPersistedEscrow(out string error)
        {
            error = null;
            if (_active == null || _active.slotId != Saves.SavesSystem.CurrentSlot
                || ModeHStakeJournalPersistence.IsWriteBarrier || _escrowItems.Count != 0
                || PlayerStorageBuffer.Instance == null)
            {
                error = "restore_slot_or_owner_unavailable";
                return false;
            }
            ModeHStakePhase phase = ToPhase(_active.phase);
            if (phase != ModeHStakePhase.EscrowRemovedDurable && phase != ModeHStakePhase.MatchLocked
                && phase != ModeHStakePhase.ResultCommitted && phase != ModeHStakePhase.AbortReturnCommitted
                && phase != ModeHStakePhase.SettlementPending)
            {
                error = "restore_phase_unproven";
                return false;
            }
            bool modern = _active.escrowItems.Count > 0 && ModeHItemTreeNormalizer.HasRestoreData(_active.escrowItems[0]);
            string current;
            if (!ModeHInventoryPersistenceBridge.TryComputeInventoryDigest(out current, out error, modern)
                || current != _active.inventoryPostDigest)
            {
                error = "restore_postimage_mismatch";
                return false;
            }
            List<ItemStatsSystem.Data.ItemTreeData> trees = new List<ItemStatsSystem.Data.ItemTreeData>();
            List<int> indices = new List<int>();
            for (int i = 0; i < _active.escrowItems.Count; i++)
            {
                if (!HasAppliedReceipt("escrow_remove", i) || HasAppliedReceipt("escrow_return", i)
                    || HasAppliedReceipt("planned_loss", i)) continue;
                var tree = ModeHItemTreeNormalizer.TryRestore(_active.escrowItems[i], out error);
                if (tree == null) return false;
                trees.Add(tree);
                indices.Add(i);
            }
            for (int i = 0; i < trees.Count; i++)
            {
                PlayerStorageBuffer.Buffer.Add(trees[i]);
                AppendReceipt("escrow_return", indices[i], _active.escrowItems[indices[i]].semanticTreeDigest,
                    ModeHStakeReceiptStatus.Verified);
            }
            if (PersistAssetProgress(out error)) return true;
            error = "restore_write_pending:" + error;
            return false;
        }

        private static int FindUnsettledEscrowIndex(Item item)
        {
            for (int i = 0; i < _active.escrowItems.Count; i++)
            {
                if (HasAppliedReceipt("escrow_return", i) || HasAppliedReceipt("planned_loss", i)) continue;
                string error;
                ModeHItemTreeSnapshotDto snapshot = _active.escrowItems[i];
                if (ModeHItemTreeNormalizer.Matches(snapshot, item, snapshot.preCount, out error)) return i;
            }
            return -1;
        }

        /// <summary>
        /// 按语义摘要从托管表里取走一件（没收物）并销毁。找不到就什么都不做。
        /// </summary>
        private static void RemoveEscrowByDigest(string digest)
        {
            if (string.IsNullOrEmpty(digest)) return;
            for (int i = _escrowItems.Count - 1; i >= 0; i--)
            {
                Item item = _escrowItems[i];
                if (item == null)
                {
                    _escrowItems.RemoveAt(i);
                    continue;
                }
                string reason;
                ModeHItemTreeSnapshotDto snapshot =
                    ModeHItemTreeNormalizer.TryCapture(item, 0, 1, out reason);
                if (snapshot == null) continue;
                if (!string.Equals(snapshot.semanticTreeDigest, digest, StringComparison.Ordinal))
                {
                    continue;
                }
                try { item.DestroyTree(); }
                catch (Exception)
                {
                    // 没收物销毁失败不阻断结算：receipt 已记录，恢复面板可复核
                }
                _escrowItems.RemoveAt(i);
                return;
            }
        }

        /// <summary>
        /// journal 记载的「已脱离仓库且尚未了结」的件数
        /// = escrow_remove − escrow_return − planned_loss。
        ///
        /// 这是返还路径的对账基准：内存 `_escrowItems` 是纯内存 List，重启、切槽、
        /// 崩溃都会让它变空，而 journal 里的 receipt 是落过盘的事实。两者对不上就说明
        /// 实物拿不出来了，绝不能把「什么都没还」记成 RefundedTerminal。
        /// </summary>
        private static int CountOutstandingEscrow()
        {
            if (_active == null || _active.receipts == null) return 0;

            int outstanding = 0;
            for (int i = 0; i < _active.escrowItems.Count; i++)
                if (HasAppliedReceipt("escrow_remove", i) && !HasAppliedReceipt("escrow_return", i)
                    && !HasAppliedReceipt("planned_loss", i)) outstanding++;
            return outstanding;
        }

        #endregion
    }
}
