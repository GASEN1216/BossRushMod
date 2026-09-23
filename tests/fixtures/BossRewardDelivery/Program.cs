using System;
using BossRush;
using ItemStatsSystem;

internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string reason)
    { checks++; if (!ok) throw new Exception(reason); }
    private static void Reset()
    {
        UnityEngine.Random.value = 0f; Item.ThrowOnDrop = false;
        BackMountainItems.Registered = true; BackMountainUnlocks.Unlocked = true;
        ItemAssetsCollection.Last = null; ItemAssetsCollection.Creates = 0;
        ItemAssetsCollection.ReturnNull = false;
        AchievementTracker.Collections = 0; ModBehaviour.Logs.Clear();
    }
    private static Inventory Full()
    {
        var inv = new Inventory { Capacity = 8 };
        for (int i = 0; i < 8; i++) inv.AddItem(new Item { TypeID = i + 1 });
        return inv;
    }
    private static void Main()
    {
        var owner = new ModBehaviour();
        string[] bosses = { "descendant", "king", "PhantomWitch" };
        int[] seeds = { 500062, 500063, 500064 };
        for (int i = 0; i < bosses.Length; i++)
        {
            Reset(); var inv = Full(); Item first = inv.Content[0];
            owner.Seed(inv, bosses[i]);
            Check(inv.Content.Count == 9 && inv.Content[8].TypeID == seeds[i], "full box lost seed: " + bosses[i]);
            Check(inv.Capacity == 9 && inv.Growths == 1 && inv.Content[0] == first, "extra reward replaced original loot");
            Check(!ItemAssetsCollection.Last.Destroyed, "delivered seed destroyed");
        }
        Reset(); var hole = Full(); hole.Content[3] = null;
        owner.Seed(hole, "king");
        Check(hole.Capacity == 8 && hole.Content[3].TypeID == 500063 && hole.Growths == 0, "empty slot must be reused");
        Reset(); var space = new Inventory(); owner.Seed(space, "king");
        Check(space.Capacity == 8 && space.Growths == 0, "non-full box needlessly expanded");

        for (int fault = 0; fault < 3; fault++)
        {
            Reset(); var inv = Full();
            inv.Reject = fault == 0; inv.ThrowBefore = fault == 1; inv.FailGrowth = fault == 2;
            owner.Seed(inv, "king");
            Check(inv.Content.Count == 8 && ItemAssetsCollection.Last.Destroyed, "failed seed leaked instance");
            Check(!ModBehaviour.Logs.Exists(x => x.Contains("掉落菜地种子:")), "failed seed logged success");
        }
        Reset(); var observed = Full(); observed.ThrowAfter = true; owner.Seed(observed, "king");
        Check(observed.Content.Count == 9 && !ItemAssetsCollection.Last.Destroyed, "observer exception destroyed delivered seed");

        for (int gate = 0; gate < 5; gate++)
        {
            Reset(); owner.Enabled = gate != 0; BackMountainUnlocks.Unlocked = gate != 1;
            BackMountainItems.Registered = gate != 2; UnityEngine.Random.value = gate == 3 ? .26f : 0f;
            owner.Seed(Full(), gate == 4 ? "ordinary" : "king");
            Check(ItemAssetsCollection.Creates == 0, "seed bypassed existing gate: " + gate);
        }
        owner.Enabled = true;
        Reset(); ItemAssetsCollection.ReturnNull = true; owner.Seed(Full(), "king");
        Check(!ModBehaviour.Logs.Exists(x => x.Contains("掉落菜地种子:")), "missing prefab logged success");

        // 官方 / 原生掉落箱路径（随机 Boss 掉落关闭、Mode E/F、找不到奖励箱模板）：种子进 characterItem，官方随后按它建箱。
        for (int i = 0; i < bosses.Length; i++)
        {
            Reset(); var official = Full();
            owner.SeedOfficialBox(ModBehaviour.Boss(bosses[i], official));
            Check(official.Content.Count == 9 && official.Content[8].TypeID == seeds[i] && !ItemAssetsCollection.Last.Destroyed,
                "official loot box path lost seed: " + bosses[i]);
        }
        Reset(); owner.SeedOfficialBox(ModBehaviour.Boss("king", null));
        Check(ItemAssetsCollection.Creates == 0, "missing characterItem must not create a seed");
        Reset(); UnityEngine.Random.value = .26f; var unlucky = Full(); owner.SeedOfficialBox(ModBehaviour.Boss("king", unlucky));
        Check(ItemAssetsCollection.Creates == 0 && unlucky.Content.Count == 8, "official box path keeps the 25% roll");

        // 无间炼狱：没有任何箱子，种子落在尸体处。
        Reset(); var worldBoss = ModBehaviour.Boss("PhantomWitch", null); owner.SeedWorld(worldBoss);
        Check(ItemAssetsCollection.Last != null && ItemAssetsCollection.Last.TypeID == 500064 && ItemAssetsCollection.Last.Dropped
            && ItemAssetsCollection.Last.DropPosition.x == 3 && ItemAssetsCollection.Last.DropPosition.z == 4 && !ItemAssetsCollection.Last.Destroyed,
            "infinite hell seed must drop at the boss body");
        for (int gate = 0; gate < 5; gate++)
        {
            Reset(); owner.Enabled = gate != 0; BackMountainUnlocks.Unlocked = gate != 1;
            BackMountainItems.Registered = gate != 2; UnityEngine.Random.value = gate == 3 ? .26f : 0f;
            owner.SeedWorld(ModBehaviour.Boss(gate == 4 ? "ordinary" : "king", null));
            Check(ItemAssetsCollection.Creates == 0, "world seed bypassed existing gate: " + gate);
        }
        owner.Enabled = true;
        Reset(); Item.ThrowOnDrop = true; owner.SeedWorld(ModBehaviour.Boss("king", null));
        Check(ItemAssetsCollection.Last.Destroyed && !ModBehaviour.Logs.Exists(x => x.Contains("掉落菜地种子（世界掉落）")), "failed world drop leaked the seed instance");

        for (int kind = 0; kind < 2; kind++)
        {
            Reset(); var inv = Full();
            if (kind == 0) owner.Descendant(inv); else Check(owner.King(inv), "king delivery rejected");
            Check(inv.Content.Count == 9 && AchievementTracker.Collections == 1, "dragon reward or collection lost");
            Check(ItemAssetsCollection.Last.Durability == 100 && ItemAssetsCollection.Last.DurabilityLoss == 0,
                "dragon reward lost full durability");
            Reset(); inv = Full(); inv.Reject = true;
            if (kind == 0) owner.Descendant(inv); else Check(!owner.King(inv), "king falsely accepted failed delivery");
            Check(ItemAssetsCollection.Last.Destroyed && AchievementTracker.Collections == 0,
                "failed dragon reward leaked or awarded collection");
        }
        Reset(); var original = Full(); Item attached = original.Content[0];
        Check(!InteractableLootboxInventoryHelper.TryAddExtraItem(new Inventory(), attached)
            && !attached.Destroyed && attached.InInventory == original, "helper stole already owned item");
        var orphan = new Item();
        Check(!InteractableLootboxInventoryHelper.TryAddExtraItem(null, orphan) && orphan.Destroyed, "missing destination leaked item");
        Console.WriteLine("BossRewardDelivery: " + checks + " assertions PASS");
    }
}
