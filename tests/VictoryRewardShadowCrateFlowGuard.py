"""
Guard: the standard BossRush victory flow must start a shadow-crate sequence
before the bubble and only complete it after the bubble returns.
"""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/LootAndRewards.cs")


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
    block = extract_block(text, "private async void OnAllEnemiesDefeated_LootAndRewards()")
    if not block:
        return fail("VictoryRewardShadowCrateFlowGuard: missing OnAllEnemiesDefeated_LootAndRewards block")

    start_call = "StartVictoryRewardShadowCrate_LootAndRewards(rewardHighCount);"
    complete_call = "CompleteVictoryRewardShadowCrate_LootAndRewards();"
    direct_spawn_call = "SpawnDifficultyRewardLootbox_LootAndRewards(rewardHighCount);"
    bubble_call = "await DialogueBubblesManager.Show("

    if start_call not in block:
        return fail("VictoryRewardShadowCrateFlowGuard: missing shadow crate start call")

    if complete_call not in block:
        return fail("VictoryRewardShadowCrateFlowGuard: missing shadow crate completion call")

    if bubble_call not in block:
        return fail("VictoryRewardShadowCrateFlowGuard: missing victory bubble call")

    if direct_spawn_call in block:
        return fail("VictoryRewardShadowCrateFlowGuard: victory reward still spawns immediately before the bubble")

    if block.find(start_call) > block.find(bubble_call):
        return fail("VictoryRewardShadowCrateFlowGuard: start call does not happen before the bubble")

    if block.find(complete_call) < block.find(bubble_call):
        return fail("VictoryRewardShadowCrateFlowGuard: completion call happens before the bubble finishes")

    print("VictoryRewardShadowCrateFlowGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
