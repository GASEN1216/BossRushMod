"""Guard: Boss lootbox path tracking refresh should reuse scratch lists."""

from pathlib import Path
import sys


SOURCE = Path("LootAndRewards/LootAndRewards.cs")


def fail(message: str) -> int:
    print("BossLootboxPathTrackingScratchGuard: FAIL - " + message)
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
    body = extract_method_body(text, "internal void RefreshBossRushLootboxPathTrackingForTrackedBosses()")
    if body is None:
        return fail("missing RefreshBossRushLootboxPathTrackingForTrackedBosses body")

    forbidden = [
        "new List<CharacterMainControl>(bossSpawnTimes.Keys)",
        "new List<CharacterMainControl>()",
        "List<CharacterMainControl> staleBosses = null",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("refresh path still allocates transient boss lists -> " + snippet)

    required = [
        "bossRushLootboxPathTrackedBossScratch",
        "bossRushLootboxPathStaleBossScratch",
        "bossRushLootboxPathTrackedBossScratch.Add(boss)",
        "bossRushLootboxPathStaleBossScratch.Add(boss)",
        "bossRushLootboxPathTrackedBossScratch.Clear();",
        "bossRushLootboxPathStaleBossScratch.Clear();",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing scratch-list reuse snippet -> " + snippet)

    print("BossLootboxPathTrackingScratchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
