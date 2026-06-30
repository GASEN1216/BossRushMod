"""Guard: Dragon King sticky charge should cache its Transform in Update."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProjectileZones.cs")


def fail(message: str) -> int:
    print("DragonKingStickyChargeTransformCacheGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
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
                return text[start : idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    cls = extract_block(text, "internal sealed class DragonKingBossGunStickyCharge")
    if cls is None:
        return fail("missing DragonKingBossGunStickyCharge class")

    if "private Transform cachedTransform;" not in cls:
        return fail("missing cached Transform field")

    accessor = extract_block(cls, "private Transform CachedTransform")
    if accessor is None:
        return fail("missing CachedTransform accessor")
    for token in [
        "cachedTransform = transform;",
        "return cachedTransform;",
    ]:
        if token not in accessor:
            return fail("CachedTransform accessor missing token -> " + token)

    update = extract_block(cls, "private void Update()")
    if update is None:
        return fail("missing Update")

    for token in [
        "Transform selfTransform = CachedTransform;",
        "Vector3 chargePosition = selfTransform.position;",
        "selfTransform.position = chargePosition;",
        "TrySpawnExplosionFx(chargePosition, profile)",
        "ApplyRadiusDamage(",
        "chargePosition,",
    ]:
        if token not in update:
            return fail("Update missing cached-position token -> " + token)

    if "transform.position" in update:
        return fail("Update should not use direct transform.position")

    print("DragonKingStickyChargeTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
