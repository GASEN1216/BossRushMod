using System;
using System.Collections.Generic;
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
        public static string T(string text, string en) { return text; }
    }
    internal class ModBehaviour
    {
        public Action<string> Record;
        public bool ThrowOnStop;
        public void ShowMessage(string text) { }
        public void StopCoroutine(object routine)
        {
            Record("stop_certification");
            if (ThrowOnStop) throw new Exception("injected stop failure");
        }
        public static void DevLog(string text) { }
        public static void CriticalLog(string text) { }
    }
    internal class HostResource
    {
        public Action<string> Record;
        public string Name;
        public bool ThrowOnRelease;
        public void Release(int generation)
        {
            Record(Name);
            if (ThrowOnRelease) throw new Exception("injected " + Name);
        }
        public void Cancel() { Release(0); }
        public void DestroyAll() { Release(0); }
        public void Hide() { Release(0); }
    }
    internal static class ModeHRuntimeGates
    {
        public static bool Active, RecoveryBlocked;
        public static Action<string> Record;
        public static void SetRunOwnerActive(bool active) { Active = active; Record("owner_gate"); }
        public static void SetRecoveryOnlyBlocked(bool active, string reason)
        { RecoveryBlocked = active; Record("recovery_gate"); }
    }
    internal static class ModeHProfilePersistence
    {
        public static ModeHSeasonDto Current;
        public static ModeHSeasonDto LoadCurrent() { return Current; }
    }
    internal static class ModeHSaveFlushCoordinator
    {
        public static bool WriteResult = true;
        public static bool ThrowOnWrite;
        public static int Writes;
        public static Action<string> Record;
        public static bool RequestSeasonWrite(ModeHSeasonDto season, out string error, bool durable)
        {
            Writes++; Record("write"); error = WriteResult ? null : "injected";
            if (ThrowOnWrite) throw new Exception("injected writer failure");
            if (!durable) throw new Exception("abandon must require durable write");
            return WriteResult;
        }
    }
    internal static partial class ModeHWarehouseStakeJournal
    {
        public static ModeHStakeJournalDto Active;
    }
    internal static partial class ModeHHallOfFamePersistence
    {
        private static bool _writeBarrier, _storeFaulted;
        private static string _lastError;
        private static ModeHHallOfFameEnvelopeDto _cache;
        public static int StageCalls;
        public static void Reset()
        { _cache = new ModeHHallOfFameEnvelopeDto { records = new List<ModeHHallOfFameRecordDto>() }; StageCalls = 0; }
        private static ModeHHallOfFameEnvelopeDto LoadCurrent() { return _cache; }
        private static bool StageEnvelope(ModeHHallOfFameEnvelopeDto dto, out string error)
        { StageCalls++; error = null; return true; }
        public static int Count { get { return _cache.records.Count; } }
    }
    internal partial class ModeHRuntimeModule
    {
        public ModeHSeasonDto _season;
        public ModeHRunState _runState;
        public ModeHSeasonRewardOperationDto _lastRewardOperation;
        public ModeHMatchReportDto _lastSettlementReport;
        public bool _commandsClosed, _shutdownCompleted, _restoredSeasonPending, _seasonDirty;
        public object _map = new object(), _activeRewardRevealRoot = new object(), _certificationRoutine = new object();
        private int _sceneGeneration = 2;
        private float _leaseCheckAccumulator;
        private bool _errorSwapInputYielded;
        private List<string> _restedProfileIds = new List<string>();
        public ModBehaviour _owner;
        public HostResource _certification, _spectatorLease, _arenaLease, _ui, _recoveryPanel;
        public List<string> Events = new List<string>();
        public bool EscrowReturnSucceeds, ThrowOnMatchRelease, PersistResult = true;
        public int Routes, Persists, Suspends;
        public bool CleanupKeptOwner = true;
        public bool CleanupOwnerExpected = true;
        public void InitHost()
        {
            _owner = new ModBehaviour { Record = Events.Add };
            _certification = Resource("cancel_certification");
            _spectatorLease = Resource("spectator"); _arenaLease = Resource("arena");
            _ui = Resource("ui"); _recoveryPanel = Resource("recovery_ui");
            ModeHRuntimeGates.Active = ModeHRuntimeGates.RecoveryBlocked = true;
            ModeHRuntimeGates.Record = Events.Add;
            ModeHSaveFlushCoordinator.Record = Events.Add;
        }
        private HostResource Resource(string name)
        {
            return new HostResource { Name = name, Record = delegate(string stage)
            {
                if (CleanupOwnerExpected) CleanupKeptOwner &= _runState != null && _season != null;
                Events.Add(stage);
            }};
        }
        private void CancelSeasonResume() { Events.Add("cancel_resume"); }
        private void ReturnEscrowFromRecovery()
        {
            Events.Add("return_escrow");
            if (EscrowReturnSucceeds) ModeHWarehouseStakeJournal.Active.phase = (int)ModeHStakePhase.RefundedTerminal;
        }
        private void LogFailure(string context, Exception error) { Events.Add("failure:" + context); }
        private void ReleaseMatchRuntime()
        {
            Events.Add("match");
            CleanupKeptOwner &= !CleanupOwnerExpected || (_runState != null && _commandsClosed);
            if (ThrowOnMatchRelease) throw new Exception("injected match release failure");
        }
        private string ResolveProfileDisplayName(string profile) { return profile; }
        private void BuildSettlementScarActions(ModeHPageContent page, ModeHMatchReportDto report) { }
        private bool TryPersistSeason(string reason, bool durable)
        { Persists++; if (!durable) throw new Exception("archive requires durable"); return PersistResult; }
        private void RequestSuspended(string reason) { Suspends++; }
        private void RouteAfterIntermission(ModeHMatchReportDto report) { Routes++; }
        public ModeHPageContent Page() { return BuildCompletedSettlementPageContent(); }
        public void Abandon() { AbandonSeasonFromRecovery(); }
        public void Complete() { CompleteSettlementAndRoute(); }
        public static string NewId() { return ComposeRunId("Scene_StormZone", 2); }
        public static long Seed(string id) { return ComposeRunSeed(id); }
    }
}
