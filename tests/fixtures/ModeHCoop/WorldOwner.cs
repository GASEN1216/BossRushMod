using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace UnityEngine { struct Vector3 {} }
namespace BossRush
{
    enum ModeHExitReason { TechnicalAbort }
    enum Teams { wolf, scav }
    enum ManagedBossRole { Primary, Auxiliary }
    enum ManagedBossCleanupReason { SpawnRejected, OwnerInvalid }
    class Item { public void SetInt(string key, int value, bool save) {} }
    class ModBehaviour { public static void DevLog(string text) {} public static void CriticalLog(string text) {} }
    class SceneLoader { public static SceneLoader Instance = new SceneLoader(); public static bool IsSceneLoading; }
    static class TaskAdapter { public static void Forget(this Task task) {} }
    class ModeHTransitionRecord {}
    static class ModeHStateMachine
    {
        public static bool TryTransition(Run run, ModeHLifecycle expected, ModeHLifecycle next, long token,
            string reason, out ModeHTransitionRecord record, out string failure)
        {
            record = new ModeHTransitionRecord(); failure = null;
            if (run.Lifecycle != expected || run.OwnerToken != token) return false;
            run.Lifecycle = next; return true;
        }
    }
    class ManagedBossSpawnContext
    {
        public Func<bool> IsOwnerValid;
        public Func<CharacterMainControl, ManagedBossRole, bool> TryCommitAuxiliaryBeforeActivation;
        public Action<CharacterMainControl, ManagedBossRole> OnAuxiliaryReleased;
        public static ManagedBossSpawnContext CreateModeHPrimary(string key, Func<bool> valid)
        { return new ManagedBossSpawnContext { IsOwnerValid = valid }; }
    }
    class ManagedBossRuntimeHandle
    {
        public int Cleanups; public ManagedBossCleanupReason LastReason;
        public void CleanupOnce(ManagedBossCleanupReason reason) { if (Cleanups != 0) return; Cleanups++; LastReason = reason; }
    }
    class ManagedBossPrepareResult { public CharacterMainControl Character; public ManagedBossRuntimeHandle Handle; }
    class ModeHGroupEntry { public string Key; }
    class ModeHGroupUnit
    {
        public ModeHGroupEntry Entry; public bool IsEnemy; public CharacterMainControl Character;
        public Health Health; public ManagedBossRuntimeHandle ManagedHandle; public UnityEngine.Vector3 SpawnPosition;
        public object SuppressedPreset;
    }
    partial class ModeHGroupBattle
    {
        public bool Surrendered;
        public int Winner=WinnerBlue;
        public static TaskCompletionSource<ManagedBossPrepareResult> Completion;
        public static ManagedBossSpawnContext LastContext;
        public int Added;
        public static Task<ManagedBossPrepareResult> PrepareCustomAsync(ModBehaviour owner, string key,
            UnityEngine.Vector3 slot, ManagedBossSpawnContext context)
        { LastContext = context; return Completion.Task; }
        public void AddUnit(ModeHGroupUnit unit) { Added++; }
        public bool RegisterAuxiliary(CharacterMainControl child, Teams team, ModeHGroupUnit summoner) { Added++; return true; }
    }
    static class ModeHDeathSuppressionRegistry
    {
        public static void RegisterPreset(object preset) {} public static void RegisterCharacter(Health health, CharacterMainControl character) {}
    }
    static class ModeHWarehouseStakeJournal { public static object Active; }
    static class ModeHRealStakeService
    {
        public static int Returns; public static long Seed; public static int Match;
        public static bool TryAbortReturn(long seed, int match, out string failure)
        { Returns++; Seed = seed; Match = match; failure = null; return true; }
    }
    static class ModeHProfilePersistence
    {
        public static object Staged;
        public static bool StageWrite(object season, out string error) { Staged = season; error = null; return true; }
    }
    partial class ModeHRuntimeModule
    {
        ModBehaviour _owner = new ModBehaviour();
        ModeHGroupBattle _groupBattle;
        int _resumeSceneIntentGeneration, _restoredSlotGeneration;
        readonly List<object> _preparedStats = new List<object>(), _preparedOutfits = new List<object>();
        public int ExitCalls, TransitionCalls, ResumeLoads;
        public string RecoveryFailure;
        public string ExitReason;
        public object PersonalLedger { get { return _coopLedger; } }
        public object ResultsOwner { get { return _coopResults; } }
        public object Season { get { return _season; } }
        public bool Active { get { return HasActiveRun; } }
        public bool Authority { get { return IsCoopWorldAuthorityCurrent; } }
        public bool Callback { get { return IsCallbackStillValid(1, 0); } }
        public bool TryAdvance() { return TryTransition(ModeHLifecycle.Drafting, ModeHLifecycle.MatchFighting, "fixture"); }
        bool _allowBriefToLoadout;
        // Auto driver/UI boundary: exercise the production first transition without starting Unity combat.
        void RunAutoAdvance(string reason, Action first) { if (first != null) first(); }
        void StartMatchFromBrief() { throw new Exception("unexpected non-drafting setup"); }
        public void StartProjectedUi()
        {
            _groupRoster.MatchIndex = GroupMatchIndex; _groupRoster.RunId = _runState.RunId;
            _groupRoster.Allies.Add(1); _groupRoster.Enemies.Add(2);
            ActualStartGroupMatch();
        }
        public ModeHLifecycle Phase { get { return _runState.Lifecycle; } }

        public void PublishResult() { PublishCoopResult(new ModeHGroupBattle()); }
        public bool ResumeCurrent { get { return IsSeasonResumeRequestCurrent(1, ModeHRuntimeGates.SlotGeneration, 77); } }
        public void SetResumePending() { _resumeScenePending = true; _resumeSceneIntentGeneration = 77; }
        public void DirtySeason() { _seasonDirty = true; }
        public void ExplicitResume() { BindCoopWorldAsset(); }
        public void BeginResume() { SetMap(); BeginSeasonResumeScene(); }
        public bool ResumePending { get { return _resumeScenePending; } }
        void HideRecoveryShell() {}
        void PresentRecoveryFailure(string failure) { RecoveryFailure = failure; }
        Task LoadSeasonResumeScene(long owner, int slot, int intent, string scene)
        { ResumeLoads++; return Task.CompletedTask; }
        void OnTransitionApplied(ModeHTransitionRecord record) { TransitionCalls++; }
        partial void ShutdownRuntimeInternal(ModeHExitReason reason, string reasonId);
        // Host shell boundary: the full production shutdown body and original stake-return body execute below.
        void ShutdownRuntime(ModeHExitReason reason, string reasonId)
        {
            if (_shutdownCompleted) return;
            ExitCalls++; ExitReason = reasonId; _shutdownCompleted = true; CancelSeasonResume();
            ShutdownRuntimeInternal(reason, reasonId);
        }
        static void ScaleGroupCustomHealth(CharacterMainControl character, string key) {}
        public Task Prepare(out Func<bool> done)
        {
            _groupBattle = new ModeHGroupBattle(); var ticket = new GroupCustomTicket();
            done = () => ticket.Done;
            return PrepareGroupCustomUnitAsync(_groupBattle, new ModeHGroupEntry { Key = "managed_dragon_king" },
                true, new UnityEngine.Vector3(), 1, 0, ticket);
        }
        public int Units { get { return _groupBattle.Added; } }
    }
    static class WorldOwnerRegression
    {
        static void NativeHost(string room = "original-room")
        { BossRushCoop.IsConnected = true; BossRushCoop.IsHost = true; BossRushCoop.IsClient = false; BossRushCoop.RunId = room; BossRushCoop.SessionReady = true; }
        static void Stop()
        { BossRushCoop.IsConnected = false; BossRushCoop.IsHost = false; BossRushCoop.IsClient = false; BossRushCoop.RunId = ""; }
        static ManagedBossPrepareResult Prepared()
        { return new ManagedBossPrepareResult { Character = new CharacterMainControl { Health = new Health(), CharacterItem = new Item() }, Handle = new ManagedBossRuntimeHandle() }; }
        public static void Run(Action<bool, string> check)
        {
            NativeHost(); BossRushCoop.IsHost = false; BossRushCoop.IsClient = true;
            var projection = new ModeHRuntimeModule(); projection.BeginProjection();
            bool projectionAuthority = projection.Authority, projectionCallback = projection.Callback;
            Stop(); projection.StartProjectedUi();
            check(projection.Phase == ModeHLifecycle.Drafting && projection.TransitionCalls == 0,
                "H actual queued StartGroupMatch cannot promote guest projection after Stop before cleanup Tick");
            check(!projectionAuthority && !projectionCallback, "H native guest projection never owns world callbacks");
            projection.Tick();
            check(!projection.Projecting && projection.Released == 1, "H stopped projection still releases through original cleanup");
            NativeHost(); var live = new ModeHRuntimeModule(); live.FreezeAsset();
            check(live.Authority && live.Callback, "H original host callback is valid");
            BossRushCoop.SessionReady = false; BossRushCoop.Players[1].IsConnected = false; live.Tick();
            check(live.ExitCalls == 0 && live.Callback, "H one native peer disconnect retains paid original host world");
            BossRushCoop.RunId = ""; live.Tick();
            check(live.ExitCalls == 0 && live.Callback, "H empty welcome while still native host is not a world loss");
            BossRushCoop.Players[1].IsConnected = true;
            NativeHost(); live.Pending(true); var ledger = live.PersonalLedger; var delivery = live.ResultsOwner;
            var originalSeason = live.Season; live.DirtySeason();
            ModeHWarehouseStakeJournal.Active = new object(); ModeHRealStakeService.Returns = 0;
            var paid = new ModeHCashBetRecord { runId = "season", matchIndex = 1, status = 1, amount = 90 };
            ModeHCashBetService.Current = paid; int payments = ModeHCashBetService.Payments;
            Stop();
            check(!live.Callback && !live.Authority && !live.TryAdvance() && live.TransitionCalls == 0,
                "H actual StopNetwork rejects callback and settlement transition before next Tick");
            live.Tick(); live.Tick();
            check(live.ExitCalls == 1 && live.ExitReason == "coop_world_owner_changed" && !live.Active,
                "H real Tick closes old host world exactly once");
            check(ReferenceEquals(ledger, live.PersonalLedger) && ReferenceEquals(delivery, live.ResultsOwner),
                "H host abort preserves original personal ledger and delivery owner");
            check(ReferenceEquals(paid, ModeHCashBetService.Current) && paid.status == 1 && ModeHCashBetService.Payments == payments,
                "H transport loss never forfeits or repays the reserved cash bet");
            check(ReferenceEquals(originalSeason, ModeHProfilePersistence.Staged) && ModeHRealStakeService.Returns == 1,
                "H actual shutdown stages original season and returns warehouse escrow through existing pipeline");
            ModeHWarehouseStakeJournal.Active = null;
            NativeHost(); var changed = new ModeHRuntimeModule(); changed.FreezeAsset(); BossRushCoop.RunId = "new-room";
            changed.Command("p0", "ready"); changed.Command("p0", "funded");
            check(!changed.Callback && changed.Funds == 0, "H new room cannot fund the old season before its cleanup tick");
            changed.Tick(); check(changed.ExitCalls == 1, "H new native room cancels old original host world");
            NativeHost(); var publisher = new ModeHRuntimeModule(); publisher.FreezeAsset(); publisher.Command("p0", "ready"); publisher.Command("p0", "funded");
            BossRushCoop.RunId = "publish-other-room"; publisher.PublishResult();
            check(publisher.StartReceipts == 0, "H old world cannot publish settlement after native room changed");
            NativeHost(); var role = new ModeHRuntimeModule(); role.FreezeAsset(); BossRushCoop.IsHost = false; BossRushCoop.IsClient = true;
            check(!role.Callback, "H host-to-client role change rejects old world continuation"); role.Tick(); check(role.ExitCalls == 1, "H role change closes original world");
            Stop(); var solo = new ModeHRuntimeModule(); solo.FreezeAsset(); check(solo.Callback, "H actual offline original season still runs");
            NativeHost(); check(!solo.Callback, "H offline old season cannot continue in a newly connected room");
            solo.ExplicitResume(); check(solo.Callback, "H explicit resume can bind the current native room without a new entry charge");
            solo.SetResumePending(); BossRushMapSelectionHelper.Pending = true; BossRushMapSelectionHelper.Generation = 77;
            check(solo.ResumeCurrent, "H original resume request retains its identity before await");
            BossRushCoop.RunId = "third-room"; check(!solo.ResumeCurrent, "H actual resume await rejects later room change");
            BossRushMapSelectionHelper.Pending = false;
            NativeHost(); var resume = new ModeHRuntimeModule(); resume.FreezeAsset();
            BossRushCoop.RunId = "resume-new-room"; resume.BeginResume();
            check(resume.ResumeLoads == 1 && resume.ResumePending && resume.Callback && resume.ResumeCurrent,
                "H actual explicit resume freezes new room before dispatching the scene await");
            Stop(); resume.Tick();
            check(resume.ExitCalls == 1 && !resume.ResumePending && !BossRushMapSelectionHelper.Pending,
                "H stopped active resume cancels pending scene identity instead of leaving an unclickable recovery shell");
            NativeHost(); BossRushCoop.IsHost = false; BossRushCoop.IsClient = true;
            var clientResume = new ModeHRuntimeModule(); clientResume.BeginResume();
            check(clientResume.ResumeLoads == 0 && !clientResume.ResumePending && clientResume.RecoveryFailure == "season_resume_host_required",
                "H native guest cannot resume a second world from a local old season");
            foreach (string change in new[] { "none", "stop", "room", "role" })
            {
                NativeHost(); var pending = new ModeHRuntimeModule(); pending.FreezeAsset();
                ModeHGroupBattle.Completion = new TaskCompletionSource<ManagedBossPrepareResult>();
                Func<bool> done; Task task = pending.Prepare(out done);
                check(!task.IsCompleted && !done() && ModeHGroupBattle.LastContext.IsOwnerValid(), "H real factory waits with frozen original owner: " + change);
                if (change == "stop") Stop();
                if (change == "room") BossRushCoop.RunId = "factory-other-room";
                if (change == "role") { BossRushCoop.IsHost = false; BossRushCoop.IsClient = true; }
                var result = Prepared(); ModeHGroupBattle.Completion.SetResult(result); task.GetAwaiter().GetResult();
                check(done(), "H real async ticket always completes: " + change);
                check(change == "none" ? pending.Units == 1 && result.Handle.Cleanups == 0 : pending.Units == 0 && result.Handle.Cleanups == 1 && result.Handle.LastReason == ManagedBossCleanupReason.OwnerInvalid,
                    "H awaited managed factory commits only original world, reclaims late handle: " + change);
            }
            NativeHost();
        }
    }
}
