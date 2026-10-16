using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public string name;
        public static bool operator ==(Object a, Object b)
        { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a,b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return this == other as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static T Instantiate<T>(T value) where T : Object
        { return (T)(Object)new BossRush.CharacterRandomPreset { name = value.name }; }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value,null)) return;
            value.Destroyed = true;
            var go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (var c in go.Components) c.Destroyed = true;
        }
    }
    public class GameObject : Object
    {
        public bool activeSelf;
        public bool activeInHierarchy { get { return activeSelf && !Destroyed; } }
        internal readonly List<Component> Components = new List<Component>();
        public void SetActive(bool value) { if (Destroyed) throw new Exception("destroyed actor"); activeSelf = value; }
        public T AddComponent<T>() where T : Component, new()
        { var c = new T { gameObject = this }; Components.Add(c); return c; }
    }
    public class Component : Object { public GameObject gameObject; public readonly Transform transform = new Transform(); }
    public class Transform { public Vector3 position; }
    public class Coroutine { }
    public class MonoBehaviour : Component
    {
        public static int Starts, InactiveStarts;
        protected Coroutine StartCoroutine(IEnumerator routine)
        {
            if (gameObject == null || !gameObject.activeInHierarchy)
            { InactiveStarts++; throw new Exception("Coroutine couldn't be started because the game object is inactive"); }
            Starts++; return new Coroutine();
        }
    }
    public struct Vector3 { public static Vector3 forward { get { return new Vector3(); } } }
    public static class Time { public static float time; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle, buildIndex; }
    public static class SceneManager { public static Scene Current; public static Scene GetActiveScene() { return Current; } }
}
namespace Cysharp.Threading.Tasks
{
    public static class UniTask
    {
        public static TaskCompletionSource<bool> Deferred;
        public static Task Yield() { Deferred = new TaskCompletionSource<bool>(); return Deferred.Task; }
    }
}
namespace BossRush
{
    internal enum Teams { player, wolf }
    internal static class Team { public static bool IsEnemy(Teams a, Teams b) { return a != b; } }
    internal class DamageInfo { }
    internal class Health
    {
        public bool showHealthBar;
        public readonly Event<DamageInfo> OnDeadEvent = new Event<DamageInfo>();
        public readonly Event<DamageInfo> OnHurtEvent = new Event<DamageInfo>();
        public int BarRequests;
        public void RequestHealthBar() { BarRequests++; }
    }
    internal class Event<T> { public int Listeners; public void AddListener(Action<T> handler) { Listeners++; } }
    internal class CharacterMainControl : UnityEngine.Component
    {
        public static CharacterMainControl Main;
        public Health Health;
        public CharacterRandomPreset characterPreset;
        public Teams Team;
        public void SetTeam(Teams value) { Team = value; }
    }
    internal class CharacterRandomPreset : UnityEngine.Object
    {
        public string nameKey, DisplayName;
        public bool showName, showHealthBar;
        public Task<CharacterMainControl> Factory;
        public Task<CharacterMainControl> CreateCharacterAsync(UnityEngine.Vector3 p,UnityEngine.Vector3 d,int s,object g,bool l) { return Factory; }
    }
    internal class EnemyPresetInfo { }
    internal class ModBehaviour : UnityEngine.Object
    {
        public static ModBehaviour Instance;
        public bool IsModeEActive, IsModeFActive;
        public class OriginalWeaponData { public UnityEngine.Object bulletPrefab; public float shootSpeed; public string shootKey; }
        public static void DevLog(string value) { }
    }
    internal static class PetNestCompanionAgent { public static bool IsCompanionCharacter(CharacterMainControl c) { return false; } }
    internal static class DragonDescendantConfig { public const string BOSS_NAME_KEY = "dragon"; public const int BulletsPerRocket=10; public const float EnragedGrenadeInterval=2, NormalGrenadeInterval=5; }
    internal static class L10n { public static string T(string a,string b) { return a; } }
    internal static class SpawnedEnemyActivationHelper
    { public static int Releases; public static void ReleaseFromPlayerDistanceSleep(CharacterMainControl c) { Releases++; c.gameObject.SetActive(true); } }
    internal class BossRushAudioManager
    {
        public static BossRushAudioManager Instance;
        public int Plays, Stops;
        public void PlayBossBGM(string key,CharacterMainControl c) { Plays++; }
        public void StopBossBGM(string key,CharacterMainControl c) { Stops++; }
    }
    internal static class BossBgmKeys { public const string DragonDescendant = "dragon"; }
    internal static class BossCleanupHelpers
    { public static void DestroyRuntimePreset(CharacterMainControl c,string a,string b,string d) { UnityEngine.Object.Destroy(c.characterPreset); } }
    internal partial class DragonDescendantRuntimeModule
    {
        internal ModBehaviour owner;
        internal CharacterRandomPreset Preset;
        internal Task Equipment;
        internal CharacterMainControl dragonDescendantInstance;
        internal DragonDescendantAbilityController dragonDescendantAbilities;
        internal int Failures, Waves, Loot, Recovery, SetBonuses, CleanupCalls;
        private CharacterRandomPreset FindQuestionMarkPreset() { return Preset; }
        private CharacterRandomPreset FindFallbackPreset() { return null; }
        private void NotifyDragonDescendantSpawnFailed() { Failures++; }
        private static void DevLog(string value) { }
        private void RegisterArenaWaveBoss(CharacterMainControl c) { Waves++; }
        private void SetupBossAttributes(CharacterMainControl c) { }
        private void ApplyBossStatMultiplier(CharacterMainControl c) { }
        private ModBehaviour.OriginalWeaponData GetWeaponDataFromEquippedWeapon(CharacterMainControl c) { return new ModBehaviour.OriginalWeaponData(); }
        private Task EquipDragonDescendant(CharacterMainControl c) { return Equipment; }
        private void SetupAIAggro(CharacterMainControl c) { }
        private void StartCoroutine(IEnumerator routine) { }
        private IEnumerator DelayedBossPositionValidation(CharacterMainControl c,float d) { yield break; }
        private void RegisterEnemyRecoveryAnchor(CharacterMainControl c,UnityEngine.Vector3 p) { Recovery++; }
        private void OnDragonDescendantDeath(CharacterMainControl c,DamageInfo d) { }
        private void RegisterDragonDescendantSetBonus(CharacterMainControl c) { SetBonuses++; }
        private void ShowMessage(string value) { }
        private void RegisterBossRandomLootTracking(CharacterMainControl c,int count) { Loot++; }
        private void UnregisterDragonDescendantSetBonus(CharacterMainControl c) { CleanupCalls++; }
        private void UnregisterEnemyRecovery(CharacterMainControl c) { }
        private void ClearBossRandomLootTracking(CharacterMainControl c) { }
        private void FinalizeBossRushLootboxPathTracking(CharacterMainControl c) { }
        private void ClearArenaCurrentBoss(CharacterMainControl c) { }
        private void RemoveArenaWaveBoss(CharacterMainControl c) { }
    }
    internal partial class DragonDescendantAbilityController : UnityEngine.MonoBehaviour
    {
        private CharacterMainControl bossCharacter, playerCharacter;
        private Health bossHealth;
        private ModBehaviour.OriginalWeaponData originalWeaponData;
        private UnityEngine.Coroutine grenadeTimerCoroutine;
        internal int PrefabPreparations, ShootSubscriptions;
        internal CharacterMainControl ArenaOpponent;
        internal bool? LastAllowMainCharacter;
        internal CharacterMainControl Target { get { return playerCharacter; } }
        private CharacterMainControl ResolveArenaOpponent(bool allowMainCharacter) { LastAllowMainCharacter = allowMainCharacter; return ArenaOpponent; }
        private void OnBossHurt(DamageInfo info) { }
        private void SubscribeToShootEvent() { ShootSubscriptions++; }
        private int bulletCounter; private bool isEnraged, hasResurrected, isResurrecting, isInvulnerable;
        internal int Rockets, Grenades;
        private void LaunchRocket() { Rockets++; }
        private void ThrowIncendiaryGrenade() { Grenades++; }
        private object GetCachedWaitForSeconds(float duration) { return duration; }
        internal void Shoot(int n) { OnBossShoot(n); }
        internal IEnumerator Timer() { return GrenadeTimerCoroutine(); }
        internal bool Clamp() { return ShouldClampLethalHealthDuringHurt(); }
        private void PreCachePrefabs() { PrefabPreparations++; }
    }
}

namespace BossRush
{
    internal static class BossRushCoop
    {
        internal static bool IsConnected, IsHost;
        internal static bool IsClient { get { return IsConnected && !IsHost; } }
        internal static string RunId="room";
        internal static CharacterMainControl CombatTarget;
        internal static CharacterMainControl GetCombatTarget(UnityEngine.Vector3 origin) { return CombatTarget; }
    }
    internal static class ModeHRuntimeGates { internal static bool IsModeHRunOwnerActive; }
    // Controlled actor-ownership API boundary; actual helper has separate 32-case Harmony regression.
    internal static class CoopAiPredictionHealthProtection
    {
        internal static readonly HashSet<CharacterMainControl> Replicas=new HashSet<CharacterMainControl>();
        internal static readonly Dictionary<CharacterMainControl,string> Owners=new Dictionary<CharacterMainControl,string>();
        internal static void MarkReplica(CharacterMainControl c) { Replicas.Add(c); }
        internal static void TrackAuthority(CharacterMainControl c) { if (BossRushCoop.IsConnected && BossRushCoop.IsHost && !Owners.ContainsKey(c)) Owners[c]=BossRushCoop.RunId; }
        internal static bool HasEntityAuthority(CharacterMainControl c) { string room;return c!=null && !Replicas.Contains(c) && (Owners.TryGetValue(c,out room) ? BossRushCoop.IsConnected && BossRushCoop.IsHost && room==BossRushCoop.RunId : !BossRushCoop.IsConnected || BossRushCoop.IsHost); }
    }
}
