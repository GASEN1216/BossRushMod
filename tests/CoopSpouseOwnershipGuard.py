"""Personal marriage keeps one independent body per owner and asset, with reversible world ownership."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]

def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))

def body(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index("{", source.index(signature))
    end, level = start + 1, 1
    while level:
        level += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start + 1:end - 1]

def compact(source):
    return re.sub(r"\s+", "", source)

def main():
    source = read("Integration/NPCs/Common/CoopNpcWorld_Spouses.cs")
    world = read("Integration/NPCs/Common/CoopNpcWorld.cs")
    bridge = read("Integration/Wedding/WeddingModBehaviourBridge.cs")
    marriage = read("Integration/Wedding/NPCMarriageSystem.cs")
    key = body(source, "internal static string SpouseWorldId(")
    for token in ("player.Length", "player", "asset.Length", "asset", "npc"):
        assert token in key, "spouse world identity lost personal scope: " + token
    factory = body(source, "private async UniTask<GameObject> CreateIndependentSpouse(")
    assert "Vector3.forward,replica,false" in compact(factory), "independent duck factory must suppress public world registration"
    assert "PermanentDuckNpcRegistry.RegisterInstance" not in factory, "personal spouse overwrites public named registry"
    for singleton in ("owner.SpawnGoblinNPC", "owner.SpawnNurseNPC"):
        assert singleton not in factory, "personal spouse factory writes singleton: " + singleton
    assert "PermanentDuckNpcModule.AttachPermanentParts" in factory, "independent permanent spouse lost original services"
    assert "GoblinInteractable" in factory and "NurseInteractable" in factory, "spouse lost original service interaction"
    request = compact(body(source, "private void OnSpouseRequest("))
    for token in ("FindPlayer(message.SenderId)", "intent.Asset!=player.AssetId", "intent.Run!=BossRushCoop.RunId", "intent.Scene!=BossRushCoop.SceneGeneration"):
        assert token in request, "spouse request lost authenticated/session scope: " + token
    assert "intent.Sequence<sequence" in compact(body(source, "private void ApplySpouseIntent(")), "stale intent can override new home/follow state"
    disconnect = body(source, "private void OnSpouseDisconnected(")
    assert "RemoveSpouse(id)" in disconnect and "spouseSequences.Remove(scope)" in disconnect, "reconnect must release entity and old intent sequence"
    assert "PlayerDisconnected += OnSpouseDisconnected" in body(source, "private void SubscribeFollow()")
    assert "PlayerDisconnected -= OnSpouseDisconnected" in body(source, "private void DisposeFollow()")
    assert "RefreshCoopNpcScope();" in request, "first request can be cleared by first scene tick"
    assert "WorldId != key" in body(source, "private void ApplySpouseIntent("), "changing personal spouse leaves old owner body"
    for signature, token in (("public Transform TrySpawnMarriedNpcAtWeddingPoint()", "RequestLocalSpouseHome"),
                             ("public bool SendSpouseHome(", "RequestFollow(npcId, false)"),
                             ("public void HandleDivorceNpcRelocation(", "ReleaseLocalSpouse"),
                             ("internal GameObject GetSpouseInstance(", "FindLocalSpouse")):
        assert token in body(bridge, signature), "connected marriage entry lost personal actor route: " + signature
    assert "FindLocalSpouse" in body(bridge, "public bool IsWeddingNpcInstance("), "foreign spouse can expose personal marriage commands"
    relocate = body(marriage, "private static void RelocateOrDespawnMarriedNpc(")
    assert relocate.index("if (BossRushCoop.IsConnected) return;") < relocate.index("mod.DestroyGoblinNPC();"), "personal marriage deletes shared resident"
    migration = body(bridge, "internal void RetireOfflineSpouseForCoop(")
    for token in ("coopSpouseMigrationScope", "HasPlayerFollow", "marker.NpcId == npcId", "DestroyWeddingGoblinNpc", "SpawnWeddingGoblinNpc"):
        assert token in migration, "offline spouse migration missing: " + token
    assert "ClearSpouses();" in body(world, "internal void ClearScene()"), "scene cleanup leaves spouse projections"
    for path in ("Integration/NPCs/Goblin/GoblinNPC.cs", "Integration/NPCs/Nurse/NurseNPC.cs", "Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs"):
        assert "!BossRushCoop.IsConnected && AffinityManager.IsMarriedToPlayer" in read(path), "host marriage removes guests' ordinary resident: " + path
    print("CoopSpouseOwnershipGuard PASS")

if __name__ == "__main__":
    main()
