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
        public int GetInstanceID() { return GetHashCode(); }
        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            go.Destroyed = true;
            foreach (Component c in go.Components) c.Destroyed = true;
            foreach (Transform child in go.transform.Children) Destroy(child.gameObject);
        }
    }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        public static int TreeScans;
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T GetComponent<T>() where T : Component { return Components.Find(c => c is T && c != null) as T; }
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component
        { T value = GetComponent<T>(); if (value != null) return value; foreach (Transform child in transform.Children) { value = child.gameObject.GetComponentInChildren<T>(includeInactive); if (value != null) return value; } return null; }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            TreeScans++;
            var result = new List<T>(); Collect(result); return result.ToArray();
        }
        private void Collect<T>(List<T> result) where T : Component
        { foreach (Component component in Components) if (component != null && component is T) result.Add((T)component); foreach (Transform child in transform.Children) child.gameObject.Collect(result); }
        public T Add<T>(T component) where T : Component { component.gameObject = this; Components.Add(component); return component; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : Component { return gameObject.GetComponentInChildren<T>(includeInactive); }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component { return gameObject.GetComponentsInChildren<T>(includeInactive); }
    }
    public class MonoBehaviour : Component { }
    public class Transform : Object
    {
        public GameObject gameObject;
        public new string name { get { return gameObject != null ? gameObject.name : base.name; } set { if (gameObject != null) gameObject.name = value; else base.name = value; } }
        public readonly List<Transform> Children = new List<Transform>();
        private Transform owner;
        public Transform parent { get { return owner; } set { if (owner != null) owner.Children.Remove(this); owner = value; if (owner != null) owner.Children.Add(this); } }
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
        public Vector3 position
        {
            get { return parent == null ? localPosition : parent.position + Vector3.Scale(localPosition, parent.lossyScale); }
            set { localPosition = parent == null ? value : Vector3.Divide(value - parent.position, parent.lossyScale); }
        }
        public Quaternion rotation { get { return parent == null ? localRotation : parent.rotation * localRotation; } set { localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value; } }
        public Vector3 lossyScale { get { return parent == null ? localScale : Vector3.Scale(localScale, parent.lossyScale); } }
        public Vector3 forward { get { return new Vector3(2 * (rotation.x * rotation.z + rotation.w * rotation.y), 2 * (rotation.y * rotation.z - rotation.w * rotation.x), 1 - 2 * (rotation.x * rotation.x + rotation.y * rotation.y)); } }
        public bool IsChildOf(Transform root) { for (Transform node = this; node != null; node = node.parent) if (node == root) return true; return false; }
    }
    public class Collider : Component { public bool enabled = true; }
    public class HeadCollider : Component { }
    public class Rigidbody : Component
    {
        public bool isKinematic;
        public Vector3 velocity, angularVelocity;
        public Vector3 position { get { return transform.position; } set { transform.position = value; } }
        public Quaternion rotation { get { return transform.rotation; } set { transform.rotation = value; } }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity { get { return new Quaternion(0, 0, 0, 1); } }
        public static Quaternion Inverse(Quaternion q) { return new Quaternion(-q.x, -q.y, -q.z, q.w); }
        public static Quaternion operator *(Quaternion a, Quaternion b) { return new Quaternion(a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w,a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 Scale(Vector3 a, Vector3 b) { return new Vector3(a.x*b.x, a.y*b.y, a.z*b.z); }
        public static Vector3 Divide(Vector3 a, Vector3 b) { return new Vector3(a.x/b.x, a.y/b.y, a.z/b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static float Clamp(float v, float min, float max) { return Math.Max(min, Math.Min(max, v)); }
    }
    public static class Time { public static float time, deltaTime, unscaledTime; }
    public static class Debug { public static readonly List<string> Warnings = new List<string>(); public static void LogWarning(string value) { Warnings.Add(value); } }
    public sealed class WaitForSeconds { public readonly float Seconds; public WaitForSeconds(float value) { Seconds = value; } }
    public struct LayerMask { }
    public enum QueryTriggerInteraction { Ignore }
    public struct RaycastHit { public Vector3 point; }
    public static class Physics
    {
        public static Func<Vector3, Vector3?> Ground;
        public static int Probes, Syncs;
        public static bool ThrowOnSync;
        public static void SyncTransforms() { Syncs++; if (ThrowOnSync) throw new Exception("Physics sync failed"); }
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
namespace ECM2
{
    using UnityEngine;
    public class CharacterMovement : Component
    {
        private Vector3 currentVelocity;
        public ref Vector3 velocity { get { return ref currentVelocity; } }
        public Vector3 PendingForces, PendingImpulses, PendingLaunch;
        public int SafeTeleports, ClearForces;
        public bool ThrowOnSafeTeleport;
        public void ClearAccumulatedForces() { PendingForces = PendingImpulses = PendingLaunch = Vector3.zero; ClearForces++; }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation, bool updateGround)
        { SafeTeleports++; if (ThrowOnSafeTeleport) throw new Exception("ECM sync failed"); transform.position = position; transform.rotation = rotation; }
        public void Advance() { currentVelocity += PendingForces + PendingImpulses + PendingLaunch; transform.position += currentVelocity; }
    }
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
    public class DamageReceiver : Component { }
    public class Movement
    {
        private readonly Transform root;
        public Movement(Transform root) { this.root = root; }
        public bool forceMove;
        public Vector3 forceMoveVelocity, MoveInput, targetAimDirection;
        public Vector3 Velocity { get { return root.gameObject.GetComponent<ECM2.CharacterMovement>().velocity; } set { root.gameObject.GetComponent<ECM2.CharacterMovement>().velocity = value; } }
        public int ForcePositions;
        public void SetMoveInput(Vector3 value) { MoveInput = value; }
        public void ForceSetPosition(Vector3 value) { root.position = value; Velocity = Vector3.zero; ForcePositions++; }
        public void Advance() { Velocity = forceMove ? forceMoveVelocity : MoveInput; forceMove = false; root.position += Velocity; }
    }
    public class CharacterMainControl : MonoBehaviour
    {
        public static CharacterMainControl Main;
        public readonly Health Health = new Health();
        public readonly DamageReceiver mainDamageReceiver;
        public readonly Movement movementControl;
        public int PositionEvents;
        public int Teleports { get { return PositionEvents + Controller.SafeTeleports; } }
        public readonly ECM2.CharacterMovement Controller;
        public bool ThrowOnSetPosition, HealOnTeleport;
        public CharacterMainControl(string name, Vector3 position)
        {
            this.name = name; var go = new GameObject { name = name }; go.Add(this); transform.position = position;
            Controller = go.Add(new ECM2.CharacterMovement());
            movementControl = new Movement(transform);
            var receiver = new GameObject { name = "DamageReceiver" }; receiver.transform.parent = transform;
            mainDamageReceiver = receiver.Add(new DamageReceiver()); receiver.Add(new Collider());
        }
        public void SetPosition(Vector3 position)
        {
            PositionEvents++;
            if (ThrowOnSetPosition) throw new Exception("SetPosition failed");
            movementControl.ForceSetPosition(position);
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
