"""ZombieModeBossRushSpawnPointsOnlyGuard: ZombieMode uses BossRush stored map spawn points only."""

from pathlib import Path
import re
import sys


SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")


def fail(message: str) -> int:
    print("ZombieModeBossRushSpawnPointsOnlyGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, method_name: str) -> str:
    match = re.search(r"\b" + re.escape(method_name) + r"\s*\([^)]*\)\s*\{", text)
    if match is None:
        return ""

    depth = 0
    for index in range(match.end() - 1, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[match.start():index + 1]
    return ""


def main() -> int:
    spawner = SPAWNER.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")

    collect_method = extract_method_body(spawner, "CollectZombieModeSpawnPoints")
    if not collect_method:
        return fail("CollectZombieModeSpawnPoints not found")

    if "zombieModeRunState.MapProfile.StaticSpawnPoints" not in collect_method:
        return fail("spawn collection must read ZombieModeMapProfile.StaticSpawnPoints")
    if "TryPopulateZombieModeSpawnPointsFromCachedOriginalSpawnerPositions" not in collect_method:
        return fail("spawn collection must reuse cached original spawner positions before giving up")

    if "GetCurrentMapConfig()" in collect_method:
        return fail("spawn collection must not rebuild its own map-config point source")

    forbidden_tokens = [
        "CollectZombieModeOriginalSpawnerPoints",
        "CharacterSpawnerRoot",
        "FindObjectsOfType<CharacterSpawnerRoot>",
        "mapConfig.modeESpawnPoints",
        "mapConfig.spawnPoints",
        "mapConfig.customSpawnPos",
        "for (int i = 0; i < 16; i++)",
        "Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 24f",
    ]
    for token in forbidden_tokens:
        if token in spawner:
            return fail("ZombieModeSpawner still depends on original or duplicate map point source: " + token)

    profile_assignment = (
        "profile.StaticSpawnPoints = mapConfig.modeESpawnPoints != null && mapConfig.modeESpawnPoints.Length > 0\n"
        "                    ? mapConfig.modeESpawnPoints\n"
        "                    : (mapConfig.spawnPoints ?? new Vector3[0]);"
    )
    if profile_assignment not in entry:
        return fail("MapProfile must keep modeE points first and BossRush spawnPoints fallback")

    print("ZombieModeBossRushSpawnPointsOnlyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
