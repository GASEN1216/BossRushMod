using System;
namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        private int NativeReturns;
        private void PrimeDeliveryClient()
        {
            coopZombieLocalAsset = BossRushCoop.LocalAssetId; coopZombieLocalRun = runState.RunId;
            coopZombieStarterChosen = coopZombieStarterGranted = true;
            coopZombiePersonal.Bind(ApplyCoopZombiePersonal);
            ApplyCoopZombiePersonal(new CoopZombiePersonal { Run = runState.RunId, Kind = "points", Amount = 7 });
        }
        internal static void RunDeliveryChain()
        {
            BossRushCoop.Players.Clear();
            foreach (string id in new[] { "host", "online", "offline" }) BossRushCoop.Players.Add(new BossRushCoopPlayer { Id = id, AssetId = id + "-original" });
            BossRushCoop.Select("host"); var host = new ZombieModeRuntimeModule(); host.PrimeDeliveryClient();
            foreach (var player in BossRushCoop.Players) { host.coopZombieParticipants[player.Id] = player.AssetId; host.coopZombieStarterReady.Add(CoopZombieParticipantKey(player)); host.coopZombiePointsIssued[CoopZombieParticipantKey(player)] = 7; }
            BossRushCoop.Select("online"); var online = new ZombieModeRuntimeModule(); online.PrimeDeliveryClient();
            BossRushCoop.Select("offline"); var offline = new ZombieModeRuntimeModule(); offline.PrimeDeliveryClient();
            BossRushCoop.Select("host"); host.runState.ExtractionChanneling = true; host.OnUpdate(1, 1); BossRushCoop.FindPlayer("offline").IsConnected = false;
            host.CompleteZombieModeExtractionSuccess(11); host.OnUpdate(1, 1);
            Check(host.NativeReturns == 0, "Z waits for online original-asset extraction ACK");
            Check(host.coopZombiePersonal.Actual.HasPendingDeliveries, "Z offline debt survives Complete and Tick");
            var onlinePacket = BossRushCoop.Take("online", false); BossRushCoop.Dispatch(onlinePacket);
            Check(online.runState.CombatPhase == ZombieModeCombatPhase.SuccessExit && Duckov.Economy.EconomyManager.Cash == 14, "Z actual online personal settlement paid full points");
            BossRushCoop.Dispatch(BossRushCoop.Take("host", true)); host.OnUpdate(1, 1);
            Check(host.NativeReturns == 1 && host.coopZombiePersonal.Actual.HasPendingDeliveries, "Z native return proceeds while offline original debt remains");
            BossRushCoop.SceneGeneration = 2; BossRushCoop.HasBoundScene = false;
            var reconnect = BossRushCoop.FindPlayer("offline"); reconnect.IsConnected = true; reconnect.AssetId = "new-slot";
            host.OnUpdate(1, 1); var oldReceipt = BossRushCoop.Take("offline", false); BossRushCoop.Dispatch(oldReceipt);
            Check(Duckov.Economy.EconomyManager.Cash == 14 && !BossRushCoop.Packets.Exists(p => p.Target == "host"), "Z wrong slot cannot consume or ACK retained debt");
            reconnect.AssetId = "offline-original"; BossRushCoop.Select("host"); host.OnUpdate(1, 1);
            var original = BossRushCoop.Take("offline", false); BossRushCoop.Dispatch(original);
            Check(Duckov.Economy.EconomyManager.Cash == 21, "Z original reconnect receives full seven points after generation change");
            BossRushCoop.Dispatch(original); Check(Duckov.Economy.EconomyManager.Cash == 21, "Z real delivery replay cannot duplicate cash");
            BossRushCoop.Dispatch(BossRushCoop.Take("host", true)); host.OnUpdate(1, 1);
            Check(!host.coopZombiePersonal.Actual.HasPendingDeliveries && host.NativeReturns == 1, "Z original ACK closes debt with no second return");
            Console.WriteLine("Zombie real Delivery Complete/Tick/native-return/reconnect: " + assertions + " assertions PASS");
        }
    }
}
