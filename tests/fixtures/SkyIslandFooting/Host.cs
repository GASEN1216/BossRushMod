using System;
using System.Collections.Generic;

// Only the Unity query boundary is replaced. No real scene or physics engine is loaded.
namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool aNull = ReferenceEquals(a, null) || a.destroyed;
            bool bNull = ReferenceEquals(b, null) || b.destroyed;
            return aNull || bNull ? aNull == bNull : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.destroyed = true;
            var gameObject = value as GameObject;
            if (!ReferenceEquals(gameObject, null)) gameObject.transform.destroyed = true;
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }

    public class Transform : Object
    {
        public string name;
        public Vector3 position;
        public Transform parent;
        public GameObject gameObject;
        public bool IsChildOf(Transform root)
        {
            if (root == null) return false;
            for (var current = this; current != null; current = current.parent)
                if (current == root) return true;
            return false;
        }
    }

    public class GameObject : Object
    {
        public readonly Transform transform;
        public GameObject() { transform = new Transform { gameObject = this }; }
    }

    public struct RaycastHit { public Vector3 point; public Transform transform; }
    public enum QueryTriggerInteraction { Ignore }
    public sealed class Surface { public float Height; public Transform Transform; }

    public static class Physics
    {
        public static readonly Dictionary<float, Surface> Hits = new Dictionary<float, Surface>();
        public static float WallX;
        public static int GroundMask, WallMask;
        public static bool Raycast(Vector3 start, Vector3 direction, out RaycastHit hit, float distance,
            int mask, QueryTriggerInteraction triggers)
        {
            if (mask != GroundMask || triggers != QueryTriggerInteraction.Ignore || direction.y != -1)
                throw new Exception("Wrong ground query contract");
            hit = new RaycastHit();
            Surface surface;
            if (!Hits.TryGetValue(start.x, out surface) || surface.Height > start.y || start.y - surface.Height > distance)
                return false;
            hit.point = new Vector3(start.x, surface.Height, start.z);
            hit.transform = surface.Transform;
            return true;
        }
        public static bool CheckCapsule(Vector3 bottom, Vector3 top, float radius, int mask, QueryTriggerInteraction triggers)
        {
            if (mask != WallMask || triggers != QueryTriggerInteraction.Ignore)
                throw new Exception("Wrong wall query contract");
            // A controlled low obstruction; this is not a general PhysX approximation.
            return Math.Abs(bottom.x - WallX) < radius && bottom.y - radius < 1.2f && top.y + radius > 0.2f;
        }
    }

    public static class Debug
    {
        public static void Log(object text) { }
        public static void LogWarning(object text) { }
    }
}

namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static class Layers { public static int wallLayerMask; }
    }
}

namespace BossRush
{
    internal static class L10n { public static string T(string chinese, string english) { return english; } }
    internal sealed class Player
    {
        public UnityEngine.Vector3 Position;
        public void SetPosition(UnityEngine.Vector3 position) { Position = position; }
    }
}
