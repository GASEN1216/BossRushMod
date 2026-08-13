#!/usr/bin/env python3
"""
ModeGNemesisPersistenceGuard — 宿敌持久化守卫（规格 §20 第 22 条）。

不变式：
- v1 key 冻结：BossRush_ModeG_NemesisRecord_v1；
- DTO 字段冻结（schemaVersion 保持默认 0、墓碑 tombstone）；
- rank clamp = max(旧+1, current)，上限 MaxRank=3，不允许降级；
- KeyExisits 前置分类（先判存在再 Load）；
- SuspendedPersistentV1 挂起不写盘（仅内存；flush 路径不得触碰 _suspended）；
- OnSaveDeleted 重置全部内存状态（幂等）；
- Store/flush 异常进入单向 _storeFaulted 且 Store 对其 fail-closed。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NEMESIS = os.path.join(REPO_ROOT, "ModeG", "ModeGNemesisPersistence.cs")


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def strip_comments(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def main():
    errors = []
    text = read(NEMESIS, errors)
    if not text:
        print("ModeGNemesisPersistenceGuard: FAIL (1 errors)")
        return 1

    checks = [
        ("StorageKey",
         r'public const string StorageKey = "BossRush_ModeG_NemesisRecord_v1";',
         "v1 存档 key 冻结"),
        ("MaxRank", r"public const int MaxRank = 3;", "Rank 上限 R3"),
        ("DtoFields",
         r"public sealed class NemesisRecordDto[\s\S]*?public int schemaVersion;"
         r"[\s\S]*?public string bossPresetKey;"
         r"[\s\S]*?public int rank;"
         r"[\s\S]*?public int temperamentId;"
         r"[\s\S]*?public int defeatsByPlayer;"
         r"[\s\S]*?public int defeatsOfPlayer;"
         r"[\s\S]*?public long lastUpdatedTicks;"
         r"[\s\S]*?public ulong originRunId;"
         r"[\s\S]*?public bool tombstone;",
         "DTO 字段集冻结（schemaVersion 无初始化器）"),
        ("TombstoneWrite",
         r"public static bool MarkTombstone\(\)[\s\S]{0,300}?copy\.tombstone = true;",
         "墓碑写入入口"),
        ("TombstoneGate",
         r"return dto != null && !dto\.tombstone && !string\.IsNullOrEmpty\(dto\.bossPresetKey\);",
         "墓碑宿敌不出场"),
        ("RankClamp",
         r"int next = Math\.Max\(oldRank \+ 1, currentRank\);"
         r"[\s\S]{0,120}?if \(next > MaxRank\) next = MaxRank;",
         "rank clamp = max(旧+1, current) 且上限 R3"),
        ("KeyExisitsFirst",
         r"if \(SavesSystem\.KeyExisits\(StorageKey\)\)"
         r"[\s\S]{0,200}?SavesSystem\.Load<NemesisRecordDto>\(StorageKey\);",
         "KeyExisits 前置分类再 Load"),
        ("SchemaMismatchSuspend",
         r'_suspended = new SuspendedPersistentV1[\s\S]{0,200}?reason = "schema_mismatch",',
         "schema 不匹配 → 内存挂起"),
        ("SaveDeletedReset",
         r"private static void HandleSaveDeleted\(\)[\s\S]{0,400}?_cache = null;"
         r"[\s\S]{0,200}?_pending = null;"
         r"[\s\S]{0,200}?_pendingFlushActive = false;"
         r"[\s\S]{0,200}?_suspended = null;",
         "OnSaveDeleted 重置全部内存状态"),
        ("StoreFailClosed",
         r"if \(_storeFaulted\) return false; // fail-closed",
         "Store 对 StoreFaulted fail-closed"),
        ("StoreFaultOneWay",
         r"catch \(Exception e\)\s*\{\s*_storeFaulted = true; // 单向，不可恢复",
         "Store 异常单向进入 StoreFaulted"),
        ("FlushFaultOneWay",
         r"宿敌 flush 异常，进入 StoreFaulted",
         "flush 异常同样单向 StoreFaulted"),
        ("TypedSave",
         r"SavesSystem\.Save<NemesisRecordDto>\(StorageKey, _pending\);",
         "原生 typed Save（非裸 PlayerPrefs）"),
    ]
    for name, pattern, desc in checks:
        if not re.search(pattern, text):
            errors.append("[{}] 不满足: {}".format(name, desc))

    # SuspendedPersistentV1 挂起不写盘：flush 路径不得触碰 _suspended
    m = re.search(r"private static void FlushPendingLocked\(bool writeFile\)[\s\S]*?\n        \}", text)
    if m:
        if "_suspended" in m.group(0):
            errors.append("[SuspendNoWrite] flush 路径触碰 _suspended（挂起记录不得写盘）")
    else:
        errors.append("[FlushMethod] FlushPendingLocked 方法未找到")

    # DTO 禁字段初始化器
    dto_m = re.search(r"public sealed class NemesisRecordDto\s*\{([\s\S]*?)\n        \}", text)
    if dto_m:
        body = strip_comments(dto_m.group(1))
        if re.search(r"public\s+\w[\w<>\[\]]*\s+\w+\s*=", body):
            errors.append("[DtoNoInitializer] NemesisRecordDto 含字段初始化器")

    if errors:
        print("ModeGNemesisPersistenceGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGNemesisPersistenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
