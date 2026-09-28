using System;
using System.Collections.Generic;
using BossRush;
using Saves;
using ItemStatsSystem.Data;

internal static class SkyIslandProductionReadinessRegression
{
    internal static void Run(Action<bool, string> check)
    {
        Objectives(check);
        StoredKeepsakes(check);
        KeepsakeRetry(check);
    }

    private static SkyIslandStoryService OpenRaid(int slot)
    {
        SavesSystem.Switch(slot);
        PlayerStorage.IncomingItemBuffer.Clear();
        var story = new SkyIslandStoryService(); story.Open(); story.RaidHeldCosts = true;
        return story;
    }

    private static void Apply(SkyIslandStoryService story, SkyIslandStoryAction action)
    {
        string message;
        if (!story.TryApply(action, out message)) throw new Exception(action + ": " + message);
    }

    private static void StartBeaconQuest(SkyIslandStoryService story)
    {
        Apply(story, SkyIslandStoryAction.AcceptPrelude);
        Apply(story, SkyIslandStoryAction.RecoverPreludeInstrument);
        Apply(story, SkyIslandStoryAction.UnlockRoute);
        Apply(story, SkyIslandStoryAction.AcceptBeaconQuest);
    }

    private static void Objectives(Action<bool, string> check)
    {
        int slot = 928101;
        foreach (bool chinese in new[] { true, false })
        {
            L10n.IsChinese = chinese;
            var story = OpenRaid(slot++);
            StartBeaconQuest(story);
            string before = story.CurrentObjective;
            string summaryBefore = story.Summary;
            foreach (string group in new[] { "D", "D_02", "G", "G_02" })
            {
                check(story.RecordEncounterCleared(group), "accept guard clear: " + group);
                check(story.CurrentObjective == SkyIslandStoryRules.Objective(story.Current), "objective follows current guard facts: " + group);
                check(story.Summary.StartsWith(story.CurrentObjective, StringComparison.Ordinal), "summary follows the same refreshed objective");
            }
            check(story.CurrentObjective != before && story.Summary != summaryBefore, "clearing guards refreshes both cached views");
            string stable = story.CurrentObjective;
            string stableSummary = story.Summary;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
                if (!ReferenceEquals(stable, story.CurrentObjective) || !ReferenceEquals(stableSummary, story.Summary))
                    throw new Exception("stable objective/summary allocated another string");
            check(GC.GetAllocatedBytesForCurrentThread() == allocated, "stable cached views allocate zero bytes in 10000 reads");
            check(story.TryClose(), "objective probe closes");
        }
        L10n.IsChinese = true;
    }

    private static void StoredKeepsakes(Action<bool, string> check)
    {
        int slot = 928110;
        // Both the explicitly stored core and a full-backpack compass use the actual buffer receipt.
        foreach (string note in new[] { "Keepsake_Core", "Keepsake_Compass" })
        foreach (bool failPhysical in new[] { false, true })
        {
            int currentSlot = slot++;
            var story = OpenRaid(currentSlot);
            StartBeaconQuest(story);
            story.RecordEncounterCleared("D"); story.RecordEncounterCleared("D_02");
            Apply(story, SkyIslandStoryAction.RepairWindBeacon);
            story.RecordEncounterCleared("G"); story.RecordEncounterCleared("G_02");
            Apply(story, SkyIslandStoryAction.RepairStarLamp);
            Apply(story, SkyIslandStoryAction.StormSlain);
            string message;
            check(story.RecordNote("Letter_01", out message), "compass prerequisite can be recorded");
            story.Tick(true);
            var reward = SkyIslandItemRules.FindKeepsake(note);
            check(SkyIslandItemRules.Due(story.Current, reward), "keepsake initially due: " + note);
            int packSaves = CharacterMainControl.Main.CharacterItem.SaveCalls;
            int writes = SavesSystem.PhysicalWrites;
            check(story.BeginKeepsakeDelivery(note, out message), "start guarded keepsake transfer");
            check(!story.BeginKeepsakeDelivery(note, out message), "nested transfer is refused");
            check(!story.BeginOfficialDelivery(OfficialQuestItems.AssetCollector(true), out message), "quest cannot reenter unfinished item transfer");
            check(story.RecordNote(note, out message), "record prepared keepsake");
            SavesSystem.Collect();
            var premature = SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey));
            check(!SkyIslandItemRules.Granted(premature, note), "reentrant official collect cannot publish the unfinished grant");
            PlayerStorage.IncomingItemBuffer.Add(new ItemTreeData { RootTypeID = reward.TypeId });
            story.EndKeepsakeDelivery(note, true, true);
            check(SavesSystem.PhysicalWrites == writes, "automatic grant queues without writing on a combat frame");
            check(story.RecordNote("Light_F", out message), "unrelated raid cost remains held");
            check(story.BeginOfficialDelivery(OfficialQuestItems.AssetCollector(true), out message), "later quest can share the pending buffered reward");
            check(story.TryDeliverQuest(SkyIslandStoryAction.DeliverBeaconQuest, SkyIslandOfficialQuestTable.BeaconQuestMoney, out message), "deliver real beacon quest");
            SavesSystem.FailPhysical = failPhysical;
            story.EndOfficialDelivery(true);
            if (failPhysical)
            {
                check(SavesSystem.PhysicalWrites == writes, "failed physical write retains reward batch");
                SavesSystem.FailPhysical = false;
                SavesSystem.ClearStuckSaving();
                story.Tick(true);
            }
            check(SavesSystem.PhysicalWrites == writes + 1, "quest and buffered keepsake persist in one successful batch");
            check(CharacterMainControl.Main.CharacterItem.SaveCalls == packSaves, "inbox-only commit never saves the raid backpack");
            var saved = SkyIslandStoryCodec.Decode((string)SavesSystem.Durable[currentSlot][SkyIslandStoryRules.StorageKey]);
            var buffer = (List<ItemTreeData>)SavesSystem.Durable[currentSlot]["PlayerStorage_Buffer"];
            check(buffer.Count == 1 && buffer[0].RootTypeID == reward.TypeId && SkyIslandItemRules.Granted(saved, note), "durable item and grant receipt agree");
            check(!SkyIslandItemRules.Granted(saved, "Light_F"), "buffer save cannot make raid material spending durable");
            story.SettleRaidHeld(false);
            check(story.TryClose(), "quitting preserves stored reward obligation");
            SavesSystem.CrashReload(currentSlot);
            story = new SkyIslandStoryService(); story.Open();
            check(!SkyIslandItemRules.Due(story.Current, reward), "restart does not grant the stored keepsake twice");
            check(story.TryClose(), "restarted story closes");
        }

        int directSlot = slot++;
        var direct = OpenRaid(directSlot);
        string directReason;
        check(direct.RecordNote("Letter_01", out directReason), "standalone compass is due after its letter");
        check(direct.BeginKeepsakeDelivery("Keepsake_Compass", out directReason), "begin standalone buffered reward");
        check(direct.RecordNote("Keepsake_Compass", out directReason), "prepare standalone receipt");
        PlayerStorage.IncomingItemBuffer.Add(new ItemTreeData { RootTypeID = BossRushItemIds.SkyIslandWindVaneCompass });
        direct.EndKeepsakeDelivery("Keepsake_Compass", true, true);
        int directWrites = SavesSystem.PhysicalWrites;
        var oldBufferOwner = PlayerStorageBuffer.Instance;
        PlayerStorageBuffer.Instance = null;
        direct.Tick(true);
        check(SavesSystem.PhysicalWrites == directWrites, "unavailable buffer blocks standalone receipt persistence");
        PlayerStorageBuffer.Instance = oldBufferOwner;
        direct.Tick(true);
        var directSaved = SkyIslandStoryCodec.Decode((string)SavesSystem.Durable[directSlot][SkyIslandStoryRules.StorageKey]);
        check(SavesSystem.PhysicalWrites == directWrites + 1 && SkyIslandItemRules.Granted(directSaved, "Keepsake_Compass")
            && ((List<ItemTreeData>)SavesSystem.Durable[directSlot]["PlayerStorage_Buffer"]).Count == 1,
            "standalone stored reward retries its own buffer collector without an official quest");
        check(direct.TryClose(), "standalone grant closes");

        // 官方 NotifySaveBeforeLoadScene(false) 只 Collect，不保证紧接着 SaveFile。
        // typed pending 被消费以后，单独的缓冲快照义务仍应使下一安全帧落盘。
        int collectedSlot = slot++;
        var collected = OpenRaid(collectedSlot);
        check(collected.BeginKeepsakeDelivery("Keepsake_Core", out directReason), "begin collect-only reward");
        check(collected.RecordNote("Keepsake_Core", out directReason), "prepare collect-only receipt");
        PlayerStorage.IncomingItemBuffer.Add(new ItemTreeData { RootTypeID = BossRushItemIds.SkyIslandWindeaterCore });
        collected.EndKeepsakeDelivery("Keepsake_Core", true, true);
        int collectedWrites = SavesSystem.PhysicalWrites;
        int collectedPackSaves = CharacterMainControl.Main.CharacterItem.SaveCalls;
        SavesSystem.Collect();
        check(SavesSystem.PhysicalWrites == collectedWrites, "official collect does not imply a physical save");
        collected.Tick(true);
        check(SavesSystem.PhysicalWrites == collectedWrites + 1, "snapshot-only obligation survives consumed typed pending");
        var collectedSaved = SkyIslandStoryCodec.Decode((string)SavesSystem.Durable[collectedSlot][SkyIslandStoryRules.StorageKey]);
        check(SkyIslandItemRules.Granted(collectedSaved, "Keepsake_Core")
            && ((List<ItemTreeData>)SavesSystem.Durable[collectedSlot]["PlayerStorage_Buffer"]).Count == 1,
            "collect-only reward and receipt reach durable storage together");
        check(CharacterMainControl.Main.CharacterItem.SaveCalls == collectedPackSaves,
            "collect-only recovery does not serialize the raid backpack");
        collected.Tick(true);
        check(SavesSystem.PhysicalWrites == collectedWrites + 1, "successful snapshot-only obligation does not keep saving");
        check(collected.TryClose(), "collect-only grant closes");

        var packStory = OpenRaid(slot++);
        string why;
        check(packStory.BeginKeepsakeDelivery("Keepsake_Compass", out why), "start backpack-only grant");
        check(packStory.RecordNote("Keepsake_Compass", out why), "backpack grant records in memory");
        packStory.EndKeepsakeDelivery("Keepsake_Compass", true, false);
        Apply(packStory, SkyIslandStoryAction.FindOldLetter);
        packStory.Tick(true);
        var held = SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey));
        check(!SkyIslandItemRules.Granted(held, "Keepsake_Compass"), "unextracted backpack reward remains raid-held");
        packStory.SettleRaidHeld(false);
        check(packStory.TryClose(), "backpack rollback closes");

        var failed = OpenRaid(slot++);
        check(failed.BeginKeepsakeDelivery("Keepsake_Core", out why), "begin failed delivery");
        check(failed.RecordNote("Keepsake_Core", out why) && failed.RemoveNote("Keepsake_Core"), "failed physical transfer rolls back exact receipt");
        failed.EndKeepsakeDelivery("Keepsake_Core", false, false);
        check(failed.BeginOfficialDelivery(OfficialQuestItems.AssetCollector(true), out why), "failed grant releases the mutation gate");
        failed.EndOfficialDelivery(false);
        check(failed.TryClose(), "failed delivery closes");

        var changed = OpenRaid(slot++);
        check(changed.BeginKeepsakeDelivery("Keepsake_Core", out why), "begin slot-change probe");
        check(changed.RecordNote("Keepsake_Core", out why), "old slot accepts prepared receipt");
        SavesSystem.Switch(slot++);
        changed.EndKeepsakeDelivery("Keepsake_Core", true, true);
        check(changed.TryClose() && !SavesSystem.KeyExisits(SkyIslandStoryRules.StorageKey), "slot switch never publishes old reward receipt into new slot");
        PlayerStorage.IncomingItemBuffer.Clear();
    }

    private static void KeepsakeRetry(Action<bool, string> check)
    {
        var story = OpenRaid(928140);
        string message;
        check(story.RecordNote("Letter_01", out message), "retry probe makes compass due without changing flags");
        int flags = story.Current.flags;
        var world = new SkyIslandKeepsakeHarness(story);
        SkyIslandItems.MissingResources = true;
        SkyIslandItems.Attempts = SkyIslandItems.Delivered = 0;
        UnityEngine.Time.unscaledTime = 2000f;
        int warnings = UnityEngine.Debug.Warnings;
        world.GrantForTest();
        check(SkyIslandItems.Attempts == 1 && SkyIslandItems.Delivered == 0
            && !SkyIslandItemRules.Granted(story.Current, SkyIslandItemRules.CompassKeepsake),
            "missing prefab preserves the real journal claim for retry");
        check(UnityEngine.Debug.Warnings == warnings + 1, "first pending episode warns once");
        for (int frame = 0; frame < 120; frame++) world.TickRetryForTest();
        UnityEngine.Time.unscaledTime = 2000.5f;
        world.TickRetryForTest();
        check(SkyIslandItems.Attempts == 1, "same-frame and sub-second retry ticks do no item preparation");
        UnityEngine.Time.unscaledTime = 2001f;
        world.TickRetryForTest();
        check(SkyIslandItems.Attempts == 2 && UnityEngine.Debug.Warnings == warnings + 1,
            "one-second retry runs once without repeating warnings");
        SkyIslandItems.MissingResources = false;
        UnityEngine.Time.unscaledTime = 2001.5f;
        world.TickRetryForTest();
        check(SkyIslandItems.Attempts == 2, "restored resources still respect the outstanding retry deadline");
        UnityEngine.Time.unscaledTime = 2002f;
        world.TickRetryForTest();
        check(story.Current.flags == flags && SkyIslandItems.Attempts == 3 && SkyIslandItems.Delivered == 1,
            "unchanged flags do not prevent recovery when prefab becomes available");
        check(SkyIslandItemRules.Granted(story.Current, SkyIslandItemRules.CompassKeepsake)
            && PlayerStorage.IncomingItemBuffer.Count == 1 && world.Announcements == 1,
            "successful retry records and announces exactly one delivery");
        for (int frame = 0; frame < 120; frame++)
        {
            UnityEngine.Time.unscaledTime += 1f;
            world.TickRetryForTest();
        }
        world.GrantForTest();
        check(SkyIslandItems.Attempts == 3 && SkyIslandItems.Delivered == 1
            && UnityEngine.Debug.Warnings == warnings + 1 && world.Announcements == 1,
            "finished retry stays idle and later explicit grant checks cannot duplicate it");
        check(story.TryClose(), "retry probe closes its real save owner");
        SkyIslandItems.MissingResources = false;
        PlayerStorage.IncomingItemBuffer.Clear();
    }
}
