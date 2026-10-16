"""ready belongs to the current asset incarnation, host handshake, and actual scene."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(source, signature):
    matches = list(re.finditer(re.escape(signature) + r"(?=\s*\{)", source))
    assert len(matches) == 1, "unique member: " + signature
    start = source.index("{", matches[0].start())
    depth = 0
    for i in range(start, len(source)):
        depth += (source[i] == "{") - (source[i] == "}")
        if depth == 0:
            return re.sub(r"\s+", " ", source[start + 1:i]).strip()
    raise AssertionError("unclosed member: " + signature)


def need(source, tokens, label):
    for token in tokens:
        assert token in source, label + ": " + token


def main():
    protocol = read("Common/Coop/BossRushCoopProtocol.cs")
    for dto in ("BossRushCoopHello", "BossRushCoopWelcome", "BossRushCoopRosterEntry", "BossRushCoopReady", "BossRushCoopReadyMember"):
        need(body(protocol, "internal sealed class " + dto), ("public string assetId;", "public long assetRevision;"), dto)
    for dto in ("BossRushCoopWelcome", "BossRushCoopRosterEntry", "BossRushCoopReady", "BossRushCoopReadyMember"):
        need(body(protocol, "internal sealed class " + dto), ("public string readyEpoch;",), dto)
    need(body(protocol, "internal sealed class BossRushCoopReady"), ("public long revision;", "public BossRushCoopReadyMember[] members;"), "complete wire snapshot")
    need(body(protocol, "internal bool Replace(string sceneKey, IEnumerable<string> players)"), ("ready[sceneKey] = new HashSet<string>(players, StringComparer.Ordinal);", "ready.Count >= 64"), "replace bounded membership")
    runtime = read("Common/Coop/BossRushCoopRuntimeModule.cs")
    need(body(runtime, "internal bool HasBoundScene"), ("HasCurrentSceneCore()",), "actual Core binding")
    hello = body(runtime, "private void OnHello(object peer, BossRushCoopHello hello)")
    need(hello, ("hello.assetRevision <= 0", "hello.assetRevision < player.AssetRevision", "hello.assetRevision == player.AssetRevision", "bool identityChanged = player.AssetId != hello.assetId || player.AssetRevision != hello.assetRevision;",
                 "bool connectionChanged = !player.IsConnected || !ReferenceEquals(player.Peer, peer);", "if (identityChanged || connectionChanged)", "barrier.Remove(player.Id);", "player.ReadyEpoch = Guid.NewGuid().ToString(\"N\");",
                 "assetRevision = player.AssetRevision, readyEpoch = player.ReadyEpoch"), "host admission identity")
    welcome = body(runtime, "private void OnWelcome(BossRushCoopWelcome welcome)")
    need(welcome, ("welcome.helloSequence < minimumWelcomeSequence", "welcome.assetId != GetLocalAssetId()", "welcome.assetRevision != localAssetRevision", "string.IsNullOrEmpty(welcome.readyEpoch)", "welcomeHostPeer = transport.HostPeer;", "foreach (string key in localReadyKeys) SendReady(key);"), "welcome binding")
    invalidate = body(runtime, "private void InvalidateReadyHandshake()")
    need(invalidate, ("minimumWelcomeSequence = helloSequence + 1;", "welcomeHostPeer = helloHostPeer = null; localReadyEpoch = null;", "barrier.Remove(LocalId);"), "connection gap invalidation")
    asset = body(runtime, "internal string GetLocalAssetId()")
    need(asset, ("localAssetRevision++;", "localReadyKeys.Clear(); barrier.Remove(LocalId);", "localReadyEpoch = Host ? Guid.NewGuid().ToString(\"N\") : null;"), "slot incarnation")
    sender = body(runtime, "private void SendReady(string key)")
    need(sender, ("if (!HasBoundScene || string.IsNullOrEmpty(localReadyEpoch)) return;", "assetId = localAssetId, assetRevision = localAssetRevision, readyEpoch = localReadyEpoch", "ReferenceEquals(welcomeHostPeer, transport.HostPeer)"), "actual ready sender")
    receiver = body(runtime, "private void OnReady(string sender, BossRushCoopReady ready)")
    need(receiver, ("ready.assetId != player.AssetId", "ready.assetRevision != player.AssetRevision", "ready.readyEpoch != player.ReadyEpoch", "ready.revision <= applied", "member.assetId == player.AssetId", "member.assetRevision == player.AssetRevision", "member.readyEpoch == player.ReadyEpoch", "barrier.Replace(ready.key, accepted)", "appliedReadyRevisions[ready.key] = ready.revision;"), "actual ready receiver")
    roster = body(runtime, "private void OnRoster(BossRushCoopRoster roster)")
    need(roster, ("if (!entry.connected || player.AssetId != nextAsset || player.AssetRevision != nextAssetRevision || player.ReadyEpoch != nextEpoch) barrier.Remove(player.Id);",), "roster identity retirement")
    broadcast = body(runtime, "private void BroadcastReady(string key, object targetPeer = null)")
    need(broadcast, ("revision = ++readyRevision, members = members.ToArray()", "assetRevision = player.AssetRevision, readyEpoch = player.ReadyEpoch"), "host complete ready snapshots")
    need(body(runtime, "private void OnPeerDisconnected(object peer)"), ("player.ReadyEpoch = null;", "barrier.Remove(player.Id);", "BroadcastReady(key)"), "disconnect retirement")
    need(body(runtime, "internal bool AllPlayersSceneReady(string key)"), ("if (!Ready || !HasBoundScene) return false;",), "all participants actual Core")
    need(body(runtime, "internal bool ArePlayersSceneReady(string key, IEnumerable<string> participants)"), ("if (!Ready || !HasBoundScene || participants == null) return false;",), "selected participants actual Core")
    fixture = (ROOT / "tests/fixtures/CoopSession/CoopSession.csproj").read_text(encoding="utf-8-sig")
    assert 'Compile Include="ReadyAssetIdentity.cs"' in fixture, "actual runtime lifecycle regression must be compiled"
    assert "class BossRushCoopPlayer" not in read("tests/fixtures/CoopSession/Stubs.cs"), "player must be production declaration"
    print("CoopReadyAssetOwnershipGuard: PASS")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("CoopReadyAssetOwnershipGuard: FAIL - " + str(error))
        raise SystemExit(1)
