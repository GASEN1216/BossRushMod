"""
Guard: Phantom Witch custom spawn path must participate in shared BossRush lootbox tracking.

Reason:
- generic bosses call RegisterBossRandomLootTracking(), which marks reward-box defer state
- Phantom Witch uses a custom spawn path, so it must reuse the same shared helper and finalize path
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

    spawn_block = extract_block(
        text,
        "public async UniTask<CharacterMainControl> SpawnPhantomWitch(",
    )
    if not spawn_block:
        return fail("PhantomWitchLootboxTrackingGuard: missing SpawnPhantomWitch block")

    if "RegisterBossRandomLootTracking(character, originalLootCount, 0f);" not in spawn_block:
        return fail("PhantomWitchLootboxTrackingGuard: SpawnPhantomWitch does not reuse RegisterBossRandomLootTracking(..., 0f)")

    cleanup_targets = [
        "private void CleanupFailedPhantomWitchSpawn(CharacterMainControl character)",
        "private void CleanupTrackedPhantomWitchCharacter(",
        "private void OnPhantomWitchDeath(CharacterMainControl deadWitch, DamageInfo damageInfo)",
    ]

    for signature in cleanup_targets:
        block = extract_block(text, signature)
        if not block:
            return fail(f"PhantomWitchLootboxTrackingGuard: missing cleanup block {signature}")
        if "FinalizeBossRushLootboxPathTracking(" not in block:
            return fail(f"PhantomWitchLootboxTrackingGuard: cleanup block missing FinalizeBossRushLootboxPathTracking -> {signature}")
        if "ClearBossRandomLootTracking(" not in block:
            return fail(f"PhantomWitchLootboxTrackingGuard: cleanup block missing ClearBossRandomLootTracking -> {signature}")

    print("PhantomWitchLootboxTrackingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
