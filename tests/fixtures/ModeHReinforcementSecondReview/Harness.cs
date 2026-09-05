using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

// 测试用 awaitable：只适配 UniTask 的语言接口；完成由测试主线程同步触发，无线程池调度。
namespace Cysharp.Threading.Tasks
{
    public enum UniTaskStatus { Pending, Succeeded, Faulted, Canceled }
    [AsyncMethodBuilder(typeof(FixtureTaskBuilder<>))]
    public struct UniTask<T>
    {
        internal Task<T> Inner;
        public UniTask(Task<T> task) { Inner = task; }
        public UniTaskStatus Status { get { return !Inner.IsCompleted ? UniTaskStatus.Pending : Inner.IsFaulted ? UniTaskStatus.Faulted : UniTaskStatus.Succeeded; } }
        public TaskAwaiter<T> GetAwaiter() { return Inner.GetAwaiter(); }
    }
    public struct FixtureTaskBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> _builder;
        public static FixtureTaskBuilder<T> Create() { return new FixtureTaskBuilder<T> { _builder = AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T>(_builder.Task); } }
        public void SetResult(T value) { _builder.SetResult(value); }
        public void SetException(Exception e) { _builder.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine state) { _builder.SetStateMachine(state); }
        public void Start<TState>(ref TState state) where TState : IAsyncStateMachine { _builder.Start(ref state); }
        public void AwaitOnCompleted<TAwaiter,TState>(ref TAwaiter awaiter, ref TState state) where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine { _builder.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<TAwaiter,TState>(ref TAwaiter awaiter, ref TState state) where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine { _builder.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }
}
namespace UnityEngine
{
    public struct Vector3 { }
    public sealed class Coroutine { public IEnumerator Routine; public bool Stopped, Done; }
}
namespace BossRush
{
    using UnityEngine;
    internal enum Teams { scav, wolf, all }
    internal static class Team { internal static bool IsEnemy(Teams a, Teams b) { return a != b; } }
    internal sealed class Health { }
    internal sealed class Character { public Teams Team; }
    internal sealed class CharacterRandomPreset { }
    internal sealed class ModeHSpawnHandle
    {
        public Character Character; public Health Health; public Teams Team;
        public string StableKey; public bool Activated; public int Recycles;
    }
    internal sealed class ModeHSpawnDiagnostics
    {
        public static bool FailWindow;
        public bool HasWindowSideEffects() { return FailWindow; }
    }
    internal static class ModeHConfig
    {
        public const int MaxSpawnPerFrame = 1, MaxConcurrentEnemyInstances = 3, MaxConcurrentFighterInstances = 1;
    }
    internal sealed class ModeHSupportedMap
    {
        public Vector3 StagingPos, ArenaCenter, PlayerSpawnPos;
        public IList<Vector3> ArenaSpawnPoints = new List<Vector3> { new Vector3(), new Vector3(), new Vector3() };
    }
    internal static class ModeHSpawnBridge
    {
        public static readonly Queue<Func<Task<ModeHSpawnHandle>>> Creates = new Queue<Func<Task<ModeHSpawnHandle>>>();
        public static readonly List<ModeHSpawnHandle> Handles = new List<ModeHSpawnHandle>();
        public static int Requests, MainThread = Environment.CurrentManagedThreadId;
        public static bool FailCommit;
        public static ModeHSpawnHandle Handle(Teams team = Teams.wolf)
        {
            var value = new ModeHSpawnHandle { Character = new Character { Team = team }, Health = new Health(), Team = team };
            Handles.Add(value); return value;
        }
        public static Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle> CreateIsolatedAsync(CharacterRandomPreset p, string key, Teams team, Vector3 position, ModeHSpawnDiagnostics d)
        {
            Requests++;
            return new Cysharp.Threading.Tasks.UniTask<ModeHSpawnHandle>(Creates.Count > 0 ? Creates.Dequeue()() : Task.FromResult(Handle(team)));
        }
        public static bool TryPrepareForArena(ModeHSpawnHandle handle, Vector3 pos, out string reason)
        { reason = FailCommit ? "injected_commit_failure" : null; return !FailCommit; }
        public static bool TryActivate(ModeHSpawnHandle handle, out string reason) { reason = null; handle.Activated = true; return true; }
        public static void Recycle(ModeHSpawnHandle handle)
        {
            if (handle == null) return;
            Program.Check(Environment.CurrentManagedThreadId == MainThread, "recycle stays on driving thread", false);
            handle.Recycles++; handle.Character = null; handle.Health = null; handle.Activated = false;
        }
        public static void Reset() { Creates.Clear(); Handles.Clear(); Requests = 0; FailCommit = false; ModeHSpawnDiagnostics.FailWindow = false; }
    }
    internal sealed class Owner
    {
        public readonly List<Coroutine> Routines = new List<Coroutine>();
        public bool DisposeOnStop = true;
        public int Stops;
        public Coroutine StartCoroutine(IEnumerator e)
        {
            var c = new Coroutine { Routine = e }; Routines.Add(c); Advance(c); return c;
        }
        public void StopCoroutine(Coroutine c) { Stops++; c.Stopped = true; if (DisposeOnStop) ((IDisposable)c.Routine).Dispose(); }
        public void Advance(Coroutine c, bool force = false)
        { if (c.Done || c.Stopped && !force) return; if (!c.Routine.MoveNext()) c.Done = true; }
        public void Drain() { for (int i = 0; i < 20; i++) foreach (var c in Routines.ToArray()) Advance(c); }
    }
    internal enum ModeHLifecycle { MatchFighting, RelayPending, Intermission, Recovering }
    internal sealed class Run { public long OwnerToken = 7; public int MatchIndex = 1; public ModeHLifecycle Lifecycle = ModeHLifecycle.MatchFighting; }
    internal sealed class ModeHMatchPlanDto { public List<string> enemyStableKeys = new List<string>(); public List<int> enemyBatchIndices = new List<int>(); }
    internal sealed class ModeHMatchCorridor { public int SimultaneousCap; }
    internal static class ModeHEncounterPlanner
    { public static int Cap = 3; public static ModeHMatchCorridor GetCorridor(int i) { return new ModeHMatchCorridor { SimultaneousCap = Cap }; } }
    internal static class ModeHPresetRegistry
    { public static string Missing; public static CharacterRandomPreset GetAuditedPreset(string key) { return key == Missing ? null : new CharacterRandomPreset(); } }
    internal sealed class ModeHParticipantRef { public int BatchIndex; public Character Character; }
    internal sealed class ModeHBattleSnapshotContext { }
    internal enum ModeHSnapshotTrigger { Interval, BatchEntered }
    internal sealed class Snapshot { public int SnapshotSequence; public bool TickInterval(float d) { return false; } }
    internal sealed class FireContext { public object ArenaCenter, NearestEnemy, LowestHealthEnemy; public int EnemyCount; }
    internal sealed class Window
    {
        public bool CanRingBell, BellConsumed; public string LockedCommandId; public float CommandWindowRemainingSeconds;
        public void Tick(float d, object a, object b, object c, int count) { } public void Tick(float d, FireContext context) { }
    }
    internal sealed class Telemetry
    {
        public int LiveEnemyCount; public bool HasResult, Timeout; public string PendingDownProfileId; public float RemainingSeconds;
        public bool Tick(float d) { if (Timeout) HasResult = true; return Timeout; }
        public bool TryClaimDefeatByCowardice(string s) { return false; }
        public bool TryClaimVictory(bool alive) { if (!alive || LiveEnemyCount != 0) return false; HasResult = true; return true; }
        public void ConsumePendingDown() { } public void OnEnemyEntered(ModeHParticipantRef enemy) { LiveEnemyCount++; }
    }
    internal sealed partial class ModeHCombatControl
    {
        internal Telemetry _telemetry = new Telemetry();
        private readonly FireContext _fireContext = new FireContext();
        private readonly Window _commandController = new Window(), _injuryAndScar = new Window();
        private readonly Snapshot _snapshot = new Snapshot();
        private int _entryBatchIndex, _lastEntryBatchIndex;
        private bool _enemySpawningPending;
        internal bool Alive = true;
        public Window CommandController { get { return _commandController; } }
        public Snapshot Snapshot { get { return _snapshot; } }
        public bool IsRelayWindowOpen;
        public void Configure(int last) { _lastEntryBatchIndex = last; }
        private void RefreshFireContext(float d, bool f) { }
        private void TickErrorSwap(float d) { } private void EvaluateTriggeredInjuries() { }
        private bool TryEvaluateCowardice(out string c) { c = null; return false; }
        private void TryEvaluateErrorTrigger() { } private bool IsAnyFighterAlive() { return Alive; }
        private void CaptureSnapshot(ModeHSnapshotTrigger t, ModeHBattleSnapshotContext c) { }
        private bool HandleFighterDown(string p, ModeHBattleSnapshotContext c) { return false; }
        public void RestoreAll() { }
    }
    internal sealed class Spectator { public void StopAcceptingBell() { } public void ReclaimInputAfterErrorSwap() { } }
    internal sealed class UI { public void DestroyHud() { } public void TickHud(params object[] args) { } }
    internal static class ModeHEventRouter { public static void ClearMatchRegistry() { } public static void Unbind() { } }
    internal static class ModeHLoadoutKitApplicator { public static void Recycle(object x) { } }
    internal static class ModBehaviour { public static void DevLog(string s) { } }
    internal sealed partial class ModeHRuntimeModule
    {
        private Run _runState = new Run(); private int _sceneGeneration = 1;
        private bool _commandsClosed, _shutdownCompleted, _errorSwapInputYielded;
        private Coroutine _relaySpawnRoutine;
        private readonly Owner _owner = new Owner(); private readonly ModeHSupportedMap _map = new ModeHSupportedMap();
        private ModeHCombatControl _combatControl = new ModeHCombatControl(); private Telemetry _combatTelemetry;
        private readonly ModeHBattleSnapshotContext _battleSnapshotContext = new ModeHBattleSnapshotContext();
        private readonly List<ModeHParticipantRef> _enemyParticipants = new List<ModeHParticipantRef>();
        private readonly List<object> _snapshotEnemies = new List<object>();
        private object _activeKitApplication, _activeFighterHandle, _starterParticipant, _relayParticipant;
        private ModeHSpawnTransaction _spawnTransaction, _relaySpawnTransaction;
        private readonly Spectator _spectatorLease = new Spectator(); private readonly UI _ui = new UI();
        private string _starterDisplayName, _relayDisplayName;
        internal int Retries, Settlements; internal bool ThrowRegister;
        internal ModeHRuntimeModule(int last = 1) { _combatControl.Configure(last); _combatTelemetry = _combatControl._telemetry; }
        private static int ResolveEnemyBatchIndex(ModeHMatchPlanDto plan, int i) { return plan.enemyBatchIndices[i]; }
        private static ModeHParticipantRef BuildParticipant(ModeHSpawnHandle h, string p, bool e, int i, bool r) { return new ModeHParticipantRef { Character = h.Character }; }
        private void RegisterParticipant(ModeHSpawnHandle h, ModeHParticipantRef r) { if (ThrowRegister) throw new InvalidOperationException("register"); }
        private void RefreshBattleSnapshotContext() { } private void AttachAndPersistBattleSnapshot(string s) { }
        private void RequestTechnicalRetry(string s) { Retries++; ReleaseCombatRuntimeObjects(); _runState.Lifecycle = ModeHLifecycle.Recovering; }
        private void LogFailure(string s, Exception e) { Console.WriteLine("HANDLED " + s + ":" + e.GetType().Name); }
        private string ResolveCommandDisplayName(string id) { return id; }
        private void SyncErrorSwapInputYield() { } private void TryBeginErrorSwapIfDue() { }
        private bool TryTransition(ModeHLifecycle a, ModeHLifecycle b, string reason) { _runState.Lifecycle = b; return true; }
        private IEnumerator DriveRelaySpawning() { yield break; }
        private void BeginMatchSettlement() { Settlements++; _runState.Lifecycle = ModeHLifecycle.Intermission; ReleaseCombatRuntimeObjects(); }

        internal void Queue(int count, int batch = 1)
        { for (int i = 0; i < count; i++) _pendingEnemyBatchKeys.Add(new ModeHPendingEnemyEntry { BatchIndex = batch, StableKey = "enemy" + i }); }
        internal void Tick() { TickActiveCombat(.016f); }
        internal void Start() { TryReleaseNextEnemyBatch(); }
        internal void Drain() { _owner.Drain(); }
        internal void Clear() { ReleaseCombatRuntimeObjects(); }
        internal void Live(int value) { _combatTelemetry.LiveEnemyCount = value; }
        internal int Owned { get { return _reinforcementTransactions.Count; } }
        internal int Reserved { get { return _reinforcementReservedEnemyCount; } set { _reinforcementReservedEnemyCount = value; } }
        internal bool Busy { get { return _reinforcementSpawnInFlight || _reinforcementRoutine != null; } }
        internal int LiveCount { get { return _combatTelemetry != null ? _combatTelemetry.LiveEnemyCount : 0; } }
        internal void Relay() { _runState.Lifecycle = ModeHLifecycle.RelayPending; }
        internal void Fighting() { _runState.Lifecycle = ModeHLifecycle.MatchFighting; }
        internal void CrossScene() { _sceneGeneration++; }
        internal void Timeout() { _combatTelemetry.Timeout = true; }
        internal Coroutine OldRoutine() { return _reinforcementRoutine; }
        internal void DoNotDisposeOnStop() { _owner.DisposeOnStop = false; }
        internal void ResumeOld(Coroutine c) { _owner.Advance(c, true); }
        internal void NewMatchSameOwner()
        {
            _runState.Lifecycle = ModeHLifecycle.MatchFighting;
            _combatControl = new ModeHCombatControl(); _combatControl.Configure(1);
            _combatTelemetry = _combatControl._telemetry;
        }
        internal void NewMatchDifferentIndex() { _runState.MatchIndex++; NewMatchSameOwner(); }
    }
    internal static class Program
    {
        private static int _checks;
        internal static void Check(bool value, string name, bool print = true)
        { _checks++; if (!value) throw new Exception("FAIL " + name); if (print) Console.WriteLine("PASS " + name); }
        private static void Reset() { ModeHSpawnBridge.Reset(); ModeHEncounterPlanner.Cap = 3; ModeHPresetRegistry.Missing = null; }
        private static TaskCompletionSource<ModeHSpawnHandle> Pending()
        { var p = new TaskCompletionSource<ModeHSpawnHandle>(); ModeHSpawnBridge.Creates.Enqueue(() => p.Task); return p; }
        private static void AllRecycled(string name)
        { foreach (var h in ModeHSpawnBridge.Handles) Check(h.Recycles == 1 && h.Character == null, name, false); Console.WriteLine("PASS " + name); }
        private static ModeHSpawnTransaction Transaction(out IEnumerator batch, out ModeHSpawnBatchResult result, bool fighter = false, int count = 1)
        {
            var tx = new ModeHSpawnTransaction(); string reason;
            Check(tx.Begin(new ModeHSupportedMap(), 1, 7, out reason), "transaction begin", false);
            result = new ModeHSpawnBatchResult(); var presets = new List<CharacterRandomPreset>(); var keys = new List<string>();
            for (int i = 0; i < count; i++) { presets.Add(new CharacterRandomPreset()); keys.Add("enemy" + i); }
            batch = tx.SpawnBatch(presets, keys, fighter ? Teams.scav : Teams.wolf, fighter, new ModeHSpawnDiagnostics(), result);
            return tx;
        }
        private static void Drain(IEnumerator e) { for (int i = 0; i < 30 && e.MoveNext(); i++) { } }

        public static void Main()
        {
            Reset(); var runtime = new ModeHRuntimeModule(); runtime.Queue(1); runtime.Tick();
            Check(runtime.Settlements == 0 && runtime.Busy && runtime.Owned == 1, "synchronous creation still yields; cannot win before reinforcement registration");
            runtime.Drain(); Check(!runtime.Busy && runtime.Owned == 1 && runtime.LiveCount == 1, "successful reinforcement remains owned after commit");
            runtime.Live(0); runtime.Tick(); Check(runtime.Settlements == 1 && runtime.Owned == 0, "genuine all-batches victory settles and releases committed reinforcement"); AllRecycled("victory cleans reinforcement once");

            Reset(); var delayed = Pending(); runtime = new ModeHRuntimeModule(); runtime.Queue(1); runtime.Tick(); runtime.Tick();
            Check(runtime.Settlements == 0 && runtime.Busy && ModeHSpawnBridge.Requests == 1, "pending async creation blocks victory and duplicate starts");
            delayed.SetResult(ModeHSpawnBridge.Handle()); runtime.Drain(); runtime.Timeout(); runtime.Tick();
            Check(runtime.Settlements == 1, "timeout still settles without changing terminal priority"); AllRecycled("timeout cleans successful reinforcement");

            Reset(); runtime = new ModeHRuntimeModule(); runtime.Live(2); runtime.Queue(2); runtime.Start();
            Check(ModeHSpawnBridge.Requests == 0 && !runtime.Busy, "cap=3 rejects a two-enemy batch with only one vacancy");
            runtime.Live(1); runtime.Reserved = 1; runtime.Start(); Check(ModeHSpawnBridge.Requests == 0, "whole-batch capacity includes in-flight reservations");
            runtime.Reserved = 0; runtime.Start(); Check(runtime.Reserved == 2 && runtime.Busy, "two vacancies reserve the entire batch before first yield");
            runtime.Drain(); Check(runtime.LiveCount == 3, "whole-batch commit reaches but never exceeds cap"); runtime.Clear(); AllRecycled("capacity case cleanup");

            Reset(); runtime = new ModeHRuntimeModule(); runtime.Queue(4); runtime.Tick();
            Check(runtime.Retries == 1 && runtime.Settlements == 0 && ModeHSpawnBridge.Requests == 0, "impossible batch is technical failure; synchronous cleanup does not dereference old controller");
            Reset(); ModeHEncounterPlanner.Cap = 9; runtime = new ModeHRuntimeModule(); runtime.Live(2); runtime.Queue(2); runtime.Start();
            Check(ModeHSpawnBridge.Requests == 0, "global instance cap constrains a larger corridor cap"); runtime.Clear();

            Reset(); runtime = new ModeHRuntimeModule(2); runtime.Queue(1, 1); runtime.Queue(1, 2); runtime.Tick(); runtime.Drain();
            runtime.Live(0); runtime.Tick(); Check(runtime.Settlements == 0, "later planned batch prevents victory after previous batch clears");
            runtime.Drain(); runtime.Live(0); runtime.Tick(); Check(runtime.Settlements == 1, "all sequential batches can reach genuine victory"); AllRecycled("multiple committed batches share match cleanup");

            Reset(); ModeHPresetRegistry.Missing = "enemy0"; runtime = new ModeHRuntimeModule(); runtime.Queue(1); runtime.Tick();
            Check(runtime.Retries == 1 && !runtime.Busy && runtime.Owned == 0 && runtime.Settlements == 0, "missing audited preset fails technically rather than silently dropping planned enemy");
            Reset(); ModeHSpawnBridge.Creates.Enqueue(() => { throw new InvalidOperationException("create"); }); runtime = new ModeHRuntimeModule(); runtime.Queue(1); runtime.Tick();
            Check(runtime.Retries == 1 && !runtime.Busy && runtime.Owned == 0, "synchronous create exception leaves no stale coroutine handle");

            Reset(); ModeHSpawnBridge.FailCommit = true; runtime = new ModeHRuntimeModule(); runtime.Queue(1); runtime.Start(); runtime.Drain();
            Check(runtime.Retries == 1 && runtime.Owned == 0, "failed commit cancels owned transaction and requests technical retry"); AllRecycled("failed commit cleanup");
            Reset(); runtime = new ModeHRuntimeModule { ThrowRegister = true }; runtime.Queue(2); runtime.Start(); runtime.Drain();
            Check(runtime.Retries == 1 && runtime.Owned == 0 && !runtime.Busy, "exception after activation rolls back complete batch and clears partial registration"); AllRecycled("post-commit exception cleanup");

            Reset(); delayed = Pending(); runtime = new ModeHRuntimeModule(); runtime.Queue(1); runtime.Start(); runtime.Clear();
            Check(!runtime.Busy && runtime.Owned == 0, "cleanup stops suspended coroutine and cancels transaction immediately");
            delayed.SetResult(ModeHSpawnBridge.Handle()); AllRecycled("late result reclaimed even when stopped iterator never resumes"); runtime.Clear(); AllRecycled("repeated cleanup remains idempotent");

            Reset(); delayed = Pending(); runtime = new ModeHRuntimeModule(); runtime.DoNotDisposeOnStop(); runtime.Queue(1); runtime.Start(); var old = runtime.OldRoutine();
            runtime.Clear(); runtime.NewMatchSameOwner(); var fresh = Pending(); runtime.Queue(1); runtime.Start();
            delayed.SetResult(ModeHSpawnBridge.Handle()); runtime.ResumeOld(old);
            Check(runtime.Busy && runtime.Reserved == 1 && runtime.Owned == 1 && runtime.Retries == 0, "old finally cannot clear replacement match with same run/scene/match identity");
            fresh.SetResult(ModeHSpawnBridge.Handle()); runtime.Drain(); runtime.Clear(); AllRecycled("same-owner replacement reclaims old and new batches exactly once");

            Reset(); delayed = Pending(); runtime = new ModeHRuntimeModule(); runtime.Queue(1); runtime.Start(); runtime.CrossScene();
            delayed.SetResult(ModeHSpawnBridge.Handle()); runtime.Drain();
            Check(runtime.LiveCount == 0 && runtime.Owned == 0 && runtime.Retries == 0, "changed scene prevents stale commit without retrying new owner"); AllRecycled("scene-stale result cleanup");
            Reset(); delayed = Pending(); runtime = new ModeHRuntimeModule(); runtime.Queue(1); runtime.Start(); runtime.NewMatchDifferentIndex();
            delayed.SetResult(ModeHSpawnBridge.Handle()); runtime.Drain();
            Check(runtime.LiveCount == 0 && runtime.Owned == 0 && runtime.Retries == 0, "different match index rejects stale commit under same scene generation"); AllRecycled("match-stale result cleanup");

            Reset(); delayed = Pending(); runtime = new ModeHRuntimeModule(); runtime.Queue(1); runtime.Start(); runtime.Relay();
            delayed.SetResult(ModeHSpawnBridge.Handle()); runtime.Drain();
            Check(runtime.LiveCount == 1 && runtime.Owned == 1, "valid reinforcement survives relay window within the same match"); runtime.Fighting(); runtime.Live(0); runtime.Tick(); Check(runtime.Settlements == 1, "relay-resumed match still reaches victory");

            Reset(); IEnumerator batch; ModeHSpawnBatchResult result; var tx = Transaction(out batch, out result);
            Check(batch.MoveNext() && tx.EnemyCount == 1 && !result.Success, "transaction owns synchronous handle before post-create yield");
            tx.Cancel(); ((IDisposable)batch).Dispose(); tx.RollbackAll(); AllRecycled("stopped initial transaction and duplicate rollback reclaim once");
            Reset(); delayed = Pending(); tx = Transaction(out batch, out result); Check(batch.MoveNext(), "transaction awaiting", false);
            tx.Cancel(); ((IDisposable)batch).Dispose(); string why; Check(tx.Begin(new ModeHSupportedMap(), 1, 7, out why), "transaction may restart after cancel", false);
            delayed.SetResult(ModeHSpawnBridge.Handle()); Check(tx.EnemyCount == 0 && tx.IsActive, "late result cannot join reused transaction generation"); AllRecycled("reused transaction late result cleanup");

            Reset(); delayed = Pending(); tx = Transaction(out batch, out result); batch.MoveNext(); tx.Cancel();
            tx.Begin(new ModeHSupportedMap(), 1, 7, out why); delayed.SetResult(ModeHSpawnBridge.Handle()); Drain(batch);
            Check(tx.IsActive && tx.EnemyCount == 0, "resuming an old iterator cannot roll back the reused transaction"); AllRecycled("resumed old iterator late result cleanup");
            Reset(); tx = Transaction(out batch, out result, false, 2); batch.MoveNext(); batch.MoveNext(); tx.Cancel(); Drain(batch);
            Check(ModeHSpawnBridge.Requests == 1, "cancel during per-frame budget yield does not start another create"); AllRecycled("per-frame yield cancellation cleanup");
            Reset(); delayed = Pending(); tx = Transaction(out batch, out result); batch.MoveNext(); delayed.SetResult(ModeHSpawnBridge.Handle());
            Check(tx.EnemyCount == 1, "async completion transfers ownership before the caller resumes"); tx.Cancel(); ((IDisposable)batch).Dispose(); AllRecycled("completion-to-resume cancellation gap cleanup");

            Reset(); ModeHSpawnBridge.Creates.Enqueue(() => Task.FromResult(ModeHSpawnBridge.Handle())); delayed = Pending(); tx = Transaction(out batch, out result, false, 2);
            Check(batch.MoveNext() && batch.MoveNext() && batch.MoveNext(), "second create awaits after first owned handle", false);
            delayed.SetException(new InvalidOperationException("async_create")); Drain(batch);
            Check(!result.Success && !tx.IsActive && tx.EnemyCount == 0, "faulted second task rolls back previously created handles"); AllRecycled("async failure partial batch cleanup");
            Reset(); delayed = Pending(); tx = Transaction(out batch, out result); batch.MoveNext(); tx.Cancel(); ((IDisposable)batch).Dispose();
            delayed.SetException(new InvalidOperationException("late_fault")); Check(tx.EnemyCount == 0, "stopped transaction observes late task fault without late ownership");
            Reset(); ModeHSpawnDiagnostics.FailWindow = true; tx = Transaction(out batch, out result); Drain(batch); Check(!result.Success && tx.EnemyCount == 0, "diagnostic side effect fails complete batch"); AllRecycled("diagnostic failure cleanup without double recycle");
            Reset(); tx = Transaction(out batch, out result, true); Drain(batch); Check(result.Success && tx.FighterCount == 1 && tx.TryCommit(new List<Vector3>(), new Vector3(), out why), "initial and relay fighter transaction behavior remains intact"); tx.RollbackAll(); AllRecycled("fighter cleanup");

            var control = new ModeHCombatControl(); control.Configure(0); control.SetEnemySpawningPending(true);
            Check(!control.Tick(.016f, new ModeHBattleSnapshotContext()), "in-flight flag blocks victory even when last batch index already reached");
            control.SetEnemySpawningPending(false); control.Alive = false; Check(!control.Tick(.016f, new ModeHBattleSnapshotContext()), "no living fighter never claims victory");
            control.Alive = true; Check(control.Tick(.016f, new ModeHBattleSnapshotContext()), "single-batch genuine victory remains enabled");
            Console.WriteLine("PASS " + _checks + " assertions; production SpawnTransaction compiled whole, reinforcement region and Tick/cleanup extracted verbatim.");
        }
    }
}
