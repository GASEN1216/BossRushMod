"""Guard: zombie enemy recovery should reuse marker AI cache without changing other modes."""

from pathlib import Path
import sys


SOURCE = Path("Utilities/EnemyRecoveryMonitor.cs")
WAVES = Path("WavesArena/WavesArenaBossSpawning.cs")


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
    text = SOURCE.read_text(encoding="utf-8-sig")
    monitor_zombie = extract_method_body(text, "private void MonitorZombieModeEnemyRecovery(")
    monitor = extract_method_body(text, "private void MonitorEnemyRecovery(")
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
        "private void MonitorEnemyRecovery(CharacterMainControl enemy, CharacterMainControl player, ZombieModeEnemyRuntimeMarker zombieMarker = null)",
        "private bool TryRecoverEnemyToNearestSpawnPoint(",
        "ZombieModeEnemyRuntimeMarker zombieMarker,",
        "private void RestoreRecoveredEnemyAggro(CharacterMainControl enemy, CharacterMainControl player, ZombieModeEnemyRuntimeMarker zombieMarker)",
    ]
    for snippet in required_text:
        if snippet not in text:
            return fail("missing recovery marker signature snippet -> " + snippet)

    required_monitor_zombie = "MonitorEnemyRecovery(enemy, player, marker);"
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

    waves_text = WAVES.read_text(encoding="utf-8-sig")
    if "TryRecoverEnemyToNearestSpawnPoint(boss, state, main, reason, null, out recoveredPos)" not in waves_text:
        return fail("WavesArena boss recovery should pass null zombie marker")

    print("ZombieModeEnemyRecoveryAICacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
