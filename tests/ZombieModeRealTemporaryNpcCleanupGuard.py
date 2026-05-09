from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")


def fail(message: str) -> int:
    print("ZombieModeRealTemporaryNpcCleanupGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    drops = DROPS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    extraction = EXTRACTION.read_text(encoding="utf-8")

    for snippet in [
        "public sealed class ZombieModeTemporaryRealNpcRecord",
        "public readonly List<ZombieModeTemporaryRealNpcRecord> TemporaryRealNpcs = new List<ZombieModeTemporaryRealNpcRecord>();",
    ]:
        result = require(models, snippet, "real NPC run-state storage")
        if result:
            return result

    result = require(drops, "private void RecycleZombieModeTemporaryRealNpcs(int runId)", "real NPC cleanup helper")
    if result:
        return result
    result = require(drops, "private void RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(int runId)", "safe-zone real NPC cleanup helper")
    if result:
        return result
    result = require(waves, "RecycleZombieModeTemporaryRealNpcs(runId);", "wave cleanup wiring")
    if result:
        return result
    result = require(extraction, "RecycleZombieModeSafeZoneBoundTemporaryRealNpcs(runId);", "safe-zone cleanup wiring")
    if result:
        return result

    for snippet in [
        "AttachZombieModeTemporaryRealNpcMarker(",
        "zombieModeRunState.TemporaryRealNpcs.Add(record);",
    ]:
        result = require(rewards, snippet, "real NPC spawn tracking")
        if result:
            return result

    print("ZombieModeRealTemporaryNpcCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
