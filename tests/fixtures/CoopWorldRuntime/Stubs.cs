using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace UnityEngine
{
    public class Object
    {
        static int next; readonly int id = ++next; internal bool Destroyed;
        public int GetInstanceID() { return id; }
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj,null)) return;
            GameObject go=obj as GameObject;
            if (!ReferenceEquals(go,null)) { go.BeforeDestroy?.Invoke(); foreach(Object component in go.Components) component.Destroyed=true; }
            obj.Destroyed=true;
        }
        public static void DestroyImmediate(Object obj){Destroy(obj);}
        public static T Instantiate<T>(T original,Transform parent) where T:Object {throw new Exception("Unexpected shell creation in existing-container scene binding regression");}
        public static bool operator ==(Object a,Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b); }
        public static bool operator !=(Object a,Object b) {return !(a==b);} public override bool Equals(object b) {return this==b as Object;} public override int GetHashCode(){return id;}
    }
    public class GameObject:Object
    { public UnityEngine.SceneManagement.Scene scene; public Action BeforeDestroy; public List<Object> Components=new List<Object>();public string name;public bool activeInHierarchy=true;public readonly Transform transform=new Transform();public GameObject(string name=null){this.name=name;}public void SetActive(bool value){activeInHierarchy=value;} }
    public class Transform { public Vector3 position,localScale,lossyScale;public Quaternion rotation;public void SetParent(Transform value,bool preserve){} }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero {get{return new Vector3();}}
        public static Vector3 operator *(Vector3 a,float f){return new Vector3(a.x*f,a.y*f,a.z*f);}
        public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 ClampMagnitude(Vector3 a,float f){float m=(float)Math.Sqrt(a.x*a.x+a.y*a.y+a.z*a.z);return m>f?a*(f/m):a;}
    }
    public struct Vector3Int
    {
        int x,y,z; public Vector3Int(int x,int y,int z){this.x=x;this.y=y;this.z=z;}
        public override int GetHashCode(){return unchecked(x*73856093^y*19349663^z*83492791);}
    }
    public struct Quaternion { public float x,y,z,w; }
    public static class Mathf{public static int RoundToInt(float x){return (int)Math.Round(x);}}
    public static class Time {public static float unscaledTime;}
}
namespace UnityEngine.SceneManagement
{ public struct Scene {public int handle;public string name;public bool isLoaded;public bool IsValid(){return handle!=0;}}public static class SceneManager{public static event Action<Scene> sceneUnloaded;public static void Unload(Scene value){sceneUnloaded?.Invoke(value);}public static Scene GetSceneByName(string name){return new Scene();}public static void MoveGameObjectToScene(UnityEngine.GameObject value,Scene scene){value.scene=scene;}} }
namespace ItemStatsSystem {public class Inventory:UnityEngine.Object {public int Capacity;public bool NeedInspection;public void SetCapacity(int value){Capacity=value;}}}
public class LootBoxLoader:UnityEngine.Object{}
public static class GameplayDataSettings {public static PrefabSet Prefabs;public class PrefabSet{public InteractableLootbox LootBoxPrefab;}}
public class InteractableLootbox:UnityEngine.Object
{
    public ItemStatsSystem.Inventory Inventory=new ItemStatsSystem.Inventory();
    public readonly Transform transform=new Transform();public readonly GameObject gameObject=new GameObject();
    public string name;public bool needInspect;public T GetComponent<T>() where T:class{return null;}
    public InteractableLootbox(){gameObject.Components.Add(this);}
}
public class AICharacterController {public bool enabled=true;}
public class Health {public bool IsDead;}
public class CharacterMainControl:UnityEngine.Object
{
    public static CharacterMainControl Main; public Health Health=new Health(); public readonly Transform transform=new Transform();
    public readonly AICharacterController Ai=new AICharacterController(); public bool Ads,Run,Fire; public Vector3 Move,Aim;
    public T GetComponentInChildren<T>(bool flag) where T:class{return Ai as T;}
    public void SetMoveInput(Vector3 v){Move=v;} public void SetAimPoint(Vector3 v){Aim=v;}
    public void Trigger(bool f,bool p,bool r){Fire=f;} public void Dash(){} public bool TryToReload(){return true;} public void SwitchWeapon(int w){}
    public void SetRunInput(bool v){Run=v;} public void SetAdsInput(bool v){Ads=v;}
}
public class LevelManager
{public static LevelManager Instance=new LevelManager();public CharacterMainControl ControllingCharacter;public void SetControllingCharacter(CharacterMainControl c){ControllingCharacter=c;}}
public class GameCamera
{public static GameCamera Instance=new GameCamera();public CharacterMainControl Target;public void SetTarget(CharacterMainControl c){Target=c;}}
namespace HarmonyLib
{
    public class Harmony {public readonly string Id;public Harmony(string id){Id=id;}public void Patch(MethodInfo m,HarmonyMethod prefix){}public void UnpatchAll(string id){}}
    public class HarmonyMethod {public HarmonyMethod(Type type,string name){}}
    public static class AccessTools{public static MethodInfo Method(Type t,string name){return t.GetMethod(name);}}
}
namespace Duckov.Scenes
{public class Info{public string ID;public SceneReference SceneReference;}public class SceneReference{public string Name;}public class MultiSceneCore:UnityEngine.Object{public static MultiSceneCore Instance;public bool IsLoading;public Info SceneInfo;public GameObject gameObject;}public class SceneLoadingContext{public string sceneName;}public static class SceneInfoCollection{public static Info GetSceneInfo(string id){return new Info{ID=id,SceneReference=new SceneReference{Name=id}};}}public static class SceneLoader{public static bool IsSceneLoading;public static event Action<SceneLoadingContext> onStartedLoadingScene;public static void Begin(string id){IsSceneLoading=true;onStartedLoadingScene?.Invoke(new SceneLoadingContext{sceneName=id});}}}
namespace BossRush
{
    class BossRushCoopPlayer {public string Id,AssetId="asset-A";public bool IsConnected=true,IsLocal,HostAlive;public CharacterMainControl Character;public bool IsAlive{get{return IsConnected&&Character!=null&&!Character.Health.IsDead;}}}
    class BossRushCoopMessage {public string SenderId,Json;public bool IsFromHost;public T Read<T>(){return BossRushCoopJson.FromJson<T>(Json);}}
    static class BossRushCoop
    {
        public static bool IsClient {get{return IsConnected&&!IsHost;}}
        public static bool IsConnected=true,IsHost=true,SessionReady=true,HasBoundScene;public static string LocalPlayerId="host",LocalAssetId="asset-A",RunId="room";public static int SceneGeneration=1;
        public static List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();public static Dictionary<string,CharacterMainControl> Entities=new Dictionary<string,CharacterMainControl>();
        public static event Action<string> PlayerDisconnected;public static int StatesSent;public static object LastState;public static bool DropStates;
        public static readonly List<string> WorldPackets=new List<string>();
        public static bool Broadcast<T>(string topic,T value){WorldPackets.Add(System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(BossRushCoopJson.ToJson(value))));return true;}
        static readonly Dictionary<string,List<Action<BossRushCoopMessage>>> handlers=new Dictionary<string,List<Action<BossRushCoopMessage>>>();
        public static IDisposable Register(string topic,Action<BossRushCoopMessage> callback){List<Action<BossRushCoopMessage>> list;if(!handlers.TryGetValue(topic,out list))handlers[topic]=list=new List<Action<BossRushCoopMessage>>();list.Add(callback);return new Token(()=>list.Remove(callback));}
        public static bool SendToPlayer<T>(string player,string topic,T value)
        {if(topic=="control-state"){StatesSent++;LastState=value;}if(DropStates)return false;if(player==LocalPlayerId)Inject(topic,LocalPlayerId,true,value);return true;}
        public static bool SendToHost<T>(string topic,T value){if(!IsHost)return true;Inject(topic,LocalPlayerId,true,value);return true;}
        public static void Inject<T>(string topic,string sender,bool host,T value)
        {List<Action<BossRushCoopMessage>> list;if(handlers.TryGetValue(topic,out list))foreach(var callback in list.ToArray())callback(new BossRushCoopMessage{SenderId=sender,IsFromHost=host,Json=BossRushCoopJson.ToJson(value)});}
        public static BossRushCoopPlayer FindPlayer(string id){return Players.Find(p=>p.Id==id);}public static CharacterMainControl FindEntity(string id){CharacterMainControl c;return id!=null&&Entities.TryGetValue(id,out c)?c:null;}
        public static void RaiseControlLeaseChanged(string p,string l,string e){}public static void Disconnect(string p){FindPlayer(p).IsConnected=false;PlayerDisconnected?.Invoke(p);}
        class Token:IDisposable{Action action;public Token(Action a){action=a;}public void Dispose(){action?.Invoke();action=null;}}
    }
    // Native registry model follows its inventory-reused entry and reference-checked Unregister semantics.
    class BossRushCoopTransport
    {
        public BossRushCoopSceneIdentity SceneIdentity;
        public sealed class Entry {public InteractableLootbox Box;public ItemStatsSystem.Inventory Inventory;public int Key;}
        readonly Dictionary<int,Entry> byInstance=new Dictionary<int,Entry>();readonly Dictionary<ItemStatsSystem.Inventory,Entry> byInventory=new Dictionary<ItemStatsSystem.Inventory,Entry>();
        public readonly Dictionary<int,Entry> Positions=new Dictionary<int,Entry>();
        public int Key(InteractableLootbox box){Vector3 p=box.transform.position*10;return new Vector3Int(Mathf.RoundToInt(p.x),Mathf.RoundToInt(p.y),Mathf.RoundToInt(p.z)).GetHashCode();}
        public bool RegisterLoot(InteractableLootbox box)
        {Entry e;if(!byInventory.TryGetValue(box.Inventory,out e))e=new Entry();e.Box=box;e.Inventory=box.Inventory;e.Key=Key(box);byInstance[box.GetInstanceID()]=e;byInventory[box.Inventory]=e;Positions[e.Key]=e;return true;}
        public bool UnregisterLoot(InteractableLootbox box)
        {Entry e;if(!byInstance.TryGetValue(box.GetInstanceID(),out e))return true;byInstance.Remove(box.GetInstanceID());Entry cur;if(Positions.TryGetValue(e.Key,out cur)&&ReferenceEquals(cur,e))Positions.Remove(e.Key);if(byInventory.TryGetValue(e.Inventory,out cur)&&ReferenceEquals(cur,e))byInventory.Remove(e.Inventory);return true;}
    }
    static class InteractableLootboxInventoryHelper
    {public static bool EnsureLocalInventory(InteractableLootbox box,int capacity=0){box.Inventory=new ItemStatsSystem.Inventory();box.gameObject.Components.Add(box.Inventory);return true;}}
    partial class BossRushCoopWorldLoot
    {
        private class Box {internal string Id;internal InteractableLootbox Value;internal bool Owned;}
        private readonly Dictionary<int,Box> boxes=new Dictionary<int,Box>();private readonly BossRushCoopTransport transport;
        private readonly Dictionary<string,float> waiting=new Dictionary<string,float>();
        public BossRushCoopWorldLoot(BossRushCoopTransport value){transport=value;}
        public void Owned(InteractableLootbox box){boxes[Key(box.transform.position)].Owned=true;}
        public long AppliedRevision {get{return revision;}}
        public int PendingCount {get{return pending==null?-1:pending.boxes.Length;}}
        public string PendingName {get{return pending.boxes[0].name;}}
        public void Receive(string json,bool fromHost=true){OnSnapshot(new BossRushCoopMessage{IsFromHost=fromHost,Json=json});}
        public void PublishBoxes(int count,string name){var values=new BoxState[count];for(int i=0;i<count;i++)values[i]=new BoxState{id="box:"+i,scene="arena",name=name,position=new Vector3(i,1,1),scale=new Vector3(1,1,1),rotation=new Quaternion{w=1},capacity=32,active=true,inspect=true};PublishSnapshot(values);}
    }
    partial class SceneOwner
    {
        public readonly List<BossRushCoopPlayer> players=new List<BossRushCoopPlayer>();
        readonly Dictionary<string,long> appliedReadyRevisions=new Dictionary<string,long>();
        string officialSceneId;int localSceneHandle=int.MinValue,generationSceneHandle=int.MinValue;float nextRoster;
        readonly BossRushCoopTransport transport = new BossRushCoopTransport();
        string generationSceneToken;bool generationScenePending;int pendingGeneration;
        public bool Host=true;public int Generation;public HashSet<string> localReadyKeys=new HashSet<string>();public readonly BossRushCoopSceneBarrier barrier=new BossRushCoopSceneBarrier();
        public Dictionary<int,CharacterMainControl> aiCharacters=new Dictionary<int,CharacterMainControl>();readonly BossRushCoopControl control;readonly BossRushCoopWorldLoot worldLoot;
        public SceneOwner(BossRushCoopControl c,BossRushCoopWorldLoot w){control=c;worldLoot=w;transport.SceneIdentity=new BossRushCoopSceneIdentity(()=>true,()=>Host,()=>transport,()=>null,()=>"00112233445566778899aabbccddeeff");transport.SceneIdentity.Install();}public void Refresh(){RefreshSceneIdentity();}
        public void BeginLoad(string id){transport.SceneIdentity.ApproveHost(new BossRushCoopSceneMarker{session="00112233445566778899aabbccddeeff",serial=Generation+1,token=Guid.NewGuid().ToString("N"),sceneId=id});Duckov.Scenes.SceneLoader.Begin(id);}
        public void Dispose(){transport.SceneIdentity.Dispose();}
    }
    partial class GravityOwner
    {
        internal string Action,Text;internal int Count;
        private void SendCoopZombieCommand(string action,int count,string text){Action=action;Count=count;Text=text;}
    }
}
