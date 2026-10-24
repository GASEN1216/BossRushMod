using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BossRush;
using Duckov.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static readonly Vector3 Stand=new Vector3(20,0,30), Ordinary=new Vector3(3,0,4);
    private static void Check(bool value,string name) { if(!value)throw new Exception("FAIL: "+name);checks++; }
    private static bool At(Vector3 p) { var q=CharacterMainControl.Main.transform.position;return q.x==p.x&&q.y==p.y&&q.z==p.z; }
    private static Scene Target(string name="arena") { return new Scene{name=name,handle=2,isLoaded=true}; }
    private static ModeHRuntimeModule Reset(string name="arena",string id="main")
    {
        BossRushInitialSpawn.Arrived=false;
        SceneLoader.ResetEvents();SceneManager.ResetEvents();ZombieModeMapSelectionHelper.HasPendingZombieEntry=false;
        AstarPath.active = new AstarPath();
        CharacterMainControl.Main=null;LevelManager.LevelInited=false;LevelManager.AfterInit=false;LevelManager.LevelInitializingComment="";SceneLoader.IsSceneLoading=true;
        SceneLoader.Instance=new SceneLoader{Load=()=>Task.CompletedTask};
        MultiSceneCore.Instance=new MultiSceneCore{IsLoading=true};
        SceneManager.Active=new Scene{name="loading",handle=1,isLoaded=true};
        BossRushMapSelectionHelper.Pending=false;BossRushMapSelectionHelper.Generation=0;BossRushMapSelectionHelper.Paid=true;
        ModeHRuntimeGates.SlotGeneration=1;ModeHRuntimeGates.Active=false;
        ModeHArenaIsolationLease.Acquired=ModeHSpectatorLease.Acquired=0;
        BossRushUI.Paused=false;Time.unscaledDeltaTime=.1f;
        ModeHMapSupportRegistry.Map=new ModeHSupportedMap{SceneName=name,SceneId=id,SpectatorPos=Stand,PlayerSpawnPos=Ordinary};
        BossRushMapSelectionHelper.Freeze(name,id);
        return new ModeHRuntimeModule();
    }
    private static void Ready(Scene target)
    {
        if(CharacterMainControl.Main==null)CharacterMainControl.Main=new CharacterMainControl();
        CharacterMainControl.Main.SetPosition(Ordinary);SceneManager.Active=target;
        LevelManager.LevelInited=true;LevelManager.AfterInit=true;SceneLoader.IsSceneLoading=false;MultiSceneCore.Instance.IsLoading=false;
    }
    private static void Ticks(ModeHRuntimeModule runtime,int count) { for(int i=0;i<count;i++)runtime._owner.Tick(); }
    private static void Main(string[] args)
    {
        using(var maps=JsonDocument.Parse(File.ReadAllText(args.Length>0?args[0]:Environment.GetEnvironmentVariable("BOSSRUSH_MODEH_MAPS"))))
            foreach(var map in maps.RootElement.EnumerateArray())
                NewMapEntry(map.GetProperty("scene").GetString(),map.GetProperty("id").GetString());
        LegacyOrdering();CustomTeleportOwnership();Cancellation();ReadinessGates();Resume().GetAwaiter().GetResult();
        Console.WriteLine("PASS: "+checks+" assertions; production scene callbacks, wait, resume and Legacy coroutine; no Unity smoke");
    }
    private static void NewMapEntry(string name,string id)
    {
        var r=Reset(name,id);var target=Target(name);
        r.OnSceneLoaded(new SceneRuntimeContext(target));
        Check(r.PendingRoutine!=null&&r.FreshStarted==0,"sceneLoaded defers leases: "+name);
        Ticks(r,2);Check(ModeHArenaIsolationLease.Acquired==0&&r.Aborts==0,"missing player during normal loading is not an abort");
        CharacterMainControl.Main=new CharacterMainControl();LevelManager.LevelInited=true;
        SceneLoader.IsSceneLoading=false;SceneManager.Active=target;
        Ticks(r,2);Check(r.FreshStarted==0,"subscene teleporter still owns position");
        MultiSceneCore.Instance.IsLoading=false;
        // 现机官方顺序：LevelInited 置真后还会等待 0.25 秒，再最终搬人并置 AfterInit。
        Time.unscaledDeltaTime=1f/60f;Ticks(r,15);
        Check(r.FreshStarted==0&&ModeHSpectatorLease.Acquired==0,
            "final official 0.25-second positioning window cannot acquire leases: "+name);
        CharacterMainControl.Main.SetPosition(Ordinary);LevelManager.AfterInit=true;
        Ticks(r,1);Check(r.FreshStarted==0,"first ready frame still defers");
        Ticks(r,1);Check(r.FreshStarted==1&&At(Stand),"leases run after final official teleport: "+name);
        Check(ModeHArenaIsolationLease.Acquired==1&&ModeHSpectatorLease.Acquired==1,"exactly one lease pair");
        r._owner.Legacy(Ordinary);Ticks(r,3);
        Check(At(Stand)&&r._owner.LegacySetups==0,"consumed intent remains protected by live H owner");

        // 2026-10-12：第三方 OnAfterLevelInitialized 订阅者抛异常时 afterInit 永远不置位；
        // 官方在最终落点之后写入的「Done!」足以证明搬人已完成，不能因此卡满 30 秒再退票。
        r=Reset(name,id);target=Target(name);
        r.OnSceneLoaded(new SceneRuntimeContext(target));
        CharacterMainControl.Main=new CharacterMainControl();LevelManager.LevelInited=true;
        SceneLoader.IsSceneLoading=false;SceneManager.Active=target;MultiSceneCore.Instance.IsLoading=false;
        LevelManager.LevelInitializingComment="Setting character position...";
        Ticks(r,3);Check(r.FreshStarted==0,"intermediate init comment is not completion: "+name);
        CharacterMainControl.Main.SetPosition(Ordinary);LevelManager.LevelInitializingComment="Done!";
        Ticks(r,2);Check(r.FreshStarted==1&&r.Aborts==0,"official Done! marker admits entry when AfterInit is stuck: "+name);
    }
    private static void LegacyOrdering()
    {
        var r=Reset();BossRushMapSelectionHelper.Pending=false;
        var old=r._owner.Legacy(Ordinary); // waiting for player before a Mode H intent exists
        BossRushMapSelectionHelper.Freeze("arena","main");
        Ready(Target());r.OnSceneLoaded(new SceneRuntimeContext(Target()));
        Ticks(r,4);
        Check(old.Stopped&&r.FreshStarted==1&&At(Stand)&&r._owner.LegacySetups==0,"old Legacy task cannot overwrite new H spectator");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());
        old=r._owner.Legacy(Ordinary); // already waiting its final half second
        BossRushMapSelectionHelper.Freeze("arena","main");
        CharacterMainControl.Main.SetPosition(Stand);Ticks(r,1);
        Check(old.Stopped&&At(Stand),"typed intent blocks resumed Legacy before an H run owner exists");

        r=Reset();Ready(Target());var never=r._owner.Legacy(Ordinary);
        Check(never.Stopped&&CharacterMainControl.Main.PositionWrites==1,"typed H rejects Legacy before any waits or writes");

        BossRushMapSelectionHelper.Pending=false;ModeHRuntimeGates.Active=false;
        r._owner.Legacy(Stand);Ticks(r,2);
        Check(At(Stand)&&r._owner.LegacySetups==1,"ordinary entry remains available");

        r=Reset();Ready(Target());BossRushMapSelectionHelper.Pending=false;ModeHRuntimeGates.Active=false;
        SceneLoader.IsSceneLoading=true;int before=CharacterMainControl.Main.PositionWrites;
        r._owner.Legacy(Stand);Ticks(r,4);
        Check(CharacterMainControl.Main.PositionWrites==before&&r._owner.LegacySetups==0,"Legacy waits for final official player spawn");
        SceneLoader.IsSceneLoading=false;MultiSceneCore.Instance.IsLoading=true;Ticks(r,4);
        Check(CharacterMainControl.Main.PositionWrites==before,"Legacy also waits for nested subscene teleport");
        MultiSceneCore.Instance.IsLoading=false;Ticks(r,2);
        Check(At(Stand)&&r._owner.LegacySetups==1,"Legacy enters arena once after official spawn completes");
        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());CharacterMainControl.Main.PositionWrites=0;BossRushInitialSpawn.Arrived=true;
        r._owner.Legacy(Stand);Ticks(r,3);
        Check(CharacterMainControl.Main.PositionWrites==0&&r._owner.LegacySetups==1,"initial spawn skips second teleport but retains arena setup");
    }
    private static void Cancellation()
    {
        var r=Reset();r.OnSceneLoaded(new SceneRuntimeContext(Target()));var old=r.PendingRoutine;
        ModeHEntry.CancelPendingEntry();BossRushMapSelectionHelper.Freeze("arena","main");
        r.OnSceneLoaded(new SceneRuntimeContext(Target()));var current=r.PendingRoutine;
        Ready(Target());while(old.Body.MoveNext()){} // even a manually resumed stopped enumerator is stale
        Check(ReferenceEquals(r.PendingRoutine,current)&&r.Aborts==0&&BossRushMapSelectionHelper.Pending,"old finally cannot clear or refund replacement intent");
        Ticks(r,2);Check(r.FreshStarted==1,"replacement request starts once");

        r=Reset();r.OnSceneLoaded(new SceneRuntimeContext(Target()));
        ModeHEntry.CancelPendingEntry();Ready(Target());Ticks(r,3);
        Check(r.FreshStarted==0&&r.Aborts==0&&r.PendingRoutine==null,"explicit cancel invalidates pending ready request");

        r=Reset();r.OnSceneLoaded(new SceneRuntimeContext(Target()));ModeHRuntimeGates.SlotGeneration++;
        Ready(Target());Ticks(r,3);
        Check(r.FreshStarted==0&&r.Aborts==0,"slot switch cannot refund or start using new slot");

        r=Reset();r.OnSceneLoaded(new SceneRuntimeContext(Target()));old=r.PendingRoutine;r.Shutdown();
        Ready(Target());while(old.Body.MoveNext()){}
        Check(r.FreshStarted==0&&r.Aborts==0&&r.PendingRoutine==null,"shutdown cancels even a late enumerator");

        r=Reset();r.OnSceneLoaded(new SceneRuntimeContext(Target()));
        r.OnSceneLoaded(new SceneRuntimeContext(new Scene{name="other",handle=3,isLoaded=true}));Ticks(r,3);
        Check(r.FreshStarted==0&&r.Aborts==1&&!BossRushMapSelectionHelper.Pending,"scene generation mismatch aborts current entry once");
    }
    private static void CustomTeleportOwnership()
    {
        // sceneLoaded can precede target activation and replacement of the old scene's Main.
        var r=Reset();BossRushMapSelectionHelper.Pending=false;
        CharacterMainControl.Main=new CharacterMainControl();var departing=CharacterMainControl.Main;
        r._owner.Legacy(Stand,Target());Ticks(r,2);
        Check(departing.PositionWrites==0,"custom target waits while main scene remains active");
        UnityEngine.Object.Destroy(departing);Ready(Target());Ticks(r,2);
        Check(At(Stand)&&r._owner.LegacySetups==1,"same entry permits official replacement Main in actual target scene");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());
        r._owner.Legacy(Vector3.zero);Ticks(r,2);
        Check(At(Vector3.zero)&&r._owner.LegacySetups==1,"zero is a valid configured custom landing");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());r._owner.ModeESelected=true;
        int writes=CharacterMainControl.Main.PositionWrites;r._owner.Legacy(Stand);Ticks(r,2);
        Check(At(Ordinary)&&CharacterMainControl.Main.PositionWrites==writes&&r._owner.LegacySetups==1,"Mode E preserves official landing and setup");
        Ticks(r,1);Check(r._owner.VerificationAfterWait==1&&r._owner.LegacySetupCompletions==1,"normal Mode E nested verification completes and spawns common NPCs once");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());LevelManager.AfterInit=false;
        r._owner.Legacy(Stand);Ticks(r,3);
        Check(At(Ordinary)&&r._owner.LegacySetups==0,"custom waits through official final placement window");
        LevelManager.AfterInit=true;Ticks(r,2);
        Check(At(Stand)&&r._owner.LegacySetups==1,"custom starts once official AfterInit completes");

        foreach(string blocker in new[]{"active","pending","startup"})
        {
            r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());MultiSceneCore.Instance.IsLoading=true;
            var old=r._owner.Legacy(Stand);writes=CharacterMainControl.Main.PositionWrites;
            if(blocker=="active")r._owner.IsZombieModeActive=true;
            else if(blocker=="pending")ZombieModeMapSelectionHelper.HasPendingZombieEntry=true;
            else r._owner.ZombieStarting=true;
            Ticks(r,1);MultiSceneCore.Instance.IsLoading=false;Ticks(r,2);
            Check(old.Stopped&&At(Ordinary)&&CharacterMainControl.Main.PositionWrites==writes&&r._owner.LegacySetups==0,
                "Zombie takeover cancels loading custom before any position write: "+blocker);
        }

        foreach(string cancellation in new[]{"scene","same-name-new-handle","player","death","load-event","unload-event","owner","zombie-tail"})
        {
            r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());
            var old=r._owner.Legacy(Stand); // final 0.5s wait, bound to this exact scene and Main
            var previous=CharacterMainControl.Main;writes=previous.PositionWrites;
            if(cancellation=="scene")SceneManager.Active=new Scene{name="elsewhere",handle=9,isLoaded=true};
            else if(cancellation=="same-name-new-handle")SceneManager.Active=new Scene{name="arena",handle=9,isLoaded=true};
            else if(cancellation=="player") { UnityEngine.Object.Destroy(previous);CharacterMainControl.Main=new CharacterMainControl(); }
            else if(cancellation=="death")previous.Health.IsDead=true;
            else if(cancellation=="load-event")SceneLoader.BeginLoad();
            else if(cancellation=="unload-event")SceneManager.Unload(Target());
            else if(cancellation=="owner")r._owner.DestroyOwner();
            else r._owner.IsZombieModeActive=true;
            Ticks(r,3);while(old.Body.MoveNext()) { }
            Check(r._owner.LegacySetups==0&&previous.PositionWrites==writes&&
                (cancellation!="player"||CharacterMainControl.Main.PositionWrites==0),
                "tail-frame cancellation prevents stale writes/setup including manually resumed task: "+cancellation);
        }

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());
        var stale=r._owner.Legacy(Stand);r._owner.Legacy(Vector3.zero);Ticks(r,2);while(stale.Body.MoveNext()) { }
        Check(At(Vector3.zero)&&r._owner.LegacySetups==1,"new custom generation exclusively owns position and continuation");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());
        stale=r._owner.Legacy(Stand);r._owner.OrdinaryEntry(Target());Ticks(r,2);while(stale.Body.MoveNext()) { }
        Check(At(Ordinary)&&r._owner.LegacySetups==0,"new ordinary entry generation also cancels the old custom request");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());MultiSceneCore.Instance.IsLoading=true;
        stale=r._owner.Legacy(Stand);Time.unscaledDeltaTime=31;Ticks(r,2);MultiSceneCore.Instance.IsLoading=false;Ticks(r,2);
        Check(stale.Stopped&&At(Ordinary)&&r._owner.LegacySetups==0,"loading timeout cancels without late movement when loader settles");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());SceneLoader.IsSceneLoading=true;
        stale=r._owner.Legacy(Stand);Time.unscaledDeltaTime=31;Ticks(r,2);SceneLoader.IsSceneLoading=false;Ticks(r,2);
        Check(stale.Stopped&&At(Ordinary)&&r._owner.LegacySetups==0,"global loader timeout also cancels before a late release");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());LevelManager.AfterInit=false;
        stale=r._owner.Legacy(Stand);Ticks(r,305);LevelManager.AfterInit=true;Ticks(r,2);
        Check(stale.Stopped&&At(Ordinary)&&r._owner.LegacySetups==0,"official readiness timeout cannot fall through into teleport");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());
        r._owner.Legacy(Stand);Ticks(r,1);Check(r._owner.LegacySetups==1&&r._owner.LegacySetupCompletions==0,"custom setup continuation enters owned wait");
        SceneLoader.BeginLoad();Ticks(r,2);
        Check(r._owner.LegacySetupCompletions==0,"scene load cancels custom GroundZero continuation");
        int cleanup=r._owner.CleanupTicks;Ticks(r,2);
        Check(r._owner.CleanupTicks==cleanup,"scene load also cancels independently scheduled continuous clearing");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());r._owner.ModeESelected=true;
        r._owner.Legacy(Stand);Ticks(r,2);
        Check(r._owner.VerificationEntered==1&&r._owner.VerificationAfterWait==0,"real GroundZero Mode E branch drives nested verification");
        SceneLoader.BeginLoad();Ticks(r,2);
        Check(r._owner.VerificationDisposed==1&&r._owner.VerificationAfterWait==0&&r._owner.LegacySetupCompletions==0,
            "Mode E verification is disposed and cannot recover or spawn NPCs after cancellation");

        r=Reset();BossRushMapSelectionHelper.Pending=false;Ready(Target());MultiSceneCore.Instance=null;
        r._owner.ForceFallback(Stand);Ticks(r,3);
        Check(At(Stand)&&r._owner.LegacySetups==1,"real ForceTeleport no-core fallback uses owned active-scene custom entry");
    }
    private static void ReadinessGates()
    {
        var unpaid=Reset();BossRushMapSelectionHelper.Paid=false;Ready(Target());
        unpaid.OnSceneLoaded(new SceneRuntimeContext(Target()));Ticks(unpaid,2);
        Check(unpaid.FreshStarted==0&&unpaid.Aborts==1&&ModeHArenaIsolationLease.Acquired==0,
            "fresh target without actual payment receipt cannot acquire run or leases");
        var navigation=Reset();Ready(Target());AstarPath.active.isScanning=true;
        navigation.OnSceneLoaded(new SceneRuntimeContext(Target()));Ticks(navigation,4);
        Check(navigation.FreshStarted==0&&navigation.Aborts==0,"pending navigation scan waits before arena selection");
        AstarPath.active.isScanning=false;Ticks(navigation,2);
        Check(navigation.FreshStarted==1,"completed navigation scan allows the existing entry to continue");

        var nested=Reset();Ready(Target());MultiSceneCore.Instance.IsLoading=true;
        nested.OnSceneLoaded(new SceneRuntimeContext(Target()));Ticks(nested,3);
        Check(nested.FreshStarted==0,"subscene teleporter still owns position after level initialization");
        MultiSceneCore.Instance.IsLoading=false;Ticks(nested,2);
        Check(nested.FreshStarted==1,"nested subscene completes after global AfterInit was already true");

        var r=Reset();r.OnSceneLoaded(new SceneRuntimeContext(Target()));Ready(Target());
        CharacterMainControl.Main.Health.IsDead=true;Ticks(r,2);Check(r.FreshStarted==0,"dead player cannot acquire spectator");
        UnityEngine.Object.Destroy(CharacterMainControl.Main);Ticks(r,2);Check(r.FreshStarted==0,"destroyed player compares as null");
        CharacterMainControl.Main=new CharacterMainControl();SceneManager.Active=new Scene{name="arena",handle=77,isLoaded=true};
        Ticks(r,2);Check(r.FreshStarted==0,"same scene name with different handle is not the target instance");
        SceneManager.Active=Target();Ticks(r,2);Check(r.FreshStarted==1,"valid replacement player and exact scene can finish");

        r=Reset();r.OnSceneLoaded(new SceneRuntimeContext(Target()));Time.unscaledDeltaTime=31;BossRushUI.Paused=true;
        Ticks(r,3);Check(r.Aborts==0,"pause does not spend ready timeout");
        BossRushUI.Paused=false;Ticks(r,1);Check(r.Aborts==1&&!BossRushMapSelectionHelper.Pending,"timeout uses existing abort/refund route once");
        Ticks(r,3);Check(r.Aborts==1,"completed timeout cannot repeat refund");
    }
    private static async Task Resume()
    {
        var r=Reset();r.BeginResume(BossRushMapSelectionHelper.Generation);
        var gate=new TaskCompletionSource<bool>();SceneLoader.Instance.Load=()=>gate.Task;
        Task loading=r.LoadResume();r.OnSceneLoaded(new SceneRuntimeContext(Target()));
        Ready(Target());gate.SetResult(true);await loading;
        Check(r.ResumePending&&r.PendingRoutine!=null&&r.Resumed==0,"outer LoadScene completion preserves pending ready owner");
        Ticks(r,2);Check(r.Resumed==1&&r.FreshStarted==0&&At(Stand)&&!r.ResumePending,"resume takes leases after readiness without new season");

        r=Reset();r.BeginResume(BossRushMapSelectionHelper.Generation);
        gate=new TaskCompletionSource<bool>();SceneLoader.Instance.Load=()=>gate.Task;
        loading=r.LoadResume();BossRushMapSelectionHelper.Freeze("replacement","replacement-main");
        Ready(new Scene{name="intermediate",handle=5,isLoaded=true});gate.SetResult(true);await loading;
        Check(MultiSceneCore.Instance.Teleports==0&&r.Resumed==0&&BossRushMapSelectionHelper.Pending,
            "late resume load cannot navigate or clear a replacement entry intent");

        foreach(string name in new[]{"Level_StormZone_B0","Level_SnowMilitaryBase_ColdStorage"})
        {
            r=Reset(name,name+"_Main");r.BeginResume(BossRushMapSelectionHelper.Generation);
            var captured=r;var target=Target(name);
            Ready(new Scene{name="intermediate",handle=5,isLoaded=true});
            MultiSceneCore.Instance.Teleport=(id,pos)=>
            {
                MultiSceneCore.Instance.IsLoading=true;
                captured.OnSceneLoaded(new SceneRuntimeContext(target));
                CharacterMainControl.Main.SetPosition(pos);SceneManager.Active=target;MultiSceneCore.Instance.IsLoading=false;
                return Task.CompletedTask;
            };
            await r.LoadResume();
            Check(MultiSceneCore.Instance.Teleports==1&&MultiSceneCore.Instance.Target==name,"resume routes exact nested target: "+name);
            Ticks(r,2);Check(r.Resumed==1&&r.FreshStarted==0&&At(Stand),"nested resume final position follows official teleport");
        }
    }
}
