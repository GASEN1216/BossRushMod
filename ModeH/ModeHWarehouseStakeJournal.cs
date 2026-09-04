using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Mode H 真实仓库抵押 journal（设计提案 §22、§25.1）。
    ///
    /// **Mode H 唯一的真实仓库写入者。** `ModeHRewardTransaction` 只生成不可变结果计划
    /// 并调用本类的 `CommitResult/Settle`，绝不自己调用库存 API。
    ///
    /// 冻结契约：
    /// - **没有真实资产开关**：不存在 `GatePassed`，也不存在任何可写的启停字段。
    ///   系统唯一会自行禁用真实押品的情况是 `IsSlotConsistent` 为 false，
    ///   它是只读派生结果（取值规则见 §22.1 四条）；
    /// - 单个 slot 只有一个 active journal；下一笔交易必须先确认不存在非终态 journal；
    /// - `phase` 是唯一终态来源，不另设可漂移的 `terminal` 布尔；
    /// - 阶段转换严格单向（§22.2），非法转换一律拒绝；
    /// - 证据不一致时进入 `ManualIntervention`，绝不自动猜测、绝不静默删除 journal；
    /// - 每次移除/返还/奖励都生成 receipt，与 inventory/journal 在同一写屏障提交；
    /// - 禁止 Courier/Deposit 的“先删源再回调”，禁止把 `Item.GetInstanceID`/TypeID/
    ///   `LockIndex` 当作所有权证明。
    /// </summary>
    internal static partial class ModeHWarehouseStakeJournal
    {
        #region 状态

        private static ModeHStakeJournalDto _active;
        private static readonly List<Item> _escrowItems = new List<Item>();
        private static string _lastError;
        private static bool _slotConsistent;
        private static string _slotInconsistentReasonId;

        #endregion

        #region 只读

        /// <summary>当前 active journal（终态或不存在时为 null）。</summary>
        public static ModeHStakeJournalDto Active { get { return _active; } }

        /// <summary>最后一次失败原因。</summary>
        public static string LastError { get { return _lastError; } }

        /// <summary>
        /// §22.1 冻结的只读派生结果。**不是可写开关**：
        /// 为 false 时禁用押品选择器并原位显示原因，赛季照常用虚拟筹码跑完整闭环。
        /// </summary>
        public static bool IsSlotConsistent { get { return _slotConsistent; } }

        /// <summary>`IsSlotConsistent=false` 的具体原因（原位展示用）。</summary>
        public static string SlotInconsistentReasonId
        {
            get { return _slotInconsistentReasonId != null ? _slotInconsistentReasonId : string.Empty; }
        }

        /// <summary>当前托管中的 escrow 根物品数量（UI 的“临时托管”显示用）。</summary>
        public static int EscrowCount { get { return _escrowItems.Count; } }

        #endregion

        #region 槽位一致性

        /// <summary>
        /// 按 §22.1 的四条取值规则重算 `IsSlotConsistent`。
        /// 只读：不写 journal、不动库存。
        /// </summary>
        public static void RecomputeSlotConsistency(ModeHStakeJournalDto persisted)
        {
            string reason;
            if (!ModeHInventoryPersistenceBridge.IsStorageReady(out reason))
            {
                // 第三条：路径 / header / 摘要 / slot 身份无法判定
                _slotConsistent = false;
                _slotInconsistentReasonId = "slot_storage_unavailable:" + reason;
                ModeHRuntimeGates.SetExternalAssetRiskBlocked(true, _slotInconsistentReasonId);
                return;
            }

            if (persisted == null)
            {
                // 第一条：journal 不存在
                _slotConsistent = true;
                _slotInconsistentReasonId = null;
                ModeHRuntimeGates.SetExternalAssetRiskBlocked(false, null);
                return;
            }

            ModeHStakePhase phase = ToPhase(persisted.phase);
            if (phase == ModeHStakePhase.ManualIntervention)
            {
                // 第四条：任一记录处于 ManualIntervention
                _slotConsistent = false;
                _slotInconsistentReasonId = "slot_manual_intervention";
                ModeHRuntimeGates.SetExternalAssetRiskBlocked(true, _slotInconsistentReasonId);
                return;
            }
            if (IsTerminalPhase(phase))
            {
                // 第一条：处于终态
                _slotConsistent = true;
                _slotInconsistentReasonId = null;
                ModeHRuntimeGates.SetExternalAssetRiskBlocked(false, null);
                return;
            }
            if (phase == ModeHStakePhase.Unknown)
            {
                _slotConsistent = false;
                _slotInconsistentReasonId = "slot_phase_unknown";
                ModeHRuntimeGates.SetExternalAssetRiskBlocked(true, _slotInconsistentReasonId);
                return;
            }

            // 第二条：存在非终态 active journal -> 打开 §22.4 恢复流程
            _slotConsistent = false;
            _slotInconsistentReasonId = "slot_active_journal:" + phase;
            ModeHRuntimeGates.SetExternalAssetRiskBlocked(true, _slotInconsistentReasonId);
        }

        /// <summary>三个终态。`phase` 是唯一终态来源。</summary>
        public static bool IsTerminalPhase(ModeHStakePhase phase)
        {
            return phase == ModeHStakePhase.Terminal
                || phase == ModeHStakePhase.CancelledTerminal
                || phase == ModeHStakePhase.RefundedTerminal;
        }

        private static ModeHStakePhase ToPhase(int raw)
        {
            if (!Enum.IsDefined(typeof(ModeHStakePhase), raw)) return ModeHStakePhase.Unknown;
            return (ModeHStakePhase)raw;
        }

        #endregion

        #region 阶段机

        /// <summary>§22.2 冻结的单向转换表。未列出的转换一律非法。</summary>
        public static bool IsLegalTransition(ModeHStakePhase from, ModeHStakePhase to)
        {
            if (to == ModeHStakePhase.ManualIntervention)
            {
                // 任一非终态且证据不一致都可以进入人工介入
                return !IsTerminalPhase(from);
            }
            switch (from)
            {
                case ModeHStakePhase.None:
                    return to == ModeHStakePhase.Prepared;
                case ModeHStakePhase.Prepared:
                    return to == ModeHStakePhase.EscrowSnapshotDurable
                        || to == ModeHStakePhase.CancelledTerminal;
                case ModeHStakePhase.EscrowSnapshotDurable:
                    return to == ModeHStakePhase.EscrowRemovedDurable
                        || to == ModeHStakePhase.CancelledTerminal;
                case ModeHStakePhase.EscrowRemovedDurable:
                    return to == ModeHStakePhase.MatchLocked
                        || to == ModeHStakePhase.AbortReturnCommitted;
                case ModeHStakePhase.MatchLocked:
                    return to == ModeHStakePhase.ResultCommitted
                        || to == ModeHStakePhase.AbortReturnCommitted;
                case ModeHStakePhase.ResultCommitted:
                    return to == ModeHStakePhase.SettlementPending;
                case ModeHStakePhase.AbortReturnCommitted:
                    return to == ModeHStakePhase.SettlementPending;
                case ModeHStakePhase.SettlementPending:
                    // 终态由不可变的 settlementKind 决定，禁止在两种结算之间切换
                    return to == ModeHStakePhase.Terminal || to == ModeHStakePhase.RefundedTerminal;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 推进阶段。非法转换、settlementKind 漂移与终态回环一律拒绝。
        /// 每次推进都递增 `phaseSequence`。
        /// </summary>
        public static bool TryAdvancePhase(
            ModeHStakePhase next, ModeHSettlementKind settlementKind, out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }

            ModeHStakePhase current = ToPhase(_active.phase);
            if (!IsLegalTransition(current, next))
            {
                failureReasonId = "journal_illegal_transition:" + current + "->" + next;
                return false;
            }

            // settlementKind 在进入 committed phase 后必填且不可改写
            if (next == ModeHStakePhase.ResultCommitted && settlementKind != ModeHSettlementKind.MatchResult)
            {
                failureReasonId = "journal_settlement_kind_mismatch";
                return false;
            }
            if (next == ModeHStakePhase.AbortReturnCommitted
                && settlementKind != ModeHSettlementKind.AbortReturn)
            {
                failureReasonId = "journal_settlement_kind_mismatch";
                return false;
            }
            ModeHSettlementKind existing = ToSettlementKind(_active.settlementKind);
            if (existing != ModeHSettlementKind.None && existing != ModeHSettlementKind.Unknown
                && settlementKind != ModeHSettlementKind.None && existing != settlementKind)
            {
                failureReasonId = "journal_settlement_kind_drift";
                return false;
            }
            if (next == ModeHStakePhase.Terminal && existing != ModeHSettlementKind.MatchResult)
            {
                failureReasonId = "journal_terminal_requires_match_result";
                return false;
            }
            if (next == ModeHStakePhase.RefundedTerminal && existing != ModeHSettlementKind.AbortReturn)
            {
                failureReasonId = "journal_refunded_requires_abort_return";
                return false;
            }

            int previousPhase = _active.phase;
            int previousSequence = _active.phaseSequence;
            int previousKind = _active.settlementKind;

            _active.phase = (int)next;
            _active.phaseSequence = _active.phaseSequence + 1;
            if (settlementKind != ModeHSettlementKind.None)
            {
                _active.settlementKind = (int)settlementKind;
            }
            if (!TryRestampDigest(out failureReasonId))
            {
                _active.phase = previousPhase;
                _active.phaseSequence = previousSequence;
                _active.settlementKind = previousKind;
                return false;
            }

            // 落盘是阶段推进的一部分，不是事后补登记。三个 *Durable 阶段的名字
            // 就是这个意思：内存说"已持久化"而磁盘上没有，等于崩溃后无法证明
            // 玩家那件装备去了哪。写失败必须整体回滚阶段，让调用方看到失败并停在原地
            // ——绝不能出现"物品已脱离仓库但 journal 还停在上一阶段"的错位。
            string writeError;
            if (!ModeHSaveFlushCoordinator.RequestStakeJournalWrite(_active, out writeError))
            {
                _active.phase = previousPhase;
                _active.phaseSequence = previousSequence;
                _active.settlementKind = previousKind;
                string restampError;
                TryRestampDigest(out restampError);
                failureReasonId = "journal_persist_failed:"
                    + (writeError != null ? writeError : "unknown");
                _lastError = failureReasonId;
                return false;
            }

            // 终态落盘后重算一致性：三个终态都表示"这个槽没有未结算事务"，
            // IsSlotConsistent 必须转回 true，否则玩家打完一场之后押品被永久禁用。
            if (IsTerminalPhase(next))
            {
                RecomputeSlotConsistency(_active);
            }
            return true;
        }

        /// <summary>证据不一致的统一出口。绝不自动猜测、绝不修成取消。</summary>
        public static void EnterManualIntervention(string reasonId)
        {
            if (_active == null) return;
            _active.phase = (int)ModeHStakePhase.ManualIntervention;
            _active.phaseSequence = _active.phaseSequence + 1;
            string error;
            TryRestampDigest(out error);
            // 人工介入必须落盘，否则重启后玩家看到的是上一个正常阶段，
            // 而物品早已不在仓库里——那正是最需要留证据的情形。
            // 这里**不因写失败回滚**：内存已经是人工介入态，回滚只会把它藏起来；
            // 写不下去时 store 会进 faulted，恢复面板照样只读展示。
            string writeError;
            if (!ModeHSaveFlushCoordinator.RequestStakeJournalWrite(_active, out writeError))
            {
                ModBehaviour.CriticalLog(
                    "[ModeH] [WARNING] 人工介入状态落盘失败，仅存在于内存: "
                    + (writeError != null ? writeError : "unknown"));
            }
            _slotConsistent = false;
            _slotInconsistentReasonId = reasonId;
            ModeHRuntimeGates.SetExternalAssetRiskBlocked(true, reasonId);
            ModBehaviour.CriticalLog("[ModeH] 押品事务进入人工介入: " + reasonId);
        }

        private static ModeHSettlementKind ToSettlementKind(int raw)
        {
            if (!Enum.IsDefined(typeof(ModeHSettlementKind), raw)) return ModeHSettlementKind.Unknown;
            return (ModeHSettlementKind)raw;
        }

        #endregion

        #region Prepared

        /// <summary>
        /// 创建 `Prepared`：写 txId、runId、matchIndex、slotId、slotGeneration、
        /// 押品与最坏损失候选、品质、赔率、随机域和 inventory pre-digest。
        /// 此阶段 `settlementKind=None`，结果/退款 token 与两个父 operation 都为空。
        /// </summary>
        public static bool TryPrepare(
            string txId,
            string runId,
            int matchIndex,
            int slotId,
            int slotGeneration,
            IList<ModeHItemTreeSnapshotDto> escrowItems,
            IList<ModeHItemTreeSnapshotDto> plannedLosses,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (_active != null && !IsTerminalPhase(ToPhase(_active.phase)))
            {
                failureReasonId = "journal_active_exists";
                return false;
            }
            if (!_slotConsistent)
            {
                failureReasonId = "journal_slot_inconsistent";
                return false;
            }
            if (string.IsNullOrEmpty(txId) || escrowItems == null || escrowItems.Count == 0)
            {
                failureReasonId = "journal_prepare_input_invalid";
                return false;
            }

            string preDigest;
            if (!ModeHInventoryPersistenceBridge.TryComputeInventoryDigest(
                    out preDigest, out failureReasonId))
            {
                return false;
            }

            ModeHStakeJournalDto journal = new ModeHStakeJournalDto();
            journal.schemaVersion = ModeHConfig.CurrentSchemaVersion;
            journal.signatureAlgorithmVersion = ModeHConfig.CurrentSignatureAlgorithmVersion;
            string modSignature, signatureError;
            if (!ModeHCanonicalDigest.TryGetModBuildSignature(out modSignature, out signatureError))
            {
                failureReasonId = "journal_mod_signature_failed:" + signatureError;
                return false;
            }
            journal.modBuildSignature = modSignature;
            journal.txId = txId;
            journal.runId = runId;
            journal.matchIndex = matchIndex;
            journal.slotId = slotId;
            journal.slotGeneration = slotGeneration;
            journal.phase = (int)ModeHStakePhase.Prepared;
            journal.phaseSequence = 1;
            journal.settlementKind = (int)ModeHSettlementKind.None;
            journal.inventoryPreDigest = preDigest;
            journal.inventoryPostDigest = string.Empty;
            journal.escrowItems = new List<ModeHItemTreeSnapshotDto>(escrowItems);
            journal.lossItems = plannedLosses != null
                ? new List<ModeHItemTreeSnapshotDto>(plannedLosses)
                : new List<ModeHItemTreeSnapshotDto>();
            journal.rewardItems = new List<ModeHItemTreeSnapshotDto>();
            journal.receipts = new List<ModeHStakeReceiptDto>();
            journal.resultToken = string.Empty;
            journal.abortReturnToken = string.Empty;

            _active = journal;
            _escrowItems.Clear();
            if (!TryRestampDigest(out failureReasonId))
            {
                _active = null;
                return false;
            }

            // Prepared 是第一个阶段，没有经过 TryAdvancePhase，因此在这里单独落盘。
            // 落不下去就把 journal 整体丢弃：此刻还没动过任何物品，丢弃是安全的，
            // 而留着一个只存在于内存的 journal 会让下一笔交易撞上 journal_active_exists。
            string writeError;
            if (!ModeHSaveFlushCoordinator.RequestStakeJournalWrite(_active, out writeError))
            {
                _active = null;
                failureReasonId = "journal_persist_failed:"
                    + (writeError != null ? writeError : "unknown");
                _lastError = failureReasonId;
                return false;
            }

            // 新 journal 落盘后立刻重算：此时存在非终态 active journal，
            // IsSlotConsistent 必须转 false，防止同槽再开第二笔交易。
            RecomputeSlotConsistency(_active);
            return true;
        }

        #endregion

        #region Escrow

        /// <summary>
        /// `EscrowSnapshotDurable`：escrow 根物品的规范化树、摘要、原槽位和出现次数
        /// 已写入并读回。此阶段仍为 `settlementKind=None`，物品尚未脱离仓库。
        /// </summary>
        public static bool TryCommitEscrowSnapshot(out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }
            for (int i = 0; i < _active.escrowItems.Count; i++)
            {
                ModeHItemTreeSnapshotDto snapshot = _active.escrowItems[i];
                if (snapshot == null || string.IsNullOrEmpty(snapshot.semanticTreeDigest))
                {
                    failureReasonId = "escrow_snapshot_invalid";
                    return false;
                }
                int occurrences =
                    ModeHInventoryPersistenceBridge.CountOccurrences(snapshot.semanticTreeDigest);
                if (occurrences < snapshot.preCount)
                {
                    failureReasonId = "escrow_occurrence_shrunk";
                    return false;
                }
            }
            return TryAdvancePhase(
                ModeHStakePhase.EscrowSnapshotDurable, ModeHSettlementKind.None, out failureReasonId);
        }

        /// <summary>
        /// `EscrowRemovedDurable`：把全部 escrow 根物品从运行时 inventory 脱离，
        /// 写 post-removal digest 并读回。任一项失败就整批逆序放回并保持在上一阶段。
        /// </summary>
        public static bool TryRemoveEscrow(out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }
            if (ToPhase(_active.phase) != ModeHStakePhase.EscrowSnapshotDurable)
            {
                failureReasonId = "escrow_remove_wrong_phase";
                return false;
            }

            List<Item> removed = new List<Item>();
            for (int i = 0; i < _active.escrowItems.Count; i++)
            {
                Item item = ModeHInventoryPersistenceBridge.TryDetachAt(
                    _active.escrowItems[i], out failureReasonId);
                if (item == null)
                {
                    RollbackDetached(removed);
                    return false;
                }
                removed.Add(item);
                _active.escrowItems[i].postCount =
                    ModeHInventoryPersistenceBridge.CountOccurrences(
                        _active.escrowItems[i].semanticTreeDigest);
                AppendReceipt("escrow_remove", i, _active.escrowItems[i].semanticTreeDigest,
                    ModeHStakeReceiptStatus.Applied);
            }

            string postDigest;
            if (!ModeHInventoryPersistenceBridge.TryComputeInventoryDigest(
                    out postDigest, out failureReasonId))
            {
                RollbackDetached(removed);
                return false;
            }
            _active.inventoryPostDigest = postDigest;

            _escrowItems.Clear();
            _escrowItems.AddRange(removed);
            // 阶段推进内含落盘，`SavesSystem.IsSaving` 时必然失败（FlushBatch 的
            // flush_deferred_is_saving），而此刻物品**已经脱离仓库**。TryAdvancePhase
            // 失败会把 phase 回滚到 EscrowSnapshotDurable，若这里不同步把物品放回去，
            // 就会留下「账上说没移除、仓库里却没有」的错位：之后每一条返还路径
            // （TryAbortReturn -> TryCancelWithoutRemoval）都会被 cancel_escrow_still_held
            // 挡死，重启后 LoadPersisted 清空 _escrowItems 更会让它被静默归档成
            // CancelledTerminal，玩家的真实装备就此永久消失。
            // 必须与上面 TryComputeInventoryDigest 失败分支完全对称。
            string advanceError;
            if (!TryAdvancePhase(
                    ModeHStakePhase.EscrowRemovedDurable, ModeHSettlementKind.None, out advanceError))
            {
                failureReasonId = advanceError;
                // 先清空再回滚：RollbackDetached 会把**放不回去**的那些重新塞进
                // _escrowItems 并转人工介入，不清空会让成功放回的项也留在表里。
                _escrowItems.Clear();
                RollbackDetached(removed);
                return false;
            }
            return true;
        }

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
        }

        /// <summary>`MatchLocked`：计划、赔率、装备快照与 journal 全部不可变。</summary>
        public static bool TryLockMatch(out string failureReasonId)
        {
            return TryAdvancePhase(
                ModeHStakePhase.MatchLocked, ModeHSettlementKind.None, out failureReasonId);
        }

        #endregion

        #region 结算

        /// <summary>
        /// `ResultCommitted`：写入唯一 result token 与父 reward operation，
        /// 冻结不可变的返还/没收/奖励计划。**不执行第二次移除。**
        /// 只由 `ModeHRewardTransaction` 调用。
        /// </summary>
        public static bool CommitResult(
            string resultToken, ModeHRewardOperationDto rewardOperation, out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }
            if (string.IsNullOrEmpty(resultToken) || rewardOperation == null)
            {
                failureReasonId = "commit_result_input_invalid";
                return false;
            }
            if (!string.IsNullOrEmpty(_active.resultToken))
            {
                failureReasonId = "commit_result_already_committed";
                return false;
            }
            if (!ValidateOperationBackReferences(
                    rewardOperation.operationId, rewardOperation.eventTokenId,
                    rewardOperation.receipts, out failureReasonId))
            {
                return false;
            }

            _active.resultToken = resultToken;
            _active.rewardOperation = rewardOperation;
            if (!TryAdvancePhase(
                    ModeHStakePhase.ResultCommitted, ModeHSettlementKind.MatchResult,
                    out failureReasonId))
            {
                // 阶段推进内含落盘，撞上 SavesSystem.IsSaving 时必然失败。TryAdvancePhase 只
                // 回滚 phase/phaseSequence/settlementKind；这两个字段若不一并回滚，本函数开头的
                // commit_result_already_committed 早退会让**任何重试永久失败**，
                // 押品就此卡在 MatchLocked 再也结算不掉。
                _active.resultToken = null;
                _active.rewardOperation = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// `AbortReturnCommitted`：在同一写屏障创建 abortReturnToken 与唯一父
        /// abortReturnOperation(status=Committed)，冻结完整返还计划与子 receipts。
        /// 缺少父记录或回指不一致时该 phase 无效。
        /// </summary>
        public static bool CommitAbortReturn(
            string abortReturnToken, ModeHAbortReturnOperationDto operation, out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }
            if (string.IsNullOrEmpty(abortReturnToken) || operation == null)
            {
                failureReasonId = "commit_abort_input_invalid";
                return false;
            }
            if (!ValidateOperationBackReferences(
                    operation.operationId, operation.eventTokenId, operation.receipts,
                    out failureReasonId))
            {
                return false;
            }

            int previousOperationStatus = operation.status;
            _active.abortReturnToken = abortReturnToken;
            _active.abortReturnOperation = operation;
            operation.status = (int)ModeHAbortReturnOperationStatus.Committed;
            if (!TryAdvancePhase(
                    ModeHStakePhase.AbortReturnCommitted, ModeHSettlementKind.AbortReturn,
                    out failureReasonId))
            {
                // 同 CommitResult：落盘失败时这三处写入必须一并回滚，
                // 否则下一次重试会带着半提交的父 operation 继续，证据链对不上。
                _active.abortReturnToken = null;
                _active.abortReturnOperation = null;
                operation.status = previousOperationStatus;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 进入 `SettlementPending`：journal phase 与父 operation status 同屏障更新。
        /// </summary>
        public static bool EnterSettlementPending(out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }
            ModeHSettlementKind kind = ToSettlementKind(_active.settlementKind);
            if (kind == ModeHSettlementKind.MatchResult && _active.rewardOperation != null)
            {
                _active.rewardOperation.status = (int)ModeHRewardOperationStatus.SettlementPending;
            }
            else if (kind == ModeHSettlementKind.AbortReturn && _active.abortReturnOperation != null)
            {
                _active.abortReturnOperation.status =
                    (int)ModeHAbortReturnOperationStatus.SettlementPending;
            }
            else
            {
                failureReasonId = "settlement_pending_missing_parent";
                return false;
            }
            return TryAdvancePhase(ModeHStakePhase.SettlementPending, kind, out failureReasonId);
        }

        /// <summary>
        /// 终结：逐项 receipt 与最终 inventory digest 全部读回一致后，
        /// **父 operation 先在同屏障标为 Settled，journal 才可写终态**。
        /// 部分匹配、回调重复或 digest 不符一律保持 pending 或进入人工介入。
        /// </summary>
        public static bool Settle(out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }
            if (ToPhase(_active.phase) != ModeHStakePhase.SettlementPending)
            {
                failureReasonId = "settle_wrong_phase";
                return false;
            }

            if (!AllReceiptsVerified(out failureReasonId)) return false;

            string finalDigest;
            if (!ModeHInventoryPersistenceBridge.TryComputeInventoryDigest(
                    out finalDigest, out failureReasonId))
            {
                return false;
            }

            ModeHSettlementKind kind = ToSettlementKind(_active.settlementKind);
            if (kind == ModeHSettlementKind.MatchResult)
            {
                if (_active.rewardOperation == null)
                {
                    failureReasonId = "settle_missing_reward_operation";
                    return false;
                }
                _active.rewardOperation.status = (int)ModeHRewardOperationStatus.Settled;
                _active.inventoryPostDigest = finalDigest;
                return TryAdvancePhase(ModeHStakePhase.Terminal, kind, out failureReasonId);
            }
            if (kind == ModeHSettlementKind.AbortReturn)
            {
                if (_active.abortReturnOperation == null)
                {
                    failureReasonId = "settle_missing_abort_operation";
                    return false;
                }
                _active.abortReturnOperation.status = (int)ModeHAbortReturnOperationStatus.Settled;
                _active.inventoryPostDigest = finalDigest;
                return TryAdvancePhase(ModeHStakePhase.RefundedTerminal, kind, out failureReasonId);
            }

            failureReasonId = "settle_unknown_settlement_kind";
            return false;
        }

        /// <summary>
        /// `CancelledTerminal` 仅用于**已证明没有 escrow 移除**的取消
        /// （`Prepared` / `EscrowSnapshotDurable` 两个分支）。
        /// </summary>
        public static bool TryCancelWithoutRemoval(out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }
            ModeHStakePhase current = ToPhase(_active.phase);
            if (current != ModeHStakePhase.Prepared && current != ModeHStakePhase.EscrowSnapshotDurable)
            {
                failureReasonId = "cancel_requires_no_removal_phase";
                return false;
            }
            if (_escrowItems.Count > 0)
            {
                failureReasonId = "cancel_escrow_still_held";
                return false;
            }
            // _escrowItems 为空**不等于**物品还在仓库：LoadPersisted 在切槽/重启时
            // 会把它清空，此时仅凭内存态判断会把「物品已脱离但阶段回滚过」的错位
            // 静默归档成 CancelledTerminal（语义是"已证明从未移除"），等于确认丢失。
            // 按设计稿 §22 恢复矩阵，取消前必须有 durable 证据证明 escrow 未脱离；
            // 拿不出证据一律进人工介入，绝不自动猜测。
            if (!VerifyEscrowStillInInventory(out failureReasonId))
            {
                EnterManualIntervention(failureReasonId);
                return false;
            }
            return TryAdvancePhase(
                ModeHStakePhase.CancelledTerminal, ModeHSettlementKind.None, out failureReasonId);
        }

        /// <summary>
        /// 逐项核对 escrow 快照对应的根物品是否仍在仓库里（取消前的 durable 证据）。
        ///
        /// 用逐项 occurrence 比对而不是整仓 `inventoryPreDigest` 全等：后者对
        /// 「玩家在基地挪了任何别的东西」都会误报，而恢复壳打开时距 Prepare 可能已隔
        /// 一次重启。逐项比对直接回答「押品还在不在」这一个不变式。
        /// 读不出仓库或数量少于 preCount 一律判失败（fail-closed）。
        /// </summary>
        private static bool VerifyEscrowStillInInventory(out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null || _active.escrowItems == null) return true;

            string storageReason;
            if (!ModeHInventoryPersistenceBridge.IsStorageReady(out storageReason))
            {
                failureReasonId = "cancel_escrow_preimage_unreadable:" + storageReason;
                return false;
            }

            for (int i = 0; i < _active.escrowItems.Count; i++)
            {
                ModeHItemTreeSnapshotDto snapshot = _active.escrowItems[i];
                if (snapshot == null || string.IsNullOrEmpty(snapshot.semanticTreeDigest))
                {
                    failureReasonId = "cancel_escrow_preimage_snapshot_invalid";
                    return false;
                }
                int current = ModeHInventoryPersistenceBridge.CountOccurrences(
                    snapshot.semanticTreeDigest);
                if (current < snapshot.preCount)
                {
                    failureReasonId = "cancel_escrow_preimage_mismatch";
                    return false;
                }
            }
            return true;
        }

        #endregion

        #region 返还与奖励（唯一仓库写入者）

        /// <summary>
        /// 把托管中的 escrow 根物品放回仓库。每项写一条 receipt，
        /// 目标格必须已确认为空；没有空位就保持 pending，不覆盖已有物品。
        /// </summary>
        public static bool ReturnEscrowItems(IList<string> digestsToReturn, out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }

            bool buffered = false;
            for (int i = _escrowItems.Count - 1; i >= 0; i--)
            {
                Item item = _escrowItems[i];
                if (item == null)
                {
                    _escrowItems.RemoveAt(i);
                    continue;
                }
                int position = ModeHInventoryPersistenceBridge.FindConfirmedEmptyPosition();
                if (position < 0)
                {
                    // 仓库满时交官方溢出缓冲区，**绝不**保持 pending：保持 pending 等于把真实
                    // 装备只留在内存 _escrowItems 里，玩家退出/切槽时 LoadPersisted 会抹掉它。
                    string bufferReason;
                    if (!TryHandOffToStorageBuffer(item, out bufferReason))
                    {
                        failureReasonId = "return_no_empty_slot:" + bufferReason;
                        return false;
                    }
                    AppendReceipt("escrow_return", i, string.Empty, ModeHStakeReceiptStatus.Verified);
                    _escrowItems.RemoveAt(i);
                    buffered = true;
                    continue;
                }
                string reason;
                if (!ModeHInventoryPersistenceBridge.TryAddAtEmpty(item, position, out reason))
                {
                    failureReasonId = "return_add_failed:" + reason;
                    return false;
                }
                AppendReceipt("escrow_return", i, string.Empty, ModeHStakeReceiptStatus.Verified);
                _escrowItems.RemoveAt(i);
            }
            if (buffered) FlushStorageBuffer();
            return true;
        }

        /// <summary>
        /// 兑付 `rewardOperation` 里 `resultKind == "reward"` 的同品质奖励计划。
        /// typeId 在计划阶段是 0（§22.3 不预先伪造），此处按 `gameQuality` 完全相等
        /// 从官方物品池确定性抽取并实例化，逐件写 receipt。
        ///
        /// 幂等：已 `Applied` 的 reward receipt 不会重复发放，所以崩溃重放安全。
        /// 仓库满或池为空时保持 pending 并返回 false，绝不覆盖已有物品。
        /// </summary>
        public static bool GrantPlannedRewards(long runSeed, out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }
            ModeHRewardOperationDto operation = _active.rewardOperation;
            if (operation == null || operation.itemResults == null) return true;

            bool buffered = false;
            for (int i = 0; i < operation.itemResults.Count; i++)
            {
                ModeHRewardItemResultDto planned = operation.itemResults[i];
                if (planned == null) continue;
                if (!string.Equals(planned.resultKind, "reward", StringComparison.Ordinal)) continue;
                if (HasAppliedReceipt("escrow_reward", i)) continue;

                int typeId = ModeHRewardItemPool.TryPickSameQualityTypeId(
                    planned.gameQuality, runSeed, _active.txId, i, out failureReasonId);
                if (typeId <= 0) return false;

                Item granted = ModeHRewardItemPool.TryInstantiate(typeId, out failureReasonId);
                if (granted == null) return false;

                int position = ModeHInventoryPersistenceBridge.FindConfirmedEmptyPosition();
                if (position < 0)
                {
                    // 与押品返还同口径：仓库满不代表奖励作废，交官方溢出缓冲区。
                    string bufferReason;
                    if (!TryHandOffToStorageBuffer(granted, out bufferReason))
                    {
                        ModeHRewardItemPool.DestroyUngranted(granted);
                        failureReasonId = "reward_no_empty_slot:" + bufferReason;
                        return false;
                    }
                    planned.typeId = typeId;
                    AppendReceipt("escrow_reward", i, string.Empty, ModeHStakeReceiptStatus.Applied);
                    buffered = true;
                    continue;
                }
                string reason;
                if (!ModeHInventoryPersistenceBridge.TryAddAtEmpty(granted, position, out reason))
                {
                    ModeHRewardItemPool.DestroyUngranted(granted);
                    failureReasonId = "reward_add_failed:" + reason;
                    return false;
                }

                planned.typeId = typeId;
                AppendReceipt("escrow_reward", i, string.Empty, ModeHStakeReceiptStatus.Applied);
            }
            if (buffered) FlushStorageBuffer();
            return true;
        }

        /// <summary>
        /// 某条 reward 是否已发放过。重放时据此跳过，避免重复发实物。
        /// </summary>
        private static bool HasAppliedReceipt(string kind, int index)
        {
            if (_active == null || _active.receipts == null) return false;
            string operationId = _active.txId + "|" + kind + "|" + index;
            for (int i = 0; i < _active.receipts.Count; i++)
            {
                ModeHStakeReceiptDto receipt = _active.receipts[i];
                if (receipt == null) continue;
                if (!string.Equals(receipt.operationId, operationId, StringComparison.Ordinal)) continue;
                ModeHStakeReceiptStatus status = (ModeHStakeReceiptStatus)receipt.status;
                if (status == ModeHStakeReceiptStatus.Applied
                    || status == ModeHStakeReceiptStatus.Verified)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 失败清算：只保留 `Prepared` 时冻结的 `plannedLosses`，其余 escrow 原样返还。
        /// 绝不在比赛结束时重新从仓库抽取。
        /// </summary>
        public static bool ApplyPlannedLosses(out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null)
            {
                failureReasonId = "journal_missing";
                return false;
            }
            // 被没收的项已经在 escrow 中且不再返还：这里只记 receipt，不做第二次移除
            for (int i = 0; i < _active.lossItems.Count; i++)
            {
                ModeHItemTreeSnapshotDto loss = _active.lossItems[i];
                if (loss == null) continue;
                AppendReceipt("planned_loss", i, loss.semanticTreeDigest,
                    ModeHStakeReceiptStatus.Applied);
                RemoveEscrowByDigest(loss.semanticTreeDigest);
            }
            return true;
        }

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

        #endregion

        #region receipt 与摘要

        private static void AppendReceipt(
            string kind, int index, string digest, ModeHStakeReceiptStatus status)
        {
            if (_active == null) return;
            if (_active.receipts == null) _active.receipts = new List<ModeHStakeReceiptDto>();

            ModeHStakeReceiptDto receipt = new ModeHStakeReceiptDto();
            receipt.operationId = _active.txId + "|" + kind + "|" + index;
            receipt.parentOperationId = ResolveParentOperationId(kind);
            receipt.kind = kind;
            receipt.eventTokenId = ResolveEventTokenId(kind);
            receipt.expectedBeforeDigest = _active.inventoryPreDigest;
            receipt.expectedAfterDigest = digest != null ? digest : string.Empty;
            receipt.status = (int)status;

            string receiptDigest, error;
            if (ModeHCanonicalDigest.TryComputeObjectDigest(
                    receipt, "receiptDigest", out receiptDigest, out error))
            {
                receipt.receiptDigest = receiptDigest;
            }
            _active.receipts.Add(receipt);
        }

        private static string ResolveParentOperationId(string kind)
        {
            if (_active == null) return string.Empty;
            if (string.Equals(kind, "escrow_return", StringComparison.Ordinal)
                && _active.abortReturnOperation != null)
            {
                return _active.abortReturnOperation.operationId;
            }
            if (_active.rewardOperation != null) return _active.rewardOperation.operationId;
            return string.Empty;
        }

        private static string ResolveEventTokenId(string kind)
        {
            if (_active == null) return string.Empty;
            if (string.Equals(kind, "escrow_return", StringComparison.Ordinal)
                && _active.abortReturnOperation != null)
            {
                return _active.abortReturnOperation.eventTokenId;
            }
            if (_active.rewardOperation != null) return _active.rewardOperation.eventTokenId;
            return string.Empty;
        }

        /// <summary>每个 receipt 的父/子回指与状态都必须一致，否则不得终结。</summary>
        private static bool AllReceiptsVerified(out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null || _active.receipts == null) return true;
            for (int i = 0; i < _active.receipts.Count; i++)
            {
                ModeHStakeReceiptDto receipt = _active.receipts[i];
                if (receipt == null)
                {
                    failureReasonId = "receipt_null";
                    return false;
                }
                ModeHStakeReceiptStatus status = ToReceiptStatus(receipt.status);
                if (status == ModeHStakeReceiptStatus.ManualIntervention
                    || status == ModeHStakeReceiptStatus.Rejected)
                {
                    failureReasonId = "receipt_manual_or_rejected:" + receipt.operationId;
                    return false;
                }
                if (status == ModeHStakeReceiptStatus.Planned || status == ModeHStakeReceiptStatus.Pending)
                {
                    failureReasonId = "receipt_incomplete:" + receipt.operationId;
                    return false;
                }
            }
            return true;
        }

        private static ModeHStakeReceiptStatus ToReceiptStatus(int raw)
        {
            if (!Enum.IsDefined(typeof(ModeHStakeReceiptStatus), raw))
            {
                return ModeHStakeReceiptStatus.Unknown;
            }
            return (ModeHStakeReceiptStatus)raw;
        }

        /// <summary>父/子 operation 回指校验（§22.2：缺少父记录或回指不一致时 phase 无效）。</summary>
        private static bool ValidateOperationBackReferences(
            string parentOperationId,
            string eventTokenId,
            IList<ModeHStakeReceiptDto> receipts,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (string.IsNullOrEmpty(parentOperationId))
            {
                failureReasonId = "operation_parent_id_missing";
                return false;
            }
            if (string.IsNullOrEmpty(eventTokenId))
            {
                failureReasonId = "operation_event_token_missing";
                return false;
            }
            if (receipts == null) return true;
            for (int i = 0; i < receipts.Count; i++)
            {
                ModeHStakeReceiptDto receipt = receipts[i];
                if (receipt == null)
                {
                    failureReasonId = "operation_receipt_null";
                    return false;
                }
                if (!string.Equals(receipt.parentOperationId, parentOperationId, StringComparison.Ordinal))
                {
                    failureReasonId = "operation_receipt_parent_mismatch";
                    return false;
                }
                if (!string.Equals(receipt.eventTokenId, eventTokenId, StringComparison.Ordinal))
                {
                    failureReasonId = "operation_receipt_token_mismatch";
                    return false;
                }
            }
            return true;
        }

        private static bool TryRestampDigest(out string failureReasonId)
        {
            failureReasonId = null;
            if (_active == null) return true;
            string digest, error;
            if (!ModeHCanonicalDigest.TryComputeObjectDigest(
                    _active, "payloadDigest", out digest, out error))
            {
                failureReasonId = "journal_digest_failed:" + error;
                return false;
            }
            _active.payloadDigest = digest;
            return true;
        }

        #endregion

        #region 恢复与复位

        /// <summary>从存档恢复 active journal（只读装载，不做任何写入）。</summary>
        public static void LoadPersisted(ModeHStakeJournalDto persisted)
        {
            _active = persisted;
            // 换槽：PlayerStorage 已指向新槽，绝不能把旧槽的托管物 Push 过去。
            DrainEscrowToStorageBuffer("LoadPersisted", false);
            _escrowItems.Clear();
            RecomputeSlotConsistency(persisted);
        }

        /// <summary>导出当前 journal 供存档写入。</summary>
        public static ModeHStakeJournalDto Export()
        {
            return _active;
        }

        /// <summary>清空静态状态（切槽 / Mod 卸载）。</summary>
        public static void ResetStaticCaches()
        {
            _active = null;
            // 宿主销毁 / Mod 卸载：仍是同一个槽，交缓冲区是安全且必要的。
            DrainEscrowToStorageBuffer("ResetStaticCaches", true);
            _escrowItems.Clear();
            _lastError = null;
            _slotConsistent = false;
            _slotInconsistentReasonId = null;
        }

        #endregion
    }
}
