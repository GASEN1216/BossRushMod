"""Guard: Mode F bounty radar direction should not normalize target vector repeatedly."""

from pathlib import Path
import sys


SOURCE = Path("ModeF/ModeFUI_BountyRadarAndHealthBars.cs")


def fail(message: str) -> int:
    print("ModeFBountyRadarDirectionMathGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private static Vector2 GetModeFBountyRadarDirection(")
    if body is None:
        return fail("missing GetModeFBountyRadarDirection body")

    forbidden = [
        "toTarget.normalized",
        "direction.normalized",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("radar direction still normalizes avoidably -> " + snippet)

    required = [
        "float directionSqr = direction.sqrMagnitude;",
        "float invDirectionMagnitude = 1f / Mathf.Sqrt(directionSqr);",
        "return direction * invDirectionMagnitude;",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing single-sqrt direction normalization snippet -> " + snippet)

    print("ModeFBountyRadarDirectionMathGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
