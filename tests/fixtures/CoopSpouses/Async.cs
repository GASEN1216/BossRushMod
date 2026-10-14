using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(Builder<>))]
    public struct UniTask<T>
    { readonly Task<T> task; public UniTask(Task<T> value) { task=value; } public TaskAwaiter<T> GetAwaiter() { return task.GetAwaiter(); } }
    public struct Builder<T>
    {
        AsyncTaskMethodBuilder<T> b;
        public static Builder<T> Create() { return new Builder<T> { b=AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T>(b.Task); } }
        public void SetResult(T v) { b.SetResult(v); } public void SetException(Exception e) { b.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine s) { b.SetStateMachine(s); }
        public void Start<S>(ref S s) where S:IAsyncStateMachine { b.Start(ref s); }
        public void AwaitOnCompleted<A,S>(ref A a,ref S s) where A:INotifyCompletion where S:IAsyncStateMachine { b.AwaitOnCompleted(ref a,ref s); }
        public void AwaitUnsafeOnCompleted<A,S>(ref A a,ref S s) where A:ICriticalNotifyCompletion where S:IAsyncStateMachine { b.AwaitUnsafeOnCompleted(ref a,ref s); }
    }
    [AsyncMethodBuilder(typeof(Builder))]
    public struct UniTask
    { readonly Task task; public UniTask(Task value) { task=value; } public TaskAwaiter GetAwaiter() { return task.GetAwaiter(); } }
    [AsyncMethodBuilder(typeof(VoidBuilder))]
    public struct UniTaskVoid
    { readonly Task task; public UniTaskVoid(Task value) { task=value; } public void Forget() { if(task.IsCompleted) task.GetAwaiter().GetResult(); else BossRush.Probe.Tasks.Add(task); } }
    public struct Builder
    {
        AsyncTaskMethodBuilder b;
        public static Builder Create() { return new Builder { b=AsyncTaskMethodBuilder.Create() }; }
        public UniTask Task { get { return new UniTask(b.Task); } }
        public void SetResult() { b.SetResult(); } public void SetException(Exception e) { b.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine s) { b.SetStateMachine(s); } public void Start<S>(ref S s) where S:IAsyncStateMachine { b.Start(ref s); }
        public void AwaitOnCompleted<A,S>(ref A a,ref S s) where A:INotifyCompletion where S:IAsyncStateMachine { b.AwaitOnCompleted(ref a,ref s); }
        public void AwaitUnsafeOnCompleted<A,S>(ref A a,ref S s) where A:ICriticalNotifyCompletion where S:IAsyncStateMachine { b.AwaitUnsafeOnCompleted(ref a,ref s); }
    }
    public struct VoidBuilder
    {
        AsyncTaskMethodBuilder b;
        public static VoidBuilder Create() { return new VoidBuilder { b=AsyncTaskMethodBuilder.Create() }; }
        public UniTaskVoid Task { get { return new UniTaskVoid(b.Task); } }
        public void SetResult() { b.SetResult(); } public void SetException(Exception e) { b.SetException(e); }
        public void SetStateMachine(IAsyncStateMachine s) { b.SetStateMachine(s); } public void Start<S>(ref S s) where S:IAsyncStateMachine { b.Start(ref s); }
        public void AwaitOnCompleted<A,S>(ref A a,ref S s) where A:INotifyCompletion where S:IAsyncStateMachine { b.AwaitOnCompleted(ref a,ref s); }
        public void AwaitUnsafeOnCompleted<A,S>(ref A a,ref S s) where A:ICriticalNotifyCompletion where S:IAsyncStateMachine { b.AwaitUnsafeOnCompleted(ref a,ref s); }
    }
}
