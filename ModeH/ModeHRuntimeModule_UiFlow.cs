// ============================================================================
// ModeHRuntimeModule_UiFlow.cs - Mode H 状态投影与界面路由（设计提案 §18.2、§20.3、§23.1、§25.3）
// ============================================================================
// 补上四个 partial 接入点中的 OnTransitionApplied，并持有 UI 与恢复壳。
//
// 落盘策略（本文件的核心裁决）：
//   OnTransitionApplied **只做三件事** —— 把 runState 投影进 Season、按 lifecycle 路由页面、
//   标脏。它**绝不**自己调 RequestSeasonWrite。
//   理由：§20.3 要求每批至多一次 SaveFile，§17.8 要求 MatchSettling 是唯一原子全量写入点；
//   而状态转换在一局里有几十次，逐次落盘既违反上面两条，也把写放大推到不可控。
//   真正的落盘发生在少数几个显式点（见 MarkSeasonDirty 的注释）。
//
// 页面纪律（§25.3）：页面自身不读全局状态，内容与按钮回调全部由这里组装，
// 保证每个按钮绑定的是当前 lifecycle 与 owner token。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        #region UI 字段

        /// <summary>正式 UI（HUD / 诊断 / 六个模态页共用同一个 modal lease）。</summary>
        private ModeHUI _ui;

        /// <summary>恢复壳。整个模式只有一个实例。</summary>
        private ModeHRecoveryPanel _recoveryPanel;

        /// <summary>
        /// 当前正在播放的奖励揭晓实例根节点。
        /// 恢复壳要在显示前终止奖励揭晓，但只能终止**我们自己**播的那一个——
        /// 全场 FindObjectsOfType 会把原版许愿池正在放的动画一并销毁。
        /// </summary>
        private UnityEngine.GameObject _activeRewardRevealRoot;

        /// <summary>
        /// 自动流程（选人后一路开打、结算默认值）进行中：页面相位的路由先不建页，结束后统一路由一次。
        /// 只挡「开页面」，入场收页 / 开 HUD、恢复壳、挂起照常路由。纯运行时，不落盘。
        /// </summary>
        private bool _deferPageRoutes;

        /// <summary>恢复壳里上一个操作的结果（取回押品成功 / 失败），下一次打开恢复壳时写在最上面。纯运行时。</summary>
        private string _recoveryResultText;

        /// <summary>结算页上「已自动处理」的说明行，按奖励 operation 归属；换场自动作废。纯运行时。</summary>
        private readonly List<string> _settlementNotes = new List<string>();
        private string _settlementNotesOperationId;

        #endregion

        #region UI 生命周期

        private void EnsureUi()
        {
            if (_ui == null) _ui = new ModeHUI();
        }

        private void DestroyUi()
        {
            try
            {
                if (_ui != null) _ui.DestroyAll();
            }
            catch (Exception e)
            {
                LogFailure("ui_destroy", e);
            }
            _ui = null;

            try
            {
                // 整体销毁：立即销毁，不淡出（展示 bundle 可能紧接着卸载）
                if (_recoveryPanel != null) _recoveryPanel.Hide(true);
            }
            catch (Exception e)
            {
                LogFailure("recovery_hide", e);
            }
            _recoveryPanel = null;
            _activeRewardRevealRoot = null;
        }

        #endregion

        #region 状态投影

        partial void OnTransitionApplied(ModeHTransitionRecord record)
        {
            try
            {
                ProjectRunStateIntoSeason();
                MarkSeasonDirty();
                // 战斗相位内顺延物理落盘（照 ModeG 的 IsModeGHostFileWriteDeferred）：
                // 战场快照每 10 秒就要写一次整档 + 备份复制，正打着会卡帧。
                ModeHRuntimeGates.SetCombatFrameActive(IsCombatLifecycle(record.To));
                RouteUiForLifecycle(record.To);
            }
            catch (Exception e)
            {
                LogFailure("transition_applied", e);
            }
        }

        /// <summary>正在打的相位（物理落盘要顺延到这之外）。</summary>
        private static bool IsCombatLifecycle(ModeHLifecycle lifecycle)
        {
            return lifecycle == ModeHLifecycle.MatchFighting
                || lifecycle == ModeHLifecycle.RelayPending;
        }

        /// <summary>
        /// 把内存 run owner 投影进 Season payload。
        /// owner token 是 runtime-only 的，ToDto 不会写它（§20.1）。
        /// </summary>
        private void ProjectRunStateIntoSeason()
        {
            if (_season == null || _runState == null) return;
            _season.runState = _runState.ToDto();
            _season.appliedEventTokenIds = _runState.ExportEventTokens();
        }

        /// <summary>
        /// 标记 Season 有未落盘改动。
        ///
        /// 真正落盘只发生在这些点，其余状态转换只标脏：
        /// Drafting 创建 / RosterLocked / MatchBrief / LoadoutLocked / 战斗内四类快照采集 /
        /// MatchSettling（唯一原子全量写入）/ Intermission 归档 / TransferWindow 决议 /
        /// HallOfFame 两段 / SeasonEnded / 进入 Suspended。
        /// 中间态（LoadoutEditing、OddsPreview 等）不写盘不丢任何契约保障：
        /// §20.3 的恢复规则规定战前无事实 token 时一律回落到同一场 MatchBrief。
        /// </summary>
        private void MarkSeasonDirty()
        {
            _seasonDirty = true;
        }

        /// <summary>把当前 Season 落盘一次（显式落盘点专用）。失败只标记，不抛。</summary>
        private bool TryPersistSeason(string reasonId)
        {
            return TryPersistSeason(reasonId, false);
        }

        private bool TryPersistSeason(string reasonId, bool requireDurable)
        {
            if (_season == null) return false;
            try
            {
                ProjectRunStateIntoSeason();
                string error;
                if (!ModeHSaveFlushCoordinator.RequestSeasonWrite(_season, out error, requireDurable))
                {
                    ModBehaviour.DevLog("[ModeH] [WARNING] Season 落盘失败 (" + reasonId + "): "
                        + (error != null ? error : "unknown"));
                    return false;
                }
                _seasonDirty = false;
                return true;
            }
            catch (Exception e)
            {
                LogFailure("persist_season", e);
                return false;
            }
        }

        #endregion

        #region 页面路由

        /// <summary>
        /// 按 lifecycle 决定该显示什么。页面内容在各自的 Build* 里组装，
        /// 本方法只负责「什么时候开、什么时候关」。
        /// </summary>
        private void RouteUiForLifecycle(ModeHLifecycle lifecycle)
        {
            if (_commandsClosed) return;
            EnsureUi();
            if (_ui == null) return;

            // 离开恢复通道时收起恢复壳。DriveRecovery 会把 Recovering 推回同一场看盘，
            // 壳不收起来就会盖在新页面上（恢复壳不占模态输入，不收也不会锁死，但会挡视线）。
            if (lifecycle != ModeHLifecycle.Recovering
                && lifecycle != ModeHLifecycle.ErrorRecoveryPending
                && lifecycle != ModeHLifecycle.Suspended)
            {
                HideRecoveryShell();
            }

            // 自动流程的中间相位不建页（名单、看盘、整备、赔率一闪而过），链条结束后统一路由一次
            if (_deferPageRoutes && IsPageLifecycle(lifecycle)) return;

            // 押物品选择页盖在挂押注行的那几页上；离开这些相位（开打、转会、名人堂）就收起，不留到下一次
            if (!IsItemBetPickerHost(lifecycle)) _showItemBetPicker = false;
            if (_showItemBetPicker)
            {
                OpenPage(ModeHPage.ItemBet, BuildItemBetPickerPage());
                return;
            }

            switch (lifecycle)
            {
                case ModeHLifecycle.Drafting:
                    OpenPage(ModeHPage.Entry, BuildDraftPageContent());
                    break;
                case ModeHLifecycle.RosterLocked:
                case ModeHLifecycle.MatchBrief:
                    OpenPage(ModeHPage.Brief, BuildBriefPageContent());
                    break;
                case ModeHLifecycle.LoadoutEditing:
                case ModeHLifecycle.OddsPreview:
                    OpenPage(ModeHPage.Odds, BuildOddsPageContent());
                    break;
                case ModeHLifecycle.MatchSpawning:
                case ModeHLifecycle.MatchFighting:
                case ModeHLifecycle.RelayPending:
                    // 战斗期只留观战 HUD，模态页必须关掉（它会暂停输入）
                    _ui.ClosePage();
                    _ui.EnsureHud(OnBellPressed);
                    _ui.SetBellCommand(ResolveCommandDisplayName(ResolveLockedCommandId()),
                        ResolveLockedCommandPlain());
                    break;
                case ModeHLifecycle.MatchSettling:
                    OpenPage(ModeHPage.Settlement, BuildSettlementPageContent());
                    break;
                case ModeHLifecycle.Intermission:
                    // 战痕 / 整备奖励按默认值自动处理，结算页只剩战报与一个「下一场」
                    ApplySettlementDefaults();
                    if (_commandsClosed || _ui == null || _runState == null
                        || _runState.Lifecycle != ModeHLifecycle.Intermission) break;
                    OpenPage(ModeHPage.Settlement, BuildSettlementPageContent());
                    break;
                case ModeHLifecycle.TransferWindow:
                    OpenPage(ModeHPage.Transfer, BuildTransferPageContent());
                    break;
                case ModeHLifecycle.HallOfFame:
                    OpenPage(ModeHPage.HallOfFame, BuildHallOfFamePageContent());
                    break;
                case ModeHLifecycle.Recovering:
                case ModeHLifecycle.ErrorRecoveryPending:
                case ModeHLifecycle.Suspended:
                    OpenRecoveryShell(_lastExitReasonId);
                    break;
                case ModeHLifecycle.SeasonEnded:
                case ModeHLifecycle.None:
                    _ui.ClosePage();
                    break;
            }
        }

        private void OpenPage(ModeHPage page, ModeHPageContent content)
        {
            if (_ui == null || _runState == null) return;
            // 就地失败提示只画一次（审查 B-11）：谁建的页谁取走
            if (content != null && !string.IsNullOrEmpty(_pageFailureText)) content.FailureText = _pageFailureText;
            _pageFailureText = null;
            _ui.OpenPage(page, _runState.Lifecycle, _runState.RunId, content);
        }

        /// <summary>会弹模态页的相位（自动流程期间先不建页）。</summary>
        private static bool IsPageLifecycle(ModeHLifecycle lifecycle)
        {
            switch (lifecycle)
            {
                case ModeHLifecycle.Drafting:
                case ModeHLifecycle.RosterLocked:
                case ModeHLifecycle.MatchBrief:
                case ModeHLifecycle.LoadoutEditing:
                case ModeHLifecycle.OddsPreview:
                case ModeHLifecycle.MatchSettling:
                case ModeHLifecycle.Intermission:
                case ModeHLifecycle.TransferWindow:
                case ModeHLifecycle.HallOfFame:
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region 自动开打（2026-09-23：选完人就开打，场间最多按一个键）

        /// <summary>
        /// 自动开打链：先做 firstStep（选人签约 / 结算归档 / 转会决议），再从当前相位一路推到入场：
        /// RosterLocked → 第一场看盘 → 整备 → 赔率 → 按默认值锁盘 → 入场。每一步都是原有的命令方法，
        /// 走原有冻结转换边与落盘点，这里只负责「不停下来等玩家点」。
        ///
        /// 中间相位不建页面（<see cref="_deferPageRoutes"/>）；链条停在哪一步——锁盘被拒、技术重试、
        /// 转会窗口、名人堂——结束后就按那一步正常路由一次，玩家看到的就是那一页。
        /// </summary>
        private void RunAutoAdvance(string reasonId, Action firstStep)
        {
            if (_commandsClosed || _runState == null) return;
            bool outer = !_deferPageRoutes;
            _deferPageRoutes = true;
            try
            {
                if (firstStep != null) firstStep();
                AdvanceTowardsFight();
            }
            catch (Exception e)
            {
                LogFailure("auto_advance", e);
                ModBehaviour.DevLog("[ModeH] 自动开打中断 (" + (reasonId ?? "unknown") + ")");
            }
            finally
            {
                if (outer) _deferPageRoutes = false;
            }
            if (outer && !_commandsClosed && _runState != null && IsPageLifecycle(_runState.Lifecycle))
            {
                RouteUiForLifecycle(_runState.Lifecycle);
            }
        }

        /// <summary>
        /// 从名单 / 看盘 / 整备相位推到入场。默认值：不下虚拟注、不押真实物品、
        /// 默认阵容与配装（EnsurePreparedMatchSelection）、口令取先发的招牌（不可用时取第一条可用口令）。
        /// </summary>
        private void AdvanceTowardsFight()
        {
            if (_commandsClosed || _runState == null || _season == null) return;
            // 转会窗口没有报价时没有可决定的事：直接关窗进下一场，不停在一张只有「下一场」的空页上（V6-3）
            if (_runState.Lifecycle == ModeHLifecycle.TransferWindow && EnsureTransferOffer() == null)
                CloseTransferWindow("no_offer");
            if (_commandsClosed || _runState == null || _season == null) return;
            if (_runState.Lifecycle == ModeHLifecycle.RosterLocked) OpenFirstMatchBrief();

            if (_commandsClosed || _runState == null || _season == null) return;
            if (_runState.Lifecycle == ModeHLifecycle.MatchBrief)
            {
                // OpenNextMatchBrief 只清旧计划，建新计划原本发生在看盘页组装里
                EnsureMatchPlan();
                if (_runState == null || _season == null || _season.currentMatchPlan == null
                    || _runState.Lifecycle != ModeHLifecycle.MatchBrief) return;
                EnterLoadoutEditing();
            }

            if (_commandsClosed || _runState == null || _season == null) return;
            if (_runState.Lifecycle != ModeHLifecycle.LoadoutEditing
                && _runState.Lifecycle != ModeHLifecycle.OddsPreview) return;
            _selectedVirtualStake = 0;
            _showLoadoutEditor = false;
            // 正常流程从不押真实物品：兜底页上勾过的押品格不能被这条链静默带进锁盘
            ModeHRealStakeService.ClearSelection();
            string prepareFailure;
            if (EnsurePreparedMatchSelection(out prepareFailure)) ApplyAutoRosterDefaults();
            LockLoadoutAndStartMatch();
        }

        /// <summary>
        /// 默认阵容的一处取舍（好玩优先，owner 2026-09-23 授权拍板）：主将带伤、接力健康时让接力先上。
        /// 带伤选手再被击倒会直接赛季退役，玩家在自动流程里没有机会自己换人；让它坐接力席，
        /// 先发赢下来它就「整场没上」、赛后自动养好伤。两人都带伤或都健康时维持主将先发。
        /// 交换方式与整备页的「首发」选项一致（选手带着各自的配装换席位），口令按新先发重选。
        /// </summary>
        private void ApplyAutoRosterDefaults()
        {
            ModeHMatchRosterDto roster = _season != null ? _season.matchRoster : null;
            if (roster == null || (roster.enteredProfileIds != null && roster.enteredProfileIds.Count > 0)) return;
            ModeHProfileDto starter = FindSeasonProfile(roster.matchStarterProfileId);
            ModeHProfileDto relay = FindSeasonProfile(roster.matchRelayProfileId);
            if (starter == null || relay == null) return;
            if (string.IsNullOrEmpty(starter.injuryId) || !string.IsNullOrEmpty(relay.injuryId)) return;

            roster.matchStarterProfileId = relay.profileId;
            roster.matchRelayProfileId = starter.profileId;
            List<string> starterKits = roster.starterKitIds;
            roster.starterKitIds = roster.relayKitIds;
            roster.relayKitIds = starterKits;
            roster.activeProfileId = relay.profileId;
            _selectedMatchCommandId = null;
        }

        private string ResolveLockedCommandId()
        {
            return _season != null && _season.currentLoadoutLock != null
                ? _season.currentLoadoutLock.commandId : null;
        }

        /// <summary>本场锁定口令的白话说明（拍铃卡副标题）。</summary>
        private string ResolveLockedCommandPlain()
        {
            string commandId = ResolveLockedCommandId();
            if (string.IsNullOrEmpty(commandId)) return string.Empty;
            return L10n.T(ModeHConfig.LocalizationKeyPrefix + "Command_" + commandId + "_Plain");
        }

        /// <summary>
        /// 结算页的必选项按默认值处理，与玩家在结算页上点按钮走同一批方法（身份围栏、落盘屏障不变）：
        /// - 战痕候选：有空位就留下（「百战留痕」本来就是让选手身上长出故事），已满三条或已有同名就换名声；
        /// - 整备奖励：领第一件候选，之后按默认配装自动穿上（奖励套装 ID 排在起手套装前面）。
        /// 处理结果写成说明行挂在结算页上。任何一步失败都留在原结算页，按钮照旧可点。
        /// </summary>
        private void ApplySettlementDefaults()
        {
            if (_commandsClosed || _season == null || _runState == null
                || _runState.Lifecycle != ModeHLifecycle.Intermission || _season.matchReports == null) return;
            bool outer = !_deferPageRoutes;
            _deferPageRoutes = true;
            try
            {
                string prefix = ModeHConfig.LocalizationKeyPrefix;
                for (int attempt = 0; attempt <= _season.matchReports.Count; attempt++)
                {
                    ModeHMatchReportDto pending = null;
                    ModeHProfileDto profile = null;
                    for (int i = 0; i < _season.matchReports.Count && pending == null; i++)
                    {
                        ModeHMatchReportDto candidate = _season.matchReports[i];
                        if (IsScarOfferPending(candidate) && TryGetScarOfferProfile(candidate, out profile)) pending = candidate;
                    }
                    if (pending == null) break;

                    string scarId = pending.scarOfferId;
                    bool decline = (profile.scarIds != null && profile.scarIds.Contains(scarId))
                        || (profile.scarIds != null && profile.scarIds.Count >= ModeHConfig.MaxScarsPerProfile);
                    string owner = ResolveProfileDisplayName(profile.profileId);
                    ResolveScarOffer(pending.seasonRewardOperationId, scarId, null, decline,
                        _runState.OwnerToken, _runState.MatchIndex);
                    if (_commandsClosed || _runState == null || _runState.Lifecycle != ModeHLifecycle.Intermission) return;
                    if (!decline && IsScarOfferPending(pending))
                    {
                        // 留不下（与原型不合等）就换名声：自动流程不能卡在一条战痕上
                        decline = true;
                        ResolveScarOffer(pending.seasonRewardOperationId, scarId, null, true,
                            _runState.OwnerToken, _runState.MatchIndex);
                        if (_commandsClosed || _runState == null || _runState.Lifecycle != ModeHLifecycle.Intermission) return;
                    }
                    // 仍没处理掉（落盘失败会挂起，走不到这里）就交还给结算页的按钮，不在这里反复重试
                    if (IsScarOfferPending(pending)) break;
                    AddSettlementNote(decline
                        ? L10n.T(prefix + "Settle_AutoScarDeclined").Replace("{0}", owner)
                        : L10n.T(prefix + "Settle_AutoScarTaken").Replace("{0}", owner)
                            .Replace("{1}", L10n.T(prefix + "Scar_" + scarId))
                            .Replace("{2}", L10n.T(prefix + "Scar_" + scarId + "_Desc")));
                }

                ModeHMatchReportDto report = FindLatestPendingReport();
                ModeHSeasonRewardOperationDto operation = FindRewardOperation(
                    report != null ? report.seasonRewardOperationId : null);
                if (operation == null || operation.status != (int)ModeHSeasonRewardOperationStatus.Offered
                    || operation.candidateKitIds == null || operation.candidateKitIds.Count == 0) return;
                string kitId = operation.candidateKitIds[0];
                string failureReasonId;
                if (!ModeHSeasonRewardService.TrySelectKit(_season, operation.operationId, kitId, out failureReasonId))
                {
                    ModBehaviour.DevLog("[ModeH] 自动领取整备奖励失败: " + (failureReasonId ?? "unknown"));
                    return;
                }
                // 普通落盘即可（按批节流，不强制本帧 SaveFile）：结算帧里 match_settling 与战痕已各写过一次整档。
                // 万一这次没落到盘上就崩了，读档回来 operation 仍是 Offered，这里会原样再领一次，结果相同。
                // 「下一场」归档时 CompleteSettlementAndRoute 会做 durable 落盘。
                TryPersistSeason("reward_auto_selected");
                AddSettlementNote(L10n.T(prefix + "Settle_AutoKit").Replace("{0}", L10n.T(prefix + "Kit_" + kitId)));
            }
            catch (Exception e)
            {
                LogFailure("settlement_defaults", e);
            }
            finally
            {
                if (outer) _deferPageRoutes = false;
            }
        }

        private void AddSettlementNote(string note)
        {
            ModeHMatchReportDto report = FindLatestPendingReport();
            string operationId = report != null ? report.seasonRewardOperationId : null;
            if (!string.Equals(operationId, _settlementNotesOperationId, StringComparison.Ordinal))
            {
                _settlementNotes.Clear();
                _settlementNotesOperationId = operationId;
            }
            if (!string.IsNullOrEmpty(note)) _settlementNotes.Add(note);
        }

        /// <summary>
        /// 结算页收尾：挂上自动处理的说明行；只剩一个「确认」时把它换成「下一场」（最后一场、
        /// 转会窗口前是「继续」），点下去归档本场并直接开打下一场。原确认回调（含身份围栏）原样保留在链首。
        /// </summary>
        private void DecorateSettlementPage(ModeHPageContent page)
        {
            if (page == null || _runState == null || _runState.Lifecycle != ModeHLifecycle.Intermission) return;
            ModeHMatchReportDto report = FindLatestPendingReport();
            if (report != null && _settlementNotes.Count > 0 && string.Equals(
                    report.seasonRewardOperationId, _settlementNotesOperationId, StringComparison.Ordinal))
            {
                page.NoteLines.AddRange(_settlementNotes);
            }

            string prefix = ModeHConfig.LocalizationKeyPrefix;
            if (page.Actions.Count != 1 || page.Actions[0] == null || page.Actions[0].OnClick == null
                || !string.Equals(page.Actions[0].Label, L10n.T(prefix + "Button_Confirm"), StringComparison.Ordinal)) return;
            Action archive = page.Actions[0].OnClick;
            // 转会窗口前也写「下一场」：没有报价时自动链直接关窗开打，有报价才停在转会页让玩家拿主意（V6-3）
            List<string> live = ModeHTransferMarket.GetLiveContractProfileIds(_season);
            bool nextIsMatch = live != null && live.Count > 0
                && _runState.MatchIndex < ModeHConfig.SeasonMatchCount;
            page.Actions[0].Label = L10n.T(prefix + (nextIsMatch ? "Button_NextMatch" : "Button_Continue"));
            page.Actions[0].OnClick = delegate { RunAutoAdvance("next_match", archive); };
            if (!nextIsMatch) return;
            // 下一场押多少就在这一页定：页脚一排押注档，按钮上写着押多少（「下一场 · 押 5,000」）
            AppendCashBetRow(page);
            page.Actions[0].Label += DescribeStandingBetSuffix();
            page.Actions[0].IsPrimary = true;
        }

        #endregion

        #region 恢复壳

        /// <summary>
        /// 打开恢复壳（§22.4、§23.1）。
        /// 四个打开时机：宿主恢复出 Suspended 后的首个基地场景、船坞入口的恢复分支、
        /// 运行中转入异常态、journal 恢复扫描命中。
        /// </summary>
        internal void OpenRecoveryShell(string technicalReasonId)
        {
            try
            {
                if (_recoveryPanel == null) _recoveryPanel = new ModeHRecoveryPanel();

                ModeHSeasonDto season = _season != null ? _season : ModeHProfilePersistence.LoadCurrent();
                ModeHStakeJournalDto journal = ModeHWarehouseStakeJournal.Export();

                List<string> lines = ModeHRecoveryPanel.BuildLines(season, journal, technicalReasonId);
                // 刚才在恢复壳里点的那一下结果如何，写在最上面（审查 B-11：旧版只走官方全局提示，可能被恢复壳盖住）
                if (!string.IsNullOrEmpty(_recoveryResultText))
                {
                    lines.Insert(0, string.Empty);
                    lines.Insert(0, _recoveryResultText);
                    _recoveryResultText = null;
                }
                List<ModeHActionData> actions = BuildRecoveryActions(season);
                // 证据不足时只读展示：允许看，不允许动资产（§22.4）
                bool allowActions = ModeHWarehouseStakeJournal.IsSlotConsistent || journal == null;

                _recoveryPanel.Show(
                    L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Recovery"),
                    lines, actions, allowActions, StopOwnRewardReveal);
            }
            catch (Exception e)
            {
                LogFailure("open_recovery", e);
            }
        }

        /// <summary>收起恢复壳（幂等）。实例保留，下次 OpenRecoveryShell 复用。</summary>
        private void HideRecoveryShell()
        {
            try
            {
                if (_recoveryPanel != null) _recoveryPanel.Hide();
            }
            catch (Exception e)
            {
                LogFailure("recovery_hide", e);
            }
        }

        private List<ModeHActionData> BuildRecoveryActions(ModeHSeasonDto season)
        {
            List<ModeHActionData> actions = new List<ModeHActionData>();
            if (season == null || _runState == null) return actions;

            // Suspended：允许玩家从同一场重开（技术中止绝不判负，§17.4）
            if ((_runState.Lifecycle == ModeHLifecycle.Suspended || _restoredSeasonPending)
                && !_resumeScenePending)
            {
                actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_SameMatchRestart"),
                    OnClick = ResumeFromSuspended,
                    IsPrimary = true,
                });
            }

            // 存档暂时写不下去时给一个显式重试，而不是让玩家干等
            if (_seasonDirty)
            {
                actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Retry"),
                    OnClick = delegate { TryPersistSeason("recovery_retry"); },
                });
            }

            // 风险扫描因 I/O 异常失败时可重试；这是 contracts.md 承诺的「提供重试」
            if (ModeHRuntimeGates.IsModeHRiskScanFaulted)
            {
                actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_RetryScan"),
                    OnClick = delegate { ModeHRuntimeGates.TryRetryRiskScan(0f); },
                });
            }

            // 内存里仍握着押品实物时，必须给一条出路。
            // 这条动作刻意绕过 allowActions 的置灰（见 OpenRecoveryShell 的
            // bypassReadOnly）：IsSlotConsistent=false 恰恰是"押品还没归位"的
            // 同义词，用它把唯一的补救按钮关掉会让玩家除删档外无路可走。
            // 只读保护的本意是"证据不足时不许动资产"，而把托管物还回玩家仓库
            // 是**减少**资产暴露，不是增加，所以这里放行是安全的。
            //
            // 判据不能只看内存 EscrowCount：`_escrowItems` 是纯内存 List，重启或切槽后
            // 必为空，而 journal 里可能仍记着一笔非终态事务。只看内存的话，跨会话回来
            // 的玩家看不到这个按钮，等于「有账没结却没有任何出口」。
            ModeHStakeJournalDto activeJournal = ModeHWarehouseStakeJournal.Active;
            bool journalUnsettled = activeJournal != null
                && !ModeHWarehouseStakeJournal.IsTerminalPhase(
                    ModeHStateModel.ToStakePhase(activeJournal.phase));
            if (ModeHWarehouseStakeJournal.EscrowCount > 0 || journalUnsettled)
            {
                actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_ReturnEscrow"),
                    OnClick = ReturnEscrowFromRecovery,
                    BypassReadOnly = true,
                });
            }

            // 中断赛季必须有一条「不玩了」的出路。此前只有 Suspended 给了「同场重开」，
            // 而停在 Intermission / MatchBrief 的赛季一个动作都没有：recovery-only 闸
            // 已经立起，新赛季开不了，玩家除删档外无路可走。
            // 这条只做「放弃」：把 journal 结清、清掉活动赛季，回到能开新赛季的状态。
            // 真正的「续赛」需要回载 _season 并补齐状态机落点，是独立一步。
            if (HasResumableSeasonRecord(season))
            {
                actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_AbandonSeason"),
                    // 不可逆：先弹共享确认框写清后果（审查 B-02）；红描边红字、排最左由动作行统一处理（B-04）
                    OnClick = ConfirmAbandonSeasonFromRecovery,
                    BypassReadOnly = true,
                    IsDanger = true,
                });
            }

            // 恒有一条出口（审查 B-10）：恢复壳现在占模态输入，只读或一个动作都没有时玩家不能被锁在这一页。
            // 只收起面板、不动赛季与押品；回到鸭王杯入口会再打开它。
            actions.Add(new ModeHActionData
            {
                Label = L10n.T("稍后处理", "Later"),
                OnClick = HideRecoveryShell,
                BypassReadOnly = true,
                IsCancel = true,
            });
            return actions;
        }

        /// <summary>放弃赛季前的确认（审查 B-02）：写清押品先原样还回仓库、这一季不再继续。</summary>
        private void ConfirmAbandonSeasonFromRecovery()
        {
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("放弃本赛季？", "Abandon this season?"),
                Body = L10n.T("这一场押的钱原样退回，旧档托管的仓库物品也还回仓库，然后结束这一季，之后可以重新开一季。",
                    "Any money bet on this match is refunded and any storage items held from an old save go back to storage; then this season ends and you can start a new one."),
                Warning = L10n.T("这一季没打完的场次、战绩与名声都不再继续。",
                    "The remaining matches, results and fame of this season will not carry on."),
                ConfirmLabel = L10n.T("放弃赛季", "Abandon season"),
                Danger = true,
                OnConfirm = AbandonSeasonFromRecovery,
            });
        }

        /// <summary>磁盘上是否还有一份「活着但进行不下去」的赛季记录。</summary>
        private static bool HasResumableSeasonRecord(ModeHSeasonDto season)
        {
            if (season == null || season.runState == null) return false;
            ModeHLifecycle lifecycle = ModeHStateModel.ToLifecycle(season.runState.lifecycle);
            return lifecycle != ModeHLifecycle.None
                && lifecycle != ModeHLifecycle.SeasonEnded
                && lifecycle != ModeHLifecycle.Unknown;
        }

        /// <summary>
        /// 恢复壳里的「放弃赛季」：先把押品原样结清，再清掉赛季记录与 recovery-only 闸。
        /// 顺序不能反——押品是玩家真实物品，赛季记录被清掉后就再没有恢复入口了。
        /// </summary>
        private void AbandonSeasonFromRecovery()
        {
            RefundCashBet("abandon_season");
            try
            {
                ModeHStakeJournalDto journal = ModeHWarehouseStakeJournal.Active;
                if (journal != null
                    && !ModeHWarehouseStakeJournal.IsTerminalPhase(
                        ModeHStateModel.ToStakePhase(journal.phase)))
                {
                    // 押品没结清就不许放弃赛季：结清失败时 journal 会自行进人工介入，
                    // 恢复壳保持可达，玩家不会因为这一步把证据弄丢。
                    ReturnEscrowFromRecovery();
                    ModeHStakeJournalDto after = ModeHWarehouseStakeJournal.Active;
                    if (after != null
                        && !ModeHWarehouseStakeJournal.IsTerminalPhase(
                            ModeHStateModel.ToStakePhase(after.phase)))
                    {
                        return;
                    }
                }

                // 把磁盘上的赛季记录标成已结束，而不是删 key：保留历史、也不动 schema。
                // 标不成功就不解闸——宁可让玩家停在恢复壳，也不能出现「闸开了但磁盘上
                // 还挂着一个活赛季」，那正是新赛季静默覆盖旧赛季的成因。
                ModeHSeasonDto persisted = _season != null ? _season : ModeHProfilePersistence.LoadCurrent();
                if (persisted != null && persisted.runState != null)
                {
                    persisted.runState.lifecycle = (int)ModeHLifecycle.SeasonEnded;
                    string writeError;
                    if (!ModeHSaveFlushCoordinator.RequestSeasonWrite(persisted, out writeError, true))
                    {
                        ModBehaviour.CriticalLog("[ModeH] 放弃赛季落盘失败: "
                            + (writeError != null ? writeError : "unknown"));
                        if (_owner != null)
                        {
                            _owner.ShowMessage(
                                L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_AbandonSeason_Failed"));
                        }
                        return;
                    }
                }

                // durable 成功后才退出；owner 与 generation 必须活到逆序释放完成，
                // 否则 spectator 的输入锁、arena 与比赛页面将失去最后的清理入口。
                _commandsClosed = true;
                CancelSeasonResume();
                ReleaseRuntimeObjects();
                _shutdownCompleted = true;
                _restoredSeasonPending = false;
                _season = null;
                _runState = null;
                _map = null;
                _seasonDirty = false;
                ModeHRuntimeGates.SetRunOwnerActive(false);
                ModeHRuntimeGates.SetRecoveryOnlyBlocked(false, null);
                HideRecoveryShell();
                if (_owner != null)
                {
                    _owner.ShowMessage(
                        L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_AbandonSeason_Done"));
                }
            }
            catch (Exception e)
            {
                LogFailure("recovery_abandon", e);
            }
        }

        /// <summary>
        /// 恢复壳里的「取回押品」：把仍在内存托管中的物品还回仓库。
        /// 成功后闸门由 journal 侧按终态自行解除；失败保持 pending 并给出可见提示。
        /// </summary>
        private void ReturnEscrowFromRecovery()
        {
            try
            {
                // runSeed/matchIndex 只用于派生确定性的 operationId 与 eventTokenId，
                // 所以必须取**落盘过的** journal/season 值而不是 _runState —— 恢复壳
                // 常在 _runState 已被清掉之后打开（跨重启就是这种情形）。
                ModeHStakeJournalDto journal = ModeHWarehouseStakeJournal.Active;
                ModeHRunStateDto persistedRun = _season != null ? _season.runState : null;
                long runSeed = persistedRun != null ? persistedRun.runSeed : 0L;
                int matchIndex = journal != null
                    ? journal.matchIndex
                    : (persistedRun != null ? persistedRun.matchIndex : 0);

                string failureReasonId;
                if (ModeHRealStakeService.TryAbortReturn(runSeed, matchIndex, out failureReasonId))
                {
                    _recoveryResultText = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_ReturnEscrow_Done");
                }
                else
                {
                    ModBehaviour.CriticalLog("[ModeH] 恢复壳取回押品失败: "
                        + (failureReasonId != null ? failureReasonId : "unknown"));
                    _recoveryResultText = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_ReturnEscrow_Failed");
                }
                // 重开恢复壳而不是留着旧内容：押品行与动作列表都要按新阶段重算，
                // 成功后 EscrowCount 归零，这个按钮会自然消失。
                OpenRecoveryShell(_lastExitReasonId);
            }
            catch (Exception e)
            {
                LogFailure("recovery_return_escrow", e);
            }
        }

        /// <summary>Suspended → Recovering：生成新 owner token 并按同一场重建。</summary>
        private void ResumeFromSuspended()
        {
            try
            {
                if (_runState == null) return;
                if (_resumeScenePending) return;
                string resumeFailure;
                if (!TryPrepareSeasonResume(out resumeFailure))
                {
                    OpenRecoveryShell(resumeFailure);
                    return;
                }
                if (_restoredSeasonPending || _arenaLease == null || !_arenaLease.IsActive)
                {
                    BeginSeasonResumeScene();
                    return;
                }
                // 玩家主动重开：先给回全新的同场重试预算，再进恢复通道。
                // 不重置的话 DriveRecovery 会当场判定「预算已耗尽」把玩家弹回挂起，
                // 这个按钮就等于没有；而且计划缓存含重试序号，不重置还会复用刚失败的那份计划。
                _runState.ResetTechnicalRetry();
                if (!TryTransition(ModeHLifecycle.Suspended, ModeHLifecycle.Recovering, "player_resume"))
                {
                    return;
                }
                ModeHRuntimeGates.SetRecoveryOnlyBlocked(false, null);
            }
            catch (Exception e)
            {
                LogFailure("resume_suspended", e);
            }
        }

        /// <summary>
        /// 只终止**我们自己**播的奖励揭晓。
        /// 恢复壳过去用 FindObjectsOfType 全场扫，会把原版许愿池正在放的动画一并销毁。
        /// </summary>
        private void StopOwnRewardReveal()
        {
            try
            {
                if (_activeRewardRevealRoot != null)
                {
                    UnityEngine.Object.Destroy(_activeRewardRevealRoot);
                }
            }
            catch (Exception)
            {
                // 已被销毁：置空即可
            }
            _activeRewardRevealRoot = null;
        }

        #endregion
    }
}
