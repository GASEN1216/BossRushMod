#!/usr/bin/env python3
"""
ModeGCompileListGuard — Mode G 编译清单守卫（规格 §20 第 1 条）。

不变式：
- ModeG/ 目录下所有 .cs 文件必须登记进 compile_official.bat（动态扫描，
  新增文件未登记即 FAIL）；
- Utilities\\ManagedBossSpawnContracts.cs 与三个托管 Boss adapter 必须登记。
"""
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")
COMPILE_BAT = os.path.join(REPO_ROOT, "compile_official.bat")

EXTRA_REQUIRED = [
    "Utilities\\ManagedBossSpawnContracts.cs",
    "Integration\\DragonDescendant\\DragonDescendantBoss_ModeGAdapter.cs",
    "Integration\\DragonKing\\DragonKingBoss_ModeGAdapter.cs",
    "Integration\\PhantomWitch\\PhantomWitchBoss_ModeGAdapter.cs",
]


def main():
    errors = []

    if not os.path.isdir(MODEG_DIR):
        errors.append("ModeG/ 目录不存在")
    if not os.path.exists(COMPILE_BAT):
        errors.append("compile_official.bat 不存在")

    if errors:
        print("ModeGCompileListGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    with open(COMPILE_BAT, "r", encoding="utf-8", errors="replace") as fh:
        bat = fh.read()

    modeg_files = sorted(f for f in os.listdir(MODEG_DIR) if f.endswith(".cs"))
    if len(modeg_files) < 22:
        errors.append("ModeG/ 下 .cs 文件少于 22 个（当前 {}）".format(len(modeg_files)))

    required = ["ModeG\\" + f for f in modeg_files] + EXTRA_REQUIRED
    for entry in required:
        if entry not in bat:
            errors.append("compile_official.bat 缺失登记: " + entry)

    if errors:
        print("ModeGCompileListGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGCompileListGuard: PASS")
    print("  ModeG .cs: {} 个，附加登记: {} 个".format(len(modeg_files), len(EXTRA_REQUIRED)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
