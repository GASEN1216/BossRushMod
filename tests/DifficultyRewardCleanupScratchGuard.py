"""Guard: difficulty reward cleanup should reuse scratch lists and avoid unused buckets."""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/LootAndRewardsSpecialLoot.cs")
FIELDS_SOURCE = Path("LootAndRewards/LootAndRewards.cs")


def fail(message: str) -> int:
    print("DifficultyRewardCleanupScratchGuard: FAIL - " + message)
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
    fields_text = FIELDS_SOURCE.read_text(encoding="utf-8")
    body = extract_method_body(text, "private IEnumerator CleanupDifficultyRewardLootboxInventory_LootAndRewards(")
    if body is None:
        return fail("missing CleanupDifficultyRewardLootboxInventory_LootAndRewards body")

    forbidden = [
        "List<Item> preferred = new List<Item>();",
        "List<Item> fallbackHighQuality = new List<Item>();",
        "List<Item> fallbackOthers = new List<Item>();",
        "List<Item> keep = new List<Item>(target);",
        "fallbackOthers.Add(item);",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("cleanup still allocates or fills unused scratch data -> " + snippet)

    required = [
        "difficultyRewardPreferredScratch",
        "difficultyRewardFallbackHighQualityScratch",
        "difficultyRewardKeepScratch",
        "preferred.Clear();",
        "fallbackHighQuality.Clear();",
        "keep.Clear();",
        "ClearDifficultyRewardCleanupScratch();",
    ]
    combined = text + "\n" + fields_text
    for snippet in required:
        if snippet not in combined:
            return fail("missing reusable cleanup scratch snippet -> " + snippet)

    print("DifficultyRewardCleanupScratchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
