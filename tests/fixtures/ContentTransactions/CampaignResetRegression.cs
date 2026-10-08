using System;
using BossRush;
using Saves;

partial class Program
{
    static void PrepareCampaignReset()
    {
        Reset();
        UnityEngine.SceneManagement.SceneManager.ActiveName = "Base_SceneV2";
        ModBehaviour.Instance.IsActive = ModBehaviour.Instance.IsModeDActive =
            ModBehaviour.Instance.IsModeEActive = ModBehaviour.Instance.IsModeFActive = false;
        CampaignNoteBridge.Refreshes = 0;
        CampaignNoteBridge.ThrowOnRefresh = false;
        CampaignSaveCoordinator.EnsureSubscribed();
        CampaignSaveData prior = CampaignPersistence.Current;
        prior.chapters = new[] { new CampaignChapterRecord { chapterId = "ch1", state = (int)CampaignChapterState.Completed } };
        prior.grantedTokens = new[] { CampaignFacilityUnlocks.BuildTokenForChapter(1) };
        prior.unlockedClues = new[] { "clue_ch1" };
        prior.acceptedGuides = prior.experiencedGuides = prior.completedGuides = new[] { CampaignGuideTable.ModeG };
        Check(CampaignPersistence.Store(prior), "reset fixture stages completed campaign facts");
        string error;
        Check(CampaignSaveCoordinator.RequestImmediateFlush(out error), "reset fixture saves the original progress");
        CampaignProgressService.EnsureInitialized();
    }

    static void CheckResetKeptOriginal(string scenario)
    {
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.Completed
            && CampaignPersistence.IsGuideCompleted(CampaignGuideTable.ModeG)
            && CampaignFacilityUnlocks.IsTokenGranted(CampaignFacilityUnlocks.BuildTokenForChapter(1))
            && CampaignNoteBridge.Refreshes == 0, scenario + " keeps original progress and published unlocks");
        UnityEngine.Time.frameCount++; UnityEngine.Time.unscaledTime += 2f;
        CampaignSaveCoordinator.Tick();
        ReopenCampaignFromDisk();
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.Completed
            && CampaignPersistence.IsGuideCompleted(CampaignGuideTable.ModeG),
            scenario + " never saves a delayed reset after failure or restart");
    }

    static void CampaignResetTransactions()
    {
        string detail;
        PrepareCampaignReset();
        long money = Duckov.Economy.EconomyManager.Money;
        object bag = CharacterMainControl.Main.CharacterItem.Inventory;
        Check(CampaignProgressDevReset.DevResetCurrentSlot(ModBehaviour.Instance, SavesSystem.CurrentSlot, out detail),
            "confirmed base reset saves the new campaign snapshot");
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.Available
            && !CampaignPersistence.IsGuideAccepted(CampaignGuideTable.ModeG)
            && !CampaignFacilityUnlocks.IsTokenGranted(CampaignFacilityUnlocks.BuildTokenForChapter(1))
            && CampaignNoteBridge.Refreshes == 1, "successful reset publishes empty facts and refreshes notes once");
        Check(Duckov.Economy.EconomyManager.Money == money
            && ReferenceEquals(bag, CharacterMainControl.Main.CharacterItem.Inventory), "campaign reset preserves wallet and inventory");
        ReopenCampaignFromDisk();
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.Available
            && !CampaignPersistence.IsGuideCompleted(CampaignGuideTable.ModeG), "successful reset survives losing every campaign cache");

        PrepareCampaignReset();
        CampaignRuntimeModule runtime = new CampaignRuntimeModule();
        runtime.OnAwake(ModBehaviour.Instance); runtime.OnStart();
        ModBehaviour.Instance.CampaignRuntime = runtime;
        CampaignOfficialQuestClient.Refreshes = 0;
        CampaignOfficialQuestClient.ClearedPending = 0;
        runtime.SyncOfficialQuests();
        Check(CampaignOfficialQuestClient.Refreshes == 1 && CampaignOfficialQuestClient.ClearedPending == 0,
            "ordinary quest synchronization preserves queued delivery dialogue");
        CampaignOfficialQuestClient.Refreshes = 0;
        CampaignNoteBridge.ThrowOnRefresh = true;
        Check(CampaignProgressDevReset.DevResetCurrentSlot(ModBehaviour.Instance, SavesSystem.CurrentSlot, out detail)
            && detail.EndsWith(";refresh_pending", StringComparison.Ordinal),
            "a refresh exception cannot report a committed reset as a failed save");
        Check(CampaignOfficialQuestClient.Refreshes > 0, "quest refresh still runs after the notes refresh throws");
        Check(CampaignOfficialQuestClient.ClearedPending == 1, "reset clears queued delivery dialogue and unlock notices before quest refresh");
        CampaignNoteBridge.ThrowOnRefresh = false;
        ModBehaviour.Instance.CampaignRuntime = null;
        runtime.OnDestroy();
        ReopenCampaignFromDisk();
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.Available,
            "the reset remains saved when its post-commit presentation refresh fails");

        PrepareCampaignReset();
        SavesSystem.IsSaving = true;
        Check(!CampaignProgressDevReset.DevResetCurrentSlot(ModBehaviour.Instance, SavesSystem.CurrentSlot, out detail)
            && detail == "save_in_progress", "an in-flight official save rejects reset before mutation");
        SavesSystem.IsSaving = false;
        CheckResetKeptOriginal("official save gate");

        PrepareCampaignReset();
        Check(CampaignSaveCoordinator.BeginQuestDelivery(() => true, out detail), "reset fixture opens a real quest delivery");
        Check(!CampaignProgressDevReset.DevResetCurrentSlot(ModBehaviour.Instance, SavesSystem.CurrentSlot, out detail),
            "an active delivery rejects reset before replacing its completion facts");
        CampaignSaveCoordinator.EndQuestDelivery(false);
        CheckResetKeptOriginal("delivery gate");

        PrepareCampaignReset();
        Check(CampaignSaveCoordinator.BeginQuestDelivery(() =>
        {
            CampaignSaveData collected = CampaignProgressService.CloneSaveData(CampaignPersistence.Current);
            collected.completedGuides = new[] { CampaignGuideTable.ModeG, CampaignGuideTable.ModeH };
            return CampaignPersistence.Store(collected);
        }, out detail), "reset fixture opens a delivery whose collector updates the current facts");
        SavesSystem.IsSaving = true;
        CampaignSaveCoordinator.EndQuestDelivery(true);
        SavesSystem.IsSaving = false;
        SavesSystem.AfterPhysicalSave = () => { SavesSystem.FailPhysical = 1; };
        Check(!CampaignProgressDevReset.DevResetCurrentSlot(ModBehaviour.Instance, SavesSystem.CurrentSlot, out detail)
            && CampaignPersistence.IsGuideCompleted(CampaignGuideTable.ModeH),
            "failed reset rolls back to facts accepted by the preflight collector");
        CheckResetKeptOriginal("post-collection snapshot");
        Check(CampaignPersistence.IsGuideCompleted(CampaignGuideTable.ModeH),
            "rollback does not erase the newer facts saved by preflight collection");

        foreach (string failure in new[] { "key", "cache-readback", "physical", "rollback-physical", "disk-mismatch" })
        {
            PrepareCampaignReset();
            if (failure == "key") SavesSystem.FailKey = CampaignTuning.ProgressSaveKey;
            if (failure == "cache-readback") SavesSystem.FailReadAfterSaveKey = CampaignTuning.ProgressSaveKey;
            if (failure == "physical" || failure == "rollback-physical")
            { SavesSystem.FailPhysical = failure == "physical" ? 1 : 2; SavesSystem.StickSavingOnFailure = true; }
            if (failure == "disk-mismatch") SavesSystem.SkipNextPhysicalWrite = true;
            Check(!CampaignProgressDevReset.DevResetCurrentSlot(ModBehaviour.Instance, SavesSystem.CurrentSlot, out detail),
                failure + " is reported as a failed reset");
            CheckResetKeptOriginal(failure);
        }

        PrepareCampaignReset();
        int writes = SavesSystem.Writes;
        Check(!CampaignProgressDevReset.DevResetCurrentSlot(ModBehaviour.Instance, SavesSystem.CurrentSlot + 1, out detail)
            && detail == "slot_changed" && SavesSystem.Writes == writes,
            "confirming another slot cannot clear the current slot");
        UnityEngine.SceneManagement.SceneManager.ActiveName = "Level_1";
        Check(!CampaignProgressDevReset.DevResetCurrentSlot(ModBehaviour.Instance, SavesSystem.CurrentSlot, out detail)
            && detail == "not_in_base" && SavesSystem.Writes == writes, "raid reset is rejected without writing");
        UnityEngine.SceneManagement.SceneManager.ActiveName = "Base_SceneV2";
        CheckResetKeptOriginal("slot and scene gates");

        Reset();
        const string future = "{\"schemaVersion\":99,\"chapters\":[]}";
        SavesSystem.Cache[CampaignTuning.ProgressSaveKey] = future;
        writes = SavesSystem.Writes;
        Check(!CampaignProgressDevReset.DevResetCurrentSlot(ModBehaviour.Instance, SavesSystem.CurrentSlot, out detail)
            && CampaignPersistence.HasWriteBarrier && SavesSystem.Writes == writes
            && SavesSystem.Load<string>(CampaignTuning.ProgressSaveKey) == future,
            "a not-yet-loaded future schema establishes its write barrier before the reset");
    }
}

// ES3 and scene are host boundaries. Location.File must read the disk snapshot independently of Cache.
internal sealed class ES3Settings
{
    internal ES3.Location location;
    // 保留官方重载集合；无类型的 null 在真实编译器中有歧义。
    internal ES3Settings(string path, ES3Settings options) { location = ES3.Location.Cache; }
    internal ES3Settings(string path, params Enum[] options) { location = ES3.Location.Cache; }
}
internal static class ES3
{
    internal enum Location { File, Cache }
    internal static bool KeyExists(string key, string path, ES3Settings settings)
    { return settings.location == Location.File ? SavesSystem.Disk.ContainsKey(key) : SavesSystem.Cache.ContainsKey(key); }
    internal static T Load<T>(string key, string path, ES3Settings settings)
    { return (T)(settings.location == Location.File ? SavesSystem.Disk[key] : SavesSystem.Cache[key]); }
}
namespace UnityEngine.SceneManagement
{
    internal struct Scene { public string name; }
    internal static class SceneManager
    {
        internal static string ActiveName = "Base_SceneV2";
        internal static Scene GetActiveScene() { return new Scene { name = ActiveName }; }
    }
}
