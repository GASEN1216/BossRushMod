using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static class Trace
{
    internal static readonly List<string> Events = new List<string>();
    internal static void Add(string text) { Events.Add(text); }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public bool ThrowOnDestroy;
        public static bool ThrowOnPersist;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void DontDestroyOnLoad(Object value)
        {
            if (ThrowOnPersist) throw new InvalidOperationException("persist");
            ((GameObject)value).Persistent = true;
        }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            Trace.Add("destroy:" + (value is GameObject ? ((GameObject)value).name : value.GetType().Name));
            if (value.ThrowOnDestroy) throw new InvalidOperationException("destroy");
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null))
            {
                foreach (Component component in go.Components) component.Destroyed = true;
                foreach (GameObject child in go.Children) Destroy(child);
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        private bool active = true;
        public bool enabled { get { return active; } set { active = value; Trace.Add("enabled:" + GetType().Name + ":" + value); } }
        public T GetComponent<T>() where T : class { return gameObject.Components.FirstOrDefault(x => x != null && x is T) as T; }
        public T GetComponentInChildren<T>(bool includeInactive = false) where T : class
        { return GetComponentsInChildren<T>(includeInactive).FirstOrDefault(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class
        { return gameObject.AllComponents().Where(x => x != null && x is T).Cast<T>().ToArray(); }
    }
    public class MonoBehaviour : Component { }
    public sealed class GameObject : Object
    {
        public static readonly List<GameObject> Created = new List<GameObject>();
        public readonly List<Component> Components = new List<Component>();
        public readonly List<GameObject> Children = new List<GameObject>();
        public string name;
        public bool Persistent;
        public GameObject(string name) { this.name = name; Created.Add(this); }
        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T(); component.gameObject = this; Components.Add(component); return component;
        }
        public IEnumerable<Component> AllComponents()
        {
            foreach (Component component in Components) yield return component;
            foreach (GameObject child in Children) foreach (Component component in child.AllComponents()) yield return component;
        }
    }
    public sealed class WaitForSeconds
    {
        public readonly float Seconds;
        public WaitForSeconds(float seconds) { Seconds = seconds; }
    }
    public static class Mathf
    {
        public static bool Approximately(float a, float b) { return Math.Abs(a - b) < 0.000001f; }
        public static float Min(float a, float b) { return Math.Min(a, b); }
    }
}

namespace ItemStatsSystem
{
    public sealed class Stat
    {
        private float value;
        public bool ThrowOnWrite;
        public Stat(float value) { this.value = value; }
        public float BaseValue { get { return value; } set { if (ThrowOnWrite) throw new InvalidOperationException("stat"); this.value = value; } }
    }
    public sealed class Item : UnityEngine.Object
    {
        public int TypeId;
        public readonly Dictionary<int, Stat> Stats = new Dictionary<int, Stat>();
        public readonly HashSet<int> ThrowOnRead = new HashSet<int>();
        public Stat GetStat(int id)
        {
            Trace.Add("stat:" + id);
            if (ThrowOnRead.Contains(id)) throw new InvalidOperationException("stat lookup");
            Stat result; return Stats.TryGetValue(id, out result) ? result : null;
        }
    }
    public static class ItemAssetsCollection
    {
        public static readonly List<int> Requested = new List<int>();
        public static Func<int, Item> Factory;
        public static Item InstantiateSync(int id)
        { Requested.Add(id); return Factory != null ? Factory(id) : new Item { TypeId = id }; }
    }
}

public enum SkillTypes { characterSkill }
public class SkillBase : UnityEngine.MonoBehaviour { }
public sealed class Skill_Grenade : SkillBase { }
public sealed class skill_grenade : SkillBase { }
public sealed class OtherSkill : SkillBase { }
public class AISpecialAttachmentBase : UnityEngine.MonoBehaviour { }
public class AISpecialAttachment_BoomCar : AISpecialAttachmentBase { }
public sealed class AISpecialAttachment_BoomCarVariant : AISpecialAttachment_BoomCar { }
public sealed class aispecialattachment_boomcar : AISpecialAttachmentBase { }
public sealed class CharacterRandomPreset
{ public List<AISpecialAttachmentBase> specialAttachmentBases; }
public sealed class Health : UnityEngine.Object
{
    public Func<float> MaxQuery;
    public float Current;
    public bool ThrowOnSet;
    public float MaxHealth { get { return MaxQuery(); } }
    public void SetHealth(float value) { Trace.Add("heal"); if (ThrowOnSet) throw new InvalidOperationException("health"); Current = value; }
}
public sealed class CharacterMainControl : UnityEngine.MonoBehaviour
{
    private ItemStatsSystem.Item item;
    public bool ThrowOnItem;
    public ItemStatsSystem.Item CharacterItem { get { if (ThrowOnItem) throw new InvalidOperationException("item"); return item; } set { item = value; } }
    public Health Health;
    public CharacterRandomPreset characterPreset;
    public int ClearSkillCalls;
    public Action OnSetSkill;
    public void SetSkill(SkillTypes type, object skill, object context)
    { Trace.Add("clear-skill"); ClearSkillCalls++; if (OnSetSkill != null) OnSetSkill(); }
}
public sealed class AICharacterController : UnityEngine.MonoBehaviour
{
    public float baseReactionTime, reactionTime, shootDelay;
    public bool hasSkill;
    public SkillBase skillInstance, skillPfb;
    public CharacterMainControl CharacterMainControl;
}
public sealed class StockShopDatabase
{
    public sealed class ItemEntry
    { public int typeID, maxStock; public bool forceUnlock, lockInDemo; public float priceFactor, possibility; }
}
namespace Duckov.Economy
{
    public sealed class StockShop : UnityEngine.MonoBehaviour
    {
        private string merchantID;
        private bool accountAvaliable;
        private Dictionary<int, ItemStatsSystem.Item> itemInstances;
        public List<Entry> entries;
        public static Action<StockShop> Constructed;
        public static Action<StockShop> UiCallback;
        public int UiCalls;
        public string MerchantID { get { return merchantID; } }
        public bool AccountAvaliable { get { return accountAvaliable; } }
        public Dictionary<int, ItemStatsSystem.Item> Items { get { return itemInstances; } set { itemInstances = value; } }
        public StockShop() { if (Constructed != null) Constructed(this); }
        public void ShowUI() { UiCalls++; if (UiCallback != null) UiCallback(this); }
        public sealed class Entry
        {
            public readonly StockShopDatabase.ItemEntry Raw;
            public int CurrentStock;
            public int MaxStock { get { return Raw.maxStock; } }
            public Entry(StockShopDatabase.ItemEntry raw) { Raw = raw; }
        }
    }
}
namespace BossRush
{
    public static class ModBehaviour { public static void DevLog(string message) { } }
    public enum ZombieModeEnemyKind { Normal, Special }
    public enum ZombieModeSpecialKind { None, OfficialExploder }
    public sealed class ZombieModeEnemyRuntimeMarker : UnityEngine.MonoBehaviour
    { public bool IsBoss; public ZombieModeEnemyKind EnemyKind; public ZombieModeSpecialKind SpecialKind; }
    internal static class BossRushEagerReflectionCache
    {
        internal static FieldInfo StockShop_MerchantID = Field("merchantID");
        internal static FieldInfo StockShop_AccountAvaliable = Field("accountAvaliable");
        internal static FieldInfo StockShop_ItemInstances = Field("itemInstances");
        private static FieldInfo Field(string name)
        { return typeof(Duckov.Economy.StockShop).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance); }
    }
    internal sealed partial class IntegrationRuntimeModule
    { internal Duckov.Economy.StockShop ProbeShop { get { return ammoShop; } } }
}
