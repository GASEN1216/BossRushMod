// ============================================================================
// ModeHRuntimeModule_SeasonFlow.cs - Mode H 幕间路由与赛季终局（设计提案 §17.7-§17.8、§18.2）
// ============================================================================
// 承载 Intermission 之后的路由、转会窗口与名人堂两段提交。
//
// 冻结的出口优先级（§18.2）：
//   无存活合同选手 -> SeasonEnded
//   第 6 场胜利且有存活 -> HallOfFame -> SeasonEnded
//   第 6 场失败 -> SeasonEnded
//   第 2/4 场 -> TransferWindow
//   第 1/3/5 场 -> 下一场 MatchBrief
//
// matchIndex **只**在真正打开下一场 MatchBrief 时推进一格，且必须小于 SeasonMatchCount。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        #region 转会窗口

        private ModeHPageContent BuildTransferPageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Transfer");
            if (_season == null) return page;

            ModeHOfferDto offer = EnsureTransferOffer();
            if (offer == null)
            {
                page.Body = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Summary_NoOffer");
                page.Actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                    OnClick = delegate { CloseTransferWindow("no_offer"); },
                });
                return page;
            }

            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                OnClick = delegate { AcceptTransferOffer(offer); },
            });
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Cancel"),
                OnClick = delegate { RejectTransferOffer(offer); },
            });
            return page;
        }

        /// <summary>同一窗口内只抽一次 offer：关页重开不得重抽（§17.7）。</summary>
        private ModeHOfferDto EnsureTransferOffer()
        {
            if (_season == null || _runState == null) return null;
            if (_season.currentOffer != null) return _season.currentOffer;

            try
            {
                string failureReasonId;
                ModeHOfferDto offer = ModeHTransferMarket.BuildOffer(
                    _season, _runState.MatchIndex, out failureReasonId);
                if (offer == null)
                {
                    ModBehaviour.DevLog("[ModeH] 转会 offer 生成为空: "
                        + (failureReasonId != null ? failureReasonId : "none"));
                    return null;
                }
                _season.currentOffer = offer;
                TryPersistSeason("transfer_offer");
                return offer;
            }
            catch (Exception e)
            {
                LogFailure("build_offer", e);
                return null;
            }
        }

        private void AcceptTransferOffer(ModeHOfferDto offer)
        {
            if (_commandsClosed || _season == null || offer == null) return;
            if (_runState == null || _runState.Lifecycle != ModeHLifecycle.TransferWindow) return;

            try
            {
                string failureReasonId;
                if (!ModeHTransferMarket.TryAcceptOffer(_season, offer.offerId, out failureReasonId))
                {
                    ModBehaviour.DevLog("[ModeH] 接受 offer 失败: "
                        + (failureReasonId != null ? failureReasonId : "unknown"));
                    return;
                }
                CloseTransferWindow("offer_accepted");
            }
            catch (Exception e)
            {
                LogFailure("accept_offer", e);
            }
        }

        private void RejectTransferOffer(ModeHOfferDto offer)
        {
            if (_commandsClosed || _season == null || offer == null) return;
            if (_runState == null || _runState.Lifecycle != ModeHLifecycle.TransferWindow) return;

            try
            {
                string failureReasonId;
                ModeHTransferMarket.TryRejectOffer(_season, offer.offerId, out failureReasonId);
                CloseTransferWindow("offer_rejected");
            }
            catch (Exception e)
            {
                LogFailure("reject_offer", e);
            }
        }

        /// <summary>
        /// 接受 / 拒绝 / 过期走同一个出口（§18.2）：清 offer、落盘、打开下一场看盘。
        /// </summary>
        private void CloseTransferWindow(string reasonId)
        {
            if (_season == null || _runState == null) return;
            try
            {
                ModeHTransferMarket.ExpireOffer(_season);
                _season.currentOffer = null;
                TryPersistSeason("transfer_closed");
                OpenNextMatchBrief(reasonId);
            }
            catch (Exception e)
            {
                LogFailure("close_transfer", e);
            }
        }

        #endregion

        #region 下一场

        /// <summary>
        /// 打开下一场看盘。**这里是 matchIndex 唯一的推进点**（§18.2）：
        /// 只有真正开下一场时才 +1，且必须小于 SeasonMatchCount。
        /// </summary>
        private void OpenNextMatchBrief(string reasonId)
        {
            if (_season == null || _runState == null) return;
            try
            {
                string failureReasonId;
                if (!_runState.TryAdvanceMatchIndex(out failureReasonId))
                {
                    // 已经是最后一场：按赛季结束路由，不是错误
                    FinishSeason(failureReasonId != null ? failureReasonId : "season_complete");
                    return;
                }

                // 新一场的敌军计划必须原子替换，不能留上一场的残留
                _season.currentMatchPlan = null;
                _season.currentLoadoutLock = null;
                _season.preMatchSnapshot = null;

                if (TryTransition(_runState.Lifecycle, ModeHLifecycle.MatchBrief, reasonId))
                {
                    TryPersistSeason("match_brief");
                }
            }
            catch (Exception e)
            {
                LogFailure("open_next_brief", e);
            }
        }

        #endregion

        #region 名人堂与赛季终局

        private ModeHPageContent BuildHallOfFamePageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_HallOfFame");
            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                OnClick = delegate { FinishSeason("hall_of_fame_ack"); },
            });
            return page;
        }

        /// <summary>赛季终局：唯一允许把持久 lifecycle 写成 None 的路径之一（§18.2）。</summary>
        private void FinishSeason(string reasonId)
        {
            if (_runState == null) return;
            try
            {
                if (_runState.Lifecycle != ModeHLifecycle.SeasonEnded)
                {
                    if (!TryTransition(_runState.Lifecycle, ModeHLifecycle.SeasonEnded, reasonId))
                    {
                        return;
                    }
                }
                TryPersistSeason("season_ended");
                RequestExit(ModeHExitReason.SeasonComplete, reasonId);
            }
            catch (Exception e)
            {
                LogFailure("finish_season", e);
            }
        }

        #endregion
    }
}
