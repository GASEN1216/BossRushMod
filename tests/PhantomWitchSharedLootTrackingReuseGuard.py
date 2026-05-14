"""
Guard: Phantom Witch should reuse shared boss loot tracking helpers instead of
keeping a parallel loot-hook map.
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchBoss.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    if "phantomWitchLootEventHandlers" in text:
        return fail("PhantomWitchSharedLootTrackingReuseGuard: parallel phantomWitchLootEventHandlers map still exists")

    spawn_block = extract_block(text, "public async UniTask<CharacterMainControl> SpawnPhantomWitch(")
    if not spawn_block:
        return fail("PhantomWitchSharedLootTrackingReuseGuard: missing SpawnPhantomWitch block")

    if "RegisterBossRandomLootTracking(character, originalLootCount, 0f);" not in spawn_block:
        return fail("PhantomWitchSharedLootTrackingReuseGuard: SpawnPhantomWitch must reuse RegisterBossRandomLootTracking(..., 0f)")

    for signature in [
        "private void CleanupFailedPhantomWitchSpawn(CharacterMainControl character)",
        "private void CleanupTrackedPhantomWitchCharacter(",
        "private void OnPhantomWitchDeath(CharacterMainControl deadWitch, DamageInfo damageInfo)",
    ]:
        block = extract_block(text, signature)
        if not block:
            return fail(f"PhantomWitchSharedLootTrackingReuseGuard: missing block {signature}")
        if "ClearBossRandomLootTracking(" not in block and "ClearBossRandomLootTracking," not in block:
            return fail(f"PhantomWitchSharedLootTrackingReuseGuard: block missing ClearBossRandomLootTracking -> {signature}")

    print("PhantomWitchSharedLootTrackingReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
