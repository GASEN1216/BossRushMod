using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    public class Coroutine { }
    public struct Vector3 { }
    public class WaitForSeconds { public readonly float Seconds; public WaitForSeconds(float seconds) { Seconds = seconds; } }
    public class WaitForSecondsRealtime { public readonly float Seconds; public WaitForSecondsRealtime(float seconds) { Seconds = seconds; } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name;
        public int handle, buildIndex;
        public bool isLoaded { get { return SceneManager.LoadedHandles.Contains(handle); } }
    }
    public static class SceneManager
    {
        public static Scene Current;
        public static readonly HashSet<int> LoadedHandles = new HashSet<int>();
        public static Scene GetActiveScene() { return Current; }
    }
}
namespace ItemStatsSystem { public class Item { } }
namespace ItemStatsSystem.Data { public class ItemTreeData { } }
namespace Cysharp.Threading.Tasks { public static class UniTaskExtensions { public static void Forget(object task) { } } }
public class LevelManager : UnityEngine.Object
{
    public static LevelManager Instance;
    public static bool LevelInited, AfterInit;
}
public class SceneLoader
{
    public static SceneLoader Instance;
    public static bool IsSceneLoading;
    internal static int BaseLoads;
    public object LoadBaseScene(object context, bool heal) { BaseLoads++; return null; }
}
namespace BossRush
{
    internal static class ModeHRuntimeGates
    {
        internal static bool IsLegacyModeEntryAllowed() { return true; }
        internal static string ResolveLegacyBlockedMessageKey() { return "blocked"; }
    }
    internal static class L10n { internal static string T(string key) { return key; } }
    internal static class ZombieModeTuning { internal const int CashToPurificationRatio = 100; }
    public sealed class ZombieModeMapProfile { }
    public sealed class ZombieModeRunState
    {
        public int RunId, SceneBuildIndex = -1, PurificationPoints, Resets;
        public string SceneName;
        public bool IsCleaningUp, EntryResourcesFinalized;
        public long PendingCashInvestment, ConfirmedCashInvested;
        public ZombieModeLifecyclePhase LifecyclePhase;
        public ZombieModeCombatPhase CombatPhase;
        public ZombieModeMapProfile MapProfile;
        public void ResetForNewRun(int id, int index, string name)
        {
            Resets++; RunId = id; SceneBuildIndex = index; SceneName = name;
            IsCleaningUp = false; EntryResourcesFinalized = false;
            PendingCashInvestment = 0; ConfirmedCashInvested = 0;
            LifecyclePhase = ZombieModeLifecyclePhase.None;
        }
    }
    public static class ZombieModeMapSelectionHelper
    {
        public static bool HasPendingZombieEntry;
        public static string TargetSceneName;
        public static int Clears;
        public static string GetPendingTargetSubSceneName() { return TargetSceneName; }
        public static void ClearPendingZombieEntry() { Clears++; HasPendingZombieEntry = false; TargetSceneName = null; }
    }
    internal static class ZombieModeEntryDebt
    {
        internal static int Invitations, CashCalls;
        internal static long Cash;
        internal static bool RefundInvitation() { Invitations++; return true; }
        internal static bool RefundCash(long amount) { CashCalls++; Cash += amount; return true; }
    }
    public sealed class ModBehaviour : UnityEngine.Object
    {
        internal ZombieModeRuntimeModule Module;
        internal int CleanupCalls, CoroutineStarts, Presets, Events, Hud;
        internal Action OnReadLoader;
        internal bool IsAnyBossRushLikeModeActive() { return false; }
        internal void ShowMessage(string text) { }
        internal static void DevLog(string text) { }
        internal bool ReadSceneLoaderDoneWithWarningForRuntimeModule(string context)
        {
            if (OnReadLoader != null) OnReadLoader();
            return !SceneLoader.IsSceneLoading;
        }
        internal bool ReadLevelInitedWithWarningForRuntimeModule(string context) { return LevelManager.LevelInited; }
        internal bool EnsureZombieModeCharacterPresetForRuntimeModule() { Module.InitializeBoundary("preset"); Presets++; return true; }
        internal void RegisterZombieModeEventListenersForRuntimeModule(int runId) { Module.InitializeBoundary("events"); Events++; }
        internal void CreateZombieModeHudForRuntimeModule(int runId) { Module.InitializeBoundary("hud"); Hud++; }
        internal void CleanupZombieModeForRuntimeModule(ZombieModeFailureReason reason)
        {
            CleanupCalls++;
            Module.CancelMapSelectionShell();
            Module.State.LifecyclePhase = ZombieModeLifecyclePhase.None;
        }
        internal Coroutine StartCoroutine(IEnumerator routine) { CoroutineStarts++; return new Coroutine(); }
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal readonly List<string> InitializeCalls = new List<string>();
        internal ZombieModeRunState State { get { return runState; } }
        internal ZombieModeEntryTransaction Transaction { get { return entryTransaction; } }
        internal bool Pending { get { return pendingEntry; } }
        internal int Attempt { get { return entryAttemptId; } }
        internal void BindForTest(ModBehaviour host)
        {
            owner = host; host.Module = this;
            AdoptHostState(new ZombieModeRunState(), new ZombieModeEntryTransaction(),
                new Dictionary<string, int[]>(), new List<int>(), new HashSet<int>(), false);
        }
        internal void InitializeBoundary(string name)
        {
            if (SceneLoader.IsSceneLoading || !LevelManager.LevelInited || !LevelManager.AfterInit)
                throw new Exception("initialization crossed an unready official boundary: " + name);
            if (SceneManager.Current.buildIndex != runState.SceneBuildIndex)
                throw new Exception("initialization crossed the wrong scene boundary: " + name);
            InitializeCalls.Add(name);
        }
        internal ZombieModeMapProfile BuildZombieModeMapProfile(int index, string name) { return new ZombieModeMapProfile(); }
        private void PrepareSoulCubePrefabCacheForZombieRun() { InitializeBoundary("cache"); }
        private bool PrepareZombieModeInventoryTransfer(int id) { InitializeBoundary("inventory"); return true; }
        private bool CollectZombieModeSpawnPoints(int id) { InitializeBoundary("spawn"); return true; }
        private bool ApplyZombieModeMapIsolationShell(int id) { InitializeBoundary("isolation"); return true; }
        private bool InitializeZombieModeContainersShell(int id) { InitializeBoundary("containers"); return true; }
        private bool GrantZombieModeBeacon(int id) { InitializeBoundary("beacon"); return true; }
        private void ShowZombieModeStarterChoice(int id) { InitializeBoundary("choice"); }
    }
}
