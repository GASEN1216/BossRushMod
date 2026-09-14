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
        public static UniTask WaitUntil(Func<bool> predicate, CancellationToken cancellationToken = default(CancellationToken))
        {
            var waiter = new Waiter { predicate = predicate, token = cancellationToken };
            waiters.Add(waiter);
            return new UniTask { task = waiter.completion.Task };
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
namespace UnityEngine
{
    public class Object { public static void DontDestroyOnLoad(Object value) { } public static void Destroy(Object value) { } }
    public class GameObject : Object { public GameObject(string name) { } }
    public class Transform { public GameObject gameObject = new GameObject("resident"); }
    public struct Vector3 { public Vector3(float x, float y, float z) { } }
}
namespace Duckov.UI.Animations
{
    public class FadeGroup { public bool Visible; public void Show() { Visible = true; } public void Hide() { Visible = false; } }
}
namespace NodeCanvas.DialogueTrees
{
    public interface IDialogueActor { }
    public interface IStatement { }
    public class SubtitlesRequestInfo
    {
        public Action done;
        public SubtitlesRequestInfo(IDialogueActor a, IStatement s, Action done) { this.done = done; }
    }
    public class MultipleChoiceRequestInfo
    {
        public Action<int> done;
        public MultipleChoiceRequestInfo(IDialogueActor a, Dictionary<IStatement, int> s, float timeout, Action<int> done) { this.done = done; }
    }
    public static class DialogueTree
    {
        public static readonly List<Action> Lines = new List<Action>();
        public static readonly List<Action<int>> Choices = new List<Action<int>>();
        public static void RequestSubtitles(SubtitlesRequestInfo info) { Lines.Add(info.done); }
        public static void RequestMultipleChoices(MultipleChoiceRequestInfo info) { Choices.Add(info.done); }
    }
}
namespace SodaCraft.Localizations
{
    public class LocalizedStatement : NodeCanvas.DialogueTrees.IStatement { public LocalizedStatement(string key) { } }
}
namespace Dialogues
{
    public class DialogueUI
    {
        public static DialogueUI instance = new DialogueUI();
        private Duckov.UI.Animations.FadeGroup mainFadeGroup = new Duckov.UI.Animations.FadeGroup();
        private Duckov.UI.Animations.FadeGroup textAreaFadeGroup = new Duckov.UI.Animations.FadeGroup();
        public static event Action OnDialogueStatusChanged;
        public static void HideTextFadeGroup() { }
    }
}
public static class InputManager
{
    public static bool Disabled;
    public static void DisableInput(UnityEngine.GameObject token) { Disabled = true; }
    public static void ActiveInput(UnityEngine.GameObject token) { Disabled = false; }
}
namespace BossRush
{
    public static class ModBehaviour { public static void DevLog(string message) { } }
    public static class L10n { public static string T(string cn, string en) { return en; } }
    public static class LocalizationHelper { public static void InjectLocalization(string key, string text) { } }
    public class DuckovDialogueActor : NodeCanvas.DialogueTrees.IDialogueActor { }
    public static class DialogueActorFactory
    {
        public static bool Fail;
        public static DuckovDialogueActor Get(UnityEngine.GameObject host) { return null; }
        public static DuckovDialogueActor CreateBilingual(UnityEngine.GameObject host, string id, string cn, string en, UnityEngine.Vector3 offset, object portrait)
        { if (Fail) throw new InvalidOperationException("actor failure"); return new DuckovDialogueActor(); }
    }
    public static class SkyIslandWorldStory { public static string ResidentName(string id) { return id; } }
    public static class SkyIslandUiArt { public static object GetPortrait(string id) { return null; } }
}
