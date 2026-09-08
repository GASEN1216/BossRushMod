#!/usr/bin/env python3
"""
ModeGWeaponCompatibilityGuard — Mode G 武器计分兼容矩阵守卫（规格 §20 第 9 条）。

不变式：
- 冻结的 ModeGWeaponScoringCompatibilityMatrix 必须存在；
- 覆盖 REQUIRED_ENTRIES 里的每一条，且稳定 key 与 TypeID 都要对得上；
- 同一稳定 key / 同一非零 TypeID 不得重复登记；
- 解析前先剥 C# 注释：注释掉的条目不算覆盖；
- revision 过期 fail-closed（矩阵绑定 verification revision）。

注：武器族分类语义（WeaponFamily Gun/Melee + ModeGDirectDamageClassifier）
已在 Utilities/ManagedBossSpawnContracts.cs 与 ModeGCombatTelemetry.cs 冻结，
但规格第 9 条要求的「计分兼容矩阵」是独立冻结结构，二者不可互相替代。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

MATRIX_SYMBOL = "ModeGWeaponScoringCompatibilityMatrix"

# 稳定 key -> 期望 TypeID（0 = 套装/图腾能力 key，无 TypeID）。
# TypeID 一并钉死：只查名字的话，把条目写成 ("FrostSpear", 500999) 也能过。
REQUIRED_ENTRIES = {
    "DragonBreath": 500005,
    "FenHuangHalberd": 500034,
    "Frostmourne": 500041,
    "PhantomWitchScythe": 500044,
    "ReverseScale": 500013,
    # P0 五把新武器 2026-09-07 开放获取（Boss 掉落 + 叮当商店）
    "ViperDagger": 500048,
    "SummonStaff": 500049,
    "EnergyShield": 500050,
    "FrostSpear": 500051,
    "ThunderRing": 500052,
    # 套装被动：无自己的 TypeID
    "DragonSet": 0,
    "ThunderSet": 0,
    "FrostSet": 0,
}
SEARCH_DIRS = ["ModeG", "Integration", "Utilities", "Common"]

# 逐条抓 new ModeGWeaponScoringEntry("<stableKey>", <typeId>,
ENTRY_PATTERN = re.compile(
    r"new\s+ModeGWeaponScoringEntry\s*\(\s*\"([A-Za-z0-9_]+)\"\s*,\s*(\d+)")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cs_source_util import clean_source  # noqa: E402

# 每条目必须绑定 RequiredVerificationRevision 常量，而不是写死的字符串。
# 写死一个过期 revision 会让 Mode G 正式入口在运行时 fail-closed，而只查
# 「文件里出现过 revision 这个词」的断言完全看不出来。
ENTRY_REVISION_PATTERN = re.compile(
    r"new\s+ModeGWeaponScoringEntry\s*\((?:[^()]|\([^()]*\))*?\)", re.S)


def iter_cs_files():
    for d in SEARCH_DIRS:
        root = os.path.join(REPO_ROOT, d)
        if not os.path.isdir(root):
            continue
        for dirpath, _, filenames in os.walk(root):
            for f in filenames:
                if f.endswith(".cs"):
                    yield os.path.join(dirpath, f)


def main():
    errors = []

    matrix_files = []
    for path in iter_cs_files():
        try:
            with open(path, "r", encoding="utf-8", errors="replace") as fh:
                content = fh.read()
        except OSError:
            continue
        if MATRIX_SYMBOL in content:
            matrix_files.append((path, content))

    if not matrix_files:
        errors.append(
            "[MatrixMissing] 规格 §20 第 9 条要求冻结 {}，"
            "全库未找到该符号（实现缺失，不放宽断言）".format(MATRIX_SYMBOL))
    else:
        merged = "\n".join(clean_source(c) for _, c in matrix_files)

        for entry_text in ENTRY_REVISION_PATTERN.findall(merged):
            if "RequiredVerificationRevision" not in entry_text:
                key = ENTRY_PATTERN.search(entry_text)
                errors.append("[EntryRevision:{}] 条目未绑定 RequiredVerificationRevision".format(
                    key.group(1) if key else "?"))

        # 解析真正的条目，而不是在整份文件文本里做子串匹配。
        # 旧写法是 re.search(weapon, merged, IGNORECASE)：把条目改名成 FrostSpearX 仍然命中，
        # 名字只出现在注释里也算通过，等于没有断言。现在既剥注释、又精确比对 key 与 TypeID。
        pairs = ENTRY_PATTERN.findall(merged)
        if not pairs:
            errors.append(
                "[MatrixEntriesUnparsed] 找到矩阵符号但解析不出任何 "
                "new ModeGWeaponScoringEntry(\"key\", typeId) 条目")

        # 先查同名重复：直接 dict() 会让后一条覆盖前一条，重复 TypeID 检查跟着漏判
        entries = {}
        for key, type_id in pairs:
            if key in entries:
                errors.append("[DuplicateStableKey:{}] 同一稳定 key 登记了多次".format(key))
                continue
            entries[key] = type_id

        for expected_key, expected_type_id in sorted(REQUIRED_ENTRIES.items()):
            if expected_key not in entries:
                errors.append("[WeaponCoverage:{}] 矩阵未覆盖该条目".format(expected_key))
                continue
            actual = entries[expected_key]
            if actual != str(expected_type_id):
                errors.append("[TypeIdMismatch:{}] 期望 TypeID {}，实际 {}".format(
                    expected_key, expected_type_id, actual))

        # TypeID 不得重复登记（0 是套装/图腾能力 key 的占位，可多条）
        seen = {}
        for key, type_id in entries.items():
            if type_id == "0":
                continue
            if type_id in seen:
                errors.append("[DuplicateTypeId:{}] {} 与 {} 重复登记".format(
                    type_id, seen[type_id], key))
            else:
                seen[type_id] = key

        if not re.search(r"[Rr]evision", merged):
            errors.append("[MatrixRevision] 矩阵未绑定 verification revision（过期 fail-closed）")

    if errors:
        print("ModeGWeaponCompatibilityGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGWeaponCompatibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
