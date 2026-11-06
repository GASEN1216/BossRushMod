using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BossRush;
using Duckov.MiniMaps;
using UnityEngine;
using Cat = BossRush.SkyIslandLootCategory;

internal static class Program
{
    private sealed class Marker
    {
        internal string Id, Kind;
        internal bool Fixed;
        internal Vector3 Position;
        internal Transform Spawn()
        {
            var t = new GameObject(Id).transform; t.position = Position; return t;
        }
    }
    private static List<Marker> places, boxes;
    private static readonly Dictionary<string, string[]> routes = new Dictionary<string, string[]>
    {
        { "Pier", new[] { "POI_栈桥", "crate", "Medical" } },
        { "Works", new[] { "POI_第三水厂", "tool", "Workshop" } },
        { "Flats", new[] { "POI_滩涂", "buried", "Valuable" } }
    };

    private static void Check(bool ok, string condition)
    { if (!ok) throw new Exception(condition); }

    private static List<Marker> Read(JsonElement root, bool containers)
    {
        return root.GetProperty(containers ? "containers" : "pois").EnumerateArray().Select(x =>
        {
            var p = x.GetProperty("position");
            return new Marker {
                Id = containers ? x.GetProperty("id").GetString() : "POI_" + x.GetProperty("name").GetString(),
                Kind = containers ? x.GetProperty("type").GetString() : "",
                Fixed = containers && x.GetProperty("fixed").GetArrayLength() > 0,
                Position = new Vector3(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle()) };
        }).ToList();
    }

    private sealed class Raid : IDisposable
    {
        internal readonly TidegateWorld World = new TidegateWorld();
        internal readonly CharacterMainControl Player = new GameObject("Player").AddComponent<CharacterMainControl>();
        internal readonly List<string> Notices = new List<string>();
        internal readonly TidegateLoot Loot;
        internal int OpenEvents;
        internal Raid(int seed, bool withRoutes = true)
        {
            Check(InteractableLootbox.Subscribers == 0, "previous raid left a global callback");
            SkyIslandRewardCrate.Boxes.Clear();
            World.LootMarkers.AddRange(boxes.OrderBy(x => x.Id,StringComparer.Ordinal).Select(x => x.Spawn()));
            if (withRoutes) World.Pois.AddRange(places.Select(x => x.Spawn()));
            Loot = new TidegateLoot(World, new TidegateTide(), seed, () => true, (s,w) => Notices.Add(s));
            Loot.Opened += id => OpenEvents++;
        }
        internal InteractableLootbox Box(string id)
        {
            Player.transform.position = boxes.First(p => p.Id == id).Position;
            for (int i = 0; i < boxes.Count + 1 && !SkyIslandRewardCrate.Boxes.ContainsKey(id); i++)
            { Time.time += 1; Loot.Tick(Player); }
            Check(SkyIslandRewardCrate.Boxes.ContainsKey(id), "walking to actual marker did not build " + id);
            return SkyIslandRewardCrate.Boxes[id];
        }
        internal SimplePointOfInterest[] Pins()
        { return World.Root.transform.GetComponentsInChildren<SimplePointOfInterest>(true).Where(c => c != null && c.gameObject.activeSelf).ToArray(); }
        internal void Visit(string id)
        {
            Player.transform.position = boxes.Single(p => p.Id == id).Position;
            Time.time += 1; Loot.Tick(Player);
        }
        internal Dictionary<string, string> Discover(int expected = 2)
        {
            Box(boxes.First(c => c.Kind == "house" && !c.Fixed).Id).Open();
            var pins = Pins();
            Check(pins.Length == expected && Notices.Count == 1 && Notices[0].Contains("manifest"), "ordinary search did not reveal available routes");
            Check(expected == 2 ? Loot.ManifestObjective.Contains("two") : Loot.ManifestObjective.Contains("shipment"), "objective did not describe available routes");
            var result = new Dictionary<string,string>();
            foreach (var pin in pins)
            {
                string key = pin.Key.Replace("BossRush_TidegateCargo_", "");
                string[] route = routes[key];
                var place = places.Single(p => p.Id == route[0]);
                var selected = boxes.Single(c => (c.Position - pin.transform.position).sqrMagnitude < .0001f);
                Check(pin.IsArea && pin.AreaRadius > 0 && !selected.Fixed && selected.Kind == route[1] &&
                      (selected.Position-place.Position).sqrMagnitude < 30*30, "map lead did not identify an eligible box for " + key);
                result.Add(key, selected.Id);
            }
            return result;
        }
        internal static int CargoCount()
        { return SkyIslandRewardCrate.Boxes.Values.Sum(b => b.Stocks.Count(s => s.Source == "TidegateCargo")); }
        internal static string Stock(InteractableLootbox box)
        { return string.Join(";",box.Stocks.Select(s => s.Source + ":" + s.Id + "x" + s.Amount).OrderBy(s => s,StringComparer.Ordinal)); }
        public void Dispose()
        {
            var components = World.Root.transform.GetComponentsInChildren<SimplePointOfInterest>(true);
            var crateComponents = SkyIslandRewardCrate.Boxes.Values.ToArray();
            Loot.Dispose(); Loot.Dispose();
            Check(components.All(c => c == null) && crateComponents.All(c => c == null) &&
                InteractableLootbox.Subscribers == 0 && Loot.PointCount == 0, "disposed owner left pin, crate or callback alive");
            UnityEngine.Object.Destroy(World.Root);
        }
    }

    private static readonly Cat[] CargoCategories = { Cat.Medical, Cat.Workshop, Cat.Valuable };
    private static int oneSlot;
    private static void SearchBothWays(int seed, Dictionary<string,string> leads)
    {
        foreach (var lead in leads)
        {
            string target = lead.Value, other = leads.Single(p => p.Key != lead.Key).Value;
            string baselineOther;
            int baselineCount;
            using (var baseline = new Raid(seed, false))
            {
                Check(baseline.Loot.ManifestObjective == null, "no routes offered an objective");
                baselineCount = baseline.Box(target).Stocks.Count;
                baselineOther = Raid.Stock(baseline.Box(other));
            }
            string winningSignature = null;
            for (int manifest = 1; manifest >= 0; manifest--)
            {
                using (var raid = new Raid(seed))
                {
                    Check(raid.Loot.ManifestObjective.Contains("ordinary"), "fresh expedition did not explain where the manifest is");
                    if (manifest == 1)
                    {
                        raid.Discover(); raid.Box(other);
                    }
                    var winner = raid.Box(target);
                    Check(Raid.CargoCount() == 0 && raid.Pins().Length == 2,
                        "building nearby boxes claimed cargo or did not warn before choice");
                    if (manifest == 0) Check(raid.Notices.Single().Contains("seal"), "direct approach did not explain the choice before opening");
                    winner.Open();
                    var cargo = winner.Stocks.Single(s => s.Source == "TidegateCargo");
                    var cat = (Cat)Enum.Parse(typeof(Cat),routes[lead.Key][2]);
                    int grade = ItemStatsSystem.ItemAssetsCollection.GetPrefab(cargo.Id).Quality;
                    Check(cargo.Amount == 1 && cargo.Id/10000 == (int)cat+1 && grade >= (cat == Cat.Valuable ? 5 : 4) && grade <= 5,
                        "route promised a different category or grade: " + lead.Key);
                    Check(winner.Stocks.Count == baselineCount, "shipment increased or reduced the number of stacks");
                    if (winner.Stocks.Count == 1) oneSlot++;
                    if (manifest == 1) winningSignature = Raid.Stock(winner);
                    else Check(Raid.Stock(winner) == winningSignature, "discovery order changed the shipment");
                    Check(raid.Pins().Length == 0 && raid.Loot.ManifestObjective == null, "obsolete cargo route or objective remained after claiming");
                    var loser = raid.Box(other); loser.Open();
                    Check(Raid.Stock(loser) == baselineOther, "unchosen box lost its ordinary stock or gave a second shipment");
                    int events = raid.OpenEvents, messages = raid.Notices.Count;
                    winner.Open(); loser.Open();
                    Check(events == raid.OpenEvents && raid.Notices.Count == messages && Raid.CargoCount() == 1,
                        "reopening crates duplicated stock, messages or events");
                    if (manifest == 0 && seed < 4)
                    {
                        foreach (Marker m in boxes) raid.Box(m.Id).Open();
                        Check(Raid.CargoCount() == 1, "searching the entire town paid the shipment twice");
                    }
                }
            }
        }
    }

    private static void CheckEbbCache()
    {
        var world=new TidegateWorld();
        Transform parent=world.Root.transform;
        foreach(string name in new[]{"Visual","VIS_containers","LOOT_buried_50"})
        {
            var part=new GameObject(name).transform;part.SetParent(parent,false);parent=part;
        }
        world.LootMarkers.Add(new GameObject("LOOT_buried_50").transform);
        var player=new GameObject("Duck").AddComponent<CharacterMainControl>();
        player.transform.position=new Vector3(100,0,0);
        var tide=new TidegateTide{Level=.5f}; Time.time=1;SkyIslandRewardCrate.Boxes.Clear();
        using(var owner=new TidegateLoot(world,tide,21,()=>true,(s,w)=>{}))
        {
            Check(!parent.gameObject.activeSelf && SkyIslandRewardCrate.Boxes.Count==0,"buried mesh visible before streaming");
            tide.Level=.1f;owner.Tick(player);Time.time++;
            Check(parent.gameObject.activeSelf && SkyIslandRewardCrate.Boxes.Count==0,"ebb did not reveal distant mud cache");
            player.transform.position=new Vector3(5,0,0);owner.Tick(player);Time.time++;
            var box=SkyIslandRewardCrate.Boxes["LOOT_buried_50"];
            Check(box.gameObject.activeSelf,"exposed cache not searchable");
            box.Open();int count=box.Stocks.Count;
            tide.Level=.6f;owner.Tick(player);Time.time++;
            Check(!parent.gameObject.activeSelf && !box.gameObject.activeSelf,"reflood left visual or box active");
            tide.Level=.1f;owner.Tick(player);box.Open();
            Check(parent.gameObject.activeSelf && box.gameObject.activeSelf && count==box.Stocks.Count,"ebb refilled searched cache");
        }
    }

    private static void NoDelivery(bool missingPrefabs)
    {
        if (!missingPrefabs) SkyIslandLootPools.Missing.UnionWith(CargoCategories);
        ItemStatsSystem.ItemAssetsCollection.NoCargoPrefabs = missingPrefabs;
        try
        {
            using (var raid = new Raid(42))
            {
                foreach (Marker m in boxes) raid.Box(m.Id).Open();
                Check(!raid.Notices.Any(n => n.Contains("manifest") || n.Contains("seal") || n.Contains("shipment")) && raid.Pins().Length == 0 && Raid.CargoCount() == 0 &&
                      raid.Loot.ManifestObjective == null, "unavailable official items left an unfulfillable offer");
            }
        }
        finally { ItemStatsSystem.ItemAssetsCollection.NoCargoPrefabs = false; SkyIslandLootPools.Missing.Clear(); }
    }
    private static void Failures()
    {
        NoDelivery(false); NoDelivery(true);
        foreach (Cat allowed in CargoCategories)
        {
            SkyIslandLootPools.Missing.UnionWith(CargoCategories.Where(c => c != allowed));
            try
            {
                using (var raid = new Raid(5))
                {
                    var lead = raid.Discover(1).Single();
                    Check(routes[lead.Key][2] == allowed.ToString(), "partial outage substituted another category");
                    raid.Box(lead.Value).Open();
                    Check(Raid.CargoCount() == 1 && raid.Loot.ManifestObjective == null, "single offer was not redeemable");
                }
            }
            finally { SkyIslandLootPools.Missing.Clear(); }
        }
        SkyIslandLootPools.Missing.Add(Cat.Medical);
        ItemStatsSystem.ItemAssetsCollection.ThrowCategory = (int)Cat.Workshop + 1;
        try
        {
            using (var raid = new Raid(5))
                Check(raid.Discover(1).Single().Key == "Flats", "one broken lookup suppressed another healthy route");
        }
        finally { ItemStatsSystem.ItemAssetsCollection.ThrowCategory = -1; SkyIslandLootPools.Missing.Clear(); }

        const int seed = 42;
        Dictionary<string,string> leads;
        using (var raid = new Raid(seed)) leads = raid.Discover();
        var ids = leads.Values.ToArray();
        string baseline;
        using (var raid = new Raid(seed, false)) baseline = Raid.Stock(raid.Box(ids[0]));
        foreach (bool buildFails in new[] { false, true })
        {
            using (var raid = new Raid(seed))
            {
                raid.Discover();
                if (buildFails)
                {
                    SkyIslandRewardCrate.FailBuild = ids[0];
                    try { raid.Visit(ids[0]); } finally { SkyIslandRewardCrate.FailBuild = null; }
                    Check(!SkyIslandRewardCrate.Boxes.ContainsKey(ids[0]), "failure injection missed the target crate");
                }
                else
                {
                    var target = raid.Box(ids[0]);
                    SkyIslandRewardCrate.RejectCargo = true;
                    try { target.Open(); } finally { SkyIslandRewardCrate.RejectCargo = false; }
                    Check(Raid.Stock(target) == baseline, "failed cargo insert did not return the originally rolled slot");
                }
                Check(raid.Pins().Length == 1 && !raid.Loot.ManifestObjective.Contains("two") && Raid.CargoCount() == 0,
                    "late failure eliminated a healthy alternate route");
                raid.Box(ids[1]).Open();
                Check(raid.Pins().Length == 0 && raid.Loot.ManifestObjective == null && Raid.CargoCount() == 1,
                    "alternative did not resolve cleanly after a late failure");
            }
        }
        using (var raid = new Raid(seed))
        {
            raid.Discover();
            SkyIslandRewardCrate.RejectCargo = true;
            try { foreach (string id in ids) raid.Box(id).Open(); }
            finally { SkyIslandRewardCrate.RejectCargo = false; }
            Check(Raid.CargoCount() == 0 && raid.Pins().Length == 0 && raid.Loot.ManifestObjective == null, "both failed offers remained on the map");
        }
    }
    private static void Main()
    {
        string path = "ArtSource/Tidegate/export/tidegate_layout.json";
        if (!File.Exists(path))
        {
            Console.WriteLine("SKIP: full private author export unavailable; using checked-in real marker excerpt.");
            path = "tests/fixtures/TidegateCargo/LayoutExcerpt.json";
        }
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(path)))
        { places = Read(doc.RootElement,false); boxes = Read(doc.RootElement,true); }
        Check(boxes.Count(c => c.Fixed) >= 6, "expected all fixed authors' containers");
        using (var raid = new Raid(42))
        {
            foreach (Marker m in boxes.Where(c => c.Fixed)) raid.Box(m.Id).Open();
            Check(!raid.Notices.Any() && raid.Pins().Length == 0, "story and shipwreck safes offered cargo");
        }
        var pairs = new HashSet<string>();
        var sites = routes.Keys.ToDictionary(key => key,key => new HashSet<string>());
        const int seeds = 48;
        for (int seed = 0; seed < seeds; seed++)
        {
            Dictionary<string,string> leads;
            using (var raid = new Raid(seed)) leads = raid.Discover();
            pairs.Add(string.Join("+", leads.Keys.OrderBy(k => k,StringComparer.Ordinal)));
            foreach (var lead in leads) sites[lead.Key].Add(lead.Value);
            SearchBothWays(seed, leads);
        }
        Check(pairs.Count == 3, "some pair of authored routes never appeared");
        foreach (var route in routes)
        {
            var center = places.Single(p => p.Id == route.Value[0]).Position;
            int eligible = boxes.Count(p => p.Kind == route.Value[1] && !p.Fixed && (p.Position-center).sqrMagnitude < 30*30);
            Check(sites[route.Key].Count >= Math.Min(2,eligible), "offer always used the same box despite alternatives");
        }
        Check(oneSlot > 0, "fixture never exercised an initially empty one-slot shipment box");
        Failures();
        CheckEbbCache();
        // 独立于装船货：没有读货单或追封箱，轮班钥匙仍能开柜；重入不会保留开锁状态。
        if (!boxes.Any(b=>b.Id=="LOOT_safe_35"))
            foreach (var row in new[]{"LOOT_safe_35","LOOT_tool_3","LOOT_tool_4"})
                if (!boxes.Any(b=>b.Id==row)) boxes.Add(new Marker {Id=row,Kind=row.Split('_')[1],Position=new Vector3(boxes.Count*2,0,0)});
        for (int seed=0;seed<8;seed++)
            using (var raid=new Raid(seed,false))
            {
                var cabinet=raid.Box("LOOT_safe_35");
                Check(!cabinet.gameObject.activeSelf,"locked cabinet was lootable on arrival");
                int before=raid.OpenEvents;
                cabinet.Open();
                Check(raid.OpenEvents==before,"ignored lock via early loot event");
                raid.Box("LOOT_tool_3").Open(); raid.Box("LOOT_tool_4").Open();
                Check(cabinet.gameObject.activeSelf && raid.Notices.Count(n=>n.Contains("A shift key"))==1,"missing/repeated shift key");
                int stacks=cabinet.Stocks.Count;
                cabinet.Open();cabinet.Open();
                Check(stacks==cabinet.Stocks.Count && raid.Notices.Count(n=>n.Contains("stays in the lock"))==1,"lock reused its key or refilled");
            }
        Console.WriteLine("PASS: " + boxes.Count + " authored containers, " + seeds + " seeds, all three route pairs and " + oneSlot +
            " one-stack wins; both choices and discovery orders, same normal losing stocks, exclusive cargo, official failures and cleanup.");
    }
}
