using System;
using System.Threading;
using System.Threading.Tasks;
using BossRush;

// Unity objects are adapters. Destroy invalidates the object and all owned components.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool left = ReferenceEquals(a, null) || a.Destroyed;
            bool right = ReferenceEquals(b, null) || b.Destroyed;
            return left || right ? left == right : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            obj.Destroyed = true;
            var go = obj as GameObject;
            if (!ReferenceEquals(go, null) && !ReferenceEquals(go.Character, null))
            {
                go.Character.Destroyed = true;
                if (!ReferenceEquals(go.Character.Health, null)) go.Character.Health.Destroyed = true;
            }
        }
    }
    public class GameObject : Object { public CharacterMainControl Character; }
    public struct Vector3 { }
}

public class DeathEvent
{
    private Action<DamageInfo> handlers;
    public int Count { get { return handlers == null ? 0 : handlers.GetInvocationList().Length; } }
    public void AddListener(Action<DamageInfo> handler) { handlers += handler; }
    public void RemoveListener(Action<DamageInfo> handler) { handlers -= handler; }
    public void Invoke() { if (handlers != null) handlers(new DamageInfo()); }
    public Action<DamageInfo> Snapshot() { return handlers; }
}

namespace BossRush
{
    internal static partial class CampaignDialoguePlayer
    {
        private static int _playbackGeneration;
        private static CancellationTokenSource _playbackCancellation;
        public static CancellationToken ObservedToken;
        internal static void ResetStaticCaches() { InvalidatePlayback(); }
        public static Task PlayFinalBossPrologueAsync()
        {
            ObservedToken = PlaybackToken();
            return Task.Delay(Timeout.Infinite, ObservedToken);
        }
    }
    public enum PhantomWitchDeathPresentation { Standard, CampaignFinal }
    // Presentation-only summon burst (VA-25). Counts calls so the schedule can be asserted without rendering.
    internal static class CampaignFinalBossFx
    {
        public static int SummonBursts;
        internal static void PlaySummonBurst(UnityEngine.Vector3 position) { SummonBursts++; }
    }
    public static class BossBgmKeys { public const string PhantomWitch = "witch"; }
    public static class BossBgmEvents { public const string RunVictory = "victory"; }
    public class BossRushAudioManager
    {
        public static BossRushAudioManager Instance;
        public void StopBossBGM(string key, CharacterMainControl owner) { }
        public void PlayStinger(string key) { }
    }
    internal static class TaskExtensions
    {
        internal static void Forget(this Task task) { }
    }
    public partial class ModBehaviour
    {
        public static bool DevModeEnabled = true;
        public bool Arena, NonWaveRequested;
        public bool ModeBusy { get { return modeGActive; } set { modeGActive = value; } }
        public int ClearedLoot;
        public TaskCompletionSource<CharacterMainControl> SpawnResult;
        public bool FinalActive { get { return IsCampaignFinalBossActive; } }
        public int ArenaQueryCount;
        public bool IsCurrentSceneValidBossRushArena() { ArenaQueryCount++; return Arena; }
        private void ClearBossRandomLootTracking(CharacterMainControl boss) { ClearedLoot++; }
        private void ApplyBossStatMultiplier(CharacterMainControl boss, float multiplier) { }
        internal Task<CharacterMainControl> SpawnPhantomWitch(UnityEngine.Vector3 position, bool notify,
            bool defer, PhantomWitchDeathPresentation presentation, float scale, bool isNonWaveSpawn = false)
        {
            NonWaveRequested = isNonWaveSpawn;
            return SpawnResult.Task;
        }
        public Task BeginSpawn() { return campaignRuntime.BeginSpawn(); }
        public Task BeginPrologue() { return campaignRuntime.BeginPrologue(); }
    }
    internal sealed class CampaignOfficialQuestClient { internal void ClearPending() { } internal void UnregisterAll() { } }
    internal static class CampaignSaveCoordinator
    {
        internal static void TryFlushOnHostDestroy() { }
        internal static void ShutdownSubscription() { }
        internal static void ResetStaticCaches() { }
    }
    internal static class CampaignHud { internal static void ResetStaticCaches() { } }
    internal sealed partial class CampaignRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour _owner;
        private bool _bootstrapped;
        private int _sceneGeneration;
        private CampaignOfficialQuestClient _questClient;
        internal int SceneGeneration { get { return _sceneGeneration; } }
        internal bool IsEnabled { get { return _owner != null && _owner.IsCampaignConfiguredEnabled(); } }
        private void ShutdownIfEnabledTurnedOff() { }
        private void EnsureBootstrapped() { }
        private static void LogFailure(string stage, Exception error) { throw new Exception(stage, error); }
        internal CampaignRuntimeModule(ModBehaviour owner) { _owner = owner; }
        internal void TickCampaignFinalBossAltar() { }
        private UnityEngine.Vector3 ResolveCampaignFinalBossSpawnPosition() { return new UnityEngine.Vector3(); }
        private void ApplyCampaignFinalBossVariant(CharacterMainControl boss) { }
        internal Task BeginSpawn()
        {
            campaignFinalBossActive = true;
            return StartCampaignFinalBossAsync(++campaignFinalBossRunId);
        }
        internal Task BeginPrologue()
        {
            campaignFinalBossActive = true;
            return StartCampaignFinalBossPrologueThenSpawnAsync(++campaignFinalBossRunId);
        }
    }
}

internal static class FinalBossRegression
{
    private static CharacterMainControl Boss()
    {
        var value = new CharacterMainControl { Health = new Health { OnDeadEvent = new DeathEvent() } };
        value.gameObject = new UnityEngine.GameObject { Character = value };
        return value;
    }

    internal static void Run(Action<bool, string> check)
    {
        CharacterMainControl.Main = new CharacterMainControl { Health = new Health() };
        CampaignProgressService.Active = "ch6";
        CampaignProgressService.State = CampaignChapterState.ReadyToDeliver;
        var owner = new ModBehaviour { Arena = true };
        ModBehaviour.Instance = owner;
        check(!owner.CanStartCampaignFinalBoss(), "ready chapter rejects final summon through actual interaction gate");
        CampaignProgressService.State = CampaignChapterState.ContractActive;
        check(owner.CanStartCampaignFinalBoss(), "active final contract can summon");
        int sceneQueries = owner.ArenaQueryCount;
        check(owner.CanStartCampaignFinalBoss() && owner.ArenaQueryCount == sceneQueries,
            "same module scene generation reuses arena query");
        owner.CampaignRuntime.OnSceneLoaded(new SceneRuntimeContext());
        check(owner.CampaignRuntime.SceneGeneration == 1 && owner.CanStartCampaignFinalBoss()
            && owner.ArenaQueryCount == sceneQueries + 1, "scene dispatch invalidates the module arena cache once");
        CharacterMainControl.Main.Health.IsDead = true;
        check(!owner.CanStartCampaignFinalBoss(), "dead player cannot summon");
        CharacterMainControl.Main.Health.IsDead = false;

        owner.StartCampaignFinalBoss();
        check(owner.IsCampaignFinalBossActive && !owner.NonWaveRequested,
            "original host start bridge arms the module before the prologue wait");
        owner.CleanupCampaignFinalBoss(true);
        check(!owner.IsCampaignFinalBossActive && CampaignDialoguePlayer.ObservedToken.IsCancellationRequested,
            "original cleanup bridge cancels the same module run");

        Task prologue = owner.BeginPrologue();
        CancellationToken oldToken = CampaignDialoguePlayer.ObservedToken;
        check(!prologue.IsCompleted, "prologue waits for player dialogue");
        owner.CleanupCampaignFinalBoss(true);
        check(oldToken.IsCancellationRequested, "aborted showdown cancels dialogue wait");
        check(prologue.Wait(2000) && !owner.NonWaveRequested, "cancelled prologue never starts factory");
        check(CampaignFinalBossFx.SummonBursts == 0, "cancelled prologue never plays the summon burst");
        Task nextPrologue = owner.BeginPrologue();
        check(!CampaignDialoguePlayer.ObservedToken.IsCancellationRequested && !nextPrologue.IsCompleted,
            "successor dialogue receives a fresh cancellation token");
        owner.CleanupCampaignFinalBoss(true);
        check(nextPrologue.Wait(2000), "successor cancellation finishes without leaking wait");

        var first = owner.SpawnResult = new TaskCompletionSource<CharacterMainControl>();
        Task firstRun = owner.BeginSpawn();
        check(owner.NonWaveRequested && !firstRun.IsCompleted, "final boss uses non-wave spawn while awaiting factory");
        check(CampaignFinalBossFx.SummonBursts == 1, "summon burst plays once alongside the pending factory without delaying it");
        owner.CleanupCampaignFinalBoss(true);
        var late = Boss(); first.SetResult(late); firstRun.GetAwaiter().GetResult();
        check(late == null && !owner.FinalActive && owner.ClearedLoot == 1,
            "late spawn clears loot subscription and destroys owned character");

        first = owner.SpawnResult = new TaskCompletionSource<CharacterMainControl>();
        firstRun = owner.BeginSpawn();
        owner.CleanupCampaignFinalBoss(true);
        var successor = owner.SpawnResult = new TaskCompletionSource<CharacterMainControl>();
        Task successorRun = owner.BeginSpawn();
        first.SetException(new InvalidOperationException("old request failed"));
        firstRun.GetAwaiter().GetResult();
        check(owner.FinalActive, "old spawn exception cannot cancel successor");
        var live = Boss(); successor.SetResult(live); successorRun.GetAwaiter().GetResult();
        check(live.Health.OnDeadEvent.Count == 1, "successful spawn owns one named death listener");
        owner.ModeBusy = true;
        owner.TickCampaignFinalBossYield();
        check(live == null && live.Health.OnDeadEvent.Count == 0 && !owner.FinalActive,
            "starting another mode releases listener and destroys final boss");

        owner.ModeBusy = false;
        successor = owner.SpawnResult = new TaskCompletionSource<CharacterMainControl>();
        successorRun = owner.BeginSpawn();
        live = Boss(); successor.SetResult(live); successorRun.GetAwaiter().GetResult();
        // Model the mandatory Unity scene teardown, then the host's existing scene cleanup.
        UnityEngine.Object.Destroy(live.gameObject);
        owner.CampaignRuntime.OnSceneLoaded(new SceneRuntimeContext());
        check(!owner.FinalActive && owner.CanStartCampaignFinalBoss(), "scene destruction permits another challenge");

        var isolatedOwner = new ModBehaviour { Arena = true };
        isolatedOwner.StartCampaignFinalBoss();
        check(isolatedOwner.IsCampaignFinalBossActive && !owner.IsCampaignFinalBossActive,
            "separate hosts do not share final boss ownership state");
        isolatedOwner.CleanupCampaignFinalBoss(true);

        CampaignObjectiveTracker.ResetSession();
        CampaignProgressService.Notifications = 0;
        CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeFinal);
        successor = owner.SpawnResult = new TaskCompletionSource<CharacterMainControl>();
        successorRun = owner.BeginSpawn();
        live = Boss(); successor.SetResult(live); successorRun.GetAwaiter().GetResult();
        Action<DamageInfo> capturedDeath = live.Health.OnDeadEvent.Snapshot();
        capturedDeath(new DamageInfo()); capturedDeath(new DamageInfo());
        check(CampaignProgressService.Notifications == 1 && !CampaignObjectiveTracker.IsArmed
            && !owner.FinalActive && live != null && live.Health.OnDeadEvent.Count == 0
            && owner.CampaignFinalBossDeathPresentationCount == 1,
            "victory completes once, releases tracking, and leaves corpse to normal loot");

        var destroyedOwner = new ModBehaviour { Arena = true };
        var pendingAtDestroy = destroyedOwner.SpawnResult = new TaskCompletionSource<CharacterMainControl>();
        Task destroyRun = destroyedOwner.BeginSpawn();
        destroyedOwner.CampaignRuntime.OnDestroy();
        UnityEngine.Object.Destroy(destroyedOwner);
        var destroyLate = Boss(); pendingAtDestroy.SetResult(destroyLate); destroyRun.GetAwaiter().GetResult();
        check(destroyedOwner == null && destroyLate == null && destroyedOwner.ClearedLoot == 1
            && !destroyedOwner.IsCampaignFinalBossActive,
            "module destruction keeps original host for late spawn loot cleanup and destroy");
    }
}
