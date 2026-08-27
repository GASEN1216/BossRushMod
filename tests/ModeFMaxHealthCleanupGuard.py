"""Guard: Mode F exit must remove temporary max-health growth and clamp surplus health."""

from pathlib import Path
import sys


PHASES = Path("ModeF/ModeFPhases.cs")


def fail(message: str) -> int:
    print("ModeFMaxHealthCleanupGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : index + 1]

    return None


def main() -> int:
    phases = PHASES.read_text(encoding="utf-8")

    cleanup = extract_method_body(phases, "private void CleanupModeFPlayerMaxHealthGrowth()")
    if cleanup is None:
        return fail("missing centralized max-health cleanup helper")

    for needle, message in (
        ("growthModifier.RemoveFromTarget();", "growth Modifier must detach from its original Stat target"),
        ("float restoredMaxHealth = health.MaxHealth;", "cleanup must read max health after Modifier removal"),
        ("health.CurrentHealth > restoredMaxHealth", "cleanup must detect surplus current health"),
        ("health.SetHealth(restoredMaxHealth);", "cleanup must clamp current health to the restored maximum"),
    ):
        if needle not in cleanup:
            return fail(message)

    remove_index = cleanup.find("growthModifier.RemoveFromTarget();")
    max_index = cleanup.find("float restoredMaxHealth = health.MaxHealth;")
    clamp_index = cleanup.find("health.SetHealth(restoredMaxHealth);")
    if not remove_index < max_index < clamp_index:
        return fail("cleanup order must be remove Modifier -> read restored maximum -> clamp current health")

    exit_body = extract_method_body(phases, "private void ExitModeF(bool showEndMessage = true)")
    if exit_body is None:
        return fail("missing ExitModeF")
    if "CleanupModeFPlayerMaxHealthGrowth();" not in exit_body:
        return fail("ExitModeF must invoke max-health cleanup")
    if 'GetStat("MaxHealth")' in exit_body or "RemoveModifier(modeFMaxHealthModifier)" in exit_body:
        return fail("ExitModeF must not retain the old partial cleanup path")

    print("ModeFMaxHealthCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
