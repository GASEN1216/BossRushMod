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

        SavesSystem.Switch(100104);
        var data = SkyIslandStoryRules.CreateDefault();
        data.discoveredNotes = new[] { "Search_A", "Search_S1" };
        data.flags = (int)SkyIslandStoryFlag.PlantingRecord;
        SavesSystem.Save(SkyIslandStoryRules.StorageKey, SkyIslandStoryCodec.Encode(data));
        NoteIndex.Instance = new NoteIndex();
        int subscribers = SavesSystem.Subscribers;
        SkyIslandNoteBridge.EnsureRuntime();
        SkyIslandNoteBridge.EnsureRuntime();
        check(SavesSystem.Subscribers == subscribers + 1, "note mirror runtime subscribes once");
        SkyIslandNoteBridge.Tick();
        check(NoteIndex.Instance.Notes.Count == 20 && NoteIndex.GetNoteUnlocked(SkyIslandNoteBridge.BuildNoteKey("Search_A")),
            "cold start at base restores note list and saved discovery without entering the island");
        check(NoteIndex.GetNoteUnlocked(SkyIslandNoteBridge.BuildNoteKey("Search_S1")), "saved puzzle evidence restores its corresponding official note");
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
