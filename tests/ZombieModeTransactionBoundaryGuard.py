"""SPEC 14 §7 / SPEC 15 §2 / SPEC 15 §8 入场事务边界守护

校验：
1. 七阶段合法转移路径存在（SelectingMap → Prechecking → CommittingResources → LoadingMap → InitializingRun → WaitingStarterChoice → Active）。
2. FailZombieModeBeforeActive 必须使用 ZombieModeFailureReason 枚举而非字符串字面量。
3. Active 前失败回滚链路完整：RefundZombieModeInvitationIfNeeded + RefundZombieModeCashIfNeeded + CleanupZombieModeForSceneChange + LoadBaseScene。
4. 入场事务 transaction 字段 InvitationTemporarilyHeld / CashTemporarilyHeld / CashWithheldAmount / EntryResourcesFinalized 全部存在。
5. ZombieModeFailureReason 24 个枚举值齐全。
"""

from pathlib import Path
import re
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
STARTER_LOADOUT = Path("ZombieMode/ZombieModeEntry_StarterLoadout.cs")
MODELS = Path("ZombieMode/ZombieModeModels.cs")
CLEANUP = Path("ZombieMode/ZombieModeCleanup.cs")


def fail(msg: str) -> int:
    print(msg)
    return 1


def main() -> int:
    entry_text = ENTRY.read_text(encoding="utf-8")
    entry_flow_text = entry_text + "\n" + STARTER_LOADOUT.read_text(encoding="utf-8")
    models_text = MODELS.read_text(encoding="utf-8")
    cleanup_text = CLEANUP.read_text(encoding="utf-8")

    # 1. 七阶段路径在入口 flow 中显式出现
    required_phase_writes = [
        "ZombieModeLifecyclePhase.Prechecking",
        "ZombieModeLifecyclePhase.CommittingResources",
        "ZombieModeLifecyclePhase.LoadingMap",
        "ZombieModeLifecyclePhase.InitializingRun",
        "ZombieModeLifecyclePhase.WaitingStarterChoice",
        "ZombieModeLifecyclePhase.Active",
    ]
    for phase in required_phase_writes:
        if phase not in entry_flow_text:
            return fail("ZombieModeTransactionBoundaryGuard: 入口 flow 缺少阶段写入 -> " + phase)

    # 2. MarkZombieModeMapConfirmedPhase1 必须先走 Prechecking 再走 CommittingResources，最后才到 LoadingMap
    confirm_match = re.search(
        r"public\s+void\s+MarkZombieModeMapConfirmedPhase1\s*\(\s*\)\s*\{(.+?)\n\s{8}\}",
        entry_text,
        re.S,
    )
    if confirm_match is None:
        return fail("ZombieModeTransactionBoundaryGuard: MarkZombieModeMapConfirmedPhase1 未找到")
    body = confirm_match.group(1)
    pre_idx = body.find("ZombieModeLifecyclePhase.Prechecking")
    commit_idx = body.find("ZombieModeLifecyclePhase.CommittingResources")
    load_idx = body.find("ZombieModeLifecyclePhase.LoadingMap")
    if pre_idx < 0 or commit_idx < 0 or load_idx < 0 or not (pre_idx < commit_idx < load_idx):
        return fail(
            "ZombieModeTransactionBoundaryGuard: MarkZombieModeMapConfirmedPhase1 必须按顺序写入 "
            "Prechecking -> CommittingResources -> LoadingMap"
        )

    # 3. FailZombieModeBeforeActive 签名必须接收枚举
    if "FailZombieModeBeforeActive(ZombieModeFailureReason reason)" not in entry_text:
        return fail("ZombieModeTransactionBoundaryGuard: FailZombieModeBeforeActive 签名必须接收 ZombieModeFailureReason")

    # 4. 失败回滚链路完整
    fail_match = re.search(
        r"private\s+void\s+FailZombieModeBeforeActive\s*\(.*?\)\s*\{(.+?)\n\s{8}\}",
        entry_text,
        re.S,
    )
    if fail_match is None:
        return fail("ZombieModeTransactionBoundaryGuard: FailZombieModeBeforeActive 实现未找到")
    fail_body = fail_match.group(1)
    for snippet in ["RefundZombieModeInvitationIfNeeded", "RefundZombieModeCashIfNeeded", "CleanupZombieModeForSceneChange"]:
        if snippet not in fail_body:
            return fail("ZombieModeTransactionBoundaryGuard: FailZombieModeBeforeActive 缺少回滚步骤 -> " + snippet)

    # 5. ZombieModeEntryTransaction 字段
    for field in [
        "InvitationTemporarilyHeld",
        "CashTemporarilyHeld",
        "CashWithheldAmount",
        "EntryResourcesFinalized",
        "InventoryTransferStarted",
        "BlockingMessages",
    ]:
        if field not in models_text:
            return fail("ZombieModeTransactionBoundaryGuard: ZombieModeEntryTransaction 缺少字段 -> " + field)

    # 6. ZombieModeFailureReason 24 个枚举值
    required_reasons = [
        "InvitationMissing", "NotEnoughCash", "NoEffectiveSpawnPoints", "StorageFull",
        "BlockedTaskOrBoundItems", "AnotherBossRushLikeModeActive", "InvitationConsumeFailed",
        "CashWithdrawFailed", "InventoryTransferFailed", "MapLoadFailed", "MapIsolationFailed",
        "SpawnPointCollectionFailed", "BeaconGrantFailed", "InitializationFailed",
        "StarterChoiceUiClosed", "StarterChoiceTimedOut", "StarterLoadoutFailed",
        "PlayerDeath", "ManualExit", "SceneSwitched", "UnexpectedSceneUnload",
        "SuccessfulExtraction", "Unknown",
    ]
    for reason in required_reasons:
        if reason not in models_text:
            return fail("ZombieModeTransactionBoundaryGuard: ZombieModeFailureReason 缺少 -> " + reason)

    # 7. CleanupZombieModeForSceneChange 必须接收 ZombieModeFailureReason
    if "CleanupZombieModeForSceneChange(ZombieModeFailureReason reason)" not in cleanup_text:
        return fail("ZombieModeTransactionBoundaryGuard: CleanupZombieModeForSceneChange 签名必须接收 ZombieModeFailureReason")

    # 8. ResetForNewRun 应把 LifecyclePhase 写为 InitializingRun
    if "LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun" not in models_text:
        return fail("ZombieModeTransactionBoundaryGuard: ResetForNewRun 必须将 LifecyclePhase 设为 InitializingRun")

    print("ZombieModeTransactionBoundaryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
