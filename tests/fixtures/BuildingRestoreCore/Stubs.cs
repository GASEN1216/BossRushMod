using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull && rightNull || !leftNull && !rightNull && ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object obj) { return ReferenceEquals(this, obj); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }

    public class GameObject : Object
    {
        private readonly int id;
        public readonly string Name;
        public GameObject(int id, string name) { this.id = id; Name = name; }
        public int GetInstanceID() { return id; }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public string Kind;
        public Component(GameObject go, string kind) { gameObject = go; Kind = kind; }
    }

    public class Coroutine
    {
        public IEnumerator Iterator;
        public Coroutine(IEnumerator iterator) { Iterator = iterator; }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; }
    public static class SceneManager
    {
        public static int ActiveHandle;
        public static Scene GetActiveScene() { return new Scene { handle = ActiveHandle }; }
    }
}

namespace BossRush
{
    public class ModBehaviour
    {
        public int Started;
        public int Stopped;
        public readonly List<UnityEngine.Coroutine> Coroutines = new List<UnityEngine.Coroutine>();
        public UnityEngine.Coroutine StartCoroutine(IEnumerator iterator)
        {
            Started++;
            var coroutine = new UnityEngine.Coroutine(iterator);
            Coroutines.Add(coroutine);
            return coroutine;
        }
        public void StopCoroutine(UnityEngine.Coroutine coroutine) { Stopped++; }
    }

    internal static class BuildingInjectionHelper
    {
        internal static Type GetBuildingType() { return typeof(UnityEngine.Component); }
    }

    internal static class ObjectCache
    {
        internal static UnityEngine.Object[] Buildings;
        internal static int Invalidations;
        internal static UnityEngine.Object[] GetSceneObjectsByType(Type type) { return Buildings; }
        internal static void InvalidateSceneObjectsByType(Type type) { Invalidations++; }
    }
}
