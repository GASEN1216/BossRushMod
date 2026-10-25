using System;
using System.Collections.Generic;
using BossRush;
using Duckov.ItemUsage;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

internal static partial class Program
{
    private static Item NewItem(int typeId = 100)
    {
        var item = new GameObject().AddComponent<Item>();
        item.TypeID = typeId; item.Quality = 1; item.Value = 100; item.CanBeSold = true;
        item.Stackable = true; item.StackCount = 24; item.MaxStackCount = 100;
        item.Tags = new HashSet<string>(); item.Slots = new List<object>();
        return item;
    }
    private static CharacterMainControl NewPlayer(out Inventory inventory)
    {
        ItemWishlist.Instance = null;
        var player = new GameObject().AddComponent<CharacterMainControl>();
        player.CharacterItem = NewItem(1);
        inventory = new GameObject().AddComponent<Inventory>();
        inventory.Content = new List<Item> { null, null, null, null };
        inventory.Locked = new HashSet<int>();
        player.CharacterItem.Inventory = inventory;
        return player;
    }
    private static Item Ammunition(Inventory inventory, int slot, int count = 24)
    {
        var item = NewItem(); item.StackCount = count; item.gameObject.AddComponent<ItemSetting_Bullet>();
        inventory.AddAt(item, slot); return item;
    }
    private static void RejectedItem(Action<Item, Inventory> protect, string label)
    {
        Inventory inventory; NewPlayer(out inventory);
        Item item = Ammunition(inventory, 0);
        protect(item, inventory);
        Check(!GreedyMimicInventory.Eligible(item, inventory, 0, GreedyMimicFood.Ammunition), label);
    }
    private static void InventoryEligibilityAndSelection()
    {
        RejectedItem((item, pack) => pack.Locked.Add(0), "locked inventory slot is never offered as mimic food");
        RejectedItem((item, pack) => item.Sticky = true, "sticky items are protected from mimic feeding");
        RejectedItem((item, pack) => item.CanBeSold = false, "unsellable items are protected from mimic feeding");
        RejectedItem((item, pack) => item.Quality = 0, "invalid low quality is protected from mimic feeding");
        RejectedItem((item, pack) => item.TypeID = 0, "invalid type ids are protected from mimic feeding");
        RejectedItem((item, pack) => item.TypeID = BossRushItemIds.BossRushTicket, "custom item id range is protected from mimic feeding");
        RejectedItem((item, pack) => item.Tags.Add("Quest"), "quest tags are protected from mimic feeding");
        RejectedItem((item, pack) => item.Tags.Add("Special"), "special tags are protected from mimic feeding");
        RejectedItem((item, pack) => item.Inventory = new GameObject().AddComponent<Inventory>(), "containers are protected from mimic feeding");
        RejectedItem((item, pack) => item.Slots.Add(new object()), "items with equipment slots are protected from mimic feeding");
        RejectedItem((item, pack) => item.StackCount = 11, "insufficient ammunition is never offered");
        RejectedItem((item, pack) => item.InInventory = null, "stale inventory pointers cannot authorize feeding");
        RejectedItem((item, pack) => pack.Content[0] = NewItem(), "a replaced selected slot cannot authorize feeding the old item");
        RejectedItem((item, pack) => item.IsBeingDestroyed = true, "items already being destroyed cannot be fed");
        RejectedItem((item, pack) => UnityEngine.Object.Destroy(item.gameObject), "destroying the item game object invalidates its component for feeding");
        RejectedItem((item, pack) => ItemWishlist.Instance = new ItemWishlist { Wanted = new HashSet<int> { item.TypeID } },
            "manually wishlisted items are protected from mimic feeding");

        Inventory highPack; NewPlayer(out highPack);
        Item highQuality = Ammunition(highPack, 0); highQuality.Quality = 8;
        Check(GreedyMimicInventory.Eligible(highQuality, highPack, 0, GreedyMimicFood.Ammunition),
            "high quality food is eligible since 2026-10-25; selection still prefers the cheapest match");

        Inventory inventory; CharacterMainControl player = NewPlayer(out inventory);
        Item expensive = Ammunition(inventory, 0); expensive.Value = 200;
        Item cheap = Ammunition(inventory, 1); cheap.Value = 10;
        Item higherQuality = Ammunition(inventory, 2); higherQuality.Quality = 2; higherQuality.Value = 1;
        Item cheapestLocked = Ammunition(inventory, 3); cheapestLocked.Value = 1; inventory.Locked.Add(3);
        int slot;
        Check(ReferenceEquals(GreedyMimicInventory.Find(player, GreedyMimicFood.Ammunition, out slot), cheap) && slot == 1,
            "food selection prefers lowest eligible quality then price and skips protected stock");
        Check(!GreedyMimicInventory.Eligible(cheap, inventory, -1, GreedyMimicFood.Ammunition), "negative selected slots are rejected");
        Check(!GreedyMimicInventory.Eligible(cheap, inventory, 1, (GreedyMimicFood)3), "invalid food category cannot consume a valid item");
        Check(GreedyMimicInventory.Find(null, GreedyMimicFood.Medicine, out slot) == null && slot == -1, "missing player produces no food offer");
        int growth = 0;
        inventory.Content[1] = null;
        Check(!GreedyMimicInventory.Consume(cheap, player, 1, GreedyMimicFood.Ammunition, () => growth++) && growth == 0,
            "changing the selected slot after the menu closes blocks consumption");

        player = NewPlayer(out inventory);
        Item medicine = NewItem(101); medicine.Stackable = false; medicine.UseDurability = true;
        medicine.Durability = 100f; medicine.MaxDurabilityWithLoss = 100f;
        var child = new GameObject(); medicine.gameObject.Children.Add(child); child.AddComponent<Drug>().healValue = 20f;
        inventory.AddAt(medicine, 0);
        Check(GreedyMimicInventory.Eligible(medicine, inventory, 0, GreedyMimicFood.Medicine), "unused healing medicine in a child component is eligible");
        medicine.Durability = 99f;
        Check(!GreedyMimicInventory.Eligible(medicine, inventory, 0, GreedyMimicFood.Medicine), "partially used medical supplies are protected");
        medicine.Durability = 100f; child.GetComponent<Drug>().healValue = 0;
        Check(!GreedyMimicInventory.Eligible(medicine, inventory, 0, GreedyMimicFood.Medicine), "nonhealing drugs cannot teach recovery");

        Item explosive = NewItem(102); inventory.AddAt(explosive, 1);
        var skill = explosive.gameObject.AddComponent<ItemSetting_Skill>();
        var grenade = new Skill_Grenade { createExplosion = true, damageInfo = new DamageInfo { damageValue = 20f } };
        skill.Skill = grenade; skill.onRelease = ItemSetting_Skill.OnReleaseAction.reduceCount;
        Check(GreedyMimicInventory.Eligible(explosive, inventory, 1, GreedyMimicFood.Explosive), "consumable damaging grenades are eligible");
        grenade.createExplosion = false;
        Check(!GreedyMimicInventory.Eligible(explosive, inventory, 1, GreedyMimicFood.Explosive), "utility throwables cannot teach explosive attacks");
        grenade.createExplosion = true; grenade.damageInfo = new DamageInfo();
        Check(!GreedyMimicInventory.Eligible(explosive, inventory, 1, GreedyMimicFood.Explosive), "zero damage grenades cannot teach explosive attacks");
        grenade.damageInfo = new DamageInfo { damageValue = 20f }; skill.onRelease = ItemSetting_Skill.OnReleaseAction.none;
        Check(!GreedyMimicInventory.Eligible(explosive, inventory, 1, GreedyMimicFood.Explosive), "reusable grenade abilities are protected from consumption");
    }

    private static void InventoryConsumptionCommitAndRollback()
    {
        Inventory pack; CharacterMainControl player = NewPlayer(out pack);
        Item ammo = Ammunition(pack, 0);
        int growth = 0;
        Check(GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () =>
        {
            Check(ammo.StackCount == 12 && ammo.InInventory == pack, "partial stack deduction commits before visible growth");
            growth++;
        }), "partial stack meal succeeds");
        Check(ammo.StackCount == 12 && growth == 1 && ammo.DestroyCalls == 0, "partial stack meal preserves the remaining original item");
        Check(GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () =>
        {
            Check(pack.GetItemAt(0) == null && !SkyIslandInventoryTransaction.HasOwner(ammo) && ammo.DestroyCalls == 0,
                "exact stack is reserved as the same undestroyed original before growth");
        }), "exact stack meal succeeds");
        Check(ammo.DestroyCalls == 1 && ammo == null && ammo.gameObject.Destroyed, "exact stack is destroyed only after growth succeeds");

        player = NewPlayer(out pack); ammo = Ammunition(pack, 0);
        int writes = 0;
        ammo.AfterStackWrite = count => { if (++writes == 1) throw new ApplicationException("post-commit count notification"); };
        Check(GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () => { }),
            "stack notification exception after deduction still commits the meal");
        Check(ammo.StackCount == 12 && writes == 1, "post-commit stack exception cannot duplicate or refund consumed ammo");

        player = NewPlayer(out pack); ammo = Ammunition(pack, 0); growth = 0;
        ammo.BeforeStackWrite = count => { throw new ApplicationException("pre-commit count failure"); };
        Check(!GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () => growth++),
            "stack exception before deduction prevents growth");
        Check(ammo.StackCount == 24 && growth == 0, "failed stack mutation leaves all original ammo intact");

        player = NewPlayer(out pack); ammo = Ammunition(pack, 0, 12);
        pack.AfterRemove = item => { throw new ApplicationException("post-commit removal notification"); };
        Check(GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () => { }),
            "removal notification exception after reservation still commits the meal");
        Check(ammo.DestroyCalls == 1 && pack.GetItemAt(0) == null, "post-commit removal destroys only the reserved original");

        player = NewPlayer(out pack); ammo = Ammunition(pack, 0, 12); growth = 0;
        pack.BeforeRemove = item => { throw new ApplicationException("pre-commit removal failure"); };
        Check(!GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () => growth++),
            "removal exception before reservation prevents growth");
        Check(ReferenceEquals(pack.GetItemAt(0), ammo) && ammo.DestroyCalls == 0 && growth == 0,
            "failed reservation leaves the same original item in its slot");

        player = NewPlayer(out pack); ammo = Ammunition(pack, 0);
        Check(!GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, null) && ammo.StackCount == 24,
            "missing growth callback refunds the partial deduction");
        Check(!GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () =>
        {
            ammo.StackCount -= 2;
            throw new ApplicationException("growth failure after unrelated external consumption");
        }), "growth exception rolls back its own consumption");
        Check(ammo.StackCount == 22, "partial rollback preserves unrelated external count changes");

        player = NewPlayer(out pack); ammo = Ammunition(pack, 0, 12);
        pack.AfterAddAt = (item, slot) => { throw new ApplicationException("post-commit restore notification"); };
        Check(!GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () => { throw new ApplicationException("model failed"); }),
            "growth failure returns false even when restore notification also fails");
        Check(ReferenceEquals(pack.GetItemAt(0), ammo) && ammo.InInventory == pack && ammo.DestroyCalls == 0 && ammo.DropCalls == 0,
            "rollback after notification failure restores the same original without a duplicate drop");

        player = NewPlayer(out pack); ammo = Ammunition(pack, 0, 12);
        Item replacement = NewItem(999);
        Check(!GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () =>
        {
            pack.AddAt(replacement, 0); throw new ApplicationException("growth failed after slot reuse");
        }), "growth failure with a reused slot rolls back through another free slot");
        Check(ReferenceEquals(pack.GetItemAt(0), replacement) && ReferenceEquals(pack.GetItemAt(1), ammo)
            && ammo.DestroyCalls == 0, "rollback preserves the replacement and returns the original to another slot");

        player = NewPlayer(out pack); ammo = Ammunition(pack, 0, 12); pack.RejectAddAt = true;
        Check(!GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () => { throw new ApplicationException("growth failed"); }),
            "restore insertion rejection still reports a failed meal");
        Check(ammo.ActiveAgent != null && ammo.DestroyCalls == 0 && ammo.DropCalls == 1,
            "failed AddAt rollback must drop the same original instead of leaving it ownerless");

        player = NewPlayer(out pack); ammo = Ammunition(pack, 0, 12);
        pack.BeforeAddAt = (item, slot) => { throw new ApplicationException("pre-commit restore failure"); };
        Check(!GreedyMimicInventory.Consume(ammo, player, 0, GreedyMimicFood.Ammunition, () => { throw new ApplicationException("growth failed"); }),
            "restore insertion exception still reports a failed meal");
        Check(ammo.ActiveAgent != null && ammo.DestroyCalls == 0 && ammo.DropCalls == 1,
            "pre-commit restore exception must fall back to dropping the same original");
    }

    private static void InventoryRewardOwnership()
    {
        const int rewardId = 200;
        var created = new List<Item>();
        Item prefab = NewItem(rewardId);
        ItemAssetsCollection.PrefabLookup = id => id == rewardId ? prefab : null;
        ItemAssetsCollection.Factory = id => { Item item = NewItem(id); item.MaxStackCount = 10; created.Add(item); return item; };
        GreedyMimicInventory.Reward(rewardId, 21, new Vector3());
        Check(created.Count == 3 && created[0].StackCount == 10 && created[1].StackCount == 10 && created[2].StackCount == 1,
            "mimic reward splits the requested amount across real item stack limits");
        foreach (Item item in created)
            Check(item.ActiveAgent != null && item.DestroyCalls == 0 && item.DropCalls == 1,
                "successfully dropped rewards retain their ground owner and are never destroyed");

        Item afterDrop = null;
        ItemAssetsCollection.Factory = id =>
        {
            afterDrop = NewItem(id); afterDrop.AfterDrop = () => { throw new ApplicationException("post-drop notification"); };
            return afterDrop;
        };
        try { GreedyMimicInventory.Reward(rewardId, 1, new Vector3()); }
        catch (ApplicationException) { }
        Check(afterDrop != null && afterDrop.ActiveAgent != null && afterDrop.DestroyCalls == 0,
            "reward notification exception after ground ownership cannot destroy the delivered item");

        Item beforeDrop = null;
        ItemAssetsCollection.Factory = id =>
        {
            beforeDrop = NewItem(id); beforeDrop.BeforeDrop = () => { throw new ApplicationException("pre-drop failure"); };
            return beforeDrop;
        };
        try { GreedyMimicInventory.Reward(rewardId, 1, new Vector3()); }
        catch (ApplicationException) { }
        Check(!ReferenceEquals(beforeDrop, null) && beforeDrop.DestroyCalls == 1 && beforeDrop == null,
            "undelivered reward is destroyed after drop failure");

        int factoryCalls = 0;
        ItemAssetsCollection.PrefabLookup = id => null;
        ItemAssetsCollection.Factory = id => { factoryCalls++; return NewItem(1); };
        try { GreedyMimicInventory.Reward(rewardId, 1, new Vector3()); }
        catch (Exception) { }
        Check(factoryCalls == 0, "unregistered rewards cannot instantiate a fallback item");
        ItemAssetsCollection.Factory = null; ItemAssetsCollection.PrefabLookup = null;
    }
}
