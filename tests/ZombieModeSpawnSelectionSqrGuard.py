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
    body = extract_method_body(text, "private Vector3 GetZombieModeSpawnPosition()")
    if body is None:
        return fail("missing GetZombieModeSpawnPosition body")

    if "float distance = delta.magnitude;" in body:
        return fail("spawn selection still computes magnitude before min-distance rejection")

    required = [
        "float minPlayerDistance = ZombieModeTuning.SpawnPointMinPlayerDistance;",
        "float minPlayerDistanceSqr = minPlayerDistance * minPlayerDistance;",
        "float distanceSqr = delta.sqrMagnitude;",
        "if (distanceSqr < minPlayerDistanceSqr)",
        "float distance = Mathf.Sqrt(distanceSqr);",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing squared-distance spawn selection snippet -> " + snippet)

    print("ZombieModeSpawnSelectionSqrGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
