using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object o) { if (!ReferenceEquals(o, null)) { o.Destroyed = true; var go = o as GameObject; if (!ReferenceEquals(go, null)) foreach (var c in go.Components.Values) Destroy(c); } }
        public static bool operator ==(Object a, Object b) { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return this == o as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public static class Debug { public static void LogWarning(string value) { } }
    public static class Mathf
    {
        public static float Abs(float v) { return Math.Abs(v); }
        public static float Round(float v) { return (float)Math.Round(v); }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Clamp(float v, float a, float b) { return Math.Max(a, Math.Min(v, b)); }
        public static bool Approximately(float a, float b) { return Math.Abs(a - b) < 0.00001f; }
    }
    public static class Random { public static float value = 0.4f; }
    public class GameObject : Object
    {
        public Scene scene;
        public Dictionary<Type, MonoBehaviour> Components = new Dictionary<Type, MonoBehaviour>();
        public T GetComponent<T>() where T : class { MonoBehaviour result; return Components.TryGetValue(typeof(T),out result) ? result as T : null; }
        public T AddComponent<T>() where T : MonoBehaviour, new() { var result = new T(); result.gameObject=this;Components[typeof(T)]=result;return result; }
        public bool HasLevel, HasCore;
        public T GetComponentInChildren<T>(bool includeInactive) where T : class, new()
        { return (typeof(T) == typeof(LevelManager) ? HasLevel : HasCore) ? new T() : null; }
    }
}
namespace UnityEngine.EventSystems { public class PointerEventData { } }
namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public struct Scene
    {
        public string name;
        public int handle, buildIndex;
        public GameObject[] Roots;
        public bool IsValid() { return handle > 0; }
        public GameObject[] GetRootGameObjects() { return Roots ?? new GameObject[0]; }
        public static bool operator ==(Scene a, Scene b) { return a.handle == b.handle; }
        public static bool operator !=(Scene a, Scene b) { return !(a == b); }
        public override bool Equals(object o) { return o is Scene && this == (Scene)o; }
        public override int GetHashCode() { return handle; }
    }
    public static class SceneManager { public static Scene Active; public static Scene GetActiveScene() { return Active; } }
}
public class LevelManager : MonoBehaviour { }
public class SceneLoader { public static bool IsSceneLoading; }
public static class SceneInfoCollection { public static List<SceneInfoEntry> Entries; public static SceneInfoEntry GetSceneInfo(string id) { return Entries == null ? null : Entries.Find(i => i.Id == id); } }
public class SceneInfoEntry { public string Id; public SceneReference SceneReference; }
public class SceneReference { public string Name; }
namespace Duckov.Scenes
{
    public struct MultiSceneLocation { public string SceneID, LocationName; }
    public class SubSceneEntry { public string sceneID; public SceneInfoEntry Info; public class Location { public string path; public Vector3 position; } }
    public class MultiSceneCore : MonoBehaviour
    {
        public static MultiSceneCore Instance;
        public List<SubSceneEntry> SubScenes = new List<SubSceneEntry>();
        public bool UsedLocalCoordinates; public string LoadedId; public Vector3 Position;
        public Func<System.Threading.Tasks.Task<bool>> Load = () => System.Threading.Tasks.Task.FromResult(true);
        public System.Threading.Tasks.Task<bool> LoadAndTeleport(MultiSceneLocation location) { throw new Exception("virtual path reached original overload"); }
        public async System.Threading.Tasks.Task<bool> LoadAndTeleport(string id, Vector3 position, bool subSceneLocation)
        { LoadedId=id;UsedLocalCoordinates=subSceneLocation;bool ok=await Load();if(ok)Position=position;return ok; }
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch(Type t,string name,Type[] args=null) { } }
    public class HarmonyPostfix : Attribute { }
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPriority : Attribute { public HarmonyPriority(int p) { } }
    public static class Priority { public const int First=800,Last=0; }
    public static class AccessTools { public static System.Reflection.MethodInfo Method(Type t,string name,Type[] args=null) { return args == null ? t.GetMethod(name,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance) : t.GetMethod(name,args); } }
    public class Patch { public System.Reflection.MethodInfo PatchMethod; }
    public class Patches { public List<Patch> Prefixes = new List<Patch>(); }
    public static class Harmony { public static Patches Installed; public static Patches GetPatchInfo(System.Reflection.MethodInfo m) { return Installed; } }
}
namespace Duckov.UI
{
    public class MapSelectionView { }
    public static class StatInfoDatabase
    {
        public static bool Throw;
        public static readonly Dictionary<string, Polarity> Values = new Dictionary<string, Polarity>();
        public static Polarity GetPolarity(string key)
        { if (Throw) throw new Exception("database absent"); Polarity value; return Values.TryGetValue(key, out value) ? value : Polarity.Neutral; }
    }
    public class MapSelectionEntry
    {
        public bool ConditionsSatisfied = true;
        public Cost Cost = new Cost();
        public BossRush.BossRushMapEntryClickHandler Marker;
        public T GetComponent<T>() where T : class { return Marker as T; }
    }
    public class Cost { public bool Enough = true; }
}
namespace ItemStatsSystem
{
    public enum Polarity { Negative = -1, Neutral, Positive }
    public class ModifierDescription { public string Key; public float Value; public bool Display = true; }
    public class Modifiers : List<ModifierDescription> { public void ReapplyModifiers() { } }
    public class CustomData { public string Key; public float Value; public float GetFloat() { return Value; } }
    public class Item
    {
        public int Quality = 3;
        public Modifiers Modifiers = new Modifiers();
        public List<Stat> Stats = new List<Stat>();
        public List<CustomData> Variables = new List<CustomData>();
        public HashSet<string> Locked = new HashSet<string>();
        public Item Prefab;
        public int GetInstanceID() { return 17; }
    }
}
namespace ItemStatsSystem.Stats { public class Stat { public string Key; public float BaseValue; } }
namespace BossRush
{
    public enum PropertyType { Modifier, Stat, Variable }
    public static class PropertyLockSystem
    { public static bool IsPropertyLocked(Item item, string key, PropertyType type) { return item.Locked.Contains(key); } }
    public static class ReforgeDataPersistence { public static void MarkAsRestored(Item item) { } }
    public static class L10n { public static string T(string cn, string en) { return en; } }
    public static partial class ReforgeSystem
    {
        private static readonly HashSet<string> FORCE_ABSOLUTE_RANGE_KEYS = new HashSet<string> { "HeadArmor", "BodyArmor" };
        public static bool CanExecuteReforge(Item item) { return item != null; }
        private static float GetItemValue(Item item) { return 100; }
        private static int GetDiscountedCost(Item item) { return 1; }
        private static float FinalProbability(int r, float v, int m) { return 0.5f; }
        private static int GenerateRandomSeed(string u, int i) { return 123; }
        private static Item GetItemPrefab(Item item) { return item.Prefab; }
        private static bool IsModifierEligibleForReforge(Item i, Item p, ModifierDescription m) { return m.Display; }
        private static bool IsNativeModifier(Item i, Item p, ModifierDescription m) { return true; }
        private static bool IsStatEligibleForReforge(Stat s) { return true; }
        private static bool IsVariableEligibleForReforge(CustomData d) { return true; }
        private static float GetPrefabPropertyValue(Item item, string key, PropertyType type, float fallback)
        {
            if (type == PropertyType.Stat) return item.Stats.Find(s => s.Key == key).BaseValue;
            if (type == PropertyType.Modifier) return item.Modifiers.Find(s => s.Key == key).Value;
            return item.Variables.Find(s => s.Key == key).Value;
        }
        private static void ApplyPropertyChange(ReforgeableProperty prop, float value, Item item, Item prefab)
        {
            if (prop.Type == PropertyType.Stat) ((Stat)prop.Source).BaseValue = value;
            else if (prop.Type == PropertyType.Modifier) ((ModifierDescription)prop.Source).Value = value;
            else ((CustomData)prop.Source).Value = value;
        }
        private static void IncrementReforgeCount(Item item) { }
    }
    public static class IntegrationUIFeedback { public const string LegendaryHex = "best", SecondaryHex = "neutral"; }
    public static partial class ReforgeUIManager
    {
        public static string Bound(string key, float original, float current, float diff)
        { return GetReforgeBoundLabelMarkup(key, original, current, diff); }
        private static readonly List<ReforgeRevealRow> pendingRevealRows = new List<ReforgeRevealRow>();
        private static float baseline;
        private static bool TryGetCachedPrefabValue(string key, PropertyType type, int ordinal, out float value) { value = baseline; return true; }
        public static bool[] Reveal(string key, float original, float value, float diff)
        { baseline = original; pendingRevealRows.Clear(); QueueReforgeReveal(key, PropertyType.Stat, 0, value, diff); var row = pendingRevealRows[0]; return new[] { row.Beneficial, row.AtMax }; }
    }
    public class BossRushMapConfig { public string sceneName, sceneID; public Vector3? customSpawnPos; }
    public enum BossRushPendingEntryKind { None, ModeG, ModeH }
    public static partial class BossRushMapSelectionHelper
    {
        private static int pendingMapEntryIndex = -1;
        private static bool pendingInitialSpawn, pendingPrepaidTicketForCurrentEntry;
        private static BossRushEntryFlowSource pendingEntryFlowSource;
        private static BossRushPendingEntryKind pendingEntryKind;
        private static string pendingModeHTargetSceneName, pendingModeHTargetSceneId;
        private static int pendingModeHSceneGeneration;
        public static bool EnoughTickets = true, Planned;
        public static bool HasEnoughTickets() { return EnoughTickets; }
        private static void SetBossRushArenaPlanned(bool planned) { Planned=planned; }
        public static void SetPendingMapEntryIndex(int index) { pendingMapEntryIndex = index; }
    }
    public partial class BossRushMapEntryClickHandler
    {
        public int entryIndex = -1;
        public Duckov.UI.MapSelectionEntry Entry;
        public T GetComponent<T>() where T : class { return Entry as T; }
    }
    internal static partial class BossRushMapEntrySelectionPatch
    { public static void Select(Duckov.UI.MapSelectionEntry entry) { RecordSelection(entry); } }
    public static class SceneRuntimeGate
    {
        public static bool IsBaseHubSceneName(string name) { return name == "Base_SceneV2"; }
        public static bool IsGameplaySceneName(string name) { return name != "LoadingScreen" && name != "MainMenu"; }
    }
    public static class StoneOutpostSceneLease { public static bool IsResourceScene(Scene scene) { return false; } }
    public struct SceneRuntimeContext { public SceneRuntimeContext(Scene s, LoadSceneMode m) { } }
    public class Host { public void OnSceneLoaded(SceneRuntimeContext context) { } }
    internal static class IntegrationRuntimeModule
    {
        internal static int TicketId;
        internal static bool ThrowTicketQuery;
        internal static int BossRushTicketTypeId
        { get { if (ThrowTicketQuery) throw new InvalidOperationException("registration unavailable"); return TicketId; } }
    }
    public partial class ModBehaviour
    {
        public static ModBehaviour Instance;
        public static Vector3 SpawnPosition;
        private BossRushEntryMode RequestedMode;
        public void SetMode(int mode) { RequestedMode=(BossRushEntryMode)mode; }
        private BossRushEntryMode DetermineBossRushEntryMode(string context) { return RequestedMode; }
        public static Vector3 GetDefaultPositionForScene(string scene) { return SpawnPosition; }
        public static BossRushMapConfig[] Maps = { new BossRushMapConfig { sceneName = "arena", sceneID = "main" } };
        public static BossRushMapConfig[] GetAllMapConfigs() { return Maps; }
        public static void DevLog(string text) { }
        private static bool bossRushArenaPlanned, bossRushArenaActive;
        private Host runtimeModuleHost = new Host();
        public int Cleanups, Callbacks;
        public void Load(Scene scene, LoadSceneMode mode, bool pending, bool active)
        { bossRushArenaPlanned = pending; bossRushArenaActive = active; OnSceneLoaded(scene, mode); }
        private void PrepareSceneRuntimeForLoad() { Cleanups++; }
        private void OnSceneUnloadAlwaysOnRuntime() { }
        private void CleanupEnemyRecoveryForSceneChange() { }
        private void CleanupModeRuntimeForSceneLoad(Scene scene) { }
        private void CleanupCashMagnetForSceneChange() { }
        private void OnSceneLoadedDebugToolsRuntime(Scene scene, LoadSceneMode mode) { }
        private void OnSceneLoadedIntegrationRuntime(Scene scene, LoadSceneMode mode) { Callbacks++; }
    }
}
