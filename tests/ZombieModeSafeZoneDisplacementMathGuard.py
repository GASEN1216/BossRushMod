"""Guard: zombie safe-zone enemy displacement should avoid repeated distance math."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModeSafeZoneController.cs")


def fail(message: str) -> int:
    print("ZombieModeSafeZoneDisplacementMathGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private void KeepZombieModeEnemiesOutsideSafeZone()")
    if body is None:
        return fail("missing KeepZombieModeEnemiesOutsideSafeZone body")

    if "delta.normalized * repelRadius" in body:
        return fail("safe-zone displacement still normalizes delta directly")

    required = [
        "float deltaDistanceSqr = delta.sqrMagnitude;",
        "if (deltaDistanceSqr > radiusSqr)",
        "if (deltaDistanceSqr < 0.01f)",
        "deltaDistanceSqr = delta.sqrMagnitude;",
        "deltaDistanceSqr = 1f;",
        "float inverseDistance = 1f / Mathf.Sqrt(deltaDistanceSqr);",
        "Vector3 destination = center + delta * (repelRadius * inverseDistance);",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing cached displacement snippet -> " + snippet)

    print("ZombieModeSafeZoneDisplacementMathGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
