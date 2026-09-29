// Mode H 跨会话续赛：重建完整赛季后，显式确认、校验、回到原图并重取双租约。
using System;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    internal sealed partial class ModeHRuntimeModule
    {
        private bool _restoredSeasonPending;
        private int _restoredSlotGeneration;
        private bool _resumeScenePending;
        private int _resumeSceneIntentGeneration;
        private bool _resumeNeedsMatchReset;

        private bool TryPrepareSeasonResume(out string failure)
        {
            failure = null;
            if (_season == null || _runState == null || _owner == null || !IsEnabled)
            { failure = "season_resume_unavailable"; return false; }
            if (_restoredSeasonPending && _restoredSlotGeneration != ModeHRuntimeGates.SlotGeneration)
            { failure = "season_resume_slot_changed"; return false; }
            if (ModeHProfilePersistence.IsWriteBarrier || ModeHProfilePersistence.IsStoreFaulted)
            { failure = "season_resume_write_barrier"; return false; }
            if (_owner.HasLegacyModeConflictForModeH(out failure)) return false;
            EnsureContentScanned();
            if (!ModeHRuntimeGates.IsModeHContentReady)
            { failure = "season_resume_content_unavailable"; return false; }

            string game, mod, signatureError;
            if (!ModeHCanonicalDigest.TryGetGameBuildSignature(out game, out signatureError)
                || !ModeHCanonicalDigest.TryGetModBuildSignature(out mod, out signatureError)
                || !string.Equals(_season.gameBuildSignature, game, StringComparison.Ordinal)
                || !string.Equals(_season.modBuildSignature, mod, StringComparison.Ordinal)
                || !string.Equals(_season.contentCatalogSignature,
                    ModeHContentCatalog.ContentCatalogSignature, StringComparison.Ordinal))
            { failure = "season_resume_signature_mismatch"; return false; }

            if (!ModeHMapSupportRegistry.TryGetMap(_runState.SceneName, out _map)
                || _map == null || string.IsNullOrEmpty(_map.SceneId))
            { failure = "season_resume_map_unavailable"; return false; }
            if (ResolveRecoveryResumeLifecycle() == ModeHLifecycle.Unknown)
            { failure = "season_resume_target_unknown"; return false; }

            // 回看盘之前先归还仍托管的押品；失败保留 journal 与恢复壳，不开始新比赛。
            ModeHStakeJournalDto journal = ModeHWarehouseStakeJournal.Active;
            if (journal != null && !ModeHWarehouseStakeJournal.IsTerminalPhase(
                    ModeHStateModel.ToStakePhase(journal.phase)))
            {
                if (!ModeHRealStakeService.TryAbortReturn(
                        _runState.RunSeed, _runState.MatchIndex, out failure)) return false;
            }
            if (!ModeHWarehouseStakeJournal.IsSlotConsistent)
            { failure = "season_resume_assets_unresolved"; return false; }
            ModeHProductionCertification restoredCertification = new ModeHProductionCertification();
            // 续赛与普通新局同样走发布目录，不因 Dev 构建或旧动态报告要求玩家重新测试。
            if (!restoredCertification.TryUseReleaseCatalog())
            { failure = "season_resume_release_catalog_unavailable"; return false; }
            _certification = restoredCertification;
            return true;
        }

        private void BeginSeasonResumeScene()
        {
            if (_resumeScenePending || _runState == null || _map == null) return;
            if (SceneLoader.Instance == null || SceneLoader.IsSceneLoading)
            { PresentRecoveryFailure("season_resume_scene_busy"); return; }
            _commandsClosed = false;
            _shutdownCompleted = false;
            _resumeScenePending = true;
            _restoredSeasonPending = true;
            _restoredSlotGeneration = ModeHRuntimeGates.SlotGeneration;
            // 原赛季入场票已消费，续赛不预扣新票，也不进入创建赛季的路径。
            _resumeSceneIntentGeneration = BossRushMapSelectionHelper.FreezeModeHEntryIntent(
                _map.SceneName, _map.SceneId);
            HideRecoveryShell();
            LoadSeasonResumeScene(_runState.OwnerToken, _restoredSlotGeneration,
                _resumeSceneIntentGeneration, _map.SceneId).Forget();
        }

        private async UniTask LoadSeasonResumeScene(long ownerToken, int slotGeneration,
            int intentGeneration, string sceneId)
        {
            try
            {
                await SceneLoader.Instance.LoadScene(sceneId, null, true);
                // 风暴 B0/冷库的加载 ID 先到主图；续赛也必须走到冻结的目标子场景。
                if (IsSeasonResumeRequestCurrent(ownerToken, slotGeneration, intentGeneration)
                    && !HasSceneReadyWait(intentGeneration) && _map != null
                    && Duckov.Scenes.MultiSceneCore.Instance != null
                    && !string.Equals(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                        _map.SceneName, StringComparison.Ordinal))
                {
                    await Duckov.Scenes.MultiSceneCore.Instance.LoadAndTeleport(
                        _map.SceneName, _map.PlayerSpawnPos);
                }
                if (IsSeasonResumeRequestCurrent(ownerToken, slotGeneration, intentGeneration))
                {
                    // 场景回调已接到时，让同一 owner 的就绪等待完成，不能在这里抢先取消。
                    if (HasSceneReadyWait(intentGeneration)) return;
                    // 没有命中回调时保持可重试，绝不在错误地图开赛。
                    CancelSeasonResume();
                    PresentRecoveryFailure("season_resume_scene_not_matched");
                }
            }
            catch (Exception e)
            {
                if (!IsSeasonResumeRequestCurrent(ownerToken, slotGeneration, intentGeneration)) return;
                LogFailure("season_resume_scene", e);
                CancelSeasonResume();
                PresentRecoveryFailure("season_resume_scene_failed");
            }
        }

        private bool IsSeasonResumeRequestCurrent(long ownerToken, int slotGeneration, int intentGeneration)
        {
            return _resumeScenePending && !_shutdownCompleted && _runState != null
                && _runState.OwnerToken == ownerToken
                && ModeHRuntimeGates.SlotGeneration == slotGeneration
                && _resumeSceneIntentGeneration == intentGeneration
                && BossRushMapSelectionHelper.HasPendingModeHEntryIntent()
                && BossRushMapSelectionHelper.GetPendingModeHSceneGeneration() == intentGeneration;
        }

        private bool TryHandleSeasonResumeScene(SceneRuntimeContext context)
        {
            if (!_resumeScenePending) return false;
            int intentGeneration;
            if (_runState == null || _map == null
                || ModeHRuntimeGates.SlotGeneration != _restoredSlotGeneration) return true;
            if (!string.Equals(context.SceneName, _map.SceneName, StringComparison.Ordinal)
                || !BossRushMapSelectionHelper.TryMatchModeHSceneIntent(
                    context.SceneName, _map.SceneId, out intentGeneration)
                || intentGeneration != _resumeSceneIntentGeneration) return true;

            ScheduleSceneReadyWait(context, _map.SceneId, intentGeneration, true);
            return true;
        }

        private void CompleteSeasonResumeScene()
        {
            CancelSeasonResume();
            string failure;
            try
            {
                ModeHSupportedMap restoredMap;
                if (!ModeHMapSupportRegistry.TryCreateRunVariant(_map, _runState.RunSeed, out restoredMap, out failure))
                { FailSeasonResume(failure); return; }
                _map = restoredMap;
                _arenaLease = new ModeHArenaIsolationLease();
                if (!_arenaLease.TryAcquire(_map.SceneName, _sceneGeneration,
                        _runState.OwnerToken, out failure))
                { FailSeasonResume(failure); return; }
                _spectatorLease = new ModeHSpectatorLease();
                if (!_spectatorLease.TryAcquire(_map.SpectatorPos, _sceneGeneration,
                        _runState.OwnerToken, out failure))
                { FailSeasonResume(failure); return; }

                _runState.ResetTechnicalRetry();
                _resumeNeedsMatchReset = true;
                _recoveryDriveStateSequence = -1;
                if (_runState.Lifecycle != ModeHLifecycle.Recovering
                    && !TryTransition(_runState.Lifecycle, ModeHLifecycle.Recovering, "player_resume"))
                { FailSeasonResume("season_resume_transition_failed"); return; }
                _restoredSeasonPending = false;
                ModeHRuntimeGates.SetRecoveryOnlyBlocked(false, null);
                DriveRecovery();
            }
            catch (Exception e)
            {
                LogFailure("season_resume_leases", e);
                FailSeasonResume("season_resume_lease_exception");
            }
        }

        private void FailSeasonResume(string failure)
        {
            ReleaseRuntimeObjects();
            _restoredSeasonPending = true;
            ModeHRuntimeGates.SetRecoveryOnlyBlocked(true, failure);
            PresentRecoveryFailure(failure);
        }

        #region 恢复通道的玩家可见面（2026-09-29 owner：恢复页是开发调试信息，不进玩家路径）

        /// <summary>赛季在场内挂起后送玩家回基地的退出原因（ShutdownRuntime 按它走回基地 + 本场总结）。</summary>
        internal const string SuspendedExitReasonId = "technical_suspend_exit";

        /// <summary>已为哪个状态序号发起过挂起离场（每次进入 Suspended 只发一次）。</summary>
        private int _suspendExitStateSequence = -1;

        /// <summary>
        /// Recovering / ErrorRecoveryPending / Suspended 三个相位的界面。
        ///
        /// 旧写法三者一律弹恢复壳（「技术中止，本场按同一看盘重开」等调试措辞，整块盖在画面上），
        /// 而且关停后内存 owner 已清空，从入口再打开时一个按钮都没有，关不掉。
        /// 现在：技术故障的同场重试（前两个相位）完全静默，DriveRecovery 下一帧就把它推回同一场看盘；
        /// Suspended（自动重试用完）由每帧驱动送玩家回基地，赛季与押注原样保留，入口处直接续赛。
        /// </summary>
        private void RouteRecoveryLifecycle(ModeHLifecycle lifecycle)
        {
            if (_ui != null) _ui.ClosePage();
        }

        /// <summary>
        /// 每帧驱动：本场景内进入 Suspended 后送回基地（不在状态转换回调里同步关停，
        /// RequestSuspended 在转换之后还要退押品、立闸、落盘）。返回 true 表示本帧已处理。
        /// </summary>
        private bool TryDriveSuspendedExit()
        {
            if (_runState == null || _runState.Lifecycle != ModeHLifecycle.Suspended) return false;
            if (_runState.StateSequence == _suspendExitStateSequence) return true;
            _suspendExitStateSequence = _runState.StateSequence;
            try
            {
                if (_owner != null)
                {
                    _owner.ShowMessage(L10n.T(
                        "这一场暂时开不了，先送你回基地。本赛季和押注都留着，到鸭王杯入口就能接着打。",
                        "This match can't start right now, so you're heading back to base. Your season and bet are kept; use the cup entrance to continue."));
                }
            }
            catch (Exception)
            {
                // 提示失败不影响离场
            }
            RequestExit(ModeHExitReason.UserMapReturn, SuspendedExitReasonId);
            return true;
        }

        /// <summary>
        /// 鸭王杯入口的恢复分支（ModeHInteractable 调）。有可续的赛季时正式构建直接续赛，
        /// 不先弹恢复壳；Dev 构建、押品 journal 没结清、没有可续赛季时仍打开恢复壳（它恒有「稍后处理」）。
        /// </summary>
        internal void ContinueSeasonFromEntry(string reasonId)
        {
            try
            {
                EnsureRunOwnerForRecovery();
                ModeHSeasonDto season = _season != null ? _season : ModeHProfilePersistence.LoadCurrent();
                bool resumable = _runState != null && !_resumeScenePending && HasResumableSeasonRecord(season)
                    && (_runState.Lifecycle == ModeHLifecycle.Suspended || _restoredSeasonPending);
                if (ModBehaviour.DevModeEnabled || HasUnsettledStakeJournal() || !resumable)
                {
                    OpenRecoveryShell(reasonId);
                    return;
                }
                ResumeFromSuspended();
            }
            catch (Exception e)
            {
                LogFailure("entry_continue", e);
                OpenRecoveryShell(reasonId);
            }
        }

        /// <summary>
        /// 同一进程里关停过（技术中止、挂起离场、观战退出）后内存 run owner 已清空，
        /// 而磁盘上的赛季还活着：按存档重建，与换档恢复走同一条路（新 owner token、续赛待确认）。
        /// 不重建的话恢复壳的 BuildRecoveryActions 一个按钮都给不出来。
        /// </summary>
        private void EnsureRunOwnerForRecovery()
        {
            if (_runState != null || _resumeScenePending) return;
            if (!HasResumableSeasonRecord(ModeHProfilePersistence.LoadCurrent())) return;
            RestoreForSlotChange();
        }

        private static bool HasUnsettledStakeJournal()
        {
            ModeHStakeJournalDto journal = ModeHWarehouseStakeJournal.Active;
            return journal != null && !ModeHWarehouseStakeJournal.IsTerminalPhase(
                ModeHStateModel.ToStakePhase(journal.phase));
        }

        /// <summary>
        /// 续赛失败的玩家可见面。Dev 构建打开恢复壳看诊断；正式构建不弹调试面板：
        /// 这一季永久接不上（游戏 / 模组 / 内容签名变了、地图不可用）时给一个可取消的「放弃这一季」确认框，
        /// 其余（切图忙、场景没对上等临时情况）只提示稍后再从入口试，赛季与押注都不动。
        /// 押品 journal 没结清时仍需恢复壳里的「取回押品」，照旧打开。
        /// </summary>
        private void PresentRecoveryFailure(string reasonId)
        {
            try
            {
                if (ModBehaviour.DevModeEnabled || HasUnsettledStakeJournal())
                {
                    OpenRecoveryShell(reasonId);
                    return;
                }
                HideRecoveryShell();
                if (IsPermanentResumeFailure(reasonId)
                    && HasResumableSeasonRecord(_season != null ? _season : ModeHProfilePersistence.LoadCurrent()))
                {
                    BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
                    {
                        Title = L10n.T("这一季接不上了", "This season can't continue"),
                        Body = L10n.T("游戏或模组更新过，或本季用到的内容已不可用，上一季没法接着打。放弃这一季后押的钱原样退回，可以重新开一季。",
                            "The game or mod was updated, or content this season relies on is gone, so it can't continue. Abandon it to get your bet back and start a new season."),
                        ConfirmLabel = L10n.T("放弃这一季", "Abandon season"),
                        Danger = true,
                        OnConfirm = AbandonSeasonFromRecovery,
                    });
                    return;
                }
                if (_owner != null)
                {
                    _owner.ShowMessage(L10n.T("鸭王杯暂时接不上，稍后再到入口试一次。本赛季和押注都保留着。",
                        "The cup can't resume right now. Try the entrance again in a moment; your season and bet are kept."));
                }
            }
            catch (Exception e)
            {
                LogFailure("recovery_failure_present", e);
            }
        }

        /// <summary>续赛前置检查里「再试也不会好」的那几类（签名、内容、地图、目标状态、存档写屏障）。</summary>
        private static bool IsPermanentResumeFailure(string reasonId)
        {
            if (string.IsNullOrEmpty(reasonId)) return false;
            return reasonId == "season_resume_signature_mismatch"
                || reasonId == "season_resume_content_unavailable"
                || reasonId == "season_resume_map_unavailable"
                || reasonId == "season_resume_target_unknown"
                || reasonId == "season_resume_release_catalog_unavailable"
                || reasonId == "season_resume_write_barrier";
        }

        #endregion

        private void CancelSeasonResume()
        {
            CancelSceneReadyWait();
            if (_resumeScenePending
                && BossRushMapSelectionHelper.GetPendingModeHSceneGeneration() == _resumeSceneIntentGeneration)
                ModeHEntry.CancelPendingEntry();
            _resumeScenePending = false;
            _resumeSceneIntentGeneration = 0;
        }
    }
}
