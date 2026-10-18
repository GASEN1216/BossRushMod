using System;
using System.Linq;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static void Assert(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    private static ZombieModeRuntimeModule New(out ModBehaviour host,out ZombieModeRunState state)
    {
        BossRushCoop.IsConnected = false; BossRushCoop.Players.Clear();
        Probe.Trace.Clear(); Assert(Probe.Yields.Count==0,"Previous asynchronous wait must be drained");
        UnityEngine.AI.NavMesh.Reset(); AstarPath.active=null;
        CharacterMainControl.Main=new CharacterMainControl("player") { Team=Teams.player, IsMainCharacter=true };
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
    private static void CoopTeamFallback()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state);
        BossRushCoop.IsConnected=true; BossRushCoop.IsHost=true; CharacterMainControl.Main=null;
        var guest=new BossRushCoopPlayer {Id="guest",Character=new CharacterMainControl("guest") {Team=Teams.scav}};
        guest.Character.transform.position=new Vector3(40,0,0); BossRushCoop.Players.Add(guest); module.Admit(guest);
        var task=module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3(42,0,0)).Inner;
        var enemy=new CharacterMainControl("non-hostile-candidate");host.Requests.Dequeue().Complete(enemy);
        Assert(task.Result==enemy && enemy.Team==Teams.wolf,
            "Actual spawn preparation restores hostility to admitted remote team after host body unload");
        Probe.Trace.Clear();guest.AssetId="replacement";
        task=module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3(42,0,0)).Inner;
        Assert(task.Result==null && host.Requests.Count==0 && !Probe.Trace.Contains("team:wolf"),
            "Unadmitted replacement cannot supply a navigation or hostile-team safety anchor");
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
        Assert(boss.AI.forceTracePlayerDistance==ZombieModeTuning.NormalZombieForceTraceDistance,"Boss tracking uses the same range as the surrounding zombies");
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
        // 官方关卡只有 A* 图、没有 Unity NavMesh：可达性必须以 A* 连通区为准（Demo 地图收集为 0 的回归）。
        var astar=new AstarPath(); AstarPath.active=astar; UnityEngine.AI.NavMesh.SampleSuccess=false;
        var playerNode=new Pathfinding.GraphNode { Area=1 }; var connected=new Pathfinding.GraphNode { Area=1 }; var island=new Pathfinding.GraphNode { Area=2 };
        Func<Pathfinding.GraphNode,Vector3,Func<Vector3,Pathfinding.NNInfo>> graph=(node,offset)=>p=>p.x==0&&p.z==0
            ? new Pathfinding.NNInfo { node=playerNode,position=p }
            : new Pathfinding.NNInfo { node=node,position=p+offset };
        astar.Nearest=graph(connected,new Vector3(1,0,1));
        Assert(module.Resolve(new Vector3(20,0,0),false,out result) && result.x==21 && result.z==1 && result.y==ZombieModeTuning.NavMeshLiftOffset,"A* graph must accept connected official points without Unity NavMesh, using full node XYZ plus lift");
        astar.Nearest=graph(island,Vector3.zero);
        Assert(!module.Resolve(new Vector3(20,0,0),false,out result),"A* point in another connected area must be rejected");
        Assert(module.TrySpawnZombieModeNormalZombieAsync(1,new Vector3(20,0,0)).Inner.Result==null && host.Requests.Count==0,"Spawn core must not receive A*-disconnected points");
        astar.Nearest=graph(connected,new Vector3(0,3,0));
        Assert(!module.Resolve(new Vector3(20,0,0),false,out result),"A* node on another floor must be rejected");
        astar.Nearest=graph(connected,new Vector3(5,0,0));
        Assert(!module.Resolve(new Vector3(20,0,0),false,out result),"A* node beyond the sample radius must be rejected");
        astar.Nearest=graph(new Pathfinding.GraphNode { Area=1,Walkable=false },Vector3.zero);
        Assert(!module.Resolve(new Vector3(20,0,0),false,out result),"Unwalkable A* node must be rejected");
        astar.Nearest=graph(connected,Vector3.zero); astar.isScanning=true;
        Assert(!module.Resolve(new Vector3(20,0,0),false,out result),"A* graph being scanned must not be trusted");
        astar.isScanning=false;
        Assert(!module.Resolve(new Vector3(1,0,0),true,out result),"A* virtual ring must preserve player-safe distance");
        AstarPath.active=null; UnityEngine.AI.NavMesh.Reset();
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
    private static void BossSpawnNearPlayer()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state);
        state.SpawnPoints.Add(new ZombieModeSpawnPoint { Position=new Vector3(400,0,0) });
        state.SpawnPoints.Add(new ZombieModeSpawnPoint { Position=new Vector3(25,0,0) });
        Assert(module.GetZombieModeBossSpawnPosition(0).x==25,"Boss prefers the nearest reachable map point instead of a remote point by index");
        var firstBoss=new CharacterMainControl("first boss"); firstBoss.transform.position=new Vector3(25,0,0);
        state.CurrentWaveBossInstances.Add(new ZombieModeBossInstance { Character=firstBoss });
        module.VirtualSpawnAvailable=true; module.VirtualSpawnPosition=new Vector3(-24,0,0);
        Assert(module.GetZombieModeBossSpawnPosition(1).x==-24,"Crowded boss point spreads using the existing reachable player ring");
        module.VirtualSpawnAvailable=false;
        Assert(module.GetZombieModeBossSpawnPosition(1).x==25,"Unavailable spread ring retains a reachable spawn instead of skipping the boss");
    }
    private static void CompanionIsolationAndKills()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state);
        var companion=new CharacterMainControl("companion") { Team=Teams.wolf,IsCompanion=true };
        Assert(module.PreserveDuringIsolation(companion),"pet identity survives original-character isolation even before its owner faction synchronizes");
        var unrelated=new CharacterMainControl("unrelated") { Team=Teams.wolf };
        Assert(!module.PreserveDuringIsolation(unrelated),"hostile original character is still isolated");
        state.LivingZombieCount=2;state.LivingNormalZombieCount=1;state.CurrentWaveKillTarget=1;
        module.Kill(companion.Health,unrelated);
        Assert(state.LivingZombieCount==2 && state.CurrentWaveKills==0 && module.DeathStarPoints==0,"companion death cannot consume enemy count or produce purification points");
        module.TrackDeathCase(unrelated);module.Kill(unrelated.Health,companion);module.Kill(unrelated.Health,companion);
        Assert(state.LivingZombieCount==1 && state.LivingNormalZombieCount==0 && state.CurrentWaveKills==1 && module.DeathStarPoints==1 && module.CompletedWaves==1,"pet kill advances a normal wave exactly once through its registered victim");
        var boss=new CharacterMainControl("boss");state.CurrentWaveBossesRemaining=1;state.CurrentWaveKillTarget=0;
        module.TrackDeathCase(boss,true);module.Kill(boss.Health,companion);module.Kill(boss.Health,companion);
        Assert(state.LivingZombieCount==0 && state.CurrentWaveBossesRemaining==0 && state.CurrentWaveKills==1 && module.CompletedWaves==2,"pet boss kill resolves boss count exactly once without becoming a normal kill");
    }
    private static void CoopInitializationNavigation()
    {
        ModBehaviour host; ZombieModeRunState state; var module = New(out host, out state);
        BossRushCoop.IsConnected = true; BossRushCoop.IsHost = true;
        var hostPeer = new BossRushCoopPlayer { Id = "host", Character = CharacterMainControl.Main };
        var guest = new BossRushCoopPlayer { Id = "guest", Character = new CharacterMainControl("guest") };
        BossRushCoop.Players.Add(hostPeer); BossRushCoop.Players.Add(guest);
        state.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
        host.CachedPoints = new[] { new Vector3(24, 0, 0), new Vector3(-24, 0, 0) };
        Assert(module.CollectZombieModeSpawnPoints(1) && state.SpawnPoints.Count == 2,
            "Co-op initialization must collect real reachable cached map points before the starter admission table exists");
        hostPeer.Character.Health.IsDead = true;
        Assert(module.CollectZombieModeSpawnPoints(1) && state.SpawnPoints.Count == 2,
            "Dead host still initializes navigation from its real living guest before starter choice");
        state.LifecyclePhase = ZombieModeLifecyclePhase.Active;
        Vector3 resolved;
        Assert(!module.Resolve(new Vector3(24, 0, 0), false, out resolved),
            "Active combat cannot borrow an unadmitted room member as its spawn anchor");
        module.Admit(guest);
        Assert(module.Resolve(new Vector3(24, 0, 0), false, out resolved),
            "Admitted original guest supplies the active combat navigation anchor");
        guest.AssetId = "replacement";
        Assert(!module.Resolve(new Vector3(24, 0, 0), false, out resolved),
            "Replacement asset cannot inherit the original admitted combat anchor");
        state.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun; guest.Character.Health.IsDead = true;
        Assert(!module.CollectZombieModeSpawnPoints(1), "All dead room members cannot manufacture a navigation anchor");
        BossRushCoop.IsConnected = false; BossRushCoop.IsHost = false; BossRushCoop.Players.Clear();
    }
    private static void NearestScanAndAnchor()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state);
        Vector3 result;
        foreach(float x in new float[]{30,13,5,19,45}) state.SpawnPoints.Add(new ZombieModeSpawnPoint {Position=new Vector3(x,0,0)});
        var astar=new AstarPath();AstarPath.active=astar;int queries=0;var node=new Pathfinding.GraphNode {Area=1};
        astar.Nearest=p=>{queries++;return new Pathfinding.NNInfo {node=node,position=p};};
        Assert(module.TryGetNearestZombieModeMapSpawnPositionToPlayer(out result)&&result.x==19&&state.NextSpawnPointIndex==4,"Nearest preferred point preserves rotation and distance tiers");
        Assert(queries==4,"Sorted scan queries one fallback and one preferred point, without rescanning the chosen point");
        state.SpawnPoints.RemoveAll(p=>p.Position.x>=18);state.NextSpawnPointIndex=0;
        Assert(module.TryGetNearestZombieModeMapSpawnPositionToPlayer(out result)&&result.x==13,"Without preferred points the nearest safe fallback remains valid");
        state.SpawnPoints.Clear();state.SpawnPoints.Add(new ZombieModeSpawnPoint {Position=new Vector3(5,0,0)});
        Assert(!module.TryGetNearestZombieModeMapSpawnPositionToPlayer(out result),"Points inside 12 m never qualify");
        state.SpawnPoints.Clear();state.SpawnPoints.Add(new ZombieModeSpawnPoint {Position=new Vector3(25,0,0)});
        queries=0;astar.Nearest=p=>{queries++;return new Pathfinding.NNInfo {node=node,position=p+new Vector3(5,0,0)};};
        module.VirtualSpawnAvailable=true;module.VirtualSpawnPosition=new Vector3(-24,0,0);UnityEngine.Time.frameCount=100;
        Assert(!module.TryGetZombieModeReliableSpawnPosition(out result)&&queries==1,"Unsnappable solo anchor rejects the complete search after one query");
        Assert(!module.TryGetZombieModeReliableSpawnPosition(out result)&&queries==1,"Same-frame failed search never repeats navigation work");
        UnityEngine.Time.frameCount++;
        Assert(!module.TryGetZombieModeReliableSpawnPosition(out result)&&queries==2,"Next frame retries the anchor check");
        astar.Nearest=p=>{queries++;return new Pathfinding.NNInfo {node=node,position=p};};UnityEngine.Time.frameCount++;
        Assert(module.TryGetZombieModeReliableSpawnPosition(out result)&&result.x==25,"A recovered anchor resumes nearest map spawn selection");

        var hostPeer=new BossRushCoopPlayer {Id="host",Character=CharacterMainControl.Main};
        var guest=new BossRushCoopPlayer {Id="guest",Character=new CharacterMainControl("guest")};guest.Character.transform.position=new Vector3(40,0,0);
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.Players.Add(hostPeer);BossRushCoop.Players.Add(guest);
        state.SpawnPoints.Clear();state.SpawnPoints.Add(new ZombieModeSpawnPoint {Position=new Vector3(60,0,0)});module.VirtualSpawnAvailable=false;
        astar.Nearest=p=>new Pathfinding.NNInfo {node=node,position=p.x==0?p+new Vector3(5,0,0):p};
        state.LifecyclePhase=ZombieModeLifecyclePhase.InitializingRun;UnityEngine.Time.frameCount++;
        Assert(module.TryGetZombieModeReliableSpawnPosition(out result)&&result.x==60,"Initialization checks all living anchors when the nearest-to-origin host is off navigation");
        state.LifecyclePhase=ZombieModeLifecyclePhase.Active;module.Admit(hostPeer);UnityEngine.Time.frameCount++;
        Assert(!module.TryGetZombieModeReliableSpawnPosition(out result),"An unadmitted reachable guest cannot bypass an admitted host's failed navigation anchor");
        module.Admit(guest);UnityEngine.Time.frameCount++;
        Assert(module.TryGetZombieModeReliableSpawnPosition(out result)&&result.x==60,"A reachable admitted guest keeps spawn selection available while host is off navigation");
        hostPeer.Character.Health.IsDead=true;UnityEngine.Time.frameCount++;
        Assert(module.TryGetZombieModeReliableSpawnPosition(out result),"Dead host does not block living admitted guest spawn anchors");
        guest.AssetId="replacement";UnityEngine.Time.frameCount++;
        Assert(!module.TryGetZombieModeReliableSpawnPosition(out result),"Replacement guest asset cannot inherit original admitted navigation eligibility");
    }
    private static void CoopBossTargetAndBackoff()
    {
        ModBehaviour host; ZombieModeRunState state; var module=New(out host,out state);
        var hostPeer=new BossRushCoopPlayer {Id="host",Character=CharacterMainControl.Main};hostPeer.Character.Health.IsDead=true;
        var guest=new BossRushCoopPlayer {Id="guest",Character=new CharacterMainControl("guest")};guest.Character.transform.position=new Vector3(40,0,0);
        BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.Players.Add(hostPeer);BossRushCoop.Players.Add(guest);module.Admit(guest);
        var boss=new CharacterMainControl("boss");boss.transform.position=new Vector3(42,0,0);
        var skill=new ZombieModeBossSkillState();var instance=new ZombieModeBossInstance {Character=boss,Kind=ZombieModeBossKind.Hunter,SkillState=skill};
        instance.Lifecycle.Alive=true;instance.Lifecycle.LastKnownPosition=boss.transform.position;state.CurrentWaveBossInstances.Add(instance);module.Clock=100;
        CharacterMainControl.Main=null;module.TickZombieModeBossController(.1f);
        Assert(skill.Ticks==1&&instance.Lifecycle.LastReachableTime==100&&boss.transform.position.x==42,"Live admitted guest keeps Boss skills running and prevents false stuck teleports after host body unload");
        int dashes=ZombieModeSprinterDashRuntime.Started;
        module.TickZombieModeHunterState(new ZombieModeHunterState(),instance,100);
        Assert(ZombieModeSprinterDashRuntime.Started==dashes+1&&ZombieModeSprinterDashRuntime.Target.x==40,"Hunter dash targets the actual living admitted guest");
        module.TickZombieModeCorruptorState(new ZombieModeCorruptorState(),instance,200);
        Assert(module.CorruptionZones==1&&module.CorruptionTarget.x==40,"Corruptor area targets the actual living admitted guest");
        guest.AssetId="replacement";module.TickZombieModeHunterState(new ZombieModeHunterState(),instance,300);
        Assert(ZombieModeSprinterDashRuntime.Started==dashes+1,"Unadmitted replacement body cannot become a Boss skill target");

        module=New(out host,out state);boss=new CharacterMainControl("stuck boss");boss.transform.position=new Vector3(100,0,0);
        instance=new ZombieModeBossInstance {Character=boss};instance.Lifecycle.Alive=true;instance.Lifecycle.LastKnownPosition=boss.transform.position;state.CurrentWaveBossInstances.Add(instance);
        int queries=0;AstarPath.active=new AstarPath {Nearest=p=>{queries++;return new Pathfinding.NNInfo();}};
        module.Clock=100;UnityEngine.Time.frameCount=200;module.TickZombieModeBossController(.1f);
        Assert(queries==1&&instance.Lifecycle.LastReachableTime==89,"Failed Boss fallback schedules its next search one second later");
        for(int i=1;i<=9;i++){module.Clock=100+i*.1f;UnityEngine.Time.frameCount++;module.TickZombieModeBossController(.1f);}
        Assert(queries==1,"Unreachable stuck Boss performs no additional navigation queries before the backoff expires");
        module.Clock=101;UnityEngine.Time.frameCount++;module.TickZombieModeBossController(.1f);
        Assert(queries==2,"Stuck Boss retries the real search when the one-second backoff expires");
    }
    public static void Main() { NormalOrder(); CoopTeamFallback(); GateAndSlot(); PauseRetry(); BossOrder(); NavigationAndPoison(); BossSpawnNearPlayer(); CompanionIsolationAndKills(); CoopInitializationNavigation(); NearestScanAndAnchor(); CoopBossTargetAndBackoff(); Console.WriteLine("PASS ZombieModeSpawnRuntime"); }
}
