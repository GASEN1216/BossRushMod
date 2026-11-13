using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using Duckov.NoteIndexs;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Saves;
using UnityEngine;

internal static class Program
{
    private static TidegateStoryPersistence persistence;
    private static int checks;
    private static readonly int Watch = BossRushItemIds.TidegateTideWatch, Bag = BossRushItemIds.TidegateDryBag;
    private static void Check(bool condition, string label)
    { checks++; if (!condition) throw new Exception(label); }
    private static Item Item(int id, bool container = false)
    { return new Item { TypeID = id, Inventory = container ? new Inventory() : null }; }
    private static void Setup()
    {
        TidegateStory.Bind(null);
        if (persistence != null) persistence.Dispose();
        TidegateNoteBridge.ResetStaticCaches();
        SavesSystem.Reset(); BossRushSaveFileThrottle.ResetStaticCaches();
        Time.frameCount++; Time.unscaledTime += 2f;
        LevelManager.Instance = new LevelManager { IsBaseLevel = true };
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = Item(100, true), Health = new CharacterHealth() };
        PlayerStorage.Instance = new PlayerStorage(); PlayerStorage.Loading = false;
        PlayerStorageBuffer.Instance = new PlayerStorageBuffer(); PlayerStorageBuffer.Buffer = new List<ItemTreeData>();
        NoteIndex.Instance = new NoteIndex(); NoteIndex.onNoteStatusChanged = null;
        ItemAssetsCollection.MissingType = 0; ItemUtilities.RejectDelivery = false; ItemUtilities.AfterSend = null;
        GameClock.Instance = new GameClock(); GameClock.Day = 10;
        persistence = new TidegateStoryPersistence(TidegateNoteBridge.RequestSync);
        TidegateStory.Bind(persistence); TidegateNoteBridge.EnsureRuntime();
    }
    private static void Advance()
    { Time.frameCount++; Time.unscaledTime += 2f; TidegateStory.Tick(); TidegateNoteBridge.Tick(); }
    private static void SaveInitial(TidegateStoryData data)
    {
        Check(OfficialQuestItems.AssetCollector(false)(), "initial assets");
        Check(persistence.Store(data), "initial facts");
        SavesSystem.History.Clear(); SavesSystem.Operations.Clear(); Time.frameCount++;
    }
    private static IEnumerable<ItemTreeData> AssetTrees(Dictionary<string, object> snapshot)
    {
        object value;
        if (snapshot.TryGetValue("Item/MainCharacterItemData", out value)) yield return (ItemTreeData)value;
        if (snapshot.TryGetValue("Inventory/PlayerStorage", out value))
            foreach (InventoryData.Entry entry in ((InventoryData)value).entries) yield return entry.itemTreeData;
        if (snapshot.TryGetValue("PlayerStorage_Buffer", out value))
            foreach (ItemTreeData tree in (List<ItemTreeData>)value) if (tree != null) yield return tree;
    }
    private static int Count(Dictionary<string, object> snapshot, int type)
    { return AssetTrees(snapshot).SelectMany(t => t.entries).Count(e => e.typeID == type); }
    private static TidegateStoryData Fact(Dictionary<string, object> snapshot)
    { return snapshot.ContainsKey(TidegateStoryPersistence.StorageKey) ? TidegateStoryPersistence.Decode((string)snapshot[TidegateStoryPersistence.StorageKey]) : new TidegateStoryData(); }
    private static void CheckHandoverSnapshots()
    {
        Check(SavesSystem.History.Count > 0, "handover eventually durable");
        foreach (var saved in SavesSystem.History)
        {
            bool ending = Fact(saved).WatchDelivered;
            Check(ending ? Count(saved, Watch) == 0 && Count(saved, Bag) == 1 : Count(saved, Bag) == 0 && (Count(saved, Watch) == 1 || saved.Count == 0),
                "every snapshot is complete old/new handover state");
        }
    }
    private static void MirrorBarriers()
    {
        foreach (string raw in new[] { "{\"schemaVersion\":999}", "{\"schemaVersion\":1}" })
        {
            Setup(); SavesSystem.Cache[TidegateStoryPersistence.StorageKey] = raw;
            string letter = TidegateNoteBridge.NoteKeyPrefix + TidegateNoteBridge.LetterId;
            string page = TidegateNoteBridge.NoteKeyPrefix + TidegateNoteBridge.PageId(0);
            NoteIndex.Instance.UnlockedNotes.Add(letter); NoteIndex.Instance.UnlockedNotes.Add(page);
            TidegateNoteBridge.Tick();
            Check(NoteIndex.GetNoteUnlocked(letter) && NoteIndex.GetNoteUnlocked(page), "first tick cannot relock defaults behind barrier");
            Check(!TidegateNoteBridge.EnsureRegistered(new TidegateStoryData()), "direct registration also checks loaded authority barrier");
            Check(NoteIndex.GetNoteUnlocked(letter) && NoteIndex.GetNoteUnlocked(page), "direct registration cannot relock defaults behind barrier");
            TidegateStory.ReadLetter(); Check(!TidegateStory.RecordPage(1), "page not accepted behind barrier");
            TidegateNoteBridge.Unlock(TidegateNoteBridge.EndingId);
            Check(NoteIndex.GetNoteUnlocked(letter) && NoteIndex.GetNoteUnlocked(page), "active calls preserve prior mirrors behind barrier");
            Check(!NoteIndex.GetNoteUnlocked(TidegateNoteBridge.NoteKeyPrefix + TidegateNoteBridge.EndingId), "unrecorded ending cannot unlock");
            Check((string)SavesSystem.Cache[TidegateStoryPersistence.StorageKey] == raw, "barrier payload untouched");
        }
        Setup(); TidegateNoteBridge.Tick();
        TidegateNoteBridge.Unlock(TidegateNoteBridge.EndingId);
        Check(!NoteIndex.GetNoteUnlocked(TidegateNoteBridge.NoteKeyPrefix + TidegateNoteBridge.EndingId), "direct unlock requires recorded fact");
        SavesSystem.FailWrite = TidegateStoryPersistence.StorageKey;
        TidegateStory.ReadLetter();
        Check(!NoteIndex.GetNoteUnlocked(TidegateNoteBridge.NoteKeyPrefix + TidegateNoteBridge.LetterId), "failed key flush blocks active mirror");
        Advance();
        Check(NoteIndex.GetNoteUnlocked(TidegateNoteBridge.NoteKeyPrefix + TidegateNoteBridge.LetterId), "recovered accepted letter mirrors on next tick");
    }
    private static void DeliveryFailuresAndRetry()
    {
        foreach (string failure in new[] { "none", "key", "readback", "physical", "asset" })
        {
            Setup(); Item container = Item(9001, true), watch = Item(Watch);
            CharacterMainControl.Main.CharacterItem.Inventory.AddItem(container); container.Inventory.AddItem(watch);
            SaveInitial(new TidegateStoryData());
            ItemUtilities.AfterSend = () => { SavesSystem.Collect(); Check(!TidegateStory.RecordPage(0), "reentrant mutation refused"); throw new Exception("notification after delivery"); };
            if (failure == "key") SavesSystem.FailWrite = TidegateStoryPersistence.StorageKey;
            if (failure == "readback") SavesSystem.FailReadback = TidegateStoryPersistence.StorageKey;
            if (failure == "physical") SavesSystem.FailPhysical = true;
            if (failure == "asset") SavesSystem.FailWrite = "Inventory/PlayerStorage";
            string reason;
            bool published = failure == "none" || failure == "physical";
            Check(TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason) == published, "handover only accepted after cache publication " + failure);
            Check(TidegateStory.WatchDelivered == published && watch.IsBeingDestroyed == published, "facts and watch consumption follow cache publication " + failure);
            if (failure == "none")
                foreach (string key in new[] { "Item/MainCharacterItemData", "MainCharacterHealth", "Inventory/PlayerStorage", "PlayerStorage_Buffer" })
                    Check(SavesSystem.Operations.Count(operation => operation == key) == 1, "successful batch collects each asset once: " + key);
            // Another module can flush the shared ES3 cache before this module gets its retry Tick.
            SavesSystem.SaveFile(false);
            CheckHandoverSnapshots();
            using (var reload = new TidegateStoryPersistence(null))
                Check(reload.Load().WatchDelivered == Fact(SavesSystem.Disk).WatchDelivered,
                    "fresh owner reload reads only complete durable facts");
            ItemUtilities.AfterSend = null; Advance();
            if (!published)
            {
                Time.frameCount++;
                Check(TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason), "publication failure can be retried by player " + failure);
            }
            CheckHandoverSnapshots();
            Check(!SavesSystem.IsSaving, "physical failure releases saving latch");
            Check(!TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason), "accepted handover cannot grant twice");
            Check(Count(SavesSystem.Disk, Bag) == 1, "one gift after recovery " + failure);
        }
        foreach (bool missing in new[] { false, true })
        {
            Setup(); Item watch = Item(Watch); CharacterMainControl.Main.CharacterItem.Inventory.AddAt(watch, 7);
            SaveInitial(new TidegateStoryData());
            ItemUtilities.RejectDelivery = !missing; ItemAssetsCollection.MissingType = missing ? Bag : 0;
            string reason;
            Check(!TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason), "failed gift rejects handover");
            Check(!TidegateStory.WatchDelivered && watch.InInventory != null && watch.InInventory.GetItemAt(7) == watch,
                "failed gift preserves watch and original position");
            Check(Count(SavesSystem.Disk, Watch) == 1 && Count(SavesSystem.Disk, Bag) == 0, "rollback snapshot has original assets");
            ItemUtilities.RejectDelivery = false; ItemAssetsCollection.MissingType = 0; Time.frameCount++;
            Check(TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason), "failed gift retry succeeds");
        }
        Setup(); CharacterMainControl.Main.CharacterItem.Inventory.Capacity = 1; PlayerStorage.Inventory.Capacity = 0;
        Item bagged = Item(Watch); CharacterMainControl.Main.CharacterItem.Inventory.AddItem(bagged);
        SaveInitial(new TidegateStoryData());
        // Fill the just-freed watch slot during reservation, forcing the gift into the official inbox.
        CharacterMainControl.Main.CharacterItem.Inventory.AfterRemove = item => CharacterMainControl.Main.CharacterItem.Inventory.AddItem(Item(9009));
        string bufferedReason;
        Check(TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out bufferedReason), "buffer receipt counts as delivery");
        CheckHandoverSnapshots();

        Setup(); CharacterMainControl.Main.CharacterItem.Inventory.AddItem(Item(Watch));
        SavesSystem.FailReadback = TidegateStoryPersistence.StorageKey;
        Check(!TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out bufferedReason), "first-ever batch rejected after readback failure");
        SavesSystem.SaveFile(false);
        Check(!Fact(SavesSystem.Disk).WatchDelivered && Count(SavesSystem.Disk, Watch) == 1 && Count(SavesSystem.Disk, Bag) == 0,
            "first-ever rollback publishes old runtime assets and old/default facts");
        Advance(); CheckHandoverSnapshots();
    }
    private static void SlotDriftBeforeCollect()
    {
        Setup(); CharacterMainControl.Main.CharacterItem.Inventory.AddItem(Item(Watch)); SaveInitial(new TidegateStoryData());
        SavesSystem.FailPhysical = true;
        string reason; Check(TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason), "pending old-slot batch");
        SavesSystem.SetSlot(2, false); // Earlier event subscriber prevented OnSetFile: collect precedes next Tick.
        SavesSystem.Operations.Clear(); SavesSystem.Collect();
        Check(SavesSystem.Cache.Count == 0 && SavesSystem.Operations.Count == 0, "old asset obligation cannot collect into silently changed slot");
        Advance(); Check(SavesSystem.Cache.Count == 0, "old batch never retried into new slot");
        SavesSystem.SetSlot(-1, true); Check(!persistence.BeginAssetMutation(), "unknown slot blocks asset mutations");
    }
    private static void ResetPreservesOtherTrees()
    {
        foreach (string failure in new[] { "none", "key", "physical" })
        {
            Setup();
            Item targetBag = Item(Bag, true), nestedWatch = Item(Watch), keep = Item(9101, true), keepChild = Item(9102);
            targetBag.Inventory.AddItem(keep); keep.Inventory.AddItem(keepChild); targetBag.Inventory.AddItem(nestedWatch);
            CharacterMainControl.Main.CharacterItem.Inventory.AddItem(targetBag);
            Item unrelated = Item(9103, true), bufferedTarget = Item(Bag, true), bufferedKeep = Item(9104);
            unrelated.Inventory.AddItem(bufferedTarget); bufferedTarget.Inventory.AddItem(bufferedKeep);
            ItemTreeData mixed = ItemTreeData.FromItem(unrelated);
            var metadata = mixed.entries.Find(e => e.instanceID == bufferedKeep.Id);
            metadata.variables.Add("custom-data"); metadata.inventorySortLocks.Add(7);
            PlayerStorage.IncomingItemBuffer.Add(mixed);
            Item pureKeep = Item(9105); PlayerStorage.IncomingItemBuffer.Add(ItemTreeData.FromItem(pureKeep));
            PlayerStorage.Inventory.AddItem(Item(BossRushItemIds.TidegateLetter));
            SaveInitial(new TidegateStoryData { LetterDelivered = true, LetterRead = true, Pages = 15, WatchDelivered = true });
            if (failure == "key") SavesSystem.FailWrite = TidegateStoryPersistence.StorageKey;
            if (failure == "physical") SavesSystem.FailPhysical = true;
            bool published = failure != "key";
            string detail; Check(TidegateStory.DevResetCurrentSlot(out detail) == published, "reset accepted only after publication " + failure);
            SavesSystem.SaveFile(false); // Independent publisher races the reset's retry.
            Advance();
            if (!published) { Time.frameCount++; Check(TidegateStory.DevResetCurrentSlot(out detail), "failed reset can be retried"); }
            Check(SavesSystem.History.Count > 0, "reset eventually durable");
            foreach (var snapshot in SavesSystem.History)
            {
                TidegateStoryData fact = Fact(snapshot);
                bool reset = !fact.LetterDelivered && !fact.LetterRead && fact.Pages == 0 && !fact.WatchDelivered;
                Check(reset || (fact.LetterDelivered && fact.LetterRead && fact.Pages == 15 && fact.WatchDelivered), "reset facts are complete old/new state");
                foreach (int target in TidegateStory.DevStoryItemIds)
                {
                    int before = target == Bag ? 2 : target == Watch || target == BossRushItemIds.TidegateLetter ? 1 : 0;
                    Check(Count(snapshot, target) == (reset ? 0 : before), "reset target assets match durable reset facts");
                }
                foreach (int other in new[] { 9101, 9102, 9103, 9104, 9105 }) Check(Count(snapshot, other) == 1, "reset preserves non-target " + other);
            }
            ItemTreeData savedKeep = AssetTrees(SavesSystem.Disk).Single(t => t.entries.Any(e => e.instanceID == bufferedKeep.Id));
            var keptMetadata = savedKeep.entries.Single(e => e.instanceID == bufferedKeep.Id);
            Check(keptMetadata.variables.Single() == "custom-data" && keptMetadata.inventorySortLocks.Single() == 7, "preserved subtree metadata");
            Check(savedKeep.rootInstanceID == bufferedKeep.Id, "non-target child becomes independent inbox root");
            Check(mixed.entries.Count == 3 && mixed.entries.Find(e => e.instanceID == unrelated.Id).inventory.Count == 1, "buffer plan leaves original trees unchanged");
            Time.frameCount++; TidegateStory.TryDeliverLetter();
            Check(Count(SavesSystem.Disk, BossRushItemIds.TidegateLetter) == 1, "reset permits exactly one fresh letter");
        }
    }
    private static void PersistentFailureWithOfficialCollectors()
    {
        foreach (bool storageBeforeStory in new[] { true, false })
        {
            Setup(); Item watch = Item(Watch); CharacterMainControl.Main.CharacterItem.Inventory.AddItem(watch);
            SaveInitial(new TidegateStoryData());
            SavesSystem.PersistentFailWrite = TidegateStoryPersistence.StorageKey;
            string reason;
            Check(!TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason), "persistent story failure rejects handover");
            Check(watch.InInventory != null && !TidegateStory.WatchDelivered, "persistent failure restores runtime qualification");
            CharacterMainControl.Main.CharacterItem.Save("MainCharacterItemData"); // LevelManager saves before CollectSaveData.
            if (storageBeforeStory) { PlayerStorage.Inventory.Save("PlayerStorage"); PlayerStorageBuffer.SaveBuffer(); }
            SavesSystem.Collect();
            if (!storageBeforeStory) { PlayerStorage.Inventory.Save("PlayerStorage"); PlayerStorageBuffer.SaveBuffer(); }
            SavesSystem.SaveFile(false);
            CheckHandoverSnapshots();
            Check(!Fact(SavesSystem.Disk).WatchDelivered && Count(SavesSystem.Disk, Watch) == 1, "official collect preserves complete old state under persistent fault");
            SavesSystem.PersistentFailWrite = null; Advance(); Time.frameCount++;
            Check(TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason), "retry after persistent failure clears");
            CheckHandoverSnapshots();
        }
        Setup(); Item retainedWatch = Item(Watch); CharacterMainControl.Main.CharacterItem.Inventory.AddItem(retainedWatch);
        SaveInitial(new TidegateStoryData());
        SavesSystem.FailWrite = TidegateStoryPersistence.StorageKey;
        SavesSystem.AfterWriteFailure = () => SavesSystem.PersistentFailLoad = TidegateStoryPersistence.StorageKey;
        string retryReason;
        Check(!TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out retryReason), "key failure followed by unreadable recovery rejects delivery");
        Check(!TidegateStory.WatchDelivered && retainedWatch.InInventory != null, "failed replacement preserves old accepted in-memory facts");
        SavesSystem.AfterWriteFailure = null; SavesSystem.PersistentFailLoad = null; Advance();
        Check(!Fact(SavesSystem.Disk).WatchDelivered, "read recovery must not resurrect rolled-back handover");
        Time.frameCount++;
        Check(TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out retryReason), "retry after combined write/read fault succeeds");
        CheckHandoverSnapshots();
    }
    private static void DisposeKeepsFailedRollback()
    {
        Setup(); CharacterMainControl.Main.CharacterItem.Inventory.AddItem(Item(Watch));
        SaveInitial(new TidegateStoryData());
        SavesSystem.FailWrite = "PlayerStorage_Buffer";
        SavesSystem.AfterWriteFailure = () => SavesSystem.PersistentFailWrite = "Item/MainCharacterItemData";
        string reason;
        Check(!TidegateStory.TryDeliverWatch(CharacterMainControl.Main, out reason), "failed asset rollback rejects handover");
        int collections = ItemStatsSystem.Item.SaveCalls;
        var field = typeof(TidegateStoryPersistence).GetField("publicationRollback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        object before = field.GetValue(persistence);
        Check(before != null, "failed cache rollback remains owed");
        persistence.Dispose();
        Check(ItemStatsSystem.Item.SaveCalls == collections, "Dispose must restore owed rollback before another asset collection");
        Check(ReferenceEquals(before, field.GetValue(persistence)), "Dispose retains original failed rollback baseline");
        SavesSystem.AfterWriteFailure = null; SavesSystem.PersistentFailWrite = null;
    }
    private static int Main()
    {
        MirrorBarriers(); DeliveryFailuresAndRetry(); SlotDriftBeforeCollect(); ResetPreservesOtherTrees(); PersistentFailureWithOfficialCollectors(); DisposeKeepsFailedRollback();
        TidegateDevices.TestHandwheel(Check);
        Console.WriteLine("PASS TidegateStoryTransactions: " + checks + " assertions");
        return 0;
    }
}
