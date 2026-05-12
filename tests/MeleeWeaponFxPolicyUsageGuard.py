"""
Guard: 三把近战武器的 Config 文件必须使用 MeleeWeaponFxPolicy.ApplyTo，
且禁止新增 EnsureMeleeAttackFx 式的 copy-paste 模板。

检查对象：
  - Integration/DragonKing/Weapons/FenHuangHalberdWeaponConfig.cs（焚煌戟）
  - Integration/Frostmourne/FrostmourneWeaponConfig.cs（霜之哀伤）
  - Integration/PhantomWitch/PhantomWitchScytheWeaponConfig.cs（噬魂挽歌）

通过条件（每把武器）：
  1. 文件中包含 MeleeWeaponFxPolicy.ApplyTo 或 FxPolicy.ApplyTo（策略调用）
  2. 文件中不包含多行 EnsureMeleeAttackFx 方法体内直接的 slashFx = / hitFx = 赋值
     （即禁止 copy-paste 模板）

注意：在武器尚未迁移到 MeleeWeaponFxPolicy 之前（Tasks 5.5-5.7），
本 guard 预期会 FAIL。迁移完成后应全部 PASS。
"""

from pathlib import Path
import re
import sys


# 三把近战武器的 Config 文件路径
WEAPON_CONFIGS = {
    "焚煌戟": Path("Integration/DragonKing/Weapons/FenHuangHalberdWeaponConfig.cs"),
    "霜之哀伤": Path("Integration/Frostmourne/FrostmourneWeaponConfig.cs"),
    "噬魂挽歌": Path("Integration/PhantomWitch/PhantomWitchScytheWeaponConfig.cs"),
}

# 策略使用的正则匹配（MeleeWeaponFxPolicy.ApplyTo 或 FxPolicy.ApplyTo）
POLICY_USAGE_PATTERN = re.compile(
    r"(MeleeWeaponFxPolicy|FxPolicy)\s*\.\s*ApplyTo\s*\("
)

# copy-paste 模板检测：EnsureMeleeAttackFx 方法体内直接赋值 slashFx 或 hitFx
# 匹配形如 meleeAgent.slashFx = ... 或 meleeAgent.hitFx = ... 的直接赋值
DIRECT_SLASH_FX_ASSIGN = re.compile(
    r"meleeAgent\s*\.\s*slashFx\s*=\s*"
)
DIRECT_HIT_FX_ASSIGN = re.compile(
    r"meleeAgent\s*\.\s*hitFx\s*=\s*"
)


def extract_block(text: str, signature: str) -> str:
    """提取方法体（从签名到匹配的闭合大括号）"""
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


def check_weapon(name: str, path: Path) -> tuple:
    """
    检查单把武器的 Config 文件。
    返回 (passed: bool, message: str)
    """
    if not path.exists():
        return (False, f"[FAIL] {name}: 文件不存在 ({path})")

    text = path.read_text(encoding="utf-8")

    # 检查 1: 是否使用了 MeleeWeaponFxPolicy.ApplyTo
    has_policy_usage = bool(POLICY_USAGE_PATTERN.search(text))

    # 检查 2: 是否存在 copy-paste 模板（EnsureMeleeAttackFx 方法体内直接赋值）
    has_copy_paste = False
    ensure_block = extract_block(
        text,
        "EnsureMeleeAttackFx(ItemAgent_MeleeWeapon meleeAgent)"
    )
    if ensure_block:
        # 在方法体内检查是否有直接的 slashFx = 或 hitFx = 赋值
        if DIRECT_SLASH_FX_ASSIGN.search(ensure_block) or DIRECT_HIT_FX_ASSIGN.search(ensure_block):
            has_copy_paste = True

    # 判定结果
    if has_policy_usage and not has_copy_paste:
        return (True, f"[PASS] {name}: 已使用 MeleeWeaponFxPolicy.ApplyTo，无 copy-paste 模板")
    elif not has_policy_usage and has_copy_paste:
        return (False, f"[FAIL] {name}: 未使用 MeleeWeaponFxPolicy.ApplyTo，仍存在 copy-paste 模板")
    elif not has_policy_usage and not has_copy_paste:
        return (False, f"[FAIL] {name}: 未使用 MeleeWeaponFxPolicy.ApplyTo")
    else:
        # has_policy_usage and has_copy_paste — 迁移不完整
        return (False, f"[FAIL] {name}: 已引入 MeleeWeaponFxPolicy.ApplyTo 但仍残留 copy-paste 模板")


def main() -> int:
    print("MeleeWeaponFxPolicyUsageGuard: 开始检查三把近战武器...")
    all_passed = True
    results = []

    for name, path in WEAPON_CONFIGS.items():
        passed, message = check_weapon(name, path)
        results.append(message)
        if not passed:
            all_passed = False

    for msg in results:
        print(f"  {msg}")

    if all_passed:
        print("MeleeWeaponFxPolicyUsageGuard: PASS（全部武器已迁移到 MeleeWeaponFxPolicy）")
        return 0
    else:
        print("MeleeWeaponFxPolicyUsageGuard: FAIL（存在未迁移的武器）")
        return 1


if __name__ == "__main__":
    sys.exit(main())
