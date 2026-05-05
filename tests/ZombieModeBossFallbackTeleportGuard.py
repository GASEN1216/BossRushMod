"""ZombieModeBossFallbackTeleportGuard: boss stuck recovery must reuse shared valid-position helpers."""

from pathlib import Path
import sys


BOSS = Path("ZombieMode/ZombieModeBossController.cs")


def fail(message: str) -> int:
    print("ZombieModeBossFallbackTeleportGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = BOSS.read_text(encoding="utf-8")

    required_tokens = [
        "private bool TryResolveZombieModeBossFallbackPosition(",
        "SpawnPositionHelper.TryFindAroundPlayer(",
        "TryGetNearestZombieModeMapSpawnPositionToPlayer(out target)",
        "SpawnPositionHelper.TrySampleNavMesh(",
        "if (!TryResolveZombieModeBossFallbackPosition(instance, out target))",
        "SetZombieModeEnemyTargetToMainPlayer(ai);",
    ]
    for token in required_tokens:
        if token not in text:
            return fail("missing shared fallback token -> " + token)

    forbidden_tokens = [
        "instance.Character.transform.position = target;",
        "center + offset.normalized * 16f + Vector3.up * ZombieModeTuning.NavMeshLiftOffset",
        "offset = Random.insideUnitSphere;",
    ]
    for token in forbidden_tokens:
        if token in text:
            return fail("stale direct-position fallback remains -> " + token)

    print("ZombieModeBossFallbackTeleportGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
