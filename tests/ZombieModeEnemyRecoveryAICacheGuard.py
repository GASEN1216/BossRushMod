"""Guard: zombie enemy recovery should reuse marker AI cache without changing other modes."""

from pathlib import Path
import sys
from cs_source_util import clean_source


SOURCE = Path("Utilities/EnemyRecoveryMonitor.cs")
ZOMBIE = Path("ZombieMode/ZombieModeRuntimeModule_Recovery.cs")
HOST = Path("Utilities/EnemyRecoveryHostBridge.cs")


def fail(message: str) -> int:
    print("ZombieModeEnemyRecoveryAICacheGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = clean_source(SOURCE.read_text(encoding="utf-8-sig"))
    zombie = clean_source(ZOMBIE.read_text(encoding="utf-8-sig"))
    host = clean_source(HOST.read_text(encoding="utf-8-sig"))
    monitor_zombie = extract_method_body(zombie, "internal void MonitorZombieModeEnemyRecovery(")
    monitor = extract_method_body(text, "internal void MonitorEnemyRecovery(")
    recover = extract_method_body(text, "private bool TryRecoverEnemyToNearestSpawnPoint(")
    restore = extract_method_body(text, "private void RestoreRecoveredEnemyAggro(")
    if monitor_zombie is None:
        return fail("missing MonitorZombieModeEnemyRecovery body")
    if monitor is None:
        return fail("missing MonitorEnemyRecovery body")
    if recover is None:
        return fail("missing TryRecoverEnemyToNearestSpawnPoint body")
    if restore is None:
        return fail("missing RestoreRecoveredEnemyAggro body")

    required_text = [
        "internal void MonitorEnemyRecovery(CharacterMainControl enemy, CharacterMainControl player, Component zombieMarker = null)",
        "private bool TryRecoverEnemyToNearestSpawnPoint(",
        "Component zombieMarker,",
        "private void RestoreRecoveredEnemyAggro(CharacterMainControl enemy, CharacterMainControl player, Component zombieMarker)",
    ]
    for snippet in required_text:
        if snippet not in text:
            return fail("missing recovery marker signature snippet -> " + snippet)

    required_monitor_zombie = "enemyRecoveryMonitor.MonitorEnemyRecovery(enemy, player, marker);"
    if required_monitor_zombie not in monitor_zombie:
        return fail("zombie recovery monitor should pass marker into recovery")

    required_monitor = "TryRecoverEnemyToNearestSpawnPoint(enemy, state, player, reason, zombieMarker, out recoveredPos)"
    if required_monitor not in monitor:
        return fail("recovery monitor should pass marker to recovery action")

    required_recover = "RestoreRecoveredEnemyAggro(enemy, player, zombieMarker);"
    if required_recover not in recover:
        return fail("recovery action should pass marker to aggro restore")

    required_restore = [
        "AICharacterController ai = null;",
        "if (zombieMarker != null && zombieMarker.gameObject == enemy.gameObject)",
        "ai = GetZombieModeEnemyAI(enemy.gameObject, zombieMarker);",
        "if (ai == null)",
        "ai = enemy.GetComponentInChildren<AICharacterController>();",
    ]
    for snippet in required_restore:
        if snippet not in restore:
            return fail("aggro restore missing cache/fallback snippet -> " + snippet)

    if "MonitorEnemyRecovery(enemies[i], player, marker)" in text:
        return fail("non-zombie recovery list should not pass zombie marker")

    validation = extract_method_body(text, "internal void ValidateAndFixBossPosition(")
    if validation is None or "TryRecoverEnemyToNearestSpawnPoint(boss, state, main, reason, null, out recoveredPos)" not in validation:
        return fail("WavesArena boss recovery should pass null zombie marker")
    if "ZombieModeRuntimeModule.GetZombieModeEnemyAI(go, (ZombieModeEnemyRuntimeMarker)marker)" not in host:
        return fail("marker AI resolver must bind the existing zombie cache")

    print("ZombieModeEnemyRecoveryAICacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
