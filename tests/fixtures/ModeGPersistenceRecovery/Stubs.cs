using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Cysharp.Threading.Tasks
{
    public static class UniTask
    {
        internal static readonly Queue<Action> Frames = new Queue<Action>();
        public static YieldAwaitable Yield() { return new YieldAwaitable(); }
        internal static void Frame()
        {
            UnityEngine.Time.frameCount++;
            int count = Frames.Count;
            for (int i = 0; i < count; i++) Frames.Dequeue()();
        }
    }
    public struct YieldAwaitable { public YieldAwaiter GetAwaiter() { return new YieldAwaiter(); } }
    public struct YieldAwaiter : INotifyCompletion
    {
        public bool IsCompleted { get { return false; } }
        public void OnCompleted(Action action) { UniTask.Frames.Enqueue(action); }
        public void GetResult() { }
    }
    [AsyncMethodBuilder(typeof(VoidBuilder))]
    public struct UniTaskVoid { public void Forget() { } }
    public struct VoidBuilder
    {
        private AsyncVoidMethodBuilder builder;
        public static VoidBuilder Create() { return new VoidBuilder { builder = AsyncVoidMethodBuilder.Create() }; }
        public UniTaskVoid Task { get { return new UniTaskVoid(); } }
        public void Start<T>(ref T state) where T : IAsyncStateMachine { builder.Start(ref state); }
        public void SetStateMachine(IAsyncStateMachine state) { builder.SetStateMachine(state); }
        public void SetResult() { builder.SetResult(); }
        public void SetException(Exception error) { throw error; }
        public void AwaitOnCompleted<T, S>(ref T awaiter, ref S state) where T : INotifyCompletion where S : IAsyncStateMachine { builder.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<T, S>(ref T awaiter, ref S state) where T : ICriticalNotifyCompletion where S : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }
}
namespace UnityEngine { public static class Time { public static int frameCount; } }
namespace Saves
{
    public static class SavesSystem
    {
        private static bool saving;
        public static bool IsSaving { get { return saving; } }
        public static int CurrentSlot = 1;
        public static event Action OnCollectSaveData, OnSetFile, OnSaveDeleted;
        public static readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        public static int Writes, TypedWrites;
        public static string FailAt;
        public static void BeginOfficialSave() { saving = true; }
        public static void FinishOfficialSave() { saving = false; }
        public static bool KeyExisits(string key) { return Data.ContainsKey(key); }
        public static T Load<T>(string key) { object value; return Data.TryGetValue(key, out value) ? (T)value : default(T); }
        public static void Save<T>(string key, T value) { Data[key] = value; TypedWrites++; }
        public static void SaveFile(bool writeTime)
        {
            // 与官方 SavesSystem.cs:511–519 一致：置闩后备份/写盘抛错，无 finally 复位。
            Writes++; saving = true;
            if (FailAt == "backup" || FailAt == "indexed-backup" || FailAt == "store")
                throw new System.IO.IOException(FailAt);
            saving = false;
        }
    }
}
namespace BossRush
{
    internal static class ModBehaviour { internal static void DevLog(string message) { } }
    public enum ModeGBattleResult { Victory, Defeat }
    public static class ModeGFateContract { public const int ContractCount = 8; }
    internal static class ModeGRuntimeGates { internal static bool IsModeGHostFileWriteDeferred; }
}
