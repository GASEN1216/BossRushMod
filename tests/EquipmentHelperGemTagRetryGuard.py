"""
Guard: EquipmentHelper 的 Gem tag 查找不能把首次失败永久缓存下来，并且缺少 Gem tag 时必须 fail-closed。

如果 GameplayDataSettings.Tags 在武器配置执行时还没完全初始化，后续应允许重试；
如果当前仍然拿不到 Gem tag，则不能继续创建没有 requireTags 的普通槽位。
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/EquipmentHelper.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    block = extract_block(text, "private static Tag GetGemTag()")
    configure_block = extract_block(text, "public static void ConfigureGemSlots(Item item, int slotCount = 2)")
    if not block:
        return fail("EquipmentHelperGemTagRetryGuard: missing GetGemTag block")
    if not configure_block:
        return fail("EquipmentHelperGemTagRetryGuard: missing ConfigureGemSlots block")

    if re.search(r"_gemTagSearched\s*=\s*true\s*;", block):
        return fail("EquipmentHelperGemTagRetryGuard: GetGemTag still permanently caches a failed lookup")

    cache_success_patterns = [
        re.compile(r"_gemTagSearched\s*=\s*_cachedGemTag\s*!=\s*null\s*;"),
        re.compile(r"if\s*\(\s*_cachedGemTag\s*!=\s*null\s*\)\s*\{\s*_gemTagSearched\s*=\s*true\s*;", re.DOTALL),
    ]

    if not any(pattern.search(block) for pattern in cache_success_patterns):
        return fail("EquipmentHelperGemTagRetryGuard: missing success-only Gem tag cache semantics")

    if re.search(r"var\s+allTags\s*=\s*GameplayDataSettings\.Tags\.AllTags\s*;", block):
        return fail("EquipmentHelperGemTagRetryGuard: GetGemTag still dereferences GameplayDataSettings.Tags.AllTags without a null-safe local check")

    if "var tags = GameplayDataSettings.Tags;" not in block:
        return fail("EquipmentHelperGemTagRetryGuard: missing local GameplayDataSettings.Tags null-safe access")

    if re.search(r"Tag\s+gemTag\s*=\s*GetGemTag\s*\(\s*\)\s*;\s*if\s*\(\s*gemTag\s*==\s*null\s*\)\s*\{[\s\S]*?return\s*;", configure_block) is None:
        return fail("EquipmentHelperGemTagRetryGuard: ConfigureGemSlots does not fail closed when Gem tag is unavailable")

    print("EquipmentHelperGemTagRetryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
