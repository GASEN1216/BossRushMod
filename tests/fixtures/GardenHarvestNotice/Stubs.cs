using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Cysharp.Threading.Tasks
{
    // Task controls completion deterministically; production Observe/CompleteNotice are linked unchanged.
    [AsyncMethodBuilder(typeof(FixtureTaskBuilder))]
    public struct UniTask
    {
        internal Task RawTask;
        public TaskAwaiter GetAwaiter() { return (RawTask ?? Task.CompletedTask).GetAwaiter(); }
        internal static UniTask From(Task task) { return new UniTask { RawTask = task }; }
    }
    public struct FixtureTaskBuilder
    {
        private AsyncTaskMethodBuilder builder;
        public static FixtureTaskBuilder Create() { return new FixtureTaskBuilder { builder = AsyncTaskMethodBuilder.Create() }; }
        public UniTask Task { get { return UniTask.From(builder.Task); } }
        public void SetResult() { builder.SetResult(); }
        public void SetException(Exception error) { builder.SetException(error); }
        public void SetStateMachine(IAsyncStateMachine state) { builder.SetStateMachine(state); }
        public void Start<T>(ref T state) where T : IAsyncStateMachine { builder.Start(ref state); }
        public void AwaitOnCompleted<T, S>(ref T awaiter, ref S state) where T : INotifyCompletion where S : IAsyncStateMachine { builder.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<T, S>(ref T awaiter, ref S state) where T : ICriticalNotifyCompletion where S : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }
    public static class UniTaskExtensions
    {
        public static void Forget(UniTask task) { }
        public static void Forget(UniTask task, bool otherOverload) { }
    }
}

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            var gameObject = value as GameObject;
            if (!ReferenceEquals(gameObject, null)) foreach (Component component in gameObject.Components) component.Destroyed = true;
        }
    }
    public sealed class GameObject : Object { internal readonly List<Component> Components = new List<Component>(); }
    public class Component : Object
    {
        public readonly GameObject gameObject;
        protected Component() { gameObject = new GameObject(); gameObject.Components.Add(this); }
    }
    public static class Debug
    {
        internal static readonly List<string> Warnings = new List<string>();
        public static void LogWarning(string message) { Warnings.Add(message); }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; }
    public static class SceneManager
    {
        internal static int Handle;
        public static Scene GetActiveScene() { return new Scene { handle = Handle }; }
    }
}
namespace Duckov.Crops
{
    public struct CropInfo { public int resultNormal, resultAmount; }
    public class Crop : UnityEngine.Component
    {
        internal CropInfo Value;
        internal bool ThrowOnRead;
        public CropInfo Info
        {
            get
            {
                if (Destroyed || ThrowOnRead) throw new InvalidOperationException("crop unavailable");
                return Value;
            }
        }
        public bool Harvest() { return false; }
    }
}
namespace Duckov.Economy
{
    public struct Cost
    {
        internal Cysharp.Threading.Tasks.UniTask Return(bool directToBuffer = false, bool toPlayerInventory = false,
            int amountFactor = 1, List<ItemStatsSystem.Item> generatedItemsBuffer = null)
        { return Cysharp.Threading.Tasks.UniTask.From(Task.CompletedTask); }
    }
}
namespace ItemStatsSystem
{
    public class Item { }
    public struct ItemMetaData
    {
        internal string Chinese, English;
        public string DisplayName { get { return BossRush.L10n.IsChinese ? Chinese : English; } }
    }
    public static class ItemAssetsCollection
    {
        public static Item GetPrefab(int id) { return BossRush.BackMountainItems.Ready ? new Item() : null; }
        internal static readonly Dictionary<int, ItemMetaData> Metadata = new Dictionary<int, ItemMetaData>();
        internal static bool ThrowOnRead;
        public static ItemMetaData GetMetaData(int typeId)
        {
            if (ThrowOnRead) throw new InvalidOperationException("metadata unavailable");
            ItemMetaData value;
            return Metadata.TryGetValue(typeId, out value) ? value : default(ItemMetaData);
        }
    }
}
namespace Saves { public static class SavesSystem { public static int CurrentSlot; } }
public class CharacterMainControl : UnityEngine.Component { public static CharacterMainControl Main; }
public static class SceneLoader { public static bool IsSceneLoading; }
namespace BossRush
{
    public class ModBehaviour : UnityEngine.Component
    {
        public static ModBehaviour Instance;
        internal readonly List<string> Banners = new List<string>();
        internal static readonly List<string> Logs = new List<string>();
        internal bool ThrowOnBanner;
        internal bool Enabled;
        public bool IsBackMountainConfiguredEnabled() { return Enabled; }
        public void ShowBigBanner(string text)
        {
            if (ThrowOnBanner) throw new InvalidOperationException("banner unavailable");
            Banners.Add(text);
        }
        public static void DevLog(string text) { Logs.Add(text); }
    }
    public static class L10n
    {
        public static bool IsChinese;
        public static string T(string chinese, string english) { return IsChinese ? chinese : english; }
    }
}

namespace Duckov.UI { public static class NotificationText { public static void Push(string text) { } } }
namespace BossRush
{
    internal static class BackMountainItems
    {
        internal sealed class Definition { internal bool IsSeed; }
        internal static bool Ready = true;
        internal static Definition GetDefinition(int id) { return id >= 500062 && id <= 500067 ? new Definition { IsSeed = id < 500065 } : null; }
        internal static bool EnsureRuntimeRegistration(int id) { return Ready; }
    }
}
