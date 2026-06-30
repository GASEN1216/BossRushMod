"""Guard: equipment movement input should avoid magnitude sqrt in per-frame input normalization."""

from pathlib import Path
import sys


SOURCE = Path("Common/Equipment/EquipmentAbilityAction.cs")


def fail(message: str) -> int:
    print("EquipmentMovementInputSqrGuard: FAIL - " + message)
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
    body = extract_method_body(text, "protected Vector2 GetMovementInput()")
    if body is None:
        return fail("missing GetMovementInput body")

    if "input.magnitude > 1f" in body:
        return fail("movement input still uses magnitude")

    if "input.sqrMagnitude > 1f" not in body:
        return fail("missing squared movement input threshold")

    print("EquipmentMovementInputSqrGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
