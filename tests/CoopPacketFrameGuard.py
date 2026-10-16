"""原版 UInt16 载荷边界、完整包重组及真实运行时接线。行为由 CoopSession 回归证明。"""
from CoopPlayerAliveGuard import body, norm, read

def main():
    frames=read('Common/Coop/BossRushCoopPacketFrames.cs')
    assert 'internal const int MaxMessageBytes = 262144;' in frames, 'preserve original complete message budget'
    assert 'internal const int MaxFrameBytes = 60000;' in frames, 'native frame including header must fit UInt16'
    send=norm(body(frames,'internal static bool Send('))
    for token in ('message.Length > MaxMessageBytes', 'message.Length <= MaxFrameBytes',
                  'Math.Min(PayloadBytes, message.Length - offset)', 'new byte[HeaderBytes + size]',
                  'sha.ComputeHash(message)', 'if (!send(frame)) return false;'):
        assert token in send, 'sender boundary/completeness: '+token
    receive=norm(body(frames,'internal bool Receive('))
    for token in ('frame.Length > MaxFrameBytes', 'length > MaxMessageBytes', 'index < 0 || index >= count',
                  'entries.Count >= MaxPendingPerPeer', 'value.Length != length',
                  '!Equal(value.Hash, 0, frame, 28, 32)', 'value.Parts[index] != null',
                  'if (++value.Received != count) return false;',
                  'sha.ComputeHash(complete)', 'if (!Equal(actual, 0, value.Hash, 0, 32)) return false;'):
        assert token in receive, 'receiver exact complete frame set: '+token
    runtime=read('Common/Coop/BossRushCoopRuntimeModule.cs')
    prune=norm(body(frames,'internal void Prune('))
    assert 'entry.Value.Expires <= now' in prune and 'peer.Value.Remove(id);' in prune, 'expire abandoned buffers'
    sendraw=norm(body(runtime,'private bool SendRaw('))
    assert 'return BossRushCoopPacketFrames.Send(bytes, frame => transport.Send(peer, frame));' in sendraw, 'real native send must consume framed bytes'
    packet=norm(body(runtime,'private void OnPacket('))
    assert 'Host ? frameSender == null || !frameSender.IsConnected : !ReferenceEquals(peer, transport.HostPeer)' in packet, 'authenticate sender before retaining fragmented data'
    assert packet.index('packetFrames.Receive(') < packet.index('FromJson<BossRushCoopEnvelope>'), 'assemble full envelope before decoding'
    assert 'if (!replay.Accept(senderId, envelope.messageId)) return;' in packet, 'completed duplicate envelopes retain replay gate'
    assert 'packetFrames.Clear();' in body(runtime,'private void ResetSession(string acceptedSceneSession = null, bool retainNativeScene = false)'), 'reset retires partial frames'
    assert 'packetFrames.Remove(peer);' in body(runtime,'private void OnPeerDisconnected('), 'disconnect retires peer frames'
    assert 'packetFrames.Prune(now);' in body(runtime,'public override void OnUpdate('), 'runtime expires abandoned partial frames'
    print('CoopPacketFrameGuard: PASS (structure only; runtime/native codec evidence separate)')

if __name__=='__main__':main()
