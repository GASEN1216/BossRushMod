"""Legacy guard name: official cached spawn points lead, map-profile points are a validated fallback."""

from pathlib import Path
import re
import sys
from cs_source_util import clean_source


SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
ENTRY = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
RUNTIME_MODULE = Path("ZombieMode/ZombieModeRuntimeModule.cs")
HOST_BRIDGE = Path("ZombieMode/ZombieModeEntryHostBridge.cs")


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
    runtime_module = RUNTIME_MODULE.read_text(encoding="utf-8")
    bridges = clean_source(HOST_BRIDGE.read_text(encoding="utf-8"))
    for name, statement in [
        ("GetZombieModeCachedSpawnerPositionsForRuntimeModule", "return modeECachedSpawnerPositions;"),
        ("GetZombieModeCachedSpawnerSceneNameForRuntimeModule", "return modeECachedSpawnerSceneName;"),
    ]:
        bridge = extract_method_body(bridges, name)
        if not bridge or bridge[bridge.index("{"):].split() != ("{ " + statement + " }").split():
            return fail("cached map point query must retain its original host source -> " + name)
    cache_fallback = extract_method_body(clean_source(spawner), "TryPopulateZombieModeSpawnPointsFromCachedOriginalSpawnerPositions")
    if cache_fallback.count("owner.GetZombieModeCachedSpawnerPositionsForRuntimeModule()") != 3 or cache_fallback.count("owner.GetZombieModeCachedSpawnerSceneNameForRuntimeModule()") != 1:
        return fail("cached map point fallback must retain the original array and scene query order/count")

    collect_method = extract_method_body(clean_source(spawner), "CollectZombieModeSpawnPoints")
    if not collect_method:
        return fail("CollectZombieModeSpawnPoints not found")

    if "runState.MapProfile.StaticSpawnPoints" not in collect_method:
        return fail("spawn collection must read ZombieModeMapProfile.StaticSpawnPoints")
    if "TryPopulateZombieModeSpawnPointsFromCachedOriginalSpawnerPositions" not in collect_method:
        return fail("spawn collection must reuse cached original spawner positions before giving up")
    collect_flat = " ".join(collect_method.split())
    expected = "owner.PreCacheMapSpawnerPositions(); TryPopulateZombieModeSpawnPointsFromCachedOriginalSpawnerPositions(); if (runState.SpawnPoints.Count <= 0 && runState.MapProfile != null) { AddZombieModeSpawnPointArray(runState.MapProfile.StaticSpawnPoints, false); }"
    if expected not in collect_flat:
        return fail("official Points cache must be captured before isolation and preferred over the map-profile fallback")
    reliable = extract_method_body(clean_source(spawner), "TryGetZombieModeReliableSpawnPosition")
    if reliable.find("TryGetNearestZombieModeMapSpawnPositionToPlayer(out position)") > reliable.find("TryFindZombieModeVirtualSpawnAroundPlayer(main.transform.position, out position)"):
        return fail("official/map points must precede virtual ring fallback")
    for name in ("TrySpawnZombieModeNormalZombieAsync", "TrySpawnZombieModeBossAsync"):
        body = extract_method_body(clean_source(spawner), name)
        if "!TryResolveZombieModeSpawnPoint(position, false, out reachablePosition)" not in body or "position = reachablePosition;" not in body:
            return fail("all final spawn calls, including splits and bosses, must validate the submitted position -> " + name)

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
            return fail("ZombieModeSpawner must reuse the shared official cache instead of duplicating scene scans: " + token)

    profile_assignment = (
        "profile.StaticSpawnPoints = mapConfig.modeESpawnPoints != null && mapConfig.modeESpawnPoints.Length > 0\n"
        "                    ? mapConfig.modeESpawnPoints\n"
        "                    : (mapConfig.spawnPoints ?? new Vector3[0]);"
    )
    if profile_assignment not in runtime_module:
        return fail("MapProfile must keep modeE points first and BossRush spawnPoints fallback")

    print("ZombieModeBossRushSpawnPointsOnlyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
