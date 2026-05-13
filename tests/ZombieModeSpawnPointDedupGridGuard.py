"""ZombieMode spawn point duplicate checks must use a bounded grid."""

from pathlib import Path
import re
import sys


SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")


def fail(message: str) -> int:
    print("ZombieModeSpawnPointDedupGridGuard: FAIL - " + message)
    return 1


def extract_method(text: str, name: str) -> str:
    match = re.search(r"\b" + re.escape(name) + r"\s*\([^)]*\)\s*\{", text)
    if not match:
        return ""
    start = match.end()
    depth = 1
    i = start
    while i < len(text) and depth > 0:
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
        i += 1
    return text[start:i - 1]


def main() -> int:
    text = SPAWNER.read_text(encoding="utf-8")
    required = [
        "zombieModeSpawnPointDedupGrid",
        "ResetZombieModeSpawnPointDedupGrid();",
        "HasZombieModeDuplicateSpawnPoint(snapped)",
        "RegisterZombieModeSpawnPointDedupCell(spawnPoint)",
        "GetZombieModeSpawnPointDedupCellKey",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing grid dedupe snippet: " + snippet)

    add_method = extract_method(text, "AddZombieModeSpawnPoint")
    if not add_method:
        return fail("AddZombieModeSpawnPoint not found")

    if "for (int i = 0; i < zombieModeRunState.SpawnPoints.Count; i++)" in add_method:
        return fail("AddZombieModeSpawnPoint still scans every existing spawn point")

    duplicate_method = extract_method(text, "HasZombieModeDuplicateSpawnPoint")
    if "xOffset = -1" not in duplicate_method or "zOffset = -1" not in duplicate_method:
        return fail("duplicate check must inspect neighboring grid cells")
    if "delta.y = 0f" not in duplicate_method or "delta.sqrMagnitude < duplicateDistanceSqr" not in duplicate_method:
        return fail("duplicate check must preserve flat distance threshold")

    print("ZombieModeSpawnPointDedupGridGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
