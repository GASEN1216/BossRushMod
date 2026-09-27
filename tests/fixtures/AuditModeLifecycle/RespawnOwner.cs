using System;
using System.Threading.Tasks;
namespace UnityEngine { public struct Vector3{} }
namespace UnityEngine.SceneManagement {public struct Scene{public int buildIndex;}public static class SceneManager{public static Scene GetActiveScene(){return new Scene{buildIndex=1};}}}
namespace BossRush {
 public class EnemyPresetInfo {public string displayName="dragon";}
 public class EnemySpawnContext {}
 public class EnemySpawnCoreResult {public bool success; public string failureReason="controlled failure";}
 public class ModeEFSpawnProfiler {public ModeEFSpawnProfiler(string a,string b){}public void Mark(string s){}public void Complete(string s){}}
 public class ModeFState {public int RuntimeSessionToken;}
 public class ModBehaviour {
  public TaskCompletionSource<EnemySpawnCoreResult> Pending;
  internal static void DevLog(string s){}
  internal bool IsDragonDescendantPreset(EnemyPresetInfo p){return true;}
  internal Task<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync(EnemyPresetInfo p,UnityEngine.Vector3 pos,bool a,Func<bool> valid,int n,bool skipDragonDescendant,bool skipDragonKing,bool deferActivationUntilNextFrame,Func<EnemySpawnContext,bool> onCommit){Pending=new TaskCompletionSource<EnemySpawnCoreResult>();return Pending.Task;}
 }
 public class ModeEFEnemySpawnRuntime {
  internal bool DragonDescendantSpawned;
  internal void EnsureModeEFSpawnPoolsReady(string s){}
 }
 internal sealed partial class ModeFRuntimeModule {
  private ModBehaviour owner;
  private ModeEFEnemySpawnRuntime spawnRuntime;
  internal ModeFRuntimeModule(ModBehaviour host, ModeEFEnemySpawnRuntime shared){owner=host;spawnRuntime=shared;}
  public TaskCompletionSource<EnemySpawnCoreResult> Pending { get { return owner.Pending; } }
  private ModeFState modeFState=new ModeFState();
  private bool modeFActive=true;
  public int Completions;
  private UnityEngine.Vector3 FindSpawnPointAwayFromPlayer(float f){return new UnityEngine.Vector3();}
  private EnemyPresetInfo GetRandomModeFRespawnBossPreset(){return new EnemyPresetInfo();}
  private bool IsModeFSessionStillValid(int s,int scene){return modeFActive&&modeFState.RuntimeSessionToken==s&&scene==1;}
  private bool ConfigureModeFRespawnedBoss(EnemySpawnContext c,bool d,UnityEngine.Vector3 p){return true;}
  private void CompleteModeFBossRespawnAttempt(bool success,bool requeueOnFailure){Completions++;}
  public Task Begin(int s){modeFState.RuntimeSessionToken=s;return RespawnModeFBossAsync();}
  public bool DragonReserved{get{return spawnRuntime.DragonDescendantSpawned;}}
 }
 public static class Program {
  static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
  public static async Task Main(){
   foreach(string outcome in new[]{"null","fault","failure","success"}){
    var owner=new ModeFRuntimeModule(new ModBehaviour(),new ModeEFEnemySpawnRuntime());var first=owner.Begin(1);var stale=owner.Pending;
    var second=owner.Begin(2);var current=owner.Pending;
    if(outcome=="fault")stale.SetException(new InvalidOperationException());else stale.SetResult(outcome=="null"?null:new EnemySpawnCoreResult{success=outcome=="success"});
    await first;Check(owner.Completions==0&&owner.DragonReserved,"stale respawn "+outcome+" leaves successor reservation and counters intact");
    current.SetResult(new EnemySpawnCoreResult{success=true});await second;Check(owner.Completions==1,"current respawn completes exactly once "+outcome);
   }
   var active=new ModeFRuntimeModule(new ModBehaviour(),new ModeEFEnemySpawnRuntime());var task=active.Begin(1);active.Pending.SetResult(new EnemySpawnCoreResult());await task;
   Check(active.Completions==1&&!active.DragonReserved,"current respawn failure releases own reservation");
  }
 }
}
