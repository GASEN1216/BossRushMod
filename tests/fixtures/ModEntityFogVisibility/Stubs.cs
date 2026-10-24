using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            GameObject go = value as GameObject;
            if (go != null)
            {
                if (go.activeInHierarchy) go.SetActive(false);
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (Component component in go.Components.ToArray()) Destroy(component);
                go.Destroyed = true;
                return;
            }
            Component c = value as Component;
            if (c != null)
            {
                Behaviour b = c as Behaviour;
                if (b != null && b.isActiveAndEnabled) Invoke(c, "OnDisable");
                Invoke(c, "OnDestroy");
                c.gameObject.Components.Remove(c);
            }
            value.Destroyed = true;
        }
        internal static void Invoke(Component component, string name)
        {
            MethodInfo method = component.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (method != null) method.Invoke(component, null);
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool includeInactive) where T : Component { return gameObject.GetComponentInChildren<T>(includeInactive); }
        public T GetComponentInParent<T>(bool includeInactive) where T : Component
        {
            for (Transform t = transform; t != null; t = t.Parent)
            {
                if (!includeInactive && !t.gameObject.activeInHierarchy) continue;
                T value = t.GetComponent<T>();
                if (value != null) return value;
            }
            return null;
        }
    }
    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled { get { return enabled && gameObject.activeInHierarchy && !Destroyed; } }
    }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component
    {
        public Vector3 localPosition;
        internal Transform Parent;
        internal readonly List<Transform> Children = new List<Transform>();
        public void SetParent(Transform parent, bool worldPositionStays)
        {
            if (Parent != null) Parent.Children.Remove(this);
            Parent = parent;
            if (parent != null) parent.Children.Add(this);
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        internal static readonly List<GameObject> Created = new List<GameObject>();
        internal static int Scans;
        public readonly Transform transform;
        public string name;
        public int layer;
        public bool activeSelf = true;
        public bool activeInHierarchy { get { return activeSelf && (transform.Parent == null || transform.Parent.gameObject.activeInHierarchy) && !Destroyed; } }
        public GameObject(string name = "GameObject")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            Components.Add(transform);
            Created.Add(this);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T { gameObject = this };
            Components.Add(component);
            Invoke(component, "Awake");
            Behaviour b = component as Behaviour;
            if (b != null && b.isActiveAndEnabled) Invoke(component, "OnEnable");
            return component;
        }
        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(c => c != null); }
        public T GetComponentInParent<T>(bool includeInactive) where T : Component { return transform.GetComponentInParent<T>(includeInactive); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            Scans++;
            var result = new List<T>();
            Collect(this, includeInactive, result);
            return result.ToArray();
        }
        private static void Collect<T>(GameObject go, bool includeInactive, List<T> result) where T : Component
        {
            if (includeInactive || go.activeInHierarchy) result.AddRange(go.Components.OfType<T>().Where(c => c != null));
            foreach (Transform child in go.transform.Children) Collect(child.gameObject, includeInactive, result);
        }
        public T GetComponentInChildren<T>(bool includeInactive) where T : Component { return GetComponentsInChildren<T>(includeInactive).FirstOrDefault(); }
        public void SetActive(bool value)
        {
            var states = new Dictionary<Behaviour, bool>();
            foreach (Behaviour b in GetComponentsInChildren<Behaviour>(true)) states[b] = b.isActiveAndEnabled;
            activeSelf = value;
            foreach (var item in states)
                if (item.Key.isActiveAndEnabled != item.Value) Invoke(item.Key, item.Key.isActiveAndEnabled ? "OnEnable" : "OnDisable");
        }
    }
    public class Renderer : Component { public bool enabled = true; public bool forceRenderingOff; }
    public class Light : Behaviour { public int cullingMask = -1; }
    public class Collider : Behaviour { }
}

namespace FOW
{
    public class FogOfWarHider : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Transform[] SamplePoints;
        public bool PermanentlyReveal;
        public int NumObservers;
        private Action<bool> changed;
        public event Action<bool> OnActiveChanged { add { changed += value; } remove { changed -= value; } }
        public int Subscribers { get { return changed == null ? 0 : changed.GetInvocationList().Length; } }
        public int EnableCalls;
        public bool ConfiguredOnEnable = true;
        private void OnEnable()
        {
            EnableCalls++;
            ConfiguredOnEnable &= SamplePoints != null && SamplePoints.Length > 0 && SamplePoints.All(t => t != null);
            // FOW.dll: RegisterHider -> HidersList.Add -> SetActive(false), synchronously.
            Emit(false);
        }
        public void Emit(bool visible) { if (changed != null) changed(visible); }
    }
}

public class Health { public bool IsDead; }
public struct DamageInfo { }
public class CharacterMainControl : UnityEngine.MonoBehaviour
{
    public static CharacterMainControl Main;
    public Health Health;
    public CharacterModel characterModel;
}
public class CharacterModel
{
    public UnityEngine.GameObject Visual;
    public bool Hidden;
    public int SyncCalls;
    public void SyncHiddenToMainCharacter()
    {
        SyncCalls++;
        BossRush.VisualTrace.Events.Add("sync");
        Visual.layer = Hidden ? 23 : 8;
    }
}
public class CharacterSubVisuals : UnityEngine.Component
{
    public int SetRenderersCalls;
    public readonly List<UnityEngine.Renderer> SavedRenderers = new List<UnityEngine.Renderer>();
    public void SetRenderers() { SetRenderersCalls++; SavedRenderers.Clear(); SavedRenderers.AddRange(gameObject.GetComponentsInChildren<UnityEngine.Renderer>(true)); }
}
public class LevelManager
{
    public static LevelManager Instance;
    public bool IsRaidMap;
    private static Action<DamageInfo> died;
    public static event Action<DamageInfo> OnMainCharacterDead { add { died += value; } remove { died -= value; } }
    public static int Subscribers { get { return died == null ? 0 : died.GetInvocationList().Length; } }
    public static void EmitDeath() { if (died != null) died(new DamageInfo()); }
}
namespace BossRush
{
    internal static class ModBehaviour { internal static void DevLog(string message) { } }
    internal static class VisualTrace { internal static readonly List<string> Events = new List<string>(); }
}
