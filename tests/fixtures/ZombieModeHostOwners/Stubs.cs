using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object component in go.Components) component.Destroyed = true;
        }
    }
    public class GameObject : Object { internal readonly List<Object> Components = new List<Object>(); }
    public class Coroutine : Object { internal IEnumerator Routine; }
    public struct Vector3 { }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int buildIndex; }
    public static class SceneManager { public static Scene Current; public static Scene GetActiveScene() { return Current; } }
}
namespace ItemStatsSystem { public class Item { } }
namespace ItemStatsSystem.Data { public class ItemTreeData { } }
namespace BossRush
{
    internal abstract class BossRushRuntimeModuleBase
    {
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnDestroy() { }
    }
    internal static class Trace
    {
        internal static readonly List<string> Calls = new List<string>();
        internal static void Add(string value) { Calls.Add(value); }
    }
    internal static class ZombieModeEntryDebt
    {
        internal static int AttachCount, ResetCount;
        internal static void Attach() { AttachCount++; }
        internal static void ResetStaticCaches() { ResetCount++; }
    }
    // ClearRuntime 的大量玩法字段不在本夹具范围；该替身记录生命周期调用并模拟相关可见结果。
    public sealed class ZombieModeRunState
    {
        public int RunId, SceneBuildIndex = -1, Clears;
        public bool IsCleaningUp, EntryResourcesFinalized;
        public ZombieModeLifecyclePhase LifecyclePhase;
        public readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects = new List<ZombieModeRunOnlyRecord>();
        public void ClearRuntime()
        {
            Trace.Add("clear-runtime"); Clears++; RunOnlyObjects.Clear();
            IsCleaningUp = false; EntryResourcesFinalized = false; SceneBuildIndex = -1;
        }
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal IEnumerator TargetRoutine;
        internal void RollbackZombieModeInventoryTransfer() { Trace.Add("rollback:" + runState.LifecyclePhase); }
        internal void RefundZombieModeInvitationIfNeeded() { Trace.Add("invitation"); }
        internal void RefundZombieModeCashIfNeeded() { Trace.Add("cash"); }
        private void SettleZombieModeFailureInsuranceShell(int runId) { Trace.Add("insurance:" + runId); }
        private void RemoveZombieModeAttributeModifiers() { Trace.Add("attributes"); }
        private void RemoveZombieModeOptionRuntimeEffects() { Trace.Add("effects"); }
        private void ClearZombieModeRewardShell() { Trace.Add("rewards"); }
        private void RestoreZombieModeMapIsolationShell() { Trace.Add("map-restore"); }
        internal IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize(Scene scene, Vector3? pos) { return TargetRoutine; }
        internal bool HasOwner { get { return !ReferenceEquals(owner, null); } }
    }
    public partial class ModBehaviour : UnityEngine.Object
    {
        internal int Starts, Stops;
        internal bool NullCoroutine, ThrowOnStop;
        internal Action OnStart;
        internal Coroutine LastStopped;
        public Coroutine StartCoroutine(IEnumerator routine)
        {
            Starts++; if (OnStart != null) OnStart();
            return NullCoroutine ? null : new Coroutine { Routine = routine };
        }
        public void StopCoroutine(Coroutine coroutine)
        {
            Stops++; LastStopped = coroutine; Trace.Add("stop");
            if (ThrowOnStop) throw new InvalidOperationException("stop injected failure");
        }
        internal static void DevLog(string text) { Trace.Add("dev-log"); }
        internal void CleanupZombieModeFortificationInteractionStateForRuntimeModule() { Trace.Add("fortification"); }
        internal void ClearZombieModeSupportSpawnQueueForRuntimeModule() { Trace.Add("queue:" + zombieModeRunState.RunId); }
        internal void ClearZombieModeEnemyInstanceIdsForRuntimeModule() { Trace.Add("enemy-ids"); }
        internal ZombieModeHostLifecycle Lifecycle { get { return zombieModeHostLifecycle; } }
        internal ZombieModeRunState State { get { return zombieModeRunState; } }
        internal ZombieModeEntryTransaction Transaction { get { return zombieModeEntryTransaction; } }
        internal Dictionary<string, int[]> Cache { get { return zombieModeRewardCandidateCache; } }
        internal List<int> Scratch { get { return zombieModeRewardSafeCandidateScratch; } }
        internal HashSet<int> Opaque { get { return zombieModeOpaqueFilterLogIds; } }
        internal bool Pending { get { return pendingZombieModeEntry; } set { pendingZombieModeEntry = value; } }
        internal int Next { get { return nextZombieModeRunId; } set { nextZombieModeRunId = value; } }
        internal Coroutine StartRun(IEnumerator routine, int runId) { return StartZombieModeCoroutine(routine, runId); }
        internal void SceneCleanup(ZombieModeFailureReason reason) { CleanupZombieModeForSceneChange(reason); }
        internal void DestroyCleanup() { CleanupZombieModeOnDestroy(); }
        internal IEnumerator WaitTarget() { return WaitForZombieModeTargetSceneActiveThenInitialize(new Scene(), null); }
    }
}
