"""Keep zombie shared-drop arbitration attached to the real native pickup lifecycle."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(source, signature):
    assert source.count(signature) == 1, "missing or ambiguous method: " + signature
    start = source.index("{", source.index(signature))
    end, depth = start + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return re.sub(r"\s+", " ", source[start + 1:end - 1])


def require(text, *tokens):
    for token in tokens:
        assert token in text, "missing shared-drop contract: " + token


def main():
    claims = read("ZombieMode/ZombieModeCoopDropClaims.cs")
    drop = body(read("ZombieMode/ZombieModeDropsAndPerformance.cs"), "private GameObject TryDropZombieModeItemNearPosition(")
    marker = "if (BossRushCoop.IsConnected && !bossDrop) RegisterCoopZombieSharedDrop(runId, item);"
    require(drop, marker)
    assert drop.index(marker) < drop.index("item.Drop(dropPosition, true, dropDirection,"), "mark ordinary shared drop before its native snapshot"
    coop = read("ZombieMode/ZombieModeRuntimeModule_Coop.cs")
    for signature, call in (
        ("private void InitializeCoopZombie()", "InitializeCoopZombieDropClaims();"),
        ("private void ResetCoopZombieRun(", "ResetCoopZombieDropClaims(runId);"),
        ("private void CleanupCoopZombie()", "CleanupCoopZombieDropClaims();"),
        ("public override void OnUpdate(", "TickCoopZombieDropClaims(unscaledDeltaTime);"),
    ):
        require(body(coop, signature), call)
    initialize = body(claims, "private void InitializeCoopZombieDropClaims()")
    require(initialize, 'typeof(CharacterItemControl).GetMethod("PickupItem", new[] { typeof(Item) })',
            '"PrefixCoopZombieDropPickup"', "ItemSnapshotRequested += WriteCoopZombieDropMarker;",
            "ItemSnapshotApplied += ReadCoopZombieDropMarker;")
    prefix = body(claims, "private static bool PrefixCoopZombieDropPickup(")
    require(prefix, "!module.coopZombieDropMarkers.TryGetValue(item, out marker)",
            "ReferenceEquals(module.coopZombieDropApplying, item)", "__result = false;")
    assert prefix.index("TryGetValue") < prefix.index("IsCoopZombieDropCurrent"), "unmarked native pickup must take the constant-time direct path"
    restored = body(claims, "private void ReadCoopZombieDropMarker(")
    assert "IsCoopZombieDropCurrent(marker)" not in restored, "native item metadata may precede the zombie world snapshot"
    assert "BossRushCoop.IsClient" not in restored and "BossRushCoop.IsHost" not in restored, "native metadata may precede the adapter role refresh"
    require(restored, "coopZombieDropMarkers[item] = marker;")
    accept = body(claims, "private void AcceptCoopZombieDropClaim(")
    require(accept, "!IsCoopZombieParticipant(player)", "!player.IsAlive", "player.AssetId != claim.Asset",
            "record.Finalized.ContainsKey(claim.Nonce)", "if (record.Grant != null)", "record.Holder = sender;", "record.Grant = claim;")
    assert accept.index("if (record.Grant != null)") < accept.index("record.Grant = claim;"), "holder must be checked before reserving the item"
    apply = body(claims, "private bool ApplyCoopZombieDropGrant(")
    require(apply, "Outcome = 2", "coopZombieDropOutcomes.Add(grant.Nonce, result);", "bool success = player.PickupItem(item);",
            "item.StackCount < initialCount", "result.Outcome = 3; result.Remaining = item.StackCount;",
            "networkDrop.Dispose();", "BossRushCoopTransport.ProtectNetworkDropPickup(item);")
    assert apply.index("coopZombieDropOutcomes.Add") < apply.index("player.PickupItem(item)"), "record the attempt before native mutation or notifications"
    result = body(claims, "private void AcceptCoopZombieDropResult(")
    require(result, "record.Finalized.TryGetValue", "record.Holder != sender", "result.Outcome == 1 || result.Outcome == 3",
            "result.Remaining <= 0 || result.Remaining >= record.Grant.Count", "CompleteNetworkDrop(record.NativeDrop)",
            "RestoreCoopZombieDropRemainder(record, result.Remaining)")
    clear = "else if (result.Outcome == 0) { record.Grant = null; record.Holder = null; }"
    require(result, clear)
    remainder = body(claims, "private bool RestoreCoopZombieDropRemainder(")
    require(remainder, "item.StackCount = remaining;", "RegisterCoopZombieSharedDrop(record.Marker.Run, item);",
            "item.AgentUtilities.ReleaseActiveAgent();", "item.Drop(record.Position, true, Vector3.forward, 18f);")
    assert remainder.index("RegisterCoopZombieSharedDrop") < remainder.index("item.Drop("), "the remainder snapshot needs its fresh marker"
    reset = body(claims, "private void ResetCoopZombieDropClaims(")
    require(reset, "BossRushCoopTransport.CompleteNetworkDrop", "marker.Run == keepRun", "RetireCoopZombieDropItem(pair.Key);",
            "lease == null && IsCoopZombieDropScopeCurrent(record.Marker)")
    assert reset.index("RetireCoopZombieDropItem") < reset.index("coopZombieDropMarkers.Remove"), "retired ground copies must not become unmarked native pickups"
    retire = body(claims, "private static void RetireCoopZombieDropItem(")
    require(retire, "item.InInventory != null || item.PluggedIntoSlot != null", "ReleaseActiveAgent();", "UnityEngine.Object.Destroy(item.gameObject);")
    transport = read("Common/Coop/BossRushCoopTransport.cs")
    complete = body(transport, "internal static bool CompleteNetworkDrop(")
    require(complete, "lease.Room != BossRushCoop.RunId", "lease.Generation != BossRushCoop.SceneGeneration",
            "lease.SceneToken != BossRushCoop.SceneToken", "!ReferenceEquals(lease.Registry, Read(database, null, \"Drops\"))",
            "!ReferenceEquals(lookup[1], lease.Entry)", '!ReferenceEquals(Read(lookup[1].GetType(), lookup[1], "Item"), lease.Item)', "lease.Pickup.Invoke", "lease.Broadcast.Invoke")
    protect = body(transport, "internal static void ProtectNetworkDropPickup(")
    require(protect, '"_pendingPickups"', '"Contains"', '"Client_RequestPickup"')
    print("ZombieModeCoopDropClaimsGuard PASS")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("ZombieModeCoopDropClaimsGuard FAIL:", error)
        raise SystemExit(1)
