using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

// Deterministic substitute for the Unity player loop. Production awaits and token
// propagation are compiled unchanged; tests explicitly pump predicates/callbacks.
namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(UniTaskBuilder))]
    public struct UniTask
    {
        internal Task task;
        public TaskAwaiter GetAwaiter() { return task.GetAwaiter(); }
        private sealed class Waiter
        {
            internal Func<bool> predicate;
            internal CancellationToken token;
            internal TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
        }
        private static readonly List<Waiter> waiters = new List<Waiter>();
        private static readonly List<TaskCompletionSource<bool>> deferredFrames = new List<TaskCompletionSource<bool>>();
        public static bool HoldNextFrame;
        public static UniTask WaitUntil(Func<bool> predicate, CancellationToken cancellationToken = default(CancellationToken))
        {
            var waiter = new Waiter { predicate = predicate, token = cancellationToken };
            waiters.Add(waiter);
            return new UniTask { task = waiter.completion.Task };
        }
        /// <summary>下一帧：替身里立刻完成，并把 UnityEngine.Time.realtimeSinceStartup 推进一帧（1/60 秒），有时限的轮询才停得下来。</summary>
        public static UniTask NextFrame()
        {
            UnityEngine.Time.realtimeSinceStartup += 1f / 60f;
            if (HoldNextFrame)
            {
                var frame = new TaskCompletionSource<bool>();
                deferredFrames.Add(frame);
                return new UniTask { task = frame.Task };
            }
            return new UniTask { task = Task.CompletedTask };
        }
        public static void ReleaseDeferredFrames()
        {
            var frames = deferredFrames.ToArray();
            deferredFrames.Clear();
            foreach (var frame in frames) frame.SetResult(true);
        }
        public static void Pump()
        {
            foreach (var waiter in waiters.ToArray())
            {
                if (!waiter.token.IsCancellationRequested && !waiter.predicate()) continue;
                waiters.Remove(waiter);
                if (waiter.token.IsCancellationRequested) waiter.completion.SetCanceled();
                else waiter.completion.SetResult(true);
            }
        }
        public static int Pending { get { return waiters.Count; } }
    }
    [AsyncMethodBuilder(typeof(UniTaskBuilder<>))]
    public struct UniTask<T>
    {
        internal Task<T> task;
        public TaskAwaiter<T> GetAwaiter() { return task.GetAwaiter(); }
    }
    [AsyncMethodBuilder(typeof(UniTaskVoidBuilder))]
    public struct UniTaskVoid
    {
        internal Task task;
        public void Forget() { Task pending = task; if (pending.IsCompleted) pending.GetAwaiter().GetResult(); }
    }
    public struct UniTaskBuilder
    {
        private AsyncTaskMethodBuilder builder;
        public static UniTaskBuilder Create() { return new UniTaskBuilder { builder = AsyncTaskMethodBuilder.Create() }; }
        public UniTask Task { get { return new UniTask { task = builder.Task }; } }
        public void SetResult() { builder.SetResult(); }
        public void SetException(Exception e) { builder.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine s) { builder.SetStateMachine(s); }
        public void Start<T>(ref T s) where T : IAsyncStateMachine { builder.Start(ref s); }
        public void AwaitOnCompleted<T, U>(ref T a, ref U s) where T : INotifyCompletion where U : IAsyncStateMachine { builder.AwaitOnCompleted(ref a, ref s); }
        public void AwaitUnsafeOnCompleted<T, U>(ref T a, ref U s) where T : ICriticalNotifyCompletion where U : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref a, ref s); }
    }
    public struct UniTaskBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> builder;
        public static UniTaskBuilder<T> Create() { return new UniTaskBuilder<T> { builder = AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T> { task = builder.Task }; } }
        public void SetResult(T value) { builder.SetResult(value); }
        public void SetException(Exception e) { builder.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine s) { builder.SetStateMachine(s); }
        public void Start<U>(ref U s) where U : IAsyncStateMachine { builder.Start(ref s); }
        public void AwaitOnCompleted<U, V>(ref U a, ref V s) where U : INotifyCompletion where V : IAsyncStateMachine { builder.AwaitOnCompleted(ref a, ref s); }
        public void AwaitUnsafeOnCompleted<U, V>(ref U a, ref V s) where U : ICriticalNotifyCompletion where V : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref a, ref s); }
    }
    public struct UniTaskVoidBuilder
    {
        private AsyncTaskMethodBuilder builder;
        public static UniTaskVoidBuilder Create() { return new UniTaskVoidBuilder { builder = AsyncTaskMethodBuilder.Create() }; }
        public UniTaskVoid Task { get { return new UniTaskVoid { task = builder.Task }; } }
        public void SetResult() { builder.SetResult(); }
        public void SetException(Exception e) { builder.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine s) { builder.SetStateMachine(s); }
        public void Start<T>(ref T s) where T : IAsyncStateMachine { builder.Start(ref s); }
        public void AwaitOnCompleted<T, U>(ref T a, ref U s) where T : INotifyCompletion where U : IAsyncStateMachine { builder.AwaitOnCompleted(ref a, ref s); }
        public void AwaitUnsafeOnCompleted<T, U>(ref T a, ref U s) where T : ICriticalNotifyCompletion where U : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref a, ref s); }
    }
}
