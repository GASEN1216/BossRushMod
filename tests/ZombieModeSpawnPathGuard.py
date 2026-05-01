from pathlib import Path
import sys


SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
RUNTIME = Path("ZombieMode/ZombieModeEnemyRuntime.cs")

FORBIDDEN_SPAWNER_SNIPPETS = [
    "SpawnEnemyCore",
    "CreateEnemyAt",
    "SpawnBoss",
]


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    spawner_text = SPAWNER.read_text(encoding="utf-8")
    runtime_text = RUNTIME.read_text(encoding="utf-8")

    if "TrySpawnZombieModeNormalZombieAsync" not in spawner_text:
        return fail("ZombieModeSpawnPathGuard: missing dedicated normal zombie spawn shell")

    if "IsZombieModeRunValid(runId)" not in spawner_text:
        return fail("ZombieModeSpawnPathGuard: spawn shell does not validate RunId")

    for snippet in FORBIDDEN_SPAWNER_SNIPPETS:
        if snippet in spawner_text:
            return fail("ZombieModeSpawnPathGuard: Phase 0 spawner contains forbidden snippet -> " + snippet)

    for snippet in [
        "ZombieModeEnemyRuntimeMarker",
        "SuppressDrops",
        "isBoss ? ZombieModeRunOnlyObjectKind.Boss : ZombieModeRunOnlyObjectKind.Enemy",
    ]:
        if snippet not in runtime_text:
            return fail("ZombieModeSpawnPathGuard: enemy runtime missing snippet -> " + snippet)

    for snippet in [
        "using UnityEngine.AI;",
        "private bool TryResolveZombieModeSpawnPoint(Vector3 position, bool virtualPoint, out Vector3 resolved)",
        "NavMesh.SamplePosition(position, out navHit, sampleRadius, NavMesh.AllAreas)",
        "ZombieModeTuning.SpawnPointNavMeshSampleRadius",
        "ZombieModeTuning.SpawnPointDuplicateDistance",
        "ZombieModeTuning.SpawnPointMinPlayerDistance",
        "if (virtualPoint)",
        "return false;",
        "TrySnapZombieModeSpawnPointToGround(position, out resolved)",
        "private bool TryFindZombieModeVirtualSpawnAroundPlayer(Vector3 playerPos, out Vector3 resolved)",
    ]:
        if snippet not in spawner_text:
            return fail("ZombieModeSpawnPathGuard: spawn-point validation missing snippet -> " + snippet)

    print("ZombieModeSpawnPathGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
