using System;
using System.Linq;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static void Assert(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    private static ZombieModeRuntimeModule New(out ModBehaviour host,out ZombieModeRunState state)
    {
        Probe.Trace.Clear(); Assert(Probe.Yields.Count==0,"Previous asynchronous wait must be drained");
        UnityEngine.AI.NavMesh.Reset();
        CharacterMainControl.Main=new CharacterMainControl("player") { Team=Teams.player };
        host=new ModBehaviour(); state=new ZombieModeRunState(); return new ZombieModeRuntimeModule(host,state);
    }
    private static void Flags(SpawnRequest request,bool boss)
    {
        Assert(request.Boss==boss && !request.Equipment && !request.Multiplier && !request.Normalize && request.SkipLoot==boss,"Spawn flags must preserve independent Zombie tuning and boss loot ownership");
    }
    private static void NormalOrder()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state);
        var task=module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3()).Inner;
        Assert(state.PendingNormalZombieSpawns==1 && !task.IsCompleted,"Async creation must hold one pending slot");
        var request=host.Requests.Dequeue(); Flags(request,false); Assert(request.Active(),"Original active gate must be supplied");
        var enemy=new CharacterMainControl("candidate"); request.Complete(enemy);
        Assert(task.GetAwaiter().GetResult()==enemy && state.PendingNormalZombieSpawns==0 && state.LivingNormalZombieCount==1 && state.LivingZombieCount==1,"Successful normal spawn must release the slot and add living counts exactly once");
        Assert(string.Join(",",Probe.Trace)=="ensure,spawn:normal,roll-kind,roll-special,register:0:0,sanitize,team:scav,team:wolf,heal,target,tuning,safe:1,anchor","Normal callback random/configuration/register/count/ejection order changed");
        Assert(enemy.Team==Teams.wolf && !enemy.dropBoxOnDead && enemy.AI.noticed,"Enemy hostility and drop ownership must survive preparation");
        Assert(enemy.gameObject.name=="ZombieMode_NormalZombie_Run1","Normal enemy run identity must be applied before registration");
    }
    private static void GateAndSlot()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state);
        state.LivingNormalZombieCount=ZombieModeTuning.MaxNormalZombieCount;
        Assert(module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3()).Inner.Result==null && host.Requests.Count==0,"Cap rejection must precede preset preparation and spawn");
        state.LivingNormalZombieCount=0; bool allowed=true;
        var task=module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3(),isSpawnPhaseStillAllowed:()=>allowed).Inner;
        var request=host.Requests.Dequeue(); allowed=false;
        Assert(!request.Active(),"Shared spawn callback must retain the current phase predicate");
        var rejected=new CharacterMainControl("rejected"); request.Complete(rejected);
        Assert(task.Result==null && rejected==null && rejected.gameObject==null && state.PendingNormalZombieSpawns==0 && state.LivingZombieCount==0,"Late phase rejection must destroy candidate and components without consuming counts");
        allowed=true; var failed=module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3()).Inner; request=host.Requests.Dequeue(); request.Failure();
        Assert(failed.Result==null && state.PendingNormalZombieSpawns==0,"Failure must release the reservation");
        state.PendingNormalZombieSpawns=3; request.Failure();
        Assert(state.PendingNormalZombieSpawns==3,"Repeated completion must not release another spawn's reservation");
        state.PendingNormalZombieSpawns=0;
    }
    private static void PauseRetry()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state); module.Paused=true;
        var task=module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3()).Inner;
        Assert(host.Requests.Count==0 && state.PendingNormalZombieSpawns==0 && !task.IsCompleted,"Paused entry must await before reserving a slot or preparing presets");
        module.Paused=false; Probe.Frame(); var first=host.Requests.Dequeue(); module.Paused=true;
        var rejected=new CharacterMainControl("paused"); first.Complete(rejected);
        Assert(rejected==null && state.PendingNormalZombieSpawns==0 && !task.IsCompleted && Probe.Yields.Count==1,"Midflight pause must destroy and release before awaiting retry");
        module.Paused=false; Probe.Frame(); var second=host.Requests.Dequeue(); var accepted=new CharacterMainControl("resumed"); second.Complete(accepted);
        Assert(task.Result==accepted && Probe.Trace.Count(x=>x=="roll-kind")==1 && state.LivingZombieCount==1,"Only the accepted retry may roll enemy attributes and enter living counts");
        module.Paused=true; task=module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3()).Inner; state.RunId=2; Probe.Frame();
        Assert(task.Result==null && host.Requests.Count==0,"A run invalidated during pause must not create another candidate");
    }
    private static void BossOrder()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state);
        var task=module.TrySpawnZombieModeBossAsync(1,new Vector3(),ZombieModeBossKind.Titan).Inner;
        var request=host.Requests.Dequeue(); Flags(request,true); module.Paused=true;
        var rejected=new CharacterMainControl("paused boss"); request.Complete(rejected);
        Assert(rejected==null && state.CurrentWaveBossInstances.Count==0 && !task.IsCompleted,"Paused boss callback must destroy before creating a boss lifecycle");
        module.Paused=false; Probe.Frame(); request=host.Requests.Dequeue(); Probe.Trace.Clear();
        var boss=new CharacterMainControl("boss"); request.Complete(boss);
        Assert(task.Result==boss && state.LivingZombieCount==1 && state.LivingNormalZombieCount==0,"Boss success must update only the total living count");
        Assert(string.Join(",",Probe.Trace)=="boss-points,register:0:0,sanitize,team:scav,team:wolf,heal,target,boss-tuning,clock,clock,boss-runtime:1,safe:1,anchor","Boss lifecycle/configuration/ejection order changed");
        var instance=state.CurrentWaveBossInstances.Single();
        Assert(instance.Lifecycle.Alive && instance.Character==boss && instance.Lifecycle.LastReachableTime==12 && instance.Lifecycle.LastHurtTime==12,"Boss lifecycle must be initialized before runtime registration");
    }
    private static void NavigationAndPoison()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state);
        Vector3 result;
        UnityEngine.AI.NavMesh.SampleSuccess=false;
        Assert(!module.Resolve(new Vector3(20,0,0),false,out result),"Terrain without NavMesh must not become a valid spawn");
        Assert(module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3(20,0,0)).Inner.Result==null && host.Requests.Count==0 && state.PendingNormalZombieSpawns==0,"Normal/summoned enemy must reject invalid terrain before reserving or creating");
        UnityEngine.AI.NavMesh.Reset(); UnityEngine.AI.NavMesh.PathStatus=UnityEngine.AI.NavMeshPathStatus.PathPartial;
        Assert(!module.Resolve(new Vector3(20,0,0),false,out result),"Disconnected navigation islands must be rejected");
        Assert(module.TrySpawnZombieModeBossAsync(1,new Vector3(20,0,0),ZombieModeBossKind.Titan).Inner.Result==null && host.Requests.Count==0,"Boss must reject incomplete paths before creating");
        UnityEngine.AI.NavMesh.Reset(); UnityEngine.AI.NavMesh.SampleOffset=new Vector3(0,3,0);
        Assert(!module.Resolve(new Vector3(20,0,0),false,out result),"Nearby NavMesh on another floor must not be accepted");
        UnityEngine.AI.NavMesh.Reset(); UnityEngine.AI.NavMesh.SampleOffset=new Vector3(1,0,1);
        Assert(module.Resolve(new Vector3(20,0,0),false,out result) && result.x==21 && result.z==1 && result.y==ZombieModeTuning.NavMeshLiftOffset,"Spawn must use full sampled XYZ, including lift");
        Assert(!module.Resolve(new Vector3(1,0,0),true,out result),"Virtual ring must preserve player-safe distance");
        UnityEngine.AI.NavMesh.Reset();
        var source=new CharacterMainControl("plague"); var poison=new Duckov.Buffs.Buff(); var receiver=CharacterMainControl.Main.mainDamageReceiver;
        module.DealZombieModeAreaDamageToPlayer(1,source,Vector3.zero,4,4,poison);
        Assert(receiver.Calls==1 && receiver.Last.buff==poison && receiver.Last.buffChance==1 && receiver.Last.fromCharacter==source && receiver.Last.damageValue==4,"Cloud tick must submit official poison Buff, guaranteed chance, source and damage in one receiver hit");
        module.DealZombieModeAreaDamageToPlayer(1,source,new Vector3(10,0,0),4,4,poison);
        module.Paused=true; module.DealZombieModeAreaDamageToPlayer(1,source,Vector3.zero,4,4,poison); module.Paused=false;
        module.DealZombieModeAreaDamageToPlayer(2,source,Vector3.zero,4,4,poison);
        Assert(receiver.Calls==1,"Outside, paused and stale-run clouds must not damage or poison");
        module.DealZombieModeAreaDamageToPlayer(1,source,Vector3.zero,4,4);
        Assert(receiver.Calls==2 && receiver.Last.buff==null && receiver.Last.buffChance==0,"Non-poison areas must retain their original damage without adding poison");
    }
    public static void Main() { NormalOrder(); GateAndSlot(); PauseRetry(); BossOrder(); NavigationAndPoison(); Console.WriteLine("PASS ZombieModeSpawnRuntime"); }
}
