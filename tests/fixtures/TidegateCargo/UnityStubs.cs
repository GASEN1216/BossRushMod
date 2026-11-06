using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public partial class Object
    {
        internal bool dead;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.dead, bn = ReferenceEquals(b, null) || b.dead;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return this == o as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            var go = value as GameObject;
            if (go != null)
            {
                foreach (Transform child in go.transform.children.ToArray()) Destroy(child.gameObject);
                foreach (Component c in go.components) c.dead = true;
            }
            value.dead = true;
        }
    }
    public partial class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public string name { get { return gameObject.name; } }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component
        { return gameObject.components.OfType<T>().Concat(transform.children.SelectMany(c => c.GetComponentsInChildren<T>(inactive))).ToArray(); }
    }
    public partial class GameObject : Object
    {
        public string name;
        public Transform transform;
        internal readonly List<Component> components = new List<Component>();
        public bool activeSelf = true;
        public GameObject(string name) { this.name = name; transform = AddComponent<Transform>(); }
        public T AddComponent<T>() where T : Component, new()
        { var c = new T { gameObject = this }; components.Add(c); return c; }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public partial class Transform : Component
    {
        public Vector3 position, eulerAngles;
        public Transform parent;
        public readonly List<Transform> children = new List<Transform>();
        public void SetParent(Transform value, bool preserve)
        { if (parent != null) parent.children.Remove(this); parent = value; if (parent != null) parent.children.Add(this); }
    }
    public partial struct Vector3
    {
        public float x, y, z;
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public struct Color { public Color(float r,float g,float b) { } }
    public class Renderer : Component { public bool enabled; }
    public static class Time { public static float time; }
    public static class Debug { public static void Log(string s) { } public static void LogWarning(string s) { } }
}
