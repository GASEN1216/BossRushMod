using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public struct Vector3
{
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
    public static Vector3 zero => new Vector3();
    public static Vector3 back => new Vector3(0,0,-1);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static bool operator ==(Vector3 a, Vector3 b) => a.x==b.x && a.y==b.y && a.z==b.z;
    public static bool operator !=(Vector3 a, Vector3 b) => !(a==b);
    public override bool Equals(object other) => other is Vector3 && this==(Vector3)other;
    public override int GetHashCode() => x.GetHashCode();
    public float sqrMagnitude => x*x+y*y+z*z;
}
public sealed class Transform { public Vector3 position; }
public sealed class GameObject
{
    public Transform transform=new Transform();
    public Item Item;
    public DuckNpcMovement Movement=new DuckNpcMovement();
    public bool Destroyed, Marked, Following, Idle;
    public int Queries, Refreshes;
    public T GetComponent<T>() where T:class { Queries++; return typeof(T)==typeof(Item) ? Item as T : Movement as T; }
    public static bool operator ==(GameObject a, GameObject b)
    { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed; return an||bn ? an==bn : ReferenceEquals(a,b); }
    public static bool operator !=(GameObject a, GameObject b) { return !(a==b); }
    public override bool Equals(object value) { return ReferenceEquals(this,value); }
    public override int GetHashCode() { return base.GetHashCode(); }
}
public sealed class Item
{
    public object InInventory, PluggedIntoSlot;
    public bool Destroyed;
    public static bool operator ==(Item a, Item b)
    { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed; return an||bn ? an==bn : ReferenceEquals(a,b); }
    public static bool operator !=(Item a, Item b) { return !(a==b); }
    public override bool Equals(object value) { return ReferenceEquals(this,value); }
    public override int GetHashCode() { return base.GetHashCode(); }
}
public sealed class CharacterMainControl
{
    public static CharacterMainControl Main;
    public GameObject gameObject=new GameObject();
    public Transform transform => gameObject.transform;
    public T GetComponent<T>() where T:class => gameObject.GetComponent<T>();
}
public sealed class DuckNpcMovement { public void Hold() {} }
public sealed class ZombieModeDropCandidate { public GameObject GameObject; public bool BossDrop, HighValue; public int WaveAtSpawn; public float SpawnTime; }
public sealed class RunState { public List<ZombieModeDropCandidate> EntityDropCleanupCandidates=new List<ZombieModeDropCandidate>(); public int CurrentWave=1; }
public static class ZombieModeTuning { public const float DropPickupScanIntervalSeconds=1f, DropCleanupAgeSeconds=300f; public const int DropCleanupWaveAge=3; }
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; public string name; }
    public static class SceneManager
    {
        public static Scene Current=new Scene {handle=1,name="Base"};
        public static Scene GetActiveScene() => Current;
    }
}
public static class AffinityManager
{
    public static string Spouse="xiaoman";
    public static bool Married=true, Following;
    public static string GetCurrentSpouseNpcId() => Spouse;
    public static bool IsMarriedToPlayer(string id) => Married && id==Spouse;
    public static bool IsSpouseFollowingPlayer(string id) => Following;
}
public static class AsyncFixture
{
    public static List<Task> Tasks=new List<Task>();
    public static void Forget(this Task task) { Tasks.Add(task); }
}
public sealed class DuckNpcBlueprint {}
public static class PermanentDuckNpcRegistry
{
    public static CharacterMainControl Instance;
    public static bool TryGetBlueprint(string id, out DuckNpcBlueprint value) { value=new DuckNpcBlueprint(); return true; }
    public static CharacterMainControl GetInstance(string id) => Instance;
    public static void RegisterInstance(string id, CharacterMainControl npc) { Instance=npc; }
}
public static class DuckNpcSpawner
{
    public static List<TaskCompletionSource<CharacterMainControl>> Pending=new List<TaskCompletionSource<CharacterMainControl>>();
    public static Task<CharacterMainControl> SpawnAsync(DuckNpcBlueprint b, Vector3 position, Vector3 facing)
    {
        var pending=new TaskCompletionSource<CharacterMainControl>(); Pending.Add(pending); return pending.Task;
    }
    public static void Despawn(CharacterMainControl npc) { if (npc!=null) npc.gameObject.Destroyed=true; }
}
public partial class PermanentDuckNpcModule
{
    private const string LogPrefix="fixture";
    private static int _spawnGeneration;
    public static void Invalidate() { _spawnGeneration++; }
    private static void AttachPermanentParts(CharacterMainControl npc, DuckNpcBlueprint b, Vector3 position) {}
}
public partial class ModBehaviour
{
    public static ModBehaviour Instance;
    public bool Building=true, Placeholder=true;
    internal WeddingRuntimeModule WeddingRuntime;
    public static void DevLog(string value) {}
    public static bool IsBaseHubSceneName(string name) => name=="Base";
    public void Begin(bool following) { WeddingRuntime.Begin(following); }
    public void Invalidate() { WeddingRuntime.Invalidate(); }
    public bool HasPending => WeddingRuntime.HasPending;
    public GameObject Cleanup(float age, float lastScan, bool owned, bool equipped, bool force, bool high=false, bool boss=false)
    { return new ZombieModeRuntimeModule().Cleanup(age,lastScan,owned,equipped,force,high,boss); }
}
namespace UnityEngine
{
    public static class Object
    {
        public static void Destroy(GameObject o)
        { if(o==null)return; o.Destroyed=true; if(o.Item!=null)o.Item.Destroyed=true; }
    }
}
internal sealed partial class ZombieModeRuntimeModule
{
    private RunState runState=new RunState();
    private float zombieModeLastDropPickupScanTime, now;
    private float GetZombieModeRuntimeNow() => now;
    private void RemoveZombieModeRunOnlyObjectRecord(GameObject o) {}
    private void PruneZombieModeUnknownRunOnlyRecords() {}
    public GameObject Cleanup(float age, float lastScan, bool owned, bool equipped, bool force, bool high=false, bool boss=false)
    {
        now=300.01f; zombieModeLastDropPickupScanTime=lastScan;
        var obj=new GameObject {Item=new Item {InInventory=owned ? new object() : null, PluggedIntoSlot=equipped ? new object() : null}};
        runState.EntityDropCleanupCandidates.Add(new ZombieModeDropCandidate {GameObject=obj,SpawnTime=now-age,WaveAtSpawn=1,HighValue=high,BossDrop=boss});
        CleanupZombieModeExpiredDropCandidates(force); return obj;
    }
}
internal sealed partial class WeddingRuntimeModule
{
    internal void OnAwake(ModBehaviour owner) { _owner = owner; }
    private bool HasWeddingBuildingPlaced() => _owner.Building;
    private Vector3 FindWeddingBuildingNPCPosition() => new Vector3(5,0,5);
    private bool CanCurrentSpouseFollowPlayer(string id) => AffinityManager.IsMarriedToPlayer(id);
    internal GameObject GetSpouseInstance(string id)
    { CharacterMainControl npc=PermanentDuckNpcRegistry.GetInstance(id); return npc==null ? null : npc.gameObject; }
    private bool TryGetSpouseFollowSpawnPosition(out Vector3 position)
    { position=CharacterMainControl.Main.transform.position; return true; }
    private void SnapSpouseInstanceToPosition(GameObject npc, Vector3 position) { npc.transform.position=position; }
    private void SetWeddingNpcIdle(GameObject npc) { npc.Idle=true; }
    private void MarkWeddingNpcInstance(GameObject npc, string id) { npc.Marked=true; }
    private void DestroyWeddingPlaceholder() { _owner.Placeholder=false; }
    private void RefreshSpouseInteractionOptions(GameObject npc) { npc.Refreshes++; }
    private void PrepareSpouseInstanceForFollow(GameObject npc, string id) { npc.Following=true; _owner.Placeholder=false; }
    internal void Begin(bool following) { RequestPermanentSpouseRestore("xiaoman",new Vector3(5,0,5),following); }
    internal void Invalidate() { InvalidatePermanentSpouseRestore(); }
    internal bool HasPending => permanentSpouseRestoreRequest!=null;
}
