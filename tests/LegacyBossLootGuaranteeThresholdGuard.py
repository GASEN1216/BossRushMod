"""
Threshold guard：确保"原版概率模式下的 Q6+ 保底"逻辑保持受 Boss 最大生命值门槛保护。

保底追加逻辑必须：
- 仅在 useLegacyBossLootProbabilities 为 true 且 bossMaxHealth > LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH 时触发
- 保底阈值常量 LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH 保持 250f
- OnBossBeforeSpawnLoot_LootAndRewards 调用 RandomizeBossLoot 时把 maxHealth / legacy 开关透传进去
- AddBossSpecialLootToLootboxCoroutine 的签名包含 useLegacyBossLootProbabilities 与 bossMaxHealth

该守卫以正则判定，允许空白/换行变化；不允许"保底无视 Boss 最大生命值"的回归写法。
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

    required_patterns = [
        (
            "LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH 常量声明（250f）",
            re.compile(
                r"private\s+const\s+float\s+LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH\s*=\s*250f\s*;"
            ),
        ),
        (
            "RandomizeBossLoot 调用点必须把 useLegacyProbabilities 与 maxHealth 一起透传",
            re.compile(
                r"useLegacyBossLootProbabilities\s*,\s*\n[^\n]*legacyBonusFactor\s*,\s*\n[^\n]*maxHealth\s*,",
                re.DOTALL,
            ),
        ),
        (
            "AddBossSpecialLootToLootboxCoroutine 的签名必须包含 legacy 开关与 bossMaxHealth",
            re.compile(
                r"bool\s+useLegacyBossLootProbabilities\s*,\s*\n\s*float\s+bossMaxHealth\s*,",
                re.DOTALL,
            ),
        ),
        (
            "保底追加必须受 bossMaxHealth > LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH 门槛保护",
            re.compile(
                r"useLegacyBossLootProbabilities\s*&&\s*bossMaxHealth\s*>\s*LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH"
            ),
        ),
    ]

    missing = []
    for description, pattern in required_patterns:
        if pattern.search(text) is None:
            missing.append(description)

    if missing:
        return fail(
            "Legacy guarantee threshold regression: missing required wiring | "
            + " | ".join(missing)
        )

    # 保底不能无条件触发——即"if (useLegacyBossLootProbabilities)"紧跟 TryAddLegacyBossGuaranteeItem
    # 而不先检查 bossMaxHealth 门槛，视为回归。
    forbidden_unguarded_pattern = re.compile(
        r"if\s*\(\s*useLegacyBossLootProbabilities\s*\)\s*\{\s*(?:DevLog[^\n]*\n\s*)?TryAddLegacyBossGuaranteeItem",
        re.DOTALL,
    )

    match = forbidden_unguarded_pattern.search(text)
    if match is not None:
        return fail(
            "Legacy guarantee threshold regression: 保底追加跳过了 Boss 最大生命值门槛 | "
            + match.group(0)
        )

    print("LegacyBossLootGuaranteeThresholdGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
