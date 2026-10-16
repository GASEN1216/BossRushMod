using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a,Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b); }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public static implicit operator bool(Object value) { return value!=null; }
        public static void DontDestroyOnLoad(Object value) { ((GameObject)value).Persistent=true; }
        public static void Destroy(GameObject value) { if(value==null)return;value.Destroyed=true;foreach(object item in value.Components){var component=item as Object;if(component!=null)component.Destroyed=true;var method=item.GetType().GetMethod("OnDestroy",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);if(method!=null)method.Invoke(item,null);} }
        public override bool Equals(object value) { return this==value as Object; } public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject {get;set;}
        public Transform transform {get{return gameObject.transform;}}
        public MonoBehaviour() { gameObject=new GameObject();gameObject.Components.Add(this); }
        public T GetComponent<T>() where T:class { return gameObject.Components.OfType<T>().FirstOrDefault(); }
        public T[] GetComponentsInChildren<T>(bool all) { return gameObject.Components.OfType<T>().ToArray(); }
    }
    public class GameObject : Object
    {
        public List<object> Components=new List<object>(); public bool activeSelf {get;private set;}=true; public bool Persistent;
        public bool activeInHierarchy {get{return activeSelf;}}public Transform transform=new Transform();
        public T AddComponent<T>() where T:MonoBehaviour,new() { var value=new T();value.gameObject=this;Components.Add(value);return value; }
        public void SetActive(bool value) { activeSelf=value; }
    }
    public class Renderer : MonoBehaviour { public bool enabled=true; }
    public class Collider : MonoBehaviour { public bool enabled=true; }
    public static class Time {public static float unscaledTime,unscaledDeltaTime;}
    public class Transform {public Vector3 position;}
    public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 up {get{return new Vector3(0,1,0);}}public static Vector3 down {get{return new Vector3(0,-1,0);}}public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 operator *(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}}
    public struct LayerMask { }
    public struct RaycastHit {public Vector3 point;}
    public static class Physics {public static bool Ground=true;public static bool Raycast(Vector3 origin,Vector3 direction,out RaycastHit hit,float distance,LayerMask layer){hit=new RaycastHit{point=new Vector3(origin.x,-2,origin.z)};return Ground;}}
}
namespace UnityEngine.AI {public struct NavMeshHit {public UnityEngine.Vector3 position;}public static class NavMesh {public const int AllAreas=-1;public static bool SamplePosition(UnityEngine.Vector3 raw,out NavMeshHit hit,float radius,int areas){hit=new NavMeshHit();return false;}}}
namespace Duckov.Utilities {public static class GameplayDataSettings {public static class Layers {public static UnityEngine.LayerMask groundLayerMask;}}}
public class Health {public bool IsDead;}
public class CharacterMainControl : UnityEngine.MonoBehaviour {public static CharacterMainControl Main=new CharacterMainControl();public Health Health=new Health();}
public class LevelManager {public static LevelManager Instance=new LevelManager();public static bool AfterInit=true;public CharacterMainControl MainCharacter=CharacterMainControl.Main;}
namespace ItemStatsSystem { public class Inventory : UnityEngine.MonoBehaviour { } }
public class InteractableLootbox : UnityEngine.MonoBehaviour
{
    public ItemStatsSystem.Inventory Inventory {get;private set;}
    public InteractableLootbox() { Inventory=gameObject.AddComponent<ItemStatsSystem.Inventory>();gameObject.AddComponent<UnityEngine.Renderer>();gameObject.AddComponent<UnityEngine.Collider>(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool IsInteractable() { return true; }
}
public class LootBoxLoader : UnityEngine.MonoBehaviour { }
public static class LootboxDetectUtil
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool IsPrivateInventory(ItemStatsSystem.Inventory value) { return false; }
    public static bool IsLootboxInventory(ItemStatsSystem.Inventory value) { return value!=null; }
}
public static class LootboxLifecycleGuard { public static bool Ready=true; public static bool IsLevelInventoryReady() {return Ready;} }
public static class LootSearchWorldGate { public static int Flags;public static void EnsureWorldFlag(ItemStatsSystem.Inventory value){Flags++;} }
public class LootSyncEntry {public int PositionKey {get{return 3;}}}
public class LootSyncRegistry
{
    public int Registers;
    public void Register(InteractableLootbox box,ItemStatsSystem.Inventory inventory,int owner){Registers++;}
    public bool TryGetByInventory(ItemStatsSystem.Inventory inventory,out LootSyncEntry entry){entry=new LootSyncEntry();return true;}
}
public static class CoopSyncDatabase {public static LootSyncRegistry Loot {get;}=new LootSyncRegistry();}
public class LootNet
{
    public int Requests,Visibility;public bool IsServer {get;set;}
    public bool Client_TryGetCachedVisibility(int key,out bool value){value=true;return false;}
    public void Client_SetLootActive(ItemStatsSystem.Inventory inv,bool value){Visibility++;}
    public void Client_RequestLootState(ItemStatsSystem.Inventory inv,bool force){Requests++;}
}
public static class ItemTool {public static bool InventoryHasQuestItem(ItemStatsSystem.Inventory inventory){return false;}}
public static class COOPManager {public static LootNet LootNet=new LootNet();}
public class NetService : UnityEngine.Object {public static NetService Instance=new NetService();public bool networkStarted=true;public bool IsServer {get;set;}}
namespace Duckov.Scenes {public static class SceneLoader {public static bool IsSceneLoading;}public class MultiSceneCore : UnityEngine.Object {public static MultiSceneCore Instance {get;}=new MultiSceneCore();public Dictionary<int,object> inLevelData=new Dictionary<int,object>();public static void MoveToActiveWithScene(UnityEngine.GameObject value,int index){value.Persistent=false;}}}
namespace UnityEngine.SceneManagement {public struct Scene {public int buildIndex,handle;}public static class SceneManager {public static int Handle=1;public static Scene GetActiveScene(){return new Scene{buildIndex=0,handle=Handle};}}}
namespace Duckov.UI {public class LootView {public static LootView Instance {get;set;}public ItemStatsSystem.Inventory TargetInventory {get;set;}public void Close(){Instance=null;}}}
namespace BossRush
{
    internal static class BossRushCoop {public static string RunId="room-A",LocalAssetId="slot-A";public static bool IsConnected=true,HasBoundScene=true;}
    internal sealed class ModBehaviour {public static void DevLog(string value) {Console.WriteLine(value);}internal int Disposed;public void NotifyVictoryRewardShadowCrateDisposed_LootAndRewards(VictoryRewardShadowCrateController value){Disposed++;}}
}
public static class NativeLoaderBoundary {[MethodImpl(MethodImplOptions.NoInlining)]public static void Register(LootBoxLoader value){throw new Exception("native IL not installed");}}
public static class NativeOpenBoundary {[MethodImpl(MethodImplOptions.NoInlining)]public static void Open(InteractableLootbox value){throw new Exception("native IL not installed");}}
