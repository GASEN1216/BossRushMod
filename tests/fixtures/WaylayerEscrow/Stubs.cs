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
        internal readonly List<Item> Content=new List<Item>();internal int Capacity=8;internal bool Reject,ThrowAfterAdd;internal Action<Item> AfterAdd;
        internal Item AttachedToItem;internal readonly UnityEngine.Transform transform=new UnityEngine.Transform();
        internal Item GetItemAt(int slot) {return slot<Content.Count?Content[slot]:null;}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal bool AddItem(Item item) {
#if !WAYLAYER_HARMONY_FIXTURE
        if(!BossRush.CoopWaylayerClaims.AllowInventory(item,this,false))return false;
#endif
        if(Reject)return false;int slot=Content.FindIndex(i=>i==null);if(slot<0)slot=Content.Count;return AddAt(item,slot);}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal bool AddAt(Item item,int slot) {
#if !WAYLAYER_HARMONY_FIXTURE
        if(!BossRush.CoopWaylayerClaims.AllowInventory(item,this,true))return false;
#endif
        if(Reject||item==null||item.InInventory!=null||slot<0||slot>=Capacity)return false;while(Content.Count<=slot)Content.Add(null);if(Content[slot]!=null)return false;Content[slot]=item;item.InInventory=this;if(AfterAdd!=null)AfterAdd(item);if(ThrowAfterAdd)throw new Exception("notification");return true;}
        internal Action<Item> AfterRemove;internal bool RemoveItem(Item item) {if(item.InInventory!=this)return false;Remove(item);if(AfterRemove!=null)AfterRemove(item);return true;}
        internal void Remove(Item item) {int slot=Content.IndexOf(item);if(slot>=0)Content[slot]=null;item.InInventory=null;}
    }
    internal sealed class ItemAgent {internal readonly UnityEngine.Transform transform=new UnityEngine.Transform();}
    internal sealed class AgentUtilities {internal ItemAgent ActiveAgent;internal void ReleaseActiveAgent(){ActiveAgent=null;}}
    internal sealed class Item:UnityEngine.Object
    {
        internal int TypeID;internal bool Stackable=true,IsBeingDestroyed,OnGround,RejectCount,ThrowAfterCount,RejectDetach;
        internal Action<Item> AfterCount;internal string Metadata;internal Inventory InInventory;private Inventory inventory;internal Inventory Inventory {get{return inventory;}set{inventory=value;if(value!=null)value.AttachedToItem=this;}}
        internal ItemStatsSystem.Items.Slot PluggedIntoSlot;internal Item ParentItem {get{return InInventory!=null?InInventory.AttachedToItem:PluggedIntoSlot!=null?PluggedIntoSlot.Master:null;}}
        internal readonly UnityEngine.Transform transform=new UnityEngine.Transform();internal ItemAgent ActiveAgent {get{return AgentUtilities==null?null:AgentUtilities.ActiveAgent;}}internal AgentUtilities AgentUtilities;internal readonly List<Duckov.Utilities.CustomData> Variables=new List<Duckov.Utilities.CustomData>();
        // Official variable lookup does not create a capturing predicate for every read.
        internal string GetString(string key) {foreach(var value in Variables)if(value.Key==key)return value.Text;return null;}
        internal void SetString(string key,string value) {Variables.RemoveAll(v=>v.Key==key);Variables.Add(new Duckov.Utilities.CustomData(key,value));}
        internal List<Item> GetAllChildren(bool self,bool unused) {var result=new List<Item>();if(self)result.Add(this);if(Inventory!=null)foreach(var item in Inventory.Content)if(item!=null)result.AddRange(item.GetAllChildren(true,false));return result;}
        private int count=1;internal int StackCount {get{return count;}set{if(RejectCount)return;count=value;if(AfterCount!=null)AfterCount(this);if(ThrowAfterCount)throw new Exception("count notification");}}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal void Detach() {
#if !WAYLAYER_HARMONY_FIXTURE
        if(!BossRush.CoopWaylayerClaims.AllowDetach(this))return;
#endif
        if(!RejectDetach&&InInventory!=null)InInventory.Remove(this);}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal void Combine(Item incomingItem) {
#if !WAYLAYER_HARMONY_FIXTURE
        if(!BossRush.CoopWaylayerClaims.AllowCombine(this,incomingItem))return;
#endif
        StackCount+=incomingItem.StackCount;incomingItem.StackCount=0;incomingItem.DestroyTree();}
        internal void DestroyTree() {if(InInventory!=null)InInventory.Remove(this);IsBeingDestroyed=true;Destroyed=true;}
        internal void Drop(CharacterMainControl player,bool toss) {if(!BossRush.CoopWaylayerClaims.AllowDrop(this)||ItemUtilities.RejectDrop)return;OnGround=true;AgentUtilities=new AgentUtilities {ActiveAgent=new ItemAgent()};AgentUtilities.ActiveAgent.transform.position=player.transform.position;transform.position=player.transform.position;}
    }
    internal static class ItemAssetsCollection
    {
        internal static readonly Dictionary<int,Item> Prefabs=new Dictionary<int,Item>();
        internal static Item GetPrefab(int type) {Item item;return Prefabs.TryGetValue(type,out item)?item:null;}
    }
    internal static class ItemUtilities {
        internal static int MergeCalls;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static bool AddAndMerge(Inventory inventory,Item item,int preferedFirstPosition=0) {
#if !WAYLAYER_HARMONY_FIXTURE
        if(!BossRush.CoopWaylayerClaims.AllowMerge(inventory,item))return false;
#endif
        MergeCalls++;return inventory.AddItem(item);}
        internal static bool RejectSend,RejectDrop;internal static void SendToPlayer(Item item,bool ignore,bool storage) {if(!RejectSend)CharacterMainControl.Main.CharacterItem.Inventory.AddItem(item);}}
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
        internal static bool IsHost,IsClient,HasBoundScene=true;internal static bool IsConnected {get{return IsHost||IsClient;}}internal static bool HasWorldAuthority {get{return IsHost;}}internal static string SceneToken="scene-2",LocalReadyEpoch="ready";internal static object NativeService=new object(),NativeHostPeer=new object();internal static int SceneGeneration=2;internal static string RunId="room",LocalPlayerId,LocalAssetId;
        internal sealed class PlayerMap:Dictionary<string,BossRushCoopPlayer> {public new IEnumerator<BossRushCoopPlayer> GetEnumerator(){return Values.GetEnumerator();}}
        internal static readonly PlayerMap Players=new PlayerMap();internal static readonly Dictionary<string,CharacterMainControl> Entities=new Dictionary<string,CharacterMainControl>();
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
        internal static event Action<ItemStatsSystem.Item,Dictionary<string,string>> ItemSnapshotRequested;
        internal static event Action<ItemStatsSystem.Item,IReadOnlyDictionary<string,string>> ItemSnapshotApplied;
        internal static bool Broadcast<T>(string topic,T value){Sent.Add(new SentPacket {Target="*",Topic=topic,Value=value});return true;}
        internal static void Restore(ItemStatsSystem.Item item,IReadOnlyDictionary<string,string> data){if(ItemSnapshotApplied!=null)ItemSnapshotApplied(item,data);}
        internal static void Receive(string target,string topic,object value,string sender,bool fromHost) {Handlers[target+":"+topic](new BossRushCoopMessage {Packet=value,SenderId=sender,IsFromHost=fromHost});}
    }
    internal sealed class CoopPersonalDelivery<T>:IDisposable where T:class
    {
        internal sealed class Queued {internal string Player,Asset;internal T Value;}
        internal static readonly List<Queued> Queue=new List<Queued>();internal static readonly Dictionary<string,Func<T,bool>> Apply=new Dictionary<string,Func<T,bool>>();internal static bool RejectDelivery;private readonly string player;private readonly ProductionCoopPersonalDelivery<T> runtime;
        internal CoopPersonalDelivery(string name,Func<T,bool> apply) {player=BossRushCoop.LocalPlayerId;Apply[player]=apply;if(typeof(T)==typeof(CoopWaylayerJournal.Commit))runtime=new ProductionCoopPersonalDelivery<T>(name,apply);}
        internal bool Deliver(string target,string id,T value,string asset) {if(RejectDelivery)return false;Queue.Add(new Queued {Player=target,Asset=asset,Value=value});return runtime!=null?runtime.Deliver(target,id,value,asset):true;}
        internal void Tick(float delta) {if(runtime!=null)runtime.Tick(delta);}public void Dispose() {if(runtime!=null)runtime.Dispose();Apply.Remove(player);}
    }
    internal static class BossRushCoopTransport {internal static object CaptureNetworkDrop(ItemStatsSystem.Item item){return null;}internal static bool CompleteNetworkDrop(object value){return true;}}
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

namespace ItemStatsSystem.Items {
 internal sealed class Slot {internal ItemStatsSystem.Item Master,Content;
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 internal bool Plug(ItemStatsSystem.Item otherItem,out ItemStatsSystem.Item unpluggedItem){unpluggedItem=null;
#if !WAYLAYER_HARMONY_FIXTURE
 if(!BossRush.CoopWaylayerClaims.AllowSlot(otherItem,this))return false;
#endif
 Content=otherItem;otherItem.PluggedIntoSlot=this;return true;}}
}
#if !WAYLAYER_HARMONY_FIXTURE
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)]internal sealed class HarmonyPatch:Attribute {internal HarmonyPatch(Type t,string n){}internal HarmonyPatch(Type t,string n,Type[] a){}}
 [AttributeUsage(AttributeTargets.Method)]internal sealed class HarmonyPrefix:Attribute {}
}
#endif

internal sealed class CharacterItemControl {internal CharacterMainControl characterMainControl;[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]internal bool PickupItem(ItemStatsSystem.Item item){
#if !WAYLAYER_HARMONY_FIXTURE
 if(!BossRush.CoopWaylayerClaims.AllowPickup(this,item))return false;
#endif
 return characterMainControl.CharacterItem.Inventory.AddItem(item);}}
internal static class ItemExtensions {[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] internal static ItemStatsSystem.ItemAgent Drop(ItemStatsSystem.Item item,UnityEngine.Vector3 pos,bool body,UnityEngine.Vector3 direction,float angle){return null;}}

namespace BossRush {internal static class CoopWaylayerNativeBuildScope {private sealed class Lease:IDisposable{public void Dispose(){}} internal static IDisposable Acquire(){return new Lease();}internal static void EnsureInstalled(){}internal static void RegisterTicketRoot(ItemStatsSystem.Item item){}internal static bool AllowInventory(ItemStatsSystem.Item item,ItemStatsSystem.Inventory inventory,bool mutation){return false;}internal static bool AllowSlot(ItemStatsSystem.Item item,ItemStatsSystem.Items.Slot slot){return false;}}}
