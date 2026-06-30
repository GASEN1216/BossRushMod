"""Guard: random Boss reward loot generation must cache LootBoxLoader reflection metadata."""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/LootAndRewardsRandomBossLoot.cs")


def fail(message: str) -> int:
    print("RandomBossLootReflectionCacheGuard: FAIL - " + message)
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

    required = [
        "BossLootBoxLoaderReflection.RandomCountField",
        "BossLootBoxLoaderReflection.QualitiesField",
        "BossLootBoxLoaderReflection.TagsField",
        "BossLootBoxLoaderReflection.ExcludeTagsField",
        "BossLootBoxLoaderReflection.RandomPoolField",
        "BossLootBoxLoaderReflection.FixedItemsField",
        "BossLootBoxLoaderReflection.FixedChanceField",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing cached reflection use -> " + snippet)

    forbidden = [
        "loaderType.GetField(",
        "loaderType.GetNestedType(",
        "randomPoolType.GetField(",
        "randomPoolType.GetNestedType(",
        "loaderEntryType.GetField(",
        "randomContainerEntryType.GetField(",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("hot path still resolves reflection metadata -> " + snippet)

    print("RandomBossLootReflectionCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
