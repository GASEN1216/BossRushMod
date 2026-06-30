"""Guard: FenHuang trajectory obstacle checks should skip tiny segments before sqrt."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/FenHuangHalberdAbilityManager.cs")


def fail(message: str) -> int:
    print("FenHuangTrajectoryObstacleSqrGuard: FAIL - " + message)
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
                return text[brace_start:idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "private static bool CheckTrajectoryObstacles(")
    if body is None:
        return fail("missing CheckTrajectoryObstacles body")

    if "direction.magnitude" in body:
        return fail("obstacle check still computes magnitude before tiny-segment skip")

    required = [
        "float distanceSqr = direction.sqrMagnitude;",
        "if (distanceSqr <= 0.000001f)",
        "float distance = Mathf.Sqrt(distanceSqr);",
        "direction /= distance;",
        "Physics.SphereCast(from, 0.2f, direction, out hit, distance, obstacleLayers)",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing squared obstacle-distance snippet -> " + snippet)

    print("FenHuangTrajectoryObstacleSqrGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
