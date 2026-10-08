using System;
using System.Collections;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static ModBehaviour previous;
    private static Scene arena;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception("ASSERT: " + message + "; effects=" + string.Join(",", Probe.Effects));
    }
    private static ModBehaviour Host(string mode="D", bool ready=true)
    {
        if (previous != null) previous.DestroyOwner();
        Scheduler.Reset();
        Probe.Effects.Clear(); Probe.Disposal.Clear(); Probe.Entry=mode;
        Probe.SignFrames=0; Probe.VerificationFrames=0; Probe.CustomWaiting=false; Probe.CoroutineWaiting=false;
        SceneManager.Names.Clear(); SceneManager.Loaded.Clear();
        arena=SceneManager.Add(1, "Level_DemoChallenge_1");
        SceneManager.Add(2, "BossRush_SkyIsland"); SceneManager.Add(3, "Level_DemoChallenge_1");
        SceneManager.Active=1; SceneLoader.IsSceneLoading=false;
        LevelManager.LevelInited=ready; GameCamera.Instance=new GameCamera();
        CharacterMainControl.Main=new CharacterMainControl();
        previous=new ModBehaviour(); return previous;
    }
    private static void Depart(bool notify=true)
    {
        if(notify)SceneLoader.BeginLoad();
        SceneManager.Unload(arena);
        UnityEngine.Object.Destroy(CharacterMainControl.Main.gameObject);
        CharacterMainControl.Main=new CharacterMainControl();
        SceneManager.Active=2; SceneLoader.IsSceneLoading=false;
        LevelManager.LevelInited=true; Probe.Entry="D";
    }
    private static void NoLateEffects(ModBehaviour host, string cause)
    {
        int count=Probe.Effects.Count;
        Scheduler.Tick(350);
        Check(Probe.Effects.Count==count && !host.modeDRuntime.IsActive, cause);
    }
    private static IEnumerator Child()
    {
        try { yield return Grandchild(); Probe.Touch("child-completed"); }
        finally { Probe.Disposal.Add("child"); }
    }
    private static IEnumerator Grandchild()
    {
        try { yield return null; Probe.Touch("grandchild-completed"); }
        finally { Probe.Disposal.Add("grandchild"); }
    }
    private static void Run()
    {
        foreach(string mode in new[]{"Normal","D","E","F","G","H"})
        {
            var host=Host(mode); Scheduler.Start(host.Entry(arena)); Scheduler.Tick(12);
            bool h=mode=="H", simple=mode=="Normal"||mode=="D";
            Check(host.modeDRuntime.IsActive==(mode=="D"), "actual Mode D activation only follows real naked entry: "+mode);
            Check(Probe.Has("starter")== (mode=="D"), "actual starter kit only follows Mode D: "+mode);
            Check(Probe.Has("npcs")==!h, "complete real setup preserves NPC branch: "+mode);
            Check(Probe.Has("teleport")==simple, "real DEMO teleport remains in normal / D branch: "+mode);
            if(mode=="E")Check(Probe.Has("verified") && !Probe.Has("warmup-stop"), "nested Mode E verification completes before branch choice");
        }

        var owner=Host(ready:false); Scheduler.Start(owner.Entry(arena));
        Depart(); NoLateEffects(owner,"old unloaded arena wait never activates naked island visitor after 30 seconds");

        owner=Host(ready:false); Scheduler.Start(owner.Entry(arena));
        Depart(false); NoLateEffects(owner,"scene unload cancels even without official loader notification");

        owner=Host(ready:false); Scheduler.Start(owner.Entry(arena));
        Scheduler.Tick(350); Check(!Probe.Has("inventory") && !Probe.Has("teleport"), "30 second timeout never dispatches setup");
        LevelManager.LevelInited=true; NoLateEffects(owner,"timeout cannot revive on later ready conditions");

        owner=Host(ready:false); Scheduler.Start(owner.Entry(arena));
        CharacterMainControl.Main=new CharacterMainControl(); LevelManager.LevelInited=true;
        NoLateEffects(owner,"role replacement during readiness wait cancels old request");

        owner=Host(); Scheduler.Start(owner.Entry(arena));
        Depart(); NoLateEffects(owner,"scene change during initial 0.5 second setup delay cancels before inventory or teleport");

        owner=Host(); Scheduler.Start(owner.Entry(arena));
        SceneLoader.BeginLoad(); SceneLoader.IsSceneLoading=false;
        NoLateEffects(owner,"official new load cancels before the original arena is unloaded");

        owner=Host(); Scheduler.Start(owner.Entry(arena)); Scheduler.Tick();
        Check(Probe.Has("teleport")&&!owner.modeDRuntime.IsActive,"real setup is suspended immediately before delayed D activation");
        Depart(); NoLateEffects(owner,"departure after second teleport cancels delayed Mode D and NPC side effects");

        owner=Host(); Scheduler.Start(owner.Entry(arena)); Scheduler.Tick();
        CharacterMainControl.Main=new CharacterMainControl();
        NoLateEffects(owner,"replacement before delayed Mode D activation cannot inherit entry");

        owner=Host(); Scheduler.Start(owner.Entry(arena));
        SceneManager.Active=3;
        NoLateEffects(owner,"same-name scene with another handle never inherits old entry");

        owner=Host(ready:false); Scheduler.Start(owner.Entry(arena));
        LevelManager.LevelInited=true; Scheduler.Start(owner.Entry(arena)); Scheduler.Tick(12);
        Check(owner.modeDRuntime.IsActive && Probe.Effects.FindAll(x=>x=="mode-d").Count==1, "successor request cancels waiting predecessor and starts exactly once");

        owner=Host(); Scheduler.Start(owner.Entry(arena)); Scheduler.Tick();
        Scheduler.Start(owner.Entry(arena)); Scheduler.Tick(12);
        Check(owner.modeDRuntime.IsActive && Probe.Effects.FindAll(x=>x=="mode-d").Count==1,"successor request also cancels an already-dispatched setup before delayed activation");

        owner=Host(); Scheduler.Start(owner.Entry(arena));
        owner.DestroyOwner(); NoLateEffects(owner,"destroyed module never dispatches old setup");

        owner=Host(); Scheduler.Start(owner.Entry(arena));
        CharacterMainControl.Main.Health.IsDead=true;
        NoLateEffects(owner,"player death cancels pending setup");

        owner=Host(); Scheduler.Start(owner.Entry(arena));
        UnityEngine.Object.Destroy(CharacterMainControl.Main.gameObject);
        NoLateEffects(owner,"destroyed player remaining in static Main cannot receive delayed setup");

        owner=Host("E"); Probe.VerificationFrames=12; Scheduler.Start(owner.Entry(arena)); Scheduler.Tick(2);
        Check(Probe.Has("mode-e")&&!Probe.Has("verified"),"Mode E verification really suspends inside a nested child");
        Depart(); int before=Probe.Effects.Count; Scheduler.Tick(20);
        Check(Probe.Effects.Count==before,"cancelled Mode E verification cannot dispatch NPC or fallback setup");
        Check(string.Join(",",Probe.Disposal)=="verification-child,verification-parent", "cancel releases child before parent exactly once");

        owner=Host(); Probe.SignFrames=12; Scheduler.Start(owner.Entry(arena)); Scheduler.Tick();
        Check(Probe.Has("sign")&&!Probe.Has("sign-retry"),"asynchronous sign retry is pending on its own owned task");
        Depart(); NoLateEffects(owner,"sign retry cannot create an arena entry on the island");
        Check(Probe.Disposal.Contains("sign"),"departure disposes pending sign retry");

        owner=Host(); Scheduler.Start(owner.Entry(arena));
        Probe.CustomWaiting=true; Probe.CoroutineWaiting=true;
        owner.bossRushIntegrationRuntime.StartArenaEntryContinuation(owner.CustomAndCoroutine(Child()));
        Scheduler.Tick();
        Check(Probe.Has("grandchild-completed")&&Probe.Has("child-completed"),"ordinary nested IEnumerator children are recursively executed");
        Scheduler.Tick(3); Check(!Probe.Has("native-wait-finished"),"CustomYieldInstruction retains native wait semantics");
        Probe.CustomWaiting=false; Scheduler.Tick();
        Check(Probe.Has("native-wait-finished")&&!Probe.Has("coroutine-finished"),"Coroutine yield retains native wait semantics");
        Probe.CoroutineWaiting=false; Scheduler.Tick(2); Check(Probe.Has("coroutine-finished"),"native coroutine resumes exactly after completion");
        Check(Probe.Disposal.IndexOf("grandchild")<Probe.Disposal.IndexOf("child"),"successful nested enumerators dispose in reverse order");

        owner=Host(ready:false); SceneLoader.IsSceneLoading=true; CharacterMainControl.Main=null;
        Scheduler.Start(owner.Entry(arena)); Scheduler.Tick(2);
        CharacterMainControl.Main=new CharacterMainControl(); SceneLoader.IsSceneLoading=false; LevelManager.LevelInited=true;
        Scheduler.Tick(10); Check(owner.modeDRuntime.IsActive,"entry binds a late official player after initial sceneLoaded");
    }
    public static int Main()
    {
        try { Run(); previous.DestroyOwner(); Scheduler.Reset(); Console.WriteLine("LegacyArenaEntry: PASS "+checks+" checks"); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
