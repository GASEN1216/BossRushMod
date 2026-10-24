using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public enum Teams { all, player, wolf }
public static class Team { public static bool IsEnemy(Teams a, Teams b) { return a != Teams.all && b != Teams.all && a != b; } }
public enum DamageTypes { normal }
public sealed class DamageInfo
{
    public CharacterMainControl fromCharacter;
    public DamageReceiver toDamageReceiver;
    public float damageValue, finalDamage, critRate, critDamageFactor = 1f;
    public int crit;
    public DamageTypes damageType;
    public Vector3 damagePoint, damageNormal;
    public bool isExplosion;
    public DamageInfo() { }
    public DamageInfo(CharacterMainControl source) { fromCharacter = source; }
}
public sealed class LevelManager : MonoBehaviour
{
    public static LevelManager Instance;
    public static bool AfterInit;
    public CharacterMainControl MainCharacter, ControllingCharacter;
    public bool IsBaseLevel;
}
public static class SceneLoader { public static bool IsSceneLoading; }
public sealed class Health : MonoBehaviour
{
    public UnityEvent<DamageInfo> OnDeadEvent = new UnityEvent<DamageInfo>();
    public Teams team;
    public bool IsDead, IsMainCharacterHealth, showHealthBar;
    public float CurrentHealth = 1000f;
    public float MaxHealth { get { return GetComponent<CharacterMainControl>().CharacterItem.MaxHealth.BaseValue; } }
    internal int HurtCount;
    internal float TotalDamage;
    public void SetHealth(float value) { CurrentHealth = Math.Min(value, MaxHealth); }
    public bool Hurt(DamageInfo info)
    {
        HurtCount++;
        TotalDamage += info.damageValue;
        CurrentHealth -= info.damageValue;
        if (CurrentHealth <= 0 && !IsDead) Kill(info);
        return true;
    }
    public void Kill(DamageInfo info) { IsDead = true; CurrentHealth = 0f; OnDeadEvent.Invoke(info); }
    public CharacterMainControl TryGetCharacter() { return GetComponent<CharacterMainControl>(); }
    public void RequestHealthBar() { }
}
public sealed class CharacterMainControl : MonoBehaviour
{
    public static CharacterMainControl Main;
    public Health Health;
    public DamageReceiver mainDamageReceiver;
    public ItemStatsSystem.Item CharacterItem;
    public CharacterRandomPreset characterPreset;
    public Teams Team;
    public bool isBossCharacter, dropBoxOnDead, isVehicle, protectVehicleDriver;
    public object CurrentHoldItemAgent;
    public ActionStub CurrentAction = new ActionStub();
    public bool IsMainCharacter { get { return this == Main; } }
    internal Vector3 LastMove;
    internal int MoveWrites, CancelSkills, LootBoxes;
    internal ItemStatsSystem.Items.Slot Primary, Secondary, Melee;
    private Action<DamageInfo> beforeLoot;
    public event Action<DamageInfo> BeforeCharacterSpawnLootOnDead { add { beforeLoot += value; } remove { beforeLoot -= value; } }
    internal int BeforeLootListeners { get { return beforeLoot == null ? 0 : beforeLoot.GetInvocationList().Length; } }
    internal static CharacterMainControl Create(string name, Teams team)
    {
        var go = new GameObject(name);
        var character = go.AddComponent<CharacterMainControl>();
        character.CharacterItem = ItemStatsSystem.Item.Create(0);
        character.Health = go.AddComponent<Health>();
        character.SetTeam(team);
        character.mainDamageReceiver = go.AddComponent<DamageReceiver>();
        character.mainDamageReceiver.health = character.Health;
        character.mainDamageReceiver.OnHurtEvent = new UnityEvent<DamageInfo>();
        character.mainDamageReceiver.OnDeadEvent = new UnityEvent<DamageInfo>();
        go.AddComponent<Collider>();
        character.Health.OnDeadEvent.AddListener(character.VanillaDeath);
        return character;
    }
    public void SetTeam(Teams value) { Team = value; if (Health != null) Health.team = value; }
    public void SetMoveInput(Vector3 value) { LastMove = value; MoveWrites++; }
    public void CancleSkill() { CancelSkills++; }
    public void Trigger(bool a, bool b, bool c) { }
    public void ChangeHoldItem(object value) { CurrentHoldItemAgent = value; }
    public ItemStatsSystem.Items.Slot PrimWeaponSlot() { return Primary; }
    public ItemStatsSystem.Items.Slot SecWeaponSlot() { return Secondary; }
    public ItemStatsSystem.Items.Slot MeleeWeaponSlot() { return Melee; }
    public void AddBuff(Duckov.Buffs.Buff value, CharacterMainControl source, int level) { }
    internal void DispatchBeforeLoot(DamageInfo info) { if (beforeLoot != null) beforeLoot(info); }
    private void VanillaDeath(DamageInfo info)
    {
        BossRush.BossRushCharacterOnDeadPatch.Prefix(this);
        if (info.fromCharacter != null && info.fromCharacter.IsMainCharacter && characterPreset != null && !string.IsNullOrEmpty(characterPreset.nameKey))
            SavesCounter.Keys.Add(characterPreset.nameKey);
        DispatchBeforeLoot(info);
        if (dropBoxOnDead) LootBoxes++;
    }
}
public static class SavesCounter { internal static readonly List<string> Keys = new List<string>(); }
public sealed class ActionStub { internal int Stops; public void StopAction() { Stops++; } }
public sealed class CharacterRandomPreset : UnityEngine.Object
{
    public string nameKey;
    public bool isBoss, showName, showHealthBar, hasSkill, defaultWeaponOut;
    public object skillPfb;
    internal static readonly List<CharacterRandomPreset> Clones = new List<CharacterRandomPreset>();
    internal static readonly Queue<TaskCompletionSource<CharacterMainControl>> Pending = new Queue<TaskCompletionSource<CharacterMainControl>>();
    internal static readonly Dictionary<TaskCompletionSource<CharacterMainControl>, int> PendingScenes = new Dictionary<TaskCompletionSource<CharacterMainControl>, int>();
    internal static bool FailCreate;
    internal static int CreateCalls;
    internal static readonly List<int> RequestedScenes = new List<int>();
    internal CharacterRandomPreset Clone()
    {
        var clone = new CharacterRandomPreset { nameKey = nameKey, hasSkill = hasSkill, skillPfb = skillPfb, defaultWeaponOut = defaultWeaponOut };
        Clones.Add(clone);
        return clone;
    }
    public Task<CharacterMainControl> CreateCharacterAsync(Vector3 position, Vector3 forward, int scene, object extra, bool flag)
    {
        CreateCalls++;
        RequestedScenes.Add(scene);
        if (FailCreate) return Task.FromResult<CharacterMainControl>(null);
        var completion = new TaskCompletionSource<CharacterMainControl>();
        PendingScenes.Add(completion, scene);
        Pending.Enqueue(completion);
        return completion.Task;
    }
    internal static CharacterMainControl CompleteCharacter()
    { return CompleteCharacter(Pending.Dequeue()); }
    internal static CharacterMainControl CompleteCharacter(TaskCompletionSource<CharacterMainControl> completion)
    {
        CharacterMainControl character = CharacterMainControl.Create("vanilla", Teams.all);
        Scene target = SceneManager.GetSceneByBuildIndex(PendingScenes[completion]);
        if (target.IsValid() && target.isLoaded) SceneManager.MoveGameObjectToScene(character.gameObject, target);
        // 官方 CreateCharacterAsync 把角色放进主关卡的 active-with-scene 容器；生产必须显式解除这个父级再绑定。
        if (LevelManager.Instance != null && LevelManager.Instance.gameObject.scene != target)
        {
            SceneManager.MoveGameObjectToScene(character.gameObject, LevelManager.Instance.gameObject.scene);
            character.transform.SetParent(LevelManager.Instance.transform);
        }
        character.Primary = new ItemStatsSystem.Items.Slot { Content = ItemStatsSystem.Item.Create(11) };
        character.Secondary = new ItemStatsSystem.Items.Slot { Content = ItemStatsSystem.Item.Create(12) };
        character.Melee = new ItemStatsSystem.Items.Slot { Content = ItemStatsSystem.Item.Create(13) };
        var aiGo = new GameObject("vanilla AI"); aiGo.transform.SetParent(character.transform);
        aiGo.AddComponent<AICharacterController>();
        aiGo.AddComponent<NodeCanvas.BehaviourTrees.BehaviourTreeOwner>();
        completion.SetResult(character);
        return character;
    }
}
public sealed class AICharacterController : MonoBehaviour
{
    public bool defaultWeaponOut = true, hasSkill = true;
    public TreeStub patrolTree = new TreeStub(), alertTree = new TreeStub(), combatTree = new TreeStub(), combat_Attack_Tree = new TreeStub();
    public void StopMove() { }
}
public sealed class TreeStub { internal bool Stopped; public void Stop(bool value) { Stopped = value; } }
public enum CharacterIconTypes { boss }
namespace NodeCanvas.BehaviourTrees
{
    public sealed class BehaviourTreeOwner : MonoBehaviour { public bool Stopped; public void StopBehaviour(bool value) { Stopped = value; } }
}
namespace Duckov.Buffs { public sealed class Buff { } }
namespace Duckov.Utilities { public static class GameplayDataSettings { public static class Layers { public static int wallLayerMask = 1, groundLayerMask = 2; } } }

namespace ItemStatsSystem.Stats
{
    public enum ModifierType { PercentageMultiply }
    public sealed class Modifier
    {
        internal ItemStatsSystem.Stat Target;
        internal bool Removed;
        public Modifier(ModifierType type, float value, bool overwrite, int order, object source) { }
        public void RemoveFromTarget() { Removed = true; if (Target != null) Target.Modifiers.Remove(this); Target = null; }
    }
}
namespace ItemStatsSystem
{
    public sealed class Stat
    {
        public float BaseValue = 1000f;
        internal readonly List<Modifier> Modifiers = new List<Modifier>();
        public void AddModifier(Modifier modifier) { modifier.Target = this; Modifiers.Add(modifier); }
    }
    public sealed class Inventory
    {
        public readonly List<Item> Content = new List<Item>();
        public int Capacity;
        internal bool Reject, Throw;
        internal int AddCalls;
        public void SetCapacity(int value) { Capacity = value; }
        public bool AddAndMerge(Item item, int index)
        {
            AddCalls++;
            if (Throw) throw new InvalidOperationException("inventory add injected failure");
            if (Reject) return false;
            Content.Add(item);
            return true;
        }
    }
    public sealed class Item : MonoBehaviour
    {
        public int TypeID;
        public float Durability, MaxDurability = 100f;
        public Inventory Inventory = new Inventory();
        public Stat MaxHealth = new Stat();
        public Stat BodyArmor = new Stat { BaseValue = 5f }, HeadArmor = new Stat { BaseValue = 4f };
        internal int DropCalls;
        internal bool DropThrows;
        internal static readonly List<Item> Created = new List<Item>();
        internal static Item Create(int typeId)
        {
            var item = new GameObject("Item_" + typeId).AddComponent<Item>(); item.TypeID = typeId;
            Created.Add(item); return item;
        }
        public Stat GetStat(string key) { return key == "MaxHealth" ? MaxHealth : key == "BodyArmor" ? BodyArmor : key == "HeadArmor" ? HeadArmor : null; }
        public void Drop(Vector3 position, bool a, Vector3 forward, float spin)
        {
            if (DropThrows) throw new InvalidOperationException("world drop injected failure");
            DropCalls++; transform.position = position;
        }
        public void DestroyTree() { UnityEngine.Object.Destroy(gameObject); }
    }
    public static class ItemAssetsCollection
    {
        internal static bool Missing;
        internal static int OverrideType;
        public static Item InstantiateSync(int typeId) { return Missing ? null : Item.Create(OverrideType == 0 ? typeId : OverrideType); }
    }
}
namespace ItemStatsSystem.Items
{
    public sealed class Slot
    {
        public ItemStatsSystem.Item Content;
        public ItemStatsSystem.Item Unplug() { var item = Content; Content = null; return item; }
    }
}

namespace BossRush
{
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour host) { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float delta, float unscaled) { }
        public virtual void OnDestroy() { }
    }
    internal struct SceneRuntimeContext
    {
        internal Scene Scene;
        internal string SceneName;
        internal SceneRuntimeContext(Scene scene, LoadSceneMode mode) { Scene = scene; SceneName = scene.name; }
    }
    internal enum ZombieModeLifecyclePhase { None, Active }
    internal sealed class ZombieRun { internal ZombieModeLifecyclePhase LifecyclePhase; }
    internal enum ModeHLifecycle { None, Active }
    internal sealed class ModeHRun { internal ModeHLifecycle Lifecycle; }
    internal sealed class ModeHModule { internal ModeHRun RunState; }
    public sealed class ModBehaviour : MonoBehaviour
    {
        public static ModBehaviour Instance;
        internal bool IsActive, IsCampaignFinalBossActive, CampaignModeDActiveForRuntime, CampaignModeEActiveForRuntime;
        internal bool CampaignModeFActiveForRuntime, CampaignModeGActiveForRuntime;
        internal bool CampaignArenaActiveForRuntime, ValidArena = true;
        internal static bool MapRegistered = true;
        internal static object GetMapConfigBySceneName(string name)
        { return MapRegistered && name == "Level_SnowMilitaryBase" ? new object() : null; }
        internal ZombieRun CampaignZombieRunForRuntime;
        internal ModeHModule ModeHRuntime;
        internal BossRushSignInteractable CampaignArenaSignForRuntime;
        internal readonly List<string> Messages = new List<string>();
        internal int MultiplierCalls;
        internal Vector3[] SpawnPoints;
        internal bool IsModeEActive { get { return CampaignModeEActiveForRuntime; } }
        internal bool IsModeFActive { get { return CampaignModeFActiveForRuntime; } }
        internal bool HasArenaEnemyPresetCatalog = true;
        internal readonly List<EnemyPresetInfo> PresetCatalog = new List<EnemyPresetInfo>();
        internal readonly List<CharacterMainControl> WaveBosses = new List<CharacterMainControl>();
        internal CharacterMainControl CurrentBoss;
        internal EnemyPresetInfo FindArenaEnemyPreset(string key) { return PresetCatalog.Find(value => value.name == key); }
        internal void AddArenaEnemyPreset(EnemyPresetInfo preset) { PresetCatalog.Add(preset); }
        internal void RegisterArenaWaveBossFromContent(CharacterMainControl character) { WaveBosses.Add(character); CurrentBoss = character; }
        internal void ClearArenaCurrentBossFromContent(CharacterMainControl character) { if (ReferenceEquals(CurrentBoss, character)) CurrentBoss = null; }
        internal void RemoveArenaWaveBossFromContent(CharacterMainControl character) { WaveBosses.Remove(character); }
        public static void DevLog(string message) { }
        internal void ApplyCampaignBossStatMultiplierForRuntime(CharacterMainControl boss) { MultiplierCalls++; }
        internal bool IsCurrentSceneValidBossRushArena() { return ValidArena; }
        internal Vector3[] GetCurrentSceneSpawnPoints() { return SpawnPoints; }
        internal void ShowMessage(string message) { Messages.Add(message); }
    }
    internal sealed class BossRushSignInteractable : MonoBehaviour { }
    internal sealed class EnemyPresetInfo
    {
        internal string name, displayName;
        internal int team, expReward;
        internal float baseHealth, baseDamage, healthMultiplier, damageMultiplier;
    }
    internal enum BossRushPendingEntryKind { None, ModeG, ModeH, Zombie }
    internal static class BossRushMapSelectionHelper
    {
        internal static BossRushPendingEntryKind Pending;
        internal static BossRushPendingEntryKind GetPendingEntryKind() { return Pending; }
    }
    internal sealed class WavesArenaRuntimeModule
    { internal static bool ArenaPlanned, ArenaActive; }
    internal static class EclipseBloomIntroduction
    {
        internal static bool CanSpawnEncounter = true;
        internal static int Defeats, Spawns;
        internal static void NotifyDefeated() { Defeats++; }
        internal static void NotifyEncounterSpawned() { Spawns++; }
    }
    internal sealed class BossAIController
    {
        internal static int Pauses;
        internal BossAIController(CharacterMainControl boss, string name) { }
        internal void Pause() { Pauses++; }
    }
    internal static class BossRushEagerReflectionCache { internal static FieldInfo CharacterRandomPreset_CharacterIconType; }
    internal static class BossSkillDamageRules
    {
        internal static float ReadMeleeDamageStatBase(CharacterMainControl boss) { return 1f; }
        internal static float ResolveMeleeDamageScale(CharacterMainControl boss, float baseline) { return 1f; }
    }
    internal static class SpawnedEnemyActivationHelper
    { internal static int Releases; internal static void ReleaseFromPlayerDistanceSleep(CharacterMainControl boss) { Releases++; } }
    internal static class BossRushUI { internal static bool Paused; internal static bool IsGamePaused() { return Paused; } }
    internal sealed class BossRushAudioManager
    {
        internal static BossRushAudioManager Instance = new BossRushAudioManager();
        internal int Starts, Stops;
        internal readonly List<string> StartKeys = new List<string>(), StopKeys = new List<string>();
        internal readonly List<CharacterMainControl> StartOwners = new List<CharacterMainControl>(), StopOwners = new List<CharacterMainControl>();
        internal void PlayBossBGM(string key, CharacterMainControl boss)
        { Starts++; StartKeys.Add(key); StartOwners.Add(boss); }
        internal void StopBossBGM(string key, CharacterMainControl boss)
        { Stops++; StopKeys.Add(key); StopOwners.Add(boss); }
    }
    internal static class BossBgmKeys
    {
        internal const string PhantomWitch = "PhantomWitch";
        internal const string EclipseBloom = "EclipseBloom";
    }
    internal static class BossRushItemIds { internal const int ResidualSunShield = 500110; }
    internal static class BossRushDynamicItemRegistry { internal static void EnsureRegistered(int id) { } }
    internal static class ResidualSunShieldWeaponConfig
    { internal static bool Fails; internal static bool TryConfigure(ItemStatsSystem.Item item) { return !Fails; } }
    internal static class CodexKillCollector
    { internal static int Calls; internal static void OnKnownBossDead(Health health, DamageInfo info, string key) { Calls++; } }
    internal static class L10n { internal static string T(string cn, string en) { return en; } }
    internal static class SpawnPositionHelper
    {
        internal const float DefaultLiftOffset = 0f;
        internal static bool Reject;
        internal static bool TryFindAroundPlayer(Vector3 center, int attempts, float radius, out Vector3 result, float a, float b, float c)
        { result = center + new Vector3(4, 0, 0); return !Reject; }
        internal static bool TryResolveReachableFrom(Vector3 raw, Vector3 from, float radius, float lift, out Vector3 result)
        { result = raw; return !Reject; }
    }
    internal static class EclipseBloomAssetManager
    { internal static void BuildAltar(GameObject altar) { } }
    internal sealed class EclipseBloomInteractable : MonoBehaviour
    { internal EclipseBloomRuntimeModule Module; internal void Bind(EclipseBloomRuntimeModule module) { Module = module; } }
    internal sealed class EclipseBloomBody : MonoBehaviour
    {
        internal static bool FailAttach;
        internal static readonly List<EclipseBloomBody> Created = new List<EclipseBloomBody>();
        internal CharacterMainControl Boss;
        internal int Frames, Phase, IntactMask, AttackMask, Breaks, Dissipations;
        internal float Rotation;
        internal float PetalScaleFactor = 0.1f;
        internal bool Damaging;
        internal bool GroundWarning = true;
        internal bool HasGroundWarningAt(Vector3 point) { return GroundWarning; }
        internal static EclipseBloomBody Attach(CharacterMainControl boss)
        {
            if (FailAttach) return null;
            var body = new GameObject("Body").AddComponent<EclipseBloomBody>(); body.Boss = boss;
            Created.Add(body); return body;
        }
        internal Vector3 GetPetalPosition(int index)
        {
            float x, z;
            EclipseBloomRules.GetSectorPoint(index, Rotation, EclipseBloomTuning.PetalOrbitRadius, 0f, out x, out z);
            return Boss.transform.position + new Vector3(x, EclipseBloomTuning.PetalCenterHeight, z);
        }
        internal Quaternion GetPetalRotation(int index) { return new Quaternion { y = index + 10 }; }
        internal Vector3 GetPetalScale(int index) { return Vector3.one * (PetalScaleFactor * (index + 1)); }
        internal Vector3 GetCorePosition() { return Boss.transform.position + Vector3.up * 1.4f; }
        internal float GetCoreRadius() { return 1.37f; }
        internal void ApplyFrame(int phase, int intactMask, float rotation, int attackMask, float progress, bool damage)
        { Frames++; Phase = phase; IntactMask = intactMask; Rotation = rotation; AttackMask = attackMask; Damaging = damage; }
        internal void PlayPetalBreak(int index) { Breaks++; }
        internal int CoreHits, PetalHits;
        internal void PlayCoreHit(Vector3 point) { CoreHits++; }
        internal void PlayPetalHit(int index, Vector3 point) { PetalHits++; }
        internal void Dissipate() { Dissipations++; UnityEngine.Object.Destroy(gameObject); }
    }
    internal static class ExtraDrops { internal static int Calls; }
    // 日珥只记录落点与生命周期；真实地面射线、预警与落地表现由 Unity 侧另验。
    internal sealed class EclipseBloomSolarFlares
    {
        internal static readonly List<Vector3> Launches = new List<Vector3>();
        internal static int Disposed;
        internal int Ticks;
        internal static EclipseBloomSolarFlares Create(CharacterMainControl owner) { return owner == null ? null : new EclipseBloomSolarFlares(); }
        internal bool Launch(Vector3 target, float radius, float warning, float damage) { Launches.Add(target); return true; }
        internal void Tick(float deltaTime, CharacterMainControl player) { Ticks++; }
        internal void Dispose() { Disposed++; }
    }
    internal static class SandstormChampionMinionMarker { internal static bool IsSummonedMinion(CharacterMainControl character) { return false; } }
    internal static class ModeGRuntimeGates
    {
        internal static bool IsModeGSuppressionActive { get { return false; } }
        internal static bool IsModeGOnDeadSuppressionActive(Health health) { return false; }
    }
    internal static class ModeHDeathSuppressionRegistry
    {
        internal static bool IsSuppressionArmed { get { return false; } }
        internal static bool IsModeHOnDeadSuppressionActive(Health health) { return false; }
    }
    internal static class PetNestDeathSuppressionRegistry
    {
        internal static bool IsSuppressionArmed { get { return false; } }
        internal static bool IsPetNestOnDeadSuppressionActive(Health health) { return false; }
    }
    internal static class FrostmourneBlueBossDropHandler { internal static void TryHandleBlueBossDeath(CharacterMainControl character) { ExtraDrops.Calls++; } }
    internal static class PhantomWitchScytheBossDropHandler { internal static void TryHandlePhantomWitchDeath(CharacterMainControl character) { ExtraDrops.Calls++; } }
    internal static class SetBonusBossDropHandler { internal static void TryHandleSetBonusBossDeath(CharacterMainControl character) { ExtraDrops.Calls++; } }
    internal static class NewWeaponBossDropHandler { internal static void TryHandleNewWeaponBossDeath(CharacterMainControl character) { ExtraDrops.Calls++; } }
}

namespace Duckov.Scenes
{
    public sealed class MultiSceneCore : MonoBehaviour
    { public static MultiSceneCore Instance; public static Scene? ActiveSubScene; public bool IsLoading; }
}

namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public sealed class HarmonyPrefix : Attribute { }
}
