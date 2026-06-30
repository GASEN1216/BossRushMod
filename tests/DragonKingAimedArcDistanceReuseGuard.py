"""Guard: Dragon King aimed-arc launch setup should reuse target distance math."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs")


def fail(message: str) -> int:
    print("DragonKingAimedArcDistanceReuseGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private static ProjectileContext BuildProjectileContext(")
    if body is None:
        return fail("missing BuildProjectileContext body")

    forbidden = [
        "float horizontalDist = Mathf.Max(1f, toTarget.magnitude);",
        "Vector3 horizontalDir = toTarget.normalized;",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("aimed arc still repeats toTarget distance math -> " + snippet)

    required = [
        "float toTargetDistanceSqr = toTarget.sqrMagnitude;",
        "float toTargetDistance = Mathf.Sqrt(toTargetDistanceSqr);",
        "float horizontalDist = Mathf.Max(1f, toTargetDistance);",
        "Vector3 horizontalDir = toTargetDistanceSqr > 0.0000000001f ? toTarget / toTargetDistance : Vector3.zero;",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing aimed-arc distance reuse snippet -> " + snippet)

    print("DragonKingAimedArcDistanceReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
