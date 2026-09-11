using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using ItemStatsSystem;
using Saves;

namespace UnityEngine
{
    static class Time { public static int frameCount; }
    static class Random { public static float value = 0; public static int Range(int min, int max) { return min; } }
    static class Mathf { public static int Min(int a, int b) { return Math.Min(a, b); } public static float Clamp01(float v) { return Math.Max(0, Math.Min(1, v)); } }
    static class JsonUtility
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public static string ToJson(object value) { return JsonSerializer.Serialize(value, value.GetType(), Options); }
        public static T FromJson<T>(string value) { return JsonSerializer.Deserialize<T>(value, Options); }
    }
}
namespace Saves
{
    static class SavesSystem
    {
        public static bool IsSaving, StickSavingOnFailure;
        public static int CurrentSlot = 0, Writes, FailPhysical;
        public static string FailKey, FailReadAfterSaveKey;
        static string pendingReadFailure;
        public static Dictionary<string, object> Cache = new Dictionary<string, object>();
        public static Dictionary<string, object> Disk = new Dictionary<string, object>();
        public static List<Dictionary<string, object>> History = new List<Dictionary<string, object>>();
        public static event Action OnCollectSaveData, OnSetFile, OnSaveDeleted;
        public static void Collect() { OnCollectSaveData?.Invoke(); }
        public static bool KeyExisits(string key) { return Cache.ContainsKey(key); }
        public static T Load<T>(string key)
        {
            if (pendingReadFailure == key) { pendingReadFailure = null; throw new InvalidOperationException("injected readback failure"); }
            return Cache.ContainsKey(key) ? (T)Cache[key] : default(T);
        }
        public static void Save<T>(string key, T value)
        {
            if (key == FailKey) { FailKey = null; throw new InvalidOperationException("injected key failure"); }
            Cache[key] = value;
            if (FailReadAfterSaveKey == key) { pendingReadFailure = key; FailReadAfterSaveKey = null; }
        }
        // Matches official SaveFile: persist cached keys; it does not collect live data.
        public static void SaveFile(bool writeSaveTime)
        {
            if (FailPhysical > 0)
            {
                FailPhysical--;
                if (StickSavingOnFailure) IsSaving = true;
                throw new InvalidOperationException("injected file failure");
            }
            Writes++;
            Disk = new Dictionary<string, object>(Cache);
            History.Add(Disk);
        }
        public static void Reset()
        {
            IsSaving = StickSavingOnFailure = false; CurrentSlot = 0; Writes = 0; FailPhysical = 0; FailKey = null;
            Cache.Clear(); Disk.Clear(); History.Clear();
            FailReadAfterSaveKey = pendingReadFailure = null;
        }
    }
}
namespace Duckov.Economy
{
    class Cost { public long money; public Cost(long amount) { money = amount; } }
    class EconomyManager
    {
        public static EconomyManager Instance = new EconomyManager();
        public static long Money;
        public static int Adds;
        public static bool RejectAdd, RejectPay;
        public struct SaveData { public long money; }
        public object GenerateSaveData() { return new SaveData { money = Money }; }
        // Official Add mutates live Money and returns false if Instance is absent.
        public static bool Add(long value) { if (Instance == null || RejectAdd) return false; Money += value; Adds++; return true; }
        public static bool Pay(Cost cost, bool account, bool cash) { if (RejectPay || Money < cost.money) return false; Money -= cost.money; return true; }
    }
}
namespace Duckov.UI { static class NotificationText { public static void Push(string value) { } } }
enum ElementTypes { electricity, poison, ice }
class LevelManager { public static LevelManager Instance = new LevelManager(); public static bool AfterInit = true; public bool IsBaseLevel = true; }
static class ItemUtilities
{
    public static List<Item> Delivered = new List<Item>();
    public static void SendToPlayer(Item item) { Delivered.Add(item); }
}
class Health { public float CurrentHealth = 100, MaxHealth = 100; public void SetHealth(float health) { CurrentHealth = health; } }
class CharacterMainControl { public static CharacterMainControl Main; public Item CharacterItem; public Health Health = new Health(); }
class PlayerStorage
{
    public static PlayerStorage Instance = new PlayerStorage(); public static bool Loading;
    public static Inventory Inventory; public bool HasInitialized() { return true; }
}
class PlayerStorageBuffer
{
    public static PlayerStorageBuffer Instance = new PlayerStorageBuffer();
    public static void SaveBuffer() { SavesSystem.Save("Courier", 0); }
}
namespace ItemStatsSystem
{
    static class ItemAssetsCollection
    {
        public static object Instance = new object();
        public static bool FailInstantiate;
        public static Item InstantiateSync(int id) { return FailInstantiate ? null : new Item { TypeID = id, Lineage = null }; }
        public static ItemMetaData GetMetaData(int id) { return new ItemMetaData { id = id, quality = id >= 200 ? 8 : 5 }; }
    }
    struct ItemMetaData { public int id, quality; }
    public class Slot { public Item Content; }
    public class Item
    {
        public int TypeID = 500059, MaxStackCount = 20, Quality;
        public string Lineage = "test";
        public bool Destroyed, FailSaveOnce, Stackable = true;
        public Inventory InInventory, Inventory;
        public List<Slot> Slots;
        int count = 1;
        // Real StackCount setter clamps to max; raw Count KV deliberately does not.
        public int StackCount { get { return count; } set { count = Math.Max(0, Math.Min(MaxStackCount, value)); } }
        public void SetInt(string key, int value, bool notify) { if (key == "Count") count = value; }
        public void Save(string key)
        {
            if (FailSaveOnce) { FailSaveOnce = false; throw new InvalidOperationException("injected item failure"); }
            SavesSystem.Save(key, Inventory != null ? Inventory.Count : 0);
        }
        public void DestroyTree() { Destroyed = true; }
    }
    public class Inventory : IEnumerable<Item>
    {
        public Dictionary<int, Item> Items = new Dictionary<int, Item>();
        public int Count { get { return Items.Count; } }
        public int GetIndex(Item item) { foreach (var p in Items) if (ReferenceEquals(p.Value, item)) return p.Key; return -1; }
        public bool RemoveAt(int index, out Item item)
        {
            if (!Items.TryGetValue(index, out item)) return false;
            Items.Remove(index); item.InInventory = null; return true;
        }
        public bool AddAt(Item item, int index)
        {
            if (Items.ContainsKey(index)) return false;
            Items[index] = item; item.InInventory = this; return true;
        }
        public void Save(string key) { SavesSystem.Save(key, Count); }
        public IEnumerator<Item> GetEnumerator() { return Items.Values.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
    public abstract class UsageBehavior
    {
        public struct DisplaySettingsData { public bool display; public string description; }
        public abstract DisplaySettingsData DisplaySettings { get; }
        public abstract bool CanBeUsed(Item item, object user);
        protected abstract void OnUse(Item item, object user);
        public void Use(Item item, object user) { OnUse(item, user); }
    }
}
namespace BossRush
{
    class ModBehaviour
    {
        public static bool DevModeEnabled = true;
        public static ModBehaviour Instance = new ModBehaviour();
        public static void DevLog(string text) { }
        public static void CriticalLog(string text) { throw new Exception(text); }
        public void ShowMessage(string text) { }
        public bool IsBackMountainConfiguredEnabled() { return true; }
    }
    static class L10n { public static string T(string cn, string en) { return en; } }
    static class CampaignTuning
    {
        public const string LogPrefix = "Campaign", ProgressSaveKey = "BossRush_Campaign_Progress_v1";
        public const int FirstChapter = 1;
    }
    static class CampaignContentCatalog
    {
        static CampaignChapterDef def = new CampaignChapterDef { ChapterId = "ch1", Order = 1, RewardCash = 4000 };
        public static CampaignChapterDef GetChapter(string id) { return def; }
        public static CampaignChapterDef GetChapterByOrder(int order) { return def; }
    }
    static class CampaignFacilityUnlocks
    {
        public static bool TryGrant(string key) { return true; }
        public static void ResetForSlotReload() { }
        public static void LoadGrantedTokens(IEnumerable<string> keys) { }
    }
    static class CampaignObjectiveTracker { public static void ResetSession() { } }
    static class DailyReportTuning { public const string LogPrefix = "DailyReport"; }
    class DailyReportData
    {
        public bool BountyCompleted, BountyRewardClaimed; public string BountyKindId; public int BountyDayIndex;
        public DailyReportData Clone() { return (DailyReportData)MemberwiseClone(); }
    }
    class DailyReportBountyDef { public string Id = "bounty"; public long CashReward = 700; }
    static class DailyReportBounty { public static DailyReportBountyDef SelectForDay(long seed, int day) { return new DailyReportBountyDef(); } }
    static class DailyReportStatsCollector { public static bool Suppressed; public static void SetMoneyDeltaSuppressed(bool value) { Suppressed = value; } }
    static partial class DailyReportService
    {
        private static long EnsureBountySeed(DailyReportData data) { return 123; }
        public static void SyncCarrySecondsToPersistence() { }
    }
    static partial class DailyReportRewards { }
    static partial class DailyReportPersistence
    {
        public static DailyReportData Current;
        public static bool HasPendingWrite, IsStoreFaulted, HasWriteBarrier, RejectStore;
        public static string LastError;
        public static void EnsureSubscribed() { }
        public static void ShutdownSubscription() { }
        public static void ResetStaticCaches() { HasPendingWrite = false; IsStoreFaulted = HasWriteBarrier = RejectStore = false; }
        public static bool Store(DailyReportData data)
        {
            if (RejectStore) return false;
            Current = data; HasPendingWrite = true; return true;
        }
        public static bool FlushPending() { SavesSystem.Save("BountyClaimed", Current.BountyRewardClaimed); HasPendingWrite = false; return true; }
        public static void Collect() { HandleCollectSaveData(); }
    }
    class PetNestLineageInfo { public string DisplayName = "test"; public ElementTypes Element = ElementTypes.electricity; }
    static class PetNestLineageCatalog
    {
        public static bool TryGet(string key, out PetNestLineageInfo info) { info = key == "test" ? new PetNestLineageInfo() : null; return info != null; }
        public static bool IsKnownLineage(string key) { return key == "test"; }
        public static ElementTypes GetDestinationElement(string id) { return ElementTypes.electricity; }
    }
    static class RelicEggConfig
    {
        public const int TYPE_ID = 500059;
        public static bool FailStamp;
        public static string ReadLineage(Item item) { return item.Lineage; }
        public static bool TryStampLineage(Item item, string lineage) { if (FailStamp) return false; item.Lineage = lineage; return true; }
    }
    static class BossRushDynamicItemRegistry { public static void EnsureRegistered(int id) { } }
    static class PetNestProgressionService { public static void AddExp(PetNestPetRecord pet, int exp) { pet.exp += exp; } }
    static class PetNestDownedHandler { public static void AppendScar(PetNestPetRecord pet, string id, string reason) { } }
    static class BossRushAchievementManager
    {
        public static HashSet<string> Unlocked = new HashSet<string>();
        public static void TryUnlock(string id) { Unlocked.Add(id); }
    }
    static class BackMountainItems
    {
        public class Definition { public bool IsSeed; public string NameCN = "餐", NameEN = "meal"; }
        public static Definition GetDefinition(int id) { return id == 500065 ? new Definition() : null; }
    }
    enum BackMountainFacility { Showcase }
    static class BackMountainUnlocks { public static bool IsFacilityUnlocked(BackMountainFacility facility) { return true; } }
    class ZombieModeAttributeModifierRecord { }
    static class ZombieModeStatNames { public const string MaxHealth = "MaxHealth"; }
    static class RuntimeStatModifierTracker
    {
        public static void RemoveAll(List<ZombieModeAttributeModifierRecord> records, string label) { records.Clear(); }
        public static void TryAdd(CharacterMainControl main, string stat, float amount, object source, List<ZombieModeAttributeModifierRecord> records, string label) { records.Add(new ZombieModeAttributeModifierRecord()); }
    }
    static class BackMountainConfig
    {
        public const string LogPrefix = "BackMountain", ShowcaseSaveKey = "BossRush_BackMountain_Showcase_v1";
        public const int ShowcaseSlotCount = 8;
    }
    static class RaidMealService
    {
        public static bool Reject, Throw; public static int Registered;
        public static bool RegisterMeal(int typeId)
        {
            if (Throw) throw new InvalidOperationException("injected meal failure");
            if (SavesSystem.IsSaving || Reject) return false;
            Registered++; return true;
        }
    }
}
