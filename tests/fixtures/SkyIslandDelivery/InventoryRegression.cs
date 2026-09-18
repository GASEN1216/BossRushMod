using System;
using BossRush;
using ItemStatsSystem;

internal static class InventoryRegression
{
    private static Item Pack(int count)
    {
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = new Item { Inventory = new Inventory() } };
        var fiber = new Item { TypeID = BossRushItemIds.SkyIslandCloudmossFiber, StackCount = count };
        CharacterMainControl.Main.CharacterItem.Inventory.AddItem(fiber);
        return fiber;
    }

    internal static void Run(Action<bool, string> check)
    {
        string message;
        for (int count = 1; count <= 3; count += 2)
        {
            Item fiber = Pack(count);
            var gnats = new SkyIslandGnats();
            Inventory pack = CharacterMainControl.Main.CharacterItem.Inventory;
            Action<Item> fault = item => { throw new InvalidOperationException("inventory observer after mutation"); };
            if (count == 1) pack.AfterRemove = fault; else fiber.OnCount = fault;
            bool taken = false;
            Exception error = null;
            try { taken = gnats.TakeSpawn(out message); } catch (Exception e) { error = e; }
            check(error == null && !taken && !gnats.carryingSpawn && !fiber.IsBeingDestroyed
                && fiber.InInventory == pack && fiber.StackCount == count,
                "frogspawn failure restores original fiber and does not carry spawn: stack=" + count);
            pack.AfterRemove = null; fiber.OnCount = null;
            check(gnats.TakeSpawn(out message) && gnats.carryingSpawn && ItemFactory.GetItemCountInInventory(fiber.TypeID) == count - 1,
                "frogspawn retries once after inventory observer recovers: stack=" + count);
            check(!gnats.TakeSpawn(out message) && ItemFactory.GetItemCountInInventory(fiber.TypeID) == count - 1,
                "already carried spawn cannot consume again");
        }

        for (int invalidation = 0; invalidation < 2; invalidation++)
        {
            Item fiber = Pack(1);
            var gnats = new SkyIslandGnats();
            Inventory pack = CharacterMainControl.Main.CharacterItem.Inventory;
            pack.AfterRemove = item => { if (invalidation == 0) gnats.owner.session.IsReady = false; else gnats.owner.disposed = true; };
            check(!gnats.TakeSpawn(out message) && !gnats.carryingSpawn && fiber.InInventory == pack && !fiber.IsBeingDestroyed,
                "session shutdown during reserve restores fiber: " + invalidation);
        }

        Item retained = Pack(3);
        var reentry = new SkyIslandGnats();
        bool entered = false, nestedTaken = false;
        retained.OnCount = item =>
        {
            if (entered) return;
            entered = true;
            string nested;
            nestedTaken = reentry.TakeSpawn(out nested);
        };
        check(reentry.TakeSpawn(out message) && !nestedTaken && retained.StackCount == 2,
            "reentrant inventory notification cannot consume fiber twice");

        // 缺配装的补发入口仍须兑现必掉一件；装箱后通知出错不许销毁已交付原件。
        for (int fault = 0; fault < 4; fault++)
        {
            Inventory inventory = new Inventory { Capacity = 1 };
            Item existing = new Item(); inventory.AddItem(existing);
            inventory.AfterAdd = fault == 1 ? (Action<Item>)(item => { throw new Exception("post-attach notification"); }) : null;
            inventory.Reject = fault == 2;
            inventory.FailGrowth = fault == 3;
            bool sent = SkyIslandBossLoot.Give(new Item { Inventory = inventory }, BossRushItemIds.SkyIslandStarbrassVisorHelm);
            Item created = ItemAssetsCollection.Last;
            check(sent == (fault < 2) && inventory.Content[0] == existing && !existing.IsBeingDestroyed,
                "boss fallback preserves original loot and reports receipt: " + fault);
            check(fault < 2 ? created != null && created.InInventory == inventory && !created.IsBeingDestroyed
                : created.IsBeingDestroyed,
                "boss fallback retains delivered gear and destroys only failed delivery: " + fault);
        }
        ItemAssetsCollection.MissingPrefab = true;
        check(!SkyIslandBossLoot.Give(new Item { Inventory = new Inventory() }, BossRushItemIds.SkyIslandStarbrassVisorHelm),
            "missing gear prefab never creates fallback item");
        ItemAssetsCollection.MissingPrefab = false;

        // 共用事务也覆盖普通合成的材料归还、交付和合堆，不只验证新的一件扣料入口。
        Item material = Pack(3);
        SkyIslandInventoryTransaction reservation;
        check(SkyIslandInventoryTransaction.TryReserve(CharacterMainControl.Main,
            new[] { new SkyIslandIngredient(material.TypeID, 2) }, out reservation), "reserve partial stack for craft");
        reservation.Dispose(); reservation.Dispose();
        check(material.StackCount == 3, "uncommitted craft restores quantity once");
        var output = new Item { TypeID = material.TypeID, StackCount = 2 };
        check(SkyIslandInventoryTransaction.TryDeliver(output, CharacterMainControl.Main) && material.StackCount == 5
            && output.IsBeingDestroyed, "craft output uses real combine and receipt logic");
    }
}
