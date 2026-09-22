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
        CharacterMainControl.Main=null;LevelManager.LevelInited=false;LevelManager.AfterInit=false;SceneLoader.IsSceneLoading=true;
        SceneLoader.Instance=new SceneLoader{Load=()=>Task.CompletedTask};
        MultiSceneCore.Instance=new MultiSceneCore{IsLoading=true};
        SceneManager.Active=new Scene{name="loading",handle=1,isLoaded=true};
        BossRushMapSelectionHelper.Pending=false;BossRushMapSelectionHelper.Generation=0;
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
        using(var maps=JsonDocument.Parse(File.ReadAllText(args[0])))
            foreach(var map in maps.RootElement.EnumerateArray())
                NewMapEntry(map.GetProperty("scene").GetString(),map.GetProperty("id").GetString());
        LegacyOrdering();Cancellation();ReadinessGates();Resume().GetAwaiter().GetResult();
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
    private static void ReadinessGates()
    {
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
