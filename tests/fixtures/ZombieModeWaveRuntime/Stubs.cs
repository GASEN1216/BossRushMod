using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool left = ReferenceEquals(a, null) || a.Destroyed;
            bool right = ReferenceEquals(b, null) || b.Destroyed;
            return left || right ? left == right : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            GameObject gameObject = value as GameObject;
            if (ReferenceEquals(gameObject, null)) return;
            foreach (Component component in gameObject.Components) component.Destroyed = true;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public string name { get { return gameObject.name; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public sealed class GameObject : Object
    {
        public string name;
        public bool activeSelf = true;
        public readonly Transform transform = new Transform();
        public readonly List<Component> Components = new List<Component>();
        public GameObject(string name) { this.name = name; }
        public T Add<T>(T component) where T : Component { component.gameObject = this; Components.Add(component); return component; }
        public T GetComponent<T>() where T : Component
        {
            foreach (Component component in Components) if (component is T && component != null) return (T)component;
            return null;
        }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public sealed class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
    }
    public static class Time { public static float unscaledDeltaTime, unscaledTime; }
    public static class Mathf
    {
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
    }
    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void LogWarning(object value) { Warnings.Add(value.ToString()); }
        public static void LogException(Exception error) { Warnings.Add(error.Message); }
    }
    public sealed class Coroutine : Object { internal IEnumerator Iterator; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int buildIndex; }
    public static class SceneManager
    {
        public static int ActiveBuildIndex;
        public static Scene GetActiveScene() { return new Scene { buildIndex = ActiveBuildIndex }; }
    }
}
namespace Cysharp.Threading.Tasks
{
    public struct UniTask { public void Forget() { } }
}
namespace Duckov.UI
{
    public static class NotificationText
    {
        public static readonly List<string> Messages = new List<string>();
        public static void Push(string value) { Messages.Add(value); }
    }
}
namespace BossRush
{
    public static class L10n
    {
        public static string T(string value) { return value; }
        public static string T(string chinese, string english) { return chinese; }
    }
    public sealed class CharacterMainControl : Component
    {
        public static CharacterMainControl Main;
        public bool IsMainCharacter;
        public Health Health;
        public CharacterMainControl(string name, bool player = false)
        {
            var root = new GameObject(name);
            root.Add(this);
            IsMainCharacter = player;
            Health = root.Add(new Health { Owner = this });
        }
    }
    public sealed class Health : Component
    {
        public static event Action<Health, DamageInfo> OnDead;
        public static event Action<Health, DamageInfo> OnHurt;
        public CharacterMainControl Owner;
        public bool IsDead;
        public CharacterMainControl TryGetCharacter() { return Owner; }
        public static void Reset() { OnDead = null; OnHurt = null; }
        // The tested host boundary matches official Health.Hurt: mark dead, invoke
        // the global event, then deactivate. Escaping mod exceptions skip deactivation.
        public void DeliverDeath(CharacterMainControl killer)
        {
            IsDead = true;
            if (OnDead != null) OnDead(this, new DamageInfo(killer));
            gameObject.SetActive(false);
            if (OnHurt != null) OnHurt(this, new DamageInfo(killer));
        }
    }
    public struct DamageInfo
    {
        public CharacterMainControl fromCharacter;
        public DamageInfo(CharacterMainControl value) { fromCharacter = value; }
    }
    public sealed class ZombieModeEnemyRuntimeMarker : Component
    {
        public int RunId, PurificationPointValue = 1;
        public bool IsBoss, DeathSettled, RemovedFromRuntime;
        public ZombieModeEnemyKind EnemyKind;
    }
    public sealed class ZombieModeRunState
    {
        public int RunId, SceneBuildIndex, CurrentWave, CurrentWaveKills, CurrentWaveKillTarget;
        public int CurrentWaveBossesRemaining, LivingZombieCount, LivingNormalZombieCount;
        public int PollutionFromNatural, NextSpawnPointIndex;
        public bool IsCleaningUp, BeaconChanneling, ExtractionChanneling;
        public bool ActiveSafeZoneActive, PortableSafeZoneActive, PlayerInsideSafeZone;
        public Vector3 ActiveSafeZoneCenter;
        public float ActiveSafeZoneRadius, PreparationTimer, PeriodicSpawnTimer;
        public float BeaconChannelStartTime, BeaconChannelDuration;
        public ZombieModeLifecyclePhase LifecyclePhase;
        public ZombieModeCombatPhase CombatPhase;
        public readonly List<object> CurrentWaveBossInstances = new List<object>();
    }
    public sealed class ModBehaviour
    {
        internal ZombieModeRuntimeModule Module;
        internal readonly HashSet<string> Faults = new HashSet<string>();
        internal readonly Dictionary<string, int> Calls = new Dictionary<string, int>();
        internal readonly List<Coroutine> Coroutines = new List<Coroutine>();
        internal readonly List<Exception> CoroutineErrors = new List<Exception>();
        internal readonly List<Action> Cleanup = new List<Action>();
        internal Action<string> OnStage;
        internal bool PendingStars, Paused, ReturnNullCoroutine;
        public static void DevLog(string value) { Debug.Warnings.Add(value); }
        internal int Count(string stage) { int count; return Calls.TryGetValue(stage, out count) ? count : 0; }
        internal void Stage(string stage)
        {
            Calls[stage] = Count(stage) + 1;
            if (OnStage != null) OnStage(stage);
            if (Faults.Contains(stage)) throw new InvalidOperationException("injected " + stage);
        }
        public void ShowBigBanner(string value)
        {
            Stage(value.Contains("WaveCleared") ? "clear-banner" : "banner");
        }
        public Coroutine StartZombieModeCoroutineForRuntimeModule(IEnumerator routine, int runId)
        {
            Stage("start-coroutine");
            if (ReturnNullCoroutine) return null;
            var coroutine = new Coroutine { Iterator = routine };
            Coroutines.Add(coroutine);
            Step(coroutine);
            return coroutine;
        }
        internal void Step(Coroutine coroutine)
        {
            if (coroutine == null || coroutine.Destroyed) return;
            bool running;
            try { running = coroutine.Iterator.MoveNext(); }
            catch
            {
                coroutine.Destroyed = true;
                IDisposable failed = coroutine.Iterator as IDisposable;
                if (failed != null) failed.Dispose();
                throw;
            }
            if (!running)
            {
                coroutine.Destroyed = true;
                IDisposable disposable = coroutine.Iterator as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }
        internal void Frame(float delta)
        {
            Time.unscaledDeltaTime = delta;
            Time.unscaledTime += delta;
            foreach (Coroutine coroutine in Coroutines.ToArray())
            {
                try { Step(coroutine); }
                catch (Exception error) { CoroutineErrors.Add(error); }
            }
        }
        public void StartZombieModeWaveForRuntimeModule(int runId) { Module.StartZombieModeWave(runId); }
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        private readonly ModBehaviour owner;
        private readonly ZombieModeRunState runState;
        private Action<Health, DamageInfo> zombieModeOnDeadHandler, zombieModeOnHurtHandler;
        private readonly Dictionary<CharacterMainControl, ZombieModeEnemyRuntimeMarker> markers = new Dictionary<CharacterMainControl, ZombieModeEnemyRuntimeMarker>();
        internal ZombieModeRuntimeModule(ModBehaviour owner, ZombieModeRunState state) { this.owner = owner; runState = state; owner.Module = this; }
        internal ZombieModeEnemyRuntimeMarker Track(CharacterMainControl enemy, ZombieModeEnemyKind kind = ZombieModeEnemyKind.Normal, bool boss = false)
        {
            var marker = enemy.gameObject.Add(new ZombieModeEnemyRuntimeMarker { RunId = runState.RunId, EnemyKind = kind, IsBoss = boss });
            markers[enemy] = marker;
            runState.LivingZombieCount++;
            if (!boss) runState.LivingNormalZombieCount++;
            return marker;
        }
        private bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl enemy, out ZombieModeEnemyRuntimeMarker marker) { return markers.TryGetValue(enemy, out marker); }
        private void UnregisterZombieModeEnemyInstanceId(CharacterMainControl enemy) { markers.Remove(enemy); }
        private bool IsZombieModeRuntimePaused() { return owner.Paused; }
        private void RegisterZombieModeRunOnlyObject(int id, ZombieModeRunOnlyObjectKind kind, object root, object target, Action cleanup) { if (cleanup != null) owner.Cleanup.Add(cleanup); }
        private void HandleZombieModeHealthHurt(int id, Health health, DamageInfo damage) { }
        private void FailZombieModeActive(int id) { owner.Stage("fail-run"); runState.CombatPhase = ZombieModeCombatPhase.FailedExit; }
        private void TryHandleZombieModeSafeZonePlayerAttack(int id, DamageInfo damage, CharacterMainControl victim) { owner.Stage("safe-zone-attack"); }
        private void HandleZombieModeOptionHealthDead(int id, Health health, DamageInfo damage, CharacterMainControl victim, ZombieModeEnemyRuntimeMarker marker) { owner.Stage("option-death"); }
        private int GetZombieModeDeathStarCount(ZombieModeEnemyRuntimeMarker marker) { return 1; }
        private void SpawnZombieModeDeathStars(int id, Vector3 position, int points, int count) { owner.Stage("death-stars"); }
        private void HandleZombieModeBossDefeated(int id, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl victim) { runState.CurrentWaveBossesRemaining = Math.Max(0, runState.CurrentWaveBossesRemaining - 1); }
        private void HandleZombieModeBossDeathEffects(int id, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl victim) { owner.Stage("boss-effect"); }
        private void TrySpawnZombieModeBossDrop(int id, ZombieModeEnemyRuntimeMarker marker, Vector3 position) { owner.Stage("boss-drop"); }
        private void HandleZombieModeEliteDeathEffects(int id, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl victim) { owner.Stage("elite-effect"); }
        private void HandleZombieModeSpecialDeathEffects(int id, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl victim) { owner.Stage("special-effect"); }
        private void TrySpawnZombieModeEnemyDrop(int id, ZombieModeEnemyRuntimeMarker marker, Vector3 position) { owner.Stage("enemy-drop"); }
        private void PruneZombieModeRunOnlyEnemyRecords(int id) { owner.Stage("prune-records"); }
        private void TickZombieModeSafeZone()
        {
            owner.Stage("safe-zone-tick");
            runState.PlayerInsideSafeZone = IsZombieModePlayerInsideActiveSafeZone();
        }
        internal bool IsZombieModePlayerInsideActiveSafeZone()
        { return AnyZombieModeSafeZoneActive && (CharacterMainControl.Main.transform.position - runState.ActiveSafeZoneCenter).sqrMagnitude <= runState.ActiveSafeZoneRadius * runState.ActiveSafeZoneRadius; }
        private void TickZombieModeAmbientZombiePressure(int id, float delta) { owner.Stage("ambient-pressure"); }
        private void CleanupZombieModePreparationObjects(int id)
        { owner.Stage("cleanup-preparation"); runState.ActiveSafeZoneActive = runState.PortableSafeZoneActive = false; }
        private void CleanupZombieModeExpiredDropCandidates(bool all) { owner.Stage("cleanup-drops"); }
        private void ReleaseZombieModeSafeZoneThreatSuppression() { owner.Stage("release-threat"); }
        private void SpawnPendingZombieModeEliteSquad(int id) { owner.Stage("spawn-elite-squad"); }
        private int GetZombieModeBossCountForWave(int wave) { return 1; }
        private Cysharp.Threading.Tasks.UniTask SpawnZombieModeBossWaveAsync(int id, int count) { owner.Stage("spawn-boss-wave"); return new Cysharp.Threading.Tasks.UniTask(); }
        private int GetZombieModeBaseWaveKillTarget() { return 10; }
        private void CleanupZombieModeEnemiesNearPlayerSafeZone(int id, string reason) { owner.Stage("cleanup-enemies"); }
        private void RecycleZombieModeTemporaryNpcs(int id) { owner.Stage("recycle-npcs"); }
        private void RecycleZombieModeTemporaryRealNpcs(int id) { owner.Stage("recycle-real-npcs"); }
        private bool TryGiveZombieModeWaveClearHealingItem() { owner.Stage("clear-healing"); return true; }
        private bool HasZombieModePendingPurificationStars() { return owner.PendingStars; }
        private void ForceCollectZombieModePendingPurificationStars(int id) { owner.Stage("collect-stars"); owner.PendingStars = false; }
        private void ShowZombieModeRewardSelection(int id, bool boss)
        { owner.Stage("reward-selection"); runState.CombatPhase = ZombieModeCombatPhase.RewardSelection; }
    }
}
