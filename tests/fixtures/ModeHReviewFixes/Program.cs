using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Data;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label)
    { if (!value) throw new Exception("FAIL: " + label); checks++; }
    private static void Reset()
    {
        ModeHSaveFlushCoordinator.ResetStaticCaches(); BossRushSaveFileThrottle.ResetStaticCaches();
        Saves.SavesSystem.PhysicalWrites = 0; Saves.SavesSystem.IsSaving = false;
        Saves.SavesSystem.FailNext = false; ModeHProfilePersistence.IsWriteBarrier = false;
        ModeHProfilePersistence.IsStoreFaulted = false; ModeHHallOfFamePersistence.IsStoreFaulted = false;
        ModeHRuntimeGates.IsModeHCombatFrameActive = false; ModeHRuntimeGates.SlotGeneration = 1;
        ModeHWarehouseStakeJournal.Active = null; ModeHWarehouseStakeJournal.IsSlotConsistent = true;
        UnityEngine.Time.frameCount++;
    }
    private static ModeHRuntimeModule Runtime(ModeHLifecycle lifecycle, int match = 6)
    {
        var season = new ModeHSeasonDto {
            runState = new ModeHRunStateDto { runId = "saved-run", runSeed = 42, lifecycle = (int)lifecycle, matchIndex = match, sceneName = "arena" },
            contract = new ModeHContractDto { contractMainProfileId = "main", contractSubProfileId = "sub" },
            profiles = new List<ModeHProfileDto> {
                new ModeHProfileDto { profileId = "main", status = (int)ModeHParticipantStatus.Available },
                new ModeHProfileDto { profileId = "sub", status = (int)ModeHParticipantStatus.Injured, injuryId = "injury" } },
            appliedEventTokenIds = new List<string> { "kept-token" },
            matchReports = new List<ModeHMatchReportDto>(),
            seasonRewardOperations = new List<ModeHSeasonRewardOperationDto>(),
            productionCertificationSnapshot = new ModeHProductionCertificationDto { overallPassed = true },
            gameBuildSignature = "game", modBuildSignature = "mod", contentCatalogSignature = "content",
        };
        return new ModeHRuntimeModule { _season = season, _runState = ModeHRunState.FromDto(season.runState) };
    }
    private static void Main()
    {
        DurableProgress(); ValidationArchive(); LegacyOccurrences(); Recovery(); Rest();
        Console.WriteLine("PASS: " + checks + " assertions; production branches with host boundaries stubbed, no Unity smoke");
    }
    private static void DurableProgress()
    {
        Reset(); var run = Runtime(ModeHLifecycle.OddsPreview, 1); run.Lock();
        Check(run.Started == 1 && run.Failure == null, "stake barriers followed by lock actually reach spawn");
        Check(Saves.SavesSystem.PhysicalWrites == 5, "four journal writes and lock barrier are durable");
        string error;
        Check(!ModeHSaveFlushCoordinator.RequestSeasonWrite(run._season, out error), "ordinary writes still throttle");
        UnityEngine.Time.frameCount++; ModeHSaveFlushCoordinator.Tick();
        Check(!ModeHSaveFlushCoordinator.HasDeferredFlush, "ordinary debt still retries");
        Reset(); run = Runtime(ModeHLifecycle.Intermission);
        var report = new ModeHMatchReportDto { matchIndex = 6, winner = (int)ModeHMatchOutcome.PlayerVictory, seasonRewardOperationId = "reward" };
        run._season.matchReports.Add(report); run._lastSettlementReport = report;
        run._lastRewardOperation = new ModeHSeasonRewardOperationDto { operationId = "reward", status = (int)ModeHSeasonRewardOperationStatus.Applied };
        run._season.seasonRewardOperations.Add(run._lastRewardOperation);
        run.Complete();
        Check(run._runState.Lifecycle == ModeHLifecycle.HallOfFame && run.Failure == null, "virtual-only champion reaches hall in one callback");
        Check(Saves.SavesSystem.PhysicalWrites == 4, "archive/pending/insert/completed barriers physically write");
        Check(run._season.hallOfFameCommand.status == (int)ModeHHallOfFameCommandStatus.Completed, "hall command completed after writes");
        Reset(); Saves.SavesSystem.FailNext = true;
        Check(!ModeHSaveFlushCoordinator.RequestSeasonWrite(run._season, out error, true), "durable barrier does not hide IO failure");
        UnityEngine.Time.frameCount++; ModeHSaveFlushCoordinator.Tick();
        Check(Saves.SavesSystem.PhysicalWrites == 1, "failed durable write retains retry debt");
        Saves.SavesSystem.IsSaving = true;
        Check(!ModeHSaveFlushCoordinator.RequestSeasonWrite(run._season, out error, true), "durable barrier still respects active host save");
    }
    private static void LegacyOccurrences()
    {
        Reset(); var item = new Item { Tree = new ItemTreeData { rootInstanceID = 7 } };
        item.Tree.entries.Add(new ItemTreeData.DataEntry { instanceID = 7, typeID = 500030 });
        item.Tree.RootData.variables.Add(new CustomData("AFX_name", CustomDataType.String, new byte[] { 65 }));
        string reason;
        var legacy = ModeHItemTreeNormalizer.TryCapture(item, 0, 1, out reason, false);
        var current = ModeHItemTreeNormalizer.TryCapture(item, 0, 1, out reason);
        ModeHInventoryPersistenceBridge.Inventory.Content.Clear();
        ModeHInventoryPersistenceBridge.Inventory.Content.Add(item);
        Check(legacy.semanticTreeDigest != current.semanticTreeDigest, "legacy/current formats remain distinct");
        Check(ModeHInventoryPersistenceBridge.CountOccurrences(legacy) == 1, "legacy original item matches");
        Check(ModeHInventoryPersistenceBridge.CountOccurrences(current) == 1, "current original item matches");
        item.Tree.RootData.variables.Clear();
        Check(ModeHInventoryPersistenceBridge.CountOccurrences(legacy) == 0, "same TypeID with different content rejected");
        Check(ModeHInventoryPersistenceBridge.CountOccurrences(current) == 0, "current replacement rejected");
    }
    private static void ValidationArchive()
    {
        Reset(); var run = Runtime(ModeHLifecycle.Drafting, 0);
        string error;
        Check(ModeHSaveFlushCoordinator.RequestSeasonWrite(run._season, out error, true), "simulate same-frame certification write");
        int writes = Saves.SavesSystem.PhysicalWrites;
        Check(run.DebugFinishValidationSeason(), "F3 archive must complete despite ordinary same-frame throttle");
        Check(run._runState.Lifecycle == ModeHLifecycle.None && run.Exits == 1
            && Saves.SavesSystem.PhysicalWrites == writes + 1, "F3 completion includes physical archive before exit");
        Reset(); run = Runtime(ModeHLifecycle.Drafting, 0); Saves.SavesSystem.FailNext = true;
        Check(!run.DebugFinishValidationSeason(), "F3 archive must still report an actual write failure");
    }
    private static void Recovery()
    {
        Reset(); var original = Runtime(ModeHLifecycle.Suspended, 2);
        ModeHProfilePersistence.Current = original._season;
        var run = new ModeHRuntimeModule(); run.Restore();
        Check(ReferenceEquals(run._season, original._season), "complete season restored into runtime");
        Check(run._runState.OwnerToken != original._runState.OwnerToken && run._runState.HasEventToken("kept-token"), "fresh owner preserves committed events");
        Check(run.Derive() == ModeHLifecycle.MatchBrief && run._restoredSeasonPending, "saved match is resumable but awaits explicit preparation");
        typeof(ModeHCanonicalDigest).GetField("_cachedGameSignature", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, "game");
        typeof(ModeHCanonicalDigest).GetField("_cachedModSignature", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, "mod");
        string error;
        Check(run.Prepare(out error), "matching build/catalog/slot/certification permits preparation: " + error);
        run._season.modBuildSignature = "other";
        Check(!run.Prepare(out error) && error == "season_resume_signature_mismatch", "different build cannot resume or restamp");
        run._season.modBuildSignature = "mod";
        ModeHRuntimeGates.SlotGeneration++;
        Check(!run.Prepare(out error), "stale slot rejected before gameplay");
        ModeHRuntimeGates.SlotGeneration--;
        run._resumeScenePending = true; run._resumeSceneIntentGeneration = 7;
        BossRushMapSelectionHelper.Generation = 7; BossRushMapSelectionHelper.SceneName = "arena";
        Check(run.Scene("unrelated") && run._resumeScenePending, "unrelated scene callback cannot consume intent");
        Check(!run.Current(original._runState.OwnerToken, 1, 7), "old owner cannot complete async resume");
        Check(run.Scene("arena") && !run._restoredSeasonPending && !run._resumeScenePending, "target scene consumes current resume");
        Check(run._runState.Lifecycle == ModeHLifecycle.MatchBrief && run.MatchResets == 1, "after leases resume same match with reservation reset");
        Check(ModeHArenaIsolationLease.Acquired == 1 && ModeHSpectatorLease.Acquired == 1, "both leases acquired");
        Check(BossRushMapSelectionHelper.Generation == 0, "resume intent cleared after arrival");
        ModeHProfilePersistence.Current = null; ModeHRuntimeGates.SlotGeneration++;
        run.SwitchSlot();
        Check(run._runState == null && run._season == null && run.Released > 0, "empty new slot clears old season and runtime");
        var settled = Runtime(ModeHLifecycle.Suspended, 3);
        settled._season.matchReports.Add(new ModeHMatchReportDto { matchIndex = 3, reportStatus = (int)ModeHMatchReportStatus.SettledPendingArchive });
        Check(settled.Derive() == ModeHLifecycle.Intermission, "settled fact resumes to intermission without replaying fight");
    }
    private static void Rest()
    {
        var run = Runtime(ModeHLifecycle.MatchSettling, 2);
        run._season.currentLoadoutLock = new ModeHLoadoutLockDto { matchStarterProfileId = "main", matchRelayProfileId = null };
        run._combatTelemetry._enteredProfileIds.Add("main");
        run.SettleRest();
        Check(run._season.profiles[1].status == (int)ModeHParticipantStatus.Available, "unselected injured relay recovers");
        Check(run._restedProfileIds.Count == 1 && run._restedProfileIds[0] == "sub", "rest presentation tracks recovered contract");
        run._season.profiles[1].status = (int)ModeHParticipantStatus.Injured;
        run._combatTelemetry._enteredProfileIds.Add("sub"); run.SettleRest();
        Check(run._season.profiles[1].status == (int)ModeHParticipantStatus.Injured, "actual entrant cannot recover by resting");
        foreach (var status in new[] { ModeHParticipantStatus.Released, ModeHParticipantStatus.Retired, ModeHParticipantStatus.Removed })
        {
            run._season.profiles[1].status = (int)status; run._combatTelemetry._enteredProfileIds.Clear(); run.SettleRest();
            Check(run._season.profiles[1].status == (int)status && run._restedProfileIds.Count == 0, "excluded contract remains " + status);
        }
    }
}
