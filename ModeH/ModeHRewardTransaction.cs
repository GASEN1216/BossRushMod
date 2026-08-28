using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 真实资产结果计划（设计提案 §22.3、§25.1）。
    ///
    /// 冻结契约：
    /// - 本类**只生成**真实资产父/子 operation 与 receipt 计划，
    ///   随后经 `ModeHWarehouseStakeJournal` 的 `CommitResult/Settle` 提交；
    ///   **绝不自己调用任何库存 API**（`ModeHWarehouseStakeJournal` 是唯一仓库写入者）；
    /// - 双 nonce 由 seed 派生，父/子 operation 与 receipt 互相回指；
    /// - 胜利返还全部未损失 escrow 与原押品，再写入**同品质**额外奖励；
    ///   “同品质”只能指 `gameQuality` 完全相等，不套用赔率评分的封顶；
    /// - 真实押品路径的“件数”按**原始整数倍率**结算（`x3` = 3 件），
    ///   不套用 §17.5 的 `1 + odds` 净赔率语义；
    /// - 失败清算只使用 `Prepared` 时冻结的 `plannedLosses`，不重新抽取；
    /// - 无法原样返回时**不以同 TypeID 新建物品冒充**，直接进入恢复面板。
    /// </summary>
    internal static class ModeHRewardTransaction
    {
        #region 计划构造

        /// <summary>
        /// 构造一次比赛结果的父 reward operation（含子 receipt 计划）。
        /// 只构造，不提交；提交由 journal 完成。
        /// </summary>
        public static ModeHRewardOperationDto BuildMatchResultPlan(
            ModeHStakeJournalDto journal,
            long runSeed,
            int matchIndex,
            string resultToken,
            bool won,
            int lockedOdds,
            string planDigest,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (journal == null || string.IsNullOrEmpty(resultToken))
            {
                failureReasonId = "reward_plan_input_invalid";
                return null;
            }

            // 双 nonce：operationId 与 eventTokenId 都从同一 seed 域确定性派生，
            // 因此重启后重放同一场只会得到同一对 ID，天然幂等。
            ModeHSeedStream stream = ModeHSeedStream.Create(
                runSeed, ModeHSeedStream.Domains.Reward, matchIndex);
            string operationId = "rop|" + journal.txId + "|" + matchIndex + "|"
                + stream.NextUInt64().ToString("x16");
            string eventTokenId = "rtk|" + journal.txId + "|" + matchIndex + "|"
                + stream.NextUInt64().ToString("x16");

            ModeHRewardOperationDto operation = new ModeHRewardOperationDto();
            operation.operationId = operationId;
            operation.eventTokenId = eventTokenId;
            operation.matchIndex = matchIndex;
            operation.resultToken = resultToken;
            operation.settlementKind = (int)ModeHSettlementKind.MatchResult;
            operation.planDigest = planDigest != null ? planDigest : string.Empty;
            operation.status = (int)ModeHRewardOperationStatus.Planned;
            operation.itemResults = new List<ModeHRewardItemResultDto>();
            operation.receipts = new List<ModeHStakeReceiptDto>();

            if (won)
            {
                AppendReturnAll(journal, operation);
                AppendSameQualityRewards(journal, operation, lockedOdds);
            }
            else
            {
                AppendPlannedLosses(journal, operation);
                AppendReturnRemaining(journal, operation);
            }

            StampReceipts(operation);
            return operation;
        }

        /// <summary>
        /// 构造技术中止的父 abort-return operation：完整返还计划 + 逐项 receipt。
        /// 专用 `eventTokenId` 与 `abortReturnToken` 都非空，且子 receipt 回指父操作。
        /// </summary>
        public static ModeHAbortReturnOperationDto BuildAbortReturnPlan(
            ModeHStakeJournalDto journal,
            long runSeed,
            int matchIndex,
            string abortReturnToken,
            string planDigest,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (journal == null || string.IsNullOrEmpty(abortReturnToken))
            {
                failureReasonId = "abort_plan_input_invalid";
                return null;
            }

            ModeHSeedStream stream = ModeHSeedStream.Create(
                runSeed, ModeHSeedStream.Domains.Reward, matchIndex + 1000);
            ModeHAbortReturnOperationDto operation = new ModeHAbortReturnOperationDto();
            operation.operationId = "aop|" + journal.txId + "|" + matchIndex + "|"
                + stream.NextUInt64().ToString("x16");
            operation.abortReturnToken = abortReturnToken;
            operation.eventTokenId = "atk|" + journal.txId + "|" + matchIndex + "|"
                + stream.NextUInt64().ToString("x16");
            operation.matchIndex = matchIndex;
            operation.settlementKind = (int)ModeHSettlementKind.AbortReturn;
            operation.planDigest = planDigest != null ? planDigest : string.Empty;
            operation.status = (int)ModeHAbortReturnOperationStatus.Committed;
            operation.itemResults = new List<ModeHRewardItemResultDto>();
            operation.receipts = new List<ModeHStakeReceiptDto>();

            for (int i = 0; i < journal.escrowItems.Count; i++)
            {
                AppendItemResult(operation.itemResults, operation.operationId, "return",
                    journal.escrowItems[i]);
            }
            StampAbortReceipts(operation);
            return operation;
        }

        #endregion

        #region 计划分量

        /// <summary>胜利：返还全部 escrow（含原押品）。</summary>
        private static void AppendReturnAll(
            ModeHStakeJournalDto journal, ModeHRewardOperationDto operation)
        {
            for (int i = 0; i < journal.escrowItems.Count; i++)
            {
                AppendItemResult(operation.itemResults, operation.operationId, "return",
                    journal.escrowItems[i]);
            }
        }

        /// <summary>
        /// 胜利额外奖励：按**原始整数倍率**给同品质件数（`x3` = 3 件）。
        /// 品质取押品中的最高 `gameQuality`，且必须完全相等才算同品质。
        /// </summary>
        private static void AppendSameQualityRewards(
            ModeHStakeJournalDto journal, ModeHRewardOperationDto operation, int lockedOdds)
        {
            if (journal.escrowItems == null || journal.escrowItems.Count == 0) return;
            int count = lockedOdds;
            if (count < ModeHConfig.MinOdds) count = ModeHConfig.MinOdds;
            if (count > ModeHConfig.MaxOdds) count = ModeHConfig.MaxOdds;

            int quality = 0;
            for (int i = 0; i < journal.escrowItems.Count; i++)
            {
                ModeHItemTreeSnapshotDto item = journal.escrowItems[i];
                if (item != null && item.gameQuality > quality) quality = item.gameQuality;
            }
            if (quality < ModeHConfig.MinGameQuality || quality > ModeHConfig.MaxGameQuality) return;

            for (int i = 0; i < count; i++)
            {
                ModeHRewardItemResultDto result = new ModeHRewardItemResultDto();
                result.operationId = operation.operationId + "|reward|" + i;
                result.resultKind = "reward";
                result.typeId = 0; // 具体 typeId 由清算时按同品质池确定，绝不预先伪造
                result.gameQuality = quality;
                result.itemSnapshot = null;
                operation.itemResults.Add(result);
            }
        }

        /// <summary>失败：只没收 `Prepared` 时冻结的 plannedLosses。</summary>
        private static void AppendPlannedLosses(
            ModeHStakeJournalDto journal, ModeHRewardOperationDto operation)
        {
            if (journal.lossItems == null) return;
            for (int i = 0; i < journal.lossItems.Count; i++)
            {
                AppendItemResult(operation.itemResults, operation.operationId, "loss",
                    journal.lossItems[i]);
            }
        }

        /// <summary>失败：没收之外的 escrow 原样返还。</summary>
        private static void AppendReturnRemaining(
            ModeHStakeJournalDto journal, ModeHRewardOperationDto operation)
        {
            if (journal.escrowItems == null) return;
            for (int i = 0; i < journal.escrowItems.Count; i++)
            {
                ModeHItemTreeSnapshotDto item = journal.escrowItems[i];
                if (item == null) continue;
                if (IsPlannedLoss(journal, item.semanticTreeDigest)) continue;
                AppendItemResult(operation.itemResults, operation.operationId, "return", item);
            }
        }

        private static bool IsPlannedLoss(ModeHStakeJournalDto journal, string digest)
        {
            if (journal.lossItems == null || string.IsNullOrEmpty(digest)) return false;
            for (int i = 0; i < journal.lossItems.Count; i++)
            {
                ModeHItemTreeSnapshotDto loss = journal.lossItems[i];
                if (loss != null
                    && string.Equals(loss.semanticTreeDigest, digest, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AppendItemResult(
            List<ModeHRewardItemResultDto> results, string parentOperationId, string kind,
            ModeHItemTreeSnapshotDto snapshot)
        {
            if (snapshot == null) return;
            ModeHRewardItemResultDto result = new ModeHRewardItemResultDto();
            result.operationId = parentOperationId + "|" + kind + "|" + snapshot.semanticTreeDigest;
            result.resultKind = kind;
            result.typeId = 0;
            result.gameQuality = snapshot.gameQuality;
            result.itemSnapshot = snapshot;
            results.Add(result);
        }

        #endregion

        #region receipt 计划

        /// <summary>每个 item result 生成一条 Planned receipt，回指父 operation 与事件 token。</summary>
        private static void StampReceipts(ModeHRewardOperationDto operation)
        {
            for (int i = 0; i < operation.itemResults.Count; i++)
            {
                ModeHRewardItemResultDto item = operation.itemResults[i];
                operation.receipts.Add(BuildReceipt(
                    item, operation.operationId, operation.eventTokenId));
            }
        }

        private static void StampAbortReceipts(ModeHAbortReturnOperationDto operation)
        {
            for (int i = 0; i < operation.itemResults.Count; i++)
            {
                ModeHRewardItemResultDto item = operation.itemResults[i];
                operation.receipts.Add(BuildReceipt(
                    item, operation.operationId, operation.eventTokenId));
            }
        }

        private static ModeHStakeReceiptDto BuildReceipt(
            ModeHRewardItemResultDto item, string parentOperationId, string eventTokenId)
        {
            ModeHStakeReceiptDto receipt = new ModeHStakeReceiptDto();
            receipt.operationId = item.operationId;
            receipt.parentOperationId = parentOperationId;
            receipt.kind = item.resultKind;
            receipt.eventTokenId = eventTokenId;
            receipt.expectedBeforeDigest = item.itemSnapshot != null
                ? item.itemSnapshot.semanticTreeDigest
                : string.Empty;
            receipt.expectedAfterDigest = string.Empty;
            receipt.status = (int)ModeHStakeReceiptStatus.Planned;

            string digest, error;
            if (ModeHCanonicalDigest.TryComputeObjectDigest(
                    receipt, "receiptDigest", out digest, out error))
            {
                receipt.receiptDigest = digest;
            }
            return receipt;
        }

        #endregion
    }
}
