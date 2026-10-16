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
                  "return !Character.Health.IsDead;", "!BossRushCoop.Runtime.HasHostPeer", "!BossRushCoop.Runtime.HasBoundScene", "HostAlive = false; return false;", "BossRushCoop.IsClient && HostAlive", "HostAliveGeneration == BossRushCoop.SceneGeneration",
                  "string.Equals(HostAliveAssetId, AssetId, StringComparison.Ordinal)", "ReferenceEquals(HostAliveCharacter, Character)"):
        assert token in player, "player authority/binding: " + token
    assert "CurrentHealth" not in player, "official IsDead remains native death predicate"
    runtime = read("Common/Coop/BossRushCoopRuntimeModule.cs")
    sender = norm(body(runtime, "private void SendRoster()"))
    assert "alive = players[i].IsAlive" in sender, "host emits actual life, never a constant"
    receiver = norm(body(runtime, "private void OnRoster(BossRushCoopRoster roster)"))
    assert "roster.revision <= rosterRevision || roster.generation < Generation" in receiver, "old life snapshots cannot resurrect"
    assert "roster.generation < pendingGeneration" in receiver, "pending future generation cannot be replaced by intermediate old rosters"
    for token in ("ClearRemoteAlive();", "if (!TryBindGenerationScene(roster.sceneId, roster.sceneToken)) return;", "player.HostAlive = entry.connected && entry.alive;",
                  "player.HostAliveAssetId = player.AssetId;", "player.HostAliveGeneration = Generation;", "player.HostAliveCharacter = player.Character;"):
        assert token in receiver, "roster production binding: " + token
    assert receiver.index("transport.RefreshCharacters(OnNativePlayerSpawned);") < receiver.index("player.HostAlive ="), "bind actual body before host fact"
    for signature in ("private void SetGeneration(int generation)", "private void RefreshSceneIdentity()"):
        assert "ClearRemoteAlive();" in body(runtime, signature), "new generation/scene retires old life: " + signature
    assert "sceneId = officialSceneId" in sender, "host binds roster to its actual Core scene ID"
    assert "sceneToken = generationSceneToken" in sender, "host roster carries the token of its committed native load"
    pending = norm(body(runtime, "private void SetGeneration(int generation)"))
    assert "pendingGeneration = generation;" in pending and not re.search(r"(?<!\w)Generation\s*=", pending), "future host generation stays private until native Core binding"
    binding = norm(body(runtime, "private bool TryBindGenerationScene(string sceneId, string sceneToken)"))
    for token in ("RefreshSceneIdentity();", "!HasCurrentSceneCore()", "string.IsNullOrEmpty(sceneId)", "!string.Equals(sceneId, officialSceneId, StringComparison.Ordinal)",
                  "if (Generation == 0) transport.SceneIdentity.TryAdoptInitial(sceneToken, sceneId);",
                  "if (!transport.SceneIdentity.Matches(sceneToken)) return false;", "Generation = pendingGeneration;", "pendingGeneration = 0;",
                  "generationSceneHandle = localSceneHandle;", "generationSceneToken = sceneToken;", "generationScenePending = false;", "return HasBoundScene && generationSceneToken == sceneToken;"):
        assert token in binding, "actual target-scene generation binding: " + token
    assert binding.index("!transport.SceneIdentity.Matches(sceneToken)") < binding.index("Generation = pendingGeneration;"), "token proof precedes public world generation and cleanup"
    assert "return false;" in binding[binding.index("generationScenePending = false;"):], "binding packet cannot revive remote bodies"
    actual = norm(body(runtime, "private bool HasCurrentSceneCore()"))
    for token in ("core == null || core.SceneInfo == null || core.IsLoading", "scene.IsValid() && scene.isLoaded && scene.handle == localSceneHandle", "string.Equals(core.SceneInfo.ID, officialSceneId, StringComparison.Ordinal)"):
        assert token in actual, "actual loaded Core required: " + token
    bound = norm(body(runtime, "internal bool HasBoundScene"))
    assert "Generation > 0 && !generationScenePending && localSceneHandle == generationSceneHandle && HasCurrentSceneCore()" in bound, "bound Core must still exist and be loaded"
    assert "transport.SceneIdentity.Matches(generationSceneToken)" in bound, "native Begin immediately invalidates old binding before physical Core replacement"
    assert "if (!HasBoundScene || string.IsNullOrEmpty(localReadyEpoch)) return;" in norm(body(runtime, "private void SendReady(string key)")), "unbound host/client scene cannot report generation readiness"
    reset = norm(body(runtime, "private void ResetSession(string acceptedSceneSession = null, bool retainNativeScene = false)"))
    assert "generationScenePending = false; pendingGeneration = 0; generationSceneToken = null;" in reset, "new room clears pending scene binding"
    assert "transport.SceneIdentity.ResetSession(acceptedSceneSession);" in reset, "new room/stop retires actual scene ownership"
    assert "ResetSession(null, started && !host);" in norm(body(runtime, "public override void OnUpdate(")), "client native Begin preceding first update must survive until the exact welcome"
    assert "ResetSession(welcome.session);" in norm(body(runtime, "private void OnWelcome(")), "switching rooms may retain only the accepted room's native load"
    refresh = norm(body(runtime, "private void RefreshSceneIdentity()"))
    for token in ("transport.SceneIdentity.ObserveCore(core);", "string token = transport.SceneIdentity.LocalToken;", "if (string.IsNullOrEmpty(token) || token == generationSceneToken) return;", "generationSceneToken = token;"):
        assert token in refresh, "host commits only actual stable native load: " + token
    assert "player.HostAlive = false;" in body(runtime, "private void OnNativePlayerSpawned(string nativeId, CharacterMainControl character, bool local)"), "replacement body awaits fresh fact"
    assert "player.HostAlive = false;" in body(runtime, "private void OnPeerDisconnected(object peer)"), "disconnect retires life"
    assert "public bool alive;" in body(read("Common/Coop/BossRushCoopProtocol.cs"), "internal sealed class BossRushCoopRosterEntry"), "wire life fact"
    stub = read("tests/fixtures/CoopSession/Stubs.cs")
    assert "class BossRushCoopPlayer" not in stub, "fixture must execute actual production player"
    project = (ROOT / "tests/fixtures/CoopSession/CoopSession.csproj").read_text(encoding="utf-8-sig")
    assert "ProductionPlayer.g.cs" in project and "prepare.py" in project, "build-time complete production declaration"
    assert "BossRushCoopSceneIdentity.cs" in project and "SceneGeneration.cs" in project, "full real scene identity and runtime lifecycle cases must execute together"
    print("CoopPlayerAliveGuard: PASS")

if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("CoopPlayerAliveGuard: FAIL - " + str(error))
        raise SystemExit(1)
