#!/usr/bin/env python3
"""
ModeGPlayerLoadoutGuard — Mode G 玩家装备守卫（规格 §20 第 12 条）。

不变式：
- 不 Patch CharacterDieTask、不快照/保险玩家 Item：ModeG/ 全目录禁
  CharacterDieTask 及玩家物品快照式操作；
- 唯一物品变更是入场消耗 1 信物（TypeID 500057，TryConsumeModeEntryItem）；
- 胜利返还幂等一次：TryReturnRelicOnce + Interlocked CAS 全局闸门。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")
ENTRY = os.path.join(MODEG_DIR, "ModeGEntry.cs")
REWARD = os.path.join(MODEG_DIR, "ModeGRewardTransaction.cs")

FORBIDDEN = ["CharacterDieTask"]


def strip_comments(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []

    # 1. ModeG/ 全目录禁止快照/保险玩家 Item 相关符号
    for f in sorted(os.listdir(MODEG_DIR)):
        if not f.endswith(".cs"):
            continue
        content = strip_comments(read(os.path.join(MODEG_DIR, f)))
        for sym in FORBIDDEN:
            if sym in content:
                errors.append("[NoDieTaskPatch] ModeG/{} 引用 {}".format(f, sym))
        if re.search(r"\[HarmonyPatch", content):
            errors.append("[NoNewHarmony] ModeG/{} 新增 Harmony patch（装备隔离要求零 patch）".format(f))

    # 2. 唯一物品变更 = 入场消耗 1 信物
    if os.path.exists(ENTRY):
        entry = strip_comments(read(ENTRY))
        if not re.search(r"TryConsumeModeEntryItem\(", entry):
            errors.append("[RelicConsume] Entry 缺少信物消耗入口 TryConsumeModeEntryItem")
        if "500057" not in read(ENTRY):
            errors.append("[RelicTypeId] Entry 未绑定 TypeID 500057 信物")
    else:
        errors.append("ModeGEntry.cs 不存在")

    # 3. 胜利返还幂等一次
    if os.path.exists(REWARD):
        reward = read(REWARD)
        checks = [
            ("ReturnRelicOnce",
             r"private static void TryReturnRelicOnce\(",
             "信物返还幂等入口"),
            ("InterlockedGate",
             r"Interlocked\.Exchange\(ref _relicReturnExecuted, 1\) != 0\) return;",
             "Interlocked CAS 全局一次闸门"),
            ("VictoryOnlyReturn",
             r"battleResult != ModeGBattleResult\.Victory",
             "返还仅在 Victory 结算"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, reward):
                errors.append("[{}] 不满足: {}".format(name, desc))
    else:
        errors.append("ModeGRewardTransaction.cs 不存在")

    if errors:
        print("ModeGPlayerLoadoutGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGPlayerLoadoutGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
