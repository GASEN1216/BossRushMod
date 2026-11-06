using System;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using Duckov.MiniMaps;
using BossRush;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    private static void Invoke(object obj,string method){obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);}
    private static void Tick(TidegateSandBoss b,float time){Time.time=time;Invoke(b,"Update");}
    private static Vector3 P(float x){return new Vector3(x,0,0);}
    private static int Hits{get{return SkyIslandBossForge.Hits;}}
    private static void Main()
    {
        var prefab=new CharacterRandomPreset();TidegateSandBoss.Tune(prefab);
        Check(prefab.isBoss&&prefab.showName&&prefab.nameKey==TidegateText.MapBossKey&&prefab.forceTracePlayerDistance==0&&prefab.specialAttachmentBases.Count==0,"fixed identity / foreign skills");
        bool flat=true,valid=true;var root=new GameObject("raid");
        var boss=new GameObject("boss").AddComponent<CharacterMainControl>();boss.transform.SetParent(root.transform,false);
        var player=new GameObject("player").AddComponent<CharacterMainControl>();CharacterMainControl.Main=player;
        var ai=new AICharacterController();var tide=new TidegateTide();var bossOwner=boss.gameObject.AddComponent<TidegateSandBoss>();
        Action bind=()=>bossOwner.Bind(boss,ai,tide,new TidegateWorld(),P(30),P(0),()=>flat,()=>valid,TidegateSandBoss.SourceName);
        bind();bind();Check(boss.Subscribers==1&&bossOwner.OwnsDrop,"double bind");
        player.transform.position=P(2); Tick(bossOwner,0); Tick(bossOwner,4);
        Check(SkyIslandBossForge.Ring==null,"boss located a player it never saw");
        ai.searchedEnemy=player.mainDamageReceiver; Physics.Wall=true;Tick(bossOwner,5);
        Check(SkyIslandBossForge.Ring==null,"warning through wall");
        Physics.Wall=false;Tick(bossOwner,6);Check(SkyIslandBossForge.Ring.transform.position.x==0,"sweep followed the player");
        Tick(bossOwner,7.8f);Check(Hits==0,"early sweep");Tick(bossOwner,8.1f);
        Check(Hits==1&&SkyIslandBossForge.Last.x==0&&SkyIslandBossForge.LastRadius==3.6f,"sweep resolution");
        Tick(bossOwner,19);flat=false; Tick(bossOwner,20);Tick(bossOwner,22.5f);
        Check(Hits==1&&!bossOwner.OnFlats&&ai.Moves>0&&ai.patrolPosition.x==30,"tide failed to cancel sweep or order retreat");
        player.transform.position=P(14);Tick(bossOwner,24);Check(Hits==1&&!SkyIslandBossForge.Ring.gameObject.activeSelf,"attack before returning to deck");
        boss.transform.position=P(30);Tick(bossOwner,25);Check(SkyIslandBossForge.Ring.transform.position.x==14,"deck mark");
        player.transform.position=P(20); Tick(bossOwner,27);Check(Hits==1,"deck early hit");Tick(bossOwner,27.8f);
        Check(Hits==2&&SkyIslandBossForge.Last.x==14&&SkyIslandBossForge.LastRadius==2.15f,"winch followed target or hit twice");
        Tick(bossOwner,44); SkyIslandBossForge.Ring.enabled=false;Tick(bossOwner,47);
        Check(Hits==2,"invisible warning hurt");
        SkyIslandBossForge.Material=false;Tick(bossOwner,52);Tick(bossOwner,56);Check(Hits==2,"missing material hurt");
        SkyIslandBossForge.Material=true;Tick(bossOwner,61);tide.Deep=true;Tick(bossOwner,64);Check(Hits==2,"flooded mark hurt");
        tide.Deep=false; Tick(bossOwner,69);boss.Health.IsDead=true;Tick(bossOwner,72);Check(Hits==2,"dead boss finished attack");
        valid=false;boss.Die();Check(boss.CharacterItem.Inventory.Count==0,"reward after return");
        valid=true;InteractableLootboxInventoryHelper.During=boss.Die;boss.Die();boss.Die();
        Check(boss.CharacterItem.Inventory.Count==1&&boss.CharacterItem.Inventory[0].TypeID==500116,"doubled or missing corpse drop");
        Invoke(bossOwner,"OnDestroy");Check(boss.Subscribers==0,"death handler leak");
        // 带着响铊再来打：尸体里换成私货；私货目录不可用时照旧给响铊；身上没有响铊（第一次打倒）不换。
        Func<Func<int>,CharacterMainControl> fight=pick=>{var b=new GameObject("boss").AddComponent<CharacterMainControl>();b.transform.SetParent(root.transform,false);
            var o=b.gameObject.AddComponent<TidegateSandBoss>();o.Bind(b,new AICharacterController(),tide,new TidegateWorld(),P(30),P(0),()=>flat,()=>true,TidegateSandBoss.SourceName);
            o.OfferSpare(pick);InteractableLootboxInventoryHelper.During=null;b.Die();return b;};
        TidegateInventory.CarryingLead=true;
        var swapped=fight(()=>777);Check(swapped.CharacterItem.Inventory.Count==1&&swapped.CharacterItem.Inventory[0].TypeID==777,"carried lead still dropped a duplicate lead");
        var noStock=fight(()=>0);Check(noStock.CharacterItem.Inventory.Count==1&&noStock.CharacterItem.Inventory[0].TypeID==500116,"empty spare stock lost the lead");
        var unwired=fight(null);Check(unwired.CharacterItem.Inventory.Count==1&&unwired.CharacterItem.Inventory[0].TypeID==500116,"unwired spare lost the lead");
        TidegateInventory.CarryingLead=false;
        var first=fight(()=>777);Check(first.CharacterItem.Inventory.Count==1&&first.CharacterItem.Inventory[0].TypeID==500116,"first kill swapped the lead away");
        var model=new GameObject("model");first.characterModel=model.AddComponent<CharacterModel>();TidegateSandBoss.ApplyLook(first);
        Check(model.transform.localScale.x==TidegateSandBoss.ModelScale&&TidegateSandBoss.ModelScale>1.1f,"boss silhouette not enlarged");
        TidegateSandBoss.ApplyLook(new GameObject("bare").AddComponent<CharacterMainControl>());
        Check(prefab.health==TidegateSandBoss.BossHealth&&TidegateSandBoss.BossHealth==1000f,"boss health left the sky island chief floor");
        var sess=root.AddComponent<TidegateSession>();UnityEngine.Object.Session=sess;sess.root=root;sess.player=player;sess.loot=new TidegateLoot();sess.tide=tide;
        var use=root.AddComponent<TidegateLeadUsage>();
        foreach(var spec in new[]{("buried",21,true,false),("crate",20,false,false),("buried",20,false,true),("buried",26,false,false),("buried",45,false,false)})
        {var marker=new GameObject(spec.Item1);marker.transform.position=P(spec.Item2);sess.loot.points.Add(new TidegateLoot.Point{Kind=spec.Item1,Marker=marker.transform,Opened=spec.Item3,Failed=spec.Item4});}
        var lead=boss.CharacterItem.Inventory[0];use.Use(lead,boss);Check(SimplePointOfInterest.Last==null,"NPC used lead");
        tide.Deep=true;use.Use(lead,player);Check(SimplePointOfInterest.Last.transform.position.x==26&&sess.Message.Contains("underwater"),"lead skipped closest submerged unsearched point");
        var old=SimplePointOfInterest.Last;sess.loot.points[3].Opened=true;tide.Deep=false;use.Use(lead,player);
        Check(!old.gameObject.activeSelf&&SimplePointOfInterest.Last.transform.position.x==45&&sess.Message.Contains("exposed"),"reused lead left old marker or missed next cache");
        sess.loot.points[4].Opened=true;use.Use(lead,player);Check(sess.Message.Contains("No buried caches"),"exhausted caches");
        Console.WriteLine("PASS "+checks+" checks: telegraphed tide attacks, official corpse callback reentry, sounding lead discovery.");
    }
}
