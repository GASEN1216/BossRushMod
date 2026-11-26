using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;

// 宿主替身：只模拟生产代码碰到的官方表面（关卡、存档、物品树、UniTask 帧）。
// 生产 BossRushEquipmentRescue.cs 与 BossRushStartupGuard.cs 原样链接编译。
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        internal bool Active = true;
        public string name = string.Empty;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }

        public static T[] FindObjectsByType<T>(FindObjectsInactive findObjectsInactive, FindObjectsSortMode sortMode) where T : Object
        {
            World.SceneScans++;
            return World.SceneObjects.OfType<T>()
                .Where(o => o != null && (findObjectsInactive == FindObjectsInactive.Include || o.Active))
                .ToArray();
        }
    }
    public enum FindObjectsInactive { Exclude, Include }
    public enum FindObjectsSortMode { None, InstanceID }
    public class GameObject : Object { public SceneManagement.Scene scene = new SceneManagement.Scene { valid = true }; }
    public class Component : Object { public GameObject gameObject = new GameObject(); }
    public static class Debug
    {
        internal static readonly List<string> Lines = new List<string>();
        public static void Log(object message) { Lines.Add("I " + message); }
        public static void LogWarning(object message) { Lines.Add("W " + message); }
        public static void LogError(object message) { Lines.Add("E " + message); }
    }
    public static class Application { public static string dataPath { get { return null; } } }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name;
        internal bool valid;
        public bool IsValid() { return valid; }
    }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { name = "Level_Fixture", valid = true }; } }
}

namespace Cysharp.Threading.Tasks
{
    /// <summary>可控帧：NextFrame 挂起，直到测试调 StepFrame。</summary>
    [AsyncMethodBuilder(typeof(UniTaskBuilder))]
    public struct UniTask
    {
        internal Task task;
        public TaskAwaiter GetAwaiter() { return task.GetAwaiter(); }
        private static readonly List<TaskCompletionSource<bool>> frames = new List<TaskCompletionSource<bool>>();
        public static UniTask NextFrame()
        {
            var frame = new TaskCompletionSource<bool>();
            frames.Add(frame);
            return new UniTask { task = frame.Task };
        }
        internal static void StepFrame()
        {
            var due = frames.ToArray();
            frames.Clear();
            foreach (var frame in due) frame.SetResult(true);
        }
        internal static void DropFrames() { frames.Clear(); }
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
        public void Forget() { if (task.IsCompleted) task.GetAwaiter().GetResult(); }
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

namespace ItemStatsSystem
{
    public class Inventory : UnityEngine.Object { }
    public class Item : UnityEngine.Component
    {
        public int TypeID;
        public readonly SlotCollection Slots = new SlotCollection();
        public Item ParentObject;
        public Inventory InInventory;
        public Slot PluggedIntoSlot;
        public void DestroyTree()
        {
            Destroyed = true;
            foreach (Slot slot in Slots.All) if (slot.Content != null) slot.Content.DestroyTree();
        }
    }
}

namespace ItemStatsSystem.Items
{
    public class Slot
    {
        public Item Content;
        public bool CanPlug(Item item)
        {
            // 装备槽要标签：官方空壳 FallbackItem_<id> 没有标签，插不进去。
            return item != null && item.name != "FallbackItem_" + item.TypeID;
        }
        public bool Plug(Item item, out Item unplugged)
        {
            unplugged = Content;
            Content = item;
            item.PluggedIntoSlot = this;
            return true;
        }
    }
    public class SlotCollection
    {
        private readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>();
        public Slot this[string key] { get { Slot slot; return key != null && slots.TryGetValue(key, out slot) ? slot : null; } }
        internal void Add(string key) { slots[key] = new Slot(); }
        internal IEnumerable<Slot> All { get { return slots.Values; } }
    }
}

namespace ItemStatsSystem.Data
{
    public class ItemTreeData
    {
        public int rootInstanceID;
        public List<DataEntry> entries = new List<DataEntry>();
        public DataEntry RootData { get { return entries.Find(e => e != null && e.instanceID == rootInstanceID); } }

        public class DataEntry
        {
            public int instanceID;
            public int typeID;
            public List<SlotInstanceIDPair> slotContents = new List<SlotInstanceIDPair>();
            public List<InventoryDataEntry> inventory = new List<InventoryDataEntry>();
        }
        public class SlotInstanceIDPair { public string slot; public int instanceID; }
        public class InventoryDataEntry { public int position; public int instanceID; }

        internal static Func<ItemTreeData, UniTask<Item>> Hook;
        internal static int Calls;
        public static UniTask<Item> InstantiateAsync(ItemTreeData data)
        {
            Calls++;
            return Hook(data);
        }
    }
}

namespace Duckov.UI
{
    public static class NotificationText
    {
        internal static readonly List<string> Messages = new List<string>();
        public static void Push(string text) { Messages.Add(text); }
    }
}

namespace Saves
{
    public static class SavesSystem
    {
        public static int CurrentSlot = 1;
        internal static ItemTreeData Stored;
        internal static int LoadCalls;
        public static T Load<T>(string prefix, string key)
        {
            LoadCalls++;
            if (prefix != "Item/" || key != "MainCharacterItemData") throw new InvalidOperationException("unexpected save key " + prefix + key);
            return (T)(object)Stored;
        }
    }
}

public class CharacterMainControl : UnityEngine.Object
{
    public Item CharacterItem;
    internal readonly List<Item> Received = new List<Item>();
}

public class LevelManager : UnityEngine.Object
{
    internal static LevelManager instance;
    internal bool levelInited;
    public CharacterMainControl MainCharacter { get; internal set; }
    public static LevelManager Instance { get { return instance == null ? null : instance; } }
    public static bool LevelInited { get { return instance != null && instance.levelInited; } }
    public static event Action OnAfterLevelInitialized;
    internal static void RaiseAfterLevelInitialized() { Action handler = OnAfterLevelInitialized; if (handler != null) handler(); }
    internal static int Subscribers { get { Action handler = OnAfterLevelInitialized; return handler == null ? 0 : handler.GetInvocationList().Length; } }
}

public static class LevelConfig { public static bool SaveCharacter = true; }

public static class ItemUtilities
{
    internal static int InventorySends;
    internal static int StorageSends;
    public static bool SendToPlayerCharacterInventory(Item item, bool dontMerge)
    {
        InventorySends++;
        LevelManager level = LevelManager.Instance;
        CharacterMainControl main = level != null ? level.MainCharacter : null;
        if (main == null) return false;
        main.Received.Add(item);
        return true;
    }
    public static bool SendToPlayerStorage(Item item, bool directToBuffer)
    {
        StorageSends++;
        return true;
    }
}

namespace BossRush
{
    public partial class ModBehaviour : UnityEngine.Object
    {
        internal static string GetModPath() { return string.Empty; }
        internal static void DevLog(string message) { }
    }
    internal static class BossRushDynamicItemRegistry
    {
        internal static bool IsBossRushDynamicItemType(int typeID) { return typeID >= 500000 && typeID < 600000; }
        internal static void EnsureAllRegistered() { }
    }
    internal static class L10n { internal static string T(string zh, string en) { return zh; } }
}

internal static class World
{
    internal static readonly List<UnityEngine.Object> SceneObjects = new List<UnityEngine.Object>();
    internal static int SceneScans;
}
