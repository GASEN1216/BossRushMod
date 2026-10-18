using System;
using BossRush;
using UnityEngine;
using ItemStatsSystem;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    public static void Main()
    {
        var point = new Vector3(33, 4, 19);
        CharacterMainControl.Main = new CharacterMainControl();
        var host = CharacterMainControl.Main;
        var solo = new ZombieModeRuntimeModule();
        solo.Deliver(point);
        Check(host.CharacterItem.Inventory.Calls == 1 && ItemAssetsCollection.Last.GroundDrops == 0 && solo.Pickups == 1, "solo supplies still go directly to inventory");
        host.MaxWeight = 1;
        solo.Deliver(point);
        Check(host.CharacterItem.Inventory.Calls == 1 && ItemAssetsCollection.Last.FeetDrops == 1 && solo.Spotted == 1, "solo overweight keeps the original feet-drop path");

        BossRushCoop.IsConnected = true; BossRushCoop.Host = true;
        host.MaxWeight = 50;
        var world = new ZombieModeRuntimeModule();
        var obj = world.Deliver(point, true, true);
        var item = ItemAssetsCollection.Last;
        Check(host.CharacterItem.Inventory.Calls == 1 && world.Pickups == 0, "co-op enemy supplies must not silently enter host inventory");
        Check(item.GroundDrops == 1 && item.FeetDrops == 0 && item.DropPosition.x == point.x && item.DropPosition.y == point.y + 0.35f && item.DropPosition.z == point.z, "co-op uses one native Item.Drop at the original corpse position");
        Check(item.Force == 30 && world.Registered.Count == 1 && ReferenceEquals(world.Registered[0], obj), "native dropped item remains registered for run-only cleanup");
        Check(!item.MarkedAtGroundDrop, "boss-exclusive loot does not enter ordinary shared claims");
        var candidate = world.runState.EntityDropCleanupCandidates[0];
        Check(candidate.HighValue && candidate.BossDrop && candidate.WaveAtSpawn == 7 && candidate.SpawnTime == 20, "cleanup retains the original value, wave and drop metadata");
        host.Dead = true; CharacterMainControl.Main = null;
        world.Deliver(point);
        Check(ItemAssetsCollection.Last.GroundDrops == 1 && ItemAssetsCollection.Last.Force == 18 && world.Registered.Count == 2, "living guest's world drops continue with host body unavailable");
        Check(ItemAssetsCollection.Last.MarkedAtGroundDrop, "ordinary shared drop is marked before native Drop captures its snapshot");

        int calls = ItemAssetsCollection.Calls;
        BossRushCoop.Host = false;
        Check(world.Deliver(point) == null && ItemAssetsCollection.Calls == calls, "client replicas cannot instantiate a second set of drops");
        BossRushCoop.Host = true; world.Valid = false;
        Check(world.Deliver(point) == null && ItemAssetsCollection.Calls == calls, "retired run cannot create supplies");
        Console.WriteLine("ZombieCoopDrops PASS " + checks + " production delivery assertions; native sync/physics are isolated");
    }
}
