using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace UnityEngine
{
    public class Object
    {
        private static int next;
        private readonly int id = ++next;
        internal bool Destroyed;
        public int GetInstanceID() { return id; }
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null)) return;
            obj.Destroyed = true;
            GameObject go = obj as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object child in go.Components) child.Destroyed = true;
        }
        public static bool operator ==(Object a, Object b)
        { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object obj) { return this == obj as Object; }
        public override int GetHashCode() { return id; }
    }
    public class GameObject : Object { internal readonly List<Object> Components = new List<Object>(); }
    public class MonoBehaviour : Object { }
    public class Transform { public Vector3 position, localScale = new Vector3(1, 1, 1); }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Min(int a, int b) { return Math.Min(a, b); }
        public static int Clamp(int x, int a, int b) { return Math.Max(a, Math.Min(x, b)); }
    }
}
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { Add, PercentageAdd, PercentageMultiply }
    public sealed class Modifier
    {
        internal readonly ModifierType Type;
        internal readonly float Amount;
        public Modifier(ModifierType type, float amount, object source) { Type = type; Amount = amount; }
    }
    public sealed class Stat
    {
        public float BaseValue = 1;
        internal readonly List<Modifier> Modifiers = new List<Modifier>();
        public float Value
        {
            get
            {
                float add = 0, percent = 0, factor = 1;
                foreach (Modifier m in Modifiers)
                    if (m.Type == ModifierType.Add) add += m.Amount;
                    else if (m.Type == ModifierType.PercentageAdd) percent += m.Amount;
                    else factor *= 1 + m.Amount;
                return (BaseValue * (1 + percent) + add) * factor;
            }
        }
        public void AddModifier(Modifier mod) { Modifiers.Add(mod); }
        public void RemoveModifier(Modifier mod) { Modifiers.Remove(mod); }
    }
}
namespace ItemStatsSystem
{
    public sealed class Item
    {
        private readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        public Stat GetStat(string name)
        {
            Stat value;
            if (!stats.TryGetValue(name, out value)) stats[name] = value = new Stat { BaseValue = name == "MaxHealth" ? 100 : 1 };
            return value;
        }
    }
}
public sealed class AICharacterController { public float forceTracePlayerDistance; public bool noticed; }
public sealed class CharacterMainControl : MonoBehaviour
{
    public static CharacterMainControl Main;
    public readonly GameObject gameObject = new GameObject();
    public readonly Transform transform = new Transform();
    public Item CharacterItem = new Item();
    public readonly Health Health;
    public CharacterMainControl() { Health = new Health(this); gameObject.Components.Add(this); gameObject.Components.Add(Health); }
    public T GetComponentInChildren<T>() where T : class { return null; }
    public void AddHealth(float amount) { Health.AddHealth(amount); }
}
public sealed class Health : UnityEngine.Object
{
    public static event Action<Health, DamageInfo> OnDead;
    private readonly CharacterMainControl owner;
    public bool IsDead;
    public float CurrentHealth = 100;
    public float MaxHealth { get { return owner.CharacterItem.GetStat("MaxHealth").Value; } }
    public Health(CharacterMainControl owner) { this.owner = owner; }
    public CharacterMainControl TryGetCharacter() { return owner; }
    public void AddHealth(float amount) { SetHealth(CurrentHealth + amount); }
    public void SetHealth(float value) { CurrentHealth = Math.Min(MaxHealth, value); }
    public void ReportDead(DamageInfo info) { IsDead = true; OnDead?.Invoke(this, info); }
}
public enum ElementTypes { fire }
public enum ExplosionFxTypes { normal }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public float damageValue;
    public bool isFromBuffOrEffect, isExplosion;
    public int fromWeaponItemID;
    public Vector3 damagePoint;
    public DamageInfo(CharacterMainControl source) { this = default(DamageInfo); fromCharacter = source; }
    public void AddElementFactor(ElementTypes element, float factor) { }
}
public sealed class ExplosionManager
{
    public int Calls;
    public DamageInfo Last;
    public float Radius;
    public bool HurtSelf;
    internal Action OnExplosion;
    public void CreateExplosion(Vector3 position, float radius, DamageInfo info, ExplosionFxTypes fx, float shake, bool hurtSelf)
    { Calls++; Last = info; Radius = radius; HurtSelf = hurtSelf; OnExplosion?.Invoke(); }
}
public sealed class LevelManager : UnityEngine.Object
{
    public static LevelManager Instance = new LevelManager();
    public readonly ExplosionManager ExplosionManager = new ExplosionManager();
}
namespace BossRush
{
    public sealed class ModBehaviour : UnityEngine.Object
    {
        public static ModBehaviour Instance;
        private readonly List<IEnumerator> pending = new List<IEnumerator>();
        public ModBehaviour() { Instance = this; }
        public static void DevLog(string value) { }
        public void StartCoroutine(IEnumerator routine) { if (routine.MoveNext()) pending.Add(routine); }
        internal void Advance() { foreach (IEnumerator routine in pending.ToArray()) if (!routine.MoveNext()) { pending.Remove(routine); (routine as IDisposable)?.Dispose(); } }
    }
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnUpdate(float dt, float udt) { }
        public virtual void OnDestroy() { }
    }
    internal sealed class BossRushCoopPlayer
    {
        internal string Id;
        internal string AssetId, ReadyEpoch;
        internal bool IsLocal;
        internal CharacterMainControl Character;
        internal bool IsConnected = true;
        internal bool IsAlive { get { return IsConnected && Character != null && !Character.Health.IsDead; } }
    }
    internal static partial class BossRushCoop
    {
        internal static bool IsConnected = true, IsHost = true;
        internal static bool HasBoundScene;
        internal static string SceneToken;
        internal static bool IsClient { get { return IsConnected && !IsHost; } }
        internal static bool HasWorldAuthority { get { return !IsConnected || IsHost; } }
        internal static string RunId = "room";
        internal static int SceneGeneration = 1;
        internal static readonly List<BossRushCoopPlayer> Players = new List<BossRushCoopPlayer>();
        internal static readonly Dictionary<string, CharacterMainControl> Entities = new Dictionary<string, CharacterMainControl>();
        internal static string GetPlayerId(CharacterMainControl character) { foreach (var p in Players) if (p.Character == character) return p.Id; return null; }
        internal static BossRushCoopPlayer FindPlayer(string id) { return Players.Find(p => p.Id == id); }
        internal static bool IsPlayer(CharacterMainControl character) { return character != null && GetPlayerId(character) != null; }
        internal static string GetEntityId(CharacterMainControl character) { foreach (var pair in Entities) if (pair.Value == character) return pair.Key; return null; }
        internal static CharacterMainControl FindEntity(string id) { CharacterMainControl value; return id != null && Entities.TryGetValue(id, out value) ? value : null; }
    }
    internal sealed class CoopGameplayChannel<T> : IDisposable
    {
        private readonly Func<T> capture;
        internal static T LastPublished;
        internal CoopGameplayChannel(string topic, Func<T> capture, Action<T> apply, object execute) { this.capture = capture; }
        internal void Publish() { LastPublished = capture(); }
        internal void Tick(float dt) { if (BossRushCoop.IsHost) Publish(); }
        public void Dispose() { }
    }
#if !MUTATOR_REAL_DELIVERY
    internal sealed class CoopPersonalDelivery<T> : IDisposable
    {
        internal struct Delivery { internal string Player, Id; internal T Value; }
        internal static readonly List<Delivery> Sent = new List<Delivery>();
        internal CoopPersonalDelivery(string topic, Func<T, bool> apply) { }
        internal void Tick(float dt) { }
        internal bool Deliver(string player, string id, T value, string targetAssetId = null) { Sent.Add(new Delivery { Player = player, Id = id, Value = value }); return true; }
        public void Dispose() { }
    }
#endif
    internal static class BossRushUI { internal static bool Paused; internal static bool IsGamePaused() { return Paused; } }
    internal static class MutatorUI { internal static int Banners; internal static void ShowBanner() { Banners++; } internal static void HideAll() { } }
    public static class L10n { public static string T(string cn, string en) { return cn; } }
}

namespace BossRush { internal static class CoopAiPredictionHealthProtection { internal static void MarkReplica(CharacterMainControl character) { } } }
