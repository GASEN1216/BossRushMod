using System;
using System.Threading.Tasks;
namespace UnityEngine {
 public class Object { public bool Destroyed; public static void Destroy(GameObject o){if(o!=null)o.Destroyed=true;} }
 public class GameObject:Object {}
 public struct Vector3 { public static Vector3 operator +(Vector3 a,Vector3 b){return a;} public static Vector3 operator *(Vector3 a,float b){return a;} public static Vector3 operator -(Vector3 a){return a;} }
 public class Transform {public Vector3 position,forward;}
}
namespace UnityEngine.SceneManagement {
 public struct Scene {public int buildIndex;}
 public static class SceneManager {public static Scene GetActiveScene(){return new Scene{buildIndex=1};}}
}
namespace BossRush {
 public class CharacterMainControl {public static CharacterMainControl Main=new CharacterMainControl();public UnityEngine.Transform transform=new UnityEngine.Transform();public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();public int Team;public void SetTeam(int t){Team=t;} }
 public class CharacterRandomPreset {
  public TaskCompletionSource<CharacterMainControl> Pending;
  public Task<CharacterMainControl> CreateCharacterAsync(UnityEngine.Vector3 p,UnityEngine.Vector3 d,int s,object a,bool b){Pending=new TaskCompletionSource<CharacterMainControl>();return Pending.Task;}
 }
 public partial class ModBehaviour {
  private CharacterMainControl modeEMerchantNPC;
  private bool modeEShellEconomyAvailable=true;
  private int modeEPlayerFaction=1,session=1;
  public int Failures;
  public readonly CharacterRandomPreset Preset=new CharacterRandomPreset();
  private bool IsModeEOrModeFSpawnSessionStillValid(int f,int fs,int e,int es){return e==session&&es==1;}
  private bool VerifyModeEShellPatchInstallation(){return true;}
  private CharacterRandomPreset GetModeEMerchantPreset(){return Preset;}
  private void SetModeEShellEconomyUnavailable(string r,bool b){modeEShellEconomyAvailable=false;}
  private void FailModeEShellMerchantBuild(UnityEngine.GameObject o,string r){Failures++;modeEShellEconomyAvailable=false;UnityEngine.Object.Destroy(o??modeEMerchantNPC?.gameObject);}
  private void SetModeEMerchantHealth(CharacterMainControl c){}
  private void BuildModeEMerchantShop(UnityEngine.GameObject o){}
  private static void DevLog(string s){}
  public Task Begin(int s){session=s;modeEShellEconomyAvailable=true;return SpawnModeEMerchant(modeESessionToken:s,modeESessionRelatedScene:1);}
  public bool CurrentIs(CharacterMainControl c){return modeEMerchantNPC==c&&!c.gameObject.Destroyed&&modeEShellEconomyAvailable;}
 }
 public static class Program {
  static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
  public static async Task Main(){
   foreach(string outcome in new[]{"null","fault","success"}){
    var owner=new ModBehaviour();var first=owner.Begin(1);var oldRequest=owner.Preset.Pending;
    var second=owner.Begin(2);var current=new CharacterMainControl();owner.Preset.Pending.SetResult(current);await second;
    var stale=new CharacterMainControl();
    if(outcome=="fault")oldRequest.SetException(new InvalidOperationException("factory failed"));else oldRequest.SetResult(outcome=="null"?null:stale);
    await first;
    Check(owner.CurrentIs(current)&&owner.Failures==0,"stale merchant "+outcome+" preserves successor merchant/economy");
    if(outcome=="success")Check(stale.gameObject.Destroyed,"stale successful merchant recycled");
   }
   foreach(bool fault in new[]{false,true}){
    var owner=new ModBehaviour();var task=owner.Begin(1);
    if(fault)owner.Preset.Pending.SetException(new InvalidOperationException());else owner.Preset.Pending.SetResult(null);
    await task;Check(owner.Failures==1,"current merchant failure still closes unavailable economy "+fault);
   }
  }
 }
}
