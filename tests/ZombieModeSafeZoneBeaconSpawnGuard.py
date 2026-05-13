"""ZombieModeSafeZoneBeaconSpawnGuard: guard map safe zone POI, reusable beacon, and bounded wave spawns."""

from pathlib import Path

MODELS = Path("ZombieMode/ZombieModeModels.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")
EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")


def fail(message):
    print("ZombieModeSafeZoneBeaconSpawnGuard: FAIL - " + message)
    raise SystemExit(1)


def require(text, needle, message):
    if needle not in text:
        fail(message)


def forbid(text, needle, message):
    if needle in text:
        fail(message)


def main():
    models = MODELS.read_text(encoding="utf-8") + "\n" + TUNING.read_text(encoding="utf-8")
    extraction = EXTRACTION.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")

    require(
        extraction,
        "using Duckov.MiniMaps;",
        "safe zone controller must use vanilla minimap POI types",
    )
    require(
        extraction,
        "ActiveSafeZoneMapPoi",
        "run state safe zone map POI must be created and cleaned with the safe zone",
    )
    require(
        extraction,
        "SimplePointOfInterest.Create",
        "safe zone must register a vanilla SimplePointOfInterest so the map renders an area circle",
    )
    require(
        extraction,
        "poi.IsArea = true;",
        "safe zone POI must render as an area on the map",
    )
    require(
        extraction,
        "poi.AreaRadius = ZombieModeTuning.SafeZoneRadius;",
        "safe zone POI radius must match the gameplay safe zone radius",
    )
    require(
        extraction,
        "poi.Setup(null, displayName, false, sceneId);",
        "safe zone POI must re-register after area fields are configured so an open map refreshes",
    )

    require(
        models,
        "public SimplePointOfInterest ActiveSafeZoneMapPoi;",
        "run state must track the active safe zone map POI",
    )
    require(
        models,
        "public const int MaxNormalZombieCount = Spawn.MaxNormalZombieCount;",
        "tuning must expose the bounded normal-zombie field cap",
    )

    forbid(
        waves,
        "SpawnZombieModeWaveAsync(runId, initialSpawnCount",
        "wave start must not burst-spawn an initial wave",
    )
    forbid(
        waves,
        "GetZombieModeInitialWaveSpawnCount(effectiveSpawnPointCount)",
        "wave start must not compute burst initial spawn count from all map spawn points",
    )
    require(
        waves,
        "zombieModeRunState.NextSpawnPointIndex = 0;",
        "wave start must reset the map-point round-robin index",
    )
    require(
        waves,
        "SpawnZombieModeWaveAcrossMapAsync(runId, spawnCount, false).Forget();",
        "combat pressure must spawn through the map-wide round-robin helper",
    )
    require(
        waves,
        "GetZombieModePeriodicSpawnCount()",
        "periodic pressure spawn must use a separate capped normal-zombie count",
    )
    require(
        waves,
        "GetZombieModeNormalZombieSpawnSlots()",
        "periodic pressure spawn must check open normal-zombie cap slots",
    )
    forbid(
        waves,
        "CurrentWaveKillTarget = Mathf.Max(1, effectiveSpawnPointCount +",
        "kill target must not scale directly with every collected map spawn point",
    )

    require(
        entry,
        "GrantZombieModeBeacon(int runId)",
        "beacon grant path must remain in the ZombieMode entry flow",
    )
    forbid(
        entry,
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Beacon",
        "beacon must not be registered as a run-only cleanup item so it can be reused",
    )
    forbid(
        entry,
        "CleanupZombieModeBeaconItem(beacon)",
        "granted beacon must not be destroyed by ZombieMode cleanup",
    )

    print("ZombieModeSafeZoneBeaconSpawnGuard: PASS")


if __name__ == "__main__":
    main()
