"""Guard: random Boss reward lootboxes must use the shared local-inventory helper."""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/LootAndRewardsRandomBossLoot.cs")


def fail(message: str) -> int:
    print("RandomBossLootboxInventoryHelperGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8")
    body = extract_method_body(text, "private void RandomizeBossLoot_LootAndRewards(")
    if body is None:
        return fail("missing RandomizeBossLoot_LootAndRewards body")

    if "InteractableLootboxInventoryHelper.EnsureLocalInventory(lootbox" not in body:
        return fail("random Boss loot path must call InteractableLootboxInventoryHelper.EnsureLocalInventory")

    forbidden_tokens = [
        "typeof(InteractableLootbox)",
        "\"CreateLocalInventory\"",
        "\"inventoryReference\"",
    ]
    for token in forbidden_tokens:
        if token in body:
            return fail("random Boss loot path still contains local lootbox inventory reflection -> " + token)

    print("RandomBossLootboxInventoryHelperGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
