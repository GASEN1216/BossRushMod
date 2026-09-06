using System;
using System.Collections.Generic;
using System.Text.Json;
namespace Saves { internal static class SavesSystem { public static int CurrentSlot = 1; } }
namespace BossRush
{
    internal class ModeHPageContent
    {
        public string Title, Body;
        public List<string> Lines = new List<string>();
        public List<ModeHActionData> Actions = new List<ModeHActionData>();
    }
    internal class ModeHActionData { public string Label; public Action OnClick; }
    internal static class L10n
    {
        public static string T(string text) { return text; }
        public static string T(string cn, string en) { return cn; }
    }
    internal class ModBehaviour
    {
        public List<string> Messages = new List<string>();
        public void ShowMessage(string text) { Messages.Add(text); }
        public static void DevLog(string text) { }
    }
    internal static class ModeHAvailability { public static string GetReasonLocalizationKey(string text) { return text; } }
    internal static class ModeHRuntimeGates
    {
        public static int SlotGeneration = 1;
        public static void SetRecoveryOnlyBlocked(bool active, string reason) { }
    }
    internal static class ModeHInventoryPersistenceBridge
    {
        public static ModeHItemTreeSnapshotDto TryCaptureAt(int position, out string error)
        { error = null; return new ModeHItemTreeSnapshotDto { sourcePosition = position, gameQuality = 1 }; }
        public static List<int> ListOccupiedPositions(int max) { return new List<int> { 0 }; }
        public static bool TryDescribePosition(int position, out string name, out int quality)
        { name = "item"; quality = 1; return true; }
    }
    internal static class ModeHRewardTransaction
    {
        public static ModeHAbortReturnOperationDto BuildAbortReturnPlan(ModeHStakeJournalDto journal,
            long seed, int match, string token, string digest, out string error)
        { error = null; return new ModeHAbortReturnOperationDto(); }
        public static ModeHRewardOperationDto BuildMatchResultPlan(ModeHStakeJournalDto journal,
            long seed, int match, string token, bool won, int odds, string digest, out string error)
        { error = null; return new ModeHRewardOperationDto(); }
    }
    // 库存/持久化边界：可在每一步注入失败，记录是否错误越过屏障。
    internal static partial class ModeHWarehouseStakeJournal
    {
        public static ModeHStakeJournalDto Active;
        public static bool IsSlotConsistent = true;
        public static string FailAt;
        public static List<string> Calls = new List<string>();
        public static void Reset()
        { Active = null; IsSlotConsistent = true; FailAt = null; Calls.Clear(); ModeHRealStakeService.ClearSelection(); }
        private static bool Step(string step, out string error)
        { Calls.Add(step); error = FailAt == step ? "injected:" + step : null; return error == null; }
        public static bool TryPrepare(string tx, string run, int match, int slot, int gen,
            IList<ModeHItemTreeSnapshotDto> items, IList<ModeHItemTreeSnapshotDto> losses, out string error)
        {
            if (!Step("prepare", out error)) return false;
            Active = new ModeHStakeJournalDto { txId = tx, runId = run, matchIndex = match,
                phase = (int)ModeHStakePhase.Prepared, settlementKind = (int)ModeHSettlementKind.None };
            IsSlotConsistent = false; return true;
        }
        public static bool TryCommitEscrowSnapshot(out string error)
        { if (!Step("snapshot", out error)) return false; Active.phase = (int)ModeHStakePhase.EscrowSnapshotDurable; return true; }
        public static bool TryRemoveEscrow(out string error)
        { if (!Step("remove", out error)) return false; Active.phase = (int)ModeHStakePhase.EscrowRemovedDurable; return true; }
        public static bool TryLockMatch(out string error)
        { if (!Step("lock", out error)) return false; Active.phase = (int)ModeHStakePhase.MatchLocked; return true; }
        public static bool TryCancelWithoutRemoval(out string error)
        { if (!Step("cancel", out error)) return false; Active.phase = (int)ModeHStakePhase.CancelledTerminal; IsSlotConsistent = true; return true; }
        public static bool CommitAbortReturn(string token, ModeHAbortReturnOperationDto operation, out string error)
        { if (!Step("abort", out error)) return false; Active.phase = (int)ModeHStakePhase.AbortReturnCommitted; Active.settlementKind = (int)ModeHSettlementKind.AbortReturn; return true; }
        public static bool CommitResult(string token, ModeHRewardOperationDto operation, out string error)
        { if (!Step("result", out error)) return false; Active.phase = (int)ModeHStakePhase.ResultCommitted; Active.settlementKind = (int)ModeHSettlementKind.MatchResult; return true; }
        public static bool EnterSettlementPending(out string error)
        { if (!Step("pending", out error)) return false; Active.phase = (int)ModeHStakePhase.SettlementPending; return true; }
        public static bool ReturnEscrowItems(object destination, out string error) { return Step("return", out error); }
        public static bool GrantPlannedRewards(long seed, out string error) { return Step("grant", out error); }
        public static bool ApplyPlannedLosses(out string error) { return Step("loss", out error); }
        public static bool Settle(out string error)
        {
            if (!Step("settle", out error)) return false;
            Active.phase = Active.settlementKind == (int)ModeHSettlementKind.AbortReturn
                ? (int)ModeHStakePhase.RefundedTerminal : (int)ModeHStakePhase.Terminal;
            IsSlotConsistent = true; return true;
        }
    }
    internal static class ModeHSaveFlushCoordinator
    {
        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        public static bool FailBeforeWrite, FailAfterWrite;
        public static string Disk;
        public static int DurableWrites;
        public static bool RequestSeasonWrite(ModeHSeasonDto season, out string error, bool durable)
        {
            if (durable) DurableWrites++;
            error = FailBeforeWrite || FailAfterWrite ? "injected-write-failure" : null;
            if (!FailBeforeWrite) Disk = JsonSerializer.Serialize(season, Json);
            return error == null;
        }
        public static void Reset() { FailBeforeWrite = FailAfterWrite = false; Disk = null; DurableWrites = 0; }
    }
    internal static class ModeHTransferMarket
    {
        public static List<string> GetLiveContractProfileIds(ModeHSeasonDto season)
        { return new List<string> { "fighter" }; }
    }
    internal class FakeSpawn { public void RollbackAll() { } }
    internal partial class ModeHRuntimeModule
    {
        public ModeHSeasonDto _season;
        public ModeHRunState _runState;
        public bool _commandsClosed, _seasonDirty, _resumeNeedsMatchReset;
        public int _recoveryDriveStateSequence = -1;
        public ModBehaviour _owner = new ModBehaviour();
        private readonly List<string> _restedProfileIds = new List<string>();
        private object _currentOddsQuote, _spawnRoutine;
        private FakeSpawn _spawnTransaction;
        private int _selectedVirtualStake;
        private string _starterDisplayName, _relayDisplayName;
        private ModeHMatchReportDto _lastSettlementReport;
        private ModeHSeasonRewardOperationDto _lastRewardOperation;
        public int Routes, Releases, ImplicitAbortReturns, UiRefreshes;
        public ModeHPageContent Page() { return BuildCompletedSettlementPageContent(); }
        public void Retry() { RequestTechnicalRetry("reinforcement_spawn_failed"); }
        public void AbortSpawn() { AbortMatchSpawning("spawn_failed"); }
        public void Recover() { DriveRecovery(); }
        public void Register(ModeHMatchReportDto report) { RecordScarOffer(report); }
        public bool Save() { return TryPersistSeason("fixture_snapshot", true); }
        public void Choose(string operation, string scar, string replaced, bool decline, long owner, int match)
        { ResolveScarOffer(operation, scar, replaced, decline, owner, match); }
        public bool Pending { get { return HasPendingScarOffers(); } }
        private void OnTransitionApplied(ModeHTransitionRecord record) { ProjectRunStateIntoSeason(); }
        private void TryReturnRealStakeOnAbort(string context) { ImplicitAbortReturns++; }
        private void ReleaseCombatRuntimeObjects() { Releases++; }
        private void LogFailure(string context, Exception error) { throw new Exception(context, error); }
        private string ResolveProfileDisplayName(string id) { return id; }
        private void RouteUiForLifecycle(ModeHLifecycle lifecycle) { UiRefreshes++; }
        private void FinishSeason(string reason) { Routes++; }
        private void EnterHallOfFame() { Routes++; }
        private void OpenNextMatchBrief(string reason) { Routes++; }
    }
    internal static class Program
    {
        private static int _assertions;
        private static void Check(bool passed, string name)
        { if (!passed) throw new Exception(name); _assertions++; Console.WriteLine("PASS " + name); }
        private static ModeHRuntimeModule Create(bool offer = true, int match = 1)
        {
            ModeHWarehouseStakeJournal.Reset(); ModeHSaveFlushCoordinator.Reset();
            ModeHSeasonDto season = new ModeHSeasonDto
            {
                runState = new ModeHRunStateDto { runId = "run", runSeed = 42, matchIndex = match,
                    lifecycle = (int)ModeHLifecycle.Intermission },
                profiles = new List<ModeHProfileDto> { new ModeHProfileDto { profileId = "fighter", scarIds = new List<string>() } },
                matchReports = new List<ModeHMatchReportDto>(), seasonRewardOperations = new List<ModeHSeasonRewardOperationDto>(),
            };
            ModeHRuntimeModule runtime = new ModeHRuntimeModule { _season = season, _runState = ModeHRunState.FromDto(season.runState) };
            if (offer) AddOffer(runtime, match, true);
            return runtime;
        }
        private static ModeHMatchReportDto AddOffer(ModeHRuntimeModule runtime, int match, bool register)
        {
            var report = new ModeHMatchReportDto { matchIndex = match, resultToken = "result" + match,
                seasonRewardOperationId = "reward" + match, scarOfferId = "center_keeper",
                winner = (int)ModeHMatchOutcome.PlayerVictory, reportStatus = (int)ModeHMatchReportStatus.SettledPendingArchive };
            runtime._season.matchReports.Add(report);
            runtime._season.seasonRewardOperations.Add(new ModeHSeasonRewardOperationDto { operationId = "reward" + match,
                matchIndex = match, resultToken = "result" + match, rewardProfileId = "fighter",
                status = (int)ModeHSeasonRewardOperationStatus.Offered, rewardKind = (int)ModeHRewardKind.UnlockKit,
                candidateKitIds = new List<string> { "kitA" } });
            if (register) runtime.Register(report);
            return report;
        }
        private static ModeHRuntimeModule Cold(string disk)
        {
            var season = JsonSerializer.Deserialize<ModeHSeasonDto>(disk, ModeHSaveFlushCoordinator.Json);
            var run = ModeHRunState.FromDto(season.runState); run.RestoreEventTokens(season.appliedEventTokenIds);
            return new ModeHRuntimeModule { _season = season, _runState = run };
        }
        private static ModeHProfileDto Fighter(ModeHRuntimeModule run) { return run._season.profiles[0]; }
        private static Action Click(ModeHRuntimeModule run, string prefix)
        { return run.Page().Actions.Find(x => x.Label.StartsWith(prefix, StringComparison.Ordinal)).OnClick; }
        private static void ScarTests()
        {
            var run = Create(); run.Save(); string offeredDisk = ModeHSaveFlushCoordinator.Disk;
            Check(run.Page().Actions.Count == 4, "live candidate and kit actions are reachable");
            var cold = Cold(offeredDisk);
            Check(cold.Page().Actions.Count == 4, "cold candidate resolves persisted reward owner without runtime cache");
            Action accept = Click(cold, "留下战痕"); accept(); accept();
            Check(Fighter(cold).scarIds.Count == 1 && !cold.Pending, "accept grants once and closes offer");
            Check(ModeHSaveFlushCoordinator.DurableWrites >= 2, "offer snapshot and resolution cross durable barriers");
            var resolved = Cold(ModeHSaveFlushCoordinator.Disk);
            Check(resolved.Page().Actions.Count == 2 && Fighter(resolved).scarIds.Count == 1, "accepted scar and receipt survive cold reload together");
            cold = Cold(offeredDisk); Action decline = Click(cold, "拒绝战痕"); decline(); decline();
            Check(Fighter(cold).fameDisplayCount == 1 && !cold.Pending, "decline fame is exactly once");
            resolved = Cold(ModeHSaveFlushCoordinator.Disk);
            Check(Fighter(resolved).fameDisplayCount == 1 && resolved.Page().Actions.Count == 2, "decline receipt survives reload without resurrecting choice");
            run = Create(); Fighter(run).scarIds.AddRange(new[] { "old1", "old2", "old3" }); run.Save(); cold = Cold(ModeHSaveFlushCoordinator.Disk);
            Check(cold.Page().Actions.Count == 6, "three owned scars expose explicit replacements and decline");
            Action replace = Click(cold, "替换：" + ModeHConfig.LocalizationKeyPrefix + "Scar_old2"); replace(); replace();
            Check(Fighter(cold).scarIds.Count == 3 && !Fighter(cold).scarIds.Contains("old2") && Fighter(cold).scarIds.Contains("center_keeper"), "replacement applies exactly once");
            resolved = Cold(ModeHSaveFlushCoordinator.Disk);
            Check(!resolved.Pending && !Fighter(resolved).scarIds.Contains("old2") && Fighter(resolved).scarIds.Count == 3,
                "replacement and resolution receipt survive cold reload together");
            run = Create(); Fighter(run).scarIds.AddRange(new[] { "old1", "old2", "old3" });
            run.Choose("reward1", "center_keeper", "missing", false, run._runState.OwnerToken, 1);
            Check(run.Pending && Fighter(run).scarIds.Count == 3, "invalid replacement preserves original scars and pending offer");
            run = Create(); Action stale = Click(run, "拒绝战痕"); run._commandsClosed = true; stale();
            Check(Fighter(run).fameDisplayCount == 0, "closed owner rejects queued scar callback");
            run._commandsClosed = false; run._runState.RestoreMatchIndex(2, 0, 0); stale();
            Check(Fighter(run).fameDisplayCount == 0, "changed page match rejects stale callback");
            run = Create(); long oldOwner = run._runState.OwnerToken; run._runState = ModeHRunState.FromDto(run._runState.ToDto());
            run.Register(run._season.matchReports[0]); run.Choose("reward1", "center_keeper", null, true, oldOwner, 1);
            Check(Fighter(run).fameDisplayCount == 0, "new runtime owner rejects old callback");
            run.Choose("different-operation", "center_keeper", null, true, run._runState.OwnerToken, 1);
            run.Choose("reward1", "other-scar", null, true, run._runState.OwnerToken, 1);
            Check(Fighter(run).fameDisplayCount == 0, "operation and candidate identities are both checked");
            run._season.seasonRewardOperations[0].resultToken = "other-result";
            Check(run.Page().Actions.Count == 2, "mismatched report and reward result cannot choose another owner");
            foreach (bool after in new[] { false, true })
            {
                run = Create(); run.Save(); string before = ModeHSaveFlushCoordinator.Disk;
                ModeHSaveFlushCoordinator.FailBeforeWrite = !after; ModeHSaveFlushCoordinator.FailAfterWrite = after;
                Action action = Click(run, "拒绝战痕"); action(); action();
                Check(Fighter(run).fameDisplayCount == 1 && !run.Pending && run._runState.Lifecycle == ModeHLifecycle.Suspended,
                    "uncertain write retains effect and token, suspends without replay (after=" + after + ")");
                ModeHSaveFlushCoordinator.FailBeforeWrite = ModeHSaveFlushCoordinator.FailAfterWrite = false;
                cold = Cold(after ? ModeHSaveFlushCoordinator.Disk : before);
                cold._runState.RestoreLifecycle(ModeHLifecycle.Intermission, ModeHLifecycle.Unknown, ModeHLifecycle.Unknown);
                if (cold.Pending) Click(cold, "拒绝战痕")();
                Check(Fighter(cold).fameDisplayCount == 1 && !cold.Pending, "cold uncertain-write recovery grants exactly once (after=" + after + ")");
            }
            run = Create(); Click(run, "解锁整备")();
            Check(run.Routes == 1 && run.Pending, "kit first may advance while scar remains durably pending");
            run._runState.RestoreMatchIndex(2, 0, 0); AddOffer(run, 2, true); run.Save(); cold = Cold(ModeHSaveFlushCoordinator.Disk);
            Click(cold, "拒绝战痕")();
            Check(Fighter(cold).fameDisplayCount == 1 && cold.Pending, "next match first resolves prior archived offer");
            Click(cold, "拒绝战痕")();
            Check(Fighter(cold).fameDisplayCount == 2 && !cold.Pending, "deferred queue resolves each report once");
            run = Create(true, ModeHConfig.SeasonMatchCount); Click(run, "解锁整备")();
            Check(run.Routes == 0 && run.Pending, "final archive cannot discard unresolved scar");
            Click(run, "拒绝战痕")(); Click(run, ModeHConfig.LocalizationKeyPrefix + "Button_Confirm")();
            Check(run.Routes == 1 && !run.Pending, "final season remains finishable after scar resolution");
            run = Create(false); AddOffer(run, 1, false); run.Save(); cold = Cold(ModeHSaveFlushCoordinator.Disk);
            Check(cold.Page().Actions.Count == 2 && cold.Page().Lines.Exists(x => x.Contains("旧版未记录"))
                && Fighter(cold).fameDisplayCount == 0, "ambiguous legacy receipt is preserved without invented reward");
            string oldRecord = JsonSerializer.Serialize(cold._season.matchReports[0], ModeHSaveFlushCoordinator.Json);
            cold.Page(); cold.Page(); cold.Save(); resolved = Cold(ModeHSaveFlushCoordinator.Disk);
            Check(oldRecord == JsonSerializer.Serialize(resolved._season.matchReports[0], ModeHSaveFlushCoordinator.Json)
                && !resolved.Pending && resolved._runState.ExportEventTokens().Count == 0,
                "viewing and resaving ambiguous legacy offer does not invent history or migrate the original report");
            run = Create(false, ModeHConfig.SeasonMatchCount); AddOffer(run, ModeHConfig.SeasonMatchCount, false);
            Click(run, "解锁整备")();
            Check(run.Routes == 1 && !run.Pending, "ambiguous legacy offer cannot block ordinary season completion");
            run = Create(false); AddOffer(run, 1, false); Fighter(run).scarIds.Add("center_keeper"); Fighter(run).fameDisplayCount = 7;
            run.Save(); cold = Cold(ModeHSaveFlushCoordinator.Disk);
            Check(cold.Page().Actions.Count == 2 && !cold.Page().Lines.Exists(x => x.Contains("旧版未记录"))
                && Fighter(cold).scarIds.Count == 1 && Fighter(cold).fameDisplayCount == 7,
                "legacy already-owned scar is recognized without regranting or claiming an unresolved reward");
        }
        private static ModeHRuntimeModule Battle(ModeHStakePhase phase)
        {
            var run = Create(false);
            run._runState.RestoreLifecycle(ModeHLifecycle.MatchFighting, ModeHLifecycle.Unknown, ModeHLifecycle.Unknown);
            run._season.virtualStakeCredits = 4; run._season.reservedVirtualStake = 2;
            run._season.preMatchSnapshot = new ModeHPreMatchSnapshotDto { virtualStakeBalanceBeforeReservation = 6, reservedVirtualStake = 2 };
            run._season.currentLoadoutLock = new ModeHLoadoutLockDto { realStakeSelected = true };
            ModeHWarehouseStakeJournal.Active = new ModeHStakeJournalDto { txId = "tx", runId = "run", matchIndex = 1,
                phase = (int)phase, settlementKind = (int)(phase == ModeHStakePhase.AbortReturnCommitted || phase == ModeHStakePhase.SettlementPending
                    ? ModeHSettlementKind.AbortReturn : ModeHSettlementKind.None) };
            ModeHWarehouseStakeJournal.IsSlotConsistent = false;
            return run;
        }
        private static void StakeTests()
        {
            foreach (var phase in new[] { ModeHStakePhase.Prepared, ModeHStakePhase.EscrowSnapshotDurable,
                ModeHStakePhase.EscrowRemovedDurable, ModeHStakePhase.MatchLocked, ModeHStakePhase.AbortReturnCommitted,
                ModeHStakePhase.SettlementPending })
            {
                var run = Battle(phase); run.Retry();
                Check(run._runState.Lifecycle == ModeHLifecycle.Recovering && run._season.virtualStakeCredits == 6
                    && run._season.currentLoadoutLock == null && ModeHWarehouseStakeJournal.IsSlotConsistent,
                    "technical retry proves cancellation/refund before clearing virtual reservation: " + phase);
                run.Recover();
                Check(run._runState.Lifecycle == ModeHLifecycle.MatchBrief, "safe retry reaches same match brief: " + phase);
            }
            foreach (string failure in new[] { "abort", "pending", "return", "settle" })
            {
                var run = Battle(ModeHStakePhase.MatchLocked); ModeHWarehouseStakeJournal.FailAt = failure; run.Retry();
                Check(run._runState.Lifecycle == ModeHLifecycle.Suspended && run._season.currentLoadoutLock != null
                    && run._season.preMatchSnapshot != null && run._season.virtualStakeCredits == 4 && run.ImplicitAbortReturns == 0,
                    "failed " + failure + " preserves locked assets/snapshot and prevents implicit second return");
            }
            var current = Battle(ModeHStakePhase.MatchLocked); current._runState.RestoreLifecycle(ModeHLifecycle.MatchSpawning, ModeHLifecycle.Unknown, ModeHLifecycle.Unknown);
            ModeHWarehouseStakeJournal.FailAt = "return"; current.AbortSpawn();
            Check(current._runState.Lifecycle == ModeHLifecycle.Suspended && current._season.preMatchSnapshot != null, "initial spawn failure uses same strict refund barrier");
            current = Battle(ModeHStakePhase.ManualIntervention); current.Retry();
            Check(current._runState.Lifecycle == ModeHLifecycle.Suspended && current._season.currentLoadoutLock != null, "physical return in ManualIntervention is not a proven terminal refund");
            current = Battle(ModeHStakePhase.ResultCommitted); ModeHWarehouseStakeJournal.Active.settlementKind = (int)ModeHSettlementKind.MatchResult; current.Retry();
            Check(current._runState.Lifecycle == ModeHLifecycle.Suspended && ModeHWarehouseStakeJournal.Calls.Count == 0
                && current._season.currentLoadoutLock != null, "committed match result is never converted to refund and replay");
            current = Battle(ModeHStakePhase.MatchLocked); ModeHWarehouseStakeJournal.Active.runId = "other"; current.Retry();
            Check(current._runState.Lifecycle == ModeHLifecycle.Suspended && ModeHWarehouseStakeJournal.Calls.Count == 0, "different journal owner cannot be settled by retry");
            current = Battle(ModeHStakePhase.MatchLocked); current._runState.RestoreLifecycle(ModeHLifecycle.Recovering, ModeHLifecycle.MatchFighting, ModeHLifecycle.MatchFighting);
            current._resumeNeedsMatchReset = true; ModeHWarehouseStakeJournal.FailAt = "return"; current.Recover();
            Check(current._runState.Lifecycle == ModeHLifecycle.Suspended && current._season.preMatchSnapshot != null, "direct recovery cannot bypass refund barrier");
            current._resumeNeedsMatchReset = false; ModeHWarehouseStakeJournal.FailAt = null;
            current._runState.RestoreLifecycle(ModeHLifecycle.Recovering, ModeHLifecycle.MatchFighting, ModeHLifecycle.MatchFighting);
            current._recoveryDriveStateSequence = -1; current.Recover();
            Check(current._runState.Lifecycle == ModeHLifecycle.MatchBrief && current._season.virtualStakeCredits == 6
                && current._season.currentLoadoutLock == null && current._season.preMatchSnapshot == null,
                "successful same-session retry clears preserved reservation even when no scene-resume flag was set");
            current = Battle(ModeHStakePhase.MatchLocked); string error;
            Check(!ModeHRealStakeService.TryLockForMatch("run", 1, 42, out error) && ModeHWarehouseStakeJournal.Calls.Count == 0, "empty selection rejects active inconsistent journal");
            ModeHWarehouseStakeJournal.Reset(); ModeHWarehouseStakeJournal.IsSlotConsistent = false;
            Check(!ModeHRealStakeService.TryLockForMatch("run", 1, 42, out error), "empty selection rejects unresolved historical slot evidence even without active DTO");
            ModeHWarehouseStakeJournal.IsSlotConsistent = true;
            Check(ModeHRealStakeService.TryLockForMatch("run", 1, 42, out error) && ModeHWarehouseStakeJournal.Active == null, "consistent pure virtual match creates no journal");
            Check(ModeHRealStakeService.ToggleSelection(0, out error) && ModeHRealStakeService.TryLockForMatch("run", 1, 42, out error)
                && ModeHRealStakeService.SelectedCount == 0, "normal explicit selection remains lockable");
            Check(ModeHRealStakeService.HasLockedStakeForMatch("run", 1)
                && !ModeHRealStakeService.HasLockedStakeForMatch("other", 1), "locked stake belongs only to exact run and match");
            Check(ModeHRealStakeService.TryPrepareTechnicalRetry("run", 42, 1, out error)
                && ModeHRealStakeService.TryLockForMatch("run", 1, 42, out error), "refunded empty retry remains playable");
            Check(!ModeHRealStakeService.HasLockedStakeForMatch("run", 1), "refunded terminal journal is not displayed as a new real stake");
            current = Battle(ModeHStakePhase.ResultCommitted); ModeHWarehouseStakeJournal.Active.settlementKind = (int)ModeHSettlementKind.MatchResult;
            AddOffer(current, 1, true); current.Retry(); current.Recover();
            Check(current._runState.Lifecycle == ModeHLifecycle.Intermission && current._season.matchReports.Count == 1
                && current._season.currentLoadoutLock != null, "complete persistent report resumes settlement without virtual rollback");
        }
        private static void Main()
        {
            ScarTests(); StakeTests();
            Console.WriteLine("TOTAL " + _assertions + " production execution assertions");
        }
    }
}
