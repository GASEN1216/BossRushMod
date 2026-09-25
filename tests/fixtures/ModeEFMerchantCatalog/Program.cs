using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
}
namespace Duckov.Utilities
{
    public class Tag : UnityEngine.Object { public string name; }
    public static class GameplayDataSettings
    {
        public class TagsData { public Tag Gun, Bullet, Helmat, Armor, Backpack, Bait; }
        public static TagsData Tags;
    }
}
namespace ItemStatsSystem
{
    public struct ItemFilter
    {
        public Duckov.Utilities.Tag[] requireTags, excludeTags;
        public int minQuality, maxQuality;
    }
    public static class ItemAssetsCollection
    {
        public static Func<ItemFilter, int[]> SearchImpl;
        public static int[] Search(ItemFilter filter) { return SearchImpl(filter); }
    }
}
namespace BossRush
{
    using Duckov.Utilities;
    using ItemStatsSystem;
    internal class CharacterRandomPreset : UnityEngine.Object { public string nameKey; public int icon; }
    internal static class ModBehaviour { public static void DevLog(string value) { } }
    internal static class ObjectCache
    {
        internal static CharacterRandomPreset[] Presets;
        internal static int Reads;
        internal static CharacterRandomPreset[] GetCharacterPresets() { Reads++; return Presets; }
    }
    internal static class BossRushEagerReflectionCache
    { internal static FieldInfo CharacterRandomPreset_CharacterIconType = typeof(CharacterRandomPreset).GetField("icon"); }
    internal static class Program
    {
        private static Tag T(string name) { return new Tag { name = name }; }
        private static CharacterRandomPreset P(string name, int icon = 0)
        { return new CharacterRandomPreset { nameKey = name, icon = icon }; }
        private static void Check(bool condition, string label)
        { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
        private static void Main()
        {
            ModeEFMerchantCatalog.ResetStaticCaches();
            var generic = P("Merchant_Generic");
            var myst = P("Merchant_Myst");
            IReadOnlyDictionary<string, CharacterRandomPreset> presets = new Dictionary<string, CharacterRandomPreset>
            { { generic.nameKey, generic }, { myst.nameKey, myst } };
            var aliases = new Dictionary<string, Tag>();
            var queries = new List<string>();
            Func<string, Tag> find = name => { queries.Add(name); Tag tag; return aliases.TryGetValue(name, out tag) ? tag : null; };
            int secondReads = 0;
            var catalog = new ModeEFMerchantCatalog(() => presets, find);
            var second = new ModeEFMerchantCatalog(() => { secondReads++; return presets; }, find);
            ObjectCache.Presets = new[] { P("Merchant_Myst_Scanned") };
            Check(ReferenceEquals(catalog.GetModeEMerchantPreset(), myst), "preset dictionary prefers mystical merchant");
            Check(ReferenceEquals(second.GetModeEMerchantPreset(), myst) && secondReads == 0 && ObjectCache.Reads == 0,
                "preset cache remains shared across consumers without scanning");
            myst.Destroyed = true;
            presets = new Dictionary<string, CharacterRandomPreset> { { generic.nameKey, generic } };
            Check(ReferenceEquals(catalog.GetModeEMerchantPreset(), generic), "destroyed cached preset is replaced by dictionary fallback");
            ModeEFMerchantCatalog.ResetStaticCaches();
            presets = null;
            var scanned = P("Merchant_Myst_Scanned");
            ObjectCache.Presets = new[] { generic, scanned, P("Merchant_Myst_Destroyed") };
            ObjectCache.Presets[2].Destroyed = true;
            Check(ReferenceEquals(catalog.GetModeEMerchantPreset(), scanned), "scene scan prefers mystical merchant");
            ModeEFMerchantCatalog.ResetStaticCaches();
            var icon = P("IconOnly", 4);
            ObjectCache.Presets = new[] { P("Ordinary", 2), icon };
            Check(ReferenceEquals(catalog.GetModeEMerchantPreset(), icon), "icon fallback remains last resort");

            aliases["Medical"] = T("Medical"); aliases["Injector"] = T("Injector");
            aliases["FaceMask"] = T("FaceMask"); aliases["Headset"] = T("Headset");
            aliases["Food"] = T("Food");
            GameplayDataSettings.Tags = new GameplayDataSettings.TagsData { Gun = T("Gun"), Bait = T("Bait") };
            var categories = catalog.GetModeEMerchantCategories(GameplayDataSettings.Tags);
            Check(categories.Select(c => c.Item3).SequenceEqual(new[] { "Gun", "Mask", "Medical", "Food", "Bait" }),
                "available category order retains combined facewear and medical groups");
            Check(categories[1].Item1[0] == aliases["FaceMask"] && categories[1].Item1[1] == aliases["Headset"]
                && categories[2].Item1[1] == aliases["Injector"] && queries.IndexOf("Medic") < queries.IndexOf("Medical"),
                "tag aliases preserve fallback and injector ordering");
            Check(catalog.GetModeEMerchantCategories(null).Count == 0, "missing tags yields no categories");
            var excluded = new[] { T("Excluded") };
            int searches = 0;
            ItemAssetsCollection.SearchImpl = filter =>
            {
                searches++;
                Check(filter.minQuality == 1 && filter.maxQuality == 99, "search retains quality bounds");
                if (filter.requireTags[0] == aliases["Medical"]) return new[] { 88, 9, 3 };
                if (filter.requireTags[0] == aliases["Injector"]) return new[] { 3, 6, 89 };
                return new[] { 7 };
            };
            ModeEFMerchantCatalog.ResetStaticCaches();
            var medical = categories[2].Item1;
            var first = catalog.ModeESearchItemsMultiTag(medical, excluded);
            Check(first.SequenceEqual(new[] { 88, 9, 3, 6, 89 }) && searches == 2, "multi-tag search deduplicates without sorting");
            first.Clear();
            var repeated = second.ModeESearchItemsMultiTag(medical, excluded);
            Check(repeated.Count == 5 && searches == 2, "consumer mutations cannot alter shared cached IDs");
            catalog.ModeESearchItemsMultiTag(medical, new[] { T("Another") });
            Check(searches == 4, "exclude tags participate in cache identity");
            var ids = catalog.GetModeEMerchantCategoryPoolIds("Medical");
            Check(ids.SequenceEqual(new[] { 9, 3, 6 }) && catalog.IsExcludedMedicalItem(1429)
                && !catalog.IsExcludedMedicalItem(9), "medical pool excludes exact legacy IDs");
            int beforeReset = searches;
            ModeEFMerchantCatalog.ResetStaticCaches();
            second.ModeESearchItemsMultiTag(medical, excluded);
            Check(searches == beforeReset + 2, "reset invalidates shared category search cache");
            Check(catalog.GetModeEMerchantCategoryPoolIds(null).Length == 0
                && catalog.GetModeEMerchantCategoryPoolIds("missing").Length == 0, "missing category returns empty pool");
            ModeEFMerchantCatalog.ResetStaticCaches();
            int frames = 0;
            var warmup = catalog.WarmModeEMerchantCachesAsync();
            while (warmup.MoveNext()) { Check(warmup.Current == null, "warmup yields only frame boundaries"); frames++; }
            Check(frames == categories.Count + 1, "warmup preserves one preset frame and one per category");
            GameplayDataSettings.Tags = null;
            warmup = catalog.WarmModeEMerchantCachesAsync();
            Check(warmup.MoveNext() && !warmup.MoveNext(), "missing tags still yields preset frame");
            ModeEFMerchantCatalog.ResetStaticCaches();
            ItemAssetsCollection.SearchImpl = filter => { throw new Exception("fixture search failure"); };
            Check(catalog.ModeESearchItemsMultiTag(medical, excluded).Count == 0, "failed searches retain empty fallback");
            ModeEFMerchantCatalog.ResetStaticCaches();
        }
    }
}
