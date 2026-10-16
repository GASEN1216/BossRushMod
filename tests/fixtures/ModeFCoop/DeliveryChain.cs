using System;
namespace BossRush
{
    internal partial class ModeFRuntimeModule
    {
        internal void PrimeDeliveryClient(string epoch)
        { coopEpoch = epoch; coopLocalAsset = BossRushCoop.LocalAssetId; PreserveCoopModeFLocalLedger(); coopPersonal.Bind(ApplyCoopModeFPersonal); }
    }
    internal static class DeliveryChain
    {
        private static int checks;
        private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
        private static void Main()
        {
            BossRushCoop.HasBoundScene = true;
            BossRushCoop.Players.Add(new BossRushCoopPlayer { Id = "host", AssetId = "host-slot" });
            BossRushCoop.Players.Add(new BossRushCoopPlayer { Id = "online", AssetId = "online-original" });
            BossRushCoop.Players.Add(new BossRushCoopPlayer { Id = "offline", AssetId = "offline-original" });
            BossRushCoop.Select("host"); var host = new ModeFRuntimeModule(); host.Begin(); host.PrimeDeliveryClient(host.coopEpoch);
            host.modeFState.ExtractionPointSpawned = true;
            foreach (var player in host.coopPlayers.Values) { player.Admitted = true; player.StarterSent = true; player.Marks = player.Id == "offline" ? 7 : 3; }
            BossRushCoop.Select("online"); var online = new ModeFRuntimeModule(); online.PrimeDeliveryClient(host.coopEpoch);
            BossRushCoop.Select("offline"); var offline = new ModeFRuntimeModule(); offline.PrimeDeliveryClient(host.coopEpoch);
            BossRushCoop.Select("host"); host.Tick(); BossRushCoop.FindPlayer("offline").IsConnected = false;
            host.Extract(); host.Tick();
            Check(host.NativeReturns == 0, "F waits for online original-asset extraction ACK");
            Check(host.coopPersonal.Actual.HasPendingDeliveries, "F offline debt survives Complete and Tick");
            var onlinePacket = BossRushCoop.Take("online", false); BossRushCoop.Dispatch(onlinePacket);
            Check(online.Extractions == 1, "F online actual personal settlement executes");
            BossRushCoop.Dispatch(BossRushCoop.Take("host", true)); host.Tick();
            Check(host.NativeReturns == 1 && host.coopPersonal.Actual.HasPendingDeliveries, "F native return proceeds with only offline original debt retained");
            BossRushCoop.SceneGeneration = 2; BossRushCoop.HasBoundScene = false;
            var reconnect = BossRushCoop.FindPlayer("offline"); reconnect.IsConnected = true; reconnect.AssetId = "new-slot";
            host.Tick(); var oldReceipt = BossRushCoop.Take("offline", false); BossRushCoop.Dispatch(oldReceipt);
            Check(offline.Extractions == 0 && !BossRushCoop.Packets.Exists(p => p.Target == "host"), "F wrong slot cannot apply or ACK old receipt after Base transition");
            reconnect.AssetId = "offline-original"; BossRushCoop.Select("host"); host.Tick();
            var original = BossRushCoop.Take("offline", false); BossRushCoop.Dispatch(original);
            Check(offline.Extractions == 1 && PlayerStorageBuffer.Buffer.Count == 13, "F reconnect settles full 3+3+7 original personal marks across generation");
            BossRushCoop.Dispatch(original); Check(offline.Extractions == 1 && PlayerStorageBuffer.Buffer.Count == 13, "F actual delivery dedup prevents replayed extraction quantity");
            BossRushCoop.Dispatch(BossRushCoop.Take("host", true)); host.Tick();
            Check(!host.coopPersonal.Actual.HasPendingDeliveries && host.NativeReturns == 1, "F original ACK closes retained debt without another return");
            Console.WriteLine("ModeF real Delivery Complete/Tick/native-return/reconnect: " + checks + " assertions PASS");
        }
    }
}
