using System; using System.Collections; using System.Collections.Generic; using System.Reflection; using System.Globalization; using System.Text.Json; using BossRush; using UnityEngine;
namespace UnityEngine {
 public class Object { internal bool Destroyed; public static bool operator==(Object a,Object b){bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b);}public static bool operator!=(Object a,Object b){return !(a==b);}public override bool Equals(object o){return ReferenceEquals(this,o);}public override int GetHashCode(){return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);}public static void Destroy(Object o){if(ReferenceEquals(o,null))return;o.Destroyed=true;var g=o as GameObject;if(!ReferenceEquals(g,null)){foreach(var c in g.Components)c.Destroyed=true;g.transform.Destroyed=true;}} }
 public class Component:Object {public GameObject gameObject;public Transform transform{get{return gameObject.transform;}}}
 public class Transform:Object { public GameObject gameObject;public Vector3 position;public Dictionary<string,Transform> Children=new Dictionary<string,Transform>();public Transform Find(string key){Transform t;return Children.TryGetValue(key,out t)?t:null;}public Vector3 TransformPoint(Vector3 offset){return position+offset;} }
 public class GameObject:Object {public UnityEngine.SceneManagement.Scene scene=new UnityEngine.SceneManagement.Scene(1);public string name;public Transform transform;public List<Component> Components=new List<Component>();public GameObject(string n){name=n;transform=new Transform{gameObject=this};}public T GetComponent<T>() where T:class {foreach(var c in Components)if(c is T)return c as T;return null;}public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};Components.Add(c);return c;} }
 public class Sprite:Object {}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 zero{get{return new Vector3();}}public static Vector3 operator+(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static bool operator==(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}public static bool operator!=(Vector3 a,Vector3 b){return !(a==b);}public override bool Equals(object o){return o is Vector3&&this==(Vector3)o;}public override int GetHashCode(){return x.GetHashCode()^y.GetHashCode()^z.GetHashCode();} }
 public static class Random {public static int Range(int a,int b){return a;} }
 public static class Mathf {public static int Max(int a,int b){return Math.Max(a,b);} }
}
namespace NodeCanvas.DialogueTrees {public interface IDialogueActor {} }
public class DuckovDialogueActor:UnityEngine.Component,NodeCanvas.DialogueTrees.IDialogueActor {private string id,nameKey;private Vector3 offset;private Sprite _portraitSprite;public string NameKey{get{return nameKey;}}public static DuckovDialogueActor Get(string id){return null;} }
namespace BossRush {
 public class ModBehaviour {public static void DevLog(string s){}public static void LogError(string s){} }
 public static class L10n {public static bool IsChinese=true;public static string T(string cn,string en){return IsChinese?cn:en;} }
 public static class LocalizationHelper {public static Dictionary<string,string> Values=new Dictionary<string,string>();public static void InjectLocalization(string key,string value){Values[key]=value;} }
 public static class AffinityManager {public static bool CanWrite=true; public static bool MaxReached; public static bool HasAnyNPCEverReachedMaxLevel(){return MaxReached;}public const int UNIFIED_MAX_POINTS=2300,UNIFIED_MAX_LEVEL=10;public static int GetPointsRequiredForLevel(int x){return x;}public static string GetCurrentSpouseNpcId(){return "nurse_yuzhi";}public static string GetMarriageDateText(string id){return DateTime.Now.AddDays(-2).ToString("yyyy年M月d日");} }
 public static class BossRushItemIds {public const int BirthdayCake=500045;}
 public static class DiamondConfig {public const int TYPE_ID=500001;public static string GetDisplayName(){return L10n.T("钻石","Diamond");} }
 public static class ColdQuenchFluidConfig {public const int TYPE_ID=500002;public static string GetDisplayName(){return L10n.T("冷淬液","Cold Quench");} }
 public static class DiamondRingConfig {public const int TYPE_ID=500003,AFFINITY_BONUS=500,UNLOCK_LEVEL=7;public static string GetDisplayName(){return L10n.T("戒指","Ring");} }
 public static class BrickStoneConfig {public const int TYPE_ID=500004;}
 public static class AffixForgeStoneConfig {public const int TYPE_ID=500060;public static string GetDisplayName(){return L10n.T("熔石","Stone");} }
 public static class FrostThunderSetConfig {public const int FROST_HELMET_ID=500053,FROST_ARMOR_ID=500054,THUNDER_HELMET_ID=500055,THUNDER_ARMOR_ID=500056,SET_PIECE_UNLOCK_LEVEL=6,SET_PIECE_MAX_STOCK=1;}
 public static class NewWeaponIds {public const int ViperDaggerTypeId=500010,SummonStaffTypeId=500011,EnergyShieldTypeId=500012,FrostSpearTypeId=500013,ThunderRingTypeId=500014;}
 public static class NewWeaponShopConfig {public const int UnlockLevel=5,MaxStock=1;}
 public static class ObjectCache {public static UnityEngine.Object[] Buildings;public static UnityEngine.Object[] GetSceneObjectsByType(Type t){return Buildings;}}
}

namespace UnityEngine.SceneManagement { public struct Scene {public int id;public Scene(int id){this.id=id;}public bool isLoaded{get{return id>0;}}public bool IsValid(){return id>0;}public static bool operator==(Scene a,Scene b){return a.id==b.id;}public static bool operator!=(Scene a,Scene b){return a.id!=b.id;}public override bool Equals(object o){return o is Scene && this==(Scene)o;}public override int GetHashCode(){return id;}}public static class SceneManager {public static Scene GetActiveScene(){return new Scene(1);}}}
namespace Duckov.Scenes {public static class MultiSceneCore {public static UnityEngine.SceneManagement.Scene? MainScene {get{return new UnityEngine.SceneManagement.Scene(1);}}}}
namespace Duckov.Buildings {public struct BuildingInfo {public string id,prefabName;}}
namespace HarmonyLib {public class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string m){}}public class HarmonyPostfix:Attribute {}}
class Program {
 static int checks; static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static void Main(){
  var g=new GoblinAffinityConfig();var n=new NurseAffinityConfig();L10n.IsChinese=true;string cn=g.PositiveBubbles[0];string heal=(string)typeof(NurseAffinityConfig).GetMethod("GetRandomHealSuccessDialogue",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(n,null);L10n.IsChinese=false;
  Check(g.PositiveBubbles[0]!=cn,"goblin gift language refresh");Check((string)typeof(NurseAffinityConfig).GetMethod("GetRandomHealSuccessDialogue",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(n,null)!=heal,"nurse healing language refresh");L10n.IsChinese=true;Check(g.PositiveBubbles[0]==cn,"language switches back");
  for(int i=0;i<100;i++){var go=new GameObject("actor"+i);DialogueActorFactory.Create(go,"npc","key");UnityEngine.Object.Destroy(go);DialogueActorFactory.Remove(go);}
  var cache=(IDictionary)typeof(DialogueActorFactory).GetField("actorCache",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);Check(cache.Count==0,"destroyed actor keys removed by managed identity");
  for(int i=0;i<100;i++){var go=new GameObject("actor"+i);DialogueActorFactory.Create(go,"npc","key");UnityEngine.Object.Destroy(go);}var live=new GameObject("live");DialogueActorFactory.Create(live,"live","key");Check(cache.Count==1,"create prunes destroyed actors without per-frame scan");
  var template=new GameObject("template");template.scene=new UnityEngine.SceneManagement.Scene(2);var placed=new GameObject("placed");Check(!WeddingBuildingRuntimePolicy.IsPlacedSceneObject(template,template),"template excluded");Check(WeddingBuildingRuntimePolicy.IsPlacedSceneObject(placed,template),"placed current scene accepted");var stale=new GameObject("stale");stale.scene=new UnityEngine.SceneManagement.Scene(3);Check(!WeddingBuildingRuntimePolicy.IsPlacedSceneObject(stale,template),"other scene excluded");
  var patch=typeof(WeddingBuildingRequirementsPatch).GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static);foreach(bool eligible in new[]{true,false,true}){AffinityManager.MaxReached=eligible;object[] args={new Duckov.Buildings.BuildingInfo{id="wedding_chapel",prefabName="WeddingChapel"},true};patch.Invoke(null,args);Check((bool)args[1]==eligible,"A-B-A slot eligibility is current");}
  Console.WriteLine("Presentation "+checks+" PASS");
 }
}
