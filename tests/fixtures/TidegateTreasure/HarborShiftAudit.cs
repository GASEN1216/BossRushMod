using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BossRush;
using ItemStatsSystem;
using UnityEngine;

internal static partial class Program
{
    // 完整 78 箱，用真实出货路径核对普通点的总经济、主题组合、锁柜和单格占比；不含封签、Boss 与任务物。
    private static void AuditShift(bool baseline)
    {
        string[] kinds=File.ReadAllText("tests/fixtures/TidegateTreasure/HarborBoxTypes.txt").Trim().Split(' ');
        long street=0,town=0; int small=0,streetBoxes=0,lucky=0,kitBoxes=0,kitThemed=0,openerRepeats=0,sharedPremium=0;
        var fishOpeners=new HashSet<int>(); var roleTag=new Dictionary<string,string>{{"tool","Tool"},{"fish","Fish"},{"med","Medic"}};
        const int seeds=256;
        for(int seed=0;seed<seeds;seed++)
        {
            var world=new TidegateWorld(); Time.time=1; SkyIslandRewardCrate.Boxes.Clear();
            for(int i=0;i<kinds.Length;i++) world.LootMarkers.Add(new GameObject("LOOT_"+kinds[i]+"_"+i).transform);
            var player=new GameObject("duck").AddComponent<CharacterMainControl>();
            using(var raid=new TidegateLoot(world,new TidegateTide(),seed,()=>true,(s,w)=>{}))
            {
                for(int i=0;i<kinds.Length+2;i++) { raid.Tick(player);Time.time+=1; }
                int surprises=0; var openers=new Dictionary<string,HashSet<int>>(); var premium=new Dictionary<string,HashSet<int>>();
                for(int i=0;i<kinds.Length;i++)
                {
                    var box=SkyIslandRewardCrate.Boxes["LOOT_"+kinds[i]+"_"+i];box.Open();
                    var stock=box.Stocks.Where(s=>s.Id<500000).ToArray();
                    long value=stock.Sum(s=>(long)ItemAssetsCollection.GetPrefab(s.Id).Value*Math.Min(s.Amount,ItemAssetsCollection.GetPrefab(s.Id).MaxStackCount));
                    town+=value;
                    string boxId="LOOT_"+kinds[i]+"_"+i;
                    if(boxId=="LOOT_vault_71"||boxId=="LOOT_safe_77")premium[boxId]=new HashSet<int>(stock.Where(s=>ItemAssetsCollection.GetPrefab(s.Id).Quality>=6).Select(s=>s.Id));
                    bool ordinary=new[]{"house","fish","med","tool","crate"}.Contains(kinds[i]);
                    if(!ordinary) continue;
                    street+=value;streetBoxes++;if(stock.Length==1)small++;
                    if(stock.Any(s=>ItemAssetsCollection.GetPrefab(s.Id).Quality>=4))surprises++;
                    string tag;
                    // 工具柜 / 渔篓 / 药柜：开头对题（工具、鱼、药），一趟里同类箱的开头不重样；惊喜格替换开头的不计。
                    if(stock.Length>0 && roleTag.TryGetValue(kinds[i],out tag) && ItemAssetsCollection.GetPrefab(stock[0].Id).Quality<4)
                    {
                        kitBoxes++;
                        if(ItemAssetsCollection.GetPrefab(stock[0].Id).Tags.Contains(tag))kitThemed++;
                        HashSet<int> seen; if(!openers.TryGetValue(kinds[i],out seen))openers[kinds[i]]=seen=new HashSet<int>();
                        if(!seen.Add(stock[0].Id))openerRepeats++;
                        if(kinds[i]=="fish")fishOpeners.Add(stock[0].Id);
                    }
                }
                Check(baseline || surprises<=1,"more than one street jackpot in a raid");lucky+=surprises;
                HashSet<int> director,captain;
                if(premium.TryGetValue("LOOT_vault_71",out director)&&premium.TryGetValue("LOOT_safe_77",out captain)&&director.Overlaps(captain))sharedPremium++;
            }
        }
        Console.WriteLine("Harbor shift: 256 x "+kinds.Length+" boxes; total/raid="+town/seeds+" street/raid="+street/seeds
            +" single street="+small+"/"+streetBoxes+" lucky raids="+lucky+" kit themed="+kitThemed+"/"+kitBoxes
            +" opener repeats in raid="+openerRepeats+" distinct fish openers="+fishOpeners.Count+" director/captain shared premium raids="+sharedPremium);
        if(!baseline)
        {
            Check(small < streetBoxes*.16, "model capacity absent: too many single-stack street boxes");
            // 2026-11-07 实机：写死的班次单让一趟 7 只渔篓都以同一种沙丁鱼开头，所长柜和船长柜开出同一件精品。
            Check(kitThemed>=kitBoxes*.95, "kit opener lost its purpose (tool / fish / medicine)");
            Check(openerRepeats<=kitBoxes*.01, "same kind of box repeats its opener within one raid");
            Check(fishOpeners.Count>=8, "fish baskets keep opening with the same few fish");
            Check(sharedPremium<=seeds/100, "director and captain safes share a premium item");
            Check(lucky>=10 && lucky<=52, "jackpot absent or flooding the economy");
        }
    }
}
