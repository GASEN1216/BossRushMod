using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public class Object
    {
        private bool destroyed;
        public string name;

        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.destroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }

        public static void Destroy(Object value)
        {
            if (value == null) return;
            var go = value as GameObject;
            if (go != null)
            {
                foreach (var child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (var component in go.Components.ToArray()) Destroy(component);
            }
            if (value is MonoBehaviour)
            {
                var method = value.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                if (method != null) method.Invoke(value, null);
            }
            value.destroyed = true;
        }

        public static GameObject Instantiate(GameObject prefab, Transform parent, bool worldPositionStays)
        {
            var clone = new GameObject(prefab.name);
            clone.transform.SetParent(parent, worldPositionStays);
            var renderer = clone.AddComponent<Renderer>();
            renderer.sharedMaterials = prefab.GetComponent<Renderer>().sharedMaterials;
            return clone;
        }
    }

    public class GameObject : Object
    {
        public static readonly List<GameObject> All = new List<GameObject>();
        internal readonly List<MonoBehaviour> Components = new List<MonoBehaviour>();
        public Scene scene;
        public readonly Transform transform;
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy { get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); } }

        public GameObject(string value)
        {
            name = value;
            scene = SceneManager.GetActiveScene();
            transform = new Transform(this);
            All.Add(this);
        }

        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            var component = new T { gameObject = this, name = name };
            Components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : MonoBehaviour { return Components.OfType<T>().FirstOrDefault(x => x != null); }
        public T GetComponentInChildren<T>() where T : MonoBehaviour { return GetComponentsInChildren<T>(true).FirstOrDefault(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : MonoBehaviour
        {
            return Components.OfType<T>().Concat(transform.Children.SelectMany(x => x.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        }
    }

    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public bool isActiveAndEnabled { get { return this != null && gameObject != null && gameObject.activeInHierarchy; } }
        public T GetComponent<T>() where T : MonoBehaviour { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>() where T : MonoBehaviour { return gameObject.GetComponentInChildren<T>(); }
        public T GetComponentInParent<T>() where T : MonoBehaviour
        {
            for (Transform at = transform; at != null; at = at.parent)
            {
                T component = at.gameObject.GetComponent<T>();
                if (component != null) return component;
            }
            return null;
        }

        private static readonly List<(MonoBehaviour Owner, IEnumerator Routine)> routines = new List<(MonoBehaviour, IEnumerator)>();

        public void StartCoroutine(IEnumerator routine)
        {
            if (routine.MoveNext()) routines.Add((this, routine));
        }

        public static void FinishCoroutines()
        {
            foreach (var running in routines.ToArray())
            {
                if (running.Owner != null && running.Owner.isActiveAndEnabled) running.Routine.MoveNext();
                routines.Remove(running);
            }
        }
    }

    public class Transform
    {
        public readonly GameObject gameObject;
        public readonly List<Transform> Children = new List<Transform>();
        public Transform parent { get; private set; }
        public Vector3 position;
        public Quaternion rotation;
        public Transform(GameObject root) { gameObject = root; }

        public void SetParent(Transform value, bool worldPositionStays)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = value;
            if (value == null) return;
            value.Children.Add(this);
            SceneManager.MoveGameObjectToScene(gameObject, value.gameObject.scene);
        }

        public Transform Find(string value) { return Children.Find(x => x.gameObject.name == value); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 left = new Vector3(-1f, 0f, 0f);
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
    }

    public struct Quaternion { public static Quaternion Euler(float x, float y, float z) { return default(Quaternion); } }
    public struct Color { public Color(float r, float g, float b) { } }
    public static class Time { public static float unscaledTime, realtimeSinceStartup; }
    public static class Debug
    {
        public static void Log(string value) { }
        public static void LogWarning(string value) { }
    }
    public static class DynamicGI { public static void UpdateEnvironment() { } }
    public class WaitForSecondsRealtime { public WaitForSecondsRealtime(float seconds) { } }
    public enum SystemLanguage { English, Chinese }
    public enum LightType { Point }
    public enum LightShadows { None }
    public class Light : MonoBehaviour
    {
        public LightType type;
        public Color color;
        public float range, intensity;
        public LightShadows shadows;
    }
    public class SphereCollider : MonoBehaviour { public float radius; public bool isTrigger; }
    public class Shader : Object { public bool isSupported = true; }
    public class Material : Object { public Shader shader; }
    public class Renderer : MonoBehaviour { public Material[] sharedMaterials; }

    public class AssetBundle : Object
    {
        public static bool FailShader;
        public static int Loaded, Unloaded;
        private GameObject prefab;
        public AssetBundle() { Loaded++; }
        public T LoadAsset<T>(string path) where T : Object
        {
            prefab = new GameObject("EntryPrefab");
            prefab.AddComponent<Renderer>().sharedMaterials = new[] { new Material
            {
                shader = new Shader { name = "BossRush/LastShift/Environment", isSupported = !FailShader },
            } };
            return prefab as T;
        }

        public void Unload(bool all) { Unloaded++; Destroy(prefab); Destroy(this); }
    }

    public static class Application
    {
        public static event Action quitting;
        public static int Subscribers { get { return quitting == null ? 0 : quitting.GetInvocationList().Length; } }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
        public string name;
        public bool IsValid() { return handle != 0; }
        public bool isLoaded { get { return SceneManager.Loaded.ContainsKey(handle); } }
    }

    public enum LoadSceneMode { Additive }

    public static class SceneManager
    {
        internal static readonly Dictionary<int, Scene> Loaded = new Dictionary<int, Scene>();
        private static Scene active;
        public static event Action<Scene, LoadSceneMode> sceneLoaded;
        public static event Action<Scene> sceneUnloaded;
        public static int UnloadSubscribers { get { return sceneUnloaded == null ? 0 : sceneUnloaded.GetInvocationList().Length; } }
        public static Scene GetActiveScene() { return active; }
        public static Scene GetSceneByName(string value) { return Loaded.Values.FirstOrDefault(s => s.name == value); }

        public static void MoveGameObjectToScene(GameObject go, Scene scene)
        {
            go.scene = scene;
            foreach (Transform child in go.transform.Children) MoveGameObjectToScene(child.gameObject, scene);
        }

        public static Scene Load(int handle, string name)
        {
            active = new Scene { handle = handle, name = name };
            Loaded.Add(handle, active);
            sceneLoaded?.Invoke(active, LoadSceneMode.Additive);
            return active;
        }

        public static void Unload(Scene scene)
        {
            Loaded.Remove(scene.handle);
            foreach (var go in GameObject.All.Where(g => g != null && g.scene.handle == scene.handle && g.transform.parent == null).ToArray())
                UnityEngine.Object.Destroy(go);
            if (active.handle == scene.handle) active = default(Scene);
            sceneUnloaded?.Invoke(scene);
        }
    }
}
