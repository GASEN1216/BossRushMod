#!/usr/bin/env python3
"""
ModeGWeaponCompatibilityGuard — Mode G 武器计分兼容矩阵守卫（规格 §20 第 9 条）。

不变式：
- 冻结的 ModeGWeaponScoringCompatibilityMatrix 必须存在；
- 覆盖 9 种首发武器：Dragon Breath / Fen Huang Halberd / Frostmourne /
  Phantom Witch Scythe / Viper Dagger / Thunder Ring / Reverse Scale /
  DragonSet / ThunderSet；
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
REQUIRED_WEAPONS = [
    "DragonBreath", "FenHuang", "Frostmourne", "PhantomWitchScythe",
    "ViperDagger", "ThunderRing", "ReverseScale", "DragonSet", "ThunderSet",
]
SEARCH_DIRS = ["ModeG", "Integration", "Utilities", "Common"]


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
        merged = "\n".join(c for _, c in matrix_files)
        for weapon in REQUIRED_WEAPONS:
            if not re.search(weapon, merged, re.IGNORECASE):
                errors.append("[WeaponCoverage:{}] 矩阵未覆盖该武器".format(weapon))
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
