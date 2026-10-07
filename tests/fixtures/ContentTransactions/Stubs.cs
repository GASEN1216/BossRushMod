using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using ItemStatsSystem;
using Saves;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object child in go.Children) Destroy(child);
        }
    }
    public class GameObject : Object { public readonly List<Object> Children = new List<Object>(); }
    static class Debug { public static void LogWarning(string value) { } }
    static class Time { public static int frameCount; public static float unscaledTime; }
    // 2026-09-23：遗种巢卡片数据带一张图（PetNestCardData.Icon，UA-10）；事务逻辑不碰它，替身只要能编译。
    class Sprite { }
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
    static partial class SavesSystem
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
        public static void SetFile(int slot) { CurrentSlot = slot; Cache.Clear(); OnSetFile?.Invoke(); }
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
            SaveDiskIfRequested();
        }
        public static void Reset()
        {
            IsSaving = StickSavingOnFailure = false; CurrentSlot = 0; Writes = 0; FailPhysical = 0; FailKey = null;
            DiskPath = null;
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
    public static bool ThrowBeforePack, ThrowBeforeBuffer, ThrowAfterBuffer;
    public static Action DuringDelivery;
    public static bool SendToPlayerCharacter(Item item, bool dontMerge)
    {
        if (ThrowBeforePack) throw new InvalidOperationException("pack unavailable");
        bool added = CharacterMainControl.Main.CharacterItem.Inventory.AddItem(item);
        if (DuringDelivery != null) DuringDelivery();
        return added;
    }
    public static void SendToPlayer(Item item, bool dontMerge, bool storage)
    {
        if (!SendToPlayerCharacter(item, dontMerge))
        {
            if (storage) SendToPlayerStorage(item, false);
            else item.Drop(CharacterMainControl.Main, true);
        }
    }
    public static void SendToPlayerStorage(Item item, bool directToBuffer)
    {
        if (ThrowBeforeBuffer) throw new InvalidOperationException("buffer unavailable");
        if (!directToBuffer && PlayerStorage.Inventory != null && PlayerStorage.Inventory.AddItem(item)) return;
        PlayerStorage.IncomingItemBuffer.Add(ItemStatsSystem.Data.ItemTreeData.FromItem(item));
        item.Detach(); item.DestroyTree();
        if (DuringDelivery != null) DuringDelivery();
        if (ThrowAfterBuffer) throw new InvalidOperationException("buffer notification failed");
    }
}
class Health { public float CurrentHealth = 100, MaxHealth = 100; public void SetHealth(float health) { CurrentHealth = health; } }
class CharacterMainControl { public static CharacterMainControl Main; public Item CharacterItem; public Health Health = new Health(); }
static class PetProxy { public static Inventory PetInventory; }
static class LevelConfig { public static bool SavePet = true; }
class PlayerStorage
{
    public static readonly List<ItemStatsSystem.Data.ItemTreeData> IncomingItemBuffer = new List<ItemStatsSystem.Data.ItemTreeData>();
    public static PlayerStorage Instance = new PlayerStorage(); public static bool Loading;
    public static Inventory Inventory; public bool HasInitialized() { return true; }
}
class PlayerStorageBuffer
{
    public static PlayerStorageBuffer Instance = new PlayerStorageBuffer();
    public static void SaveBuffer()
    {
        SavesSystem.Save("Courier", 0);
        SavesSystem.Save("PlayerStorage_Buffer", new List<ItemStatsSystem.Data.ItemTreeData>(PlayerStorage.IncomingItemBuffer));
    }
}
namespace ItemStatsSystem.Data
{
    public class ItemTreeData
    {
        public static event Action<Item> OnItemLoaded;
        public int rootInstanceID, RootTypeID, Count;
        public List<DataEntry> entries = new List<DataEntry>();
        public sealed class DataEntry
        {
            public int instanceID, typeID;
            public List<Duckov.Utilities.CustomData> variables = new List<Duckov.Utilities.CustomData>();
            public List<SlotInstanceIDPair> slotContents = new List<SlotInstanceIDPair>();
            public List<InventoryDataEntry> inventory = new List<InventoryDataEntry>();
            public List<int> inventorySortLocks = new List<int>();
        }
        // 刻意与官方一致：连接类不带 Serializable，生产 codec 不依赖 Unity 自动序列化。
        public sealed class SlotInstanceIDPair
        {
            public string slot; public int instanceID;
            public SlotInstanceIDPair(string slot, int id) { this.slot = slot; instanceID = id; }
        }
        public sealed class InventoryDataEntry
        {
            public int position, instanceID;
            public InventoryDataEntry(int position, int id) { this.position = position; instanceID = id; }
        }
        public static ItemTreeData FromItem(Item item) { return new ItemTreeData { rootInstanceID = item.GetInstanceID(), RootTypeID = item.TypeID, Count = item.StackCount }; }
    }
}
namespace Duckov.Utilities
{
    public enum CustomDataType { Raw = 0, Float = 1, Int = 2, Bool = 3, String = 4 }
    public sealed class CustomData
    {
        private readonly byte[] bytes;
        public string Key { get; private set; }
        public CustomDataType DataType { get; private set; }
        public bool Display { get; set; }
        public CustomData(string key, CustomDataType type, byte[] raw)
        { Key = key; DataType = type; bytes = (byte[])raw.Clone(); }
        public byte[] GetRawCopied() { return (byte[])bytes.Clone(); }
    }
    public sealed class CustomDataCollection
    {
        public static string ThrowOnKey;
        public readonly List<CustomData> Entries = new List<CustomData>();
        public void SetRaw(string key, CustomDataType type, byte[] raw, bool create, bool display)
        { if (key == ThrowOnKey) throw new InvalidOperationException("injected variable failure"); Entries.Add(new CustomData(key, type, raw) { Display = display }); }
    }
}
namespace ItemStatsSystem
{
    static class ItemAssetsCollection
    {
        public static object Instance = new object();
        public static bool FailInstantiate;
        public static int MissingPrefabId, InstantiateCalls;
        public static readonly List<Item> Instances = new List<Item>();
        public static Item GetPrefab(int id) { return id == MissingPrefabId ? null : new Item { TypeID = id }; }
        // 官方已知条目缺 prefab 时返回同 TypeID 非 null 空壳，不能用 null 替身掩盖它。
        public static Item InstantiateSync(int id)
        {
            InstantiateCalls++;
            if (FailInstantiate) return null;
            var item = new Item { TypeID = id, Lineage = null, IsFallback = id == MissingPrefabId };
            Instances.Add(item); return item;
        }
        public static ItemMetaData GetMetaData(int id) { return new ItemMetaData { id = id, quality = id >= 200 ? 8 : 5 }; }
    }
    struct ItemMetaData { public int id, quality; public string DisplayName { get { return "#" + id; } } }
    public class Slot
    {
        public Item Content; public string Key; public bool Reject;
        public bool Plug(Item item, out Item displaced)
        {
            displaced = null; if (Reject) return false;
            if (Content != null && Content.Stackable && Content.TypeID == item.TypeID)
            { Content.Combine(item); return item.StackCount == 0; }
            displaced = Content; Content = item; item.PluggedIntoSlot = this; return true;
        }
        public Item Unplug() { Item previous = Content; Content = null; if (previous != null) previous.PluggedIntoSlot = null; return previous; }
    }
    public class SlotCollection : List<Slot>
    {
        public Slot GetSlot(string key) { return Find(slot => slot.Key == key); }
    }
    public class Item : UnityEngine.Object
    {
        public T GetComponent<T>() where T : class { return null; }
        public int TypeID = 500059, MaxStackCount = 20, Quality;
        public string Lineage = "test";
        public bool FailSaveOnce, IsFallback, Stackable = true;
        private static int nextId;
        private readonly int instanceId = ++nextId;
        public bool IsBeingDestroyed { get { return Destroyed; } }
        public object PluggedIntoSlot;
        public AgentUtilities AgentUtilities;
        public Inventory InInventory, Inventory;
        public SlotCollection Slots;
        public Duckov.Utilities.CustomDataCollection Variables = new Duckov.Utilities.CustomDataCollection();
        public void CreateSlotsComponent() { Slots = new SlotCollection(); }
        public void CreateInventoryComponent() { Inventory = new Inventory(); }
        int count = 1;
        // Real StackCount setter clamps to max; raw Count KV deliberately does not.
        public int StackCount { get { return count; } set { count = Math.Max(0, Math.Min(MaxStackCount, value)); } }
        public void SetInt(string key, int value, bool notify) { if (key == "Count") count = value; }
        public void Save(string key)
        {
            if (FailSaveOnce) { FailSaveOnce = false; throw new InvalidOperationException("injected item failure"); }
            SavesSystem.Save(key, Inventory != null ? Inventory.Count : 0);
            SavesSystem.Save(key + "_counts", Inventory != null ? Inventory.Counts() : new Dictionary<int, int>());
        }
        public void DestroyTree()
        {
            if (Destroyed) return;
            Destroyed = true;
            if (Slots != null) foreach (Slot slot in Slots) if (slot.Content != null) slot.Content.DestroyTree();
            if (Inventory != null) foreach (Item child in Inventory) if (child != null) child.DestroyTree();
        }
        public int GetInstanceID() { return instanceId; }
        public void Detach() { if (InInventory != null) InInventory.RemoveItem(this); PluggedIntoSlot = null; AgentUtilities = null; }
        internal void Drop(CharacterMainControl player, bool random) { AgentUtilities = new AgentUtilities { ActiveAgent = new object() }; }
        public void Combine(Item other) { int take = Math.Min(MaxStackCount - StackCount, other.StackCount); StackCount += take; other.StackCount -= take; }
    }
    public class AgentUtilities { public object ActiveAgent; }
    public class Inventory : IEnumerable<Item>
    {
        public bool Loading;
        public int Capacity = 64;
        public List<int> lockedIndexes = new List<int>();
        public void SetCapacity(int capacity) { Capacity = capacity; }
        public void LockIndex(int index) { if (!lockedIndexes.Contains(index)) lockedIndexes.Add(index); }
        public bool Reject;
        public Action<Item> AfterAdd, AfterRemove;
        public Dictionary<int, Item> Items = new Dictionary<int, Item>();
        public List<Item> Content
        {
            get
            {
                int length = 0; foreach (int key in Items.Keys) length = Math.Max(length, key + 1);
                var list = new List<Item>(); for (int i = 0; i < length; i++) list.Add(GetItemAt(i)); return list;
            }
        }
        public int Count { get { return Items.Count; } }
        public int GetIndex(Item item) { foreach (var p in Items) if (ReferenceEquals(p.Value, item)) return p.Key; return -1; }
        public bool RemoveAt(int index, out Item item)
        {
            if (!Items.TryGetValue(index, out item)) return false;
            Items.Remove(index); item.InInventory = null; if (AfterRemove != null) AfterRemove(item); return true;
        }
        public bool AddAt(Item item, int index)
        {
            if (Reject || Items.ContainsKey(index) || index >= Capacity) return false;
            Items[index] = item; item.InInventory = this; if (AfterAdd != null) AfterAdd(item); return true;
        }
        public Item GetItemAt(int index) { Item item; return Items.TryGetValue(index, out item) ? item : null; }
        public bool AddItem(Item item) { for (int i = 0; i < Capacity; i++) if (!Items.ContainsKey(i)) return AddAt(item, i); return false; }
        public bool RemoveItem(Item item) { Item ignored; return RemoveAt(GetIndex(item), out ignored); }
        public Dictionary<int, int> Counts()
        {
            var counts = new Dictionary<int, int>();
            foreach (Item item in Items.Values) if (item != null) { int before; counts.TryGetValue(item.TypeID, out before); counts[item.TypeID] = before + item.StackCount; }
            return counts;
        }
        public void Save(string key) { SavesSystem.Save(key, Count); SavesSystem.Save(key + "_counts", Counts()); }
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
namespace ItemStatsSystem.Items
{
    public class Slot : ItemStatsSystem.Slot
    {
        public Slot(string key) { Key = key; }
    }
}
namespace BossRush
{
    internal struct SkyIslandIngredient { internal int TypeId, Count; internal SkyIslandIngredient(int id, int count) { TypeId = id; Count = count; } }
    internal static class BossRushDynamicItemRegistry
    {
        internal static bool IsBossRushDynamicItemType(int id) { return false; }
        internal static bool EnsureRegistered(int id) { return true; }
    }
    class ModBehaviour
    {
        public static bool DevModeEnabled = true;
        public static ModBehaviour Instance = new ModBehaviour();
        public static void DevLog(string text) { }
        public static void CriticalLog(string text) { throw new Exception(text); }
        public void ShowMessage(string text) { }
        public void CleanupCampaignFinalBoss(bool destroyBoss) { }
        public bool IsBackMountainConfiguredEnabled() { return true; }
    }
    static class L10n { public static bool IsChinese { get { return false; } } public static string T(string cn, string en) { return en; } }
    static class PetNestLocalization { public static string DescribeFailure(string reason) { return reason; } }
    internal static partial class PetNestUIPages
    {
        internal static int DepartCardRequests;
        private static string T(string key) { return key; }
        private static PetNestCardData BuildExpeditionCard(PetNestExpeditionRecord record) { return new PetNestCardData(); }
        private static void AppendDepartCards(PetNestPageContent page, PetNestPetRecord pet, Action refresh) { DepartCardRequests++; }
        // 远征页的选崽区（2026-09-24）：只在面板传了换人回调时画，本夹具传 null，替身不参与断言
        private static void AppendPetPicker(PetNestPageContent page, string selectedPetId, Action<string> select, Action refresh) { }
    }

    // 2026-09-20：席位变化的表现层同步入口。本夹具只验数据事务，
    // 生成 / 回收随从是 Unity 侧行为，这里用记次数的替身，保证生产方法体原样可编译可执行。
    internal static class PetNestBaseIdleSpawner
    {
        internal static int DeployNotifications;
        internal static void NotifyDeployedPetChanged() { DeployNotifications++; }
    }

    internal static class PetNestCompanionRuntime
    {
        internal static string ActiveCompanionPetId;
        internal static int Cleanups;
        internal static void CleanupOnce() { Cleanups++; ActiveCompanionPetId = null; }
    }
    // 背包实体和官方 UI 在本夹具外；这里只让真实保存协调器执行其资产采集边界。
    internal static class PetNestBackpack
    {
        internal static bool CollectAll(out string error) { error = null; return true; }
        internal static bool TryGetLiveItemCount(string id, out int count) { count = 0; return false; }
        internal static void ResetStaticCaches() { }
        internal static void DiscardForSlotChange() { }
        internal static void NotifyPhysicalSaveSucceeded() { }
    }
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
    static class CampaignDialoguePlayer { public static void InvalidatePlayback() { } }
    static class DailyReportTuning { public const string LogPrefix = "DailyReport"; }
    class DailyReportData
    {
        public bool BountyCompleted, BountyRewardClaimed; public string BountyKindId; public int BountyDayIndex, BountyTarget; public long BountyCashReward, PendingBountyCash;
        public DailyReportData Clone() { return (DailyReportData)MemberwiseClone(); }
    }
    class DailyReportBountyDef { public string Id = "bounty"; public long CashReward = 700; }
    static class DailyReportBounty { public static DailyReportBountyDef Rebuild(string id, int target) { return new DailyReportBountyDef(); } public static DailyReportBountyDef SelectForDay(long seed, int day) { return new DailyReportBountyDef(); } }
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
    class PetNestLineageInfo { public string DisplayName = "test"; public string LineageKey = "test"; public ElementTypes Element = ElementTypes.electricity; }
    static class PetNestLineageCatalog
    {
        // 2026-09-24：PetNestHatchService.CountCondensable（孵化页签徽标、交互菜单显隐）遍历全部血脉
        public static readonly IList<PetNestLineageInfo> All = new List<PetNestLineageInfo> { new PetNestLineageInfo() };
        public static bool TryGet(string key, out PetNestLineageInfo info) { info = IsKnownLineage(key) ? new PetNestLineageInfo() : null; return info != null; }
        public static bool IsKnownLineage(string key) { return key == "test" || (key != null && key.StartsWith("test_", StringComparison.Ordinal)); }
        public static ElementTypes GetDestinationElement(string id) { return ElementTypes.electricity; }
    }
    static class RelicEggConfig
    {
        public const int TYPE_ID = 500059;
        public static bool FailStamp;
        public static string ReadLineage(Item item) { return item.Lineage; }
        public static bool TryStampLineage(Item item, string lineage) { if (FailStamp) return false; item.Lineage = lineage; return true; }
    }
    static class PetNestProgressionService { public static void AddExp(PetNestPetRecord pet, int exp) { pet.exp += exp; } }
    static class PetNestDownedHandler { public static void AppendScar(PetNestPetRecord pet, string id, string reason) { } }
    static class BossRushAchievementManager
    {
        public static HashSet<string> Unlocked = new HashSet<string>();
        public static Dictionary<string, int> Grants = new Dictionary<string, int>();
        public static bool Initialized;
        public static void Initialize() { Initialized = true; }
        public static void Reset() { Unlocked.Clear(); Grants.Clear(); Initialized = false; }
        public static bool TryUnlock(string id)
        {
            if (!Initialized || !Unlocked.Add(id)) return false;
            int count; Grants.TryGetValue(id, out count); Grants[id] = count + 1; return true;
        }
    }
    static class BossRushQualityItemPool
    {
        public static int[] GetCandidates(int quality) { return new int[] { 101 }; }
        public static void ResetStaticCaches() { }
    }
    static class BackMountainItems
    {
        public class Definition { public bool IsSeed; public string NameCN = "餐", NameEN = "meal"; }
        public static Definition GetDefinition(int id) { return id >= 500065 && id <= 500067 ? new Definition() : null; }
    }
    enum BackMountainFacility { Showcase }
    static class BackMountainUnlocks { public static bool IsFacilityUnlocked(BackMountainFacility facility) { return true; } }
    class BossRushStatModifierRecord { }
    static class ZombieModeStatNames { public const string MaxHealth = "MaxHealth"; }
    static class RuntimeStatModifierTracker
    {
        public static void RemoveAll(List<BossRushStatModifierRecord> records, string label) { records.Clear(); }
        public static void TryAdd(CharacterMainControl main, string stat, float amount, object source, List<BossRushStatModifierRecord> records, string label) { records.Add(new BossRushStatModifierRecord()); }
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
    // 本夹具只隔离使用动作成功/拒绝/异常，不模拟变身行为。
    // 完整生产变身、装备与碰撞器回归见 BackMountainMorph。
    internal static class BackMountainBossMorphService
    {
        internal static bool Reject, Throw;
        internal static int Started;
        internal static void Clear() { }
        internal static bool CanUse { get { return !SceneLoader.IsSceneLoading; } }
        internal static bool TryBegin(int typeId, ModBehaviour owner)
        {
            if (Throw) throw new InvalidOperationException("injected morph failure");
            if (Reject || !CanUse) return false;
            if (typeId != BossRushItemIds.DragonFruit && typeId != BossRushItemIds.EmberChili
                && typeId != BossRushItemIds.PhantomMushroom) return false;
            Started++; return true;
        }
    }
}

static class SceneLoader { public static bool IsSceneLoading; }

namespace BossRush { static class BossRushItemIds { public const int DragonFruit = 500065, EmberChili = 500066, PhantomMushroom = 500067; } }

// Attributes only; signature/IL binding is separately checked against the installed game DLL.
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
}
class CA_UseItem { public CharacterMainControl characterController; }
