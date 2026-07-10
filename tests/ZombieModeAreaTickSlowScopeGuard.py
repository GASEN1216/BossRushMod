"""Guard: ZombieMode area tick slow must stay scoped to the visible area."""

from pathlib import Path
import sys


BOSS = Path("ZombieMode/ZombieModeBossController.cs")
RUNTIME = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")


def fail(message: str) -> int:
    print("ZombieModeAreaTickSlowScopeGuard: FAIL - " + message)
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


def extract_class_body(text: str, class_name: str, next_class_name: str) -> str | None:
    start_token = "public sealed class " + class_name
    end_token = "\n    public sealed class " + next_class_name
    start = text.find(start_token)
    if start < 0:
        return None

    end = text.find(end_token, start + len(start_token))
    if end < 0:
        return None

    return text[start:end]


def main() -> int:
    boss = BOSS.read_text(encoding="utf-8-sig")
    runtime = RUNTIME.read_text(encoding="utf-8-sig")

    area_runtime = extract_class_body(boss, "ZombieModeAreaTickRuntime", "ZombieModeBossShieldRuntime")
    if area_runtime is None:
        return fail("missing ZombieModeAreaTickRuntime class")

    for token in [
        "float startupDelay = 0f",
        "bool followSource = false",
        "private bool followSourcePosition;",
        "nextTickTime = Time.unscaledTime + Mathf.Max(Mathf.Max(0f, startupDelay), tickInterval);",
        "if (tickDamage > 0f)",
        "inst.DealZombieModeRuntimeAreaDamageToPlayer(RuntimeRunId, source, transform.position, radius, tickDamage);",
        "inst.TryApplyZombieModePlayerSlowInArea(RuntimeRunId, transform.position, radius, slowPercent, tickInterval * 2f);",
    ]:
        if token not in area_runtime:
            return fail("area tick runtime missing scoped slow/startup token -> " + token)

    if "inst.TryApplyZombieModePlayerSlow(RuntimeRunId, slowPercent" in area_runtime:
        return fail("area tick runtime reverted to global player slow")

    corruption_zone = extract_method_body(boss, "private void SpawnZombieModeCorruptionZone(")
    if corruption_zone is None:
        return fail("missing SpawnZombieModeCorruptionZone body")
    for token in [
        "ZombieModeTuning.CorruptorZoneStartupSeconds + ZombieModeTuning.CorruptorZoneDurationSeconds",
        "ZombieModeTuning.CorruptorZoneSlowPercent,",
        "ZombieModeTuning.CorruptorZoneStartupSeconds",
    ]:
        if token not in corruption_zone:
            return fail("corruption zone no longer passes startup/slow tuning -> " + token)

    slow_area = extract_method_body(runtime, "public void TryApplyZombieModePlayerSlowInArea(")
    if slow_area is None:
        return fail("missing TryApplyZombieModePlayerSlowInArea helper")
    for token in [
        "Vector3 delta = player.transform.position - origin;",
        "if (delta.sqrMagnitude > radius * radius)",
        "TryApplyZombieModePlayerSlow(runId, percent, duration);",
    ]:
        if token not in slow_area:
            return fail("area slow helper missing range check token -> " + token)

    print("ZombieModeAreaTickSlowScopeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
