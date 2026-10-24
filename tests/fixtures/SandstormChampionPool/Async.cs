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
        public UniTask(Task<T> value) { task = value; }
        public TaskAwaiter<T> GetAwaiter() { return task.GetAwaiter(); }
        public Task<T> AsTask() { return task; }
    }

    public struct UniTaskMethodBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> builder;
        public static UniTaskMethodBuilder<T> Create() { return new UniTaskMethodBuilder<T> { builder = AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T>(builder.Task); } }
        public void SetResult(T value) { builder.SetResult(value); }
        public void SetException(Exception value) { builder.SetException(value); }
        public void SetStateMachine(IAsyncStateMachine state) { builder.SetStateMachine(state); }
        public void Start<TState>(ref TState state) where TState : IAsyncStateMachine { builder.Start(ref state); }
        public void AwaitOnCompleted<TAwaiter, TState>(ref TAwaiter value, ref TState state)
            where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine { builder.AwaitOnCompleted(ref value, ref state); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TState>(ref TAwaiter value, ref TState state)
            where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref value, ref state); }
    }

    public static class UniTask
    {
        internal static readonly Queue<TaskCompletionSource<bool>> Frames = new Queue<TaskCompletionSource<bool>>();
        public static Task Yield()
        {
            BossRush.Probe.Events.Add("yield");
            var frame = new TaskCompletionSource<bool>(); Frames.Enqueue(frame); return frame.Task;
        }
        internal static void Frame() { Frames.Dequeue().SetResult(true); }
    }
}
