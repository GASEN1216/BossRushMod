"""Guard: DragonKing tracking projectile end-speed clamp should reuse squared length."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/DragonKingAbilityController.cs")


def fail(message: str) -> int:
    print("DragonKingTrackingProjectileVelocityNormalizeGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private void UpdateTrackingProjectiles()")
    if body is None:
        return fail("missing UpdateTrackingProjectiles body")

    forbidden = "state.currentVelocity = state.currentVelocity.normalized * state.speed;"
    if forbidden in body:
        return fail("tracking end path still normalizes currentVelocity via Vector3.normalized")

    required = [
        "float currentVelocitySqr = state.currentVelocity.sqrMagnitude;",
        "if (currentVelocitySqr > 0.01f)",
        "state.currentVelocity *= state.speed / Mathf.Sqrt(currentVelocitySqr);",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing squared velocity reuse snippet -> " + snippet)

    print("DragonKingTrackingProjectileVelocityNormalizeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
