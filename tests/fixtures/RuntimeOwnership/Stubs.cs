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
}
public sealed class Item { public object InInventory, PluggedIntoSlot; }
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
    private int permanentSpouseRestoreGeneration;
    private PermanentSpouseRestoreRequest permanentSpouseRestoreRequest;
    private RunState zombieModeRunState=new RunState();
    private float zombieModeLastDropPickupScanTime, now;
    public bool Building=true, Placeholder=true;
    public static void DevLog(string value) {}
    private float GetZombieModeRuntimeNow() => now;
    private void RemoveZombieModeRunOnlyObjectRecord(GameObject o) {}
    private void PruneZombieModeUnknownRunOnlyRecords() {}
    private void Destroy(GameObject o) { o.Destroyed=true; }
    private bool IsBaseHubSceneName(string name) => name=="Base";
    private bool HasWeddingBuildingPlaced() => Building;
    private Vector3 FindWeddingBuildingNPCPosition() => new Vector3(5,0,5);
    private bool CanCurrentSpouseFollowPlayer(string id) => AffinityManager.IsMarriedToPlayer(id);
    private bool TryGetSpouseFollowSpawnPosition(out Vector3 position) { position=CharacterMainControl.Main.transform.position; return true; }
    private void SnapSpouseInstanceToPosition(GameObject npc, Vector3 position) { npc.transform.position=position; }
    private void SetWeddingNpcIdle(GameObject npc) { npc.Idle=true; }
    private void MarkWeddingNpcInstance(GameObject npc, string id) { npc.Marked=true; }
    private void DestroyWeddingPlaceholder() { Placeholder=false; }
    private void RefreshSpouseInteractionOptions(GameObject npc) { npc.Refreshes++; }
    private void PrepareSpouseInstanceForFollow(GameObject npc, string id) { npc.Following=true; Placeholder=false; }
    public void Begin(bool following) { RequestPermanentSpouseRestore("xiaoman",new Vector3(5,0,5),following); }
    public void Invalidate() { InvalidatePermanentSpouseRestore(); }
    public bool HasPending => permanentSpouseRestoreRequest!=null;
    public GameObject Cleanup(float age, float lastScan, bool owned, bool equipped, bool force, bool high=false, bool boss=false)
    {
        now=300.01f; zombieModeLastDropPickupScanTime=lastScan;
        var obj=new GameObject {Item=new Item {InInventory=owned ? new object() : null, PluggedIntoSlot=equipped ? new object() : null}};
        zombieModeRunState.EntityDropCleanupCandidates.Add(new ZombieModeDropCandidate {GameObject=obj,SpawnTime=now-age,WaveAtSpawn=1,HighValue=high,BossDrop=boss});
        CleanupZombieModeExpiredDropCandidates(force); return obj;
    }
}
