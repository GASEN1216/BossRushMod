using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        { return ReferenceEquals(a, b) || (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            var go = value as GameObject;
            if (!ReferenceEquals(go, null))
            {
                foreach (var c in go.Components)
                {
                    var destroy = c.GetType().GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (destroy != null) destroy.Invoke(c, null);
                    c.Destroyed = true;
                }
            }
            value.Destroyed = true;
        }
        public static void DontDestroyOnLoad(Object value) { }
        public static T Instantiate<T>(T source) where T : Object
        {
            var item = source as Item;
            if (item != null) return new GameObject().AddComponent<Item>() as T;
            throw new NotSupportedException();
        }
        public static T FindObjectOfType<T>() where T : Object { FindCalls++; return Selector as T; }
        public static Object Selector;
        public static int FindCalls;
    }
    public class MonoBehaviour : Component { }
    public enum KeyCode { Escape }
    public static class Input { public static bool Escape; public static bool GetKeyDown(KeyCode key) { bool value = Escape; Escape = false; return value; } }
    public enum HideFlags { HideAndDontSave }
    public class GameObject : Object
    {
        public HideFlags hideFlags;
        public readonly List<Component> Components = new List<Component>();
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; Components.Add(c); return c; }
        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(c => !c.Destroyed); }
        public void SetActive(bool active) { }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public static class JsonUtility
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public static T FromJson<T>(string value) { return JsonSerializer.Deserialize<T>(value, Options); }
        public static string ToJson(object value) { return JsonSerializer.Serialize(value, value.GetType(), Options); }
    }
    namespace Events { public class UnityEvent { } }
}
namespace Saves
{
    public static class SavesSystem
    {
        static readonly Dictionary<int, Dictionary<string, object>> Slots = new Dictionary<int, Dictionary<string, object>>();
        public static bool IsSaving;
        public static int CurrentSlot;
        public static string FailWrite, FailReadback;
        private static string pendingRead;
        public static event Action OnSetFile, OnSaveDeleted;
        static Dictionary<string, object> Cache
        {
            get { if (!Slots.ContainsKey(CurrentSlot)) Slots[CurrentSlot] = new Dictionary<string, object>(); return Slots[CurrentSlot]; }
        }
        public static bool KeyExisits(string key) { return Cache.ContainsKey(key); }
        public static T Load<T>(string key)
        {
            if (pendingRead == key) { pendingRead = null; throw new InvalidOperationException("readback"); }
            return Cache.ContainsKey(key) ? (T)Cache[key] : default(T);
        }
        public static void Save<T>(string key, T value)
        {
            if (FailWrite == key) { FailWrite = null; throw new InvalidOperationException("write"); }
            Cache[key] = value;
            if (FailReadback == key) { pendingRead = key; FailReadback = null; }
        }
        public static void Switch(int slot) { CurrentSlot = slot; OnSetFile?.Invoke(); }
        public static void Delete() { Slots.Remove(CurrentSlot); OnSaveDeleted?.Invoke(); }
        public static void Reset() { Slots.Clear(); IsSaving = false; CurrentSlot = 0; FailWrite = FailReadback = pendingRead = null; }
    }
}
public class LevelManager
{
    public static LevelManager Instance;
    public bool IsBaseLevel, IsRaidMap;
    public static bool AfterInit;
    public static event Action OnAfterLevelInitialized;
    public static void Ready() { AfterInit = true; OnAfterLevelInitialized?.Invoke(); }
}
public static class RaidUtilities
{
    public struct RaidInfo { public bool valid, ended, dead; public uint ID; }
    public static RaidInfo CurrentRaid;
    public static event Action<RaidInfo> OnRaidEnd;
    public static void End() { var r = CurrentRaid; r.ended = true; CurrentRaid = r; OnRaidEnd?.Invoke(r); }
}
public class Health
{
    readonly Item owner;
    public Health(Item item) { owner = item; }
    public float CurrentHealth = 100;
    public float MaxHealth { get { return owner.GetStat("MaxHealth").Value; } }
    public void SetHealth(float value) { CurrentHealth = value; }
}
public class CharacterMainControl : UnityEngine.Component
{
    public static CharacterMainControl Main;
    public Item CharacterItem;
    public Health Health;
}
public class BaseBGMSelector : UnityEngine.Component
{
    public struct Entry { public string musicName, author, switchName, filePath; }
    public List<Entry> entries = new List<Entry>();
}
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { Add, PercentageAdd = 100 }
    public class Modifier
    {
        public readonly ModifierType Type;
        public readonly float Value;
        public Modifier(ModifierType type, float value, object source) { Type = type; Value = value; }
    }
    public class Stat
    {
        public float BaseValue;
        public bool Reject;
        public readonly List<Modifier> Modifiers = new List<Modifier>();
        public float Value { get { return (BaseValue + Modifiers.Where(m => m.Type == ModifierType.Add).Sum(m => m.Value))
            * (1 + Modifiers.Where(m => m.Type == ModifierType.PercentageAdd).Sum(m => m.Value)); } }
        public void AddModifier(Modifier m) { if (Reject) throw new InvalidOperationException("stat"); Modifiers.Add(m); }
        public void RemoveModifier(Modifier m) { Modifiers.Remove(m); }
    }
}
namespace ItemStatsSystem
{
    public class Item : UnityEngine.Component
    {
        public int TypeID, MaxStackCount = 20, Value, Quality;
        public string DisplayNameRaw;
        public string DescriptionRaw { get { return DisplayNameRaw + "_Desc"; } }
        private UsageUtilities usageUtilities;
        public UsageUtilities UsageUtilities { get { return usageUtilities; } }
        public bool Stackable { get { return MaxStackCount > 1; } }
        private int count = 1;
        public int StackCount { get { return count; } set { count = Math.Max(0, Math.Min(MaxStackCount, value)); } }
        public void SetInt(string key, int value, bool notify) { count = value; }
        public void SetTypeID(int value) { TypeID = value; }
        public readonly Dictionary<string, Stat> Stats = new Dictionary<string, Stat>();
        public Stat GetStat(string name) { Stat s; return Stats.TryGetValue(name, out s) ? s : null; }
    }
    public struct ItemMetaData { public int id, quality; }
    public static class ItemAssetsCollection
    {
        public static readonly Dictionary<int, Item> Prefabs = new Dictionary<int, Item>();
        public static Item GetPrefab(int id) { Item item; return Prefabs.TryGetValue(id, out item) && item != null ? item : null; }
        public static void AddDynamicEntry(Item item) { Prefabs[item.TypeID] = item; }
        public static ItemMetaData GetMetaData(int id) { Item item = GetPrefab(id); return item == null ? default(ItemMetaData) : new ItemMetaData { id = id, quality = item.Quality }; }
    }
    public class UtilityBase : UnityEngine.Component { private Item master; public Item Master { get { return master; } } }
    public class UsageUtilities : UtilityBase
    {
        public List<UsageBehavior> behaviors;
        public UnityEngine.Events.UnityEvent OnItemUsedEvent;
        public bool useDurability, hasSound;
        public float durabilityUsage, useTime;
        public string actionSound, useSound;
    }
    public abstract class UsageBehavior : UnityEngine.Component
    {
        public struct DisplaySettingsData { public bool display; public string description; }
        public abstract DisplaySettingsData DisplaySettings { get; }
        public abstract bool CanBeUsed(Item item, object user);
        protected abstract void OnUse(Item item, object user);
        public void Use(Item item) { if (CanBeUsed(item, null)) OnUse(item, null); }
    }
}
namespace Duckov.Crops
{
    public struct CropInfo { public string id; public int resultPoor, resultNormal, resultGood, resultAmount; public long totalGrowTicks; }
    public struct SeedInfo { public int itemTypeID; public Duckov.Utilities.RandomContainer<string> cropIDs; }
    public class CropDatabase
    {
        public List<CropInfo> entries = new List<CropInfo>();
        public List<SeedInfo> seedInfos = new List<SeedInfo>();
    }
}
namespace Duckov.Utilities
{
    public class RandomContainer<T> { public readonly List<T> Entries = new List<T>(); public void AddEntry(T value, float weight) { Entries.Add(value); } }
    public static class GameplayDataSettings { public static Duckov.Crops.CropDatabase CropDatabase; }
}
namespace Duckov.UI { public static class NotificationText { public static void Push(string message) { } } }
namespace BossRush
{
    internal struct SceneRuntimeContext { public string SceneName; }
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnStart() { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float dt, float udt) { }
        public virtual void OnDestroy() { }
    }
    public class ModBehaviour
    {
        public static ModBehaviour Instance;
        public bool Enabled = true, UnlockAll;
        public int Buildings, BuildingResets;
        public bool IsBackMountainConfiguredEnabled() { return Enabled; }
        public bool IsBackMountainUnlockAllConfigured() { return UnlockAll; }
        public static bool IsBaseHubSceneName(string name) { return name == "Base"; }
        public void InitBackMountainShowcase() { Buildings++; }
        public void NotifyShowcaseSlotChanged() { BuildingResets++; }
        public void ShowMessage(string message) { }
        public static void DevLog(string message) { }
        public static void CriticalLog(string message) { }
    }
    public static partial class BossRushItemIds { public const int BossRushTicket = 500001, RelicEgg = 500059, PortableSafeZoneDevice = 500058, ZombieTideBeacon = 500040, ZombieTideInvitation = 500041; }
    public static class ItemFactory
    {
        public static readonly Dictionary<int, Action<Item>> Configurators = new Dictionary<int, Action<Item>>();
        public static void RegisterConfigurator(int id, Action<Item> configure) { Configurators[id] = configure; }
        public static Item GetLoadedItem(int id) { return ItemAssetsCollection.GetPrefab(id); }
    }
    public static class EquipmentHelper
    {
        public static readonly List<KeyValuePair<int, string>> Tagged = new List<KeyValuePair<int, string>>();
        public static void AddTagToItem(Item item, string tag) { Tagged.Add(new KeyValuePair<int, string>(item.TypeID, tag)); }
    }
    public static class EquipmentHelperIcon { public static void TryInjectIcon(Item item, object unused, string icon) { } }
    public static class ModeFItemUsageHelper { public static void AttachToItem(Item item) { } }
    public static class L10n { public static bool Chinese = true; public static bool IsChinese { get { return Chinese; } } public static string T(string cn, string en) { return Chinese ? cn : en; } }
    public static class LocalizationHelper
    {
        public static readonly Dictionary<string, string> Entries = new Dictionary<string, string>();
        public static void InjectLocalizations(Dictionary<string, string> map) { foreach (var pair in map) Entries[pair.Key] = pair.Value; }
    }
    public static class CampaignFacilityUnlocks
    {
        public static readonly HashSet<string> Tokens = new HashSet<string>();
        public static event Action<string> OnFacilityTokenGranted;
        public static string BuildTokenForChapter(int chapter) { return "Ch" + chapter; }
        public static bool IsTokenGranted(string token) { return Tokens.Contains(token); }
        public static void Grant(int chapter) { string t = BuildTokenForChapter(chapter); Tokens.Add(t); OnFacilityTokenGranted?.Invoke(t); }
    }
    // 官方陈列柜采集与菜地工地都是 Unity 场景依赖：判据在 *Judges（已链接），这里只替身取数层的静态入口。
    internal static class ShowcaseDisplayScanner
    {
        internal static int RefreshCalls, ClearCalls;
        internal static bool LastInBase, LastUnlocked, AnyShowcaseFound;
        internal static int SubscriptionCount { get { return 0; } }
        internal static void RefreshForScene(bool inBaseScene, bool unlocked) { RefreshCalls++; LastInBase = inBaseScene; LastUnlocked = unlocked; }
        internal static void ClearSubscriptions() { ClearCalls++; }
        internal static void FlushIfDirty() { }
        internal static void ResetStaticCaches() { RefreshCalls = ClearCalls = 0; }
    }
    internal static class GardenConstructionSite
    {
        internal static string PendingNotice;
        internal static bool Built;
        internal static int OpenCalls;
        internal static bool LastUnlocked, LastInBase, LastCropsInjected;
        internal static void NotifySceneChanged(int generation) { }
        internal static void EnsureSiteOpen(int generation, bool moduleEnabled, bool gardenUnlocked, bool inBaseScene, bool cropsInjected)
        { OpenCalls++; LastUnlocked = gardenUnlocked; LastInBase = inBaseScene; LastCropsInjected = cropsInjected; }
        internal static bool IsGardenBuilt() { return Built; }
        internal static void ResetStaticCaches() { PendingNotice = null; Built = false; OpenCalls = 0; }
    }
    internal enum CampaignObjectiveKind { Unknown = 0, GardenBuilt = 10, TrophyDisplayed = 11 }
    internal static class CampaignBaseObjectives
    {
        internal static readonly Dictionary<CampaignObjectiveKind, Func<bool>> Providers = new Dictionary<CampaignObjectiveKind, Func<bool>>();
        internal static void RegisterProvider(CampaignObjectiveKind kind, Func<bool> provider) { Providers[kind] = provider; }
        internal static void UnregisterProvider(CampaignObjectiveKind kind) { Providers.Remove(kind); }
        internal static bool IsDone(CampaignObjectiveKind kind) { Func<bool> p; return Providers.TryGetValue(kind, out p) && p(); }
    }
    internal static class DialogueManager { public static bool IsDialogueActive; }
    internal static class BossRushUI
    {
        internal static bool Hidden, Paused;
        internal static bool IsOfficialHudHidden() { return Hidden; }
        internal static bool IsGamePaused() { return Paused; }
    }
    internal static class BossRushDynamicItemRegistry
    {
        internal static readonly List<int> Published = new List<int>();
        internal static int[] GetPublishedTypeIds() { var ids = Published.ToArray(); Array.Sort(ids); return ids; }
    }
    internal static class BossBgmCoordinator
    {
        public static readonly List<BossBgmJukeboxEntry> Tracks = new List<BossBgmJukeboxEntry>();
        public static IList<BossBgmJukeboxEntry> GetJukeboxTracks() { return Tracks; }
        public static string ResolveSoundPath(string file) { return file; }
    }
    public class ZombieModeAttributeModifierRecord { public Item CharacterItem; public Stat Stat; public Modifier Modifier; public string StatName; }
    public static class ZombieModeStatNames
    {
        public const string GunDamageMultiplier = "GunDamageMultiplier", MeleeDamageMultiplier = "MeleeDamageMultiplier",
            RunSpeed = "RunSpeed", WalkSpeed = "WalkSpeed", ReloadSpeedGain = "ReloadSpeedGain", ElementFactorPhysics = "ElementFactor_Physics", MaxHealth = "MaxHealth";
    }
}
