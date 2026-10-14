using System;
using BossRush;
using UnityEngine;

internal static class WorldRegression
{
    private static void Check(bool value,string label) { if(!value)throw new Exception(label); }
    private static CharacterMainControl Add(string id,float x,float z=0)
    {
        var actor=new CharacterMainControl { Id=id };actor.transform.position=new Vector3(x,0,z);
        BossRushCoop.Players[id]=new BossRushCoopPlayer { Id=id,Character=actor };return actor;
    }
    internal static void Run()
    {
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;BossRushCoop.RunId="sky-original";
        var oldScope=new SkyIslandSession();oldScope.BindScope();
        BossRushCoop.RunId="";oldScope.SessionChanged();Check(oldScope.ScopeValid && oldScope.Cleanups==0,"sky original trip survives an empty same-room handshake");
        BossRushCoop.RunId="sky-original";BossRushCoop.IsConnected=false;oldScope.SessionChanged();
        Check(!oldScope.ScopeValid && oldScope.Cleanups==1,"true Stop immediately cleans old sky trip before offline authority can tick");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="sky-new";
        Check(!oldScope.ScopeValid,"old client trip cannot promote after reconnect as new host");
        var hostScope=new SkyIslandSession();hostScope.BindScope();BossRushCoop.RunId="sky-third";hostScope.SessionChanged();
        Check(!hostScope.ScopeValid && hostScope.Cleanups==1,"new nonempty room revokes old host trip");
        BossRushCoop.IsConnected=false;var soloScope=new SkyIslandSession();soloScope.BindScope();
        Check(soloScope.ScopeValid,"new standalone sky trip remains usable without COOP");
        BossRushCoop.Players.Clear();BossRushCoop.Requests.Clear();BossRushCoop.Handlers.Clear();BossRushCoop.Broadcasts.Clear();
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";BossRushCoop.LocalAssetId="asset-1";
        Time.time=Time.unscaledTime=Time.realtimeSinceStartup=0;BossRushCoop.SceneGeneration=4;BossRushCoop.SessionReady=true;
        SkyIslandSession.Prepared=SkyIslandSession.Joined=0;var owner=new ModBehaviour();
        using(var travel=new SkyIslandCoopRuntime(owner))
        {
            SkyIslandSceneReferenceBridge.NativePrefix(false);
            Check(owner.Session==null,"ordinary native map cannot acquire an island session");
            SkyIslandSceneReferenceBridge.NativePrefix(true);SkyIslandSceneReferenceBridge.NativePrefix(true);
            Check(owner.Session!=null && SkyIslandSession.Joined==1 && SkyIslandSession.Prepared==0,"native late arrival prepares exactly once before travel identity arrives");
            var barrier=owner.Session.Barrier("island:sky.runtime");BossRushCoop.Marks=0;BossRushCoop.BarrierReady=true;
            Check(barrier.MoveNext() && BossRushCoop.Marks==0,"arrival waits for authenticated travel identity");
            var data=new SkyIslandCoopRuntime.Travel { Seed=123,Id="trip",Generation=4,InRaid=true };
            BossRushCoop.Emit("sky.travel.start",data,false);Check(travel.TravelId==null,"non-host cannot replace travel identity");
            BossRushCoop.Emit("sky.travel.start",data);BossRushCoop.Emit("sky.travel.start",data);
            Check(travel.TravelId=="trip" && travel.Seed==123 && SkyIslandSession.Joined==1,"duplicate current-generation announcement binds prepared native arrival without second session");
            Check(barrier.MoveNext() && BossRushCoop.Marks==0,"previous map generation cannot release arrival barrier");
            BossRushCoop.SceneGeneration=5;BossRushCoop.BarrierReady=false;
            Check(barrier.MoveNext() && BossRushCoop.Marks==1,"fresh generation marks readiness but waits for the real party barrier");
            BossRushCoop.BarrierReady=true;Check(!barrier.MoveNext(),"party readiness completes arrival");
            Check(!owner.Session.Barrier("sky.prepare").MoveNext(),"late arrival skips expired departure preparation barrier");
            BossRushCoop.IsHost=true;travel.Tick();Check(BossRushCoop.Broadcasts.Count==1,"host refreshes travel snapshot in current generation");
            var replay=(SkyIslandCoopRuntime.Travel)BossRushCoop.Broadcasts[0].Payload;
            Check(replay.InRaid && replay.Id=="trip" && replay.Generation==4,"late-join refresh preserves seed/identity/departure generation");
            travel.Tick();Check(BossRushCoop.Broadcasts.Count==1,"travel refresh is throttled");
            owner.Session.CoopTravelActive=false;Time.unscaledTime=1;travel.Tick();Check(BossRushCoop.Broadcasts.Count==1,"returning session cannot start another arrival");

            var local=Add("guest",SkyIslandBossRules.CaveCenterX,SkyIslandBossRules.CaveCenterZ);CharacterMainControl.Main=local;
            var effects=new SkyIslandCoopWorldEffects();long heard=0;CharacterMainControl heardFrom=null;
            BossRushCoop.IsHost=false;effects.WorldPacket(2);effects.WorldPacket(1);effects.WorldPacket(2);
            Check(effects.WorldRevision==2,"duplicate and reordered world snapshots cannot rewind a removed telegraph");
            effects.WorldPacket(3,false);effects.WorldPacket(3,true,"former-travel");effects.WorldPacket(0);
            Check(effects.WorldRevision==2,"world snapshot requires actual host, current travel and positive revision");
            effects.WorldPacket(4);Check(effects.WorldRevision==4,"newer full snapshot still advances world presentation");
            BossRushCoop.IsHost=true;
            Action<CharacterMainControl,long> hear=(actor,noise)=>{ heard+=noise;heardFrom=actor; };
            SkyIslandCoopWorldEffects.PlayerGunNoise+=hear;
            effects.Receive("guest",effects.Snapshot(8,true));effects.Receive("guest",effects.Snapshot(8,true));effects.Receive("guest",effects.Snapshot(3,false));
            Check(heard==8 && heardFrom==local,"duplicate and reordered gun totals count each sound once");
            BossRushCoop.LocalPlayerId="host";Check(SkyIslandCoopWorldEffects.WorldEarmuffs,"guest earmuffs affect shared telegraphs even when host does not wear them");
            BossRushCoop.Players["guest"].IsAlive=false;Check(!SkyIslandCoopWorldEffects.WorldEarmuffs,"dead guest cannot keep shared equipment benefit active");
            effects.Receive("guest",effects.Snapshot(10,true));Check(heard==8,"dead player's delayed shots cannot cast rockfall");
            BossRushCoop.Players["guest"].IsAlive=true;
            effects.Receive("guest",effects.Snapshot(2,true,"stream-2"));effects.Receive("guest",effects.Snapshot(50,true));
            Check(heard==10,"reconnected stream resets count and retired packets cannot revive older stream");
            BossRushCoop.Players["guest"].AssetId="asset-2";
            effects.Receive("guest",effects.Snapshot(100,true,"stream-2"));Check(heard==10 && !SkyIslandCoopWorldEffects.WorldEarmuffs,"old save slot cannot contribute sound or equipment");
            effects.Receive("guest",effects.Snapshot(2,false,"stream-3","asset-2"));Check(heard==12,"current slot starts its own sound stream");
            BossRushCoop.Players["guest"].AssetId="asset-1";BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";
            BossRushCoop.Requests.Clear();SkyIslandBossGearWorn.Earmuffs=true;effects.Shot(local);
            BossRushCoop.IsHost=true;effects.Receive("guest",BossRushCoop.Requests[0].Payload);
            Check(heard==13,"earmuff wearer contributes half the normal gun noise");
            BossRushCoop.IsHost=false;SkyIslandBossGearWorn.Earmuffs=false;effects.Shot(local);
            BossRushCoop.IsHost=true;effects.Receive("guest",BossRushCoop.Requests[1].Payload);Check(heard==15,"ordinary shot contributes full noise without repeated previous shot");
            SkyIslandCoopWorldEffects.PlayerGunNoise-=hear;

            BossRushCoop.LocalPlayerId="host";SkyIslandBossGearWorn.RootweavePieces=0;
            effects.Receive("guest",effects.Snapshot(20,false,"root-stream","asset-1",2));
            Check(SkyIslandRewardCrate.BonusPieces(true)==2 && SkyIslandRewardCrate.BonusPieces(false)==0,"guest two-piece bonus applies to one shared inventory while host personal reward retains its own gear");
            Add("guest-two",15);effects.Receive("guest-two",effects.Snapshot(21,false,"root-second","asset-1",2));
            Check(SkyIslandRewardCrate.BonusPieces(true)==2,"two wearers cannot multiply the shared box bonus");
            BossRushCoop.Players["guest"].IsAlive=false;BossRushCoop.Players["guest-two"].AssetId="switched";
            Check(SkyIslandRewardCrate.BonusPieces(true)==0,"dead wearer and another save slot cannot improve a new shared box");
            BossRushCoop.Players["guest"].IsAlive=true;
            effects.Receive("guest",effects.Snapshot(22,false,"root-stream","asset-1",4));
            Check(SkyIslandRewardCrate.BonusPieces(true)==2,"invalid equipment count cannot replace authenticated state");
            effects.Receive("guest",effects.Snapshot(23,false,"root-stream","asset-1",0));
            Check(SkyIslandRewardCrate.BonusPieces(true)==0,"taking off the set releases the shared bonus");

            BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";Time.time=0;effects.AddLocalSlow("finite",2);effects.AddLocalSlow("body",0);
            Check(effects.SlowCount==2 && RuntimeStatModifierTracker.Active[local]==4,"independent slows each own two real modifier records");
            Time.time=3;effects.Expire();Check(effects.SlowCount==1 && RuntimeStatModifierTracker.Active[local]==2,"expired slow removes records and its cache entry");
            CharacterMainControl.Main=new CharacterMainControl();effects.Expire();
            Check(effects.SlowCount==0 && RuntimeStatModifierTracker.Active[local]==0,"respawn body replacement releases permanent old-body slow");
            var newBody=CharacterMainControl.Main;
            effects.SlowPacket("ordered",100,-.3f);effects.SlowPacket("ordered",101,0);effects.SlowPacket("ordered",100,-.3f);
            Check(effects.SlowCount==0 && RuntimeStatModifierTracker.Active[newBody]==0,"late start cannot reapply a released slow");
            effects.SlowPacket("wrong-slot",102,-.3f,2,"other-slot");
            Check(effects.SlowCount==0,"old save asset cannot slow the current character");
        }
        Check(BossRushCoop.Handlers.Count==0 && SkyIslandCoopRuntime.Current==null,"travel owner disposal unregisters both handlers");

        BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="host";BossRushCoop.Players.Clear();BossRushCoop.Personal.Clear();Time.time=0;Physics.BlockedX.Clear();
        var engine=new SkyIslandBellEngineMoves();var actors=new CharacterMainControl[16];
        for(int i=0;i<16;i++)actors[i]=Add("p"+i,10);
        // 同点 16 人：每人只能吃一次，脚下爆炸不能再误伤另外 15 人。
        engine.Strike(new Vector3());foreach(var actor in actors)Check(actor.mainDamageReceiver.Hits==1 && actor.mainDamageReceiver.Total==SkyIslandBellEngineMoves.FarDamage,"all 16 players receive exactly one annulus hit");
        Check(BossRushCoop.Personal.Count==16 && SkyIslandBossForge.Detonations==0,"each remote player has individual slow and no repeated co-located explosions");
        engine.Expire(3);Check(BossRushCoop.Personal.Count==32,"all independent remote slows release on expiry");
        BossRushCoop.Players.Clear();BossRushCoop.Personal.Clear();
        var safe=Add("safe",2);var outer=Add("outer",21);var high=Add("high",11);high.transform.position=new Vector3(11,3,0);
        var blocked=Add("wall",12);Physics.BlockedX.Add(12);var valid=Add("valid",15);var dead=Add("dead",16);BossRushCoop.Players["dead"].IsAlive=false;
        engine.Strike(new Vector3());Check(valid.mainDamageReceiver.Hits==1 && safe.mainDamageReceiver.Hits==0 && outer.mainDamageReceiver.Hits==0 && high.mainDamageReceiver.Hits==0 && blocked.mainDamageReceiver.Hits==0 && dead.mainDamageReceiver.Hits==0,"annulus inner/outer/height/wall/death boundaries are evaluated per player");
        BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;CharacterMainControl.Main=valid;engine.Strike(new Vector3());
        Check(SkyIslandBossForge.Detonations==1,"no-COOP single player retains official explosion settlement");
    }
}
