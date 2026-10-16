using System;
using System.Collections.Generic;
namespace UnityEngine {
 public static class Time { public static float unscaledTime; }
 public class Object {public bool Destroyed;public static void Destroy(Object value){if(ReferenceEquals(value,null))return;value.Destroyed=true;var go=value as GameObject;if(go!=null)foreach(var component in go.Components)Destroy(component);}public static bool operator ==(Object a,Object b){bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b);}public static bool operator !=(Object a,Object b){return !(a==b);}public override bool Equals(object o){return ReferenceEquals(this,o);}public override int GetHashCode(){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);}}
 public class GameObject:Object { public UnityEngine.SceneManagement.Scene scene;public readonly List<Object> Components=new List<Object>(); }
}
namespace UnityEngine.SceneManagement { public struct Scene { public int handle; public bool isLoaded; public bool IsValid(){return handle!=0;} } public static class SceneManager { public static event Action<Scene> sceneUnloaded;public static void Unload(Scene value){sceneUnloaded?.Invoke(value);} } }
namespace Duckov.Scenes { public class MultiSceneCore:UnityEngine.Object { public static MultiSceneCore Instance; public bool IsLoading;public SceneInfo SceneInfo; public UnityEngine.GameObject gameObject; } public class SceneInfo {public string ID;public SceneReference SceneReference;} public class SceneReference{public string Name;} public class SceneLoadingContext{public string sceneName;} public static class SceneInfoCollection {public static SceneInfo GetSceneInfo(string id){return new SceneInfo{ID=id,SceneReference=new SceneReference{Name=id}};}} public static class SceneLoader{public static bool IsSceneLoading;public static event Action<SceneLoadingContext> onStartedLoadingScene;public static void Begin(string id){IsSceneLoading=true;onStartedLoadingScene?.Invoke(new SceneLoadingContext{sceneName=id});}} }
namespace Saves {public static class SavesSystem {public static int CurrentSlot=1; public static event Action OnSetFile,OnSaveDeleted; public static void Select(int slot){CurrentSlot=slot;OnSetFile?.Invoke();}public static void Delete(){OnSaveDeleted?.Invoke();}}}
public class Health { public bool IsDead; }
public class CharacterMainControl:UnityEngine.Object { public static CharacterMainControl Main=new CharacterMainControl(); public Health Health=new Health(); }
public class InteractableLootbox {}
public class HealthSimpleBase {}
namespace BossRush {
 internal class BossRushRuntimeModuleBase {public virtual string ModuleName{get{return "";}}public virtual void OnAwake(ModBehaviour owner){}public virtual void OnStart(){}public virtual void OnUpdate(float d,float u){}public virtual void OnDestroy(){}}
 internal class ModBehaviour {public static readonly List<string> Messages=new List<string>();public static string GetModPath(){return System.IO.Path.GetTempPath();}public void ShowMessage(string s){Messages.Add(s);}public static void CriticalLog(string k,string s){Console.WriteLine(s);}public static void DevLog(string s){} }
 internal static class L10n {public static string T(string a,string b){return b;}}
 internal class BossRushCoopMessage {public string SenderId,Topic,Json,MessageId;public bool IsFromHost;}
 internal static partial class BossRushCoop {
  public static BossRushCoopRuntimeModule Runtime;public static int SessionChanges;public static readonly List<string> Disconnected=new List<string>();
  public static int SceneGeneration{get{return Runtime!=null?Runtime.Generation:0;}}
  public static void RaiseSessionChanged(){SessionChanges++;}public static void RaisePlayerDisconnected(string id){Disconnected.Add(id);}public static void RaisePlayerSpawned(BossRushCoopPlayer p){}public static void RaiseAiSpawned(int id,CharacterMainControl c){}public static void RaiseSceneReadyChanged(string key){}public static void Shutdown(BossRushCoopRuntimeModule r){Runtime=null;}
 }
 internal class BossRushCoopTransport : IDisposable {
  public static bool Server;public static bool Started=true;public static readonly List<object> NativePeers=new List<object>();public static BossRushCoopTransport Current;
  public static bool BindSucceeds=true, StateKnown=true;public static object NativeHost;public bool Bound{get{return BindSucceeds;}}public bool NetworkStarted{get{return Started;}}public bool IsServer{get{return Server;}}public object HostPeer{get{return NativeHost;}}public int PeerCount{get{return NativePeers.Count;}}
  public bool TryGetNetworkState(out bool started,out bool host){started=Started;host=StateKnown&&Server;return StateKnown;}
  public List<object> ConnectedPeers(){return new List<object>(NativePeers);}
  public string PeerDisplayName(object peer){return NativeId(peer);}
  public static readonly List<string> NativeNotices=new List<string>();public bool BroadcastCompatibilityNotice(string text){NativeNotices.Add(text);return true;}
  public static readonly Dictionary<object,CharacterMainControl> PeerCharacters=new Dictionary<object,CharacterMainControl>();
  public static readonly Dictionary<string,CharacterMainControl> RemoteCharacters=new Dictionary<string,CharacterMainControl>();
  readonly Action<object,byte[]> receive; public readonly List<string> Sent=new List<string>(); public readonly List<byte[]> SentBytes=new List<byte[]>();
  public readonly BossRushCoopSceneIdentity SceneIdentity;private long travelSerial;
  public object Service{get{return this;}}
  public BossRushCoopTransport(Action<object,byte[]> r,Action<object> d,Action<string,CharacterMainControl,bool> p,Action<int,CharacterMainControl> a){Current=this;receive=r;SceneIdentity=new BossRushCoopSceneIdentity(()=>Started,()=>Server,()=>this,()=>NativeHost,()=>BossRushCoop.Runtime.SessionId);SceneIdentity.Install();}
  public static string Room(string name){Guid parsed;if(string.IsNullOrEmpty(name)||Guid.TryParseExact(name,"N",out parsed))return name;using(var hash=System.Security.Cryptography.MD5.Create())return new Guid(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(name))).ToString("N");}
  public static string Token(int generation){return generation.ToString("x32");}
  public void BeginLoad(int generation,string id,string room=null){var marker=new BossRushCoopSceneMarker{session=room??BossRushCoop.Runtime.SessionId,serial=++travelSerial,token=Token(generation),sceneId=id};if(Server)SceneIdentity.ApproveHost(marker);else{SceneIdentity.StageMarker(marker);SceneIdentity.ObserveNativeBegin(marker);}Duckov.Scenes.SceneLoader.Begin(id);}
  public bool IsPeerConnected(object p){return p!=null&&NativePeers.Contains(p);} public bool TryBind(){return BindSucceeds;}public string SelfId(){return "native-local";}public string NativeId(object p){return "native-"+p.GetHashCode();}
  public void RefreshCharacters(Action<string,CharacterMainControl,bool> f){f(SelfId(),CharacterMainControl.Main,true);foreach(var pair in PeerCharacters)f(NativeId(pair.Key),pair.Value,false);foreach(var pair in RemoteCharacters)f(pair.Key,pair.Value,false);}public void RefreshAi(Action<int,CharacterMainControl> f){}public bool RegisterAi(CharacterMainControl c){return true;}
  public CharacterMainControl CharacterForPeer(object p){CharacterMainControl c;return p!=null&&PeerCharacters.TryGetValue(p,out c)?c:null;} public bool Send(object p,byte[] bytes){Sent.Add(System.Text.Encoding.UTF8.GetString(bytes));var native=new byte[(ushort)bytes.Length];Array.Copy(bytes,native,native.Length);SentBytes.Add(native);return true;}
  public void PacketBytes(object p,byte[] bytes){receive(p,bytes);}
  public void Packet(object p,string topic,object value,string session=""){var welcome=value as BossRushCoopWelcome;if(welcome!=null)welcome.session=Room(welcome.session);if(topic=="$roster"){var token=value.GetType().GetField("sceneToken");if(token!=null&&token.GetValue(value)==null)token.SetValue(value,Token((int)value.GetType().GetField("generation").GetValue(value)));}var e=new BossRushCoopEnvelope{session=Room(session),topic=topic,messageId=Guid.NewGuid().ToString("N"),json=BossRushCoopJson.ToJson(value)};receive(p,System.Text.Encoding.UTF8.GetBytes(BossRushCoopJson.ToJson(e)));}
  public bool RegisterDestructible(HealthSimpleBase h,string k){return true;}public void Dispose(){SceneIdentity.Dispose();}
 }
 internal class BossRushCoopWorldLoot:IDisposable {public static int Resets;public BossRushCoopWorldLoot(BossRushCoopTransport t){}public void Tick(){}public bool Register(InteractableLootbox b){return true;}public void ChangeGeneration(){}public void Reset(){Resets++;}public void Dispose(){}}
 internal class BossRushCoopControl:IDisposable {public static int Resets;public void Tick(){}public void Reset(){Resets++;}public string Begin(string p,string e){return null;}public void End(string l){}public bool Submit(string l,UnityEngine.Vector3 m,UnityEngine.Vector3 a,bool f,bool d,bool r,int w){return false;}public bool Contains(string l){return false;}public void Dispose(){}}
}
namespace UnityEngine {public struct Vector3{}}
