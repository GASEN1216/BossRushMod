"""
Guard: 噬魂挽歌 EnsureMeleeAttackFx 必须保持原有 FX 回退行为。

重构契约：
    * AllowSlashFxFallback 必须为 true —— 保持旧实现中 slashFx 为空时的 fallback
    * AllowHitFxFallback 必须为 true —— hitFx 命中反馈不能丢
    * EnsureMeleeAttackFx 方法体通过 FxPolicy.ApplyTo 统一入口完成

兼容模式：
- 新模式：使用 MeleeWeaponFxPolicy，检查 allowSlashFxFallback: true 和 FxPolicy.ApplyTo
- 旧模式（向后兼容）：直接在方法体内赋值 slashFx 和 hitFx
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheWeaponConfig.cs")


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


def check_new_pattern(text: str, block: str) -> bool:
    """
    检查新模式：MeleeWeaponFxPolicy 策略声明 + ApplyTo 调用
    要求：
      1. 文件中声明了 FxPolicy 且 allowSlashFxFallback: true
      2. EnsureMeleeAttackFx 方法体中调用了 FxPolicy.ApplyTo
      3. FxPolicy 声明中包含 allowHitFxFallback: true
    """
    # 检查 FxPolicy 声明包含 allowSlashFxFallback: true
    has_slash_fx_true = bool(re.search(
        r"MeleeWeaponFxPolicy\s*\(\s*allowSlashFxFallback\s*:\s*true",
        text
    ))

    # 检查 FxPolicy 声明包含 allowHitFxFallback: true
    has_hit_fx_true = bool(re.search(
        r"allowHitFxFallback\s*:\s*true",
        text
    ))

    # 检查方法体中调用了 FxPolicy.ApplyTo
    has_apply_to = bool(re.search(
        r"FxPolicy\s*\.\s*ApplyTo\s*\(",
        block
    ))

    return has_slash_fx_true and has_hit_fx_true and has_apply_to


def check_old_pattern(block: str) -> bool:
    """
    检查旧模式（向后兼容）：方法体内直接赋值 slashFx 和 hitFx
    """
    has_slash = bool(re.search(r"meleeAgent\.slashFx\s*=\s*GetFallbackSlashFx\s*\(", block))
    has_hit = bool(re.search(r"meleeAgent\.hitFx\s*=\s*GetFallbackHitFx\s*\(", block))
    return has_slash and has_hit


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    block = extract_block(text, "internal static void EnsureMeleeAttackFx(ItemAgent_MeleeWeapon meleeAgent)")
    if not block:
        return fail("PhantomWitchScytheSlashFxGuard: missing EnsureMeleeAttackFx block")

    # 优先检查新模式（MeleeWeaponFxPolicy）
    if check_new_pattern(text, block):
        print("PhantomWitchScytheSlashFxGuard: PASS (MeleeWeaponFxPolicy 模式，allowSlashFxFallback=true)")
        return 0

    # 向后兼容：检查旧模式
    if check_old_pattern(block):
        print("PhantomWitchScytheSlashFxGuard: PASS (旧模式，直接赋值)")
        return 0

    return fail(
        "PhantomWitchScytheSlashFxGuard: FAIL\n"
        "  噬魂挽歌 EnsureMeleeAttackFx 既未使用 MeleeWeaponFxPolicy(allowSlashFxFallback: true)\n"
        "  也未包含直接的 slashFx/hitFx fallback 赋值。\n"
        "  契约要求：allowSlashFxFallback 与 allowHitFxFallback 必须为 true，以保持原行为。"
    )


if __name__ == "__main__":
    sys.exit(main())
