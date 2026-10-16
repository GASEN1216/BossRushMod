using System;
using System.Reflection;
using BossRush;
using UnityEngine;

internal static class ContactRegression
{
    private static CharacterMainControl host,guest;
    private static DragonKingCoopContactRuntimeModule server,client;
    private static void Host() { BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="host";CharacterMainControl.Main=host; }
    private static void Guest() { BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="guest";CharacterMainControl.Main=guest; }
    private static BossRushCoop.Sent Last() { return BossRushCoop.Outbox[BossRushCoop.Outbox.Count-1]; }
    private static BossRushCoop.Sent ExchangeBody()
    {
        var request=Last();Guest();BossRushCoop.Emit(request,true);var body=Last();
        Host();BossRushCoop.Emit(body,false);server.OnUpdate(0,.01f);return body;
    }
    internal static void Run(Action<bool,string> check)
    {
        BossRushCoop.Players.Clear();BossRushCoop.Outbox.Clear();BossRushCoop.Handlers.Clear();
        BossRushCoop.IsConnected=true;BossRushCoop.RunId="contact-room";BossRushCoop.SceneGeneration=3;
        BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="contact-core";BossRushCoop.LocalAssetId="asset";
        Time.time=Time.unscaledTime=1;Time.deltaTime=.05f;
        guest=new CharacterMainControl();guest.transform.position=new Vector3(1,0,0);
        host=new CharacterMainControl();host.transform.position=new Vector3(10,0,0);
        var member=new BossRushCoopPlayer { Id="guest",Character=guest };
        BossRushCoop.Players.Add(member);BossRushCoop.Players.Add(new BossRushCoopPlayer { Id="host",Character=host });
        ModBehaviour.Instance=new ModBehaviour { gameObject=new GameObject() };
        Guest();client=new DragonKingCoopContactRuntimeModule();client.OnAwake(null);
        for(int i=0;i<100;i++) { Time.unscaledTime+=.25f;client.OnUpdate(0,.25f); }
        check(BossRushCoop.Outbox.Count==0,"idle client has no contact owner and publishes no 4 Hz body traffic");
        Host();server=new DragonKingCoopContactRuntimeModule();server.OnAwake(null);
        var controller=new DragonKingAbilityController();controller.Knock(guest);
        check(Last().Topic=="dragon.contact.request" && guest.Pushes==0,"first contact requests receiver life permission without moving host mirror");
        var firstBody=ExchangeBody();var hit=Last();
        check(firstBody.Json.Contains("\"Token\"") && firstBody.Json.Contains("\"Revision\""),"actual shared codec serializes body identity");
        check(hit.Target=="guest" && hit.Topic=="BossRush.Personal.DragonKing.Contact","handshake releases frozen guest push");
        Guest();BossRushCoop.Emit(hit,false);check(guest.Pushes==0,"untrusted sender cannot push guest");
        BossRushCoop.Emit(hit,true);BossRushCoop.Emit(hit,true);
        check(guest.Pushes==1 && Math.Abs(guest.LastPush.sqrMagnitude-64)<.001f,"real receipt dedup preserves one full official impulse");
        Host();controller.Knock(host);check(host.Pushes==1,"host local push uses same actual receipt callback");
        controller.Knock(guest);var staleBodyHit=Last();
        guest=new CharacterMainControl();guest.transform.position=new Vector3(1,0,0);member.Character=guest;
        Guest();client.OnUpdate(0,.01f);var freshBody=Last();BossRushCoop.Emit(staleBodyHit,true);
        check(guest.Pushes==0,"replacement body rejects old life receipt");
        Host();BossRushCoop.Emit(freshBody,false);BossRushCoop.Emit(firstBody,false);controller.Knock(guest);hit=Last();
        Guest();BossRushCoop.Emit(hit,true);check(guest.Pushes==1,"reordered old body cannot replace current life");
        Host();controller.Knock(guest);var oldHit=Last();Guest();guest.Health.IsDead=true;BossRushCoop.Emit(oldHit,true);
        guest.Health.IsDead=false;client.OnUpdate(0,.01f);BossRushCoop.Emit(oldHit,true);
        check(guest.Pushes==1,"death and same-object revival revoke old push");
        freshBody=Last();Host();BossRushCoop.Emit(freshBody,false);controller.Knock(guest);hit=Last();
        Guest();BossRushCoop.LocalAssetId="other-slot";BossRushCoop.Emit(hit,true);check(guest.Pushes==1,"wrong asset cannot consume impulse");
        BossRushCoop.LocalAssetId="asset";BossRushCoop.SceneGeneration=4;BossRushCoop.Emit(hit,true);check(guest.Pushes==1,"same-map new generation rejects old push");
        BossRushCoop.SceneGeneration=3;BossRushCoop.RunId="other-room";BossRushCoop.Emit(hit,true);check(guest.Pushes==1,"new room rejects old push");
        BossRushCoop.RunId="contact-room";BossRushCoop.HasBoundScene=false;BossRushCoop.Emit(hit,true);check(guest.Pushes==1,"unbound scene rejects old push");
        BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="next-core";client.OnUpdate(0,.01f);freshBody=Last();
        Host();BossRushCoop.Emit(freshBody,false);controller.Knock(guest);hit=Last();
        Guest();BossRushCoop.Emit(hit,true);check(guest.Pushes==2,"new actual scene handshake restores knockback");

        Host();controller.Knock(guest);var pausedHit=Last();Time.unscaledTime+=5;
        Guest();BossRushCoop.Emit(pausedHit,true);check(guest.Pushes==2,"long compatibility pause expires an immediate contact on receiver clock");
        Host();controller.Knock(guest);check(Last().Topic=="dragon.contact.request","expired host body cache requests fresh permission");
        var oldBody=freshBody;freshBody=ExchangeBody();hit=Last();Guest();BossRushCoop.Emit(pausedHit,true);BossRushCoop.Emit(hit,true);
        check(guest.Pushes==3,"new permission cannot resurrect old same-life contact");
        Host();BossRushCoop.Emit(oldBody,false);
        var sand=new SandstormChampionMinionArts();int before=guest.Pushes;
        check(sand.Hit(guest,new Vector3(10,0,0)) && guest.Pushes==before,"actual sand Strike damages world target but never forces host mirror");
        hit=Last();Guest();BossRushCoop.Emit(hit,true);check(guest.Pushes==before+1 && Math.Abs(guest.LastPush.x-10)<.001f,"sand Strike delivers original force on victim client");
        BossSkillDamageRules.Dodging.Add(guest);ModBehaviour.Instance.Frame();check(guest.Pushes==before+1,"0.14s force respects dodge each frame");
        BossSkillDamageRules.Dodging.Remove(guest);ModBehaviour.Instance.Frame();
        check(guest.Pushes==before+2 && guest.LastPush.x>0 && guest.LastPush.x<10,"sand push keeps original linear decay after dodge");
        ModBehaviour.Instance.Frame();check(ModBehaviour.Instance.Routines.Count==0,"sand push ends after original 0.14 seconds");

        Host();var descendant=new DragonDescendantAbilityController();host.Health.IsDead=true;
        check(descendant.TargetForTest()==guest,"actual descendant refresh targets alive guest after host death");
        check(descendant.CanCompleteRocket(LevelManager.Instance,1),"actual delayed rocket gate accepts selected guest");
        SceneLoader.IsSceneLoading=true;check(!descendant.CanCompleteRocket(LevelManager.Instance,1),"rocket still cancels on real loading boundary");SceneLoader.IsSceneLoading=false;
        guest.Health.IsDead=true;check(!descendant.CanCompleteRocket(LevelManager.Instance,1),"rocket cancels when selected target dies");guest.Health.IsDead=false;host.Health.IsDead=false;
        int guestHits=guest.Health.Hits,hostHits=host.Health.Hits;
        Time.time=100;descendant.OnCollisionWithPlayer(host);descendant.OnCollisionWithPlayer(guest);descendant.OnCollisionWithPlayer(guest);
        check(host.Health.Hits==hostHits+1 && guest.Health.Hits==guestHits+1,"actual descendant contact cooldown is per participant without duplicate collider damage");
        var detector=new DragonDescendantCollisionDetector { gameObject=new GameObject() };detector.Initialize(descendant);
        Action<string,Collider> trigger=(name,c)=>typeof(DragonDescendantCollisionDetector).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(detector,new object[]{c});
        Time.time=101;trigger("OnTriggerEnter",host.gameObject.Colliders[0]);trigger("OnTriggerEnter",guest.gameObject.Colliders[0]);
        Time.time=102;detector.Frame();check(guest.Health.Hits==guestHits+2,"real trigger stay includes non-Main guest");
        trigger("OnTriggerExit",host.gameObject.Colliders[0]);Time.time=103;detector.Frame();
        check(guest.Health.Hits==guestHits+3,"host exit does not stop guest stay collision");
        typeof(DragonDescendantCollisionDetector).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(detector,null);
        check(detector.Routines.Count==0,"detector cleanup cancels stay loop");
        CoopAiPredictionHealthProtection.Denied.Add(descendant.BossForTest);guestHits=guest.Health.Hits;Time.time=104;descendant.OnCollisionWithPlayer(guest);
        check(guest.Health.Hits==guestHits,"replica ownership gate remains effective at actual contact entry");
        client.OnDestroy();server.OnDestroy();check(BossRushCoop.Handlers.Count==0,"destroy releases body/request/receipt subscriptions");
        BossRushCoop.IsConnected=false;controller.Knock(host);check(host.gameObject.Body.Impulses==1,"offline retains original Rigidbody force");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;DragonKingAbilityController.Faction=true;controller.Knock(host);
        check(host.gameObject.Body.Impulses==2,"faction AI retains original force path");DragonKingAbilityController.Faction=false;
        BossRushCoop.LocalPlayerId="host";BossRushCoop.RunId="room";BossRushCoop.SceneGeneration=1;BossRushCoop.Outbox.Clear();
    }
}
