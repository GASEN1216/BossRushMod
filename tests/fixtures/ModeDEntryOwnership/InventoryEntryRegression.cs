// Links the real naked inventory traversal and entry resolver; only official containers/other-mode availability are hosts.
using System;
using System.Collections.Generic;
using BossRush;
using ItemStatsSystem;

namespace ItemStatsSystem
{
    public class Item { public int TypeID; public string DisplayName; public Inventory Inventory; public Slots Slots = new Slots(); }
    public class Inventory { public List<Item> Content = new List<Item>(); public bool Loading; }
    public class Slots
    {
        public readonly Dictionary<string, Items.Slot> Content = new Dictionary<string, Items.Slot>();
        public Items.Slot GetSlot(string name) { Items.Slot slot; return Content.TryGetValue(name, out slot) ? slot : null; }
    }
}
namespace ItemStatsSystem.Items { public class Slot { public Item Content; } }
public class CharacterMainControl { public static CharacterMainControl Main; public Item CharacterItem; }
public static class PetProxy { public static Inventory PetInventory; }
namespace UnityEngine { public static class Time { public static float unscaledTime; } }
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
    public class PetNestPetRecord { public int Items; }
    static class PetNestService
    {
        public static PetNestPetRecord DeployedPet;
        public static bool HasBackpackItems(PetNestPetRecord pet) { return pet != null && pet.Items > 0; }
    }
    static class L10n { public static string T(string cn, string en) { return en; } }
    static class FactionFlagConfig { public static readonly int[] ALL_FLAG_TYPE_IDS = { 500010 }; }
    public partial class ModBehaviour
    {
        private int bossRushTicketTypeId;
        public static void DevLog(string message) { }
        public static readonly List<string> Messages = new List<string>();
        public void ShowMessage(string message) { Messages.Add(message); }
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

        // 2026-10-12：狗子背包跨局保留，挡路时必须明说是哪件东西；船票放狗子背包也算合规。
        ModBehaviour.Messages.Clear();
        PetProxy.PetInventory.Content.Add(new Item { TypeID = 2, DisplayName = "bandage" });
        Check(!host.IsPlayerNaked(), "dog pack item blocks naked entry");
        Check(ModBehaviour.Messages.Count == 1 && ModBehaviour.Messages[0].Contains("dog") && ModBehaviour.Messages[0].Contains("bandage"),
            "dog pack blocker is named to the player");
        Check(!host.IsPlayerNaked() && ModBehaviour.Messages.Count == 1, "same blocker is not repeated within the dedupe window");
        UnityEngine.Time.unscaledTime += 30f;
        Check(!host.IsPlayerNaked() && ModBehaviour.Messages.Count == 2, "same blocker is shown again after the window");
        PetProxy.PetInventory.Content.Clear();
        PetProxy.PetInventory.Content.Add(new Item { TypeID = 500001, DisplayName = "ticket" });
        Check(host.IsPlayerNaked() && host.ResolvesFromScratch(), "ticket kept in the dog pack is allowed");
        PetProxy.PetInventory.Content.Clear();
        PetProxy.PetInventory.Loading = true;
        Check(!host.IsPlayerNaked(), "dog pack still loading is not treated as empty");
        PetProxy.PetInventory.Loading = false;
        PetNestService.DeployedPet = new PetNestPetRecord { Items = 1 };
        int before = ModBehaviour.Messages.Count;
        Check(!host.IsPlayerNaked() && ModBehaviour.Messages.Count == before + 1 && ModBehaviour.Messages[before].Contains("pet"),
            "deployed PetNest backpack blocks naked entry and says so");
        PetNestService.DeployedPet = null;
        // 2026-10-14：只剩饰品（面罩 / 耳机 / 图腾）挡路也要说清；手里有武器的普通挑战玩家照旧不提示。
        before = ModBehaviour.Messages.Count;
        var slots = CharacterMainControl.Main.CharacterItem.Slots.Content;
        slots["Totem1"] = new ItemStatsSystem.Items.Slot { Content = new Item { TypeID = 3, DisplayName = "totem" } };
        Check(!host.IsPlayerNaked() && ModBehaviour.Messages.Count == before + 1 && ModBehaviour.Messages[before].Contains("totem"),
            "accessory-only blocker is named to the player");
        slots["PrimaryWeapon"] = new ItemStatsSystem.Items.Slot { Content = new Item { TypeID = 4, DisplayName = "gun" } };
        UnityEngine.Time.unscaledTime += 30f;
        before = ModBehaviour.Messages.Count;
        Check(!host.IsPlayerNaked() && ModBehaviour.Messages.Count == before, "armed players wearing a totem get no From Scratch notice");
        slots.Clear();
        before = ModBehaviour.Messages.Count;
        CharacterMainControl.Main.CharacterItem.Slots.Content["Armor"] = new ItemStatsSystem.Items.Slot { Content = new Item { TypeID = 2 } };
        Check(!host.IsPlayerNaked() && ModBehaviour.Messages.Count == before, "geared normal-challenge players get no From Scratch notice");
        CharacterMainControl.Main.CharacterItem.Slots.Content.Clear();
        Check(host.ResolvesFromScratch(), "clearing every container restores Mode D admission");
        Console.WriteLine("ModeD inventory entry: PASS (real resolver, ticket bridge, inventory and equipment traversal)");
        return 0;
    }
}
