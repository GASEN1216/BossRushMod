using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

internal static class AsyncProbe
{
    internal static readonly Queue<TaskCompletionSource<bool>> Frames = new Queue<TaskCompletionSource<bool>>();
    internal static readonly List<Task> Forgotten = new List<Task>();

    internal static Task NextFrame()
    {
        var completion = new TaskCompletionSource<bool>();
        Frames.Enqueue(completion);
        return completion.Task;
    }

    internal static void Frame()
    {
        if (Frames.Count == 0) throw new InvalidOperationException("no pending production frame yield");
        Frames.Dequeue().SetResult(true);
    }

    internal static void Reset()
    {
        Frames.Clear();
        Forgotten.Clear();
    }
}

namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(FixtureTaskBuilder))]
    public struct UniTask
    {
        internal Task Inner;
        public TaskAwaiter GetAwaiter() { return Inner.GetAwaiter(); }
        public void Forget() { AsyncProbe.Forgotten.Add(Inner); }
        public static Task Yield() { return AsyncProbe.NextFrame(); }
    }

    [AsyncMethodBuilder(typeof(FixtureTaskBuilder<>))]
    public struct UniTask<T>
    {
        internal Task<T> Inner;
        public TaskAwaiter<T> GetAwaiter() { return Inner.GetAwaiter(); }
        public void Forget() { AsyncProbe.Forgotten.Add(Inner); }
    }

    [AsyncMethodBuilder(typeof(FixtureVoidBuilder))]
    public struct UniTaskVoid
    {
        internal Task Inner;
        public void Forget() { AsyncProbe.Forgotten.Add(Inner); }
    }

    public struct FixtureTaskBuilder
    {
        private AsyncTaskMethodBuilder inner;
        public static FixtureTaskBuilder Create() { return new FixtureTaskBuilder { inner = AsyncTaskMethodBuilder.Create() }; }
        public UniTask Task { get { return new UniTask { Inner = inner.Task }; } }
        public void SetResult() { inner.SetResult(); }
        public void SetException(Exception value) { inner.SetException(value); }
        public void SetStateMachine(IAsyncStateMachine value) { inner.SetStateMachine(value); }
        public void Start<T>(ref T value) where T : IAsyncStateMachine { inner.Start(ref value); }
        public void AwaitOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine { inner.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine { inner.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }

    public struct FixtureTaskBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> inner;
        public static FixtureTaskBuilder<T> Create() { return new FixtureTaskBuilder<T> { inner = AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T> { Inner = inner.Task }; } }
        public void SetResult(T value) { inner.SetResult(value); }
        public void SetException(Exception value) { inner.SetException(value); }
        public void SetStateMachine(IAsyncStateMachine value) { inner.SetStateMachine(value); }
        public void Start<TState>(ref TState value) where TState : IAsyncStateMachine { inner.Start(ref value); }
        public void AwaitOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine { inner.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine { inner.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }

    public struct FixtureVoidBuilder
    {
        private AsyncTaskMethodBuilder inner;
        public static FixtureVoidBuilder Create() { return new FixtureVoidBuilder { inner = AsyncTaskMethodBuilder.Create() }; }
        public UniTaskVoid Task { get { return new UniTaskVoid { Inner = inner.Task }; } }
        public void SetResult() { inner.SetResult(); }
        public void SetException(Exception value) { inner.SetException(value); }
        public void SetStateMachine(IAsyncStateMachine value) { inner.SetStateMachine(value); }
        public void Start<T>(ref T value) where T : IAsyncStateMachine { inner.Start(ref value); }
        public void AwaitOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine { inner.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine { inner.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }
}
