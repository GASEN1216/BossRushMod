"""Guard: Phantom Witch teleport fallback should reuse one distance calculation."""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAbilityController_MovementAndDamage.cs")


def fail(message: str) -> int:
    print("PhantomWitchTeleportFallbackDistanceGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private Vector3 ResolveTeleportPosition(")
    if body is None:
        return fail("missing ResolveTeleportPosition body")

    forbidden = [
        "toTarget.normalized * Mathf.Min(toTarget.magnitude, maxDistance * 0.6f)",
        "toTarget.magnitude",
        "toTarget.normalized",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("teleport fallback still repeats target distance math -> " + snippet)

    required = [
        "float toTargetDistanceSqr = toTarget.sqrMagnitude;",
        "if (toTargetDistanceSqr > 0.01f)",
        "float toTargetDistance = Mathf.Sqrt(toTargetDistanceSqr);",
        "float fallbackMoveDistance = Mathf.Min(toTargetDistance, maxDistance * 0.6f);",
        "Vector3 approach = bossPos + toTarget * (fallbackMoveDistance / toTargetDistance);",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing teleport fallback distance snippet -> " + snippet)

    print("PhantomWitchTeleportFallbackDistanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
