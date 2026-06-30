"""Guard: zombie safe-zone suppression should reuse run-only enemy markers."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModeSafeZoneController.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")


def fail(message: str) -> int:
    print("ZombieModeSafeZoneMarkerReuseGuard: FAIL - " + message)
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
    spawner_text = SPAWNER.read_text(encoding="utf-8-sig")
    keep = extract_method_body(text, "private void KeepZombieModeEnemiesOutsideSafeZone()")
    suppress = extract_method_body(text, "private void SuppressZombieModeSafeZoneThreats()")
    release = extract_method_body(text, "private void ReleaseZombieModeSafeZoneThreatSuppression()")
    setter = extract_method_body(text, "private void SetZombieModeEnemyThreatSuppressed(")
    prepare = extract_method_body(spawner_text, "private void PrepareZombieModeSpawnedEnemy(")

    if keep is None:
        return fail("missing KeepZombieModeEnemiesOutsideSafeZone body")
    if suppress is None:
        return fail("missing SuppressZombieModeSafeZoneThreats body")
    if release is None:
        return fail("missing ReleaseZombieModeSafeZoneThreatSuppression body")
    if setter is None:
        return fail("missing SetZombieModeEnemyThreatSuppressed body")
    if prepare is None:
        return fail("missing PrepareZombieModeSpawnedEnemy body")

    signature = "private void SetZombieModeEnemyThreatSuppressed(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker, bool suppressed)"
    if signature not in text:
        return fail("threat suppression helper should accept cached marker")

    prepare_signature = "private void PrepareZombieModeSpawnedEnemy(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker, float forceTraceDistance)"
    if prepare_signature not in spawner_text:
        return fail("spawn preparation should accept cached marker")

    if "enemyObject.GetComponent<ZombieModeEnemyRuntimeMarker>()" in setter:
        expected_fallback = [
            "if (marker == null || marker.gameObject != enemyObject)",
            "marker = enemyObject.GetComponent<ZombieModeEnemyRuntimeMarker>();",
        ]
        for snippet in expected_fallback:
            if snippet not in setter:
                return fail("marker lookup should only be the validated fallback -> " + snippet)

    required_keep = "SetZombieModeEnemyThreatSuppressed(record.GameObject, marker, true);"
    if required_keep not in keep:
        return fail("safe-zone displacement path should pass the already resolved marker")

    for method_name, body, state in (
        ("SuppressZombieModeSafeZoneThreats", suppress, "true"),
        ("ReleaseZombieModeSafeZoneThreatSuppression", release, "false"),
    ):
        required = [
            "ZombieModeEnemyRuntimeMarker marker = record.Target as ZombieModeEnemyRuntimeMarker;",
            f"SetZombieModeEnemyThreatSuppressed(record.GameObject, marker, {state});",
        ]
        for snippet in required:
            if snippet not in body:
                return fail(f"{method_name} should reuse record.Target marker -> {snippet}")

    old_call = "SetZombieModeEnemyThreatSuppressed(record.GameObject, true)"
    if old_call in keep or old_call in suppress:
        return fail("old suppress call without marker remains")
    old_release_call = "SetZombieModeEnemyThreatSuppressed(record.GameObject, false)"
    if old_release_call in release:
        return fail("old release call without marker remains")

    spawn_required = [
        "PrepareZombieModeSpawnedEnemy(zombie, marker, ZombieModeTuning.NormalZombieForceTraceDistance);",
        "PrepareZombieModeSpawnedEnemy(boss, bossMarker, 180f);",
        "SetZombieModeEnemyThreatSuppressed(enemy.gameObject, marker, true);",
    ]
    for snippet in spawn_required:
        if snippet not in spawner_text:
            return fail("normal zombie spawn path should reuse registered marker -> " + snippet)

    if "SetZombieModeEnemyThreatSuppressed(enemy.gameObject, true)" in prepare:
        return fail("spawn preparation still calls threat suppression without marker")

    print("ZombieModeSafeZoneMarkerReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
