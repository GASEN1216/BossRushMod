using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string message) { checks++;if(!value)throw new Exception(message); }
    public static int Main()
    {
        ContactRegression.Run(Check);
        Time.time=1;BossRushCoop.Players.Clear();var players=new CharacterMainControl[16];var group=new GameObject();
        for(int i=0;i<16;i++) { players[i]=new CharacterMainControl();players[i].Parent(group.transform);players[i].transform.position=new Vector3(.5f,0,0);BossRushCoop.Players.Add(new BossRushCoopPlayer { Character=players[i] }); }
        CharacterMainControl.Main=new CharacterMainControl();CharacterMainControl.Main.Health.IsDead=true;
        var controller=new DragonKingAbilityController();var detector=new DragonKingCollisionDetector { gameObject=new GameObject() };detector.Initialize(controller);
        // 真 Update 是引擎边界，反射调用当作宿主的一帧；目标和冷却判定完整执行生产方法。
        var update=typeof(DragonKingCollisionDetector).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        update.Invoke(detector,null);Check(controller.ContactHits.Count==16,"dead host cannot stop all sixteen guest body contacts");
        Time.time=1.1f;update.Invoke(detector,null);Check(controller.ContactHits.Count==16,"multiple body checks obey each player's cooldown");
        Time.time=1.6f;update.Invoke(detector,null);Check(controller.ContactHits.Count==32,"all body contacts resume together after cooldown");

        var beam=new SunBeamDamageTrigger();beam.Initialize(controller);var process=typeof(SunBeamDamageTrigger).GetMethod("ProcessCollision",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        for(int i=0;i<16;i++) { process.Invoke(beam,new object[]{players[i].gameObject.Colliders[0]});process.Invoke(beam,new object[]{players[i].gameObject.Colliders[0]}); }
        foreach(var actor in players)Check(actor.Health.Hits==1 && actor.Health.Damage==8,"beam owns actual touched player, multiplied damage and per-player duplicate cooldown");
        BossSkillDamageRules.Dodging.Add(players[0]);Time.time=2;
        foreach(var actor in players)process.Invoke(beam,new object[]{actor.gameObject.Colliders[0]});
        Check(players[0].Health.Hits==1 && players[1].Health.Hits==2,"dodge immunity is checked on each touched player");
        var stranger=new CharacterMainControl();process.Invoke(beam,new object[]{stranger.gameObject.Colliders[0]});Check(stranger.Health.Hits==0,"nonparticipant collider cannot become a player beam target");
        BossRushCoop.IsHost=false;Time.time=3;process.Invoke(beam,new object[]{players[1].gameObject.Colliders[0]});update.Invoke(detector,null);
        Check(players[1].Health.Hits==2 && controller.ContactHits.Count==32,"client visual helpers never apply authoritative beam or contact damage");

        BossRushCoop.IsHost=true;BossSkillDamageRules.Dodging.Clear();var zone=new DragonKingLavaZone();
        var outside=players[15];outside.transform.position=new Vector3(8,0,0); // 同一 COOP parent 容器，必须只扫该角色自己的碰撞器。
        int[] before=new int[16];for(int i=0;i<16;i++)before[i]=players[i].Health.Hits;
        DragonKingLavaZone.Pulse();for(int i=0;i<15;i++)Check(players[i].Health.Hits==before[i]+1 && players[i].Buffs==1,"lava reaches every individual player once and burns them");
        Check(outside.Health.Hits==before[15] && outside.Buffs==0,"common parent cannot let another player's collider place outside player in lava");
        DragonKingLavaZone.Pulse();Check(players[0].Health.Hits==before[0]+1,"lava periodic cooldown survives another world tick");
        BossRushCoop.Players[1].IsAlive=false;Time.time=3.25f;DragonKingLavaZone.Pulse();
        Check(players[0].Health.Hits==before[0]+2 && players[1].Health.Hits==before[1]+1,"dead participant is independently excluded from periodic lava");
        BossRushCoop.IsHost=false;Time.time=4;DragonKingLavaZone.Pulse();Check(players[0].Health.Hits==before[0]+2,"client never executes lava damage");
        BossRushCoop.IsHost=true;zone.Close();Check(DragonKingLavaZone.GeometryCount==0,"last pooled zone releases all player collider cache references");

        for(int i=0;i<16;i++) { BossRushCoop.Players[i].IsAlive=true;players[i].transform.position=new Vector3(.5f,0,0);before[i]=players[i].Health.Hits; }
        BossSkillDamageRules.Dodging.Add(players[0]);
        Check(controller.Projectile(new Vector3(.5f,1,0)),"manual projectile can hit an actual guest when nearest snapshot is absent");
        Check(players[0].Health.Hits==before[0] && players[1].Health.Hits==before[1]+1 && players[2].Health.Hits==before[2],"single projectile passes dodger and consumes only first actual nondodging hit");
        Check(controller.Projectile(new Vector3(.5f,1,0),true),"persistent rainbow checks all touched players");
        for(int i=1;i<16;i++)Check(players[i].Health.Hits==before[i]+(i==1?2:1),"persistent rainbow cannot shield co-located room members");
        var lance=new GameObject();lance.transform.position=new Vector3(.5f,1,0);
        Check(controller.Lance(lance) && players[1].Health.Hits==before[1]+3,"lance evaluates actual target and preserves dodge immunity");
        CharacterMainControl.Main=players[3];var wave=new DragonKingShockwaveEffect();
        Check(wave.LocalTarget()==players[3],"replayed shockwave chooses this endpoint's local body rather than nearest network mirror");
        DragonKingAbilityController.Faction=true;Check(wave.LocalTarget()==null,"faction mode shockwave still spares spectators");
        DragonKingAbilityController.Faction=false;BossRushCoop.IsHost=false;
        Check(!controller.Projectile(new Vector3(.5f,1,0),true) && !controller.Lance(lance),"client projectile helpers cannot authorize damage or consume shared projectiles");
        BossRushCoop.IsHost=true;BossSkillDamageRules.Dodging.Clear();

        DragonKingAbilityController.Faction=true;var aiZone=new DragonKingLavaZone();Time.time=5;
        var hits=new List<Collider>();var enemies=new List<CharacterMainControl>();
        for(int i=0;i<40;i++) { var enemy=new CharacterMainControl { Team=Teams.player };enemies.Add(enemy);hits.Add(enemy.gameObject.Colliders[0]);hits.Add(enemy.gameObject.Colliders[0]); }
        hits.Add(players[0].gameObject.Colliders[0]);Physics.Hits=hits.ToArray();Physics.Queries=0;
        DragonKingLavaZone.Pulse();foreach(var enemy in enemies)Check(enemy.Health.Hits==1,"AI faction lava deduplicates repeated colliders without truncating large roster");
        Check(Physics.Queries>=2 && players[0].Health.Hits==before[0],"full nonalloc query grows and spectators remain exempt in faction mode");
        aiZone.Close();DragonKingAbilityController.Faction=false;BossRushCoop.IsConnected=false;
        var solo=new CharacterMainControl { IsMainCharacter=true };CharacterMainControl.Main=solo;solo.transform.position=new Vector3();Time.time=6;
        var soloZone=new DragonKingLavaZone();DragonKingLavaZone.Pulse();Check(solo.Health.Hits==1,"no-COOP single-player lava preserves official path");soloZone.Close();
        var leakedZone=new DragonKingLavaZone();DragonKingLavaZone.Pulse();int damageBeforeReset=solo.Health.Hits;
        DragonKingLavaZone.ResetStaticCaches();Time.time=7;DragonKingLavaZone.Pulse();
        Check(DragonKingLavaZone.GeometryCount==0 && solo.Health.Hits==damageBeforeReset,"module static-cache cleanup removes active pooled zones as well as cached character references");
        var witchRealm=new PhantomWitchBossCurseRealmRuntime();var witchTargets=new List<CharacterMainControl>();hits.Clear();
        for(int i=0;i<16;i++) { var actor=new CharacterMainControl();witchTargets.Add(actor);for(int j=0;j<4;j++)hits.Add(actor.gameObject.Colliders[0]); }
        var friendly=new CharacterMainControl { Team=Teams.wolf };hits.Add(friendly.gameObject.Colliders[0]);Physics.Hits=hits.ToArray();Physics.Queries=0;
        witchRealm.Pulse();foreach(var actor in witchTargets)Check(actor.Health.Hits==1 && actor.Buffs==1,"witch shared realm reaches all 16 players with multiple colliders and one curse each");
        Check(Physics.Queries>=3 && friendly.Health.Hits==0,"witch query grows beyond 32 while keeping faction allies immune");
        var full=new Collider[600];for(int i=0;i<full.Length;i++)full[i]=witchTargets[0].gameObject.Colliders[0];Physics.Hits=full;
        Collider[] query=new Collider[32];Check(PhantomWitchAbilityController.QueryDamageReceivers(new Vector3(),5,ref query)==600,"dense AI query uses complete fallback instead of a capped 512-target result");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;Physics.Hits=hits.ToArray();witchRealm.Pulse();
        Check(witchTargets[15].Health.Hits==1,"client realm cannot authorize a second damage decision");
        BossRushCoop.IsConnected=false;CharacterMainControl.Main=players[0];Time.time=30;
        CoopAiPredictionHealthProtection.Denied.Add(controller.BossForTest);
        int contacts=controller.ContactHits.Count,oldDamage=players[0].Health.Hits;
        update.Invoke(detector,null);process.Invoke(beam,new object[]{players[0].gameObject.Colliders[0]});controller.Knock(players[0]);
        Check(controller.ContactHits.Count==contacts && players[0].Health.Hits==oldDamage,"old denied entity cannot upgrade to offline beam/contact authority after Stop");
        Check(!controller.Projectile(new Vector3(.5f,1,0),true) && !controller.Lance(lance),"old entity cannot upgrade projectile/lance damage after Stop");
        var oldZone=new DragonKingLavaZone();CoopAiPredictionHealthProtection.Denied.Add(oldZone.BossForTest);DragonKingLavaZone.Pulse();oldZone.Close();
        Check(players[0].Health.Hits==oldDamage,"old lava owner remains denied while facade has offline authority");
        CoopAiPredictionHealthProtection.Denied.Add(witchRealm.CasterForTest);witchRealm.Pulse();
        Check(witchTargets[15].Health.Hits==1,"old witch realm cannot upgrade after Stop");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="birth-room";
        var spawnLease=new CoopBossCreationLease();Check(spawnLease.IsValid(),"original host may create a world boss");
        BossRushCoop.RunId="";Check(spawnLease.IsValid(),"empty same-room handshake preserves in-flight factory");
        BossRushCoop.RunId="birth-room";BossRushCoop.IsConnected=false;Check(!spawnLease.IsValid(),"real Stop cancels in-flight boss factory before it resumes as offline");
        BossRushCoop.IsConnected=true;BossRushCoop.RunId="birth-room";Check(!spawnLease.IsValid(),"revoked factory lease never resurrects on reconnect");
        var roomLease=new CoopBossCreationLease();BossRushCoop.RunId="replacement-room";Check(!roomLease.IsValid(),"in-flight boss factory cannot migrate to a new room");
        BossRushCoop.IsHost=false;Check(!new CoopBossCreationLease().IsValid(),"client never admits a world boss factory");
        BossRushCoop.IsConnected=false;var soloLease=new CoopBossCreationLease();Check(soloLease.IsValid(),"new no-COOP boss factory retains normal solo behavior");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;Check(!soloLease.IsValid(),"solo factory cannot continue as a host world summon after joining a room");
        BossRushCoop.IsConnected=false;Check(!soloLease.IsValid(),"revoked solo factory cannot resume after leaving the joined room");
        var soloToGuest=new CoopBossCreationLease();BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;
        Check(!soloToGuest.IsValid(),"solo factory cannot continue as client-created AI after joining a room");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;BossRushCoop.RunId="wave-room";CharacterMainControl.Main=players[0];
        var personalWave=new DragonKingShockwaveEffect();personalWave.BindScopeForTest();int pushes=players[0].Pushes;personalWave.KnockForTest();
        Check(players[0].Pushes==pushes+1,"valid host-replayed shockwave preserves actual local player's official force move");
        BossRushCoop.IsConnected=false;personalWave.KnockForTest();Check(players[0].Pushes==pushes+1,"Stop revokes previous world shockwave's personal force without promoting it to solo");
        BossRushCoop.IsConnected=true;BossRushCoop.RunId="wave-room";personalWave.KnockForTest();Check(players[0].Pushes==pushes+1,"revoked local shockwave cannot resurrect even when reconnecting to the same room");
        BossRushCoop.IsConnected=true;BossRushCoop.RunId="wave-other";personalWave.KnockForTest();Check(players[0].Pushes==pushes+1,"previous world shockwave cannot move player in a replacement room");
        BossRushCoop.RunId="wave-room";CharacterMainControl.Main=new CharacterMainControl();personalWave.KnockForTest();
        Check(players[0].Pushes==pushes+1 && CharacterMainControl.Main.Pushes==0,"new body cannot inherit old shockwave force lease");
        BossRushCoop.IsConnected=false;
        var descendant=new DragonDescendantAbilityController();var victim=new CharacterMainControl();descendant.CollideForTest(victim);
        Check(victim.Health.Hits==1,"fresh solo descendant retains actual collision Hurt endpoint");
        CoopAiPredictionHealthProtection.Denied.Add(descendant.BossForTest);descendant.CollideForTest(victim);
        Check(victim.Health.Hits==1,"old descendant cannot promote to offline collision damage after Stop");
        Console.WriteLine("PASS DragonKingCoopWorld: "+checks+" checks, 16-player contacts/beam/lava, ownership, per-player cooldown, authority, collider cache cleanup and 40 AI targets");return 0;
    }
}
