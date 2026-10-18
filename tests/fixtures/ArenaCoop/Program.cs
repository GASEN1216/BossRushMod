using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;
internal static partial class Program
{
    private static int checks;
    private static void Check(bool value,string label) { checks++;if(!value)throw new Exception(label); }
    private static void Room()
    { BossRushCoop.Peers.Clear();BossRushCoop.Wire.Clear();for(int i=0;i<16;i++)BossRushCoop.Peers.Add(new TestPeer {Id=i==0?"host":"p"+i,Host=i==0});foreach(var peer in BossRushCoop.Peers)peer.BindScene();BossRushCoop.Current=BossRushCoop.Peers[0]; }
    private static void Main()
    {
        Room();var host=BossRushCoop.Current;var worlds=new List<WavesArenaRuntimeModule>();var owners=new List<ModBehaviour>();
        foreach(var peer in BossRushCoop.Peers) {BossRushCoop.Current=peer;var owner=new ModBehaviour();owners.Add(owner);worlds.Add(new WavesArenaRuntimeModule(owner));}
        BossRushCoop.Current=host;worlds[0].Begin();BossRushCoop.Flush();
        Check(worlds[0].Copies==16,"16 actual participants freeze at run start");
        for(int i=1;i<16;i++)Check(owners[i].Begins==1,"each guest begins own achievement session");
        BossRushCoop.Peers.Add(new TestPeer {Id="late"});worlds[0].Wave();Check(worlds[0].Copies==16,"late observer cannot add victory share");
        BossRushCoop.Peers[1].AssetId="slot-B";worlds[0].Fact("clear");BossRushCoop.Flush();
        Check(owners[0].Clears==1 && owners[0].Courier==1,"host shares production personal fact path");
        Check(owners[1].Clears==0,"paid cohort old asset cannot receive into new slot");
        for(int i=2;i<16;i++)Check(owners[i].Clears==1 && owners[i].Courier==1,"every participant retains personal clear and courier");
        BossRushCoop.Peers[1].AssetId="slot-A";worlds[0].Tick();BossRushCoop.Flush();Check(owners[1].Clears==1,"original slot return retries original personal clear");
        Check(worlds[0].Victory() && !worlds[0].Victory(),"world victory materialization is epoch-idempotent");
        worlds[0].QueueVictory(10);BossRushCoop.Flush();
        Check(worlds.TrueForAll(x=>x.Boxes==0),"personal victory receipt cannot ACK a merely scheduled shadow as a materialized reward");
        for(int i=0;i<16;i++)
        {
            BossRushCoop.Current=BossRushCoop.Peers[i];
            foreach(var shadow in VictoryRewardShadowCrateController.Pending.ToArray())if(shadow.Player==BossRushCoop.LocalPlayerId&&shadow!=null)shadow.Land();
        }
        BossRushCoop.Current=host;for(int i=0;i<3;i++){worlds[0].Tick();BossRushCoop.Flush();}
        Check(worlds.TrueForAll(x=>x.Boxes==1&&x.Quality==10&&x.PersonalBoxes[0].Inventory.Content.Count==10),"all 16 original participants receive one complete interactive 10-item crate each");
        worlds[0].InfiniteHellMode=true;BossRushCoop.Peers[1].AssetId="slot-B";worlds[0].Wave();
        Check(worlds[0].Copies==15,"wave cohort revalidates original participant assets");
        host.AssetId="slot-B";
        var boss=new CharacterMainControl {name="dragonking"};worlds[0].Boss(boss);worlds[0].Boss(boss);BossRushCoop.Flush();
        Check(owners[0].BossKills==0,"host native death entry cannot write old-run boss into switched slot");
        for(int i=2;i<16;i++)Check(owners[i].BossKills==1,"one death yields own guest boss fact once");
        host.AssetId="slot-A";worlds[0].Tick();BossRushCoop.Flush();worlds[0].Tick();BossRushCoop.Flush();
        Check(owners[0].BossKills==1,"host original slot receives frozen boss fact once after return");
        worlds[0].Fact("hell",100);BossRushCoop.Flush();Check(owners[15].Hell==100,"guest receives own infinite wave achievement");
        string old=worlds[0].Epoch;int oldSequence=worlds[0].RunSequence;worlds[0].Begin();BossRushCoop.Flush();
        int begins=owners[15].Begins;BossRushCoop.Current=BossRushCoop.Peers[15];
        worlds[15].Apply(new CoopArenaPersonal {Kind="begin",Epoch=old,RunSequence=oldSequence});Check(owners[15].Begins==begins,"old delayed start cannot reset new personal session");
        AchievementTracker.HasTakenDamage=false;worlds[15].Apply(new CoopArenaPersonal {Kind="clear",Epoch=old,Difficulty=1});
        Check(owners[15].Flawless==1 && !AchievementTracker.HasTakenDamage,"old delayed cumulative clear cannot borrow new-run flawless and restores current observation");
        foreach(var world in worlds)world.Dispose();
        Room();var modes=new List<ModeDRuntimeModule>();owners.Clear();host=BossRushCoop.Current;
        foreach(var peer in BossRushCoop.Peers){BossRushCoop.Current=peer;var owner=new ModBehaviour();owners.Add(owner);modes.Add(new ModeDRuntimeModule(owner));}
        BossRushCoop.Current=host;Check(modes[0].StartActual(),"Mode D real player entry commits its original host kit");modes[0].Tick();BossRushCoop.Flush();
        modes[0].modeDWaveIndex=10;modes[0].Freeze();modes[0].Complete();BossRushCoop.Flush();
        for(int i=0;i<16;i++)Check(owners[i].DFlawless==1 && owners[i].DClears==1,"Mode D threshold achievements remain personal for all 16");
        owners[0].IsModeDActive=true;var dArena=new WavesArenaRuntimeModule(owners[0]);
        host.AssetId="slot-B";boss=new CharacterMainControl {name="dragonking"};dArena.Boss(boss);dArena.Boss(boss);BossRushCoop.Flush();
        Check(owners[0].BossKills==0,"Mode D host native death entry also freezes its original asset");
        for(int i=1;i<16;i++)Check(owners[i].BossKills==1,"Mode D real arena death dispatch preserves each guest boss fact");
        modes[0].Tick();BossRushCoop.Flush();Check(modes[0].ItemPool.Kits==1,"Mode D host changed slot cannot receive another old starter");
        host.AssetId="slot-A";modes[0].Tick();BossRushCoop.Flush();Check(owners[0].BossKills==1 && modes[0].ItemPool.Kits==1,"Mode D original asset retries boss receipt and starter once");
        modes[0].Tick();BossRushCoop.Flush();Check(owners[0].BossKills==1 && modes[0].ItemPool.Kits==1,"Mode D repeat tick cannot duplicate personal grant");
        BossRushCoop.Peers[1].AssetId="slot-B";modes[0].modeDWaveIndex=11;modes[0].Freeze();modes[0].Complete();BossRushCoop.Flush();
        Check(owners[1].DClears==1 && owners[2].DClears==2,"Mode D next wave excludes switched asset while other players continue");
        dArena.Dispose();
        foreach(var mode in modes)mode.Dispose();

        Room();modes.Clear();owners.Clear();host=BossRushCoop.Current;
        foreach(var peer in BossRushCoop.Peers){BossRushCoop.Current=peer;var owner=new ModBehaviour();owners.Add(owner);modes.Add(new ModeDRuntimeModule(owner));}
        owners[15].Naked=false;modes[14].ItemPool.Fail=true;modes[14].ItemPool.FailDestroy=true;
        var kept=new ItemStatsSystem.Item {TypeID=500002,StackCount=3,Root=BossRushCoop.Peers[14].Character.CharacterItem};kept.Root.Inventory.Content.Add(kept);
        var originalRoot13=BossRushCoop.Peers[13].Character.CharacterItem;var newRoot13=new ItemStatsSystem.Item();
        var newAssetItem=new ItemStatsSystem.Item {TypeID=1254,StackCount=7,Root=newRoot13};newRoot13.Inventory.Content.Add(newAssetItem);
        modes[13].ItemPool.Callback=()=>{BossRushCoop.Peers[13].AssetId="slot-B";BossRushCoop.Peers[13].Character.CharacterItem=newRoot13;};
        BossRushCoop.Current=host;Check(modes[0].StartActual(),"Mode D host entry starts while guests validate their own naked inventory");
        string dEpoch=modes[0].Epoch;int dGeneration=modes[0].Generation;owners[0].Naked=false;
        Check(modes[0].StartActual()&&modes[0].Epoch==dEpoch&&modes[0].Generation==dGeneration&&modes[0].ItemPool.Kits==1,"same-scene repeated real entry cannot reset host world or grant another kit");
        modes[0].Tick();BossRushCoop.Flush();
        BossRushCoop.Current=BossRushCoop.Peers[12];BossRushCoop.Current.Bound=false;for(int i=0;i<50;i++)modes[12].Tick();
        Check(modes[12].PendingAdmission==1,"D pending native binding retains one periodic admission request across 50 retries");
        BossRushCoop.Current.Bound=true;modes[12].Tick();BossRushCoop.Flush();BossRushCoop.Current=host;modes[0].Snapshot();BossRushCoop.Flush();
        BossRushCoop.Current=BossRushCoop.Peers[12];int stableAttempts=BossRushCoop.SendAttempts;for(int i=0;i<50;i++)modes[12].Tick();
        Check(BossRushCoop.SendAttempts==stableAttempts&&modes[12].PendingAdmission==0,"D accepted host snapshot stops stable periodic admission reports");BossRushCoop.Current=host;
        Check(modes[15].ItemPool.Kits==0&&!modes[0].PendingFor("p15"),"non-naked personal decline is acknowledged without permanent pending starter");
        Check(modes[14].RollbackDebt==1&&modes[14].ItemPool.Last!=null&&!modes[14].ItemPool.Last.Destroyed,"failed partial-kit rollback retains exact object debt");
        Check(newRoot13.Inventory.Content.Count==1&&!newAssetItem.Destroyed&&originalRoot13.Inventory.Content.Count==0,"mid-starter asset switch removes only old character grant and preserves new asset inventory");
        modes[0].modeDWaveIndex=10;modes[0].Freeze();modes[0].Complete();BossRushCoop.Flush();
        Check(owners[15].DClears==0&&owners[14].DClears==0&&owners[13].DClears==0&&owners[12].DClears==1,"unadmitted and switched guests cannot inherit D wave threshold achievements");
        BossRushCoop.Current=host;modes[0].modeDWaveIndex=11;modes[0].Freeze();
        BossRushCoop.Current=BossRushCoop.Peers[14];modes[14].Tick();Check(modes[14].RollbackDebt==0&&modes[14].ItemPool.Last.Destroyed&&!kept.Destroyed&&kept.StackCount==3,"failed starter rollback retries and preserves full original item quantity");
        modes[14].ItemPool.Fail=false;Check(modes[14].StartActual(),"real player entry can retry after partial grant rollback");BossRushCoop.Flush();
        BossRushCoop.Current=BossRushCoop.Peers[15];owners[15].Naked=true;Check(modes[15].StartActual(),"rejected guest can explicitly retry after becoming naked");BossRushCoop.Flush();
        BossRushCoop.Current=BossRushCoop.Peers[13];Check(!modes[13].StartActual(),"new asset cannot retry original D starter");
        BossRushCoop.Peers[13].AssetId="slot-A";BossRushCoop.Peers[13].Character.CharacterItem=originalRoot13;modes[13].ItemPool.Callback=null;
        Check(modes[13].StartActual(),"original asset can retry after interrupted starter");BossRushCoop.Flush();
        BossRushCoop.Current=host;modes[0].Tick();BossRushCoop.Flush();modes[0].Complete();BossRushCoop.Flush();
        Check(owners[13].DClears==0&&owners[14].DClears==0&&owners[15].DClears==0,"mid-wave D admission cannot receive retroactive current-wave rewards");
        modes[0].modeDWaveIndex=12;modes[0].Freeze();modes[0].Complete();BossRushCoop.Flush();
        Check(owners[13].DClears==1&&owners[14].DClears==1&&owners[15].DClears==1,"D admission starts a full personal share at the next actual frozen wave");
        Check(modes[14].ItemPool.Kits==2&&modes[15].ItemPool.Kits==1&&modes[13].ItemPool.Kits==2,"retries replace failed grants once without duplicating successful kits");
        foreach(var mode in modes)mode.Dispose();
        Room();var deathOwner=new ModBehaviour();var deathArena=new WavesArenaRuntimeModule(deathOwner);host=BossRushCoop.Current;
        deathArena.Begin();host.Character.Health.IsDead=true;deathOwner.PlayerDied(host.Character.Health);
        Check(deathOwner.IsActive,"real Arena death entry preserves shared world for the 15 living original guests");
        host.AssetId="new-slot";host.Character=new CharacterMainControl();
        foreach(var peer in BossRushCoop.Peers)if(peer!=host)peer.Character.Health.IsDead=true;
        deathOwner.PlayerDied(BossRushCoop.Peers[15].Character.Health);
        Check(!deathOwner.IsActive,"real Arena death entry ends when all original participants die despite live host replacement asset");deathArena.Dispose();
        Room();deathOwner=new ModBehaviour {IsModeDActive=true};var deathD=new ModeDRuntimeModule(deathOwner);deathArena=new WavesArenaRuntimeModule(deathOwner);host=BossRushCoop.Current;
        Check(deathD.StartActual(),"D all-dead regression starts through actual kit transaction");deathD.Tick();BossRushCoop.Flush();
        host.Character.Health.IsDead=true;deathOwner.PlayerDied(host.Character.Health);Check(deathOwner.IsActive&&deathD.modeDActive,"real D death entry preserves guests' shared world after host death");
        host.AssetId="new-slot";host.Character=new CharacterMainControl();
        foreach(var peer in BossRushCoop.Peers)if(peer!=host)peer.Character.Health.IsDead=true;
        deathOwner.PlayerDied(BossRushCoop.Peers[15].Character.Health);
        Check(!deathOwner.IsActive&&!deathD.modeDActive,"real D death entry ends shared D when original cohort all dies with replacement host still alive");deathArena.Dispose();deathD.Dispose();
        Room();var delivery=new InfiniteHellMilestoneDelivery();var active=new ModBehaviour();
        delivery.Enqueue(1,new Vector3(),16);delivery.Enqueue(1,new Vector3(),1);
        for(int i=0;i<80;i++)delivery.Tick(active);
        Check(ItemStatsSystem.Probe.Crowns==16,"100-wave crown amount preserves each participant original share");
        Check(ItemStatsSystem.Probe.Cash+Duckov.Economy.EconomyManager.Money==160000000,"physical and account milestone cash preserve 16 full shares");
        Room();BossRushCoop.Current=BossRushCoop.Peers[1];var projectedArena=new WavesArenaRuntimeModule(new ModBehaviour());projectedArena.World(new CoopArenaState {Active=true,ArenaActive=true,RunSequence=1,Epoch="projection"});var replica=new CharacterMainControl();projectedArena.CurrentWaveBosses.Add(replica);projectedArena.Tick();Check(projectedArena.IsActive&&!replica.gameObject.Destroyed,"Arena original room keeps native projection and replica");BossRushCoop.Current.Connected=false;projectedArena.Tick();Check(!projectedArena.IsActive&&!projectedArena.ArenaActive&&projectedArena.CoopEntryMode==0&&replica==null,"Arena native stop retires active owner and destroys replica component with object");projectedArena.Tick();Check(projectedArena.CurrentWaveBosses.Count==0,"Arena former client does not inherit offline wave references");projectedArena.Dispose();
        Room();BossRushCoop.Current=BossRushCoop.Peers[1];var deferred=new WavesArenaRuntimeModule(new ModBehaviour());var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();int entered=0,clears=BossRushMapSelectionHelper.Clears;var awaiting=deferred.SetupCoopClientEntry(scene,()=>entered++,()=>entered++,()=>entered++);Check(awaiting.MoveNext(),"real original client entry waits for original host mode");BossRushCoop.Current.Connected=false;Check(!awaiting.MoveNext()&&entered==0&&BossRushMapSelectionHelper.Clears==clears,"native stop cancels original deferred entry without clearing newer same-scene prepaid flow");BossRushCoop.Current.Connected=true;var otherAwaiting=deferred.SetupCoopClientEntry(scene,()=>entered++,()=>entered++,()=>entered++);Check(otherAwaiting.MoveNext(),"original room reentry may wait again");BossRushCoop.Current.Run="room-new";Check(!otherAwaiting.MoveNext()&&entered==0&&BossRushMapSelectionHelper.Clears==clears,"new native room cancels old entry without running its callbacks or tail cleanup");deferred.Dispose();
        Room();BossRushCoop.Current=BossRushCoop.Peers[1];var entryOwner=new ModBehaviour();var entryArena=new WavesArenaRuntimeModule(entryOwner);
        var entryState=new CoopArenaState {ArenaActive=true,EntryMode=1};
        entryArena.World(entryState);Check(entryOwner.EntryRequests==1,"accepted native guest snapshot schedules actual scene entry without local pending map intent");
        entryArena.World(entryState);Check(entryOwner.EntryRequests==1,"repeated world snapshots do not cancel and restart scene entry");
        BossRushCoop.Current.Token="same-map-next";UnityEngine.SceneManagement.SceneManager.Handle=32;entryArena.World(entryState);
        Check(entryOwner.EntryRequests==2,"same map new native token and handle schedules its own facilities once");
        entryState.EntryMode=3;BossRushCoop.Current.Token="mode-e";entryArena.World(entryState);Check(entryOwner.EntryRequests==2,"E world does not initialize ordinary arena facilities");entryArena.Dispose();
        PersonalRewardRegression();
        Room();host=BossRushCoop.Current;worlds.Clear();owners.Clear();
        foreach(var peer in BossRushCoop.Peers){BossRushCoop.Current=peer;var o=new ModBehaviour();owners.Add(o);worlds.Add(new WavesArenaRuntimeModule(o));}
        BossRushCoop.Current=host;var sweeping=worlds[0];sweeping.Begin();sweeping.Wave();BossRushCoop.Flush();
        sweeping.InfiniteHellMode=true;sweeping.BossesPerWave=2;sweeping.BossesInCurrentWaveRemaining=2;
        var firstDead=new CharacterMainControl{name="dead-a"};firstDead.Health.IsDead=true;
        var secondDead=new CharacterMainControl{name="dead-b"};secondDead.Health.IsDead=true;
        sweeping.CurrentWaveBosses.Add(firstDead);sweeping.CurrentWaveBosses.Add(secondDead);sweeping.CurrentBoss=secondDead;
        host.AssetId="slot-new";BossRushCoop.Peers[1].AssetId="slot-new";
        sweeping.SweepSilentWaveBossDeaths(.2f);BossRushCoop.Flush();Check(sweeping.DefeatedEnemies==0,"silent sweep keeps quarter-second budget");
        sweeping.SweepSilentWaveBossDeaths(.05f);BossRushCoop.Flush();
        Check(sweeping.DefeatedEnemies==2&&sweeping.BossesInCurrentWaveRemaining==0&&sweeping.CurrentWaveBosses.Count==0&&sweeping.WaveAdvances==1,"silent wave bosses use actual normal membership, count and final advance");
        Check(sweeping.InfiniteHellCashPool==2000,"silent death retains both original max-health cash rewards");
        Check(owners[0].BossKills==0&&owners[1].BossKills==0&&owners[15].BossKills==2,"silent death publishes both real personal Boss receipts only to original assets");
        host.AssetId="slot-A";BossRushCoop.Peers[1].AssetId="slot-A";sweeping.Tick();BossRushCoop.Flush();
        Check(owners[0].BossKills==2&&owners[1].BossKills==2,"original assets recover each silent death receipt");
        sweeping.SweepSilentWaveBossDeaths(1);sweeping.Boss(firstDead);BossRushCoop.Flush();
        Check(sweeping.DefeatedEnemies==2&&sweeping.InfiniteHellCashPool==2000&&owners[15].BossKills==2,"late death callback and repeat sweep cannot double count cash or personal receipts");
        BossRushCoop.Current=BossRushCoop.Peers[2];var guestBoss=new CharacterMainControl();guestBoss.Health.IsDead=true;worlds[2].CurrentBoss=guestBoss;worlds[2].SweepSilentWaveBossDeaths(1);
        Check(worlds[2].DefeatedEnemies==0,"guest sweep cannot settle local native replicas");
        BossRushCoop.Current=host;owners[0].IsModeDActive=true;var modeDBoss=new CharacterMainControl();modeDBoss.Health.IsDead=true;sweeping.CurrentBoss=modeDBoss;sweeping.BossesPerWave=1;sweeping.SweepSilentWaveBossDeaths(1);
        Check(sweeping.DefeatedEnemies==2,"standard sweep cannot consume Mode D world deaths");
        foreach(var world in worlds)world.Dispose();
        Console.WriteLine("ArenaCoop: "+checks+" PASS; real Arena/D personal channels and frozen reward cohorts, fake engine materialization.");
    }
}
