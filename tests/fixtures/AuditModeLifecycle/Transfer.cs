using System;
using System.Collections.Generic;
using System.Linq;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace Duckov.Modding { public class ModBehaviour { } }
namespace ItemStatsSystem.Items { public class NamespaceMarker { } }
namespace ItemStatsSystem.Data {
 public sealed class ItemTreeData {
  public string Identity;
  public static ItemTreeData FromItem(Item item) { return new ItemTreeData { Identity = item.Identity }; }
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
  public void Detach() { if (InInventory != null) InInventory.Content.Remove(this); InInventory = null; }
  public void DestroyTree() { Detach(); IsBeingDestroyed = true; }
 }
 public sealed class Inventory {
  public List<Item> Content = new List<Item>(); public bool Full;
  public int GetFirstEmptyPosition(int start) { return Full ? -1 : Content.Count; }
  public bool AddAt(Item item, int index) { if (Full) return false; item.Detach(); Content.Add(item); item.InInventory = this; return true; }
 }
 public sealed class Slot { public Item Content; }
 public sealed class SlotCollection : List<Slot> { }
}
public sealed class CharacterMainControl { public static CharacterMainControl Main; public Item CharacterItem; }
public static class PlayerStorage { public static Inventory Inventory; }
public static class PlayerStorageBuffer {
 public static List<ItemTreeData> Buffer = new List<ItemTreeData>();
 public static List<ItemTreeData> Saved = new List<ItemTreeData>();
 public static int SaveCalls, FailOnSaveCall;
 // Official PlayerStorageBuffer.SaveBuffer copies the current non-null list to its save key.
 public static void SaveBuffer() { SaveCalls++; if (SaveCalls == FailOnSaveCall) throw new InvalidOperationException("injected buffer save failure"); Saved = Buffer.Where(x => x != null).ToList(); }
}
public static class ItemUtilities {
 public static void SendToPlayer(Item item, bool dontMerge, bool sendToStorage) {
  if (item.IsBeingDestroyed) throw new InvalidOperationException("destroyed source cannot be delivered");
  if (CharacterMainControl.Main.CharacterItem.Inventory.AddAt(item, 0)) return;
  throw new InvalidOperationException("control player inventory unavailable");
 }
}
namespace BossRush {
 public static class ReforgeDataPersistence { public static void SyncCurrentReforgeState(Item item) { } }
 public static class RunScopedRegistry {
  public static void ForEachReverse<T>(IList<T> items, Action<T> action, Action<Exception,T> failed) {
   for (int i = items.Count - 1; i >= 0; i--) { try { action(items[i]); } catch(Exception e) { failed(e, items[i]); } }
  }
 }
 public sealed class EntryTransaction {
  public bool InventoryTransferStarted;
  public List<Item> InventoryTransferredItems = new List<Item>();
  public List<ItemTreeData> InventoryTransferredInboxItems = new List<ItemTreeData>();
 }
 public partial class ModBehaviour {
  private EntryTransaction zombieModeEntryTransaction = new EntryTransaction();
  private bool IsZombieModeRunValid(int runId) { return runId == 1; }
  public static void DevLog(string message) { Console.WriteLine(message); }
  public bool Prepare() { return PrepareZombieModeInventoryTransferShell(1); }
  public void Rollback() { RollbackZombieModeInventoryTransferShell(); }
 }
}
static class Program {
 static int failures;
 static void Check(bool result, string label) { Console.WriteLine((result ? "PASS " : "FAIL ") + label); if (!result) failures++; }
 static Item Setup(bool full) {
  PlayerStorage.Inventory = new Inventory { Full = full };
  PlayerStorageBuffer.Buffer.Clear(); PlayerStorageBuffer.Saved.Clear();
  PlayerStorageBuffer.SaveCalls = 0; PlayerStorageBuffer.FailOnSaveCall = 0;
  CharacterMainControl.Main = new CharacterMainControl { CharacterItem = new Item("character") { Inventory = new Inventory() } };
  Item original = new Item("unique-equipped-loot-identity");
  CharacterMainControl.Main.CharacterItem.Inventory.AddAt(original, 0);
  return original;
 }
 static int OwnedCopies(Item original) {
  return CharacterMainControl.Main.CharacterItem.Inventory.Content.Count(x => x.Identity == original.Identity && !x.IsBeingDestroyed)
   + PlayerStorage.Inventory.Content.Count(x => x.Identity == original.Identity && !x.IsBeingDestroyed)
   + PlayerStorageBuffer.Buffer.Count(x => x.Identity == original.Identity);
 }
 static void Main() {
  Item item = Setup(true); var mode = new BossRush.ModBehaviour();
  Check(mode.Prepare(), "full storage entry transfer succeeds");
  Check(item.IsBeingDestroyed && OwnedCopies(item) == 1 && PlayerStorageBuffer.Saved.Count == 1, "buffer fallback is sole persisted copy before rollback");
  mode.Rollback(); mode.Rollback();
  Console.WriteLine("full-storage post-rollback: originalDestroyed=" + item.IsBeingDestroyed + ", ownedCopies=" + OwnedCopies(item) + ", savedBuffer=" + PlayerStorageBuffer.Saved.Count);
  Check(OwnedCopies(item) == 1, "CONSERVATION: failed initialization must retain exactly one original item tree");
  Check(PlayerStorageBuffer.Saved.Count == 1 || !item.IsBeingDestroyed, "DURABILITY: source destroyed only if a persisted or returned copy remains");
  item = Setup(false); mode = new BossRush.ModBehaviour();
  Check(mode.Prepare(), "control with free warehouse slot succeeds"); mode.Rollback();
  Check(OwnedCopies(item) == 1 && CharacterMainControl.Main.CharacterItem.Inventory.Content.Contains(item), "control free-slot rollback returns live original");
  item = Setup(true); mode = new BossRush.ModBehaviour();
  Check(mode.Prepare() && OwnedCopies(item) == 1 && PlayerStorageBuffer.Saved.Count == 1, "control committed transfer without rollback preserves buffer");
  item = Setup(true); mode = new BossRush.ModBehaviour();
  var second = new Item("second-distinct-identity"); CharacterMainControl.Main.CharacterItem.Inventory.AddAt(second, 1);
  PlayerStorageBuffer.FailOnSaveCall = 2;
  Check(!mode.Prepare(), "later save fault aborts entry transfer"); mode.Rollback();
  Check(OwnedCopies(item) == 1 && item.IsBeingDestroyed && PlayerStorageBuffer.Saved.Count == 1, "prior committed inbox tree survives partial-transfer failure");
  Check(OwnedCopies(second) == 1 && !second.IsBeingDestroyed && CharacterMainControl.Main.CharacterItem.Inventory.Content.Contains(second), "failed item returns live without duplicate inbox candidate");
  Console.WriteLine("audit probe failures=" + failures); Environment.ExitCode = failures == 0 ? 0 : 1;
 }
}
