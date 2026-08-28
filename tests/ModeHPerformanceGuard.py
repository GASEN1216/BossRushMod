#!/usr/bin/env python3
"""
ModeHPerformanceGuard — Mode H 性能守卫（设计提案 §24.2、§26.1）。

不变式：
- 每帧热路径（Tick/Update/Reassert/postfix 快门）里没有全量扫描：
  不得出现 FindObjectsOfType / GetComponentsInChildren / Resources.FindObjectsOfTypeAll
  / GetAllMapConfigs / ObjectCache 全表枚举；
- 每帧热路径里不得 new 集合（List/Dictionary/HashSet）或做 LINQ；
- spawn 与 UI 预算常量存在且被引用：
  MaxSpawnPerFrame / MaxConcurrentEnemyInstances / MaxConcurrentFighterInstances /
  HudRefreshIntervalSeconds / CommandReassertIntervalSeconds /
  StandInRepathIntervalSeconds / BattleSnapshotIntervalSeconds / WarmupTargetSeconds；
- HUD 刷新受 HudRefreshIntervalSeconds 节流；
- 战场快照不在每帧路径写盘（间隔由 BattleSnapshotIntervalSeconds 控制）；
- 看台解冻 postfix 是零分配快路径。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
CONFIG = os.path.join(MODEH_DIR, "ModeHConfig.cs")

REQUIRED_BUDGETS = [
    ("MaxSpawnPerFrame", "每帧生成上限"),
    ("MaxConcurrentEnemyInstances", "同时在场敌人上限"),
    ("MaxConcurrentFighterInstances", "同时在场选手上限"),
    ("HudRefreshIntervalSeconds", "HUD 刷新节流"),
    ("CommandReassertIntervalSeconds", "口令重申间隔"),
    ("StandInRepathIntervalSeconds", "看台重设间隔"),
    ("BattleSnapshotIntervalSeconds", "快照采集间隔"),
    ("WarmupTargetSeconds", "warmup 预算"),
]

# 每帧热路径方法：这些方法体内不得出现全量扫描或分配
HOT_PATH_PATTERNS = [
    r"public void Tick\(float deltaTime[\s\S]*?\n        \}",
    r"public bool Tick\(float deltaTime[\s\S]*?\n        \}",
    r"public void Reassert\([\s\S]*?\n        \}",
    r"private void TickErrorSwap\([\s\S]*?\n        \}",
    r"internal static bool ShouldUnfreeze\([\s\S]*?\n        \}",
]

FORBIDDEN_SCANS = [
    "FindObjectsOfType",
    "FindObjectOfType",
    "Resources.FindObjectsOfTypeAll",
    "GetComponentsInChildren",
    "GetAllMapConfigs",
    "GetCharacterPresets",
    "GetFilteredEnemyPresets",
]

FORBIDDEN_ALLOCATIONS = [
    r"new List<",
    r"new Dictionary<",
    r"new HashSet<",
    r"\.ToArray\(\)",
    r"\.ToList\(\)",
    r"\.Where\(",
    r"\.Select\(",
    r"\.OrderBy\(",
]


def check_config(errors):
    config = read_text(CONFIG)
    if config is None:
        errors.append("[File] 缺少 ModeH/ModeHConfig.cs")
        return
    for name, desc in REQUIRED_BUDGETS:
        if not re.search(r"public const \w+ {} = ".format(name), config):
            errors.append("[Budget] 缺少预算常量: {}（{}）".format(name, desc))


def check_hot_paths(errors):
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        code = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")

        # 每帧热路径不得做全量扫描
        for pattern in HOT_PATH_PATTERNS:
            for match in re.finditer(pattern, code):
                body = match.group(0)
                for forbidden in FORBIDDEN_SCANS:
                    if forbidden in body:
                        errors.append(
                            "[HotPath] {} 的每帧路径出现全量扫描: {}".format(name, forbidden))
                for forbidden in FORBIDDEN_ALLOCATIONS:
                    if re.search(forbidden, body):
                        errors.append(
                            "[HotPath] {} 的每帧路径出现分配/LINQ: {}".format(
                                name, forbidden.replace("\\", "")))

        # Mode H 不得在 Unity Update 里直接做重活（宿主 host 才有 tick 回调）
        if re.search(r"private void Update\(\)", code):
            errors.append("[HotPath] {} 不得自建 Update；tick 只走宿主回调".format(name))


def check_throttles(errors):
    ui = read_text(os.path.join(MODEH_DIR, "ModeHUI.cs"))
    if ui is None:
        errors.append("[File] 缺少 ModeH/ModeHUI.cs")
    else:
        code = strip_cs_comments(ui)
        if "ModeHConfig.HudRefreshIntervalSeconds" not in code:
            errors.append("[Throttle] HUD 刷新必须受 HudRefreshIntervalSeconds 节流")
        if not re.search(r"_lastTimerSeconds|valueChanged", code):
            errors.append("[Throttle] HUD 必须只在值变化时重写文本")

    snapshot = read_text(os.path.join(MODEH_DIR, "ModeHBattleSnapshot.cs"))
    if snapshot is None:
        errors.append("[File] 缺少 ModeH/ModeHBattleSnapshot.cs")
    else:
        code = strip_cs_comments(snapshot)
        if "ModeHConfig.BattleSnapshotIntervalSeconds" not in code:
            errors.append("[Throttle] 快照采集必须受 BattleSnapshotIntervalSeconds 控制")
        if "SavesSystem" in code or "SaveFile" in code:
            errors.append("[Throttle] 快照采集不得直接写盘")

    performer = read_text(os.path.join(MODEH_DIR, "ModeHStandInPerformer.cs"))
    if performer is not None:
        code = strip_cs_comments(performer)
        if "ModeHConfig.StandInRepathIntervalSeconds" not in code:
            errors.append("[Throttle] 看台表演必须受 StandInRepathIntervalSeconds 控制")

    spawn = read_text(os.path.join(MODEH_DIR, "ModeHSpawnTransaction.cs"))
    if spawn is not None:
        code = strip_cs_comments(spawn)
        for name in ["MaxSpawnPerFrame", "MaxConcurrentEnemyInstances"]:
            if "ModeHConfig." + name not in code:
                errors.append("[Throttle] 生成事务必须引用预算常量: " + name)


def main():
    errors = []
    check_config(errors)
    check_hot_paths(errors)
    check_throttles(errors)

    if errors:
        print("ModeHPerformanceGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHPerformanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
