using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
    }
    public class Component { public GameObject gameObject; public bool enabled = true; }
    public class Transform : Component
    {
        public string name;
        public Vector3 position;
        internal readonly List<Transform> Children = new List<Transform>();
        public Transform Find(string path)
        {
            foreach (Transform child in Children) if (child.name == path) return child;
            return null;
        }
        public T GetComponent<T>() where T : class { return gameObject == null ? null : gameObject.GetComponent<T>(); }
    }
    public class Mesh { public bool isReadable = true; public int vertexCount = 3533; }
    public class MeshFilter : Component { public Mesh sharedMesh = new Mesh(); }
    public class GameObject
    {
        public SceneManagement.Scene scene;
        public bool activeSelf;
        public readonly Transform transform;
        internal readonly List<object> Parts = new List<object>();
        public GameObject() { transform = new Transform { gameObject = this }; }
        public GameObject(string name) : this() { transform.name = name; }
        public T GetComponent<T>() where T : class
        {
            foreach (object part in Parts) { T typed = part as T; if (typed != null) return typed; }
            return null;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class
        {
            var found = new List<T>();
            Collect(transform, found);
            return found.ToArray();
        }
        private static void Collect<T>(Transform node, List<T> found) where T : class
        {
            if (node.gameObject != null)
                foreach (object part in node.gameObject.Parts) { T typed = part as T; if (typed != null) found.Add(typed); }
            foreach (Transform child in node.Children) Collect(child, found);
        }
        /// <summary>夹具装配用：把组件挂到本对象，并让 Transform 层级与真实场景一致。</summary>
        public T Attach<T>(T part) where T : class { Parts.Add(part); return part; }
        public GameObject AddChild(string name, Vector3 at)
        {
            var child = new GameObject(name) { scene = scene };
            child.transform.position = at;
            transform.Children.Add(child.transform);
            return child;
        }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
        public bool isLoaded;
        public bool IsValid() { return handle > 0; }
    }
    public static class SceneManager
    {
        public static Scene Active;
        public static Scene GetActiveScene() { return Active; }
    }
}
public class Health { }
public class CharacterMainControl
{
    public static CharacterMainControl Main;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public object CharacterItem = new object();
    public Health Health = new Health();
}
public class GameCamera { public object renderCamera = new object(); }
public class LevelManager
{
    public static LevelManager Instance;
    public static bool AfterInit = true;
    public static bool LevelInited = true;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public bool IsRaidMap = true;
    public bool IsBaseLevel;
    public object CharacterCreator = new object();
    public object InputManager = new object();
    public GameCamera GameCamera = new GameCamera();
    public CharacterMainControl MainCharacter;
}
public class LevelConfig
{
    public static LevelConfig Instance;
    public static bool SaveCharacter = true;
    public static bool SpawnTomb = true;
    public bool enabled = true;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public object timeOfDayConfig = new object();
    public List<object> startBuffPrefabs = new List<object>();
}
public class SceneInfoCollection
{
    public static object Entry = new object();
    public static object GetSceneInfo(string id) { return Entry; }
}
namespace Duckov
{
    public class DeadBodyManager { public static DeadBodyManager Instance; }
}
namespace Duckov.Scenes
{
    public class SubSceneEntry
    {
        public class Location { public string path; public UnityEngine.Vector3 position; }
        public string sceneID;
        public List<Location> cachedLocations = new List<Location>();
        public List<object> cachedTeleporters = new List<object>();
    }
    public class MultiSceneCore
    {
        public static MultiSceneCore Instance;
        public static string ActiveSubSceneID;
        public static UnityEngine.SceneManagement.Scene? ActiveSubScene;
        public bool enabled = true;
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public List<SubSceneEntry> SubScenes = new List<SubSceneEntry>();
    }
    public class SceneLocationsProvider
    {
        public static object Provider = new object();
        public static object GetProviderOfScene(UnityEngine.SceneManagement.Scene scene) { return Provider; }
        public bool enabled = true;
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        // 位置表按 path 精确返回，缺失返回 null，与官方 provider 的查询语义一致。
        public readonly Dictionary<string, UnityEngine.Transform> Locations = new Dictionary<string, UnityEngine.Transform>();
        public UnityEngine.Transform GetLocation(string path)
        {
            UnityEngine.Transform found;
            return Locations.TryGetValue(path, out found) ? found : null;
        }
    }
}

namespace BossRush
{
    internal static class SkyIslandSceneReferenceBridge
    {
        internal const string SceneId = "BossRush_SkyIsland";
        internal const string SpawnLocation = "StartPoints/PlayerSpawn";
    }
}
