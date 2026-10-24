using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static implicit operator bool(Object value) { return value != null; }
        public static bool operator !(Object value) { return value == null; }
        public static bool operator ==(Object left, Object right)
        {
            bool a = ReferenceEquals(left, null) || left.Destroyed, b = ReferenceEquals(right, null) || right.Destroyed;
            return a || b ? a == b : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            GameObject root = value as GameObject;
            if (root != null)
            {
                foreach (Transform child in root.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (Component component in root.Components.ToArray())
                {
                    MethodInfo teardown = component.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (teardown != null) teardown.Invoke(component, null);
                    component.Destroyed = true;
                }
            }
            value.Destroyed = true;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        internal bool Awakened;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public readonly Transform transform;
        internal readonly List<Component> Components = new List<Component>();
        public bool activeSelf = true;
        public int layer;
        public GameObject(string name = "") { transform = new Transform { gameObject = this }; }
        public void SetActive(bool active)
        {
            activeSelf = active;
            if (active)
                foreach (Component component in Components.ToArray()) Awake(component);
        }
        private void Awake(Component component)
        {
            if (component.Awakened || !activeSelf) return;
            component.Awakened = true;
            MethodInfo awake = component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            if (awake != null) awake.Invoke(component, null);
        }
        public T AddComponent<T>() where T : Component, new()
        { var component = new T { gameObject = this }; Components.Add(component); Awake(component); return component; }
        public T GetComponent<T>() where T : class
        { foreach (Component component in Components) if (component is T) return component as T; return null; }
    }
    public class Transform
    {
        public GameObject gameObject;
        public Vector3 position, localPosition;
        public Quaternion rotation;
        public Vector3 TransformPoint(Vector3 point) { return position + rotation * point; }
        internal readonly List<Transform> Children = new List<Transform>();
        private Transform parent;
        public void SetParent(Transform next, bool worldPositionStays)
        { if (parent != null) parent.Children.Remove(this); parent = next; if (next != null) next.Children.Add(this); }
    }
    public class Collider : Component { }
    public class BoxCollider : Collider { public Vector3 size, center; }
    public class SphereCollider : Collider { public float radius; public bool isTrigger; }
    public class Rigidbody : Component { public bool isKinematic, useGravity; public Vector3 position; public Quaternion rotation; }
    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 value, Vector3 extent) { center = value; size = extent; }
        public void Encapsulate(Bounds other)
        {
            Vector3 low = center - size * .5f, high = center + size * .5f;
            Vector3 otherLow = other.center - other.size * .5f, otherHigh = other.center + other.size * .5f;
            low = new Vector3(Math.Min(low.x, otherLow.x), Math.Min(low.y, otherLow.y), Math.Min(low.z, otherLow.z));
            high = new Vector3(Math.Max(high.x, otherHigh.x), Math.Max(high.y, otherHigh.y), Math.Max(high.z, otherHigh.z));
            center = (low + high) * .5f; size = high - low;
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public Vector3 normalized { get { float n = (float)Math.Sqrt(sqrMagnitude); return n == 0 ? zero : this * (1 / n); } }
        public void Normalize() { this = normalized; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return a + (b - a) * t; }
    }
    public struct Quaternion
    {
        private float angle;
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion { angle = y * (float)Math.PI / 180f }; }
        public static Quaternion LookRotation(Vector3 forward) { return new Quaternion { angle = (float)Math.Atan2(forward.x, forward.z) }; }
        public static Vector3 operator *(Quaternion q, Vector3 v)
        { float c = (float)Math.Cos(q.angle), s = (float)Math.Sin(q.angle); return new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c); }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Abs(float f) { return Math.Abs(f); }
        public const float PI = (float)Math.PI;
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Sin(float f) { return (float)Math.Sin(f); }
    }
    public static class LayerMask { public static int NameToLayer(string name) { return 8; } }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public static bool Blocked;
        public static void SyncTransforms() { }
        public static void IgnoreCollision(Collider a, Collider b, bool ignore) { }
        public static bool Linecast(Vector3 a, Vector3 b, int mask, QueryTriggerInteraction q) { return Blocked; }
    }
    public static class Debug { public static void LogWarning(object value) { Console.WriteLine(value); } }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction<T>(T value);
    public class UnityEvent<T>
    {
        private event UnityAction<T> handlers;
        public void AddListener(UnityAction<T> action) { handlers += action; }
        public void RemoveListener(UnityAction<T> action) { handlers -= action; }
        public void Invoke(T value) { if (handlers != null) handlers(value); }
    }
}
public static class Random { public static float Range(float a, float b) { return 0.5f; } }
namespace UnityEngine.SceneManagement { public struct Scene { public int handle; } }
namespace Duckov.Utilities { public static class GameplayDataSettings { public static class Layers { public static int wallLayerMask = 1, groundLayerMask = 2; } } }
namespace Duckov.Economy { public static class EconomyManager { public const int CashItemID = 451; } }
namespace Duckov.Buffs { public class Buff { } }
public enum Teams { wolf, player, all }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public DamageReceiver toDamageReceiver;
    public float damageValue, critRate, critDamageFactor;
    public int crit;
    public bool isExplosion, isFromBuffOrEffect, ignoreDifficulty;
    public UnityEngine.Vector3 damagePoint, damageNormal;
    public DamageInfo(CharacterMainControl player) { this = default(DamageInfo); fromCharacter = player; critDamageFactor = 1; crit = -1; }
}
public class Health : UnityEngine.Component
{
    public bool IsDead, IsMainCharacterHealth;
    public Teams team;
    public int DamageCalls;
    public float LastDamage;
    public readonly UnityEngine.Events.UnityEvent<DamageInfo> OnDeadEvent = new UnityEngine.Events.UnityEvent<DamageInfo>();
    public bool Hurt(DamageInfo info) { DamageCalls++; LastDamage = info.damageValue; return true; }
    public CharacterMainControl TryGetCharacter() { return GetComponent<CharacterMainControl>(); }
}
public class CharacterMainControl : UnityEngine.Component
{
    public static CharacterMainControl Main;
    public bool IsMainCharacter, isVehicle, protectVehicleDriver;
    public Health Health;
    public void AddBuff(Duckov.Buffs.Buff buff, CharacterMainControl source, int value) { }
}
public class LevelManager { public static LevelManager Instance = new LevelManager(); public CharacterMainControl ControllingCharacter; }
public class InteractableBase : UnityEngine.Component { }
namespace ItemStatsSystem { public class Item { public int TypeID; public string DisplayName; } }
namespace BossRush.Utils
{
    public static class NPCInteractionGroupHelper
    {
        public static List<InteractableBase> PrepareGroupedInteractionOwner(InteractableBase owner, string label) { return new List<InteractableBase>(); }
        public static T AddSubInteractable<T>(UnityEngine.Transform parent, string name, List<InteractableBase> group, Action<T> bind) where T : InteractableBase, new()
        { var root = new UnityEngine.GameObject(name); root.transform.SetParent(parent, false); T value = root.AddComponent<T>(); bind(value); group.Add(value); return value; }
    }
}
namespace BossRush
{
    public class ModBehaviour { }
    // 地形只返回可控落点；真实射线 / 胶囊净空由 Unity 侧另验。
    internal static class GreedyMimicTerrain
    {
        public static bool RejectLanding, WallBlocksShells;
        public static bool TryLanding(UnityEngine.Vector3 from, UnityEngine.Vector3 target, float radius, float maxLeap, out UnityEngine.Vector3 landing)
        {
            UnityEngine.Vector3 delta = target - from; delta.y = 0;
            float distance = (float)Math.Sqrt(delta.sqrMagnitude);
            landing = from + delta.normalized * Math.Min(distance, maxLeap);
            return !RejectLanding && distance >= 0.5f;
        }
        public static bool SegmentBlocked(UnityEngine.Vector3 from, UnityEngine.Vector3 to) { return WallBlocksShells; }
    }
    internal class GreedyMimicVisual : IDisposable
    {
        internal const int ShellCapacity = 10;
        public int Slams, Explosions, ShellPops, Muzzles, Heals, LeapFrames, LaneShows, CircleShows;
        public readonly bool[] ShellVisible = new bool[ShellCapacity];
        public void SetLeap(float progress) { if (progress >= 0f) LeapFrames++; }
        public void SetShell(int index, UnityEngine.Vector3 position, bool visible) { ShellVisible[index] = visible; }
        public void ShellPop(UnityEngine.Vector3 position) { ShellPops++; }
        public void Muzzle() { Muzzles++; }
        public void Slam(UnityEngine.Vector3 position, float radius) { Slams++; }
        public void Explode(UnityEngine.Vector3 position, float radius) { Explosions++; }
        public void Heal(UnityEngine.Vector3 position) { Heals++; }
        public readonly UnityEngine.GameObject Root = new UnityEngine.GameObject();
        public readonly UnityEngine.Transform Facing;
        public UnityEngine.Vector3 TongueTarget;
        public int TongueStrikes, AttackClears;
        private float scale = 1f;
        internal GreedyMimicVisual(UnityEngine.SceneManagement.Scene scene, UnityEngine.Vector3 position)
        { Root.transform.position = position; Facing = new UnityEngine.GameObject().transform; Facing.SetParent(Root.transform, false); }
        public void SyncHudVisibility() { }
        public void Grow(GreedyMimicRules rules) { scale = rules.Scale; }
        // 仅满足 Encounter 的装配接口；真实动画包络与物理发布由原生 FieldAdventureVisuals 验证。
        public void GetDamageBounds(out UnityEngine.Bounds bodyBounds, out UnityEngine.Bounds lidBounds)
        {
            bodyBounds = new UnityEngine.Bounds(new UnityEngine.Vector3(0, 1.35f * scale, 0), new UnityEngine.Vector3(2.8f, 2.5f, 2f) * scale);
            lidBounds = bodyBounds;
        }
        public void Tick(float dt, UnityEngine.Vector3 player, bool fighting, float health, float warning, GreedyMimicAttack attack) { }
        public void ClearAttack() { AttackClears++; }
        public void Burst(UnityEngine.Vector3 position, float duration) { }
        public void AnimateBomb(int index, UnityEngine.Vector3 origin, UnityEngine.Vector3 target, float progress) { }
        public void ShowCircle(int index, UnityEngine.Vector3 position, float radius, bool healing) { CircleShows++; }
        public void ShowLane(int index, UnityEngine.Vector3 start, UnityEngine.Vector3 end) { LaneShows++; }
        public void StrikeTongue(UnityEngine.Vector3 target) { TongueTarget = target; TongueStrikes++; }
        public void Defeated() { }
        public void Dispose() { UnityEngine.Object.Destroy(Root); }
    }
    internal class GreedyMimicInteractable : InteractableBase { public void Bind(GreedyMimicEncounter encounter, int action) { } public void RefreshName() { } }
    internal static class FieldAdventureSupport
    {
        public static readonly List<string> Notices = new List<string>();
        public static bool TryGetRaid(ModBehaviour owner, out CharacterMainControl player, out UnityEngine.SceneManagement.Scene scene)
        { player = CharacterMainControl.Main; scene = default(UnityEngine.SceneManagement.Scene); return true; }
        public static void Notify(string cn, string en) { Notices.Add(en); }
    }
    internal static class GreedyMimicInventory
    {
        public static ItemStatsSystem.Item Find(CharacterMainControl player, GreedyMimicFood food, out int slot) { slot = 0; return null; }
        public static bool Consume(ItemStatsSystem.Item item, CharacterMainControl player, int slot, GreedyMimicFood food, Action grow) { return false; }
        public static void Reward(int id, int count, UnityEngine.Vector3 position) { }
    }
    internal static class L10n { public static string T(string cn, string en) { return en; } }
    internal static class BossRushConfirmDialog
    {
        public class Options { public string Title, Target, Body, Warning, ConfirmLabel, CancelLabel; public UnityEngine.GameObject Anchor; public Action OnConfirm; }
        public static void Show(Options options) { }
    }
}
