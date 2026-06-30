"""Guard: random Boss loot generation should reuse scratch collections."""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/LootAndRewardsRandomBossLoot.cs")


def fail(message: str) -> int:
    print("RandomBossLootScratchReuseGuard: FAIL - " + message)
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

    forbidden = [
        "List<int> candidateIds = new List<int>();",
        "Dictionary<int, int> itemQualities = new Dictionary<int, int>();",
        "int[] highQualityCounts = new int[4];",
        "int[] lowQualityCountsByGrade = new int[4];",
        "float[] perHighWeightByQuality = new float[4];",
        "float[] highQualityRatios = new float[]",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("random Boss loot path still allocates scratch data -> " + snippet)

    required = [
        "bossRandomLootCandidateIdScratch",
        "bossRandomLootQualityScratch",
        "bossRandomLootHighQualityCountsScratch",
        "bossRandomLootLowQualityCountsScratch",
        "bossRandomLootHighWeightsScratch",
        "candidateIds.Clear();",
        "itemQualities.Clear();",
        "Array.Clear(highQualityCounts, 0, highQualityCounts.Length);",
        "Array.Clear(lowQualityCountsByGrade, 0, lowQualityCountsByGrade.Length);",
        "Array.Clear(perHighWeightByQuality, 0, perHighWeightByQuality.Length);",
        "GetBossLootHighQualityRatio(",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing scratch reuse snippet -> " + snippet)

    print("RandomBossLootScratchReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
