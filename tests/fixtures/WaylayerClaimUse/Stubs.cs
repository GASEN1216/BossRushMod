using System;using System.Collections.Generic;using System.Runtime.CompilerServices;using ItemStatsSystem;
namespace UnityEngine{
 public class Object {public bool Destroyed;public static implicit operator bool(Object o){return !ReferenceEquals(o,null)&&!o.Destroyed;}public static bool operator==(Object a,Object b){return (!a&&!b)||ReferenceEquals(a,b);}public static bool operator!=(Object a,Object b){return !(a==b);}public override bool Equals(object o){return ReferenceEquals(this,o);}public override int GetHashCode(){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);}}
 public class Component:Object{public Transform transform{get;set;}=new Transform();}public class MonoBehaviour:Component{}
 public class Transform{public Vector3 position{get;set;}public void SetParent(Transform t){}}
 public struct Vector3{public static Vector3 up{get{return new Vector3();}}}
 public static class Mathf{public static int CeilToInt(float x){return (int)Math.Ceiling(x);}}
 public static class Debug{public static void Log(object o){}public static void LogException(Exception e){throw e;}}
 namespace Events{public class UnityEvent{public int Calls;public void Invoke(){Calls++;}}}
}
namespace ItemStatsSystem{
 public class ItemComponent:UnityEngine.MonoBehaviour{}
 public class Item:UnityEngine.Component{
  public int TypeID;public int StackCount{get;set;}=1;public bool Stackable{get;set;}=true;public bool UseDurability{get;set;}public bool IsBeingDestroyed{get;set;}public float MaxDurability{get;set;}=100;public float Durability{get;set;}=100;public string Marker;public Item Root;
  public UsageUtilities usageUtilities=new UsageUtilities();public UsageUtilities UsageUtilities{get{return usageUtilities;}}public ItemAgentUtilities AgentUtilities{get;set;}=new ItemAgentUtilities();public ItemAgent ActiveAgent{get{return AgentUtilities.ActiveAgent;}}public float UseTime{get;set;}=1;
  public Action<Item,object> onUse;public static Action<Item,object> onUseStatic;
  [MethodImpl(MethodImplOptions.NoInlining)]public void Use(object user,bool inBaseLevel=false){throw new Exception("full actual Item.Use IL required");}
  public string GetString(string key){return key==BossRush.CoopWaylayerJournal.ClaimLockKey?Marker:null;}public Item GetRoot(){return Root??this;}public void Detach(){}public void SetInt(string k,int v,bool b){if(k=="Count")StackCount=v;}
 }
 public class ItemAgentUtilities{public ItemAgent ActiveAgent{get;set;}public void ReleaseActiveAgent(){ActiveAgent=null;}}
 public class ItemAgent:UnityEngine.Component{public Item Item{get;set;}}
 public class UsageUtilities:ItemComponent{
  public bool useDurability,dontUseDurabilityInBase;public int durabilityUsage=1;public List<UsageBehavior> behaviors=new List<UsageBehavior>();public UnityEngine.Events.UnityEvent OnItemUsedEvent=new UnityEngine.Events.UnityEvent();public static Action<Item> OnItemUsedStaticEvent;
  [MethodImpl(MethodImplOptions.NoInlining)]public bool IsUsable(Item item,object user){throw new Exception("full actual IsUsable IL required");}
  [MethodImpl(MethodImplOptions.NoInlining)]public void Use(Item item,object user,bool inBaseLevel=false){throw new Exception("full actual Utilities.Use IL required");}
 }
 public abstract class UsageBehavior:UnityEngine.MonoBehaviour{
  public struct DisplaySettingsData{public bool display;public string description;}public virtual DisplaySettingsData DisplaySettings{get{return default(DisplaySettingsData);}}
  public abstract bool CanBeUsed(Item item,object user);protected abstract void OnUse(Item item,object user);
  [MethodImpl(MethodImplOptions.NoInlining)]public void Use(Item item,object user){throw new Exception("full actual Behavior.Use IL required");}
 }
 public static class ItemTreeExtensions{public static Item GetRoot(Item item){return item.Root??item;}public static void DestroyTree(Item item){item.Destroyed=item.IsBeingDestroyed=true;}}
}
public static class ItemExtensions{public static void Drop(Item item,CharacterMainControl c,bool value){}}
public interface IAgentUsable{}
public class DuckovItemAgent:ItemAgent,IAgentUsable{}
public class Health{public float CurrentHealth{get;set;}=20;public float MaxHealth{get;set;}=100;public bool Hurt(DamageInfo info){CurrentHealth-=info.damageValue;return true;}}
public struct DamageInfo{public float damageValue;public UnityEngine.Vector3 damagePoint,damageNormal;public DamageInfo(CharacterMainControl c){damageValue=0;damagePoint=damageNormal=new UnityEngine.Vector3();}}
public class CharacterActionBase:UnityEngine.Object{public CharacterMainControl characterController;public bool Running{get;set;}public InteractableBase InteractingTarget{get;set;}public float ActionTimer{get;set;}public bool StopAction(){Running=false;var use=this as CA_UseItem;if(use!=null)use.Stop();return true;}}
public class CA_Interact:CharacterActionBase{}public class CA_ControlOtherCharacter:CharacterActionBase{}
public class CA_UseItem:CharacterActionBase{
 private Item item;public static Action<Item> OnItemUsedByPlayer;public IAgentUsable agentUsable;public int Sounds;
 public void SetUseItem(Item value){item=value;}public Item Target{get{return item;}}
 [MethodImpl(MethodImplOptions.NoInlining)]protected bool OnStart(){throw new Exception("full actual OnStart IL required");}
 [MethodImpl(MethodImplOptions.NoInlining)]private void OnFinish(){throw new Exception("full actual OnFinish IL required");}
 [MethodImpl(MethodImplOptions.NoInlining)]protected void OnUpdateAction(float deltaTime){throw new Exception("full actual OnUpdateAction IL required");}[MethodImpl(MethodImplOptions.NoInlining)]protected void OnStop(){throw new Exception("full actual OnStop IL required");}public void Tick(float value){ActionTimer=value;OnUpdateAction(value);}public void Stop(){OnStop();}private void StopSound(){}public bool Begin(){return OnStart();}public void Finish(){OnFinish();}private void PostActionSound(){Sounds++;}private void PostUseSound(){Sounds++;}
}
public class LevelManager:UnityEngine.Object{public static LevelManager Instance{get;set;}=new LevelManager();public bool IsBaseLevel{get;set;}}
public class InteractableBase:UnityEngine.Component{}public class InteractableLootbox:InteractableBase{}
public class CharacterMainControl:UnityEngine.Component{
 public static CharacterMainControl Main;public static Action<Item> OnMainCharacterStartUseItem;public bool IsMainCharacter{get;set;}=true;public Health Health{get;set;}=new Health();public Item characterItem=new Item();public Item CharacterItem{get{return characterItem;}}public float Energy,Water;public int Started;
 public CA_ControlOtherCharacter controlOtherCharacterAction=new CA_ControlOtherCharacter();public CA_Interact interactAction=new CA_Interact();public CA_UseItem useItemAction;public CharacterActionBase CurrentAction{get;set;}public DuckovItemAgent CurrentHoldItemAgent{get;set;}
 public CharacterMainControl(){useItemAction=new CA_UseItem{characterController=this};}
 [MethodImpl(MethodImplOptions.NoInlining)]public void UseItem(Item item){throw new Exception("full actual player UseItem IL required");}
 public void SwitchToWeaponBeforeUse(){if(CurrentHoldItemAgent!=null)CurrentHoldItemAgent.Item.AgentUtilities.ReleaseActiveAgent();CurrentHoldItemAgent=null;}public bool PickupItem(Item item){return true;}public void StoreHoldWeaponBeforeUse(){}public bool StartAction(CharacterActionBase action){Started++;CurrentAction=action;action.Running=true;return ((CA_UseItem)action).Begin();}
 public bool ChangeHoldItem(Item item){CurrentHoldItemAgent=new DuckovItemAgent{Item=item};item.AgentUtilities.ActiveAgent=CurrentHoldItemAgent;return true;}
 public void AddHealth(float value){Health.CurrentHealth+=value;}public void AddEnergy(float x){Energy+=x;}public void AddWater(float x){Water+=x;}
}
namespace SodaCraft.Localizations{public static class LocalizationManager{public static string ToPlainText(this string s){return s;}}}
namespace Duckov.UI{public static class NotificationText{public static void Push(string text){}}}
namespace Duckov.ItemUsage{
 public class FoodDrink:UsageBehavior{public float energyValue=35,waterValue=20,UseDurability;[MethodImpl(MethodImplOptions.NoInlining)]public override bool CanBeUsed(Item item,object user){throw new Exception("actual food CanBeUsed required");}[MethodImpl(MethodImplOptions.NoInlining)]protected override void OnUse(Item item,object user){throw new Exception("actual food OnUse required");}[MethodImpl(MethodImplOptions.NoInlining)]private void Eat(CharacterMainControl character){throw new Exception("actual food Eat required");}}
 public class Drug:UsageBehavior{public int healValue=15;public bool useDurability,canUsePart;public float durabilityUsage;[MethodImpl(MethodImplOptions.NoInlining)]public override bool CanBeUsed(Item item,object user){throw new Exception("actual drug CanBeUsed required");}[MethodImpl(MethodImplOptions.NoInlining)]protected override void OnUse(Item item,object user){throw new Exception("actual drug OnUse required");}[MethodImpl(MethodImplOptions.NoInlining)]private bool CheckCanHeal(CharacterMainControl character){throw new Exception("actual CheckCanHeal required");}[MethodImpl(MethodImplOptions.NoInlining)]private void Heal(CharacterMainControl character,Item selfItem,float value){throw new Exception("actual Heal required");}}
}
namespace BossRush{
 internal static partial class CoopWaylayerClaims{} 
 internal static class BossRushCoop{internal static bool IsClient;}
 internal enum SkyIslandFieldBuff{None,Meal,Soothe,Lantern,Incense,Charm,Zapper,Fan}
 internal static class SkyIslandFieldcraftRules{internal static string UsageText(SkyIslandFieldBuff b){return "";}internal const string OffIsland="off island";}
 internal sealed class SkyIslandFieldcraft{internal static SkyIslandFieldcraft Current;internal int Applied;internal bool CanUse(SkyIslandFieldBuff b){return true;}internal bool UseConsumable(SkyIslandFieldBuff b){Applied++;return true;}internal bool QueueCoopGnatUse(SkyIslandFieldBuff b,Item i){return false;}}
 internal static class ModBehaviour{internal static void DevLog(string s){}}
}
