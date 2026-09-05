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
            if (!restoredCertification.TryRestoreSeasonReport(_season.productionCertificationSnapshot))
            { failure = "season_resume_certification_unavailable"; return false; }
            _certification = restoredCertification;
            return true;
        }

        private void BeginSeasonResumeScene()
        {
            if (_resumeScenePending || _runState == null || _map == null) return;
            if (SceneLoader.Instance == null || SceneLoader.IsSceneLoading)
            { OpenRecoveryShell("season_resume_scene_busy"); return; }
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
                if (IsSeasonResumeRequestCurrent(ownerToken, slotGeneration, intentGeneration))
                {
                    // 正常由场景回调完成；没有命中回调时保持可重试，绝不在错误地图开赛。
                    CancelSeasonResume();
                    OpenRecoveryShell("season_resume_scene_not_matched");
                }
            }
            catch (Exception e)
            {
                if (!IsSeasonResumeRequestCurrent(ownerToken, slotGeneration, intentGeneration)) return;
                LogFailure("season_resume_scene", e);
                CancelSeasonResume();
                OpenRecoveryShell("season_resume_scene_failed");
            }
        }

        private bool IsSeasonResumeRequestCurrent(long ownerToken, int slotGeneration, int intentGeneration)
        {
            return _resumeScenePending && !_shutdownCompleted && _runState != null
                && _runState.OwnerToken == ownerToken
                && ModeHRuntimeGates.SlotGeneration == slotGeneration
                && _resumeSceneIntentGeneration == intentGeneration;
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

            CancelSeasonResume();
            string failure;
            try
            {
                _arenaLease = new ModeHArenaIsolationLease();
                if (!_arenaLease.TryAcquire(_map.SceneName, _sceneGeneration,
                        _runState.OwnerToken, out failure))
                { FailSeasonResume(failure); return true; }
                _spectatorLease = new ModeHSpectatorLease();
                if (!_spectatorLease.TryAcquire(_map.SpectatorPos, _sceneGeneration,
                        _runState.OwnerToken, out failure))
                { FailSeasonResume(failure); return true; }

                _runState.ResetTechnicalRetry();
                _resumeNeedsMatchReset = true;
                _recoveryDriveStateSequence = -1;
                if (_runState.Lifecycle != ModeHLifecycle.Recovering
                    && !TryTransition(_runState.Lifecycle, ModeHLifecycle.Recovering, "player_resume"))
                { FailSeasonResume("season_resume_transition_failed"); return true; }
                _restoredSeasonPending = false;
                ModeHRuntimeGates.SetRecoveryOnlyBlocked(false, null);
                DriveRecovery();
            }
            catch (Exception e)
            {
                LogFailure("season_resume_leases", e);
                FailSeasonResume("season_resume_lease_exception");
            }
            return true;
        }

        private void FailSeasonResume(string failure)
        {
            ReleaseRuntimeObjects();
            _restoredSeasonPending = true;
            ModeHRuntimeGates.SetRecoveryOnlyBlocked(true, failure);
            OpenRecoveryShell(failure);
        }

        private void CancelSeasonResume()
        {
            if (_resumeScenePending
                && BossRushMapSelectionHelper.GetPendingModeHSceneGeneration() == _resumeSceneIntentGeneration)
                ModeHEntry.CancelPendingEntry();
            _resumeScenePending = false;
            _resumeSceneIntentGeneration = 0;
        }
    }
}
