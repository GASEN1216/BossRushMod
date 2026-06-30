"""Guard: legacy boss guarantee selection should reuse scratch containers."""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/LootAndRewardsSpecialLoot.cs")


def fail(message: str) -> int:
    print("LegacyBossGuaranteeScratchGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private int GetLegacyBossGuaranteeTypeId(")
    if body is None:
        return fail("missing GetLegacyBossGuaranteeTypeId body")

    forbidden = [
        "List<int> candidateIds = new List<int>();",
        "Dictionary<int, List<int>> qualityBuckets = new Dictionary<int, List<int>>();",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("legacy guarantee still allocates scratch container -> " + snippet)

    required = [
        "legacyBossGuaranteeCandidateScratch",
        "legacyBossGuaranteeQualityBucketsScratch",
        "ClearLegacyBossGuaranteeQualityBucketsScratch();",
        "TryGetLegacyBossLootCandidates(candidateIds, qualityBuckets)",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing legacy guarantee scratch snippet -> " + snippet)

    print("LegacyBossGuaranteeScratchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
