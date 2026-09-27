using System;
using System.Collections.Generic;
using System.Threading.Tasks;
internal static class Probe
{
 internal static readonly List<string> Events=new List<string>();
 internal static Task LastTask;
 internal static void Add(string e){Events.Add(e);}
}
namespace UnityEngine
{
 public class Object
 {
  public bool Destroyed; public string name;
  public static bool operator ==(Object a,Object b){bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b);}
  public static bool operator !=(Object a,Object b){return !(a==b);}
  public override bool Equals(object o){return this==o as Object;} public override int GetHashCode(){return base.GetHashCode();}
  public static void Destroy(Object value){if(ReferenceEquals(value,null))return;value.Destroyed=true;var go=value as GameObject;if(!ReferenceEquals(go,null))foreach(var c in go.Components)c.Destroyed=true;Probe.Add("destroy:"+value.name);}
 }
 public class Component:Object {public GameObject gameObject;public Transform transform{get{return gameObject.transform;}}}
 public class Transform:Component {public Vector3 position,forward=Vector3.forward;}
 public class GameObject:Object
 {
  public readonly List<Component> Components=new List<Component>();public Transform transform;public bool activeSelf;
  public GameObject(string label="object"){name=label;transform=new Transform{gameObject=this};Components.Add(transform);}
  public void SetActive(bool value){activeSelf=value;Probe.Add("active:"+name);}
 }
 public struct Vector3
 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero,forward=new Vector3(0,0,1),up=new Vector3(0,1,0);
  public float sqrMagnitude{get{return x*x+y*y+z*z;}}public Vector3 normalized{get{return this;}}
  public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
  public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
  public static Vector3 operator -(Vector3 a){return zero-a;}public static Vector3 operator *(Vector3 a,float s){return new Vector3(a.x*s,a.y*s,a.z*s);}
  public static Vector3 LerpUnclamped(Vector3 a,Vector3 b,float t){return a+(b-a)*t;}
 }
 public struct Color{}
 public static class Mathf{public static int Max(int a,int b){return Math.Max(a,b);}public static float Max(float a,float b){return Math.Max(a,b);}}
 public static class Time{public static float deltaTime=.25f;}
 public static class Resources{public static object Values;public static int Calls;public static T[] FindObjectsOfTypeAll<T>(){Calls++;return Values as T[];}}
}
namespace UnityEngine.SceneManagement
{
 public struct Scene{public int buildIndex;}
 public static class SceneManager{public static int Index=1;public static Scene GetActiveScene(){return new Scene{buildIndex=Index};}}
}
public enum Teams{player,middle,wolf}
public enum SoundTypes{grenadeDropSound}
public struct AISound{public UnityEngine.Vector3 pos;public float radius;public Teams fromTeam;public SoundTypes soundType;public object fromCharacter,fromObject;}
public static class AIMainBrain{public static AISound Last;public static void MakeSound(AISound sound){Last=sound;Probe.Add("ai-sound");}}
public static class Team{public static bool IsEnemy(Teams a,Teams b){return b==Teams.wolf;}}
public class Health:UnityEngine.Component{public bool IsDead;}
public class CharacterMainControl:UnityEngine.Component
{
 public static CharacterMainControl Main;public Health Health;public Teams Team=Teams.middle;public bool Companion;
 public CharacterMainControl(string label="character"){gameObject=new UnityEngine.GameObject(label);gameObject.Components.Add(this);Health=new Health{gameObject=gameObject};gameObject.Components.Add(Health);}
 public void SetTeam(Teams value){Team=value;Probe.Add("team:"+value);}
}
public class CharacterRandomPreset:UnityEngine.Object
{
 public Func<Task<CharacterMainControl>> Factory;
 public Task<CharacterMainControl> CreateCharacterAsync(UnityEngine.Vector3 pos,UnityEngine.Vector3 dir,int scene,object callback,bool active){Probe.Add("factory");return Factory();}
}
namespace Duckov.ItemUsage{public class SpawnEgg:UnityEngine.Object{public CharacterRandomPreset spawnCharacter;}}
namespace Duckov.Economy
{
 public class StockShop:UnityEngine.Component{public StockShop(){gameObject=new UnityEngine.GameObject("shop");gameObject.Components.Add(this);}}
}
namespace Duckov.Economy.UI
{
 public class StockShopView{public static StockShopView Instance;public Duckov.Economy.StockShop Target;public UnityEngine.GameObject gameObject=new UnityEngine.GameObject("view");public bool Throw;public void Close(){Probe.Add("close-shop");if(Throw)throw new Exception();}}
}
namespace Duckov.Scenes{public static class MultiSceneCore{public static void MoveToActiveWithScene(UnityEngine.GameObject go,int scene){Probe.Add("move-scene");}}}
namespace Duckov.Weathers
{
 public enum Weather{Sunny,Rain}
 public class WeatherManager{public static WeatherManager Instance;public bool ForceWeather;public Weather ForceWeatherValue;public static void SetForceWeather(bool force,Weather value){Instance.ForceWeather=force;Instance.ForceWeatherValue=value;Probe.Add("weather");}}
}
namespace Duckov{public static class AudioManager{public static void PlayStringer(string key){Probe.Add("stinger");}}}
namespace FX{public class PopText{public static PopText instance;public static void Pop(string text,UnityEngine.Vector3 pos,UnityEngine.Color color,float size){Probe.Add("pop");}}}
namespace BossRush
{
 using UnityEngine;
 public static class TaskAdapter{public static void Forget(this Task task){Probe.LastTask=task;}}
 public class EnemyPresetInfo{public string displayName="enemy";}
 public class EnemySpawnContext{public CharacterMainControl character;public EnemyPresetInfo preset;}
 public class EnemySpawnCoreResult{public bool success;public EnemySpawnContext context;public string failureReason;}
 public class EnemySpawnCoreOptions{public bool SuppressWaveBossRegistration;}
 internal sealed partial class RandomEventsRuntimeModule
 {
  private readonly ModBehaviour _owner;internal RandomEventsRuntimeModule(ModBehaviour owner){_owner=owner;}
  internal static bool ShopSuccess=true;
  private Duckov.Economy.StockShop BuildRandomEventMerchantShop(GameObject character){Probe.Add("build-shop");return ShopSuccess?new Duckov.Economy.StockShop():null;}
 }
 public partial class ModBehaviour:UnityEngine.Object
 {
  private readonly RandomEventsRuntimeModule randomEventsRuntime;
  internal RandomEventsRuntimeModule Runtime{get{return randomEventsRuntime;}}
  internal bool RandomEventInfiniteHellForRuntime;
  internal readonly List<CharacterMainControl> RandomEventCachedCharactersForRuntime=new List<CharacterMainControl>();
  internal Duckov.ItemUsage.SpawnEgg RandomEventSpawnEggBehaviorForRuntime;
  internal CharacterRandomPreset RandomEventEggSpawnPresetForRuntime;
  internal CharacterRandomPreset Merchant;
  internal TaskCompletionSource<EnemySpawnCoreResult> SpawnPending;
  internal Func<EnemySpawnContext,bool> Commit;
  internal EnemySpawnCoreOptions Options;
  internal ModBehaviour(){randomEventsRuntime=new RandomEventsRuntimeModule(this);}
  internal static void DevLog(string message){}
  internal Vector3[] GetCurrentSceneSpawnPoints(){return null;}
  internal string GetRandomEventDirectionForRuntime(Vector3 pos,Vector3 player){return "north";}
  internal static string GetModPath(){return null;}
  internal void PlaySoundEffect(string path){Probe.Add("sound");}
  internal void ShowBigBanner(string text){Probe.Add("banner");}
  internal void RefreshRandomEventCharacterCacheForRuntime(){Probe.Add("refresh-cache");}
  internal void EnsureCharacterPresetsCacheReady(){Probe.Add("prepare-presets");}
  internal void RegisterRandomEventRecoveryAnchorForRuntime(CharacterMainControl character,Vector3 position){Probe.Add("register-anchor");}
  internal void UnregisterRandomEventRecoveryForRuntime(CharacterMainControl character){Probe.Add("unregister-anchor");}
  internal CharacterRandomPreset GetRandomEventMerchantPresetForRuntime(){return Merchant;}
  internal void SetRandomEventMerchantHealthForRuntime(CharacterMainControl character){Probe.Add("merchant-health");}
  internal Task<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync(EnemyPresetInfo preset,Vector3 position,bool isBoss,Func<bool> valid,int wave,bool skipDragonDescendant,bool skipDragonKing,bool deferActivationUntilNextFrame,Func<EnemySpawnContext,bool> onCommit,EnemySpawnCoreOptions options)
  {if(!isBoss||wave!=1||skipDragonDescendant||skipDragonKing||!deferActivationUntilNextFrame)throw new Exception("spawn contract");Commit=onCommit;Options=options;SpawnPending=new TaskCompletionSource<EnemySpawnCoreResult>();Probe.Add("spawn-core");return SpawnPending.Task;}
 }
 internal static class PetNestCompanionAgent{internal static bool IsCompanionCharacter(CharacterMainControl c){return c.Companion;}}
 internal static class L10n{internal static string Direction(string direction){return direction;}}
 internal static class SpawnPositionHelper
 {
  internal const float DefaultLiftOffset=.1f;
  internal static Vector3 FindNearestSafeSpawnPoint(Vector3[] p,Vector3 pos,float distance){return p[0];}
  internal static bool TryFindAroundPlayer(Vector3 pos,int attempts,float radius,out Vector3 result,float lift,float min){result=pos;return false;}
  internal static Vector3 SnapToGround(Vector3 p){return p;}
 }
 internal static class SpawnedEnemyActivationHelper{internal static void ReleaseFromPlayerDistanceSleep(CharacterMainControl c){Probe.Add("release-sleep");}}
 internal static class RandomEventsTuning{internal const string LogPrefix="test";internal const float DuckParadeSpacing=2,AirdropLandingSoundRadius=20;}
 internal static class BossBgmKeys{internal const string DragonDescendant="descendant",PhantomWitch="witch",DragonKing="king";}
 internal sealed class BossRushAudioManager
 {
  internal static BossRushAudioManager Instance;
  internal void StopBossBGM(string key,CharacterMainControl c){Probe.Add("bgm:"+key);}
 }
 internal static class BossRushUI{internal static float EaseOut(float t){return t;}}
 internal static class RandomEventFx
 {
  internal static void SpawnAirdropMarker(Vector3 p,GameObject c,float duration){Probe.Add("marker");}
  internal static void PlayAirdropLanding(Vector3 p){Probe.Add("landing");}
 }
}
