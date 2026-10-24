using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed, Destroying;
        public string name;
        public static implicit operator bool(Object value) { return value != null; }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static T Instantiate<T>(T source) where T : Object
        {
            if (source is CharacterRandomPreset) return ((CharacterRandomPreset)(object)source).Clone() as T;
            throw new NotSupportedException("fixture Instantiate: " + typeof(T).Name);
        }
        public static void Destroy(Object value)
        {
            if (value == null || value.Destroying) return;
            value.Destroying = true;
            var go = value as GameObject;
            if (go != null)
            {
                foreach (Component component in go.Components.ToArray()) Invoke(component, "OnDisable");
                foreach (Component component in go.Components.ToArray()) Invoke(component, "OnDestroy");
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (Component component in go.Components) component.Destroyed = true;
            }
            else if (value is Component) Invoke((Component)value, "OnDestroy");
            value.Destroyed = true;
            value.Destroying = false;
        }
        public static void Destroy(Object value, float seconds) { if (seconds <= 0f) Destroy(value); }
        internal static void Invoke(Component component, string name)
        {
            MethodInfo method = component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                try { method.Invoke(component, null); }
                catch (TargetInvocationException error) { throw error.InnerException; }
            }
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        internal bool Awoken, Started;
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T[] GetComponents<T>() where T : class { return gameObject.Components.Where(c => c != null).OfType<T>().ToArray(); }
        public T GetComponentInChildren<T>(bool inactive = false) where T : class { return gameObject.GetComponentInChildren<T>(inactive); }
        public T[] GetComponentsInChildren<T>(bool inactive = false) where T : class { return gameObject.GetComponentsInChildren<T>(inactive); }
    }

    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        internal static readonly List<GameObject> Scene = new List<GameObject>();
        internal readonly List<Component> Components = new List<Component>();
        public Transform transform;
        public bool activeSelf = true;
        public int layer;
        public UnityEngine.SceneManagement.Scene scene;
        public bool activeInHierarchy { get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); } }
        public GameObject(string name = "fixture")
        {
            this.name = name;
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            transform = new Transform { gameObject = this };
            Components.Add(transform);
            Scene.Add(this);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            Components.Add(component);
            if (activeInHierarchy) Activate(component);
            return component;
        }
        private static void Activate(Component component)
        {
            if (!component.Awoken) { component.Awoken = true; Invoke(component, "Awake"); }
            Invoke(component, "OnEnable");
        }
        public void SetActive(bool active)
        {
            if (active == activeSelf) return;
            activeSelf = active;
            foreach (Component component in Components.ToArray())
            {
                if (activeInHierarchy) Activate(component); else Invoke(component, "OnDisable");
            }
        }
        public T GetComponent<T>() where T : class { return Components.FirstOrDefault(c => c != null && c is T) as T; }
        public T GetComponentInChildren<T>(bool inactive = false) where T : class { return GetComponentsInChildren<T>(inactive).FirstOrDefault(); }
        public T[] GetComponentsInChildren<T>(bool inactive = false) where T : class
        {
            var result = new List<T>();
            if (inactive || activeInHierarchy) result.AddRange(Components.Where(c => c != null).OfType<T>());
            foreach (Transform child in transform.Children)
                if (child != null) result.AddRange(child.gameObject.GetComponentsInChildren<T>(inactive));
            return result.ToArray();
        }
        internal static T[] Live<T>() where T : class
        { return Scene.Where(go => go != null).SelectMany(go => go.Components).Where(c => c != null).OfType<T>().ToArray(); }
        internal static void StartPending()
        {
            foreach (Component component in Scene.Where(go => go != null && go.activeInHierarchy).SelectMany(go => go.Components).ToArray())
                if (component != null && component.Awoken && !component.Started) { component.Started = true; Invoke(component, "Start"); }
        }
        internal static void Reset()
        {
            foreach (GameObject go in Scene.ToArray()) if (go != null) Destroy(go);
            Scene.Clear();
        }
    }

    public sealed class Transform : Component
    {
        public Vector3 position, localPosition, localScale = Vector3.one;
        public Quaternion rotation, localRotation;
        public Transform parent;
        internal readonly List<Transform> Children = new List<Transform>();
        public void SetParent(Transform value, bool worldPositionStays = true)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = value;
            if (value != null) value.Children.Add(this);
        }
        public Transform Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Transform current = this;
            foreach (string part in path.Split('/'))
            {
                current = current.Children.FirstOrDefault(child => child != null && child.gameObject.name == part);
                if (current == null) return null;
            }
            return current;
        }
        public Vector3 TransformPoint(Vector3 point)
        {
            float yaw = localRotation.y * Mathf.Deg2Rad;
            return position + new Vector3(point.x * Mathf.Cos(yaw) + point.z * Mathf.Sin(yaw), point.y,
                point.z * Mathf.Cos(yaw) - point.x * Mathf.Sin(yaw));
        }
        public Vector3 InverseTransformPoint(Vector3 world)
        {
            Vector3 point = world - position; float yaw = -localRotation.y * Mathf.Deg2Rad;
            return new Vector3(point.x * Mathf.Cos(yaw) + point.z * Mathf.Sin(yaw), point.y,
                point.z * Mathf.Cos(yaw) - point.x * Mathf.Sin(yaw));
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { return magnitude > 0 ? this * (1f / magnitude) : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static float Distance(Vector3 a, Vector3 b) { return (a - b).magnitude; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }
        public Vector2 normalized { get { float length = (float)Math.Sqrt(x * x + y * y); return length > 0f ? this * (1f / length) : new Vector2(); } }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator *(Vector2 a, float b) { return new Vector2(a.x * b, a.y * b); }
    }
    public enum HideFlags { HideAndDontSave }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1f, 1f, 1f); } }
        public static Color Lerp(Color a, Color b, float t) { t = Math.Min(1f, Math.Max(0f, t)); return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t); }
    }
    public sealed class Mesh : Object
    {
        public HideFlags hideFlags;
        public Vector3[] vertices;
        public Vector2[] uv;
        public Color[] colors;
        public int[] triangles;
        public int vertexCount { get { return vertices == null ? 0 : vertices.Length; } }
        public Bounds bounds;
        public void SetVertices(List<Vector3> values) { vertices = values.ToArray(); }
        public void SetTriangles(List<int> values, int submesh) { triangles = values.ToArray(); }
        public void RecalculateNormals() { }
        public void MarkDynamic() { }
        public void RecalculateBounds()
        {
            if (vertices == null || vertices.Length == 0) { bounds = new Bounds(); return; }
            bounds = new Bounds { size = new Vector3(vertices.Max(v => v.x) - vertices.Min(v => v.x),
                vertices.Max(v => v.y) - vertices.Min(v => v.y), vertices.Max(v => v.z) - vertices.Min(v => v.z)) };
        }
    }
    public struct Bounds { public Vector3 size; }
    public struct Quaternion
    {
        public float y;
        public static Quaternion identity { get { return new Quaternion(); } }
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion { y = y }; }
    }
    public class Collider : Behaviour { public bool isTrigger; }
    public sealed class SphereCollider : Collider { public float radius; }
    public sealed class CapsuleCollider : Collider { public float radius, height; public Vector3 center; public int direction; }
    public sealed class BoxCollider : Collider { public Vector3 size, center; }
    public sealed class Rigidbody : Component { public bool isKinematic, useGravity; }
    public enum QueryTriggerInteraction { Ignore }
    public struct RaycastHit { public Vector3 point, normal; }
    public static class Physics
    {
        internal static bool Blocked;
        internal static int IgnoredContacts;
        internal static int RaycastCalls;
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float length, int mask, QueryTriggerInteraction query)
        { RaycastCalls++; hit = new RaycastHit { point = new Vector3(origin.x, 0f, origin.z), normal = Vector3.up }; return true; }
        public static bool Linecast(Vector3 from, Vector3 to, int mask, QueryTriggerInteraction query) { return Blocked; }
        public static void IgnoreCollision(Collider a, Collider b, bool ignore) { if (ignore) IgnoredContacts++; }
    }
    public static class LayerMask
    {
        internal static bool MissingDamageLayer;
        internal static int DamageLayerCalls, FailDamageLayerAt;
        public static int NameToLayer(string name)
        {
            DamageLayerCalls++;
            return MissingDamageLayer || (FailDamageLayerAt > 0 && DamageLayerCalls == FailDamageLayerAt) ? -1 : 17;
        }
    }
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Sqrt(float value) { return (float)Math.Sqrt(value); }
        public static int Clamp(int value, int min, int max) { return Math.Min(max, Math.Max(min, value)); }
        public static float Clamp01(float value) { return Math.Min(1f, Math.Max(0f, value)); }
        public static float DeltaAngle(float from, float to) { float delta = (to - from) % 360f; return delta > 180f ? delta - 360f : delta < -180f ? delta + 360f : delta; }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static float Floor(float value) { return (float)Math.Floor(value); }
        public static int FloorToInt(float value) { return (int)Math.Floor(value); }
        public static float Abs(float value) { return Math.Abs(value); }
        public static float Sin(float value) { return (float)Math.Sin(value); }
        public static float Cos(float value) { return (float)Math.Cos(value); }
        public static float Lerp(float a, float b, float time) { return a + (b - a) * time; }
    }
    public static class Random { public static float Range(float min, float max) { return max; } }
    public static class Time { public static float deltaTime, unscaledTime, time; public static int frameCount; }
    public static class Debug
    {
        internal static readonly List<string> Warnings = new List<string>();
        public static void Log(object value) { }
        public static void LogWarning(object value) { Warnings.Add(value.ToString()); }
    }
    public static class Resources
    {
        internal static readonly List<Object> Assets = new List<Object>();
        public static T[] FindObjectsOfTypeAll<T>() where T : Object { return Assets.Where(a => a != null).OfType<T>().ToArray(); }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction<T>(T value);
    public sealed class UnityEvent<T>
    {
        private readonly List<UnityAction<T>> listeners = new List<UnityAction<T>>();
        internal int Count { get { return listeners.Count; } }
        public void AddListener(UnityAction<T> listener) { listeners.Add(listener); }
        public void RemoveListener(UnityAction<T> listener) { listeners.RemoveAll(value => value == listener); }
        public void Invoke(T value) { foreach (UnityAction<T> listener in listeners.ToArray()) listener(value); }
    }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public struct Scene
    {
        public int handle, buildIndex;
        public string name;
        public bool IsValid() { return handle != 0; }
        public bool isLoaded { get { return SceneManager.Loaded.ContainsKey(handle); } }
        public static bool operator ==(Scene a, Scene b) { return a.handle == b.handle; }
        public static bool operator !=(Scene a, Scene b) { return !(a == b); }
        public override bool Equals(object value) { return value is Scene && this == (Scene)value; }
        public override int GetHashCode() { return handle; }
    }
    public static class SceneManager
    {
        internal static Scene Current = new Scene { handle = 1, buildIndex = 2, name = "Level_BossRush" };
        internal static readonly Dictionary<int, Scene> Loaded = new Dictionary<int, Scene>();
        public static Scene GetActiveScene() { return Current; }
        public static int sceneCount { get { return Loaded.Count; } }
        public static Scene GetSceneAt(int index) { return Loaded.Values.ElementAt(index); }
        public static Scene GetSceneByName(string name) { return Loaded.Values.FirstOrDefault(value => value.name == name); }
        public static Scene GetSceneByBuildIndex(int buildIndex) { return Loaded.Values.FirstOrDefault(value => value.buildIndex == buildIndex); }
        public static void MoveGameObjectToScene(GameObject go, Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("target scene is unavailable");
            if (go.transform.parent != null) throw new InvalidOperationException("only root GameObjects can change scene");
            MoveTree(go, scene);
        }
        private static void MoveTree(GameObject go, Scene scene)
        { go.scene = scene; foreach (Transform child in go.transform.Children) MoveTree(child.gameObject, scene); }
        internal static void Unload(Scene scene)
        {
            Loaded.Remove(scene.handle);
            foreach (GameObject go in GameObject.Scene.Where(value => value != null && value.scene == scene).ToArray()) Object.Destroy(go);
        }
    }
}
