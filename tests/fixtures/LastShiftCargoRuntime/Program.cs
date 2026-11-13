using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using ItemStatsSystem;
using UnityEngine;

internal static class Program
{
    private const int Seed = 1437;
    private static int passed;

    private static void Main()
    {
        Run("ordinary and premium cargo on all three manifests", NormalAndPremium);
        Run("wrong platform or an empty platform cannot sign", PlatformGate);
        Run("partial instantiation rolls back and retries the same cargo", FailedInstantiation);
        Run("inventory rejection rolls back and the opposite platform can sign later", FailedInsertion);
        Run("a post-insertion exception keeps the actual delivered slot", InsertedThenThrew);
        Run("changed instance quality rolls back despite a full box", WrongQuality);
        Run("a missing prize band cannot silently downgrade", MissingPrizeBand);
        Run("an empty ordinary pool preserves the signed cargo chance", EmptyGoodsPool);
        Run("a failed prebuild can recover at the signature device", FailedPrebuild);
        Run("closing during a partial delivery destroys the hidden stock", CloseDuringDelivery);
        Run("scene unload destroys delivered stock and the next raid can sign", SceneUnload);
        Run("a later train reclaims the old cabinet and signs a smaller one", LaterTrainResigns);
        Run("a failed cabinet activation restores the stamp and allows a retry", StampedCabinet);
        Run("a stamp picked up this raid does not open the cabinet", FreshStamp);
        Run("a fresh stamp cannot replace a dropped entry stamp", SwappedStamp);
        Console.WriteLine("LastShiftCargoRuntime: " + passed + " scenarios passed");
    }

    private static void Run(string name, Action test)
    {
        GameObject.Activating = null;
        ItemAssetsCollection.Instantiating = null;
        ItemAssetsCollection.Instances.Clear();
        ItemAssetsCollection.Attempts.Clear();
        Inventory.Adding = null;
        Inventory.Added = null;
        SkyIslandLootPools.EmptyPrizes = false;
        SkyIslandLootPools.EmptyGoods = false;
        SkyIslandLootPools.MissingPrizeGrade = 0;
        GameplayDataSettings.Prefabs.LootBoxPrefab = new GameObject("Official prefab").AddComponent<InteractableLootbox>();
        Time.time = 0;
        Debug.Messages.Clear();
        test();
        Check(InteractableLootbox.SubscriberCount == 0, "raid loot listener leaked across test");
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void NormalAndPremium()
    {
        foreach (string manifest in new[] { "medical", "repair", "precision" })
        foreach (bool premium in new[] { false, true })
        using (var run = new Raid(manifest, premium))
        {
            run.Dock();
            run.Loot.Tick(run.Player);
            Check(run.World.Root.GetComponentsInChildren<InteractableLootbox>(true).Any(), "proximity prebuild did not run");
            Check(run.ActiveBoxes().Length == 0, "prebuilt box was visible");
            run.Sign();
            AssertDelivery(run);
            int attempts = ItemAssetsCollection.Attempts.Count;
            run.Sign();
            run.Sign(Other(run.Side));
            Check(ItemAssetsCollection.Attempts.Count == attempts, "claim repeated after delivery");
        }
    }

    private static void PlatformGate()
    {
        using (var run = new Raid())
        {
            run.Sign();
            Check(!run.Loot.CargoClaimed && ItemAssetsCollection.Attempts.Count == 0, "signed before docking");
            run.Dock();
            run.Sign(Other(run.Side));
            Check(!run.Loot.CargoClaimed && ItemAssetsCollection.Attempts.Count == 0, "signed on the opposite platform");
            run.Sign();
            AssertDelivery(run);
        }
    }

    private static void FailedInstantiation()
    {
        using (var run = new Raid())
        {
            run.Dock();
            ItemAssetsCollection.Instantiating = item =>
            {
                if (ItemAssetsCollection.Attempts.Count == 3) item.TypeID += 1;
                return item;
            };
            run.Sign();
            AssertRollback(run, 3);
            int[] first = ItemAssetsCollection.Attempts.ToArray();
            ItemAssetsCollection.Instantiating = null;
            run.Sign();
            AssertDelivery(run);
            Check(ItemAssetsCollection.Attempts.Skip(first.Length).Take(first.Length).SequenceEqual(first), "failed attempt altered the seeded prize plan");
        }
    }

    private static void FailedInsertion()
    {
        using (var run = new Raid())
        {
            run.Dock();
            Inventory.Adding = (inventory, item) => inventory.GetItemCount() < 4;
            run.Sign();
            AssertRollback(run, 5);
            Inventory.Adding = null;
            run.Dock(Other(run.Side));
            run.Sign();
            AssertDelivery(run);
            Check(run.Loot.Signatures == 1, "a rolled-back attempt counted as a signature");
        }
    }

    private static void InsertedThenThrew()
    {
        using (var run = new Raid())
        {
            run.Dock();
            Inventory.Added = (inventory, item) =>
            {
                run.Sign();
                if (inventory.GetItemCount() == 3) throw new InvalidOperationException("official add listener failed after attach");
            };
            run.Sign();
            AssertDelivery(run);
            Check(ItemAssetsCollection.Attempts.Count == run.ExpectedCount, "an inventory listener reentered the signature transaction");
        }
    }

    private static void WrongQuality()
    {
        using (var run = new Raid(premium: true))
        {
            run.Dock();
            ItemAssetsCollection.Instantiating = item =>
            {
                if (ItemAssetsCollection.Attempts.Count == 2) item.Quality = 3;
                return item;
            };
            run.Sign();
            AssertRollback(run, 9);
            ItemAssetsCollection.Instantiating = null;
            run.Sign();
            AssertDelivery(run);
        }
    }

    private static void MissingPrizeBand()
    {
        using (var run = new Raid(premium: true))
        {
            run.Dock();
            SkyIslandLootPools.EmptyPrizes = true;
            run.Sign();
            AssertRollback(run, 0);
            SkyIslandLootPools.EmptyPrizes = false;
            // 缺二号精品档时，杂货池本身仍有 Q4，不能拿普通格替代。
            SkyIslandLootPools.MissingPrizeGrade = 4;
            run.Sign();
            AssertRollback(run, 0);
            SkyIslandLootPools.MissingPrizeGrade = 0;
            run.Sign();
            AssertDelivery(run);
        }
    }

    private static void EmptyGoodsPool()
    {
        using (var run = new Raid())
        {
            run.Dock();
            SkyIslandLootPools.EmptyGoods = true;
            run.Sign();
            AssertRollback(run, 0);
            SkyIslandLootPools.EmptyGoods = false;
            run.Sign();
            AssertDelivery(run);
        }
    }

    private static void FailedPrebuild()
    {
        using (var run = new Raid())
        {
            run.Dock();
            InteractableLootbox prefab = GameplayDataSettings.Prefabs.LootBoxPrefab;
            GameplayDataSettings.Prefabs.LootBoxPrefab = null;
            run.Loot.Tick(run.Player);
            run.Sign();
            AssertRollback(run, 0);
            GameplayDataSettings.Prefabs.LootBoxPrefab = prefab;
            run.Sign();
            AssertDelivery(run);
        }
    }

    private static void CloseDuringDelivery()
    {
        using (var run = new Raid())
        {
            run.Dock();
            Inventory.Added = (inventory, item) =>
            {
                if (inventory.GetItemCount() == 3) run.Dispose();
            };
            run.Sign();
            Check(!run.Loot.CargoClaimed && ItemAssetsCollection.Attempts.Count == 3, "delivery continued after session close");
            Check(ItemAssetsCollection.Instances.All(item => item == null), "session close left partially staged cargo alive");
        }
    }

    private static void SceneUnload()
    {
        using (var run = new Raid())
        {
            run.Dock();
            run.Sign();
            InteractableLootbox box = AssertDelivery(run);
            Item[] items = box.Inventory.Content.ToArray();
            run.Dispose();
            Check(box == null && items.All(item => item == null), "signed cargo survived scene unload");
        }
        using (var run = new Raid())
        {
            run.Dock();
            run.Sign();
            AssertDelivery(run);
        }
    }

    private static void StampedCabinet()
    {
        using (var run = new Raid(carryStampAtEntry: true))
        {
            Item stamp = run.EntryStamp;
            int originalIndex = run.Backpack.Content.IndexOf(stamp);
            GameObject.Activating = host =>
            {
                if (host.name != "LastShiftLoot_stamp") return;
                Check(stamp != null && stamp.InInventory == null, "the cabinet became visible before stamp reservation");
                throw new InvalidOperationException("fixture rejected cabinet activation");
            };
            run.PrebuildStampCabinet();
            LastShiftDeviceInteractable device = run.Device("stamp");
            Check(run.ActiveBoxes().Length == 0, "proximity exposed the sealed cabinet");
            device.Press();
            Check(device.Available && run.ActiveBoxes().Length == 0, "failed activation left the cabinet unlocked");
            Check(stamp != null && stamp.InInventory == run.Backpack && run.Backpack.GetItemAt(originalIndex) == stamp,
                "failed activation did not return the stamp to its slot");
            Check(Debug.Messages.Any(message => message.Contains("fixture rejected cabinet activation")), "fixture did not reach cabinet activation");

            GameObject.Activating = null;
            device.Press();
            InteractableLootbox box = run.ActiveBoxes().Single();
            Check(box.name == "LastShiftLoot_stamp" && box.Inventory.GetItemCount() > 0, "retry did not publish the stocked cabinet");
            Check(stamp == null && !device.Available, "successful cabinet did not commit the stamp exactly once");
        }
    }

    private static void FreshStamp()
    {
        using (var run = new Raid())
        {
            run.PrebuildStampCabinet();
            Item stamp = run.CarryStamp();
            LastShiftDeviceInteractable device = run.Device("stamp");
            device.Press();
            Check(stamp != null && stamp.InInventory == run.Backpack, "fresh stamp was consumed");
            Check(device.Available && run.ActiveBoxes().Length == 0, "fresh stamp opened the cabinet");
        }
    }

    private static void SwappedStamp()
    {
        using (var run = new Raid(carryStampAtEntry: true))
        {
            Item oldStamp = run.EntryStamp;
            run.Backpack.RemoveAt(run.Backpack.Content.IndexOf(oldStamp));
            Item freshStamp = run.CarryStamp();
            LastShiftDeviceInteractable device = run.Device("stamp");
            device.Press();
            Check(device.Available && run.ActiveBoxes().Length == 0, "fresh stamp replaced an entry stamp that was dropped");
            Check(freshStamp != null && freshStamp.InInventory == run.Backpack, "replacement stamp was consumed");
            Check(run.Backpack.AddItem(oldStamp), "fixture could not recover the entry stamp");
            device.Press();
            Check(!device.Available && oldStamp == null && freshStamp != null && freshStamp.InInventory == run.Backpack,
                "cabinet did not choose the recovered entry stamp from the carried stamps");
        }
    }

    /// <summary>每班可签（2026-11-13）：下一班停到同一侧时收回上一柜，再签是一柜续签（4 / 5 件，只保底头格），同一班不能签两次。</summary>
    private static void LaterTrainResigns()
    {
        using (var run = new Raid())
        {
            run.Dock();
            run.Sign();
            AssertDelivery(run);
            int firstTrip = run.Timetable.Trip;
            run.NextDock();
            Check(run.Timetable.Trip > firstTrip && run.Timetable.Phase == LastShiftPhase.Dwell, "fixture did not reach a later train");
            Check(run.ActiveBoxes().Length == 0, "the previous cabinet was not reclaimed when the next train docked");
            Check(run.Device(run.Side).Available && !run.Loot.SignedThisTrain, "the signature device did not return for the new train");
            run.Sign();
            InteractableLootbox box = AssertDelivery(run);
            Check(run.Loot.Signatures == 2 && run.Loot.SignedThisTrain, "the second train's signature was not counted");
            Check(box.Inventory.GetItemCount() == LastShiftLoot.CargoCount(run.Side == run.Timetable.Premium, 1)
                && box.Inventory.GetItemCount() < LastShiftLoot.SealedCount, "a re-signed cabinet is not the smaller one");
            int attempts = ItemAssetsCollection.Attempts.Count;
            run.Sign();
            Check(ItemAssetsCollection.Attempts.Count == attempts, "the same train signed twice");
        }
    }

    private static void AssertRollback(Raid run, int attempts)
    {
        Check(!run.Loot.CargoClaimed, "failed delivery consumed the raid signature");
        Check(run.ActiveBoxes().Length == 0, "failed delivery left a lootable cabinet");
        Check(run.Device(run.Side).Available, "signature device disappeared after rollback");
        Check(ItemAssetsCollection.Attempts.Count == attempts, "fault did not occur after the expected attempt count");
        Check(ItemAssetsCollection.Instances.All(item => item == null), "rollback leaked a created instance");
        Check(!Debug.Messages.Any(message => message.Contains("CARGO_CLAIMED")), "failed delivery emitted the success record");
    }

    private static InteractableLootbox AssertDelivery(Raid run)
    {
        InteractableLootbox[] boxes = run.ActiveBoxes();
        Check(run.Loot.CargoClaimed && boxes.Length == 1, "successful signature did not publish exactly one cabinet");
        InteractableLootbox box = boxes[0];
        Check(box.name == "LastShiftLoot_cargo" + run.Side && !run.Device(run.Side).Available, "signed cabinet did not replace the matching device");
        Check(box.Inventory.GetItemCount() == run.ExpectedCount, "incorrect delivered item count");
        int prizeSlots = LastShiftLoot.PrizeSlots(run.Loot.Signatures - 1);
        Check(box.Inventory.GetItemAt(0).Quality >= 4 && (prizeSlots < 2 || box.Inventory.GetItemAt(1).Quality == 4), "the required premium slots were not delivered");
        foreach (Item item in box.Inventory.Content.Skip(prizeSlots))
            Check(item.Quality >= 2 && item.Quality <= 4, "ordinary item outside Q2–Q4");
        Check(box.Inventory.Content.All(item => item.InInventory == box.Inventory && !item.Inspected), "stock must be attached and subject to inspection");
        Check(Debug.Messages.Any(message => message.Contains("CARGO_CLAIMED") && message.Contains("items=" + run.ExpectedCount + "/" + run.ExpectedCount)),
            "missing committed cargo record");
        int opened = 0;
        run.Loot.Opened += id => { if (id == "cargo" + run.Side) opened++; };
        box.Open();
        box.Open();
        Check(opened == 1, "official open callback is not connected exactly once");
        return box;
    }

    private static char Other(char side) { return side == 'W' ? 'E' : 'W'; }

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
    }

    private sealed class Raid : IDisposable
    {
        internal readonly LastShiftWorld World = new LastShiftWorld();
        internal readonly LastShiftTimetable Timetable = new LastShiftTimetable(Seed, false);
        internal readonly LastShiftLoot Loot;
        internal readonly CharacterMainControl Player;
        internal readonly Inventory Backpack;
        internal readonly Item EntryStamp;
        internal char Side;
        /// <summary>下一柜（还没签时）或刚签完那一柜的件数：按这一班的加价台与本趟第几柜（首柜 8 / 9，续签 4 / 5）。</summary>
        internal int ExpectedCount { get { return LastShiftLoot.CargoCount(Side == Timetable.Premium, Math.Max(0, Loot.Signatures - (Loot.SignedThisTrain ? 1 : 0))); } }
        private bool disposed;

        internal Raid(string manifest = "medical", bool premium = false, bool carryStampAtEntry = false)
        {
            Side = premium ? Timetable.Premium : Other(Timetable.Premium);
            foreach (char side in new[] { 'W', 'E' })
            {
                var marker = new GameObject("cargo" + side);
                marker.transform.SetParent(World.Root.transform, false);
                marker.transform.position = new Vector3(side == Side ? 1 : 10, 0, 0);
                World.LootMarkers.Add(marker.transform);
            }
            var stampAnchor = new GameObject("stamp");
            stampAnchor.transform.SetParent(World.Root.transform, false);
            stampAnchor.transform.position = new Vector3(40, 0, 0);
            World.Interacts.Add("stamp", stampAnchor.transform);
            Player = new GameObject("Player").AddComponent<CharacterMainControl>();
            Player.CharacterItem = Player.gameObject.AddComponent<Item>();
            Backpack = Player.gameObject.AddComponent<Inventory>();
            Backpack.SetCapacity(8);
            CharacterMainControl.Main = Player;
            if (carryStampAtEntry) EntryStamp = CarryStamp();
            Loot = new LastShiftLoot(World, Timetable, manifest, Seed, Player, () => !disposed, null);
            GameObject.Activating = go =>
            {
                if (!go.name.StartsWith("LastShiftLoot_cargo", StringComparison.Ordinal)) return;
                var box = go.GetComponent<InteractableLootbox>();
                Check(box != null && box.Inventory != null && box.Inventory.GetItemCount() == ExpectedCount,
                    "an incomplete cabinet became visible, even briefly");
            };
        }

        internal void Dock(char? side = null)
        {
            if (side.HasValue) Side = side.Value;
            Timetable.MarkCartHit();
            bool assigned = false;
            for (int i = 0; i < 40; i++)
            {
                if (Timetable.Phase == LastShiftPhase.Approach && !assigned)
                    assigned = Timetable.TrySwitch(Side, out string reason);
                if (Timetable.Phase == LastShiftPhase.Dwell && Timetable.LineUsed == Side) return;
                Timetable.Tick(Timetable.PhaseLength(Timetable.Phase), LastShiftTimetable.WatchX, LastShiftTimetable.WatchZ);
            }
            throw new InvalidOperationException("fixture did not dock the train at the requested platform");
        }

        internal LastShiftDeviceInteractable Device(char side)
        { return Device("cargo" + side); }

        internal LastShiftDeviceInteractable Device(string id)
        {
            return World.Root.GetComponentsInChildren<LastShiftDeviceInteractable>(true)
                .Single(device => device.transform.parent.gameObject.name == "LastShiftLootDevice_" + id);
        }

        internal Item CarryStamp()
        {
            Item stamp = new GameObject("Stamp").AddComponent<Item>();
            stamp.TypeID = BossRushItemIds.LastShiftStamp;
            Check(Backpack.AddItem(stamp), "fixture could not carry the stamp");
            return stamp;
        }

        internal void PrebuildStampCabinet()
        {
            Player.transform.position = World.Interacts["stamp"].position;
            Loot.Tick(Player);
        }

        internal void Sign(char? side = null) { Device(side ?? Side).Press(); }

        /// <summary>开出这一班，等下一班停到同一侧（快车与停到另一侧的那几班跳过；道岔留在原位）。</summary>
        internal void NextDock()
        {
            int trip = Timetable.Trip;
            for (int i = 0; i < 80 && Timetable.Trip == trip; i++)
                Timetable.Tick(Timetable.PhaseLength(Timetable.Phase), LastShiftTimetable.WatchX, LastShiftTimetable.WatchZ);
            Dock();
            Loot.Tick(Player);
        }
        internal InteractableLootbox[] ActiveBoxes() { return World.Root.GetComponentsInChildren<InteractableLootbox>(false).ToArray(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Loot.Dispose();
            UnityEngine.Object.Destroy(World.Root);
            UnityEngine.Object.Destroy(Player.gameObject);
            CharacterMainControl.Main = null;
            GameObject.Activating = null;
        }
    }
}
