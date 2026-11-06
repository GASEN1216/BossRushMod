using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Duckov.Utilities
{
    public sealed class Tag { public string name; }
    internal static class GameplayDataSettings
    {
        internal static TagsData Tags;
        internal sealed class TagsData
        {
            public Tag[] AllTags;
            public Tag Character, LockInDemoTag, Helmat, Armor, Backpack, Bullet, Bait, Special,
                DestroyOnLootBox, DontDropOnDeadInSlot, Gun;
            internal TagsData(string[] names, string[] fixedNames)
            {
                AllTags = names.Select(n => new Tag { name=n }).ToArray();
                var f = fixedNames.Select(n => AllTags.Single(t => t.name == n)).ToArray();
                Character=f[0]; LockInDemoTag=f[1]; Helmat=f[2]; Armor=f[3]; Backpack=f[4]; Bullet=f[5];
                Bait=f[6]; Special=f[8]; DestroyOnLootBox=f[9]; DontDropOnDeadInSlot=f[10]; Gun=f[11];
            }
        }
    }
}
namespace ItemStatsSystem
{
    using Duckov.Utilities;
    internal sealed class Item
    {
        internal int TypeID, Quality, Value, MaxStackCount;
        internal HashSet<string> Tags;
        public bool CanBeSold { get { return !Tags.Contains("Sticky") && !Tags.Contains("NotSellable"); } }
    }
    internal struct ItemFilter { internal Tag[] requireTags, excludeTags; internal int minQuality, maxQuality; internal string caliber; }
    internal static class ItemAssetsCollection
    {
        internal static Dictionary<int,Item> Items;
        internal static void Load()
        {
            var lines = File.ReadAllLines("tests/fixtures/TidegateTreasure/catalog.txt");
            GameplayDataSettings.Tags = new GameplayDataSettings.TagsData(
                lines.Single(s=>s.StartsWith("# all ")).Substring(6).Split(','),
                lines.Single(s=>s.StartsWith("# fixed ")).Substring(8).Split(','));
            Items = lines.Where(s=>!s.StartsWith("#")).Select(s=>s.Split('|')).Select(c=>new Item
            {
                TypeID=int.Parse(c[0]), Quality=int.Parse(c[1]), Value=int.Parse(c[2]),
                MaxStackCount=int.Parse(c[3]), Tags=new HashSet<string>(c[4].Split(','))
            }).ToDictionary(i=>i.TypeID);
        }
        public static int[] GetAllTypeIds(ItemFilter f)
        {
            if (!string.IsNullOrEmpty(f.caliber)) throw new NotSupportedException("caliber filter not requested by production pool");
            return Items.Values.Where(i=>i.Quality>=f.minQuality && i.Quality<=f.maxQuality
                && (f.requireTags==null || f.requireTags.All(t=>t==null || i.Tags.Contains(t.name)))
                && (f.excludeTags==null || !f.excludeTags.Any(t=>t!=null && i.Tags.Contains(t.name)))).Select(i=>i.TypeID).ToArray();
        }
        public static Item GetPrefab(int id)
        {
            Item result;
            if (Items.TryGetValue(id, out result)) return result;
            if (id==BossRush.BossRushItemIds.TidegateHandwheel || id==BossRush.BossRushItemIds.TidegateTideWatch ||
                id==BossRush.BossRushItemIds.TidegateFlippers) return new Item {TypeID=id, MaxStackCount=1};
            return null;
        }
    }
}
namespace BossRush
{
    internal static class ModBehaviour { public static void DevLog(string message) { } }
    internal static class SkyIslandEnemyArmory { public static IEnumerator Prewarm() { yield break; } }
    // The ID exclusions are loaded from the same shipped JSON as the in-game registry.
    internal static class LootBlacklistRegistry
    {
        internal static readonly HashSet<int> Ids = new HashSet<int>(JsonDocument.Parse(File.ReadAllText("Assets/Data/LootBlacklist.json"))
            .RootElement.GetProperty("itemIds").EnumerateArray().Select(v=>v.GetInt32()));
        public static bool Contains(int id) { return Ids.Contains(id); }
    }
}
