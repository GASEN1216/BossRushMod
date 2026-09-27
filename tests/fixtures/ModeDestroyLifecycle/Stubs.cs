using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using ItemStatsSystem;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) { return (ReferenceEquals(a, null) || a.Destroyed) ? ReferenceEquals(b, null) || b.Destroyed : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            var go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (var component in go.Components)
            {
                component.Destroyed = true;
                var host = component as BossRush.ModBehaviour;
                if (!ReferenceEquals(host, null)) host.DestroyNativeCoroutines();
            }
        }
        public int GetInstanceID() { return GetHashCode(); }
    }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        internal readonly List<Object> Components = new List<Object>();
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class MonoBehaviour : Object
    {
        public readonly GameObject gameObject = new GameObject();
        public MonoBehaviour() { gameObject.Components.Add(this); }
        public T GetComponent<T>() where T : class { return this is BossRush.CharacterMainControl && typeof(T) == typeof(BossRush.Health) ? ((BossRush.CharacterMainControl)this).Health as T : null; }
    }
    public sealed class Coroutine { public bool Stopped; }
    public struct Vector3 { }
    public sealed class WaitForSeconds { public readonly float Seconds; public WaitForSeconds(float s) { Seconds = s; } }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction<T>(T value);
    public sealed class UnityEvent<T>
    {
        private readonly List<UnityAction<T>> handlers = new List<UnityAction<T>>();
        public int Count { get { return handlers.Count; } }
        public void AddListener(UnityAction<T> h) { handlers.Add(h); }
        public void RemoveListener(UnityAction<T> h) { handlers.Remove(h); }
        public void Invoke(T v) { foreach (var h in handlers.ToArray()) h(v); }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle, buildIndex; public string name; }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { handle = 1, buildIndex = 1, name = "test" }; } }
}
namespace ItemStatsSystem
{
    public class Modifier
    {
        public bool Removed; public Stat Target;
        public void RemoveFromTarget() { Removed = true; if (Target != null) Target.RemoveModifier(this); }
    }
    public sealed class Stat
    {
        public readonly List<Modifier> Modifiers = new List<Modifier>();
        public Modifier Add() { var m = new Modifier { Target = this }; Modifiers.Add(m); return m; }
        public void RemoveModifier(Modifier m) { Modifiers.Remove(m); m.Removed = true; }
    }
    public sealed class Item
    {
        private readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        public Stat GetStat(string key) { Stat s; if (!stats.TryGetValue(key, out s)) stats[key] = s = new Stat(); return s; }
    }
}
namespace BossRush
{
    internal static class Probe { internal static readonly List<string> Events = new List<string>(); }
    public enum Teams { player, wolf, middle }
    public sealed class DamageInfo { }
    public sealed class Health : UnityEngine.Object
    {
        public readonly UnityEvent<DamageInfo> OnDeadEvent = new UnityEvent<DamageInfo>();
        public float MaxHealth = 100, CurrentHealth = 140;
        public void SetHealth(float h) { CurrentHealth = h; }
    }
    public sealed class CharacterMainControl : MonoBehaviour
    {
        public static CharacterMainControl Main;
        public Teams Team = Teams.wolf;
        public readonly Health Health = new Health();
        public readonly Item CharacterItem = new Item();
        public bool dropBoxOnDead = true;
        public event Action<DamageInfo> BeforeCharacterSpawnLootOnDead;
        public int LootHandlerCount { get { return BeforeCharacterSpawnLootOnDead == null ? 0 : BeforeCharacterSpawnLootOnDead.GetInvocationList().Length; } }
        public CharacterMainControl() { gameObject.Components.Add(Health); }
        public void SetTeam(Teams t) { Team = t; }
        public void SetRunInput(bool value) { }
        public void FireLoot() { if (BeforeCharacterSpawnLootOnDead != null) BeforeCharacterSpawnLootOnDead(new DamageInfo()); }
    }
    public sealed class CharacterRandomPreset { }
    public sealed class CountDownArea : MonoBehaviour { }
    public sealed class AICharacterController { public object searchedEnemy; public bool noticed, enabled; public float forceTracePlayerDistance; }
    public sealed class EnemyPresetInfo { }
    internal sealed class ModeDItemPool { internal void TryPrewarmModeDGlobalItemPool() { } }
    internal sealed class SceneRuntimeContext { }
    internal abstract class BossRushRuntimeModuleBase
    {
        public virtual string ModuleName { get { return "stub"; } }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnDestroy() { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float delta, float unscaled) { }
    }
    public sealed class ModBehaviour : MonoBehaviour
    {
        internal ModeFRuntimeModule ModeF;
        internal readonly HashSet<CharacterMainControl> Recovery = new HashSet<CharacterMainControl>();
        internal readonly HashSet<string> Mutators = new HashSet<string>();
        internal int Messages, CourierCleanup, StartedCoroutines;
        internal readonly List<IEnumerator> Deferred = new List<IEnumerator>();
        private readonly List<Coroutine> nativeCoroutines = new List<Coroutine>();
        private readonly Dictionary<Coroutine, IEnumerator> nativeTasks = new Dictionary<Coroutine, IEnumerator>();
        internal Coroutine OwnCoroutine() { var c = new Coroutine(); nativeCoroutines.Add(c); return c; }
        internal void DestroyNativeCoroutines() { foreach (var c in nativeCoroutines) c.Stopped = true; Deferred.Clear(); nativeTasks.Clear(); }
        public static void DevLog(string s) { if (s.Contains("ERROR") || s.Contains("WARNING")) Probe.Events.Add(s); }
        public void StopCoroutine(Coroutine c) { if (this == null) throw new InvalidOperationException("StopCoroutine on destroyed host"); c.Stopped = true; IEnumerator task; if (nativeTasks.TryGetValue(c, out task)) { Deferred.Remove(task); nativeTasks.Remove(c); } }
        public Coroutine StartCoroutine(IEnumerator task) { if (this == null) throw new InvalidOperationException("StartCoroutine on destroyed host"); StartedCoroutines++; Deferred.Add(task); var coroutine = OwnCoroutine(); nativeTasks[coroutine] = task; return coroutine; }
        public void ClearModeDEnemyRecoveryState() { Recovery.Clear(); }
        public void UnregisterEnemyRecoveryForArena(CharacterMainControl enemy) { Recovery.Remove(enemy); }
        public void ClearModeDMutators(string mode) { Mutators.Remove(mode); }
        public void ClearModeEFSpawnPostprocessScheduler() { Probe.Events.Add("scheduler-clear"); }
        public void ResetModeEFLootboxTrackerState() { }
        public void DestroyCourierNPC() { CourierCleanup++; }
        public void ShowMessage(string message) { Messages++; }
        public bool IsModeFActive { get { return ModeF != null && ModeF.modeFActive; } }
        public bool IsModeFSessionStillValid(int token, int scene) { return ModeF != null && ModeF.IsModeFSessionStillValid(token, scene); }
    }
    internal static class L10n { internal static string T(string a, string b) { return b; } }
    internal static class DragonBreathBuffHandler { internal static void Cleanup() { Probe.Events.Add("dragon-cleanup"); } }
    internal static class EvacuationCountdownUI { internal static void Release(CountDownArea area) { Probe.Events.Add("extraction-release"); } }
    internal static class ModeFStatusHud { internal static void Dispose() { Probe.Events.Add("hud-dispose"); } }
    internal sealed class ModeEFEnemySpawnRuntime { internal void ResetSpawnTracking() { } internal void BuildModeEFactionPresetCaches() { } }
    internal sealed class ModeEFVirtualSpawnerRegistry
    {
        internal void ClearRegisteredEnemies() { }
        internal void UnregisterModeEEnemyFromSpawnerRoot(CharacterMainControl c) { }
    }
    internal sealed class ModeEFSpawnPreparation { internal void Reset(bool allocation, bool cache) { } }
    internal sealed class ModeEFMerchantRuntime { internal int Cleans; internal void CleanupModeEMerchant() { Cleans++; } }
    internal sealed class WavesArenaRuntimeModule { internal void ClearBossRandomLootTracking(CharacterMainControl c) { } }
    internal sealed partial class ModeDRuntimeModule { private void TickModeDIntegrity(float dt) { } }
    internal sealed partial class ModeERuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour modeEHost;
        private bool modeERuntimeDestroyed, modeECleanupPending;
        internal bool modeEActive;
        internal int modeESessionToken;
        private int modeESessionSerial;
        private ModeEFEnemyRegistry enemyRegistry;
        private ModeEFEnemySpawnRuntime spawnRuntime;
        private ModeEFVirtualSpawnerRegistry virtualSpawnerRegistry;
        private ModeEFSpawnPreparation spawnPreparation;
        private ModeDItemPool equipment;
        private Teams modeEPlayerFaction;
        private float modeEIntegrityTimer, modeEScalingBatchTimer;
        private int modeEPlayerLastHitKillCount, modeERespawnKillCounter;
        private readonly Dictionary<Teams, int> modeEFactionDeathCount = new Dictionary<Teams, int>();
        private readonly HashSet<Teams> modeEPendingScalingFactions = new HashSet<Teams>();
        private readonly Dictionary<CharacterMainControl, float> modeEPendingAggroTraceDistance = new Dictionary<CharacterMainControl, float>();
        private readonly List<CharacterMainControl> modeEEndCleanupEnemyScratch = new List<CharacterMainControl>();
        private List<CharacterMainControl> modeEAliveEnemies { get { return enemyRegistry.LegacyAliveEnemies; } }
        private Coroutine modeEStartupWarmupCoroutine;
        private Coroutine modeEMerchantWarmupCoroutine;
        private string modeEStartupWarmupSceneName;
        private sealed class ModeEEnemyScalingState { public Modifier hp, gunDmg, meleeDmg; public int appliedStacks; }
        private sealed class ModeEPlayerScalingState { public Modifier hp, gunDmg, meleeDmg; }
        private ModeEPlayerScalingState modeEPlayerScalingState;
        private Item modeEPlayerScalingItem;
        private readonly Dictionary<CharacterMainControl, ModeEEnemyScalingState> modeEEnemyScalingStates = new Dictionary<CharacterMainControl, ModeEEnemyScalingState>();
        internal Modifier PlayerModifier, EnemyModifier;
        internal Coroutine Warmup;
        internal Coroutine CurrentWarmup { get { return modeEStartupWarmupCoroutine; } }
        internal Coroutine MerchantWarmup { get { return modeEMerchantWarmupCoroutine; } }
        private sealed class ModeEStartupProfiler { internal ModeEStartupProfiler(string scope, string reason) { } internal void Mark(string text) { } internal void Complete(string text = null) { } }
        private void InitializeModeDItemPools() { }
        private void InitializeModeDEnemyPools() { }
        private void PreCacheMapSpawnerPositions() { }
        private void PrewarmModeESmokeVfxPrefab() { }
        private IEnumerator WarmModeEMerchantCachesAsync() { yield return null; }
        internal int DeathCallbacks, ShellDisposes, ShellInvalidations, MerchantCleanup;
        internal void Bind(ModeEFEnemyRegistry registry)
        { enemyRegistry = registry; spawnRuntime = new ModeEFEnemySpawnRuntime(); virtualSpawnerRegistry = new ModeEFVirtualSpawnerRegistry(); spawnPreparation = new ModeEFSpawnPreparation(); equipment = new ModeDItemPool(); }
        internal void Seed(CharacterMainControl enemy)
        {
            modeESessionSerial = 16; modeEActive = true; BeginModeESession(); modeEPlayerFaction = Teams.wolf;
            CharacterMainControl.Main.SetTeam(Teams.wolf);
            PlayerModifier = CharacterMainControl.Main.CharacterItem.GetStat("MaxHealth").Add();
            modeEPlayerScalingState = new ModeEPlayerScalingState { hp = PlayerModifier };
            modeEPlayerScalingItem = CharacterMainControl.Main.CharacterItem;
            EnemyModifier = enemy.CharacterItem.GetStat("MaxHealth").Add();
            modeEEnemyScalingStates[enemy] = new ModeEEnemyScalingState { hp = EnemyModifier };
            enemyRegistry.BindDeathCallback((c, d) => DeathCallbacks++);
            enemyRegistry.TrackModeEAliveEnemy(enemy, enemy.Team); enemyRegistry.RegisterModeEEnemyDeath(enemy);
            enemyRegistry.AttachLootHandler(enemy, d => DeathCallbacks++);
            modeEStartupWarmupCoroutine = Warmup = modeEHost.OwnCoroutine();
        }
        private void CleanupModeELotteryAndHiringRuntime() { Probe.Events.Add("lottery-cleanup"); }
        private void DestroyModeEShellRuntimeState() { ShellDisposes++; }
        private void ResetModeEMerchantStaticCaches() { }
        private void InvalidateAndResetModeEShellSession(string reason) { ShellInvalidations++; }
        private void ClearPendingBossAggroQueue() { }
        private void CleanupModeEPlayerNameTag() { }
        private void ResetModeEUiCaches() { }
        private void CleanupModeEMerchant() { MerchantCleanup++; }
        private void ClearModeEBossRegenCache() { enemyRegistry.ClearModeEBossRegenCache(); }
        private void CleanupModeEVirtualSpawnerRoot() { }
        private void ResetModeERespawnRuntimeState() { }
        private void UnregisterModeEBossHireRuntime(CharacterMainControl c, bool b) { }
        private void UnregisterModeEEnemyDeath(CharacterMainControl c) { enemyRegistry.UnregisterModeEEnemyDeath(c); }
        private void UnregisterModeEEnemyLootHandler(CharacterMainControl c) { enemyRegistry.UnregisterModeEEnemyLootHandler(c); }
        private void UnregisterModeEEnemyFromSpawnerRoot(CharacterMainControl c) { virtualSpawnerRegistry.UnregisterModeEEnemyFromSpawnerRoot(c); }
        private void UntrackModeEAliveEnemy(CharacterMainControl c, Teams? f) { enemyRegistry.UntrackModeEAliveEnemy(c, f); }
        internal void RemovePendingModeEAggroTarget(CharacterMainControl c) { }
    }
    internal sealed partial class ModeFRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private bool modeFRuntimeDestroyed, modeFCleanupPending;
        internal bool modeFActive;
        internal readonly ModeFState modeFState = new ModeFState();
        private int modeFSessionSerial;
        private ModeERuntimeModule modeE;
        private ModeEFEnemyRegistry enemyRegistry;
        private ModeEFVirtualSpawnerRegistry virtualSpawnerRegistry;
        private WavesArenaRuntimeModule arena;
        private ModeEFMerchantRuntime merchantRuntime;
        private Modifier modeFMaxHealthModifier;
        private float modeFBossRetargetTimer, modeFBossIntegrityTimer;
        private bool modeFPlayerDeathHandled, modeFBountyLeaderDirty, modeFHasActiveFortificationHighlight;
        private int modeFPendingRespawnCount, modeFRespawnInFlightCount;
        private readonly HashSet<int> modeFHandledBossDeathIds = new HashSet<int>();
        private readonly HashSet<CharacterMainControl> modeFActiveBossSet = new HashSet<CharacterMainControl>();
        private CharacterMainControl modeFBountyLeaderPreferred;
        private readonly Dictionary<CharacterMainControl, (Modifier hp, Modifier gunDmg, Modifier meleeDmg)> modeFBossModifiers = new Dictionary<CharacterMainControl, (Modifier, Modifier, Modifier)>();
        private readonly Dictionary<CharacterMainControl, UnityAction<DamageInfo>> modeFBossDeathHandlers = new Dictionary<CharacterMainControl, UnityAction<DamageInfo>>();
        private readonly Dictionary<CharacterMainControl, Action<DamageInfo>> modeFBossLootHandlers = new Dictionary<CharacterMainControl, Action<DamageInfo>>();
        private readonly Dictionary<CharacterMainControl, object> modeFBossForcedTargets = new Dictionary<CharacterMainControl, object>();
        private readonly Dictionary<CharacterMainControl, object> modeFBossAppliedSpeedBonuses = new Dictionary<CharacterMainControl, object>();
        private readonly Dictionary<CharacterMainControl, object> modeFBossAiControllers = new Dictionary<CharacterMainControl, object>();
        private readonly Dictionary<int, int> modeFPendingUtilityRewardCounts = new Dictionary<int, int>();
        private readonly List<int> modeFPendingUtilityRewardTypeScratch = new List<int>();
        private readonly Dictionary<GameObject, Coroutine> modeFDeferredExitBossObjects = new Dictionary<GameObject, Coroutine>();
        private GameObject modeFPlacementPreview;
        private bool modeFPlacementActive, modeFRepairSelectionActive;
        private int modeFPlacementItemTypeId, modeFRepairSelectionItemTypeId;
        private bool? modeFPlacementLastCanPlace;
        private ModeFFortificationMarker modeFRepairSelectionTarget;
        private readonly List<object> modeFPlacementPreviewRendererCache = new List<object>();
        private readonly Dictionary<object, object> modeFPlacementPreviewOriginalMaterials = new Dictionary<object, object>();
        internal Modifier PlayerModifier, BossModifier;
        internal GameObject Preview;
        internal int Awards, Refunds, Callbacks;
        internal int PendingCount { get { return modeFPendingUtilityRewardCounts.Count; } }
        internal int DeferredExitCount { get { return modeFDeferredExitBossObjects.Count; } }
        internal void MarkUtilityActionsCommitted()
        { modeFPlacementActive = false; modeFPlacementItemTypeId = 0; modeFRepairSelectionActive = false; modeFRepairSelectionItemTypeId = 0; }
        internal void Bind(ModeERuntimeModule e, ModeEFEnemyRegistry registry)
        { modeE = e; enemyRegistry = registry; virtualSpawnerRegistry = new ModeEFVirtualSpawnerRegistry(); arena = new WavesArenaRuntimeModule(); merchantRuntime = new ModeEFMerchantRuntime(); }
        internal void Seed(CharacterMainControl boss, ModeFFortificationMarker fort)
        {
            modeFSessionSerial = 16; modeFActive = true; modeFState.IsActive = true; BeginModeFSession();
            modeFState.TempMaxHealthGrowth = 40;
            modeFMaxHealthModifier = PlayerModifier = CharacterMainControl.Main.CharacterItem.GetStat("MaxHealth").Add();
            BossModifier = boss.CharacterItem.GetStat("MaxHealth").Add(); modeFBossModifiers[boss] = (BossModifier, null, null);
            modeFState.ActiveBosses.Add(boss); modeFActiveBossSet.Add(boss);
            UnityAction<DamageInfo> dead = d => Callbacks++; Action<DamageInfo> loot = d => Callbacks++;
            modeFBossDeathHandlers[boss] = dead; boss.Health.OnDeadEvent.AddListener(dead);
            modeFBossLootHandlers[boss] = loot; boss.BeforeCharacterSpawnLootOnDead += loot;
            enemyRegistry.TrackModeEAliveEnemy(boss, boss.Team); enemyRegistry.RegisterModeEEnemyDeath(boss); enemyRegistry.AttachLootHandler(boss, d => Callbacks++);
            modeFState.ActiveFortifications[1] = fort;
            modeFPlacementPreview = Preview = new GameObject(); modeFPlacementActive = true; modeFPlacementItemTypeId = 1;
            modeFRepairSelectionActive = true; modeFRepairSelectionItemTypeId = 2; modeFRepairSelectionTarget = fort;
            modeFPendingUtilityRewardCounts[3] = 3;
            modeFState.ActiveExtractionArea = new CountDownArea();
        }
        private void FlushModeFPendingUtilityRewards(bool world) { Probe.Events.Add("awards"); Awards += 3; modeFPendingUtilityRewardCounts.Clear(); }
        private string GetModeFUtilityItemDisplayName(int id) { return "item:" + id; }
        private bool TryGiveItemToPlayerOrDrop(int id, string name, bool bubble) { Probe.Events.Add("refund:" + id); Refunds++; return true; }
        private void HighlightModeFFortification(ModeFFortificationMarker f, bool value) { }
        private void EndModeFBloodfireOverload(bool force) { modeFState.BloodfireOverloadActive = false; }
        private void ResetModeFPlayerBountyKillLatch() { }
        private void ResetPlayerBountyKillLatch() { }
        private void CleanupModeFPlayerNameTag() { }
        private void CleanupModeFBountyRadarUI() { }
        private void CleanupModeFExtractionMapMarker() { }
        private void ResetModeFUiCaches() { }
        private void ClearModeFBossMoveSpeedModifiers() { }
        private void RestoreOriginalExtractionPoints() { Probe.Events.Add("extraction-restore"); }
        private void ClearModeFLeaderChangeContext() { }
        private void ClearModeFBossRegenCache() { }
        private void ClearAllModeFBossPlunderLootState() { }
        private void MarkModeFBossRegenCacheDirty() { }
        private void ClearModeFBossPlunderLootState(CharacterMainControl c) { }
        private Teams? TryGetModeFBossTeam(CharacterMainControl c, string reason) { return c.Team; }
        private bool TryGetModeFBossInstanceId(CharacterMainControl c, out int id, string reason) { id = c.GetInstanceID(); return true; }
        private void TryRemoveModeFBountyMarks(int id, string reason) { modeFState.BountyMarksByCharacterId.Remove(id); }
        private void ApplyModeFBossMoveSpeedModifier(CharacterMainControl c, float amount) { }
        private AICharacterController GetModeFBossAIController(CharacterMainControl c) { return null; }
    }
}
