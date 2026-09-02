// ============================================================================
// ModeHRealStakeService.cs - 真实仓库押品的选择与结算编排
// ============================================================================
// 定位：
//   本类是「玩家在押品选择器里点了哪几件」与 ModeHWarehouseStakeJournal 之间的
//   唯一桥梁。它自己**不碰任何库存 API**——移除、返还、没收全部由 journal 执行
//   （journal 是 Mode H 唯一的仓库写入者，§22.3）。本类只负责：
//     1. 把玩家选中的仓库槽位翻译成规范化快照；
//     2. 按赔率算出「最坏损失件数」并冻结 plannedLosses；
//     3. 按固定顺序驱动 journal 的阶段机；
//     4. 结算时构造结果计划并交给 journal 提交。
//
// 冻结口径（§22.1 / §22.3）：
//   - 押品**默认不押**：玩家不选就没有 journal，赛季照常用虚拟筹码跑完整闭环；
//   - 件数上限 MaxRealStakeItemsPerMatch，且「唯一装备不豁免」——不做任何
//     "这是你最后一把枪所以帮你挡一下"的隐式保护，那与风险提示矛盾；
//   - 最坏损失 = 全部押品（败场按 plannedLosses 没收）。押多少就可能亏多少，
//     这是玩家在知情同意下的选择，不做暗中打折；
//   - 胜利返还全部押品 + 按**原始整数倍率**发同品质奖励（lockedOdds 件）；
//   - 任何一步失败都不静默：调用方拿到 failureReasonId 后原位展示，
//     绝不"当作没押"继续开战（那会让玩家以为物品安全而实际已脱离仓库）。
//
// 顺序是硬约束（§22.2 单向阶段机）：
//   Prepare -> CommitEscrowSnapshot -> RemoveEscrow -> LockMatch
//   -> (胜负已定) CommitResult -> EnterSettlementPending -> 返还/没收 -> Settle
//   每一步都先落盘再动物品，崩溃后靠 journal 阶段就能判断物品在哪。
// ============================================================================

using System;
using System.Collections.Generic;


namespace BossRush
{
    /// <summary>真实押品编排。无自有持久状态，唯一状态在 journal 里。</summary>
    internal static class ModeHRealStakeService
    {
        #region 选择（看盘页）

        /// <summary>
        /// 玩家当前选中的仓库槽位。只在看盘页有效，锁盘时被翻译成 journal 后即清空。
        /// 存槽位而不存 Item 引用：Item 引用会在切场景后失效，而槽位配合摘要
        /// 在 TryDetachAt 里会被重新核对一次。
        /// </summary>
        private static readonly List<int> _selectedPositions = new List<int>();

        /// <summary>
        /// 押品区枚举的最大槽位数。每格快照都要做树规范化 + SHA256，
        /// 全仓库扫会明显卡顿；看盘页只需要给出一个够用的候选窗口。
        /// </summary>
        private const int MaxScannedPositions = 40;

        /// <summary>当前选中件数。</summary>
        internal static int SelectedCount { get { return _selectedPositions.Count; } }

        /// <summary>是否已选了押品（决定本场要不要开 journal）。</summary>
        internal static bool HasSelection { get { return _selectedPositions.Count > 0; } }

        /// <summary>当前选中的槽位快照（UI 展示用，返回副本）。</summary>
        internal static List<int> GetSelectedPositions()
        {
            return new List<int>(_selectedPositions);
        }

        /// <summary>
        /// 切换一个仓库槽位的选中状态。
        /// 只有 IsSlotConsistent 为真时才允许选（证据不足时押品禁用，§22.1）。
        /// </summary>
        internal static bool ToggleSelection(int inventoryPosition, out string failureReasonId)
        {
            failureReasonId = null;
            if (inventoryPosition < 0)
            {
                failureReasonId = "stake_position_invalid";
                return false;
            }
            if (!ModeHWarehouseStakeJournal.IsSlotConsistent)
            {
                failureReasonId = "stake_slot_inconsistent";
                return false;
            }

            if (_selectedPositions.Contains(inventoryPosition))
            {
                _selectedPositions.Remove(inventoryPosition);
                return true;
            }

            if (_selectedPositions.Count >= ModeHConfig.MaxRealStakeItemsPerMatch)
            {
                failureReasonId = "stake_limit_reached";
                return false;
            }

            // 选中即校验一次可押性：不能等到锁盘才发现这格是空的或读不出品质
            string captureError;
            if (ModeHInventoryPersistenceBridge.TryCaptureAt(inventoryPosition, out captureError) == null)
            {
                failureReasonId = captureError != null ? captureError : "stake_item_unstakeable";
                return false;
            }

            _selectedPositions.Add(inventoryPosition);
            return true;
        }

        /// <summary>清空选择（离开看盘页、技术中止、赛季结束）。</summary>
        internal static void ClearSelection()
        {
            _selectedPositions.Clear();
        }

        /// <summary>
        /// 可押的仓库槽位列表（看盘页押品区用）。
        ///
        /// 只扫前 MaxScannedPositions 格：这是看盘页的一个副区而不是仓库管理器，
        /// 而每格快照都要做一次树规范化 + SHA256，全仓库扫会明显卡顿。
        /// 玩家要押靠后的物品可以先在官方仓库界面把它挪到前面。
        /// 已选中的槽位一定包含在结果里，即使它超出扫描窗口——否则玩家会看不到
        /// 自己已经押上的东西，也就无法取消。
        /// </summary>
        internal static List<int> GetSelectablePositions()
        {
            List<int> result =
                ModeHInventoryPersistenceBridge.ListOccupiedPositions(MaxScannedPositions);

            // 补上扫描窗口外的已选项，保证可取消
            for (int i = 0; i < _selectedPositions.Count; i++)
            {
                if (!result.Contains(_selectedPositions[i])) result.Add(_selectedPositions[i]);
            }
            return result;
        }

        /// <summary>
        /// 一格物品的按钮文案：名字 + 品质。取不到名字时退回槽位号，
        /// 绝不返回 null（那会让按钮变成空白）。
        /// </summary>
        internal static string DescribePosition(int position)
        {
            string fallback = L10n.T("槽位 ", "Slot ") + position;
            try
            {
                string name;
                int quality;
                if (!ModeHInventoryPersistenceBridge.TryDescribePosition(
                        position, out name, out quality))
                {
                    return fallback;
                }
                if (string.IsNullOrEmpty(name)) name = fallback;
                return quality > 0 ? name + "  Q" + quality : name;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>
        /// 本场胜利时能赢到的同品质奖励件数（UI 预览用）。
        /// 真实押品按**原始整数倍率**结算，不套虚拟筹码的 1+odds 净赔率语义。
        /// </summary>
        internal static int PreviewRewardCount(int lockedOdds)
        {
            if (_selectedPositions.Count <= 0) return 0;
            if (lockedOdds < ModeHConfig.MinOdds) return ModeHConfig.MinOdds;
            if (lockedOdds > ModeHConfig.MaxOdds) return ModeHConfig.MaxOdds;
            return lockedOdds;
        }

        /// <summary>最坏损失件数（UI 预览用）：押多少就可能亏多少。</summary>
        internal static int PreviewWorstCaseLossCount()
        {
            return _selectedPositions.Count;
        }

        #endregion

        #region 锁盘（Prepared -> MatchLocked）

        /// <summary>
        /// 把当前选择翻译成 journal 并推进到 MatchLocked。
        ///
        /// 没有选择时返回 true 且不建 journal —— 这是"默认不押"的正常路径，
        /// 调用方据此继续走纯虚拟筹码的比赛。
        /// 任何一步失败都返回 false 并带出原因；已经动过的物品由 journal 内部回滚。
        /// </summary>
        internal static bool TryLockForMatch(
            string runId, int matchIndex, long runSeed, out string failureReasonId)
        {
            failureReasonId = null;
            if (_selectedPositions.Count == 0) return true;

            if (!ModeHWarehouseStakeJournal.IsSlotConsistent)
            {
                failureReasonId = "stake_slot_inconsistent";
                return false;
            }

            List<ModeHItemTreeSnapshotDto> escrow = new List<ModeHItemTreeSnapshotDto>();
            for (int i = 0; i < _selectedPositions.Count; i++)
            {
                ModeHItemTreeSnapshotDto snapshot = ModeHInventoryPersistenceBridge.TryCaptureAt(
                    _selectedPositions[i], out failureReasonId);
                if (snapshot == null) return false;
                escrow.Add(snapshot);
            }

            // 最坏损失 = 全部押品。plannedLosses 在 Prepared 就冻结，
            // 结算时只认这份清单，绝不在比赛结束后重新抽取（§22.3）。
            List<ModeHItemTreeSnapshotDto> plannedLosses =
                new List<ModeHItemTreeSnapshotDto>(escrow);

            string txId = BuildTransactionId(runId, matchIndex, runSeed);
            if (!ModeHWarehouseStakeJournal.TryPrepare(
                    txId, runId, matchIndex,
                    ReadCurrentSlotSafe(), ModeHRuntimeGates.SlotGeneration,
                    escrow, plannedLosses, out failureReasonId))
            {
                return false;
            }

            if (!ModeHWarehouseStakeJournal.TryCommitEscrowSnapshot(out failureReasonId)) return false;
            if (!ModeHWarehouseStakeJournal.TryRemoveEscrow(out failureReasonId)) return false;
            if (!ModeHWarehouseStakeJournal.TryLockMatch(out failureReasonId)) return false;

            // journal 已接管这批物品，选择器状态不再需要
            ClearSelection();
            return true;
        }

        #endregion

        #region 结算（CommitResult -> Terminal）

        /// <summary>
        /// 比赛分出胜负后的真实押品结算。无 active journal 时直接返回 true（没押）。
        /// </summary>
        internal static bool TrySettleMatch(
            long runSeed, int matchIndex, bool won, int lockedOdds, out string failureReasonId)
        {
            failureReasonId = null;
            ModeHStakeJournalDto journal = ModeHWarehouseStakeJournal.Active;
            if (journal == null) return true;
            if (ModeHWarehouseStakeJournal.IsTerminalPhase(
                    ModeHStateModel.ToStakePhase(journal.phase)))
            {
                return true;
            }

            string resultToken = "res|" + journal.txId + "|" + matchIndex;
            ModeHRewardOperationDto operation = ModeHRewardTransaction.BuildMatchResultPlan(
                journal, runSeed, matchIndex, resultToken, won, lockedOdds,
                journal.payloadDigest, out failureReasonId);
            if (operation == null) return false;

            if (!ModeHWarehouseStakeJournal.CommitResult(resultToken, operation, out failureReasonId))
            {
                return false;
            }
            if (!ModeHWarehouseStakeJournal.EnterSettlementPending(out failureReasonId)) return false;

            if (won)
            {
                // 胜利：先把全部押品原样返还，再兑付同品质奖励。
                // 顺序不能反：押品是玩家本来就有的东西，必须优先占用空位；
                // 奖励发不出时（仓库满/池为空）journal 保持 pending，押品已安全归位。
                if (!ModeHWarehouseStakeJournal.ReturnEscrowItems(null, out failureReasonId))
                {
                    return false;
                }
                // typeId 在计划阶段是 0，此处按 gameQuality 完全相等确定性抽取并实例化
                // （§22.3 不预先伪造 typeId，也不以同 TypeID 新建物品冒充原押品）。
                if (!ModeHWarehouseStakeJournal.GrantPlannedRewards(runSeed, out failureReasonId))
                {
                    return false;
                }
            }
            else
            {
                // 失败：只没收 Prepared 时冻结的 plannedLosses，其余原样返还
                if (!ModeHWarehouseStakeJournal.ApplyPlannedLosses(out failureReasonId)) return false;
                if (!ModeHWarehouseStakeJournal.ReturnEscrowItems(null, out failureReasonId))
                {
                    return false;
                }
            }

            return ModeHWarehouseStakeJournal.Settle(out failureReasonId);
        }

        /// <summary>
        /// 技术中止的完整返还。开战前后都可调；无 active journal 时直接返回 true。
        /// 与失败清算的区别：中止**不没收任何东西**，全部原样退回。
        /// </summary>
        internal static bool TryAbortReturn(
            long runSeed, int matchIndex, out string failureReasonId)
        {
            failureReasonId = null;
            ModeHStakeJournalDto journal = ModeHWarehouseStakeJournal.Active;
            if (journal == null) return true;

            ModeHStakePhase phase = ModeHStateModel.ToStakePhase(journal.phase);
            if (ModeHWarehouseStakeJournal.IsTerminalPhase(phase)) return true;

            // 还没动过物品的两个阶段：直接取消，不需要走返还计划
            if (phase == ModeHStakePhase.Prepared || phase == ModeHStakePhase.EscrowSnapshotDurable)
            {
                return ModeHWarehouseStakeJournal.TryCancelWithoutRemoval(out failureReasonId);
            }

            string abortToken = "abt|" + journal.txId + "|" + matchIndex;
            ModeHAbortReturnOperationDto operation = ModeHRewardTransaction.BuildAbortReturnPlan(
                journal, runSeed, matchIndex, abortToken, journal.payloadDigest,
                out failureReasonId);
            if (operation == null) return false;

            if (!ModeHWarehouseStakeJournal.CommitAbortReturn(
                    abortToken, operation, out failureReasonId))
            {
                return false;
            }
            if (!ModeHWarehouseStakeJournal.EnterSettlementPending(out failureReasonId)) return false;
            if (!ModeHWarehouseStakeJournal.ReturnEscrowItems(null, out failureReasonId)) return false;
            return ModeHWarehouseStakeJournal.Settle(out failureReasonId);
        }

        #endregion

        #region 辅助

        /// <summary>
        /// 派生本场押品事务 ID。
        ///
        /// 用 PlannedLoss 域而**不是** Reward 域：ModeHRewardTransaction 已经在
        /// (runSeed, Reward, matchIndex) 上取过前两个 UInt64 来派生 operationId 与
        /// eventTokenId，txId 再从同一条流上取会与它们抢同一个序列位置，
        /// 让"重启后重放同一场得到同一组 ID"的幂等性依赖变得脆弱。
        /// 真实资产路径本来就为此预留了独立域。
        /// </summary>
        /// <summary>
        /// 当前存档槽 ID。no-throw：读不到返回 -1，journal 仍会写下这一笔
        /// （槽身份不明由 RecomputeSlotConsistency 的第三条负责拦截，
        /// 不在这里静默改写成 0 冒充"0 号槽"）。
        /// </summary>
        private static int ReadCurrentSlotSafe()
        {
            try
            {
                return Saves.SavesSystem.CurrentSlot;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static string BuildTransactionId(string runId, int matchIndex, long runSeed)
        {
            ModeHSeedStream stream = ModeHSeedStream.Create(
                runSeed, ModeHSeedStream.Domains.PlannedLoss, matchIndex);
            return "stx|" + (runId != null ? runId : "norun") + "|" + matchIndex + "|"
                + stream.NextUInt64().ToString("x16");
        }

        /// <summary>宿主销毁 / 切槽：只清选择器，绝不动 journal。</summary>
        internal static void ResetStaticCaches()
        {
            _selectedPositions.Clear();
        }

        #endregion
    }
}
