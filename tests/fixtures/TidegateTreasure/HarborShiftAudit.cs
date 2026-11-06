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
        long street=0,town=0; int small=0,streetBoxes=0,paired=0,lucky=0;
        const int seeds=256;
        for(int seed=0;seed<seeds;seed++)
        {
            var world=new TidegateWorld(); Time.time=1; SkyIslandRewardCrate.Boxes.Clear();
            for(int i=0;i<kinds.Length;i++) world.LootMarkers.Add(new GameObject("LOOT_"+kinds[i]+"_"+i).transform);
            var player=new GameObject("duck").AddComponent<CharacterMainControl>();
            using(var raid=new TidegateLoot(world,new TidegateTide(),seed,()=>true,(s,w)=>{}))
            {
                for(int i=0;i<kinds.Length+2;i++) { raid.Tick(player);Time.time+=1; }
                int surprises=0;
                for(int i=0;i<kinds.Length;i++)
                {
                    var box=SkyIslandRewardCrate.Boxes["LOOT_"+kinds[i]+"_"+i];box.Open();
                    var stock=box.Stocks.Where(s=>s.Id<500000).ToArray();
                    long value=stock.Sum(s=>(long)ItemAssetsCollection.GetPrefab(s.Id).Value*Math.Min(s.Amount,ItemAssetsCollection.GetPrefab(s.Id).MaxStackCount));
                    town+=value;
                    bool ordinary=new[]{"house","fish","med","tool","crate"}.Contains(kinds[i]);
                    if(!ordinary) continue;
                    street+=value;streetBoxes++;if(stock.Length==1)small++;
                    if(stock.Any(s=>ItemAssetsCollection.GetPrefab(s.Id).Quality>=4))surprises++;
                    if(stock.Length>=2)
                    {
                        int a=stock[0].Id,b=stock[1].Id;
                        if ((a==332&&b==335)||(a==91&&b==30)||(a==1106&&(b==87||b==13))||(a==10&&b==20)||(a==17&&b==428))paired++;
                    }
                }
                Check(baseline || surprises<=1,"more than one street jackpot in a raid");lucky+=surprises;
            }
        }
        Console.WriteLine("Harbor shift: 256 x "+kinds.Length+" boxes; total/raid="+town/seeds+" street/raid="+street/seeds
            +" single street="+small+"/"+streetBoxes+" pairs="+paired+" lucky raids="+lucky);
        if(!baseline)
        {
            Check(small < streetBoxes*.16 && paired>1000, "model capacity or work kits absent");
            Check(lucky>=10 && lucky<=52, "jackpot absent or flooding the economy");
        }
    }
}
