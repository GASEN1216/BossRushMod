using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object child in go.Components) child.Destroyed = true;
        }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public static implicit operator bool(Object value) { return value != null; }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public sealed class GameObject : Object { internal readonly List<Object> Components = new List<Object>(); }
    public class Component : Object
    {
        public readonly GameObject gameObject;
        public Component() { gameObject = new GameObject(); gameObject.Components.Add(this); }
    }
    public sealed class Transform : Object { public Vector3 position, forward = Vector3.forward; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 down { get { return -up; } }
        public static Vector3 forward { get { return new Vector3(0f, 0f, 1f); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { float m = magnitude; return m > 0.00001f ? this / m : zero; } }
        public void Normalize() { this = normalized; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return zero - a; }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator *(float a, Vector3 b) { return b * a; }
        public static Vector3 operator /(Vector3 a, float b) { return a * (1f / b); }
        public static float Distance(Vector3 a, Vector3 b) { return (a - b).magnitude; }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 ProjectOnPlane(Vector3 a, Vector3 b) { return a - b * (Dot(a, b) / b.sqrMagnitude); }
        public static Vector3 Reflect(Vector3 a, Vector3 b) { return a - 2f * Dot(a, b) * b; }
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float maxDistanceDelta)
        {
            Vector3 delta = b - a;
            return delta.magnitude <= maxDistanceDelta ? b : a + delta.normalized * maxDistanceDelta;
        }
        public override string ToString() { return "(" + x + "," + y + "," + z + ")"; }
    }
    public static class Mathf
    {
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Abs(float a) { return Math.Abs(a); }
        public static float Clamp01(float a) { return Math.Min(1f, Math.Max(0f, a)); }
    }
    public enum QueryTriggerInteraction { Ignore, Collide }
    public struct Bounds { public Vector3 center; }
    public sealed class Collider : Component
    {
        public Bounds bounds;
        public DamageReceiver Receiver;
        public T GetComponent<T>() where T : class { return Receiver as T; }
    }
    public struct RaycastHit { public Vector3 point, normal; public float distance; public Collider collider; }
    public sealed class PlaneWall
    {
        public Vector3 Point, Normal;
        public float Side(Vector3 point) { return Vector3.Dot(point - Point, Normal); }
    }
    public struct RayRecord { public Vector3 Origin, Direction; public float Distance; }
    public static class Physics
    {
        public static readonly List<PlaneWall> Walls = new List<PlaneWall>();
        public static readonly List<Collider> Targets = new List<Collider>();
        public static readonly List<RayRecord> Rays = new List<RayRecord>();
        public static bool RecoilBlocked, GroundAvailable;
        public static void Reset() { Walls.Clear(); Targets.Clear(); Rays.Clear(); RecoilBlocked = false; GroundAvailable = true; }
        private static bool Intersect(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = new RaycastHit();
            bool found = false;
            foreach (PlaneWall wall in Walls)
            {
                float denominator = Vector3.Dot(wall.Normal, direction);
                if (Math.Abs(denominator) < 0.00001f) continue;
                float t = -wall.Side(origin) / denominator;
                if (t <= 0.00001f || t > distance || (found && t >= hit.distance)) continue;
                found = true;
                hit = new RaycastHit { distance = t, point = origin + direction * t,
                    normal = denominator < 0f ? wall.Normal : -wall.Normal };
            }
            return found;
        }
        public static bool Linecast(Vector3 from, Vector3 to, out RaycastHit hit, int mask, QueryTriggerInteraction query)
        { return Intersect(from, (to - from).normalized, Vector3.Distance(from, to), out hit); }
        public static bool Linecast(Vector3 from, Vector3 to, int mask, QueryTriggerInteraction query)
        { RaycastHit hit; return Linecast(from, to, out hit, mask, query); }
        public static bool Raycast(Vector3 from, Vector3 direction, out RaycastHit hit, float distance, int mask, QueryTriggerInteraction query)
        {
            if (mask == 2)
            {
                hit = new RaycastHit { point = new Vector3(from.x, 0f, from.z), normal = Vector3.up };
                return GroundAvailable;
            }
            Rays.Add(new RayRecord { Origin = from, Direction = direction, Distance = distance });
            return Intersect(from, direction, distance, out hit);
        }
        public static bool SphereCast(Vector3 from, float radius, Vector3 direction, out RaycastHit hit,
            float distance, int mask, QueryTriggerInteraction query) { hit = new RaycastHit(); return RecoilBlocked; }
        public static RaycastHit[] SphereCastAll(Vector3 from, float radius, Vector3 direction,
            float distance, int mask, QueryTriggerInteraction query)
        {
            var result = new List<RaycastHit>();
            foreach (Collider target in Targets)
            {
                float t = Vector3.Dot(target.bounds.center - from, direction);
                if (t < 0f || t > distance || Vector3.Distance(from + direction * t, target.bounds.center) > radius) continue;
                result.Add(new RaycastHit { collider = target, distance = t, point = target.bounds.center });
            }
            return result.ToArray();
        }
    }
}
namespace UnityEngine.SceneManagement { public struct Scene { } }
public enum Teams { player, wolf }
public static class Team { public static bool IsEnemy(Teams a, Teams b) { return a != b; } }
public enum DamageTypes { normal }
public sealed class DamageInfo
{
    public float damageValue;
    public DamageTypes damageType;
    public bool isFromBuffOrEffect, isExplosion;
    public Vector3 damagePoint, damageNormal;
    public DamageInfo(CharacterMainControl attacker) { }
}
public sealed class Health : Component
{
    public bool IsMainCharacterHealth;
    public float CurrentHealth = 1000f;
    public bool IsDead { get { return CurrentHealth <= 0f; } }
}
public sealed class HealthSimpleBase : Component { public float HealthValue = 1000f; }
public sealed class DamageReceiver : Component
{
    public bool useSimpleHealth;
    public Health health;
    public HealthSimpleBase simpleHealth;
    public Teams Team;
    public void Hurt(DamageInfo info) { if (useSimpleHealth) simpleHealth.HealthValue -= info.damageValue; else health.CurrentHealth -= info.damageValue; }
}
public sealed class CharacterMainControl : Component
{
    public readonly Transform transform = new Transform();
    public GunBase CurrentHoldItemAgent;
    public float CharacterMoveability = 1f;
    public Vector3 Aim, ForceVelocity;
    public Teams Team;
    public bool Swimming;
    public Vector3 GetCurrentAimPoint() { return Aim; }
    public void SetForceMoveVelocity(Vector3 value) { ForceVelocity = value; }
}
public class GunBase : Component
{
    public CharacterMainControl Holder;
    public Transform muzzle = new Transform();
}
public sealed class ItemAgent_Gun : GunBase { }
public static class GameplayDataSettings
{
    public static class Layers { public const int wallLayerMask = 1, groundLayerMask = 2, damageReceiverLayerMask = 4; }
}
namespace BossRush
{
    internal sealed partial class DangerousPrototypeRuntimeModule
    {
        private object owner;
        private CharacterMainControl borrower;
        private ItemAgent_Gun borrowedGun;
        private DangerousPrototypeVisuals visuals;
        private DangerousPrototypeRules rules;
        private int generation;
        private float recoilTime;
        private Vector3 recoilDirection;
        private float recoilSpeed = 6f;
        // 轨道炮 / 雷霆 / 烟花另有出手路径；本夹具只验证射向、墙体与后坐合同，这里只记录分发。
        internal int SpecialShots;
        private void FireSpecial(DangerousPrototypeKind kind, Vector3 from, Vector3 direction) { SpecialShots++; }
        private int WallMask { get { return GameplayDataSettings.Layers.wallLayerMask | GameplayDataSettings.Layers.groundLayerMask; } }
        internal DangerousPrototypeRuntimeModule(CharacterMainControl player, ItemAgent_Gun gun, DangerousPrototypeKind kind)
        {
            owner = new object(); borrower = player; borrowedGun = gun;
            rules = new DangerousPrototypeRules(); rules.Borrow(kind);
            FieldAdventureSupport.Player = player;
        }
        internal void Fire(ItemAgent_Gun gun) { OnShot(gun); }
        internal void Recoil(float dt) { TickRecoil(dt); }
        internal int Progress { get { return rules.Progress; } }
        internal Vector3 RecoilDirection { get { return recoilDirection; } }
        private void EndLease(bool cancel) { if (cancel) rules.Cancel(); }
    }
    internal sealed class DangerousPrototypeVisuals
    {
        internal void Fire() { }
        internal void Beam(Vector3 from, Vector3 end, bool jet, int bounce) { }
        internal void Impact(Vector3 point, Vector3 normal, int bounce) { }
    }
    internal static class FieldAdventureSupport
    {
        internal static CharacterMainControl Player;
        internal static bool Allowed = true;
        internal static bool TryGetRaid(object owner, out CharacterMainControl player, out UnityEngine.SceneManagement.Scene scene)
        { player = Player; scene = new UnityEngine.SceneManagement.Scene(); return Allowed; }
        internal static void Notify(string cn, string en) { }
    }
    internal static class BossRushUI { internal static bool Paused; internal static bool IsGamePaused() { return Paused; } }
    internal static class PetNestCompanionAgent { internal static bool IsCompanionHealth(Health value) { return false; } }
    internal static class ModBehaviour
    {
        internal static readonly List<string> Errors = new List<string>();
        internal static void DevLog(string message) { Errors.Add(message); }
    }
}
