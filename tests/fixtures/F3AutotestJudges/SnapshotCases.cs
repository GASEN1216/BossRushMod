using System;
using System.Collections.Generic;
using BossRush;
using Saves;

/// <summary>3. 快照：编码往返，以及在替身 SavesSystem 上真开 SkyIslandStoryService 走「快照 → 阶段替换 → 还原」。</summary>
internal static partial class Program
{
    private static bool SameSnapshot(F3AutotestSnapshotRecord a, F3AutotestSnapshotRecord b, out string diff)
    {
        diff = null;
        if (a == null || b == null) { diff = "null"; return false; }
        if (a.RunId != b.RunId) diff = "runId";
        else if (a.Slot != b.Slot) diff = "slot";
        else if (a.RawExists != b.RawExists) diff = "rawExists";
        else if (!string.Equals(a.Raw, b.Raw, StringComparison.Ordinal)) diff = "raw";
        else if (a.Money != b.Money) diff = "money";
        else if (a.Language != b.Language) diff = "language";
        else if (a.ForceNight != b.ForceNight) diff = "forceNight";
        else if (a.BufferCountsIncluded != b.BufferCountsIncluded) diff = "bufferCountsIncluded";
        else if (a.TimeScale != b.TimeScale) diff = "timeScale " + a.TimeScale.ToString("R") + "!=" + b.TimeScale.ToString("R");
        else if (a.OfficialUnlocked.Count != b.OfficialUnlocked.Count) diff = "officialUnlocked.count";
        else if (a.Items.Count != b.Items.Count) diff = "items.count";
        if (diff != null) return false;
        for (int i = 0; i < a.OfficialUnlocked.Count; i++)
            if (a.OfficialUnlocked[i] != b.OfficialUnlocked[i]) { diff = "officialUnlocked[" + i + "]"; return false; }
        foreach (KeyValuePair<int, int> pair in a.Items)
        {
            int count;
            if (!b.Items.TryGetValue(pair.Key, out count) || count != pair.Value) { diff = "items[" + pair.Key + "]"; return false; }
        }
        return true;
    }

    private static void RoundTrip(F3AutotestSnapshotRecord snapshot, string name)
    {
        string json = F3AutotestJudges.EncodeSnapshot(snapshot);
        F3AutotestSnapshotRecord decoded = F3AutotestJudges.DecodeSnapshot(json);
        string diff = null;
        Check(SameSnapshot(snapshot, decoded, out diff), "snapshot round trip keeps every field (" + name + "): " + diff);
    }

    private static void SnapshotCodecCases()
    {
        var full = new F3AutotestSnapshotRecord
        {
            RunId = "autotest-20260914T101500Z", Slot = 7, RawExists = true,
            Raw = "{\"schemaVersion\":1,\"说明\":\"中文\\\"引号\\\"\"}\n第二行\t制表\\反斜杠 \"裸引号\" {}[]",
            Money = 5000000000L, Language = "ChineseSimplified", ForceNight = true, TimeScale = 0.37f, BufferCountsIncluded = true
        };
        full.OfficialUnlocked.AddRange(new[] { "SkyIsland_Note_Search_S1", "SkyIsland_Note_Letter_01", "带中文与\"引号\"的键" });
        full.Items[500069] = 1;
        full.Items[500079] = 3;
        full.Items[500082] = 12;
        RoundTrip(full, "chinese raw with quotes and newlines, money beyond int, fractional timeScale, several items and notes");
        var empty = new F3AutotestSnapshotRecord { RunId = "autotest-new-save", Slot = 0, RawExists = false, Raw = null, Money = -3L, Language = "English", TimeScale = 1f };
        RoundTrip(empty, "raw null, no items");
        Check(F3AutotestJudges.DecodeSnapshot(F3AutotestJudges.EncodeSnapshot(empty)).Raw == null, "raw null decodes as null, not empty string");
        var extreme = new F3AutotestSnapshotRecord { RunId = "x", Slot = int.MaxValue, RawExists = true, Raw = string.Empty, Money = long.MaxValue, Language = "", TimeScale = 1.5E-05f };
        extreme.Items[-1] = int.MaxValue;
        RoundTrip(extreme, "long.MaxValue money, tiny timeScale, empty raw");

        F3AutotestSnapshotRecord tampered = F3AutotestJudges.DecodeSnapshot(F3AutotestJudges.EncodeSnapshot(full));
        tampered.Money++;
        string diff = null;
        Check(!SameSnapshot(full, tampered, out diff) && diff == "money", "red: field comparison notices a changed field");

        string encoded = F3AutotestJudges.EncodeSnapshot(full);
        Check(F3AutotestJudges.DecodeSnapshot(encoded).BufferCountsIncluded, "new snapshot preserves complete Buffer counting contract");
        Check(!F3AutotestJudges.DecodeSnapshot(encoded.Replace(",\"bufferCountsIncluded\":true", "")).BufferCountsIncluded,
            "legacy snapshot defaults to uncounted Buffer and cannot authorize deleting unknown old items");
        Check(encoded.StartsWith("{\"version\":1,", StringComparison.Ordinal), "snapshot starts with version 1");
        Check(F3AutotestJudges.DecodeSnapshot(encoded.Replace("{\"version\":1,", "{\"version\":2,")) == null, "red: snapshot version 2 is rejected");
        Check(F3AutotestJudges.DecodeSnapshot(encoded.Replace("{\"version\":1,", "{")) == null, "red: snapshot without version is rejected");
        Check(F3AutotestJudges.DecodeSnapshot(encoded.Replace("{\"version\":1,", "{\"version\":\"1\",")) == null, "red: string version is rejected");
        Check(F3AutotestJudges.DecodeSnapshot(encoded.Substring(0, encoded.Length - 3)) == null, "red: truncated snapshot is rejected");
        Check(F3AutotestJudges.DecodeSnapshot("{not json") == null && F3AutotestJudges.DecodeSnapshot("") == null && F3AutotestJudges.DecodeSnapshot(null) == null,
            "red: garbage, empty and null snapshots are rejected");

        Check(F3AutotestJudges.SnapshotStory(null) == null, "SnapshotStory(null) is null");
        SkyIslandStoryData fresh = F3AutotestJudges.SnapshotStory(empty);
        Check(fresh != null && F3AutotestJudges.SameStory(fresh, SkyIslandStoryRules.CreateDefault(), out diff), "rawExists=false gives the default story: " + diff);
        SkyIslandStoryData beacons = SkyIslandStoryRules.CreateDefault();
        beacons.flags = (int)(SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp);
        beacons.clearedEncounters = new[] { "D", "G" };
        var valid = new F3AutotestSnapshotRecord { RawExists = true, Raw = SkyIslandStoryCodec.Encode(beacons) };
        Check(F3AutotestJudges.SameStory(F3AutotestJudges.SnapshotStory(valid), beacons, out diff), "rawExists=true decodes the raw story: " + diff);
        Check(F3AutotestJudges.SnapshotStory(new F3AutotestSnapshotRecord { RawExists = true, Raw = "{\"schemaVersion\":1,\"flags\":" }) == null,
            "red: broken raw gives null (never restored)");
        Check(F3AutotestJudges.SnapshotStory(new F3AutotestSnapshotRecord { RawExists = true, Raw = null }) == null, "red: rawExists with null raw gives null");
        var ending = SkyIslandStoryRules.CreateDefault();
        ending.flags = (int)SkyIslandStoryFlag.Ending;
        Check(F3AutotestJudges.SnapshotStory(new F3AutotestSnapshotRecord { RawExists = true, Raw = SkyIslandStoryCodec.Encode(ending) }) == null,
            "red: raw the production codec rejects gives null");
    }

    private static string StoryKey { get { return SkyIslandStoryRules.StorageKey; } }

    private static void SnapshotServiceCases()
    {
        F3GameplayValidationRunner.AutotestWritesOpen = false;
        try { SnapshotServiceFlow(); }
        finally { F3GameplayValidationRunner.AutotestWritesOpen = false; }
    }

    private static void SnapshotServiceFlow()
    {
        // 一份非默认剧情：走生产入口写入并落盘。
        SavesSystem.Switch(41);
        var live = new SkyIslandStoryService();
        live.Open();
        string message;
        Check(live.RecordEncounterCleared("D") && live.RecordEncounterCleared("D_02") && live.RecordEncounterCleared("B"), "live: clears accepted");
        Check(live.TryApply(SkyIslandStoryAction.RepairWindBeacon, out message) && live.TryApply(SkyIslandStoryAction.FindOldLetter, out message), "live: story actions accepted: " + message);
        Check(live.RecordRegionVisited("A") && live.RecordRegionVisited("D"), "live: regions accepted");
        Check(live.RecordNote("Letter_01", out message), "live: journal note accepted: " + message);
        Check(live.TryClose() && SavesSystem.KeyExisits(StoryKey), "live: story flushed to the stub save");
        string originalRaw = SavesSystem.Load<string>(StoryKey);
        SkyIslandStoryData original = SkyIslandStoryCodec.Decode(originalRaw);
        Check(original != null && original.Has(SkyIslandStoryFlag.WindBeacon) && original.Has(SkyIslandStoryFlag.OldLetter) && original.EncounterCleared("D_02")
            && Array.IndexOf(original.discoveredNotes, "Letter_01") >= 0 && original.visitedRegions != 0, "live: non-default story on disk");

        var snapshot = new F3AutotestSnapshotRecord
        {
            RunId = "fixture", Slot = SavesSystem.CurrentSlot, RawExists = SavesSystem.KeyExisits(StoryKey), Raw = originalRaw, Language = "English", TimeScale = 1f
        };
        F3AutotestSnapshotRecord stored = F3AutotestJudges.DecodeSnapshot(F3AutotestJudges.EncodeSnapshot(snapshot));
        string diff = null;
        Check(stored != null && stored.Raw == originalRaw && F3AutotestJudges.SameStory(F3AutotestJudges.SnapshotStory(stored), original, out diff),
            "snapshot keeps the story raw verbatim: " + diff);

        var errors = new List<string>();
        bool simulated;
        SkyIslandStoryData stage = Expect(ExpectedStages(FreshTable(), errors, out simulated), "ending_lamps").Copy();
        string reason;
        Check(!Judge(original, stage, out reason), "red baseline: the pre-run story is not the ending_lamps stage");

        var service = new SkyIslandStoryService();
        service.DevAutotestOpen();
        Check(F3AutotestJudges.SameStory(service.Current, original, out diff), "autotest open reads the slot: " + diff);
        int writes = SavesSystem.PhysicalWrites;
        string error;
        Check(!service.DevAutotestReplace(stage, true, out error) && error == F3GameplayValidationRunner.ClosedReason, "closed gate: replace refused with the gate reason: " + error);
        Check(!service.DevAutotestFlush(out error) && error == F3GameplayValidationRunner.ClosedReason, "closed gate: flush refused");
        Check(SavesSystem.Load<string>(StoryKey) == originalRaw && SavesSystem.PhysicalWrites == writes && F3AutotestJudges.SameStory(service.Current, original, out diff),
            "closed gate: save byte-identical, no disk write, cache untouched");

        F3GameplayValidationRunner.AutotestWritesOpen = true;
        Check(service.DevAutotestReplace(stage, false, out error) && SavesSystem.Load<string>(StoryKey) == originalRaw, "open gate, no flush: accepted but not yet on disk: " + error);
        F3GameplayValidationRunner.AutotestWritesOpen = false;
        Check(!service.DevAutotestFlush(out error) && SavesSystem.Load<string>(StoryKey) == originalRaw, "closed gate blocks the pending flush");
        F3GameplayValidationRunner.AutotestWritesOpen = true;
        Check(service.DevAutotestFlush(out error) && error == null && SavesSystem.PhysicalWrites == writes + 1, "open gate: pending replacement flushed: " + error);
        string stageRaw = SavesSystem.Load<string>(StoryKey);
        SkyIslandStoryData onDisk = SkyIslandStoryCodec.Decode(stageRaw);
        string back = null;
        Check(stageRaw == SkyIslandStoryCodec.Encode(stage) && Judge(onDisk, stage, out reason) && Judge(stage, onDisk, out back), "stage data is what the save holds: " + reason + back);
        stage.flags = 0;
        Check(service.Current.Has(SkyIslandStoryFlag.Ending), "replacement is copied, later edits of the argument do not leak");

        SkyIslandStoryData corrupt = SkyIslandStoryRules.CreateDefault();
        corrupt.flags = (int)SkyIslandStoryFlag.Ending;
        Check(!service.DevAutotestReplace(corrupt, true, out error) && error == "replacement_rejected_by_codec" && SavesSystem.Load<string>(StoryKey) == stageRaw,
            "red: codec-invalid replacement refused, save unchanged: " + error);
        Check(!service.DevAutotestReplace(null, true, out error) && error == "replacement_null", "red: null replacement refused");

        Check(service.DevAutotestReplace(F3AutotestJudges.SnapshotStory(stored), true, out error), "restore from snapshot accepted: " + error);
        string restoredRaw = SavesSystem.Load<string>(StoryKey);
        SkyIslandStoryData restored = SkyIslandStoryCodec.Decode(restoredRaw);
        Check(Judge(restored, original, out reason) && Judge(original, restored, out back), "restored story matches the pre-run story both ways: " + reason + back);
        Check(F3AutotestJudges.SameStory(restored, original, out diff) && restoredRaw == originalRaw, "restored raw is byte-identical to the snapshot: " + diff);
        Check(service.TryClose(), "autotest service closes");
        var reread = new SkyIslandStoryService();
        reread.Open();
        Check(F3AutotestJudges.SameStory(reread.Current, original, out diff), "a fresh session reads the restored story: " + diff);
        reread.TryClose();

        SnapshotServiceBarriers(stage);
    }

    private static void SnapshotServiceBarriers(SkyIslandStoryData stageTemplate)
    {
        var errors = new List<string>();
        bool simulated;
        SkyIslandStoryData stage = Expect(ExpectedStages(FreshTable(), errors, out simulated), "both_beacons");
        string error;

        // 写屏障：读不动的原文，门开着也不许覆盖。
        SavesSystem.Switch(42);
        const string broken = "{\"schemaVersion\":1,\"flags\":0,\"visitedRegions\":0,\"clearedEncounters\":42,\"discoveredNotes\":[]}";
        SavesSystem.Save(StoryKey, broken);
        var barred = new SkyIslandStoryService();
        barred.DevAutotestOpen();
        F3GameplayValidationRunner.AutotestWritesOpen = true;
        Check(!barred.DevAutotestReplace(stage, true, out error) && error != null && error.StartsWith("story_cannot_write:", StringComparison.Ordinal)
            && SavesSystem.Load<string>(StoryKey) == broken, "red: write barrier refuses replacement and keeps the unreadable raw: " + error);
        barred.TryClose();

        // 槽位变了：旧门面不许写，新槽也不出现剧情键。
        SavesSystem.Switch(43);
        var moved = new SkyIslandStoryService();
        moved.DevAutotestOpen();
        SavesSystem.Switch(44);
        Check(!moved.DevAutotestReplace(stage, true, out error) && error == "story_cannot_write:slot_changed" && !SavesSystem.KeyExisits(StoryKey),
            "red: slot change refuses replacement and never writes the new slot: " + error);
        Check(!moved.DevAutotestFlush(out error) && error == "slot_changed", "red: slot change refuses flush");
        moved.Close();
        SavesSystem.Switch(43);
        Check(!SavesSystem.KeyExisits(StoryKey), "old slot untouched after the refused write");

        // 新档快照（原文不存在）：还原写的是默认剧情。没有删键入口，结果是一份默认值的键。
        SavesSystem.Switch(45);
        var newSave = new F3AutotestSnapshotRecord { RawExists = SavesSystem.KeyExisits(StoryKey), Raw = null };
        var service = new SkyIslandStoryService();
        service.DevAutotestOpen();
        Check(service.DevAutotestReplace(stage, true, out error), "new save: stage written: " + error);
        Check(service.DevAutotestReplace(F3AutotestJudges.SnapshotStory(newSave), true, out error), "new save: restore accepted: " + error);
        string diff = null;
        Check(!newSave.RawExists && F3AutotestJudges.SameStory(SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(StoryKey)), SkyIslandStoryRules.CreateDefault(), out diff),
            "new save: restore leaves the default story on disk: " + diff);
        service.TryClose();
        F3GameplayValidationRunner.AutotestWritesOpen = false;
    }
}
