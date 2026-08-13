#!/usr/bin/env python3
"""
ModeGSpawnTransactionGuard — Mode G 生成事务守卫（规格 §20 第 13 条）。

不变式：
- lease/nonce 分层：SpawnAttemptLease + CommitJournalEntry DTO；
  每槽至多 MaxAttemptsPerSlot=2 次尝试；已提交槽拒绝再 lease；
- spawnLeasesInvalidated 后全部 lease fail-closed（单向故障）；
- TryCommit 顺序：先 RegisterTrackedBoss 再 ResolveSlotOnce，失败回滚登记；
- Mode G 固定 options：HoldForExternalCommit=true、ApplySharedMutators=false、
  AllowRandomRetryFallback=false（official/managed 两路均冻结）；
- 禁第二个 Health.Hurt Patch：全库 [HarmonyPatch(typeof(Health), nameof(Health.Hurt))]
  恰好 1 处；
- __state 配对：BossLethalHealthProtectionPatch Prefix ref bool __state 与
  Finalizer bool __state 成对。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRANSACTION = os.path.join(REPO_ROOT, "ModeG", "ModeGSpawnTransaction.cs")
HURT_PATCH = os.path.join(REPO_ROOT, "Patches", "Combat", "BossLethalHealthProtectionPatch.cs")
HURT_PATCH_ATTR = "[HarmonyPatch(typeof(Health), nameof(Health.Hurt))]"


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.basename(path))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def count_hurt_patches(errors):
    """全库扫描 Health.Hurt Harmony patch，必须恰好 1 处。"""
    count = 0
    for dirpath, _, filenames in os.walk(REPO_ROOT):
        if os.sep + "tests" in dirpath or dirpath.endswith("tests"):
            continue
        for f in filenames:
            if not f.endswith(".cs"):
                continue
            try:
                with open(os.path.join(dirpath, f), "r", encoding="utf-8", errors="replace") as fh:
                    if HURT_PATCH_ATTR in fh.read():
                        count += 1
            except OSError:
                continue
    return count


def main():
    errors = []
    tx = read(TRANSACTION, errors)
    patch = read(HURT_PATCH, errors)

    if tx:
        checks = [
            ("LeaseDto", r"public struct SpawnAttemptLease", "lease DTO 存在"),
            ("JournalDto", r"public struct CommitJournalEntry", "commit journal DTO 存在"),
            ("MaxAttempts", r"public const int MaxAttemptsPerSlot = 2", "每槽至多 2 次尝试"),
            ("LeaseFailClosed",
             r"if \(_state\.spawnLeasesInvalidated \|\| _committedSlots\.Contains\(slotIndex\)\)",
             "lease 单向故障/已提交槽 fail-closed"),
            ("CommitOrder",
             r"if \(!_state\.RegisterTrackedBoss\(health, character\)\) return false;"
             r"[\s\S]{0,200}?if \(!_state\.ResolveSlotOnce\(slotIndex, ModeGSlotOutcome\.Committed\)\)"
             r"[\s\S]{0,120}?_state\.UnregisterTrackedBoss\(health\);",
             "TryCommit 先登记后结案，失败回滚"),
            ("OfficialOptions",
             r"public static EnemySpawnCoreOptions CreateOfficialSpawnOptions\(\)"
             r"[\s\S]{0,400}?HoldForExternalCommit = true,"
             r"[\s\S]{0,200}?ApplySharedMutators = false,"
             r"[\s\S]{0,200}?AllowRandomRetryFallback = false,",
             "official options 三开关冻结"),
            ("ManagedOptions",
             r"internal static EnemySpawnCoreOptions CreateManagedSpawnOptions\(ManagedBossSpawnContext ctx\)",
             "managed options 构造入口存在"),
            ("ExhaustedOutcome",
             r"public bool MarkExhausted\(int slotIndex\)",
             "两次尝试耗尽结案入口"),
            ("KilledOutcome",
             r"public bool MarkKilled\(Health health\)",
             "死亡结案入口（exact Health 引用身份）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, tx):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # managed options 同样三开关
        m = re.search(r"CreateManagedSpawnOptions\(ManagedBossSpawnContext ctx\)[\s\S]{0,500}?\}", tx)
        if m:
            body = m.group(0)
            for flag in ["HoldForExternalCommit = true",
                         "ApplySharedMutators = false",
                         "AllowRandomRetryFallback = false"]:
                if flag not in body:
                    errors.append("[ManagedOptionsFlags] managed options 缺少 {}".format(flag))

    if patch:
        if "ref bool __state" not in patch:
            errors.append("[StatePairPrefix] Prefix 缺少 ref bool __state")
        if not re.search(r"Finalizer\(Exception __exception, bool __state\)", patch):
            errors.append("[StatePairFinalizer] Finalizer 缺少 bool __state 配对")

    hurt_count = count_hurt_patches(errors)
    if hurt_count != 1:
        errors.append("[SingleHurtPatch] 全库 Health.Hurt patch 必须恰好 1 处（当前 {}）".format(hurt_count))

    if errors:
        print("ModeGSpawnTransactionGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGSpawnTransactionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
