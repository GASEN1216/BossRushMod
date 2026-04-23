"""
Guard: 幽灵女巫额外掉落前必须兜底确保噬魂挽歌 prefab 已加载。

Reason:
- 运行时日志已经证明某些会话里 `phantom_scythe` bundle 根本没被 EquipmentFactory 自动加载
- 此时 100% 掉落也会因为 `InstantiateSync(500044)` 前没有 prefab 而静默失败
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheBootstrap.cs")


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

    if "EnsureScytheRewardPrefabLoaded" not in text:
        return fail("PhantomWitchScytheRewardBundleGuard: missing EnsureScytheRewardPrefabLoaded helper")

    helper_block = extract_block(text, "private static bool EnsureScytheRewardPrefabLoaded()")
    if not helper_block:
        return fail("PhantomWitchScytheRewardBundleGuard: missing EnsureScytheRewardPrefabLoaded block")

    if 'EquipmentFactory.LoadBundle("phantom_scythe")' not in helper_block:
        return fail("PhantomWitchScytheRewardBundleGuard: helper must attempt EquipmentFactory.LoadBundle(\"phantom_scythe\")")

    add_block = extract_block(text, "private static bool TryAddScytheToInventory(Inventory inventory, string logPrefix)")
    if not add_block:
        return fail("PhantomWitchScytheRewardBundleGuard: missing TryAddScytheToInventory block")

    if "EnsureScytheRewardPrefabLoaded()" not in add_block:
        return fail("PhantomWitchScytheRewardBundleGuard: TryAddScytheToInventory must ensure prefab is loaded before InstantiateSync")

    print("PhantomWitchScytheRewardBundleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
