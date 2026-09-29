using System;
using System.Collections.Generic;
using System.IO;
using ItemStatsSystem;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            return ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed)
                && (ReferenceEquals(b, null) || b.Destroyed));
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object obj) { return ReferenceEquals(this, obj); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null)) return;
            obj.Destroyed = true;
            GameObject go = obj as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object component in go.Components) component.Destroyed = true;
        }
    }
    public class GameObject : Object { public readonly List<Object> Components = new List<Object>(); }
    public class Sprite : Object { }
    public static class Time { public static int frameCount; public static float realtimeSinceStartup; }
}

public class LevelManager { public static LevelManager Instance; public bool IsBaseLevel; }
public class GameClock { public static GameClock Instance; public float clockTimeScale; }
public class CharacterMainControl { public static CharacterMainControl Main; public Item CharacterItem; }

namespace Saves
{
    public static class SavesSystem
    {
        public static int CurrentSlot, Attempts, Writes, FailAt;
        public static bool IsSaving;
        public static string FailReadback;
        private static string pendingReadFailure;
        public static Dictionary<string, object> Cache = new Dictionary<string, object>();
        public static Dictionary<string, object> Disk = new Dictionary<string, object>();
        public static readonly List<Dictionary<string, object>> History = new List<Dictionary<string, object>>();
        public static event Action OnSetFile, OnSaveDeleted, OnCollectSaveData;
        public static bool KeyExisits(string key) { return Cache.ContainsKey(key); }
        public static T Load<T>(string key)
        {
            if (pendingReadFailure == key) { pendingReadFailure = null; throw new IOException("readback"); }
            return Cache.ContainsKey(key) ? (T)Cache[key] : default(T);
        }
        public static void Save<T>(string key, T value)
        {
            Cache[key] = value;
            if (FailReadback == key) { pendingReadFailure = key; FailReadback = null; }
        }
        public static void SaveFile(bool timestamp = true)
        {
            IsSaving = true;
            Attempts++;
            // 官方 SaveFile 没有 finally；写失败后 IsSaving 会继续为 true。
            if (Attempts == FailAt) throw new IOException("StoreCachedFile");
            Disk = new Dictionary<string, object>(Cache);
            History.Add(Disk);
            Writes++;
            IsSaving = false;
        }
        public static void Collect() { OnCollectSaveData?.Invoke(); }
        public static void ChangeSlot(int slot)
        {
            CurrentSlot = slot; Cache.Clear(); Disk.Clear(); OnSetFile?.Invoke();
        }
        public static void Reset()
        {
            Cache.Clear(); Disk.Clear(); History.Clear(); CurrentSlot = 1;
            IsSaving = false; Attempts = Writes = FailAt = 0; FailReadback = pendingReadFailure = null;
            OnSetFile = OnSaveDeleted = OnCollectSaveData = null;
        }
    }
}

namespace Duckov.Economy
{
    public class Cost { public long Amount; public Cost(long value) { Amount = value; } }
    public class EconomyManager
    {
        public static EconomyManager Instance;
        public static long Money;
        public static bool Reject, ThrowAfter;
        public class SaveData { public long money; }
        public object GenerateSaveData() { return new SaveData { money = Money }; }
        public static bool Add(long value)
        {
            if (Instance == null || Reject) return false;
            Money += value;
            if (ThrowAfter) throw new InvalidOperationException("money notification");
            return true;
        }
        public static bool Pay(Cost cost, bool a, bool b)
        {
            if (Money < cost.Amount) return false;
            return Add(-cost.Amount);
        }
    }
}

namespace ItemStatsSystem
{
    public class Inventory : List<Item>
    {
        public Item Owner;
        public new int Capacity;
        public bool ThrowBeforeRemove, ThrowAfterRemove, ThrowAfterAdd;
        public Item GetItemAt(int position) { return position >= 0 && position < Count ? this[position] : null; }
        public bool RemoveAt(int position, out Item removed)
        {
            removed = null;
            if (ThrowBeforeRemove) { ThrowBeforeRemove = false; throw new InvalidOperationException("remove before mutation"); }
            removed = GetItemAt(position);
            if (removed == null) return false;
            base.RemoveAt(position); removed.Parent = null;
            if (ThrowAfterRemove) { ThrowAfterRemove = false; throw new InvalidOperationException("remove notification"); }
            return true;
        }
        public bool AddAt(Item item, int position)
        {
            if (position != Count || !AddItem(item)) return false;
            if (ThrowAfterAdd) { ThrowAfterAdd = false; throw new InvalidOperationException("add notification"); }
            return true;
        }
        public int GetFirstEmptyPosition(int from = 0) { return Count < Capacity ? Count : -1; }
        public bool AddItem(Item item)
        {
            if (GetFirstEmptyPosition() < 0) return false;
            item.Detach(); Add(item); item.Parent = Owner; return true;
        }
    }
    public class Item : UnityEngine.Object
    {
        static int nextId;
        readonly int id = ++nextId;
        public readonly UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public int TypeID, Quality = 3, Value = 100;
        private int stackCount = 1;
        public bool ThrowBeforeStackChange, ThrowAfterStackChange, ThrowAfterDetach;
        public int StackCount
        {
            get { return stackCount; }
            set
            {
                if (ThrowBeforeStackChange) { ThrowBeforeStackChange = false; throw new InvalidOperationException("stack before mutation"); }
                stackCount = value;
                // 官方 setter 先更新 Count KV，再通知 onSetStackCount / 容器。
                if (ThrowAfterStackChange) { ThrowAfterStackChange = false; throw new InvalidOperationException("stack notification"); }
            }
        }
        public string DisplayName;
        public bool Sticky;
        public bool IsBeingDestroyed { get { return Destroyed; } }
        public UnityEngine.Sprite Icon;
        public Item Parent;
        public Item ParentItem { get { return Parent; } }
        public Inventory InInventory { get { return Parent != null ? Parent.Inventory : null; } }
        public object PluggedIntoSlot;
        public Inventory Inventory;
        public readonly Dictionary<string, string> Variables = new Dictionary<string, string>();
        public Item() { gameObject.Components.Add(this); }
        public Item GetCharacterItem() { Item item = this; while (item.Parent != null) item = item.Parent; return item; }
        public int GetInstanceID() { return id; }
        public string GetString(string key, string fallback) { return Variables.ContainsKey(key) ? Variables[key] : fallback; }
        public void SetString(string key, string value, bool create) { Variables[key] = value; }
        public int GetTotalRawValue()
        {
            int total = Value * StackCount;
            if (Inventory != null) foreach (Item item in Inventory) if (item != null) total += item.GetTotalRawValue();
            return total;
        }
        public List<Item> GetAllChildren(bool includingGrandChildren, bool excludeSelf)
        {
            List<Item> items = new List<Item>();
            if (!excludeSelf) items.Add(this);
            if (Inventory != null) foreach (Item item in Inventory)
            {
                if (item == null) continue;
                items.Add(item);
                if (includingGrandChildren) items.AddRange(item.GetAllChildren(true, true));
            }
            return items;
        }
        public void Detach()
        {
            if (Parent != null && Parent.Inventory != null) Parent.Inventory.Remove(this);
            Parent = null;
            // 官方 RemoveAt / Unplug 在通知订阅者之前已经清除所有权。
            if (ThrowAfterDetach) { ThrowAfterDetach = false; throw new InvalidOperationException("detach notification"); }
        }
        public void DestroyTree()
        {
            if (Inventory != null) foreach (Item item in Inventory.ToArray()) item.DestroyTree();
            Detach(); UnityEngine.Object.Destroy(gameObject);
        }
        public static bool FailSnapshot;
        public void Save(string key)
        {
            if (FailSnapshot) throw new IOException("asset snapshot");
            Saves.SavesSystem.Save("Item/" + key, Snapshot.Capture(this));
        }
    }
    public class Snapshot
    {
        public int Type, Count, Value, Quality, Capacity;
        public string Name;
        public Dictionary<string, string> Variables;
        public List<Snapshot> Children;
        public static Snapshot Capture(Item item)
        {
            Snapshot data = new Snapshot { Type = item.TypeID, Count = item.StackCount, Value = item.Value,
                Quality = item.Quality, Name = item.DisplayName, Variables = new Dictionary<string, string>(item.Variables) };
            if (item.Inventory != null)
            {
                data.Capacity = item.Inventory.Capacity; data.Children = new List<Snapshot>();
                foreach (Item child in item.Inventory) if (child != null) data.Children.Add(Capture(child));
            }
            return data;
        }
        public Item Restore()
        {
            Item item = new Item { TypeID = Type, StackCount = Count, Quality = Quality, Value = Value, DisplayName = Name };
            foreach (var kv in Variables) item.Variables.Add(kv.Key, kv.Value);
            if (Children != null)
            {
                item.Inventory = new Inventory { Owner = item, Capacity = Capacity };
                foreach (Snapshot child in Children) item.Inventory.AddItem(child.Restore());
            }
            return item;
        }
    }
    public static class ItemAssetsCollection
    {
        public static Item Instance;
        public static bool Available;
        public static Item GetPrefab(int id)
        {
            return Available && id == 900 ? new Item { TypeID = 900, Value = 100, DisplayName = "prize", Quality = 3 } : null;
        }
    }
}

public static class ItemUtilities
{
    public static bool ThrowBefore, ThrowAfter;
    public static int Deliveries, FailAfter;
    public static Action OnDelivery;
    public static bool SendToPlayerCharacterInventory(Item item, bool dontMerge)
    {
        if (ThrowBefore || (FailAfter >= 0 && Deliveries >= FailAfter)) throw new InvalidOperationException("delivery before transfer");
        var player = CharacterMainControl.Main;
        if (player == null || !player.CharacterItem.Inventory.AddItem(item)) return false;
        Deliveries++;
        OnDelivery?.Invoke();
        if (ThrowAfter) throw new InvalidOperationException("delivery notification");
        return true;
    }
}

namespace BossRush
{
    // 仓库桥的物品摘要是宿主边界；摘取和放回的生产方法逐字抽取执行。
    class ModeHItemTreeSnapshotDto { public int sourcePosition; public Item Expected; }
    static class ModeHItemTreeNormalizer
    {
        public static bool Matches(ModeHItemTreeSnapshotDto expected, Item item, int occurrences, out string error)
        { error = null; return ReferenceEquals(expected.Expected, item); }
    }
    static partial class ModeHInventoryPersistenceBridge
    {
        public static Inventory TestInventory;
        static Inventory TryGetInventory(out string error) { error = null; return TestInventory; }
        static int CountOccurrences(ModeHItemTreeSnapshotDto expected) { return 1; }
    }
    static class L10n { public static string T(string cn, string en) { return en; } }
    class ModBehaviour
    {
        public static ModBehaviour Instance;
        public static bool DevModeEnabled;
        public bool IsDailyReportConfiguredEnabled() { return true; }
        public static bool IsModeHRunInProgressSafe() { return false; }
        public static void DevLog(string text) { }
        public static void LogError(string text) { }
        public static void CriticalLog(string a, string b) { }
    }
    static class DailyReportRewards
    {
        public static bool TryGrantBountyCash(long amount, out string reason) { reason = null; return Duckov.Economy.EconomyManager.Add(amount); }
        public static bool TryGrantMilestone(int q, long seed, int day, int slot, out string reason) { reason = "unavailable"; return false; }
    }
    static class BossRushQualityItemPool { public static int[] GetCandidates(int q) { return q == 3 ? new[] { 900 } : new int[0]; } }
    static class ModeHRewardItemPool
    {
        public static readonly List<Item> Created = new List<Item>();
        public static Item TryInstantiate(int type, out string reason)
        {
            reason = null;
            Item item = ItemAssetsCollection.GetPrefab(type);
            if (item != null) Created.Add(item);
            return item;
        }
        public static void DestroyUngranted(Item item) { if (item != null) item.DestroyTree(); }
    }
    enum ModeHMatchOutcome { PlayerVictory = 1, PlayerDefeat = 2 }
    class ModeHRunState { public long RunSeed; public string RunId; }
    class ModeHRunStateDto { public string runId; public long runSeed; public int lifecycle; }
    // 与生产枚举同值（ModeHStateModel.cs）；只列续赛判定用到的成员
    enum ModeHLifecycle { Unknown = 0, None = 1, MatchFighting = 12, SeasonEnded = 19, Suspended = 21 }
    static class ModeHStateModel { public static ModeHLifecycle ToLifecycle(int raw) { return (ModeHLifecycle)raw; } }
    class ModeHMatchReportDto { public int matchIndex, winner; }
    class ModeHSeasonDto { public ModeHRunStateDto runState; public List<ModeHMatchReportDto> matchReports; }
    static class ModeHProfilePersistence
    {
        public static ModeHSeasonDto Saved;
        public static ModeHSeasonDto LoadCurrent() { return Saved; }
    }
    static class ModeHWarehouseStakeJournal { public static void TryRecomputeDeferredSlotConsistency() { } }
    static class ModeHSessionSummary { public static int Notes; public static void NoteBet(ModeHCashBetRecord record) { Notes++; } }
    class RuntimeModule { public virtual void OnStart() { } }
    partial class ModeHRuntimeModule : RuntimeModule
    {
        private ModeHRunState _runState;
        private ModeHSeasonDto _season;
        public bool LevelReady;
        public ModeHRuntimeModule(long seed) { _runState = new ModeHRunState { RunSeed = seed }; }
        public void Settle(bool won) { SettleReservedBet(ModeHCashBetService.Current, won); }
        public void Configure(ModeHSeasonDto season)
        { _season = season; _runState = season == null ? null : new ModeHRunState { RunId = season.runState.runId, RunSeed = 42 }; }
        public void Reconcile() { ReconcileCashBetOnRestore(); }
        public void DropRunOwner() { _runState = null; _season = null; }
        public bool ResolveBeforeAbandon() { return TryResolveCashBetBeforeAbandon(); }
        public void OnReady() { HandleLevelReady(); }
        private bool IsLevelAfterInit() { return LevelReady; }
        private void EnsureContentScanned() { }
        private void LogFailure(string tag, Exception e) { throw new Exception(tag, e); }
        private void RefundCashBet(string context) { long refunded; ModeHCashBetService.TryRefund(context, out refunded); }
    }
}
