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
            if (!ReferenceEquals(go, null))
            {
                Destroy(go.transform);
                foreach (object component in go.Components) Destroy(component as Object);
            }
        }
    }
    public class GameObject : Object
    {
        public CharacterMainControl Character;
        public readonly Transform transform = new Transform();
        public readonly System.Collections.Generic.List<object> Components = new System.Collections.Generic.List<object>();
        public GameObject(string name = null) { }
        public T AddComponent<T>() where T : new() { var value = new T(); Components.Add(value); return value; }
    }
    public class Transform : Object { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public class BoxCollider : Object { public bool isTrigger; public Vector3 size, center; }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; }
    public static class SceneManager
    {
        public static int ActiveHandle;
        public static Scene GetActiveScene() { return new Scene { handle = ActiveHandle }; }
    }
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

public static class SceneLoader { public static bool IsSceneLoading; }
public static class LevelManager { public static bool AfterInit; }

namespace BossRush
{
    internal static partial class CampaignDialoguePlayer
    {
        private static int _playbackGeneration;
        private static CancellationTokenSource _playbackCancellation;
        public static CancellationToken ObservedToken;
        public static TaskCompletionSource<bool> IndependentPrologue;
        internal static void ResetStaticCaches() { InvalidatePlayback(); }
        public static Task PlayFinalBossPrologueAsync()
        {
            ObservedToken = PlaybackToken();
            if (IndependentPrologue != null) return IndependentPrologue.Task;
            return Task.Delay(Timeout.Infinite, ObservedToken);
        }
    }
    public enum PhantomWitchDeathPresentation { Standard, CampaignFinal }
    // Presentation-only summon burst (VA-25). Counts calls so the schedule can be asserted without rendering.
    internal static class CampaignFinalBossFx
    {
        public static int SummonBursts, AltarBuilds, AltarDismisses;
        internal static void Build(UnityEngine.GameObject altar) { AltarBuilds++; }
        internal static void Dismiss(UnityEngine.GameObject altar) { AltarDismisses++; UnityEngine.Object.Destroy(altar); }
        internal static void PlaySummonBurst(UnityEngine.Vector3 position) { SummonBursts++; }
    }
    internal sealed class CampaignFinalBossInteractable : UnityEngine.Object { }
    internal static class SpawnPositionHelper
    {
        internal static bool GroundAvailable = true;
        internal static int GeometryQueries;
        internal static bool TryFindAroundPlayer(UnityEngine.Vector3 player, int count, float radius,
            out UnityEngine.Vector3 position, float lift, float minDistance, float sample)
        { GeometryQueries++; position = new UnityEngine.Vector3(player.x + radius, player.y, player.z); return GroundAvailable; }
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
        internal UnityEngine.GameObject AltarForTest { get { return campaignFinalBossAltar; } }
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
        CampaignProgressService.State = CampaignChapterState.ContractActive;
        LevelManager.AfterInit = true;
        var delayedArena = new ModBehaviour { Arena = false };
        UnityEngine.SceneManagement.SceneManager.ActiveHandle = 20;
        check(!delayedArena.CanStartCampaignFinalBoss(), "loading scene is not an arena yet");
        delayedArena.Arena = true;
        UnityEngine.SceneManagement.SceneManager.ActiveHandle = 21;
        check(delayedArena.CanStartCampaignFinalBoss(),
            "late active-scene change must release the final altar without another scene-loaded callback");
        delayedArena.Arena = false;
        UnityEngine.SceneManagement.SceneManager.ActiveHandle = 22;
        check(!delayedArena.CanStartCampaignFinalBoss(),
            "changing the active scene also invalidates a cached positive arena result");

        // A map registry may also become ready after its scene, with an unchanged scene handle.
        var delayedRegistry = new ModBehaviour { Arena = false };
        check(!delayedRegistry.CanStartCampaignFinalBoss(), "unregistered scene initially rejects final altar");
        int negativeSceneQueries = delayedRegistry.ArenaQueryCount;
        check(!delayedRegistry.CanStartCampaignFinalBoss() && delayedRegistry.ArenaQueryCount == negativeSceneQueries,
            "a negative arena result does not repeat the scene-name query inside its one-second retry window");
        delayedRegistry.Arena = true;
        UnityEngine.Time.unscaledTime += 2f;
        check(delayedRegistry.CanStartCampaignFinalBoss(), "negative arena cache retries after map registry becomes ready");
        SceneLoader.IsSceneLoading = true;
        LevelManager.AfterInit = false;
        delayedRegistry.CampaignRuntime.TickCampaignFinalBossAltar();
        check(delayedRegistry.CampaignRuntime.AltarForTest == null, "loading with a live player must not create the altar at the old player position");
        SceneLoader.IsSceneLoading = false;
        delayedRegistry.CampaignRuntime.TickCampaignFinalBossAltar();
        check(delayedRegistry.CampaignRuntime.AltarForTest == null, "initialization tail still waits for the player's final placement");
        CharacterMainControl.Main.transform.position = new UnityEngine.Vector3(100f, 0f, 200f);
        LevelManager.AfterInit = true;
        SpawnPositionHelper.GroundAvailable = false;
        delayedRegistry.CampaignRuntime.TickCampaignFinalBossAltar();
        check(delayedRegistry.CampaignRuntime.AltarForTest == null, "missing ground does not create an unreachable altar");
        SpawnPositionHelper.GroundAvailable = true;
        UnityEngine.Time.unscaledTime += 2f;
        delayedRegistry.CampaignRuntime.TickCampaignFinalBossAltar();
        UnityEngine.GameObject createdAltar = delayedRegistry.CampaignRuntime.AltarForTest;
        check(createdAltar != null && createdAltar.Components.Count == 2
            && createdAltar.transform.position.x == 103f && createdAltar.transform.position.z == 200f
            && createdAltar.Components[0] is UnityEngine.BoxCollider
            && createdAltar.Components[1] is CampaignFinalBossInteractable,
            "real altar tick creates its collider and player interaction after ground becomes available");
        int geometryQueries = SpawnPositionHelper.GeometryQueries;
        delayedRegistry.CampaignRuntime.TickCampaignFinalBossAltar();
        check(ReferenceEquals(createdAltar, delayedRegistry.CampaignRuntime.AltarForTest)
            && SpawnPositionHelper.GeometryQueries == geometryQueries, "existing altar is a singleton with no repeat geometry query");
        CampaignProgressService.State = CampaignChapterState.ReadyToDeliver;
        delayedRegistry.CampaignRuntime.TickCampaignFinalBossAltar();
        check(createdAltar == null && delayedRegistry.CampaignRuntime.AltarForTest == null,
            "ready-to-deliver chapter dismisses the actual altar and prevents a second showdown");

        CampaignProgressService.State = CampaignChapterState.ReadyToDeliver;
        var owner = new ModBehaviour { Arena = true, bossRushArenaActive = true };
        ModBehaviour.Instance = owner;
        check(!owner.CanStartCampaignFinalBoss(), "ready chapter rejects final summon through actual interaction gate");
        CampaignProgressService.State = CampaignChapterState.ContractActive;
        check(owner.CanStartCampaignFinalBoss(), "active final contract can summon after entering the arena before starting waves");
        owner.IsActive = true;
        check(!owner.CanStartCampaignFinalBoss(), "started arena waves still prevent a simultaneous final showdown");
        owner.IsActive = false;
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

        var independent = CampaignDialoguePlayer.IndependentPrologue = new TaskCompletionSource<bool>();
        Task independentRun = owner.BeginPrologue();
        independent.SetCanceled();
        check(independentRun.Wait(2000) && !owner.FinalActive && owner.CanStartCampaignFinalBoss(),
            "official dialogue cancellation without campaign cleanup must release the final stone for retry");
        independent = CampaignDialoguePlayer.IndependentPrologue = new TaskCompletionSource<bool>();
        independentRun = owner.BeginPrologue();
        owner.CleanupCampaignFinalBoss(true);
        var independentSuccessor = CampaignDialoguePlayer.IndependentPrologue = new TaskCompletionSource<bool>();
        Task independentSuccessorRun = owner.BeginPrologue();
        independent.SetCanceled();
        check(independentRun.Wait(2000) && owner.FinalActive && !independentSuccessorRun.IsCompleted,
            "late cancellation from an old official dialogue cannot clean up the successor showdown");
        independentSuccessor.SetCanceled();
        check(independentSuccessorRun.Wait(2000) && !owner.FinalActive,
            "successor independent cancellation also releases its own showdown");
        CampaignDialoguePlayer.IndependentPrologue = null;

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
