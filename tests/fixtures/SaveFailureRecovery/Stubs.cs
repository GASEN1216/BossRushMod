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
        public int TypeID, StackCount = 1, Quality = 3, Value = 100;
        public string DisplayName;
        public bool Sticky;
        public bool IsBeingDestroyed { get { return Destroyed; } }
        public UnityEngine.Sprite Icon;
        public Item Parent;
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
        public List<Item> GetAllChildren(bool self, bool unused)
        {
            List<Item> items = new List<Item>();
            if (self) items.Add(this);
            if (Inventory != null) foreach (Item item in Inventory) if (item != null) items.AddRange(item.GetAllChildren(true, false));
            return items;
        }
        public void Detach()
        {
            if (Parent != null && Parent.Inventory != null) Parent.Inventory.Remove(this);
            Parent = null;
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
    class ModeHRunState { public long RunSeed; }
    partial class ModeHRuntimeModule
    {
        private ModeHRunState _runState;
        public ModeHRuntimeModule(long seed) { _runState = new ModeHRunState { RunSeed = seed }; }
        public void Settle(bool won) { SettleReservedBet(ModeHCashBetService.Current, won); }
    }
}
