using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(UniTaskMethodBuilder<>))]
    public struct UniTask<T>
    {
        private readonly Task<T> task;
        public UniTask(Task<T> task) { this.task = task; }
        public TaskAwaiter<T> GetAwaiter() { return task.GetAwaiter(); }
        public Task<T> AsTask() { return task; }
    }
    public struct UniTaskMethodBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> builder;
        public static UniTaskMethodBuilder<T> Create() { return new UniTaskMethodBuilder<T> { builder = AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T>(builder.Task); } }
        public void SetResult(T value) { builder.SetResult(value); }
        public void SetException(Exception error) { builder.SetException(error); }
        public void SetStateMachine(IAsyncStateMachine state) { builder.SetStateMachine(state); }
        public void Start<TState>(ref TState state) where TState : IAsyncStateMachine { builder.Start(ref state); }
        public void AwaitOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine { builder.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }
    [AsyncMethodBuilder(typeof(UniTaskVoidMethodBuilder))]
    public struct UniTaskVoid
    {
        private readonly Task task;
        public UniTaskVoid(Task task) { this.task = task; }
        public void Forget() { if (task.IsCompleted) task.GetAwaiter().GetResult(); }
    }
    public struct UniTaskVoidMethodBuilder
    {
        private AsyncTaskMethodBuilder builder;
        public static UniTaskVoidMethodBuilder Create() { return new UniTaskVoidMethodBuilder { builder = AsyncTaskMethodBuilder.Create() }; }
        public UniTaskVoid Task { get { return new UniTaskVoid(builder.Task); } }
        public void SetResult() { builder.SetResult(); }
        public void SetException(Exception error) { builder.SetException(error); }
        public void SetStateMachine(IAsyncStateMachine state) { builder.SetStateMachine(state); }
        public void Start<TState>(ref TState state) where TState : IAsyncStateMachine { builder.Start(ref state); }
        public void AwaitOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine { builder.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }
    public static class UniTask
    {
        public static Task Yield() { BossRush.Probe.Events.Add("yield"); return BossRush.Probe.YieldGate == null ? Task.CompletedTask : BossRush.Probe.YieldGate.Task; }
    }
    public class UniTaskCompletionSource<T>
    {
        private readonly TaskCompletionSource<T> source = new TaskCompletionSource<T>();
        public UniTask<T> Task { get { return new UniTask<T>(source.Task); } }
        public bool TrySetResult(T value) { return source.TrySetResult(value); }
    }
}
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public string name;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            BossRush.Probe.Events.Add("destroy:" + go.name);
            go.Destroyed = true;
            foreach (var component in go.Components) component.Destroyed = true;
        }
    }
    public class GameObject : Object
    {
        public bool Active;
        public readonly List<Object> Components = new List<Object>();
        public void SetActive(bool active) { Active = active; BossRush.Probe.Events.Add("active:" + active); }
    }
    public struct Vector3 { public static Vector3 forward; }
    public static class Time { public static int frameCount; public static float realtimeSinceStartup; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int buildIndex; }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { buildIndex = 17 }; } }
}
namespace BossRush
{
    using UnityEngine;
    using Cysharp.Threading.Tasks;
    internal static class Probe
    {
        internal static readonly List<string> Events = new List<string>();
        internal static TaskCompletionSource<bool> YieldGate;
    }
    public class EnemyPresetInfo { public string name, displayName; public float baseHealth = 120; }
    public class CharacterMainControl : UnityEngine.Object
    {
        public sealed class HealthState
        {
            public bool Invincible;
            public void SetInvincible(bool value) { Invincible = value; Probe.Events.Add("invincible"); }
        }
        public sealed class CharacterItemState { public object Inventory = new object(); }
        public readonly GameObject gameObject;
        public readonly HealthState Health = new HealthState();
        public readonly CharacterItemState CharacterItem = new CharacterItemState();
        public CharacterMainControl(string name)
        {
            this.name = name; gameObject = new GameObject { name = name }; gameObject.Components.Add(this);
        }
    }
    public class CharacterRandomPreset : UnityEngine.Object
    {
        public Func<CharacterMainControl> Create;
        public Task<CharacterMainControl> PendingCreation;
        public int Calls;
        public UniTask<CharacterMainControl> CreateCharacterAsync(Vector3 p, Vector3 direction, int scene, object data, bool active)
        {
            if (scene != 17 || active) throw new Exception("creation contract changed");
            Calls++; Probe.Events.Add("create:" + name);
            return new UniTask<CharacterMainControl>(PendingCreation ?? Task.FromResult(Create()));
        }
    }
    internal class ManagedBossRuntimeHandle { }
    internal class ManagedBossPrepareResult { public CharacterMainControl Character; public ManagedBossRuntimeHandle Handle; }
    internal sealed class ModeDItemPool
    { internal sealed class SharedModeEnemyEquipmentMaterializationPlan { } }
    internal class ModBehaviour : UnityEngine.Object
    {
        internal ModeFRuntimeModule F;
        internal bool IsModeFActive { get { return F != null && F.modeFActive; } }
        internal bool IsModeFSessionStillValid(int token, int scene) { return F != null && F.IsModeFSessionStillValid(token, scene); }
        internal static bool DevModeEnabled { get { return false; } }
        internal static bool ModeEFSpawnProfilingEnabled { get { return false; } }
        internal static void DevLog(string text) { }
    }
    internal static class SpawnedEnemyActivationHelper
    { internal static void ReleaseFromPlayerDistanceSleep(CharacterMainControl c) { Probe.Events.Add("wake"); } }
    internal static class MutatorManager
    { internal static void ApplyToEnemy(CharacterMainControl c) { Probe.Events.Add("mutator"); } }
}
