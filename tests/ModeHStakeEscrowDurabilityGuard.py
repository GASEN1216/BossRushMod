#!/usr/bin/env python3
"""ModeHStakeEscrowDurabilityGuard — 真实押品「不得只存在于内存」与「结算必须能续做」。

两条不变式都对应过玩家真实仓库装备永久丢失的 P0：

1. **托管物必须有持久归宿**。`_escrowItems` 是纯内存 static List，`LoadPersisted` 与
   `ResetStaticCaches` 都会清空它。因此凡是「放不回仓库」的分支都必须把物品交给官方溢出
   缓冲区（`PlayerStorage.Push(item, toBufferDirectly:true)`），而不是 `return false` 保持
   pending；清空 `_escrowItems` 之前也必须先排空。三个滞留点是
   ReturnEscrowItems / GrantPlannedRewards / RollbackDetached。

2. **已冻结 settlementKind 的阶段必须能续做**。`ResultCommitted` /
   `AbortReturnCommitted` / `SettlementPending` 在冻结表里没有通向 `AbortReturnCommitted`
   的出边，`TryAbortReturn` 若对它们仍走 CommitAbortReturn 就必然
   `journal_illegal_transition`——押品退不回来，且非终态 journal 会经
   RecomputeSlotConsistency 把七个旧模式入口一起锁死。
   同理 `CommitResult` / `CommitAbortReturn` 的字段写入必须随 TryAdvancePhase 一起回滚，
   否则 commit_result_already_committed 会让任何重试永久失败。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JOURNAL = os.path.join(ROOT, "ModeH", "ModeHWarehouseStakeJournal.cs")
BUFFER = os.path.join(ROOT, "ModeH", "ModeHWarehouseStakeJournalStorageBuffer.cs")
SERVICE = os.path.join(ROOT, "ModeH", "ModeHRealStakeService.cs")


def read(path):
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8", errors="ignore") as fh:
        return fh.read()


def body_of(source, signature_regex):
    """截取方法体：从签名到第一处 8 空格缩进的右花括号。"""
    m = re.search(signature_regex + r"[\s\S]*?\n        \}", source)
    return m.group(0) if m else None


def main():
    errors = []

    journal = read(JOURNAL)
    buffer_src = read(BUFFER)
    service = read(SERVICE)
    if journal is None or buffer_src is None or service is None:
        print("ModeHStakeEscrowDurabilityGuard: FAIL 缺少源文件")
        return 1

    combined = journal + buffer_src

    # --- 不变式 1：三个滞留点都要有缓冲区出口 ---
    if "PlayerStorage.Push(item, true)" not in buffer_src:
        errors.append("[Escrow] 缺少官方溢出缓冲区出口 PlayerStorage.Push(item, true)")
    for helper in ("TryHandOffToStorageBuffer", "DrainEscrowToStorageBuffer", "FlushStorageBuffer"):
        if helper not in combined:
            errors.append("[Escrow] 缺少 " + helper)

    for sig, label in (
        (r"public static bool ReturnEscrowItems\(", "ReturnEscrowItems"),
        (r"public static bool GrantPlannedRewards\(", "GrantPlannedRewards"),
        (r"private static void RollbackDetached\(", "RollbackDetached"),
    ):
        body = body_of(combined, sig)
        if body is None:
            errors.append("[Escrow] 找不到 " + label + " 方法体")
            continue
        if "TryHandOffToStorageBuffer" not in body:
            errors.append(
                "[Escrow] " + label + " 放不回仓库时必须交溢出缓冲区，不得只保持 pending")

    # 清空内存表之前必须排空
    for sig, label in (
        (r"public static void LoadPersisted\(", "LoadPersisted"),
        (r"public static void ResetStaticCaches\(", "ResetStaticCaches"),
    ):
        body = body_of(combined, sig)
        if body is None:
            errors.append("[Escrow] 找不到 " + label + " 方法体")
            continue
        if "_escrowItems.Clear()" in body and "DrainEscrowToStorageBuffer" not in body:
            errors.append(
                "[Escrow] " + label + " 清空 _escrowItems 前必须先 DrainEscrowToStorageBuffer")

    # 缓冲区写入后必须落盘，且要门控 Awake 是否跑过
    if "PlayerStorageBuffer.SaveBuffer()" not in buffer_src:
        errors.append("[Escrow] 缓冲区写入后必须 SaveBuffer，否则中途退出即丢失")
    if "PlayerStorageBuffer.Instance == null" not in buffer_src:
        errors.append("[Escrow] 必须门控 PlayerStorageBuffer.Instance（Awake 里 LoadBuffer 会先清表）")

    # --- 不变式 2：续做分支 ---
    abort = body_of(service, r"internal static bool TryAbortReturn\(")
    if abort is None:
        errors.append("[Settle] 找不到 TryAbortReturn 方法体")
    else:
        for phase in ("ResultCommitted", "AbortReturnCommitted", "SettlementPending"):
            if phase not in abort:
                errors.append("[Settle] TryAbortReturn 必须显式处理 " + phase + "（冻结表无返还出边）")
        if "TryCompleteFrozenSettlement" not in abort:
            errors.append("[Settle] TryAbortReturn 必须经 TryCompleteFrozenSettlement 续做")
        if "ManualIntervention" not in abort:
            errors.append("[Settle] TryAbortReturn 必须给 ManualIntervention 留物理返还出路")

    if "private static bool TryCompleteFrozenSettlement(" not in service:
        errors.append("[Settle] 缺少 TryCompleteFrozenSettlement")
    settle_body = body_of(service, r"internal static bool TrySettleMatch\(")
    if settle_body is not None and "TryCompleteFrozenSettlement" not in settle_body:
        errors.append("[Settle] TrySettleMatch 缺少重入保护（已 committed 时必须续做而非重提交）")

    # Commit* 字段必须随阶段推进一起回滚
    for sig, token, label in (
        (r"public static bool CommitResult\(", "_active.resultToken = null;", "CommitResult"),
        (r"public static bool CommitAbortReturn\(", "_active.abortReturnToken = null;", "CommitAbortReturn"),
    ):
        body = body_of(combined, sig)
        if body is None:
            errors.append("[Settle] 找不到 " + label + " 方法体")
            continue
        if token not in body:
            errors.append(
                "[Settle] " + label + " 在 TryAdvancePhase 失败时必须回滚已写字段，"
                "否则 already_committed 早退会让重试永久失败")

    if errors:
        print("ModeHStakeEscrowDurabilityGuard: FAIL ({0} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHStakeEscrowDurabilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
