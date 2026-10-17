using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public static class Mathf
    {
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
    }

    public static class Random { public static int Range(int min, int max) { return min; } }
}

namespace Duckov.Utilities
{
    public sealed class Tag { public string name; }
}

namespace ItemStatsSystem
{
    using Duckov.Utilities;

    /// <summary>可记录替身：数量设置按官方语义夹到 MaxStackCount，不堆叠的物品数量恒为 1。</summary>
    public sealed class Item
    {
        public int TypeID;
        public bool Stackable;
        public int MaxStackCount = 1;
        private int stack = 1;
        public int StackCount
        {
            get { return Stackable ? stack : 1; }
            set
            {
                if (!Stackable) { if (value != 1) Table.NonStackableCountWrites++; return; }
                stack = value >= 1 && value > MaxStackCount ? MaxStackCount : value;
            }
        }
    }

    public struct ItemFilter
    {
        public Tag[] requireTags;
        public int minQuality;
        public int maxQuality;
        public string caliber;
    }

    public struct ItemMetaData { public int id; public int quality; }

    public sealed class TableEntry
    {
        public int Id, Quality, MaxStack;
        public bool Stackable;
        public string Caliber;
        public string[] Tags;
    }

    public static class Table
    {
        public static readonly List<TableEntry> Entries = new List<TableEntry>();
        public static readonly List<Item> Sent = new List<Item>();
        public static int NonStackableCountWrites;
        public static void Reset() { Entries.Clear(); Sent.Clear(); NonStackableCountWrites = 0; }
    }

    public static class ItemAssetsCollection
    {
        private static bool Matches(TableEntry e, ItemFilter f)
        {
            if (e.Quality < f.minQuality || e.Quality > f.maxQuality) return false;
            if (!string.IsNullOrEmpty(f.caliber) && e.Caliber != f.caliber) return false;
            if (f.requireTags != null && f.requireTags.Any(t => !e.Tags.Contains(t.name))) return false;
            return true;
        }

        public static int[] GetAllTypeIds(ItemFilter filter) { return Table.Entries.Where(e => Matches(e, filter)).Select(e => e.Id).ToArray(); }

        /// <summary>官方 Search 落空时只把品质区间往下降级，不会往上找。</summary>
        public static int[] Search(ItemFilter filter)
        {
            int[] result = GetAllTypeIds(filter);
            while (result.Length < 1)
            {
                filter.minQuality--; filter.maxQuality--;
                if (filter.maxQuality < 0 || filter.minQuality < 0) break;
                result = GetAllTypeIds(filter);
            }
            return result;
        }

        public static ItemMetaData GetMetaData(int id)
        {
            TableEntry e = Table.Entries.FirstOrDefault(x => x.Id == id);
            return e == null ? default(ItemMetaData) : new ItemMetaData { id = e.Id, quality = e.Quality };
        }

        public static Item InstantiateSync(int id)
        {
            TableEntry e = Table.Entries.FirstOrDefault(x => x.Id == id);
            return e == null ? null : new Item { TypeID = e.Id, Stackable = e.Stackable, MaxStackCount = e.MaxStack };
        }
    }
}

public static class ItemUtilities
{
    public static void SendToPlayer(ItemStatsSystem.Item item, bool dontMerge = false, bool sendToStorage = true) { ItemStatsSystem.Table.Sent.Add(item); }
}

namespace BossRush
{
    using Duckov.Utilities;

    public sealed class ModBehaviour { public static void DevLog(string message) { } }

    public static class ZombieModeTuning { public const int HeavyAmmoMinimumRounds = 4; }

    internal sealed partial class ZombieModeRuntimeModule
    {
        internal static readonly HashSet<string> KnownTags = new HashSet<string> { "Ammo", "Bullet" };

        private Tag[] ResolveZombieModeTags(string[] tagNames)
        {
            return tagNames.Where(KnownTags.Contains).Select(n => new Tag { name = n }).ToArray();
        }

        internal bool Give(string caliber, int count, int minQuality, int maxQuality) { return TryGiveZombieModeAmmo(caliber, count, minQuality, maxQuality); }
    }
}
