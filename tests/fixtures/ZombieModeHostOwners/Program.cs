using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception("FAIL " + label); }
    private static IEnumerator Routine() { yield return null; }
    private static object Field(object owner, string name) { return owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    private static ZombieModeRuntimeModule Attach(ModBehaviour host)
    {
        var module = new ZombieModeRuntimeModule();
        Check(module.RunState == null && module.EntryTransaction == null && !module.HasOwner, "module fields null before production Awake");
        module.OnAwake(host); return module;
    }
    private static void Identity()
    {
        var host = new ModBehaviour(); var otherHost = new ModBehaviour();
        object[] original = { host.State, host.Transaction, host.Cache, host.Scratch, host.Opaque };
        string[] names = { "zombieModeUnattachedRunState", "zombieModeUnattachedEntryTransaction", "zombieModeUnattachedRewardCandidateCache", "zombieModeUnattachedRewardCandidateScratch", "zombieModeUnattachedOpaqueFilterLogIds" };
        object[] another = { otherHost.State, otherHost.Transaction, otherHost.Cache, otherHost.Scratch, otherHost.Opaque };
        for (int i = 0; i < names.Length; i++)
        {
            Check(original[i] != null && ReferenceEquals(original[i], Field(host.Lifecycle, names[i])), "eager fallback " + names[i]);
            Check(!ReferenceEquals(original[i], another[i]), "per-host fallback " + names[i]);
        }
        host.Pending = true; host.Cache["probe"] = new[] { 7 }; host.Scratch.Add(9); host.Opaque.Add(11);
        host.AttachZombieModeRuntimeModule(null);
        Check(ReferenceEquals(host.State, original[0]) && host.Pending, "null attach preserves fallback");
        var module = Attach(host);
        object[] adopted = { module.RunState, module.EntryTransaction, module.RewardCandidateCache, module.RewardCandidateScratch, module.OpaqueFilterLogIds };
        for (int i = 0; i < names.Length; i++)
        {
            Check(ReferenceEquals(original[i], adopted[i]), "adopt exact object " + names[i]);
            Check(Field(host.Lifecycle, names[i]) == null, "clear transferred fallback " + names[i]);
        }
        Check(module.PendingEntry && host.Cache["probe"][0] == 7 && host.Scratch[0] == 9 && host.Opaque.Contains(11), "pre-Awake values survive transfer");
        host.Pending = false; Check(!module.PendingEntry, "attached pending setter targets module");
        module.PendingEntry = true; Check(host.Pending, "attached pending getter targets module");
        host.DetachZombieModeRuntimeModule(new ZombieModeRuntimeModule());
        Check(ReferenceEquals(host.Lifecycle.Runtime, module) && Field(host.Lifecycle, names[0]) == null, "wrong owner detach ignored");
        host.Next = 41; Check(otherHost.Next == 41, "next run id remains global runtime counter");
        module.OnDestroy();
        Check(!module.HasOwner && host.Lifecycle.Runtime == null && host.Pending, "destroy detaches after clearing runtime owner");
        object[] returned = { host.State, host.Transaction, host.Cache, host.Scratch, host.Opaque };
        for (int i = 0; i < names.Length; i++) Check(ReferenceEquals(original[i], returned[i]), "detach returns exact object " + names[i]);
        host.DetachZombieModeRuntimeModule(module); Check(ReferenceEquals(host.State, original[0]), "repeated detach preserves fallback");
        var successor = Attach(host); Check(ReferenceEquals(successor.RunState, original[0]), "reattach adopts same fallback");
    }
    private static void Cleanup()
    {
        foreach (bool attached in new[] { false, true })
        foreach (bool destroy in new[] { false, true })
        foreach (bool finalized in new[] { false, true })
        {
            var host = new ModBehaviour(); if (attached) Attach(host);
            var state = host.State; var transaction = host.Transaction;
            state.RunId = 23; state.LifecyclePhase = ZombieModeLifecyclePhase.Active; state.EntryResourcesFinalized = finalized;
            host.Pending = true; transaction.CashTemporarilyHeld = true; transaction.CashWithheldAmount = 100; transaction.BlockingMessages.Add("probe");
            var go = new GameObject(); var component = new UnityEngine.Object(); go.Components.Add(component);
            state.RunOnlyObjects.Add(new ZombieModeRunOnlyRecord { GameObject = go, CleanupAction = () => Trace.Add("record-first") });
            state.RunOnlyObjects.Add(new ZombieModeRunOnlyRecord { CleanupAction = () => Trace.Add("record-last") });
            Trace.Calls.Clear();
            if (destroy) host.DestroyCleanup(); else host.SceneCleanup(ZombieModeFailureReason.SceneSwitched);
            var expected = new System.Collections.Generic.List<string>();
            if (attached)
            {
                if (!finalized) expected.AddRange(new[] { "rollback:" + (destroy ? "Active" : "Exiting"), "invitation", "cash" });
                expected.AddRange(new[] { "insurance:23", "attributes", "effects", "fortification", "queue:-23", "owned-drops", "record-last", "record-first", "enemy-ids", "rewards", "map-restore" });
            }
            expected.Add("clear-runtime");
            Check(Trace.Calls.SequenceEqual(expected), "cleanup order attached=" + attached + " destroy=" + destroy + " finalized=" + finalized + " actual=" + string.Join(",", Trace.Calls));
            Check(ReferenceEquals(state, host.State) && ReferenceEquals(transaction, host.Transaction), "cleanup preserves objects");
            Check(state.LifecyclePhase == ZombieModeLifecyclePhase.None && state.Clears == 1 && !host.Pending && state.RunOnlyObjects.Count == 0, "cleanup resets lifecycle");
            Check(!transaction.CashTemporarilyHeld && transaction.CashWithheldAmount == 0 && transaction.BlockingMessages.Count == 0, "real transaction Reset executed");
            Check(attached ? go == null && component == null : go != null && component != null, "unattached retains original no-op dependent cleanup");
        }
        foreach (ZombieModeLifecyclePhase phase in Enum.GetValues(typeof(ZombieModeLifecyclePhase)))
        foreach (bool pending in new[] { false, true })
        {
            var host = new ModBehaviour(); host.State.LifecyclePhase = phase; host.Pending = pending;
            bool expected = phase != ZombieModeLifecyclePhase.None || pending;
            host.SceneCleanup(ZombieModeFailureReason.SceneSwitched);
            Check(host.State.Clears == (expected ? 1 : 0), "scene early gate " + phase + " pending=" + pending);
        }
        var orphan = new ModBehaviour(); orphan.State.RunOnlyObjects.Add(new ZombieModeRunOnlyRecord());
        orphan.SceneCleanup(ZombieModeFailureReason.SceneSwitched); Check(orphan.State.Clears == 1, "orphan run-only record triggers cleanup");
    }
    private static void Coroutines()
    {
        var host = new ModBehaviour(); Check(host.StartRun(Routine(), 5) == null && host.Starts == 0, "unattached start remains null");
        var module = Attach(host); var state = host.State; state.RunId = 5; state.LifecyclePhase = ZombieModeLifecyclePhase.Active; state.SceneBuildIndex = 3;
        SceneManager.Current = new Scene { buildIndex = 3 };
        Check(host.StartRun(null, 5) == null && host.StartRun(Routine(), 4) == null && host.Starts == 0, "null routine and stale run gated");
        state.IsCleaningUp = true; Check(host.StartRun(Routine(), 5) == null, "cleanup blocks start"); state.IsCleaningUp = false;
        SceneManager.Current = new Scene { buildIndex = 7 }; Check(host.StartRun(Routine(), 5) == null, "wrong scene blocks start"); SceneManager.Current = new Scene { buildIndex = 3 };
        host.NullCoroutine = true; Check(host.StartRun(Routine(), 5) == null && state.RunOnlyObjects.Count == 0, "null Unity handle not registered"); host.NullCoroutine = false;
        host.OnStart = () => Check(state.RunOnlyObjects.Count == 1, "registry owns the routine before Unity's synchronous first step");
        var coroutine = host.StartRun(Routine(), 5);
        Check(coroutine != null && state.RunOnlyObjects.Count == 1 && state.RunOnlyObjects[0].Kind == ZombieModeRunOnlyObjectKind.Coroutine, "successful start registered once");
        var record = state.RunOnlyObjects[0]; module.OnDestroy(); UnityEngine.Object.Destroy(host);
        Check(host == null && !module.HasOwner, "destroyed Unity host and detached module boundary");
        record.Cleanup(true); Check(host.Stops == 1 && ReferenceEquals(host.LastStopped, coroutine), "late cleanup stops original host after owner cleared");
        record.Cleanup(true); Check(host.Stops == 1, "real run-only record cleanup is idempotent");
        var faultHost = new ModBehaviour(); Attach(faultHost); faultHost.State.RunId = 8; faultHost.State.LifecyclePhase = ZombieModeLifecyclePhase.Active;
        faultHost.StartRun(Routine(), 8); faultHost.ThrowOnStop = true;
        faultHost.State.RunOnlyObjects[0].Cleanup(true); Check(faultHost.Stops == 1, "StopCoroutine failure contained");
        CoroutineCompletion();
    }

    private sealed class DisposableProbe : IEnumerator, IDisposable
    {
        internal int Steps, Disposals;
        internal bool Throws;
        public object Current { get { return null; } }
        public bool MoveNext() { Steps++; if (Throws) throw new InvalidOperationException("nested iterator failure"); return Steps < 2; }
        public void Reset() { throw new NotSupportedException(); }
        public void Dispose() { Disposals++; }
    }
    private static IEnumerator Empty() { yield break; }
    private static IEnumerator ReentrantExit(ModBehaviour host) { host.SceneCleanup(ZombieModeFailureReason.SceneSwitched); yield return null; }
    private static void CoroutineCompletion()
    {
        var host = new ModBehaviour(); Attach(host);
        host.State.RunId = 81; host.State.LifecyclePhase = ZombieModeLifecyclePhase.Active;
        for (int i = 0; i < 512; i++)
        {
            var coroutine = host.StartRun(Routine(), 81);
            Check(coroutine != null && host.State.RunOnlyObjects.Count == 1, "one active coroutine has one cleanup record");
            Check(!coroutine.Routine.MoveNext() && host.State.RunOnlyObjects.Count == 0, "completion releases the run record " + i);
        }
        Check(host.StartRun(Empty(), 81) == null && host.State.RunOnlyObjects.Count == 0, "synchronous completion leaves no record");
        object token = new object();
        var child = new DisposableProbe();
        var nested = host.StartRun(Tokens(token, child), 81);
        Check(ReferenceEquals(nested.Routine.Current, token), "Unity yield token is preserved");
        Check(nested.Routine.MoveNext() && child.Steps == 1, "nested iterator is actually advanced");
        Check(nested.Routine.MoveNext() && child.Disposals == 1, "completed child is disposed once before the parent continues");
        Check(!nested.Routine.MoveNext() && host.State.RunOnlyObjects.Count == 0, "nested completion removes only the root record");

        child = new DisposableProbe();
        nested = host.StartRun(Tokens(token, child), 81);
        nested.Routine.MoveNext();
        child.Throws = true;
        bool threw = false;
        try { nested.Routine.MoveNext(); } catch (InvalidOperationException) { threw = true; }
        Check(threw && child.Disposals == 1 && host.State.RunOnlyObjects.Count == 0, "nested failure unwinds the root and its registry record");

        child = new DisposableProbe();
        var cancelled = host.StartRun(child, 81);
        host.State.RunOnlyObjects[0].Cleanup(false);
        Check(child.Disposals == 1 && !cancelled.Routine.MoveNext() && host.State.RunOnlyObjects.Count == 0,
            "explicit cleanup disposes even when native StopCoroutine does not dispose iterators");
        host.ThrowOnStart = true;
        child = new DisposableProbe();
        Check(host.StartRun(child, 81) == null && child.Disposals == 1 && host.State.RunOnlyObjects.Count == 0,
            "start failure releases the unstarted iterator and record");
        host.ThrowOnStart = false;

        var first = host.StartRun(Routine(), 81);
        var second = host.StartRun(Routine(), 81);
        host.SceneCleanup(ZombieModeFailureReason.SceneSwitched);
        Check(host.State.RunOnlyObjects.Count == 0 && !first.Routine.MoveNext() && !second.Routine.MoveNext(),
            "bulk cleanup can traverse every record while cancellation disposes routines");
        host.State.RunId = 82; host.State.LifecyclePhase = ZombieModeLifecyclePhase.Active;
        Check(host.StartRun(ReentrantExit(host), 82) == null && host.State.RunOnlyObjects.Count == 0,
            "a first-step reentrant scene exit cannot publish an orphan coroutine");
    }
    private static IEnumerator Tokens(object first, IEnumerator second) { yield return first; yield return second; yield return null; }
    private static void BossVisualCache()
    {
        var cache = (Texture2D[])typeof(ZombieModeBossVisuals).GetField("SigilTextures", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        for (int i = 0; i < cache.Length; i++) cache[i] = new Texture2D();
        var allocated = (Texture2D[])cache.Clone();
        UnityEngine.Object.Destroy(allocated[2]);
        int before = UnityEngine.Object.DestroyCalls;
        var module = Attach(new ModBehaviour());
        module.OnDestroy();
        Check(UnityEngine.Object.DestroyCalls == before + 4, "module destroy releases each live sigil texture and skips fake-null texture");
        for (int i = 0; i < cache.Length; i++)
        {
            Check(allocated[i] == null, "sigil texture native object released " + i);
            Check(ReferenceEquals(cache[i], null), "sigil managed cache reference cleared " + i);
        }
        before = UnityEngine.Object.DestroyCalls;
        module.OnDestroy();
        Check(UnityEngine.Object.DestroyCalls == before, "repeated module destroy does not release textures twice");
        cache[0] = new Texture2D();
        new ZombieModeRuntimeModule().OnDestroy();
        Check(UnityEngine.Object.DestroyCalls == before + 1 && ReferenceEquals(cache[0], null), "missing host still clears module static sigil cache");
    }
    private static void Iterators()
    {
        var host = new ModBehaviour(); Check(!host.WaitTarget().MoveNext(), "unattached iterator ends");
        var module = Attach(host); var first = new object(); var child = Routine(); module.TargetRoutine = Tokens(first, child);
        IEnumerator bridge = host.WaitTarget();
        Check(bridge.MoveNext() && ReferenceEquals(bridge.Current, first), "iterator preserves Current token");
        Check(bridge.MoveNext() && ReferenceEquals(bridge.Current, child), "iterator preserves nested IEnumerator for Unity");
        Check(bridge.MoveNext() && bridge.Current == null && !bridge.MoveNext(), "iterator preserves frame boundary and exhaustion");
    }
    public static void Main()
    {
        Identity(); Cleanup(); Coroutines(); Iterators(); BossVisualCache();
        Console.WriteLine("ZombieModeHostOwners: " + checks + " PASS / 0 FAIL");
    }
}
