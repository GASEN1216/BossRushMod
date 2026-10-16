using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using Duckov.Scenes;

internal static partial class Program
{
    private static void Select(TestPeer peer) { BossRushCoop.Current = peer; }
    private static int Pending(CoopGameplayChannel<State> channel)
    { return ((System.Collections.ICollection)channel.GetType().GetField("pendingRequests", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(channel)).Count; }
    private static CoopGameplaySnapshot Snapshot(TestPeer host, int value, long revision, int scene, string token)
    { return new CoopGameplaySnapshot { RunId = host.Run, SceneGeneration = scene, SceneToken = token, Revision = revision, Payload = BossRushCoopJson.ToJson(new State { count = value }) }; }
    private static void ChannelScenes()
    {
        QueuedCommands(); RepeatedFacts(); OrderedCommands(); ThrowAfterEffect(); ScopedRequests(); DeferredSnapshots(); CoalescedPublication(); HandshakeStall();
    }
    private static void HandshakeStall()
    {
        MakeRoom();var host=BossRushCoop.Current;var guest=BossRushCoop.Peers[1];int effects=0,balance=100;
        var server=new CoopGameplayChannel<State>("join-stall",null,null,c=>{effects++;balance-=c.Value;});
        Select(guest);var client=new CoopGameplayChannel<State>("join-stall",null,null,null);
        client.Request("purchase",value:7);BossRushCoop.Flush();Check(effects==1&&balance==93&&Pending(client)==0,"existing ready room establishes one paid action before a late join");
        string epoch=guest.ReadyEpoch,token=guest.Token,room=guest.Run;var core=guest.Core;int generation=guest.Scene;
        BossRushCoop.Peers.Add(new TestPeer{Id="unfinished-handshake",Ready=false});host.Ready=guest.Ready=false;
        Select(guest);client.Request("purchase",value:13);
        for(int second=0;second<60;second++){client.Tick(1);BossRushCoop.Flush();Select(guest);}
        Check(Pending(client)==1&&effects==1&&balance==93,"sixty seconds of global handshake stall retain the explicit unpaid command");
        Check(guest.ReadyEpoch==epoch&&guest.Token==token&&guest.Run==room&&ReferenceEquals(core,guest.Core)&&guest.Scene==generation,"new peer handshake does not replace the original request identity");
        host.Ready=guest.Ready=true;BossRushCoop.DropAcks=1;client.Tick(1);BossRushCoop.Flush();
        Check(effects==2&&balance==80&&Pending(client)==1,"ready recovery executes full original command once even when its first ACK is lost");
        Select(guest);client.Tick(1);BossRushCoop.Flush();Check(effects==2&&balance==80&&Pending(client)==0,"retry after lost ACK cannot charge twice or lose the paid command");
        client.Dispose();server.Dispose();
    }
    private static void CoalescedPublication()
    {
        MakeRoom();var host=BossRushCoop.Current;int executions=0,captures=0;
        var server=new CoopGameplayChannel<State>("dirty",()=>{captures++;return new State{count=executions};},null,c=>executions++);
        server.Tick(0);BossRushCoop.Wire.Clear();Check(captures==1,"normal periodic host capture remains available");
        for(int i=1;i<16;i++)
        {
            var p=BossRushCoop.Peers[i];var c=new CoopGameplayCommand{RunId=host.Run,AssetId=p.AssetId,ReadyEpoch=p.ReadyEpoch,
                SceneToken=host.Token,SceneGeneration=host.Scene,Sequence=1,Action="manual"};
            BossRushCoop.Deliver(host,"BossRush.Gameplay.dirty.command",Message(p.Id,false,c));
        }
        Check(executions==15&&captures==1&&BossRushCoop.Wire.Count==15,"all 15 actual commands execute and ACK immediately without per-command full snapshots");
        Select(host);server.Tick(0);Check(captures==2&&BossRushCoop.Wire.Count==30,"next host tick broadcasts one full latest state to all 15 peers");
        server.Tick(0);Check(captures==2,"unchanged frame cannot repeat dirty capture");
        server.Tick(.25f);Check(captures==3,"periodic snapshots still recover loss without requiring another command");server.Dispose();
        MakeRoom();host=BossRushCoop.Current;executions=captures=0;
        server=new CoopGameplayChannel<State>("dirty-throw",()=>{captures++;return new State();},null,c=>{executions++;throw new InvalidOperationException("partial");});
        server.Tick(0);BossRushCoop.Wire.Clear();var guest=BossRushCoop.Peers[1];
        var cmd=new CoopGameplayCommand{RunId=host.Run,AssetId=guest.AssetId,ReadyEpoch=guest.ReadyEpoch,SceneToken=host.Token,SceneGeneration=host.Scene,Sequence=1};
        try{BossRushCoop.Deliver(host,"BossRush.Gameplay.dirty-throw.command",Message(guest.Id,false,cmd));}catch(InvalidOperationException){}
        server.Tick(0);Check(executions==1&&captures==2,"partial-effect exception remains terminal and its changed state publishes next tick");server.Dispose();
    }
    private static object FirstPending(CoopGameplayChannel<State> channel)
    { return ((System.Collections.IList)channel.GetType().GetField("pendingRequests", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(channel))[0]; }
    private static void RepeatedFacts()
    {
        MakeRoom(); var host = BossRushCoop.Current; var guest = BossRushCoop.Peers[1]; int executed = 0;
        var server = new CoopGameplayChannel<State>("repeat", null, null, c => executed++);
        Select(guest); var client = new CoopGameplayChannel<State>("repeat", null, null, null);
        guest.Bound = false;
        client.Request("utility", text: "transaction-A");
        var original = FirstPending(client);
        for (int i = 0; i < 10000; i++) client.RequestRepeat("utility", text: "transaction-A");
        Check(Pending(client) == 1 && ReferenceEquals(original, FirstPending(client)), "ten thousand periodic reports retain the original manual transaction once");
        Check(BossRushCoop.SendAttempts == 0, "unbound repeated fact cannot send or allocate extra work");
        for (int i = 0; i < 512; i++) client.RequestRepeat("utility", text: "transaction-" + i);
        client.RequestRepeat("utility", value: 1, text: "transaction-A");
        client.RequestRepeat("utility", text: "transaction-A", flag: true);
        client.RequestRepeat("hire", text: "transaction-A");
        client.Request("utility", text: "transaction-A"); client.Request("utility", text: "transaction-A");
        Check(Pending(client) == 518, "all distinct IDs, values, flags, actions and both identical manual interactions survive");
        guest.Bound = true; client.Tick(0); BossRushCoop.Flush();
        Check(executed == 518 && Pending(client) == 0, "all valid transactions dispatch in order without quantity reduction");
        client.RequestRepeat("utility", text: "transaction-A"); BossRushCoop.Flush();
        Check(executed == 519 && Pending(client) == 0, "ACK completes transport only and permits a later business retry");
        BossRushCoop.FailSends = 1; int attempts = BossRushCoop.SendAttempts;
        client.RequestRepeat("paid", text: "failed-send"); original = FirstPending(client);
        for (int i = 0; i < 10000; i++) client.RequestRepeat("paid", text: "failed-send");
        Check(Pending(client) == 1 && ReferenceEquals(original, FirstPending(client)) && BossRushCoop.SendAttempts == attempts + 1,
            "failed send keeps its exact sequence without each periodic report resending it");
        client.Tick(0); BossRushCoop.Flush();
        Check(executed == 520 && Pending(client) == 0, "regular transport tick retries the retained paid transaction");
        client.Dispose(); server.Dispose();

        foreach (string change in new[] { "core", "handle", "asset", "room", "service", "peer", "epoch", "token" })
        {
            MakeRoom(); guest = BossRushCoop.Peers[1]; Select(guest); guest.Bound = false;
            client = new CoopGameplayChannel<State>("repeat-owner", null, null, null);
            client.RequestRepeat("admitted", text: "same-fact"); original = FirstPending(client);
            if (change == "core") { guest.Core.Destroyed = true; guest.Core = new MultiSceneCore(); }
            if (change == "handle") guest.Core.gameObject.scene.handle++;
            if (change == "asset") guest.AssetId = "other-asset";
            if (change == "room") guest.Run = "other-room";
            if (change == "service") guest.NativeService = new object();
            if (change == "peer") guest.NativePeer = new object();
            if (change == "epoch") guest.Epoch++;
            if (change == "token") guest.Token = "other-token";
            client.RequestRepeat("admitted", text: "same-fact");
            Check(Pending(client) == 1 && !ReferenceEquals(original, FirstPending(client)), "periodic reuse cannot retain an earlier owner: " + change);
            client.Dispose();
        }
    }
    private static void QueuedCommands()
    {
        MakeRoom(); var host = BossRushCoop.Current; var guest = BossRushCoop.Peers[1]; int executed = 0;
        var server = new CoopGameplayChannel<State>("deferred", null, null, c => executed++);
        Select(guest); var client = new CoopGameplayChannel<State>("deferred", null, null, null);
        guest.Ready = guest.Bound = false; guest.Run = ""; guest.Token = null;
        Check(client.Request("first") && client.Request("first"), "unready UI owns and queues both identical legitimate actions");
        Check(Pending(client) == 2 && BossRushCoop.Wire.Count == 0, "unready requests do not mint sequence/send");
        guest.Ready = guest.Bound = true; guest.Run = host.Run; guest.Token = host.Token;
        BossRushCoop.FailSends = 1; client.Tick(0);
        Check(Pending(client) == 2 && BossRushCoop.Wire.Count == 1, "failed send retains first exact request while later packet can arrive");
        BossRushCoop.Flush(); Check(executed == 0 && Pending(client) == 2, "seq2 arrival never acknowledges missing seq1");
        client.Tick(0); BossRushCoop.Flush();
        Check(executed == 2 && Pending(client) == 0, "retry executes both real actions in sequence and ACKs each");

        host.Bound = false; client.Request("during-host-load"); BossRushCoop.Flush();
        Check(executed == 2 && Pending(client) == 1, "host unbound rejects without completion ACK");
        host.Bound = true; client.Tick(1); BossRushCoop.DropAcks = 1; BossRushCoop.Flush();
        Check(executed == 3 && Pending(client) == 1, "lost execution ACK retains only retry obligation");
        client.Tick(1); BossRushCoop.Flush();
        Check(executed == 3 && Pending(client) == 0, "executed sequence replay ACKs without repeating world action");
        client.Request("forged-ack");
        var ack = new CoopGameplayAck { RunId = guest.Run, AssetId = guest.AssetId, ReadyEpoch = guest.ReadyEpoch,
            SceneToken = guest.Token, SceneGeneration = guest.Scene, Sequence = 4 };
        BossRushCoop.Deliver(guest, "BossRush.Gameplay.deferred.ack", Message("p2", false, ack));
        Check(Pending(client) == 1, "non-host cannot cancel queued request by ACK");
        ack.SceneToken = "wrong-token";
        BossRushCoop.Deliver(guest, "BossRush.Gameplay.deferred.ack", Message("host", true, ack));
        Check(Pending(client) == 1, "authentic host wrong scene ACK cannot cancel pending owner request");
        BossRushCoop.Flush(); Check(executed == 4 && Pending(client) == 0, "correct actual ACK commits queued action");
        client.Dispose(); server.Dispose();
    }
    private static void OrderedCommands()
    {
        MakeRoom(); var host = BossRushCoop.Current; var guest = BossRushCoop.Peers[1]; var order = new List<int>();
        var server = new CoopGameplayChannel<State>("ordered", null, null, c => order.Add(c.Value));
        Func<long, CoopGameplayCommand> command = seq => new CoopGameplayCommand { RunId = host.Run, AssetId = guest.AssetId,
            ReadyEpoch = guest.ReadyEpoch, SceneToken = host.Token, SceneGeneration = host.Scene, Sequence = seq, Value = (int)seq };
        BossRushCoop.Deliver(host, "BossRush.Gameplay.ordered.command", Message(guest.Id, false, command(2)));
        Check(order.Count == 0 && BossRushCoop.Wire.Count == 0, "out-of-order action not executed or ACKed");
        BossRushCoop.Deliver(host, "BossRush.Gameplay.ordered.command", Message(guest.Id, false, command(1)));
        Check(order.Count == 2 && order[0] == 1 && order[1] == 2, "continuous execution preserves order-sensitive ready/funded/leave");
        BossRushCoop.Wire.Clear();
        BossRushCoop.Deliver(host, "BossRush.Gameplay.ordered.command", Message(guest.Id, false, command(1)));
        Check(order.Count == 2 && BossRushCoop.Wire.Count == 1, "completed duplicate gets ACK only");
        BossRushCoop.Wire.Clear();
        BossRushCoop.Deliver(host, "BossRush.Gameplay.ordered.command", Message(guest.Id, false, command(132)));
        Check(order.Count == 2 && BossRushCoop.Wire.Count == 0, "oversized untrusted gap cannot consume or ACK missing actions");
        host.Scene = guest.Scene = 2; host.Token = guest.Token = "scene-2";
        BossRushCoop.Deliver(host, "BossRush.Gameplay.ordered.command", Message(guest.Id, false, command(1)));
        Check(order.Count == 3 && order[2] == 1, "new scene starts fresh command sequence despite same ReadyEpoch");
        var stale = command(2); stale.ReadyEpoch = "prior-epoch";
        BossRushCoop.Deliver(host, "BossRush.Gameplay.ordered.command", Message(guest.Id, false, stale));
        Check(order.Count == 3, "old connection ready epoch rejected");
        guest.Epoch++;
        BossRushCoop.Deliver(host, "BossRush.Gameplay.ordered.command", Message(guest.Id, false, command(1)));
        Check(order.Count == 4, "new ready epoch starts at one without consuming rejected request");
        server.Dispose();
    }
    private static void ScopedRequests()
    {
        foreach (string change in new[] { "core", "handle", "unloaded", "destroyed", "asset", "room", "peer", "epoch", "token", "stop" })
        {
            MakeRoom(); var guest = BossRushCoop.Peers[1]; Select(guest);
            var client = new CoopGameplayChannel<State>("scope", null, null, null);
            guest.Bound = false; client.Request(change); Check(Pending(client) == 1, "scope setup queues " + change);
            if (change == "core") guest.Core = new MultiSceneCore();
            if (change == "handle") guest.Core.gameObject.scene.handle++;
            if (change == "unloaded") guest.Core.gameObject.scene.isLoaded = false;
            if (change == "destroyed") guest.Core.Destroyed = true;
            if (change == "asset") guest.AssetId = "other";
            if (change == "room") guest.Run = "other-room";
            if (change == "peer") guest.NativePeer = new object();
            if (change == "epoch") guest.Epoch++;
            if (change == "token") guest.Token = "other-token";
            if (change == "stop") guest.Connected = false;
            guest.Bound = true; client.Tick(2);
            Check(Pending(client) == 0 && BossRushCoop.Wire.Count == 0, "old interaction cancelled on actual owner change: " + change);
            client.Dispose();
        }
        MakeRoom(); var host = BossRushCoop.Current; var g = BossRushCoop.Peers[1]; int executed = 0;
        var server = new CoopGameplayChannel<State>("newcore", null, null, c => executed++);
        Select(g); var channel = new CoopGameplayChannel<State>("newcore", null, null, null);
        channel.Request("old"); BossRushCoop.Flush();
        g.Core.Destroyed = true; g.BindScene(); g.Token = "new-core"; g.Bound = false;
        channel.Request("new-current-core");
        Check(Pending(channel) == 1 && BossRushCoop.Wire.Count == 0, "new Core request waits unnumbered while public generation still old");
        host.Scene = g.Scene = 2; host.Token = g.Token; g.Bound = true;
        channel.Tick(1); BossRushCoop.Flush();
        Check(executed == 2 && Pending(channel) == 0, "new Core request receives fresh bound generation sequence one");
        channel.Dispose(); server.Dispose();
    }
    private static void ThrowAfterEffect()
    {
        MakeRoom(); var host=BossRushCoop.Current;var guest=BossRushCoop.Peers[1];int effects=0;
        var server=new CoopGameplayChannel<State>("exception",null,null,c=>{effects++;if(c.Value==1)throw new InvalidOperationException("after real business side effect");});
        Func<long,CoopGameplayCommand> command=seq=>new CoopGameplayCommand {RunId=host.Run,AssetId=guest.AssetId,ReadyEpoch=guest.ReadyEpoch,
            SceneToken=host.Token,SceneGeneration=host.Scene,Sequence=seq,Value=(int)seq};
        BossRushCoop.Deliver(host,"BossRush.Gameplay.exception.command",Message(guest.Id,false,command(2)));
        bool threw=false;try {BossRushCoop.Deliver(host,"BossRush.Gameplay.exception.command",Message(guest.Id,false,command(1)));}
        catch(InvalidOperationException){threw=true;}
        Check(threw&&effects==1&&BossRushCoop.Wire.Count==1,"exception after effect propagates but terminal dispatch still sends its transport ACK");
        BossRushCoop.Deliver(host,"BossRush.Gameplay.exception.command",Message(guest.Id,false,command(1)));
        Check(effects==1,"retry after partial-effect exception cannot repeat a non-idempotent business handler");
        BossRushCoop.Deliver(host,"BossRush.Gameplay.exception.command",Message(guest.Id,false,command(2)));
        Check(effects==2,"failed earlier business dispatch cannot block the next distinct queued action");
        server.Dispose();
    }
    private static void DeferredSnapshots()
    {
        MakeRoom(); var host = BossRushCoop.Current; var guest = BossRushCoop.Peers[1]; int applied = 0, captures = 0;
        var server = new CoopGameplayChannel<State>("snapshots", () => { captures++; return new State { count = captures }; }, null, null);
        host.Bound = false; server.Publish(); Check(captures == 0 && BossRushCoop.Wire.Count == 0, "host half-load cannot capture or publish world snapshot");
        host.Bound = true;
        Select(guest); var client = new CoopGameplayChannel<State>("snapshots", null, s => applied = s.count, null);
        guest.Bound = false;
        BossRushCoop.Deliver(guest, "BossRush.Gameplay.snapshots.snapshot", Message("host", true, Snapshot(host, 5, 1, 1, "scene-1")));
        Check(applied == 0, "unbound actual Core cannot consume even a matching generation snapshot");
        BossRushCoop.Deliver(guest, "BossRush.Gameplay.snapshots.snapshot", Message("host", true, Snapshot(host, 12, 1, 2, "scene-2")));
        BossRushCoop.Deliver(guest, "BossRush.Gameplay.snapshots.snapshot", Message("host", true, Snapshot(host, 999, 99, 1, "scene-1")));
        Check(applied == 0, "future generation retained without old Core write or stale override");
        guest.Core.Destroyed = true; guest.Core = new MultiSceneCore(); guest.Token = "scene-2"; client.Tick(0);
        Check(applied == 0, "client new Core with old public generation cannot consume old world state");
        guest.Scene = 2; guest.Bound = true; client.Tick(0);
        Check(applied == 12, "retained future snapshot applies once actual Core token/generation bind");
        BossRushCoop.Deliver(guest, "BossRush.Gameplay.snapshots.snapshot", Message("host", true, Snapshot(host, 555, 2000, 2, "wrong-core")));
        Check(applied == 12, "same generation wrong-token snapshot rejected at application");
        BossRushCoop.Deliver(guest, "BossRush.Gameplay.snapshots.snapshot", Message("host", true, Snapshot(host, 13, 3, 2, "scene-2")));
        Check(applied == 13, "correct token remains usable after wrong-token packet");
        guest.Core.Destroyed = true; client.Tick(0);
        BossRushCoop.Deliver(guest, "BossRush.Gameplay.snapshots.snapshot", Message("host", true, Snapshot(host, 14, 4, 2, "scene-2")));
        Check(applied == 13, "destroyed actual Core cannot receive world state");
        client.Dispose(); server.Dispose();
        Check(!client.Request("disposed"), "disposed channel cannot retain request");
    }
}
