// Test-only host boundaries. Algorithms and lifecycle transitions come from production sources.
using System;
using System.Collections.Generic;
using ItemStatsSystem;
namespace ItemStatsSystem { public class Inventory { public List<Item> Content = new List<Item>(); } }
namespace BossRush
{
    public partial class ModBehaviour
    {
        public bool Conflict;
        public static void DevLog(string text) { }
        public bool HasLegacyModeConflictForModeH(out string id) { id = Conflict ? "conflict" : null; return Conflict; }
        public void ShowMessage(string text) { }
    }
    public static partial class ModeHRuntimeGates
    {
        public static int SlotGeneration = 1;
        public static bool OwnerActive, IsModeHContentReady = true;
        public static void SetRunOwnerActive(bool active) { OwnerActive = active; }
    }
    public static partial class ModeHProfilePersistence
    {
        public static bool IsWriteBarrier;
        public static ModeHSeasonDto Current;
        public static ModeHSeasonDto LoadCurrent() { return Current; }
    }
    public static partial class ModeHWarehouseStakeJournal
    {
        public static ModeHStakeJournalDto Active;
        public static bool IsSlotConsistent = true;
        public static bool IsTerminalPhase(ModeHStakePhase phase) { return ModeHStateModel.IsStakePhaseTerminal(phase); }
    }
    public static class ModeHRealStakeService
    {
        public static bool TryAbortReturn(long seed, int match, out string error) { error = null; return true; }
    }
    public static class ModeHContentCatalog { public static string ContentCatalogSignature = "content"; }
    public class ModeHProductionCertification
    {
        public bool TryRestoreSeasonReport(ModeHProductionCertificationDto report) { return report != null && report.overallPassed; }
    }
    public class ModeHSupportedMap { public string SceneName = "arena", SceneId = "arena-id"; public int SpectatorPos; }
    public static class ModeHMapSupportRegistry
    {
        public static ModeHSupportedMap Map = new ModeHSupportedMap();
        public static bool TryGetMap(string name, out ModeHSupportedMap map) { map = Map; return map != null && map.SceneName == name; }
    }
    public class ModeHArenaIsolationLease
    {
        public static int Acquired; public static bool Reject;
        public bool TryAcquire(string name, int gen, long owner, out string error) { error = null; if (Reject) return false; Acquired++; return true; }
    }
    public class ModeHSpectatorLease
    {
        public static int Acquired; public static bool Reject;
        public bool TryAcquire(int position, int gen, long owner, out string error) { error = null; if (Reject) return false; Acquired++; return true; }
    }
    public class SceneRuntimeContext { public string SceneName; }
    public static class BossRushMapSelectionHelper
    {
        public static int Generation; public static string SceneName;
        public static int GetPendingModeHSceneGeneration() { return Generation; }
        public static bool TryMatchModeHSceneIntent(string name, string id, out int generation)
        { generation = Generation; return name == SceneName && id == "arena-id"; }
    }
    public static class ModeHEntry { public static void CancelPendingEntry() { BossRushMapSelectionHelper.Generation = 0; } }
    public static class ModeHSeasonRewardService
    {
        public static bool TryArchive(ModeHSeasonDto season, string id, out string error) { error = null; return true; }
    }
    internal static partial class ModeHInventoryPersistenceBridge
    {
        public static Inventory Inventory = new Inventory();
        private static Inventory TryGetInventory(out string reason) { reason = null; return Inventory; }
    }
    internal partial class ModeHCombatTelemetry { public HashSet<string> _enteredProfileIds = new HashSet<string>(); }
    internal class ModeHCombatControl { public ModeHInjuryAndScarSystem InjuryAndScar = new ModeHInjuryAndScarSystem(); }
    internal partial class ModeHRuntimeModule
    {
        public ModeHSeasonDto _season;
        public ModeHRunState _runState;
        public ModeHMatchReportDto _lastSettlementReport;
        public ModeHSeasonRewardOperationDto _lastRewardOperation;
        public int _sceneGeneration, _restoredSlotGeneration, _resumeSceneIntentGeneration;
        public bool _restoredSeasonPending, _resumeScenePending, _resumeNeedsMatchReset, _commandsClosed, _shutdownCompleted;
        private bool _seasonDirty;
        private int _recoveryDriveStateSequence = -1;
        public ModBehaviour _owner = new ModBehaviour();
        public bool IsEnabled = true;
        public ModeHSupportedMap _map;
        private ModeHProductionCertification _certification;
        private ModeHArenaIsolationLease _arenaLease;
        private ModeHSpectatorLease _spectatorLease;
        public int Started, MatchResets, Released;
        public string Failure;
        public List<string> _restedProfileIds = new List<string>();
        public ModeHCombatTelemetry _combatTelemetry = new ModeHCombatTelemetry();
        private ModeHCombatControl _combatControl = new ModeHCombatControl();
        public void Restore() { RestoreFromSaveIfPresent(); }
        public void SwitchSlot() { RestoreForSlotChange(); }
        public void Complete() { CompleteSettlementAndRoute(); }
        public void Lock() { LockLoadoutAndStartMatch(); }
        public ModeHLifecycle Derive() { return DeriveResumeFromSeasonProgress(); }
        public bool Prepare(out string failure) { return TryPrepareSeasonResume(out failure); }
        public bool Scene(string name) { return TryHandleSeasonResumeScene(new SceneRuntimeContext { SceneName = name }); }
        public bool Current(long token, int slot, int intent) { return IsSeasonResumeRequestCurrent(token, slot, intent); }
        public void Cancel() { CancelSeasonResume(); }
        private void EnsureContentScanned() { }
        private void BeginNewRunSession() { _commandsClosed = false; _seasonDirty = false; }
        private void OnTransitionApplied(ModeHTransitionRecord r) { ProjectRunStateIntoSeason(); }
        private void LogFailure(string tag, Exception e) { Failure = tag; }
        private void RequestSuspended(string reason) { Failure = reason; TryTransition(_runState.Lifecycle, ModeHLifecycle.Suspended, reason); }
        private void RequestTechnicalRetry(string reason) { Failure = reason; }
        private void OpenRecoveryShell(string failure) { Failure = failure; }
        private void ReleaseRuntimeObjects() { Released++; }
        private void FinishSeason(string reason) { TryTransition(_runState.Lifecycle, ModeHLifecycle.SeasonEnded, reason); }
        private void OpenNextMatchBrief(string reason) { TryTransition(_runState.Lifecycle, ModeHLifecycle.MatchBrief, reason); }
        private ModeHHallOfFameRecordDto BuildHallOfFameRecord() { return new ModeHHallOfFameRecordDto { hallOfFameId = "hof|fixture" }; }
        private bool EnsureOddsPreview() { return _runState.Lifecycle == ModeHLifecycle.OddsPreview; }
        private bool PrepareLockedMatch(out string failure)
        {
            failure = null;
            for (int i = 0; i < 4; i++)
                if (!ModeHSaveFlushCoordinator.RequestStakeJournalWrite(new ModeHStakeJournalDto(), out failure)) return false;
            return true;
        }
        private void RestoreMatchReservationAndSnapshot() { MatchResets++; }
        private string ResolveLockRejectReason(string id) { return id; }
        private void StartMatchSpawning() { Started++; }
    }
}
