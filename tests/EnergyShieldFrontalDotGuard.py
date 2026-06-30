"""Guard: Energy Shield frontal check should avoid Angle/normalized hot-path math."""

from pathlib import Path
import sys


SOURCE = Path("Integration/NewWeapons/EnergyShield/EnergyShieldRuntime.cs")


def fail(message: str) -> int:
    print("EnergyShieldFrontalDotGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private static bool IsFrontalAttack(")
    if body is None:
        return fail("missing IsFrontalAttack body")

    forbidden = [
        "Vector3.Angle(",
        ".normalized",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("frontal check still uses expensive angle math -> " + snippet)

    required = [
        "EnergyShieldFrontalAngleCos",
        "Vector3.Dot(playerForward, toAttacker)",
        "dot <= 0f",
        "dot * dot >=",
        "playerForwardSqr * toAttackerSqr",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing dot-product frontal check snippet -> " + snippet)

    print("EnergyShieldFrontalDotGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
