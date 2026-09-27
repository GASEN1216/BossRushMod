using System;
namespace UnityEngine {
 public class Object { public bool Destroyed; public static bool operator ==(Object a,Object b){return (ReferenceEquals(a,null)||a.Destroyed)?ReferenceEquals(b,null)||b.Destroyed:ReferenceEquals(a,b);} public static bool operator !=(Object a,Object b){return !(a==b);} public override bool Equals(object o){return ReferenceEquals(this,o);} public override int GetHashCode(){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);} public static void Destroy(GameObject o){if(ReferenceEquals(o,null))return;o.Destroyed=true;foreach(var c in o.Components)c.Destroyed=true;} }
 public class GameObject:Object {public System.Collections.Generic.List<Object> Components=new System.Collections.Generic.List<Object>();public void SetActive(bool value){} }
 public struct Vector3 {} public class MonoBehaviour:Object {} public class Coroutine {}
}
namespace UnityEngine.SceneManagement {
 public struct Scene { public int handle; }
 public static class SceneManager { public static int Handle=1; public static Scene GetActiveScene(){return new Scene{handle=Handle};} }
}
namespace ItemStatsSystem { public sealed class Item {} }
public sealed class Health:UnityEngine.Object { public bool IsDead; }
public sealed class InteractableLootbox {}
public sealed class CharacterRandomPreset {}
public sealed class CharacterMainControl:UnityEngine.Object { public static CharacterMainControl Main; public Health Health=new Health(); public bool dropBoxOnDead; public UnityEngine.GameObject gameObject=new UnityEngine.GameObject(); public CharacterMainControl(){gameObject.Components.Add(this);gameObject.Components.Add(Health);} }
namespace BossRush {
 // This fixture links only the arena generation owner; loot event cleanup has its own guard.
 internal sealed partial class WavesArenaRuntimeModule { private void ReleaseBossRandomLootTrackingOnDestroy() {} }
 public class EnemyPresetInfo { public string name; }
 internal sealed class ModeDItemPool {}
 internal sealed partial class ModeDRuntimeModule {
  internal int CompletionCalls;
  private void TickModeDIntegrity(float deltaTime) {}
  private void OnModeDWaveComplete() { CompletionCalls++; modeDWaveCompletePending=true; }
 }
 internal class SceneRuntimeContext {}
 internal abstract class BossRushRuntimeModuleBase {
  public abstract string ModuleName {get;}
  public virtual void OnUpdate(float d,float u){}
  public virtual void OnAwake(ModBehaviour o){}
  public virtual void OnDestroy(){}
  public virtual void OnSceneLoaded(SceneRuntimeContext c){}
 }
 internal class InfiniteHellMilestoneDelivery { public void Tick(ModBehaviour o){} public void Enqueue(int t,UnityEngine.Vector3 v){} }
 public class ModBehaviour { public void StopCoroutine(UnityEngine.Coroutine c){} public void ClearModeDEnemyRecoveryState(){} public void ClearModeDMutators(string mode){} public void UnregisterEnemyRecoveryForArena(CharacterMainControl c){} public bool IsActive,IsModeDActive;public int ModeDWaveIndex;public void TickModeDIntegrity(float d){} public static void DevLog(string message){} }
 static class Program {
  static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
  static void Main(){
   var host=new ModBehaviour{IsActive=true};var module=new WavesArenaRuntimeModule();module.OnAwake(host);
   for(int i=0;i<22;i++)module.EnemyPresets.Add(new EnemyPresetInfo{name="normal"+i});
   module.EnemyPresets[1].name="DragonDescendant";
   module.EnsureEarlyWavesNoStrongBoss();
   Check(module.EnemyPresets[1].name=="normal20"&&module.EnemyPresets[20].name=="DragonDescendant"&&module.EnemyPresets.Count==22,"early-wave strong boss moved without changing pool size");
   CharacterMainControl.Main=new CharacterMainControl();
   var old=WavesArenaRuntimeModule.CaptureValidity(host,true,true);Check(old(),"active current wave accepted");
   host.IsActive=false;Check(!old(),"ended run rejected");host.IsActive=true;
   var current=WavesArenaRuntimeModule.CaptureValidity(host,true,true);Check(!old()&&current(),"new wave rejects old result");
   module.OnSceneLoaded(new SceneRuntimeContext());Check(!current(),"same scene reload invalidates generation");
   current=WavesArenaRuntimeModule.CaptureValidity(host,true,true);
   CharacterMainControl.Main.Health.IsDead=true;Check(!current(),"death invalidates spawn");
   CharacterMainControl.Main=new CharacterMainControl();Check(!current(),"replacement player cannot receive old result");
   host.IsActive=false;var victory=WavesArenaRuntimeModule.CaptureValidity(host,false,false);Check(victory(),"victory continuation allowed with inactive combat");
   UnityEngine.SceneManagement.SceneManager.Handle++;Check(!victory(),"victory rejected after scene change");
   victory=WavesArenaRuntimeModule.CaptureValidity(host,false,false);module.OnDestroy();Check(!victory(),"host cleanup invalidates continuation");
   var modeD=new ModeDRuntimeModule();modeD.OnAwake(host);modeD.modeDActive=true;modeD.modeDWaveIndex=1;
   host.IsModeDActive=false;host.ModeDWaveIndex=99;
   var oldD=ModeDRuntimeModule.CaptureValidity(host,true);Check(oldD(),"Mode D current dispatch accepted");
   ModeDRuntimeModule.Invalidate(host);modeD.modeDActive=false;Check(!oldD(),"Mode D ended queue rejected before next dispatch");
   modeD.modeDActive=true;modeD.modeDWaveIndex=1;var nextD=ModeDRuntimeModule.CaptureValidity(host,true);
   Check(!oldD()&&nextD(),"Mode D reused wave 1 rejects old callbacks and automatic-next-wave task");
   modeD.modeDExpectedEnemiesInCurrentWave=2;
   modeD.ResolveModeDSpawnCount(99);Check(modeD.modeDSpawnResolvedInCurrentWave==0,"late Mode D spawn cannot settle the current wave");
   modeD.ResolveModeDSpawnCount(1);Check(modeD.CompletionCalls==0,"empty Mode D wave waits for every spawn result");
   var survivor=new CharacterMainControl();modeD.modeDCurrentWaveEnemies.Add(survivor);
   modeD.ResolveModeDSpawnCount(1);Check(modeD.CompletionCalls==0,"resolved Mode D wave waits for living enemies");
   survivor.Health.IsDead=true;modeD.TryResolveModeDWaveComplete();
   Check(modeD.CompletionCalls==1&&modeD.modeDCurrentWaveEnemies.Count==0,"dead Mode D enemy is pruned before completing a fully resolved wave");
   modeD.TryResolveModeDWaveComplete();Check(modeD.CompletionCalls==1,"pending Mode D completion is idempotent");
   modeD.OnSceneLoaded(new SceneRuntimeContext());Check(!nextD(),"Mode D same-scene reload invalidates queued work");
   nextD=ModeDRuntimeModule.CaptureValidity(host,true);modeD.OnDestroy();Check(!nextD(),"Mode D runtime destruction invalidates queued work");
  }
 }
}
