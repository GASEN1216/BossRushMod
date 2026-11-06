using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using BossRush;
using ItemStatsSystem;

internal static partial class Program
{
    // Markers present in the shipped layout; leave POIs empty to isolate non-manifest stock.
    private static readonly Dictionary<string,int> Floors = new Dictionary<string,int> {
        {"LOOT_house_0",0}, {"LOOT_crate_39",0}, {"LOOT_safe_68",4}, {"LOOT_buried_51",4},
        {"LOOT_wreck_73",4}, {"LOOT_locker_72",4}, {"LOOT_vault_65",5}, {"LOOT_vault_71",6}, {"LOOT_safe_77",6}
    };
    private const int Raids = 2048;
    private static void Check(bool ok, string message) { if(!ok) throw new Exception(message); }
    private sealed class Stats
    {
        internal List<long> totals = new List<long>(), prizePrices = new List<long>();
        internal Dictionary<int,int> prizes = new Dictionary<int,int>();
        internal int has4, has6, has7, qsum, stacks;
        internal StringBuilder fingerprint = new StringBuilder();
        internal void Collect(string id, int floor, List<Stack> loot, bool baseline)
        {
            var stock = loot.Where(s=>s.Id<500000).ToList();
            Check(stock.Count>0,id+" unexpectedly empty");
            Check(loot.Any(s=>s.Id==BossRushItemIds.TidegateHandwheel)==(id=="LOOT_locker_72"),"handwheel identity");
            Check(loot.Any(s=>s.Id==BossRushItemIds.TidegateTideWatch)==(id=="LOOT_vault_71"),"watch identity");
            int best=0; long total=0;
            foreach(var s in stock)
            {
                var m=ItemAssetsCollection.GetPrefab(s.Id);
                Check(m!=null && m.Value<=SkyIslandLootTables.MaxPoolItemValue && !LootBlacklistRegistry.Contains(s.Id), id+" illegal "+s.Id);
                Check(!m.Tags.Overlaps(new[]{"Character","Quest","Special","DestroyOnLootBox","DontDropOnDeadInSlot","LockInDemo"}),id+" excluded tag "+s.Id);
                best=Math.Max(best,m.Quality); total+=(long)m.Value*Math.Min(m.MaxStackCount,s.Amount);
                qsum+=m.Quality; stacks++;
                fingerprint.Append(s.Id).Append(':').Append(s.Amount).Append(',');
            }
            fingerprint.Append(';'); totals.Add(total);
            if(best>=4)has4++; if(best>=6)has6++; if(best>=7)has7++;
            var prizeStack = id=="LOOT_safe_77" ? stock.Single(s=>s.Source=="TidegateWreck") : stock[0];
            var prize=ItemAssetsCollection.GetPrefab(prizeStack.Id);
            if(!baseline && floor>0)
            {
                Check(prize.Quality>=floor && prize.Value>0,id+" quality floor missed, id="+prize.TypeID+" Q="+prize.Quality);
                Check(prize.CanBeSold && prize.Value>=(floor==6 ? 7500 : floor==5 ? 2500 : 1000),
                    id+" low value or unsellable prize: "+prize.TypeID);
                Check(!prize.Tags.Overlaps(new[]{"Key","SpecialKey"}),id+" off-map access prize: "+prize.TypeID);
            }
            prizePrices.Add(prize.Value);
            if(!prizes.ContainsKey(prize.TypeID))prizes[prize.TypeID]=0;
            prizes[prize.TypeID]++;
        }
        private static long P(List<long> scores, double fraction) { return scores.OrderBy(x=>x).ElementAt((int)Math.Floor((scores.Count-1)*fraction)); }
        internal void Print(string id)
        {
            string hash=Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(fingerprint.ToString()))).Substring(0,12);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0}  box￥ mean={1:F0} p10={2} min={3}; meanQ={4:F2} boxQ4+/6+/7+={5:F1}%/{6:F1}%/{7:F1}%; prize￥ min/p10/median={8}/{9}/{10}; unique={11} top={12} hash={13}",
                id,totals.Average(),P(totals,.10),P(totals,0),(double)qsum/stacks,100.0*has4/Raids,100.0*has6/Raids,100.0*has7/Raids,
                P(prizePrices,0),P(prizePrices,.10),P(prizePrices,.50),prizes.Count,
                string.Join(" ",prizes.OrderByDescending(p=>p.Value).Take(3).Select(p=>p.Key+":"+p.Value)),hash));
        }
    }
    private static void Main()
    {
        bool baseline = Environment.GetEnvironmentVariable("TIDEGATE_MEASURE_BASELINE")=="1";
        ItemAssetsCollection.Load(); var stats=Floors.Keys.ToDictionary(id=>id,id=>new Stats());
        for(int seed=0;seed<Raids;seed++)
        {
            Time.time=1;
            var world=new TidegateWorld();
            var player=new GameObject("Player").AddComponent<CharacterMainControl>();
            foreach(var id in Floors.Keys) world.LootMarkers.Add(new GameObject(id).transform);
            SkyIslandRewardCrate.Boxes.Clear();
            using(var raid=new TidegateLoot(world,new TidegateTide(),seed,()=>true,(s,w)=>{}))
            {
                for(int i=0;i<Floors.Count+2;i++) { raid.Tick(player); Time.time+=1; }
                Check(SkyIslandRewardCrate.Boxes.Count==Floors.Count, "markers not materialized");
                foreach(var f in Floors)
                {
                    var box=SkyIslandRewardCrate.Boxes[f.Key];
                    int count=box.Stocks.Count;
                    box.Open(); box.Open(); raid.Tick(player);
                    Check(box.Stocks.Count==count, f.Key+" refilled on second open");
                    stats[f.Key].Collect(f.Key,f.Value,box.Stocks,baseline);
                }
                Check(raid.OpenedCount==Floors.Count, "double-opened progression");
            }
            Check(InteractableLootbox.Subscribers==0,"raid leaked loot-open handler");
        }
        Console.WriteLine(Raids+" seeds per marker, official catalog "+ItemAssetsCollection.Items.Count+" items; totals exclude fixed mod items.");
        foreach(var e in stats) e.Value.Print(e.Key);
        if(!baseline)
        {
            AuditManifest();
            foreach(var e in stats)
            {
                int floor = Floors[e.Key];
                if (floor==0) Check(e.Value.has4==0,"unearned treasure in everyday supply "+e.Key);
                if (floor==6)
                {
                    Check(e.Value.prizes.Count>=7 && e.Value.prizes.Values.Max()<Raids/2,
                        e.Key+" dominated by one prize across repeat visits");
                    Check(e.Value.has7>Raids/10 && e.Value.has7<Raids/2,e.Key+" rare grade is absent or commonplace");
                }
            }
            Check(stats["LOOT_vault_65"].totals.Average()>stats["LOOT_wreck_73"].totals.Average() &&
                stats["LOOT_wreck_73"].totals.Average()>stats["LOOT_house_0"].totals.Average(),
                "stock value no longer tracks route risk");
        }
    }
}
