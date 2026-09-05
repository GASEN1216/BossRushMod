using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Duckov.Utilities;

namespace UnityEngine
{
    public struct Vector2Int { public int x, y; public Vector2Int(int a, int b) { x = a; y = b; } }
    public static class Mathf
    {
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static int Clamp(int n, int min, int max) { return Math.Min(max, Math.Max(min, n)); }
    }
}
namespace ItemStatsSystem
{
    public struct ItemMetaData { public int id, quality; }
    public static class ItemAssetsCollection
    {
        public static readonly Dictionary<int, ItemMetaData> Metadata = new Dictionary<int, ItemMetaData>();
        public static ItemMetaData GetMetaData(int id)
        {
            if (id == 777) throw new InvalidOperationException("metadata fault");
            ItemMetaData meta;
            return Metadata.TryGetValue(id, out meta) ? meta : default(ItemMetaData);
        }
    }
}
namespace Duckov.Utilities
{
    public class RandomContainer<T>
    {
        public class Entry { public T value; public float weight; }
        public List<Entry> entries = new List<Entry>();
        public void AddEntry(T value, float weight) { entries.Add(new Entry { value = value, weight = weight }); }
        public void RefreshPercent() { }
    }
    public class Tag { }
    public static class GameplayDataSettings
    {
        public class TagsData { public IList<Tag> AllTags = new List<Tag>(); }
        public static TagsData Tags = new TagsData();
    }
    public class LootBoxLoader
    {
        public class Entry { public int itemTypeID; }
        public UnityEngine.Vector2Int randomCount;
        public RandomContainer<int> qualities = new RandomContainer<int>();
        public RandomContainer<Tag> tags = new RandomContainer<Tag>();
        public List<Tag> excludeTags;
        public RandomContainer<Entry> randomPool = new RandomContainer<Entry>();
        public List<int> fixedItems;
        public float fixedChance;
        public bool randomFromPool, ignoreLevelConfig;
        public int SetupCalls;
        public void CalculateChances() { }
        public void StartSetup() { SetupCalls++; }
    }
}
public static class BossLootBoxLoaderReflection
{
    public static Type LoaderEntryType = typeof(LootBoxLoader.Entry);
    public static Type RandomPoolEntryType = typeof(RandomContainer<LootBoxLoader.Entry>.Entry);
    public static FieldInfo RandomPoolField = typeof(LootBoxLoader).GetField("randomPool");
    public static FieldInfo RandomPoolEntriesField = typeof(RandomContainer<LootBoxLoader.Entry>).GetField("entries");
    public static FieldInfo LootEntryItemIdField = LoaderEntryType.GetField("itemTypeID");
    public static FieldInfo RandomPoolEntryValueField = RandomPoolEntryType.GetField("value");
    public static FieldInfo RandomPoolEntryWeightField = RandomPoolEntryType.GetField("weight");
    public static FieldInfo RandomCountField = typeof(LootBoxLoader).GetField("randomCount");
    public static FieldInfo QualitiesField = typeof(LootBoxLoader).GetField("qualities");
    public static FieldInfo TagsField = typeof(LootBoxLoader).GetField("tags");
    public static FieldInfo ExcludeTagsField = typeof(LootBoxLoader).GetField("excludeTags");
    public static FieldInfo FixedItemsField = typeof(LootBoxLoader).GetField("fixedItems");
    public static FieldInfo FixedChanceField = typeof(LootBoxLoader).GetField("fixedChance");
}
public static class RandomEventsTuning { public const string LogPrefix = "test"; }
public partial class ModBehaviour
{
    public HashSet<int> Candidates = new HashSet<int>();
    public HashSet<int> Blacklist = new HashSet<int>();
    private HashSet<int> BuildGeneralBossLootCandidateIdSet() { return Candidates; }
    private bool IsItemBlacklisted(int id) { return Blacklist.Contains(id); }
    private static void DevLog(string message) { }
    private List<Tag> BuildGeneralLootExcludeTags(GameplayDataSettings.TagsData data, bool extra) { return new List<Tag>(); }
    private void MergeGeneralLootExcludeTags(List<Tag> tags, GameplayDataSettings.TagsData data) { }
    public void Configure(LootBoxLoader loader, int count, int min, int max)
    { ConfigureRandomEventAirdropLoader(loader, count, min, max); }
}
public static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    private static int Quality(RandomContainer<LootBoxLoader.Entry>.Entry e)
    { return ItemStatsSystem.ItemAssetsCollection.GetMetaData(e.value.itemTypeID).quality; }
    private static ModBehaviour MakeHost()
    {
        var host = new ModBehaviour();
        for (int q = 1; q <= 8; q++)
            for (int n = 1; n <= q; n++)
            {
                int id = n * 100 + q;
                host.Candidates.Add(id);
                ItemStatsSystem.ItemAssetsCollection.Metadata[id] = new ItemStatsSystem.ItemMetaData { id = id, quality = q };
            }
        host.Candidates.UnionWith(new[] { 0, 777, 999, 888 });
        ItemStatsSystem.ItemAssetsCollection.Metadata[888] = new ItemStatsSystem.ItemMetaData { id = 889, quality = 4 };
        host.Blacklist.Add(104);
        return host;
    }
    private static LootBoxLoader Loader()
    {
        var loader = new LootBoxLoader();
        loader.randomPool.AddEntry(new LootBoxLoader.Entry { itemTypeID = 999 }, 999f);
        loader.fixedItems = new List<int> { 999 };
        loader.fixedChance = 1f;
        return loader;
    }
    public static void Main()
    {
        var host = MakeHost();
        foreach (int cap in new[] { 5, 7, 8 })
        {
            var loader = Loader();
            host.Configure(loader, 4, 4, cap);
            Check(loader.SetupCalls == 1 && loader.randomFromPool, "Q4-Q" + cap + " uses configured pool");
            Check(loader.randomPool.entries.All(e => Quality(e) >= 4 && Quality(e) <= cap), "actual pool respects both quality bounds");
            Check(!loader.randomPool.entries.Any(e => new[] { 0, 104, 777, 888, 999 }.Contains(e.value.itemTypeID)), "blacklist and invalid metadata excluded");
            Check(loader.randomCount.x == 4 && loader.randomCount.y == 4 && loader.fixedItems.Count == 0 && loader.fixedChance == 0, "fixed item count and no inherited fixed rewards");
            var buckets = loader.randomPool.entries.GroupBy(Quality).ToArray();
            Check(buckets.Length == cap - 3 && buckets.All(b => Math.Abs(b.Sum(e => e.weight) - 1f) < 0.00001f), "nonempty quality buckets have equal mass");
            Check(buckets.All(b => b.All(e => e.weight == b.First().weight)), "items within a quality have equal weight");
        }
        var normalized = Loader();
        host.Configure(normalized, 0, 9, 2);
        Check(normalized.randomCount.x == 1 && normalized.randomPool.entries.All(e => Quality(e) == 8), "inverted high bounds and zero count normalize");
        normalized = Loader(); host.Configure(normalized, 4, -3, 99);
        Check(normalized.randomPool.entries.Select(Quality).Distinct().Count() == 8, "full legal quality range remains available");
        normalized = Loader(); host.Configure(normalized, 4, 5, 4);
        Check(normalized.randomPool.entries.All(e => Quality(e) == 5), "inverted bounds keep requested minimum");
        host.Candidates = new HashSet<int> { 0, 777, 888, 999 };
        var empty = Loader(); host.Configure(empty, 4, 4, 8);
        Check(empty.randomPool.entries.Count == 0 && empty.SetupCalls == 0, "all-invalid candidates clear template pool and do not start empty setup");
        host.Candidates.Clear(); empty = Loader(); host.Configure(empty, 4, 4, 8);
        Check(empty.randomPool.entries.Count == 0 && empty.SetupCalls == 0, "empty catalog cannot retain template rewards");
        host.Candidates = null; empty = Loader(); host.Configure(empty, 4, 4, 8);
        Check(empty.randomPool.entries.Count == 0 && empty.SetupCalls == 0, "null catalog cannot retain template rewards");
        var field = BossLootBoxLoaderReflection.RandomPoolField;
        BossLootBoxLoaderReflection.RandomPoolField = null;
        empty = Loader(); MakeHost().Configure(empty, 4, 4, 8);
        Check(empty.SetupCalls == 0, "missing pool binding does not run unrestricted setup");
        BossLootBoxLoaderReflection.RandomPoolField = field;
        var missingBucket = MakeHost();
        missingBucket.Candidates.RemoveWhere(id => id > 0 && id != 777 && ItemStatsSystem.ItemAssetsCollection.GetMetaData(id).quality == 5);
        empty = Loader(); missingBucket.Configure(empty, 4, 4, 5);
        Check(empty.SetupCalls == 1 && empty.randomPool.entries.All(e => Quality(e) == 4)
            && Math.Abs(empty.randomPool.entries.Sum(e => e.weight) - 1f) < 0.00001f, "missing quality redistributes only among valid nonempty buckets");
        Console.WriteLine(checks + " assertions; production methods unchanged, metadata/Unity/loader setup are host stubs.");
    }
}
