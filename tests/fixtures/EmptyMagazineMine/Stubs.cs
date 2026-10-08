using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using ItemStatsSystem.Items;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        private static int nextId;
        private readonly int instanceId = ++nextId;
        internal bool Destroyed;
        public int GetInstanceID() { return instanceId; }
        public static bool operator ==(Object a, Object b)
        {
            bool absentA = ReferenceEquals(a, null) || a.Destroyed;
            bool absentB = ReferenceEquals(b, null) || b.Destroyed;
            return absentA || absentB ? absentA == absentB : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return instanceId; }

        public static T Instantiate<T>(T source) where T : Object
        {
            Texture2D texture = source as Texture2D;
            if (texture == null) throw new InvalidOperationException("fixture only clones textures");
            Texture2D.CloneCalls++;
            return new Texture2D(texture.width, texture.height, texture.format, false)
            {
                isReadable = texture.isReadable,
                Pixels = texture.Pixels == null ? null : (Color32[])texture.Pixels.Clone()
            } as T;
        }

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
                MethodInfo destroy = item.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                if (destroy != null) destroy.Invoke(item, null);
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
    public enum HideFlags { DontSave }
    public enum FilterMode { Bilinear }
    public enum TextureWrapMode { Clamp }
    public enum TextureFormat { RGBA32, BC7 }
    public enum SpriteMeshType { FullRect }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Vector4 { public float x, y, z, w; }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    public sealed class Texture2D : Object
    {
        internal static int CloneCalls;
        internal static readonly List<Texture2D> All = new List<Texture2D>();
        public string name;
        public HideFlags hideFlags;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public readonly int width, height;
        public readonly TextureFormat format;
        public bool isReadable = true;
        internal Color32[] Pixels;
        public Texture2D(int width, int height, TextureFormat format, bool mipmap)
        { this.width = width; this.height = height; this.format = format; All.Add(this); }
        public void SetPixels32(Color32[] pixels)
        {
            if (!isReadable) throw new InvalidOperationException("unreadable texture");
            Pixels = (Color32[])pixels.Clone();
        }
        public void Apply(bool mipmaps, bool makeNoLongerReadable) { isReadable = !makeNoLongerReadable; }
    }
    public sealed class Sprite : Object
    {
        internal static int CreateCalls;
        internal static bool FailNextCreate;
        public Texture2D texture;
        public Rect rect;
        public Vector2 pivot;
        public Vector4 border;
        public float pixelsPerUnit;
        public HideFlags hideFlags;
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float ppu,
            uint extrude = 0, SpriteMeshType mesh = SpriteMeshType.FullRect, Vector4 border = default(Vector4))
        {
            CreateCalls++;
            if (FailNextCreate) { FailNextCreate = false; throw new InvalidOperationException("injected Sprite allocation failure"); }
            if (texture == null) throw new InvalidOperationException("destroyed texture");
            return new Sprite { texture = texture, rect = rect, pivot = new Vector2(pivot.x * rect.width, pivot.y * rect.height),
                pixelsPerUnit = ppu, border = border };
        }
    }
    public sealed class GameObject : Object
    {
        internal static readonly List<GameObject> All = new List<GameObject>();
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        internal readonly string Name;
        internal bool Active = true;
        public GameObject(string name)
        {
            Name = name;
            transform = new Transform { gameObject = this };
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
            foreach (Component component in Components)
                if (!component.Destroyed && component is T) return component as T;
            return null;
        }
        public void SetActive(bool active) { Active = active; }
        internal static void DestroyScene()
        {
            foreach (GameObject go in All.ToArray()) Destroy(go);
            All.Clear();
        }
    }
    public sealed class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();
        private Transform parent;
        public Vector3 position, eulerAngles;
        public void SetParent(Transform value, bool keepWorld)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = value;
            if (parent != null) parent.Children.Add(this);
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 down { get { return new Vector3(0f, -1f, 0f); } }
        public Vector3 normalized
        {
            get { float length = (float)Math.Sqrt(x * x + y * y + z * z); return length > 0f ? this * (1f / length) : new Vector3(); }
        }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float amount) { return new Vector3(a.x * amount, a.y * amount, a.z * amount); }
    }
    public struct Bounds { public Vector3 center; }
    public struct RaycastHit { public Vector3 point; }
    public class Collider : Component { public Bounds bounds; }
    public enum QueryTriggerInteraction { Ignore, Collide }
    public static class Time { public static float time, deltaTime; }
    public static class Physics
    {
        internal static Collider[] Hits = new Collider[0];
        internal static Func<Vector3, bool> IsBlocked;
        internal static int OverlapCalls;
        internal static float LastRadius;
        internal static int LastWallMask;
        public static bool Raycast(Vector3 from, Vector3 direction, out RaycastHit hit, float distance, int mask, QueryTriggerInteraction query)
        {
            hit = new RaycastHit { point = new Vector3(from.x, 0f, from.z) };
            return true;
        }
        public static Collider[] OverlapSphere(Vector3 center, float radius, int mask, QueryTriggerInteraction query)
        {
            OverlapCalls++;
            LastRadius = radius;
            return (Collider[])Hits.Clone();
        }
        public static bool Linecast(Vector3 from, Vector3 to, int mask, QueryTriggerInteraction query)
        {
            LastWallMask = mask;
            return IsBlocked != null && IsBlocked(to);
        }
        internal static void Reset()
        {
            Hits = new Collider[0]; IsBlocked = null; OverlapCalls = 0; LastRadius = 0f; LastWallMask = 0;
        }
    }
}

namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static class Layers
        {
            public const int wallLayerMask = 1;
            public const int groundLayerMask = 2;
            public const int damageReceiverLayerMask = 4;
        }
    }
}
namespace ItemStatsSystem.Items { public sealed class Slot { } }
namespace ItemStatsSystem
{
    public sealed class Item : MonoBehaviour
    {
        public Sprite Icon;
        public string DisplayNameRaw;
    }
}
public enum Teams { player, wolf, red, blue, middle, all }
public static class Team
{
    // 官方 Team.IsEnemy 的受控替身，保留 middle 与当前自队阵营语义。
    public static bool IsEnemy(Teams source, Teams target)
    {
        return source != Teams.middle && (source == Teams.all || (target != Teams.middle && source != target));
    }
}
public enum DamageTypes { normal }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public float damageValue;
    public DamageTypes damageType;
    public bool isExplosion, isFromBuffOrEffect;
    public int fromWeaponItemID;
    public Vector3 damagePoint, damageNormal;
    public DamageInfo(CharacterMainControl source) { this = default(DamageInfo); fromCharacter = source; }
}
public class CharacterActionBase { }
public sealed class CA_Reload : CharacterActionBase { }
public class DuckovItemAgent : Component { }
public sealed class ItemAgent_Gun : DuckovItemAgent
{
    public static event Action<ItemAgent_Gun> OnMainCharacterShootEvent;
    public event Action OnLoadedEvent;
    public CharacterMainControl Holder;
    public int BulletCount;
    internal int PelletsEmitted;
    internal static int ShootSubscribers { get { return OnMainCharacterShootEvent == null ? 0 : OnMainCharacterShootEvent.GetInvocationList().Length; } }
    internal int LoadedSubscribers { get { return OnLoadedEvent == null ? 0 : OnLoadedEvent.GetInvocationList().Length; } }
    internal void Fire(int pellets = 1)
    {
        if (BulletCount <= 0) throw new InvalidOperationException("fixture tried firing an empty gun");
        BulletCount--;
        PelletsEmitted += pellets;
        // 官方事件在扣弹后触发，一次开火只发一次，与本发弹丸数量无关。
        if (OnMainCharacterShootEvent != null) OnMainCharacterShootEvent(this);
    }
    internal void FinishLoading(int ammo)
    {
        BulletCount = ammo;
        if (OnLoadedEvent != null) OnLoadedEvent();
    }
}
public sealed class CharacterMainControl : Component
{
    public static CharacterMainControl Main;
    public static event Action<CharacterMainControl, DuckovItemAgent> OnMainCharacterChangeHoldItemAgentEvent;
    public static event Action<CharacterMainControl, Slot> OnMainCharacterSlotContentChangedEvent;
    public event Action<CharacterActionBase> OnActionStartEvent;
    public Health Health;
    public Teams Team;
    public bool IsMainCharacter { get { return ReferenceEquals(this, Main); } }
    public DuckovItemAgent CurrentHoldItemAgent;
    internal int ActionSubscribers { get { return OnActionStartEvent == null ? 0 : OnActionStartEvent.GetInvocationList().Length; } }
    internal static int SlotSubscribers { get { return OnMainCharacterSlotContentChangedEvent == null ? 0 : OnMainCharacterSlotContentChangedEvent.GetInvocationList().Length; } }
    internal static int HoldSubscribers { get { return OnMainCharacterChangeHoldItemAgentEvent == null ? 0 : OnMainCharacterChangeHoldItemAgentEvent.GetInvocationList().Length; } }
    internal void Hold(DuckovItemAgent agent)
    {
        CurrentHoldItemAgent = agent;
        if (OnMainCharacterChangeHoldItemAgentEvent != null) OnMainCharacterChangeHoldItemAgentEvent(this, agent);
    }
    internal void EquipmentChanged() { if (OnMainCharacterSlotContentChangedEvent != null) OnMainCharacterSlotContentChangedEvent(this, new Slot()); }
    internal void BeginReload() { if (OnActionStartEvent != null) OnActionStartEvent(new CA_Reload()); }
    internal void CancelReload() { /* 官方取消不会再发开始事件，也不发装填完成事件。 */ }
}
public sealed class Health : Component
{
    public static event Action<Health, DamageInfo> OnDead;
    public CharacterMainControl Character;
    public bool IsDead;
    public bool IsMainCharacterHealth { get { return Character != null && Character.IsMainCharacter; } }
    internal readonly List<DamageInfo> Hits = new List<DamageInfo>();
    internal Action<DamageInfo> OnReceive;
    internal static int DeathSubscribers { get { return OnDead == null ? 0 : OnDead.GetInvocationList().Length; } }
    public CharacterMainControl TryGetCharacter() { return Character; }
    internal void Die()
    {
        IsDead = true;
        if (OnDead != null) OnDead(this, new DamageInfo());
    }
}
public sealed class DamageReceiver : Component
{
    public Health health;
    public void Hurt(DamageInfo info)
    {
        health.Hits.Add(info);
        if (health.OnReceive != null) health.OnReceive(info);
    }
}
public sealed class SceneLoadingContext { }
public static class SceneLoader
{
    public static bool IsSceneLoading;
    public static event Action<SceneLoadingContext> onStartedLoadingScene;
    internal static int StartedSubscribers { get { return onStartedLoadingScene == null ? 0 : onStartedLoadingScene.GetInvocationList().Length; } }
    internal static void BeginScene()
    {
        // 官方先标记加载并广播，再渐变黑幕/卸旧场景；目标 LevelManager 此时尚未开始初始化。
        IsSceneLoading = true;
        if (onStartedLoadingScene != null) onStartedLoadingScene(new SceneLoadingContext());
    }
    internal static void FinishScene() { IsSceneLoading = false; }
}
public sealed class LevelManager
{
    public static LevelManager Instance;
    public static event Action OnLevelBeginInitializing;
    public static event Action OnAfterLevelInitialized;
    public bool IsBaseLevel;
    internal static int BeginSubscribers { get { return OnLevelBeginInitializing == null ? 0 : OnLevelBeginInitializing.GetInvocationList().Length; } }
    internal static int EndSubscribers { get { return OnAfterLevelInitialized == null ? 0 : OnAfterLevelInitialized.GetInvocationList().Length; } }
    internal static void BeginLevel() { if (OnLevelBeginInitializing != null) OnLevelBeginInitializing(); }
    internal static void FinishLevel() { if (OnAfterLevelInitialized != null) OnAfterLevelInitialized(); }
}
namespace BossRush
{
    internal sealed class NewWeaponTotemSpec
    {
        internal int TypeId;
        internal string BaseName, LogPrefix, DisplayLabelCN, DisplayNameCN, DisplayNameEN, DescriptionCN, DescriptionEN;
    }
    internal static class EquipmentFactory
    {
        internal static readonly Dictionary<string, Action<ItemStatsSystem.Item, string>> Configurators =
            new Dictionary<string, Action<ItemStatsSystem.Item, string>>();
        internal static void RegisterConfigurator(string key, Action<ItemStatsSystem.Item, string> action) { Configurators[key] = action; }
    }
    internal static class NewWeaponConfiguratorCore
    {
        internal static bool ConfigureTotem(ItemStatsSystem.Item item, string baseName, NewWeaponTotemSpec spec)
        { return item != null && baseName == spec.BaseName; }
    }
    internal static class ModeFItemConfigHelper
    {
        internal static void SetHiddenMember(ItemStatsSystem.Item item, string name, object value) { }
        internal static void ClearInheritedUsage(ItemStatsSystem.Item item) { }
    }
    internal static class ProductionIconCache
    {
        internal static Sprite Source;
        internal static string LastPath;
        internal static Sprite Get(string path) { LastPath = path; return Source; }
        internal static void ResetStaticCaches()
        {
            // AssetBundle.Unload(true) 会同时销毁被借出的 Sprite 与它依赖的 Texture。
            if (Source != null) { UnityEngine.Object.Destroy(Source.texture); UnityEngine.Object.Destroy(Source); }
            Source = null;
        }
    }
    internal static class L10n { internal static string T(string cn, string en) { return cn; } }
    internal static class LocalizationHelper { internal static void InjectLocalization(string key, string text) { } }
    public static class ModBehaviour
    {
        internal static bool Spectating;
        internal static readonly List<string> Logs = new List<string>();
        public static bool IsModeHRunInProgressSafe() { return Spectating; }
        public static void DevLog(string text) { Logs.Add(text); }
    }
    public static class BossRushUI
    {
        internal static bool Paused;
        public static bool IsGamePaused() { return Paused; }
    }
    internal static class NewWeaponEquipState
    {
        // 不给 true 默认值；夹具必须经装备事件把真实测试前提装配好。
        internal static bool Equipped;
        internal static bool IsTotemEquipped(int typeId) { return Equipped && typeId == BossRushItemIds.EmptyMagazineMine; }
        internal static void MarkDirty() { }
    }
    internal static class PetNestCompanionAgent
    {
        internal static readonly HashSet<Health> Companions = new HashSet<Health>();
        internal static bool IsCompanionHealth(Health health) { return Companions.Contains(health); }
    }
    internal sealed class EmptyMagazineMineFx : MonoBehaviour
    {
        internal static int Created, Exploded;
        internal static bool ThrowDuringFuse;
        internal static EmptyMagazineMineFx Create(Vector3 position, float yaw, float radius)
        {
            Created++;
            GameObject go = new GameObject("fixture mine visual");
            go.transform.position = position;
            return go.AddComponent<EmptyMagazineMineFx>();
        }
        internal void ShowFuse(float elapsed, float duration)
        {
            if (ThrowDuringFuse) throw new InvalidOperationException("injected visual failure");
        }
        internal static void PlayExplosion(Vector3 center, float radius) { Exploded++; }
    }
}
