using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BossRush;

internal static partial class BundleSourceAudit
{
    private static T Field<T>(object plan, string name)
    {
        return (T)plan.GetType().GetField(name).GetValue(plan);
    }

    internal static void Verify(Action<bool, string> check)
    {
        using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllText("tools/resource_release_manifest.json")))
        using (JsonDocument snapshot = JsonDocument.Parse(File.ReadAllText("tools/item_bundle_inventory.json")))
        {
            check(snapshot.RootElement.GetProperty("schema").GetInt32() == 1, "unsupported item bundle inventory schema");
            string[] listedPaths = manifest.RootElement.GetProperty("bundles").EnumerateArray().Select(row => row.GetString()).ToArray();
            var manifestPaths = new HashSet<string>(listedPaths, StringComparer.Ordinal);
            var inventory = snapshot.RootElement.GetProperty("bundles").EnumerateObject()
                .ToDictionary(row => row.Name, row => row.Value, StringComparer.Ordinal);
            var itemBundlePaths = manifestPaths.Where(path => path.StartsWith(EquipmentRoot + "/", StringComparison.Ordinal)
                || path.StartsWith(ItemRoot + "/", StringComparison.Ordinal)).Concat(SpecialPaths.Values);
            foreach (string path in inventory.Keys.Concat(itemBundlePaths).Distinct(StringComparer.Ordinal))
            {
                check(manifestPaths.Contains(path), "item bundle missing from release manifest: " + path);
                check(inventory.ContainsKey(path), "item bundle missing from prefab inventory: " + path);
            }
            var exportedItems = new HashSet<int>(inventory.SelectMany(bundle => bundle.Value.GetProperty("items").EnumerateArray()
                .Select(root => bundle.Key == SpecialPaths["EnsureAdventureJournal"] && IsWikiBook(root[1].GetString())
                    ? BossRushItemIds.AdventureJournal : root[0].GetInt32())));

            FieldInfo plansField = typeof(BossRushDynamicItemRegistry).GetField("Plans", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (DictionaryEntry entry in (IDictionary)plansField.GetValue(null))
            {
                int typeId = (int)entry.Key;
                object plan = entry.Value;
                string[] equipment = Field<string[]>(plan, "EquipmentBundles").Select(name => EquipmentRoot + "/" + name).ToArray();
                string[] items = Field<string[]>(plan, "ItemBundles").Select(name => ItemRoot + "/" + name).ToArray();
                var specialLoader = Field<Func<bool>>(plan, "SpecialLoader");
                var fallbackLoader = Field<Func<int, bool>>(plan, "FallbackLoader");
                string specialPath = null;
                if (specialLoader != null)
                    check(SpecialPaths.TryGetValue(specialLoader.Method.Name, out specialPath), "unmapped special loader for TypeID " + typeId);
                string[] paths = equipment.Concat(items).Concat(specialPath == null ? Array.Empty<string>() : new[] { specialPath }).Distinct().ToArray();
                foreach (string path in paths)
                {
                    check(manifestPaths.Contains(path), "TypeID " + typeId + " references a bundle missing from release manifest: " + path);
                    check(inventory.ContainsKey(path), "TypeID " + typeId + " references a bundle missing from prefab inventory: " + path);
                }

                string modelKey = Field<string>(plan, "SplitModelBaseName");
                if (modelKey != null)
                    CheckModel(check, typeId, equipment, inventory, modelKey);

                if (typeId == BossRushItemIds.ResidualSunShield && items.Length == 0 && specialPath == null)
                {
                    check(fallbackLoader != null && equipment.Contains(EquipmentRoot + "/" + ShieldModelBundle),
                        "model-only shield requires its authored bundle and runtime item fallback");
                    CheckModel(check, typeId, equipment, inventory, ShieldModelKey);
                    continue;
                }
                if (paths.Length == 0)
                {
                    check(!exportedItems.Contains(typeId), "TypeID " + typeId + " has an exported root Item but its plan has no release bundle");
                    check(fallbackLoader != null, "TypeID " + typeId + " has neither a release bundle nor a runtime fallback");
                    continue;
                }

                // The special Wiki loader accepts a named book and corrects the serialized legacy TypeID.
                bool wikiBook = typeId == BossRushItemIds.AdventureJournal && specialLoader?.Method.Name == "EnsureAdventureJournal";
                IEnumerable<JsonElement> roots = paths.SelectMany(path => inventory[path].GetProperty("items").EnumerateArray());
                bool hasItem = roots.Any(root => wikiBook ? IsWikiBook(root[1].GetString()) : root[0].GetInt32() == typeId);
                check(hasItem, "TypeID " + typeId + " has no matching root Item prefab in " + string.Join(", ", paths));
            }
        }
    }

    private static bool IsWikiBook(string name)
    {
        return name.IndexOf("wikibook", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("wiki_book", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void CheckModel(Action<bool, string> check, int typeId, string[] equipment,
        Dictionary<string, JsonElement> inventory, string modelKey)
    {
        bool hasModel = equipment.SelectMany(path => inventory[path].GetProperty("models").EnumerateArray())
            .Any(root => string.Equals(ExtractBaseName(root.GetString()), modelKey, StringComparison.OrdinalIgnoreCase));
        check(hasModel, "TypeID " + typeId + " has no model " + modelKey + " in " + string.Join(", ", equipment));
    }
}
