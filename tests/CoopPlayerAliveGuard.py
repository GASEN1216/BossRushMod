"""房主存活事实的生产发送、消费、生命周期绑定与真实 player 回归接线。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]

def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

def body(source, signature):
    assert source.count(signature) == 1, "unique member: " + signature
    start = source.index("{", source.index(signature))
    depth = 0
    for index in range(start, len(source)):
        depth += (source[index] == "{") - (source[index] == "}")
        if depth == 0:
            return source[start + 1:index]
    raise AssertionError("unclosed member: " + signature)

def norm(source):
    return re.sub(r"\s+", " ", source).strip()

def main():
    player = norm(body(read("Common/Coop/BossRushCoop.cs"), "public bool IsAlive"))
    for token in ("!IsConnected || Character == null || Character.Health == null", "IsLocal || (BossRushCoop.Runtime != null && BossRushCoop.Runtime.Host)",
                  "return !Character.Health.IsDead;", "!BossRushCoop.Runtime.HasHostPeer", "HostAlive = false; return false;", "BossRushCoop.IsClient && HostAlive", "HostAliveGeneration == BossRushCoop.SceneGeneration",
                  "string.Equals(HostAliveAssetId, AssetId, StringComparison.Ordinal)", "ReferenceEquals(HostAliveCharacter, Character)"):
        assert token in player, "player authority/binding: " + token
    assert "CurrentHealth" not in player, "official IsDead remains native death predicate"
    runtime = read("Common/Coop/BossRushCoopRuntimeModule.cs")
    sender = norm(body(runtime, "private void SendRoster()"))
    assert "alive = players[i].IsAlive" in sender, "host emits actual life, never a constant"
    receiver = norm(body(runtime, "private void OnRoster(BossRushCoopRoster roster)"))
    assert "roster.revision <= rosterRevision || roster.generation < Generation" in receiver, "old life snapshots cannot resurrect"
    for token in ("ClearRemoteAlive();", "if (localSceneHandle != generationSceneHandle) return;", "player.HostAlive = entry.connected && entry.alive;",
                  "player.HostAliveAssetId = player.AssetId;", "player.HostAliveGeneration = Generation;", "player.HostAliveCharacter = player.Character;"):
        assert token in receiver, "roster production binding: " + token
    assert receiver.index("transport.RefreshCharacters(OnNativePlayerSpawned);") < receiver.index("player.HostAlive ="), "bind actual body before host fact"
    for signature in ("private void SetGeneration(int generation)", "private void RefreshSceneIdentity()"):
        assert "ClearRemoteAlive();" in body(runtime, signature), "new generation/scene retires old life: " + signature
    assert "player.HostAlive = false;" in body(runtime, "private void OnNativePlayerSpawned(string nativeId, CharacterMainControl character, bool local)"), "replacement body awaits fresh fact"
    assert "player.HostAlive = false;" in body(runtime, "private void OnPeerDisconnected(object peer)"), "disconnect retires life"
    assert "public bool alive;" in body(read("Common/Coop/BossRushCoopProtocol.cs"), "internal sealed class BossRushCoopRosterEntry"), "wire life fact"
    stub = read("tests/fixtures/CoopSession/Stubs.cs")
    assert "class BossRushCoopPlayer" not in stub, "fixture must execute actual production player"
    project = (ROOT / "tests/fixtures/CoopSession/CoopSession.csproj").read_text(encoding="utf-8-sig")
    assert "ProductionPlayer.g.cs" in project and "prepare.py" in project, "build-time complete production declaration"
    print("CoopPlayerAliveGuard: PASS")

if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("CoopPlayerAliveGuard: FAIL - " + str(error))
        raise SystemExit(1)
