using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b) { bool an = ReferenceEquals(a, null) || a.Destroyed; bool bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null) || value.Destroyed) return;
            value.Destroyed = true;
            var go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object component in go.Components) component.Destroyed = true;
        }
        public static void DontDestroyOnLoad(Object value) { }
    }
    public sealed class GameObject : Object
    {
        internal readonly List<Object> Components = new List<Object>();
        public GameObject(string name = "fixture") { }
        public T AddComponent<T>() where T : MonoBehaviour, new() { T value = new T(); value.gameObject = this; Components.Add(value); return value; }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject = new GameObject();
        internal readonly List<Coroutine> Coroutines = new List<Coroutine>();
        public Coroutine StartCoroutine(IEnumerator routine) { var handle = new Coroutine(routine); Coroutines.Add(handle); handle.MoveNext(); return handle; }
        public void StopCoroutine(Coroutine handle) { handle.Stopped = true; var disposable = handle.Routine as IDisposable; if (disposable != null) disposable.Dispose(); }
    }
    public sealed class Coroutine : IEnumerator
    {
        internal readonly IEnumerator Routine;
        internal bool Stopped;
        internal Coroutine(IEnumerator routine) { Routine = routine; }
        public object Current { get { return Routine.Current; } }
        public bool MoveNext() { return !Stopped && Routine.MoveNext(); }
        public void Reset() { throw new NotSupportedException(); }
    }
    public sealed class WaitForSeconds { public readonly float Seconds; public WaitForSeconds(float seconds) { Seconds = seconds; } }
    public class Transform { }
    public struct Vector3 { public float x; public static Vector3 zero { get { return new Vector3(); } } }
}
namespace UnityEngine.SceneManagement { public struct Scene { public string name; public Scene(string name) { this.name = name; } } }
namespace ItemStatsSystem.Items { public sealed class Slot { } }
namespace ItemStatsSystem.Stats
{
    public sealed class Modifier { }
    public sealed class Stat { internal int Removes; public void RemoveModifier(Modifier modifier) { Removes++; } }
}
public sealed class DamageInfo { }
public sealed class Health
{
    public static Action<Health, DamageInfo> OnHurt;
    public static Action<Health, DamageInfo> OnDead;
}
public sealed class CharacterMainControl : MonoBehaviour
{
    public static CharacterMainControl Main;
    public static Action<CharacterMainControl, ItemStatsSystem.Items.Slot> OnMainCharacterSlotContentChangedEvent;
    internal Vector3 Force;
    public void SetForceMoveVelocity(Vector3 value) { Force = value; }
}
public static class LevelManager { public static Action OnAfterLevelInitialized; }
namespace BossRush.Common.Equipment { public class EquipmentAbilityConfig { public virtual string LogPrefix { get { return "fixture"; } } } }
namespace BossRush
{
    internal abstract class BossRushRuntimeModuleBase
    {
        public virtual string ModuleName { get { return "fixture"; } }
        public virtual void OnAwake(ModBehaviour owner) { }
        public virtual void OnDestroy() { }
    }
    public partial class ModBehaviour : MonoBehaviour
    {
        internal WavesArenaRuntimeModule wavesArenaRuntime = new WavesArenaRuntimeModule();
        internal bool IsActive = true;
        internal const float WaveIntegrityCheckInterval = 2f;
        public static void DevLog(string value) { }
        internal static bool IsGameplaySceneName(string name) { return name == "Gameplay"; }
        internal static readonly WaitForSeconds FlightTotemSharedWait05sForRuntime = new WaitForSeconds(0.5f);
        internal static readonly WaitForSeconds ReverseScaleSharedWait05sForRuntime = new WaitForSeconds(0.5f);
        internal static readonly WaitForSeconds FrostmourneSharedWait05sForRuntime = new WaitForSeconds(0.5f);
    }
    internal sealed partial class ModeDRuntimeModule
    {
        private bool modeDActive = true;
        private ModBehaviour owner;
        internal int Fixes;
        internal ModeDRuntimeModule(ModBehaviour owner) { this.owner = owner; }
        private void TryFixStuckWaveIfNoModeDEnemyAlive() { Fixes++; }
    }
    internal sealed class EnemyPresetInfo { internal string name, displayName; }
    internal sealed partial class WavesArenaRuntimeModule
    {
        private float WaveIntegrityCheckTimer { get; set; }
        internal MonoBehaviour CurrentBoss { get; set; }
        internal int BossesPerWave { get; set; } = 1;
        internal readonly List<MonoBehaviour> CurrentWaveBosses = new List<MonoBehaviour>();
        internal List<EnemyPresetInfo> EnemyPresets = new List<EnemyPresetInfo>();
        private readonly Dictionary<CharacterMainControl, float> bossSpawnTimes = new Dictionary<CharacterMainControl, float>();
        private readonly Dictionary<CharacterMainControl, int> bossOriginalLootCounts = new Dictionary<CharacterMainControl, int>();
        internal float Clock { get { return WaveIntegrityCheckTimer; } }
        internal bool HasRecord(CharacterMainControl c, float time, int count) { return bossSpawnTimes[c] == time && bossOriginalLootCounts[c] == count; }
    }
    public sealed class FlightConfig : Common.Equipment.EquipmentAbilityConfig { public static readonly FlightConfig Instance = new FlightConfig(); }
    public sealed class ReverseScaleConfig : Common.Equipment.EquipmentAbilityConfig { public static readonly ReverseScaleConfig Instance = new ReverseScaleConfig(); }
    public class FlightAbilityManager : MonoBehaviour
    {
        public static FlightAbilityManager Instance;
        internal static int Cleans;
        public static void EnsureInstance() { if (Instance == null) Instance = new FlightAbilityManager(); }
        public static void Cleanup() { Cleans++; UnityEngine.Object.Destroy(Instance); Instance = null; }
        public void OnSceneChanged() { }
    }
    public class FlightTotemEffectManager : MonoBehaviour
    {
        public static FlightTotemEffectManager Instance;
        internal static int Checks;
        public static void EnsureInstance() { if (Instance == null) Instance = new GameObject().AddComponent<FlightTotemEffectManager>(); }
        public void CheckCurrentEquipment() { Checks++; }
    }
    public class ReverseScaleAbilityManager : MonoBehaviour
    {
        public static ReverseScaleAbilityManager Instance;
        internal static int Cleans;
        public static void EnsureInstance() { if (Instance == null) Instance = new ReverseScaleAbilityManager(); }
        public static void Cleanup() { Cleans++; UnityEngine.Object.Destroy(Instance); Instance = null; }
        public void OnSceneChanged() { }
    }
    public class ReverseScaleEffectManager : MonoBehaviour
    {
        public static ReverseScaleEffectManager Instance;
        internal static int Checks;
        public static void EnsureInstance() { if (Instance == null) Instance = new GameObject().AddComponent<ReverseScaleEffectManager>(); }
        public void CheckCurrentEquipment() { Checks++; }
    }
    internal partial class FlightTotemRuntimeModule { private void InitializeFlightTotemItem() { } private void InjectFlightTotemLocalization() { } }
    internal partial class ReverseScaleRuntimeModule { private void InitializeReverseScaleItem() { } private void InjectReverseScaleLocalization() { } }
    public class FrostmourneAbilityManager : MonoBehaviour
    {
        public static FrostmourneAbilityManager Instance;
        internal static int Cleans, Binds;
        public bool IsAbilityEnabled;
        public CharacterMainControl TargetCharacter;
        public FrostmourneAbilityManager() { Instance = this; }
        public static void CleanupStatic() { Cleans++; UnityEngine.Object.Destroy(Instance); Instance = null; }
        public void OnSceneChanged() { }
        public void RegisterAbility(CharacterMainControl target) { Binds++; TargetCharacter = target; }
        public void RebindToCharacter(CharacterMainControl target) { Binds++; TargetCharacter = target; }
    }
    internal static class FrostmourneAction { internal static int Cleans; public static void CleanupAllSummonedZombies() { Cleans++; } }
    internal static class FrostmourneBlueBossDropHandler { internal static int Cleans; public static void Cleanup() { Cleans++; } }
    internal sealed class GoblinNPCController : MonoBehaviour { }
    internal sealed class NurseNPCController : MonoBehaviour { }
    internal sealed class CourierNPCController : MonoBehaviour { }
    internal static class CourierPaidLootSweepService { internal static int Releases; public static void ReleasePendingSweepResultToPlayer(bool release, bool message) { Releases++; } }
    internal sealed class SetEyeLightState { }
    internal partial class SetBonusRuntimeModule
    {
        internal int CallbackCalls, EffectCleanups, LateEffects;
        private void OnMainCharacterSlotContentChanged(CharacterMainControl c, ItemStatsSystem.Items.Slot s) { CallbackCalls++; }
        private void OnSlotChangedForSetBonus(CharacterMainControl c, ItemStatsSystem.Items.Slot s) { CallbackCalls++; }
        private void OnLevelInitializedCheckDragonSet() { CallbackCalls++; }
        private void OnLevelInitializedCheckSetBonus() { CallbackCalls++; }
        private void OnDragonSetHurt(Health h, DamageInfo d) { CallbackCalls++; }
        private void OnFrostSetHurt(Health h, DamageInfo d) { CallbackCalls++; }
        private void OnFrostSetAnyDead(Health h, DamageInfo d) { CallbackCalls++; }
        private void OnThunderSetHurt(Health h, DamageInfo d) { CallbackCalls++; }
        private void OnThunderSetAnyDead(Health h, DamageInfo d) { CallbackCalls++; }
        private void DestroyDragonEyeEffect() { EffectCleanups++; }
        private void StopAndClearFrostFallbackSlowCoroutines() { EffectCleanups++; }
        private void BumpSetBonusGeneration() { EffectCleanups++; }
        private void DestroySetEyeLights(ref SetEyeLightState state) { state = null; EffectCleanups++; }
        private void StopFrostMist() { EffectCleanups++; }
        private void ResetFrostNovaState() { EffectCleanups++; }
        private void StopThunderAmbientArcLoop() { EffectCleanups++; }
        private void DestroySetArcPool() { EffectCleanups++; }
        private void ResetThunderChainState() { EffectCleanups++; }
        internal void Arm()
        {
            RegisterDragonSetEvents(); RegisterSetBonusEvents();
            dragonSetActive = frostSetActive = thunderSetActive = true;
            RegisterDragonHurtEvent(); RegisterFrostSetHurtEvent(); RegisterThunderSetHurtEvent();
            dragonDashCharacter = CharacterMainControl.Main; dragonDashCharacter.Force = new Vector3 { x = 9f }; isDragonDashing = true;
            StartSetBonusCoroutine(PendingEffect());
        }
        private IEnumerator PendingEffect() { yield return new WaitForSeconds(1f); LateEffects++; }
        internal bool ReferencesCleared { get { return ownedCoroutines.Count == 0 && _owner == null && cachedSlotChangedEventField == null && !slotChangedEventFieldCached && dragonDashCharacter == null; } }
    }
}

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
    private static int Count(Delegate value) { return value == null ? 0 : value.GetInvocationList().Length; }
    private static void Set(object owner, string name, object value) { owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value); }
    private static object Get(object owner, string name) { return owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    private static void SetBonus()
    {
        CharacterMainControl.Main = new CharacterMainControl();
        var host = new ModBehaviour(); var module = new SetBonusRuntimeModule(); module.OnAwake(host); module.Arm(); module.RegisterDragonSetEvents(); module.RegisterSetBonusEvents();
        Check(Count(CharacterMainControl.OnMainCharacterSlotContentChangedEvent) == 2 && Count(LevelManager.OnAfterLevelInitialized) == 2, "real registration must install exactly one delegate per owner");
        Check(Count(Health.OnHurt) == 3 && Count(Health.OnDead) == 2, "real effect subscriptions must be armed before destroy");
        module.OnDestroy();
        Check(Count(CharacterMainControl.OnMainCharacterSlotContentChangedEvent) == 0 && Count(LevelManager.OnAfterLevelInitialized) == 0 && Count(Health.OnHurt) == 0 && Count(Health.OnDead) == 0, "direct SetBonus OnDestroy must execute real unsubscribe chain");
        Check(module.ReferencesCleared && host.Coroutines.All(c => c.Stopped), "direct SetBonus OnDestroy must release coroutine and reflection owners");
        Check(CharacterMainControl.Main.Force.x == 0f, "direct SetBonus OnDestroy must release dash velocity");
        foreach (Coroutine c in host.Coroutines) c.MoveNext();
        Check(module.LateEffects == 0, "destroyed set must not apply delayed effects");
        int cleaned = module.EffectCleanups; module.OnDestroy(); Check(cleaned == module.EffectCleanups, "repeated SetBonus OnDestroy must be idempotent");
        var second = new SetBonusRuntimeModule(); second.OnAwake(host); second.Arm(); second.UnregisterDragonSetEvents(); second.UnregisterSetBonusEvents(); second.OnDestroy();
        Check(Count(Health.OnHurt) == 0 && Count(Health.OnDead) == 0, "legacy early cleanup followed by module destruction must remain safe");
    }
    private static void Equipment()
    {
        var host = new ModBehaviour(); var flight = new FlightTotemRuntimeModule(); flight.OnAwake(host); flight.InitializeFlightTotemSystem(); var effect = FlightTotemEffectManager.Instance; flight.SetupFlightTotemForScene(new Scene("Gameplay")); flight.OnDestroy(); flight.OnDestroy();
        Check(effect == null && FlightAbilityManager.Instance == null && FlightAbilityManager.Cleans == 1, "direct FlightTotem destroy must clean managers once");
        foreach (Coroutine c in host.Coroutines) c.MoveNext(); Check(FlightTotemEffectManager.Checks == 0 && host.Coroutines.All(c => c.Stopped), "FlightTotem destroy must cancel both-wait equipment check");
        host = new ModBehaviour(); var reverse = new ReverseScaleRuntimeModule(); reverse.OnAwake(host); reverse.InitializeReverseScaleSystem(); var reverseEffect = ReverseScaleEffectManager.Instance; reverse.SetupReverseScaleForScene(new Scene("Gameplay")); reverse.OnDestroy(); reverse.OnDestroy();
        Check(reverseEffect == null && ReverseScaleAbilityManager.Instance == null && ReverseScaleAbilityManager.Cleans == 1, "direct ReverseScale destroy must clean managers once");
        foreach (Coroutine c in host.Coroutines) c.MoveNext(); Check(ReverseScaleEffectManager.Checks == 0 && host.Coroutines.All(c => c.Stopped), "ReverseScale destroy must cancel pending check");
        host = new ModBehaviour(); var frost = new FrostmourneRuntimeModule(); frost.OnAwake(host); frost.InitializeFrostmourneSystem(); CharacterMainControl.Main = null; frost.SetupFrostmourneForScene(new Scene("Gameplay")); frost.OnDestroy(); frost.OnDestroy(); CharacterMainControl.Main = new CharacterMainControl();
        foreach (Coroutine c in host.Coroutines) c.MoveNext();
        Check(FrostmourneAbilityManager.Instance == null && FrostmourneAbilityManager.Cleans == 1 && FrostmourneAction.Cleans == 1 && FrostmourneBlueBossDropHandler.Cleans == 1, "direct Frostmourne destroy must execute original cleanup order once");
        Check(FrostmourneAbilityManager.Binds == 0 && host.Coroutines.All(c => c.Stopped), "Frostmourne destroy must cancel delayed player binding");
    }
    private static void Npcs()
    {
        foreach (string label in new[] { "Goblin", "Nurse", "Courier" })
        {
            Type type = typeof(ModBehaviour).Assembly.GetType("BossRush." + label + "NpcRuntimeModule");
            var module = (BossRushRuntimeModuleBase)Activator.CreateInstance(type); module.OnAwake(new ModBehaviour());
            var instance = new GameObject(); var controller = (MonoBehaviour)Activator.CreateInstance(typeof(ModBehaviour).Assembly.GetType("BossRush." + label + "NPCController")); instance.Components.Add(controller);
            Set(module, label.ToLowerInvariant() + "NPCInstance", instance); Set(module, label.ToLowerInvariant() + "Controller", controller);
            module.OnDestroy(); int released = CourierPaidLootSweepService.Releases; module.OnDestroy();
            Check(instance == null && controller == null, label + " direct destroy must destroy object and component");
            Check(Get(module, label.ToLowerInvariant() + "NPCInstance") == null && Get(module, label.ToLowerInvariant() + "Controller") == null && Get(module, "owner") == null, label + " direct destroy must clear all references");
            Check(CourierPaidLootSweepService.Releases == released, label + " repeated destroy must not repeat release side effects");
            var external = (BossRushRuntimeModuleBase)Activator.CreateInstance(type); external.OnAwake(new ModBehaviour());
            var externalObject = new GameObject(); var externalController = (MonoBehaviour)Activator.CreateInstance(typeof(ModBehaviour).Assembly.GetType("BossRush." + label + "NPCController")); externalObject.Components.Add(externalController);
            Set(external, label.ToLowerInvariant() + "NPCInstance", externalObject); Set(external, label.ToLowerInvariant() + "Controller", externalController);
            UnityEngine.Object.Destroy(externalObject); external.OnDestroy();
            Check(Get(external, label.ToLowerInvariant() + "NPCInstance") == null && Get(external, label.ToLowerInvariant() + "Controller") == null, label + " scene-destroyed references must also clear");
        }
    }
    private static void Arena()
    {
        var host = new ModBehaviour(); var arena = host.wavesArenaRuntime; var d = new ModeDRuntimeModule(host);
        Check(!arena.AdvanceWaveIntegrityCheck(1.2f), "Arena clock must retain fractional time"); d.TickModeDIntegrity(0.9f); Check(d.Fixes == 1 && arena.Clock == 0f, "D must advance and consume the same Arena clock");
        d.TickModeDIntegrity(0.7f); Check(!arena.AdvanceWaveIntegrityCheck(0.4f), "switching back must preserve shared time"); Check(arena.AdvanceWaveIntegrityCheck(0.9f) && arena.Clock == 0f, "shared threshold must reset exactly once");
        d.TickModeDIntegrity(1f); host.IsActive = false; d.TickModeDIntegrity(1f); Check(arena.Clock == 0f && d.Fixes == 1, "inactive D must reset shared clock without integrity action");
        var first = new CharacterMainControl(); var second = new CharacterMainControl(); host.RegisterArenaWaveBossFromContent(first); Check(arena.CurrentBoss == first && arena.CurrentWaveBosses.Count == 0, "single boss registration must not append multi-boss list");
        arena.BossesPerWave = 2; host.RegisterArenaWaveBossFromContent(first); host.RegisterArenaWaveBossFromContent(first); host.RegisterArenaWaveBossFromContent(second); Check(arena.CurrentBoss == second && arena.CurrentWaveBosses.SequenceEqual(new MonoBehaviour[] { first, second }), "multi-boss registration must preserve order and deduplicate");
        host.ClearArenaCurrentBossFromContent(first); Check(arena.CurrentBoss == second, "foreign cleanup must not clear current boss"); host.RemoveArenaWaveBossFromContent(first); host.ClearArenaCurrentBossFromContent(second); Check(arena.CurrentBoss == null && arena.CurrentWaveBosses.Count == 1, "separate clear/remove actions must preserve original slots");
        var preset = new EnemyPresetInfo { name = "boss" }; host.AddArenaEnemyPreset(preset); Check(ReferenceEquals(host.FindArenaEnemyPreset("boss"), preset) && host.FindArenaEnemyPreset("BOSS") == null, "preset lookup must preserve exact match and object identity");
        host.RecordArenaBossLoot(first, 42f, 5); Check(arena.HasRecord(first, 42f, 5) && host.ArenaBossLootRecordCount == 1, "content loot bookkeeping must keep exact time and count"); var copied = new HashSet<CharacterMainControl>(); host.CopyArenaTrackedBossCharactersTo(copied); copied.Clear(); Check(host.ArenaBossLootRecordCount == 1, "cleanup query must not expose owner dictionary"); host.RemoveArenaBossLootRecord(first); Check(host.ArenaBossLootRecordCount == 0, "content cleanup must remove both loot records");
    }
    public static int Main()
    {
        try { SetBonus(); Equipment(); Npcs(); Arena(); Console.WriteLine("ModuleOwnerCleanup PASS (" + checks + " checks)"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
