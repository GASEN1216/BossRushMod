using System;
using System.Collections.Generic;
using BossRush;
namespace BossRush
{
    internal static partial class BossRushCoop
    {
        internal static IEnumerable<BossRushCoopPlayer> Players
        { get { foreach (TestPeer peer in Peers) yield return FindPlayer(peer.Id); } }
    }
    internal sealed partial class BossRushCoopPlayer
    {
        internal bool IsAlive { get { return IsConnected; } }
        internal object Character;
    }
    internal sealed class EntryPersonal
    {
        internal int Attempts;
        internal bool Accept;
        internal void Tick(float dt) { }
        internal bool Deliver(string player, string id, CoopModeEPersonal value, string asset) { Attempts++; return Accept; }
    }
    internal sealed partial class ModeERuntimeModule
    {
        internal readonly EntryPersonal coopPersonal = new EntryPersonal();
        private readonly Dictionary<string, bool> coopEntryDecisions = new Dictionary<string, bool>();
        private readonly Dictionary<string, CoopModeEParticipant> coopModeEParticipants = new Dictionary<string, CoopModeEParticipant>();
        private readonly Dictionary<string, CoopModeEHireRequest> coopHireRequests = new Dictionary<string, CoopModeEHireRequest>();
        private bool modeEActive = true;
        private string coopModeELocalAsset, coopEpoch = "actual-E-entry";
        private float coopEntryAdmissionRetry, coopHireRetry;
        private int modeEPlayerFaction;
        private void RetireLostCoopProjection() { }
        private bool RestoreCoopModeEPlayerTeams() { return false; }
        private void TickCoopEntryRefunds() { RefundTicks++; }
        private void SetCoopModeEPlayerTeam(object character, int faction) { }
        internal void EntryTick() { TickCoopModeE(1); }
        internal void Decision()
        {
            coopModeELocalAsset = BossRushCoop.LocalAssetId;
            coopEntryDecisions["E:" + coopModeELocalAsset + ":" + coopEpoch] = true;
        }
        internal void Confirm(string asset)
        { coopModeEParticipants[BossRushCoop.LocalPlayerId] = new CoopModeEParticipant { AssetId = asset, Admitted = true }; }
    }
}
internal static partial class Program
{
    private static void EntryStartup()
    {
        BossRushCoop.Wire.Clear(); BossRushCoop.Peers.Clear();
        var host = new TestPeer { Id = "host", Host = true }; var guest = new TestPeer { Id = "guest" }; host.BindScene(); guest.BindScene();
        BossRushCoop.Peers.Add(host); BossRushCoop.Peers.Add(guest); BossRushCoop.Current = host;
        var e = new ModeERuntimeModule { coopChannel = new CoopGameplayChannel<RepeatState>("entry", null, null, null) };
        e.EntryTick(); Check(e.coopPersonal.Attempts == 2, "real E tick retains rejected starter queue attempts");
        e.coopPersonal.Accept = true; e.EntryTick(); int attempts = e.coopPersonal.Attempts;
        for (int i = 0; i < 100; i++) e.EntryTick();
        Check(attempts == 4 && e.coopPersonal.Attempts == attempts, "real E host tick queues each accepted starter once across 100 frames");
        BossRushCoop.Current = guest; guest.Bound = false;
        var client = new ModeERuntimeModule { coopChannel = new CoopGameplayChannel<RepeatState>("entry-client", null, null, null) };
        client.Decision(); for (int i = 0; i < 100; i++) client.EntryTick();
        Check(Pending(client.coopChannel) == 1, "E unconfirmed periodic admission retains one original in-flight transaction");
        client.Confirm("other-asset"); client.EntryTick(); Check(Pending(client.coopChannel) == 1, "wrong asset host confirmation does not complete E admission");
        client.Confirm(guest.AssetId); guest.Bound = true;
        BossRushCoop.Current = host; var server = new CoopGameplayChannel<RepeatState>("entry-client", null, null, command => { });
        BossRushCoop.Current = guest; client.EntryTick(); BossRushCoop.Flush(); int sends = BossRushCoop.SendAttempts;
        for (int i = 0; i < 100; i++) client.EntryTick();
        Check(BossRushCoop.SendAttempts == sends && Pending(client.coopChannel) == 0, "genuine host confirmation stops E stable admission reports after exact original ACK");
        e.coopChannel.Dispose(); client.coopChannel.Dispose(); server.Dispose();
    }
}
