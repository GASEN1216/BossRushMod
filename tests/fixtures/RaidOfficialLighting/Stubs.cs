using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;

        public static bool operator ==(Object left, Object right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (ReferenceEquals(left, null)) return right.Destroyed;
            if (ReferenceEquals(right, null)) return left.Destroyed;
            return false;
        }

        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }

        public static void Destroy(Object value)
        {
            if (value == null) return;
            value.Destroyed = true;
            var node = value as GameObject;
            if (ReferenceEquals(node, null)) return;
            foreach (Component component in node.GetComponents<Component>()) component.Destroyed = true;
            foreach (Transform child in node.transform.Children.ToArray()) Destroy(child.gameObject);
        }

        public static void DontDestroyOnLoad(Object value)
        {
            SceneManager.MoveGameObjectToScene((GameObject)value, SceneManager.Persistent);
        }

        public static T Instantiate<T>(T original) where T : Object
        {
            var profile = (Umbra.UmbraProfile)(Object)original;
            return (T)(Object)new Umbra.UmbraProfile { name = profile.name, contactStrength = profile.contactStrength };
        }

        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Component
        {
            if (parent.gameObject.activeInHierarchy) throw new InvalidOperationException("替身要求从停用父物体克隆");
            var source = (Light)(Component)original;
            var clone = new GameObject(source.gameObject.name + "(Clone)", parent.gameObject.scene);
            clone.transform.SetParent(parent, worldPositionStays);
            clone.transform.forward = source.transform.forward;
            var light = clone.AddComponent<Light>();
            light.type = source.type;
            light.color = source.color;
            light.intensity = source.intensity;
            light.shadows = source.shadows;
            light.bakingOutput = source.bakingOutput;
            var sourceData = source.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            clone.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().settings = sourceData.settings;
            var shadows = source.GetComponent<Umbra.UmbraSoftShadows>();
            if (shadows != null) clone.AddComponent<Umbra.UmbraSoftShadows>().profile = shadows.profile;
            return (T)(Component)light;
        }
    }

    public class ScriptableObject : Object
    {
        public string name;
        public static T CreateInstance<T>() where T : ScriptableObject, new() { return new T(); }
    }

    public static class Resources
    {
        internal static Object[] Assets = Array.Empty<Object>();
        public static T[] FindObjectsOfTypeAll<T>() where T : Object { return Assets.OfType<T>().ToArray(); }
    }

    public static class Debug
    {
        internal static readonly List<string> Warnings = new List<string>();
        public static void LogWarning(string warning) { Warnings.Add(warning); }
    }

    public static class Shader
    {
        internal static readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
        internal static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>();
        public static float GetGlobalFloat(string key) { return Floats.TryGetValue(key, out float value) ? value : 0f; }
        public static Color GetGlobalColor(string key) { return Colors.TryGetValue(key, out Color value) ? value : default(Color); }
    }

    public class GameObject : Object
    {
        internal static readonly List<GameObject> All = new List<GameObject>();
        private readonly List<Component> components = new List<Component>();
        public readonly string name;
        public bool activeSelf { get; private set; } = true;
        public Scene scene { get; internal set; }
        public Transform transform { get; }
        public bool activeInHierarchy
        {
            get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); }
        }

        public GameObject(string name) : this(name, SceneManager.GetActiveScene()) { }

        public GameObject(string name, Scene scene)
        {
            this.name = name;
            this.scene = scene;
            transform = AddComponent<Transform>();
            All.Add(this);
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component { return components.OfType<T>().FirstOrDefault(); }
        public T[] GetComponents<T>() where T : Component { return components.OfType<T>().ToArray(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            if (!activeInHierarchy && !includeInactive) return Array.Empty<T>();
            return GetComponents<T>().Concat(transform.Children.Where(child => child != null)
                .SelectMany(child => child.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        }

        public void SetActive(bool active) { activeSelf = active; }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T[] GetComponents<T>() where T : Component { return gameObject.GetComponents<T>(); }
        public T GetComponentInChildren<T>(bool includeInactive) where T : Component
        {
            return gameObject.GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        }
    }

    public sealed class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();
        public Transform parent { get; private set; }
        public int childCount { get { return Children.Count(child => child != null); } }
        public Vector3 forward = new Vector3(0, 0, 1);

        public void SetParent(Transform value, bool worldPositionStays)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = value;
            if (parent != null)
            {
                gameObject.scene = parent.gameObject.scene;
                parent.Children.Add(this);
            }
        }
    }

    public enum LightType { Spot, Directional, Point }
    public class Camera : Component { }
    public class Renderer : Component { public bool enabled = true; }
    public enum LightShadows { None, Hard, Soft }
    public enum LightmapBakeType { Mixed = 1, Baked = 2, Realtime = 4 }
    public struct LightBakingOutput
    {
        public bool isBaked;
        public LightmapBakeType lightmapBakeType;
        public int probeOcclusionLightIndex, occlusionMaskChannel;
    }

    public sealed class Light : Component
    {
        public LightType type;
        public bool enabled = true;
        public bool isActiveAndEnabled { get { return enabled && gameObject.activeInHierarchy; } }
        public Color color = new Color(1, 1, 1);
        public float intensity = 1;
        public LightShadows shadows;
        public LightBakingOutput bakingOutput;
    }

    public enum ColorSpace { Gamma, Linear }

    public static class QualitySettings
    {
        public static ColorSpace activeColorSpace;
    }

    public struct Color
    {
        public readonly float r, g, b;
        public Color(float red, float green, float blue) { r = red; g = green; b = blue; }
        public Color linear { get { return new Color(ToLinear(r), ToLinear(g), ToLinear(b)); } }
        public static Color operator *(Color color, float intensity)
        {
            return new Color(color.r * intensity, color.g * intensity, color.b * intensity);
        }
        private static float ToLinear(float value)
        {
            return value <= 0.04045f ? value / 12.92f : (float)Math.Pow((value + 0.055f) / 1.055f, 2.4f);
        }
    }

    public struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 value) { return new Vector3(-value.x, -value.y, -value.z); }
    }

    public static class RenderSettings
    {
        public static Light sun { get { return SceneManager.ActiveSettings.Sun; } set { SceneManager.ActiveSettings.Sun = value; } }
        public static AmbientMode ambientMode
        {
            get { return SceneManager.ActiveSettings.Mode; }
            set { SceneManager.ActiveSettings.Mode = value; }
        }
        public static Color ambientEquatorColor
        {
            get { return SceneManager.ActiveSettings.Equator; }
            set { SceneManager.ActiveSettings.Equator = value; }
        }
        public static Color ambientSkyColor { get { return SceneManager.ActiveSettings.Sky; } set { SceneManager.ActiveSettings.Sky = value; } }
        public static Color ambientGroundColor { get { return SceneManager.ActiveSettings.Ground; } set { SceneManager.ActiveSettings.Ground = value; } }
        public static bool fog { get { return SceneManager.ActiveSettings.Fog; } set { SceneManager.ActiveSettings.Fog = value; } }
    }
}

namespace UnityEngine.Rendering
{
    public enum AmbientMode { Skybox, Trilight, Flat, Custom }
    public class VolumeProfile : ScriptableObject { }
    public class VolumeManager
    {
        public static readonly VolumeManager instance = new VolumeManager();
        public VolumeStack stack;
    }
    public class VolumeStack
    {
        public readonly Dictionary<Type, ScriptableObject> Components = new Dictionary<Type, ScriptableObject>();
        public T GetComponent<T>() where T : ScriptableObject
        {
            return Components.TryGetValue(typeof(T), out ScriptableObject value) ? (T)value : null;
        }
    }
}

namespace SodaCraft
{
    public class LightControl : ScriptableObject
    {
        private static Light light;
        internal static void SetTarget(Light target) { light = target; }
        public sealed class IntensityParameter { public float value; }
        public readonly IntensityParameter sunIntensity = new IntensityParameter();
        public bool Active;
        public bool IsActive() { return Active; }
    }
    public class SunFogTD : ScriptableObject
    {
        public bool Active;
        public bool IsActive() { return Active; }
    }
}

namespace UnityEngine.Rendering.Universal
{
    public sealed class UniversalAdditionalLightData : Component { public int settings; }
}

namespace Umbra
{
    public sealed class UmbraProfile : ScriptableObject { public float contactStrength; }
    public sealed class UmbraSoftShadows : Component { public UmbraProfile profile; }
}

namespace Duckov.Weathers
{
    public enum Weather { Sunny, Cloudy, Rainy, Stormy_I, Stormy_II, Snow }
}

public enum TimePhaseTags { day, dawn, night }

public struct TimeOfDayPhase
{
    public TimePhaseTags timePhaseTag;
    public VolumeProfile volumeProfile;
}

public class TimeOfDayEntry : UnityEngine.Object
{
    private readonly List<TimeOfDayPhase> phases;

    public TimeOfDayEntry(params VolumeProfile[] profiles)
    {
        phases = profiles.Select((profile, index) =>
            new TimeOfDayPhase { timePhaseTag = (TimePhaseTags)index, volumeProfile = profile }).ToList();
    }

    public TimeOfDayPhase GetPhase(TimePhaseTags tag) { return phases.Single(phase => phase.timePhaseTag == tag); }
}

public class TimeOfDayConfig : UnityEngine.Object
{
    public readonly Dictionary<Duckov.Weathers.Weather, TimeOfDayEntry> Entries =
        new Dictionary<Duckov.Weathers.Weather, TimeOfDayEntry>();

    public TimeOfDayEntry GetCurrentEntry(Duckov.Weathers.Weather weather) { return Entries[weather]; }
}

public class TimeOfDayController : UnityEngine.Object
{
    public static TimeOfDayController Instance;
    public TimeOfDayPhase CurrentPhase;
    public Duckov.Weathers.Weather CurrentWeather;
    public float Time;
}

public class GameCamera : UnityEngine.Object
{
    public static GameCamera Instance;
    public Camera renderCamera;
}

public class SunFogEntry : Component { }

namespace UnityEngine.SceneManagement
{
    public struct Scene : IEquatable<Scene>
    {
        public readonly int handle;
        public readonly string name;
        internal Scene(int handle, string name) { this.handle = handle; this.name = name; }
        public bool isLoaded { get { return SceneManager.Loaded.Contains(handle); } }
        public bool IsValid() { return name != null; }
        public GameObject[] GetRootGameObjects()
        {
            Scene scene = this;
            return GameObject.All.Where(go => go != null && go.scene == scene && go.transform.parent == null).ToArray();
        }
        public bool Equals(Scene other) { return handle == other.handle && name == other.name; }
        public override bool Equals(object other) { return other is Scene && Equals((Scene)other); }
        public override int GetHashCode() { return handle; }
        public static bool operator ==(Scene left, Scene right) { return left.Equals(right); }
        public static bool operator !=(Scene left, Scene right) { return !left.Equals(right); }
    }

    public static class SceneManager
    {
        internal sealed class Settings
        {
            internal Light Sun;
            internal AmbientMode Mode = AmbientMode.Custom;
            internal Color Equator, Sky, Ground;
            internal bool Fog;
        }

        internal static readonly Scene Persistent = new Scene(0, "DontDestroyOnLoad");
        internal static readonly HashSet<int> Loaded = new HashSet<int>();
        private static readonly Dictionary<int, Settings> settings = new Dictionary<int, Settings>();
        private static Scene active;
        public static event Action<Scene, Scene> activeSceneChanged;
        internal static int ActiveSceneSubscribers
        {
            get { return activeSceneChanged == null ? 0 : activeSceneChanged.GetInvocationList().Length; }
        }
        internal static Settings ActiveSettings { get { return settings[active.handle]; } }
        internal static bool FailNextMove;

        internal static void Reset()
        {
            foreach (GameObject node in GameObject.All) UnityEngine.Object.Destroy(node);
            GameObject.All.Clear();
            Loaded.Clear();
            settings.Clear();
            active = Load(0, Persistent.name);
            FailNextMove = false;
        }

        internal static Scene Load(int handle, string name)
        {
            Loaded.Add(handle);
            settings.Add(handle, new Settings());
            return new Scene(handle, name);
        }

        internal static Scene LoadSingle(int handle, string name)
        {
            Scene next = Load(handle, name);
            foreach (int old in Loaded.Where(value => value != 0 && value != handle).ToArray())
                Unload(new Scene(old, null));
            Activate(next);
            return next;
        }

        internal static void Activate(Scene next)
        {
            Scene previous = active;
            active = next;
            activeSceneChanged?.Invoke(previous, next);
        }

        internal static void Unload(Scene scene)
        {
            foreach (GameObject node in GameObject.All.Where(go => go != null && go.scene.handle == scene.handle).ToArray())
                UnityEngine.Object.Destroy(node);
            Loaded.Remove(scene.handle);
        }

        public static Scene GetActiveScene() { return active; }

        public static void MoveGameObjectToScene(GameObject node, Scene scene)
        {
            if (FailNextMove)
            {
                FailNextMove = false;
                throw new InvalidOperationException("模拟移动物体失败");
            }
            if (node.transform.parent != null) throw new InvalidOperationException("只能移动场景根节点");
            MoveTree(node, scene);
        }

        private static void MoveTree(GameObject node, Scene scene)
        {
            node.scene = scene;
            foreach (Transform child in node.transform.Children) MoveTree(child.gameObject, scene);
        }
    }
}
