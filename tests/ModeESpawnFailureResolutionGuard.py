"""
Guard: Mode E spawn attempts must resolve both success and failure paths, while
startup verification must not treat a failed attempt as a successfully spawned Boss.
"""

from pathlib import Path
import sys


SOURCE = Path("ModeE/ModeEBattle.cs")
MODE_E = Path("ModeE/ModeE.cs")


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
    battle_text = SOURCE.read_text(encoding="utf-8")
    mode_e_text = MODE_E.read_text(encoding="utf-8")

    if "private void ResolveModeESpawnAttempt" not in battle_text:
        return fail("ModeESpawnFailureResolutionGuard: missing ResolveModeESpawnAttempt helper")

    spawned_body = extract_method_body(battle_text, "private void OnModeEEnemySpawned")
    if spawned_body is None:
        return fail("ModeESpawnFailureResolutionGuard: missing OnModeEEnemySpawned body")
    if "ResolveModeESpawnAttempt(" not in spawned_body:
        return fail("ModeESpawnFailureResolutionGuard: successful spawns do not resolve the attempt")
    if "modeESpawnResolved++" in spawned_body:
        return fail("ModeESpawnFailureResolutionGuard: OnModeEEnemySpawned increments resolved directly")

    single_body = extract_method_body(battle_text, "private void SpawnSingleModeEBoss")
    if single_body is None:
        return fail("ModeESpawnFailureResolutionGuard: missing SpawnSingleModeEBoss body")
    if "onFailed:" not in single_body or "ResolveModeESpawnAttempt(" not in single_body:
        return fail("ModeESpawnFailureResolutionGuard: failed spawns do not resolve the attempt")

    verify_body = extract_method_body(mode_e_text, "private System.Collections.IEnumerator WaitForModeEStartupVerification")
    if verify_body is None:
        return fail("ModeESpawnFailureResolutionGuard: missing WaitForModeEStartupVerification body")
    if "modeESpawnResolved > 0" in verify_body:
        return fail("ModeESpawnFailureResolutionGuard: startup verification still treats resolved attempts as successful spawns")

    print("ModeESpawnFailureResolutionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
