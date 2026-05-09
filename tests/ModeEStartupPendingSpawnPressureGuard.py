"""
Guard: Mode E startup must count queued-but-not-yet-dispatched Boss spawns as
pending pressure so respawn consumables cannot bypass the active Boss cap during
the delayed opening spawn sequence.
"""

from pathlib import Path
import sys


SOURCE = Path("ModeE/ModeEBattle.cs")


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

    spawn_all_body = extract_method_body(battle_text, "public async UniTaskVoid ModeESpawnAllBosses")
    if spawn_all_body is None:
        return fail("ModeEStartupPendingSpawnPressureGuard: missing ModeESpawnAllBosses body")

    single_body = extract_method_body(battle_text, "private void SpawnSingleModeEBoss")
    if single_body is None:
        return fail("ModeEStartupPendingSpawnPressureGuard: missing SpawnSingleModeEBoss body")

    precount = "modeETotalSpawnExpected = spawnTasks.Count;"
    if precount not in spawn_all_body:
        return fail("ModeEStartupPendingSpawnPressureGuard: startup spawn queue is not pre-counted")

    reset_idx = spawn_all_body.find("modeESpawnResolved = 0;")
    sort_idx = spawn_all_body.find("spawnTasks.Sort")
    precount_idx = spawn_all_body.find(precount)
    loop_idx = spawn_all_body.find("for (int i = 0; i < spawnTasks.Count; i++)")
    if min(reset_idx, sort_idx, precount_idx, loop_idx) < 0:
        return fail("ModeEStartupPendingSpawnPressureGuard: missing expected startup spawn structure")
    if not reset_idx < precount_idx:
        return fail("ModeEStartupPendingSpawnPressureGuard: spawn resolution counter must reset before pre-counting")
    if not sort_idx < precount_idx < loop_idx:
        return fail("ModeEStartupPendingSpawnPressureGuard: startup queue must be pre-counted after collection and before dispatch")

    if "countSpawnAttemptImmediately: false" not in spawn_all_body:
        return fail("ModeEStartupPendingSpawnPressureGuard: startup dispatch must not double-count pre-counted attempts")

    for required in (
        "bool countSpawnAttemptImmediately = true",
        "bool spawnAttemptCounted = !countSpawnAttemptImmediately;",
        "private void ResolveModeESpawnAttemptIfCounted",
    ):
        if required not in battle_text:
            return fail(f"ModeEStartupPendingSpawnPressureGuard: missing counted-attempt safeguard -> {required}")

    count_guard_idx = single_body.find("if (countSpawnAttemptImmediately)")
    increment_idx = single_body.find("modeETotalSpawnExpected++")
    counted_true_idx = single_body.find("spawnAttemptCounted = true;", count_guard_idx)
    if not (0 <= count_guard_idx < increment_idx < counted_true_idx):
        return fail("ModeEStartupPendingSpawnPressureGuard: non-startup dispatches must increment and mark counted attempts together")

    skip_idx = single_body.find("无任何匹配预设")
    if skip_idx < 0:
        return fail("ModeEStartupPendingSpawnPressureGuard: missing no-preset skip path")
    skip_return_idx = single_body.find("return;", skip_idx)
    skip_segment = single_body[skip_idx:skip_return_idx]
    if "ResolveModeESpawnAttemptIfCounted(spawnAttemptCounted);" not in skip_segment:
        return fail("ModeEStartupPendingSpawnPressureGuard: pre-counted no-preset startup attempts can remain pending forever")

    catch_idx = single_body.rfind("catch (Exception e)")
    if catch_idx < 0:
        return fail("ModeEStartupPendingSpawnPressureGuard: missing SpawnSingleModeEBoss catch path")
    catch_segment = single_body[catch_idx:]
    if "ResolveModeESpawnAttemptIfCounted(spawnAttemptCounted);" not in catch_segment:
        return fail("ModeEStartupPendingSpawnPressureGuard: counted synchronous spawn exceptions can remain pending forever")

    print("ModeEStartupPendingSpawnPressureGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
