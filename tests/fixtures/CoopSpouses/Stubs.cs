using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
namespace UnityEngine
{
    class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a,Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b); }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object b) { return ReferenceEquals(this,b); } public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static GameObject Instantiate(GameObject prefab,Vector3 p,Quaternion q) { var result=new GameObject(prefab.name+" copy");result.transform.SetPositionAndRotation(p,q);BossRush.Probe.Creates++;return result; }
        public static void Destroy(Object value)
        { if(value==null)return;var go=value as GameObject;if(go!=null){foreach(var child in go.transform.Children.ToArray())Destroy(child.gameObject);foreach(var c in go.Components)c.Destroyed=true;}value.Destroyed=true; }
    }
    class Component:Object
    {
        public GameObject gameObject; public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T:Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool inactive=false) where T:Component { return gameObject.GetComponentInChildren<T>(inactive); }
    }
    class MonoBehaviour:Component { public bool enabled=true; }
    class GameObject:Object
    {
        public string name;public int layer;public bool activeSelf=true;public readonly Transform transform;internal readonly List<Component> Components=new List<Component>();
        public GameObject(string name="object") { this.name=name;transform=new Transform { gameObject=this }; }
        public void SetActive(bool b) { activeSelf=b; }
        public T AddComponent<T>() where T:Component { var c=(T)Activator.CreateInstance(typeof(T),true);c.gameObject=this;Components.Add(c);return c; }
        public T GetComponent<T>() where T:Component { return Components.OfType<T>().FirstOrDefault(c=>c!=null); }
        public T GetComponentInChildren<T>(bool inactive=false) where T:Component { return GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool inactive=false) where T:Object { if(typeof(T)==typeof(Transform))return new[]{(T)(Object)transform};return Components.OfType<T>().Where(c=>c!=null).ToArray(); }
    }
    class Transform:Object { public GameObject gameObject;public readonly List<Transform> Children=new List<Transform>();public Vector3 position;public Quaternion rotation;public void SetPositionAndRotation(Vector3 p,Quaternion q){position=p;rotation=q;} }
    struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 zero {get{return new Vector3();}}public static Vector3 forward{get{return new Vector3(0,0,1);}}public static Vector3 back{get{return new Vector3(0,0,-1);}}public float sqrMagnitude{get{return x*x+y*y+z*z;}}public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}public static Vector3 operator *(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);} }
    struct Quaternion {public static Quaternion identity{get{return new Quaternion();}}public static Vector3 operator *(Quaternion q,Vector3 v){return v;} }
    static class Time {public static float unscaledTime,time;}
    static class LayerMask {public static int NameToLayer(string s){return 0;}}
}
namespace UnityEngine.AI {class NavMeshAgent:MonoBehaviour{}}
namespace UnityEngine.SceneManagement {struct Scene{public string name;}static class SceneManager {internal static string Name="Level_Ground";public static Scene GetActiveScene(){return new Scene { name=Name };}}}
class CharacterMainControl:MonoBehaviour {public static CharacterMainControl Main;}
class InteractableBase:MonoBehaviour{}
class LevelManager {public static bool AfterInit=true;}
class SceneLoader {public static bool IsSceneLoading;}
namespace BossRush.Utils {static class NPCCommonUtils {public static void FixShaders(GameObject go,string s){}public static void SetLayerRecursively(GameObject go,int layer){go.layer=layer;}}static class NPCNameTagHelper {public static void RegisterOriginalHealthBarName(Transform t,string n,float h,string l){}}}
namespace BossRush
{
    static class Probe {internal static int Creates,Attaches,RegistryWrites,SpawnsWithTrack;internal static readonly List<Task> Tasks=new List<Task>();internal static TaskCompletionSource<CharacterMainControl> DuckGate;}
    class BossRushCoopPlayer {public string Id,AssetId;public CharacterMainControl Character;public bool IsConnected=true,IsAlive=true;}
    class BossRushCoopMessage {public string SenderId;public bool IsFromHost;public object Payload;public T Read<T>(){return BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(Payload));}}
    class EmptyDisposable:IDisposable {readonly Action cleanup;internal EmptyDisposable(Action c){cleanup=c;}public void Dispose(){cleanup();}}
    static class BossRushCoop
    {
        public static bool IsHost=true,IsClient=false,IsConnected=true,SessionReady=true;public static bool HasWorldAuthority{get{return !IsClient;}}
        public static string RunId="run",LocalPlayerId="host",LocalAssetId="host-asset";public static int SceneGeneration=1;
        public static readonly List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();
        internal static readonly Dictionary<string,Action<BossRushCoopMessage>> Channels=new Dictionary<string,Action<BossRushCoopMessage>>();
        internal static readonly List<object> Broadcasts=new List<object>(),Sends=new List<object>();public static event Action<string> PlayerDisconnected;
        public static IDisposable Register(string t,Action<BossRushCoopMessage> callback){Channels[t]=callback;return new EmptyDisposable(()=>Channels.Remove(t));}
        public static BossRushCoopPlayer FindPlayer(string id){return Players.Find(p=>p.Id==id);}
        public static bool SendToHost<T>(string topic,T payload){Sends.Add(BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(payload)));return true;}
        public static void SendToPlayer<T>(string id,string topic,T payload){Sends.Add(BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(payload)));}
        public static void Broadcast<T>(string topic,T payload){Broadcasts.Add(BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(payload)));}
        internal static void Disconnect(string id){FindPlayer(id).IsConnected=false;PlayerDisconnected?.Invoke(id);}
        internal static int Subscribers{get{return PlayerDisconnected==null?0:PlayerDisconnected.GetInvocationList().Length;}}
    }
    class ModBehaviour {public readonly GoblinNpcRuntimeModule GoblinNpcRuntime=new GoblinNpcRuntimeModule();public readonly WeddingRuntimeModule WeddingRuntime=new WeddingRuntimeModule();public static void DevLog(string s){throw new Exception(s);}public static bool IsBaseHubSceneName(string s){return s=="Level_Ground";}public void SpawnGoblinNPC(Vector3? p=null,bool still=false,bool force=false){CoopNpcWorld.Track("goblin","goblin",new GameObject());}public void SpawnNurseNPC(Vector3? p=null,bool still=false,bool force=false){CoopNpcWorld.Track("nurse","nurse",new GameObject());}public void SpawnCourierNPC(){CoopNpcWorld.Track("courier","courier",new GameObject());}}
    class WeddingRuntimeModule {internal bool HasHome=true;internal void RetireOfflineSpouseForCoop(string npc){}internal bool TryGetCoopSpouseHome(out Vector3 v){v=new Vector3(10,0,10);return HasHome;}}
    static class L10n{public static string T(string z,string e){return e;}}
    static class AffinityManager{public static string Spouse;public static bool Following;public static string GetCurrentSpouseNpcId(){return Spouse;}public static bool IsSpouseFollowingPlayer(string id){return Following;}}
    static class GoblinAffinityConfig{public const string NPC_ID="goblin";}static class NurseAffinityConfig{public const string NPC_ID="nurse";}
    class GoblinNpcRuntimeModule{public GameObject GoblinPrefab=new GameObject("goblin-prefab");public bool LoadGoblinAssetBundle(){return true;}}
    class NurseNpcRuntimeModule{static GameObject prefab=new GameObject("nurse-prefab");public static GameObject GetPrefabForRuntime(){return prefab;}}
    class NPCFollowMovementBase:MonoBehaviour{public Transform Target;public void EnablePlayerFollow(Transform t){Target=t;}public void DisablePlayerFollow(){Target=null;}}
    class GoblinMovement:NPCFollowMovementBase{public void SetSceneName(string n){}}
    class NurseMovement:NPCFollowMovementBase{public void SetSceneName(string n){}}
    class CourierMovement:MonoBehaviour{}
    class DuckNpcMovement:MonoBehaviour{public Transform Target;public void EnablePlayerFollow(Transform t){Target=t;}public void DisablePlayerFollow(){Target=null;}public void Hold(){}public void Release(float n){}}
    class GoblinNPCController:MonoBehaviour{public int Changes;public void EnterStationaryIdleState(){Changes++;}public void ExitStationaryIdleState(){Changes++;}}
    class NurseNPCController:MonoBehaviour{public void StartIdleAnimation(){}}
    class GoblinInteractable:InteractableBase{public int Refreshes;public void RefreshMarriageOptionVisibility(){Refreshes++;}}
    class NurseInteractable:InteractableBase{public void RefreshMarriageOptionVisibility(){}}
    class PermanentDuckNpcInteractable:InteractableBase{public void RefreshMarriageOptionVisibility(){}}
    class WeddingNpcResidentMarker:MonoBehaviour{public string NpcId;}
    class DuckNpcBlueprint{public string id;public bool isPermanent=true;}
    static class DuckNpcRegistry{public static bool TryGet(string id,out DuckNpcBlueprint b){b=(id=="duck")?new DuckNpcBlueprint {id=id}:null;return b!=null;}}
    static class PermanentDuckNpcRegistry{static readonly Dictionary<string,CharacterMainControl> entries=new Dictionary<string,CharacterMainControl>();public static bool IsPermanentDuckNpc(string id){return id=="duck";}public static CharacterMainControl GetInstance(string id){CharacterMainControl c;return entries.TryGetValue(id,out c)?c:null;}public static void RegisterInstance(string id,CharacterMainControl c){Probe.RegistryWrites++;entries[id]=c;}public static void UnregisterInstance(string id){entries.Remove(id);}}
    static class PermanentDuckNpcModule{public static void AttachPermanentParts(CharacterMainControl c,DuckNpcBlueprint b,Vector3 p){Probe.Attaches++;c.gameObject.AddComponent<DuckNpcMovement>();c.gameObject.AddComponent<PermanentDuckNpcInteractable>();}}
    static class DuckNpcSpawner
    {
        public static async UniTask<CharacterMainControl> SpawnAsync(DuckNpcBlueprint b,Vector3 p,Vector3 f,bool replica=false,bool trackWorld=true)
        {if(trackWorld)Probe.SpawnsWithTrack++;CharacterMainControl c;if(Probe.DuckGate!=null)c=await Probe.DuckGate.Task;else c=new GameObject("duck").AddComponent<CharacterMainControl>();c.transform.position=p;return c;}
        public static void Despawn(CharacterMainControl c){UnityEngine.Object.Destroy(c.gameObject);}
    }
    static class SkyIslandWorldStory {public static string ResidentName(string n){return n;}}
}
