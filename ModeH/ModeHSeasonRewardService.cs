using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 虚拟奖励服务（设计提案 §17.8、§20.1、§25.1）。
    ///
    /// **在玩家资产白名单之外**：编译 guard 断言本文件不引用 `Inventory` 或 `PlayerStorage`；
    /// 真实物品奖励由 `ModeHRewardTransaction` + journal 负责，两者不互相调用。
    ///
    /// 冻结契约：
    /// - `rewardKind = None | UnlockKit | FameDisplay`；
    /// - 胜场有候选时为 `UnlockKit + Offered` 且所选项为空，
    ///   选择后才写 `selectedRewardKitId + Applied`；
    /// - 候选耗尽自动 `FameDisplay + Applied`；败场固定 `None + Applied`；
    /// - 状态单向 `Offered -> Applied -> Archived`；
    /// - 候选列表由 seed 确定性构造，重启后不重抽；
    /// - `operationId` 幂等：同一 `eventTokenId` 只会产生一条 operation；
    /// - 不含 item、receipt 或库存字段。
    /// </summary>
    internal static class ModeHSeasonRewardService
    {
        #region 构造

        /// <summary>
        /// 为一场已结算比赛确定性构造奖励 operation。
        /// 同一 `eventTokenId` 已存在时直接返回既有条目（幂等）。
        /// </summary>
        public static ModeHSeasonRewardOperationDto BuildOrGet(
            ModeHSeasonDto season,
            ModeHMatchReportDto report,
            string rewardProfileId,
            int rewardCandidateCount,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (season == null || report == null)
            {
                failureReasonId = "reward_input_missing";
                return null;
            }
            if (season.seasonRewardOperations == null)
            {
                season.seasonRewardOperations = new List<ModeHSeasonRewardOperationDto>();
            }

            string eventTokenId = "srw|" + report.matchIndex + "|" + report.resultToken;
            ModeHSeasonRewardOperationDto existing = FindByEventToken(season, eventTokenId);
            if (existing != null) return existing;

            ModeHSeasonRewardOperationDto operation = new ModeHSeasonRewardOperationDto();
            operation.operationId = "sop|" + report.matchIndex + "|" + report.resultToken;
            operation.eventTokenId = eventTokenId;
            operation.matchIndex = report.matchIndex;
            operation.resultToken = report.resultToken;
            operation.rewardProfileId = rewardProfileId != null ? rewardProfileId : string.Empty;
            operation.lockedOdds = report.lockedOdds;
            operation.virtualStakeAmount = report.virtualStakeAmount;
            operation.grossVirtualPayout = report.grossVirtualPayout;
            operation.netVirtualProfit = ModeHVirtualStakeController.ComputeNetProfit(
                report.virtualStakeAmount, report.grossVirtualPayout);
            operation.selectedRewardKitId = string.Empty;
            operation.candidateKitIds = new List<string>();

            bool won = report.winner == (int)ModeHMatchOutcome.PlayerVictory;
            if (!won)
            {
                // 败场固定 None + Applied
                operation.rewardKind = (int)ModeHRewardKind.None;
                operation.status = (int)ModeHSeasonRewardOperationStatus.Applied;
                season.seasonRewardOperations.Add(operation);
                return operation;
            }

            List<string> candidates = BuildCandidates(
                season, rewardProfileId, rewardCandidateCount, report.matchIndex);
            if (candidates.Count == 0)
            {
                // 候选耗尽：自动 FameDisplay + Applied
                operation.rewardKind = (int)ModeHRewardKind.FameDisplay;
                operation.status = (int)ModeHSeasonRewardOperationStatus.Applied;
                season.seasonRewardOperations.Add(operation);
                ApplyFameDisplay(season, rewardProfileId);
                return operation;
            }

            operation.candidateKitIds = candidates;
            operation.rewardKind = (int)ModeHRewardKind.UnlockKit;
            operation.status = (int)ModeHSeasonRewardOperationStatus.Offered;
            season.seasonRewardOperations.Add(operation);
            return operation;
        }

        /// <summary>
        /// 候选列表：从与该选手兼容且尚未解锁的非 starter kit 中按 seed 固定抽取。
        /// 结果按 kitId ordinal 升序，重启后重放同一 seed 得到同一列表。
        /// </summary>
        private static List<string> BuildCandidates(
            ModeHSeasonDto season, string rewardProfileId, int candidateCount, int matchIndex)
        {
            List<string> result = new List<string>();
            if (candidateCount <= 0) return result;

            ModeHProfileDto profile = FindProfile(season, rewardProfileId);
            string archetypeId = profile != null ? profile.archetypeId : null;
            string templateId = profile != null ? profile.profileId : null;

            List<string> pool = ModeHLoadoutKitRegistry.GetRewardCandidatePool(
                season.unlockedKitIds, archetypeId, templateId);
            if (pool.Count == 0) return result;

            ModeHSeedStream stream = ModeHSeedStream.Create(
                season.runState != null ? season.runState.runSeed : 0L,
                ModeHSeedStream.Domains.Reward, matchIndex);
            List<string> picked = stream.TakeDistinct(pool, candidateCount);
            picked.Sort(StringComparer.Ordinal);
            return picked;
        }

        #endregion

        #region 应用

        /// <summary>
        /// 选择一件候选套装：写 `selectedRewardKitId` 并转 `Applied`，
        /// 同时把该 kit 加入本季解锁集合（集合幂等）。
        /// </summary>
        public static bool TrySelectKit(
            ModeHSeasonDto season, string operationId, string kitId, out string failureReasonId)
        {
            failureReasonId = null;
            ModeHSeasonRewardOperationDto operation = FindByOperationId(season, operationId);
            if (operation == null)
            {
                failureReasonId = "reward_operation_missing";
                return false;
            }
            if (operation.status != (int)ModeHSeasonRewardOperationStatus.Offered)
            {
                failureReasonId = "reward_not_offered";
                return false;
            }
            if (operation.rewardKind != (int)ModeHRewardKind.UnlockKit)
            {
                failureReasonId = "reward_kind_mismatch";
                return false;
            }
            if (operation.candidateKitIds == null || !operation.candidateKitIds.Contains(kitId))
            {
                failureReasonId = "reward_kit_not_candidate";
                return false;
            }

            operation.selectedRewardKitId = kitId;
            operation.status = (int)ModeHSeasonRewardOperationStatus.Applied;

            if (season.unlockedKitIds == null) season.unlockedKitIds = new List<string>();
            if (!season.unlockedKitIds.Contains(kitId))
            {
                season.unlockedKitIds.Add(kitId);
                season.unlockedKitIds.Sort(StringComparer.Ordinal);
            }
            return true;
        }

        /// <summary>拒绝全部候选：转为名声展示（`fameDisplayCount +1`，上限 99）。</summary>
        public static bool TryDeclineToFame(
            ModeHSeasonDto season, string operationId, out string failureReasonId)
        {
            failureReasonId = null;
            ModeHSeasonRewardOperationDto operation = FindByOperationId(season, operationId);
            if (operation == null)
            {
                failureReasonId = "reward_operation_missing";
                return false;
            }
            if (operation.status != (int)ModeHSeasonRewardOperationStatus.Offered)
            {
                failureReasonId = "reward_not_offered";
                return false;
            }

            operation.rewardKind = (int)ModeHRewardKind.FameDisplay;
            operation.selectedRewardKitId = string.Empty;
            operation.status = (int)ModeHSeasonRewardOperationStatus.Applied;
            ApplyFameDisplay(season, operation.rewardProfileId);
            return true;
        }

        /// <summary>本场结算归档后把 operation 推进到 `Archived`（单向）。</summary>
        public static bool TryArchive(
            ModeHSeasonDto season, string operationId, out string failureReasonId)
        {
            failureReasonId = null;
            ModeHSeasonRewardOperationDto operation = FindByOperationId(season, operationId);
            if (operation == null)
            {
                failureReasonId = "reward_operation_missing";
                return false;
            }
            if (operation.status != (int)ModeHSeasonRewardOperationStatus.Applied)
            {
                failureReasonId = "reward_archive_requires_applied";
                return false;
            }
            operation.status = (int)ModeHSeasonRewardOperationStatus.Archived;
            return true;
        }

        /// <summary>名声只用于赛季战报与名人堂展示，不影响战斗、赔率、奖励或市场。</summary>
        private static void ApplyFameDisplay(ModeHSeasonDto season, string profileId)
        {
            ModeHProfileDto profile = FindProfile(season, profileId);
            if (profile == null) return;
            int next = profile.fameDisplayCount + ModeHConfig.ScarDeclineFameGain;
            profile.fameDisplayCount = next > ModeHConfig.MaxFameDisplayCount
                ? ModeHConfig.MaxFameDisplayCount
                : next;
        }

        #endregion

        #region 查询

        /// <summary>
        /// `rewardKind=Kit` 才允许喂官方奖励滚轮；`Fame` 必须走 Mode H 自有静态横幅，
        /// 且不得构造任何物品 typeId（§26.1 由 ModeHSeasonRewardGuard 断言）。
        /// </summary>
        public static bool AllowsRewardWheel(ModeHSeasonRewardOperationDto operation)
        {
            return operation != null && operation.rewardKind == (int)ModeHRewardKind.UnlockKit;
        }

        private static ModeHSeasonRewardOperationDto FindByEventToken(
            ModeHSeasonDto season, string eventTokenId)
        {
            if (season.seasonRewardOperations == null || string.IsNullOrEmpty(eventTokenId)) return null;
            for (int i = 0; i < season.seasonRewardOperations.Count; i++)
            {
                ModeHSeasonRewardOperationDto operation = season.seasonRewardOperations[i];
                if (operation != null
                    && string.Equals(operation.eventTokenId, eventTokenId, StringComparison.Ordinal))
                {
                    return operation;
                }
            }
            return null;
        }

        private static ModeHSeasonRewardOperationDto FindByOperationId(
            ModeHSeasonDto season, string operationId)
        {
            if (season == null || season.seasonRewardOperations == null
                || string.IsNullOrEmpty(operationId))
            {
                return null;
            }
            for (int i = 0; i < season.seasonRewardOperations.Count; i++)
            {
                ModeHSeasonRewardOperationDto operation = season.seasonRewardOperations[i];
                if (operation != null
                    && string.Equals(operation.operationId, operationId, StringComparison.Ordinal))
                {
                    return operation;
                }
            }
            return null;
        }

        private static ModeHProfileDto FindProfile(ModeHSeasonDto season, string profileId)
        {
            if (season == null || season.profiles == null || string.IsNullOrEmpty(profileId)) return null;
            for (int i = 0; i < season.profiles.Count; i++)
            {
                ModeHProfileDto profile = season.profiles[i];
                if (profile != null
                    && string.Equals(profile.profileId, profileId, StringComparison.Ordinal))
                {
                    return profile;
                }
            }
            return null;
        }

        #endregion
    }
}
