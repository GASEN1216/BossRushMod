using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;

        public static void Destroy(GameObject gameObject)
        {
            if (gameObject != null) gameObject.Destroyed = true;
        }

        public static GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            GameObject.InstanceCount++;
            GameObject instance = new GameObject(prefab != null ? prefab.name + "(Clone)" : "clone");
            instance.transform.position = position;
            return instance;
        }

        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull ? rightNull : !rightNull && ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform { get { return gameObject != null ? gameObject.transform : null; } }
    }

    public class Transform : Component
    {
        public Vector3 position;
        public Vector3 forward = new Vector3(0f, 0f, 1f);
    }

    public class GameObject : Object
    {
        private readonly List<Component> components = new List<Component>();
        public static int InstanceCount;
        public string name;
        public bool activeSelf;
        public readonly Transform transform;

        public GameObject(string name = "GameObject")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            components.Add(transform);
        }

        public void SetActive(bool value) { activeSelf = value; }

        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T { gameObject = this };
            components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            foreach (Component component in components)
                if (component is T) return (T)component;
            return null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            List<T> found = new List<T>();
            foreach (Component component in components)
                if (component is T) found.Add((T)component);
            return found.ToArray();
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0f, 0f, 0f); } }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 down { get { return new Vector3(0f, -1f, 0f); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float value) { return new Vector3(a.x * value, a.y * value, a.z * value); }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public static float Distance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
        public override bool Equals(object other) { return other is Vector3 && this == (Vector3)other; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
        public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
    }

    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } }
    public struct RaycastHit { public Vector3 point; }
    public static class Physics
    {
        public static bool ReturnHit = true;
        public static Vector3 GroundPoint;
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float distance)
        {
            hit = new RaycastHit { point = GroundPoint };
            return ReturnHit;
        }
    }
    public static class LayerMask { public static int NameToLayer(string name) { return 0; } }
    public class AssetBundle : Object { }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager
    {
        public static Scene ActiveScene = new Scene { name = "fixture_scene" };
        public static Scene GetActiveScene() { return ActiveScene; }
    }
}

namespace BossRush
{
    using UnityEngine;

    internal interface IBossRushRuntimeModule { }
    internal abstract class BossRushRuntimeModuleBase : IBossRushRuntimeModule
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnDestroy() { }
    }

    public partial class ModBehaviour
    {
        private GameObject courierNPCInstance;
        internal bool RandomSelection;
        internal bool ArenaPlacement;
        internal bool ValidArena = true;
        internal static Vector3[] SharedSpawnPoints = { new Vector3(3f, 0f, 4f) };

        public static ModBehaviour Instance { get; private set; }
        public ModBehaviour() { Instance = this; }
        internal void InitializeGoblinRuntime()
        {
            goblinNpcRuntime = new GoblinNpcRuntimeModule();
            goblinNpcRuntime.OnAwake(this);
        }
        internal void SetCourierNpc(GameObject courier) { courierNPCInstance = courier; }
        public bool ShouldUseRandomSupportNpcSelection(string sceneName) { return RandomSelection; }
        public bool UsesArenaSupportNpcPlacement() { return ArenaPlacement; }
        public bool IsValidBossRushArenaScene(string sceneName) { return ValidArena; }
        public static Vector3[] GetSharedCommonNPCSpawnPointsForScene(string sceneName) { return SharedSpawnPoints; }
        internal static void DevLog(string message) { }
    }

    internal static class FixtureAssets { internal static GameObject GoblinPrefab = new GameObject("GoblinPrefab"); }
    internal static class GoblinAffinityConfig { internal const string NPC_ID = "goblin"; }
    internal static class AffinityManager
    {
        internal static bool Married;
        internal static bool IsMarriedToPlayer(string id) { return Married; }
    }
    internal static class NPCAffinityInteractionHelper
    {
        internal static int SpawnCalls;
        internal static void ApplyDailyDecayOnSpawn(string id, string logPrefix) { SpawnCalls++; }
    }
    internal static class NPCSpawnConfig
    {
        internal static bool HasNormalModeConfig = true;
        internal static int SharedPositionCalls;
        internal static Vector3[] LastAvoidPositions;
        internal static float LastMinimumDistance;
        internal static bool LastRequireAvoidance;

        internal static bool HasCourierNormalModeConfig(string sceneName) { return HasNormalModeConfig; }
        internal static bool TryGetSharedSpawnPosition(Vector3[] spawnPoints, out Vector3 position,
            Vector3[] avoidPositions, float minDistance = 10f, bool requireAvoidance = false)
        {
            SharedPositionCalls++;
            LastAvoidPositions = avoidPositions;
            LastMinimumDistance = minDistance;
            LastRequireAvoidance = requireAvoidance;
            position = spawnPoints != null && spawnPoints.Length > 0 ? spawnPoints[0] : Vector3.zero;
            return position != Vector3.zero;
        }
    }
    internal static class NPCCommonUtils
    {
        internal static void FixShaders(GameObject gameObject, string prefix) { }
        internal static void SetLayerRecursively(GameObject gameObject, int layer) { }
    }
    public class GoblinNPCController : Component
    {
        internal int RunCalls, StationaryCalls;
        internal void RunToPlayer() { RunCalls++; }
        internal void EnterStationaryIdleState() { StationaryCalls++; }
    }
    public class GoblinMovement : Component
    {
        internal bool enabled = true;
        internal bool Stopped;
        internal string SceneName;
        internal void SetSceneName(string name) { SceneName = name; }
        internal void StopMove() { Stopped = true; }
    }
    public class GoblinInteractable : Component { }
}

namespace BossRush.Utils
{
    using System;
    using UnityEngine;
    using BossRush;

    internal static class NPCExceptionHandler
    {
        internal static void TryExecute(Action action, string context)
        {
            try { action(); } catch { }
        }
        internal static void LogAndIgnore(Exception error, string context) { }
    }

    internal static class NPCAssetBundleHelper
    {
        internal static int LoadCalls;
        internal static bool CanLoad = true;
        internal static bool LoadNPCPrefab(string bundle, string prefab, string prefix,
            ref AssetBundle cachedBundle, ref GameObject cachedPrefab)
        {
            LoadCalls++;
            if (!CanLoad) return false;
            if (cachedPrefab == null) cachedPrefab = FixtureAssets.GoblinPrefab;
            if (cachedBundle == null) cachedBundle = new AssetBundle();
            return cachedPrefab != null;
        }
    }
}
