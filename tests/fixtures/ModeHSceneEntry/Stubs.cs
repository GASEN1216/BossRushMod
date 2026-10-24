using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a,b) || (ReferenceEquals(a,null) || a.Destroyed) && (ReferenceEquals(b,null) || b.Destroyed); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object o) { if (!ReferenceEquals(o,null)) o.Destroyed = true; }
        public static T[] FindObjectsOfType<T>(bool inactive) { return new T[0]; }
    }
    public class Coroutine { public IEnumerator Body; public Stack<IEnumerator> Nested=new Stack<IEnumerator>(); public bool Stopped; }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 zero => new Vector3(); public static Vector3 up => new Vector3(0,1,0); public static Vector3 down => new Vector3(0,-1,0);
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a,float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
    }
    public class Transform { public Vector3 position; }
    public class WaitForSeconds { public WaitForSeconds(float seconds) { } }
    public abstract class CustomYieldInstruction : IEnumerator
    { public object Current { get { return null; } } public virtual bool MoveNext() { return false; } public void Reset() { } }
    public static class Time { public static float unscaledDeltaTime=.1f; }
    public static class Mathf { public static float Max(float a,float b) { return Math.Max(a,b); } public static float Abs(float a) { return Math.Abs(a); } }
    public struct RaycastHit { public Vector3 point; }
    public static class Physics
    {
        public static RaycastHit[] RaycastAll(Vector3 a,Vector3 b,float distance) { return new RaycastHit[0]; }
        public static bool Raycast(Vector3 a,Vector3 b,out RaycastHit hit,float distance) { hit=new RaycastHit();return false; }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name; public int handle; public bool isLoaded;
        public bool IsValid() { return handle!=0; }
        public static bool operator ==(Scene a, Scene b) { return a.handle==b.handle; }
        public static bool operator !=(Scene a, Scene b) { return a.handle!=b.handle; }
        public override bool Equals(object value) { return value is Scene && this==(Scene)value; }
        public override int GetHashCode() { return handle; }
    }
    public static class SceneManager
    {
        public static Scene Active; public static event Action<Scene> sceneUnloaded;
        public static Scene GetActiveScene() { return Active; }
        public static void Unload(Scene scene) { if(sceneUnloaded!=null)sceneUnloaded(scene); }
        public static void ResetEvents() { sceneUnloaded=null; }
    }
}
public class Health { public bool IsDead; }
public class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main; public Health Health=new Health(); public UnityEngine.Transform transform=new UnityEngine.Transform();
    public int PositionWrites; public void SetPosition(UnityEngine.Vector3 p) { transform.position=p;PositionWrites++; }
}
public static class LevelManager { public static bool LevelInited,AfterInit; public static string LevelInitializingComment=""; }
public class AstarPath { public static AstarPath active; public bool isScanning; }
public class GameCamera { public static GameCamera Instance; public UnityEngine.Transform transform=new UnityEngine.Transform(); }
public class SceneLoader
{
    public static event Action<SceneLoadingContext> onStartedLoadingScene;
    public static void BeginLoad() { IsSceneLoading=true;if(onStartedLoadingScene!=null)onStartedLoadingScene(new SceneLoadingContext()); }
    public static void ResetEvents() { onStartedLoadingScene=null; }
    public static SceneLoader Instance; public static bool IsSceneLoading;
    public Func<Task> Load;
    public Task LoadScene(string id,object curtain,bool click) { return Load(); }
}
public struct SceneLoadingContext { }
namespace Duckov.Economy
{
    public struct Cost { }
    public static class EconomyManager { public static event Action<Cost> OnCostPaid { add { } remove { } } }
}
namespace Cysharp.Threading.Tasks { public static class UniTaskExtensions { public static void Forget(Task<bool> task) { } } }
namespace Duckov.Scenes
{
    public struct MultiSceneLocation { public string SceneID; }
    public class MultiSceneTeleporter { public MultiSceneLocation Target;public string InteractName,name;public void DoTeleport() { } }
    public class MultiSceneCore
    {
        public static MultiSceneCore Instance; public bool IsLoading;
        public int Teleports; public string Target; public Func<string,UnityEngine.Vector3,Task> Teleport;
        public Task<bool> LoadAndTeleport(string id,UnityEngine.Vector3 p)
        { Teleports++;Target=id;return Complete(Teleport==null?Task.CompletedTask:Teleport(id,p)); }
        private static async Task<bool> Complete(Task t) { await t;return true; }
        public Task<bool> LoadAndTeleport(string id,UnityEngine.Vector3 p,bool local) { return LoadAndTeleport(id,p); }
    }
}
namespace BossRush
{
    using UnityEngine;
    using UnityEngine.SceneManagement;
    public class SceneRuntimeContext { public Scene Scene; public string SceneName; public SceneRuntimeContext(Scene s) { Scene=s;SceneName=s.name; } }
    public static class BossRushUI { public static bool Paused; public static bool IsGamePaused() { return Paused; } }
    public enum BossRushEntryMode { Normal,ModeD,ModeE,ModeF,ModeG,ModeH }
    public enum ModeHLifecycle { None,EntryIntent,SceneLoading,ProductionCertifying,Recovering,MatchBrief }
    public enum ModeHExitReason { SceneGenerationMismatch,TechnicalAbort }
    public class ModeHRunState
    {
        public long OwnerToken,RunSeed; public string SceneName; public ModeHLifecycle Lifecycle;
        public ModeHRunState(string id,long seed,string scene,int generation) { SceneName=scene;OwnerToken=seed;RunSeed=seed; }
        public void UpdateSceneGeneration(int value) { } public void ResetTechnicalRetry() { }
    }
    public static class ModeHSeedStream { public static ulong Fnv1a64(string value) { return 12345; } }
    public class ModeHSupportedMap { public string SceneName,SceneId; public Vector3 SpectatorPos,PlayerSpawnPos; }
    public static class ModeHMapSupportRegistry
    {
        public static ModeHSupportedMap Map;
        public static bool TryCreateRunVariant(ModeHSupportedMap source,long seed,out ModeHSupportedMap result,out string reason) { result=source;reason=null;return source!=null; }
        public static bool TryGetMap(string name,out ModeHSupportedMap map) { map=Map;return map!=null && map.SceneName==name; }
    }
    public static class BossRushInitialSpawn { public static bool Arrived; public static bool HasArrived(Vector3 p) { return Arrived; } }
    public class BossRushMapConfig { public Vector3? customSpawnPos; }
    public static class BossRushMapRuntime { public static Vector3 ResolveArenaSignPosition(BossRushMapConfig map,Vector3 p) { return p; } }
    public static class ModeGInteractable
    { public static bool TryOpenConfirmation(ModBehaviour owner) { return false; } public static bool IsConfirmationOpen,LastConfirmationAttemptedStart; }
    public static class L10n { public static string T(string cn,string en) { return en; } }
    public static class BossRushMapSelectionHelper
    {
        public static int Generation; public static string Name,Id; public static bool Pending, Paid = true;
        internal static void NotePaidCost(Duckov.Economy.Cost cost) { }
        public static void ClearPendingEntryFlowState() { }
        public static bool HasPendingPrepaidTicket() { return Paid; }
        public static bool HasPendingModeHEntryIntent() { return Pending; }
        public static int GetPendingModeHSceneGeneration() { return Generation; }
        public static int Freeze(string name,string id) { Name=name;Id=id;Pending=true;return ++Generation; }
        public static bool TryMatchModeHSceneIntent(string name,string id,out int generation)
        { generation=Generation;return Pending && Name==name && Id==id; }
    }
    public static class ModeHEntry { public static void CancelPendingEntry() { BossRushMapSelectionHelper.Pending=false; } }
    public static class ZombieModeMapSelectionHelper { public static bool HasPendingZombieEntry; }
    public static class ModeHRuntimeGates
    {
        public static int SlotGeneration=1; public static bool Active;
        public static void SetRunOwnerActive(bool value) { Active=value; }
        public static void SetRecoveryOnlyBlocked(bool value,string reason) { }
    }
    public static class ModeHWarehouseStakeJournal { public static void TryRecomputeDeferredSlotConsistency() { } }
    public class ModeHArenaIsolationLease
    {
        public static int Acquired; public bool IsActive;
        public bool TryAcquire(string name,int generation,long token,out string reason)
        { reason=null;Acquired++;IsActive=true;return true; }
    }
    public class ModeHSpectatorLease
    {
        public static int Acquired;
        public bool TryAcquire(Vector3 point,int generation,long token,out string reason)
        { reason=null;Acquired++;CharacterMainControl.Main.SetPosition(point);return true; }
    }
    public partial class ModBehaviour : UnityEngine.Object
    {
        public readonly List<Coroutine> Routines=new List<Coroutine>(); public int LegacySetups;
        private WaitForSeconds sharedWait05s=new WaitForSeconds(.5f);
        private IntegrationRuntimeModule bossRushIntegrationRuntime;
        public bool IsZombieModeActive, ZombieStarting, ModeESelected;
        public int LegacySetupCompletions;
        public int VerificationEntered,VerificationDisposed,VerificationAfterWait,CleanupTicks;
        private bool spawnersDisabled,bossRushArenaActive,modeGActive;
        public ModBehaviour() { bossRushIntegrationRuntime=new IntegrationRuntimeModule(this); }
        internal WaitForSeconds IntegrationSharedWait05s { get { return sharedWait05s; } }
        internal WaitForSeconds IntegrationSharedWait1s { get { return new WaitForSeconds(1); } }
        public static void DevLog(string text) { }
        public static bool IsModeHRunInProgressSafe() { return ModeHRuntimeGates.Active; }
        public Coroutine StartCoroutine(IEnumerator body)
        { var c=new Coroutine{Body=body};c.Nested.Push(body);Routines.Add(c);Advance(c);return c; }
        public void StopCoroutine(Coroutine c) { c.Stopped=true; }
        public void Tick()
        { foreach(var c in Routines.ToArray())if(!c.Stopped)Advance(c); }
        private static void Advance(Coroutine coroutine)
        {
            while(coroutine.Nested.Count>0)
            {
                IEnumerator current=coroutine.Nested.Peek();
                if(!current.MoveNext()) { coroutine.Nested.Pop();continue; }
                IEnumerator nested=current.Current as IEnumerator;
                if(nested!=null) { coroutine.Nested.Push(nested);continue; }
                return;
            }
            coroutine.Stopped=true;
        }
        public Coroutine Legacy(Vector3 point) { return StartCoroutine(TeleportPlayerToCustomPositionForIntegration(point)); }
        public Coroutine Legacy(Vector3 point,Scene scene) { return StartCoroutine(bossRushIntegrationRuntime.BeginCustomTeleport(scene,TeleportPlayerToCustomPosition(point))); }
        public Coroutine ForceFallback(Vector3 point) { return StartCoroutine(bossRushIntegrationRuntime.ForceTeleportToSubScene("sub",point)); }
        public Coroutine OrdinaryEntry(Scene scene) { return StartCoroutine(bossRushIntegrationRuntime.BeginOrdinary(scene)); }
        public void DestroyOwner() { bossRushIntegrationRuntime.Close();UnityEngine.Object.Destroy(this); }
        internal void StartBossRushDemoChallengeSetupForScene(Scene scene) { }
        internal void SetBossRushArenaPlannedForIntegration(bool value) { }
        internal void ClearBossRushPendingMapEntryForIntegration() { }
        internal void ClearBossRushPendingEntryFlowStateForIntegration() { }
        private bool ReadMainExistsWithWarning(string context) { return CharacterMainControl.Main!=null; }
        private bool ReadLevelInitedWithWarning(string context) { return LevelManager.LevelInited; }
        private bool IsZombieModeStartupInProgress() { return ZombieStarting; }
        private BossRushEntryMode DetermineBossRushEntryMode(string context)
        { return BossRushMapSelectionHelper.Pending?BossRushEntryMode.ModeH:ModeESelected?BossRushEntryMode.ModeE:BossRushEntryMode.Normal; }
        private BossRushMapConfig GetMapConfigBySceneName(string name) { return new BossRushMapConfig{customSpawnPos=new Vector3(1,0,1)}; }
        private void SetArenaCenterFromMapConfig(string scene) { LegacySetups++; }
        private void InitializeEnemyPresets() { }
        private void InitializeItemValueCacheAsync() { }
        private void InitializeBossPoolFilter() { }
        private void PreCacheMapSpawnerPositions() { }
        private void ScheduleModeEStartupWarmup(string context) { }
        private void DisableAllSpawners() { }
        private void ClearEnemiesForBossRush() { }
        private bool TryStartModeE() { return true; }
        private bool TryStartModeF() { return true; }
        private bool TryStartModeD() { return true; }
        private void StopModeEStartupWarmupIfPending() { }
        private void SpawnCommonNPCs(string context) { LegacySetupCompletions++; }
        private void ScheduleRestoreFollowingSpouse(string scene,string context) { }
        private void TryRefundModeGPendingPrepaidTicket() { }
        private void ShowMessage(string message) { }
        private void SpawnBossRushMapObjects() { }
        private void TryCreateArenaDifficultyEntryPoint(Vector3 p) { }
        private void SetCurrentMapSpawnPoints(string scene) { }
        // Boundaries controlled by the fixture: their production callers and cancellation driver are real.
        private IEnumerator WaitForModeEStartupVerification(Action<bool> completed)
        { VerificationEntered++;try { yield return null;VerificationAfterWait++;completed(true); } finally { VerificationDisposed++; } }
        private IEnumerator ContinuousClearEnemiesUntilWaveStart()
        { while(true) { CleanupTicks++;yield return null; } }
    }
    internal partial class IntegrationRuntimeModule
    {
        private ModBehaviour _owner;
        public IntegrationRuntimeModule(ModBehaviour owner) { _owner=owner;SubscribeArenaEntryEvents(); }
        public void Close() { CleanupArenaEntry();_owner=null; }
        public IEnumerator BeginOrdinary(Scene scene) { return BeginArenaEntryWait(scene); }
        private bool ReadSceneLoaderDoneWithWarning(string context) { return !SceneLoader.IsSceneLoading; }
        private bool ReadCameraExistsWithWarning(string context) { return GameCamera.Instance!=null; }
        private void LogIntegrationWarningLimited(string a,string b,Exception e) { }
        private bool ReadMainExistsWithWarning(string context) { return CharacterMainControl.Main!=null; }
        private bool ReadLevelInitedWithWarning(string context) { return LevelManager.LevelInited; }
    }
    internal partial class ModeHRuntimeModule
    {
        private bool _waitingForBetReveal; private string _draftPrimaryProfileId, _draftRelayProfileId; private int _draftRefreshCount;
        private const float SceneReadyTimeoutSeconds=30f;
        private Coroutine _sceneReadyRoutine; private int _sceneReadyRequestSerial,_sceneReadyIntentGeneration;
        public ModBehaviour _owner=new ModBehaviour(); public bool IsEnabled=true;
        private int _sceneGeneration,_restoredSlotGeneration,_resumeSceneIntentGeneration;
        private bool _resumeScenePending,_restoredSeasonPending,_resumeNeedsMatchReset,_commandsClosed,_shutdownCompleted;
        private ModeHRunState _runState; private ModeHSupportedMap _map;
        private ModeHArenaIsolationLease _arenaLease; private ModeHSpectatorLease _spectatorLease;
        private int _recoveryDriveStateSequence; private float _leaseCheckAccumulator;
        private bool _errorSwapInputYielded,_seasonDirty;
        private int _selectedVirtualStake;
        private string _lastExitReasonId,_pendingContractMainId,_starterDisplayName,_relayDisplayName;
        private object _currentOddsQuote,_lastSettlementReport,_lastRewardOperation;
        public int FreshStarted,Resumed,Aborts,Exits; public string Failure;
        private bool HasActiveRun { get { return _runState!=null; } }
        public Coroutine PendingRoutine { get { return _sceneReadyRoutine; } }
        public bool ResumePending { get { return _resumeScenePending; } }
        public void Shutdown() { _shutdownCompleted=true;CancelSeasonResume();CancelSceneReadyWait(); }
        public void BeginResume(int intent)
        { _map=ModeHMapSupportRegistry.Map;_runState=new ModeHRunState("saved",54321,_map.SceneName,0);_resumeScenePending=true;_restoredSeasonPending=true;_restoredSlotGeneration=ModeHRuntimeGates.SlotGeneration;_resumeSceneIntentGeneration=intent; }
        public Task LoadResume() { return LoadSeasonResumeScene(_runState.OwnerToken,_restoredSlotGeneration,_resumeSceneIntentGeneration,_map.SceneId); }
        private void LogFailure(string tag,Exception e) { Failure=tag+":"+e.Message; }
        private void RequestExit(ModeHExitReason reason,string text) { Exits++;Shutdown(); }
        private bool TryTransition(ModeHLifecycle before,ModeHLifecycle after,string reason) { _runState.Lifecycle=after;return true; }
        private void StartCertification() { FreshStarted++;ModeHEntry.CancelPendingEntry(); }
        private void DriveRecovery() { Resumed++;_runState.Lifecycle=ModeHLifecycle.MatchBrief; }
        private void AbortSetup(string reason,bool leases) { Aborts++;Failure=reason;CancelSceneReadyWait();ModeHEntry.CancelPendingEntry(); }
        private void FailSeasonResume(string reason) { Aborts++;Failure=reason;CancelSceneReadyWait();_restoredSeasonPending=true; }
        private void OpenRecoveryShell(string reason) { Failure=reason; }
        // 续赛失败的玩家可见面（生产在 Recovery.PresentRecoveryFailure；正式构建不弹恢复壳）：此处只记原因
        private void PresentRecoveryFailure(string reason) { Failure=reason; }
        private void ReconcileCashBetOnRestore() { } // 押钱账本对账（ModeHCashBetGuard 守接线）
    }
}
