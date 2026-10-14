using System;
using System.Runtime.CompilerServices;
using BossRush;
using HarmonyLib;
using UnityEngine;

internal static class Program
{
    private static int count;
    private static void Check(bool condition,string message) { count++;if(!condition)throw new Exception(message); }
    private static void AuthorityLifetimes()
    {
        BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;BossRushCoop.RunId="";
        var solo=new CharacterMainControl();
        Check(CoopAiPredictionHealthProtection.HasEntityAuthority(solo),"unmarked new single-player entity keeps original world authority");
        Check(!CoopAiPredictionHealthProtection.HasEntityAuthority(null),"missing entity has no world authority");
        BossRushCoop.IsConnected=true;BossRushCoop.RunId="replica-room";
        var replica=new CharacterMainControl();
        CoopAiPredictionHealthProtection.MarkReplica(replica);
        Check(replica.GetComponent<CoopAiPredictionReplicaMarker>().Replica&&!CoopAiPredictionHealthProtection.HasEntityAuthority(replica),"known replica birth is permanent even before prediction owner exists");
        var owner=new CoopAiPredictionHealthRuntimeModule();owner.OnAwake(new ModBehaviour());
        CoopAiPredictionHealthProtection.MarkReplica(replica);owner.OnUpdate(0,0);
        BossRushCoop.IsConnected=false;BossRushCoop.RunId="";owner.OnUpdate(0,0);
        Check(!replica.GetComponent<CoopAiPredictionReplicaMarker>().Armed&&!CoopAiPredictionHealthProtection.HasEntityAuthority(replica),"actual world cleanup disarms health but stopped client replica cannot become offline authority");
        Check(CoopAiPredictionHealthProtection.HasEntityAuthority(new CharacterMainControl()),"new offline actor remains available while former replica is retired");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="host-new";
        CoopAiPredictionHealthProtection.TrackAuthority(replica);
        Check(!CoopAiPredictionHealthProtection.HasEntityAuthority(replica)&&!replica.GetComponent<CoopAiPredictionReplicaMarker>().AuthorityBound,"client becoming host cannot relabel previous replica through explicit registration");
        var authority=new CharacterMainControl();CoopAiPredictionHealthProtection.TrackAuthority(authority);
        Check(CoopAiPredictionHealthProtection.HasEntityAuthority(authority),"registered host entity owns its actual creation room");
        BossRushCoop.IsHost=false;
        Check(!CoopAiPredictionHealthProtection.HasEntityAuthority(authority),"host entity loses authority when local role changes to client");
        BossRushCoop.IsConnected=false;BossRushCoop.RunId="";owner.OnUpdate(0,0);
        Check(!CoopAiPredictionHealthProtection.HasEntityAuthority(authority),"host stop cannot make its old network entity an offline world owner");
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.RunId="another-room";
        CoopAiPredictionHealthProtection.TrackAuthority(authority);
        Check(!CoopAiPredictionHealthProtection.HasEntityAuthority(authority)&&authority.GetComponent<CoopAiPredictionReplicaMarker>().AuthorityRun=="host-new","same old entity cannot be rebound into a different host room");
        var replacement=new CharacterMainControl();CoopAiPredictionHealthProtection.TrackAuthority(replacement);
        Check(CoopAiPredictionHealthProtection.HasEntityAuthority(replacement),"new entity in new room retains full world authority");
        owner.OnDestroy();BossRushCoop.IsConnected=false;BossRushCoop.IsHost=false;
        Check(!CoopAiPredictionHealthProtection.HasEntityAuthority(replica)&&!CoopAiPredictionHealthProtection.HasEntityAuthority(authority),"module teardown cannot revive old entity roles");
        UnityEngine.Object.Destroy(solo.gameObject);
        Check(solo==null&&solo.Health==null&&!CoopAiPredictionHealthProtection.HasEntityAuthority(solo),"actual fixture scene destruction cascades and destroyed Unity entity has no authority");
        BossRushCoop.IsConnected=true;BossRushCoop.RunId="room";
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var player=new CharacterMainControl { PlayerId="local" };
        var replica=new CharacterMainControl(10f,0f) { Ai=-7 };
        var owner=new CoopAiPredictionHealthRuntimeModule();BossRushCoop.IsHost=false;owner.OnAwake(new ModBehaviour());
        CoopAiPredictionHealthProtection.MarkReplica(replica);
        NativePredictionFixturePatch.Reports.Clear();replica.Health.Hurt(new DamageInfo { fromCharacter=player,damageValue=20f });
        Check(replica.Health.CurrentHealth==1f&&!replica.Health.IsDead,"marked client AI predicts hits with lethal health floor");
        Check(NativePredictionFixturePatch.Reports.Count==1&&NativePredictionFixturePatch.Reports[0]==20f,"native raw damage remains; no 99999 forced death report");
        var hostCharacter=new CharacterMainControl(10f,20f);BossRushCoop.IsHost=true;
        hostCharacter.Health.Hurt(new DamageInfo { fromCharacter=player,damageValue=NativePredictionFixturePatch.Reports[0] });
        Check(hostCharacter.Health.CurrentHealth>8f&&!hostCharacter.Health.IsDead,"host re-evaluates raw damage against actual stronger armor");
        BossRushCoop.IsHost=false;replica.Health.CurrentHealth=0f;
        Check(replica.Health.IsDead,"host death snapshot setter outside local Hurt remains authoritative");
        replica.Health.CurrentHealth=10f;var otherPlayer=new CharacterMainControl { PlayerId="other" };
        replica.Health.Hurt(new DamageInfo { fromCharacter=otherPlayer,damageValue=20f });
        Check(replica.Health.IsDead,"non-local attacker context does not enter local prediction clamp");
        var vanilla=new CharacterMainControl();NativePredictionFixturePatch.Reports.Clear();
        vanilla.Health.Hurt(new DamageInfo { fromCharacter=player,damageValue=20f });
        Check(vanilla.Health.IsDead&&NativePredictionFixturePatch.Reports.Count==2,"unmarked native AI preserves upstream behavior and reproduces forced death bug");
        replica.Health.CurrentHealth=10f;replica.Health.Throw=true;
        try { replica.Health.Hurt(new DamageInfo { fromCharacter=player,damageValue=20f }); } catch(InvalidOperationException) { }
        replica.Health.Throw=false;replica.Health.CurrentHealth=0f;
        Check(replica.Health.IsDead&&!BossRushHealthHurtContextPatch.IsInsideHurt,"exception Finalizer restores both contexts; later authoritative setter is unclamped");
        replica.Health.CurrentHealth=10f;
        replica.Health.Nested=()=> { var nested=new CharacterMainControl();nested.Health.Hurt(new DamageInfo { fromCharacter=otherPlayer,damageValue=20f });Check(nested.Health.IsDead,"nested unrelated health remains unclamped");replica.Health.CurrentHealth=-2f; };
        replica.Health.Hurt(new DamageInfo { fromCharacter=player,damageValue=20f });replica.Health.Nested=null;
        Check(replica.Health.CurrentHealth==1f,"nested Finalizer restores outer exact health context");
        owner.OnDestroy();replica.Health.CurrentHealth=10f;
        replica.Health.Hurt(new DamageInfo { fromCharacter=player,damageValue=20f });
        Check(replica.Health.IsDead,"mod teardown removes locally bound marker, no protection persists into next owner");

        // 统一归属桥只接受明确登记对象；ID延后产生、负ID、复制延后、旧generation均覆盖。
        BossRushCoop.IsHost=true;var authorityOwner=new CoopAiPredictionHealthRuntimeModule();authorityOwner.OnAwake(new ModBehaviour());
        var authority=new CharacterMainControl();CoopAiPredictionHealthProtection.TrackAuthority(authority);
        Time.unscaledTime=0;authorityOwner.OnUpdate(0,0);object empty=BossRushCoop.Last;
        authority.Ai=-11;Time.unscaledTime=0.6f;authorityOwner.OnUpdate(0,0);object state=BossRushCoop.Last;
        BossRushCoop.IsHost=false;var clientOwner=new CoopAiPredictionHealthRuntimeModule();clientOwner.OnAwake(new ModBehaviour());
        BossRushCoop.Handler(new BossRushCoopMessage { IsFromHost=false,Value=state });clientOwner.OnUpdate(0,0);
        var late=new CharacterMainControl { Ai=-11 };BossRushCoop.Ai[-11]=late;
        Check(late.GetComponent<CoopAiPredictionReplicaMarker>()==null,"peer cannot nominate arbitrary native AI");
        BossRushCoop.Handler(new BossRushCoopMessage { IsFromHost=true,Value=empty });Time.unscaledTime=1f;clientOwner.OnUpdate(0,0);
        Check(late.GetComponent<CoopAiPredictionReplicaMarker>()==null,"unresolved zero ID is never projected");
        BossRushCoop.Handler(new BossRushCoopMessage { IsFromHost=true,Value=state });Time.unscaledTime=1.2f;clientOwner.OnUpdate(0,0);
        Check(late.GetComponent<CoopAiPredictionReplicaMarker>()!=null&&late.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"host-confirmed delayed negative identity binds actual replica");
        var replacement=new CharacterMainControl { Ai=-11 };BossRushCoop.Ai[-11]=replacement;Time.unscaledTime=1.41f;clientOwner.OnUpdate(0,0);
        Check(replacement.GetComponent<CoopAiPredictionReplicaMarker>()!=null&&replacement.GetComponent<CoopAiPredictionReplicaMarker>().Armed&&!late.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"same native ID with a replacement body rebinds and retires the old body marker");
        BossRushCoop.SceneGeneration++;Time.unscaledTime=1.5f;clientOwner.OnUpdate(0,0);
        Check(!replacement.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"scene generation immediately removes old protection without delayed Destroy");
        CoopAiPredictionHealthProtection.MarkReplica(replacement);
        Check(replacement.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"same-frame owner-specific binding can reactivate retained component without pending destruction");
        BossRushCoop.SceneGeneration++;Time.unscaledTime=1.8f;clientOwner.OnUpdate(0,0);
        BossRushCoop.Handler(new BossRushCoopMessage { IsFromHost=true,Value=state });Time.unscaledTime=2f;clientOwner.OnUpdate(0,0);
        Check(!replacement.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"stale-generation authority cannot re-enable old replica");
        clientOwner.OnDestroy();BossRushCoop.IsHost=true;
        var arrival=new CharacterMainControl();CoopAiPredictionHealthProtection.Owner=authorityOwner;CoopAiPredictionHealthProtection.TrackAuthority(arrival);
        arrival.Ai=-12;BossRushCoop.RaiseAi(-12,arrival);object birth=BossRushCoop.Last;
        BossRushCoop.IsHost=false;var arrivalOwner=new CoopAiPredictionHealthRuntimeModule();arrivalOwner.OnAwake(new ModBehaviour());
        BossRushCoop.Ai.Remove(-12);BossRushCoop.Handler(new BossRushCoopMessage { IsFromHost=true,Value=birth });
        var arrivalReplica=new CharacterMainControl { Ai=-12 };BossRushCoop.Ai[-12]=arrivalReplica;BossRushCoop.RaiseAi(-12,arrivalReplica);
        Check(arrivalReplica.GetComponent<CoopAiPredictionReplicaMarker>()!=null&&arrivalReplica.GetComponent<CoopAiPredictionReplicaMarker>().Armed,"confirmed ownership before native birth protects in AiSpawned callback without tick delay");
        arrivalOwner.OnDestroy();authorityOwner.OnDestroy();Check(CoopAiPredictionHealthProtection.Owner==null&&BossRushCoop.Handler==null&&!BossRushCoop.HasAiSubscriber,"single runtime owner tears down channel/event and static owner");
        AuthorityLifetimes();
        Console.WriteLine("CoopAiPrediction: PASS "+count+" real installed Harmony assertions; production Hurt context, shared lethal setter, exact owned-AI projection and frozen entity authority.");
    }
    public static int Main()
    {
        try
        {
            var harmony=new Harmony("BossRush.fixture.coop-prediction");
            foreach(Type type in new[] { typeof(BossRushHealthHurtContextPatch),typeof(BossRushBossLethalHealthProtectionPatch),typeof(CoopAiPredictionHealthHurtPatch),typeof(NativePredictionFixturePatch) })
            { Console.WriteLine("Install "+type.Name);harmony.CreateClassProcessor(type).Patch(); }
            Run();return 0;
        }
        catch(Exception error) { Console.WriteLine("FAIL "+error.GetType().FullName+": "+error.Message);return 1; }
    }
}
