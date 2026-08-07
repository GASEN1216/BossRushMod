"""ZombieModeNormalZombieCapAndAggroGuard: triple pressure stays bounded, reachable, and player-focused."""

from pathlib import Path
import re
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
RECOVERY = Path("Utilities/EnemyRecoveryMonitor.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)



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
    models = MODELS.read_text(encoding="utf-8") + "\n" + TUNING.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    recovery = RECOVERY.read_text(encoding="utf-8")
    rewards = read_rewards()

    for token in [
        "public const int MaxNormalZombieCount = Spawn.MaxNormalZombieCount;",
        "public const int MaxNormalZombieCount = 150;",
        "public const int NormalWavePressureBase = 24;",
        "public const int NormalWaveKillTargetBase = 18;",
        "public const int NormalWavePressurePerRemainingKill = 3;",
        "public const int PreparationPressureMinimum = 12;",
        "public const int PreparationPressureMaximum = 48;",
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
        "GetZombieModeAmbientPressureTarget()",
        "GetZombieModeSpawnBatchSize()",
        "desiredSlots",
        "ZombieModeTuning.MaxNormalZombieCount",
    ]:
        result = require(periodic_count, token, "periodic spawns must approach the tidal pressure target in bounded batches")
        if result:
            return result
    if "Mathf.Max(1, effectiveSpawnPointCount)" in periodic_count:
        return fail("periodic spawn count must not scale from collected map spawn point count")

    pressure_target = extract_method(waves, "GetZombieModeAmbientPressureTarget")
    if not pressure_target:
        return fail("GetZombieModeAmbientPressureTarget not found")
    for token in [
        "CurrentWaveKillTarget - zombieModeRunState.CurrentWaveKills",
        "GetZombieModePreparationPressureTarget(zombieModeRunState.CurrentWave + 1)",
        "remainingToKill * ZombieModeTuning.NormalWavePressurePerRemainingKill",
        "target = Mathf.Min(target, ebbTarget);",
        "return GetZombieModePreparationPressureTarget(pacingWave);",
    ]:
        result = require(pressure_target, token, "combat pressure must ebb near wave completion and delegate preparation pressure")
        if result:
            return result

    preparation_target = extract_method(waves, "GetZombieModePreparationPressureTarget")
    if not preparation_target:
        return fail("GetZombieModePreparationPressureTarget not found")
    for token in [
        "GetZombieModeWavePressureTarget(wave)",
        "ZombieModeTuning.PreparationPressureFraction",
        "ZombieModeTuning.PreparationPressureMinimum",
        "ZombieModeTuning.PreparationPressureMaximum",
    ]:
        result = require(preparation_target, token, "preparation pressure must remain a bounded fraction of the next wave")
        if result:
            return result

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
        "ReconcileZombieModeLivingEnemyCounts(runId);",
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

    reconcile = extract_method(waves, "ReconcileZombieModeLivingEnemyCounts")
    if not reconcile:
        return fail("ReconcileZombieModeLivingEnemyCounts not found")
    for token in [
        "CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, true)",
        "!marker.IsBoss",
        "zombieModeRunState.LivingZombieCount = livingTotal;",
        "zombieModeRunState.LivingNormalZombieCount = livingNormal;",
        "zombieModeEnemyMarkerScratch.Clear();",
    ]:
        result = require(reconcile, token, "periodic pressure must repair stale living counters without allocations")
        if result:
            return result

    next_position = extract_method(waves, "TryGetNextZombieModeMapSpawnPosition")
    if not next_position:
        return fail("TryGetNextZombieModeMapSpawnPosition not found")
    result = require(
        next_position,
        "return TryGetZombieModeReliableSpawnPosition(out position);",
        "map pressure spawns must reuse the reliable shared selector")
    if result:
        return result

    reliable = extract_method(spawner, "TryGetZombieModeReliableSpawnPosition")
    if not reliable:
        return fail("TryGetZombieModeReliableSpawnPosition not found")
    for token in [
        "TryFindZombieModeVirtualSpawnAroundPlayer(main.transform.position, out position)",
        "TryGetNearestZombieModeMapSpawnPositionToPlayer(out position)",
        "ZombieModeTuning.SpawnPointMinPlayerDistance",
        "position = Vector3.zero;",
    ]:
        result = require(reliable, token, "reliable selector must prefer nearby NavMesh points and retain a 12m fallback")
        if result:
            return result

    nearest = extract_method(spawner, "TryGetNearestZombieModeMapSpawnPositionToPlayer")
    if not nearest:
        return fail("TryGetNearestZombieModeMapSpawnPositionToPlayer not found")
    for token in [
        "CharacterMainControl.Main",
        "preferredMinDistanceSqr",
        "fallbackMinDistanceSqr",
        "bestPreferredIndex",
        "bestFallbackIndex",
        "delta.sqrMagnitude",
        "GetZombieModeSpawnPointMinPlayerDistance()",
        "ZombieModeTuning.SpawnPointMinPlayerDistance",
        "int bestIndex = bestPreferredIndex >= 0 ? bestPreferredIndex : bestFallbackIndex;",
        "if (bestIndex < 0)",
        "return false;",
    ]:
        result = require(nearest, token, "nearest helper must select preferred and 12m-safe candidates in one scan")
        if result:
            return result

    for token in [
        "GetZombieModeSpawnPointMinPlayerDistance()",
        "Mathf.Max(18f, minPlayerDistance + 6f)",
        "minPlayerDistance: minPlayerDistance",
        "startIndex: startIndex",
    ]:
        result = require(spawner, token, "virtual spawn ring must expand with the dynamic player-safe distance")
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

    distant_recovery = extract_method(recovery, "ShouldRecoverDistantZombie")
    recover = extract_method(recovery, "TryRecoverEnemyToNearestSpawnPoint")
    if not distant_recovery or not recover:
        return fail("distant normal-zombie recovery path not found")
    for token in [
        "zombieMarker == null || zombieMarker.IsBoss",
        "ZombieModeTuning.NormalZombieDistantRecoveryDistance",
        "ZombieModeTuning.NormalZombieDistantRecoveryDelaySeconds",
        "GetHorizontalSqrDistance(currentPos, player.transform.position)",
    ]:
        result = require(distant_recovery, token, "only long-distance non-Boss zombies should be recovered")
        if result:
            return result
    for token in [
        "reason == ZombieModeDistantRecoveryReason",
        "TryGetZombieModeReliableSpawnPosition(out targetPos)",
        "TryGetNearestAlternateSpawnPoint(currentPos, state, player, out targetPos)",
    ]:
        result = require(recover, token, "distant recovery must reuse the player-near selector and retain generic fallback")
        if result:
            return result

    print("ZombieModeNormalZombieCapAndAggroGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
