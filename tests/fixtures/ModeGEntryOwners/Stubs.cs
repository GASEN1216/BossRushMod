using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ItemStatsSystem;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public string name;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.destroyed;
            bool bn = ReferenceEquals(b, null) || b.destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object component in go.components) component.destroyed = true;
        }
        public static T Instantiate<T>(T original) where T : Object
        {
            CharacterRandomPreset preset = original as CharacterRandomPreset;
            if (preset == null) throw new InvalidOperationException("Unsupported fixture clone");
            return (T)(Object)preset.Clone();
        }
        public static T FindObjectOfType<T>() where T : Object { return BossRush.Harness.Sign as T; }
    }
    public class GameObject : Object
    {
        internal readonly List<Object> components = new List<Object>();
        public bool activeSelf = true;
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class Transform { public Vector3 position; }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
    }
    public static class Mathf
    {
        public const float Deg2Rad = (float)Math.PI / 180f;
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Abs(int a) { return Math.Abs(a); }
        public static float Cos(float a) { return (float)Math.Cos(a); }
        public static float Sin(float a) { return (float)Math.Sin(a); }
    }
    public enum KeyCode { None = 0, F4 = 285 }
    public static class Input
    {
        public static bool Pressed;
        public static bool GetKeyDown(KeyCode key) { BossRush.Harness.Events.Add("key"); return Pressed; }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager
    {
        public static string Current = "Arena";
        public static Scene GetActiveScene() { return new Scene { name = Current }; }
    }
}
public enum Teams { middle, wolf }
public struct DamageInfo { }
public class CharacterRandomPreset : UnityEngine.Object
{
    public string nameKey;
    public Teams team;
    public bool dropBoxOnDead, setActiveByPlayerDistance, canDieIfNotRaidMap;
    public int exp;
    public CharacterRandomPreset Clone() { return (CharacterRandomPreset)MemberwiseClone(); }
}
public class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main;
    public Health Health;
    public GameObject gameObject = new GameObject();
    public Transform transform = new Transform();
    public Item CharacterItem = new Item();
    public CharacterRandomPreset characterPreset;
    public CharacterMainControl()
    {
        Health = new Health { Character = this, CanDieIfNotRaidMap = true };
        gameObject.components.Add(this);
        gameObject.components.Add(Health);
    }
    public int GetInstanceID() { return GetHashCode(); }
}
public class Health : UnityEngine.Object
{
    public bool IsDead, CanDieIfNotRaidMap, Invincible;
    public CharacterMainControl Character;
    public void SetInvincible(bool value) { Invincible = value; }
    public CharacterMainControl TryGetCharacter() { return Character; }
}
namespace ItemStatsSystem
{
    public class Item : UnityEngine.Object
    {
        public int TypeID;
        public Inventory Inventory = new Inventory();
        public int Exp;
        public void SetInt(string key, int value, bool persistent) { Exp = value; }
        public void DestroyTree() { UnityEngine.Object.Destroy(this); }
    }
    public class Inventory { }
    public struct ItemMetaData { public int id, quality, priceEach, defaultStackCount; }
    public static class ItemAssetsCollection
    {
        public static Item GetPrefab(int id) { return new Item { TypeID = id }; }
        public static Item InstantiateSync(int id) { return new Item { TypeID = id }; }
        public static ItemMetaData GetMetaData(int id) { return new ItemMetaData { id = id, quality = 6, priceEach = 17, defaultStackCount = 2 }; }
    }
}
namespace HarmonyLib { public static class AccessTools { public static Type TypeByName(string name) { return null; } } }
namespace BossRush
{
    internal static class Harness
    {
        internal static readonly List<string> Events = new List<string>();
        internal static readonly Dictionary<int, int> Refunds = new Dictionary<int, int>();
        internal static bool InitOk, StartOk, HudThrow, RefundOk;
        internal static BossRushSignInteractable Sign;
        internal static void Refund(int id)
        {
            int value; Refunds.TryGetValue(id, out value); Refunds[id] = value + 1;
            Events.Add("refund:" + id);
        }
        internal static int RefundCount(int id) { int value; Refunds.TryGetValue(id, out value); return value; }
        internal static void Reset()
        {
            ModeGRunContext.Unbind(ModeGRunContext.Current);
            Events.Clear(); Refunds.Clear(); ModeGRuntimeModule.Created.Clear();
            InitOk = StartOk = RefundOk = true; HudThrow = false;
            CharacterMainControl.Main = new CharacterMainControl();
            Sign = new BossRushSignInteractable();
            Input.Pressed = false; UnityEngine.SceneManagement.SceneManager.Current = "Arena";
            ModeGAvailability.Ready = ModeGPresentationAssetCache.Ready = ModeHRuntimeGates.Allowed = true;
            ModeGNemesisPersistence.IsStoreFaulted = ModeGProfilePersistence.IsStoreFaulted = ModeGPersistenceFlushCoordinator.IsFaulted = false;
            ModeGLateCleanupSink.HasPendingLeases = false; BossRushMapSelectionHelper.Prepaid = false;
            ModeGAbandonPresenter.IsOpen = false; ZombieModeUIHelper.IsModalInputPaused = false;
            BossRushUI.Hidden = BossRushUI.Paused = false;
        }
    }
    public class EnemyPresetInfo { public string name; }
    public class EnemySpawnCoreOptions
    {
        public bool HoldForExternalCommit, ApplySharedMutators = true, AllowRandomRetryFallback = true;
        public object ManagedBossContext;
    }
    internal class EnemySpawnContext { public CharacterMainControl character; public ManagedBossRuntimeHandle managedBossHandle; }
    internal class EnemySpawnCoreResult { public bool success; public EnemySpawnContext context; }
    public partial class ModBehaviour : UnityEngine.Object
    {
        internal static ModBehaviour Instance;
        internal bool IsActive, modeDActive, modeEActive, modeFActive, IsZombieModeActive;
        internal bool bossRushArenaActive, bossRushArenaPlanned, spawnersDisabled, DisableWorks = true;
        internal Config config = new Config();
        internal Item Ticket, Relic, Flag, Transponder;
        internal bool ConsumeTicketWorks = true, ConsumeRelicWorks = true;
        internal List<EnemyPresetInfo> Pool;
        internal Dictionary<string, CharacterRandomPreset> cachedCharacterPresets;
        internal Vector3[] Points;
        internal int CoreCalls, PrepareCalls, CleanupCalls, ActivateCalls;
        internal CharacterRandomPreset LastDirectPreset;
        internal EnemySpawnCoreOptions LastOptions;
        internal ManagedBossSpawnContext LastManagedContext;
        internal TaskCompletionSource<EnemySpawnCoreResult> PendingCore;
        internal TaskCompletionSource<ManagedBossPrepareResult> PendingManaged;
        internal string LastAdapter;
        internal bool LastActiveCheck;
        internal ModBehaviour()
        {
            Instance = this;
            Ticket = new Item { TypeID = 9001 }; Relic = new Item { TypeID = FateEchoRelicConfig.TYPE_ID };
            Pool = new List<EnemyPresetInfo> { new EnemyPresetInfo { name = "Official" } };
            cachedCharacterPresets = new Dictionary<string, CharacterRandomPreset> {
                { "Official", new CharacterRandomPreset { name = "Original", nameKey = "OfficialKey", exp = 42, team = Teams.wolf } }
            };
            Points = new[] { new Vector3(30, 0, 0), new Vector3(-30, 0, 0) };
        }
        internal bool Active { get { return modeGActive; } }
        internal ModeGRuntimeModule Core { get { return modeGRuntime; } }
        internal bool HasEntry { get { return modeGEntryRuntime != null; } }
        internal void Tick(float delta) { UpdateModeG(delta); }
        internal void Shutdown() { ShutdownModeG(); }
        internal bool DebugStart(ModeGEntryPreview preview, out bool owns) { return StartModeGRuntime(preview, false, false, out owns); }
        internal static void DevLog(string text) { }
        internal static string GetModPath() { return null; }
        internal static string TryGetSteamPersonaName() { return "fixture"; }
        internal static void DestroyManagedCharacterQuiet(CharacterMainControl character) { UnityEngine.Object.Destroy(character.gameObject); }
        private Item DetectBossRushTicketItem() { return Ticket; }
        private (Teams? faction, Item flagItem) DetectFactionFlag() { return (null, Flag); }
        private Item DetectBloodhuntTransponder() { return Transponder; }
        private bool TryConsumeModeEntryItem(Item item, string mode, string label)
        {
            Harness.Events.Add("consume:" + item.TypeID);
            if (item.TypeID == 9001) return ConsumeTicketWorks;
            return ConsumeRelicWorks;
        }
        private int GetBossRushTicketTypeId() { return 9001; }
        private void ShowMessage(string value) { Harness.Events.Add("message"); }
        private void ShowBigBanner(string value) { Harness.Events.Add("banner"); }
        private void InitializeEnemyPresets() { Harness.Events.Add("pool-init"); }
        private void InitializeBossPoolFilter() { Harness.Events.Add("pool-filter"); }
        private void EnsureCharacterPresetsCacheReady() { Harness.Events.Add("preset-cache"); }
        private List<EnemyPresetInfo> GetFilteredEnemyPresets() { return Pool; }
        private bool IsDragonDescendantPreset(EnemyPresetInfo info) { return info.name == "Dragon"; }
        private bool IsDragonKingPreset(EnemyPresetInfo info) { return info.name == "King"; }
        private bool IsPhantomWitchPreset(EnemyPresetInfo info) { return info.name == "Witch"; }
        private bool IsManagedBossPreset(EnemyPresetInfo info) { return info.name == "OtherManaged"; }
        private HashSet<int> BuildGeneralBossLootCandidateIdSet() { return new HashSet<int> { 77 }; }
        private CharacterRandomPreset FindQuestionMarkPreset() { return cachedCharacterPresets["Official"]; }
        private CharacterRandomPreset FindFallbackPreset() { return null; }
        private CharacterRandomPreset FindDragonKingBasePreset() { return cachedCharacterPresets["Official"]; }
        private CharacterRandomPreset FindPhantomWitchBasePreset() { return cachedCharacterPresets["Official"]; }
        private Task<EnemySpawnCoreResult> SpawnEnemyCoreInternalAsync(EnemyPresetInfo preset, Vector3 position, bool isBoss,
            Func<bool> isActiveCheck, int waveIndex = 1, bool skipDragonDescendant = false, bool skipDragonKing = false,
            bool applyEquipment = true, bool applyBossMultiplier = true, CharacterRandomPreset directPreset = null,
            bool skipBossRushLootTracking = false, bool normalizeDamageMultiplier = true,
            bool deferActivationUntilNextFrame = false, Func<EnemySpawnContext, bool> onCommit = null, EnemySpawnCoreOptions options = null)
        {
            CoreCalls++; LastOptions = options; LastDirectPreset = directPreset; LastActiveCheck = isActiveCheck();
            if (applyEquipment || !applyBossMultiplier || !skipBossRushLootTracking || normalizeDamageMultiplier || deferActivationUntilNextFrame)
                throw new InvalidOperationException("Mode G spawn flags changed");
            PendingCore = new TaskCompletionSource<EnemySpawnCoreResult>(); return PendingCore.Task;
        }
        private Task<ManagedBossPrepareResult> PrepareManagedDragonDescendantAsync(Vector3 p, ManagedBossSpawnContext ctx) { return Prepare("Dragon", ctx); }
        private Task<ManagedBossPrepareResult> PrepareManagedDragonKingAsync(Vector3 p, ManagedBossSpawnContext ctx) { return Prepare("King", ctx); }
        private Task<ManagedBossPrepareResult> PrepareManagedPhantomWitchAsync(Vector3 p, ManagedBossSpawnContext ctx) { return Prepare("Witch", ctx); }
        private Task<ManagedBossPrepareResult> Prepare(string adapter, ManagedBossSpawnContext ctx)
        {
            PrepareCalls++; LastAdapter = adapter; LastManagedContext = ctx;
            PendingManaged = new TaskCompletionSource<ManagedBossPrepareResult>(); return PendingManaged.Task;
        }
        private void ActivateModeGManagedCharacter(CharacterMainControl character)
        { ActivateCalls++; character.Health.SetInvincible(false); character.gameObject.SetActive(true); }
        private void CleanupModeGManagedCharacter(CharacterMainControl character, string key, string preset, string tag)
        { CleanupCalls++; DestroyManagedCharacterQuiet(character); }
        private Vector3[] GetCurrentSceneSpawnPoints() { return Points; }
        private void SetCurrentMapSpawnPoints(string scene) { Harness.Events.Add("set-points:" + scene); }
        private void InitializeItemValueCacheAsync() { Harness.Events.Add("item-cache"); }
        private void TryCreateArenaDifficultyEntryPoint() { Harness.Events.Add("entry-point"); }
        private void PreCacheMapSpawnerPositions() { Harness.Events.Add("precache"); }
        private void DisableAllSpawners() { Harness.Events.Add("disable"); if (DisableWorks) spawnersDisabled = true; }
        private void ClearEnemiesForBossRush() { Harness.Events.Add("clear-enemies"); }
    }
    internal class Config { internal int modeGAbandonHotkey = (int)KeyCode.F4; }
    internal abstract class RuntimeBase { public virtual void OnUpdate(float delta, float unscaled) { } }
    internal sealed partial class ModeGRuntimeModule : RuntimeBase
    {
        internal static readonly List<ModeGRuntimeModule> Created = new List<ModeGRuntimeModule>();
        internal ModeGRunState State;
        internal ModBehaviour Host;
        internal bool Disposed, RefundTicket, RefundRelic;
        internal int DisposeCalls, TickCalls;
        internal ModeGRuntimeModule() { Created.Add(this); Harness.Events.Add("new-core"); }
        internal bool Initialize(ModeGRunState state, ModeGEntryPreview preview)
        { State = state; Host = ModBehaviour.Instance; Harness.Events.Add("initialize"); return Harness.InitOk; }
        internal void ArmStartupRefund(bool ticket, bool relic)
        {
            if (!ReferenceEquals(ModeGRunContext.CurrentModule, this) || !ReferenceEquals(ModeGRunContext.Current, State) || !State.IsStarting)
                throw new InvalidOperationException("Refund arm must follow Starting and Bind");
            RefundTicket = ticket; RefundRelic = relic; Harness.Events.Add("arm-refund");
        }
        internal bool StartRun()
        {
            Harness.Events.Add("start");
            if (!Harness.StartOk) return false;
            State.TryAdvanceLifecycle(ModeGLifecyclePhase.Active); return true;
        }
        internal void End(ModeGExitReason reason)
        {
            Harness.Events.Add("end:" + reason);
            if (RefundTicket) Host.TryRefundModeGStartupItem(Host.GetModeGTicketTypeId(), "ticket");
            if (RefundRelic) Host.TryRefundModeGStartupItem(FateEchoRelicConfig.TYPE_ID, "relic");
            RefundTicket = RefundRelic = false;
            State.exitReason = reason;
            State.TryAdvanceLifecycle(ModeGLifecyclePhase.Exiting); State.TryAdvanceLifecycle(ModeGLifecyclePhase.None);
        }
        internal void Dispose() { DisposeCalls++; Disposed = true; Harness.Events.Add("core-dispose"); }
        private void DriveCore(float delta) { TickCalls++; Harness.Events.Add("core-tick"); }
    }
    internal sealed class ModeGHUD
    {
        internal static ModeGRuntimeModule LastCore;
        internal ModeGHUD(ModeGRuntimeModule core)
        {
            Harness.Events.Add("hud-new"); if (Harness.HudThrow) throw new InvalidOperationException("HUD fixture failure");
            LastCore = core;
        }
        internal void Update(float delta) { Harness.Events.Add("hud-tick"); }
        internal void Dispose() { Harness.Events.Add("hud-dispose"); }
    }
    internal static class ModeEntryInventory { internal static Item FindFirstPlayerInventoryItemByTypeId(int id, string mode, string label) { return ModBehaviour.Instance.Relic; } }
    internal static class FateEchoRelicConfig { internal const int TYPE_ID = 500057; }
    internal static class DragonDescendantConfig { internal const int DRAGON_HELM_TYPE_ID = 1, DRAGON_ARMOR_TYPE_ID = 2, DRAGON_BREATH_TYPE_ID = 3; internal const string BasePresetNameKey = "Dragon"; }
    internal static class DragonKingConfig { internal const int DRAGON_KING_HELM_TYPE_ID = 4, DRAGON_KING_ARMOR_TYPE_ID = 5; internal const string AssetBundlePath = "absent", BossNameKey = "King"; }
    internal static class PhantomWitchConfig { internal const int ReservedScytheTypeId = 6, PlaceholderScytheTypeId = 7; internal const string BossNameKey = "Witch", FallbackPresetNameKey = "WitchFallback"; }
    internal static class ModeGAvailability { internal static bool Ready; internal static bool IsProductionReady { get { return Ready; } } internal const bool AllowDevTestEntry = false; internal const string CurrentVerificationRevision = "fixture-revision"; }
    public enum ModeGRunFormat { FirstClearNarrative, RematchMix }
    internal static class ModeGEncounterVariation
    {
        internal const string ManagedDragonDescendantKey = "managed-dragon", ManagedDragonKingKey = "managed-king", ManagedPhantomWitchKey = "managed-witch";
        internal static void SetSignatureEligibility(string key, bool available) { }
        internal static string[] GetEligibleSignatureKeys() { return new[] { ManagedDragonDescendantKey }; }
        internal static Vector2[] GetSpawnOffsets(ModeGPlanVariant variant, int count, ModeGWavePlan.FormationSpec spec) { return new Vector2[count]; }
    }
    internal static class ModeGMapSupportRegistry
    {
        internal static bool TryGetVerifiedPairForScene(string scene, out string id) { id = "arena-id"; return scene == "Arena"; }
        internal static bool IsSupported(string scene, string id, string revision) { return scene == "Arena" && id == "arena-id" && revision == ModeGAvailability.CurrentVerificationRevision; }
    }
    internal static class ModeGDeterministicRandom
    {
        internal static ulong DeriveRunSeed(long counter) { return (ulong)counter + 11; }
        internal static bool ValidateGoldenVectors() { return true; }
        internal static ulong SplitMix64Next(ref ulong state) { return ++state; }
        internal static ulong Fnv1a64(string value) { return 19; }
    }
    internal static class ModeGProfilePersistence
    {
        internal static bool IsStoreFaulted;
        internal static void EnsureSubscribed() { Harness.Events.Add("profile-subscribe"); }
        internal static bool HasAnyVictory() { return false; }
        internal static int GetLastSelectedContractId() { return -1; }
        internal static bool RecordSelectedContract(int id) { Harness.Events.Add("contract:" + id); return true; }
        internal static void ShutdownSubscription() { Harness.Events.Add("profile-unsubscribe"); }
    }
    internal static class ModeGNemesisPersistence
    {
        internal static bool IsStoreFaulted;
        internal static void EnsureSubscribed() { Harness.Events.Add("nemesis-subscribe"); }
        internal static void ShutdownSubscription() { Harness.Events.Add("nemesis-unsubscribe"); }
    }
    internal static class ModeGPersistenceFlushCoordinator
    {
        internal static bool IsFaulted;
        internal static void TryFlushOnHostDestroy() { Harness.Events.Add("flush"); }
    }
    internal static class ModeGFateContract { internal const int IdTriadBreaker = 1; internal static int[] SelectEntryCandidatePair(ulong seed, int last) { return new[] { 1, 2 }; } }
    internal static class ModeHRuntimeGates { internal static bool Allowed; internal static bool IsLegacyModeEntryAllowed() { return Allowed; } internal static string ResolveLegacyBlockedMessageKey() { return "blocked"; } }
    internal static class L10n { internal static string T(string value) { return value; } internal static string T(string zh, string en) { return zh; } }
    internal static class ModeGPresentationAssetCache { internal static bool Ready; internal static bool TryPreflight() { return Ready; } }
    internal static class BossRushMapSelectionHelper
    {
        internal static bool Prepaid;
        internal static bool HasPendingPrepaidTicket() { return Prepaid; }
        internal static void ClearPendingEntryFlowState() { Prepaid = false; Harness.Events.Add("clear-prepaid"); }
    }
    internal static class ModeGWeaponScoringCompatibilityMatrix { internal static bool IsMatrixValid(out string reason) { reason = null; return true; } }
    internal static class ModeGRewardTransaction
    {
        internal static bool TryCommitItemWithGroundFallback(Item item, Inventory inv, CharacterMainControl player, string label)
        { if (!Harness.RefundOk) return false; Harness.Refund(item.TypeID); return true; }
    }
    internal static class ModeGRichText { internal const string WarningTag = ""; }
    internal static class ModeGAbandonPresenter
    {
        internal static bool IsOpen;
        internal static void TryOpen(ModeGRuntimeModule core) { IsOpen = true; Harness.Events.Add("abandon-open"); }
        internal static void CloseIfOpen() { IsOpen = false; Harness.Events.Add("abandon-close"); }
    }
    internal static class ZombieModeUIHelper { internal static bool IsModalInputPaused; }
    internal static class BossRushUI { internal static bool Hidden, Paused; internal static bool IsOfficialHudHidden() { return Hidden; } internal static bool IsGamePaused() { return Paused; } }
    internal static class ModeGInteractable { internal static void CloseActiveConfirmation() { Harness.Events.Add("confirm-close"); } }
    internal static class ModeGLateCleanupSink { internal static bool HasPendingLeases; }
    internal static class ModeGCombatTelemetry { internal static void ConsumePendingAchievementReports(ModeGRunState state) { Harness.Events.Add("drain-reports"); } }
    internal static class ModeGCleanupController { internal static void PrepareHostDestroyInternal(ModeGRunState state) { Harness.Events.Add("prepare-destroy"); } }
    internal class ModeGWavePlan
    {
        internal class WaveSlot { internal bool isNemesisWave; }
        internal struct FormationSpec { internal float playerMinDistance, bossPairMinDistance; internal FormationSpec(float a, float b) { playerMinDistance = a; bossPairMinDistance = b; } }
        internal static FormationSpec GetFormationSpec(ModeGPlanVariant variant) { return new FormationSpec(10, 5); }
    }
    internal static class SpawnPositionHelper { internal static bool TrySnapToGround(Vector3 input, out Vector3 grounded) { grounded = input; return true; } }
    internal class BossRushSignInteractable : UnityEngine.Object { internal void AddAmmoRefillOption() { Harness.Events.Add("ammo-refill"); } }
    internal static class ModeGAdaptiveCombat { internal static string GetTemperamentDisplayName(ModeGNemesisTemperament value) { return value.ToString(); } }
    internal static class ModeGManagedCharacterService { internal static bool HasModeGPlayerAuthoredBuff(CharacterMainControl character) { return false; } }
    internal class ModeGRewardCandidate { internal int TypeId; internal ModeGRewardCandidate(int id, int price, int stack) { TypeId = id; } }
}
