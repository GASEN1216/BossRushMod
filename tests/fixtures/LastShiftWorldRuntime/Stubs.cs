using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    internal struct Vector2
    {
        internal Vector2(float x, float y) { }
    }

    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal static Vector3 zero { get { return new Vector3(); } }
        internal static Vector3 up { get { return new Vector3(0, 1, 0); } }
        internal static Vector3 down { get { return new Vector3(0, -1, 0); } }
        internal float sqrMagnitude { get { return x * x + y * y + z * z; } }
        internal float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }

    internal struct Quaternion
    {
        internal static Quaternion Euler(float x, float y, float z) { return new Quaternion(); }
        internal static Quaternion FromToRotation(Vector3 from, Vector3 to) { return new Quaternion(); }
    }

    internal struct Vector4
    {
        internal float x { get { return 0f; } }
        internal float y { get { return 0f; } }
        internal float z { get { return 0f; } }
        internal float w { get { return 0f; } }
        internal static Vector4 zero { get { return new Vector4(); } }
    }

    internal struct Color
    {
        internal float g { get { return 0f; } }
    }

    internal struct Rect
    {
        internal float xMin, yMin, xMax, yMax;
        internal float width { get { return xMax - xMin; } }
        internal float height { get { return yMax - yMin; } }
        internal Vector3 center { get { return new Vector3((xMin + xMax) * .5f, (yMin + yMax) * .5f, 0f); } }
        internal static Rect MinMaxRect(float x0, float y0, float x1, float y1)
        { return new Rect { xMin = x0, yMin = y0, xMax = x1, yMax = y1 }; }
    }

    internal struct Bounds
    {
        internal Vector3 center, size;
        internal Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
    }

    internal struct Ray
    {
        internal Vector3 Origin;
        internal Ray(Vector3 origin, Vector3 direction) { Origin = origin; }
    }

    internal struct RaycastHit
    {
        internal Vector3 point, normal;
    }

    internal class Component
    {
        internal GameObject gameObject;
        internal string name { get { return gameObject.name; } }
        internal T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        internal T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        { return gameObject.GetComponentsInChildren<T>(includeInactive); }
    }

    internal sealed class GameObject
    {
        internal string name;
        internal int layer;
        internal bool activeSelf = true;
        internal bool activeInHierarchy
        {
            get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); }
        }
        internal readonly Transform transform;
        private readonly List<Component> components = new List<Component>();

        internal GameObject(string name)
        {
            this.name = name;
            transform = AddComponent<Transform>();
        }

        internal void SetActive(bool value) { activeSelf = value; }

        internal T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }

        internal T GetComponent<T>() where T : Component
        {
            foreach (Component component in components)
                if (component is T) return (T)component;
            return null;
        }

        internal T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var result = new List<T>();
            foreach (Component component in components)
                if (component is T) result.Add((T)component);
            foreach (Transform child in transform)
                if (includeInactive || child.gameObject.activeInHierarchy)
                    result.AddRange(child.GetComponentsInChildren<T>(includeInactive));
            return result.ToArray();
        }
    }

    internal sealed class Transform : Component, IEnumerable<Transform>
    {
        internal Vector3 position;
        internal Vector3 localPosition;
        internal Quaternion localRotation;
        internal Transform parent;
        private readonly List<Transform> children = new List<Transform>();

        internal void SetParent(Transform parent, bool worldPositionStays)
        {
            this.parent = parent;
            parent.children.Add(this);
        }

        internal Transform Find(string path)
        {
            Transform at = this;
            foreach (string part in path.Split('/'))
            {
                at = at.children.Find(child => child.name == part);
                if (at == null) return null;
            }
            return at;
        }

        public IEnumerator<Transform> GetEnumerator() { return children.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    internal class Renderer : Component
    {
        internal Material[] sharedMaterials = new Material[0];
        internal Material sharedMaterial { get { return sharedMaterials.Length == 0 ? null : sharedMaterials[0]; } }
        internal Bounds bounds;
    }

    internal sealed class MeshRenderer : Renderer { }
    internal sealed class ParticleSystemRenderer : Renderer { }
    internal sealed class LineRenderer : Renderer { }
    internal sealed class Texture2D
    {
        internal bool isReadable { get { return false; } }
        internal int width { get { return 0; } }
        internal int height { get { return 0; } }
        internal Color[] GetPixels() { return new Color[0]; }
    }
    internal sealed class Material
    {
        internal string name { get; set; }
        internal Shader shader { get; set; }
        internal bool HasProperty(string name) { return false; }
        internal Texture2D GetTexture(string name) { return null; }
        internal Vector4 GetVector(string name) { return Vector4.zero; }
    }
    internal sealed class Shader
    {
        internal string name { get; set; }
        internal bool isSupported { get; set; }
    }

    internal class Collider : Component
    {
        internal bool enabled = true;
        internal bool isTrigger;
        internal float Height, MinX = float.NegativeInfinity, MaxX = float.PositiveInfinity;
        internal Vector3 Normal = Vector3.up;

        internal bool Raycast(Ray ray, out RaycastHit hit, float distance)
        {
            hit = new RaycastHit { point = new Vector3(ray.Origin.x, Height, ray.Origin.z), normal = Normal };
            return enabled && gameObject.activeInHierarchy && ray.Origin.x >= MinX && ray.Origin.x <= MaxX
                && ray.Origin.y >= Height && ray.Origin.y - Height <= distance;
        }
    }

    internal sealed class BoxCollider : Collider { internal Vector3 size; }
    internal static class Mathf
    {
        internal static float Sqrt(float value) { return (float)Math.Sqrt(value); }
        internal static float Abs(float value) { return Math.Abs(value); }
    }
    internal static class Debug
    {
        internal static void Log(string message) { }
        internal static void LogWarning(string message) { Console.WriteLine(message); }
    }
    internal static class Physics { internal static void SyncTransforms() { } }
    internal struct LayerMask
    {
        internal int value;
        internal static int NameToLayer(string name) { return name == "Water" ? 12 : -1; }
    }
}

namespace UnityEngine.Rendering { }

namespace Duckov.Utilities
{
    internal static class GameplayDataSettings
    {
        internal static class Layers
        {
            internal static readonly UnityEngine.LayerMask groundLayerMask = new UnityEngine.LayerMask { value = 1 << 8 };
            internal static readonly UnityEngine.LayerMask wallLayerMask = new UnityEngine.LayerMask { value = 1 << 9 };
        }
    }
}

namespace BossRush
{
    using UnityEngine;

    internal static class LastShiftStory { internal static readonly string[] NoteMarkers = new string[0]; }
    internal static class LastShiftRaidScene { internal const string LogTag = "[LastShift]"; }
    internal static class TidegateRaidScene { internal const string LogTag = "[Tidegate]"; }
    internal sealed class RaidIndoorRoofs : IDisposable
    {
        internal RaidIndoorRoofs(GameObject root, bool active) { }
        public void Dispose() { }
    }
    internal static class RaidHalfCover
    {
        internal delegate bool GroundProbe(Vector3 position, out float height);
        internal static int Register(Transform root, int layer, GroundProbe probe) { return 0; }
    }
}
