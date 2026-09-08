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

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[1]


# 三把近战武器的 Config 文件路径
WEAPON_CONFIGS = {
    "焚煌戟": Path("Integration/DragonKing/Weapons/FenHuangHalberdWeaponConfig.cs"),
    "霜之哀伤": Path("Integration/Frostmourne/FrostmourneWeaponConfig.cs"),
    "噬魂挽歌": Path("Integration/PhantomWitch/PhantomWitchScytheWeaponConfig.cs"),
    # 毒蛇匕首 / 冰霜长矛 / 召唤法杖三把共用一处 ApplyTo，不各写一份，
    # 因此这里登记的是那个共享文件而不是三个 XxxWeaponConfig。
    "新武器近战（共享）": Path("Integration/NewWeapons/Common/NewWeaponMeleeFx.cs"),
}

# 策略使用的正则匹配（MeleeWeaponFxPolicy.ApplyTo 或 FxPolicy.ApplyTo）
POLICY_USAGE_PATTERN = re.compile(
    r"(MeleeWeaponFxPolicy|FxPolicy)\s*\.\s*ApplyTo\s*\("
)

# copy-paste 模板检测：EnsureMeleeAttackFx 方法体内直接赋值 slashFx 或 hitFx。
# 不绑定形参名：原来写死 `meleeAgent.slashFx`，把形参改名就让整条检查失效。
DIRECT_SLASH_FX_ASSIGN = re.compile(
    r"\.\s*slashFx\s*=\s*"
)
DIRECT_HIT_FX_ASSIGN = re.compile(
    r"\.\s*hitFx\s*=\s*"
)


def find_ensure_block(text: str) -> str:
    """取 EnsureMeleeAttackFx 的方法体。按方法名定位，不绑形参名或形参类型。"""
    match = re.search(r"EnsureMeleeAttackFx\s*\([^)]*\)", text)
    if not match:
        return ""
    return extract_block(text, text[match.start():match.end()])


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
    path = REPO_ROOT / path
    if not path.exists():
        return (False, f"[FAIL] {name}: 文件不存在 ({path})")

    # 剥注释：原实现直接读原文，于是把 ApplyTo 用 // 或 /* */ 注释掉守卫照样绿
    text = clean_source(path.read_text(encoding="utf-8"))

    # 检查 1: 是否使用了 MeleeWeaponFxPolicy.ApplyTo
    has_policy_usage = bool(POLICY_USAGE_PATTERN.search(text))

    # 检查 2: 是否存在 copy-paste 模板（EnsureMeleeAttackFx 方法体内直接赋值）
    has_copy_paste = False
    ensure_block = find_ensure_block(text)
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
    print("MeleeWeaponFxPolicyUsageGuard: 开始检查 {} 处近战 FX 配置...".format(len(WEAPON_CONFIGS)))
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
