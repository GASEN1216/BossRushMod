"""Guard ZombieMode SpawnEnemyCore calls from ModeD-only damage normalization."""

from pathlib import Path
import sys


SPAWN_CORE = Path("Utilities/EnemySpawnCore.cs")
ZOMBIE_SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")


def fail(message: str) -> int:
    print("ZombieModeSpawnCoreModeDIsolationGuard: FAIL - " + message)
    return 1


def main() -> int:
    core = SPAWN_CORE.read_text(encoding="utf-8")
    zombie = ZOMBIE_SPAWNER.read_text(encoding="utf-8")

    if "bool normalizeDamageMultiplier = true" not in core:
        return fail("SpawnEnemyCore must expose normalizeDamageMultiplier option")

    if "if (normalizeDamageMultiplier)" not in core:
        return fail("NormalizeDamageMultiplier calls must be gated by normalizeDamageMultiplier")

    if "normalizeDamageMultiplier: false" not in zombie:
        return fail("ZombieMode SpawnEnemyCore calls must disable ModeD damage normalization")

    normal_call = zombie.find("private async UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync")
    if normal_call < 0:
        normal_call = zombie.find("private UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync")
    boss_call = zombie.find("private async UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync")
    if boss_call < 0:
        boss_call = zombie.find("private UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync")
    if normal_call < 0 or boss_call < 0:
        return fail("ZombieMode spawn methods not found")

    if zombie.find("normalizeDamageMultiplier: false", normal_call, boss_call) < 0:
        return fail("normal zombie spawn must disable ModeD damage normalization")

    if zombie.find("normalizeDamageMultiplier: false", boss_call) < 0:
        return fail("zombie boss spawn must disable ModeD damage normalization")

    print("ZombieModeSpawnCoreModeDIsolationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
