using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using BossRush;
using Duckov.MiniMaps;
using ItemStatsSystem;

internal static partial class Program
{
    // Enumerate every possible final random index of the production picker. This uses the
    // actual stock/preferred-grade fallback, without recreating its filtering or price rules.
    private sealed class IndexRoll : Random
    {
        internal int Index, Size;
        public override int Next(int size) { Size=size; return Index%size; }
    }
    private static void AuditManifest()
    {
        var picker=typeof(TidegateLoot).GetMethod("PickPrize",BindingFlags.NonPublic|BindingFlags.Instance);
        Check(picker!=null,"manifest picker missing");
        var routes = new HashSet<string>();
        var layout=JsonDocument.Parse(File.ReadAllText("tests/fixtures/TidegateCargo/LayoutExcerpt.json")).RootElement;
        for(int seed=0;seed<48;seed++)
        {
            var world=new TidegateWorld();
            foreach(string group in new[]{"containers","pois"})
                foreach(var row in layout.GetProperty(group).EnumerateArray())
                {
                    bool poi=group=="pois";
                    var name=poi?"POI_"+row.GetProperty("name").GetString():row.GetProperty("id").GetString();
                    var t=new GameObject(name).transform; var p=row.GetProperty("position");
                    t.position=new Vector3(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle());
                    (poi?world.Pois:world.LootMarkers).Add(t);
                }
            var player=new GameObject("Player").AddComponent<CharacterMainControl>();
            player.transform.position=world.LootMarkers.Single(t=>t.name=="LOOT_house_0").position;
            Time.time=1; SkyIslandRewardCrate.Boxes.Clear();
            using(var raid=new TidegateLoot(world,new TidegateTide(),seed,()=>true,(s,w)=>{}))
            {
                if(seed==0)
                    foreach(var category in new[]{SkyIslandLootCategory.Medical,SkyIslandLootCategory.Workshop,SkyIslandLootCategory.Valuable})
                        for(int q=category==SkyIslandLootCategory.Valuable?5:4;q<=5;q++)
                        {
                            var items=new HashSet<int>(); var random=new IndexRoll();
                            do
                            {
                                int id=(int)picker.Invoke(raid,new object[]{category,q,q,q,random,category});
                                Check(id>0 && ItemAssetsCollection.GetPrefab(id).Quality==q && items.Add(id),category+" missing/duplicate Q"+q+" cargo");
                                random.Index++;
                            } while(random.Index<random.Size);
                            Console.WriteLine("Manifest "+category+" Q"+q+": "+items.Count+" eligible IDs");
                        }
                for(int i=0;i<=world.LootMarkers.Count && !SkyIslandRewardCrate.Boxes.ContainsKey("LOOT_house_0");i++)
                { raid.Tick(player); Time.time+=1; }
                SkyIslandRewardCrate.Boxes["LOOT_house_0"].Open();
                var pins=world.Root.transform.GetComponentsInChildren<SimplePointOfInterest>(true)
                    .Where(p=>p!=null && p.gameObject.activeSelf).Select(p=>p.Key).Distinct().OrderBy(k=>k).ToArray();
                Check(pins.Length==2,"official catalog did not provide two real manifest options");
                routes.Add(string.Join("+",pins));
            }
        }
        Check(routes.Count==3,"a real route pair never became available");
        Console.WriteLine("Manifest: 48 raids, all offered two routes; all 3 pairs seen.");
    }
}
