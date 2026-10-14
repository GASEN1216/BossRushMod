using System;
using System.Collections.Generic;
namespace UnityEngine {
 public static class Time { public static float unscaledTime; }
 public class GameObject { public UnityEngine.SceneManagement.Scene scene; }
}
namespace UnityEngine.SceneManagement { public struct Scene { public int handle; public bool isLoaded; public bool IsValid(){return handle!=0;} } }
namespace Duckov.Scenes { public class MultiSceneCore { public static MultiSceneCore Instance; public SceneInfo SceneInfo; public UnityEngine.GameObject gameObject; } public class SceneInfo {public string ID;} }
namespace Saves {public static class SavesSystem {public static int CurrentSlot=1; public static event Action OnSetFile,OnSaveDeleted; public static void Select(int slot){CurrentSlot=slot;OnSetFile?.Invoke();}public static void Delete(){OnSaveDeleted?.Invoke();}}}
public class Health { public bool IsDead; }
public class CharacterMainControl { public static CharacterMainControl Main=new CharacterMainControl(); public Health Health=new Health(); }
public class InteractableLootbox {}
public class HealthSimpleBase {}
namespace BossRush {
 internal class BossRushRuntimeModuleBase {public virtual string ModuleName{get{return "";}}public virtual void OnAwake(ModBehaviour owner){}public virtual void OnStart(){}public virtual void OnUpdate(float d,float u){}public virtual void OnDestroy(){}}
 internal class ModBehaviour {public static string GetModPath(){return System.IO.Path.GetTempPath();}public void ShowMessage(string s){}public static void CriticalLog(string k,string s){Console.WriteLine(s);}public static void DevLog(string s){} }
 internal static class L10n {public static string T(string a,string b){return b;}}
 internal class BossRushCoopMessage {public string SenderId,Topic,Json,MessageId;public bool IsFromHost;}
 internal static class BossRushCoop {
  public static BossRushCoopRuntimeModule Runtime;public static int SessionChanges;public static readonly List<string> Disconnected=new List<string>();
  public static bool IsClient{get{return Runtime!=null&&Runtime.Connected&&!Runtime.Host;}}public static int SceneGeneration{get{return Runtime!=null?Runtime.Generation:0;}}
  public static void RaiseSessionChanged(){SessionChanges++;}public static void RaisePlayerDisconnected(string id){Disconnected.Add(id);}public static void RaisePlayerSpawned(BossRushCoopPlayer p){}public static void RaiseAiSpawned(int id,CharacterMainControl c){}public static void RaiseSceneReadyChanged(string key){}public static void Shutdown(BossRushCoopRuntimeModule r){Runtime=null;}
 }
 internal class BossRushCoopTransport : IDisposable {
  public static bool Server;public static bool Started=true;public static readonly List<object> NativePeers=new List<object>();public static BossRushCoopTransport Current;
  public static object NativeHost;public bool Bound{get{return true;}}public bool NetworkStarted{get{return Started;}}public bool IsServer{get{return Server;}}public object HostPeer{get{return NativeHost;}}public int PeerCount{get{return NativePeers.Count;}}
  public static readonly Dictionary<object,CharacterMainControl> PeerCharacters=new Dictionary<object,CharacterMainControl>();
  public static readonly Dictionary<string,CharacterMainControl> RemoteCharacters=new Dictionary<string,CharacterMainControl>();
  readonly Action<object,byte[]> receive; public readonly List<string> Sent=new List<string>();
  public BossRushCoopTransport(Action<object,byte[]> r,Action<object> d,Action<string,CharacterMainControl,bool> p,Action<int,CharacterMainControl> a){Current=this;receive=r;}
  public bool IsPeerConnected(object p){return p!=null&&NativePeers.Contains(p);} public bool TryBind(){return true;}public string SelfId(){return "native-local";}public string NativeId(object p){return "native-"+p.GetHashCode();}
  public void RefreshCharacters(Action<string,CharacterMainControl,bool> f){f(SelfId(),CharacterMainControl.Main,true);foreach(var pair in PeerCharacters)f(NativeId(pair.Key),pair.Value,false);foreach(var pair in RemoteCharacters)f(pair.Key,pair.Value,false);}public void RefreshAi(Action<int,CharacterMainControl> f){}public bool RegisterAi(CharacterMainControl c){return true;}
  public CharacterMainControl CharacterForPeer(object p){CharacterMainControl c;return p!=null&&PeerCharacters.TryGetValue(p,out c)?c:null;} public bool Send(object p,byte[] bytes){Sent.Add(System.Text.Encoding.UTF8.GetString(bytes));return true;}
  public void Packet(object p,string topic,object value,string session=""){var e=new BossRushCoopEnvelope{session=session,topic=topic,messageId=Guid.NewGuid().ToString("N"),json=BossRushCoopJson.ToJson(value)};receive(p,System.Text.Encoding.UTF8.GetBytes(BossRushCoopJson.ToJson(e)));}
  public bool RegisterDestructible(HealthSimpleBase h,string k){return true;}public void Dispose(){}
 }
 internal class BossRushCoopWorldLoot:IDisposable {public static int Resets;public BossRushCoopWorldLoot(BossRushCoopTransport t){}public void Tick(){}public bool Register(InteractableLootbox b){return true;}public void ChangeGeneration(){}public void Reset(){Resets++;}public void Dispose(){}}
 internal class BossRushCoopControl:IDisposable {public static int Resets;public void Tick(){}public void Reset(){Resets++;}public string Begin(string p,string e){return null;}public void End(string l){}public bool Submit(string l,UnityEngine.Vector3 m,UnityEngine.Vector3 a,bool f,bool d,bool r,int w){return false;}public bool Contains(string l){return false;}public void Dispose(){}}
}
namespace UnityEngine {public struct Vector3{}}
