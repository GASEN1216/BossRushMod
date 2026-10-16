"""COOP 绑定失败权限、兼容诊断与发送委托复用的生产接线。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]

def body(path, signature):
    source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    assert source.count(signature) == 1, signature
    begin = source.index("{", source.index(signature))
    depth = 1
    end = begin + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return re.sub(r"\s+", " ", source[begin:end])

def main():
    transport = "Common/Coop/BossRushCoopTransport.cs"
    runtime = "Common/Coop/BossRushCoopRuntimeModule.cs"
    facade = "Common/Coop/BossRushCoop.cs"
    bind = body(transport, "internal bool TryBind()")
    assert "writerBuilder = Adapt(writerActionType, WriteOutgoing);" in bind, "compile writer once during successful binding"
    for signature in ("internal bool Send(", "private void SendSceneIdentityMarker("):
        method = body(transport, signature)
        assert "Adapt(" not in method and "writerBuilder" in method, "reuse cached native writer: " + signature
        assert "finally { outgoing = previous; }" in method, "restore reentrant/failing native payload: " + signature
    probe = body(transport, "internal bool TryGetNetworkState(")
    assert "if (!(active is bool)) { started = true; return false; }" in probe, "unreadable active connection is not solo"
    assert "if (!(role is bool)) return false;" in probe, "unknown native role cannot grant host authority"
    update = body(runtime, "public override void OnUpdate(")
    assert "transport.TryGetNetworkState(out started, out host)" in update, "probe native state independently of binding"
    assert "NetworkBlocked = started && (!known || !transport.Bound);" in update, "online binding failure stays fail-closed"
    assert update.index("NetworkBlocked =") < update.index("if (!transport.Bound) return;"), "do not return before updating failed-binding authority"
    assert "PublishCompatibilityStatus(now);" in update, "missing peers are diagnosed through real periodic runtime"
    assert "Runtime.Connected || Runtime.NetworkBlocked" in body(facade, "public static bool IsConnected"), "online unsupported adapter is not solo"
    assert "!Runtime.NetworkBlocked && Runtime.Host" in body(facade, "public static bool IsHost"), "old host field cannot leak blocked authority"
    assert "IsConnected && !IsHost" in body(facade, "public static bool IsClient"), "unknown role uses client policy"
    hello = body(runtime, "private void OnHello(")
    assert "incompatiblePeers.Add(peer); PublishCompatibilityStatus(Time.unscaledTime, true);" in hello, "mismatch diagnostic reaches the room"
    packet = body(runtime, "private void OnPacket(")
    assert 'envelope.topic == "$compatibility"' in packet and "ApplyCompatibilityStatus(" in packet, "clients consume host compatibility status"
    assert "!incompatiblePeers.Contains(player.Peer)" in body(runtime, "internal bool Ready"), "mismatch cannot remain ready"
    assert "transport.BroadcastCompatibilityNotice(nativeNotice.ToString());" in body(runtime, "private void PublishCompatibilityStatus("), "missing-Mod peers receive native room notice"
    print("CoopCompatibilityGuard: PASS")

if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("CoopCompatibilityGuard: FAIL -", error)
        raise SystemExit(1)
