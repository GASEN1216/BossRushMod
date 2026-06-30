"""Guard: Dragon King collision detector should cache transforms for distance ticks."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/DragonKingAbilityHelpers.cs")


def fail(message: str) -> int:
    print("DragonKingCollisionTransformCacheGuard: FAIL - " + message)
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
    cls = extract_block(text, "public class DragonKingCollisionDetector")
    if cls is None:
        return fail("missing DragonKingCollisionDetector class")

    for field in [
        "private Transform cachedTransform;",
        "private Transform cachedPlayerTransform;",
    ]:
        if field not in cls:
            return fail("missing field -> " + field)

    accessor = extract_block(cls, "private Transform CachedTransform")
    if accessor is None:
        return fail("missing CachedTransform accessor")
    for token in [
        "cachedTransform = transform;",
        "return cachedTransform;",
    ]:
        if token not in accessor:
            return fail("CachedTransform accessor missing token -> " + token)

    update = extract_block(cls, "void Update()")
    if update is None:
        return fail("missing Update")

    for token in [
        "cachedPlayerTransform = cachedPlayer.transform;",
        "if (cachedPlayerTransform == null) return;",
        "Vector3 diff = CachedTransform.position - cachedPlayerTransform.position;",
    ]:
        if token not in update:
            return fail("Update missing cached-transform token -> " + token)

    if "transform.position" in update or "cachedPlayer.transform.position" in update:
        return fail("Update should not use direct Transform properties for distance ticks")

    print("DragonKingCollisionTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
