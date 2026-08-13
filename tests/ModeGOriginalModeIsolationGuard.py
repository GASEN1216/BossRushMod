#!/usr/bin/env python3
"""
ModeGOriginalModeIsolationGuard — Mode G 原模式隔离守卫（规格 §20 第 11 条）。

不变式：
- ModeGInteractable 继承 InteractableBase，且 timeout/确认路径不引用
  ConfigureBossRushMode/StartFirstWave（不调标准推进）；
- ModeG/ 全目录不引用标准推进与 Mode D 裸装路径符号；
- TryStartModeG 不调用 IsAnyBossRushLikeModeActive（含 arena 判定会死锁），
  分别拒绝 IsActive / ModeD|E|F / ZombieMode / IsModeGEntryBlocked；
- 双层 IsModeGEntryBlocked：Legacy 三交互 IsInteractable + WavesArena 最终入口 +
  ZombieMode 最终入口均消费；
- IsAnyBossRushLikeModeActive 只纳入 IsModeGRunInProgress（绝不纳入 quarantine）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")

FORBIDDEN_PROGRESSION = ["ConfigureBossRushMode", "StartFirstWave"]


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def strip_comments(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def main():
    errors = []

    # 1. ModeGInteractable 独立交互硬契约
    interactable = os.path.join(MODEG_DIR, "ModeGInteractable.cs")
    if not os.path.exists(interactable):
        errors.append("ModeGInteractable.cs 不存在")
    else:
        content = strip_comments(read(interactable))
        if not re.search(r"class ModeGInteractable\s*:\s*InteractableBase", content):
            errors.append("[InteractableBase] ModeGInteractable 必须继承 InteractableBase")
        for sym in FORBIDDEN_PROGRESSION:
            if sym in content:
                errors.append("[NoStandardProgression] ModeGInteractable 引用 {}".format(sym))

    # 2. ModeG/ 全目录不引用标准推进符号
    for f in sorted(os.listdir(MODEG_DIR)):
        if not f.endswith(".cs"):
            continue
        content = strip_comments(read(os.path.join(MODEG_DIR, f)))
        for sym in FORBIDDEN_PROGRESSION:
            if sym in content:
                errors.append("[ModeGDirIsolation] ModeG/{} 引用 {}".format(f, sym))

    # 3. TryStartModeG 分别拒绝、不走聚合判定
    entry_path = os.path.join(MODEG_DIR, "ModeGEntry.cs")
    if os.path.exists(entry_path):
        entry = read(entry_path)
        # 排除注释后的代码体
        code = re.sub(r"//[^\n]*", "", entry)
        if re.search(r"IsAnyBossRushLikeModeActive\(\)", code):
            # 注释已剥离；任何代码调用均违规
            errors.append("[NoAggregateGate] TryStartModeG 不得调用 IsAnyBossRushLikeModeActive()")
        for pattern, label in [
                (r"if \(IsActive\)", "拒绝 Legacy BossRush IsActive"),
                (r"if \(modeDActive \|\| modeEActive \|\| modeFActive\)", "拒绝 Mode D/E/F"),
                (r"if \(IsZombieModeActive\)", "拒绝 ZombieMode"),
                (r"if \(ModeGRuntimeGates\.IsModeGEntryBlocked\)", "拒绝 IsModeGEntryBlocked")]:
            if not re.search(pattern, code):
                errors.append("[SeparateReject] TryStartModeG 缺少 {}".format(label))

    # 4. 双层 IsModeGEntryBlocked 消费点
    consumers = {
        "Interactables/BossRushInteractables.cs": r"IsModeGEntryBlockedSafe\(\)",
        "WavesArena/WavesArenaEntryAndTeleport.cs": r"ModeGRuntimeGates\.IsModeGEntryBlocked",
        "ZombieMode/ZombieModeEntry.cs": r"ModeGRuntimeGates\.IsModeGEntryBlocked",
    }
    for rel, pattern in consumers.items():
        path = os.path.join(REPO_ROOT, rel.replace("/", os.sep))
        if not os.path.exists(path):
            errors.append("[ConsumerMissing] {}".format(rel))
            continue
        if not re.search(pattern, read(path)):
            errors.append("[DoubleLayerGate] {} 未消费 IsModeGEntryBlocked".format(rel))

    # 5. IsAnyBossRushLikeModeActive 只纳入 RunInProgress，绝不纳入 quarantine
    zombie_entry = os.path.join(REPO_ROOT, "ZombieMode", "ZombieModeEntry.cs")
    if os.path.exists(zombie_entry):
        content = read(zombie_entry)
        if "IsModeGGlobalQuarantineActive" in content:
            errors.append("[QuarantineNotAggregated] IsAnyBossRushLikeModeActive 链路不得消费 quarantine")
        if not re.search(r"IsModeGRunInProgressSafe\(\)", content):
            errors.append("[AggregateRunOnly] IsAnyBossRushLikeModeActive 未纳入 IsModeGRunInProgress")

    if errors:
        print("ModeGOriginalModeIsolationGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGOriginalModeIsolationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
