using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Duckov.Economy
{
    public struct Cost
    {
        public long money;
        public ItemEntry[] items;
        public struct ItemEntry { public int id; public long amount; }
    }
}

namespace Cysharp.Threading.Tasks
{
    [AsyncMethodBuilder(typeof(UniTaskMethodBuilder<>))]
    public struct UniTask<T>
    {
        private readonly Task<T> task;
        public UniTask(Task<T> task) { this.task = task; }
        public TaskAwaiter<T> GetAwaiter() { return task.GetAwaiter(); }
        public Task<T> AsTask() { return task; }
    }
    public struct UniTaskMethodBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> builder;
        public static UniTaskMethodBuilder<T> Create() { return new UniTaskMethodBuilder<T> { builder = AsyncTaskMethodBuilder<T>.Create() }; }
        public UniTask<T> Task { get { return new UniTask<T>(builder.Task); } }
        public void SetResult(T value) { builder.SetResult(value); }
        public void SetException(Exception error) { builder.SetException(error); }
        public void SetStateMachine(IAsyncStateMachine state) { builder.SetStateMachine(state); }
        public void Start<TState>(ref TState state) where TState : IAsyncStateMachine { builder.Start(ref state); }
        public void AwaitOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine { builder.AwaitOnCompleted(ref awaiter, ref state); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state)
            where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine { builder.AwaitUnsafeOnCompleted(ref awaiter, ref state); }
    }
}

internal static class Probe
{
    internal static readonly List<string> Events = new List<string>();
    internal static void Add(string name) { Events.Add(name); }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public string name;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Component component in go.Components) component.Destroyed = true;
            Probe.Add(value is CharacterRandomPreset ? "destroy-preset" : "destroy-object");
        }
        public static GameObject Instantiate(GameObject prefab, Vector3 pos, Quaternion rotation)
        {
            GameObject.Instances++;
            var go = new GameObject { name = prefab.name };
            go.transform.position = pos;
            Probe.Add("instantiate-nurse");
            return go;
        }
        public static CharacterRandomPreset Instantiate(CharacterRandomPreset preset) { return preset.Copy(); }
    }
    public class Component : Object { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } }
    public class Transform : Component { public Vector3 position; }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        public static int Instances;
        public static string ThrowOnComponent;
        public bool activeSelf;
        public GameObject() { transform = new Transform { gameObject = this }; Components.Add(transform); }
        public void SetActive(bool value) { activeSelf = value; Probe.Add(value ? "active" : "inactive"); }
        public T GetComponent<T>() where T : Component { return Components.Find(c => c is T) as T; }
        public T AddComponent<T>() where T : Component, new()
        {
            if (typeof(T).Name == ThrowOnComponent) throw new Exception("component");
            T value = new T { gameObject = this }; Components.Add(value); Probe.Add("component:" + typeof(T).Name); return value;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        { var found = new List<T>(); foreach (var c in Components) if (c is T) found.Add((T)c); return found.ToArray(); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float scale) { return new Vector3(a.x * scale, a.y * scale, a.z * scale); }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object value) { return value is Vector3 && this == (Vector3)value; }
        public override int GetHashCode() { return x.GetHashCode(); }
    }
    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } }
    public struct RaycastHit { public Vector3 point; }
    public static class Physics
    {
        public static bool Raycast(Vector3 from, Vector3 dir, out RaycastHit hit, float distance)
        { Probe.Add("raycast"); hit = new RaycastHit { point = new Vector3(8, 0, 9) }; return true; }
    }
    public static class LayerMask { public static int NameToLayer(string name) { return 0; } }
    public class AssetBundle : Object { }
    public static class Random { public static int Calls; public static int Range(int min, int max) { Calls++; return min; } }
    public static class Mathf { public static int Clamp(int value, int min, int max) { return Math.Min(max, Math.Max(min, value)); } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; public int buildIndex; }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { name = "map", buildIndex = 4 }; } }
}
public enum Teams { middle, wolf }
public class Health : UnityEngine.Component
{
    public bool IsDead, CanDieIfNotRaidMap = true, Invincible, showHealthBar;
    public void SetInvincible(bool value) { Invincible = value; Probe.Add(value ? "invincible" : "vulnerable"); }
    public void RequestHealthBar() { Probe.Add("healthbar"); }
}
public class Buff { public CharacterMainControl fromWho; }
public class BuffManager { public readonly List<Buff> Buffs = new List<Buff>(); }
public class CharacterMainControl : UnityEngine.Component
{
    public static CharacterMainControl Main;
    public Health Health;
    public CharacterMainControl()
    {
        gameObject = new UnityEngine.GameObject();
        gameObject.Components.Add(this);
        Health = new Health { gameObject = gameObject };
        gameObject.Components.Add(Health);
    }
    public CharacterRandomPreset characterPreset;
    public ItemStatsSystem.Item CharacterItem = new ItemStatsSystem.Item();
    public BuffManager BuffManager = new BuffManager();
    public bool ThrowBuffRead;
    public Teams Team;
    public BuffManager GetBuffManager() { if (ThrowBuffRead) throw new Exception("buff"); return BuffManager; }
    public void SetTeam(Teams team) { Team = team; Probe.Add("team"); }
}
public class CharacterRandomPreset : UnityEngine.Object
{
    public string nameKey;
    public Teams team;
    public bool dropBoxOnDead = true, setActiveByPlayerDistance = true, canDieIfNotRaidMap, showName, showHealthBar;
    public int exp = 77;
    public static Func<CharacterRandomPreset, Task<CharacterMainControl>> Factory;
    public CharacterRandomPreset Copy() { return (CharacterRandomPreset)MemberwiseClone(); }
    public Cysharp.Threading.Tasks.UniTask<CharacterMainControl> CreateCharacterAsync(UnityEngine.Vector3 pos, UnityEngine.Vector3 dir, int scene, object callback, bool active)
    { Probe.Add("factory"); return new Cysharp.Threading.Tasks.UniTask<CharacterMainControl>(Factory(this)); }
}
namespace ItemStatsSystem
{
    public class Item : UnityEngine.Component
    {
        public int TypeID, Exp;
        public void SetInt(string key, int value, bool notify) { Exp = value; Probe.Add("exp"); }
    }
    public static class ItemAssetsCollection
    {
        public static readonly HashSet<int> Registered = new HashSet<int>();
        public static bool ThrowAdd;
        public static void AddDynamicEntry(Item item) { Probe.Add("register:" + item.TypeID); if (ThrowAdd) throw new Exception("register"); Registered.Add(item.TypeID); }
    }
}

namespace BossRush
{
    using UnityEngine;
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnDestroy() { }
    }
    public partial class ModBehaviour
    {
        private GameObject courierNPCInstance, goblinNPCInstance;
        internal bool Arena, ValidArena = true;
        internal static Vector3[] Shared = { new Vector3(3, 0, 4) };
        internal static Vector3[] Fallback = { new Vector3(7, 0, 8) };
        private readonly IntegrationRuntimeModule bossRushIntegrationRuntime = new IntegrationRuntimeModule();
        private const int MutatorCountMin = 1, MutatorCountMax = 10;
        internal sealed class Config { public bool enableMutators = true; public int mutatorCount = 3; }
        internal Config config = new Config();
        internal ModBehaviour()
        { nurseNpcRuntime = new NurseNpcRuntimeModule(); nurseNpcRuntime.OnAwake(this); }
        internal void SetNeighbors(GameObject courier, GameObject goblin) { courierNPCInstance = courier; goblinNPCInstance = goblin; }
        internal GameObject Nurse { get { return nurseNPCInstance; } }
        internal void ShutdownNurse() { nurseNpcRuntime.OnDestroy(); }
        internal bool ShouldUseRandomSupportNpcSelection(string scene) { return Arena; }
        internal bool UsesArenaSupportNpcPlacement() { return Arena; }
        internal bool IsValidBossRushArenaScene(string scene) { return ValidArena; }
        internal static Vector3[] GetSharedCommonNPCSpawnPointsForScene(string scene) { return Shared; }
        internal Vector3[] GetCurrentSceneSpawnPoints() { return Fallback; }
        public static void DevLog(string message) { }
        internal void SetupAIAggro(CharacterMainControl character) { Probe.Add("aggro"); }
        internal void UnregisterDragonDescendantEnemyRecovery(CharacterMainControl character) { Probe.Add("recovery"); }
        internal void ClearDragonDescendantBossRandomLootTracking(CharacterMainControl character) { Probe.Add("random-loot"); }
        internal void FinalizeDragonDescendantBossRushLootboxPathTracking(CharacterMainControl character) { Probe.Add("lootbox"); }
    }
    public class NurseNPCController : Component { }
    public class NurseMovement : Component
    {
        public bool enabled = true, Stopped;
        public void SetSceneName(string scene) { Probe.Add("scene"); }
        public void StopMove() { Stopped = true; Probe.Add("stop"); }
    }
    public class NurseInteractable : Component { }
    internal static class NurseAffinityConfig { internal const string NPC_ID = "nurse"; }
    internal static class AffinityManager { internal static bool Married; internal static bool IsMarriedToPlayer(string id) { Probe.Add("married"); return Married; } }
    internal static class NPCAffinityInteractionHelper { internal static void ApplyDailyDecayOnSpawn(string id, string prefix) { Probe.Add("decay"); } }
    internal static class NPCSpawnConfig
    {
        internal static bool Configured = true, Avoided = true;
        internal static Vector3[] AvoidPositions;
        internal static bool HasCourierNormalModeConfig(string scene) { Probe.Add("scene-gate"); return Configured; }
        internal static bool TryGetSharedSpawnPosition(Vector3[] points, out Vector3 pos, Vector3[] avoids, float distance, bool requireAvoidance)
        { AvoidPositions = avoids; if (distance != 10 || !requireAvoidance) throw new Exception("avoidance"); pos = points != null && points.Length > 0 ? points[0] : Vector3.zero; return Avoided && pos != Vector3.zero; }
    }
    internal static class NPCCommonUtils
    {
        internal static void FixShaders(GameObject value, string prefix) { Probe.Add("shaders"); }
        internal static void SetLayerRecursively(GameObject value, int layer) { Probe.Add("layer"); }
    }
    internal static class BossRushItemIds { internal const int BossRushTicket = 500001, BirthdayCake = 500002, AdventureJournal = 500003; }
    internal static class BossRushDynamicItemRegistry { internal static bool HasRegisteredPrefabWithoutEnsuring(int id) { return ItemStatsSystem.ItemAssetsCollection.Registered.Contains(id); } }
    internal sealed partial class IntegrationRuntimeModule
    {
        internal void RegisterItemContentConfigurators() { Probe.Add("configurators"); }
        internal void InitializeBirthdayCakeItem() { Probe.Add("cake"); }
        internal void InitializeWikiBookItem() { Probe.Add("journal"); }
        internal void AddTagsToItem(ItemStatsSystem.Item item, string[] tags) { Probe.Add("tags:" + item.TypeID + ":" + string.Join(",", tags)); }
    }
    internal static class ItemFactory { internal static int LoadedItemCount { get { Probe.Add("loaded-count"); return 0; } } }
    internal static class AwenLootSweepTokenConfig { internal static void EnsureRuntimeRegistration() { Probe.Add("awen"); } }
    internal static class ZombieTideInvitationConfig { internal static void EnsureRuntimeFallbackRegistrationShell() { Probe.Add("invitation"); } }
    internal static class ZombieTideBeaconConfig { internal static void EnsureRuntimeFallbackRegistrationShell() { Probe.Add("beacon"); } }
    internal static class PortableSafeZoneDeviceConfig { internal static void EnsureRuntimeFallbackRegistrationShell() { Probe.Add("safe-zone"); } }
    internal static class PeaceCharmRuntime { internal static void InitializeRuntime() { Probe.Add("peace"); } }
    internal static class MutatorManager
    {
        internal static int Count; internal static string Mode; internal static bool Throw;
        internal static void RollAndApply(CharacterMainControl player, int count, object seed, string mode) { Probe.Add("roll"); Count = count; Mode = mode; if (seed != null || Throw) throw new Exception("roll"); }
        internal static void RemoveAll() { Probe.Add("remove"); if (Throw) throw new Exception("remove"); }
    }
    internal static class MutatorUI { internal static void ShowBanner() { Probe.Add("banner"); } internal static void HideAll() { Probe.Add("hide"); } }
    internal sealed class ManagedBossSpawnContext { internal Func<bool> IsOwnerValid; internal CharacterRandomPreset FactoryPresetOverride; }
    internal sealed class ModeGRunState
    {
        internal bool AcceptPreset = true, AcceptBoss = true;
        internal readonly HashSet<CharacterRandomPreset> Presets = new HashSet<CharacterRandomPreset>();
        internal readonly HashSet<Health> Bosses = new HashSet<Health>();
        internal bool RegisterStagingPreset(CharacterRandomPreset preset) { Probe.Add("stage-preset"); return AcceptPreset && Presets.Add(preset); }
        internal bool RegisterStagingBoss(Health health, CharacterMainControl character) { Probe.Add("stage-boss"); return AcceptBoss && Bosses.Add(health); }
        internal void UnregisterStagingPreset(CharacterRandomPreset preset) { Probe.Add("unstage-preset"); Presets.Remove(preset); }
        internal void UnregisterStagingBoss(Health health) { Probe.Add("unstage-boss"); Bosses.Remove(health); }
        internal void UnregisterTrackedBoss(Health health) { Probe.Add("untrack-boss"); }
    }
    internal static class ModeGRunContext { internal static ModeGRunState Current; }
    internal static class BossCleanupHelpers
    {
        internal static void DestroyRuntimePreset(CharacterMainControl character, string key, string name, string tag)
        { Probe.Add("cleanup-preset"); UnityEngine.Object.Destroy(character.characterPreset); }
    }
}
namespace BossRush.Utils
{
    using UnityEngine;
    internal static class NPCExceptionHandler
    { internal static void TryExecute(Action action, string context) { try { action(); } catch { } } internal static void LogAndIgnore(Exception error, string context) { } }
    internal static class NPCAssetBundleHelper
    {
        internal static bool CanLoad = true;
        internal static bool LoadNPCPrefab(string bundle, string name, string prefix, ref AssetBundle cachedBundle, ref GameObject cachedPrefab)
        { Probe.Add("load-nurse"); if (!CanLoad) return false; if (cachedPrefab == null) cachedPrefab = new GameObject { name = name }; if (cachedBundle == null) cachedBundle = new AssetBundle(); return true; }
    }
    internal static class ResourceBundleLoader
    {
        internal static Object[] Assets; internal static bool CanLoad = true;
        internal static AssetBundle LoadFromFile(string path) { Probe.Add("load-ticket"); return CanLoad ? new AssetBundle() : null; }
        internal static T[] LoadAllAssets<T>(AssetBundle bundle) where T : Object { return Assets as T[]; }
    }
    internal static class AssetBundleUnloadHelper { internal static void TryUnload(AssetBundle bundle, string tag) { Probe.Add(bundle != null ? "unload-ticket" : "unload-null"); } }
}
