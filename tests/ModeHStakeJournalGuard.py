#!/usr/bin/env python3
"""
ModeHStakeJournalGuard — Mode H 真实押品 journal 守卫（设计提案 §22、§26.1）。

不变式：
- 阶段严格单向（§22.2 表），`phase` 是唯一终态来源，不得另设 terminal 布尔；
- `CancelledTerminal` 只允许从已证明没有 escrow 移除的分支进入；
- `Terminal` 只用于 MatchResult，`RefundedTerminal` 只用于 AbortReturn，
  settlementKind 进入 committed phase 后不可改写、不可在两种结算间切换；
- reward / abort-return 父 operation 与子 receipt 双向回指，
  `eventTokenId` 非空且逐项一致；父状态与 journal phase 同屏障推进；
- `ModeHWarehouseStakeJournal` 是唯一仓库写入者：
  `ModeHRewardTransaction` 不得引用 Inventory / PlayerStorage / ItemAssetsCollection；
- `AddAt` 前必须确认目标格为空、调用后读回核对，占用时不得覆盖；
- 禁止 Courier/Deposit 的“先删源再回调”，禁止把 InstanceID / TypeID / LockIndex
  当作所有权证明；
- `IsSlotConsistent` 是只读派生结果，四条取值规则齐全；
- 全仓不得出现 modeHRealWarehouseStakeEnabled /
  IsModeHRealWarehouseStakeConfiguredEnabled / GatePassed 三个符号。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import contains_symbol, read_modeh_group, read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
JOURNAL = os.path.join(MODEH_DIR, "ModeHWarehouseStakeJournal.cs")
BRIDGE = os.path.join(MODEH_DIR, "ModeHInventoryPersistenceBridge.cs")
NORMALIZER = os.path.join(MODEH_DIR, "ModeHItemTreeNormalizer.cs")
TRANSACTION = os.path.join(MODEH_DIR, "ModeHRewardTransaction.cs")

# §22.2 冻结的单向转换表
LEGAL_TRANSITIONS = {
    "None": ["Prepared"],
    "Prepared": ["EscrowSnapshotDurable", "CancelledTerminal"],
    "EscrowSnapshotDurable": ["EscrowRemovedDurable", "CancelledTerminal"],
    "EscrowRemovedDurable": ["MatchLocked", "AbortReturnCommitted"],
    "MatchLocked": ["ResultCommitted", "AbortReturnCommitted"],
    "ResultCommitted": ["SettlementPending"],
    "AbortReturnCommitted": ["SettlementPending"],
    "SettlementPending": ["Terminal", "RefundedTerminal"],
}

FORBIDDEN_GATE_SYMBOLS = [
    "modeHRealWarehouseStakeEnabled",
    "IsModeHRealWarehouseStakeConfiguredEnabled",
    "GatePassed",
]


def check_phase_machine(errors):
    source = read_text(JOURNAL)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHWarehouseStakeJournal.cs")
        return
    code = strip_cs_comments(source)

    transition = re.search(
        r"public static bool IsLegalTransition\([\s\S]*?\n        \}", code)
    if not transition:
        errors.append("[Phase] 缺少集中的 IsLegalTransition 转换表")
    else:
        body = transition.group(0)
        for src, targets in LEGAL_TRANSITIONS.items():
            case = "case ModeHStakePhase.{}:".format(src)
            if case not in body:
                errors.append("[Phase] 转换表缺少来源阶段: " + src)
                continue
            for target in targets:
                if "ModeHStakePhase.{}".format(target) not in body:
                    errors.append("[Phase] {} 缺少合法目标: {}".format(src, target))
        # 终态不得再向外转换（除人工介入的显式排除）
        if "return !IsTerminalPhase(from);" not in body:
            errors.append("[Phase] 人工介入必须只从非终态进入")

    checks = [
        (r"public static bool IsTerminalPhase\(ModeHStakePhase phase\)", "终态判定集中实现"),
        (r"public static bool TryAdvancePhase\(", "唯一阶段推进入口"),
        (r"_active\.phaseSequence = _active\.phaseSequence \+ 1;", "每次推进递增 phaseSequence"),
        (r'failureReasonId = "journal_settlement_kind_drift";', "settlementKind 不可漂移"),
        (r'failureReasonId = "journal_terminal_requires_match_result";',
         "Terminal 只用于 MatchResult"),
        (r'failureReasonId = "journal_refunded_requires_abort_return";',
         "RefundedTerminal 只用于 AbortReturn"),
        (r"public static void EnterManualIntervention\(string reasonId\)", "人工介入统一出口"),
        (r"public static bool TryCancelWithoutRemoval\(", "取消仅限未移除分支"),
        (r'failureReasonId = "cancel_requires_no_removal_phase";', "取消阶段限制"),
        (r'failureReasonId = "cancel_escrow_still_held";', "仍持有 escrow 不得取消"),
        (r"public static bool CommitResult\(", "结果提交入口"),
        (r"public static bool CommitAbortReturn\(", "退款提交入口"),
        (r"public static bool EnterSettlementPending\(out string failureReasonId\)",
         "SettlementPending 与父状态同屏障"),
        (r"public static bool Settle\(out string failureReasonId\)", "终结入口"),
        (r"private static bool ValidateOperationBackReferences\(", "父子回指校验"),
        (r"private static bool AllReceiptsVerified\(out string failureReasonId\)", "逐项 receipt 校验"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Journal] 不满足: " + desc)

    # 不得另设可漂移的 terminal 布尔
    if re.search(r"\bbool\s+_?terminal\b", code) or re.search(r"public\s+bool\s+IsTerminal\s*;", code):
        errors.append("[Journal] phase 是唯一终态来源，不得另设 terminal 布尔")

    # 终结前必须先把父 operation 标 Settled
    settle = re.search(r"public static bool Settle\(out string failureReasonId\)[\s\S]*?\n        \}", code)
    if settle:
        body = settle.group(0)
        for kind, status, terminal in [
            ("MatchResult", "ModeHRewardOperationStatus.Settled", "ModeHStakePhase.Terminal"),
            ("AbortReturn", "ModeHAbortReturnOperationStatus.Settled", "ModeHStakePhase.RefundedTerminal"),
        ]:
            si = body.find(status)
            ti = body.find(terminal)
            if si < 0 or ti < 0 or si > ti:
                errors.append(
                    "[Journal] {} 必须先把父 operation 标 Settled，journal 才可写终态".format(kind))
        if "AllReceiptsVerified" not in body:
            errors.append("[Journal] 终结前必须逐项校验 receipt")

    # 只读派生的槽位一致性
    consistency = re.search(
        r"public static void RecomputeSlotConsistency\([\s\S]*?\n        \}", code)
    if not consistency:
        errors.append("[Slot] 缺少 RecomputeSlotConsistency")
    else:
        body = consistency.group(0)
        for required, desc in [
            ("slot_storage_unavailable", "无法判定时置 false"),
            ("slot_manual_intervention", "人工介入时置 false"),
            ("slot_active_journal", "存在非终态 journal 时置 false"),
            ("IsTerminalPhase(phase)", "终态时置 true"),
        ]:
            if required not in body:
                errors.append("[Slot] 取值规则缺失: " + desc)
    if re.search(r"public static bool IsSlotConsistent\s*\{\s*get", code) is None:
        errors.append("[Slot] IsSlotConsistent 必须是只读派生结果")
    if re.search(r"public static void SetSlotConsistent", code):
        errors.append("[Slot] IsSlotConsistent 不得提供公开写入口")


def check_single_writer(errors):
    """唯一仓库写入者：只有 journal 可以经 bridge 写库存。"""
    transaction = read_text(TRANSACTION)
    if transaction is None:
        errors.append("[File] 缺少 ModeH/ModeHRewardTransaction.cs")
    else:
        code = strip_cs_comments(transaction)
        for forbidden in ["Inventory", "PlayerStorage", "ItemAssetsCollection",
                          "ModeHInventoryPersistenceBridge", "ItemTreeData"]:
            if contains_symbol(code, forbidden):
                errors.append("[Writer] 结果计划不得直接触碰库存: " + forbidden)
        if not re.search(r"public static ModeHRewardOperationDto BuildMatchResultPlan\(", code):
            errors.append("[Writer] 缺少比赛结果计划构造")
        if not re.search(r"public static ModeHAbortReturnOperationDto BuildAbortReturnPlan\(", code):
            errors.append("[Writer] 缺少技术中止返还计划构造")
        # 真实押品件数按原始整数倍率，不套用净赔率
        if re.search(r"1 \+ lockedOdds", code):
            errors.append("[Writer] 真实押品件数必须按原始整数倍率，不得套用 1+odds")

    # 全仓只有 journal 与 bridge 可以引用 PlayerStorage
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        if name in ("ModeHWarehouseStakeJournal.cs", "ModeHInventoryPersistenceBridge.cs",
                    "ModeHEntry.cs"):
            continue
        text = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        if contains_symbol(text, "PlayerStorage"):
            errors.append("[Writer] {} 不得引用 PlayerStorage".format(name))


def check_bridge(errors):
    source = read_text(BRIDGE)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHInventoryPersistenceBridge.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"public static bool IsStorageReady\(out string failureReasonId\)", "等待 PlayerStorage 初始化"),
        (r"PlayerStorage\.Instance\.HasInitialized\(\)", "读取官方初始化判定"),
        (r"public static bool TryAddAtEmpty\(", "AddAt 必须先确认空位"),
        (r'failureReasonId = "add_position_occupied";', "占用时不得覆盖"),
        (r'failureReasonId = "add_readback_mismatch";', "AddAt 后读回核对"),
        (r"public static int FindConfirmedEmptyPosition\(\)", "已确认空位查询"),
        (r"public static bool TryComputeInventoryDigest\(", "pre/post image 唯一来源"),
        (r"public static int CountOccurrences\(string semanticTreeDigest\)", "出现次数核对"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Bridge] 不满足: " + desc)

    # 禁止 Courier/Deposit 的先删源再回调
    for forbidden in ["Courier", "Deposit", "SendToPlayerStorage", "IncomingItemBuffer",
                      "PlayerStorage.Push", "TakeBufferItem"]:
        if contains_symbol(code, forbidden):
            errors.append("[Bridge] 禁止使用先删源再回调的流程: " + forbidden)


def check_identity(errors):
    source = read_text(NORMALIZER)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHItemTreeNormalizer.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"public static ModeHItemTreeSnapshotDto TryCapture\(", "树快照入口"),
        (r"public static bool TryWriteNormalizedPayload\(", "规范化文本"),
        (r"localIds\[instanceId\] = localIds\.Count;", "instance id 重映射为局部序号"),
        (r"public static bool Matches\(", "摘要 + 出现次数双重比对"),
        (r'failureReasonId = "tree_occurrence_mismatch";', "出现次数不符即拒绝"),
        (r"public static bool IsSameGameQuality\(int a, int b\)", "同品质只认 gameQuality 相等"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Identity] 不满足: " + desc)

    # 所有权证明禁令
    for forbidden in ["GetInstanceID", "LockIndex"]:
        if contains_symbol(code, forbidden):
            errors.append("[Identity] 不得把 {} 当作所有权证明".format(forbidden))
    # 输出中不得出现 instanceID 字面字段
    if re.search(r'AddProperty\("instanceID"', code) or re.search(r'AddProperty\("instanceId"', code):
        errors.append("[Identity] 规范化输出不得包含 instance id")


def check_no_gate_symbols(errors):
    """
    §22.1：三个被禁符号不得作为代码出现。全仓扫描由 ModeHConfigApiGuard 负责，
    这里只在 ModeH/ 内做一次就近复核（注释里说明禁令是允许的，因此先剥注释）。
    """
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        code = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        for forbidden in FORBIDDEN_GATE_SYMBOLS:
            if forbidden in code:
                errors.append("[Gate] {} 出现了被禁符号: {}".format(name, forbidden))


def check_dtos(errors):
    model = read_modeh_group("ModeHStateModel.cs", "ModeHStateDtos.cs")
    if model is None:
        errors.append("[File] 缺少 Mode H 状态模型")
        return
    code = strip_cs_comments(model)

    journal = re.search(r"public sealed class ModeHStakeJournalDto[\s\S]*?\n    \}", code)
    if not journal:
        errors.append("[DTO] 未找到 ModeHStakeJournalDto")
    else:
        body = journal.group(0)
        for field in ["txId", "slotId", "slotGeneration", "runId", "matchIndex", "phase",
                      "phaseSequence", "settlementKind", "inventoryPreDigest",
                      "inventoryPostDigest", "escrowItems", "lossItems", "rewardItems",
                      "rewardOperation", "abortReturnOperation", "resultToken",
                      "abortReturnToken", "receipts"]:
            if not re.search(r"\b{}\s*;".format(field), body):
                errors.append("[DTO] ModeHStakeJournalDto 缺少字段: " + field)

    receipt = re.search(r"public sealed class ModeHStakeReceiptDto[\s\S]*?\n    \}", code)
    if receipt:
        body = receipt.group(0)
        for field in ["operationId", "parentOperationId", "kind", "eventTokenId",
                      "expectedBeforeDigest", "expectedAfterDigest", "status", "receiptDigest"]:
            if not re.search(r"\b{}\s*;".format(field), body):
                errors.append("[DTO] ModeHStakeReceiptDto 缺少字段: " + field)


def main():
    errors = []
    check_phase_machine(errors)
    check_single_writer(errors)
    check_bridge(errors)
    check_identity(errors)
    check_no_gate_symbols(errors)
    check_dtos(errors)

    if errors:
        print("ModeHStakeJournalGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHStakeJournalGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
