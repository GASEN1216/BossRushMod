using System;
using System.Collections.Generic;
using BossRush;
using ItemStatsSystem;
using UnityEngine;
internal static partial class Program
{
    private static long Physical(string owner, string asset, int type)
    {
        long count = 0;
        foreach (var box in InteractableLootbox.All)
            if (box != null && box.Owner == owner && box.Asset == asset && box.Inventory != null)
                foreach (var item in box.Inventory.Content) if (!item.Destroyed && item.TypeID == type) count += item.StackCount;
        return count;
    }
    private static void PersonalRewardRegression()
    {
        Room(); var host = BossRushCoop.Current;
        var worlds = new List<WavesArenaRuntimeModule>();
        foreach (var p in BossRushCoop.Peers) { BossRushCoop.Current = p; worlds.Add(new WavesArenaRuntimeModule(new ModBehaviour())); }
        BossRushCoop.Current = host; worlds[0].ArenaActive = true; worlds[0].Begin(); BossRushCoop.Flush();
        string original = host.AssetId;
        host.AssetId = "slot-B"; BossRushCoop.Peers[1].AssetId = "slot-B";
        BossRushCoop.Peers[2].ThrowAfterItem = true; BossRushCoop.Peers[2].ThrowAfterCredit = true;
        InteractableLootbox.All.Clear();
        worlds[0].Reward("waveReward", 25000001, itemType: 1253); BossRushCoop.Flush();
        Check(host.Money == 0 && BossRushCoop.Peers[1].Money == 0 && Physical("host", "slot-B", 1) == 0,
            "host and guest replacement assets cannot receive any original-wave cash or overflow");
        for (int i = 2; i < 16; i++) Check(BossRushCoop.Peers[i].Money + Physical(BossRushCoop.Peers[i].Id, original, 1) == 25000001,
            "each admitted participant preserves full single-player cash including own overflow " + i);
        host.AssetId = original; BossRushCoop.Peers[1].AssetId = original;
        for (int tick = 0; tick < 4; tick++)
        {
            for (int i = 0; i < 16; i++) { BossRushCoop.Current = BossRushCoop.Peers[i]; worlds[i].TickBoxes(); }
            BossRushCoop.Current = host; worlds[0].Tick(); BossRushCoop.Flush();
        }
        for (int i = 0; i < 16; i++)
        {
            var peer = BossRushCoop.Peers[i];
            Check(peer.Money == 13000001 && Physical(peer.Id, original, 1) == 12000000 && Physical(peer.Id, original, 1253) == 0,
                "12 physical piles and complete overflow remain on the same asset after retries " + i);
            // A non-cash reward has StackCount 0 in this boundary model; count exact instances instead.
            int extras = 0; foreach (var box in InteractableLootbox.All) if (box.Owner == peer.Id && box.Inventory != null)
                foreach (var item in box.Inventory.Content) if (item.TypeID == 1253 && !item.Destroyed) extras++;
            Check(extras == 1, "notification and ACK retries cannot duplicate the every-five-wave item " + i);
        }
        var late = new TestPeer { Id = "late" }; late.BindScene(); BossRushCoop.Peers.Add(late);
        BossRushCoop.Current = late; var lateWorld = new WavesArenaRuntimeModule(new ModBehaviour());
        BossRushCoop.Current = host; worlds[0].Command(new CoopGameplayCommand { PlayerId = late.Id, Action = "first", AssetId = late.AssetId });
        string asset;
        Check(!worlds[0].Participant(worlds[0].Epoch, late.Id, out asset), "real mid-wave sign admission cannot borrow the already frozen wave");
        worlds[0].Wave(); Check(worlds[0].Participant(worlds[0].Epoch, late.Id, out asset) && asset == original,
            "real next wave captures the newly admitted asset");
        var view = worlds[0].SnapshotState(); BossRushCoop.Current = late; lateWorld.World(view);
        Check(lateWorld.Participant(worlds[0].Epoch, late.Id, out asset) && asset == original,
            "actual arena mode snapshot preserves the same frozen cohort on clients");
        BossRushCoop.Current = host; worlds[0].Reward("milestone", tier: 10); BossRushCoop.Flush();
        for (int tick = 0; tick < 30; tick++)
        {
            for (int i = 0; i < 16; i++) { BossRushCoop.Current = BossRushCoop.Peers[i]; worlds[i].TickBoxes(); }
            BossRushCoop.Current = late; lateWorld.TickBoxes(); BossRushCoop.Current = host; worlds[0].Tick(); BossRushCoop.Flush();
        }
        long expected = 5120000000L + 51200;
        for (int i = 0; i < 16; i++)
        {
            var p = BossRushCoop.Peers[i];
            Check(p.Money - 13000001 + Physical(p.Id, original, 1) - 12000000 + Physical(p.Id, original, 1254) * 100 == expected,
                "tier ten includes its own full crowns cash and overflow, with no earlier-tier replay " + i);
        }
        foreach (var world in worlds) world.Dispose(); lateWorld.Dispose();
        Room();var cleanupWorld=new WavesArenaRuntimeModule(new ModBehaviour());cleanupWorld.ArenaActive=true;cleanupWorld.Begin();cleanupWorld.Reward("box",quality:10);BossRushCoop.Flush();
        var shadow=VictoryRewardShadowCrateController.Pending[VictoryRewardShadowCrateController.Pending.Count-1];
        Check(shadow!=null,"personal victory has an actual pending visual owner before module cleanup");cleanupWorld.RewardShutdown();
        Check(shadow==null,"module cleanup cancels unlanded personal visual owners before clearing reward references");cleanupWorld.Dispose();
        Room();host=BossRushCoop.Current;var partial=new WavesArenaRuntimeModule(new ModBehaviour());partial.ArenaActive=true;partial.Begin();host.RejectType=1253;
        partial.Reward("waveReward",25000001,itemType:1253);BossRushCoop.Flush();
        InteractableLootbox held=null;foreach(var box in InteractableLootbox.All)if(box!=null&&box.Inventory!=null&&box.Owner==host.Id&&box.Room==host.Run&&box.gameObject.Persistent)held=box;
        Check(held!=null&&!held.Published&&held.Inventory.Content.Count==12&&host.Money==13000001,"failed final item keeps the locked original physical cash plus exact remaining debt");
        var firstCash=held.Inventory.Content[0];SceneUnload();
        Check(held!=null&&held.Inventory.Content[0]==firstCash&&!firstCash.Destroyed,"uncompleted personal box preserves its same original objects through mandatory scene destruction");
        host.RejectType=0;partial.TickBoxes();partial.Tick();BossRushCoop.Flush();
        Check(held.Published&&!held.gameObject.Persistent&&held.Inventory.Content.Count==13&&host.Money==13000001,"retry publishes one complete original box without recreating physical cash or crediting overflow twice");partial.Dispose();
        Room();host=BossRushCoop.Current;var standard=new WavesArenaRuntimeModule(new ModBehaviour());standard.ArenaActive=true;standard.Begin();
        Duckov.Utilities.LootBoxLoader.Gate=new System.Threading.Tasks.TaskCompletionSource<bool>();Duckov.Utilities.LootBoxLoader.FailAfterFirst=true;
        standard.Reward("box",quality:3);BossRushCoop.Flush();shadow=VictoryRewardShadowCrateController.Pending[VictoryRewardShadowCrateController.Pending.Count-1];shadow.Land();
        var standardBox=standard.PersonalBoxes[0];standard.TickBoxes();Check(!standardBox.Published&&standardBox.gameObject.Persistent&&standardBox.Inventory.Content.Count==0,"actual async personal owner cannot publish or ACK a box merely because native setup started");
        SceneUnload();var gate=Duckov.Utilities.LootBoxLoader.Gate;Duckov.Utilities.LootBoxLoader.Gate=null;gate.SetResult(true);
        Check(!standardBox.Published&&standardBox.Inventory.Content.Count==1,"native async failure retains its exact first original item in the locked cross-scene box");
        var firstReward=standardBox.Inventory.Content[0];Time.unscaledTime+=2;standard.TickBoxes();standard.Tick();BossRushCoop.Flush();
        Check(standardBox.Published&&standardBox.Inventory.Content.Count==3&&standardBox.Inventory.Content.Contains(firstReward)&&!firstReward.Destroyed,"native retry and original cleanup complete the full three-item box without discarding the first committed reward");standard.Dispose();
        Room();var cancelled=new WavesArenaRuntimeModule(new ModBehaviour());cancelled.ArenaActive=true;cancelled.Begin();
        Duckov.Utilities.LootBoxLoader.Gate=new System.Threading.Tasks.TaskCompletionSource<bool>();
        cancelled.Reward("box",quality:3);BossRushCoop.Flush();shadow=VictoryRewardShadowCrateController.Pending[VictoryRewardShadowCrateController.Pending.Count-1];shadow.Land();
        var cancelledBox=cancelled.PersonalBoxes[0];cancelled.RewardShutdown();
        Check(cancelledBox!=null&&!cancelledBox.Published,"shutdown preserves a closed original inventory still held by the pending native factory");
        gate=Duckov.Utilities.LootBoxLoader.Gate;Duckov.Utilities.LootBoxLoader.Gate=null;gate.SetResult(true);
        Check(cancelledBox==null&&!cancelledBox.Published,"completed cancelled native continuation releases its original inventory without publishing or ACK");cancelled.Dispose();
    }
    private static void SceneUnload()
    {
        foreach(var box in InteractableLootbox.All)if(box!=null&&!box.gameObject.Persistent)UnityEngine.Object.Destroy(box.gameObject);
        UnityEngine.SceneManagement.SceneManager.Handle++;BossRushCoop.Current.Token="scene-next-"+UnityEngine.SceneManagement.SceneManager.Handle;
    }
}
