using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b) { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object value) { if (ReferenceEquals(value, null)) return; value.Destroyed = true; var go = value as GameObject; if (!ReferenceEquals(go, null) && !ReferenceEquals(go.Item, null)) go.Item.Destroyed = true; }
    }
    public class GameObject : Object { public Item Item; }
    public class Transform : Object { }
    public static class Mathf { public static int Min(int a, int b) { return Math.Min(a, b); } public static int Max(int a, int b) { return Math.Max(a, b); } public static int CeilToInt(float x) { return (int)Math.Ceiling(x); } }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace HarmonyLib { public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } } public class HarmonyPrefix : Attribute { } }
namespace Saves
{
    public static class SavesSystem
    {
        public static int CurrentSlot = 1, Writes;
        public static Dictionary<string, object> Global = new Dictionary<string, object>();
        public static HashSet<string> BadReads = new HashSet<string>();
        public static T LoadGlobal<T>(string key, T fallback = default(T)) { if (BadReads.Contains(key)) throw new Exception("read failed"); object value; return Global.TryGetValue(key, out value) ? (T)value : fallback; }
        public static void SaveGlobal<T>(string key, T value) { Writes++; Global[key] = value; }
    }
}
namespace ItemStatsSystem
{
    public class Inventory { public List<Item> Content = new List<Item>(); public event Action<Inventory, int> onContentChanged; public bool AddItem(Item item) { Content.Add(item); item.InInventory = this; return true; } }
    public class Item : UnityEngine.Object
    {
        public readonly GameObject gameObject;
        public bool PickupCreated;
        public void Drop(CharacterMainControl player, bool rigidbody) { PickupCreated = true; }
        public void Detach() { if (InInventory != null) InInventory.Content.Remove(this); InInventory = null; }
        public int TypeID;
        public string Payload;
        public Inventory InInventory;
        public object PluggedIntoSlot;
        public string DisplayName { get { return Payload; } }
        public Item(int id, string payload) { TypeID = id; Payload = payload; gameObject = new GameObject { Item = this }; }
        public int GetTotalRawValue() { return 1000; }
        public void DestroyTree() { UnityEngine.Object.Destroy(gameObject); }
    }
}
namespace ItemStatsSystem.Data
{
    public class ItemTreeData
    {
        public int RootTypeID;
        public string Payload;
        public static readonly List<TaskCompletionSource<Item>> Pending = new List<TaskCompletionSource<Item>>();
        public static ItemTreeData FromItem(Item item) { return new ItemTreeData { RootTypeID = item.TypeID, Payload = item.Payload }; }
        public static Cysharp.Threading.Tasks.UniTask<Item> InstantiateAsync(ItemTreeData data) { var t = new TaskCompletionSource<Item>(); Pending.Add(t); return new Cysharp.Threading.Tasks.UniTask<Item> { task = t.Task }; }
    }
}
namespace Duckov.Economy
{
    public class StockShop : UnityEngine.Object { public class Entry { } }
    public class Cost { private int amount; public Cost(int n) { amount = n; } public bool Enough { get { return EconomyManager.Money >= amount; } } public void Pay() { EconomyManager.Money -= amount; } }
    public static class EconomyManager { public static int Money = 1000; public static void Add(int amount) { Money += amount; } }
}
namespace Duckov.Economy.UI
{
    public class StockShopView { public class Selection { public Duckov.Economy.StockShop.Entry Target; } public static StockShopView Instance = new StockShopView(); public Selection Selected; public Selection GetSelection() { return Selected; } }
}
namespace Duckov.UI { public static class NotificationText { public static void Push(string text) { } } }
public class CharacterMainControl : UnityEngine.Object { public static CharacterMainControl Main = new CharacterMainControl(); public Inventory Inventory = new Inventory(); }
public static class ItemUtilities
{
    public static bool FailBefore, FailAfter;
    public static int Buffered;
    public static void SendToPlayerStorage(Item item, bool buffer) { Buffered++; item.DestroyTree(); }
    public static void SendToPlayer(Item item, bool dontMerge, bool storage)
    {
        if (FailBefore) throw new Exception("delivery refused");
        item.InInventory = CharacterMainControl.Main.Inventory;
        item.InInventory.Content.Add(item);
        if (FailAfter) throw new Exception("notification failed after delivery");
    }
}
namespace BossRush
{
    public class ModBehaviour
    {
        public static ModBehaviour Instance = new ModBehaviour();
        public static int Purification = 1000;
        public static int NextSweeps;
        public void StartCoroutine(object routine) { NextSweeps++; }
        public void ShowMessage(string message) { }
        public static void DevLog(string text) { }
        public bool TrySpendZombieModePurificationPointsForRealNpc(Transform npc, int fee, string reason) { if (Purification < fee) return false; Purification -= fee; return true; }
        public void RefundZombieModePurificationPointsForRealNpc(Transform npc, int fee, bool refund) { if (refund) Purification += fee; }
    }
    public static class ReforgeDataPersistence { public static void SyncCurrentReforgeState(Item item) { } }
    public static class CustomItemRuntimeStateHelper { public static void RestoreRuntimeState(Item item, string reason) { } }
    public static class L10n { public static string T(string cn, string en) { return en; } }
    public static class LocalizationHelper { public static string GetLocalizedText(string key) { return key; } }
    public static partial class StorageDepositService
    {
        private static int depositSessionGeneration;
        private static DepositTransaction transactionOwner;
        private static bool isServiceActive, isRetrieveAllInProgress, isQuickDepositInProgress;
        private static Inventory playerInventory;
        private static GameObject shopObject;
        private static Duckov.Economy.StockShop depositShop;
        private static Transform courierNPCTransform;
        private static object courierController, courierMovement, pendingDepositItem;
        private static Dictionary<int, Item> depositItemInstances = new Dictionary<int, Item>();
        private static Dictionary<Duckov.Economy.StockShop.Entry, int> entryIndexMapping = new Dictionary<Duckov.Economy.StockShop.Entry, int>();
        private static bool purification;
        private static bool IsZombieModeTemporaryCourierPurificationService() { return purification; }
        private static string GetTemporaryCourierPurificationInsufficientText() { return "Not enough Purification"; }
        private static void RefreshShopEntries() { }
        private static void RefreshShopUI() { }
        private static void UpdateRetrieveAllButton() { }
        private static void CleanupRetrieveAllButton() { }
        private static void OnPlayerInventoryContentChanged(Inventory inventory, int index) { }
        public static void Setup(bool points = false)
        {
            Cleanup(); isServiceActive = true; purification = points; depositShop = new Duckov.Economy.StockShop(); courierNPCTransform = new Transform();
            for (int i = 0; i < DepositDataManager.GetItemCount(); i++) entryIndexMapping.Add(new Duckov.Economy.StockShop.Entry(), i);
            Select(0);
        }
        public static void Select(int index)
        {
            foreach (var entry in entryIndexMapping) if (entry.Value == index) Duckov.Economy.UI.StockShopView.Instance.Selected = new Duckov.Economy.UI.StockShopView.Selection { Target = entry.Key };
        }
        public static void CloseForTest() { Cleanup(); }
        public static Task BulkForTest() { return RetrieveAllItemsAsync(20).task; }
        public static bool BusyForTest { get { return IsTransactionBusy; } }
    }
}
namespace BossRush
{
    public static partial class NPCGiftContainerService
    {
        public static void DropForTest(Item item) { DropItemOnGround(item); }
    }
    public static partial class CourierPaidLootSweepService
    {
        private static GameObject pendingResultObject;
        private static Inventory pendingResultInventory;
        private static Transform pendingResultNpcTransform;
        private static bool HasPendingSweepResult() { return pendingResultObject != null; }
        private static void DestroyStartNextSweepButton() { }
        private static object StartNextSweepDelayed(Transform npc) { return npc; }
        private static void DiscardPendingSweepResultInternal(bool close, bool show)
        {
            if (pendingResultInventory.Content.Count != 0) throw new Exception("crate destroyed before remaining items delivered");
            UnityEngine.Object.Destroy(pendingResultObject); pendingResultObject = null;
        }
        public static Inventory SeedForTest(int count)
        {
            pendingResultObject = new GameObject(); pendingResultInventory = new Inventory(); pendingResultNpcTransform = new Transform();
            for (int i = 0; i < count; i++) pendingResultInventory.AddItem(new Item(i + 1, "sweep" + i));
            return pendingResultInventory;
        }
        public static void NextForTest() { OnStartNextSweepButtonClicked(); }
        public static bool PendingForTest { get { return HasPendingSweepResult(); } }
    }
}

class Program
{
    static int checks;
    static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
    static void Seed(bool points = false)
    {
        Saves.SavesSystem.BadReads.Clear(); Saves.SavesSystem.Global.Clear(); Saves.SavesSystem.Writes = 0;
        Saves.SavesSystem.Global["BossRush_Deposit_Items"] = new List<ItemTreeData> { new ItemTreeData { RootTypeID = 1, Payload = "gun+attachment" }, new ItemTreeData { RootTypeID = 1, Payload = "container+contents" } };
        Saves.SavesSystem.Global["BossRush_Deposit_Times"] = new List<long> { DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks };
        Saves.SavesSystem.Global["BossRush_Deposit_Values"] = new List<int> { 1000, 1000 };
        DepositDataManager.ForceReload(); ItemTreeData.Pending.Clear(); CharacterMainControl.Main = new CharacterMainControl();
        Duckov.Economy.EconomyManager.Money = ModBehaviour.Purification = 1000; ItemUtilities.FailBefore = ItemUtilities.FailAfter = false; StorageDepositService.Setup(points);
    }
    static void Main()
    {
        Seed(); Saves.SavesSystem.BadReads.Add("BossRush_Deposit_Items"); Saves.SavesSystem.BadReads.Add("BossRush_Deposit_Backup_Items"); DepositDataManager.ForceReload();
        DepositDataManager.AddItem(new Item(2, "new")); DepositDataManager.ClearAll(); DepositDataManager.Save(); Check(!DepositDataManager.CanWrite && Saves.SavesSystem.Writes == 0, "failed reads block all global writes");
        Seed(); Saves.SavesSystem.Global.Remove("BossRush_Deposit_Times"); DepositDataManager.ForceReload(); Check(!DepositDataManager.CanWrite, "partial old lists are not truncated into a writable snapshot");
        Seed(); Saves.SavesSystem.Global["BossRush_Deposit_Backup_Items"] = Saves.SavesSystem.Global["BossRush_Deposit_Items"]; Saves.SavesSystem.Global["BossRush_Deposit_Backup_Times"] = Saves.SavesSystem.Global["BossRush_Deposit_Times"]; Saves.SavesSystem.Global["BossRush_Deposit_Backup_Values"] = Saves.SavesSystem.Global["BossRush_Deposit_Values"]; Saves.SavesSystem.BadReads.Add("BossRush_Deposit_Items"); DepositDataManager.ForceReload(); Check(DepositDataManager.CanWrite && DepositDataManager.GetItemCount() == 2 && Saves.SavesSystem.Writes == 0, "read exception recovers complete backup without rewriting bytes");
        foreach (bool points in new[] { false, true })
        {
            Seed(points); var failed = StorageDepositService.RetrieveSingleAsync(1, 1).task; ItemTreeData.Pending[0].SetResult(null); Check(!failed.GetAwaiter().GetResult() && DepositDataManager.GetItemCount() == 2 && Duckov.Economy.EconomyManager.Money == 1000 && ModBehaviour.Purification == 1000, "null restoration keeps record and both currencies");
            Seed(points); var exception = StorageDepositService.RetrieveSingleAsync(1, 1).task; ItemTreeData.Pending[0].SetException(new Exception("restore failure")); Check(!exception.GetAwaiter().GetResult() && DepositDataManager.GetItemCount() == 2, "restore exception preserves original tree");
            Seed(points); ItemUtilities.FailBefore = true; var delivery = StorageDepositService.RetrieveSingleAsync(1, 1).task; var undelivered = new Item(1, "restored"); ItemTreeData.Pending[0].SetResult(undelivered); bool deliveryResult = delivery.GetAwaiter().GetResult(); Console.WriteLine("delivery failure metrics: points="+points+",result="+deliveryResult+",destroyed="+(undelivered==null)+",gold="+Duckov.Economy.EconomyManager.Money+",purification="+ModBehaviour.Purification+",records="+DepositDataManager.GetItemCount()); Check(!deliveryResult && undelivered == null && Duckov.Economy.EconomyManager.Money == 1000 && ModBehaviour.Purification == 1000 && DepositDataManager.GetItemCount() == 2, "delivery rejection refunds original currency and cleans only temporary item");
        }
        Seed(); var old = StorageDepositService.RetrieveSingleAsync(1, 1).task; StorageDepositService.CloseForTest(); StorageDepositService.Setup(); var next = StorageDepositService.RetrieveSingleAsync(1, 1).task; var late = new Item(1, "old clone"); ItemTreeData.Pending[0].SetResult(late); Check(!old.GetAwaiter().GetResult() && late == null && StorageDepositService.BusyForTest, "late old request cannot deliver or release replacement transaction"); ItemTreeData.Pending[1].SetResult(new Item(1, "gun+attachment")); Check(next.GetAwaiter().GetResult() && CharacterMainControl.Main.Inventory.Content.Count == 1 && DepositDataManager.GetAllItems()[0].itemData.Payload == "container+contents", "stable identity removes exactly the delivered record");
        Seed(); var bulk = StorageDepositService.BulkForTest(); var overlap = StorageDepositService.RetrieveSingleAsync(1, 1).task; Check(!overlap.GetAwaiter().GetResult(), "bulk owner excludes single retrieval"); ItemTreeData.Pending[0].SetResult(new Item(1, "container+contents")); ItemTreeData.Pending[1].SetResult(new Item(1, "gun+attachment")); bulk.GetAwaiter().GetResult(); Check(DepositDataManager.GetItemCount() == 0 && CharacterMainControl.Main.Inventory.Content.Count == 2 && Duckov.Economy.EconomyManager.Money == 980, "bulk delivers each full tree once at exact fee");
        Seed(); ItemUtilities.FailAfter = true; var notified = StorageDepositService.RetrieveSingleAsync(1, 1).task; var actual = new Item(1, "actual"); ItemTreeData.Pending[0].SetResult(actual); Check(notified.GetAwaiter().GetResult() && actual != null && actual.InInventory != null && DepositDataManager.GetItemCount() == 1 && Duckov.Economy.EconomyManager.Money == 990, "post-delivery notification failure neither duplicates nor destroys actual item");
        Seed(); int nextSweeps = ModBehaviour.NextSweeps; CourierPaidLootSweepService.SeedForTest(2); CourierPaidLootSweepService.NextForTest(); Check(CharacterMainControl.Main.Inventory.Content.Count == 2 && !CourierPaidLootSweepService.PendingForTest && ModBehaviour.NextSweeps == nextSweeps + 1, "next sweep returns two unclaimed items before destroying crate");
        Seed(); ItemUtilities.FailBefore = true; nextSweeps = ModBehaviour.NextSweeps; var crate = CourierPaidLootSweepService.SeedForTest(2); CourierPaidLootSweepService.NextForTest(); Check(crate.Content.Count == 2 && CourierPaidLootSweepService.PendingForTest && ModBehaviour.NextSweeps == nextSweeps, "failed crate return retains ownership and cannot start next paid sweep");
        Seed(); var gift = new Item(9, "gift"); NPCGiftContainerService.DropForTest(gift); Check(gift.PickupCreated, "gift fallback invokes official pickup Drop API"); CharacterMainControl.Main = null; int buffered = ItemUtilities.Buffered; NPCGiftContainerService.DropForTest(new Item(9,"absent-player")); Check(ItemUtilities.Buffered == buffered + 1, "gift without a player transfers to the official buffer owner");
        Console.WriteLine("Transactions " + checks + " PASS");
    }
}
