using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            value.Destroyed = true;
            var go = value as GameObject;
            // go 已置 Destroyed，不能再用 Unity 的 == null 判据跳过组件销毁。
            if (!ReferenceEquals(go, null) && !ReferenceEquals(go.Item, null)) go.Item.Destroyed = true;
        }
    }
    public sealed class GameObject : Object { internal ItemStatsSystem.Item Item; }
}
namespace ItemStatsSystem
{
    public class Item : UnityEngine.Object
    {
        public int TypeID;
        internal int Quality;
        internal bool Fallback;
        public UnityEngine.GameObject gameObject;
        internal Item(int id, int quality, bool fallback = false)
        {
            TypeID = id; Quality = quality; Fallback = fallback;
            gameObject = new UnityEngine.GameObject { Item = this };
        }
        public void DestroyTree() { UnityEngine.Object.Destroy(gameObject); }
    }
    public struct ItemFilter
    {
        public object requireTags;
        public int minQuality, maxQuality;
        public string caliber;
    }
    public static class ItemAssetsCollection
    {
        public static object Instance;
        internal static readonly Dictionary<int, Item> Prefabs = new Dictionary<int, Item>();
        internal static int[] Order;
        internal static int Queries, Instantiations;
        internal static bool InstantiateNull, InstantiateThrow, QueryThrow;
        internal static Item LastCreated;
        public static int[] GetAllTypeIds(ItemFilter filter)
        {
            Queries++;
            if (QueryThrow) throw new InvalidOperationException("query failure");
            if (Instance == null) return null;
            var ids = new List<int>();
            foreach (int id in Order)
            {
                Item item;
                if (Prefabs.TryGetValue(id, out item) && item != null && item.Quality >= filter.minQuality
                    && item.Quality <= filter.maxQuality) ids.Add(id);
            }
            return ids.ToArray();
        }
        // Official Search lowers the requested quality when the exact pool is empty.
        public static int[] Search(ItemFilter filter)
        {
            int[] ids = GetAllTypeIds(filter);
            while (ids.Length == 0 && filter.minQuality >= 0 && filter.maxQuality >= 0)
            {
                filter.minQuality--; filter.maxQuality--;
                ids = GetAllTypeIds(filter);
            }
            return ids;
        }
        public static Item GetPrefab(int id)
        {
            Item item;
            return Instance != null && Prefabs.TryGetValue(id, out item) ? item : null;
        }
        public static Item InstantiateSync(int id)
        {
            Instantiations++;
            if (InstantiateThrow) throw new InvalidOperationException("instantiate failure");
            if (InstantiateNull) return null;
            Item prefab = GetPrefab(id);
            // Official missing-resource fallback preserves TypeID and is non-null.
            return LastCreated = prefab != null ? new Item(id, prefab.Quality) : new Item(id, 0, true);
        }
    }
}
namespace Duckov.Economy
{
    internal static class EconomyManager { public static bool Add(long amount) { return true; } }
}
namespace BossRush
{
    internal static class ModBehaviour { public static void DevLog(string value) { } }
    internal static class L10n { public static string T(string cn, string en) { return en; } }
    internal static class DailyReportTuning { public const string LogPrefix = "[DailyReport] "; }
    internal static class DailyReportStatsCollector { public static void SetMoneyDeltaSuppressed(bool value) { } }
    internal static class ModeHConfig { public const int MinGameQuality = 1, MaxGameQuality = 8; }
    internal static class LootBlacklistRegistry
    {
        internal static readonly HashSet<int> Blocked = new HashSet<int>();
        public static void EnsureInitialized() { }
        public static bool Contains(int id) { return Blocked.Contains(id); }
    }
    internal static class CourierService
    {
        internal static int Attempts;
        internal static bool Reject, Fallback;
        internal static ItemStatsSystem.Item Delivered;
        public static int QuickDeliverItems(ItemStatsSystem.Item[] items, string banner, bool fallback, out int fallbackDelivered)
        {
            Attempts++;
            fallbackDelivered = !Reject && Fallback ? 1 : 0;
            if (Reject) return 0;
            Delivered = items[0];
            return Fallback ? 0 : 1;
        }
    }
}
