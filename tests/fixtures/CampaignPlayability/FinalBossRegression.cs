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
}

namespace BossRush
{
    internal static partial class CampaignDialoguePlayer
    {
        private static int _playbackGeneration;
        private static CancellationTokenSource _playbackCancellation;
        public static CancellationToken ObservedToken;
        public static Task PlayFinalBossPrologueAsync()
        {
            ObservedToken = PlaybackToken();
            return Task.Delay(Timeout.Infinite, ObservedToken);
        }
    }
    public enum PhantomWitchDeathPresentation { Standard, CampaignFinal }
    public static class BossBgmKeys { public const string PhantomWitch = "witch"; }
    public static class BossBgmEvents { public const string RunVictory = "victory"; }
    public class BossRushAudioManager
    {
        public static BossRushAudioManager Instance;
        public void StopBossBGM(string key, CharacterMainControl owner) { }
        public void PlayStinger(string key) { }
    }
    public partial class ModBehaviour
    {
        private CharacterMainControl campaignFinalBossInstance;
        private int campaignFinalBossRunId, campaignFinalBossDeathPresentationCount;
        private bool campaignFinalBossSpawnResolved;
        private UnityEngine.GameObject campaignFinalBossAltar;
        private float campaignAltarRetryAt;
        public bool Arena, ModeBusy, NonWaveRequested;
        public int ClearedLoot;
        public TaskCompletionSource<CharacterMainControl> SpawnResult;
        public bool FinalActive { get { return campaignFinalBossActive; } }
        private bool IsCampaignArenaSceneCached() { return Arena; }
        private bool IsAnyGameplayModeActiveForCampaign() { return ModeBusy; }
        private UnityEngine.Vector3 ResolveCampaignFinalBossSpawnPosition() { return new UnityEngine.Vector3(); }
        private void ApplyCampaignFinalBossVariant(CharacterMainControl boss) { }
        private void ClearBossRandomLootTracking(CharacterMainControl boss) { ClearedLoot++; }
        private Task<CharacterMainControl> SpawnPhantomWitch(UnityEngine.Vector3 position, bool notify,
            bool defer, PhantomWitchDeathPresentation presentation, float scale, bool isNonWaveSpawn = false)
        {
            NonWaveRequested = isNonWaveSpawn;
            return SpawnResult.Task;
        }
        public Task BeginSpawn()
        {
            campaignFinalBossActive = true;
            return StartCampaignFinalBossAsync(++campaignFinalBossRunId);
        }
        public Task BeginPrologue()
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
        CharacterMainControl.Main.Health.IsDead = true;
        check(!owner.CanStartCampaignFinalBoss(), "dead player cannot summon");
        CharacterMainControl.Main.Health.IsDead = false;

        Task prologue = owner.BeginPrologue();
        CancellationToken oldToken = CampaignDialoguePlayer.ObservedToken;
        check(!prologue.IsCompleted, "prologue waits for player dialogue");
        owner.CleanupCampaignFinalBoss(true);
        check(oldToken.IsCancellationRequested, "aborted showdown cancels dialogue wait");
        check(prologue.Wait(2000) && !owner.NonWaveRequested, "cancelled prologue never starts factory");
        Task nextPrologue = owner.BeginPrologue();
        check(!CampaignDialoguePlayer.ObservedToken.IsCancellationRequested && !nextPrologue.IsCompleted,
            "successor dialogue receives a fresh cancellation token");
        owner.CleanupCampaignFinalBoss(true);
        check(nextPrologue.Wait(2000), "successor cancellation finishes without leaking wait");

        var first = owner.SpawnResult = new TaskCompletionSource<CharacterMainControl>();
        Task firstRun = owner.BeginSpawn();
        check(owner.NonWaveRequested && !firstRun.IsCompleted, "final boss uses non-wave spawn while awaiting factory");
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
        owner.CleanupCampaignFinalBoss(false);
        check(!owner.FinalActive && owner.CanStartCampaignFinalBoss(), "scene destruction permits another challenge");

        CampaignObjectiveTracker.ResetSession();
        CampaignProgressService.Notifications = 0;
        CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeFinal);
        successor = owner.SpawnResult = new TaskCompletionSource<CharacterMainControl>();
        successorRun = owner.BeginSpawn();
        live = Boss(); successor.SetResult(live); successorRun.GetAwaiter().GetResult();
        live.Health.OnDeadEvent.Invoke(); live.Health.OnDeadEvent.Invoke();
        check(CampaignProgressService.Notifications == 1 && !CampaignObjectiveTracker.IsArmed
            && !owner.FinalActive && live != null && live.Health.OnDeadEvent.Count == 0,
            "victory completes once, releases tracking, and leaves corpse to normal loot");
    }
}
