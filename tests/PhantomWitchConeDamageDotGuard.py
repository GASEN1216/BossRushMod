"""Guard: Phantom Witch cone damage should avoid Vector3.Angle per collider hit."""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchAbilityController_MovementAndDamage.cs")


def fail(message: str) -> int:
    print("PhantomWitchConeDamageDotGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private int DealConeDamage(")
    if body is None:
        return fail("missing DealConeDamage body")

    if "Vector3.Angle(" in body:
        return fail("DealConeDamage still uses Vector3.Angle per hit")

    if "toTarget.normalized" in body:
        return fail("DealConeDamage still normalizes target vector per hit")

    required = [
        "float angleDotThreshold = Mathf.Cos(halfAngle * Mathf.Deg2Rad);",
        "float targetDistance = Mathf.Sqrt(sqrDistance);",
        "float targetDot = Vector3.Dot(forward, toTarget);",
        "if (targetDot < targetDistance * angleDotThreshold)",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing dot cone snippet -> " + snippet)

    print("PhantomWitchConeDamageDotGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
