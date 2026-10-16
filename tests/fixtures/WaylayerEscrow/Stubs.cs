using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed; private static int next; private readonly int instance=++next; internal int GetInstanceID() {return instance;}
        internal static void Destroy(Object value) { value.Destroyed=true; }
        public static bool operator ==(Object a,Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b); }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object value) { return ReferenceEquals(this,value); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    internal sealed class GameObject:Object { internal readonly List<Object> Components=new List<Object>(); internal void DestroyTree() { Destroy(this);foreach(Object component in Components)Destroy(component); } }
    internal sealed class Transform { internal Vector3 position; }
    internal struct Vector3 { internal float x; public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3 {x=a.x-b.x}; } internal float sqrMagnitude { get { return x*x; } } }
    internal static class Time { internal static float time;internal static int frameCount; }
    internal static class Mathf { internal static float Max(float a,float b) { return Math.Max(a,b); } }
    internal static class Debug { internal static void LogWarning(string value) { } }
}
namespace Duckov.Utilities
{
    [Serializable] internal sealed class CustomData { public string Key;public int Value;public string Text;internal CustomData(string key,int value) {Key=key;Value=value;} internal CustomData(string key,string value) {Key=key;Text=value;} internal int GetInt() {return Value;} internal string GetString() {return Text;} }
}
namespace ItemStatsSystem
{
    internal sealed class Inventory
    {
        internal readonly List<Item> Content=new List<Item>();internal int Capacity=8;internal bool Reject,ThrowAfterAdd;
        internal Item GetItemAt(int slot) {return slot<Content.Count?Content[slot]:null;}
        internal bool AddItem(Item item) {if(Reject)return false;int slot=Content.FindIndex(i=>i==null);if(slot<0)slot=Content.Count;AddAt(item,slot);return true;}
        internal void AddAt(Item item,int slot) {if(Reject)return;while(Content.Count<=slot)Content.Add(null);if(Content[slot]!=null)return;Content[slot]=item;item.InInventory=this;if(ThrowAfterAdd)throw new Exception("notification");}
        internal void Remove(Item item) {int slot=Content.IndexOf(item);if(slot>=0)Content[slot]=null;item.InInventory=null;}
    }
    internal sealed class AgentUtilities {internal object ActiveAgent;}
    internal sealed class Item:UnityEngine.Object
    {
        internal int TypeID;internal bool Stackable=true,IsBeingDestroyed,OnGround,RejectCount,ThrowAfterCount,RejectDetach;
        internal string Metadata;internal Inventory InInventory,Inventory;internal AgentUtilities AgentUtilities;internal readonly List<Duckov.Utilities.CustomData> Variables=new List<Duckov.Utilities.CustomData>();
        // Official variable lookup does not create a capturing predicate for every read.
        internal string GetString(string key) {foreach(var value in Variables)if(value.Key==key)return value.Text;return null;}
        internal void SetString(string key,string value) {Variables.RemoveAll(v=>v.Key==key);Variables.Add(new Duckov.Utilities.CustomData(key,value));}
        internal List<Item> GetAllChildren(bool self,bool unused) {var result=new List<Item>();if(self)result.Add(this);if(Inventory!=null)foreach(var item in Inventory.Content)if(item!=null)result.AddRange(item.GetAllChildren(true,false));return result;}
        private int count=1;internal int StackCount {get{return count;}set{if(RejectCount)return;count=value;if(ThrowAfterCount)throw new Exception("count notification");}}
        internal void Detach() {if(!RejectDetach&&InInventory!=null)InInventory.Remove(this);}
        internal void DestroyTree() {if(InInventory!=null)InInventory.Remove(this);IsBeingDestroyed=true;Destroyed=true;}
        internal void Drop(CharacterMainControl player,bool toss) {if(ItemUtilities.RejectDrop)return;OnGround=true;}
    }
    internal static class ItemAssetsCollection
    {
        internal static readonly Dictionary<int,Item> Prefabs=new Dictionary<int,Item>();
        internal static Item GetPrefab(int type) {Item item;return Prefabs.TryGetValue(type,out item)?item:null;}
    }
    internal static class ItemUtilities {internal static bool RejectSend,RejectDrop;internal static void SendToPlayer(Item item,bool ignore,bool storage) {if(!RejectSend)CharacterMainControl.Main.CharacterItem.Inventory.AddItem(item);}}
}
namespace ItemStatsSystem.Data
{
    [Serializable] internal sealed class ItemTreeData
    {
        public int rootInstanceID;
        public List<DataEntry> entries=new List<DataEntry>();internal DataEntry RootData {get{return entries.Find(v=>v.instanceID==rootInstanceID);}}internal int RootTypeID {get{return RootData==null?0:RootData.typeID;}}
        [Serializable] internal sealed class DataEntry {public int instanceID,typeID;public string Metadata;public List<Duckov.Utilities.CustomData> variables=new List<Duckov.Utilities.CustomData>();public List<InventoryDataEntry> inventory=new List<InventoryDataEntry>();public List<SlotInstanceIDPair> slotContents=new List<SlotInstanceIDPair>();public List<int> inventorySortLocks=new List<int>();internal int StackCount {get {var v=variables.Find(c=>c.Key=="Count");return v==null?1:v.Value;}}}
        [Serializable] internal sealed class InventoryDataEntry {public int position,instanceID;internal InventoryDataEntry(int p,int id){position=p;instanceID=id;}}
        [Serializable] internal sealed class SlotInstanceIDPair {public string slot;public int instanceID;}
        internal DataEntry GetEntry(int id) {return entries.Find(v=>v.instanceID==id);}
        internal static ItemTreeData FromItem(ItemStatsSystem.Item item) {var tree=new ItemTreeData {rootInstanceID=item.GetInstanceID()};foreach(var child in item.GetAllChildren(true,false)){var entry=new DataEntry {instanceID=child.GetInstanceID(),typeID=child.TypeID,Metadata=child.Metadata};foreach(var v in child.Variables)entry.variables.Add(new Duckov.Utilities.CustomData(v.Key,v.Text){Value=v.Value});entry.variables.RemoveAll(v=>v.Key=="Count");entry.variables.Add(new Duckov.Utilities.CustomData("Count",child.StackCount));if(child.Inventory!=null)for(int i=0;i<child.Inventory.Content.Count;i++)if(child.Inventory.Content[i]!=null)entry.inventory.Add(new InventoryDataEntry(i,child.Inventory.Content[i].GetInstanceID()));tree.entries.Add(entry);}return tree;}
        internal static ItemTreeData LastInput;internal static TaskCompletionSource<ItemStatsSystem.Item> Deferred;internal static int FactoryCalls;internal static bool Fail;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static Task<ItemStatsSystem.Item> InstantiateAsync(ItemTreeData tree) {LastInput=tree;FactoryCalls++;if(Deferred!=null)return Deferred.Task;if(Fail)return Task.FromException<ItemStatsSystem.Item>(new Exception("factory failed"));return Task.FromResult(Clone(tree));}
        internal static ItemStatsSystem.Item Clone(ItemTreeData tree) {return CloneEntry(tree,tree.RootData);}
        private static ItemStatsSystem.Item CloneEntry(ItemTreeData tree,DataEntry entry) {var item=new ItemStatsSystem.Item {TypeID=entry.typeID,StackCount=entry.StackCount,Metadata=entry.Metadata};foreach(var value in entry.variables)item.Variables.Add(new Duckov.Utilities.CustomData(value.Key,value.Text){Value=value.Value});if(entry.inventory.Count>0){item.Inventory=new ItemStatsSystem.Inventory();foreach(var child in entry.inventory)item.Inventory.AddAt(CloneEntry(tree,tree.GetEntry(child.instanceID)),child.position);}return item;}
    }
}
internal sealed class Health:UnityEngine.Object {internal bool IsDead;}
internal sealed class CharacterMainControl:UnityEngine.Object
{
    internal static CharacterMainControl Main;internal ItemStatsSystem.Item CharacterItem;internal readonly Health Health=new Health();internal readonly UnityEngine.Transform transform=new UnityEngine.Transform();internal readonly UnityEngine.GameObject gameObject=new UnityEngine.GameObject();
    internal CharacterMainControl() {gameObject.Components.Add(this);gameObject.Components.Add(Health);}
}
namespace BossRush
{
    internal static class L10n {internal static string T(string cn,string en){return en;}}
    internal static class ModBehaviour {internal static void DevLog(string value) {}}
    internal sealed class BossRushCoopPlayer {internal string Id,AssetId;internal bool IsConnected=true,IsLocal;internal CharacterMainControl Character;internal bool IsAlive {get{return IsConnected&&Character!=null&&!Character.Health.IsDead;}}}
    internal sealed class BossRushCoopMessage {internal bool IsFromHost;internal string SenderId;internal object Packet;internal T Read<T>() {return BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(Packet));}}
    internal static class BossRushCoop
    {
        internal static bool IsHost,IsClient,HasBoundScene=true;internal static string SceneToken="scene-2",LocalReadyEpoch="ready";internal static object NativeService=new object(),NativeHostPeer=new object();internal static int SceneGeneration=2;internal static string RunId="room",LocalPlayerId,LocalAssetId;
        internal static readonly Dictionary<string,BossRushCoopPlayer> Players=new Dictionary<string,BossRushCoopPlayer>();internal static readonly Dictionary<string,CharacterMainControl> Entities=new Dictionary<string,CharacterMainControl>();
        internal sealed class SentPacket {internal string Target,Topic;internal object Value;}
        internal static readonly List<SentPacket> Sent=new List<SentPacket>();internal static readonly Dictionary<string,Action<BossRushCoopMessage>> Handlers=new Dictionary<string,Action<BossRushCoopMessage>>();
        internal static BossRushCoopPlayer FindPlayer(string id) {BossRushCoopPlayer p;return id!=null&&Players.TryGetValue(id,out p)?p:null;}
        internal static string GetPlayerId(CharacterMainControl target) {foreach(var p in Players.Values)if(ReferenceEquals(p.Character,target))return p.Id;return null;}
        internal static string GetEntityId(CharacterMainControl target) {foreach(var p in Entities)if(ReferenceEquals(p.Value,target))return p.Key;return null;}
        internal static CharacterMainControl FindEntity(string id) {CharacterMainControl target;return id!=null&&Entities.TryGetValue(id,out target)?target:null;}
        private sealed class Sub:IDisposable {internal string Key;public void Dispose(){Handlers.Remove(Key);}}
        internal static IDisposable Register(string topic,Action<BossRushCoopMessage> handler) {string key=LocalPlayerId+":"+topic;Handlers[key]=handler;return new Sub {Key=key};}
        internal static void SendToPlayer<T>(string target,string topic,T value) {Sent.Add(new SentPacket {Target=target,Topic=topic,Value=BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(value))});}
        internal static void SendToHost<T>(string topic,T value) {SendToPlayer("host",topic,value);}
        internal static void Receive(string target,string topic,object value,string sender,bool fromHost) {Handlers[target+":"+topic](new BossRushCoopMessage {Packet=value,SenderId=sender,IsFromHost=fromHost});}
    }
    internal sealed class CoopPersonalDelivery<T>:IDisposable where T:class
    {
        internal sealed class Queued {internal string Player,Asset;internal T Value;}
        internal static readonly List<Queued> Queue=new List<Queued>();internal static readonly Dictionary<string,Func<T,bool>> Apply=new Dictionary<string,Func<T,bool>>();private readonly string player;
        internal CoopPersonalDelivery(string name,Func<T,bool> apply) {player=BossRushCoop.LocalPlayerId;Apply[player]=apply;}
        internal bool Deliver(string target,string id,T value,string asset) {Queue.Add(new Queued {Player=target,Asset=asset,Value=value});return true;}
        internal void Tick(float delta) { }public void Dispose() {Apply.Remove(player);}
    }
    internal static class SkyIslandInventoryTransaction {internal static bool HasOwner(ItemStatsSystem.Item item) {return item!=null&&(item.InInventory!=null||item.OnGround);}internal static void DestroyUnowned(ItemStatsSystem.Item item) {if(item!=null&&!HasOwner(item))item.DestroyTree();}}
    internal static class SkyIslandBossRules {internal const float SnatchReach=2.9f,SnatchCooldown=12f;internal static int StealScore(int type) {return type==5001?10:type==5002?20:-1;}}
    internal sealed partial class SkyIslandWaylayerChief:UnityEngine.Object
    {
        internal readonly ItemStatsSystem.Inventory Bag=new ItemStatsSystem.Inventory();private readonly List<ItemStatsSystem.Item> stolen=new List<ItemStatsSystem.Item>();internal bool Valid=true,coopSnatchPending;private float snatchReadyAt;internal int Snatches;internal bool ThrowEffect;
        internal bool CanAcceptCoopSnatch {get{return Valid&&Snatches<2;}}
        private ItemStatsSystem.Inventory BossInventory() {return Bag;}
        private void FinishSnatch(CharacterMainControl player,int type) {Snatches++;if(ThrowEffect)throw new Exception("effect");}
    }
}
namespace Duckov.UI
{
    internal static class NotificationText {internal static readonly List<string> Notices=new List<string>();internal static void Push(string text){Notices.Add(text);}}
}
