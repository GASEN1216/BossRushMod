using System;
using System.Reflection;
using BossRush;
using UnityEngine;

internal static partial class Program
{
    private static void SetToken(object packet,string value,string field="SceneToken")
    { var member=packet.GetType().GetField(field,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);if(member!=null)member.SetValue(packet,value); }
    private static void SceneBoundaries() { FxSceneBoundary();PredictionSceneBoundary(); }
    private static void FxSceneBoundary()
    {
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;BossRushCoop.RunId="fx-room";BossRushCoop.SceneGeneration=31;
        BossRushCoop.SceneToken="actual-core-32";BossRushCoop.HasBoundScene=false;CoopAiPredictionFxCounters.Spawned=0;
        var actor=new CharacterMainControl {Ai=-42};BossRushCoop.Ai[-42]=actor;
        var packet=new BossRushCoopHazardPacket {run="fx-room",scene=31,
            hazards=new[]{new BossRushCoopHazardState {id=9,kind=1},new BossRushCoopHazardState {id=10,kind=2},new BossRushCoopHazardState {id=11,kind=3}},
            bosses=new[]{new BossRushCoopBossState {ai=-42,kind="ordinary",visible=false,scale=new Vector3(7,7,7)}}};
        SetToken(packet,"actual-core-31","token");
        var harness=new FxHarness();var message=new BossRushCoopMessage {IsFromHost=true,Value=packet};
        harness.Apply(message);
        Check(CoopAiPredictionFxCounters.Spawned==0&&actor.gameObject.activeSelf&&actor.GetComponent<CoopAiPredictionReplicaMarker>()==null,
            "unbound old-generation packet cannot create hazards or project a newly registered same-ID actor");
        SetToken(packet,"actual-core-32","token");harness.Apply(message);
        Check(CoopAiPredictionFxCounters.Spawned==0,"matching local token cannot substitute for a completed scene-generation bind");
        SetToken(packet,"actual-core-31","token");
        BossRushCoop.HasBoundScene=true;harness.Apply(message);
        Check(CoopAiPredictionFxCounters.Spawned==0&&actor.gameObject.activeSelf,"wrong exact token is rejected even when numeric generation matches");
        SetToken(packet,null,"token");harness.Apply(message);Check(CoopAiPredictionFxCounters.Spawned==0,"missing hazard token cannot certify the actual world");
        SetToken(packet,"actual-core-32","token");message.IsFromHost=false;harness.Apply(message);
        Check(CoopAiPredictionFxCounters.Spawned==0,"peer cannot authorize hazards");
        message.IsFromHost=true;harness.Apply(message);
        Check(CoopAiPredictionFxCounters.Spawned==3&&!actor.gameObject.activeSelf&&actor.transform.localScale.x==7,
            "bound exact-token host packet retains all hazard creation and boss visibility/scale behavior");
        packet.run="old-room";packet.hazards[0].id=10;harness.Apply(message);
        Check(CoopAiPredictionFxCounters.Spawned==3,"old room stays rejected with a matching token");
        Console.WriteLine("Fx scene boundary: PASS");
    }
    private static void PredictionSceneBoundary()
    {
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="prediction-room";BossRushCoop.SceneGeneration=31;
        BossRushCoop.SceneToken="actual-core-31";BossRushCoop.HasBoundScene=true;Time.unscaledTime=40;
        var host=new CoopAiPredictionHealthRuntimeModule();host.OnAwake(new ModBehaviour());
        var source=new CharacterMainControl {Ai=-43};CoopAiPredictionHealthProtection.TrackAuthority(source);host.OnUpdate(0,0);object oldState=BossRushCoop.Last;
        BossRushCoop.IsHost=false;var client=new CoopAiPredictionHealthRuntimeModule();client.OnAwake(new ModBehaviour());
        var actor=new CharacterMainControl {Ai=-43};BossRushCoop.Ai[-43]=actor;
        BossRushCoop.HasBoundScene=false;BossRushCoop.SceneToken="actual-core-32";
        BossRushCoop.Handler(new BossRushCoopMessage {IsFromHost=true,Value=oldState});
        Check(actor.GetComponent<CoopAiPredictionReplicaMarker>()==null,"unbound old authority snapshot cannot mark a newly registered same-ID actor");
        SetToken(oldState,"actual-core-32");BossRushCoop.Handler(new BossRushCoopMessage {IsFromHost=true,Value=oldState});
        Check(actor.GetComponent<CoopAiPredictionReplicaMarker>()==null,"matching local token cannot certify an unbound authority snapshot");
        SetToken(oldState,"actual-core-31");
        BossRushCoop.HasBoundScene=true;BossRushCoop.Handler(new BossRushCoopMessage {IsFromHost=true,Value=oldState});
        Check(actor.GetComponent<CoopAiPredictionReplicaMarker>()==null,"same numeric generation does not make an old token eligible");
        SetToken(oldState,null);BossRushCoop.Handler(new BossRushCoopMessage {IsFromHost=true,Value=oldState});
        Check(actor.GetComponent<CoopAiPredictionReplicaMarker>()==null,"missing authority token cannot nominate a new replica");
        SetToken(oldState,"actual-core-32");BossRushCoop.Handler(new BossRushCoopMessage {IsFromHost=true,Value=oldState});
        Check(actor.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"current exact-token host certificate protects actual native replica");
        var player=new CharacterMainControl {PlayerId="local"};actor.Health.Hurt(new DamageInfo {fromCharacter=player,damageValue=20});
        Check(actor.Health.CurrentHealth==1,"current exact scene preserves lethal prediction floor");
        actor.Health.CurrentHealth=10;BossRushCoop.HasBoundScene=false;
        actor.Health.Hurt(new DamageInfo {fromCharacter=player,damageValue=20});
        Check(actor.Health.IsDead,"native Begin removes prediction eligibility immediately before owner Update");
        var birth=new CharacterMainControl {Ai=-43};BossRushCoop.Ai[-43]=birth;BossRushCoop.RaiseAi(-43,birth);
        Check(birth.GetComponent<CoopAiPredictionReplicaMarker>()==null,"native same-ID birth cannot consume cached prior snapshot while unbound");
        Time.unscaledTime=41;client.OnUpdate(0,0);
        Check(!actor.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"unbound tick revokes prior qualification without erasing permanent replica role");
        CoopAiPredictionHealthProtection.MarkReplica(birth);
        Check(birth.GetComponent<CoopAiPredictionReplicaMarker>().Replica&&!birth.GetComponent<CoopAiPredictionReplicaMarker>().Armed,
            "trusted unbound replica birth keeps permanent role but cannot arm prediction");
        BossRushCoop.HasBoundScene=true;Time.unscaledTime=42;client.OnUpdate(0,0);
        Check(!birth.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"returning bound cannot replay discarded stale authority snapshot");
        BossRushCoop.Handler(new BossRushCoopMessage {IsFromHost=true,Value=oldState});
        Check(birth.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"fresh exact-token snapshot restores same-ID late birth without reducing behavior");
        BossRushCoop.SceneToken="actual-core-33";birth.Health.Hurt(new DamageInfo {fromCharacter=player,damageValue=20});
        Check(birth.Health.IsDead,"old marker cannot clamp damage after token changes without a numeric generation change");
        client.OnDestroy();host.OnDestroy();BossRushCoop.IsHost=true;BossRushCoop.HasBoundScene=false;BossRushCoop.Last=null;
        var pendingHost=new CoopAiPredictionHealthRuntimeModule();pendingHost.OnAwake(new ModBehaviour());
        CoopAiPredictionHealthProtection.TrackAuthority(new CharacterMainControl {Ai=-44});Time.unscaledTime=43;pendingHost.OnUpdate(0,0);
        Check(BossRushCoop.Last==null,"host cannot publish an unbound old-world authority certificate");
        BossRushCoop.HasBoundScene=true;Time.unscaledTime=44;pendingHost.OnUpdate(0,0);
        Check(BossRushCoop.Last!=null,"pending host-owned registration remains available after actual world binds");
        pendingHost.OnDestroy();BossRushCoop.IsHost=false;
        Console.WriteLine("Prediction scene boundary: PASS");
    }
}
