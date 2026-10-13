using System;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem.Items;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        private static int nextId;
        private readonly int id = ++nextId;
        internal bool Destroyed;
        public int GetInstanceID() { return id; }
        public static bool operator ==(Object a, Object b)
        {
            bool absentA = ReferenceEquals(a, null) || a.Destroyed;
            bool absentB = ReferenceEquals(b, null) || b.Destroyed;
            return absentA || absentB ? absentA == absentB : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return id; }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null) || value.Destroyed) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null))
            {
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (Component component in go.Components.ToArray()) Destroy(component);
            }
            Component item = value as Component;
            if (!ReferenceEquals(item, null))
            {
                MethodInfo method = item.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                if (method != null) method.Invoke(item, null);
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component
    {
        public Vector3 position, localScale;
        internal readonly List<Transform> Children = new List<Transform>();
        internal Transform Parent;
        public void SetParent(Transform parent, bool worldPositionStays)
        {
            if (Parent != null) Parent.Children.Remove(this);
            Parent = parent;
            if (parent != null) parent.Children.Add(this);
        }
    }
    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition;
        public Rect rect = new Rect { width = 1920f, height = 1080f };
    }
    public sealed class GameObject : Object
    {
        internal static readonly List<GameObject> All = new List<GameObject>();
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        internal bool Active = true;
        internal readonly string Name;
        public GameObject(string name, params Type[] types)
        {
            Name = name;
            bool rect = Array.IndexOf(types, typeof(RectTransform)) >= 0;
            transform = rect ? new RectTransform() : new Transform();
            transform.gameObject = this;
            Components.Add(transform);
            All.Add(this);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : class
        {
            foreach (Component value in Components)
                if (!value.Destroyed && value is T) return value as T;
            return null;
        }
        public void SetActive(bool active) { Active = active; }
        internal static void DestroyScene()
        {
            foreach (GameObject go in All.ToArray()) Destroy(go);
            All.Clear();
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public bool Equals(Vector2 other) { return x == other.x && y == other.y; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public bool Equals(Vector3 other) { return x == other.x && y == other.y && z == other.z; }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 one { get { return new Vector3(1f, 1f, 1f); } }
        public Vector3 normalized
        {
            get { float length = (float)Math.Sqrt(x * x + y * y + z * z); return length > 0f ? this * (1f / length) : new Vector3(); }
        }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public struct Rect { public float width, height; }
    public struct Bounds { public Vector3 center; }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public sealed class Sprite : Object { }
    public sealed class Canvas : Component { public bool enabled; }
    public sealed class Camera : Object
    {
        public static Camera main;
        internal Vector3 Projection = new Vector3(0.5f, 0.5f, 1f);
        public Vector3 WorldToViewportPoint(Vector3 position) { return Projection; }
    }
    public class Collider : Component { public Bounds bounds; }
    public enum QueryTriggerInteraction { Ignore, Collide }
    public static class Physics
    {
        internal static Collider[] Hits = new Collider[0];
        internal static int Scans;
        internal static Func<Vector3, Vector3, bool> Blocked;
        public static Collider[] OverlapSphere(Vector3 center, float radius, int mask, QueryTriggerInteraction trigger)
        { Scans++; return Hits; }
        public static bool Linecast(Vector3 from, Vector3 to, int mask, QueryTriggerInteraction trigger)
        { return Blocked != null && Blocked(from, to); }
    }
    public static class Time { public static float time, deltaTime; }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
        public static float Clamp01(float value) { return Math.Max(0f, Math.Min(1f, value)); }
        public static int CeilToInt(float value) { return (int)Math.Ceiling(value); }
        public static float Sin(float value) { return (float)Math.Sin(value); }
    }
}
namespace UnityEngine.UI
{
    public sealed class Image : Component
    {
        public enum Type { Filled }
        public enum FillMethod { Radial360 }
        public Sprite sprite;
        public Color color;
        public bool preserveAspect, raycastTarget, fillClockwise;
        public int fillOrigin;
        public float fillAmount;
        public Type type;
        public FillMethod fillMethod;
        public RectTransform rectTransform { get { return (RectTransform)transform; } }
    }
}
namespace TMPro
{
    public enum FontStyles { Bold }
    public enum TextAlignmentOptions { Center }
    public sealed class TextMeshProUGUI : Component
    {
        public RectTransform rectTransform { get { return (RectTransform)transform; } }
        public float fontSize;
        public FontStyles fontStyle;
        public TextAlignmentOptions alignment;
        public Color color;
        public bool raycastTarget;
        public string text;
    }
}
namespace ItemStatsSystem.Items { public sealed class Slot { } }
public enum Teams { player, wolf, red, blue, middle, all }
public static class Team
{
    public static bool IsEnemy(Teams self, Teams target)
    { return self != Teams.middle && (self == Teams.all || (target != Teams.middle && self != target)); }
}
public enum DamageTypes { normal }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public float finalDamage, damageValue;
    public DamageTypes damageType;
    public bool isFromBuffOrEffect, isExplosion;
    public int fromWeaponItemID;
    public Vector3 damagePoint, damageNormal;
    public DamageInfo(CharacterMainControl source) { this = default(DamageInfo); fromCharacter = source; }
}
public class DuckovItemAgent : Component { }
public sealed class CharacterMainControl : Component
{
    public static CharacterMainControl Main;
    public static event Action<CharacterMainControl, DuckovItemAgent> OnMainCharacterChangeHoldItemAgentEvent;
    public static event Action<CharacterMainControl, Slot> OnMainCharacterSlotContentChangedEvent;
    public Health Health;
    public Teams Team;
    public bool IsMainCharacter { get { return ReferenceEquals(this, Main); } }
    internal static int HoldSubscribers { get { return Count(OnMainCharacterChangeHoldItemAgentEvent); } }
    internal static int SlotSubscribers { get { return Count(OnMainCharacterSlotContentChangedEvent); } }
    internal void EquipmentChanged() { if (OnMainCharacterSlotContentChangedEvent != null) OnMainCharacterSlotContentChangedEvent(this, new Slot()); }
    internal void HoldChanged() { if (OnMainCharacterChangeHoldItemAgentEvent != null) OnMainCharacterChangeHoldItemAgentEvent(this, null); }
    internal static int Count(Delegate value) { return value == null ? 0 : value.GetInvocationList().Length; }
}
public sealed class Health : Component
{
    public static event Action<Health, DamageInfo> OnHurt;
    public static event Action<Health, DamageInfo> OnDead;
    public CharacterMainControl Character;
    public bool IsDead;
    public float healthBarHeight = 2f;
    public bool IsMainCharacterHealth { get { return Character != null && Character.IsMainCharacter; } }
    internal readonly List<DamageInfo> Hits = new List<DamageInfo>();
    internal Action<DamageInfo> OnReceive;
    internal static int HurtSubscribers { get { return CharacterMainControl.Count(OnHurt); } }
    internal static int DeathSubscribers { get { return CharacterMainControl.Count(OnDead); } }
    public CharacterMainControl TryGetCharacter() { return Character; }
    public bool Hurt(DamageInfo info)
    {
        if (IsDead) return false;
        info.finalDamage = info.damageValue;
        Hits.Add(info);
        if (OnReceive != null) OnReceive(info);
        if (OnHurt != null) OnHurt(this, info);
        return true;
    }
    internal void DirectHit(CharacterMainControl from, float amount, bool kill = false, bool effect = false)
    {
        DamageInfo info = new DamageInfo(from) { damageValue = amount, finalDamage = amount, isFromBuffOrEffect = effect };
        if (kill) Die(info);
        if (OnHurt != null) OnHurt(this, info);
    }
    internal void Die(DamageInfo info)
    {
        IsDead = true;
        if (OnDead != null) OnDead(this, info);
    }
}
public sealed class DamageReceiver : Component { public Health health; }
public sealed class SceneLoadingContext { }
public static class SceneLoader
{
    public static bool IsSceneLoading;
    public static event Action<SceneLoadingContext> onStartedLoadingScene;
    public static event Action<SceneLoadingContext> onFinishedLoadingScene;
    internal static int Subscribers { get { return CharacterMainControl.Count(onStartedLoadingScene); } }
    internal static int FinishSubscribers { get { return CharacterMainControl.Count(onFinishedLoadingScene); } }
    internal static void Begin() { IsSceneLoading = true; if (onStartedLoadingScene != null) onStartedLoadingScene(new SceneLoadingContext()); }
    internal static void Finish() { IsSceneLoading = false; if (onFinishedLoadingScene != null) onFinishedLoadingScene(new SceneLoadingContext()); }
}
public sealed class LevelManager
{
    public static LevelManager Instance;
    public static event Action OnLevelBeginInitializing;
    public static event Action OnAfterLevelInitialized;
    public bool IsBaseLevel;
    internal static int BeginSubscribers { get { return CharacterMainControl.Count(OnLevelBeginInitializing); } }
    internal static int EndSubscribers { get { return CharacterMainControl.Count(OnAfterLevelInitialized); } }
    internal static void Begin() { if (OnLevelBeginInitializing != null) OnLevelBeginInitializing(); }
    internal static void Finish() { if (OnAfterLevelInitialized != null) OnAfterLevelInitialized(); }
}
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static class Layers
        {
            public const int damageReceiverLayerMask = 1, wallLayerMask = 2, groundLayerMask = 4;
        }
    }
}
namespace BossRush
{
    internal static class ModBehaviour
    {
        internal static readonly List<string> Logs = new List<string>();
        internal static void DevLog(string value) { Logs.Add(value); }
    }
    internal static class BossRushUI
    {
        internal static bool Paused, Hidden;
        internal static int CanvasCreated;
        internal static bool IsGamePaused() { return Paused; }
        internal static bool IsOfficialHudHidden() { return Hidden; }
        internal static Canvas CreateCanvasRoot(string name, int layer, bool raycaster)
        { CanvasCreated++; return new GameObject(name, typeof(RectTransform)).AddComponent<Canvas>(); }
        internal static void ApplyGameFont(TMPro.TextMeshProUGUI text) { }
        internal static float EaseOut(float t) { return 1f - (1f - t) * (1f - t); }
    }
    internal static class BossRushUILayers { internal const int WorldOverlay = 240; }
    internal static class BossRushUIColors
    {
        internal static readonly Color WarningText = new Color(1f, 0.7f, 0f);
        internal static readonly Color TextPrimary = new Color(1f, 1f, 1f);
        internal static readonly Color Danger = new Color(1f, 0f, 0f);
    }
    internal static class BossRushUIKit { internal static void ApplyWorldTextOutline(TMPro.TextMeshProUGUI text) { } }
    internal static class ProductionIconCache { internal static Sprite Get(string path) { return new Sprite(); } }
    internal static class BossRushFxKit { internal static Sprite GetSoftCircleSprite() { return new Sprite(); } }
    internal static class BossRushProceduralSprites { internal static Sprite GetRingSprite() { return new Sprite(); } }
    internal static class EmptyMagazineMineFx
    {
        internal static int Explosions;
        internal static Vector3 LastPosition;
        internal static void PlayExplosion(Vector3 position, float radius) { Explosions++; LastPosition = position; }
    }
    internal static class NewWeaponEquipState
    {
        internal static bool Equipped;
        internal static bool IsTotemEquipped(int type) { return Equipped && type == BossRushItemIds.PassTheBomb; }
        internal static void MarkDirty() { }
    }
    internal static class PetNestCompanionAgent
    {
        internal static readonly HashSet<Health> Companions = new HashSet<Health>();
        internal static bool IsCompanionHealth(Health health) { return Companions.Contains(health); }
    }
}
