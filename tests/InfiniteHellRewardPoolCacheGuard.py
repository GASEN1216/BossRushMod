"""Guard: Infinite Hell high-quality reward pool should reuse cache/scratch data."""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/LootAndRewards.cs")
FIELDS_SOURCE = Path("LootAndRewards/LootAndRewards.cs")
MODULE_SOURCE = Path("WavesArena/WavesArenaRuntimeModule_RewardPool.cs")


def fail(message: str) -> int:
    print("InfiniteHellRewardPoolCacheGuard: FAIL - " + message)
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
    module_text = MODULE_SOURCE.read_text(encoding="utf-8")
    bridge = extract_method_body(text, "private int GetRandomInfiniteHellHighQualityRewardTypeID()")
    if bridge is None or "return wavesArenaRuntime.GetRandomInfiniteHellHighQualityRewardTypeID();" not in bridge:
        return fail("host reward picker must forward to arena runtime")
    body = extract_method_body(module_text, "internal int GetRandomInfiniteHellHighQualityRewardTypeID()")
    if body is None:
        return fail("missing GetRandomInfiniteHellHighQualityRewardTypeID body")

    forbidden = [
        "List<Duckov.Utilities.Tag> includeTags = new List<Duckov.Utilities.Tag>();",
        "HashSet<int> idSet = new HashSet<int>();",
        "List<int> preferred = new List<int>();",
        "List<int> fallbackHighQuality = new List<int>();",
        "Item temp = ItemAssetsCollection.InstantiateSync(candidateId);",
        "infiniteHellHighQualityItemPool = pool;",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("reward pool still builds transient data synchronously -> " + snippet)

    combined = module_text + "\n" + fields_text
    required = [
        "InfiniteHellHighQualityCandidateIdScratch",
        "InfiniteHellHighQualityPreferredScratch",
        "InfiniteHellHighQualityFallbackScratch",
        "owner.BuildGeneralBossLootCandidateIdSet(InfiniteHellHighQualityCandidateIdScratch)",
        "TryGetInfiniteHellRewardCandidateValueQuality(",
        "InfiniteHellHighQualityItemPool.AddRange(",
        "ClearInfiniteHellHighQualityRewardScratch();",
    ]
    for snippet in required:
        if snippet not in combined:
            return fail("missing cache-backed reward pool snippet -> " + snippet)

    print("InfiniteHellRewardPoolCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
