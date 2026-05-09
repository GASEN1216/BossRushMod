"""
Guard: Mode E enemy recovery must reuse prevalidated spawn candidates instead of
ground-probing every Mode E spawn point for every recovered enemy.
"""

from pathlib import Path
import sys


SOURCE = Path("Utilities/EnemyRecoveryMonitor.cs")


def fail(message: str) -> int:
    print(message)
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
    text = SOURCE.read_text(encoding="utf-8")

    for required in (
        "enemyRecoveryModeEValidatedSpawnCandidates",
        "enemyRecoveryModeEValidatedSourcePoints",
        "enemyRecoverySpawnCandidatesArePrevalidated",
        "AppendModeERecoverySpawnCandidates",
    ):
        if required not in text:
            return fail(f"EnemyRecoveryModeECandidateCacheGuard: missing Mode E candidate cache invariant -> {required}")

    clear_body = extract_method_body(text, "private void ClearEnemyRecoveryMonitorState()")
    if clear_body is None:
        return fail("EnemyRecoveryModeECandidateCacheGuard: missing ClearEnemyRecoveryMonitorState body")
    for required in (
        "enemyRecoveryModeEValidatedSpawnCandidates.Clear()",
        "enemyRecoveryModeEValidatedSourcePoints = null",
        "enemyRecoverySpawnCandidatesArePrevalidated = false",
    ):
        if required not in clear_body:
            return fail(f"EnemyRecoveryModeECandidateCacheGuard: recovery clear does not reset -> {required}")

    collect_body = extract_method_body(text, "private void CollectPrimaryRecoverySpawnCandidates")
    if collect_body is None:
        return fail("EnemyRecoveryModeECandidateCacheGuard: missing CollectPrimaryRecoverySpawnCandidates body")
    if "AppendModeERecoverySpawnCandidates()" not in collect_body:
        return fail("EnemyRecoveryModeECandidateCacheGuard: Mode E recovery does not use cached candidate appender")

    select_body = extract_method_body(text, "private bool TrySelectNearestRecoverySpawnPoint")
    if select_body is None:
        return fail("EnemyRecoveryModeECandidateCacheGuard: missing TrySelectNearestRecoverySpawnPoint body")
    if "enemyRecoverySpawnCandidatesArePrevalidated" not in select_body:
        return fail("EnemyRecoveryModeECandidateCacheGuard: selection does not skip repeated ground probes for prevalidated candidates")

    print("EnemyRecoveryModeECandidateCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
