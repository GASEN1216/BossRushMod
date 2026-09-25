using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public string name;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            go.Destroyed = true;
            foreach (Component c in go.Components) c.Destroyed = true;
        }
    }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform = new Transform();
        public T GetComponent<T>() where T : Component { return Components.Find(c => c is T && c != null) as T; }
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component { return GetComponent<T>(); }
        public T Add<T>(T component) where T : Component { component.gameObject = this; Components.Add(component); return component; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public class Transform { public Vector3 position; }
    public class Rigidbody : Component { public bool isKinematic; public Vector3 velocity, angularVelocity; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static float Clamp(float v, float min, float max) { return Math.Max(min, Math.Min(max, v)); }
    }
    public static class Time { public static float time, deltaTime; }
    public sealed class WaitForSeconds { public readonly float Seconds; public WaitForSeconds(float value) { Seconds = value; } }
    public struct LayerMask { }
    public enum QueryTriggerInteraction { Ignore }
    public struct RaycastHit { public Vector3 point; }
    public static class Physics
    {
        public static Func<Vector3, Vector3?> Ground;
        public static int Probes;
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float distance, LayerMask mask, QueryTriggerInteraction query)
        {
            Probes++; var point = Ground(origin); hit = new RaycastHit { point = point ?? Vector3.zero }; return point.HasValue;
        }
    }
}
namespace UnityEngine.AI
{
    using UnityEngine;
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh
    {
        public const int AllAreas = -1;
        public static Func<Vector3, Vector3?> Ground;
        public static int Probes;
        public static bool SamplePosition(Vector3 point, out NavMeshHit hit, float distance, int areas)
        {
            Probes++; var value = Ground(point); hit = new NavMeshHit { position = value ?? Vector3.zero }; return value.HasValue;
        }
    }
}
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    { public static class Layers { public static UnityEngine.LayerMask groundLayerMask; } }
}
namespace BossRush
{
    using UnityEngine;
    public enum Teams { wolf }
    public class Health
    {
        public bool IsDead;
        public float CurrentHealth = 100, MaxHealth = 100;
        public void SetHealth(float value) { CurrentHealth = value; }
    }
    public class DamageReceiver { public readonly Transform transform = new Transform(); }
    public class CharacterMainControl : MonoBehaviour
    {
        public static CharacterMainControl Main;
        public readonly Health Health = new Health();
        public readonly DamageReceiver mainDamageReceiver = new DamageReceiver();
        public int Teleports;
        public bool ThrowOnSetPosition, HealOnTeleport;
        public CharacterMainControl(string name, Vector3 position)
        { this.name = name; var go = new GameObject { name = name }; go.Add(this); transform.position = position; }
        public void SetPosition(Vector3 position)
        {
            Teleports++;
            if (ThrowOnSetPosition) throw new Exception("SetPosition failed");
            transform.position = position;
            if (HealOnTeleport) Health.CurrentHealth = Health.MaxHealth;
        }
    }
    public class AICharacterController : Component
    {
        public float forceTracePlayerDistance;
        public DamageReceiver searchedEnemy;
        public bool noticed;
        public int Targets, Notices;
        public void SetTarget(Transform target) { Targets++; }
        public void SetNoticedToTarget(DamageReceiver target) { Notices++; }
    }
    public sealed class ZombieModeEnemyRuntimeMarker : Component { public CharacterMainControl Owner; public bool IsBoss; }
    internal enum ZombieModeRunOnlyObjectKind { Enemy, Boss, Other }
    internal sealed class ZombieModeRunOnlyRecord
    { public ZombieModeRunOnlyObjectKind Kind; public GameObject GameObject; public UnityEngine.Object Target; }
    internal sealed class ZombieModeSpawnPoint { public Vector3 Position; }
    internal sealed class ZombieModeRunState
    {
        public readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects = new List<ZombieModeRunOnlyRecord>();
        public List<ZombieModeSpawnPoint> EffectiveSpawnPoints, SpawnPoints;
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        private ZombieModeRunState runState;
        internal ZombieModeRuntimeModule(ZombieModeRunState run) { runState = run; }
    }
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal int BossesPerWave;
        internal readonly List<MonoBehaviour> CurrentWaveBosses = new List<MonoBehaviour>();
        internal MonoBehaviour CurrentBoss;
    }
    internal static class ModBehaviour { internal static void DevLog(string text) { } }
}
