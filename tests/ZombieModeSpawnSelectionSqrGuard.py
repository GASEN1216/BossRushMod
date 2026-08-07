"""Guard: zombie spawn-point selection should reject near points with squared distance first."""

from pathlib import Path
import sys


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
    text = SOURCE.read_text(encoding="utf-8")
    body = extract_method_body(text, "private bool TryGetNearestZombieModeMapSpawnPositionToPlayer(out Vector3 position)")
    if body is None:
        return fail("missing TryGetNearestZombieModeMapSpawnPositionToPlayer body")

    if ".magnitude" in body or "Mathf.Sqrt" in body:
        return fail("spawn selection should compare squared distances only")

    required = [
        "float preferredMinDistance = GetZombieModeSpawnPointMinPlayerDistance();",
        "float preferredMinDistanceSqr = preferredMinDistance * preferredMinDistance;",
        "float fallbackMinDistanceSqr = ZombieModeTuning.SpawnPointMinPlayerDistance * ZombieModeTuning.SpawnPointMinPlayerDistance;",
        "float distanceSqr = main != null ? delta.sqrMagnitude : offset;",
        "if (main != null && distanceSqr < fallbackMinDistanceSqr)",
        "if (main != null && distanceSqr < preferredMinDistanceSqr)",
        "int bestIndex = bestPreferredIndex >= 0 ? bestPreferredIndex : bestFallbackIndex;",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing squared-distance spawn selection snippet -> " + snippet)

    print("ZombieModeSpawnSelectionSqrGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
