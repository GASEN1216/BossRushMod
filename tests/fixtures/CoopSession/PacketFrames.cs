using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BossRush;

partial class Program
{
    static List<byte[]> Frames(byte[] message)
    {
        var packets = new List<byte[]>();
        Check(BossRushCoopPacketFrames.Send(message, b => { packets.Add(b); return true; }), "valid full envelope sends");
        Check(packets.All(b => b.Length <= 60000), "every native UInt16 payload fits including frame header");
        return packets;
    }
    static void PacketFrames()
    {
        var peer = new object();
        foreach (int length in new[] { 1, 59999, 60000, 60001, 65535, 65536, 131072, 262144 })
        {
            var message = Enumerable.Range(0, length).Select(i => (byte)(i * 31)).ToArray();
            var packets = Frames(message);
            var receiver = new BossRushCoopPacketFrames(); byte[] complete = null;
            int count = 0;
            foreach (byte[] packet in packets.AsEnumerable().Reverse())
            {
                byte[] output;
                if (receiver.Receive(peer, packet, 0, out output)) { complete = output; count++; }
            }
            Check(count == 1 && complete.SequenceEqual(message), "complete original bytes survive reverse native fragments: " + length);
        }
        var large = Enumerable.Range(0, 180000).Select(i => (byte)(i * 17)).ToArray();
        var frames = Frames(large); byte[] result;
        var decoder = new BossRushCoopPacketFrames();
        Check(!decoder.Receive(peer, frames[0], 0, out result) && !decoder.Receive(peer, frames[0], 0, out result), "duplicate prefix cannot publish incomplete data");
        for (int i=1;i<frames.Count;i++) decoder.Receive(peer, frames[i], 0, out result);
        Check(result != null && result.SequenceEqual(large), "identical repeated frame retains complete content");
        decoder = new BossRushCoopPacketFrames();
        var alien = new object(); decoder.Receive(peer, frames[0], 0, out result);
        for(int i=1;i<frames.Count;i++) Check(!decoder.Receive(alien, frames[i], 0, out result), "peers cannot complete another peer's frame set");
        decoder.Remove(peer);
        for(int i=1;i<frames.Count;i++) Check(!decoder.Receive(peer, frames[i], 0, out result), "disconnect removes earlier fragment");
        decoder.Clear(); decoder.Receive(peer, frames[0], 0, out result); decoder.Prune(31);
        for(int i=1;i<frames.Count;i++) Check(!decoder.Receive(peer, frames[i], 31, out result), "expired partial never publishes");
        decoder.Clear(); decoder.Receive(peer, frames[0], 0, out result); decoder.Clear();
        for(int i=1;i<frames.Count;i++) Check(!decoder.Receive(peer, frames[i], 0, out result), "session reset cannot complete previous room frame");
        decoder.Clear(); var corrupt=(byte[])frames[1].Clone();corrupt[corrupt.Length-1]^=1;
        foreach(var frame in frames.Select((f,i)=>i==1?corrupt:f)) Check(!decoder.Receive(peer,frame,0,out result), "whole-envelope SHA rejects corrupted payload");
        int attempts=0;
        Check(!BossRushCoopPacketFrames.Send(large, b => ++attempts < 2) && attempts==2, "native failure returns false without claiming complete delivery");
        Check(!BossRushCoopPacketFrames.Send(new byte[262145], b=>true), "existing full envelope bound remains enforced");
        RuntimeLargePackets();
    }
    static void RuntimeLargePackets()
    {
        var host=Start(true);var native=BossRushCoopTransport.Current;var peer=new object();
        BossRushCoopTransport.NativePeers.Add(peer);
        native.Packet(peer,"$hello",new BossRushCoopHello{assetRevision=1,sequence=1,manifest="same",assetId=Guid.NewGuid().ToString("N")});
        var member=host.Players.Single(p=>!p.IsLocal);string session=host.SessionId;
        string payload=new string('鸭',30000)+new string('"',9000)+"😀完整个人物品树";
        int calls=0;string received=null;host.Register("large-personal",m=>{calls++;received=m.Json;});
        native.SentBytes.Clear();Check(host.SendToPlayer(member.Id,"large-personal",payload), "real Runtime.SendRaw accepts full personal content");
        var outgoing=native.SentBytes.ToArray();
        Check(outgoing.Length>1&&outgoing.All(b=>b.Length<=60000), "real native send receives UInt16-safe frames");
        var fragments=(BossRushCoopPacketFrames)typeof(BossRushCoopRuntimeModule).GetField("packetFrames",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
        Func<int> pendingCount=()=>((System.Collections.IDictionary)typeof(BossRushCoopPacketFrames).GetField("pending",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fragments)).Count;
        native.PacketBytes(new object(),outgoing[0]);
        Check(pendingCount()==0,"unauthenticated partial frames allocate no retained buffers");
        foreach(var p in outgoing.Reverse())native.PacketBytes(new object(),p);
        Check(calls==0,"unauthenticated peer fragments never dispatch");
        foreach(var p in outgoing.Reverse())native.PacketBytes(peer,p);
        Check(calls==1&&received==payload,"actual OnPacket reassembles complete UTF8 and dispatches once");
        foreach(var p in outgoing)native.PacketBytes(peer,p);
        Check(calls==1,"complete duplicate frame delivery preserves actual envelope replay protection");
        var stale=BossRushCoopJson.ToJson(new BossRushCoopEnvelope{session="other-room",topic="large-personal",messageId=Guid.NewGuid().ToString("N"),json=payload});
        foreach(var p in Frames(Encoding.UTF8.GetBytes(stale)))native.PacketBytes(peer,p);
        Check(calls==1,"reassembled previous room content remains rejected");
        native.PacketBytes(peer,outgoing[0]);Check(pendingCount()==1,"real runtime retains authenticated incomplete frame");
        BossRushCoopTransport.NativePeers.Clear();UnityEngine.Time.unscaledTime=1;host.OnUpdate(1,1);
        Check(pendingCount()==0,"real disconnect event drops retained bytes");
        fragments.Receive(peer,outgoing[0],1,out var ignored);
        host.OnDestroy();Check(pendingCount()==0,"runtime shutdown resets partial frames");
    }
}
