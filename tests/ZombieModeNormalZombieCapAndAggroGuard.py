"""ZombieModeNormalZombieCapAndAggroGuard: normal zombie pressure stays capped and player-focused."""

from pathlib import Path
import re
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")


def fail(message: str) -> int:
    print("ZombieModeNormalZombieCapAndAggroGuard: FAIL - " + message)
    return 1


def extract_method(text: str, method_name: str) -> str:
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


def require(text: str, needle: str, message: str) -> int:
    if needle not in text:
        return fail(message + " -> " + needle)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")

    for token in [
        "public const int MaxNormalZombieCount = Spawn.MaxNormalZombieCount;",
        "public int LivingNormalZombieCount;",
        "public int PendingNormalZombieSpawns;",
    ]:
        result = require(models, token, "run state/tuning must track capped normal zombie population")
        if result:
            return result

    periodic_count = extract_method(waves, "GetZombieModePeriodicSpawnCount")
    if not periodic_count:
        return fail("GetZombieModePeriodicSpawnCount not found")
    for token in [
        "GetZombieModeNormalZombieSpawnSlots()",
        "ZombieModeTuning.MaxNormalZombieCount",
    ]:
        result = require(periodic_count, token, "periodic spawns must be based on open 50-cap slots")
        if result:
            return result
    if "ZombieModeTuning.MaxPeriodicSpawnCount" in periodic_count:
        return fail("periodic spawn count must fill open slots toward the 50-zombie field cap, not trickle one per interval")
    if "Mathf.Max(1, effectiveSpawnPointCount)" in periodic_count:
        return fail("periodic spawn count must not scale from collected map spawn point count")

    tick = extract_method(waves, "TickZombieModeWaveController")
    if not tick:
        return fail("TickZombieModeWaveController not found")
    for token in [
        "TickZombieModeAmbientZombiePressure(zombieModeRunState.RunId, deltaTime);",
        "ZombieModePhaseGuards.AllowsBeacon(zombieModeRunState.CombatPhase)",
    ]:
        result = require(tick, token, "preparation phases must continue maintaining ambient zombies")
        if result:
            return result

    begin_prep = extract_method(waves, "BeginZombieModePreparation")
    if not begin_prep:
        return fail("BeginZombieModePreparation not found")
    for token in [
        "CleanupZombieModeEnemiesNearPlayerSafeZone(runId, \"BeginPreparation\");",
        "EnsureZombieModeAmbientZombiePopulation(runId);",
    ]:
        result = require(begin_prep, token, "preparation start must clear the immediate safe-zone radius and refill ambient zombies")
        if result:
            return result

    ambient_tick = extract_method(waves, "TickZombieModeAmbientZombiePressure")
    if not ambient_tick:
        return fail("TickZombieModeAmbientZombiePressure not found")
    for token in [
        "IsZombieModeAmbientZombieSpawnPhase(zombieModeRunState.CombatPhase)",
        "SpawnZombieModeWaveAcrossMapAsync(runId, spawnCount, false).Forget();",
    ]:
        result = require(ambient_tick, token, "ambient pressure must run in combat and preparation phases")
        if result:
            return result

    start_wave = extract_method(waves, "StartZombieModeWave")
    complete_wave = extract_method(waves, "CompleteZombieModeWave")
    for method_name, method in [("StartZombieModeWave", start_wave), ("CompleteZombieModeWave", complete_wave)]:
        if "CleanupZombieModeCombatEnemiesForWaveEnd" in method:
            return fail(method_name + " must not clear all live zombies across the map")

    result = require(
        complete_wave,
        "CleanupZombieModeEnemiesNearPlayerSafeZone(runId, \"CompleteWave\");",
        "wave completion must immediately clear only the player safe-zone radius")
    if result:
        return result

    cleanup = extract_method(waves + spawner + models + Path("ZombieMode/ZombieModeCleanup.cs").read_text(encoding="utf-8"), "CleanupZombieModeEnemiesNearPlayerSafeZone")
    if not cleanup:
        return fail("CleanupZombieModeEnemiesNearPlayerSafeZone not found")
    for token in [
        "CharacterMainControl.Main",
        "ZombieModeTuning.SafeZoneRadius",
        "delta.sqrMagnitude > radius * radius",
        "marker.IsBoss",
    ]:
        result = require(cleanup, token, "safe-zone cleanup must filter by player-centered safe-zone radius and avoid boss lifecycle damage")
        if result:
            return result

    reserve = extract_method(spawner, "TryReserveZombieModeNormalSpawnSlot")
    if not reserve:
        return fail("TryReserveZombieModeNormalSpawnSlot not found")
    for token in [
        "zombieModeRunState.LivingNormalZombieCount + zombieModeRunState.PendingNormalZombieSpawns",
        "ZombieModeTuning.MaxNormalZombieCount",
        "zombieModeRunState.PendingNormalZombieSpawns++;",
    ]:
        result = require(reserve, token, "normal zombie spawns must reserve cap slots before async creation")
        if result:
            return result

    release = extract_method(spawner, "ReleaseZombieModeNormalSpawnSlot")
    if not release:
        return fail("ReleaseZombieModeNormalSpawnSlot not found")
    result = require(release, "zombieModeRunState.PendingNormalZombieSpawns = Mathf.Max(0, zombieModeRunState.PendingNormalZombieSpawns - 1);", "failed async spawns must release reserved slots")
    if result:
        return result

    next_position = extract_method(waves, "GetNextZombieModeMapSpawnPosition")
    if not next_position:
        return fail("GetNextZombieModeMapSpawnPosition not found")
    for token in [
        "TryGetNearestZombieModeMapSpawnPositionToPlayer(out position)",
        "return position;",
    ]:
        result = require(next_position, token, "map pressure spawns must prefer nearest BossRush stored spawn point")
        if result:
            return result

    nearest = extract_method(spawner, "TryGetNearestZombieModeMapSpawnPositionToPlayer")
    if not nearest:
        return fail("TryGetNearestZombieModeMapSpawnPositionToPlayer not found")
    for token in [
        "CharacterMainControl.Main",
        "bestDistanceSqr",
        "delta.sqrMagnitude",
        "ZombieModeTuning.SpawnPointMinPlayerDistance",
    ]:
        result = require(nearest, token, "nearest spawn-point helper must choose by player distance with minimum-distance filtering")
        if result:
            return result

    spawn = extract_method(spawner, "TrySpawnZombieModeNormalZombieAsync")
    if not spawn:
        return fail("TrySpawnZombieModeNormalZombieAsync not found")
    for token in [
        "if (!TryReserveZombieModeNormalSpawnSlot(runId))",
        "ReleaseZombieModeNormalSpawnSlot();",
        "zombieModeRunState.LivingNormalZombieCount++;",
    ]:
        result = require(spawn, token, "normal zombie spawn path must enforce cap and maintain living count")
        if result:
            return result

    dead = extract_method(waves, "HandleZombieModeHealthDead")
    result = require(dead, "zombieModeRunState.LivingNormalZombieCount = Mathf.Max(0, zombieModeRunState.LivingNormalZombieCount - 1);", "normal zombie death must free a cap slot")
    if result:
        return result

    target = extract_method(rewards, "SetZombieModeEnemyTargetToMainPlayer")
    for token in [
        "ai.searchedEnemy = main.mainDamageReceiver;",
        "ai.SetTarget(main.mainDamageReceiver.transform);",
        "ai.SetNoticedToTarget(main.mainDamageReceiver);",
        "ai.noticed = true;",
    ]:
        result = require(target, token, "spawned zombies must be hard-targeted to the main player")
        if result:
            return result

    prepare = extract_method(spawner, "PrepareZombieModeSpawnedEnemy")
    result = require(prepare, "ai.forceTracePlayerDistance = Mathf.Max(ai.forceTracePlayerDistance, forceTraceDistance);", "spawned zombies must keep enough force-trace distance")
    if result:
        return result

    print("ZombieModeNormalZombieCapAndAggroGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
