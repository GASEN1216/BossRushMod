using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BossRush;
using ItemStatsSystem;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal static int Destructions;
        internal bool Destroyed;
        public static bool operator ==(Object a,Object b){bool x=ReferenceEquals(a,null)||a.Destroyed,y=ReferenceEquals(b,null)||b.Destroyed;return x||y?x==y:ReferenceEquals(a,b);}
        public static bool operator !=(Object a,Object b){return !(a==b);}
        public static implicit operator bool(Object a){return a!=null;}
        public override bool Equals(object other){return ReferenceEquals(this,other);}
        public override int GetHashCode(){return RuntimeHelpers.GetHashCode(this);}
        public static void Destroy(Object value)
        {
            if(ReferenceEquals(value,null)||value.Destroyed)return;Destructions++;value.Destroyed=true;var go=value as GameObject;
            if(!ReferenceEquals(go,null))foreach(Component component in go.Components)
            {var item=component as Item;if(!ReferenceEquals(item,null)&&!component.Destroyed)item.EndObjectLifetime();component.Destroyed=true;}
        }
        public static void DestroyImmediate(Object value){Destroy(value);}
    }
    public class Component:Object
    {
        public GameObject gameObject{get;set;}public Transform transform{get{return gameObject.transform;}}
        public Component(){gameObject=new GameObject();gameObject.Components.Add(this);}
        public T GetComponent<T>() where T:Component{return gameObject.GetComponent<T>();}
    }
    public class MonoBehaviour:Component{}
    public sealed class GameObject:Object
    {
        public string name;internal readonly List<Component> Components=new List<Component>();public readonly Transform transform=new Transform();
        public T GetComponent<T>() where T:Component{return Components.OfType<T>().FirstOrDefault(x=>x!=null);}
        public T AddComponent<T>() where T:Component,new(){var value=new T();value.gameObject=this;Components.Add(value);return value;}
    }
    public sealed class Transform{public Vector3 position;public Quaternion rotation;public void SetParent(Transform parent){}}
    [Serializable]public struct Vector3
    {
        public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero{get{return new Vector3();}}public static Vector3 forward{get{return new Vector3(0,0,1);}}
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public float sqrMagnitude{get{return x*x+y*y+z*z;}}
    }
    public struct Quaternion{public static Quaternion Euler(float x,float y,float z){return new Quaternion();}}
    public static class Random{public static float Range(float low,float high){return low;}}
    public static class Mathf{public static float Max(float a,float b){return Math.Max(a,b);}}
    public static class Time{public static float unscaledTime{get{return BossRushCoop.Current.Now;}}}
    public static class Application{public static bool isPlaying=true;}
    public static class Debug{public static void Log(object value){}public static void LogWarning(object value){}public static void LogError(object value){}}
}
namespace UnityEngine.SceneManagement
{
    public struct Scene{public int buildIndex;}
    public static class SceneManager{public static Scene GetActiveScene(){return new Scene();}public static void MoveGameObjectToScene(GameObject go,Scene scene){}}
}
public sealed class MultiSceneCore:UnityEngine.Object
{
    public static UnityEngine.SceneManagement.Scene? MainScene;public static MultiSceneCore Instance;
    public static void MoveToActiveWithScene(GameObject go,int index){}
}
public sealed class InteractablePickup:Component{public int Throws;public void Throw(Vector3 direction,float angle){Throws++;}}
public sealed class DuckovItemAgent:ItemAgent{}
public sealed class Health{public bool IsDead;}
public sealed partial class CharacterMainControl:MonoBehaviour
{
    public static CharacterMainControl Main;
    public readonly Health health=new Health();public Health Health{get{return health;}}
    public readonly Item CharacterItem=new Item();public readonly CharacterItemControl itemControl;
    public bool IsMainCharacter{get{return ReferenceEquals(this,Main);}}
    public CharacterMainControl(){CharacterItem.Inventory=new Inventory();itemControl=new CharacterItemControl{characterMainControl=this};}
}
public sealed partial class CharacterItemControl:MonoBehaviour
{
    public CharacterMainControl characterMainControl;
    private Inventory inventory{get{return characterMainControl.CharacterItem.Inventory;}}
}
public sealed class LevelManager
{
    public static LevelManager Instance{get{return new LevelManager();}}
    public CharacterMainControl MainCharacter{get{return CharacterMainControl.Main;}}
}
public static partial class ItemUtilities
{
    public static Action OnPlayerItemOperation;public static Action<Item> OnItemSentToPlayerInventory;
}
public static partial class ItemExtensions
{
    private static ItemAgent CreatePickupAgent(this Item item,Vector3 position)
    {
        if(item.ThrowOnCreateAgent)throw new InvalidOperationException("injected native agent creation exception");
        item.GroundDrops++;var agent=new DuckovItemAgent();agent.gameObject.AddComponent<InteractablePickup>();agent.transform.position=position;
        item.AgentUtilities.ActiveAgent=agent;return agent;
    }
}
namespace ItemStatsSystem
{
    public class ItemAgent:MonoBehaviour{}
    public sealed class ItemAgentUtilities
    {
        public ItemAgent ActiveAgent;public int Releases;
        public void ReleaseActiveAgent(){Releases++;if(ActiveAgent!=null)UnityEngine.Object.Destroy(ActiveAgent.gameObject);ActiveAgent=null;}
    }
    public class ItemComponent:MonoBehaviour{public Item Master{get;set;}}
    public sealed class Slots:ItemComponent{}
    public sealed class Slot:UnityEngine.Object
    {
        public Item Content;
        public void Unplug(){if(Content!=null)Content.PluggedIntoSlot=null;Content=null;}
    }
    public sealed class Inventory:MonoBehaviour,System.Collections.Generic.IEnumerable<Item>
    {
        public bool Accept=true,ThrowBefore,ThrowAfter;public int AddCalls;
        public int Quantity{get{return Items.Where(i=>i!=null).Sum(i=>i.StackCount);}}
        public readonly List<Item> Items=new List<Item>();
        public int GetFirstEmptyPosition(int start){return Accept?Items.Count:-1;}
        public void AddAt(Item item,int position){Items.Add(item);item.InInventory=this;if(ThrowAfter)throw new InvalidOperationException("injected inventory postcommit exception");}
        public bool AddItem(Item item){return this.AddAndMerge(item);}
        public void RemoveItem(Item item){Items.Remove(item);if(ReferenceEquals(item.InInventory,this))item.InInventory=null;}
        public IEnumerator<Item> GetEnumerator(){return Items.GetEnumerator();}System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator(){return GetEnumerator();}
    }
    public sealed partial class Item:MonoBehaviour
    {
        public Inventory Inventory{get;set;}public Inventory InInventory{get;set;}public Slot PluggedIntoSlot{get;set;}public Slots Slots{get;set;}
        public bool Inspected,AcceptPlug,ThrowOnCreateAgent,ThrowPlugAfter;public int GroundDrops;private bool isBeingDestroyed;
        public bool IsBeingDestroyed{get{return isBeingDestroyed;}}private Action<Item> onDestroy;
        public int TypeID=1,MaxStackCount=1;private int count=1;
        public string DisplayName{get{return "fixture item";}}public bool Stackable{get{return MaxStackCount>1;}}
        public int StackCount{get{return Stackable?count:1;}set{if(!Stackable)return;count=Math.Min(value,MaxStackCount);if(count<1)this.DestroyTree();}}
        public ItemAgent ActiveAgent{get{return AgentUtilities.ActiveAgent;}}
        private readonly ItemAgentUtilities agentUtilities=new ItemAgentUtilities();public ItemAgentUtilities AgentUtilities{get{return agentUtilities;}}
        public void MarkDestroyed(){isBeingDestroyed=true;}internal void EndObjectLifetime(){OnDestroy();}
        public bool TryPlug(Item item,bool a,object b,int c)
        {
            if(!AcceptPlug)return false;item.PluggedIntoSlot=new Slot{Content=item};
            if(ThrowPlugAfter)throw new InvalidOperationException("injected equip postcommit exception");return true;
        }
        public void Detach(){if(PluggedIntoSlot!=null)PluggedIntoSlot.Unplug();if(InInventory!=null)InInventory.RemoveItem(this);}
    }
}
namespace BossRush
{
    internal sealed class ModBehaviour{internal static void DevLog(string message){}}
    internal sealed class ZombieModeRunState{internal int RunId=11;internal bool IsCleaningUp;}
    internal sealed class BossRushCoopMessage
    {
        internal string SenderId,Json;internal bool IsFromHost;
        internal T Read<T>(){return BossRushCoopJson.FromJson<T>(Json);}
    }
    internal sealed class BossRushCoopPlayer
    {
        internal Peer Peer;
        internal string Id{get{return Peer.Id;}}internal string AssetId{get{return Peer.Asset;}}
        internal bool IsConnected{get{return Peer.Connected;}}internal bool IsLocal{get{return ReferenceEquals(Peer,BossRushCoop.Current);}}
        internal CharacterMainControl Character{get{return Peer.Body;}}
        internal bool IsAlive{get{return IsConnected&&Character!=null&&!Character.Health.IsDead;}}
    }
    internal sealed class Peer
    {
        internal string Id,Asset="slot-A",Room="room",Scene="scene";internal int Generation=1;internal bool Host,Connected=true,Bound=true;
        internal float Now;internal CharacterMainControl Body=new CharacterMainControl();internal ZombieModeRuntimeModule Module;
        internal int NativePickups,NativeDropRequests,NativeDropRegistrations,NativeDespawns,NullPeerServerRequests;
        internal EscapeFromDuckovCoopMod.ItemNet NetworkItems=new EscapeFromDuckovCoopMod.ItemNet();
        internal EscapeFromDuckovCoopMod.DropSyncRegistry NativeDrops=new EscapeFromDuckovCoopMod.DropSyncRegistry();
        internal readonly List<Item> SpawnedItems=new List<Item>();
        internal readonly Dictionary<string,List<Action<BossRushCoopMessage>>> Handlers=new Dictionary<string,List<Action<BossRushCoopMessage>>>();
        internal readonly Dictionary<Item,Dictionary<string,string>> Snapshots=new Dictionary<Item,Dictionary<string,string>>();
        internal Action<Item,Dictionary<string,string>> SnapshotRequest;
        internal Action<Item,IReadOnlyDictionary<string,string>> SnapshotApply;
    }
    internal static class BossRushCoop
    {
        internal static Peer Current;internal static bool Connected=true,DropAck,DropResults;
        internal static int CustomMessages;
        internal static readonly List<Peer> Peers=new List<Peer>();internal static readonly Queue<Action> Wire=new Queue<Action>();
        internal static bool IsConnected{get{return Connected&&Current.Connected;}}internal static bool IsHost{get{return IsConnected&&Current.Host;}}
        internal static bool IsClient{get{return IsConnected&&!IsHost;}}internal static bool HasWorldAuthority{get{return !IsConnected||IsHost;}}
        internal static bool HasBoundScene{get{return Current.Bound;}}internal static string RunId{get{return Current.Room;}}
        internal static string SceneToken{get{return Current.Scene;}}internal static int SceneGeneration{get{return Current.Generation;}}
        internal static string LocalPlayerId{get{return Current.Id;}}internal static string LocalAssetId{get{return Current.Asset;}}
        internal static IReadOnlyList<BossRushCoopPlayer> Players{get{return Peers.Select(p=>new BossRushCoopPlayer{Peer=p}).ToArray();}}
        internal static BossRushCoopPlayer FindPlayer(string id){return Players.FirstOrDefault(p=>p.Id==id);}
        internal static string GetPlayerId(CharacterMainControl character){var peer=Peers.Find(p=>ReferenceEquals(p.Body,character));return peer==null?null:peer.Id;}
        internal static bool IsPlayer(CharacterMainControl character){return GetPlayerId(character)!=null;}
        internal static event Action<Item,Dictionary<string,string>> ItemSnapshotRequested{add{Current.SnapshotRequest+=value;}remove{Current.SnapshotRequest-=value;}}
        internal static event Action<Item,IReadOnlyDictionary<string,string>> ItemSnapshotApplied{add{Current.SnapshotApply+=value;}remove{Current.SnapshotApply-=value;}}
        internal static void Capture(Item item,Dictionary<string,string> data){Current.SnapshotRequest?.Invoke(item,data);}
        internal static void Restore(Item item,IReadOnlyDictionary<string,string> data){Current.SnapshotApply?.Invoke(item,data);}
        internal static int SnapshotSubscriptions{get{return (Current.SnapshotRequest?.GetInvocationList().Length??0)+(Current.SnapshotApply?.GetInvocationList().Length??0);}}
        internal static object NativeService{get{return EscapeFromDuckovCoopMod.NetService.Instance;}}
        internal static IDisposable Register(string topic,Action<BossRushCoopMessage> callback)
        {
            List<Action<BossRushCoopMessage>> callbacks;if(!Current.Handlers.TryGetValue(topic,out callbacks))Current.Handlers.Add(topic,callbacks=new List<Action<BossRushCoopMessage>>());
            callbacks.Add(callback);return new Subscription(()=>callbacks.Remove(callback));
        }
        internal static bool SendToHost<T>(string topic,T payload){return SendToPlayer(Peers[0].Id,topic,payload);}
        internal static bool SendToPlayer<T>(string id,string topic,T payload)
        {
            CustomMessages++;
            if((DropAck&&topic.EndsWith(".ack"))||(DropResults&&topic.IndexOf("result",StringComparison.OrdinalIgnoreCase)>=0))return true;
            var target=Peers.Find(p=>p.Id==id);if(target==null)return false;var source=Current;var json=BossRushCoopJson.ToJson(payload);
            Wire.Enqueue(()=>{var saved=Current;Use(target);try{List<Action<BossRushCoopMessage>> callbacks;if(target.Handlers.TryGetValue(topic,out callbacks))foreach(var callback in callbacks.ToArray())callback(new BossRushCoopMessage{SenderId=source.Id,Json=json,IsFromHost=source.Host});}finally{Use(saved);}});return true;
        }
        internal static void Use(Peer peer){Current=peer;CharacterMainControl.Main=peer.Body;EscapeFromDuckovCoopMod.COOPManager.ItemNet=peer.NetworkItems;ZombieModeRuntimeModule.Select(peer.Module);}
        internal static void Flush(){int count=0;while(Wire.Count>0){if(++count>1000)throw new Exception("wire did not quiesce");Wire.Dequeue()();}}
        private sealed class Subscription:IDisposable{private Action release;internal Subscription(Action value){release=value;}public void Dispose(){release?.Invoke();release=null;}}
    }
    internal static class BossRushCoopReflection
    {
        internal static Type FindType(string name){return typeof(BossRushCoop).Assembly.GetType(name);}
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal readonly ZombieModeRunState runState=new ZombieModeRunState();internal bool Invalid,Paused;
        private static ZombieModeRuntimeModule coopZombieOwner;
        internal readonly ModBehaviour owner=new ModBehaviour();
        private readonly Dictionary<string,string> coopZombieParticipants=new Dictionary<string,string>();
        private readonly HashSet<string> coopZombieStarterReady=new HashSet<string>();private string coopZombieLocalAsset="slot-A";
        internal ZombieModeRuntimeModule(){InitializeCoopZombieDropClaims();}
        internal void Admit(BossRushCoopPlayer p){coopZombieParticipants[p.Id]=p.AssetId;coopZombieStarterReady.Add(p.Id+":"+p.AssetId);}
        internal void Tick(){TickCoopZombieDropClaims(2);}
        internal void Reset(){ResetCoopZombieDropClaims();}
        internal void BindRun(int run){runState.RunId=run;ResetCoopZombieDropClaims(run);}
        internal void Dispose(){CleanupCoopZombieDropClaims();}
        internal void Mark(Item item){RegisterCoopZombieSharedDrop(runState.RunId,item);}
        internal int PendingResults{get{return coopZombieDropResults.Count;}}
        private bool IsZombieModeRunValid(int run){return !Invalid&&run==runState.RunId;}
        private bool IsZombieModeRuntimePaused(){return Paused;}
        private bool IsCoopZombieParticipant(BossRushCoopPlayer p){string asset;return p!=null&&p.IsConnected&&coopZombieParticipants.TryGetValue(p.Id,out asset)&&asset==p.AssetId&&coopZombieStarterReady.Contains(p.Id+":"+asset);}
        private bool IsCoopZombieLocalAssetCurrent(){return BossRushCoop.LocalAssetId==coopZombieLocalAsset;}
        internal static void Select(ZombieModeRuntimeModule module)
        {
            // 只选择当前进程的静态 owner；每个 peer 的模块、物品、背包与协议队列独立。
            foreach(var field in typeof(ZombieModeRuntimeModule).GetFields(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public))
                if(field.FieldType==typeof(ZombieModeRuntimeModule))field.SetValue(null,module);
        }
    }
}
