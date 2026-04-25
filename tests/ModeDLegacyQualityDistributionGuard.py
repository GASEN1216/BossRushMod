"""
Guard: Mode D legacy 品质分布接入不得回退到旧的全池随机补物逻辑。

要求：
- ModeDGlobalLoot.cs 提供一个“按品质区间选全局物品”的帮助方法
- ModeDGlobalLoot.cs 提供一个“对选中物品应用特殊降权规则（皇冠重抽）”的方法
- CreateRandomGlobalItemForModeD(int minQ, int maxQ, float enemyHealth) 不得再直接
  `return CreateRandomGlobalItemForModeD(minQ, maxQ);`
- legacy 重载和基础重载都应显式经过特殊降权规则，避免 legacy 路径绕过皇冠降权
"""

from pathlib import Path
import re
import sys


SOURCE = Path("ModeD/ModeDGlobalLoot.cs")


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
    text = SOURCE.read_text(encoding="utf-8")

    if "TryPickRandomModeDGlobalItemIdInRange" not in text:
        return fail(
            "ModeDLegacyQualityDistributionGuard: missing TryPickRandomModeDGlobalItemIdInRange helper"
        )

    if "ApplyModeDGlobalItemSpecialSelectionRules" not in text:
        return fail(
            "ModeDLegacyQualityDistributionGuard: missing ApplyModeDGlobalItemSpecialSelectionRules helper"
        )

    legacy_body = extract_method_body(
        text,
        "internal Item CreateRandomGlobalItemForModeD(int minQ, int maxQ, float enemyHealth)",
    )
    if legacy_body is None:
        return fail(
            "ModeDLegacyQualityDistributionGuard: could not locate legacy overload body"
        )

    if "return CreateRandomGlobalItemForModeD(minQ, maxQ);" in legacy_body:
        return fail(
            "ModeDLegacyQualityDistributionGuard: legacy overload still falls back to old full-pool random method"
        )

    if "ApplyModeDGlobalItemSpecialSelectionRules" not in legacy_body:
        return fail(
            "ModeDLegacyQualityDistributionGuard: legacy overload does not apply special item selection rules"
        )

    base_body = extract_method_body(
        text,
        "internal Item CreateRandomGlobalItemForModeD(int minQ, int maxQ)",
    )
    if base_body is None:
        return fail(
            "ModeDLegacyQualityDistributionGuard: could not locate base overload body"
        )

    if "ApplyModeDGlobalItemSpecialSelectionRules" not in base_body:
        return fail(
            "ModeDLegacyQualityDistributionGuard: base overload does not apply special item selection rules"
        )

    print("ModeDLegacyQualityDistributionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
