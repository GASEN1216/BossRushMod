using System;
using ItemStatsSystem;
using System.Collections.Generic;

// 隔离执行 ZombieModeEntryDebt 所需的最小替身。生产文件原样链接，这里只顶住它引用的官方类型：
// 存档（ES3 缓存语义）、经济（Instance 为空时 Add 返回 false）、物品实例化与送达、通知与本地化。
// 每个替身都带一个可编程的失败开关，用来精确复现「经济暂不可用」「资源未就绪」「没有当前存档文件」。

namespace Saves
{
    /// <summary>按 key 存值的内存替身。<see cref="FilePresent"/> 为 false 时模拟「没有当前存档文件」——
    /// 官方 <c>SavesSystem.Save</c> 此时只打一行日志就返回，写入静默丢失。</summary>
    internal static class SavesSystem
    {
        internal static readonly Dictionary<string, object> Store = new Dictionary<string, object>(StringComparer.Ordinal);
        internal static bool FilePresent = true;
        internal static bool ThrowOnSave;
        internal static bool ThrowOnLoad;
        internal static int Writes;

        internal static void Reset()
        {
            Store.Clear();
            FilePresent = true;
            ThrowOnSave = false;
            ThrowOnLoad = false;
            Writes = 0;
        }

        internal static void Save<T>(string key, T value)
        {
            if (ThrowOnSave) throw new Exception("save faulted");
            Writes++;
            if (!FilePresent) return;
            Store[key] = value;
        }

        internal static T Load<T>(string key)
        {
            if (ThrowOnLoad) throw new Exception("load faulted");
            object value;
            if (Store.TryGetValue(key, out value)) return (T)value;
            return default(T);
        }

        internal static bool KeyExisits(string key)
        {
            if (ThrowOnLoad) throw new Exception("load faulted");
            return Store.ContainsKey(key);
        }
    }
}

namespace Duckov.Economy
{
    /// <summary>经济替身。<see cref="Available"/> 为 false 对应官方 <c>Instance == null</c>：<c>Add</c> 返回 false。</summary>
    internal static class EconomyManager
    {
        internal static Action OnEconomyManagerLoaded;
        internal static bool Available = true;
        internal static bool ThrowOnAdd;
        internal static long Money;

        internal static void Reset()
        {
            OnEconomyManagerLoaded = null;
            Available = true;
            ThrowOnAdd = false;
            Money = 0L;
        }

        internal static bool Add(long amount)
        {
            if (ThrowOnAdd) throw new Exception("economy faulted");
            if (!Available) return false;
            Money += amount;
            return true;
        }

        /// <summary>模拟官方 <c>Load()</c> 末尾那次广播（<c>Awake</c> 与切换存档文件都会触发）。</summary>
        internal static void RaiseLoaded()
        {
            Action handler = OnEconomyManagerLoaded;
            if (handler != null) handler();
        }

        internal static int SubscriberCount
        {
            get { return OnEconomyManagerLoaded == null ? 0 : OnEconomyManagerLoaded.GetInvocationList().Length; }
        }
    }
}

namespace Duckov.UI
{
    internal static class NotificationText
    {
        internal static readonly List<string> Pushed = new List<string>();
        internal static void Push(string text) { Pushed.Add(text); }
        internal static void Reset() { Pushed.Clear(); }
    }
}

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed)); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        internal static void Destroy(GameObject go) { go.Destroyed = true; foreach (Object component in go.Components) component.Destroyed = true; }
    }
    internal class GameObject : Object { internal bool activeInHierarchy; internal List<Object> Components = new List<Object>(); }
}
internal class CharacterMainControl
{
    internal static CharacterMainControl Main;
    internal ItemStatsSystem.Item CharacterItem;
}
internal static class LevelManager
{
    internal static Action OnAfterLevelInitialized;
    internal static void RaiseReady() { OnAfterLevelInitialized?.Invoke(); }
    internal static int SubscriberCount { get { return OnAfterLevelInitialized == null ? 0 : OnAfterLevelInitialized.GetInvocationList().Length; } }
}
internal class InteractablePickup { }
namespace ItemStatsSystem
{
    internal class Inventory : List<Item> { }
    internal sealed class Item : UnityEngine.Object
    {
        static int next;
        readonly int id = ++next;
        internal int TypeID;
        internal Item Character;
        internal Inventory InInventory, Inventory;
        internal ItemAgent ActiveAgent;
        internal UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        internal Item() { gameObject.Components.Add(this); }
        internal bool IsBeingDestroyed { get { return Destroyed; } }
        internal int GetInstanceID() { return id; }
        internal Item GetCharacterItem() { return Character; }
        internal void DestroyTree()
        {
            if (InInventory != null) InInventory.Remove(this);
            if (ActiveAgent != null) UnityEngine.Object.Destroy(ActiveAgent.gameObject);
            UnityEngine.Object.Destroy(gameObject);
        }
    }
    internal class ItemAgent : UnityEngine.Object
    {
        internal enum AgentTypes { normal, pickUp, handheld, equipment }
        internal AgentTypes AgentType;
        internal Item Item;
        internal bool PickupPresent;
        internal UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        internal T GetComponent<T>() where T : class { return PickupPresent ? new InteractablePickup() as T : null; }
    }
    internal static class ItemAssetsCollection
    {
        internal static bool Available = true;
        internal static bool ThrowOnInstantiate;
        internal static int Instantiated;
        internal static int FailAfter = -1;
        internal static readonly List<Item> Created = new List<Item>();
        internal static void Reset()
        {
            Available = true; ThrowOnInstantiate = false; Instantiated = 0; FailAfter = -1; Created.Clear();
        }
        internal static Item GetPrefab(int typeId) { return Available ? new Item { TypeID = typeId } : null; }
        internal static Item InstantiateSync(int typeId)
        {
            if (ThrowOnInstantiate) throw new Exception("asset faulted");
            if (!Available || (FailAfter >= 0 && Instantiated >= FailAfter)) return null;
            Instantiated++;
            Item item = new Item { TypeID = typeId }; Created.Add(item); return item;
        }
    }
    internal static class ItemUtilities
    {
        internal enum Destination { Backpack, Storage, Buffer, Ground, BrokenGround }
        internal static readonly List<Item> Delivered = new List<Item>();
        internal static bool ThrowOnSend, ThrowAfterSend;
        internal static Destination Target;
        internal static int FailAfter;
        internal static void Reset()
        {
            Delivered.Clear(); ThrowOnSend = ThrowAfterSend = false; Target = Destination.Backpack; FailAfter = -1;
        }
        internal static void SendToPlayer(Item item, bool a, bool b)
        {
            if (ThrowOnSend || (FailAfter >= 0 && Delivered.Count >= FailAfter)) throw new Exception("send faulted before transfer");
            if (Target == Destination.Backpack)
            {
                item.Character = CharacterMainControl.Main.CharacterItem;
                item.InInventory = item.Character.Inventory; item.InInventory.Add(item);
            }
            else if (Target == Destination.Storage)
            {
                item.InInventory = BossRush.PlayerStorage.Inventory; item.InInventory.Add(item);
            }
            else if (Target == Destination.Buffer)
            {
                BossRush.PlayerStorage.IncomingItemBuffer.Add(new BossRush.BufferedTree {
                    rootInstanceID = item.GetInstanceID(), RootTypeID = item.TypeID });
                item.DestroyTree();
            }
            else
            {
                item.ActiveAgent = new ItemAgent { AgentType = ItemAgent.AgentTypes.pickUp, Item = item,
                    PickupPresent = Target == Destination.Ground };
                item.ActiveAgent.gameObject.activeInHierarchy = true;
            }
            if (Target != Destination.BrokenGround) Delivered.Add(item);
            if (ThrowAfterSend) throw new Exception("notification faulted after transfer");
        }
    }
}

namespace BossRush
{
    internal static class L10n
    {
        internal static string T(string key) { return key; }
    }

    internal static class ModBehaviour
    {
        internal static readonly List<string> Logs = new List<string>();
        internal static void DevLog(string message) { Logs.Add(message); }
        internal static void ResetLogs() { Logs.Clear(); }
    }

    internal static class BossRushItemIds
    {
        internal const int ZombieTideInvitation = 500045;
    }

    internal static class ZombieTideInvitationConfig
    {
        internal static void EnsureRuntimeFallbackRegistrationShell() { }
    }

    internal sealed class PlayerStorage
    {
        internal static PlayerStorage Instance;
        internal static bool Loading;
        internal static Inventory Inventory;
        internal static readonly List<BufferedTree> IncomingItemBuffer = new List<BufferedTree>();
        internal bool Initialized;
        internal bool HasInitialized() { return Initialized; }
    }
    internal sealed class BufferedTree { internal int rootInstanceID, RootTypeID; }
    internal static class PlayerStorageBuffer { internal static object Instance; }
}
