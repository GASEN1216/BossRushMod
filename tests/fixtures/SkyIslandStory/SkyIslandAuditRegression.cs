using System;
using BossRush;
using Saves;
using Duckov.NoteIndexs;

internal static class SkyIslandAuditRegression
{
    internal static void Run(Action<bool, string> check)
    {
        string message;
        SavesSystem.Switch(100101);
        var story = new SkyIslandStoryService();
        story.Open();
        L10n.IsChinese = true;
        string chinese = story.Summary;
        L10n.IsChinese = false;
        string english = story.Summary;
        check(english != chinese && english.Contains("Side paths:"), "summary refreshes without a story change when switching to English");
        L10n.IsChinese = true;
        check(story.Summary == chinese, "summary switches back to Chinese");
        check(story.TryClose(), "language probe closes cleanly");

        int slot = 100102;
        foreach (string id in new[] { "Light_E", "Frog_1" })
        {
            SavesSystem.Switch(slot++);
            story = new SkyIslandStoryService(); story.Open();
            PlayerStorage storage = PlayerStorage.Instance;
            PlayerStorage.Instance = null;
            check(!story.RecordNote(id, out message) && Array.IndexOf(story.Current.discoveredNotes, id) < 0,
                "unavailable asset snapshot rejects permanent cost record: " + id);
            PlayerStorage.Instance = storage;
            check(story.RecordNote(id, out message), "permanent cost record accepts ready assets: " + id);
            int collected = CharacterMainControl.Main.CharacterItem.SaveCalls;
            int writes = SavesSystem.PhysicalWrites;
            SavesSystem.FailPhysical = true;
            check(!story.TryClose(), "failed physical save retains permanent cost obligation: " + id);
            check(CharacterMainControl.Main.CharacterItem.SaveCalls > collected, "cost write collects current pack: " + id);
            SavesSystem.FailPhysical = false;
            SavesSystem.ClearStuckSaving();
            collected = CharacterMainControl.Main.CharacterItem.SaveCalls;
            check(story.TryClose() && SavesSystem.PhysicalWrites == writes + 1,
                "permanent cost batch retries exactly one successful physical save: " + id);
            check(CharacterMainControl.Main.CharacterItem.SaveCalls > collected && SavesSystem.KeyExisits("MainCharacterHealth"),
                "retry recollects assets before persisting permanent progress: " + id);
            story = new SkyIslandStoryService(); story.Open();
            check(Array.IndexOf(story.Current.discoveredNotes, id) >= 0, "permanent cost fact survives reopening: " + id);
            check(story.TryClose(), "reopened cost probe closes");
        }

        // 出击图里没有基地仓库（owner 2026-09-14「随撤离一起存」）：花材料换来的记录内存里立刻算数，
        // 存档里等这一趟结算——回到基地保留，退游戏撤掉；结算之前任何一次写盘都带不走它们。
        int raidSlot = 100110;
        foreach (bool keep in new[] { true, false })
        {
            SavesSystem.Switch(raidSlot++);
            story = new SkyIslandStoryService(); story.Open();
            story.RaidHeldCosts = true;
            PlayerStorage heldStorage = PlayerStorage.Instance;
            PlayerStorage.Instance = null;
            check(story.RecordNote("Light_F", out message) && Array.IndexOf(story.Current.discoveredNotes, "Light_F") >= 0,
                "raid: lamp counts in memory without a base storage snapshot (keep=" + keep + ")");
            check(story.RecordNote("Frog_2", out message), "raid: frog release accepted the same way (keep=" + keep + ")");
            check(story.TryApply(SkyIslandStoryAction.FindOldLetter, out message), "raid: unrelated story fact still commits (keep=" + keep + ")");
            story.Tick(true);
            SkyIslandStoryData midRaid = SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey));
            check(midRaid != null && midRaid.Has(SkyIslandStoryFlag.OldLetter)
                && Array.IndexOf(midRaid.discoveredNotes, "Light_F") < 0 && Array.IndexOf(midRaid.discoveredNotes, "Frog_2") < 0,
                "raid: mid-raid save carries the story fact but not the held records (keep=" + keep + ")");
            story.SettleRaidHeld(keep);
            check(story.TryClose(), "raid: settle then close persists (keep=" + keep + ")");
            SkyIslandStoryData settled = SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey));
            bool lampSaved = settled != null && Array.IndexOf(settled.discoveredNotes, "Light_F") >= 0;
            bool frogSaved = settled != null && Array.IndexOf(settled.discoveredNotes, "Frog_2") >= 0;
            check(settled != null && settled.Has(SkyIslandStoryFlag.OldLetter) && lampSaved == keep && frogSaved == keep,
                keep ? "raid: returning to base keeps the held records" : "raid: quitting drops the held records");
            PlayerStorage.Instance = heldStorage;
        }

        SavesSystem.Switch(100104);
        var data = SkyIslandStoryRules.CreateDefault();
        // 秘境物证 S1 走生产路径：解开谜题只写剧情旗标，不进 discoveredNotes（2026-09-14 审核 F-03，旧夹具直接塞进 discoveredNotes 掩盖了它）。
        data.discoveredNotes = new[] { "Search_A" };
        data.flags = (int)SkyIslandStoryFlag.PlantingRecord;
        SavesSystem.Save(SkyIslandStoryRules.StorageKey, SkyIslandStoryCodec.Encode(data));
        NoteIndex.Instance = new NoteIndex();
        // 官方那边残留的点亮（换槽、存档回滚、旧版本误点亮）：我们的存档里没有这一条。
        NoteIndex.SetNoteUnlocked(SkyIslandNoteBridge.BuildNoteKey("Search_B"));
        int subscribers = SavesSystem.Subscribers;
        SkyIslandNoteBridge.EnsureRuntime();
        SkyIslandNoteBridge.EnsureRuntime();
        check(SavesSystem.Subscribers == subscribers + 1, "note mirror runtime subscribes once");
        SkyIslandNoteBridge.Tick();
        check(NoteIndex.Instance.Notes.Count == 20 && NoteIndex.GetNoteUnlocked(SkyIslandNoteBridge.BuildNoteKey("Search_A")),
            "cold start at base restores note list and saved discovery without entering the island");
        check(NoteIndex.GetNoteUnlocked(SkyIslandNoteBridge.BuildNoteKey("Search_S1")), "saved puzzle evidence restores its corresponding official note");
        check(SkyIslandJournal.Recorded(data, "Search_S1") && SkyIslandJournal.NotesRecorded(data) == 2,
            "puzzle evidence recorded only as a story flag counts in the journal");
        check(!NoteIndex.GetNoteUnlocked(SkyIslandNoteBridge.BuildNoteKey("Search_B")),
            "official unlock missing from our save is taken back (our save is the authority)");
        SkyIslandNoteBridge.RequestSync(); SkyIslandNoteBridge.Tick();
        check(NoteIndex.Instance.Notes.Count == 20, "repeated base refresh never duplicates notes");
        SavesSystem.Switch(100105);
        NoteIndex.Instance = new NoteIndex();
        SkyIslandNoteBridge.Tick();
        check(!NoteIndex.GetNoteUnlocked(SkyIslandNoteBridge.BuildNoteKey("Search_A")) && !SavesSystem.KeyExisits(SkyIslandStoryRules.StorageKey),
            "empty slot neither inherits discoveries nor creates a story save from note registration");
        NoteIndex.Instance = null;
        SkyIslandNoteBridge.RequestSync(); SkyIslandNoteBridge.Tick();
        NoteIndex.Instance = new NoteIndex();
        UnityEngine.Time.unscaledTime += 2f;
        SkyIslandNoteBridge.Tick(data);
        check(NoteIndex.GetNoteUnlocked(SkyIslandNoteBridge.BuildNoteKey("Search_A")), "late note index retries using live island progress");
        SkyIslandNoteBridge.ResetStaticCaches();
        check(SavesSystem.Subscribers == subscribers, "note mirror unsubscribes on runtime cleanup");
    }
}
