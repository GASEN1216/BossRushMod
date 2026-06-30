"""Guard: zombie run-only marker fallbacks should cache the recovered marker."""

from pathlib import Path
import sys


SOURCES = {
    "CollectZombieModeRuntimeEnemyMarkers": (
        Path("ZombieMode/ZombieModeDropsAndPerformance.cs"),
        "private int CollectZombieModeRuntimeEnemyMarkers(",
    ),
    "PruneZombieModeRunOnlyEnemyRecords": (
        Path("ZombieMode/ZombieModeCleanup.cs"),
        "private void PruneZombieModeRunOnlyEnemyRecords(",
    ),
    "RefreshZombieModeCommanderAuraTargets": (
        Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs"),
        "internal void RefreshZombieModeCommanderAuraTargets(",
    ),
    "KeepZombieModeEnemiesOutsideSafeZone": (
        Path("ZombieMode/ZombieModeSafeZoneController.cs"),
        "private void KeepZombieModeEnemiesOutsideSafeZone()",
    ),
    "SuppressZombieModeSafeZoneThreats": (
        Path("ZombieMode/ZombieModeSafeZoneController.cs"),
        "private void SuppressZombieModeSafeZoneThreats()",
    ),
    "ReleaseZombieModeSafeZoneThreatSuppression": (
        Path("ZombieMode/ZombieModeSafeZoneController.cs"),
        "private void ReleaseZombieModeSafeZoneThreatSuppression()",
    ),
    "MonitorZombieModeEnemyRecovery": (
        Path("Utilities/EnemyRecoveryMonitor.cs"),
        "private void MonitorZombieModeEnemyRecovery(",
    ),
    "ClearZombieModeTemporaryNpcThreatTargets": (
        Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
        "private void ClearZombieModeTemporaryNpcThreatTargets()",
    ),
}


def fail(message: str) -> int:
    print("ZombieModeRunOnlyMarkerFallbackCacheGuard: FAIL - " + message)
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


def require_marker_cache(method_name: str, body: str) -> int:
    required = [
        "record.Target as ZombieModeEnemyRuntimeMarker",
        "record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();",
        "if (marker != null)",
        "record.Target = marker;",
    ]
    if method_name == "RefreshZombieModeCommanderAuraTargets":
        required = [
            "record.Target as ZombieModeEnemyRuntimeMarker",
            "recordObject.GetComponent<ZombieModeEnemyRuntimeMarker>();",
            "if (target != null)",
            "record.Target = target;",
        ]

    for snippet in required:
        if snippet not in body:
            return fail(method_name + " missing marker fallback cache snippet -> " + snippet)

    return 0


def main() -> int:
    for method_name, (path, signature) in SOURCES.items():
        text = path.read_text(encoding="utf-8-sig")
        body = extract_method_body(text, signature)
        if body is None:
            return fail("missing " + method_name + " body")

        result = require_marker_cache(method_name, body)
        if result:
            return result

    print("ZombieModeRunOnlyMarkerFallbackCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
