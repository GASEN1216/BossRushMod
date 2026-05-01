from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
SAFE_ZONE = Path("ZombieMode/ZombieModeSafeZoneController.cs")
EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
DEBUG_TOOLS = Path("DebugAndTools/DebugAndTools.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeSafeZoneGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    safe_zone = SAFE_ZONE.read_text(encoding="utf-8")
    extraction = EXTRACTION.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    spawner = SPAWNER.read_text(encoding="utf-8")
    debug_tools = DEBUG_TOOLS.read_text(encoding="utf-8")

    for snippet in [
        "public float LastSafeZoneTickTime;",
        "public bool SafeZoneThreatSuppressed;",
        "TickIntervalSeconds = 0.2f",
    ]:
        result = require(models, snippet, "safe zone state model")
        if result:
            return result

    for snippet in [
        "TickZombieModeSafeZone",
        "Time.unscaledTime - zombieModeRunState.LastSafeZoneTickTime",
        "ZombieModeTuning.SafeZoneTickIntervalSeconds",
        "UpdateZombieModeSafeZonePlayerPresence",
        "SuppressZombieModeSafeZoneThreats",
        "ReleaseZombieModeSafeZoneThreatSuppression",
        "SetZombieModeEnemyThreatSuppressed",
        "zombieModeRunState.PlayerInsideSafeZone = inside",
        "zombieModeRunState.SafeZoneThreatSuppressed = shouldSuppress",
        "AICharacterController",
        "ai.searchedEnemy = null",
        "ai.noticed = false",
        "TryRegisterZombieModeShootStealthBreaker",
        "ItemAgent_Gun.OnMainCharacterShootEvent += OnZombieModeMainCharacterShoot",
        "ItemAgent_Gun.OnMainCharacterShootEvent -= OnZombieModeMainCharacterShoot",
        "OnZombieModeMainCharacterShoot",
        "BreakZombieModeSafeZoneStealth(zombieModeRunState.RunId)",
    ]:
        result = require(safe_zone, snippet, "safe zone tick and stealth breaker")
        if result:
            return result

    for snippet in [
        "TickZombieModeSafeZone();",
        "zombieModeRunState.LastSafeZoneTickTime = 0f;",
        "zombieModeRunState.SafeZoneThreatSuppressed = false;",
    ]:
        result = require(extraction, snippet, "safe zone lifecycle")
        if result:
            return result

    for snippet in [
        "TickZombieModeSafeZone();",
    ]:
        result = require(waves, snippet, "wave controller safe zone tick")
        if result:
            return result

    for snippet in [
        "if (ShouldSuppressZombieModeEnemyAggroForSafeZone())",
        "ai.searchedEnemy = null;",
        "ai.noticed = false;",
    ]:
        result = require(spawner, snippet, "spawn aggro suppression")
        if result:
            return result

    if "if (!DevModeEnabled) return;" in safe_zone:
        return fail("ZombieModeSafeZoneGuard: zombie stealth shoot breaker must not depend on DevMode")

    if "ItemAgent_Gun.OnMainCharacterShootEvent += OnZombieModeMainCharacterShoot" in debug_tools:
        return fail("ZombieModeSafeZoneGuard: zombie stealth breaker must live in ZombieMode, not DevMode debug")

    print("ZombieModeSafeZoneGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
