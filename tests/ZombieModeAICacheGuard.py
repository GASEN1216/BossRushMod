"""Guard: zombie AI controller lookups should reuse marker cache on hot paths."""

from pathlib import Path
import sys


MARKER = Path("ZombieMode/ZombieModeEnemyRuntime.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
SAFE_ZONE = Path("ZombieMode/ZombieModeSafeZoneController.cs")
GRAVITY = Path("ZombieMode/ZombieModeRewardProjectileSpread.cs")
TEMP_NPC = Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs")
BOSS = Path("ZombieMode/ZombieModeBossController.cs")


def fail(message: str) -> int:
    print("ZombieModeAICacheGuard: FAIL - " + message)
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


def require_body_uses_helper(path: Path, signature: str, snippet: str) -> int:
    text = path.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, signature)
    if body is None:
        return fail("missing body for " + signature)

    if snippet not in body:
        return fail(signature + " missing cached AI helper call -> " + snippet)

    if "GetComponentInChildren<AICharacterController>" in body:
        return fail(signature + " still performs direct AI hierarchy lookup")

    return 0


def main() -> int:
    marker_text = MARKER.read_text(encoding="utf-8-sig")
    helper = extract_method_body(
        marker_text,
        "private static AICharacterController GetZombieModeEnemyAI(",
    )
    if helper is None:
        return fail("missing GetZombieModeEnemyAI helper")

    marker_required = [
        "public AICharacterController CachedAI;",
        "marker.CachedAI = null;",
    ]
    for snippet in marker_required:
        if snippet not in marker_text:
            return fail("missing marker AI cache snippet -> " + snippet)

    helper_required = [
        "AICharacterController ai = marker != null ? marker.CachedAI : null;",
        "ai.gameObject.activeInHierarchy",
        "ai.transform.IsChildOf(enemyObject.transform)",
        "ai = enemyObject.GetComponentInChildren<AICharacterController>();",
        "marker.CachedAI = ai;",
    ]
    for snippet in helper_required:
        if snippet not in helper:
            return fail("missing AI helper validation/cache snippet -> " + snippet)

    checks = [
        (SPAWNER, "private void PrepareZombieModeSpawnedEnemy(", "GetZombieModeEnemyAI(enemy.gameObject, marker);"),
        (SPAWNER, "private void ApplyZombieModeBossTuning(", "GetZombieModeEnemyAI(boss.gameObject, marker);"),
        (SAFE_ZONE, "private void SetZombieModeEnemyThreatSuppressed(", "GetZombieModeEnemyAI(enemyObject, marker);"),
        (GRAVITY, "internal void RefreshZombieModeGravityWellTargets(", "GetZombieModeEnemyAI(enemy.gameObject, marker);"),
        (TEMP_NPC, "private void ClearZombieModeTemporaryNpcThreatTargets()", "GetZombieModeEnemyAI(record.GameObject, marker);"),
        (BOSS, "private void TeleportZombieModeBossNearPlayer(", "GetZombieModeEnemyAI(boss.gameObject, marker);"),
    ]
    for path, signature, snippet in checks:
        result = require_body_uses_helper(path, signature, snippet)
        if result:
            return result

    boss_text = BOSS.read_text(encoding="utf-8-sig")
    teleport = extract_method_body(boss_text, "private void TeleportZombieModeBossNearPlayer(")
    if teleport is None:
        return fail("missing TeleportZombieModeBossNearPlayer body")
    if "ZombieModeEnemyRuntimeMarker marker = EnsureZombieModeBossMarker(instance);" not in teleport:
        return fail("boss teleport should resolve/cache marker before AI helper")

    safe_zone_text = SAFE_ZONE.read_text(encoding="utf-8-sig")
    suppress_helper = extract_method_body(safe_zone_text, "private void SetZombieModeEnemyThreatSuppressed(")
    if suppress_helper is None:
        return fail("missing SetZombieModeEnemyThreatSuppressed body")
    marker_recovery = "marker = enemyObject.GetComponent<ZombieModeEnemyRuntimeMarker>();"
    ai_lookup = "AICharacterController ai = GetZombieModeEnemyAI(enemyObject, marker);"
    marker_index = suppress_helper.find(marker_recovery)
    ai_index = suppress_helper.find(ai_lookup)
    if marker_index < 0 or ai_index < 0 or marker_index > ai_index:
        return fail("safe-zone threat suppression should recover marker before cached AI lookup")

    print("ZombieModeAICacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
