using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BossRush;
using ItemStatsSystem;
using UnityEngine;

internal static partial class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class WaterRun
    {
        internal int High, NativeRed, Stacks;
        internal long Value;
        internal string Land, Water, HighItems;
    }
    private static string Hash(string text) { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))); }

    private static WaterRun SearchHarbor(int seed, bool reverse, bool pois, bool baseline, bool waterOnly = false, bool waterFirst = false)
    {
        var data = JsonDocument.Parse(File.ReadAllText("tests/fixtures/TidegateTreasure/HarborLayout.json")).RootElement;
        var world = new TidegateWorld();
        var waterIds = new HashSet<string>();
        foreach (var row in data.GetProperty("containers").EnumerateArray())
        {
            string id = row.GetProperty("id").GetString(); var pos = row.GetProperty("position");
            // Independent physical oracle: every authored container below high tide minus 0.5 m.
            bool water = pos[1].GetSingle() < -.05f;
            if (water) waterIds.Add(id);
            if (waterOnly && !water) continue;
            var marker = new GameObject(id).transform;
            marker.position = new Vector3(pos[0].GetSingle(), pos[1].GetSingle(), pos[2].GetSingle());
            world.LootMarkers.Add(marker);
        }
        Check(waterIds.Count == 15, "authored underwater layout changed; audit required");
        if (pois)
            foreach (var row in data.GetProperty("pois").EnumerateArray())
            {
                var p = row.GetProperty("position");
                var t = new GameObject("POI_" + row.GetProperty("name").GetString()).transform;
                t.position = new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
                world.Pois.Add(t);
            }
        if (reverse) world.LootMarkers.Reverse();
        Time.time = 1; SkyIslandRewardCrate.Boxes.Clear();
        var player = new GameObject("duck").AddComponent<CharacterMainControl>();
        var result = new WaterRun(); var land = new StringBuilder(); var waterText = new StringBuilder(); var high = new StringBuilder();
        using (var raid = new TidegateLoot(world, new TidegateTide(), seed, () => true, (s,w) => { }))
        {
            // Invoke the real builder to vary travel/materialization order without replacing any loot logic.
            var points = (IEnumerable)typeof(TidegateLoot).GetField("points", PrivateInstance).GetValue(raid);
            var build = typeof(TidegateLoot).GetMethod("Build", PrivateInstance);
            foreach (object point in points) build.Invoke(raid, new object[] {point, player});
            if (waterFirst)
                foreach (var entry in SkyIslandRewardCrate.Boxes) if (waterIds.Contains(entry.Key)) entry.Value.Open();
            foreach (var box in SkyIslandRewardCrate.Boxes.Values) box.Open();
            foreach (var entry in SkyIslandRewardCrate.Boxes.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string id = entry.Key; var box = entry.Value; bool water = waterIds.Contains(id);
                string before = string.Join(",", box.Stocks.Select(s => s.Id + ":" + s.Amount));
                box.Open(); box.Open();
                Check(before == string.Join(",", box.Stocks.Select(s => s.Id + ":" + s.Amount)), "reopening rerolled " + id);
                var normal = box.Stocks.Where(s => s.Id < 500000).ToArray();
                var stream = water ? waterText : land;
                stream.Append(id).Append('=').Append(string.Join(",", normal.Select(s => s.Id + ":" + s.Amount))).Append(';');
                if (!water) continue;
                string kind = id.Split('_')[1];
                int maximum = kind == "vault" ? 6 : 5;
                if (id == "LOOT_safe_77") maximum++;
                Check(normal.Length <= maximum, "water prize added an extra slot " + id);
                foreach (var s in normal)
                {
                    Item item = ItemAssetsCollection.GetPrefab(s.Id);
                    int amount = Math.Min(item.MaxStackCount, s.Amount);
                    Check(item.Value <= 100000 && !LootBlacklistRegistry.Contains(s.Id), "illegal water prize " + s.Id);
                    Check(!item.Tags.Overlaps(new[] {"Special", "Quest", "Character", "DontDropOnDeadInSlot", "DestroyOnLootBox", "LockInDemo"}), "excluded water prize " + s.Id);
                    if (item.DisplayQuality >= DisplayQuality.Red) result.NativeRed += amount;
                    result.Value += (long)item.Value * amount; result.Stacks++;
                }
                // Include fixed/custom goods in the budget audit too; only the explicitly exempt watch is skipped.
                foreach (var s in box.Stocks)
                {
                    Item item = ItemAssetsCollection.GetPrefab(s.Id);
                    int amount = Math.Min(item.MaxStackCount, s.Amount);
                    bool premium = item.TypeID != BossRushItemIds.TidegateTideWatch && item.Quality >= 5;
                    if (!baseline)
                        Check((bool)typeof(TidegateLoot).GetMethod("CountsAsUnderwaterJackpot", BindingFlags.NonPublic | BindingFlags.Static)
                            .Invoke(null,new object[]{item}) == premium, "production budget mistook display color for numeric quality at " + id + " item=" + s.Id);
                    if (!premium) continue;
                    result.High += amount; high.Append(id).Append(':').Append(s.Id).Append(':').Append(amount).Append(';');
                    if (!baseline) Check(amount == 1, "jackpot stack exceeds one item");
                }
            }
            if (!baseline) Check(result.High <= 1, "underwater raid exceeded its one-item premium budget: " + result.High);
            Check(SkyIslandRewardCrate.Boxes["LOOT_vault_71"].Stocks.Count(s=>s.Id==BossRushItemIds.TidegateTideWatch)==1,
                "fixed story watch lost while reducing random prizes");
        }
        Check(InteractableLootbox.Subscribers == 0, "water raid leaked owner");
        result.Land = land.ToString(); result.Water = waterText.ToString(); result.HighItems = high.ToString();
        return result;
    }

    private static void AuditUnderwater(bool baseline)
    {
        const string authored = "ArtSource/Tidegate/export/tidegate_layout.json";
        if (File.Exists(authored))
        {
            var expected = JsonDocument.Parse(File.ReadAllText(authored)).RootElement;
            var sample = JsonDocument.Parse(File.ReadAllText("tests/fixtures/TidegateTreasure/HarborLayout.json")).RootElement;
            Func<JsonElement,string> signature = row => row.GetProperty("id").GetString() + ":" +
                string.Join(",", row.GetProperty("position").EnumerateArray().Select(v=>v.GetDouble().ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            Check(expected.GetProperty("containers").EnumerateArray().Select(signature).SequenceEqual(
                sample.GetProperty("containers").EnumerateArray().Select(signature)), "harbor fixture no longer matches authored container layout");
        }
        int hits = 0, zero = 0, highest = 0, native = 0; long value = 0, stacks = 0, highItems = 0;
        var land = new StringBuilder(); var water = new StringBuilder(); var landCargo = new StringBuilder();
        const int seeds = 2048;
        for (int seed = 0; seed < seeds; seed++)
        {
            WaterRun run = SearchHarbor(seed, false, false, baseline, seed >= 256);
            if (run.High > 0) hits++; else zero++;
            highest = Math.Max(highest, run.High); value += run.Value; native += run.NativeRed; stacks += run.Stacks; highItems += run.High;
            if (seed < 256) land.Append(seed).Append(':').Append(run.Land);
            water.Append(seed).Append(':').Append(run.Water);
            if (seed < 64)
            {
                WaterRun manifest = SearchHarbor(seed, false, true, baseline);
                WaterRun waterWinner = SearchHarbor(seed, false, true, baseline, false, true);
                landCargo.Append(seed).Append(':').Append(manifest.Land).Append('/').Append(waterWinner.Land);
                if (!baseline)
                {
                    WaterRun reversed = SearchHarbor(seed, true, false, false, true);
                    Check(run.Water == reversed.Water, "water contents depend on marker/build order seed=" + seed);
                    WaterRun reverseManifest = SearchHarbor(seed, true, true, false);
                    Check(run.HighItems == manifest.HighItems && run.HighItems == reverseManifest.HighItems && run.HighItems == waterWinner.HighItems,
                        "water premium roll depends on POI/build/open order or cargo winner seed=" + seed);
                }
            }
        }
        Console.WriteLine("Underwater budget: seeds=" + seeds + " hit=" + hits + " zero=" + zero + " max_items=" + highest
            + " high_items_per_raid=" + (double)highItems/seeds + " native_red_items=" + native + " value_per_raid=" + value/seeds + " stacks_per_raid=" + (double)stacks/seeds
            + " land256_sha256=" + Hash(land.ToString()) + " land_cargo64_sha256=" + Hash(landCargo.ToString()) + " water_sha256=" + Hash(water.ToString()));
        if (!baseline)
        {
            Check(Hash(land.ToString()) == "C688E16C66CE8FF29D0307A2B29976E26B5141852BF67611956BC820918A36F2",
                "land loot changed relative to the captured 2026-11-07 pre-budget production baseline");
            Check(Hash(landCargo.ToString()) == "BA8D38FCF4A0FDE19385D69229BAAD8A82F83CE60F81988F75DD12B7572ED183",
                "land cargo changed with a land/water winner relative to the captured pre-budget production baseline");
            Check(hits >= seeds * .77 && hits <= seeds * .83 && zero >= seeds * .17 && zero <= seeds * .23,
                "whole-raid premium probability differs from 80/20");
            Check(highest == 1, "premium never produced exactly one item");
            AuditBudgetPlanAndMissingStock();
        }
    }

    private static void AuditBudgetPlanAndMissingStock()
    {
        var planned = typeof(TidegateLoot).GetField("underwaterJackpotRolled", PrivateInstance);
        var target = typeof(TidegateLoot).GetField("underwaterJackpotPoint", PrivateInstance);
        var item = typeof(TidegateLoot).GetField("underwaterJackpotItem", PrivateInstance);
        var world = new TidegateWorld();
        foreach (var row in JsonDocument.Parse(File.ReadAllText("tests/fixtures/TidegateTreasure/HarborLayout.json"))
            .RootElement.GetProperty("containers").EnumerateArray())
            if (row.GetProperty("position")[1].GetSingle() < -.05f)
                world.LootMarkers.Add(new GameObject(row.GetProperty("id").GetString()).transform);
        var catalog = new TidegateLoot.PrizeCatalog();
        int hits = 0; var sites = new HashSet<string>();
        for (int seed = 0; seed < 10000; seed++)
            using (var raid = new TidegateLoot(world, new TidegateTide(), seed, () => true, (s,w) => {}, catalog))
            {
                if ((bool)planned.GetValue(raid))
                {
                    hits++; sites.Add((string)target.GetValue(raid));
                    Check((int)item.GetValue(raid) > 0, "winning plan has no actual item");
                }
                else Check((int)item.GetValue(raid) == 0 && target.GetValue(raid) == null, "losing plan still assigns a prize");
            }
        Check(hits >= 7800 && hits <= 8200 && sites.Count == 15, "production raid roll is biased or excludes underwater sites");
        Console.WriteLine("Production plan: 10000 seeds, hit=" + hits + " zero=" + (10000-hits) + " selected_sites=" + sites.Count);

        var original = ItemAssetsCollection.Items;
        var classify = typeof(TidegateLoot).GetMethod("CountsAsUnderwaterJackpot", BindingFlags.NonPublic | BindingFlags.Static);
        Check((bool)classify.Invoke(null,new object[]{original.Values.First(i=>i.Quality==5)}), "Q5 must consume the random budget");
        Check(!(bool)classify.Invoke(null,new object[]{ItemAssetsCollection.GetPrefab(BossRushItemIds.TidegateTideWatch)}), "fixed watch must remain exempt");
        try
        {
            ItemAssetsCollection.Items = original.Where(p => p.Value.Quality < 5).ToDictionary(p=>p.Key,p=>p.Value);
            SkyIslandLootPools.ResetStaticCaches(); Debug.Warnings.Clear();
            using (var raid = new TidegateLoot(world,new TidegateTide(),0,()=>true,(s,w)=>{}))
            {
                // Seed zero is a winning plan in the production stream; stock shortage must remain visible.
                Check((bool)planned.GetValue(raid) && (int)item.GetValue(raid) == 0, "missing-stock probe did not reach winning empty stock");
                Check(Debug.Warnings.Any(s=>s.Contains("UNDERWATER_JACKPOT_STOCK_UNAVAILABLE")), "missing premium stock silently downgraded");
            }
        }
        finally { ItemAssetsCollection.Items = original; SkyIslandLootPools.ResetStaticCaches(); }

        var colors = original.Values.ToDictionary(p=>p.TypeID,p=>p.DisplayQuality);
        try
        {
            foreach (var entry in original.Values) if (entry.Quality >= 2 && entry.Quality <= 4) entry.DisplayQuality = DisplayQuality.Red;
            var recolored = new TidegateLoot.PrizeCatalog();
            var pickWater = typeof(TidegateLoot.PrizeCatalog).GetMethod("PickWater", PrivateInstance);
            Check((int)pickWater.Invoke(recolored,new object[]{SkyIslandLootCategory.General,new Random(1),0}) > 0 &&
                (int)pickWater.Invoke(recolored,new object[]{SkyIslandLootCategory.General,new Random(1),4}) > 0,
                "display recoloring changed the Q2-Q4 underwater pool");
        }
        finally { foreach(var entry in original.Values) entry.DisplayQuality=colors[entry.TypeID]; }
    }

    private static void AuditReportedCache()
    {
        var classify = typeof(TidegateLoot).GetMethod("CountsAsUnderwaterJackpot", BindingFlags.NonPublic | BindingFlags.Static);
        Item source=ItemAssetsCollection.GetPrefab(BossRushItemIds.PortableSafeZoneDevice);
        Item egg=ItemAssetsCollection.GetPrefab(BossRushItemIds.RelicEgg);
        Item flippers=ItemAssetsCollection.GetPrefab(BossRushItemIds.TidegateFlippers);
        Check(source.Quality==4 && source.DisplayQuality==DisplayQuality.Red, "deployed fallback template snapshot changed");
        Check(egg.Quality==5 && egg.DisplayQuality==source.DisplayQuality, "real egg configurator/clone was not exercised");
        Check(flippers.Quality==4 && flippers.DisplayQuality==egg.DisplayQuality, "production flippers configuration changed Q4 or inherited display color");
        int[] reported={135,125,111,BossRushItemIds.TidegateFlippers};
        int actual=reported.Count(id=>(bool)classify.Invoke(null,new object[]{ItemAssetsCollection.GetPrefab(id)}));
        Check(actual==0,"reported zero-prize cache misclassified Q4/DQ6 flippers as Q5");
        foreach(DisplayQuality color in Enum.GetValues(typeof(DisplayQuality)))
        {
            Check(!(bool)classify.Invoke(null,new object[]{new Item{TypeID=1,Quality=4,DisplayQuality=color}}),"Q4 color changed numeric budget");
            Check((bool)classify.Invoke(null,new object[]{new Item{TypeID=1,Quality=5,DisplayQuality=color}}),"Q5 color evaded numeric budget");
        }
        WaterRun replay=SearchHarbor(-1327683468,false,false,false);
        var cache=SkyIslandRewardCrate.Boxes["LOOT_buried_50"];
        Check(replay.High==0 && cache.Stocks.Count==4 && cache.Stocks.Any(s=>s.Id==BossRushItemIds.TidegateFlippers),
            "reported seed no longer exercises a zero-prize four-slot cache containing flippers");
        Console.WriteLine("Reported cache replay: seed=-1327683468; trace=135,125,111,500114; numeric_high=0; "
            + "real clone chain=500058:Q4/DQ6 -> 500059:Q5/DQ6 -> 500114:Q4/DQ6; watch exemption unchanged");
    }
}
