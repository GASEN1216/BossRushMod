using System;
using System.Collections;
using System.Collections.Generic;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        internal virtual bool IsDestroyed { get { return Destroyed; } }
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null)) return;
            obj.Destroyed = true;
            GameObject go = obj as GameObject;
            if (go != null) foreach (Component c in go.Components) c.Destroyed = true;
        }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.IsDestroyed, bn = ReferenceEquals(b, null) || b.IsDestroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object b) { return this == b as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public class GameObject : Object { internal readonly List<Component> Components = new List<Component>(); }
    public class Component : Object
    {
        public GameObject gameObject;
        internal override bool IsDestroyed { get { return Destroyed || (!ReferenceEquals(gameObject, null) && gameObject.Destroyed); } }
    }
    public class Transform { public Vector3 position, forward = Vector3.forward; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x) { this.x = x; y = z = 0; }
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public float sqrMagnitude { get { return x*x + y*y + z*z; } }
        public Vector3 normalized { get { float m = (float)Math.Sqrt(sqrMagnitude); return m > 0 ? this * (1f/m) : new Vector3(); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x, a.y+b.y, a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x, a.y-b.y, a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b, a.y*b, a.z*b); }
    }
    public struct Quaternion
    {
        private float angle;
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion { angle = y * (float)Math.PI/180 }; }
        public static Vector3 operator *(Quaternion q, Vector3 v)
        { return new Vector3(v.x*(float)Math.Cos(q.angle)+v.z*(float)Math.Sin(q.angle), v.y, v.z*(float)Math.Cos(q.angle)-v.x*(float)Math.Sin(q.angle)); }
    }
    public static class Time { public static float time = 10f; }
    public sealed class Coroutine { internal IEnumerator Routine; }
}
namespace ItemStatsSystem
{
    public sealed class Item
    {
        public int TypeID, Quality = 7, Capacity = 3, ForgeBaseCost = 100;
        public readonly TagCollection Tags = new TagCollection();
        public object Setting;
        public T GetComponent<T>() where T : class { return Setting as T; }
        internal readonly List<AffixSlotView> Affixes = new List<AffixSlotView>();
        private readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        public Stat GetStat(string key) { Stat s; if (!stats.TryGetValue(key, out s)) stats[key] = s = new Stat(); return s; }
    }
}
public class ItemSetting_Gun { }
public class ItemSetting_MeleeWeapon { }
public class TagCollection : HashSet<string> { }
namespace ItemStatsSystem.Items { public sealed class Slot { public string Key; } }
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { Add, PercentageAdd, PercentageMultiply }
    public sealed class Modifier { public Modifier(ModifierType t, float value, object source) { } }
    public sealed class Stat
    {
        internal readonly List<Modifier> Modifiers = new List<Modifier>();
        public void AddModifier(Modifier m) { Modifiers.Add(m); }
    }
}
namespace Duckov.Buffs { public sealed class Buff { internal string Id; } }
public enum Teams { player, wolf }
public enum ElementTypes { physics, fire, electricity }
public enum ExplosionFxTypes { normal }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public float damageValue, finalDamage;
    public bool isFromBuffOrEffect, isExplosion, ignoreArmor;
    public int fromWeaponItemID;
    public Vector3 damagePoint, damageNormal;
    public ElementTypes Element;
    public DamageInfo(CharacterMainControl main) { this = default(DamageInfo); fromCharacter = main; }
    public void AddElementFactor(ElementTypes type, float ratio) { Element = type; }
}
public class DuckovItemAgent { public Item Item; }
public sealed class ItemAgent_Gun : DuckovItemAgent
{
    public static event Action<ItemAgent_Gun> OnMainCharacterShootEvent;
    internal static void Shoot() { if (OnMainCharacterShootEvent != null) OnMainCharacterShootEvent(new ItemAgent_Gun()); }
}
public sealed class CharacterMainControl : Component
{
    public static CharacterMainControl Main;
    public static event Action<CharacterMainControl, DuckovItemAgent> OnMainCharacterChangeHoldItemAgentEvent;
    public static event Action<CharacterMainControl, Slot> OnMainCharacterSlotContentChangedEvent;
    public bool IsMainCharacter { get { return ReferenceEquals(Main, this); } }
    public Transform transform = new Transform();
    public Item CharacterItem = new Item(), Armor, Helmet, Mask;
    public DuckovItemAgent CurrentHoldItemAgent;
    public Health Health;
    public readonly List<string> Buffs = new List<string>();
    public CharacterMainControl()
    {
        gameObject = new GameObject(); gameObject.Components.Add(this);
        Health = new Health(this) { gameObject = gameObject }; gameObject.Components.Add(Health);
    }
    public Item GetArmorItem() { return Armor; }
    public Item GetHelmatItem() { return Helmet; }
    public Item GetFaceMaskItem() { return Mask; }
    public void AddBuff(Duckov.Buffs.Buff b, CharacterMainControl source, int id) { Buffs.Add(b.Id); }
    internal void Equip(Item item)
    {
        Armor = item;
        if (OnMainCharacterSlotContentChangedEvent != null) OnMainCharacterSlotContentChangedEvent(this, new Slot { Key = "Armor" });
    }
    internal void Hold(Item item)
    {
        CurrentHoldItemAgent = item == null ? null : new DuckovItemAgent { Item = item };
        if (OnMainCharacterChangeHoldItemAgentEvent != null) OnMainCharacterChangeHoldItemAgentEvent(this, CurrentHoldItemAgent);
    }
}
public sealed class Health : Component
{
    public static event Action<Health, DamageInfo> OnHurt, OnDead;
    public bool IsDead, Invincible;
    public float CurrentHealth = 100f, MaxHealth = 100f;
    public Teams team;
    private readonly CharacterMainControl character;
    internal readonly List<DamageInfo> Hits = new List<DamageInfo>();
    public bool IsMainCharacterHealth { get { return character.IsMainCharacter; } }
    internal Health(CharacterMainControl c) { character = c; }
    public CharacterMainControl TryGetCharacter() { return character; }
    public void Hurt(DamageInfo info)
    {
        if (IsDead || Invincible) return;
        Hits.Add(info); info.finalDamage = info.damageValue; CurrentHealth -= info.finalDamage;
        if (CurrentHealth <= 0f)
        {
            CurrentHealth = 0f; IsDead = true;
            if (OnDead != null) OnDead(this, info);
        }
        // Official order: lethal OnDead, followed by trailing OnHurt.
        if (OnHurt != null) OnHurt(this, info);
    }
    internal void ReportDead(DamageInfo info) { if (OnDead != null) OnDead(this, info); }
    public void AddHealth(float amount) { CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount); }
    public void SetHealth(float amount) { CurrentHealth = Math.Min(MaxHealth, amount); }
}
public sealed class LevelManager : UnityEngine.Object
{
    public static LevelManager Instance;
    public static event Action OnAfterLevelInitialized;
    public bool IsBaseLevel;
    public ExplosionManager ExplosionManager = new ExplosionManager();
    internal static void Initialize() { if (OnAfterLevelInitialized != null) OnAfterLevelInitialized(); }
}
public sealed class ExplosionManager
{
    // Official CreateExplosion reuses these per-instance arrays/collections even on nested calls.
    private readonly Health[] colliders = new Health[8];
    private readonly List<Health> damaged = new List<Health>();
    internal Health[] Initial = new Health[0], Secondary = new Health[0];
    internal int Depth, MaxDepth, Calls;
    internal bool Fail;
    internal bool LastCanHurtSelf;
    internal DamageInfo Last;
    public void CreateExplosion(Vector3 position, float radius, DamageInfo info,
        ExplosionFxTypes fx = ExplosionFxTypes.normal, float shake = 1f, bool canHurtSelf = true)
    {
        Depth++; MaxDepth = Math.Max(Depth, MaxDepth); Calls++;
        LastCanHurtSelf = canHurtSelf; Last = info;
        try
        {
            if (Fail) throw new InvalidOperationException("explosion unavailable");
            Health[] selection = info.isFromBuffOrEffect ? Secondary : Initial;
            Array.Copy(selection, colliders, selection.Length); damaged.Clear();
            for (int i = 0; i < selection.Length; i++)
            {
                Health h = colliders[i];
                if (h == null || damaged.Contains(h)) continue;
                damaged.Add(h); h.Hurt(info);
            }
        }
        finally { Depth--; }
    }
}
namespace BossRush
{
    public sealed partial class ModBehaviour : UnityEngine.Object
    {
        public static ModBehaviour Instance;
        public static bool DevModeEnabled;
        internal bool Enabled = true;
        internal bool Paused, FailScheduling;
        internal int Fallbacks;
        internal ZombieModeRunState zombieModeRunState = new ZombieModeRunState { RunId = 1 };
        private float zombieModeOptionExplosionSkipLogTime;
        private bool IsZombieModeRunValid(int id) { return id > 0 && id == zombieModeRunState.RunId && !zombieModeRunState.IsCleaningUp; }
        internal bool IsZombieModeRuntimePaused() { return Paused; }
        private float GetZombieModeRuntimeNow() { return Time.time; }
        private void DealZombieModeAreaDamageToPlayer(int id, CharacterMainControl source, Vector3 origin, float radius, float damage) { Fallbacks++; }
        internal void OptionExplosion() { CreateZombieModeOptionExplosion(1, new Vector3(), 3, 25); }
        internal void DoomPulse(int stacks) { TriggerZombieModeDoomPulse(1, stacks); }
        internal readonly List<Coroutine> Pending = new List<Coroutine>();
        public bool IsAffixForgeConfiguredEnabled() { return Enabled; }
        public static void DevLog(string line) { }
        public Coroutine StartCoroutine(IEnumerator routine)
        {
            if (FailScheduling) throw new InvalidOperationException("coroutine owner unavailable");
            var c = new Coroutine { Routine = routine };
            if (routine.MoveNext()) Pending.Add(c);
            return c;
        }
        public void StopCoroutine(Coroutine c) { Pending.Remove(c); (c.Routine as IDisposable)?.Dispose(); }
        internal void Advance()
        {
            foreach (Coroutine c in Pending.ToArray())
                if (!c.Routine.MoveNext()) { Pending.Remove(c); (c.Routine as IDisposable)?.Dispose(); }
        }
    }
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnDestroy() { }
    }
    internal static class ZombieModeEntryDebt { internal static void Attach() { } internal static void ResetStaticCaches() { } }
    public enum ZombieModeRunOnlyObjectKind { Coroutine }
    internal sealed class ZombieModeRunState
    {
        internal int RunId;
        internal bool IsCleaningUp;
        internal readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects = new List<ZombieModeRunOnlyRecord>();
    }
    internal sealed class MutatorContext
    {
        internal CharacterMainControl Player;
        internal readonly List<Action<CharacterMainControl, Vector3>> EnemyKilledCallbacks = new List<Action<CharacterMainControl, Vector3>>();
    }
    internal static partial class MutatorManager
    {
        internal static bool IsActive;
        internal static MutatorContext _currentContext;
        private static bool _dispatchingEnemyKilled;
        internal static void Emit(Health health, DamageInfo info) { OnAnyCharacterDead(health, info); }
    }
    public static class ReforgeSystem
    {
        public const int MIN_REFORGE_COST = 500;
        public static int GetDiscountedCost(Item item) { return item.ForgeBaseCost; }
    }
    public static class CustomItemRuntimeStateHelper
    {
        public static bool IsRuntimeConfiguredType(int id) { return id == 500099; }
        public static bool EnsureCustomItemConfigured(Item item)
        { item.Quality = 7; item.Setting = new ItemSetting_MeleeWeapon(); return true; }
    }
    public static class L10n { public static string T(string zh, string en) { return zh; } }
    internal static class BossRushUI { internal static bool Paused; internal static bool IsGamePaused() { return Paused; } }
    public struct AffixSlotView { public string AffixId; public int Tier; public bool Locked; public bool IsEmpty { get { return string.IsNullOrEmpty(AffixId); } } }
    public static partial class AffixItemData
    {
        public static bool IsAffixEligible(Item item) { return GetEquipMask(item) != AffixEquipMask.None; }
        public static int GetCapacity(Item item) { return item == null ? 0 : item.Capacity; }
        public static bool IsLocked(Item item, int slot) { return slot <= item.Affixes.Count && item.Affixes[slot - 1].Locked; }
        public static bool TryReadSlot(Item item, int slot, out AffixSlotView view)
        { view = slot <= item.Affixes.Count ? item.Affixes[slot - 1] : new AffixSlotView(); return true; }
        public static bool HasAffixData(Item item) { return item.Affixes.Count > 0; }
        public static void ReadAllSlots(Item item, List<AffixSlotView> into) { into.AddRange(item.Affixes); }
    }
    public sealed class ZombieModeAttributeModifierRecord { public Item CharacterItem; public Stat Stat; public Modifier Modifier; public string StatName; }
    public static class RuntimeStatModifierTracker
    {
        public static void RemoveAll(List<ZombieModeAttributeModifierRecord> records, string context)
        { foreach (var r in records) r.Stat.Modifiers.Remove(r.Modifier); records.Clear(); }
    }
    internal static class AffixRuntimeTicker
    {
        internal static bool Active;
        internal static void EnsureInstance() { Active = true; }
        internal static void DestroyInstance() { Active = false; }
    }
    public static class AffixBuffFactory
    {
        public static Duckov.Buffs.Buff GetBulwarkBuff(int tier) { return new Duckov.Buffs.Buff { Id = "bulwark" }; }
        public static Duckov.Buffs.Buff GetSwiftHandBuff(int tier) { return new Duckov.Buffs.Buff { Id = "swifthand" }; }
        public static Duckov.Buffs.Buff GetFrenzyBuff(int tier) { return new Duckov.Buffs.Buff { Id = "frenzy" }; }
        public static void ResetStaticCaches() { }
    }
}
