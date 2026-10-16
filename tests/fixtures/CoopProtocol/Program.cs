using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;

internal static partial class Program
{
    [Serializable] private sealed class State { public int count; }
    private static int checks;
    private static void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
    private static BossRushCoopMessage Message<T>(string sender, bool host, T value)
    { return new BossRushCoopMessage { SenderId = sender, IsFromHost = host, Json = BossRushCoopJson.ToJson(value) }; }
    private static void Main()
    {
        ReplayAndBarrier(); RoomCommands(); ChannelScenes(); PersonalReceipts(); PausedReceiptAcknowledgement(); LocalReceipts(); AssetReceipts(); ReconnectReceipts(); ReceiptGroups();
        Console.WriteLine("PASS CoopProtocol: " + checks + " production-linked assertions (16 peers, replay, authority, scene, retry and ack)");
    }
    private static void ReplayAndBarrier()
    {
        var replay = new BossRushCoopReplayWindow(32);
        var barrier = new BossRushCoopSceneBarrier();
        var ids = new List<string>();
        for (int i = 0; i < 16; i++)
        {
            string id = "p" + i; ids.Add(id);
            Check(replay.Accept(id, "same-packet-id"), "different sender identities must not collide");
            Check(!replay.Accept(id, "same-packet-id"), "each sender replay must be rejected");
            barrier.Mark(id, "island:lease-A");
        }
        Check(barrier.AllReady("island:lease-A", ids), "all 16 ready");
        Check(!barrier.AllReady("island:lease-B", ids), "old scene nonce cannot unlock new scene");
        barrier.Remove(ids[15]); Check(!barrier.AllReady("island:lease-A", ids), "connected player missing blocks barrier");
        ids.RemoveAt(15); Check(barrier.AllReady("island:lease-A", ids), "disconnected player removed from participant set");
        ids.Add("late"); Check(!barrier.AllReady("island:lease-A", ids), "late join cannot inherit ready");
        barrier.Mark("late", "island:lease-A"); Check(barrier.AllReady("island:lease-A", ids), "late player arrives");
        barrier.Clear(); Check(!barrier.AllReady("island:lease-A", ids), "generation clears barrier");
        Check(!replay.Accept("p", new string('a', 81)), "packet ID size bounded");
        Check(!replay.Accept(null, "x"), "anonymous packets rejected");
    }
    private static void MakeRoom()
    {
        BossRushCoop.Peers.Clear(); BossRushCoop.Wire.Clear();
        for (int i = 0; i < 16; i++) BossRushCoop.Peers.Add(new TestPeer { Id = i == 0 ? "host" : "p" + i, Host = i == 0 });
        foreach (var peer in BossRushCoop.Peers) peer.BindScene();
        BossRushCoop.FailSends = BossRushCoop.DropAcks = BossRushCoop.SendAttempts = 0;
        BossRushCoop.Current = BossRushCoop.Peers[0];
    }
    private static void RoomCommands()
    {
        MakeRoom(); var host = BossRushCoop.Current; int total = 0;
        var seenOwners = new HashSet<string>();
        var hostChannel = new CoopGameplayChannel<State>("fixture", () => new State { count = total }, null,
            command => { total++; seenOwners.Add(command.PlayerId); });
        var clients = new List<CoopGameplayChannel<State>>(); int applied = 0;
        for (int i = 1; i < 16; i++)
        {
            BossRushCoop.Current = BossRushCoop.Peers[i];
            var client = new CoopGameplayChannel<State>("fixture", null, s => applied = s.count, null); clients.Add(client);
            client.Request("ready");
        }
        BossRushCoop.Flush(); Check(total == 15 && seenOwners.Count == 15, "every client command has separate owner");
        var duplicate = new CoopGameplayCommand { RunId = host.Run, AssetId = "slot-A", ReadyEpoch = BossRushCoop.Peers[1].ReadyEpoch, SceneToken = host.Token, SceneGeneration = 1, Sequence = 1, Action = "ready", PlayerId = "host" };
        BossRushCoop.Deliver(host, "BossRush.Gameplay.fixture.command", Message("p1", false, duplicate));
        Check(total == 15, "duplicate sequence rejected");
        duplicate.Sequence = 2; duplicate.RunId = "prior-room";
        BossRushCoop.Deliver(host, "BossRush.Gameplay.fixture.command", Message("p1", false, duplicate)); Check(total == 15, "previous run rejected");
        duplicate.RunId = host.Run; duplicate.SceneGeneration = 0;
        BossRushCoop.Deliver(host, "BossRush.Gameplay.fixture.command", Message("p1", false, duplicate)); Check(total == 15, "previous scene rejected");
        duplicate.SceneGeneration = 1;
        BossRushCoop.Deliver(host, "BossRush.Gameplay.fixture.command", Message("missing", false, duplicate)); Check(total == 15, "unknown owner rejected");
        host.Ready = false;
        BossRushCoop.Deliver(host, "BossRush.Gameplay.fixture.command", Message("p1", false, duplicate)); Check(total == 15, "room handshake gates command");
        host.Ready = true;
        BossRushCoop.Deliver(host, "BossRush.Gameplay.fixture.command", Message("p1", false, duplicate)); Check(total == 16 && !seenOwners.Contains("host"), "payload cannot impersonate host");
        BossRushCoop.Peers[1].AssetId = "slot-B"; duplicate.Sequence = 3;
        BossRushCoop.Deliver(host, "BossRush.Gameplay.fixture.command", Message("p1", false, duplicate)); Check(total == 16, "old slot command rejected even from authentic peer");
        duplicate.AssetId = "slot-B";
        duplicate.ReadyEpoch = BossRushCoop.Peers[1].ReadyEpoch; duplicate.Sequence = 1;
        BossRushCoop.Deliver(host, "BossRush.Gameplay.fixture.command", Message("p1", false, duplicate)); Check(total == 17, "new slot command accepted without consuming rejected sequence");
        BossRushCoop.Disconnect(BossRushCoop.Peers[1]); BossRushCoop.Peers[1].Connected = true; duplicate.Sequence = 1;
        duplicate.ReadyEpoch = BossRushCoop.Peers[1].ReadyEpoch;
        BossRushCoop.Deliver(host, "BossRush.Gameplay.fixture.command", Message("p1", false, duplicate)); Check(total == 18, "reconnected stable identity can send first sequence after previous high value");
        BossRushCoop.Current=host;hostChannel.Tick(0);BossRushCoop.Flush(); Check(applied == 18, "all clients receive latest snapshot on next host channel tick");
        var forged = new CoopGameplaySnapshot { RunId = host.Run, SceneToken = host.Token, SceneGeneration = 1, Revision = 10000, Payload = BossRushCoopJson.ToJson(new State { count = 999 }) };
        BossRushCoop.Deliver(BossRushCoop.Peers[1], "BossRush.Gameplay.fixture.snapshot", Message("p2", false, forged)); Check(applied == 18, "non-host snapshot rejected");
        forged.Revision = 1;
        BossRushCoop.Deliver(BossRushCoop.Peers[1], "BossRush.Gameplay.fixture.snapshot", Message("host", true, forged)); Check(applied == 18, "stale snapshot rejected");
        foreach (var client in clients) client.Dispose(); hostChannel.Dispose();
    }
    private static void PersonalReceipts()
    {
        MakeRoom(); var host = BossRushCoop.Current; var guest = BossRushCoop.Peers[1]; int applied = 0; bool ready = false;
        var sender = new CoopPersonalDelivery<State>("fixture", s => true);
        BossRushCoop.Current = guest;
        var receiver = new CoopPersonalDelivery<State>("fixture", s => { if (!ready) return false; applied += s.count; return true; });
        BossRushCoop.Current = host;
        Check(sender.Deliver(guest.Id, "victory", new State { count = 10 }), "receipt accepted for delivery");
        BossRushCoop.Flush(); Check(applied == 0, "failed local commit must not ack");
        host.Scene = guest.Scene = 2; ready = true;
        sender.Tick(2f); BossRushCoop.Flush(); Check(applied == 10, "unacknowledged reward survives scene transition");
        sender.Tick(2f); BossRushCoop.Flush(); Check(applied == 10, "ack stops retry");
        var replay = new CoopPersonalReceipt { RunId = host.Run, SceneGeneration = 1, Id = "1:victory", Payload = BossRushCoopJson.ToJson(new State { count = 10 }), TargetAssetId = guest.AssetId };
        BossRushCoop.Deliver(guest, "BossRush.Personal.fixture", Message("p2", false, replay)); Check(applied == 10, "another guest cannot mint reward");
        BossRushCoop.Deliver(guest, "BossRush.Personal.fixture", Message("host", true, replay)); Check(applied == 10, "duplicate receipt cannot apply twice");
        replay.Id = "1:old"; replay.RunId = "prior-room";
        BossRushCoop.Deliver(guest, "BossRush.Personal.fixture", Message("host", true, replay)); Check(applied == 10, "old room receipt rejected");
        receiver.Dispose(); sender.Dispose();
    }
    private static void PausedReceiptAcknowledgement()
    {
        MakeRoom(); var host = BossRushCoop.Current; var guest = BossRushCoop.Peers[1]; int applied = 0;
        var sender = new CoopPersonalDelivery<State>("paused-receipt", s => true);
        BossRushCoop.Current = guest;
        var receiver = new CoopPersonalDelivery<State>("paused-receipt", s => { applied += s.count; return true; });
        string epoch = guest.ReadyEpoch, token = guest.Token, room = guest.Run; var core = guest.Core; int generation = guest.Scene;
        host.Ready = guest.Ready = false;
        BossRushCoop.Current = host;
        Check(sender.Deliver(guest.Id, "earned", new State { count = 11 }), "unready room retains and sends an already earned personal receipt");
        BossRushCoop.Flush();
        Check(applied == 11 && sender.HasPendingDeliveries, "actual local commit survives a handshake-blocked ACK");
        for (int second = 0; second < 60; second++) { sender.Tick(1); BossRushCoop.Flush(); }
        Check(applied == 11 && sender.HasPendingDeliveries, "sixty seconds of duplicate receipts cannot pay twice or discard the unconfirmed obligation");
        Check(guest.ReadyEpoch == epoch && guest.Token == token && guest.Run == room && ReferenceEquals(core, guest.Core) && guest.Scene == generation,
            "receipt pause preserves the original room asset epoch and actual scene owner");
        host.Ready = guest.Ready = true;
        sender.Tick(1); BossRushCoop.Flush();
        Check(applied == 11 && !sender.HasPendingDeliveries, "recovered handshake ACKs the original committed receipt without repeating its reward");
        sender.Tick(60); BossRushCoop.Flush();
        Check(applied == 11 && !sender.HasPendingDeliveries, "confirmed personal reward stays exactly once after the retry window");
        receiver.Dispose(); sender.Dispose();
    }
    private static void LocalReceipts()
    {
        MakeRoom(); var host = BossRushCoop.Current; bool ready = false, throws = true; int applied = 0;
        CoopPersonalDelivery<State> delivery = null;
        delivery = new CoopPersonalDelivery<State>("localhost", s =>
        {
            if (throws) throw new Exception("injected local store exception");
            if (!ready) return false;
            applied += s.count;
            if (s.count == 10) delivery.Deliver(host.Id, "nested", new State { count = 1 });
            return true;
        });
        Check(delivery.Deliver(host.Id, "victory", new State { count = 10 }), "local exception still enters delivery journal");
        Check(delivery.HasPendingDeliveries && applied == 0, "failed host commit retained");
        throws = false; delivery.Tick(2f); Check(delivery.HasPendingDeliveries && applied == 0, "local false commit retained");
        host.Scene = 2; ready = true; delivery.Tick(2f);
        Check(applied == 11 && !delivery.HasPendingDeliveries, "cross-scene retry tolerates callback inserting another receipt");
        delivery.Tick(2f); Check(applied == 11, "success never pays again");
        delivery.Dispose();
        Check(!delivery.Deliver(host.Id, "disposed", new State { count = 100 }), "disposed journal rejects new obligations");
    }
    private static void AssetReceipts()
    {
        var identities = new BossRushCoopAssetIdentity();
        string first = identities.Get(1), second = identities.Get(2);
        Check(first != second && identities.Get(1) == first, "opaque asset identity remains stable on slot return");
        identities.Invalidate(1); Check(identities.Get(1) != first, "deleted slot cannot inherit old rewards");
        Check(identities.Get(-1) == string.Empty, "unknown slot cannot be a reward target");
        MakeRoom(); var host = BossRushCoop.Current; var guest = BossRushCoop.Peers[1];
        int paidA = 0, paidB = 0; bool ready = false;
        var sender = new CoopPersonalDelivery<State>("assets", s => true);
        BossRushCoop.Current = guest;
        var receiver = new CoopPersonalDelivery<State>("assets", s =>
        {
            if (!ready) return false;
            if (guest.AssetId == "slot-A") paidA += s.count; else paidB += s.count;
            return true;
        });
        BossRushCoop.Current = host; sender.Deliver(guest.Id, "old-slot", new State { count = 7 });
        guest.AssetId = "slot-B"; ready = true;
        BossRushCoop.Flush(); sender.Tick(2); BossRushCoop.Flush();
        Check(paidA == 0 && paidB == 0 && sender.HasPendingDeliveries, "remote slot switch rejects in-flight old obligation without ack");
        Check(!sender.HasPendingConnectedDeliveries && !sender.HasPendingForPlayers(new[] { guest.Id }), "old slot obligation never blocks new slot world progress");
        sender.Deliver(guest.Id, "new-slot", new State { count = 3 }); BossRushCoop.Flush();
        Check(paidB == 3 && sender.HasPendingDeliveries, "new slot rewards do not consume old slot journal");
        guest.AssetId = "slot-A"; sender.Tick(2); BossRushCoop.Flush();
        Check(paidA == 7 && paidB == 3 && !sender.HasPendingDeliveries, "return to original slot retries and pays only original reward");
        guest.AssetId = "slot-B"; sender.Tick(2); BossRushCoop.Flush();
        Check(paidA == 7 && paidB == 3, "slot return never replays committed reward");
        sender.Dispose(); receiver.Dispose();
        BossRushCoop.Current = host; int localA = 0, localB = 0; ready = false;
        var local = new CoopPersonalDelivery<State>("host-assets", s => { if (!ready) return false; if (host.AssetId == "slot-A") localA += s.count; else localB += s.count; return true; });
        local.Deliver(host.Id, "old-slot", new State { count = 9 }); host.AssetId = "slot-B"; ready = true; local.Tick(2);
        Check(localA == 0 && localB == 0 && local.HasPendingDeliveries, "host local pending cannot apply to different slot");
        host.AssetId = "slot-A"; local.Tick(2);
        Check(localA == 9 && localB == 0 && !local.HasPendingDeliveries, "host original slot can resume its pending commit");
        local.Dispose();
    }
    private static void ReconnectReceipts()
    {
        MakeRoom(); var host = BossRushCoop.Current; var guest = BossRushCoop.Peers[1]; int paid = 0;
        var sender = new CoopPersonalDelivery<State>("reconnect", s => true);
        BossRushCoop.Current = guest;
        var receiver = new CoopPersonalDelivery<State>("reconnect", s => { paid += s.count; return true; });
        BossRushCoop.Current = host;
        sender.Deliver(guest.Id, "victory", new State { count = 7 });
        BossRushCoop.Wire.Dequeue()(); // apply succeeds, but disconnect loses the ack
        BossRushCoop.Wire.Clear();
        Check(paid == 7 && sender.HasPendingDeliveries, "lost ack leaves only host obligation pending");
        BossRushCoop.Disconnect(guest);
        BossRushCoop.Current = guest;
        guest.Run = string.Empty; guest.Connected = true; guest.Ready = false; guest.Scene = 0;
        receiver.Tick(1); // native connection exists before welcome restores room identity
        guest.Run = host.Run; guest.Ready = true; guest.Scene = host.Scene;
        receiver.Tick(1);
        BossRushCoop.Current = host; sender.Tick(2); BossRushCoop.Flush();
        Check(paid == 7 && !sender.HasPendingDeliveries, "same-room reconnect acknowledges committed receipt without paying twice");
        host.Run = guest.Run = "room-B";
        sender.Deliver(guest.Id, "victory", new State { count = 7 }); BossRushCoop.Flush();
        Check(paid == 14, "a different room starts a new receipt journal");
        sender.Dispose(); receiver.Dispose();
    }
    private static void ReceiptGroups()
    {
        MakeRoom(); var host = BossRushCoop.Current; var guest = BossRushCoop.Peers[1]; int paid = 0; bool ready = false;
        var sender = new CoopPersonalDelivery<State>("groups", s => true);
        BossRushCoop.Current = guest;
        var receiver = new CoopPersonalDelivery<State>("groups", s => { if (!ready) return false; paid += s.count; return true; });
        BossRushCoop.Current = host;
        sender.Deliver(guest.Id, "extract:10:", new State { count = 10 }); BossRushCoop.Flush();
        Check(sender.HasPendingForIdPrefix("extract:10:") && !sender.HasPendingForIdPrefix("extract:1:"), "receipt group uses caller delimiter to distinguish epochs 1 and 10");
        Check(!sender.HasPendingForIdPrefix("extract:10:", id => id != guest.Id) && sender.HasPendingDeliveries,
            "departed participants can leave the wait set without deleting their obligations");
        Check(!sender.HasPendingForIdPrefix(null) && !sender.HasPendingForIdPrefix("") && !sender.HasPendingForIdPrefix("EXTRACT:10:"), "receipt group requires nonempty ordinal prefix");
        guest.Connected = false;
        Check(!sender.HasPendingForIdPrefix("extract:10:") && sender.HasPendingDeliveries, "disconnected debt remains journaled without blocking current group");
        guest.Connected = true; guest.AssetId = "slot-B";
        Check(!sender.HasPendingForIdPrefix("extract:10:") && sender.HasPendingDeliveries, "old asset debt is excluded but retained");
        host.Scene = guest.Scene = 2;
        sender.Deliver(guest.Id, "extract:1:", new State { count = 1 }); BossRushCoop.Flush();
        Check(sender.HasPendingForIdPrefix("extract:1:") && !sender.HasPendingForIdPrefix("extract:10:"), "group matches logical ID after generation prefix for current asset");
        ready = true; sender.Tick(2); BossRushCoop.Flush();
        Check(paid == 1 && !sender.HasPendingForIdPrefix("extract:1:") && sender.HasPendingDeliveries, "ack releases only current group while old debt survives");
        guest.AssetId = "slot-A";
        Check(sender.HasPendingForIdPrefix("extract:10:"), "original asset return restores pending group visibility");
        sender.Tick(2); BossRushCoop.Flush();
        Check(paid == 11 && !sender.HasPendingDeliveries, "read-only group queries preserve full cross-scene original obligation");
        sender.Dispose(); receiver.Dispose();
    }
}
