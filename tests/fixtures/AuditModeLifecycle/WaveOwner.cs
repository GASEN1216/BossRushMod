using System;
namespace UnityEngine { public struct Vector3 {} }
namespace UnityEngine.SceneManagement {
 public struct Scene { public int handle; }
 public static class SceneManager { public static int Handle=1; public static Scene GetActiveScene(){return new Scene{handle=Handle};} }
}
public sealed class Health { public bool IsDead; }
public sealed class CharacterMainControl { public static CharacterMainControl Main; public Health Health=new Health(); }
namespace BossRush {
 internal class SceneRuntimeContext {}
 internal abstract class BossRushRuntimeModuleBase {
  public abstract string ModuleName {get;}
  public virtual void OnUpdate(float d,float u){}
  public virtual void OnAwake(ModBehaviour o){}
  public virtual void OnDestroy(){}
  public virtual void OnSceneLoaded(SceneRuntimeContext c){}
 }
 internal class InfiniteHellMilestoneDelivery { public void Tick(ModBehaviour o){} public void Enqueue(int t,UnityEngine.Vector3 v){} }
 public class ModBehaviour { public bool IsActive,IsModeDActive;public int ModeDWaveIndex;public void TickModeDIntegrity(float d){} }
 static class Program {
  static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
  static void Main(){
   var host=new ModBehaviour{IsActive=true};var module=new WavesArenaRuntimeModule();module.OnAwake(host);
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
   var modeD=new ModeDRuntimeModule();modeD.OnAwake(host);host.IsModeDActive=true;host.ModeDWaveIndex=1;
   var oldD=ModeDRuntimeModule.CaptureValidity(host,true);Check(oldD(),"Mode D current dispatch accepted");
   ModeDRuntimeModule.Invalidate(host);host.IsModeDActive=false;Check(!oldD(),"Mode D ended queue rejected before next dispatch");
   host.IsModeDActive=true;host.ModeDWaveIndex=1;var nextD=ModeDRuntimeModule.CaptureValidity(host,true);
   Check(!oldD()&&nextD(),"Mode D reused wave 1 rejects old callbacks and automatic-next-wave task");
   modeD.OnSceneLoaded(new SceneRuntimeContext());Check(!nextD(),"Mode D same-scene reload invalidates queued work");
   nextD=ModeDRuntimeModule.CaptureValidity(host,true);modeD.OnDestroy();Check(!nextD(),"Mode D runtime destruction invalidates queued work");
  }
 }
}
