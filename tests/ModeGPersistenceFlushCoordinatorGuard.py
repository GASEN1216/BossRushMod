#!/usr/bin/env python3
"""
ModeGPersistenceFlushCoordinatorGuard — 持久化 flush 协调守卫（规格 §20 第 24 条）。

flush 协调不变式在宿敌/个人记录两个持久化类中共同实现：
- 每槽至多一个 pending flush（_pendingFlushActive bool，合并覆盖不叠加）；
- IsSaving 时只合并：Store 在 SavesSystem.IsSaving 时不触发写盘，
  FlushPendingLocked 顶部再次 IsSaving 早返（不打断官方保存）；
- 同批分别 Save + 回读核对再一次 SaveFile(false)：typed Save →
  Load 回读核对 → writeFile 时恰好一次 SaveFile(false)；
- Store 抛异常进入单向 _storeFaulted 且 Mode G 入口 fail-closed
  （RuntimeModule/DeathRouting 消费 IsStoreFaulted）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NEMESIS = os.path.join(REPO_ROOT, "ModeG", "ModeGNemesisPersistence.cs")
PROFILE = os.path.join(REPO_ROOT, "ModeG", "ModeGProfilePersistence.cs")
MODULE = os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeModule.cs")
ROUTING = os.path.join(REPO_ROOT, "ModeG", "ModeGDeathRouting.cs")


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def check_flush_invariants(text, label, errors):
    """对单个持久化类断言 flush 协调不变式。"""
    checks = [
        ("PendingFlushSlot",
         r"private static bool _pendingFlushActive;",
         "每槽至多一个 pending flush 标记"),
        ("MergeNotStack",
         r"_pending = record;[\s\S]{0,120}?_pendingFlushActive = true;",
         "Store 合并覆盖 pending（不叠加）"),
        ("IsSavingNoWrite",
         r"if \(!SavesSystem\.IsSaving\)\s*\{\s*FlushPendingLocked\(writeFile: true\);",
         "IsSaving 时只合并不写盘（Store 侧）"),
        ("FlushIsSavingGuard",
         r"private static void FlushPendingLocked\(bool writeFile\)"
         r"[\s\S]{0,300}?if \(SavesSystem\.IsSaving\) return;",
         "flush 顶部 IsSaving 早返（不打断官方保存）"),
        ("SaveThenReadback",
         r"SavesSystem\.Save<[\s\S]{0,80}?>\(StorageKey, _pending\);"
         r"[\s\S]{0,200}?readback = SavesSystem\.Load<",
         "typed Save + 回读核对"),
        ("SingleSaveFile",
         r"if \(readback != null && writeFile\)\s*\{\s*SavesSystem\.SaveFile\(false\);",
         "回读成功后恰好一次 SaveFile(false)"),
        ("CollectMergeOnly",
         r"private static void HandleCollectSaveData\(\)[\s\S]{0,200}?FlushPending\(writeFile: false\);",
         "官方收集时只合并不单独写文件"),
        ("StoreFaultOneWay",
         r"_storeFaulted = true;",
         "异常进入单向 StoreFaulted"),
    ]
    for name, pattern, desc in checks:
        if not re.search(pattern, text):
            errors.append("[{}:{}] 不满足: {}".format(label, name, desc))


def main():
    errors = []
    nemesis = read(NEMESIS, errors)
    profile = read(PROFILE, errors)
    module = read(MODULE, errors)
    routing = read(ROUTING, errors)

    if nemesis:
        check_flush_invariants(nemesis, "Nemesis", errors)
    if profile:
        check_flush_invariants(profile, "Profile", errors)

    # Mode G 入口 fail-closed：IsStoreFaulted 必须有运行时消费点
    consumers = 0
    if module and "ModeGNemesisPersistence.IsStoreFaulted" in module:
        consumers += 1
    if routing and "ModeGNemesisPersistence.IsStoreFaulted" in routing:
        consumers += 1
    if consumers == 0:
        errors.append("[EntryFailClosed] IsStoreFaulted 无 Mode G 运行时消费点（入口未 fail-closed）")

    # 幂等订阅（两文件同构）
    for text, label in ((nemesis, "Nemesis"), (profile, "Profile")):
        if text and not re.search(
                r"public static void EnsureSubscribed\(\)[\s\S]{0,200}?if \(_subscribed\) return;",
                text):
            errors.append("[{}:SubscribeIdempotent] 存档订阅非幂等".format(label))

    if errors:
        print("ModeGPersistenceFlushCoordinatorGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGPersistenceFlushCoordinatorGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
