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
            ModeHMatchReportDto report = _lastSettlementReport ?? FindLatestPendingReport();
            ModeHSeasonRewardOperationDto operation = _lastRewardOperation
                ?? FindRewardOperation(report != null ? report.seasonRewardOperationId : null);
            if (report == null)
            {
                page.Body = L10n.T("结算记录不可用", "Settlement record unavailable");
                return page;
            }

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
                        Label = L10n.T("解锁整备：", "Unlock kit: ") + kitId,
                        OnClick = delegate { SelectSettlementReward(selectedKitId, false); },
                    });
                }
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T("放弃整备，换取名声", "Decline kits for fame"),
                    OnClick = delegate { SelectSettlementReward(null, true); },
                });
            }
            else
            {
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                    OnClick = CompleteSettlementAndRoute,
                });
            }
            return page;
        }

        private void SelectSettlementReward(string kitId, bool decline)
        {
            if (_season == null || _lastRewardOperation == null || _runState == null
                || _runState.Lifecycle != ModeHLifecycle.Intermission)
            {
                return;
            }
            string failureReasonId;
            bool ok = decline
                ? ModeHSeasonRewardService.TryDeclineToFame(
                    _season, _lastRewardOperation.operationId, out failureReasonId)
                : ModeHSeasonRewardService.TrySelectKit(
                    _season, _lastRewardOperation.operationId, kitId, out failureReasonId);
            if (!ok)
            {
                ModBehaviour.DevLog("[ModeH] 奖励选择失败: " + (failureReasonId ?? "unknown"));
                return;
            }
            CompleteSettlementAndRoute();
        }

        private void CompleteSettlementAndRoute()
        {
            if (_season == null || _runState == null
                || _runState.Lifecycle != ModeHLifecycle.Intermission)
            {
                return;
            }
            ModeHMatchReportDto report = _lastSettlementReport ?? FindLatestPendingReport();
            ModeHSeasonRewardOperationDto operation = _lastRewardOperation
                ?? FindRewardOperation(report != null ? report.seasonRewardOperationId : null);
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
            if (!TryPersistSeason("intermission_archive"))
            {
                RequestSuspended("intermission_archive_failed");
                return;
            }
            RouteAfterIntermission(report);
        }

        private void RouteAfterIntermission(ModeHMatchReportDto report)
        {
            List<string> live = ModeHTransferMarket.GetLiveContractProfileIds(_season);
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
    }
}
