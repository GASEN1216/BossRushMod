"""Guard: zombie spawn-point selection should reject near points with squared distance first."""

from pathlib import Path
import sys
from cs_source_util import clean_source


SOURCE = Path("ZombieMode/ZombieModeSpawner.cs")


def fail(message: str) -> int:
    print("ZombieModeSpawnSelectionSqrGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = clean_source(SOURCE.read_text(encoding="utf-8-sig"))
    body = extract_method_body(text, "internal bool TryGetNearestZombieModeMapSpawnPositionToPlayer(out Vector3 position)")
    if body is None:
        return fail("missing TryGetNearestZombieModeMapSpawnPositionToPlayer body")

    if ".magnitude" in body or "Mathf.Sqrt" in body:
        return fail("spawn selection should compare squared distances only")

    required = [
        "float preferredMinDistance = GetZombieModeSpawnPointMinPlayerDistance();",
        "float preferredMinDistanceSqr = preferredMinDistance * preferredMinDistance;",
        "float fallbackMinDistanceSqr = ZombieModeTuning.SpawnPointMinPlayerDistance * ZombieModeTuning.SpawnPointMinPlayerDistance;",
        "float distanceSqr = delta.sqrMagnitude;",
        "if (distanceSqr < fallbackMinDistanceSqr)",
        "bool preferred = entry.DistanceSqr >= preferredMinDistanceSqr;",
        "zombieModeSpawnScanEntries.Sort(ZombieModeSpawnScanOrder);",
        "int bestIndex = bestPreferredIndex >= 0 ? bestPreferredIndex : bestFallbackIndex;",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing squared-distance spawn selection snippet -> " + snippet)

    reliable = extract_method_body(text, "internal bool TryGetZombieModeReliableSpawnPosition(out Vector3 position)")
    anchors = extract_method_body(text, "private bool HasZombieModeReachableSpawnAnchor()")
    if reliable is None or anchors is None:
        return fail("missing reliable spawn failure cache or eligible anchor check")
    for snippet in ("zombieModeReliableSpawnFailedFrame == frame", "!HasZombieModeReachableSpawnAnchor()", "zombieModeReliableSpawnFailedFrame = frame;"):
        if snippet not in reliable:
            return fail("missing per-frame search gate -> " + snippet)
    if reliable.index("!HasZombieModeReachableSpawnAnchor()") > reliable.index("TryGetNearestZombieModeMapSpawnPositionToPlayer("):
        return fail("navigation anchor check must precede the point scan")
    for snippet in ("ZombieModeLifecyclePhase.InitializingRun", "(!initializing && !IsCoopZombieParticipant(player))", "SpawnPositionHelper.IsReachableAnchor(player.Character.transform.position)"):
        if snippet not in anchors:
            return fail("co-op anchor must preserve initial alive / active admitted eligibility -> " + snippet)

    print("ZombieModeSpawnSelectionSqrGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
