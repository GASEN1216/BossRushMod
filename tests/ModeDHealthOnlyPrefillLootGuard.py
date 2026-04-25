"""
Guard: 敌人背包预填物的 BossRush 风格概率必须改为 health-only 模型。

要求：
- ModeDEquipment.cs 提供一个按 enemyHealth 计算 bonusFactor 的 helper
- 该 helper 必须使用 minBossBaseHealth / maxBossBaseHealth 作为主参考区间
- TryCreateBossRushStyleInventoryLootItemForSharedModes 必须使用 enemyHealth，而不是 qualityLevel
"""

from pathlib import Path
import sys


SOURCE = Path("ModeD/ModeDEquipment.cs")


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

    helper_body = extract_method_body(
        text,
        "private float ComputeModeDStyleEnemyLootBonusFactorFromHealth(float enemyHealth)",
    )
    if helper_body is None:
        return fail("ModeDHealthOnlyPrefillLootGuard: missing health-only bonus-factor helper")

    required_helper_snippets = [
        "minBossBaseHealth",
        "maxBossBaseHealth",
        "Mathf.InverseLerp",
    ]
    for snippet in required_helper_snippets:
        if snippet not in helper_body:
            return fail("ModeDHealthOnlyPrefillLootGuard: health-only helper missing snippet -> " + snippet)

    picker_body = extract_method_body(
        text,
        "private bool TryCreateBossRushStyleInventoryLootItemForSharedModes(float enemyHealth, out Item item)",
    )
    if picker_body is None:
        return fail("ModeDHealthOnlyPrefillLootGuard: missing enemyHealth-based shared picker")

    if "ComputeModeDStyleEnemyLootBonusFactorFromHealth(enemyHealth)" not in picker_body:
        return fail("ModeDHealthOnlyPrefillLootGuard: shared picker does not derive probability from enemyHealth")

    forbidden_picker_snippets = [
        "ComputeModeDStyleEnemyLootBonusFactor(",
        "qualityLevel",
    ]
    for snippet in forbidden_picker_snippets:
        if snippet in picker_body:
            return fail("ModeDHealthOnlyPrefillLootGuard: shared picker still depends on old quality-level path -> " + snippet)

    print("ModeDHealthOnlyPrefillLootGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
