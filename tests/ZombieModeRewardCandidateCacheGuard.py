"""ZombieModeRewardCandidateCacheGuard: 丧尸奖励随机物品应缓存 Search 结果。"""

from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")


def fail(message: str) -> int:
    print(message)
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
    text = ENTRY.read_text(encoding="utf-8")

    for snippet in [
        "zombieModeRewardCandidateCache",
        "BuildZombieModeRewardCandidateCacheKey(",
        "GetZombieModeRewardCandidateIds(",
        "zombieModeRewardSafeCandidateScratch",
    ]:
        if snippet not in text:
            return fail("ZombieModeRewardCandidateCacheGuard: missing cache snippet -> " + snippet)

    random_body = extract_method_body(text, "private int FindRandomItemTypeByTags(")
    if random_body is None:
        return fail("ZombieModeRewardCandidateCacheGuard: missing FindRandomItemTypeByTags")

    if "GetZombieModeRewardCandidateIds(" not in random_body:
        return fail("ZombieModeRewardCandidateCacheGuard: random item selection must use cached candidates")

    if "ItemAssetsCollection.Search(filter)" in random_body:
        return fail("ZombieModeRewardCandidateCacheGuard: random item selection still searches directly")

    cache_body = extract_method_body(text, "private int[] GetZombieModeRewardCandidateIds(")
    if cache_body is None:
        return fail("ZombieModeRewardCandidateCacheGuard: missing candidate cache helper")

    if "ItemAssetsCollection.Search(filter)" not in cache_body:
        return fail("ZombieModeRewardCandidateCacheGuard: candidate cache helper should own the single Search path")

    print("ZombieModeRewardCandidateCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
