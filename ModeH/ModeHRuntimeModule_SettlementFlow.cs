// Mode H 结算呈现与休整路由：结算页、奖励选择、归档与下一场分流。
//
// 从 ModeHRuntimeModule_CombatFlow.cs **逐字**提取，行为完全不变；拆分只为
// 单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行，CombatFlow 已贴顶）。
// 语义上仍是同一个 partial class，与 Config.cs / ConfigModConfigKeys.cs 同一处理方式。
using System;
using System.Collections.Generic;
namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {

        private ModeHPageContent BuildCompletedSettlementPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Settlement");
            ModeHMatchReportDto report = _runState != null ? FindLatestPendingReport() : null;
            ModeHSeasonRewardOperationDto operation = FindRewardOperation(
                report != null ? report.seasonRewardOperationId : null);
            if (report == null)
            {
                page.Body = L10n.T("结算记录不可用", "Settlement record unavailable");
                return page;
            }

            // 页面与动作都按本场持久记录定位；恢复时 _last* 仅是空的展示缓存。
            long ownerToken = _runState.OwnerToken;
            int matchIndex = report.matchIndex;
            string operationId = report.seasonRewardOperationId;

            bool won = report.winner == (int)ModeHMatchOutcome.PlayerVictory;
            page.Body = won ? L10n.T("本场胜利", "Victory") : L10n.T("本场失利", "Defeat");
            page.Lines.Add(L10n.T("耗时：", "Time: ") + report.elapsedSeconds.ToString("0.0") + "s");
            page.Lines.Add(L10n.T("赔率：x", "Odds: x") + report.lockedOdds
                + L10n.T("　下注：", "  Stake: ") + report.virtualStakeAmount);
            page.Lines.Add(L10n.T("筹码：", "Credits: ") + report.virtualStakeBalanceBefore
                + " → " + report.virtualStakeBalanceAfter);
            if (report.injuryEvents != null && report.injuryEvents.Count > 0)
            {
                page.Lines.Add(L10n.T("倒地伤病：", "Down injuries: ") + report.injuryEvents.Count);
                // 本场直接退役的（带伤再登场又被击倒）单列一行：这是赛季级后果，
                // 与「又添一条伤」不是一回事，混在计数里玩家看不出选手已经没了。
                for (int i = 0; i < report.injuryEvents.Count; i++)
                {
                    ModeHInjuryEventDto evt = report.injuryEvents[i];
                    if (evt == null || !evt.retired) continue;
                    page.Lines.Add(ResolveProfileDisplayName(evt.profileId) + "　"
                        + L10n.T(ModeHConfig.LocalizationKeyPrefix + "Injury_Retired"));
                }
            }
            // 完整休息：带伤但本场从未登场，赛后已解除带伤。不展示的话玩家无从判断
            // 「把他按在替补席」这个决定到底有没有生效。
            for (int i = 0; i < _restedProfileIds.Count; i++)
            {
                page.Lines.Add(ResolveProfileDisplayName(_restedProfileIds[i]) + "　"
                    + L10n.T(ModeHConfig.LocalizationKeyPrefix + "Injury_Rested"));
            }

            // 战痕候选先于整备呈现：它是不可逆的，且满三条时要玩家指名替换哪一条。
            // 此前这一步在 BeginMatchSettlement 里被替玩家做掉了，结算页只字不提。
            BuildSettlementScarActions(page, report);

            if (operation != null
                && operation.status == (int)ModeHSeasonRewardOperationStatus.Offered
                && operation.candidateKitIds != null)
            {
                for (int i = 0; i < operation.candidateKitIds.Count; i++)
                {
                    string kitId = operation.candidateKitIds[i];
                    string selectedKitId = kitId;
                    page.Actions.Add(new ModeHActionData
                    {
                        // kitId 是内部 ID（如 "assault_starter"）。32 条 Kit_ 文案早已注入，
                        // 此前直接拼原文，玩家看到的是一串英文下划线标识。
                        Label = L10n.T("解锁整备：", "Unlock kit: ")
                            + L10n.T(ModeHConfig.LocalizationKeyPrefix + "Kit_" + kitId),
                        OnClick = delegate { SelectSettlementReward(selectedKitId, false, ownerToken, matchIndex, operationId); },
                    });
                }
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("放弃整备，换取名声", "Decline kits for fame"),
                    OnClick = delegate { SelectSettlementReward(null, true, ownerToken, matchIndex, operationId); },
                });
            }
            else
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                    OnClick = delegate
                    {
                        if (IsSettlementActionCurrent(ownerToken, matchIndex, operationId))
                            CompleteSettlementAndRoute();
                    },
                });
            }
            return page;
        }

        private bool IsSettlementActionCurrent(long ownerToken, int matchIndex, string operationId)
        {
            if (_commandsClosed || _season == null || _runState == null
                || !_runState.IsOwnerTokenValid(ownerToken)
                || _runState.MatchIndex != matchIndex
                || _runState.Lifecycle != ModeHLifecycle.Intermission) return false;
            ModeHMatchReportDto report = FindLatestPendingReport();
            return report != null && string.Equals(
                report.seasonRewardOperationId, operationId, StringComparison.Ordinal);
        }

        private void SelectSettlementReward(
            string kitId, bool decline, long ownerToken, int matchIndex, string operationId)
        {
            if (!IsSettlementActionCurrent(ownerToken, matchIndex, operationId)) return;
            ModeHSeasonRewardOperationDto operation = FindRewardOperation(operationId);
            if (operation == null
                || operation.status != (int)ModeHSeasonRewardOperationStatus.Offered) return;
            string failureReasonId;
            bool ok = decline
                ? ModeHSeasonRewardService.TryDeclineToFame(
                    _season, operation.operationId, out failureReasonId)
                : ModeHSeasonRewardService.TrySelectKit(
                    _season, operation.operationId, kitId, out failureReasonId);
            if (!ok)
            {
                ModBehaviour.DevLog("[ModeH] 奖励选择失败: " + (failureReasonId ?? "unknown"));
                return;
            }
            CompleteSettlementAndRoute();
        }

        private void CompleteSettlementAndRoute()
        {
            if (_commandsClosed || _season == null || _runState == null
                || _runState.Lifecycle != ModeHLifecycle.Intermission)
            {
                return;
            }
            ModeHMatchReportDto report = _runState != null ? FindLatestPendingReport() : null;
            ModeHSeasonRewardOperationDto operation = FindRewardOperation(
                report != null ? report.seasonRewardOperationId : null);
            if (report == null || operation == null) return;

            string failureReasonId;
            if (operation.status == (int)ModeHSeasonRewardOperationStatus.Offered) return;
            if (operation.status == (int)ModeHSeasonRewardOperationStatus.Applied
                && !ModeHSeasonRewardService.TryArchive(
                    _season, operation.operationId, out failureReasonId))
            {
                return;
            }
            report.reportStatus = (int)ModeHMatchReportStatus.Archived;
            if (!TryPersistSeason("intermission_archive", true))
            {
                RequestSuspended("intermission_archive_failed");
                return;
            }
            RouteAfterIntermission(report);
        }

        private void RouteAfterIntermission(ModeHMatchReportDto report)
        {
            List<string> live = ModeHTransferMarket.GetLiveContractProfileIds(_season);
            if ((live == null || live.Count == 0 || _runState.MatchIndex >= ModeHConfig.SeasonMatchCount)
                && HasPendingScarOffers())
            {
                // 中途可把候选留到下一场结算；赛季终局之前必须处理，避免新赛季覆盖奖励。
                if (_owner != null) _owner.ShowMessage(L10n.T(
                    "赛季结束前，请先处理剩余战痕候选。", "Resolve the remaining scar offers before ending the season."));
                RouteUiForLifecycle(_runState.Lifecycle);
                return;
            }
            if (live == null || live.Count == 0)
            {
                FinishSeason("no_live_contracts");
                return;
            }
            if (_runState.MatchIndex >= ModeHConfig.SeasonMatchCount)
            {
                if (report.winner == (int)ModeHMatchOutcome.PlayerVictory)
                {
                    EnterHallOfFame();
                }
                else
                {
                    FinishSeason("final_match_defeat");
                }
                return;
            }
            if (ModeHConfig.IsTransferWindowMatch(_runState.MatchIndex))
            {
                TryTransition(ModeHLifecycle.Intermission, ModeHLifecycle.TransferWindow,
                    "transfer_window_open");
                return;
            }
            OpenNextMatchBrief("intermission_complete");
        }

        /// <summary>
        /// 把战报里尚未处置的战痕候选摆到结算页上，由玩家决定。
        ///
        /// 三种形态：
        ///   - 未满三条：接受 / 拒绝（拒绝换稳定名声）；
        ///   - 已满三条：逐条列出「用候选替换 X」+ 拒绝——这正是 §17 冻结契约里
        ///     「满三条时明确替换一条」的落点，此前 replacedScarId 恒传 null，走不到；
        ///   - 无候选或已处置：整段不出现。
        /// </summary>
        private void BuildSettlementScarActions(ModeHPageContent page, ModeHMatchReportDto report)
        {
            if (page == null || report == null || _season == null || _runState == null
                || _season.matchReports == null) return;
            // 一次只摆一组动作，处理后再展示下一条，避免历史候选把结算按钮行撑出屏幕。
            bool legacyNoticeShown = false;
            for (int i = 0; i < _season.matchReports.Count; i++)
            {
                ModeHMatchReportDto candidate = _season.matchReports[i];
                ModeHProfileDto profile;
                if (!TryGetScarOfferProfile(candidate, out profile)) continue;
                if (!IsScarOfferPending(candidate))
                {
                    if (!legacyNoticeShown && !_runState.HasEventToken(ScarOfferToken(candidate, false))
                        && (profile.scarIds == null || !profile.scarIds.Contains(candidate.scarOfferId)))
                    {
                        // 旧版没有处置凭据：保留原战报，不猜测它是未领还是已经拒绝过。
                        page.Lines.Add(L10n.T("旧版未记录战痕是否已领取，无法确认结果。原记录已保留，本次不补发，赛季可继续。",
                            "The old version did not record whether a scar was claimed. Its record is preserved; no extra reward is granted, and the season can continue."));
                        legacyNoticeShown = true;
                    }
                    continue;
                }
                AppendScarOfferActions(page, candidate, profile);
                return;
            }
        }

        private void AppendScarOfferActions(
            ModeHPageContent page, ModeHMatchReportDto report, ModeHProfileDto profile)
        {
            string scarName = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Scar_" + report.scarOfferId);
            page.Lines.Add(L10n.T("战痕候选：", "Scar offer: ") + scarName + "　"
                + ResolveProfileDisplayName(profile.profileId) + "　#" + report.matchIndex);

            string offerId = report.scarOfferId;
            string operationId = report.seasonRewardOperationId;
            long ownerToken = _runState.OwnerToken;
            int pageMatchIndex = _runState.MatchIndex;
            bool full = profile.scarIds != null
                && profile.scarIds.Count >= ModeHConfig.MaxScarsPerProfile;

            if (!full && (profile.scarIds == null || !profile.scarIds.Contains(offerId)))
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("留下战痕：", "Take scar: ") + scarName,
                    OnClick = delegate { ResolveScarOffer(operationId, offerId, null, false, ownerToken, pageMatchIndex); },
                });
            }
            else if (full && !profile.scarIds.Contains(offerId))
            {
                for (int i = 0; i < profile.scarIds.Count; i++)
                {
                    string replaced = profile.scarIds[i];
                    page.Actions.Add(new ModeHActionData
                    {
                        Label = L10n.T("替换：", "Replace: ")
                            + L10n.T(ModeHConfig.LocalizationKeyPrefix + "Scar_" + replaced),
                        OnClick = delegate { ResolveScarOffer(operationId, offerId, replaced, false, ownerToken, pageMatchIndex); },
                    });
                }
            }

            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T("拒绝战痕，换取名声", "Decline scar for fame"),
                OnClick = delegate { ResolveScarOffer(operationId, offerId, null, true, ownerToken, pageMatchIndex); },
            });
        }

        /// <summary>候选与处置凭据复用现有事件集合，不扩展 DTO，保留 v1 摘要兼容。</summary>
        private static string ScarOfferToken(ModeHMatchReportDto report, bool resolved)
        {
            return (resolved ? "scar_resolved|" : "scar_offered|")
                + report.seasonRewardOperationId + "|" + report.scarOfferId;
        }

        private void RecordScarOffer(ModeHMatchReportDto report)
        {
            ModeHProfileDto profile;
            if (TryGetScarOfferProfile(report, out profile))
                _runState.TryApplyEventToken(ScarOfferToken(report, false));
        }

        private bool TryGetScarOfferProfile(ModeHMatchReportDto report, out ModeHProfileDto profile)
        {
            profile = null;
            if (_runState == null || report == null || string.IsNullOrEmpty(report.scarOfferId)
                || string.IsNullOrEmpty(report.resultToken) || report.matchIndex > _runState.MatchIndex)
                return false;
            ModeHSeasonRewardOperationDto operation = FindRewardOperation(report.seasonRewardOperationId);
            if (operation == null || operation.matchIndex != report.matchIndex
                || !string.Equals(operation.resultToken, report.resultToken, StringComparison.Ordinal)) return false;
            profile = FindSeasonProfile(operation.rewardProfileId);
            return profile != null;
        }

        private bool IsScarOfferPending(ModeHMatchReportDto report)
        {
            return _runState != null && report != null && !string.IsNullOrEmpty(report.scarOfferId)
                && _runState.HasEventToken(ScarOfferToken(report, false))
                && !_runState.HasEventToken(ScarOfferToken(report, true));
        }

        private bool HasPendingScarOffers()
        {
            if (_season == null || _season.matchReports == null) return false;
            for (int i = 0; i < _season.matchReports.Count; i++)
            {
                ModeHProfileDto profile;
                if (IsScarOfferPending(_season.matchReports[i])
                    && TryGetScarOfferProfile(_season.matchReports[i], out profile)) return true;
            }
            return false;
        }

        /// <summary>
        /// 以页面 owner、当前场次、历史 operation 和候选共同验证动作。
        /// 收益与 resolved token 同一 Season 写屏障；失败保留两者并挂起，恢复只补写不重复收益。
        /// </summary>
        private void ResolveScarOffer(string operationId, string scarId, string replacedScarId, bool decline,
            long ownerToken, int pageMatchIndex)
        {
            if (_commandsClosed || _season == null || _runState == null
                || !_runState.IsOwnerTokenValid(ownerToken) || _runState.MatchIndex != pageMatchIndex
                || _runState.Lifecycle != ModeHLifecycle.Intermission || _season.matchReports == null) return;
            try
            {
                ModeHMatchReportDto report = null;
                for (int i = 0; i < _season.matchReports.Count; i++)
                {
                    ModeHMatchReportDto candidate = _season.matchReports[i];
                    if (candidate != null && string.Equals(candidate.seasonRewardOperationId, operationId, StringComparison.Ordinal)
                        && string.Equals(candidate.scarOfferId, scarId, StringComparison.Ordinal))
                    { report = candidate; break; }
                }
                ModeHProfileDto profile;
                if (!IsScarOfferPending(report) || !TryGetScarOfferProfile(report, out profile)) return;
                ModeHProfileDto updated = CloneProfile(profile);
                if (decline)
                {
                    ModeHInjuryAndScarSystem.DeclineScar(updated);
                }
                else
                {
                    string acceptFailure;
                    if (!ModeHInjuryAndScarSystem.TryAcceptScar(
                            updated, scarId, replacedScarId, out acceptFailure))
                    {
                        ModBehaviour.DevLog("[ModeH] 战痕处置失败: "
                            + (acceptFailure ?? "unknown"));
                        if (_owner != null)
                        {
                            _owner.ShowMessage(
                                L10n.T(ModeHAvailability.GetReasonLocalizationKey(acceptFailure)));
                        }
                        return;
                    }
                }

                if (!_runState.TryApplyEventToken(ScarOfferToken(report, true))) return;
                ReplaceSeasonProfile(updated);
                if (!TryPersistSeason("scar_resolved", true))
                {
                    RequestSuspended("scar_resolution_persist_failed");
                    return;
                }
                if (_runState != null) RouteUiForLifecycle(_runState.Lifecycle);
            }
            catch (Exception e)
            {
                LogFailure("resolve_scar", e);
                RequestSuspended("scar_resolution_exception");
            }
        }
    }
}
