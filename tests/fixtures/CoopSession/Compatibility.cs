using System;
using System.Linq;
using BossRush;
using UnityEngine;

partial class Program
{
    static void CompatibilityNotice()
    {
        var host = Start(true);
        var native = BossRushCoopTransport.Current;
        var pending = new object();
        BossRushCoopTransport.NativePeers.Add(pending);
        ModBehaviour.Messages.Clear();
        BossRushCoopTransport.NativeNotices.Clear();
        Time.unscaledTime = 1; host.OnUpdate(1, 1);
        Check(!host.Ready && ModBehaviour.Messages.Count == 0, "new native peer blocks readiness without premature missing-installation accusation");
        Time.unscaledTime = 12; host.OnUpdate(11, 11);
        string pendingName = native.NativeId(pending);
        Check(ModBehaviour.Messages.Any(value => value.Contains(pendingName) && value.Contains("handshake is missing")),
            "host sees exactly which native peer has not provided BossRush handshake");
        Check(BossRushCoopTransport.NativeNotices.Count == 1 && BossRushCoopTransport.NativeNotices[0].Contains(pendingName)
            && BossRushCoopTransport.NativeNotices[0].Contains("check installation"), "missing-Mod peer also receives a bilingual native COOP chat notice");
        var notice = native.Sent.Select(BossRushCoopJson.FromJson<BossRushCoopEnvelope>).Last(value => value.topic == "$compatibility");
        var missing = BossRushCoopJson.FromJson<BossRushCoopCompatibilityStatus>(notice.json);
        Check(missing.issues.Length == 1 && missing.issues[0].player == pendingName && missing.issues[0].reason == "handshake_missing",
            "status wire retains exact peer and missing-handshake cause");
        native.Packet(pending, "$hello", new BossRushCoopHello { sequence = 1, manifest = "different", assetId = Guid.NewGuid().ToString("N"), assetRevision = 1 });
        Check(ModBehaviour.Messages.Last().Contains(pendingName) && ModBehaviour.Messages.Last().Contains("version or resources differ"),
            "content mismatch is shown to host with actual peer identity");
        notice = native.Sent.Select(BossRushCoopJson.FromJson<BossRushCoopEnvelope>).Last(value => value.topic == "$compatibility");
        var mismatch = BossRushCoopJson.FromJson<BossRushCoopCompatibilityStatus>(notice.json);
        native.Packet(pending, "$hello", new BossRushCoopHello { sequence = 2, manifest = "same", assetId = Guid.NewGuid().ToString("N"), assetRevision = 1 });
        Time.unscaledTime = 13; host.OnUpdate(1, 1);
        Check(host.Ready && ModBehaviour.Messages.Last().Contains("recovered"), "matching real hello releases block and reports recovery");
        var stranger = new object(); BossRushCoopTransport.NativePeers.Add(stranger);
        Time.unscaledTime = 14; host.OnUpdate(1, 1);
        Time.unscaledTime = 25; host.OnUpdate(11, 11);
        Check(!host.Ready, "later missing peer blocks already established room");
        BossRushCoopTransport.NativePeers.Remove(stranger);
        Time.unscaledTime = 26; host.OnUpdate(1, 1);
        Check(host.Ready && ModBehaviour.Messages.Last().Contains("recovered"), "missing peer departure clears current diagnostics");
        host.OnDestroy();

        var client = Start(false);
        native = BossRushCoopTransport.Current;
        var upstream = new object(); BossRushCoopTransport.NativeHost = upstream; BossRushCoopTransport.NativePeers.Add(upstream);
        ModBehaviour.Messages.Clear();
        native.Packet(new object(), "$compatibility", mismatch);
        Check(ModBehaviour.Messages.Count == 0, "untrusted peer cannot inject compatibility warning");
        native.Packet(upstream, "$compatibility", mismatch);
        Check(ModBehaviour.Messages.Count == 1 && ModBehaviour.Messages[0].Contains(pendingName),
            "joining client sees host diagnostic even before its own welcome");
        native.Packet(upstream, "$compatibility", mismatch);
        Check(ModBehaviour.Messages.Count == 1, "periodic diagnostics do not spam unchanged message");
        native.Packet(upstream, "$compatibility", new BossRushCoopCompatibilityStatus { issues = new BossRushCoopCompatibilityIssue[0] });
        Check(ModBehaviour.Messages.Last().Contains("recovered"), "client sees all-clear after missing peer resolves");
        client.OnDestroy();
    }

    static void BindingFailure()
    {
        BossRushCoopTransport.BindSucceeds = false;
        BossRushCoopTransport.Started = false;
        ModBehaviour.Messages.Clear();
        var runtime = Start(false);
        Check(!BossRushCoop.IsConnected && BossRushCoop.HasWorldAuthority && BossRushCoop.SessionReady,
            "installed offline adapter failure retains normal single-player authority");
        BossRushCoopTransport.Started = true;
        Time.unscaledTime = .1f; runtime.OnUpdate(.1f, .1f);
        Check(BossRushCoop.IsConnected && BossRushCoop.IsClient && !BossRushCoop.IsHost && !BossRushCoop.HasWorldAuthority,
            "native client is fail-closed even inside one-second binding retry interval");
        Check(!BossRushCoop.SessionReady && !BossRushCoop.HasBoundScene && ModBehaviour.Messages.Count == 1,
            "blocked native connection is visible and cannot enter scene gameplay");
        for (int i = 0; i < 10; i++) runtime.OnUpdate(0, 0);
        Check(ModBehaviour.Messages.Count == 1, "binding failure warning is deduplicated across frames");
        Check(!runtime.SendToHost("command", "{}") && !runtime.SendToPlayer("host", "reward", "{}")
            && !runtime.Broadcast("world", "{}"), "blocked transport cannot dispatch world or reward messages");
        BossRushCoopTransport.Server = true;
        BossRushCoopTransport.StateKnown = false;
        Time.unscaledTime = 2; runtime.OnUpdate(1, 1);
        Check(BossRushCoop.IsClient && !BossRushCoop.HasWorldAuthority,
            "unknown native role is treated as client instead of granting authority");
        BossRushCoopTransport.BindSucceeds = true;
        runtime.OnUpdate(0, 0);
        Check(BossRushCoop.IsClient && !BossRushCoop.HasWorldAuthority,
            "binding success alone cannot override an unreadable native role");
        BossRushCoopTransport.StateKnown = true;
        runtime.OnUpdate(0, 0);
        Check(BossRushCoop.IsHost && BossRushCoop.HasWorldAuthority && runtime.Connected,
            "recovered native host initializes its actual session normally");
        BossRushCoopTransport.BindSucceeds = false;
        runtime.OnUpdate(0, 0);
        Check(BossRushCoop.IsClient && !BossRushCoop.HasWorldAuthority,
            "previous host state cannot leak authority after live binding loss");
        BossRushCoopTransport.Started = false;
        runtime.OnUpdate(0, 0);
        Check(!BossRushCoop.IsConnected && BossRushCoop.HasWorldAuthority,
            "native disconnect restores single-player even while adapter remains unbound");
        runtime.OnDestroy();
        BossRushCoopTransport.BindSucceeds = BossRushCoopTransport.StateKnown = BossRushCoopTransport.Started = true;
    }
}
