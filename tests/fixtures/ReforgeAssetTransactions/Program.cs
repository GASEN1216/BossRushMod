using System;
using BossRush;
using ItemStatsSystem;

class Program
{
    static int checks, failures;
    static void Check(bool result, string text)
    { checks++; if (!result) { failures++; Console.WriteLine("FAIL " + text); } }
    static Inventory Reset()
    {
        ReforgeUIManager.CancelPendingPropertyLock();
        Item.DropReject = Item.DropThrowAfter = NotificationText.Throw = false;
        ItemAssetsCollection.MissingPrefab = false;
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = new Item { Inventory = new Inventory() } };
        return CharacterMainControl.Main.CharacterItem.Inventory;
    }
    static void Click(Row row) { row.OnPointerClick(null); row.OnPointerClick(null); }
    static void Locks()
    {
        foreach (bool locking in new[] { false, true })
        foreach (int count in new[] { 1, 3 })
        foreach (bool notification in new[] { false, true })
        {
            Inventory pack = Reset();
            int typeId = locking ? ColdQuenchFluidConfig.TYPE_ID : TemperingSolventConfig.TYPE_ID;
            Item material = new Item { TypeID = typeId, StackCount = count }; pack.AddItem(material);
            var row = new Row { TargetItem = new Item(), IsLocked = !locking };
            row.TargetItem.SetFloat(PropertyLockSystem.LOCK_STAT_PREFIX + "damage", locking ? 0 : 1, true);
            if (notification)
            {
                if (count == 1) pack.AfterRemove = i => { throw new Exception("remove notification"); };
                else material.OnCount = i => { throw new Exception("count notification"); };
            }
            Click(row);
            Check(ItemFactory.GetItemCountInInventory(typeId) == (notification ? count : count - 1), "material conservation " + locking + "/" + count + "/" + notification);
            Check(PropertyLockSystem.IsPropertyLocked(row.TargetItem, "damage", PropertyType.Stat) == (notification ? !locking : locking), "lock matches charge " + locking + "/" + count + "/" + notification);
            if (notification)
            {
                pack.AfterRemove = null; material.OnCount = null;
                Click(row);
                Check(ItemFactory.GetItemCountInInventory(typeId) == count - 1, "retry consumes exactly one");
            }
        }
        foreach (bool after in new[] { false, true })
        {
            Inventory pack = Reset(); var material = new Item { TypeID = 500108 }; pack.AddItem(material);
            var row = new Row { TargetItem = new Item() };
            row.TargetItem.SetFloat(PropertyLockSystem.LOCK_STAT_PREFIX + "damage", 1, true);
            row.TargetItem.FloatThrowBefore = !after; row.TargetItem.FloatThrowAfter = after;
            Click(row);
            Check(ItemFactory.GetItemCountInInventory(500108) == (after ? 0 : 1), "variable commit boundary " + after);
        }
        Inventory rebound = Reset(); var solvent = new Item { TypeID = 500108 }; rebound.AddItem(solvent);
        var original = new Item(); original.SetFloat(PropertyLockSystem.LOCK_STAT_PREFIX + "damage", 1, true);
        var rebinding = new Row { TargetItem = original };
        rebound.AfterRemove = i => { rebinding.TargetItem = new Item(); };
        Click(rebinding);
        Check(solvent.InInventory == rebound && PropertyLockSystem.IsPropertyLocked(original, "damage", PropertyType.Stat), "row rebound during reservation restores original solvent");
        Inventory reentrant = Reset(); reentrant.AddItem(new Item { TypeID = 500108, StackCount = 2 });
        var sameRow = new Row { TargetItem = new Item() };
        sameRow.TargetItem.SetFloat(PropertyLockSystem.LOCK_STAT_PREFIX + "damage", 1, true);
        bool entered = false;
        reentrant.Content[0].OnCount = i => { if (!entered) { entered = true; Click(sameRow); } };
        Click(sameRow);
        Check(ItemFactory.GetItemCountInInventory(500108) == 1
            && !PropertyLockSystem.IsPropertyLocked(sameRow.TargetItem, "damage", PropertyType.Stat), "inventory notification cannot reenter the same lock transaction");
    }
    static void Gifts()
    {
        Inventory reentrant = Reset();
        var owner = new GoblinNPCController();
        bool reentered = false, nested = false;
        reentrant.AfterAdd = i => { if (!reentered) { reentered = true; nested = owner.TryGiveRewardItem(500108, 2); } };
        Check(owner.TryGiveRewardItem(500108, 2) && !nested && ItemFactory.GetItemCountInInventory(500108) == 2,
            "inventory observer cannot reenter gift delivery before claimed is recorded");
        foreach (int fault in new[] { 0, 1, 2, 3, 4, 5 })
        {
            Inventory pack = Reset();
            Item existing = null;
            if (fault == 1 || fault == 5) { existing = new Item { TypeID = 500108, StackCount = 1 }; pack.AddItem(existing); }
            if (fault == 0) pack.AfterAdd = i => { throw new Exception("add notification"); };
            if (fault == 1) existing.OnCount = i => { throw new Exception("merge notification"); };
            if (fault >= 2 && fault <= 4) pack.Reject = true;
            if (fault == 3) Item.DropThrowAfter = true;
            if (fault == 4) Item.DropReject = true;
            if (fault == 5) { existing.MaxStackCount = 2; pack.Reject = true; Item.DropReject = true; }
            NotificationText.Throw = true;
            bool claimed = false; var npc = new GoblinNPCController();
            for (int i = 0; i < 2; i++) if (!claimed && npc.TryGiveRewardItem(500108, 2, "success", "drop")) claimed = true;
            Check(claimed == (fault < 4), "gift actual receipt controls claimed " + fault);
            if (fault <= 1) Check(ItemFactory.GetItemCountInInventory(500108) == (fault == 1 ? 3 : 2), "no duplicate after inventory observer failure " + fault);
            if (fault == 5) Check(existing.StackCount == 1, "failed gift rolls back partial merge");
            if (fault == 4 || fault == 5) Check(ItemAssetsCollection.Last.IsBeingDestroyed, "unowned failed gift cleaned " + fault);
            if (fault == 2 || fault == 3) Check(ItemAssetsCollection.Last.ActiveAgent != null, "NPC drop receipt retained " + fault);
        }
    }
    static void Main() { Locks(); Gifts(); Console.WriteLine("checks=" + checks + " failures=" + failures); if (failures != 0) Environment.Exit(1); }
}
