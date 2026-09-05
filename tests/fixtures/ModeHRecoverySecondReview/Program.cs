using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
namespace BossRush
{
    internal static class Program
    {
        private static int _assertions;
        private static void Check(bool value, string description)
        {
            if (!value) throw new Exception(description);
            _assertions++; Console.WriteLine("PASS " + description);
        }
        private static ModeHRuntimeModule Create(ModeHLifecycle lifecycle = ModeHLifecycle.Intermission)
        {
            ModeHSaveFlushCoordinator.WriteResult = true;
            ModeHSaveFlushCoordinator.ThrowOnWrite = false;
            ModeHSaveFlushCoordinator.Writes = 0;
            ModeHWarehouseStakeJournal.Active = null;
            ModeHProfilePersistence.Current = null;
            var season = new ModeHSeasonDto
            {
                runState = new ModeHRunStateDto { runId = "legacy_run", runSeed = 42, sceneName = "Scene_StormZone",
                    sceneGeneration = 2, matchIndex = 1, lifecycle = (int)lifecycle },
                matchReports = new List<ModeHMatchReportDto> { new ModeHMatchReportDto {
                    matchIndex = 1, seasonRewardOperationId = "op1",
                    reportStatus = (int)ModeHMatchReportStatus.SettledPendingArchive } },
                seasonRewardOperations = new List<ModeHSeasonRewardOperationDto> { new ModeHSeasonRewardOperationDto {
                    operationId = "op1", status = (int)ModeHSeasonRewardOperationStatus.Offered,
                    rewardKind = (int)ModeHRewardKind.UnlockKit, rewardProfileId = "fighter",
                    candidateKitIds = new List<string> { "kitA" } } },
                profiles = new List<ModeHProfileDto> { new ModeHProfileDto { profileId = "fighter" } }
            };
            var runtime = new ModeHRuntimeModule { _season = season, _runState = ModeHRunState.FromDto(season.runState) };
            runtime.InitHost(); return runtime;
        }
        private static void Rewards()
        {
            var run = Create(); var page = run.Page();
            Check(run._lastRewardOperation == null && page.Actions.Count == 2, "restored DTO renders both reward choices without runtime caches");
            page.Actions[0].OnClick();
            Check(run._season.unlockedKitIds.Count == 1 && run._season.unlockedKitIds[0] == "kitA", "restored kit click invokes production reward application");
            Check(run._season.seasonRewardOperations[0].status == (int)ModeHSeasonRewardOperationStatus.Archived
                && run._season.matchReports[0].reportStatus == (int)ModeHMatchReportStatus.Archived
                && run.Persists == 1 && run.Routes == 1, "applied reward archives durably and routes");
            page.Actions[0].OnClick(); page.Actions[1].OnClick();
            Check(run.Routes == 1 && run._season.profiles[0].fameDisplayCount == 0, "repeated and alternate old click cannot apply twice");
            run = Create(); page = run.Page(); page.Actions[1].OnClick();
            Check(run._season.profiles[0].fameDisplayCount == ModeHConfig.ScarDeclineFameGain
                && run._season.unlockedKitIds == null && run.Routes == 1, "restored decline awards fame once and advances");
            run = Create(); page = run.Page(); run._runState = ModeHRunState.FromDto(run._season.runState); page.Actions[0].OnClick();
            Check(run.Persists == 0 && run._season.unlockedKitIds == null, "callback from former run owner is rejected");
            run = Create(); page = run.Page(); run._runState.RestoreMatchIndex(2, 0, 2); page.Actions[1].OnClick();
            Check(run.Persists == 0 && run._season.profiles[0].fameDisplayCount == 0, "callback from previous match is rejected");
            run = Create(); page = run.Page(); run._season.matchReports[0].seasonRewardOperationId = "replacement"; page.Actions[0].OnClick();
            Check(run.Persists == 0, "replaced operation cannot be targeted by stale page");
            run = Create(); page = run.Page(); run._commandsClosed = true; page.Actions[0].OnClick();
            Check(run.Persists == 0, "closed commands reject reward clicks");
            run = Create(); page = run.Page(); run._runState.RestoreLifecycle(ModeHLifecycle.Suspended, ModeHLifecycle.Unknown, ModeHLifecycle.Unknown); page.Actions[0].OnClick();
            Check(run.Persists == 0, "non-intermission rejects reward click");
            run = Create(); run._lastRewardOperation = new ModeHSeasonRewardOperationDto { operationId = "wrong", status = (int)ModeHSeasonRewardOperationStatus.Archived };
            run._lastSettlementReport = new ModeHMatchReportDto { matchIndex = 0, seasonRewardOperationId = "wrong" };
            run.Page().Actions[0].OnClick();
            Check(run._season.unlockedKitIds.Contains("kitA") && run.Routes == 1, "stale runtime caches cannot redirect current report or operation");
            run = Create(); page = run.Page(); run.PersistResult = false; page.Actions[0].OnClick();
            Check(run.Routes == 0 && run.Suspends == 1, "failed archive durability prevents route");
            run = Create(); run._season.seasonRewardOperations[0].status = (int)ModeHSeasonRewardOperationStatus.Applied;
            page = run.Page(); run._runState = ModeHRunState.FromDto(run._season.runState); page.Actions[0].OnClick();
            Check(run.Persists == 0, "stale confirmation also checks owner");
            run = Create(); run._season.seasonRewardOperations[0].status = (int)ModeHSeasonRewardOperationStatus.Applied;
            run.Page().Actions[0].OnClick(); Check(run.Routes == 1, "restored applied reward confirmation can archive");
        }
        private static void Abandonment()
        {
            var run = Create(ModeHLifecycle.Suspended); var old = run._season;
            run.Abandon();
            Check(old.runState.lifecycle == (int)ModeHLifecycle.SeasonEnded && ModeHSaveFlushCoordinator.Writes == 1,
                "abandon marks ended with durable barrier");
            Check(run.Events.IndexOf("write") < run.Events.IndexOf("match")
                && run.Events.IndexOf("match") < run.Events.IndexOf("spectator")
                && run.Events.IndexOf("spectator") < run.Events.IndexOf("arena")
                && run.Events.IndexOf("arena") < run.Events.IndexOf("ui")
                && run.Events.IndexOf("ui") < run.Events.IndexOf("owner_gate") && run.CleanupKeptOwner,
                "abandon releases match then spectator then arena then UI before clearing live owner");
            Check(run._runState == null && run._season == null && run._map == null && run._ui == null
                && run._spectatorLease == null && run._arenaLease == null && run._commandsClosed && run._shutdownCompleted
                && !ModeHRuntimeGates.Active && !ModeHRuntimeGates.RecoveryBlocked, "success closes commands and clears resources and gates");
            int oldSpectatorCalls = run.Events.FindAll(x => x == "spectator").Count;
            run.CleanupOwnerExpected = false; run.Abandon();
            Check(run.Events.FindAll(x => x == "spectator").Count == oldSpectatorCalls, "duplicate abandon has no repeated lease release");
            run = Create(ModeHLifecycle.Suspended); ModeHSaveFlushCoordinator.WriteResult = false; run.Abandon();
            Check(run._runState != null && run._season != null && run._recoveryPanel != null
                && !run._commandsClosed && !run.Events.Contains("match") && ModeHRuntimeGates.Active && ModeHRuntimeGates.RecoveryBlocked,
                "failed durable write preserves owner, leases and recovery controls");
            run = Create(ModeHLifecycle.Suspended); ModeHSaveFlushCoordinator.ThrowOnWrite = true; run.Abandon();
            Check(!run.Events.Contains("match") && run._runState != null && run._recoveryPanel != null,
                "throwing writer preserves recovery access");
            run = Create(ModeHLifecycle.Suspended); ModeHWarehouseStakeJournal.Active = new ModeHStakeJournalDto { phase = (int)ModeHStakePhase.EscrowRemovedDurable };
            run.Abandon();
            Check(run.Events.Contains("return_escrow") && ModeHSaveFlushCoordinator.Writes == 0
                && run._runState != null && !run.Events.Contains("match"), "unresolved escrow blocks abandonment before writes or cleanup");
            run = Create(ModeHLifecycle.Suspended); ModeHWarehouseStakeJournal.Active = new ModeHStakeJournalDto { phase = (int)ModeHStakePhase.EscrowRemovedDurable };
            run.EscrowReturnSucceeds = true; run.Abandon();
            Check(run.Events.IndexOf("return_escrow") < run.Events.IndexOf("write") && run._runState == null,
                "settled escrow precedes durable abandon and release");
            foreach (var terminal in new[] { ModeHStakePhase.Terminal, ModeHStakePhase.CancelledTerminal })
            {
                run = Create(ModeHLifecycle.Suspended);
                ModeHWarehouseStakeJournal.Active = new ModeHStakeJournalDto { phase = (int)terminal };
                run.Abandon();
                Check(!run.Events.Contains("return_escrow") && run._runState == null,
                    "terminal journal needs no second physical return: " + terminal);
            }
            run = Create(ModeHLifecycle.Suspended); run.ThrowOnMatchRelease = true;
            run._spectatorLease.ThrowOnRelease = true; run._ui.ThrowOnRelease = true; run._owner.ThrowOnStop = true;
            run.Abandon();
            Check(run.Events.Contains("failure:release_match") && run.Events.Contains("failure:release_spectator")
                && run.Events.Contains("arena") && run.Events.Contains("recovery_ui") && run._runState == null,
                "one failing cleanup stage cannot suppress later lease and UI cleanup attempts");
            run = Create(ModeHLifecycle.Suspended); ModeHProfilePersistence.Current = run._season;
            run._season = null; run._runState = null; run.CleanupOwnerExpected = false; run.Abandon();
            Check(ModeHProfilePersistence.Current.runState.lifecycle == (int)ModeHLifecycle.SeasonEnded
                && run._recoveryPanel == null && !ModeHRuntimeGates.RecoveryBlocked, "recovery shell can abandon persisted-only season");
        }
        private static string ChildId()
        {
            var start = new ProcessStartInfo("dotnet", "\"" + Assembly.GetExecutingAssembly().Location + "\" --emit-id")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var child = Process.Start(start))
            {
                string id = child.StandardOutput.ReadToEnd().Trim(); string error = child.StandardError.ReadToEnd();
                child.WaitForExit(); if (child.ExitCode != 0) throw new Exception(error);
                return id;
            }
        }
        private static void Identity()
        {
            string a = ChildId(), b = ChildId();
            Check(a != b && a.StartsWith("mh_Scene_StormZone_2_") && b.StartsWith("mh_Scene_StormZone_2_"),
                "fresh processes with same scene/generation generate distinct season IDs");
            var identities = new HashSet<string>(); for (int i = 0; i < 10000; i++) identities.Add(ModeHRuntimeModule.NewId());
            Check(identities.Count == 10000, "10000 same-scene new seasons have unique IDs");
            var state = new ModeHRunState(a, ModeHRuntimeModule.Seed(a), "Scene_StormZone", 2);
            var restored = ModeHRunState.FromDto(state.ToDto());
            Check(restored.RunId == state.RunId && restored.RunSeed == state.RunSeed && restored.OwnerToken != state.OwnerToken,
                "restore preserves persisted identity and seed but renews memory owner");
            var legacy = ModeHRunState.FromDto(new ModeHRunStateDto { runId = "mh_Scene_StormZone_2", runSeed = 12345 });
            Check(legacy.RunId == "mh_Scene_StormZone_2" && legacy.RunSeed == 12345, "legacy identifiers remain readable without migration");
            Check(ModeHRuntimeModule.Seed(a) == ModeHRuntimeModule.Seed(restored.RunId), "restored run identity preserves deterministic seed derivation");
            ModeHHallOfFamePersistence.Reset(); string error;
            Check(ModeHHallOfFamePersistence.StageRecordInsert(new ModeHHallOfFameRecordDto { hallOfFameId = "hof|" + a }, out error)
                && ModeHHallOfFamePersistence.StageRecordInsert(new ModeHHallOfFameRecordDto { hallOfFameId = "hof|" + b }, out error)
                && ModeHHallOfFamePersistence.Count == 2, "different sessions retain both champion records");
            Check(ModeHHallOfFamePersistence.StageRecordInsert(new ModeHHallOfFameRecordDto { hallOfFameId = "hof|" + restored.RunId }, out error)
                && ModeHHallOfFamePersistence.Count == 2 && ModeHHallOfFamePersistence.StageCalls == 2,
                "restored same-season hall command remains idempotent");
        }
        private static void Main(string[] args)
        {
            if (args.Length != 0 && args[0] == "--emit-id") { Console.WriteLine(ModeHRuntimeModule.NewId()); return; }
            Rewards(); Abandonment(); Identity();
            Console.WriteLine(_assertions + " assertions passed; production methods/DTO/run state, Unity/IO boundaries stubbed.");
        }
    }
}
