using System;
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

namespace ItemStatsSystem
{
    internal sealed class Item
    {
        internal int TypeID;
    }

    /// <summary><see cref="Available"/> 为 false 对应资源未就绪：<c>InstantiateSync</c> 返回 null。</summary>
    internal static class ItemAssetsCollection
    {
        internal static bool Available = true;
        internal static bool ThrowOnInstantiate;
        internal static int Instantiated;
        /// <summary>造出这么多个之后资源就掉线（负数表示不限制）。用来复现「结账到一半资源没了」。</summary>
        internal static int FailAfter = -1;

        internal static void Reset()
        {
            Available = true;
            ThrowOnInstantiate = false;
            Instantiated = 0;
            FailAfter = -1;
        }

        internal static Item InstantiateSync(int typeId)
        {
            if (ThrowOnInstantiate) throw new Exception("asset faulted");
            if (!Available) return null;
            if (FailAfter >= 0 && Instantiated >= FailAfter) return null;
            Instantiated++;
            return new Item { TypeID = typeId };
        }
    }

    internal static class ItemUtilities
    {
        internal static readonly List<Item> Delivered = new List<Item>();
        internal static bool ThrowOnSend;

        internal static void Reset()
        {
            Delivered.Clear();
            ThrowOnSend = false;
        }

        internal static void SendToPlayer(Item item, bool a, bool b)
        {
            if (ThrowOnSend) throw new Exception("send faulted");
            Delivered.Add(item);
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

    internal static class PlayerStorage
    {
        internal static object Inventory = new object();
    }
}
