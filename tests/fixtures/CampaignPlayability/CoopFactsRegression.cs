using System;
using BossRush;

internal static partial class CoopFactsRegression
{
    private static Action<bool, string> check;
    private static CampaignRuntimeModule Setup(string chapter, string scope = "original-scope", bool snapshot = true, bool host = false)
    {
        CampaignFactNetwork.Reset();
        CampaignObjectiveTracker.ResetStaticCaches();
        CampaignProgressService.Active = chapter; CampaignProgressService.State = CampaignChapterState.ContractActive;
        CampaignProgressService.Notifications = 0; CampaignProgressService.Reject = false;
        BossRushCoop.IsConnected = true; BossRushCoop.IsHost = host; BossRushCoop.HasBoundScene = true;
        BossRushCoop.RunId = "original-room"; BossRushCoop.SceneToken = "native-31";
        BossRushCoop.SceneGeneration = 31; BossRushCoop.LocalAssetId = "asset-A"; BossRushCoop.LocalPlayerId = "guest";
        CharacterMainControl.Main = new CharacterMainControl { Health = new Health(), IsMainCharacter = true };
        var owner = new ModBehaviour { bossRushArenaActive = true, IsActive = chapter == "ch1",
            modeDActive = chapter == "ch2", modeEActive = chapter == "ch3", modeFActive = chapter == "ch4", ModeDWaveIndex = 1 };
        ModBehaviour.Instance = owner; owner.SetCampaignEntryForTest(scope);
        if (chapter == "ch5") owner.zombieModeRunState = new ZombieRun { RunId = 7, LifecyclePhase = 1, CurrentWave = 1 };
        owner.AdmitCampaignForTest("guest", "asset-A");
        var module = owner.CampaignRuntime; module.InitFactsForTest();
        if (snapshot && host) module.CaptureWorldForTest();
        if (snapshot && !host) module.ApplyWorldForTest(new CoopCampaignWorld { Mode = CampaignContentCatalog.GetChapter(chapter).Mode,
            SceneToken = BossRushCoop.SceneToken, Scope = CampaignContentCatalog.GetChapter(chapter).Mode + ":" + (chapter == "ch6" ? "1" : scope), FinalRun = chapter == "ch6" ? 1 : 0, FinalActive = chapter == "ch6",
            FinalParticipants = new[] { new CoopCampaignParticipant { Player="guest", Asset="asset-A" } } });
        owner.TickCampaignModeBridge(0f);
        check(CampaignObjectiveTracker.IsArmed && module.FactLeaseCountForTest == 1, "real world snapshot and mode entry capture the original contract lease " + chapter);
        return module;
    }
    private static void Transition(CampaignRuntimeModule owner, bool bound)
    {
        BossRushCoop.HasBoundScene = false; BossRushCoop.SceneToken = null;
        owner.OnSceneLoaded(new SceneRuntimeContext());
        if (bound) { BossRushCoop.HasBoundScene = true; BossRushCoop.SceneToken = "native-32"; BossRushCoop.SceneGeneration = 32; }
        check(!CampaignObjectiveTracker.IsArmed, "real scene lifecycle removes current tracking without destroying original debt");
    }
    internal static void Run(Action<bool, string> assertion)
    {
        check = assertion;
        AssetReturnAndEarlyPersonalCapture();
        InitialWorldDelay();
        FinalEntryEdges();
        foreach (bool bound in new[] { false, true })
        {
            var module = Setup("ch6"); var final = module.FactForTest("final", "final-original");
            Transition(module, bound);
            check(module.ApplyFactForTest(final) && CampaignProgressService.Notifications == 1 && !CampaignObjectiveTracker.IsArmed,
                "earned final fact settles only original contract across unbound/bound transition " + bound);
            check(module.ApplyFactForTest(final) && CampaignProgressService.Notifications == 1, "late final duplicate is idempotent");
            module.FactsCleanupForTest();
            module = Setup("ch1"); var clear = module.FactForTest("standard", "standard-original");
            Transition(module, bound);
            check(module.ApplyFactForTest(clear) && CampaignProgressService.Notifications == 1 && !CampaignObjectiveTracker.IsArmed,
                "earned standard fact remains payable after actual scene reset " + bound);
            module.FactsCleanupForTest();
        }
        var owner = Setup("ch1");
        CampaignObjectiveTracker.ReportPlayerDamaged(1);
        var damagedClear = owner.FactForTest("standard", "damaged-original");
        Transition(owner, false);
        check(owner.ApplyFactForTest(damagedClear) && CampaignProgressService.Notifications == 0 && !CampaignObjectiveTracker.IsArmed,
            "old damaged standard run cannot borrow clean progress from new scene");
        owner.FactsCleanupForTest();

        owner = Setup("ch2"); var oldWave = owner.FactForTest("wave", "wave-five", wave: 5);
        var originalWave = CampaignObjectiveTracker.Progress[0];
        Transition(owner, true);
        ModBehaviour.Instance.TickCampaignModeBridge(0f);
        check(!CampaignObjectiveTracker.IsArmed, "old cached mode flags cannot rearm a new actual scene without its world scope");
        ModBehaviour.Instance.SetCampaignEntryForTest("new-native-scope");
        owner.ApplyWorldForTest(new CoopCampaignWorld { Mode = CampaignContentCatalog.ModeModeD, Scope = CampaignContentCatalog.ModeModeD+":new-native-scope", SceneToken = "native-32" });
        ModBehaviour.Instance.TickCampaignModeBridge(0f);
        var currentWave = CampaignObjectiveTracker.Progress[0];
        check(owner.ApplyFactForTest(oldWave) && originalWave.Current == 5 && currentWave.Current == 1 && CampaignProgressService.Notifications == 0,
            "old wave fact updates original progress object without touching current run");
        owner.FactsCleanupForTest();

        owner = Setup("ch6"); var cancelled = owner.FactForTest("final", "cancelled-final");
        CampaignObjectiveTracker.InvalidateContractIdentity(); Transition(owner, true);
        check(owner.ApplyFactForTest(cancelled) && CampaignProgressService.Notifications == 0,
            "abandon and reaccept same chapter invalidates original lease identity");
        owner.FactsCleanupForTest();
        owner = Setup("ch6"); var afterDeath = owner.FactForTest("final", "earned-before-death");
        CharacterMainControl.Main.Health.IsDead = true;
        check(owner.ApplyFactForTest(afterDeath) && CampaignProgressService.Notifications == 1,
            "death after original earning cannot erase pending original final completion");
        owner.FactsCleanupForTest();

        owner = Setup("ch2"); var first = owner.FactForTest("wave", "first-run-wave", wave: 5);
        var firstProgress = CampaignObjectiveTracker.Progress[0];
        ModBehaviour.Instance.SetCampaignEntryForTest("second-scope");
        owner.ApplyWorldForTest(new CoopCampaignWorld { Mode = CampaignContentCatalog.ModeModeD, Scope = CampaignContentCatalog.ModeModeD+":second-scope", SceneToken = "native-31" });
        ModBehaviour.Instance.TickCampaignModeBridge(0f);
        var secondProgress = CampaignObjectiveTracker.Progress[0];
        check(owner.FactLeaseCountForTest == 2 && owner.ApplyFactForTest(first) && firstProgress.Current == 5 && secondProgress.Current == 1,
            "same-scene new run scope cannot inherit delayed first-run wave");
        owner.FactsCleanupForTest();

        owner = Setup("ch4"); var personal = CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeModeF);
        check(CampaignObjectiveTracker.ObserveExtractionLeaseKill(personal, 1, true), "personal marked receipt first is accepted");
        var marked = owner.FactForTest("kill", "same-marked-victim", boss: true, bounty: true);
        check(owner.ApplyFactForTest(marked) && CampaignObjectiveTracker.Progress[0].Current == 1,
            "world marked kill after personal lower bound cannot double count the same kill");
        check(owner.ApplyFactForTest(marked) && CampaignObjectiveTracker.Progress[0].Current == 1, "duplicate world kill retains exact quantity");
        owner.FactsCleanupForTest();
        owner = Setup("ch4"); personal = CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeModeF);
        check(owner.ApplyFactForTest(owner.FactForTest("kill", "world-first", boss: true, bounty: true)), "world kill first is accepted");
        check(CampaignObjectiveTracker.ObserveExtractionLeaseKill(personal, 1, true) && CampaignObjectiveTracker.Progress[0].Current == 1,
            "personal lower bound after world kill cannot double count");
        owner.FactsCleanupForTest();

        owner = Setup("ch3");
        int count = 0;
        foreach (int i in new[] { 7, 2, 5, 1, 6, 3, 4 })
        {
            var reordered = owner.FactForTest("kill", "boss-" + i, boss: true);
            check(owner.ApplyFactForTest(reordered) && owner.ApplyFactForTest(reordered)
                && CampaignObjectiveTracker.Progress[0].Current == ++count,
                "reordered unique hostile boss retains one actual count " + i);
        }
        var eighth = owner.FactForTest("kill", "boss-8", boss: true);
        CampaignProgressService.Reject = true;
        check(!owner.ApplyFactForTest(eighth) && CampaignObjectiveTracker.Progress[0].Current == 8, "failed persistence leaves earned eighth boss unACKed");
        check(!owner.ApplyFactForTest(eighth) && CampaignObjectiveTracker.Progress[0].Current == 8, "failed write retry never counts eighth kill twice");
        CampaignProgressService.Reject = false;
        check(owner.ApplyFactForTest(eighth) && CampaignProgressService.Notifications == 1, "same eighth receipt retries original write exactly once");
        owner.FactsCleanupForTest();

        PendingAndSourceScopes();
        owner=Setup("ch6");
        var frozen=CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeFinal);
        var oldFinal=owner.FactForTest("final","asset-gated-final");
        BossRushCoop.LocalAssetId="asset-B";
        check(!CampaignObjectiveTracker.ReportCoopFactLease(frozen,oldFinal) && CampaignProgressService.Notifications==0,
            "generic original lease rejects another currently selected asset before mutation");
        BossRushCoop.LocalAssetId="asset-A";
        check(CampaignObjectiveTracker.ReportCoopFactLease(frozen,oldFinal) && CampaignProgressService.Notifications==1,
            "same original lease can finish when the original asset returns");
        owner.FactsCleanupForTest();
        owner=Setup("ch6"); CampaignObjectiveTracker.ResetSession(); CampaignProgressService.State=CampaignChapterState.Available;
        owner.ApplyWorldForTest(new CoopCampaignWorld { Mode=CampaignContentCatalog.ModeFinal,Scope=CampaignContentCatalog.ModeFinal+":2",SceneToken="native-31",FinalRun=2,FinalActive=true,
            FinalParticipants=new[]{new CoopCampaignParticipant{Player="guest",Asset="asset-A"}} });
        check(!CampaignObjectiveTracker.IsArmed && owner.ApplyFactForTest(owner.FactForTest("final","unaccepted-final")) && CampaignProgressService.Notifications==0,
            "real world entry without accepted contract freezes no qualification and cannot be armed by final fact");
        owner.OnDestroy();
        check(owner.FactLeaseCountForTest==0 && owner.EntryTokenCountForTest==0,"actual runtime OnDestroy releases frozen facts including ineligible entry tombstones");
        NetworkAssetRoundTrip();
        AttributionAndAdmission();
        ModBehaviour.Instance.CampaignRuntime.OnDestroy();
        BossRushCoop.IsConnected = false; BossRushCoop.IsHost = false;
        BossRushCoop.HasBoundScene = false; BossRushCoop.SceneToken = null;
        CampaignObjectiveTracker.ResetStaticCaches();
    }
    private static void AssetReturnAndEarlyPersonalCapture()
    {
        var owner = Setup("ch1");
        var flawless = CampaignObjectiveTracker.Progress[1];
        CampaignObjectiveTracker.Tick(17.25f);
        var first = CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeStandard);
        CampaignObjectiveTracker.ResetSession(); BossRushCoop.LocalAssetId = "asset-B";
        ModBehaviour.Instance.TickCampaignModeBridge(91.5f);
        check(!CampaignObjectiveTracker.IsArmed && CampaignObjectiveTracker.Progress.Count == 0
            && !CampaignObjectiveTracker.RestoreExtractionLease(first),
            "unadmitted asset B cannot track or borrow the original A lease");
        CampaignObjectiveTracker.ResetSession(); BossRushCoop.LocalAssetId = "asset-A";
        ModBehaviour.Instance.TickCampaignModeBridge(2.5f);
        check(ReferenceEquals(flawless, CampaignObjectiveTracker.Progress[1]),
            "return admitted A restores exact original progress after unadmitted B was excluded");
        check(Math.Abs(first.Clock.Seconds - 19.75f) < .001f,
            "return A retains fractional survival seconds and excludes time spent on B");
        CampaignObjectiveTracker.ReportPlayerDamaged(1);
        check(flawless.Failed && owner.ApplyFactForTest(owner.FactForTest("standard", "clear-after-return-damage"))
            && CampaignProgressService.Notifications == 0,
            "return to original asset and actual new damage cannot complete its frozen flawless contract");
        owner.FactsCleanupForTest();
        owner = Setup("ch1");
        var completed = CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeStandard);
        CampaignObjectiveTracker.ReportStandardClear();
        CampaignObjectiveTracker.ResetSession(); ModBehaviour.Instance.TickCampaignModeBridge(30f);
        check(completed.Clock.Notified && !CampaignObjectiveTracker.IsArmed && CampaignProgressService.Notifications == 1,
            "successful original local notification latch is shared and cannot rearm on return");
        owner.FactsCleanupForTest();

        owner = Setup("ch4");
        var original = CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeModeF);
        CampaignObjectiveTracker.Tick(59.75f);
        CampaignObjectiveTracker.ResetSession(); BossRushCoop.LocalAssetId = "asset-B";
        ModBehaviour.Instance.TickCampaignModeBridge(120f);
        CampaignObjectiveTracker.ResetSession(); BossRushCoop.LocalAssetId = "asset-A";
        var personal = CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeModeF);
        check(ReferenceEquals(original.Clock, personal.Clock) && ReferenceEquals(original.Objectives[0], personal.Objectives[0]),
            "personal extraction capture shares original campaign progress and exact clock after return A");
        ModBehaviour.Instance.TickCampaignModeBridge(.5f);
        check(Math.Abs(original.Clock.Seconds - 60.25f) < .001f,
            "fractional original survival clock crosses minute boundary without rounding or away-time credit");
        ModBehaviour.Instance.SetCampaignEntryForTest("next-F-before-bridge");
        var nextPersonal = CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeModeF);
        check(nextPersonal != null && !ReferenceEquals(nextPersonal.Objectives[0], original.Objectives[0])
            && nextPersonal.Clock.Seconds == 0f && nextPersonal.Objectives[1].Current == 0,
            "early F personal start before campaign bridge freezes new epoch progress instead of old same-mode progress");
        ModBehaviour.Instance.TickCampaignModeBridge(3.25f);
        check(CampaignObjectiveTracker.IsExtractionLeaseCurrent(nextPersonal) && Math.Abs(nextPersonal.Clock.Seconds - 3.25f) < .001f
            && Math.Abs(original.Clock.Seconds - 60.25f) < .001f,
            "campaign capture after personal start keeps new shared timer and leaves old timer untouched");
        var oldFact = owner.FactForTest("kill", "old-F-marked", boss: true, bounty: true);
        oldFact.Scope = CampaignContentCatalog.ModeModeF + ":original-scope";
        check(owner.ApplyFactForTest(oldFact) && original.Objectives[0].Current == 1 && nextPersonal.Objectives[0].Current == 0,
            "late old F fact updates only old entry qualification after early personal capture");
        CampaignObjectiveTracker.InvalidateContractIdentity(); CampaignObjectiveTracker.ResetSession();
        ModBehaviour.Instance.TickCampaignModeBridge(90f);
        check(!CampaignObjectiveTracker.IsArmed && original.Clock.Seconds == 60.25f && nextPersonal.Clock.Seconds == 3.25f,
            "cancel and reaccept cannot restore either original epoch or accrue fresh progress without new entry");
        owner.OnDestroy();
        owner = Setup("ch6");
        var notified = CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeFinal);
        check(owner.ApplyFactForTest(owner.FactForTest("final", "original-fact-notified")) && notified.Clock.Notified,
            "successful original fact shares notification latch with all leases of that progress");
        owner.OnDestroy();
    }
    private static void InitialWorldDelay()
    {
        foreach (string chapter in new[] { "ch1", "ch2", "ch3", "ch4", "ch5" })
        {
            var owner = Setup(chapter, "entry-before-campaign-world", false);
            var kind = chapter == "ch1" ? "standard" : chapter == "ch2" || chapter == "ch5" ? "wave" : "kill";
            var earned = owner.FactForTest(kind, "earned-before-first-world", boss: true, bounty: true, wave: 5);
            var original = CampaignObjectiveTracker.Progress[0];
            ModBehaviour.Instance.IsActive = ModBehaviour.Instance.modeDActive = ModBehaviour.Instance.modeEActive = ModBehaviour.Instance.modeFActive = false;
            if (ModBehaviour.Instance.zombieModeRunState != null) ModBehaviour.Instance.zombieModeRunState.LifecyclePhase = 0;
            owner.ApplyWorldForTest(new CoopCampaignWorld { SceneToken = "native-31" });
            check(owner.ApplyFactForTest(earned) && original.Current > 0,
                "real entry before first Campaign world retains earned fact after terminal null snapshot " + chapter);
            Transition(owner, true);
            check(owner.ApplyFactForTest(earned) && original.Current > 0,
                "same original entry remains identifiable when first fact retry follows scene binding " + chapter);
            owner.FactsCleanupForTest();
        }
        var module = Setup("ch2", "unannounced-first-entry", false);
        var old = module.FactForTest("wave", "old-wave-without-world", wave: 5);
        var originalWave = CampaignObjectiveTracker.Progress[0];
        ModBehaviour.Instance.SetCampaignEntryForTest("unannounced-second-entry");
        ModBehaviour.Instance.TickCampaignModeBridge(0f);
        var nextWave = CampaignObjectiveTracker.Progress[0];
        check(module.FactLeaseCountForTest == 2 && !ReferenceEquals(originalWave, nextWave)
            && module.ApplyFactForTest(old) && originalWave.Current == 5 && nextWave.Current == 1,
            "two real same-mode entries before any Campaign snapshot retain separate original progress");
        module.FactsCleanupForTest();
    }
    private static void FinalEntryEdges()
    {
        int burstBaseline = CampaignFinalBossFx.SummonBursts;
        var module = Setup("ch6");
        module.OnSceneLoaded(new SceneRuntimeContext()); module.FactsCleanupForTest(); module.InitFactsForTest();
        BossRushCoop.IsHost = true;
        BossRushCoop.Players.Add(new BossRushCoopPlayer { Id="guest", AssetId="asset-A", IsConnected=true, IsAlive=true, IsLocal=true, Character=CharacterMainControl.Main });
        var host = ModBehaviour.Instance; host.Arena = true; host.CreateSignForTest();
        SceneLoader.IsSceneLoading = false; LevelManager.AfterInit = true;
        host.SpawnResult = new System.Threading.Tasks.TaskCompletionSource<CharacterMainControl>();
        host.StartCampaignFinalBoss();
        var first = module.CaptureWorldForTest();
        check(host.IsCampaignFinalBossActive && module.FactLeaseCountForTest == 1 && first.Scope == CampaignContentCatalog.ModeFinal+":"+first.FinalRun,
            "real final start captures its actual run before prologue factory or first periodic publish");
        BossRushCoop.Players.Add(new BossRushCoopPlayer { Id="late", AssetId="late-asset", IsConnected=true, IsAlive=true });
        check(first.FinalParticipants.Length==1 && first.FinalParticipants[0].Player=="guest"
            && module.CaptureWorldForTest().FinalParticipants.Length==1,
            "actual final entry freezes cohort and excludes later arrival from current fight");
        var earned = module.FactForTest("final", "first-final-before-cleanup");
        module.CleanupCampaignFinalBoss(false);
        check(module.CaptureWorldForTest().Scope == null, "actual final cleanup produces inactive source edge");
        check(host.DebugStartCampaignFinalBossForValidation(), "real Dev final entry can start the next same-scene run");
        var second = module.CaptureWorldForTest();
        check(second.Scope != first.Scope && module.FactLeaseCountForTest == 2,
            "normal and Dev final entries use separate real FinalRun scopes without a bridge tick");
        check(module.ApplyFactForTest(earned) && CampaignProgressService.Notifications == 1,
            "late earlier final fact retains the original contract after actual final restart");
        module.CleanupCampaignFinalBoss(false); module.FactsCleanupForTest();
        host.SpawnResult.TrySetResult(null);
        UnityEngine.Object.Destroy(host.CampaignArenaSignForRuntime.gameObject);
        CampaignFinalBossFx.SummonBursts = burstBaseline;
    }
    private static void PendingAndSourceScopes()
    {
        var owner = Setup("ch6");
        var early = owner.FactForTest("final", "early-final"); early.Scope=CampaignContentCatalog.ModeFinal+":2";
        check(!owner.ApplyFactForTest(early) && CampaignProgressService.Notifications == 0 && owner.FactLeaseCountForTest == 1,
            "fact arriving before its genuine world scope stays pending and cannot manufacture a lease");
        owner.ApplyWorldForTest(new CoopCampaignWorld { Mode=CampaignContentCatalog.ModeFinal, Scope=early.Scope, SceneToken="native-31", FinalRun=2, FinalActive=true,
            FinalParticipants=new[]{new CoopCampaignParticipant{Player="guest",Asset="asset-A"}} });
        check(owner.ApplyFactForTest(early) && CampaignProgressService.Notifications==1 && owner.FactLeaseCountForTest==2,
            "later real final admission captures qualification and permits pending fact retry");
        owner.FactsCleanupForTest();
        owner = Setup("ch2");
        var scope = owner.CaptureWorldForTest();
        BossRushCoop.IsHost=true; owner.ResetProjectionForTest();
        var first=owner.CaptureWorldForTest(); var repeat=owner.CaptureWorldForTest();
        check(!string.IsNullOrEmpty(first.Scope) && first.Scope==repeat.Scope, "real host world capture keeps one scope throughout same active run");
        ModBehaviour.Instance.modeDActive=false; ModBehaviour.Instance.IsActive=false; owner.CaptureWorldForTest();
        ModBehaviour.Instance.modeDActive=true; ModBehaviour.Instance.SetCampaignEntryForTest("reopened-D");
        var second=owner.CaptureWorldForTest();
        check(first.Scope!=second.Scope, "real inactive-to-active world edge creates a distinct same-scene run scope");
        CharacterMainControl.Main.Health.IsDead=true;
        for(int i=0;i<4;i++)
        {
            var before=owner.CaptureWorldForTest(); ModBehaviour.Instance.TickCampaignModeBridge(1f);
            var after=owner.CaptureWorldForTest();
            check(before.Scope==second.Scope && after.Scope==second.Scope && after.Mode==CampaignContentCatalog.ModeModeD,
                "dead host personal reset cannot replace surviving guests world scope frame "+i);
        }
        check(!CampaignObjectiveTracker.IsArmed,"dead host personal tracker remains disarmed while shared world continues");
        CharacterMainControl.Main.Health.IsDead=false;
        ModBehaviour.Instance.modeDActive=false; ModBehaviour.Instance.modeEActive=true;
        var other=owner.CaptureWorldForTest();
        check(other.Mode==CampaignContentCatalog.ModeModeE && other.Scope!=second.Scope,"real change of world mode creates another independent scope");
        owner.FactsCleanupForTest();
        owner=Setup("ch6");
        var old=owner.FactForTest("final","foreign-world");
        BossRushCoop.HasBoundScene=false;
        CampaignObjectiveTracker.ResetSession(); ModBehaviour.Instance.TickCampaignModeBridge(2f);
        check(!CampaignObjectiveTracker.IsArmed, "pending native scene blocks old cached mode timer and rearming");
        int count=owner.FactLeaseCountForTest;
        int originalFinalRun=owner.CaptureWorldForTest().FinalRun;
        owner.ApplyWorldForTest(new CoopCampaignWorld { Mode=CampaignContentCatalog.ModeFinal,Scope="fake-unbound",SceneToken="native-31",FinalActive=false,FinalRun=999 });
        check(owner.CaptureWorldForTest().FinalRun==originalFinalRun && owner.CaptureWorldForTest().FinalActive,
            "unbound world snapshot cannot mutate actual final projection");
        check(owner.FactLeaseCountForTest==count && !CampaignObjectiveTracker.IsArmed,"unbound world snapshot cannot capture another contract scope");
        check(owner.ApplyFactForTest(old) && CampaignProgressService.Notifications==1,"bound gate on world projection never blocks original personal debt");
        owner.FactsCleanupForTest();
    }

    private static void NetworkAssetRoundTrip()
    {
        var owner = Setup("ch6");
        // Generate the actual host scope, then let the actual guest world Apply and entry capture it.
        BossRushCoop.IsHost = true; owner.ResetProjectionForTest();
        var state = owner.CaptureWorldForTest();
        BossRushCoop.IsHost = false; owner.ApplyWorldForTest(state); ModBehaviour.Instance.TickCampaignModeBridge(0f);
        BossRushCoop.Players.Add(new BossRushCoopPlayer { Id = "guest", AssetId = "asset-A", IsConnected = true, IsAlive = true });
        BossRushCoop.Players.Add(new BossRushCoopPlayer { Id = "dead", AssetId = "dead-asset", IsConnected = true, IsAlive = false });
        BossRushCoop.IsHost = true;
        BossRushCoop.LocalAssetId = "host-owned";
        check(owner.DeliverForTest("actual-final", new CoopCampaignFact { Kind = "final" }) && owner.FactsPendingForTest, "real delivery freezes host-issued final fact and original recipient asset");
        check(CampaignFactNetwork.Sent.FindAll(value => value.Topic == "BossRush.Personal.Campaign").Count == 1,
            "source eligibility never adds an unadmitted recipient to shared world facts");
        var frame = CampaignFactNetwork.Sent.Find(value => value.Topic == "BossRush.Personal.Campaign");
        check(frame != null && frame.Json.Contains("asset-A"), "actual production codec preserves frozen original asset receipt");
        BossRushCoop.IsHost = false; BossRushCoop.LocalAssetId = "asset-B";
        int sent = CampaignFactNetwork.Sent.Count;
        CampaignFactNetwork.Inject(frame, true, "host");
        check(CampaignProgressService.Notifications == 0 && CampaignFactNetwork.Sent.Count == sent, "real delivery rejects old A receipt while B is selected without ACK");
        BossRushCoop.LocalAssetId = "asset-A";
        Transition(owner, true);
        CampaignFactNetwork.Inject(frame, true, "host");
        check(CampaignProgressService.Notifications == 1 && !CampaignObjectiveTracker.IsArmed, "return A settles actual old scene receipt only against A original lease");
        var ack = CampaignFactNetwork.Sent.Find(value => value.Topic == "BossRush.Personal.Campaign.ack");
        check(ack != null, "successful original contract write produces actual delivery ACK");
        CampaignFactNetwork.Inject(frame, true, "host");
        check(CampaignProgressService.Notifications == 1, "actual delivery duplicate after scene transition cannot pay twice");
        BossRushCoop.RunId = string.Empty; owner.FactsTickForTest();
        BossRushCoop.RunId = "original-room";
        CampaignFactNetwork.Inject(frame, true, "host");
        check(CampaignProgressService.Notifications == 1, "empty reconnect handshake preserves actual delivery dedup and original lease");
        BossRushCoop.IsHost = true; CampaignFactNetwork.Inject(ack, false, "guest");
        check(!owner.FactsPendingForTest, "actual host ACK retires old fact after its original contract write");
        owner.FactsCleanupForTest();
        check(owner.FactLeaseCountForTest == 0, "runtime cleanup releases every retained original fact lease");
    }
}
