using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using BossRush;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an && bn || !an && !bn && ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return this == o as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object o)
        {
            if (ReferenceEquals(o, null) || o.Destroyed) return;
            o.Destroyed = true;
            var go = o as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object c in go.Components) Destroy(c);
        }
    }
    public class GameObject : Object
    {
        internal readonly List<Object> Components = new List<Object>();
        public Scene scene = SceneManager.GetActiveScene();
        public void SetActive(bool value) { }
    }
    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public Transform transform = new Transform();
        public Component() { gameObject.Components.Add(this); }
    }
    public class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float sqrMagnitude { get { return x*x + y*y + z*z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x, a.y-b.y, a.z-b.z); }
    }
    public sealed class WaitForSeconds { public readonly float Seconds; public WaitForSeconds(float value) { Seconds = value; } }
    public abstract class CustomYieldInstruction : IEnumerator
    {
        public abstract bool keepWaiting { get; }
        public object Current { get { return null; } }
        public bool MoveNext() { return keepWaiting; }
        public void Reset() { }
    }
    public sealed class Coroutine
    {
        internal readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
        internal object Wait;
        internal bool Done;
    }
    public static class Mathf { public static int Clamp(int value, int lo, int hi) { return Math.Max(lo, Math.Min(value, hi)); } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
        public string name { get { return SceneManager.Names[handle]; } }
        public bool isLoaded { get { return SceneManager.Loaded.Contains(handle); } }
        public bool IsValid() { return handle > 0 && SceneManager.Names.ContainsKey(handle); }
        public static bool operator ==(Scene a, Scene b) { return a.handle == b.handle; }
        public static bool operator !=(Scene a, Scene b) { return a.handle != b.handle; }
        public override bool Equals(object o) { return o is Scene && this == (Scene)o; }
        public override int GetHashCode() { return handle; }
    }
    public static class SceneManager
    {
        public static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        public static readonly HashSet<int> Loaded = new HashSet<int>();
        public static int Active;
        public static event Action<Scene> sceneUnloaded;
        public static Scene GetActiveScene() { return new Scene { handle = Active }; }
        public static Scene Add(int id, string name) { Names[id] = name; Loaded.Add(id); return new Scene { handle = id }; }
        public static void Unload(Scene scene) { Loaded.Remove(scene.handle); if (sceneUnloaded != null) sceneUnloaded(scene); }
        public static void MoveGameObjectToScene(GameObject go, Scene scene) { go.scene = scene; Probe.Touch("move-scene"); }
    }
}
public struct SceneLoadingContext { }
public static class SceneLoader
{
    public static bool IsSceneLoading;
    public static event Action<SceneLoadingContext> onStartedLoadingScene;
    public static void BeginLoad() { IsSceneLoading = true; if (onStartedLoadingScene != null) onStartedLoadingScene(new SceneLoadingContext()); }
}
public static class LevelManager { public static bool LevelInited; }
public class GameCamera { public static GameCamera Instance; }
namespace ItemStatsSystem { public class Item { } }
namespace BossRush
{
    internal abstract class BossRushRuntimeModuleBase
    {
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnDestroy() { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float a, float b) { }
        public abstract string ModuleName { get; }
    }
    internal struct SceneRuntimeContext { }
    public sealed class Health { public bool IsDead; }
    public class CharacterMainControl : Component
    {
        public static CharacterMainControl Main;
        public Health Health = new Health();
        public bool dropBoxOnDead;
        public void SetPosition(Vector3 value) { transform.position = value; Probe.Touch("teleport"); }
    }
    public class EnemyPresetInfo { }
    public class CharacterRandomPreset { }
    internal static class Probe
    {
        internal static readonly List<string> Effects = new List<string>();
        internal static readonly List<string> Disposal = new List<string>();
        internal static string Entry = "Normal";
        internal static int VerificationFrames, SignFrames;
        internal static bool CustomWaiting, CoroutineWaiting;
        internal static void Touch(string value) { Effects.Add(value); }
        internal static bool Has(string value) { return Effects.Contains(value); }
    }
    internal sealed class ModeDItemPool
    {
        internal object FindTagByName(string name) { return null; }
        internal void InitializeModeDItemPools(Func<string, object> lookup) { }
        internal void EnsureModeDGlobalItemPool() { }
        internal void GivePlayerStarterKit() { Probe.Touch("starter"); }
    }
    internal sealed partial class ModeDRuntimeModule
    {
        private void TickModeDIntegrity(float dt) { }
        private void SetupSignForModeD(object sign) { }
    }
    internal static class ModeHRuntimeGates
    {
        internal static bool IsLegacyModeEntryAllowed() { return true; }
        internal static string ResolveLegacyBlockedMessageKey() { return "blocked"; }
    }
    internal static class L10n
    {
        internal static string T(string value) { return value; }
        internal static string T(string cn, string en) { return en; }
    }
    internal static class BossRushInitialSpawn { internal static bool HasArrived(Vector3 value) { return false; } }
    internal static class BossRushMapSelectionHelper
    {
        internal static bool HasPendingModeHEntryIntent() { return Probe.Entry == "H"; }
        internal static bool HasPendingPrepaidTicket() { return Probe.Entry == "F" || Probe.Entry == "G"; }
        internal static void ClearPendingEntryFlowState() { Probe.Touch("clear-entry"); }
    }
    internal static class ModeGAvailability
    { internal static bool IsProductionReady { get { return true; } } internal static bool AllowDevTestEntry { get { return false; } } }
    internal static class ModeGMapSupportRegistry { internal static bool IsVerifiedSceneName(string name) { return true; } }
    internal static class ModeGInteractable
    {
        internal static bool IsConfirmationOpen { get { return false; } }
        internal static bool LastConfirmationAttemptedStart { get { return false; } }
        internal static bool TryOpenConfirmation(ModBehaviour owner) { Probe.Touch("mode-g"); owner.modeGActive = true; return true; }
    }
    internal sealed partial class IntegrationRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour _owner;
        private object _deferredBootstrapActions;
        public override string ModuleName { get { return "Integration"; } }
        private void StopRuntimeStateMonitor() { }
        private void CleanupDeferredIntegrationBootstrap() { }
        private void CleanupRuntimeEvents() { }
        private void UnsubscribePurchaseEvents() { }
        private void UnsubscribeDragonBreathEffectEvent() { }
        internal void LogIntegrationWarningLimited(string a, string b, Exception e) { }
    }
    public partial class ModBehaviour : Component
    {
        public static ModBehaviour Instance;
        internal readonly IntegrationRuntimeModule bossRushIntegrationRuntime = new IntegrationRuntimeModule();
        internal readonly ModeDRuntimeModule modeDRuntime = new ModeDRuntimeModule();
        private Vector3 demoChallengeStartPosition;
        private CharacterMainControl playerCharacter;
        internal bool modeGActive;
        public ModBehaviour() { Instance = this; bossRushIntegrationRuntime.OnAwake(this); modeDRuntime.OnAwake(this); }
        internal object CreateIntegrationDeferredBootstrapActions() { return new object(); }
        internal IEnumerator Entry(Scene scene) { return bossRushIntegrationRuntime.WaitForLevelInitializedThenSetup_Integration(scene); }
        internal void DestroyOwner() { bossRushIntegrationRuntime.OnDestroy(); modeDRuntime.OnDestroy(); UnityEngine.Object.Destroy(gameObject); }
        internal static void DevLog(string value) { }
        public bool IsPlayerNaked() { Probe.Touch("inventory"); return Probe.Entry == "D" || Probe.Entry == "E"; }
        public bool IsModeEActive { get; private set; }
        internal int ModeDConfiguredEnemiesPerWave { get { return 3; } }
        internal object ArenaRewardSignInteract { get { return null; } }
        internal void ResetArenaForModeD() { Probe.Touch("mode-d"); }
        internal void ClearModeDEnemyRecoveryState() { }
        internal void InitializeModeDEnemyPoolsForRuntime() { }
        internal void TryRollMutatorsForArena(string mode) { }
        internal void ClearModeDMutators(string mode) { }
        internal void UnregisterEnemyRecoveryForArena(CharacterMainControl enemy) { }
        internal void ShowMessage(string value) { }
        internal void ShowBigBanner(string value) { }
        private (int?, ItemStatsSystem.Item) DetectFactionFlag()
        { return Probe.Entry == "E" ? ((int?)1, new ItemStatsSystem.Item()) : ((int?)null, null); }
        private ItemStatsSystem.Item DetectBossRushTicketItem() { return null; }
        private ItemStatsSystem.Item DetectBloodhuntTransponder() { return Probe.Entry == "F" ? new ItemStatsSystem.Item() : null; }
        private ItemStatsSystem.Item DetectFateEchoRelic() { return Probe.Entry == "G" ? new ItemStatsSystem.Item() : null; }
        private bool IsPlayerNakedForModeF() { return Probe.Entry == "F"; }
        private bool IsModeGLoadoutEligible() { return Probe.Entry == "G"; }
        private void ScheduleModeEStartupWarmup(string value) { Probe.Touch("warmup"); }
        private void PreCacheMapSpawnerPositions() { Probe.Touch("precache"); }
        private void DisableAllSpawners() { Probe.Touch("disable"); }
        private void ClearEnemiesForBossRush() { Probe.Touch("clear-enemies"); }
        private bool TryStartModeE() { Probe.Touch("mode-e"); IsModeEActive = true; return true; }
        private bool TryStartModeF() { Probe.Touch("mode-f"); return true; }
        private IEnumerator WaitForModeEStartupVerification(Action<bool> result)
        {
            try { yield return VerifyNested(result); }
            finally { Probe.Disposal.Add("verification-parent"); }
        }
        private IEnumerator VerifyNested(Action<bool> result)
        {
            try { for (int i=0; i<Probe.VerificationFrames; i++) yield return null; Probe.Touch("verified"); result(true); }
            finally { Probe.Disposal.Add("verification-child"); }
        }
        private void SpawnCommonNPCs(string value) { Probe.Touch("npcs"); }
        private void ScheduleRestoreFollowingSpouse(string a, string b) { Probe.Touch("spouse"); }
        private void StopModeEStartupWarmupIfPending() { Probe.Touch("warmup-stop"); }
        private void TryRefundModeGPendingPrepaidTicket() { Probe.Touch("refund"); }
        private static Vector3 GetCurrentSceneDefaultPosition() { return new Vector3(3,4,5); }
        private T FindObjectOfType<T>() where T : class { return CharacterMainControl.Main as T; }
        private void CreateRescueTeleportBubble() { Probe.Touch("bubble"); }
        private void TryCreateArenaDifficultyEntryPoint() { Probe.Touch("sign"); }
        private IEnumerator EnsureArenaEntryPointCreated()
        {
            try { for (int i=0; i<Probe.SignFrames; i++) yield return null; Probe.Touch("sign-retry"); }
            finally { Probe.Disposal.Add("sign"); }
        }
        internal IEnumerator CustomAndCoroutine(IEnumerator child)
        {
            yield return child;
            yield return new NativeWait();
            Probe.Touch("native-wait-finished");
            yield return StartCoroutine(WaitForExternal());
            Probe.Touch("coroutine-finished");
        }
        private sealed class NativeWait : CustomYieldInstruction
        { public override bool keepWaiting { get { return Probe.CustomWaiting; } } }
        private IEnumerator WaitForExternal() { while (Probe.CoroutineWaiting) yield return null; }
        internal Coroutine StartCoroutine(IEnumerator routine) { return Scheduler.Start(routine); }
        internal void StopCoroutine(Coroutine routine) { Scheduler.Stop(routine); }
    }
}
internal static class Scheduler
{
    private static readonly List<Coroutine> Running = new List<Coroutine>();
    internal static Coroutine Start(IEnumerator routine)
    {
        var c = new Coroutine(); c.Stack.Push(routine); Running.Add(c); Advance(c); return c;
    }
    private static void Advance(Coroutine c)
    {
        while (!c.Done && c.Stack.Count > 0)
        {
            IEnumerator next = c.Stack.Peek();
            if (!next.MoveNext()) { c.Stack.Pop(); var d=next as IDisposable; if(d!=null)d.Dispose(); continue; }
            object current=next.Current; var nested=current as IEnumerator;
            if(nested!=null && !(current is CustomYieldInstruction)) { c.Stack.Push(nested); continue; }
            c.Wait=current; return;
        }
        c.Done=true; Running.Remove(c);
    }
    internal static void Tick(int count=1)
    {
        for(int n=0;n<count;n++) foreach(Coroutine c in Running.ToArray())
        {
            if(c.Done)continue;
            var awaited=c.Wait as Coroutine; if(awaited!=null && !awaited.Done)continue;
            var custom=c.Wait as CustomYieldInstruction; if(custom!=null && custom.keepWaiting)continue;
            Advance(c);
        }
    }
    internal static void Stop(Coroutine c)
    {
        c.Done=true; Running.Remove(c);
        while(c.Stack.Count>0) { var d=c.Stack.Pop() as IDisposable; if(d!=null)d.Dispose(); }
    }
    internal static void Reset() { foreach(Coroutine c in Running.ToArray())Stop(c); }
}
