using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UnityEngine.Rendering
{
    public struct ShaderTagId
    {
        public string name;
        public ShaderTagId(string name) { this.name = name; }
    }
    public static class GraphicsSettings { public static object currentRenderPipeline; }
}
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public int DestroyCalls;
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull || rightNull ? leftNull && rightNull : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object target)
        {
            if (target == null) return;
            target.Destroyed = true; target.DestroyCalls++;
            var root = target as GameObject;
            if (ReferenceEquals(root, null)) return;
            foreach (var renderer in root.Renderers) { Destroy(renderer); Destroy(renderer.gameObject); }
            foreach (var collider in root.Colliders) { Destroy(collider); Destroy(collider.gameObject); }
        }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1f, 1f, 1f, 1f); } }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Vector4 { }
    public sealed class Texture : Object { public string name; }
    public static class Debug
    {
        public static readonly List<string> Messages = new List<string>();
        public static void Log(string message) { Messages.Add(message); }
        public static void LogWarning(string message) { Messages.Add(message); }
    }
    public sealed class Shader : Object
    {
        public string name;
        public bool isSupported = true;
        public string[] Tags = new string[0];
        public readonly HashSet<string> Properties = new HashSet<string>();
        public int TagReads;
        public int passCount { get { return Tags.Length; } }
        public Rendering.ShaderTagId FindPassTagValue(int i, Rendering.ShaderTagId key)
        { TagReads++; return new Rendering.ShaderTagId(Tags[i]); }
        public static readonly Dictionary<string, Shader> Registry = new Dictionary<string, Shader>();
        public static int FindReads;
        public static Shader Find(string name) { FindReads++; Shader shader; return Registry.TryGetValue(name, out shader) ? shader : null; }
        public static float GetGlobalFloat(string key) { return 0f; }
        public static Vector4 GetGlobalVector(string key) { return new Vector4(); }
    }
    public sealed class Material : Object
    {
        public Shader shader;
        public string name;
        public int renderQueue = 2000;
        private readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
        private readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
        private readonly Dictionary<string, Vector2> scales = new Dictionary<string, Vector2>();
        private readonly Dictionary<string, Vector2> offsets = new Dictionary<string, Vector2>();
        private readonly Dictionary<string, float> floats = new Dictionary<string, float>();
        public Material(Shader shader) { this.shader = shader; }
        public bool HasProperty(string key) { return shader != null && shader.Properties.Contains(key); }
        public Color GetColor(string key) { Color value; return colors.TryGetValue(key, out value) ? value : Color.white; }
        public Texture GetTexture(string key) { Texture value; return textures.TryGetValue(key, out value) ? value : null; }
        public Vector2 GetTextureScale(string key) { Vector2 value; return scales.TryGetValue(key, out value) ? value : new Vector2(1f, 1f); }
        public Vector2 GetTextureOffset(string key) { Vector2 value; return offsets.TryGetValue(key, out value) ? value : new Vector2(); }
        public float GetFloat(string key) { float value; return floats.TryGetValue(key, out value) ? value : 0f; }
        public void SetColor(string key, Color value) { colors[key] = value; }
        public void SetTexture(string key, Texture value) { textures[key] = value; }
        public void SetTextureScale(string key, Vector2 value) { scales[key] = value; }
        public void SetTextureOffset(string key, Vector2 value) { offsets[key] = value; }
        public void SetFloat(string key, float value) { floats[key] = value; }
    }
    public sealed class GameObject : Object
    {
        public int layer;
        public bool activeInHierarchy = true;
        public MeshRenderer[] Renderers = new MeshRenderer[0];
        public Collider[] Colliders = new Collider[0];
        public T[] GetComponentsInChildren<T>(bool inactive)
        { return typeof(T) == typeof(MeshRenderer) ? (T[])(object)Renderers : (T[])(object)Colliders; }
    }
    public sealed class MeshRenderer : Object
    {
        public string name = "IslandGround";
        public GameObject gameObject = new GameObject();
        public Material[] sharedMaterials;
        public Material sharedMaterial { get { return sharedMaterials[0]; } }
        public bool enabled = true, isVisible = true;
        public object bounds;
    }
    public sealed class Collider : Object
    {
        public string name;
        public GameObject gameObject = new GameObject();
    }
    public sealed class Camera
    {
        public string name;
        public int cullingMask;
        public string clearFlags;
        public Color backgroundColor;
        public float farClipPlane;
    }
    public static class LayerMask { public static string LayerToName(int layer) { return "layer" + layer; } }
    public static class RenderSettings { public static string ambientMode; public static bool fog; }
}
public sealed class GameCamera
{
    public static GameCamera Instance;
    public UnityEngine.Camera renderCamera;
}
