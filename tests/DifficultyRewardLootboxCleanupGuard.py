"""
Guard: central BossRush difficulty reward lootbox must schedule the
post-setup cleanup pass after LootBoxLoader.StartSetup().

Reason:
- the difficulty reward pool is intended to stay high-quality only
- CleanupDifficultyRewardLootboxInventory_LootAndRewards already exists as the
  final post-setup enforcement/logging path
- if SpawnDifficultyRewardLootbox_LootAndRewards forgets to start that
  coroutine, leaked low-quality items can survive with no final validation
"""

from pathlib import Path
import sys


SOURCES = [
    Path("LootAndRewards/LootAndRewards.cs"),
    Path("LootAndRewards/LootAndRewardsInfiniteHell.cs"),
    Path("LootAndRewards/LootAndRewardsVictoryRewards.cs"),
    Path("LootAndRewards/LootAndRewardsRandomBossLoot.cs"),
    Path("LootAndRewards/LootAndRewardsSpecialLoot.cs"),
]


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
    text = "\n".join(path.read_text(encoding="utf-8") for path in SOURCES if path.exists())
    block = extract_block(text, "private void SpawnDifficultyRewardLootbox_LootAndRewards(int highQualityCount)")
    if not block:
        return fail("DifficultyRewardLootboxCleanupGuard: spawn method not found")

    setup_call = "loader.StartSetup();"
    cleanup_call = "StartCoroutine(CleanupDifficultyRewardLootboxInventory_LootAndRewards(lootbox, highQualityCount));"

    if setup_call not in block:
        return fail("DifficultyRewardLootboxCleanupGuard: loader.StartSetup() call missing")

    if cleanup_call not in block:
        return fail("DifficultyRewardLootboxCleanupGuard: post-setup cleanup coroutine is not scheduled")

    if block.find(cleanup_call) < block.find(setup_call):
        return fail("DifficultyRewardLootboxCleanupGuard: cleanup coroutine is scheduled before loader.StartSetup()")

    print("DifficultyRewardLootboxCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
