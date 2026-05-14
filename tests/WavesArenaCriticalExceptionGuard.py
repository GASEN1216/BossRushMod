"""Guard: critical WavesArena state transitions must log outer exceptions."""

from pathlib import Path
import re
import sys


ARENA = Path("WavesArena/WavesArena.cs")
SPAWNER_CONTROL = Path("WavesArena/WavesArenaSpawnerControl.cs")
EMPTY_CATCH_RE = re.compile(r"catch\s*(?:\([^)]*\))?\s*\{\s*\}", re.S)


def fail(message: str) -> int:
    print("WavesArenaCriticalExceptionGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace_start = text.find("{", start)
    if brace_start < 0:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def assert_logs_outer_exception(text: str, signature: str, expected_log: str) -> str:
    method = extract_method(text, signature)
    if not method:
        return "missing method: " + signature
    if expected_log not in method:
        return "method missing critical exception log: " + expected_log
    return ""


def assert_no_empty_catches(text: str, signature: str) -> str:
    method = extract_method(text, signature)
    if not method:
        return "missing method: " + signature
    if EMPTY_CATCH_RE.search(method):
        return "method still contains an empty catch: " + signature
    return ""


def main() -> int:
    if not ARENA.exists() or not SPAWNER_CONTROL.exists():
        return fail("missing WavesArena source files")

    arena = ARENA.read_text(encoding="utf-8")
    spawner_control = SPAWNER_CONTROL.read_text(encoding="utf-8")

    checks = [
        (arena, "private void HandleBossDeath(", "[BossRush] [ERROR] HandleBossDeath 错误: "),
        (arena, "private void ProceedAfterWaveFinished()", "[BossRush] [ERROR] ProceedAfterWaveFinished 错误: "),
        (arena, "private void OnBossSpawnFailed(", "[BossRush] [ERROR] OnBossSpawnFailed 错误: "),
        (spawner_control, "private void TryFixStuckWaveIfNoBossAlive()", "[BossRush] [ERROR] TryFixStuckWaveIfNoBossAlive 错误: "),
    ]
    for text, signature, expected_log in checks:
        error = assert_logs_outer_exception(text, signature, expected_log)
        if error:
            return fail(error)

    no_empty_checks = [
        (arena, "private void OnEnemyDiedWithDamageInfo("),
        (arena, "private void HandleBossDeath("),
        (arena, "private void ProceedAfterWaveFinished()"),
        (arena, "private void OnBossSpawnFailed("),
        (spawner_control, "private void DisableAllSpawners()"),
        (spawner_control, "private void TryFixStuckWaveIfNoBossAlive()"),
    ]
    for text, signature in no_empty_checks:
        error = assert_no_empty_catches(text, signature)
        if error:
            return fail(error)

    print("WavesArenaCriticalExceptionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
