"""Guard: Dragon King projectile half-damage range should avoid Vector3.Distance per hit."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs")


def fail(message: str) -> int:
    print("DragonKingProjectileHalfDamageDistanceGuard: FAIL - " + message)
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
    body = extract_method_body(
        text,
        "private bool HandleDamageReceiverHit(DamageReceiver receiver, GameObject hitObject, Vector3 hitPoint, Vector3 hitNormal)",
    )
    if body is None:
        return fail("missing HandleDamageReceiverHit body")

    if "Vector3.Distance(startPointRef(projectile), hitPoint)" in body:
        return fail("half-damage range still uses Vector3.Distance per receiver hit")

    required = [
        "float halfDamageDistance = projectile.context.halfDamageDistance;",
        "float halfDamageDistanceSqr = halfDamageDistance * halfDamageDistance;",
        "Vector3 damageDistanceDelta = hitPoint - startPointRef(projectile);",
        "damageDistanceDelta.sqrMagnitude > halfDamageDistanceSqr",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing squared half-damage snippet -> " + snippet)

    print("DragonKingProjectileHalfDamageDistanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
