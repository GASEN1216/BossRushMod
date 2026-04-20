"""
Rollback guard：确保 Boss 战利品候选池的任务标签过滤不会被 legacy 开关绑架。

BuildGeneralLootExcludeTags / MergeGeneralLootExcludeTags / BuildGeneralBossLootCandidateIdSet
三个方法不应再接受 `excludeQuestTag` 参数——任务物品必须始终被排除，与
useLegacyBossLootProbabilities 的取值无关。

该守卫以正则判定签名形状，允许空白/缩进变化，但拒绝任何形式的"把 quest 排除
与 legacy 开关联动"的回归写法。
"""

from pathlib import Path
import re
import sys


SOURCE = Path("LootAndRewards/LootAndRewards.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    # 方法签名不得再出现 excludeQuestTag 参数
    forbidden_signatures = [
        re.compile(
            r"BuildGeneralLootExcludeTags\s*\([^)]*excludeQuestTag",
            re.DOTALL,
        ),
        re.compile(
            r"MergeGeneralLootExcludeTags\s*\([^)]*excludeQuestTag",
            re.DOTALL,
        ),
        re.compile(
            r"BuildGeneralBossLootCandidateIdSet\s*\([^)]*excludeQuestTag",
            re.DOTALL,
        ),
    ]

    for pattern in forbidden_signatures:
        match = pattern.search(text)
        if match is not None:
            return fail(
                "Legacy rollback regression: 方法签名重新引入 excludeQuestTag 参数，"
                "这会让任务物品过滤与 useLegacyBossLootProbabilities 开关绑定 | "
                "matched: " + match.group(0)
            )

    # 调用点也不得把 useLegacyProbabilities 透传给这三个方法
    forbidden_call_patterns = [
        re.compile(
            r"BuildGeneralLootExcludeTags\s*\([^)]*useLegacyProbabilities",
            re.DOTALL,
        ),
        re.compile(
            r"MergeGeneralLootExcludeTags\s*\([^)]*useLegacyProbabilities",
            re.DOTALL,
        ),
        re.compile(
            r"BuildGeneralBossLootCandidateIdSet\s*\(\s*(?:false|useLegacyProbabilities)",
            re.DOTALL,
        ),
    ]

    for pattern in forbidden_call_patterns:
        match = pattern.search(text)
        if match is not None:
            return fail(
                "Legacy rollback regression: 标准 Boss 掉落候选池的调用点把 quest "
                "过滤与 legacy 概率开关联动 | matched: " + match.group(0)
            )

    print("LegacyBossLootRollbackGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
