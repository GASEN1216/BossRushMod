"""Guard: Dragon King teleport offset clamp should reuse one distance calculation."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/DragonKingAbilityController_ProjectileAndMovement.cs")


def fail(message: str) -> int:
    print("DragonKingTeleportOffsetDistanceGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private Vector3 FindValidTeleportPosition(")
    if body is None:
        return fail("missing FindValidTeleportPosition body")

    forbidden = [
        "if (randomOffset.magnitude < minDistance)",
        "randomOffset = randomOffset.normalized * minDistance;",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("teleport offset clamp still repeats distance math -> " + snippet)

    required = [
        "float minDistanceSqr = minDistance * minDistance;",
        "float randomOffsetDistanceSqr = randomOffset.sqrMagnitude;",
        "if (randomOffsetDistanceSqr < minDistanceSqr)",
        "float randomOffsetDistance = Mathf.Sqrt(randomOffsetDistanceSqr);",
        "randomOffset = randomOffsetDistance > 0.00001f ? randomOffset * (minDistance / randomOffsetDistance) : Vector2.zero;",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing teleport offset distance snippet -> " + snippet)

    print("DragonKingTeleportOffsetDistanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
