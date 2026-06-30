"""Guard: Dragon King shockwave should cache transforms in pooled hot paths."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/DragonKingShockwaveEffect.cs")


def fail(message: str) -> int:
    print("DragonKingShockwaveTransformCacheGuard: FAIL - " + message)
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

    for field in [
        "private Transform cachedTransform;",
        "private Transform cachedPlayerTransform;",
    ]:
        if field not in text:
            return fail("missing field -> " + field)

    accessor = extract_block(text, "private Transform CachedTransform")
    if accessor is None:
        return fail("missing CachedTransform accessor")
    for token in [
        "cachedTransform = transform;",
        "return cachedTransform;",
    ]:
        if token not in accessor:
            return fail("CachedTransform accessor missing token -> " + token)

    find_player = extract_block(text, "private void FindPlayer()")
    if find_player is None:
        return fail("missing FindPlayer")
    if "cachedPlayerTransform = playerCharacter != null ? playerCharacter.transform : null;" not in find_player:
        return fail("FindPlayer should refresh cached player Transform")

    update = extract_block(text, "private void Update()")
    if update is None:
        return fail("missing Update")
    if "playerPos = cachedPlayerTransform.position;" not in update:
        return fail("Update should use cached player Transform position")
    if "playerCharacter.transform.position" in update:
        return fail("Update should not use direct playerCharacter.transform.position")

    for signature in [
        "public static DragonKingShockwaveEffect PlayAt(Vector3 position)",
        "public void StartShockwave(Vector3 center)",
        "private WaveRing CreateWaveRing(int index)",
        "private void ReturnToPool()",
    ]:
        block = extract_block(text, signature)
        if block is None:
            return fail("missing block -> " + signature)
        if "CachedTransform" not in block:
            return fail(signature + " should use CachedTransform")

    print("DragonKingShockwaveTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
