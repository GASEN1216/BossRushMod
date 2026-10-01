using System;
using System.Collections.Generic;

internal static class Probe
{
    internal static readonly List<string> Trace = new List<string>();
}

namespace UnityEngine
{
    public class Object
    {
        private static int nextId;
        private readonly int id = ++nextId;
        internal bool Destroyed;
        public int GetInstanceID() { return id; }
        public static bool operator ==(Object left, Object right)
        {
            bool a = ReferenceEquals(left, null) || left.Destroyed;
            bool b = ReferenceEquals(right, null) || right.Destroyed;
            return a || b ? a == b : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object obj) { return this == obj as Object; }
        public override int GetHashCode() { return id; }
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null)) return;
            obj.Destroyed = true;
            GameObject go = obj as GameObject;
            if (!ReferenceEquals(go, null))
            {
                Probe.Trace.Add("destroy:" + go.name);
                foreach (Component component in go.Components) component.Destroyed = true;
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public sealed class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly string name;
        public readonly Transform transform;
        public bool activeInHierarchy = true;
        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            Components.Add(transform);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : Component
        {
            foreach (Component component in Components)
                if (component is T && component != null) return (T)component;
            return null;
        }
        public T GetComponentInChildren<T>() where T : Component { return GetComponent<T>(); }
        public void SetActive(bool active)
        {
            Probe.Trace.Add("active:" + name + ":" + active);
            activeInHierarchy = active;
        }
    }
    public sealed class Transform : Component
    {
        public Vector3 position;
        public bool IsChildOf(Transform parent) { return ReferenceEquals(this, parent); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Sqrt(float value) { return (float)Math.Sqrt(value); }
    }
    public static class Time { public static float unscaledTime; }
}

namespace ItemStatsSystem
{
    // Only the ownership fields read by the extraction rescan are modelled.
    public sealed class Item : UnityEngine.MonoBehaviour
    {
        public object InInventory;
        public object PluggedIntoSlot;
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public int buildIndex; }
    public static class SceneManager
    {
        public static int ActiveIndex;
        public static Scene GetActiveScene() { return new Scene { buildIndex = ActiveIndex }; }
    }
}

public sealed class CharacterMainControl : UnityEngine.MonoBehaviour
{
    public static CharacterMainControl Main;
    public DamageReceiver mainDamageReceiver;
    public void SetPosition(UnityEngine.Vector3 position) { transform.position = position; }
}
public sealed class DamageReceiver : UnityEngine.MonoBehaviour { }
public sealed class AICharacterController : UnityEngine.MonoBehaviour
{
    public float forceTracePlayerDistance;
    public DamageReceiver searchedEnemy;
    public bool noticed;
    public void SetTarget(UnityEngine.Transform target) { Probe.Trace.Add("target:" + gameObject.name); }
    public void SetNoticedToTarget(DamageReceiver target) { Probe.Trace.Add("notice:" + gameObject.name); }
}
public sealed class ItemAgent_Gun
{
    public static event Action<ItemAgent_Gun> OnMainCharacterShootEvent;
    internal static int SubscriberCount { get { return OnMainCharacterShootEvent == null ? 0 : OnMainCharacterShootEvent.GetInvocationList().Length; } }
    internal static void Fire() { if (OnMainCharacterShootEvent != null) OnMainCharacterShootEvent(new ItemAgent_Gun()); }
}

namespace BossRush
{
    internal static class ModBehaviour { public static void DevLog(string text) { } }
    internal static class SpawnPositionHelper
    {
        internal static bool Succeed;
        internal static UnityEngine.Vector3 Resolved;
        internal static bool RejectCandidates, RejectFallback;
        public static bool TrySampleNavMesh(UnityEngine.Vector3 point, out UnityEngine.Vector3 result, float lift, float radius)
        {
            result = Resolved;
            return Succeed;
        }
    }
    public sealed class ZombieModeEnemyRuntimeMarker : UnityEngine.MonoBehaviour
    {
        public int RunId;
        public bool IsBoss, DeathSettled, RemovedFromRuntime;
        public CharacterMainControl Owner;
        public AICharacterController CachedAI;
        public float SuppressedForceTraceDistance;
        public bool HasSuppressedForceTraceDistance;
    }
    internal sealed class ZombieModeRunState
    {
        public int RunId, SceneBuildIndex = -1, LivingZombieCount, LivingNormalZombieCount, CurrentWaveKillTarget;
        public bool IsCleaningUp, ActiveSafeZoneActive, PortableSafeZoneActive, PlayerInsideSafeZone, SafeZoneThreatSuppressed;
        public ZombieModeLifecyclePhase LifecyclePhase = ZombieModeLifecyclePhase.Active;
        public ZombieModeCombatPhase CombatPhase = ZombieModeCombatPhase.Preparation;
        public float ActiveSafeZoneRadius, PortableSafeZoneRadius, LastSafeZoneTickTime;
        public UnityEngine.Vector3 ActiveSafeZoneCenter, PortableSafeZoneCenter;
        public readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects = new List<ZombieModeRunOnlyRecord>();
        public readonly List<ZombieModeDropCandidate> EntityDropCleanupCandidates = new List<ZombieModeDropCandidate>();
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        private bool TryResolveZombieModeSpawnPoint(UnityEngine.Vector3 point,bool virtualPoint,out UnityEngine.Vector3 result,float navMeshSampleRadius=-1f)
        {
            result=SpawnPositionHelper.Succeed ? SpawnPositionHelper.Resolved : point;
            return !SpawnPositionHelper.RejectCandidates;
        }
        private bool TryGetZombieModeReliableSpawnPosition(out UnityEngine.Vector3 result)
        {
            result=new UnityEngine.Vector3(30,0,0);
            return !SpawnPositionHelper.RejectFallback;
        }
        private readonly ZombieModeRunState runState;
        internal ZombieModeRuntimeModule(ZombieModeRunState state) { runState = state; }
        private void UpdateZombieModeSafeZoneVisual() { Probe.Trace.Add("visual:" + runState.RunId); }
    }
}
