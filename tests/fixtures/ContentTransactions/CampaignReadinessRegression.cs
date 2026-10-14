using System;
using BossRush;
using Saves;

partial class Program
{
    static void CampaignExplicitCodecRegression()
    {
        // 实档的最小脱敏形态：整个 chapters 字段缺失，交付 token 和引导仍存在。
        const string lostChapters = "{\"schemaVersion\":1,\"grantedTokens\":[\"BossRush_Campaign_Unlock_Ch1\",\"BossRush_Campaign_Unlock_Ch2\",\"BossRush_Campaign_Unlock_Ch3\",\"BossRush_Campaign_Unlock_Ch4\",\"BossRush_Campaign_Unlock_Ch5\"],\"unlockedClues\":[\"clue_ch1\"],\"acceptedGuides\":[\"mode_g\"],\"lastUpdatedTicks\":639270239882546286}";
        Reset();
        SavesSystem.Cache[CampaignTuning.ProgressSaveKey] = lostChapters;
        CampaignSaveCoordinator.EnsureSubscribed();
        CampaignProgressService.EnsureInitialized();
        for (int i = 1; i <= 5; i++)
            Check(CampaignProgressService.GetState("ch" + i) == CampaignChapterState.Completed,
                "missing chapter array recovers delivered chapter " + i + " from its persisted token");
        Check(CampaignProgressService.GetState("ch6") == CampaignChapterState.Available,
            "recovery does not invent an accepted or completed chapter six");
        Check(CampaignPersistence.IsGuideAccepted("mode_g") && SavesSystem.Writes == 0,
            "recovery preserves Jeff guide facts without writing the save during read");
        Check(CampaignProgressService.TryAcceptContract("ch6"), "recovered save can accept chapter six");
        Check(CampaignProgressService.NotifyObjectivesSatisfied("ch6"), "recovered save can complete chapter six objectives");
        Check(CampaignSaveCoordinator.TryFlushOnHostDestroy(), "exit flushes the latest recovered and newly earned facts");
        ReopenCampaignFromDisk();
        Check(CampaignProgressService.GetState("ch6") == CampaignChapterState.ReadyToDeliver,
            "production explicit codec preserves newly earned, not yet delivered progress across restart");
        for (int i = 1; i <= 5; i++) Check(CampaignProgressService.GetState("ch" + i) == CampaignChapterState.Completed,
            "recovered chapter remains completed after production encode/decode " + i);

        var decode = typeof(CampaignPersistence).GetMethod("Decode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Func<string, CampaignSaveData> parse = raw => (CampaignSaveData)decode.Invoke(null, new object[] { raw });
        var explicitEmpty = parse(lostChapters.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"chapters\":[]"));
        Check(explicitEmpty.chapters.Length == 0, "explicit chapter arrays stay authoritative; tokens do not overwrite them");
        Check(parse("{\"schemaVersion\":1,\"chapters\":null,\"acceptedGuides\":null}").chapters.Length == 0,
            "legacy null arrays retain empty-array compatibility");
        Check(parse("{\"schemaVersion\":1,\"chapters\":[{\"chapterId\":\"ch1\"}]}") == null,
            "incomplete chapter entries are rejected instead of overwriting progress with default state");
        Check(parse("{\"schemaVersion\":1,\"chapters\":[{\"chapterId\":\"ch1\",\"state\":4},{\"chapterId\":\"ch1\",\"state\":1}]}") == null,
            "conflicting duplicate chapter facts are rejected");
        Check(parse(lostChapters).lastUpdatedTicks == 639270239882546286L, "long timestamp preserves all digits");
    }

    static void CampaignFinalScopeLifecycle()
    {
        foreach (string change in new[] { "stop-host", "stop-client", "room", "role" })
        {
            Reset();
            BossRushCoop.IsConnected = true; BossRushCoop.Host = change != "stop-client"; BossRushCoop.RunId = "original";
            var runtime = new CampaignRuntimeModule(); runtime.OnAwake(ModBehaviour.Instance);
            runtime.BeginFinalScopeForTest();
            int cleanups = runtime.FinalScopeCleanups;
            if (change.StartsWith("stop")) BossRushCoop.IsConnected = false;
            else if (change == "room") BossRushCoop.RunId = "successor";
            else BossRushCoop.Host = false;
            runtime.OnUpdate(0.1f, 0.1f);
            Check(runtime.FinalScopeCleanups == cleanups + 1 && !runtime.FinalScopeActive
                && runtime.FinalScopeDestroyed == (change != "stop-client"),
                "actual runtime tick retires the frozen terminal scope on " + change);
            BossRushCoop.IsConnected = BossRushCoop.Host = true; BossRushCoop.RunId = "original";
            runtime.OnUpdate(0.1f, 0.1f);
            Check(runtime.FinalScopeLost && runtime.FinalScopeCleanups == cleanups + 1,
                "retired terminal scope cannot revive after reconnect " + change);
            runtime.OnDestroy();
        }
        Reset(); BossRushCoop.IsConnected = BossRushCoop.Host = true; BossRushCoop.RunId = "original";
        var handshake = new CampaignRuntimeModule(); handshake.OnAwake(ModBehaviour.Instance); handshake.BeginFinalScopeForTest();
        int before = handshake.FinalScopeCleanups;
        BossRushCoop.RunId = null; handshake.OnUpdate(0.1f, 0.1f);
        BossRushCoop.RunId = "original"; handshake.OnUpdate(0.1f, 0.1f);
        Check(handshake.FinalScopeActive && handshake.FinalScopeCleanups == before,
            "same room empty handshake preserves a running terminal scope");
        handshake.OnDestroy();
        Reset(); BossRushCoop.IsConnected = BossRushCoop.Host = true;
        var pending = new CampaignRuntimeModule(); pending.OnAwake(ModBehaviour.Instance); pending.BeginFinalScopeForTest();
        BossRushCoop.RunId = "first-room"; pending.OnUpdate(0.1f, 0.1f);
        Check(pending.FinalScopeActive && !pending.FinalScopeLost, "first nonempty room binds an initially empty handshake");
        BossRushCoop.RunId = "next-room"; pending.OnUpdate(0.1f, 0.1f);
        Check(!pending.FinalScopeActive && pending.FinalScopeLost, "bound first room refuses a successor room");
        pending.OnSceneLoaded(new SceneRuntimeContext());
        Check(!pending.FinalScopeBound && !pending.FinalScopeLost, "real scene callback resets terminal scope for a new scene");
        pending.OnDestroy();
        Reset();
        var solo = new CampaignRuntimeModule(); solo.OnAwake(ModBehaviour.Instance); solo.BeginFinalScopeForTest();
        before = solo.FinalScopeCleanups; solo.OnUpdate(0.1f, 0.1f);
        Check(solo.FinalScopeActive && solo.FinalScopeCleanups == before, "fresh solo terminal scope remains active");
        solo.OnDestroy(); Reset();
    }

    // Official loading owns the cache bytes; deliberately deliver them after Mod Awake in the same slot.
    // The production runtime, progress service, persistence facade, store and coordinator remain linked.
    static void CampaignReadinessLifecycle()
    {
        Reset();
        LevelManager.LevelInited = false;
        SceneLoader.IsSceneLoading = true;
        CampaignRuntimeModule runtime = new CampaignRuntimeModule();
        runtime.OnAwake(ModBehaviour.Instance);
        runtime.OnStart();
        runtime.OnSceneLoaded(new SceneRuntimeContext());
        runtime.OnUpdate(0.1f, 0.1f);
        CampaignProgressService.EnsureInitialized();
        Check(!runtime.IsBootstrapped && CampaignPersistence.Current == null && SavesSystem.Reads == 0,
            "main menu and loading cannot classify an empty official cache as a new campaign save");
        Check(!CampaignProgressService.TryAcceptContract("ch1")
            && !CampaignPersistence.TryAdvanceGuide(CampaignGuideTable.ModeG, 1)
            && !CampaignPersistence.Store(CampaignPersistence.CreateDefault()) && SavesSystem.Writes == 0,
            "early chapter, Jeff guide and direct writes are rejected before the current slot is ready");

        CampaignSaveData saved = CampaignPersistence.CreateDefault();
        saved.chapters = new[] {
            new CampaignChapterRecord { chapterId = "ch1", state = (int)CampaignChapterState.Completed },
            new CampaignChapterRecord { chapterId = "ch2", state = (int)CampaignChapterState.ContractActive }
        };
        saved.grantedTokens = new[] { CampaignFacilityUnlocks.BuildTokenForChapter(1) };
        saved.unlockedClues = new[] { "clue_ch1" };
        saved.acceptedGuides = new[] { CampaignGuideTable.ModeG, CampaignGuideTable.ModeH, CampaignGuideTable.PetNest };
        saved.experiencedGuides = new[] { CampaignGuideTable.ModeG, CampaignGuideTable.ModeH };
        saved.completedGuides = new[] { CampaignGuideTable.ModeG };
        SavesSystem.Cache[CampaignTuning.ProgressSaveKey] = UnityEngine.JsonUtility.ToJson(saved);
        LevelManager.LevelInited = true;
        runtime.OnUpdate(0.1f, 0.1f);
        Check(!runtime.IsBootstrapped && SavesSystem.Reads == 0, "scene loading still blocks late-slot initialization");
        SceneLoader.IsSceneLoading = false;
        LevelManager.LevelInitializing = true;
        runtime.OnUpdate(0.1f, 0.1f);
        Check(!runtime.IsBootstrapped && SavesSystem.Reads == 0, "official level initialization still blocks the campaign load");
        LevelManager.LevelInitializing = false;
        runtime.OnUpdate(0.1f, 0.1f);
        Check(runtime.IsBootstrapped && CampaignProgressService.GetState("ch1") == CampaignChapterState.Completed
            && CampaignProgressService.GetActiveChapterId() == "ch2", "ready tick restores late-loaded chapter facts in the same slot");
        Check(CampaignFacilityUnlocks.IsTokenGranted(saved.grantedTokens[0])
            && CampaignProgressService.IsClueUnlocked("clue_ch1"), "late load publishes persisted facility tokens and clues");
        Check(CampaignPersistence.IsGuideCompleted(CampaignGuideTable.ModeG)
            && CampaignPersistence.IsGuideExperienced(CampaignGuideTable.ModeH)
            && !CampaignPersistence.IsGuideCompleted(CampaignGuideTable.ModeH)
            && CampaignPersistence.IsGuideAccepted(CampaignGuideTable.PetNest)
            && !CampaignPersistence.IsGuideExperienced(CampaignGuideTable.PetNest) && SavesSystem.Writes == 0,
            "Jeff completed, experienced and accepted facts survive delayed load without an empty overwrite");

        SavesSystem.SetFile(2);
        runtime.OnUpdate(0.1f, 0.1f);
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.Available
            && !CampaignPersistence.IsGuideAccepted(CampaignGuideTable.ModeG)
            && !CampaignFacilityUnlocks.IsTokenGranted(saved.grantedTokens[0]), "slot change clears chapter, guide and facility facts together");
        SavesSystem.SetFile(1);
        SavesSystem.Cache[CampaignTuning.ProgressSaveKey] = UnityEngine.JsonUtility.ToJson(saved);
        runtime.OnUpdate(0.1f, 0.1f);
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.Completed
            && CampaignPersistence.IsGuideCompleted(CampaignGuideTable.ModeG), "returning to the prior slot restores only its persisted facts");
        Check(CampaignProgressService.NotifyObjectivesSatisfied("ch2"), "loaded contract accepts its terminal objective before transition");
        CampaignSaveData beforeTransition = CampaignPersistence.Current;
        LevelManager.LevelInited = false;
        SceneLoader.IsSceneLoading = true;
        Check(ReferenceEquals(beforeTransition, CampaignPersistence.Current)
            && CampaignProgressService.GetState("ch2") == CampaignChapterState.ReadyToDeliver
            && CampaignFacilityUnlocks.IsTokenGranted(saved.grantedTokens[0]),
            "already-loaded same-slot facts remain queryable during loading without clearing unlocks");
        runtime.OnDestroy();
        LevelManager.LevelInited = true;
        SceneLoader.IsSceneLoading = false;
        ReopenCampaignFromDisk();
        Check(CampaignProgressService.GetState("ch2") == CampaignChapterState.ReadyToDeliver,
            "host destroy persists previously accepted pending even after level readiness disappears");

        Reset();
        runtime = new CampaignRuntimeModule(); runtime.OnAwake(ModBehaviour.Instance);
        SavesSystem.FailKey = CampaignTuning.ProgressSaveKey;
        Check(CampaignProgressService.TryAcceptContract("ch1") && CampaignPersistence.IsStoreFaulted,
            "teardown recovery begins with a real accepted candidate and injected key-write failure");
        SceneLoader.IsSceneLoading = true; LevelManager.LevelInited = false;
        runtime.OnDestroy();
        SceneLoader.IsSceneLoading = false; LevelManager.LevelInited = true;
        ReopenCampaignFromDisk();
        Check(CampaignProgressService.GetState("ch1") == CampaignChapterState.ContractActive,
            "fault recovery and final flush retain the accepted same-slot snapshot during teardown");

        Reset();
        runtime = new CampaignRuntimeModule(); runtime.OnAwake(ModBehaviour.Instance);
        CampaignProgressService.TryAcceptContract("ch1");
        LevelManager.Instance.IsBaseLevel = false;
        CampaignProgressService.NotifyObjectivesSatisfied("ch1");
        SceneLoader.IsSceneLoading = true; LevelManager.LevelInited = false;
        int writesBeforeSwitch = SavesSystem.Writes;
        SavesSystem.SetFile(2);
        Check(CampaignPersistence.Current == null, "a newly selected slot must wait for readiness during loading");
        runtime.OnDestroy();
        Check(SavesSystem.Writes == writesBeforeSwitch && !SavesSystem.Cache.ContainsKey(CampaignTuning.ProgressSaveKey),
            "teardown after a slot change never flushes the old contract into the new slot");
    }
}

namespace BossRush
{
    internal struct SceneRuntimeContext { }
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnStart() { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float deltaTime, float unscaledDeltaTime) { }
        public virtual void OnDestroy() { }
    }
    // Presentation/destruction is recorded; scope fields/methods are extracted unchanged from CampaignFinalBoss.
    internal sealed partial class CampaignRuntimeModule
    {
        internal int FinalScopeCleanups;
        internal bool FinalScopeDestroyed;
        internal bool FinalScopeActive { get { return campaignFinalBossActive; } }
        internal bool FinalScopeLost { get { return campaignFinalScopeLost; } }
        internal bool FinalScopeBound { get { return campaignFinalScopeBound; } }
        internal void BeginFinalScopeForTest() { CaptureCampaignFinalScope(); campaignFinalBossActive = true; }
        private void CleanupCampaignFinalBoss(bool destroyBoss)
        { FinalScopeCleanups++; FinalScopeDestroyed = destroyBoss; campaignFinalBossActive = false; }
        private void TickCampaignModeBridge(float deltaTime) { }
    }
    internal sealed class OfficialQuestRuntimeAdapter { internal readonly object Projection = new object(); }
    internal sealed class CampaignOfficialQuestClient
    {
        internal static int Refreshes, ClearedPending;
        internal CampaignOfficialQuestClient(CampaignRuntimeModule module) { }
        internal void RegisterAll(object projection) { }
        internal void UnregisterAll() { }
        internal void NotifyProgressChanged() { Refreshes++; }
        internal void ClearPending() { ClearedPending++; }
    }
    internal static class CampaignGuideFacts { internal static void ObserveAccepted(ModBehaviour owner) { } }
    internal static class CampaignHud { internal static void Tick() { } internal static void ResetStaticCaches() { } }
    internal static class CampaignObjectiveCollector { internal static void ResetStaticCaches() { } }
    internal static class CampaignNoteBridge
    {
        internal static int Refreshes;
        internal static bool ThrowOnRefresh;
        internal static void EnsureNotesRegistered() { Refreshes++; if (ThrowOnRefresh) throw new InvalidOperationException("injected notes refresh failure"); }
        internal static void ResetStaticCaches() { }
    }
    internal static class CampaignAssetCache { internal static void ResetStaticCaches() { } }
    internal static class CampaignBaseObjectives { internal static void ResetStaticCaches() { } }
}
