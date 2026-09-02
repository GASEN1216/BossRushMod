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
                if (_recoveryPanel != null) _recoveryPanel.Hide();
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
                RouteUiForLifecycle(record.To);
            }
            catch (Exception e)
            {
                LogFailure("transition_applied", e);
            }
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
            if (_season == null) return false;
            try
            {
                ProjectRunStateIntoSeason();
                string error;
                if (!ModeHSaveFlushCoordinator.RequestSeasonWrite(_season, out error))
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
                    break;
                case ModeHLifecycle.MatchSettling:
                case ModeHLifecycle.Intermission:
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
            _ui.OpenPage(page, _runState.Lifecycle, _runState.RunId, content);
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
            if (_runState.Lifecycle == ModeHLifecycle.Suspended)
            {
                actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_SameMatchRestart"),
                    OnClick = ResumeFromSuspended,
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
            if (ModeHWarehouseStakeJournal.EscrowCount > 0)
            {
                actions.Add(new ModeHActionData
                {
                    Label = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_ReturnEscrow"),
                    OnClick = ReturnEscrowFromRecovery,
                    BypassReadOnly = true,
                });
            }

            return actions;
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
                    if (_owner != null)
                    {
                        _owner.ShowMessage(
                            L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_ReturnEscrow_Done"));
                    }
                }
                else
                {
                    ModBehaviour.CriticalLog("[ModeH] 恢复壳取回押品失败: "
                        + (failureReasonId != null ? failureReasonId : "unknown"));
                    if (_owner != null)
                    {
                        _owner.ShowMessage(
                            L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_ReturnEscrow_Failed"));
                    }
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
