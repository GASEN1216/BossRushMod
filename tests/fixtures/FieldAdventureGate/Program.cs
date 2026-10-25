using System;
using BossRush;
using Duckov.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;
    private static ModBehaviour owner;
    private static Scene Scene(string name, int id) { return new Scene { name = name, handle = id, isLoaded = true }; }
    private static void Check(bool pass, string label) { checks++; if (!pass) throw new Exception(label); }
    private static void Reset()
    {
        owner = new ModBehaviour();
        ModBehaviour.Instance = owner;
        ModBehaviour.ModeG = ModBehaviour.ModeH = false;
        LevelManager.Instance = new LevelManager { IsRaidMap = true };
        LevelManager.Instance.gameObject.scene = Scene("Raid_Main", 1);
        LevelManager.AfterInit = true;
        CharacterMainControl.Main = new CharacterMainControl { Health = new Health() };
        MultiSceneCore.Instance = null;
        MultiSceneCore.ActiveSubScene = null;
        SceneLoader.IsSceneLoading = false;
        WavesArenaRuntimeModule.ArenaActive = WavesArenaRuntimeModule.ArenaPlanned = false;
        BossRushMapSelectionHelper.Pending = BossRushPendingEntryKind.None;
        SpawnPositionHelper.Anchor = SpawnPositionHelper.Reachable = true;
        SpawnPositionHelper.Queries = 0;
        SpawnPositionHelper.Offset = Vector3.zero;
        Physics.Ground = true;
        Physics.Wall = false;
        Physics.Slope = 1;
        Physics.GroundOffset = 0;
        Physics.Raycasts = 0;
        FieldAdventureRoute.Walkable = true;
        FieldAdventureRoute.Queries = 0;
    }
    private static bool Allowed() { CharacterMainControl player; Scene scene; return FieldAdventureSupport.TryGetRaid(owner,out player,out scene); }
    private static void Reject(Action mutation, string label) { Reset(); mutation(); Check(!Allowed(),label); }
    private static void Main()
    {
        Reset();
        Check(Allowed(),"ordinary raid has a reachable player entry");
        Reject(()=>LevelManager.Instance.IsBaseLevel=true,"base does not create encounters");
        Reject(()=>LevelManager.Instance.IsRaidMap=false,"non-raid scenes do not create encounters");
        Reject(()=>LevelManager.AfterInit=false,"loading player cannot receive offers");
        Reject(()=>SceneLoader.IsSceneLoading=true,"transition invalidates live activity");
        Reject(()=>CharacterMainControl.Main.Health.IsDead=true,"death invalidates activity");
        Reject(()=>CharacterMainControl.Main=null,"no main player has no activity");
        Reject(()=>LevelManager.Instance=null,"no level owner has no activity");
        Reject(()=>owner=null,"no host has no activity");
        Reject(()=>owner.IsActive=true,"arena blocked");
        Reject(()=>owner.IsModeDActive=true,"mode D blocked");
        Reject(()=>owner.IsModeEActive=true,"mode E blocked");
        Reject(()=>owner.IsModeFActive=true,"mode F blocked");
        Reject(()=>owner.IsZombieModeActive=true,"zombie blocked");
        Reject(()=>owner.IsBossRushArenaActive=true,"idle arena blocked");
        Reject(()=>owner.IsCampaignFinalBossActive=true,"final battle blocked");
        Reject(()=>owner.CampaignArenaActiveForRuntime=true,"campaign arena blocked");
        Reject(()=>ModBehaviour.ModeG=true,"mode G blocked");
        Reject(()=>ModBehaviour.ModeH=true,"mode H blocked");
        Reject(()=>WavesArenaRuntimeModule.ArenaPlanned=true,"pending arena blocked before spawn");
        Reject(()=>BossRushMapSelectionHelper.Pending=BossRushPendingEntryKind.Arena,"departure lease blocked");
        Reject(()=>owner.CampaignZombieRunForRuntime=new ZombieRun { LifecyclePhase=ZombieModeLifecyclePhase.Preparing },"preparing zombie blocked");
        Reject(()=>owner.ModeHRuntime=new ModeHRuntime { RunState=new ModeHState { Lifecycle=ModeHLifecycle.Preparing } },"preparing cup blocked");
        Reject(()=>MultiSceneCore.Instance=new MultiSceneCore { IsLoading=true },"subscene transition blocked");
        foreach (string invalid in new[] { "Base", "Resource", "LoadingScreen" })
            Reject(()=>LevelManager.Instance.gameObject.scene=Scene(invalid,2),"invalid scene " + invalid);
        Reset();
        MultiSceneCore.Instance = new MultiSceneCore();
        MultiSceneCore.Instance.gameObject.scene=Scene("Raid_Main",10);
        MultiSceneCore.ActiveSubScene=Scene("Raid_Interior",11);
        CharacterMainControl actual; Scene actualScene;
        Check(FieldAdventureSupport.TryGetRaid(owner,out actual,out actualScene) && actualScene.handle==11 && actual==CharacterMainControl.Main,
            "world objects belong to visible subscene while raid quota belongs to level owner");
        MultiSceneCore.ActiveSubScene=new Scene { name="OldInterior",handle=12,isLoaded=false };
        Check(FieldAdventureSupport.TryGetRaid(owner,out actual,out actualScene) && actualScene.handle==10,"unloaded child never receives objects");
        Reset(); Vector3 point;
        Check(FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point),"connected grounded clear candidate accepted");
        Check(point.sqrMagnitude>=64 && point.sqrMagnitude<=257 && SpawnPositionHelper.Queries==1,"accepted point preserves distance budget");
        Check(FieldAdventureRoute.Queries==1,"accepted point proves a walkable route exactly once");
        Reset(); FieldAdventureRoute.Walkable=false;
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point) && FieldAdventureRoute.Queries==12,"connected point the player cannot walk to is refused and route budget is bounded");
        Reset(); Physics.Wall=true;
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point) && FieldAdventureRoute.Queries==0,"route query runs only after cheap ground and clearance checks");
        Reset(); FieldAdventureRoute.Walkable=false;
        Check(!FieldAdventureSupport.TryFindGroundBands(Vector3.zero,new float[] { 28,45,20,36,14,28 },out point) && FieldAdventureRoute.Queries==36,"fallback bands keep the route requirement in every band");
        Reset(); SpawnPositionHelper.Anchor=false;
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point) && SpawnPositionHelper.Queries==0,"no anchor avoids expensive candidate queries");
        Reset(); SpawnPositionHelper.Reachable=false;
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point) && SpawnPositionHelper.Queries==12 && Physics.Raycasts==0,"unreachable points never bypass navigation and query count is bounded");
        Reset(); Physics.Ground=false;
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point),"navigation without solid floor refused");
        Reset(); Physics.Wall=true;
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point),"walls refuse placement");
        Reset(); Physics.Slope=0.5f;
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point),"steep slope refused");
        Reset(); Physics.GroundOffset=-2f;
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point),"lower floor cannot impersonate sampled navigation");
        Reset(); SpawnPositionHelper.Offset=new Vector3(100,0,0);
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,16,out point),"navigation snapping outside requested range refused");
        Reset();
        Check(!FieldAdventureSupport.TryFindGround(new Vector3(float.NaN,0,0),8,16,out point),"invalid origin refused");
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,16,8,out point),"reversed radius refused");
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,-1,8,out point),"negative radius refused");
        Check(!FieldAdventureSupport.TryFindGround(Vector3.zero,8,float.PositiveInfinity,out point),"infinite radius refused");
        Check(SpawnPositionHelper.Queries==0,"invalid requests never touch navigation");
        Console.WriteLine("PASS: " + checks + " production-linked raid gate and placement checks");
    }
}
