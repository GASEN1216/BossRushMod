"""ZombieModeBossFallbackTeleportGuard: boss stuck recovery must reuse shared valid-position helpers."""

from pathlib import Path
import sys
from cs_source_util import clean_source


BOSS = Path("ZombieMode/ZombieModeRuntimeModule_BossController.cs")
HOST = Path("ZombieMode/ZombieModeCombatHostBridge.cs")


def fail(message: str) -> int:
    print("ZombieModeBossFallbackTeleportGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = clean_source(BOSS.read_text(encoding="utf-8"))

    required_tokens = [
        "private bool TryResolveZombieModeBossFallbackPosition(",
        "return TryGetZombieModeReliableSpawnPosition(out target);",
        "if (!TryResolveZombieModeBossFallbackPosition(instance, out target))",
        "SetZombieModeEnemyTargetToMainPlayer(ai);",
        "GetZombieModeEnemyAI(boss.gameObject, marker)",
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

    host = HOST.read_text(encoding="utf-8")
    for token in [
        "private void TickZombieModeBossController(float deltaTime)",
        "module.TickZombieModeBossController(deltaTime);",
        "SetZombieModeEnemyTargetToMainPlayer(ai);",
    ]:
        if token not in host:
            return fail("host compatibility bridge missing -> " + token)

    print("ZombieModeBossFallbackTeleportGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
