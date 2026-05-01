from pathlib import Path
import sys


ZOMBIE_FILES = list(Path("ZombieMode").glob("*.cs"))
DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
BOSS = Path("ZombieMode/ZombieModeBossController.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    combined = "\n".join(path.read_text(encoding="utf-8") for path in ZOMBIE_FILES)
    drops = DROPS.read_text(encoding="utf-8")
    boss = BOSS.read_text(encoding="utf-8")

    if "FindObjectsOfType<ZombieModeEnemyRuntimeMarker>" in combined:
        return fail("ZombieModePerformanceRegistryGuard: Zombie runtime marker full-scene scan still exists")

    for token in [
        "private readonly List<ZombieModeEnemyRuntimeMarker> zombieModeEnemyMarkerScratch",
        "private int CollectZombieModeRuntimeEnemyMarkers(",
        "zombieModeRunState.RunOnlyObjects",
        "ZombieModeRunOnlyObjectKind.Enemy",
        "ZombieModeRunOnlyObjectKind.Boss",
        "record.Target as ZombieModeEnemyRuntimeMarker",
        "record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>()",
    ]:
        if token not in drops:
            return fail("ZombieModePerformanceRegistryGuard: missing registry collection token -> " + token)

    if "CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, false)" not in drops:
        return fail("ZombieModePerformanceRegistryGuard: performance recycle does not use run-only registry")

    if "CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, true)" not in boss:
        return fail("ZombieModePerformanceRegistryGuard: boss shield does not use shared runtime marker collection")

    print("ZombieModePerformanceRegistryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
