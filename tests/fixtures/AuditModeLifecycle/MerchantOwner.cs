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
 public struct Scene {public int buildIndex,handle;}
 public static class SceneManager {public static int Handle=1;public static Scene GetActiveScene(){return new Scene{buildIndex=1,handle=Handle};}}
}
namespace BossRush {
 public class CharacterMainControl:UnityEngine.Object {
  public static CharacterMainControl Main=new CharacterMainControl();
  public UnityEngine.Transform transform=new UnityEngine.Transform();
  public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();public int Team;
  public Health Health=new Health();
  public CharacterMainControl(){gameObject.Components.Add(this);} public void SetTeam(int t){Team=t;}
 }
 public class CharacterRandomPreset {
  public string nameKey="SnowPMC";
  public TaskCompletionSource<CharacterMainControl> Pending;public int Calls;
  public Task<CharacterMainControl> CreateCharacterAsync(UnityEngine.Vector3 p,UnityEngine.Vector3 d,int s,object a,bool b){Calls++;Pending=new TaskCompletionSource<CharacterMainControl>();return Pending.Task;}
 }
 public class ModBehaviour:UnityEngine.Object {
  internal ModeFRuntimeModule F;internal ModeERuntimeModule E;public static ModBehaviour Instance;
  public static void DevLog(string s){}
  public bool IsModeFActive {get{return F!=null&&F.modeFActive;}}
  public int CurrentModeFSessionToken {get{return F.CurrentToken;}}
  public int CurrentModeESessionToken {get{return E.CurrentToken;}}
  public int ModeEPlayerFaction {get{return 1;}}
  public bool IsModeEOrModeFSpawnSessionStillValid(int f,int fs,int e,int es){return E.IsModeEOrModeFSpawnSessionStillValid(f,fs,e,es);}
  public bool IsModeFSessionStillValid(int t,int s){return F!=null&&F.IsModeFSessionStillValid(t,s);}
 }
 internal abstract class BossRushRuntimeModuleBase {public virtual void OnAwake(ModBehaviour owner){}public virtual void OnDestroy(){}}
 // Intentionally no-op effects: the production validity gate must close without fake End/Exit logic.
 // ModeDestroyLifecycle separately executes the production cleanup bodies.
 internal sealed partial class ModeERuntimeModule:BossRushRuntimeModuleBase {
  private ModBehaviour modeEHost;private bool modeERuntimeDestroyed,modeECleanupPending;
  private int modeESessionSerial,modeESessionToken;private bool modeEActive;
  internal int Begin(int requested){modeEActive=true;modeESessionSerial=requested-1;return BeginModeESession();}
  internal bool Active {get{return modeEActive;}}
  internal int CurrentToken {get{return modeESessionToken;}}
  internal static void TrackMerchantCoal(CharacterMainControl c){ModeEPetSpawner.Tracked++;}
  private void EndModeE(bool show){}private void StopModeEStartupWarmupIfPending(){}private void DestroyModeEShellRuntimeState(){}private void ResetModeEMerchantStaticCaches(){}
 }
 internal sealed class ModeFState {internal bool IsActive;internal int RuntimeSessionToken;}
 internal sealed partial class ModeFRuntimeModule:BossRushRuntimeModuleBase {
  private ModBehaviour owner;private bool modeFRuntimeDestroyed,modeFCleanupPending;private int modeFSessionSerial;internal bool modeFActive;
  private readonly ModeFState modeFState=new ModeFState();
  internal int Begin(int requested){modeFActive=modeFState.IsActive=true;modeFSessionSerial=requested-1;return BeginModeFSession();}
  internal int CurrentToken {get{return modeFState.RuntimeSessionToken;}}
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
   host.F=F;host.E=E;E.OnAwake(host);F.OnAwake(host);
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
  public bool GateValid(bool modeF,int token){return E.IsModeEOrModeFSpawnSessionStillValid(modeF?token:0,1,modeF?0:token,1);}
  public bool ModeActive(bool modeF){return modeF?F.modeFActive:E.Active;}
  public Task BeginCoal(bool modeF){ModBehaviour.Instance=host;CharacterMainControl.Main=new CharacterMainControl();ModeEPetSpawner.Prepare(Preset);
   if(modeF)F.Begin(37);else E.Begin(37);
   return ModeEPetSpawner.Begin(modeF?37:0,modeF?0:37,CharacterMainControl.Main);}
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

   foreach(bool modeF in new[]{false,true})foreach(string change in new[]{"stop","room","role","same-scene-handle"})foreach(string outcome in new[]{"null","fault","success"}){
    BossRushCoop.IsConnected=BossRushCoop.IsHost=true;BossRushCoop.RunId="original";UnityEngine.SceneManagement.SceneManager.Handle=1;
    var owner=new ModeEFMerchantRuntime();var task=owner.Begin(23,modeF);var request=owner.Preset.Pending;
    if(change=="stop")BossRushCoop.IsConnected=false;
    else if(change=="room")BossRushCoop.RunId="new-room";
    else if(change=="role")BossRushCoop.IsHost=false;
    else UnityEngine.SceneManagement.SceneManager.Handle=2;
    Check(owner.ModeActive(modeF)&&!owner.GateValid(modeF,23),"actual session gate rejects scope loss with old mode still active F="+modeF+" "+change+" "+outcome);
    var late=new CharacterMainControl();
    if(outcome=="fault")request.SetException(new InvalidOperationException("late old-room factory failed"));else request.SetResult(outcome=="null"?null:late);
    await task;
    Check(owner.Builds==0&&owner.Failures==0,"actual merchant request rejects original scope loss F="+modeF+" "+change+" "+outcome);
    if(outcome=="success")Check(late==null&&late.gameObject.Destroyed,"old-scope completed merchant is reclaimed F="+modeF+" "+change);
   }
   foreach(bool modeF in new[]{false,true})foreach(bool startedEmpty in new[]{false,true}){
    BossRushCoop.IsConnected=BossRushCoop.IsHost=true;BossRushCoop.RunId=startedEmpty?string.Empty:"original";UnityEngine.SceneManagement.SceneManager.Handle=1;
    var owner=new ModeEFMerchantRuntime();var task=owner.Begin(31,modeF);var live=new CharacterMainControl();
    BossRushCoop.RunId=startedEmpty?"first-room":string.Empty;
    owner.Preset.Pending.SetResult(live);await task;
    Check(owner.Builds==1&&owner.CurrentIs(live),"actual merchant keeps same-role empty handshake F="+modeF+" first-bind="+startedEmpty);
   }
   foreach(bool modeF in new[]{false,true})foreach(string change in new[]{"stop","room","role","same-scene-handle","none"}){
    BossRushCoop.IsConnected=BossRushCoop.IsHost=true;BossRushCoop.RunId="original";UnityEngine.SceneManagement.SceneManager.Handle=1;
    var owner=new ModeEFMerchantRuntime();var task=owner.BeginCoal(modeF);var request=owner.Preset.Pending;var coal=new CharacterMainControl();
    if(change=="stop")BossRushCoop.IsConnected=false;
    else if(change=="room")BossRushCoop.RunId="new-room";
    else if(change=="role")BossRushCoop.IsHost=false;
    else if(change=="same-scene-handle")UnityEngine.SceneManagement.SceneManager.Handle=2;
    request.SetResult(coal);await task;
    Check(owner.ModeActive(modeF),"coal original mode remains active across await F="+modeF+" "+change);
    if(change=="none")Check(coal!=null&&coal.Team==1&&ModeEPetSpawner.ResetCalls==1&&ModeEPetSpawner.Tracked==1,
     "actual original-scope coal still commits faction/hire/registration F="+modeF);
    else Check(coal==null&&ModeEPetSpawner.ResetCalls==0&&ModeEPetSpawner.Tracked==0,
     "actual coal factory rejects scope loss before cleanup F="+modeF+" "+change);
   }
   BossRushCoop.IsConnected=BossRushCoop.IsHost=false;BossRushCoop.RunId=string.Empty;UnityEngine.SceneManagement.SceneManager.Handle=1;
  }
 }
}

namespace BossRush {
 internal sealed class FixtureCoopCleanup { internal void Dispose() { } }
 internal sealed partial class ModeERuntimeModule { private FixtureCoopCleanup coopChannel,coopPersonal; private void InitializeCoopModeE() { } }
 internal sealed partial class ModeFRuntimeModule { private FixtureCoopCleanup coopChannel,coopPersonal; private void InitializeCoopModeF() { } }
}

namespace BossRush { internal sealed partial class ModeEFMerchantRuntime { private bool coopReplica; } }

namespace BossRush { internal static partial class BossRushCoop { internal static void RequestAiRegistration(object value) { } } }
namespace BossRush {
 public sealed class Health {public bool IsDead;}
 public sealed class BossRushCoopPlayer {public CharacterMainControl Character;}
 internal static partial class BossRushCoop {
  internal static string GetPlayerId(CharacterMainControl c){return c==CharacterMainControl.Main?"local":null;}
  internal static BossRushCoopPlayer FindPlayer(string id){return id=="local"?new BossRushCoopPlayer{Character=CharacterMainControl.Main}:null;}
 }
 internal static class ObjectCache {internal static CharacterRandomPreset[] GetCharacterPresets(){return new CharacterRandomPreset[0];}}
 internal static class L10n {internal static string T(string c,string e){return c;}}
 internal static class NotificationText {internal static void Push(string text){}}
 internal static class Teams {internal const int player=1;}
 internal static partial class ModeEPetSpawner {
  private static CharacterRandomPreset cachedCoalballPreset;internal static int ResetCalls,Tracked;
  internal static void Prepare(CharacterRandomPreset p){cachedCoalballPreset=p;ResetCalls=Tracked=0;}
  private static void ResetPetHireInteractable(UnityEngine.GameObject go){ResetCalls++;}
  internal static Task Begin(int f,int e,CharacterMainControl player){return SpawnPetAsync(f,1,e,1,player);}
 }
}
namespace BossRush {
 // Offline lifetime fixture: no connected coal deployments exist.
 internal sealed partial class ModeERuntimeModule { private static ModeERuntimeModule coopCoalOwner; private void CleanupCoopCoal() { } private System.IDisposable coopUtilitySmokeChannel; private void CleanupCoopUtilities() { } }
}
