// Links the real naked inventory traversal and entry resolver; only official containers/other-mode availability are hosts.
using System;
using System.Collections.Generic;
using BossRush;
using ItemStatsSystem;

namespace ItemStatsSystem
{
    public class Item { public int TypeID; public string DisplayName; public Inventory Inventory; public Slots Slots = new Slots(); }
    public class Inventory { public List<Item> Content = new List<Item>(); }
    public class Slots
    {
        public readonly Dictionary<string, Items.Slot> Content = new Dictionary<string, Items.Slot>();
        public Items.Slot GetSlot(string name) { Items.Slot slot; return Content.TryGetValue(name, out slot) ? slot : null; }
    }
}
namespace ItemStatsSystem.Items { public class Slot { public Item Content; } }
public class CharacterMainControl { public static CharacterMainControl Main; public Item CharacterItem; }
public static class PetProxy { public static Inventory PetInventory; }
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { name = "Level_GroundZero_Main" }; } }
}
namespace BossRush
{
    static class BossRushMapSelectionHelper
    {
        public static bool HasPendingModeHEntryIntent() { return false; }
        public static bool HasPendingPrepaidTicket() { return false; }
    }
    static class ModeGAvailability { public static bool IsProductionReady { get { return false; } } public static bool AllowDevTestEntry { get { return false; } } }
    static class ModeGMapSupportRegistry { public static bool IsVerifiedSceneName(string name) { return false; } }
    static class FactionFlagConfig { public static readonly int[] ALL_FLAG_TYPE_IDS = { 500010 }; }
    public partial class ModBehaviour
    {
        private int bossRushTicketTypeId;
        public static void DevLog(string message) { }
        private bool IsPlayerNakedWithAllowedItems(string tag, int a, int b, bool flags)
        { return ModeEntryInventory.IsPlayerNakedWithAllowedItems(tag, a, b, flags); }
        private (int?, Item) DetectFactionFlag() { return (null, null); }
        private Item DetectBossRushTicketItem() { return ModeEntryInventory.FindFirstPlayerInventoryItemByTypeId(GetBossRushTicketTypeId()); }
        private Item DetectBloodhuntTransponder() { return null; }
        private Item DetectFateEchoRelic() { return null; }
        private bool IsPlayerNakedForModeF() { return false; }
        private bool IsModeGLoadoutEligible() { return false; }
        public void SetRegisteredTicket(int id) { bossRushTicketTypeId = id; }
        public bool ResolvesFromScratch() { return DetermineBossRushEntryMode("FreezeEntryIntent") == BossRushEntryMode.ModeD; }
    }
}
internal static class InventoryEntryProgram
{
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("ASSERT[inventory_entry] " + name); }
    public static int Main()
    {
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = new Item { Inventory = new Inventory() } };
        PetProxy.PetInventory = new Inventory();
        var bag = CharacterMainControl.Main.CharacterItem.Inventory;
        bag.Content.Add(new Item { TypeID = 500001, DisplayName = "ticket" });
        var host = new ModBehaviour();
        foreach (int registration in new[] { 0, -1, 500001 })
        {
            host.SetRegisteredTicket(registration);
            Check(host.IsPlayerNaked() && host.ResolvesFromScratch(), "published ticket resolves Mode D through real container traversal, registration=" + registration);
        }
        bag.Content.Add(new Item { TypeID = 451 });
        Check(!host.IsPlayerNaked() && !host.ResolvesFromScratch(), "extra inventory item keeps normal challenge selection");
        bag.Content.RemoveAt(1);
        CharacterMainControl.Main.CharacterItem.Slots.Content["Armor"] = new ItemStatsSystem.Items.Slot { Content = new Item { TypeID = 2 } };
        Check(!host.ResolvesFromScratch(), "equipped armor prevents naked entry");
        CharacterMainControl.Main.CharacterItem.Slots.Content.Clear();
        PetProxy.PetInventory.Content.Add(new Item { TypeID = 2 });
        Check(!host.ResolvesFromScratch(), "official dog inventory participates in naked entry");
        PetProxy.PetInventory.Content.Clear();
        Check(host.ResolvesFromScratch(), "removing blockers restores Mode D admission without cached mode");
        Console.WriteLine("ModeD inventory entry: PASS (real resolver, ticket bridge, inventory and equipment traversal)");
        return 0;
    }
}
