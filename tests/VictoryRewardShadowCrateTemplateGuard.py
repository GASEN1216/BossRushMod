"""
Guard: victory reward visual presentation must not rely directly on the
deliver-box reward template. The visual shell should prefer a non-deliver
lootbox prefab and only fall back when no better visual exists.
"""

from pathlib import Path
import sys


REWARD_SOURCE = Path("LootAndRewards/LootAndRewards.cs")
VISUAL_SOURCE = Path("LootAndRewards/VictoryRewardShadowCrateController.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    reward_text = REWARD_SOURCE.read_text(encoding="utf-8")
    visual_text = VISUAL_SOURCE.read_text(encoding="utf-8")

    required_reward_snippets = [
        "private InteractableLootbox GetVictoryRewardVisualLootBoxTemplate_LootAndRewards()",
        'bool isDeliver = name.IndexOf("DeliverBox", StringComparison.OrdinalIgnoreCase) >= 0;',
        "InteractableLootbox visualPrefab = GetVictoryRewardVisualLootBoxTemplate_LootAndRewards();",
        "controller.Initialize(this, main, visualPrefab, highQualityCount)",
        "VictoryRewardCrateHeroVisual.AttachToLootbox(lootbox, GetVictoryRewardVisualLootBoxTemplate_LootAndRewards());",
    ]

    for snippet in required_reward_snippets:
        if snippet not in reward_text:
            return fail("VictoryRewardShadowCrateTemplateGuard: missing reward snippet -> " + snippet)

    required_visual_snippets = [
        "public bool Initialize(",
        "InteractableLootbox visualPrefab,",
        "internal static void AttachToLootbox(InteractableLootbox lootbox, InteractableLootbox visualPrefab)",
        "GameObject sourceObject = visualPrefab != null ? visualPrefab.gameObject : lootbox.gameObject;",
    ]

    for snippet in required_visual_snippets:
        if snippet not in visual_text:
            return fail("VictoryRewardShadowCrateTemplateGuard: missing visual snippet -> " + snippet)

    print("VictoryRewardShadowCrateTemplateGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
