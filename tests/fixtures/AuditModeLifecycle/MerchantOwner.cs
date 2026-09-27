using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace UnityEngine {
 public class Object {
  public bool Destroyed;
  public static bool operator ==(Object a,Object b){return (ReferenceEquals(a,null)||a.Destroyed)?ReferenceEquals(b,null)||b.Destroyed:ReferenceEquals(a,b);}
  public static bool operator !=(Object a,Object b){return !(a==b);}
  public override bool Equals(object o){return ReferenceEquals(this,o);}
  public override int GetHashCode(){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);}
  public static void Destroy(GameObject o){if(ReferenceEquals(o,null))return;o.Destroyed=true;foreach(var c in o.Components)c.Destroyed=true;}
 }
 public class GameObject:Object {public readonly List<Object> Components=new List<Object>();}
 public struct Vector3 { public static Vector3 operator +(Vector3 a,Vector3 b){return a;} public static Vector3 operator *(Vector3 a,float b){return a;} public static Vector3 operator -(Vector3 a){return a;} }
 public class Transform {public Vector3 position,forward;}
}
namespace UnityEngine.SceneManagement {
 public struct Scene {public int buildIndex;}
 public static class SceneManager {public static Scene GetActiveScene(){return new Scene{buildIndex=1};}}
}
namespace BossRush {
 public class CharacterMainControl:UnityEngine.Object {
  public static CharacterMainControl Main=new CharacterMainControl();
  public UnityEngine.Transform transform=new UnityEngine.Transform();
  public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();public int Team;
  public CharacterMainControl(){gameObject.Components.Add(this);} public void SetTeam(int t){Team=t;}
 }
 public class CharacterRandomPreset {
  public TaskCompletionSource<CharacterMainControl> Pending;public int Calls;
  public Task<CharacterMainControl> CreateCharacterAsync(UnityEngine.Vector3 p,UnityEngine.Vector3 d,int s,object a,bool b){Calls++;Pending=new TaskCompletionSource<CharacterMainControl>();return Pending.Task;}
 }
 public class ModBehaviour:UnityEngine.Object {
  internal ModeFRuntimeModule F;
  public static void DevLog(string s){}
  public bool IsModeFActive {get{return F!=null&&F.modeFActive;}}
  public bool IsModeFSessionStillValid(int t,int s){return F!=null&&F.IsModeFSessionStillValid(t,s);}
 }
 internal abstract class BossRushRuntimeModuleBase {public virtual void OnAwake(ModBehaviour owner){}public virtual void OnDestroy(){}}
 // Intentionally no-op effects: the production validity gate must close without fake End/Exit logic.
 // ModeDestroyLifecycle separately executes the production cleanup bodies.
 internal sealed partial class ModeERuntimeModule:BossRushRuntimeModuleBase {
  private ModBehaviour modeEHost;private bool modeERuntimeDestroyed,modeECleanupPending;
  private int modeESessionSerial,modeESessionToken;private bool modeEActive;
  internal int Begin(int requested){modeEActive=true;modeESessionSerial=requested-1;return BeginModeESession();}
  private void EndModeE(bool show){}private void StopModeEStartupWarmupIfPending(){}private void DestroyModeEShellRuntimeState(){}private void ResetModeEMerchantStaticCaches(){}
 }
 internal sealed class ModeFState {internal bool IsActive;internal int RuntimeSessionToken;}
 internal sealed partial class ModeFRuntimeModule:BossRushRuntimeModuleBase {
  private ModBehaviour owner;private bool modeFRuntimeDestroyed,modeFCleanupPending;private int modeFSessionSerial;internal bool modeFActive;
  private readonly ModeFState modeFState=new ModeFState();
  internal int Begin(int requested){modeFActive=modeFState.IsActive=true;modeFSessionSerial=requested-1;return BeginModeFSession();}
  private void ExitModeF(bool show){}private void ResetPlayerBountyKillLatch(){}private void CleanupModeFDeferredExitBossObjects(){}
 }
 internal static class ModeFStatusHud {internal static void Dispose(){}}
 internal sealed partial class ModeEFMerchantRuntime {
  private sealed class SpawnPolicy {
   public Func<int,int,int,int,bool> IsSpawnSessionValid;
   public Func<bool> ShellEconomyAvailable,VerifyShellPatchInstallation;
   public Action<string,bool> SetShellEconomyUnavailable;public Func<int> PlayerFaction;public Action<CharacterMainControl> SetMerchantHealth;
  }
  private readonly SpawnPolicy policy;
  internal readonly ModeERuntimeModule E=new ModeERuntimeModule();
  internal readonly ModeFRuntimeModule F=new ModeFRuntimeModule();
  private readonly ModBehaviour host=new ModBehaviour();
  public ModeEFMerchantRuntime(){
   host.F=F;E.OnAwake(host);F.OnAwake(host);
   policy=new SpawnPolicy {
    IsSpawnSessionValid=E.IsModeEOrModeFSpawnSessionStillValid,
    ShellEconomyAvailable=()=>modeEShellEconomyAvailable,VerifyShellPatchInstallation=()=>true,
    SetShellEconomyUnavailable=(r,b)=>modeEShellEconomyAvailable=false,
    PlayerFaction=()=>1,SetMerchantHealth=c=>{}
   };
  }
  private CharacterMainControl modeEMerchantNPC;private bool modeEShellEconomyAvailable=true;
  public int Failures,Builds;public readonly CharacterRandomPreset Preset=new CharacterRandomPreset();
  private CharacterRandomPreset GetModeEMerchantPreset(){return Preset;}
  private void FailModeEShellMerchantBuild(UnityEngine.GameObject o,string r){Failures++;modeEShellEconomyAvailable=false;UnityEngine.Object.Destroy(o??modeEMerchantNPC?.gameObject);}
  private void BuildModeEMerchantShop(UnityEngine.GameObject o){Builds++;}
  public Task Begin(int s,bool modeF=false){
   modeEShellEconomyAvailable=true;
   if(modeF)return SpawnModeEMerchant(modeFSessionToken:F.Begin(s),modeFRelatedScene:1);
   return SpawnModeEMerchant(modeESessionToken:E.Begin(s),modeESessionRelatedScene:1);
  }
  public Task BeginDisposed(){return SpawnModeEMerchant(modeESessionToken:77,modeESessionRelatedScene:1);}
  public void ThrowFromGate(){policy.IsSpawnSessionValid=(a,b,c,d)=>{throw new NullReferenceException("released owner");};}
  public bool CurrentIs(CharacterMainControl c){return modeEMerchantNPC==c&&!c.gameObject.Destroyed&&modeEShellEconomyAvailable;}
 }
 public static class Program {
  static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
  public static async Task Main(){
   foreach(string outcome in new[]{"null","fault","success"}){
    var owner=new ModeEFMerchantRuntime();var first=owner.Begin(1);var oldRequest=owner.Preset.Pending;
    var second=owner.Begin(2);var current=new CharacterMainControl();owner.Preset.Pending.SetResult(current);await second;
    var stale=new CharacterMainControl();
    if(outcome=="fault")oldRequest.SetException(new InvalidOperationException("factory failed"));else oldRequest.SetResult(outcome=="null"?null:stale);
    await first;
    Check(owner.CurrentIs(current)&&owner.Failures==0,"stale merchant "+outcome+" preserves successor using production session gate");
    if(outcome=="success")Check(stale==null&&stale.gameObject.Destroyed,"stale successful merchant and component recycled");
   }
   foreach(bool fault in new[]{false,true}){
    var owner=new ModeEFMerchantRuntime();var task=owner.Begin(1);
    if(fault)owner.Preset.Pending.SetException(new InvalidOperationException());else owner.Preset.Pending.SetResult(null);
    await task;Check(owner.Failures==1,"current merchant failure closes unavailable economy "+fault);
   }
   foreach(bool modeF in new[]{false,true})foreach(string outcome in new[]{"null","fault","success"}){
    var owner=new ModeEFMerchantRuntime();var pending=owner.Begin(17,modeF);var request=owner.Preset.Pending;
    if(modeF)owner.F.OnDestroy();else owner.E.OnDestroy();
    var late=new CharacterMainControl();
    if(outcome=="fault")request.SetException(new InvalidOperationException("late factory failed"));else request.SetResult(outcome=="null"?null:late);
    await pending;
    Check(owner.Builds==0&&owner.Failures==0,"production OnDestroy rejects merchant continuation F="+modeF+" "+outcome);
    if(outcome=="success")Check(late==null&&late.gameObject.Destroyed,"disposed owner recycles completed character F="+modeF);
    owner.E.OnDestroy();int calls=owner.Preset.Calls;await owner.BeginDisposed();
    Check(owner.Preset.Calls==calls&&owner.Failures==0,"request after dispose starts no factory and cannot close another economy");
   }
   var broken=new ModeEFMerchantRuntime();var work=broken.Begin(1);var character=new CharacterMainControl();broken.ThrowFromGate();broken.Preset.Pending.SetResult(character);await work;
   Check(character==null&&broken.Failures==0,"throwing validity callback fails closed without catch escape or orphan merchant");
  }
 }
}
