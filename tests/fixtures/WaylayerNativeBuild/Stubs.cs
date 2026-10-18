using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ItemStatsSystem;
using ItemStatsSystem.Items;
namespace UnityEngine
{
 public class Object
 {
  public bool Dead; public string name="test";
  public static implicit operator bool(Object value){return !ReferenceEquals(value,null)&&!value.Dead;}
  public static bool operator ==(Object a,Object b){return (ReferenceEquals(a,null)||a.Dead)?ReferenceEquals(b,null)||b.Dead:ReferenceEquals(a,b);}
  public static bool operator !=(Object a,Object b){return !(a==b);}
  public override bool Equals(object other){return ReferenceEquals(this,other);}public override int GetHashCode(){return RuntimeHelpers.GetHashCode(this);}
  public static Action<InteractableLootbox> Instantiated;
  public static T Instantiate<T>(T value,Vector3 position,Quaternion rotation) where T:Object {var box=new InteractableLootbox{gameObject=new GameObject()};if(Instantiated!=null)Instantiated(box);return (T)(Object)box;}
  public static void Destroy(Object item){if(item!=null){var go=item as GameObject;if(go!=null&&go.Owner!=null)go.Owner.Dead=true;item.Dead=true;}}
 }
 public class GameObject:Object{public Item Owner;}
 public class Component:Object{public GameObject gameObject{get;set;}public Transform transform{get;set;}=new Transform();}
 public class Transform{public void SetParent(Transform value){}}
 public static class Mathf{public static int Max(int a,int b){return Math.Max(a,b);}}
 public static class Debug{public static void Log(object text){} public static void LogError(object text){}}
}
namespace Cysharp.Threading.Tasks
{
 public struct UniTask<T>{private T value;public UniTask(T value){this.value=value;}public Awaiter GetAwaiter(){return new Awaiter(value);}public struct Awaiter{private T value;public Awaiter(T value){this.value=value;}public T GetResult(){return value;}}}
}
namespace ItemStatsSystem
{
 public sealed class Item:UnityEngine.Component
 {
  public int TypeID{get;set;}public int StackCount{get;set;}public float Durability{get;set;}public float DurabilityLoss{get;set;}public bool Inspected{get;set;}public bool Sticky{get;set;}public bool Stackable{get;set;}public int MaxStackCount{get;set;}=10;public Duckov.Utilities.TagCollection Tags{get;set;}=new Duckov.Utilities.TagCollection();
  public bool IsBeingDestroyed{get{return Dead;}}public string DisplayName{get{return name;}}
  public Inventory Inventory{get;set;}public SlotCollection Slots{get;set;}public object ActiveAgent;
  public Inventory InInventory;public Slot PluggedIntoSlot;public Action<Item> Added;
  public Item ParentItem{get{return InInventory!=null?InInventory.AttachedToItem:PluggedIntoSlot!=null?PluggedIntoSlot.Master:null;}}
  public UnityEngine.Object ParentObject{get{return InInventory!=null?(UnityEngine.Object)InInventory:PluggedIntoSlot!=null?PluggedIntoSlot.Master:null;}}
  private readonly Dictionary<string,string> values=new Dictionary<string,string>();
  public Item(int type=1){TypeID=type;gameObject=new UnityEngine.GameObject{Owner=this};Slots=new SlotCollection(this);}
  public string GetString(string key){string value;return values.TryGetValue(key,out value)?value:null;}public void SetString(string key,string value){values[key]=value;}
  public void NotifyAddedToInventory(Inventory inventory){InInventory=inventory;if(Added!=null)Added(this);}
  public void NotifyPluggedTo(Slot slot){PluggedIntoSlot=slot;if(Added!=null)Added(this);}
  public void NotifySlotPlugged(Slot slot){}public void InitiateNotifyItemTreeChanged(){}
  public void Combine(Item other){int added=Math.Min(MaxStackCount-StackCount,other.StackCount);StackCount+=added;other.StackCount-=added;if(other.StackCount==0){other.Detach();UnityEngine.Object.Destroy(other.gameObject);}}
  public void DestroyTree(){Detach();UnityEngine.Object.Destroy(gameObject);}
  public void Detach(){if(InInventory!=null){InInventory.content.Remove(this);InInventory=null;}if(PluggedIntoSlot!=null){PluggedIntoSlot.content=null;PluggedIntoSlot=null;}}
 }
 public sealed partial class Inventory:UnityEngine.Component,System.Collections.Generic.IEnumerable<Item>
 {
  public Item AttachedToItem;public int Capacity{get;private set;}public readonly List<Item> content=new List<Item>();public Action<Inventory,int> onContentChanged;
  public static int Enumerations;public bool NeedInspection{get;set;}public int GetLastItemPosition(){return content.FindLastIndex(i=>i!=null);}
  public Inventory(Item owner,int capacity=4){AttachedToItem=owner;Capacity=capacity;}
  public void SetCapacity(int value){Capacity=value;}public void RecalculateWeight(){}
  public Item GetItemAt(int index){return index>=0&&index<content.Count?content[index]:null;}
  public int GetFirstEmptyPosition(int from){for(int i=0;i<Capacity;i++)if(GetItemAt(i)==null)return i;return -1;}
  public bool RemoveAt(int index,out Item removed){removed=GetItemAt(index);if(removed==null)return false;content[index]=null;removed.InInventory=null;return true;}
  public System.Collections.Generic.IEnumerator<Item> GetEnumerator(){Enumerations++;return content.GetEnumerator();}System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator(){return GetEnumerator();}
 }
 public static class ItemAssetsCollection
 {
  public static readonly Queue<Item> Items=new Queue<Item>();
  public static Cysharp.Threading.Tasks.UniTask<Item> InstantiateAsync(int type){Item item=Items.Count>0?Items.Dequeue():new Item(type);return new Cysharp.Threading.Tasks.UniTask<Item>(item);}
 }
}
namespace ItemStatsSystem.Items
{
 public sealed class SlotCollection:UnityEngine.Component,System.Collections.Generic.IEnumerable<Slot>
 {
  public static int Enumerations;public readonly Item Root;private readonly Dictionary<string,Slot> slots=new Dictionary<string,Slot>();
  public SlotCollection(Item root){Root=root;}public Slot GetSlot(string key){Slot slot;if(!slots.TryGetValue(key,out slot))slots.Add(key,slot=new Slot(this));return slot;}
  public System.Collections.Generic.IEnumerator<Slot> GetEnumerator(){Enumerations++;return slots.Values.GetEnumerator();}System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator(){return GetEnumerator();}
 }
 public sealed partial class Slot
 {
  public Item content;public Item Content{get{return content;}}public readonly SlotCollection collection;public Item Master{get{return collection.Root;}}public string DisplayName{get{return "slot";}}
  public Action<Slot> onSlotContentChanged;public Slot(SlotCollection value){collection=value;}
  private bool CheckAbleToPlug(Item item){return item!=null;}
  public Item Unplug(){Item previous=content;content=null;if(previous!=null)previous.PluggedIntoSlot=null;return previous;}
 }
}
public sealed class CharacterMainControl{public static CharacterMainControl Main;public Item CharacterItem;}
namespace BossRush
{
 internal sealed partial class CoopWaylayerClaims
 {
  [ThreadStatic]private static Admission admission;
  internal static CoopWaylayerClaims Current=new CoopWaylayerClaims();private readonly HashSet<Inventory> lockedInventories=new HashSet<Inventory>();
  internal static bool IsWorldTicket(Item item){return item!=null&&item.GetString("lock")=="world";}
  internal static bool Locked(Item item){return item!=null&&!string.IsNullOrEmpty(item.GetString("lock"));}
  private static void TryClaim(Item item,Item destination){}
  internal static void TrackCorpse(Item item,Inventory inventory){if(item.InInventory==inventory)Anchors.Add(item);}
  internal static readonly List<Item> Anchors=new List<Item>();
 }
 internal sealed class BossRushCoopPlayer{public CharacterMainControl Character;}
 internal static class BossRushCoop{public static readonly List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();}
 internal static class ModBehaviour{public static readonly List<string> Warnings=new List<string>();public static void CriticalLog(string key,string text){Warnings.Add(text);}}
}
public partial class InteractableLootbox:UnityEngine.Component
{
 public Inventory Inventory{get;set;}public static InteractableLootbox Prefab{get;set;}=new InteractableLootbox();public bool needInspect=true;
 [MethodImpl(MethodImplOptions.NoInlining)]private void CreateLocalInventory(){Inventory=new Inventory(null,512){gameObject=gameObject};}
 [MethodImpl(MethodImplOptions.NoInlining)]
 public static InteractableLootbox CreateFromItem(Item item,UnityEngine.Vector3 position,UnityEngine.Quaternion rotation,bool moveToMainScene=true,InteractableLootbox prefab=null,bool filterDontDropOnDead=false){throw new Exception("official corpse body not installed");}
}
namespace UnityEngine{public struct Vector3{}public struct Quaternion{}}
public static partial class ItemUtilities{public static int Merges;}
namespace Duckov.Utilities
{
 public class Tag:UnityEngine.Object{}
 public class TagCollection{private HashSet<Tag> tags=new HashSet<Tag>();public bool Contains(Tag tag){return tags.Contains(tag);}public void Add(Tag tag){tags.Add(tag);}}
 public static class GameplayDataSettings
 {
  public class TagsData{public Tag DestroyOnLootBox{get;set;}=new Tag();public Tag DontDropOnDeadInSlot{get;set;}=new Tag();}
  public static TagsData Tags{get;set;}=new TagsData();
 }
}
namespace UnityEngine.SceneManagement{public struct Scene{public int buildIndex{get;set;}}public static class SceneManager{public static Scene GetActiveScene(){return new Scene();}}}
namespace Duckov.Scenes{public static class MultiSceneCore{public static int Calls;public static void MoveToActiveWithScene(UnityEngine.GameObject box,int buildIndex){Calls++;}}}
namespace ItemStatsSystem{public static class ItemTreeExtensions{public static void DestroyTree(Item item){item.DestroyTree();}}}
namespace EscapeFromDuckovCoopMod
{
 public struct ItemSnapshot{public int TypeId,Stack,InventoryCapacity;public bool HasDurability;public float Durability;public ItemInventoryEntrySnapshot[] Inventory;public ItemSlotSnapshot[] Slots;public string[] CustomDataKeys,CustomDataValues;}
 public struct ItemInventoryEntrySnapshot{public int Slot;public ItemSnapshot Item;}
 public struct ItemSlotSnapshot{public string Key;public bool HasItem;public ItemSnapshot Item;}
 public static class ItemTool
 {
  public static Action<Item,ItemSnapshot> Applied;
  [MethodImpl(MethodImplOptions.NoInlining)]public static Item BuildItemFromSnapshot(ItemSnapshot snapshot){throw new Exception("Actual native IL was not installed");}
  public static void DecodeInventoryCapacity(int packed,out int capacity,out float loss){capacity=packed;loss=0;}
  public static void ApplyCustomDataSnapshot(Item item,ItemSnapshot snapshot){if(snapshot.CustomDataKeys!=null)for(int i=0;i<snapshot.CustomDataKeys.Length;i++)item.SetString(snapshot.CustomDataKeys[i],snapshot.CustomDataValues[i]);if(Applied!=null)Applied(item,snapshot);}
  public static bool ApplySnapshot(Item item,ItemSnapshot snapshot){ApplyCustomDataSnapshot(item,snapshot);return true;}
 }
}
