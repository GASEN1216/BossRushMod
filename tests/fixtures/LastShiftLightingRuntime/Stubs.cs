using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    internal class Object
    {
        internal string name;
        private bool destroyed;
        internal static void DontDestroyOnLoad(GameObject target) { SceneManager.MoveGameObjectToScene(target, new Scene(0)); }

        internal static T Instantiate<T>(T source, Transform parent, bool keepWorld) where T : Component
        {
            var lamp = (SodaPointLight)(Component)source;
            var host = new GameObject(lamp.gameObject.name + "(Clone)");
            host.SetActive(lamp.gameObject.activeSelf);
            host.transform.SetParent(parent, keepWorld);
            host.AddComponent<MeshFilter>().sharedMesh = lamp.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = lamp.GetComponent<MeshRenderer>().sharedMaterial;
            var clone = host.AddComponent<SodaPointLight>();
            clone.BindRenderer(renderer);
            clone.enviromentTint = lamp.enviromentTint;
            clone.LightColor = lamp.LightColor;
            clone.Hardness = lamp.Hardness;
            clone.FallOff = lamp.FallOff;
            clone.Updates = 0;
            return (T)(Component)clone;
        }

        internal static void Destroy(Object target)
        {
            if (target == null) return;
            if (target is GameObject host)
            {
                foreach (GameObject child in GameObject.All.Where(g => g.transform.parent == host.transform).ToArray()) Destroy(child);
                foreach (Component component in host.Components) component.destroyed = true;
            }
            target.destroyed = true;
        }

        public static bool operator ==(Object a, Object b)
        {
            bool nullA = ReferenceEquals(a, null) || a.destroyed;
            bool nullB = ReferenceEquals(b, null) || b.destroyed;
            return nullA || nullB ? nullA && nullB : ReferenceEquals(a, b);
        }

        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }

    internal sealed class GameObject : Object
    {
        internal static readonly List<GameObject> All = new List<GameObject>();
        internal readonly List<Component> Components = new List<Component>();
        internal readonly Transform transform;
        internal bool activeSelf = true;
        internal bool activeInHierarchy { get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); } }
        internal Scene scene = SceneManager.Active;
        internal int layer;

        internal GameObject(string name = "New GameObject")
        {
            this.name = name;
            All.Add(this);
            transform = AddComponent<Transform>();
        }

        internal T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }

        internal T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(x => x != null); }
        internal T[] GetComponents<T>() where T : Component { return Components.OfType<T>().ToArray(); }
        internal void SetActive(bool value) { activeSelf = value; }
    }

    internal class Component : Object
    {
        internal GameObject gameObject;
        internal new string name { get { return gameObject.name; } }
        internal Transform transform { get { return gameObject.transform; } }
        internal T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        internal T[] GetComponents<T>() where T : Component { return gameObject.GetComponents<T>(); }
    }

    internal sealed class Transform : Component, IEnumerable<Transform>
    {
        internal Transform parent;
        internal Vector3 position, localScale = new Vector3(1f, 1f, 1f);
        internal Vector3 lossyScale
        {
            get
            {
                if (parent == null) return localScale;
                Vector3 scale = parent.lossyScale;
                return new Vector3(localScale.x * scale.x, localScale.y * scale.y, localScale.z * scale.z);
            }
        }
        internal Quaternion localRotation;
        internal int childCount { get { return GameObject.All.Count(g => g != null && g.transform.parent == this); } }
        internal Transform Find(string path)
        {
            Transform current = this;
            foreach (string name in path.Split('/'))
            {
                current = current.FirstOrDefault(child => child.name == name);
                if (current == null) break;
            }
            return current;
        }
        internal T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            return GetComponents<T>().Concat(this.SelectMany(child => child.GetComponentsInChildren<T>(includeInactive))).ToArray();
        }
        public IEnumerator<Transform> GetEnumerator()
        {
            return GameObject.All.Where(g => g != null && g.transform.parent == this).Select(g => g.transform).GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
        internal Vector3 forward { get { return new Vector3(0f, 0f, 1f); } }
        internal void SetParent(Transform value, bool keepWorld)
        {
            parent = value;
            if (parent != null) gameObject.scene = parent.gameObject.scene;
        }
    }

    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal float sqrMagnitude { get { return x * x + y * y + z * z; } }
        internal Vector3 normalized { get { return this * (1f / (float)Math.Sqrt(sqrMagnitude)); } }
        internal static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator /(Vector3 a, float b) { return a * (1f / b); }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.Equals(b); }
        public static bool operator !=(Vector3 a, Vector3 b) { return !a.Equals(b); }
        public override bool Equals(object value) { return value is Vector3 other && x == other.x && y == other.y && z == other.z; }
        public override int GetHashCode() { return (x, y, z).GetHashCode(); }
    }

    internal struct Vector2Int
    {
        private readonly int x, y;
        internal Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public override bool Equals(object value) { return value is Vector2Int other && x == other.x && y == other.y; }
        public override int GetHashCode() { return (x, y).GetHashCode(); }
    }

    internal struct Quaternion { internal static Quaternion identity { get { return default(Quaternion); } } }

    internal struct Vector4
    {
        internal float x, y, z, w;
        internal Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        internal static Vector4 zero { get { return default(Vector4); } }
    }

    internal struct Bounds
    {
        internal Vector3 center, extents;
        internal Vector3 min { get { return center - extents; } }
        internal Vector3 max { get { return center + extents; } }
        internal Bounds(Vector3 center, Vector3 size) { this.center = center; extents = size * .5f; }
        internal bool Contains(Vector3 value)
        {
            return Math.Abs(value.x - center.x) <= extents.x && Math.Abs(value.y - center.y) <= extents.y && Math.Abs(value.z - center.z) <= extents.z;
        }
    }

    internal struct Color
    {
        internal float r, g, b, a;
        internal Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color operator *(Color a, float b) { return new Color(a.r * b, a.g * b, a.b * b, a.a * b); }
    }

    internal sealed class Shader : Object
    {
        internal bool isSupported = true;
        private static readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        internal static readonly Dictionary<int, float> Floats = new Dictionary<int, float>();
        internal static readonly Dictionary<int, Vector4> Vectors = new Dictionary<int, Vector4>();
        internal static readonly Dictionary<int, Vector4[]> Arrays = new Dictionary<int, Vector4[]>();
        internal static int PropertyToID(string name)
        {
            if (!ids.TryGetValue(name, out int id)) { id = ids.Count; ids.Add(name, id); }
            return id;
        }
        internal static void SetGlobalFloat(int id, float value) { Floats[id] = value; }
        internal static float GetGlobalFloat(int id) { return Floats.TryGetValue(id, out float value) ? value : 0f; }
        internal static void SetGlobalVector(int id, Vector4 value) { Vectors[id] = value; }
        internal static Vector4 GetGlobalVector(int id) { return Vectors.TryGetValue(id, out Vector4 value) ? value : Vector4.zero; }
        internal static void SetGlobalVectorArray(int id, Vector4[] value) { Arrays[id] = (Vector4[])value.Clone(); }
    }

    internal sealed class Material : Object { internal Shader shader; }
    internal sealed class Mesh : Object { }
    internal sealed class MeshFilter : Component { internal Mesh sharedMesh; }
    internal sealed class BoxCollider : Component { internal Bounds bounds; }
    internal class Renderer : Component
    {
        internal Material sharedMaterial;
        internal ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
        internal bool receiveShadows = true;
        internal LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes;
        internal ReflectionProbeUsage reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        internal Dictionary<int, object> Properties;
        internal void SetPropertyBlock(MaterialPropertyBlock block) { Properties = new Dictionary<int, object>(block.Values); }
        internal void GetPropertyBlock(MaterialPropertyBlock block)
        {
            block.Values.Clear();
            if (Properties != null) foreach (var entry in Properties) block.Values.Add(entry.Key, entry.Value);
        }
    }

    internal sealed class MeshRenderer : Renderer { }

    internal sealed class MaterialPropertyBlock
    {
        internal readonly Dictionary<int, object> Values = new Dictionary<int, object>();
        internal void SetColor(int id, Color value) { Values[id] = value; }
        internal void SetFloat(int id, float value) { Values[id] = value; }
    }

    internal sealed class Camera : Component { internal int cullingMask; }
    internal enum LightType { Spot, Point }
    internal enum LightShadows { Hard, None }
    internal enum LightRenderMode { Auto, ForcePixel }
    internal sealed class Light : Component
    {
        internal LightType type;
        internal LightShadows shadows;
        internal LightRenderMode renderMode;
        internal Color color;
        internal float intensity, range;
    }

    internal struct LayerMask
    {
        internal int value;
        internal static int NameToLayer(string name) { return name == "SodaLight" ? 15 : -1; }
    }

    internal static class Resources
    {
        internal static int Scans;
        internal static T[] FindObjectsOfTypeAll<T>() where T : Component
        {
            Scans++;
            return GameObject.All.Where(g => g != null).SelectMany(g => g.Components.OfType<T>()).Where(c => c != null).ToArray();
        }
    }

    internal static class Debug
    {
        internal static readonly List<string> Warnings = new List<string>();
        internal static readonly List<string> Messages = new List<string>();
        internal static void Log(string value) { Messages.Add(value); }
        internal static void LogWarning(string value) { Warnings.Add(value); }
    }

    internal static class Mathf
    {
        internal static float Max(float a, float b) { return Math.Max(a, b); }
        internal static float Min(float a, float b) { return Math.Min(a, b); }
        internal static float Clamp(float value, float min, float max) { return Math.Min(Math.Max(value, min), max); }
        internal static float Lerp(float from, float to, float progress) { return from + (to - from) * Clamp(progress, 0f, 1f); }
        internal static int FloorToInt(float value) { return (int)Math.Floor(value); }
    }

    internal static class Time { internal static float unscaledTime, time; }
}

namespace UnityEngine.SceneManagement
{
    internal struct Scene
    {
        internal int handle;
        internal Scene(int value) { handle = value; }
        internal GameObject[] GetRootGameObjects()
        {
            int sceneHandle = handle;
            return GameObject.All.Where(g => g != null && g.transform.parent == null && g.scene.handle == sceneHandle).ToArray();
        }
    }

    internal static class SceneManager
    {
        internal static Scene Active = new Scene(1);
        internal static void MoveGameObjectToScene(GameObject host, Scene scene)
        {
            host.scene = scene;
            foreach (GameObject child in GameObject.All.Where(g => g != null && g.transform.parent == host.transform).ToArray())
                MoveGameObjectToScene(child, scene);
        }
        internal static void LoadSingle(int next)
        {
            foreach (GameObject gameObject in GameObject.All.Where(g => g != null && g.scene.handle != 0).ToArray())
                UnityEngine.Object.Destroy(gameObject);
            Active = new Scene(next);
        }
    }
}

namespace UnityEngine.Rendering
{
    internal enum ShadowCastingMode { On, Off, ShadowsOnly }
    internal enum LightProbeUsage { BlendProbes, Off }
    internal enum ReflectionProbeUsage { BlendProbes, Off }
}

namespace UnityEngine.Rendering.Universal
{
    internal enum RenderPassEvent { AfterRenderingOpaques, BeforeRenderingDeferredLights }

    internal class ScriptableRenderer
    {
        protected List<ScriptableRendererFeature> rendererFeatures { get; } = new List<ScriptableRendererFeature>();
        internal void AddFeature(ScriptableRendererFeature feature) { rendererFeatures.Add(feature); }
    }

    internal class ScriptableRendererFeature : Object { internal bool isActive; }
    internal sealed class UniversalAdditionalCameraData : Component { internal ScriptableRenderer scriptableRenderer; }
}

namespace UnityEngine.Experimental.Rendering.Universal
{
    using UnityEngine.Rendering.Universal;

    internal enum RenderQueueType { Opaque, Transparent }
    internal sealed class RenderObjects : ScriptableRendererFeature
    {
        internal readonly RenderObjectsSettings settings = new RenderObjectsSettings();

        internal sealed class RenderObjectsSettings
        {
            internal enum OverrideMaterialMode { None, Material }
            internal OverrideMaterialMode overrideMode;
            internal int overrideMaterialPassIndex;
            internal Material overrideMaterial;
            internal RenderPassEvent Event;
            internal readonly FilterSettings filterSettings = new FilterSettings();
        }

        internal sealed class FilterSettings
        {
            internal RenderQueueType RenderQueueType;
            internal LayerMask LayerMask;
        }
    }
}

internal sealed class SodaPointLight : Component
{
    private Renderer lightRenderer;
    internal bool enviromentTint = true;
    internal int Updates;
    private float hardness = 0.5f, fallOff = 1.5f;
    private Color lightColor = new Color(0.1f, 0.2f, 0.3f);
    internal float Hardness { get { return hardness; } set { hardness = value; SyncToLight(); } }
    internal float FallOff { get { return fallOff; } set { fallOff = value; SyncToLight(); } }
    internal Color LightColor { get { return lightColor; } set { lightColor = value; SyncToLight(); } }
    internal void BindRenderer(Renderer renderer) { lightRenderer = renderer; }

    private void SyncToLight()
    {
        if (lightRenderer == null) return;
        Updates++;
        var block = new MaterialPropertyBlock();
        block.SetColor(Shader.PropertyToID("_LightColor"), lightColor);
        block.SetFloat(Shader.PropertyToID("_Hardness"), hardness);
        block.SetFloat(Shader.PropertyToID("_FallOff"), fallOff);
        block.SetFloat(Shader.PropertyToID("_EnviromentTintOn"), enviromentTint ? 1f : 0f);
        lightRenderer.SetPropertyBlock(block);
    }
}
internal sealed class GameCamera
{
    internal static GameCamera Instance;
    internal Camera renderCamera;
}
internal sealed class CharacterMainControl : Component
{
    internal static CharacterMainControl Main;
    internal bool FlashLight;
    internal Vector3 CurrentAimDirection { get { return transform.forward; } }
}

internal sealed class OcclusionFadeTrigger : Component { }
internal enum OcclusionFadeTypes { ShadowOnly }
internal sealed class OcclusionFadeObject : Component
{
    internal OcclusionFadeTrigger[] triggers;
    internal Renderer[] renderers;
    internal OcclusionFadeTypes fadeType;
    internal void OnEnter() { foreach (Renderer renderer in renderers) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly; }
    internal void OnLeave() { foreach (Renderer renderer in renderers) renderer.shadowCastingMode = ShadowCastingMode.On; }
}

internal sealed class GameClock
{
    internal static GameClock Instance;
    internal static long Day { get { return 1L; } }
    internal static TimeSpan TimeOfDay { get { return TimeSpan.FromHours(BossRush.SkyIslandLighting.Hours); } }
}

namespace BossRush
{
    internal static class LastShiftRaidScene { internal const string LogTag = "[LastShift]"; }
    internal static class TidegateRaidScene { internal const string LogTag = "[Tidegate]"; }
    internal static class TidegateText { internal static string PhaseName(TidegateTidePhase phase) { return phase.ToString(); } }
    internal static class L10n { internal static string T(string zh, string en) { return zh; } }
    internal sealed class ArenaPrototypeNavigation { internal void SetWalkable(Func<Vector3, Vector3, Vector3, bool> eligible) { } }
    internal static class SkyIslandLighting
    {
        internal static double Hours;
        internal static double ClockHours() { return Hours; }
        internal static double ClockScale() { return 60d; }
        internal static float StreetLampNightFactor(double hours) { return hours >= 22 || hours < 6 ? 1f : 0f; }
    }

    internal static class RaidOfficialLighting
    {
        internal static Vector3 Direction;
        internal static Color Sunlight, Ambient;
        internal static bool TryRead(Scene scene, out Vector3 direction, out Color sunlight, out Color ambient)
        {
            direction = Direction;
            sunlight = Sunlight;
            ambient = Ambient;
            return SceneManager.Active.handle == scene.handle;
        }
    }

    internal sealed class LastShiftWorld
    {
        internal readonly GameObject Root = new GameObject("LastShiftWorld");
        internal RaidIndoorRoofs IndoorRoofs;
        internal readonly List<Vector3> NightLamps = new List<Vector3>();
    }

    internal sealed class TidegateWorld
    {
        internal readonly GameObject Root = new GameObject("TidegateWorld");
        internal RaidIndoorRoofs IndoorRoofs;
        internal bool InLagoon(Vector3 value) { return false; }
        internal bool TryGroundAt(Vector3 value, out float y) { y = value.y; return true; }
    }
}
