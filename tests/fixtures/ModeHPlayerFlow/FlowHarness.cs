// Host and persistence boundaries only. Entry and F3 gate methods are extracted from production.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public sealed class Coroutine { public IEnumerator Routine; public bool Done; }
}

namespace BossRush
{
    internal static class L10n
    {
        public static string T(string text, string en) { return text; }
        public static string T(string text) { return text; }
    }
    public partial class ModBehaviour
    {
        internal static bool DevModeEnabled;
        internal ModeHRuntimeModule ModeHRuntime;
        internal readonly List<string> Events = new List<string>();
        internal bool ThrowOnStart;
        internal UnityEngine.Coroutine LastRoutine;
        internal UnityEngine.Coroutine StartCoroutine(IEnumerator routine)
        {
            Events.Add("start_diagnostics");
            if (ThrowOnStart) throw new InvalidOperationException("start_failure");
            var stack = new ValidationCoroutineStack(routine);
            var handle = new UnityEngine.Coroutine { Routine = stack };
            LastRoutine = handle;
            if (!stack.MoveNext()) { stack.Dispose(); handle.Done = true; }
            return handle;
        }
        internal void Complete(UnityEngine.Coroutine routine)
        {
            if (routine.Done) return;
            int budget = 20;
            while (routine.Routine.MoveNext())
                if (--budget <= 0) throw new Exception("diagnostic fixture did not terminate");
            ((IDisposable)routine.Routine).Dispose();
            routine.Done = true;
        }
        internal void HideF3DebugCheatMenu() { Events.Add("close_f3"); }
        internal void SetF3DebugCheatStatus(string message, bool error) { Events.Add("gate_rejected"); }
        internal void ShowMessage(string message) { Events.Add("message"); }
        internal void ClickCertification() { StartModeHCertificationFromF3(); }
    }
    internal static class SavesSystem { internal static bool IsSaving; }
    internal static class SceneLoader { internal static bool IsSceneLoading; }
    internal static partial class F3GameplayValidationRunner
    {
        internal static bool Dedicated, IsRunning;
        private sealed class RunnerState { internal bool _running; }
        private static RunnerState _instance;
        internal static bool Running
        {
            set { _instance = value ? new RunnerState { _running = true } : null; }
        }
        private static bool IsDedicatedCurrentSlot() { return Dedicated; }
    }
    internal sealed class RunState
    {
        internal ModeHLifecycle Lifecycle;
        internal long OwnerToken;
    }
    internal sealed class ModeHSupportedMap { }
    internal sealed class Lease { internal bool IsActive; }
    internal sealed class ModeHProductionCertification : ReleaseCatalog
    {
        internal static int ReleaseCalls;
        internal static bool RejectRelease;
        internal static bool CompleteSynchronously, ThrowDuringRun, ThrowDuringChild;
        internal int RunCalls, CancelCalls, ChildSteps;
        internal bool Cancelled;
        internal new bool TryUseReleaseCatalog()
        { ReleaseCalls++; return !RejectRelease && base.TryUseReleaseCatalog(); }
        internal IEnumerator Run(IList<string> keys, ModeHSupportedMap map, ModeHCertificationResult result)
        {
            RunCalls++;
            result.TotalKeys = keys.Count;
            if (!CompleteSynchronously) yield return DiagnosticChild();
            if (ThrowDuringRun) throw new Exception("probe_failure");
            result.Completed = true;
            result.Passed = !Cancelled;
            result.FinishedKeys = Cancelled ? 0 : keys.Count;
        }
        private IEnumerator DiagnosticChild()
        {
            ChildSteps++;
            yield return null;
            if (ThrowDuringChild) throw new Exception("child_probe_failure");
            ChildSteps++;
        }
        internal void Cancel() { CancelCalls++; Cancelled = true; }
    }
    internal sealed class ModeHCertificationResult
    {
        internal bool Completed, Passed;
        internal int TotalKeys, FinishedKeys;
        internal string FailureReasonId;
    }
    internal sealed class ModeHUI
    {
        internal ModBehaviour Host;
        internal Action Cancel;
        internal int DestroyCalls, Routes;
        internal void ClosePage() { Host.Events.Add("close_draft"); }
        internal void EnsureDiagnostics(Action cancel) { Host.Events.Add("open_diagnostics"); Cancel = cancel; }
        internal void UpdateDiagnostics(string text, float progress) { }
        internal void DestroyDiagnostics() { DestroyCalls++; }
    }
    internal partial class ModeHRuntimeModule
    {
        internal ModBehaviour _owner;
        internal bool _shutdownCompleted, _commandsClosed, _certificationFromF3, _lastCertificationUsedCache;
        internal RunState _runState;
        internal ModeHSeasonDto _season;
        internal ModeHSupportedMap _map;
        internal Lease _arenaLease, _spectatorLease;
        internal UnityEngine.Coroutine _certificationRoutine;
        internal ModeHProductionCertification _certification;
        internal ModeHUI _ui;
        internal int _sceneGeneration = 7;
        internal bool PersistedSeasonActive;
        internal int CreatedSeasons, Aborts, Suspended;
        internal void Enter() { StartCertification(); }
        private bool BlockSetupIfPersistedSeasonActive() { return PersistedSeasonActive; }
        private void CreateDraftingSeason(ModeHProductionCertificationDto report)
        {
            CreatedSeasons++;
            _runState = new RunState { Lifecycle = ModeHLifecycle.Drafting, OwnerToken = 31 };
            _season = new ModeHSeasonDto { productionCertificationSnapshot = report };
        }
        private void AbortSetup(string reason, bool acquired) { Aborts++; }
        private void EnsureUi() { if (_ui == null) _ui = new ModeHUI { Host = _owner }; }
        private void LogFailure(string context, Exception error) { _owner.Events.Add("failure:" + context); }
        private void RequestSuspended(string reason) { Suspended++; }
        private void RouteUiForLifecycle(ModeHLifecycle lifecycle) { _ui.Routes++; }
        internal void Restore(ModeHProductionCertification certification, long owner, int generation)
        { RestorePlayerFlowAfterCertification(certification, owner, generation); }
    }
    internal static class FlowRegression
    {
        private static void Check(bool pass, string reason) { Program.Check(pass, reason); }
        private static ModeHRuntimeModule CreateReady()
        {
            var runtime = new ModeHRuntimeModule();
            runtime._owner = new ModBehaviour { ModeHRuntime = runtime };
            runtime._map = new ModeHSupportedMap();
            runtime._arenaLease = new Lease { IsActive = true };
            runtime._spectatorLease = new Lease { IsActive = true };
            runtime.Enter();
            return runtime;
        }
        internal static void Run()
        {
            foreach (bool dev in new[] { false, true })
            {
                ModBehaviour.DevModeEnabled = dev;
                var runtime = CreateReady();
                Check(runtime.CreatedSeasons == 1 && runtime._runState.Lifecycle == ModeHLifecycle.Drafting,
                    "ordinary entry immediately creates selection in release and Dev");
                Check(runtime._owner.Events.Count == 0 && runtime._certificationRoutine == null
                    && !runtime._certificationFromF3 && !runtime._lastCertificationUsedCache,
                    "ordinary entry does not schedule or open diagnostics");
                runtime.PersistedSeasonActive = true;
                runtime.Enter();
                Check(runtime.CreatedSeasons == 1, "existing season barrier prevents duplicate draft");
            }
            ModeHProductionCertification.RejectRelease = true;
            var rejected = CreateReady();
            Check(rejected.Aborts == 1 && rejected.CreatedSeasons == 0, "unavailable release catalog cannot open draft");
            ModeHProductionCertification.RejectRelease = false;

            ModBehaviour.DevModeEnabled = true;
            var ready = CreateReady();
            string reason;
            F3GameplayValidationRunner.Dedicated = true;
            Check(F3GameplayValidationRunner.CanRunModeHCertification(ready._owner, out reason), "F3 allows dedicated Dev draft");
            foreach (Action<bool> toggle in new Action<bool>[] {
                value => ModBehaviour.DevModeEnabled = !value,
                value => F3GameplayValidationRunner.Dedicated = !value,
                value => F3GameplayValidationRunner.Running = value,
                value => SavesSystem.IsSaving = value,
                value => SceneLoader.IsSceneLoading = value,
                value => ready._shutdownCompleted = value,
                value => ready._commandsClosed = value,
                value => ready._runState.Lifecycle = value ? ModeHLifecycle.MatchFighting : ModeHLifecycle.Drafting,
                value => ready._arenaLease.IsActive = !value,
                value => ready._spectatorLease.IsActive = !value,
                value => ready._certificationFromF3 = value })
            {
                toggle(true);
                Check(!F3GameplayValidationRunner.CanRunModeHCertification(ready._owner, out reason), "F3 rejects invalid gate state");
                toggle(false);
                Check(F3GameplayValidationRunner.CanRunModeHCertification(ready._owner, out reason), "F3 gate becomes usable after state clears");
            }
            string snapshot;
            Check(ModeHCanonicalDigest.TryWriteCanonicalObject(ready._season, null, out snapshot, out reason), "draft snapshot can be compared");
            ready._owner.ClickCertification();
            Check(string.Join(",", ready._owner.Events) == "close_f3,close_draft,open_diagnostics,start_diagnostics", "F3 closes before diagnostics release draft pause");
            var first = ready._certification;
            var firstRoutine = ready._certificationRoutine;
            Check(first.ChildSteps == 1 && first.RunCalls == 1 && ready._certificationFromF3, "diagnostic child coroutine actually runs");
            Check(!F3GameplayValidationRunner.CanRunModeHCertification(ready._owner, out reason), "running certification rejects second start");
            ready._owner.Complete(firstRoutine);
            Check(first.ChildSteps == 2 && first.CancelCalls == 1, "normal completion executes child and cleans old diagnostic instance");
            AssertRestored(ready, snapshot, "complete");

            Check(ready.StartCertificationFromF3(out reason), "second explicit diagnostic can start");
            var cancelled = ready._certification;
            ready._ui.Cancel();
            Check(cancelled.Cancelled, "cancel action reaches diagnostic owner");
            ready._owner.Complete(ready._certificationRoutine);
            AssertRestored(ready, snapshot, "cancel");

            ModeHProductionCertification.ThrowDuringRun = true;
            Check(ready.StartCertificationFromF3(out reason), "failing probe starts through same path");
            ready._owner.Complete(ready._certificationRoutine);
            ModeHProductionCertification.ThrowDuringRun = false;
            AssertRestored(ready, snapshot, "probe exception");

            ModeHProductionCertification.ThrowDuringChild = true;
            Check(ready.StartCertificationFromF3(out reason), "nested failing probe starts through same path");
            ready._owner.Complete(ready._certificationRoutine);
            ModeHProductionCertification.ThrowDuringChild = false;
            AssertRestored(ready, snapshot, "nested probe exception");

            ready._owner.ThrowOnStart = true;
            Check(!ready.StartCertificationFromF3(out reason), "host coroutine start failure is reported");
            ready._owner.ThrowOnStart = false;
            AssertRestored(ready, snapshot, "start exception");

            ModeHProductionCertification.CompleteSynchronously = true;
            Check(ready.StartCertificationFromF3(out reason), "synchronous diagnostic completion succeeds");
            ModeHProductionCertification.CompleteSynchronously = false;
            AssertRestored(ready, snapshot, "synchronous completion cannot leave stale handle");

            // Simulate Unity delaying a stopped coroutine's final Dispose until a newer test starts.
            Check(ready.StartCertificationFromF3(out reason), "old diagnostic starts before delayed cleanup");
            var old = ready._certification;
            var delayed = ready._certificationRoutine;
            ready.Restore(old, ready._runState.OwnerToken, ready._sceneGeneration);
            Check(ready.StartCertificationFromF3(out reason), "new diagnostic starts with same run owner and generation");
            var current = ready._certification;
            var currentRoutine = ready._certificationRoutine;
            int routes = ready._ui.Routes, destroys = ready._ui.DestroyCalls, releaseCalls = ModeHProductionCertification.ReleaseCalls;
            ((IDisposable)delayed.Routine).Dispose();
            Check(ReferenceEquals(ready._certification, current) && ReferenceEquals(ready._certificationRoutine, currentRoutine)
                && ready._certificationFromF3 && current.CancelCalls == 0,
                "late finally from old diagnostic cannot clear or cancel new instance");
            Check(ready._ui.Routes == routes && ready._ui.DestroyCalls == destroys && ModeHProductionCertification.ReleaseCalls == releaseCalls,
                "old finally cannot restore catalog or reopen UI during newer diagnostic");
            ready._owner.Complete(currentRoutine);
            AssertRestored(ready, snapshot, "new diagnostic after delayed old finally");

            Check(ready.StartCertificationFromF3(out reason), "stale owner fixture starts");
            var stale = ready._certification;
            var staleRoutine = ready._certificationRoutine;
            ready._sceneGeneration++;
            ready._owner.Complete(staleRoutine);
            Check(ReferenceEquals(ready._certification, stale) && ReferenceEquals(ready._certificationRoutine, staleRoutine)
                && ready._certificationFromF3 && stale.CancelCalls == 0,
                "scene-invalid callback leaves teardown ownership to runtime cleanup");
            ready._sceneGeneration--;
            ready.Restore(stale, ready._runState.OwnerToken, ready._sceneGeneration);
            AssertRestored(ready, snapshot, "runtime cleanup after invalid callback");

            ready._owner.Events.Clear();
            F3GameplayValidationRunner.Dedicated = false;
            ready._owner.ClickCertification();
            Check(string.Join(",", ready._owner.Events) == "gate_rejected", "rejected F3 action leaves original page open");
        }

        private static void AssertRestored(ModeHRuntimeModule runtime, string expectedSnapshot, string stage)
        {
            string snapshot, error;
            Check(ModeHCanonicalDigest.TryWriteCanonicalObject(runtime._season, null, out snapshot, out error)
                && snapshot == expectedSnapshot && runtime.CreatedSeasons == 1, stage + " preserves complete season and candidate data");
            Check(!runtime._certificationFromF3 && runtime._certificationRoutine == null
                && runtime._runState.Lifecycle == ModeHLifecycle.Drafting && runtime._certification.Report.overallPassed,
                stage + " restores playable release catalog and original draft phase");
            Check(runtime.Aborts == 0 && runtime.Suspended == 0 && runtime._ui.Routes > 0,
                stage + " returns to draft without refund or season exit");
        }
    }
}
