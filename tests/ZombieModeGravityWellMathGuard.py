"""Guard: zombie-mode gravity well should not compute magnitude and normalized separately."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModeRewardProjectileSpread.cs")


def fail(message: str) -> int:
    print("ZombieModeGravityWellMathGuard: FAIL - " + message)
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
    body = extract_method_body(text, "internal void RefreshZombieModeGravityWellTargets(")
    if body is None:
        return fail("missing RefreshZombieModeGravityWellTargets body")

    forbidden = [
        "delta.magnitude",
        "delta.normalized",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("gravity well still uses redundant vector math -> " + snippet)

    required = [
        "float distanceSqr = delta.sqrMagnitude;",
        "float radiusSqr = radius * radius;",
        "float minPullDistanceSqr = minPullDistance * minPullDistance;",
        "float distance = Mathf.Sqrt(distanceSqr);",
        "float stepDistance = Mathf.Min(distance - stopDistance, pullStrength);",
        "Vector3 step = delta * (stepDistance / distance);",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing single-sqrt gravity well snippet -> " + snippet)

    print("ZombieModeGravityWellMathGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
