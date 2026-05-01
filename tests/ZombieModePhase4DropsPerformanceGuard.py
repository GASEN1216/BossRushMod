from pathlib import Path
import sys


DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
MODELS = Path("ZombieMode/ZombieModeModels.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModePhase4DropsPerformanceGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    if not DROPS.exists():
        return fail("ZombieModePhase4DropsPerformanceGuard: missing ZombieModeDropsAndPerformance.cs")

    drops = DROPS.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    models = MODELS.read_text(encoding="utf-8")
    compile_text = COMPILE.read_text(encoding="utf-8")

    for snippet in [
        "public bool HighValue;",
        "public bool BossDrop;",
        "public float LastPerformanceEvalTime;",
        "public bool ContainersRefilled;",
    ]:
        result = require(models, snippet, "state model")
        if result:
            return result

    for snippet in [
        "InitializeZombieModeContainersShell(runId)",
        "TickZombieModeDropsAndPerformance(deltaTime)",
        "UnlockZombieModeContainersForActiveRun(runId)",
    ]:
        result = require(entry, snippet, "entry/tick integration")
        if result:
            return result

    for snippet in [
        "TrySpawnZombieModeEnemyDrop(runId, marker, character.transform.position)",
        "TrySpawnZombieModeBossDrop(runId, marker, character.transform.position)",
        "RecycleZombieModeTemporaryNpcs(runId)",
    ]:
        result = require(waves, snippet, "wave integration")
        if result:
            return result

    for snippet in [
        "private bool InitializeZombieModeContainersShell(int runId)",
        "Object.FindObjectsOfType<InteractableLootbox>(true)",
        "TryCreateZombieModeLootboxLocalInventory",
        "ClearZombieModeLootboxInventory",
        "RefillZombieModeLootboxInventory",
        "LockZombieModeContainerUntilStarterChoice",
        "ZombieModeContainerLock",
        "private void UnlockZombieModeContainersForActiveRun(int runId)",
        "private void TrySpawnZombieModeEnemyDrop(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)",
        "GetZombieModeEnemyDropChance",
        "TryDropZombieModeItemNearPosition",
        "RegisterZombieModeDropCandidate",
        "private void TrySpawnZombieModeBossDrop(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)",
        "TrySpawnZombieModeBossLootbox",
        "private void TickZombieModeDropsAndPerformance(float deltaTime)",
        "EvaluateZombieModePerformanceTier",
        "RecycleZombieModeFarEnemiesForPerformance",
        "RecycleZombieModeEnemyForPerformance",
        "CleanupZombieModeExpiredDropCandidates",
        "ZombieModeTuning.PerfTierExtreme",
    ]:
        result = require(drops, snippet, "drops/performance implementation")
        if result:
            return result

    init_start = drops.find("private bool InitializeZombieModeContainersShell(int runId)")
    refill_scan = drops.find("Object.FindObjectsOfType<InteractableLootbox>(true)", init_start)
    if init_start < 0 or refill_scan < 0:
        return fail("ZombieModePhase4DropsPerformanceGuard: container initialization is still a shell")

    if "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.MapIsolation, disabledObject," in Path("ZombieMode/ZombieModeMapIsolation.cs").read_text(encoding="utf-8"):
        return fail("ZombieModePhase4DropsPerformanceGuard: map isolation cleanup would destroy original map objects")

    if "ZombieMode\\ZombieModeDropsAndPerformance.cs" not in compile_text:
        return fail("ZombieModePhase4DropsPerformanceGuard: missing compile entry")

    print("ZombieModePhase4DropsPerformanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
