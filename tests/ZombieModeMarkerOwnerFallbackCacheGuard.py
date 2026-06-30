"""Guard: zombie runtime marker owner fallbacks should cache the resolved owner."""

from pathlib import Path
import sys


GRAVITY = Path("ZombieMode/ZombieModeRewardProjectileSpread.cs")
TRIGGERS = Path("ZombieMode/ZombieModeRewardTriggerEffects.cs")
SAFE_ZONE = Path("ZombieMode/ZombieModeSafeZoneController.cs")
RECOVERY = Path("Utilities/EnemyRecoveryMonitor.cs")


def fail(message: str) -> int:
    print("ZombieModeMarkerOwnerFallbackCacheGuard: FAIL - " + message)
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


def require_owner_cache(body: str, method_name: str) -> int:
    forbidden = "marker.Owner != null ? marker.Owner : marker.GetComponent<CharacterMainControl>()"
    if forbidden in body:
        return fail(method_name + " still uses uncached owner fallback ternary")

    required = [
        "CharacterMainControl enemy = marker.Owner;",
        "if (enemy == null)",
        "enemy = marker.GetComponent<CharacterMainControl>();",
        "marker.Owner = enemy;",
    ]
    for snippet in required:
        if snippet not in body:
            return fail(method_name + " missing cached owner fallback snippet -> " + snippet)

    return 0


def main() -> int:
    gravity_text = GRAVITY.read_text(encoding="utf-8-sig")
    trigger_text = TRIGGERS.read_text(encoding="utf-8-sig")
    safe_zone_text = SAFE_ZONE.read_text(encoding="utf-8-sig")
    recovery_text = RECOVERY.read_text(encoding="utf-8-sig")

    gravity = extract_method_body(gravity_text, "internal void RefreshZombieModeGravityWellTargets(")
    if gravity is None:
        return fail("missing RefreshZombieModeGravityWellTargets body")
    result = require_owner_cache(gravity, "RefreshZombieModeGravityWellTargets")
    if result:
        return result

    nearest = extract_method_body(trigger_text, "private CharacterMainControl TryFindZombieModeNearestEnemyTarget(")
    if nearest is None:
        return fail("missing TryFindZombieModeNearestEnemyTarget body")
    result = require_owner_cache(nearest, "TryFindZombieModeNearestEnemyTarget")
    if result:
        return result

    safe_zone = extract_method_body(safe_zone_text, "private void KeepZombieModeEnemiesOutsideSafeZone()")
    if safe_zone is None:
        return fail("missing KeepZombieModeEnemiesOutsideSafeZone body")
    safe_zone_required = [
        "CharacterMainControl owner = marker != null ? marker.Owner : null;",
        "owner = record.GameObject.GetComponent<CharacterMainControl>();",
        "if (marker != null)",
        "marker.Owner = owner;",
    ]
    for snippet in safe_zone_required:
        if snippet not in safe_zone:
            return fail("KeepZombieModeEnemiesOutsideSafeZone missing owner cache snippet -> " + snippet)

    recovery = extract_method_body(recovery_text, "private void MonitorZombieModeEnemyRecovery(")
    if recovery is None:
        return fail("missing MonitorZombieModeEnemyRecovery body")
    recovery_required = [
        "ZombieModeEnemyRuntimeMarker marker = record.Target as ZombieModeEnemyRuntimeMarker;",
        "if (marker == null && record.GameObject != null)",
        "marker = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();",
        "CharacterMainControl enemy = marker != null ? marker.Owner : null;",
        "enemy = record.GameObject.GetComponent<CharacterMainControl>();",
        "enemy = record.GameObject.GetComponentInChildren<CharacterMainControl>(true);",
        "if (marker != null)",
        "marker.Owner = enemy;",
    ]
    for snippet in recovery_required:
        if snippet not in recovery:
            return fail("MonitorZombieModeEnemyRecovery missing owner cache snippet -> " + snippet)

    print("ZombieModeMarkerOwnerFallbackCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
