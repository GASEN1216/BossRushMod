from pathlib import Path
import sys


RECOVERY = Path("Utilities/EnemyRecoveryMonitor.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    text = RECOVERY.read_text(encoding="utf-8")
    recover = extract_method(text, "private bool TryRecoverEnemyToNearestSpawnPoint")

    if not recover:
        return fail("EnemyRecoveryHealthPreservationGuard: cannot extract recovery method")

    for snippet in [
        "float preservedCurrentHealth",
        "bool hasPreservedCurrentHealth",
        "RestoreEnemyHealthAfterRecovery(enemy, preservedCurrentHealth, hasPreservedCurrentHealth);",
    ]:
        if snippet not in recover:
            return fail("EnemyRecoveryHealthPreservationGuard: recovery method missing snippet -> " + snippet)

    if text.count("RestoreEnemyHealthAfterRecovery(enemy, preservedCurrentHealth, hasPreservedCurrentHealth);") != 1:
        return fail("EnemyRecoveryHealthPreservationGuard: expected exactly one health preservation call")

    helper = extract_method(text, "private void RestoreEnemyHealthAfterRecovery")
    if not helper:
        return fail("EnemyRecoveryHealthPreservationGuard: missing health preservation helper")

    for snippet in [
        "health.CurrentHealth > clampedHealth + 0.01f",
        "health.SetHealth(clampedHealth);",
    ]:
        if snippet not in helper:
            return fail("EnemyRecoveryHealthPreservationGuard: helper missing snippet -> " + snippet)

    print("EnemyRecoveryHealthPreservationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
