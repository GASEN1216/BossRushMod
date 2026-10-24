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
        public static implicit operator bool(Object value) { return value != null; }
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
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public sealed class Transform : Component { public Vector3 position; public Quaternion rotation; }
    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } }
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
        public static Vector3 zero { get { return new Vector3(); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public Vector3 normalized { get { float len=(float)Math.Sqrt(sqrMagnitude); return len>0 ? new Vector3(x/len,y/len,z/len) : zero; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
    public static class Mathf { public static float Max(float a,float b) { return Math.Max(a,b); } }
    public static class Time { public static float time, deltaTime, unscaledTime; public static int frameCount; }
}

namespace ItemStatsSystem.Items { public sealed class Slot { } }
public enum Teams { player, wolf, red, blue, middle, all }
public static class Team
{
    // 官方 Team.IsEnemy 的原有阵营口径，不把不同阵营简单等同于敌人。
    public static bool IsEnemy(Teams self, Teams target)
    { return self != Teams.middle && (self == Teams.all || (target != Teams.middle && self != target)); }
}
public enum DamageTypes { normal, realDamage }
public enum ElementTypes { physics }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public float finalDamage, damageValue;
    public DamageTypes damageType;
    public bool isFromBuffOrEffect, isExplosion;
    public int fromWeaponItemID;
    public Vector3 damagePoint;
    internal bool HasPhysicalElement;
    public DamageInfo(CharacterMainControl source) { this = default(DamageInfo); fromCharacter = source; }
    public void AddElementFactor(ElementTypes type, float ratio) { HasPhysicalElement = type == ElementTypes.physics && ratio == 1f; }
}
public class CharacterActionBase { }
public sealed class CA_Dash : CharacterActionBase { }
public class DuckovItemAgent : Component { }
public sealed partial class CharacterMainControl : Component
{
    public static CharacterMainControl Main;
    public static event Action<CharacterMainControl, DuckovItemAgent> OnMainCharacterChangeHoldItemAgentEvent;
    public static event Action<CharacterMainControl, Slot> OnMainCharacterSlotContentChangedEvent;
    public Health Health;
    public Vector3 CurrentAimDirection = new Vector3(0,0,1);
    public Vector3 MoveInput;
    public event Action<CharacterActionBase> OnActionStartEvent;
    internal int ActionSubscribers { get { return Count(OnActionStartEvent); } }
    internal void StartAction(CharacterActionBase action) { if (OnActionStartEvent != null) OnActionStartEvent(action); }
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
    public float CurrentHealth = 1000f;
    public float DefenseFactor = 1f;
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
        info.finalDamage = info.damageValue * DefenseFactor;
        BossRush.ZombieModeDamageRuntime.ReduceFinalDamage(this, ref info);
        if (CurrentHealth < info.finalDamage) info.finalDamage = CurrentHealth + 1f;
        CurrentHealth -= info.finalDamage;
        Hits.Add(info);
        if (CurrentHealth <= 0f) Die(info);
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
    public CharacterMainControl MainCharacter { get { return CharacterMainControl.Main; } }
    internal static int BeginSubscribers { get { return CharacterMainControl.Count(OnLevelBeginInitializing); } }
    internal static int EndSubscribers { get { return CharacterMainControl.Count(OnAfterLevelInitialized); } }
    internal static void Begin() { if (OnLevelBeginInitializing != null) OnLevelBeginInitializing(); }
    internal static void Finish() { if (OnAfterLevelInitialized != null) OnAfterLevelInitialized(); }
}
namespace BossRush
{
    internal class ModBehaviour
    {
        public static ModBehaviour Instance;
        public bool IsZombieModeActive;
        public int ZombieModeCurrentRunId;
        public bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl target, out ZombieModeEnemyRuntimeMarker marker) { marker=null; return false; }
        public void ApplyZombieModeEnemyDefense(Health health, ref DamageInfo info, ZombieModeEnemyRuntimeMarker marker) { }
        public float AbsorbZombieModeBossFinalDamage(CharacterMainControl target, ZombieModeEnemyRuntimeMarker marker, float damage) { return 0; }
        public float ApplyZombieModeShielderAuraFinalDamageReduction(CharacterMainControl target, float damage) { return 0; }
        internal static readonly List<string> Logs = new List<string>();
        public static void DevLog(string value) { Logs.Add(value); }
    }
    internal static class BossRushUI { internal static bool Paused; internal static bool IsGamePaused() { return Paused; } }
    internal static class NewWeaponEquipState
    {
        internal static bool Equipped;
        internal static bool IsTotemEquipped(int type) { return Equipped && type == BossRushItemIds.ResidualSunShield; }
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

namespace BossRush {
    internal sealed class ZombieModeEnemyRuntimeMarker { public int RunId; public bool DeathSettled, RemovedFromRuntime, IsBoss; public ZombieModeBossShieldRuntime AllyShield; }
    internal sealed class ZombieModeBossShieldRuntime { internal bool IsShieldActive() { return false; } internal float AbsorbDamage(float damage) { return 0f; } }
    internal static class ZombieModeBossVisuals { internal static void RestoreOfficialPreset(ZombieModeEnemyRuntimeMarker marker) { } }
    internal sealed class ResidualSunShieldFx : MonoBehaviour {
        internal static int Absorbs, Ripostes;
        internal bool Guard, Charge;
        internal static ResidualSunShieldFx Create(Transform parent) { return parent.gameObject.AddComponent<ResidualSunShieldFx>(); }
        internal void SetState(Vector3 pos, Vector3 facing, bool guard, bool charge) { Guard=guard; Charge=charge; }
        internal static int Flashes, Releases;
        internal void Flash() { Flashes++; }
        internal void Release() { Releases++; }
        internal static void PlayAbsorb(Vector3 pos) { Absorbs++; }
        internal static void PlayRiposte(Vector3 pos) { Ripostes++; }
    }
}
