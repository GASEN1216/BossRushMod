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
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return id; }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null) || value.Destroyed) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null))
                foreach (Component component in go.Components.ToArray()) Destroy(component);
            Component item = value as Component;
            if (!ReferenceEquals(item, null))
            {
                MethodInfo callback = item.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                if (callback != null) callback.Invoke(item, null);
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
    }
    public class MonoBehaviour : Component { }
    public sealed class Transform : Component { public Vector3 position; }
    public sealed class GameObject : Object
    {
        internal static readonly List<GameObject> All = new List<GameObject>();
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        internal bool Active = true;
        public GameObject(string name)
        {
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
            foreach (Component value in Components)
                if (!value.Destroyed && value is T) return value as T;
            return null;
        }
        public void SetActive(bool value) { Active = value; }
        internal static void DestroyScene()
        {
            foreach (GameObject go in All.ToArray()) Destroy(go);
            All.Clear();
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public static class Time { public static float time, deltaTime; public static int frameCount; }
}

namespace ItemStatsSystem.Items { public sealed class Slot { } }
public enum Teams { player, wolf, red, blue, middle, all }
public static class Team
{
    // 官方 Team.IsEnemy 的原有阵营口径，不把不同阵营简单等同于敌人。
    public static bool IsEnemy(Teams self, Teams target)
    { return self != Teams.middle && (self == Teams.all || (target != Teams.middle && self != target)); }
}
public enum DamageTypes { normal }
public enum ElementTypes { physics }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public float finalDamage, damageValue;
    public DamageTypes damageType;
    public bool isFromBuffOrEffect;
    public int fromWeaponItemID;
    public Vector3 damagePoint;
    internal bool HasPhysicalElement;
    public DamageInfo(CharacterMainControl source) { this = default(DamageInfo); fromCharacter = source; }
    public void AddElementFactor(ElementTypes type, float ratio) { HasPhysicalElement = type == ElementTypes.physics && ratio == 1f; }
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
        // 官方致死调用顺序：OnDead 先于 OnHurt。目标仍然存在但 IsDead 已为 true。
        if (OnHurt != null) OnHurt(this, info);
    }
    internal void Die(DamageInfo info)
    {
        IsDead = true;
        if (OnDead != null) OnDead(this, info);
    }
}
public sealed class SceneLoadingContext { }
public static class SceneLoader
{
    public static bool IsSceneLoading;
    public static event Action<SceneLoadingContext> onStartedLoadingScene;
    public static event Action<SceneLoadingContext> onFinishedLoadingScene;
    internal static int Subscribers { get { return CharacterMainControl.Count(onStartedLoadingScene); } }
    internal static int FinishSubscribers { get { return CharacterMainControl.Count(onFinishedLoadingScene); } }
    internal static void Begin()
    {
        IsSceneLoading = true;
        if (onStartedLoadingScene != null) onStartedLoadingScene(new SceneLoadingContext());
    }
    internal static void Finish()
    {
        IsSceneLoading = false;
        if (onFinishedLoadingScene != null) onFinishedLoadingScene(new SceneLoadingContext());
    }
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
namespace BossRush
{
    public static class ModBehaviour
    {
        internal static readonly List<string> Logs = new List<string>();
        public static void DevLog(string value) { Logs.Add(value); }
    }
    internal static class BossRushUI { internal static bool Paused; internal static bool IsGamePaused() { return Paused; } }
    internal static class NewWeaponEquipState
    {
        internal static bool Equipped;
        internal static bool IsTotemEquipped(int type) { return Equipped && type == BossRushItemIds.RollCallLedger; }
        internal static void MarkDirty() { }
    }
    internal static class PetNestCompanionAgent
    {
        internal static readonly HashSet<Health> Companions = new HashSet<Health>();
        internal static bool IsCompanionHealth(Health value) { return Companions.Contains(value); }
    }
    internal static class L10n { internal static string T(string cn, string en) { return cn; } }
    internal static class NewWeaponFx
    {
        internal static int Bursts;
        internal static void PlayBurst(Vector3 pos, Color color, float radius, float life, int shards, bool light) { Bursts++; }
    }
}
namespace Duckov.UI.DialogueBubbles
{
    public static class DialogueBubblesManager
    {
        internal struct Request { internal string Text; internal Transform Target; internal float Height, Duration; }
        internal static readonly List<Request> Requests = new List<Request>();
        public static void Show(string text, Transform target, float yOffset = -1f, bool needInteraction = false,
            bool skippable = false, float speed = -1f, float duration = 2f)
        { Requests.Add(new Request { Text = text, Target = target, Height = yOffset, Duration = duration }); }
    }
}
