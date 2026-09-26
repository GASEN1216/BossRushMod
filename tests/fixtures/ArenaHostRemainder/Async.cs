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
