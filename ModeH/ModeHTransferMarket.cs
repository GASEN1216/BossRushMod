using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 有限转会市场（设计提案 §17.3、§25.1）。
    ///
    /// 冻结契约：
    /// - 全季只有两个市场窗口：第 2 场结算后（候签）与第 4 场结算后（特殊敌军资格）；
    /// - 第 2 场市场只提供“候签”或无 offer；
    /// - 第 4 场市场只读取最近已结算 `ModeHMatchReportDto` 的
    ///   `finalDefeatedProfileSnapshot + specialEnemyEligible + specialEnemySourceTag`，
    ///   且资格快照必须通过同一安全审计；任一字段缺失或不合格则无 offer；
    /// - **禁止刷新、拼接名单或运行时临时生成新 Boss**；
    /// - `contractMain` 永远不可主动出售或释放；只能在 `contractMain` 退役且
    ///   `contractSub` 存活时由后者晋升；
    /// - 被替换且未退役的旧合同选手记为 `Released`，本季不可再次签回；
    /// - 撕票选手不得从任何来源返场。
    /// </summary>
    internal static class ModeHTransferMarket
    {
        #region 窗口判定

        /// <summary>该场结算后是否开放市场窗口。</summary>
        public static bool IsMarketWindow(int matchIndex)
        {
            return matchIndex == ModeHConfig.FirstTransferWindowMatchIndex
                || matchIndex == ModeHConfig.SecondTransferWindowMatchIndex;
        }

        #endregion

        #region offer 构造

        /// <summary>
        /// 为本场结算后的市场窗口构造 offer。无合法来源时返回 null（无 offer）。
        /// 同一窗口已有 offer 时直接返回既有条目，不刷新、不重抽。
        /// </summary>
        public static ModeHOfferDto BuildOffer(
            ModeHSeasonDto season, int matchIndex, out string failureReasonId)
        {
            failureReasonId = null;
            if (season == null)
            {
                failureReasonId = "market_season_missing";
                return null;
            }
            if (!IsMarketWindow(matchIndex))
            {
                failureReasonId = "market_not_a_window";
                return null;
            }
            if (season.currentOffer != null && season.currentOffer.windowMatchIndex == matchIndex)
            {
                return season.currentOffer; // 禁止刷新
            }

            string profileId;
            string source;
            if (matchIndex == ModeHConfig.FirstTransferWindowMatchIndex)
            {
                profileId = ModeHDraftController.GetEchoProfileId(
                    season.echoAssignments, ModeHStableIds.EchoDestinationTransferCandidate);
                source = ModeHStableIds.EchoDestinationTransferCandidate;
                if (string.IsNullOrEmpty(profileId))
                {
                    failureReasonId = "market_no_transfer_candidate";
                    return null;
                }
            }
            else
            {
                if (!TryResolveSpecialEnemyOffer(season, out profileId, out failureReasonId))
                {
                    return null;
                }
                source = "special_enemy";
            }

            // 撕票选手不得从任何来源返场
            if (ModeHDraftController.IsRemovedProfile(season.echoAssignments, profileId))
            {
                failureReasonId = "market_profile_removed";
                return null;
            }
            // 已释放的旧合同选手本季不可再次签回
            ModeHProfileDto candidate = FindProfile(season, profileId);
            if (candidate != null && candidate.status == (int)ModeHParticipantStatus.Released)
            {
                failureReasonId = "market_profile_released";
                return null;
            }
            if (candidate != null && candidate.status == (int)ModeHParticipantStatus.Retired)
            {
                failureReasonId = "market_profile_retired";
                return null;
            }

            ModeHOfferDto offer = new ModeHOfferDto();
            offer.windowMatchIndex = matchIndex;
            offer.offerId = "off|" + matchIndex + "|" + profileId;
            offer.profileId = profileId;
            offer.source = source;
            offer.expiresAtStateSequence = season.runState != null
                ? season.runState.stateSequence + 1
                : 0;
            offer.status = (int)ModeHOfferStatus.Pending;
            season.currentOffer = offer;
            return offer;
        }

        /// <summary>
        /// 第 4 场：只读取最近已结算战报的三个冻结字段，并要求资格快照通过安全审计。
        /// 任一字段缺失或不合格即无 offer。
        /// </summary>
        private static bool TryResolveSpecialEnemyOffer(
            ModeHSeasonDto season, out string profileId, out string failureReasonId)
        {
            profileId = null;
            failureReasonId = null;

            ModeHMatchReportDto report = FindLatestSettledReport(season);
            if (report == null)
            {
                failureReasonId = "market_no_settled_report";
                return false;
            }
            if (!report.specialEnemyEligible)
            {
                failureReasonId = "market_special_enemy_not_eligible";
                return false;
            }
            if (string.IsNullOrEmpty(report.finalDefeatedProfileSnapshot)
                || string.IsNullOrEmpty(report.specialEnemySourceTag))
            {
                failureReasonId = "market_special_enemy_fields_missing";
                return false;
            }

            // 同一安全审计：来源模板必须仍在当前认证通过的生产池内
            ModeHProfileTemplate template =
                ModeHProfileRegistry.GetByTemplateId(report.specialEnemySourceTag);
            if (template == null || !ModeHPresetRegistry.IsProductionKey(template.StableKey))
            {
                failureReasonId = "market_special_enemy_audit_failed";
                return false;
            }

            profileId = report.finalDefeatedProfileSnapshot;
            return true;
        }

        private static ModeHMatchReportDto FindLatestSettledReport(ModeHSeasonDto season)
        {
            if (season.matchReports == null) return null;
            ModeHMatchReportDto best = null;
            for (int i = 0; i < season.matchReports.Count; i++)
            {
                ModeHMatchReportDto report = season.matchReports[i];
                if (report == null) continue;
                if (report.reportStatus != (int)ModeHMatchReportStatus.SettledPendingArchive
                    && report.reportStatus != (int)ModeHMatchReportStatus.Archived)
                {
                    continue;
                }
                if (best == null || report.matchIndex > best.matchIndex) best = report;
            }
            return best;
        }

        #endregion

        #region 接受与拒绝

        /// <summary>
        /// 接受 offer：用它替换当前 `contractSub`。确认后不可反悔。
        /// `contractMain` 永远不可被替换。
        /// </summary>
        public static bool TryAcceptOffer(
            ModeHSeasonDto season, string offerId, out string failureReasonId)
        {
            failureReasonId = null;
            if (season == null || season.currentOffer == null || season.contract == null)
            {
                failureReasonId = "market_offer_missing";
                return false;
            }
            ModeHOfferDto offer = season.currentOffer;
            if (!string.Equals(offer.offerId, offerId, StringComparison.Ordinal))
            {
                failureReasonId = "market_offer_id_mismatch";
                return false;
            }
            if (offer.status != (int)ModeHOfferStatus.Pending)
            {
                failureReasonId = "market_offer_not_pending";
                return false;
            }

            string previousSubId = season.contract.contractSubProfileId;
            if (!string.IsNullOrEmpty(previousSubId))
            {
                ModeHProfileDto previous = FindProfile(season, previousSubId);
                if (previous != null && previous.status != (int)ModeHParticipantStatus.Retired)
                {
                    // 被替换且未退役 -> Released，本季不可再次签回
                    previous.status = (int)ModeHParticipantStatus.Released;
                }
            }

            season.contract.contractSubProfileId = offer.profileId;
            EnsureProfilePresent(season, offer.profileId);
            offer.status = (int)ModeHOfferStatus.Accepted;
            return true;
        }

        /// <summary>拒绝 offer：本季不再返回同一 offer。</summary>
        public static bool TryRejectOffer(
            ModeHSeasonDto season, string offerId, out string failureReasonId)
        {
            failureReasonId = null;
            if (season == null || season.currentOffer == null)
            {
                failureReasonId = "market_offer_missing";
                return false;
            }
            if (!string.Equals(season.currentOffer.offerId, offerId, StringComparison.Ordinal))
            {
                failureReasonId = "market_offer_id_mismatch";
                return false;
            }
            if (season.currentOffer.status != (int)ModeHOfferStatus.Pending)
            {
                failureReasonId = "market_offer_not_pending";
                return false;
            }
            season.currentOffer.status = (int)ModeHOfferStatus.Rejected;
            return true;
        }

        /// <summary>窗口关闭时把未处理的 offer 标记为过期（不使用单个 accepted 布尔）。</summary>
        public static void ExpireOffer(ModeHSeasonDto season)
        {
            if (season == null || season.currentOffer == null) return;
            if (season.currentOffer.status != (int)ModeHOfferStatus.Pending) return;
            season.currentOffer.status = (int)ModeHOfferStatus.Expired;
        }

        #endregion

        #region 合同角色变更

        /// <summary>
        /// 退役结算（§17.3）：
        /// - 只有 `contractSub` 退役 -> `contractMain` 保留，替补槽置空；
        /// - `contractMain` 退役且 `contractSub` 存活 -> 后者晋升，替补槽置空；
        /// - 替补槽已空且唯一 `contractMain` 退役 -> 赛季立即 `SeasonEnded`（返回 false）；
        /// - 两名合同选手同场退役 -> 赛季立即结束，不生成应急第三人。
        /// </summary>
        public static bool ApplyRetirement(ModeHSeasonDto season, out string failureReasonId)
        {
            failureReasonId = null;
            if (season == null || season.contract == null)
            {
                failureReasonId = "retire_contract_missing";
                return false;
            }

            bool mainRetired = IsRetired(season, season.contract.contractMainProfileId);
            bool subRetired = IsRetired(season, season.contract.contractSubProfileId);
            bool hasSub = !string.IsNullOrEmpty(season.contract.contractSubProfileId);

            if (mainRetired && (subRetired || !hasSub))
            {
                failureReasonId = "retire_season_ended";
                return false;
            }
            if (mainRetired)
            {
                // 无论本场是先发还是接力，只要 contractSub 存活就晋升
                season.contract.contractMainProfileId = season.contract.contractSubProfileId;
                season.contract.contractSubProfileId = string.Empty;
                return true;
            }
            if (subRetired)
            {
                season.contract.contractSubProfileId = string.Empty;
                return true;
            }
            return true;
        }

        private static bool IsRetired(ModeHSeasonDto season, string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return false;
            ModeHProfileDto profile = FindProfile(season, profileId);
            return profile != null && profile.status == (int)ModeHParticipantStatus.Retired;
        }

        /// <summary>存活合同选手列表（roster 不变式的输入）。</summary>
        public static List<string> GetLiveContractProfileIds(ModeHSeasonDto season)
        {
            List<string> live = new List<string>();
            if (season == null || season.contract == null) return live;
            AppendIfLive(season, season.contract.contractMainProfileId, live);
            AppendIfLive(season, season.contract.contractSubProfileId, live);
            return live;
        }

        private static void AppendIfLive(ModeHSeasonDto season, string profileId, List<string> live)
        {
            if (string.IsNullOrEmpty(profileId)) return;
            ModeHProfileDto profile = FindProfile(season, profileId);
            if (profile == null) return;
            if (profile.status == (int)ModeHParticipantStatus.Retired
                || profile.status == (int)ModeHParticipantStatus.Released
                || profile.status == (int)ModeHParticipantStatus.Removed)
            {
                return;
            }
            live.Add(profileId);
        }

        #endregion

        #region 辅助

        /// <summary>市场签入的新合同选手必须在 Season 的 profile 集合内。</summary>
        private static void EnsureProfilePresent(ModeHSeasonDto season, string profileId)
        {
            if (season.profiles == null) season.profiles = new List<ModeHProfileDto>();
            if (FindProfile(season, profileId) != null) return;

            // 只从已抽取的候选档案里补齐，绝不运行时临时生成新 Boss
            ModeHProfileTemplate template = ModeHProfileRegistry.GetByTemplateId(profileId);
            if (template == null) return;

            ModeHProfileDto dto = new ModeHProfileDto();
            dto.profileId = template.ProfileTemplateId;
            dto.stableKey = template.StableKey;
            dto.displayNameKey = template.DisplayNameKey;
            dto.archetypeId = template.ArchetypeId;
            dto.temperamentId = template.TemperamentId;
            dto.quirkId = template.QuirkId != null ? template.QuirkId : string.Empty;
            dto.anomalyId = template.AnomalyId != null ? template.AnomalyId : string.Empty;
            dto.signatureCommandId = template.SignatureCommandId;
            dto.rumorKey = template.RumorKey;
            dto.standInPatternId = template.StandInPatternId;
            dto.status = (int)ModeHParticipantStatus.Available;
            dto.injuryId = string.Empty;
            dto.scarIds = new List<string>();
            dto.behaviorStatuses = new List<ModeHBehaviorStatusDto>();
            season.profiles.Add(dto);
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
