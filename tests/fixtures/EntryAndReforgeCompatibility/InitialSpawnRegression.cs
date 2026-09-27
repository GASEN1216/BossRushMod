using System;
using System.Threading.Tasks;
using BossRush;
using Duckov.Scenes;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class InitialSpawnRegression
{
    private static int checks;
    private static LevelManager level;
    private static readonly Vector3 Target = new Vector3(429.77f, .02f, 279.19f);
    private static void Check(bool ok,string reason) { checks++;if(!ok)throw new Exception(reason); }
    private static bool At(Vector3 a,Vector3 b) { return (a-b).sqrMagnitude<.0001f; }
    private static void Reset()
    {
        SceneLoader.IsSceneLoading=false;
        BossRushMapSelectionHelper.ClearPendingEntryFlowState();
        BossRushMapSelectionHelper.MarkEntryFlowFromMapSelectionUi(BossRushPendingEntryKind.None);
        BossRushMapSelectionHelper.SetPendingMapEntryIndex(0);
        BossRushMapSelectionHelper.EnoughTickets=true;
        BossRushMapSelectionHelper.Planned=true;
        ModBehaviour.Instance=new ModBehaviour();
        ModBehaviour.SpawnPosition=Target;
        ModBehaviour.Maps=new[] { new BossRushMapConfig { sceneName="basement",sceneID="main_id",customSpawnPos=Target } };
        level=new LevelManager();level.gameObject.scene=new Scene{name="main_scene",handle=10};
        SceneInfoCollection.Entries=new System.Collections.Generic.List<SceneInfoEntry> {
            new SceneInfoEntry { Id="main_id",SceneReference=new SceneReference{Name="main_scene"} } };
        MultiSceneCore.Instance=new MultiSceneCore();
        MultiSceneCore.Instance.SubScenes.Add(new SubSceneEntry { sceneID="basement_id",Info=new SceneInfoEntry{SceneReference=new SceneReference{Name="basement"}} });
        Harmony.Installed=new Patches();
        Harmony.Installed.Prefixes.Add(new Patch { PatchMethod=AccessTools.Method(typeof(BossRushInitialSpawnTeleportPatch),"Route") });
    }
    private static ValueTuple<string,SubSceneEntry.Location> Select()
    {
        var result=new ValueTuple<string,SubSceneEntry.Location>("ordinary",new SubSceneEntry.Location{path="StartPoints/1"});
        BossRushInitialSpawnSelectionPatch.Select(level,ref result);
        return result;
    }
    private static void Confirm() { BossRushMapSelectionHelper.ConfirmInitialSpawnSelection(true);SceneLoader.IsSceneLoading=true; }
    internal static async Task<int> Run()
    {
        Reset();SceneLoader.IsSceneLoading=true;
        Check(Select().Item1=="ordinary","click without confirmation cannot override spawn");
        Reset();var confirmation=new TaskCompletionSource<bool>();
        var observed=BossRushInitialSpawnConfirmationPatch.Observe(confirmation.Task);
        Check(!observed.IsCompleted&&BossRushMapSelectionHelper.TakeInitialSpawnSelection()==null,"await actual confirmation before freezing spawn");
        confirmation.SetResult(false);
        Check(!await observed&&BossRushMapSelectionHelper.TakeInitialSpawnSelection()==null,"cancel returns original false and keeps normal spawn");
        Reset();Check(await BossRushInitialSpawnConfirmationPatch.Observe(Task.FromResult(true)),"confirmation return preserved");
        SceneLoader.IsSceneLoading=true;BossRushMapSelectionHelper.CancelUnstartedMapSelection();
        var chosen=Select();
        Check(chosen.Item1=="basement_id"&&chosen.Item2.path==BossRushInitialSpawn.LocationPath,"resolve exact child by scene name, not sceneID or random first child");
        Check(At(chosen.Item2.position,Target),"first CreateMainCharacterAsync receives target world position");
        var core=MultiSceneCore.Instance;var gate=new TaskCompletionSource<bool>();core.Load=()=>gate.Task;
        var location=new MultiSceneLocation{SceneID=chosen.Item1,LocationName=chosen.Item2.path};
        Task<bool> task=null;
        Check(!BossRushInitialSpawnTeleportPatch.Route(core,location,ref task),"route synthetic location through official vector overload");
        Check(core.LoadedId=="basement_id"&&!core.UsedLocalCoordinates&&!task.IsCompleted&&!BossRushInitialSpawn.HasArrived(Target),"subscene loads asynchronously using world coordinates");
        gate.SetResult(true);Check(await task&&At(core.Position,chosen.Item2.position),"official subscene teleport stays at first spawn position");
        Check(BossRushInitialSpawn.HasArrived(Target),"successful first spawn suppresses legacy second move");
        Check(!BossRushInitialSpawn.HasArrived(new Vector3(1,2,3)),"arrival cannot suppress another destination");
        Check(Select().Item1=="ordinary","selection is consumed once");
        Check(BossRushInitialSpawnTeleportPatch.Route(core,location,ref task),"route token is consumed once");
        UnityEngine.Object.Destroy(core.gameObject);
        Check(!BossRushInitialSpawn.HasArrived(Target),"scene destruction invalidates component and arrival");

        Reset();Confirm();SceneLoader.IsSceneLoading=false;BossRushMapSelectionHelper.CancelUnstartedMapSelection();SceneLoader.IsSceneLoading=true;
        Check(Select().Item1=="ordinary"&&!BossRushMapSelectionHelper.Planned,"closed or failed UI cannot contaminate later normal raid");
        Reset();Confirm();BossRushMapSelectionHelper.MarkTargetSceneLoadStarted();SceneLoader.IsSceneLoading=false;
        BossRushMapSelectionHelper.CancelUnstartedMapSelection();
        Check(BossRushMapSelectionHelper.TakeInitialSpawnSelection()!=null&&BossRushMapSelectionHelper.Planned,"late UI close preserves started entry and prepaid ownership");
        Reset();Confirm();SceneLoader.IsSceneLoading=false;
        BossRushMapEntrySelectionPatch.Select(new Duckov.UI.MapSelectionEntry());SceneLoader.IsSceneLoading=true;
        Check(Select().Item1=="ordinary"&&!BossRushMapSelectionHelper.Planned,"ordinary map entry clears BossRush selection");
        Reset();BossRushMapSelectionHelper.EnoughTickets=false;Confirm();
        Check(Select().Item1=="ordinary","ticket unavailable at confirmation cannot freeze spawn");
        Reset();Confirm();BossRushMapSelectionHelper.MarkEntryFlowFromDirectTeleport(BossRushPendingEntryKind.None);
        Check(Select().Item1=="ordinary","direct travel does not reuse UI intent");
        Reset();Confirm();level.gameObject.scene=new Scene{name="unrelated",handle=90};
        Check(Select().Item1=="ordinary","unrelated main level cannot consume target position");
        level.gameObject.scene=new Scene{name="main_scene",handle=10};
        Check(Select().Item1=="ordinary","failed scene match discards intent");
        Reset();Confirm();core=MultiSceneCore.Instance;core.SubScenes.Clear();
        Check(Select().Item1=="ordinary","missing target subscene keeps original location");
        Reset();Confirm();Harmony.Installed=null;
        Check(Select().Item1=="ordinary","missing route patch cannot emit unresolvable path");

        for(int mode=0;mode<6;mode++) for(int custom=0;custom<2;custom++)
        {
            Reset();ModBehaviour.Instance.SetMode(mode);if(custom==0)ModBehaviour.Maps[0].customSpawnPos=null;
            Confirm();bool eligible=mode!=2&&mode!=5&&(custom==1||mode==0||mode==1);
            Check((Select().Item1=="basement_id")==eligible,"preserve actual mode positioning policy "+mode+"/"+custom);
        }
        Reset();Confirm();chosen=Select();core=MultiSceneCore.Instance;core.Load=()=>Task.FromResult(false);
        location=new MultiSceneLocation{SceneID=chosen.Item1,LocationName=chosen.Item2.path};
        var other=new MultiSceneLocation{SceneID="wrong",LocationName=chosen.Item2.path};
        Check(BossRushInitialSpawnTeleportPatch.Route(core,other,ref task),"unrelated teleport is untouched");
        BossRushInitialSpawnTeleportPatch.Route(core,location,ref task);
        Check(!await task&&!BossRushInitialSpawn.HasArrived(Target),"failed official load keeps legacy fallback enabled");
        Reset();Confirm();chosen=Select();core=MultiSceneCore.Instance;gate=new TaskCompletionSource<bool>();core.Load=()=>gate.Task;
        location=new MultiSceneLocation{SceneID=chosen.Item1,LocationName=chosen.Item2.path};
        BossRushInitialSpawnTeleportPatch.Route(core,location,ref task);UnityEngine.Object.Destroy(core.gameObject);gate.SetResult(true);await task;
        Check(!BossRushInitialSpawn.HasArrived(Target),"late async completion cannot restore destroyed scene owner");
        return checks;
    }
}
