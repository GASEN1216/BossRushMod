using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public string name;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an && bn || !an && !bn && ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null) || value.Destroyed) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null))
            {
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (Component component in go.Components) component.Destroyed = true;
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public new string name { get { return gameObject.name; } set { gameObject.name=value; } }
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public sealed class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        public int layer;
        public bool activeSelf = true;
        public GameObject(string value) { name = value; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public T AddComponent<T>() where T : Component, new() { T c = new T { gameObject = this }; Components.Add(c); if (c is Collider) Physics.Colliders.Add((Collider)(object)c); return c; }
        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(c => c != null); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            return Components.OfType<T>().Where(c => c != null).Concat(transform.Children.SelectMany(c => c.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        }
    }
    public sealed class Transform : Component, IEnumerable<Transform>
    {
        public readonly List<Transform> Children = new List<Transform>();
        public Transform parent;
        public Vector3 localPosition, localScale = Vector3.one;
        public Vector3 position { get { return parent == null ? localPosition : parent.TransformPoint(localPosition); } set { localPosition = parent == null ? value : parent.InverseTransformPoint(value); } }
        public void SetParent(Transform value, bool preserve) { Vector3 before = position; if (parent != null) parent.Children.Remove(this); parent = value; if (value != null) value.Children.Add(this); if (preserve) position = before; }
        public Vector3 TransformPoint(Vector3 value) { Vector3 point = localPosition + Vector3.Scale(localScale, value); return parent == null ? point : parent.TransformPoint(point); }
        public Vector3 InverseTransformPoint(Vector3 value) { if (parent != null) value = parent.InverseTransformPoint(value); value -= localPosition; return new Vector3(value.x/localScale.x, value.y/localScale.y, value.z/localScale.z); }
        public Vector3 lossyScale { get { return parent == null ? localScale : Vector3.Scale(parent.lossyScale, localScale); } }
        public Transform Find(string path) { Transform current = this; foreach (string piece in path.Split('/')) { current = current.Children.FirstOrDefault(c => c.gameObject.name == piece); if (current == null) return null; } return current; }
        public bool IsChildOf(Transform value) { return this == value || parent != null && parent.IsChildOf(value); }
        public IEnumerator<Transform> GetEnumerator() { return Children.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public float sqrMagnitude { get { return x*x + y*y + z*z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public static Vector3 Scale(Vector3 a, Vector3 b) { return new Vector3(a.x*b.x, a.y*b.y, a.z*b.z); }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x, a.y+b.y, a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x, a.y-b.y, a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b, a.y*b, a.z*b); }
    }
    public struct Vector4 { public float x,y,z,w; public Vector4(float a,float b,float c,float d) { x=a;y=b;z=c;w=d; } public static Vector4 zero { get { return new Vector4(); } } }
    public struct Rect
    {
        public float xMin,yMin,xMax,yMax;
        public float width { get { return xMax-xMin; } }
        public float height { get { return yMax-yMin; } }
        public Vector3 center { get { return new Vector3((xMin+xMax)*.5f,(yMin+yMax)*.5f,0); } }
        public static Rect MinMaxRect(float a,float b,float c,float d) { return new Rect { xMin=a,yMin=b,xMax=c,yMax=d }; }
    }
    public struct Color { public float r,g,b,a; public Color(float red,float green,float blue,float alpha=1) { r=red;g=green;b=blue;a=alpha; } }
    public class Texture : Object { }
    public sealed class Texture2D : Texture
    {
        public int width, height; public bool isReadable = true;
        public Color[] Pixels;
        public int GetPixelsCalls;
        public Color[] GetPixels() { GetPixelsCalls++; return (Color[])Pixels.Clone(); }
    }
    public sealed class Material : Object
    {
        public Shader shader;
        public readonly Dictionary<string,object> Values = new Dictionary<string, object>();
        public bool HasProperty(string key) { return Values.ContainsKey(key); }
        public Texture GetTexture(string key) { return (Texture)Values[key]; }
        public Vector4 GetVector(string key) { return (Vector4)Values[key]; }
    }
    public sealed class Shader : Object { public bool isSupported = true; }
    public class Renderer : Component { public Material sharedMaterial; public Material[] sharedMaterials { get { return new[] { sharedMaterial }; } } }
    public sealed class ParticleSystemRenderer : Renderer { }
    public sealed class LineRenderer : Renderer { }
    public class Collider : Component { public bool enabled = true; public virtual Bounds bounds { get { return new Bounds(); } } }
    public sealed class BoxCollider : Collider
    {
        public bool isTrigger; public Vector3 center, size;
        public override Bounds bounds { get { Vector3 c = transform.TransformPoint(center), s = Vector3.Scale(transform.lossyScale, size); return new Bounds { min = c-s*.5f, max = c+s*.5f }; } }
    }
    public struct Bounds { public Vector3 min,max; }
    public struct RaycastHit { public Transform transform; public Vector3 point; }
    public enum QueryTriggerInteraction { Ignore }
    public struct LayerMask { public int value; public static int NameToLayer(string value) { return 4; } }
    public static class Physics
    {
        internal static readonly List<Collider> Colliders = new List<Collider>();
        public static bool Raycast(Vector3 a, Vector3 b, out RaycastHit hit, float distance, int mask, QueryTriggerInteraction trigger) { hit = new RaycastHit(); return false; }
        public static int OverlapSphereNonAlloc(Vector3 point, float radius, Collider[] results, int mask)
        {
            int count = 0;
            foreach (Collider collider in Colliders)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeSelf || (mask & (1<<collider.gameObject.layer)) == 0) continue;
                Bounds b = collider.bounds;
                float dx = Math.Max(b.min.x-point.x,Math.Max(0,point.x-b.max.x));
                float dy = Math.Max(b.min.y-point.y,Math.Max(0,point.y-b.max.y));
                float dz = Math.Max(b.min.z-point.z,Math.Max(0,point.z-b.max.z));
                if (dx*dx+dy*dy+dz*dz > radius*radius) continue;
                if (count < results.Length) results[count] = collider;
                count++;
            }
            return count;
        }
    }
    public static class Mathf { public static float Max(float a,float b) { return Math.Max(a,b); } public static float Sqrt(float value) { return (float)Math.Sqrt(value); } }
    public static class Time { public static float unscaledTime; }
    public static class Debug { public static readonly List<string> Logs = new List<string>(); public static void Log(string value) { Logs.Add(value); } public static void LogWarning(string value) { Logs.Add(value); } }
}
namespace UnityEngine.Rendering { }
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static readonly LayerData Layers = new LayerData();
        public sealed class LayerData { public UnityEngine.LayerMask groundLayerMask, wallLayerMask; }
    }
}
namespace ItemStatsSystem { public class Item { public float TotalWeight; } }
namespace ItemStatsSystem.Stats { public enum ModifierType { Add, PercentageMultiply } }
public sealed class CharacterMainControl : UnityEngine.Component { public ItemStatsSystem.Item CharacterItem; public bool Swimming; }
namespace BossRush
{
    internal sealed class TidegateTide { internal float Level, LagoonLevel; }
    internal static class TidegateRaidScene { internal const string LogTag = "[Tidegate]"; }
    internal sealed class BossRushStatModifierRecord { }
    internal static class RuntimeStatModifierTracker
    {
        internal static bool TryAdd(CharacterMainControl c,string s,float v,object source,List<BossRushStatModifierRecord> records,string tag,ItemStatsSystem.Stats.ModifierType mode=ItemStatsSystem.Stats.ModifierType.Add) { return true; }
        internal static void RemoveAll(List<BossRushStatModifierRecord> records,string tag) { records.Clear(); }
    }
    internal static class TidegateInventory { internal static bool Has(CharacterMainControl p,int id) { return false; } }
    internal static class BossRushItemIds { internal const int TidegateDryBag = 500113, TidegateFlippers = 500114; }
}

// 屋顶按独立场景包回放验证；水域夹具只覆盖物理与高度查询。
namespace BossRush { internal sealed class RaidIndoorRoofs : IDisposable { internal RaidIndoorRoofs(UnityEngine.GameObject world, bool pavilions) { } public void Dispose() { } } }
