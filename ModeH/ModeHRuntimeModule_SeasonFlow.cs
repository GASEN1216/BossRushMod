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

            // 转会页走 CreateCardGrid，而那条渲染分支只画 Cards、从不读 page.Body。
            // 此前这里只 Add 了确认/取消两个按钮，玩家看到的是「标题 + 两个按钮」，
            // 对报价对象一无所知却要做不可逆的换人决定。复用选秀卡的构建器补上卡片。
            ModeHProfileDto offered = FindSeasonProfile(offer.profileId);
            if (offered != null)
            {
                ModeHCardData card = BuildProfileCard(offered);
                // 转会卡不走「签下」点击：确认/取消由下面两个动作按钮承担，
                // 卡片本身只负责把报价对象讲清楚。
                card.ActionLabel = null;
                card.OnClick = null;
                page.Cards.Add(card);
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

        /// <summary>
        /// 名人堂页。
        ///
        /// 此前这一页**只渲染标题和一个「确认」按钮**：记录一直在写、32 席上限也一直在守，
        /// 但 `ModeHHallOfFamePersistence.GetRecords()` 零调用，玩家打完整季进了名人堂，
        /// 看到的是一张空页。而「名人堂只有 32 席、第 33 个进来最底下那个被挤掉」正是
        /// 鸭王征程整条剧情线的锚点——看不见它，那条剧情就落在空处。
        ///
        /// `ModeHPageContent.Cards` 的注释本来就写着「五席候选、市场 offer、**名人堂条目**」，
        /// 这里补上它缺的第三种用法。
        ///
        /// 排序由 GetRecords 负责（createdUtc + hallOfFameId 稳定序），本方法不再重排。
        /// no-throw：读盘失败只让这一页少几张卡，不能挡住玩家点「确认」结束赛季——
        /// 那是赛季终局的唯一出口。
        /// </summary>
        private ModeHPageContent BuildHallOfFamePageContent()
        {
            ModeHPageContent page = new ModeHPageContent();
            page.Title = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_HallOfFame");

            try
            {
                List<ModeHHallOfFameRecordDto> records = ModeHHallOfFamePersistence.GetRecords();
                int count = records != null ? records.Count : 0;
                page.Body = L10n.T("已入堂 ", "Inducted ") + count
                    + " / " + ModeHConfig.MaxHallOfFameRecords
                    + L10n.T(" 席", " seats");

                for (int i = 0; i < count; i++)
                {
                    ModeHCardData card = BuildHallOfFameCard(records[i]);
                    if (card != null) page.Cards.Add(card);
                }
            }
            catch (Exception e)
            {
                // 读不出记录不该挡住赛季收尾：正文退回一句说明，按钮照常给
                page.Body = L10n.T("名人堂记录暂时读取不到", "Hall of Fame records are unavailable");
                LogFailure("hall_of_fame_records", e);
            }

            page.Actions.Add(new ModeHActionData
            {
                Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                OnClick = delegate { FinishSeason("hall_of_fame_ack"); },
            });
            return page;
        }

        /// <summary>
        /// 一条名人堂记录 -> 一张只读卡。
        ///
        /// 冠军名优先用记录自带的 `aliasKey`（外号），没有再回落到选手名 key；
        /// 两者都缺时用 profileId 兜底，绝不显示空标题。
        /// `IsAnomaly` 让带异常的冠军换成 Warning 描边，与选秀卡同一套视觉口径。
        /// </summary>
        private ModeHCardData BuildHallOfFameCard(ModeHHallOfFameRecordDto record)
        {
            if (record == null) return null;
            string prefix = ModeHConfig.LocalizationKeyPrefix;

            ModeHProfileDto snapshot = record.championProfileSnapshot;
            string profileId = snapshot != null ? snapshot.profileId : null;

            ModeHCardData card = new ModeHCardData();
            if (!string.IsNullOrEmpty(record.aliasKey))
            {
                card.Title = L10n.T(record.aliasKey);
            }
            else if (!string.IsNullOrEmpty(profileId))
            {
                card.Title = L10n.T(prefix + "Fighter_" + profileId);
            }
            else
            {
                card.Title = L10n.T("无名冠军", "Unnamed champion");
            }

            List<string> subtitle = new List<string>(2);
            if (!string.IsNullOrEmpty(record.archetypeId))
            {
                subtitle.Add(L10n.T(prefix + "Archetype_" + record.archetypeId));
            }
            if (!string.IsNullOrEmpty(record.temperamentId))
            {
                subtitle.Add(L10n.T(prefix + "Temperament_" + record.temperamentId));
            }
            card.Subtitle = string.Join(" · ", subtitle.ToArray());

            List<string> body = new List<string>(4);
            if (!string.IsNullOrEmpty(record.signatureCommandId))
            {
                body.Add(L10n.T(prefix + "Command_" + record.signatureCommandId));
            }
            if (record.maxOddsWin > 0)
            {
                body.Add(L10n.T("最高赔率胜 x", "Best odds win x") + record.maxOddsWin);
            }
            if (record.maxVirtualStakeWin > 0)
            {
                body.Add(L10n.T("最高筹码胜 ", "Best credit win ") + record.maxVirtualStakeWin);
            }
            int scarCount = record.scarIds != null ? record.scarIds.Count : 0;
            if (scarCount > 0)
            {
                body.Add(L10n.T("战痕 ", "Scars ") + scarCount);
            }
            card.Body = string.Join("　", body.ToArray());

            card.IsAnomaly = !string.IsNullOrEmpty(record.anomalyId);
            return card;
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
