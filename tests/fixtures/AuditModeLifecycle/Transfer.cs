using System;
using System.Collections.Generic;
using System.Linq;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace UnityEngine {
 public class Object { }
 public struct Vector3 {
  public float x, y, z;
  public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
 }
 public sealed class Transform { public Vector3 localScale; }
 public class Component : Object { public GameObject gameObject; public Transform transform = new Transform(); }
 public class MonoBehaviour : Component { }
 public sealed class GameObject : Object {
  private readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
  public readonly Transform transform = new Transform();
  public int GetComponentCalls, AddComponentCalls;
  public GameObject(string name = "fixture") { }
  public T GetComponent<T>() where T : class {
   GetComponentCalls++;
   Component value; return components.TryGetValue(typeof(T), out value) ? value as T : null;
  }
  public T AddComponent<T>() where T : Component, new() {
   AddComponentCalls++; T value = new T(); value.gameObject = this; components[typeof(T)] = value; return value;
  }
 }
}

namespace Duckov.Modding { public class ModBehaviour { } }
namespace ItemStatsSystem.Items {
 public static class ItemUtilities {
  public static void SendToPlayer(Item item, bool dontMerge, bool sendToStorage) {
   TransferTrace.Add("return:" + item.Identity);
   if (item.IsBeingDestroyed) throw new InvalidOperationException("destroyed source cannot be delivered");
   if (CharacterMainControl.Main.CharacterItem.Inventory.AddAt(item, 0)) return;
   throw new InvalidOperationException("control player inventory unavailable");
  }
 }
}
namespace ItemStatsSystem.Data {
 public sealed class ItemTreeData {
  public string Identity;
  public static ItemTreeData FromItem(Item item) {
   TransferTrace.Add("serialize:" + item.Identity);
   return new ItemTreeData { Identity = item.Identity };
  }
 }
}
namespace ItemStatsSystem {
 public sealed class Item {
  public string Identity;
  public bool IsBeingDestroyed;
  public Inventory Inventory;
  public SlotCollection Slots = new SlotCollection();
  public Inventory InInventory;
  public Item(string identity) { Identity = identity; }
  public void Detach() {
   if (InInventory != null) {
    InInventory.Content.Remove(this);
    InInventory = null;
    TransferTrace.Add("detach:" + Identity);
   }
  }
  public void DestroyTree() { Detach(); IsBeingDestroyed = true; TransferTrace.Add("destroy:" + Identity); }
 }
 public sealed class Inventory {
  public List<Item> Content = new List<Item>(); public bool Full;
  public int GetFirstEmptyPosition(int start) { return Full ? -1 : Content.Count; }
  public bool AddAt(Item item, int index) {
   if (Full) return false;
   item.Detach(); Content.Insert(Math.Max(0, Math.Min(index, Content.Count)), item); item.InInventory = this;
   if (object.ReferenceEquals(this, PlayerStorage.Inventory)) TransferTrace.Add("storage-add:" + item.Identity);
   else if (CharacterMainControl.Main != null && object.ReferenceEquals(this, CharacterMainControl.Main.CharacterItem.Inventory)) TransferTrace.Add("player-add:" + item.Identity);
   return true;
  }
 }
 public sealed class Slot { public Item Content; }
 public sealed class SlotCollection : List<Slot> { }
}
public static class TransferTrace {
 public static readonly List<string> Events = new List<string>();
 public static void Add(string value) { Events.Add(value); }
 public static void Clear() { Events.Clear(); }
}
public sealed class CharacterMainControl {
 private static int nextId;
 private readonly int instanceId = ++nextId;
 public static CharacterMainControl Main; public Item CharacterItem;
 public UnityEngine.GameObject gameObject = new UnityEngine.GameObject("character");
 public int GetInstanceID() { return instanceId; }
 public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
}
public static class PlayerStorage { public static Inventory Inventory; }
public sealed class TrackedItemTreeDataList : List<ItemTreeData> {
 public new void Add(ItemTreeData itemData) { base.Add(itemData); TransferTrace.Add("inbox-add:" + itemData.Identity); }
 public new bool Remove(ItemTreeData itemData) { bool removed = base.Remove(itemData); if (removed) TransferTrace.Add("inbox-remove:" + itemData.Identity); return removed; }
}
public static class PlayerStorageBuffer {
 public static TrackedItemTreeDataList Buffer = new TrackedItemTreeDataList();
 public static List<ItemTreeData> Saved = new List<ItemTreeData>();
 public static int SaveCalls, FailOnSaveCall;
 // Official SaveBuffer copies the current non-null list into its persisted snapshot.
 public static void SaveBuffer() {
  SaveCalls++;
  if (SaveCalls == FailOnSaveCall) { TransferTrace.Add("save-fail:" + SaveCalls); throw new InvalidOperationException("injected buffer save failure"); }
  Saved = Buffer.Where(x => x != null).ToList(); TransferTrace.Add("save:" + string.Join(",", Saved.Select(x => x.Identity)));
 }
}
namespace UnityEngine.SceneManagement {
 public struct Scene { public int buildIndex; }
 public static class SceneManager { public static Scene ActiveScene; public static Scene GetActiveScene() { return ActiveScene; } }
}
namespace BossRush {
 public sealed class BossRushStatModifierRecord { }
 public sealed class ZombieModeBossShieldRuntime { }
 public sealed class ZombieModeShieldedAffixRuntime { }
 public sealed class ZombieModeCommanderAuraTargetRuntime { }
 public sealed class ZombieModeVisualScaleRecord { public UnityEngine.Transform Target; public UnityEngine.Vector3 OriginalScale; }
 public sealed class ZombieModeEnemyRuntimeMarker : UnityEngine.MonoBehaviour {
  public int RunId; public int PurificationPointValue; public bool SuppressDrops; public bool IsBoss; public bool DeathSettled; public bool RemovedFromRuntime; public bool CustomExploderSkillDetonated;
  public ZombieModeBossKind BossKind; public ZombieModeEnemyKind EnemyKind; public ZombieModeSpecialKind SpecialKind;
  public readonly List<ZombieModeEliteAffix> EliteAffixes = new List<ZombieModeEliteAffix>();
  public float BaseMaxHealth; public float HealthMultiplier = 1f; public float DamageMultiplier = 1f; public float MoveSpeedMultiplier = 1f;
  public int AdaptiveRangedHitCount; public int AdaptiveMeleeHitCount; public float AdaptiveReductionEndTime; public bool AdaptiveRangedActive; public bool AdaptiveMeleeActive;
  public CharacterMainControl Owner; public AICharacterController CachedAI;
  public readonly List<BossRushStatModifierRecord> RuntimeModifierRecords = new List<BossRushStatModifierRecord>();
  public ZombieModeBossShieldRuntime AllyShield; public ZombieModeShieldedAffixRuntime ShieldedAffix; public ZombieModeCommanderAuraTargetRuntime CommanderAuraTargetRuntime;
  public float SuppressedForceTraceDistance; public bool HasSuppressedForceTraceDistance; public bool VisualIdentityApplied; public bool VisualScaleApplied; public bool VisualFaceApplied;
  public bool VisualFootMarkerFallbackApplied; public UnityEngine.GameObject VisualFootMarker;
  public readonly List<ZombieModeVisualScaleRecord> VisualScaleRecords = new List<ZombieModeVisualScaleRecord>();
 }
 public sealed class AICharacterController { }
 public static class ZombieModeFootMarkerPool {
  public static int ReleaseCalls; public static UnityEngine.GameObject LastReleased;
  internal static void Release(UnityEngine.GameObject marker) { ReleaseCalls++; LastReleased = marker; TransferTrace.Add("foot-marker-release"); }
 }
 public static class ReforgeDataPersistence {
  public static void SyncCurrentReforgeState(Item item) { TransferTrace.Add("reforge-sync:" + item.Identity); }
 }
}

static class Program {
 static int failures;
 static void Check(bool result, string label) { Console.WriteLine((result ? "PASS " : "FAIL ") + label); if (!result) failures++; }
 static Item Setup(bool full, bool storageAvailable = true) {
  PlayerStorage.Inventory = storageAvailable ? new Inventory { Full = full } : null;
  PlayerStorageBuffer.Buffer.Clear(); PlayerStorageBuffer.Saved.Clear();
  PlayerStorageBuffer.SaveCalls = 0; PlayerStorageBuffer.FailOnSaveCall = 0;
  CharacterMainControl.Main = new CharacterMainControl { CharacterItem = new Item("character") { Inventory = new Inventory() } };
  Item original = new Item("unique-equipped-loot-identity");
  CharacterMainControl.Main.CharacterItem.Inventory.AddAt(original, 0);
  TransferTrace.Clear();
  return original;
 }
 static BossRush.ModBehaviour NewMode() {
  var owner = new BossRush.ModBehaviour();
  var state = new BossRush.ZombieModeRunState { RunId = 1, LifecyclePhase = BossRush.ZombieModeLifecyclePhase.Active };
  var transaction = new BossRush.ZombieModeEntryTransaction();
  owner.AttachTransferTestModule(new BossRush.ZombieModeRuntimeModule(owner, state, transaction), transaction);
  return owner;
 }
 static void TestEnemyRuntime() {
  TransferTrace.Clear(); BossRush.ZombieModeFootMarkerPool.ReleaseCalls = 0; BossRush.ZombieModeFootMarkerPool.LastReleased = null;
  var mode = NewMode();
  var invalidEnemy = new CharacterMainControl();
  Check(mode.RegisterEnemyForTest(2, invalidEnemy) == null && invalidEnemy.gameObject.GetComponentCalls == 0 && invalidEnemy.gameObject.AddComponentCalls == 0,
   "enemy marker registration rejects a stale RunId before touching components");
  Check(mode.RunOnlyRegistrationCountForTest == 0 && !mode.IsKnownEnemyForTest(invalidEnemy),
   "stale RunId does not add enemy index or RunOnly record");

  var enemy = new CharacterMainControl();
  var marker = enemy.gameObject.AddComponent<BossRush.ZombieModeEnemyRuntimeMarker>();
  marker.RunId = 77; marker.PurificationPointValue = 4; marker.SuppressDrops = false; marker.IsBoss = true;
  marker.DeathSettled = true; marker.RemovedFromRuntime = true; marker.CustomExploderSkillDetonated = true;
  marker.EnemyKind = BossRush.ZombieModeEnemyKind.Elite; marker.CachedAI = new BossRush.AICharacterController();
  marker.RuntimeModifierRecords.Add(new BossRush.BossRushStatModifierRecord());
  marker.EliteAffixes.Add(BossRush.ZombieModeEliteAffix.Swift);
  marker.AllyShield = new BossRush.ZombieModeBossShieldRuntime();
  marker.ShieldedAffix = new BossRush.ZombieModeShieldedAffixRuntime();
  marker.CommanderAuraTargetRuntime = new BossRush.ZombieModeCommanderAuraTargetRuntime();
  marker.SuppressedForceTraceDistance = 18f; marker.HasSuppressedForceTraceDistance = true;
  marker.VisualIdentityApplied = true; marker.VisualScaleApplied = true; marker.VisualFaceApplied = true;
  marker.VisualFootMarkerFallbackApplied = true;
  var scaledRoot = new UnityEngine.Transform(); scaledRoot.localScale = new UnityEngine.Vector3(3f, 3f, 3f);
  marker.VisualScaleRecords.Add(new BossRush.ZombieModeVisualScaleRecord { Target = scaledRoot, OriginalScale = new UnityEngine.Vector3(1f, 2f, 3f) });
  var oldFootMarker = new UnityEngine.GameObject("old-foot-marker"); marker.VisualFootMarker = oldFootMarker;
  var affixes = new List<BossRush.ZombieModeEliteAffix> { BossRush.ZombieModeEliteAffix.Commander };
  TransferTrace.Clear();
  var registered = mode.RegisterEnemyForTest(1, enemy, false, BossRush.ZombieModeBossKind.Titan, -1,
   BossRush.ZombieModeEnemyKind.Special, BossRush.ZombieModeSpecialKind.None, affixes);
  Check(object.ReferenceEquals(registered, marker) && enemy.gameObject.GetComponentCalls == 1 && enemy.gameObject.AddComponentCalls == 1,
   "valid RunId reuses the existing runtime marker component");
  Check(marker.RunId == 1 && marker.PurificationPointValue == 733 && marker.SuppressDrops && !marker.IsBoss &&
   !marker.DeathSettled && !marker.RemovedFromRuntime && !marker.CustomExploderSkillDetonated &&
   marker.EnemyKind == BossRush.ZombieModeEnemyKind.Special && marker.Owner == enemy,
   "marker re-registration resets run identity, reward, death and Exploder state");
  Check(marker.CachedAI == null && marker.RuntimeModifierRecords.Count == 0 && marker.EliteAffixes.SequenceEqual(affixes) &&
   marker.AllyShield == null && marker.ShieldedAffix == null && marker.CommanderAuraTargetRuntime == null &&
   marker.SuppressedForceTraceDistance == 0f && !marker.HasSuppressedForceTraceDistance,
   "marker re-registration clears runtime caches and replaces affix state");
  Check(scaledRoot.localScale.x == 1f && scaledRoot.localScale.y == 2f && scaledRoot.localScale.z == 3f &&
   marker.VisualScaleRecords.Count == 0 && marker.VisualFootMarker == null && !marker.VisualFootMarkerFallbackApplied &&
   !marker.VisualIdentityApplied && !marker.VisualScaleApplied && !marker.VisualFaceApplied &&
   BossRush.ZombieModeFootMarkerPool.ReleaseCalls == 1 && object.ReferenceEquals(BossRush.ZombieModeFootMarkerPool.LastReleased, oldFootMarker),
   "marker re-registration restores recorded scales and releases its old pooled foot marker");
  Check(mode.IsKnownEnemyForTest(enemy) && mode.TryGetKnownMarkerForTest(enemy, out var cachedMarker) &&
   object.ReferenceEquals(cachedMarker, marker) && enemy.gameObject.GetComponentCalls == 1,
   "registered marker lookup uses the index and cached component without another GetComponent");
  Check(mode.WasIndexedAtRunOnlyRegistrationForTest && mode.RunOnlyRegistrationCountForTest == 1,
   "enemy instance ID is registered before its RunOnly cleanup record");
  string[] order = TransferTrace.Events.ToArray();
  Check(Array.IndexOf(order, "calculate-points:False:Special") >= 0 &&
   Array.IndexOf(order, "foot-marker-release") < Array.IndexOf(order, "runonly-register"),
   "purification fallback and old foot-marker release precede RunOnly registration");
  var cleanupFootMarker = new UnityEngine.GameObject("cleanup-foot-marker"); marker.VisualFootMarker = cleanupFootMarker;
  marker.VisualFootMarkerFallbackApplied = true; int releasesBeforeCleanup = BossRush.ZombieModeFootMarkerPool.ReleaseCalls;
  mode.RunLastCleanupForTest();
  Check(marker.VisualFootMarker == null && !marker.VisualFootMarkerFallbackApplied &&
   BossRush.ZombieModeFootMarkerPool.ReleaseCalls == releasesBeforeCleanup + 1 &&
   object.ReferenceEquals(BossRush.ZombieModeFootMarkerPool.LastReleased, cleanupFootMarker),
   "RunOnly cleanup callback releases the marker component's current foot marker");

  var fallbackEnemy = new CharacterMainControl();
  var fallbackMarker = fallbackEnemy.gameObject.AddComponent<BossRush.ZombieModeEnemyRuntimeMarker>();
  mode.RegisterEnemyIndexForTest(fallbackEnemy);
  Check(mode.TryGetKnownMarkerForTest(fallbackEnemy, out var fallbackFound) && object.ReferenceEquals(fallbackFound, fallbackMarker) &&
   fallbackEnemy.gameObject.GetComponentCalls == 1,
   "known instance without a cached marker lazily resolves and caches the component");
  Check(mode.TryGetKnownMarkerForTest(fallbackEnemy, out fallbackFound) && fallbackEnemy.gameObject.GetComponentCalls == 1,
   "fallback marker lookup is cached after its first component resolution");
  mode.UnregisterEnemyIndexForTest(fallbackEnemy);
  Check(!mode.IsKnownEnemyForTest(fallbackEnemy) && !mode.TryGetKnownMarkerForTest(fallbackEnemy, out fallbackFound) &&
   fallbackEnemy.gameObject.GetComponentCalls == 1,
   "unregister removes both known-enemy membership and marker cache before later lookups");

  var knownWithoutMarker = new CharacterMainControl(); mode.RegisterEnemyIndexForTest(knownWithoutMarker);
  Check(mode.TryGetKnownMarkerForTest(knownWithoutMarker, out var absentMarker) && absentMarker == null,
   "known instance lookup preserves true membership when its marker component is absent");
  var clearedEnemy = new CharacterMainControl();
  clearedEnemy.gameObject.AddComponent<BossRush.ZombieModeEnemyRuntimeMarker>();
  mode.RegisterEnemyIndexForTest(clearedEnemy); mode.TryGetKnownMarkerForTest(clearedEnemy, out _);
  mode.ClearEnemyIndexForTest();
  Check(!mode.IsKnownEnemyForTest(clearedEnemy) && !mode.TryGetKnownMarkerForTest(clearedEnemy, out var afterClear) && afterClear == null,
   "clear removes known-enemy membership and all cached marker entries");
 }
 static int OwnedCopies(Item original) {
  return CharacterMainControl.Main.CharacterItem.Inventory.Content.Count(x => x.Identity == original.Identity && !x.IsBeingDestroyed)
   + (PlayerStorage.Inventory == null ? 0 : PlayerStorage.Inventory.Content.Count(x => x.Identity == original.Identity && !x.IsBeingDestroyed))
   + PlayerStorageBuffer.Buffer.Count(x => x.Identity == original.Identity);
 }
 static bool EventsFor(Item item, params string[] expected) {
  string suffix = ":" + item.Identity;
  string[] actual = TransferTrace.Events.Where(value => value.EndsWith(suffix, StringComparison.Ordinal)).ToArray();
  string[] wanted = expected.Select(value => value + ":" + item.Identity).ToArray();
  if (!actual.SequenceEqual(wanted)) Console.WriteLine("transfer trace actual=" + string.Join(" | ", actual) + " expected=" + string.Join(" | ", wanted));
  return actual.SequenceEqual(wanted);
 }
 static void Main() {
  TestEnemyRuntime();
  Item item = Setup(true); var mode = NewMode();
  Check(mode.Prepare(), "full storage entry transfer succeeds through the legacy host bridge");
  Check(mode.InventoryTransferStarted, "completed transfer sets the transaction started flag");
  Check(item.IsBeingDestroyed && OwnedCopies(item) == 1 && PlayerStorageBuffer.Saved.Count == 1, "buffer fallback is sole persisted copy before rollback");
  Check(EventsFor(item, "detach", "reforge-sync", "serialize", "inbox-add", "save", "destroy"), "inbox fallback preserves detach, reforge sync, serialize, append, save, and destroy order");
  mode.Rollback(); mode.Rollback();
  Check(!mode.InventoryTransferStarted, "rollback resets the transaction started flag");
  Check(OwnedCopies(item) == 1 && PlayerStorageBuffer.Saved.Count == 1, "rollback retains exactly one persisted inbox tree and is repeatable");

  item = Setup(false); mode = NewMode();
  var secondStorageItem = new Item("second-storage-item"); CharacterMainControl.Main.CharacterItem.Inventory.AddAt(secondStorageItem, 1); TransferTrace.Clear();
  Check(mode.Prepare(), "free storage slots accept carried items");
  Check(mode.InventoryTransferStarted, "storage transaction is marked started after all items move");
  Check(PlayerStorage.Inventory.Content.SequenceEqual(new[] { item, secondStorageItem }), "inventory order is preserved while moving items to storage");
  mode.Rollback();
  Check(!mode.InventoryTransferStarted, "storage rollback resets transaction state");
  string[] returns = TransferTrace.Events.Where(value => value.StartsWith("return:", StringComparison.Ordinal)).ToArray();
  Check(returns.SequenceEqual(new[] { "return:second-storage-item", "return:" + item.Identity }), "rollback returns moved items in reverse order");
  Check(OwnedCopies(item) == 1 && OwnedCopies(secondStorageItem) == 1 && CharacterMainControl.Main.CharacterItem.Inventory.Content.SequenceEqual(new[] { item, secondStorageItem }), "free-slot rollback returns both original live item trees");

  item = Setup(true, false); mode = NewMode();
  CharacterMainControl.Main.CharacterItem.Slots.Add(new Slot { Content = item });
  var equippedOnly = new Item("equipped-only-item"); CharacterMainControl.Main.CharacterItem.Slots.Add(new Slot { Content = equippedOnly });
  TransferTrace.Clear();
  Check(mode.PrepareViaRuntimeBridge(), "missing storage instance uses inbox fallback through runtime bridge");
  Check(mode.InventoryTransferStarted, "runtime bridge completes the same transaction state");
  Check(PlayerStorageBuffer.Saved.Select(x => x.Identity).SequenceEqual(new[] { item.Identity, equippedOnly.Identity }), "inventory precedes equipped-only slots and duplicate references are transferred once");
  Check(OwnedCopies(item) == 1 && OwnedCopies(equippedOnly) == 1 && PlayerStorageBuffer.SaveCalls == 2, "no-storage fallback persists one copy for every top-level item");
  Check(mode.Prepare() && PlayerStorageBuffer.SaveCalls == 2, "already-started transfer is idempotent across host entry points");

  item = Setup(true); mode = NewMode();
  var second = new Item("second-distinct-identity"); CharacterMainControl.Main.CharacterItem.Inventory.AddAt(second, 1); TransferTrace.Clear();
  PlayerStorageBuffer.FailOnSaveCall = 2;
  Check(!mode.Prepare(), "later save fault aborts entry transfer"); mode.Rollback();
  Check(!mode.InventoryTransferStarted, "failed partial transfer remains unstarted after rollback");
  Check(OwnedCopies(item) == 1 && item.IsBeingDestroyed && PlayerStorageBuffer.Saved.Count == 1, "prior committed inbox tree survives partial-transfer failure");
  Check(OwnedCopies(second) == 1 && !second.IsBeingDestroyed && CharacterMainControl.Main.CharacterItem.Inventory.Content.Contains(second), "failed item returns live without duplicate inbox candidate");
  int removeCandidate = TransferTrace.Events.IndexOf("inbox-remove:" + second.Identity);
  int returnFailed = TransferTrace.Events.IndexOf("return:" + second.Identity);
  Check(removeCandidate >= 0 && returnFailed > removeCandidate, "failed inbox candidate is removed before original item return");
  Console.WriteLine("AuditModeLifecycle inventory transfer: " + (failures == 0 ? "PASS" : "FAIL") + " / " + failures + " failures");
  Environment.ExitCode = failures == 0 ? 0 : 1;
 }
}
