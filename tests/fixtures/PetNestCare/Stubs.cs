using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using ItemStatsSystem;
using Saves;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b); }
        public static bool operator !=(Object a, Object b) { return !(a==b); }
        public override bool Equals(object obj) { return ReferenceEquals(this,obj); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object obj)
        {
            if (obj==null) return;
            var go=obj as GameObject;
            if (go!=null) { foreach(var child in go.transform.Children.ToArray()) Destroy(child.gameObject);foreach(var component in go.Components.ToArray()) Destroy(component); }
            obj.Destroyed=true;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T:Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool inactive=false) where T:Component { return gameObject.GetComponentInChildren<T>(); }
    }
    public class MonoBehaviour:Component { }
    public class GameObject:Object
    {
        public readonly List<Component> Components=new List<Component>();public readonly Transform transform;
        public GameObject(){transform=new Transform{gameObject=this};}
        public T AddComponent<T>() where T:Component,new(){var value=new T{gameObject=this};Components.Add(value);return value;}
        public T GetComponent<T>() where T:Component{return Components.OfType<T>().FirstOrDefault(x=>x!=null);}
        public T GetComponentInChildren<T>() where T:Component{var own=GetComponent<T>();if(own!=null)return own;foreach(var child in transform.Children){var found=child.gameObject.GetComponentInChildren<T>();if(found!=null)return found;}return null;}
    }
    public class Transform:Object {public GameObject gameObject;public readonly List<Transform> Children=new List<Transform>();public Vector3 position;public void SetParent(Transform parent){parent.Children.Add(this);}}
    public struct Vector3 {public float x,y,z;public float sqrMagnitude{get{return x*x+y*y+z*z;}}public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3{x=a.x-b.x,y=a.y-b.y,z=a.z-b.z};}}
    public static class Time{public static int frameCount=1;public static float unscaledTime;}
    public static class Mathf{public static float Min(float a,float b){return Math.Min(a,b);}}
    public static class Debug{public static void LogWarning(string s){}}
}
public partial class InteractableBase:MonoBehaviour
{
    public bool interactableGroup,overrideInteractName,MarkerActive;public string _overrideInteractNameKey;private string defaultInteractNameKey="UI_Interact";
    public readonly List<InteractableBase> Group=new List<InteractableBase>();
    protected virtual void Awake(){}protected virtual bool IsInteractable(){return true;}protected virtual void OnTimeOut(){}
}
public static class SceneLoader{public static bool IsSceneLoading;}
public enum Teams{player,wolf}
public static class Team{public static bool IsEnemy(Teams a,Teams b){return a!=b;}}
public class Health:Component{public bool IsDead;public float MaxHealth=100,CurrentHealth=50;public CharacterMainControl Character;public void SetHealth(float x){CurrentHealth=x;}public CharacterMainControl TryGetCharacter(){return Character;}}
public class CharacterMainControl:Component{public static CharacterMainControl Main;public Item CharacterItem;public Health Health;public Teams Team;public UnityEngine.Transform modelRoot;}
public struct DamageInfo{public CharacterMainControl fromCharacter;}
public class LevelManager{public static LevelManager Instance=new LevelManager();public bool IsBaseLevel;}
public static class LevelConfig{public static bool SavePet;}
public static class PetProxy{public static Inventory PetInventory;}
public static class PlayerStorage{public static Inventory Inventory;}
public static class PlayerStorageBuffer{public static void SaveBuffer(){}}
namespace Duckov.Buffs{public class Buff{public enum BuffExclusiveTags{Bleeding,Poison,Nauseous,Pain,Burning,Electric,Freeze,Stun}}}
namespace Duckov.ItemUsage{public class FoodDrink:Component{public float energyValue=10;}}
namespace Cysharp.Threading.Tasks{public struct UniTask{public void Forget(){}}}
namespace Duckov.UI.DialogueBubbles{public static class DialogueBubblesManager{public static readonly List<string> Lines=new List<string>();public static Transform LastTarget;public static Cysharp.Threading.Tasks.UniTask Show(string s,Transform t,float a,bool b,bool c,float d,float e){Lines.Add(s);LastTarget=t;return new Cysharp.Threading.Tasks.UniTask();}}}
namespace Duckov.Economy{public class EconomyManager{public static EconomyManager Instance=new EconomyManager();public class SaveData{}public object GenerateSaveData(){return new SaveData();}}}
namespace ItemStatsSystem
{
    public class Inventory
    {
        public bool Loading,ThrowBeforeRemove,ThrowAfterRemove,ThrowAfterAdd;public int Capacity=20;public readonly List<Item> Content=new List<Item>();
        public bool RemoveItem(Item item){if(ThrowBeforeRemove){ThrowBeforeRemove=false;throw new Exception("before remove");}int slot=Content.IndexOf(item);if(slot<0)return false;Content[slot]=null;item.InInventory=null;if(ThrowAfterRemove){ThrowAfterRemove=false;throw new Exception("after remove");}return true;}
        public Item GetItemAt(int slot){return slot>=0&&slot<Content.Count?Content[slot]:null;}
        public bool AddAt(Item item,int slot){while(Content.Count<=slot)Content.Add(null);if(Content[slot]!=null)return false;Content[slot]=item;item.InInventory=this;if(ThrowAfterAdd){ThrowAfterAdd=false;throw new Exception("after add");}return true;}
        public bool AddItem(Item item){int slot=Content.FindIndex(x=>x==null);if(slot<0)slot=Content.Count;if(slot>=Capacity)return false;return AddAt(item,slot);}
        public void Save(string key){}
    }
    public class AgentUtilities{public object ActiveAgent;}
    public class Item:Component
    {
        public int TypeID=42,Value=10;public string DisplayName="food";public bool Stackable=true;public float MaxDurability;public Inventory Inventory,InInventory;public object PluggedIntoSlot;public AgentUtilities AgentUtilities;
        public bool IsBeingDestroyed{get{return Destroyed;}}public bool ThrowBeforeCount,ThrowAfterCount;private int count=1;public readonly Dictionary<string,string> Variables=new Dictionary<string,string>();
        public int StackCount{get{return count;}set{if(ThrowBeforeCount){ThrowBeforeCount=false;throw new Exception("before count");}count=value;if(ThrowAfterCount){ThrowAfterCount=false;throw new Exception("after count");}}}
        public void SetInt(string key,int value,bool b){count=value;}public void Drop(CharacterMainControl player,bool b){AgentUtilities=new AgentUtilities{ActiveAgent=new object()};}
        public void DestroyTree(){Object.Destroy(gameObject);}
        public void Save(string key){PetNestSaveCoordinator.Captures++;SavesSystem.Cache[key]=Inventory.Content.Where(x=>x!=null).Sum(x=>x.StackCount);}
    }
    public static class ItemAssetsCollection{public static Item InstantiateSync(int id){return Program.Food(id,1);}}
    public static class ItemUtilities{public static void SendToPlayer(Item item){CharacterMainControl.Main.CharacterItem.Inventory.AddItem(item);}}
}
namespace Saves
{
    public static class SavesSystem
    {
        private static bool saving;public static bool IsSaving{get{return saving;}set{saving=value;}}public static int CurrentSlot=1,FailAt,Attempts;public static Dictionary<string,object> Cache=new Dictionary<string,object>(),Disk=new Dictionary<string,object>();
        public static void Save<T>(string key,T value){Cache[key]=value;}public static void SaveFile(bool b){saving=true;Attempts++;if(FailAt==Attempts)throw new Exception("physical write");Disk=new Dictionary<string,object>(Cache);saving=false;}
    }
}
namespace SodaCraft.Localizations{public static class PlainText{public static string ToPlainText(this string key){string text;return LocalizationHelper.Texts.TryGetValue(key,out text)?text:key;}}}
namespace BossRush.Utils
{
    public static class NPCInteractionGroupHelper
    {
        public static List<InteractableBase> GetOrCreateGroupList(InteractableBase host,string log){return host.Group;}
        public static T AddSubInteractable<T>(Transform parent,string name,List<InteractableBase> group,Action<T> setup=null)where T:InteractableBase,new(){var go=new GameObject();go.transform.SetParent(parent);var value=go.AddComponent<T>();setup?.Invoke(value);group.Add(value);typeof(T).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(value,null);return value;}
    }
}
namespace BossRush
{
    internal class ModBehaviour{public static void DevLog(string s){}}
    internal static class L10n{public static bool IsChinese=true;public static string T(string cn,string en){return IsChinese?cn:en;}}
    public static class LocalizationHelper{public static readonly Dictionary<string,string> Texts=new Dictionary<string,string>();public static void InjectLocalization(string key,string text){Texts[key]=text;}public static void InjectLocalizations(Dictionary<string,string> values){foreach(var entry in values)Texts[entry.Key]=entry.Value;}}
    internal class PetNestPetRecord{public int exp,level=1,behaviour;public long lastFeedUtcTicks;public string personalityId;public PetNestPetRecord Clone(){return(PetNestPetRecord)MemberwiseClone();}}
    internal static class PetNestTuning{public const int FeedExp=20;public const double FeedCooldownMinutes=10;public const float FeedHealRatio=.1f;public const string PersonalityReckless="reckless",PersonalityCautious="cautious",PersonalityLazy="lazy",PersonalityLoyal="loyal";}
    internal static partial class PetNestService{public static PetNestPetRecord Pet;public static PetNestPetRecord DeployedPet{get{return Pet;}}public static bool Pending;public static PetNestPetRecord TryGetPet(string id){return id=="pet"?(PetNestPersistenceAccess.Active?PetNestPersistenceAccess.Candidate:Pet):null;}}
    internal static class PetNestPersistenceAccess{public static bool Active,RejectCommit;public static PetNestPetRecord Candidate;public static bool IsTransactionActive{get{return Active;}}public static bool CanStoreAll{get{return true;}}public static bool BeginTransaction(out string e){e=null;if(Active)return false;Active=true;Candidate=PetNestService.Pet.Clone();return true;}public static void AbortTransaction(){Active=false;}public static bool CommitTransaction(out string e){e=null;Active=false;if(RejectCommit)return false;PetNestService.Pet=Candidate;PetNestService.Pending=true;return true;}}
    internal static class PetNestProgressionService{public static void AddExp(PetNestPetRecord pet,int value){pet.exp+=value;if(pet.exp>=100){pet.exp-=100;pet.level++;}}}
    internal class PetNestCompanionAgent{public PetNestBehaviour Behaviour;public CharacterMainControl Self;public void NotifyEngaged(CharacterMainControl enemy){}}
    // 2026-10-17：摸摸的小动作走 PetNestUIMotionParts 的脉冲组件（Unity 侧表现），本夹具只记次数
    internal static class PetNestPulse{public static int Plays;public static void Play(UnityEngine.Transform t,float peak,float seconds){Plays++;}}
    internal static class PetNestCompanionRuntime{public static CharacterMainControl CompanionCharacter;public static PetNestCompanionAgent CompanionAgent;public static bool ThrowRefresh;public static void ApplyBehaviour(string id,PetNestBehaviour value){}public static void RefreshProgression(PetNestPetRecord pet){if(ThrowRefresh)throw new Exception("presentation");}}
    internal static class PetNestBackpack{public static bool CollectAll(out string e){e=null;return true;}}
    internal static class PetNestBaseIdleSpawner{public static int ActiveCount;public static void RefreshProgression(CharacterMainControl character,PetNestPetRecord pet){}}
    internal static partial class PetNestSaveCoordinator
    {
        private static readonly object _lock=new object();private static bool _assetSnapshotRequired,_backpackAssetSnapshotRequired;public static int Captures;
        private static BossRushSaveCoordinatorEngine _engine=new BossRushSaveCoordinatorEngine(new Source(),false);
        public static bool CanCollectAssets(out string e){e=null;return true;}public static void RequestFlush(){string e;_engine.RequestFlush(out e);}public static bool HostDestroy(){return _engine.TryFlushOnHostDestroy();}public static void Reset(){_assetSnapshotRequired=_backpackAssetSnapshotRequired=false;Captures=0;_engine.Reset();}
        private class Source:IBossRushSaveBatchSource
        {
            public string LogPrefix{get{return"[PetNest]";}}public bool HasPendingWrite{get{return PetNestService.Pending;}}public bool IsStoreFaulted{get{return false;}}public bool HasSnapshotObligation{get{return _assetSnapshotRequired||_backpackAssetSnapshotRequired;}}public string LastError{get{return null;}}public bool CollectSnapshot(out string e){return CollectPendingAssets(out e);}public bool FlushPending(){SavesSystem.Cache["Pet"]=PetNestService.Pet.Clone();PetNestService.Pending=false;return true;}public void OnPhysicalSaveSucceeded(){_assetSnapshotRequired=_backpackAssetSnapshotRequired=false;}
        }
    }
    internal struct SkyIslandIngredient{internal int TypeId,Count;internal SkyIslandIngredient(int type,int count){TypeId=type;Count=count;}}
    internal enum PetNestRiskTier{Safe,Rough,Desperate}
    internal class PetNestExpeditionRecord{public float deathRate;public int riskTier;public bool settled;}
    internal static class RelicEggConfig{public static void InjectLocalization(){}}
    internal static partial class PetNestLocalization
    {
        private static void AddCore(Dictionary<string,string> m){}private static void AddDropAndSouls(Dictionary<string,string> m){}private static void AddFailureReasons(Dictionary<string,string> m){}private static void AddPersonalities(Dictionary<string,string> m){}private static void AddStatsAndTalents(Dictionary<string,string> m){}private static void AddDestinationsAndRisk(Dictionary<string,string> m){}public static void InjectBuildingKeys(){}
    }
    // Compatibility double only lets the pre-fix executable expose ConsumeItem's actual failure contract.
    internal static class ItemFactory{public static bool ConsumeItem(int type,int count){try{var bag=CharacterMainControl.Main.CharacterItem.Inventory;var food=bag.Content.Last(x=>x!=null&&x.TypeID==type);if(food.StackCount>count)food.StackCount-=count;else bag.RemoveItem(food);return true;}catch{return false;}}}
}
