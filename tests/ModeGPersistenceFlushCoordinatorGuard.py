#!/usr/bin/env python3
"""
ModeGPersistenceFlushCoordinatorGuard — 持久化 flush 协调守卫（规格 §20 第 24 条）。

flush 协调不变式在宿敌/个人记录两个持久化类中共同实现：
- 每槽至多一个 pending flush（_pendingFlushActive bool，合并覆盖不叠加）；
- Store 始终向共享 coordinator 请求；coordinator 在 IsSaving 时最多逐帧等待 120 帧，
  DTO FlushPendingLocked 顶部仍再次 IsSaving 早返；
- 同批分别 Save + 关键字段回读核对，再由 coordinator 恰好一次 SaveFile(false)；
- 两个 key 分别维护未知版本写屏障；被屏障 key 不产生 dirty/Save，另一 key
  仍可独立进入同一 coordinator；
- 冻结 CurrentSlot + slot generation，切槽/删档取消旧批次；运行中新增 dirty 会续批；
- 战斗帧写屏障（CR-2026-08-29-019 第 2 项）：typed Save 永不推迟，只有物理
  SavesSystem.SaveFile 在 ModeGRuntimeGates.IsModeGHostFileWriteDeferred 为真时
  记欠账 _hostWriteDeferred 顺延；End() 在 Cleanup 之后补一次 RequestFlush 结清欠账；
  TryFlushOnHostDestroy 走 forceHostWrite:true 强制落盘（关停无下一帧可等）；
  切槽/删档与 fault 清空欠账；
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
ENTRY = os.path.join(REPO_ROOT, "ModeG", "ModeGEntry.cs")
COORDINATOR = os.path.join(REPO_ROOT, "ModeG", "ModeGPersistenceFlushCoordinator.cs")
SHUTDOWN = os.path.join(
    REPO_ROOT, "ModeG", "ModeGRuntimeModule_PublicApiAndShutdown.cs")
STATE_MODEL = os.path.join(REPO_ROOT, "ModeG", "ModeGStateModel.cs")


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
        ("RequestCoordinator",
         r"ModeGPersistenceFlushCoordinator\.RequestFlush\(\);",
         "Store 始终请求共享 coordinator"),
        ("FlushIsSavingGuard",
         r"private static void FlushPendingLocked\(bool writeFile\)"
         r"[\s\S]{0,300}?if \(SavesSystem\.IsSaving\) return;",
         "flush 顶部 IsSaving 早返（不打断官方保存）"),
        ("PerKeyWriteBarrier",
         r"if \(_writeBarrier\) return;",
         "未知版本只跳过当前 key 的 flush"),
        ("SaveThenReadback",
         r"SavesSystem\.Save<[\s\S]{0,80}?>\(StorageKey, _pending\);"
         r"[\s\S]{0,200}?readback = SavesSystem\.Load<",
         "typed Save + 回读核对"),
        ("CriticalReadback",
         r"if \(!CriticalFieldsMatch\(_pending, readback\)\)",
         "typed Save 后关键字段逐项回读核对"),
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
    entry = read(ENTRY, errors)
    coordinator = read(COORDINATOR, errors)
    shutdown = read(SHUTDOWN, errors)
    state_model = read(STATE_MODEL, errors)

    if nemesis:
        check_flush_invariants(nemesis, "Nemesis", errors)
    if profile:
        check_flush_invariants(profile, "Profile", errors)

    if entry and not re.search(
            r"ModeGNemesisPersistence\.IsStoreFaulted[\s\S]{0,160}?"
            r"ModeGProfilePersistence\.IsStoreFaulted[\s\S]{0,160}?"
            r"ModeGPersistenceFlushCoordinator\.IsFaulted", entry):
        errors.append("[EntryFailClosed] 扣除入场物品前未同时消费三类持久化故障")

    if coordinator:
        coordinator_checks = [
            ("BusyBoundedYield",
             r"while \(SavesSystem\.IsSaving && busyFrames < MaxBusyWaitFrames\)"
             r"[\s\S]{0,120}?await UniTask\.Yield\(\);"
             r"[\s\S]{0,900}?if \(retryBusy\)"
             r"[\s\S]{0,100}?RequestFlush\(\);",
             "IsSaving 有界逐帧顺延并分批续等"),
            ("SlotFreeze",
             r"slot = SavesSystem\.CurrentSlot;[\s\S]{0,120}?generation = _slotGeneration;"
             r"[\s\S]*?IsSameSlot\(slot, generation\)",
             "批次冻结存档槽与 generation"),
            ("FaultBeforeHostSave",
             r"ModeGNemesisPersistence\.IsStoreFaulted"
             r"[\s\S]{0,120}?ModeGProfilePersistence\.IsStoreFaulted"
             r"[\s\S]{0,700}?SavesSystem\.SaveFile\(false\);",
             "DTO fault 检查先于 host SaveFile"),
            ("TypedFlushBeforeDeferral",
             r"ModeGNemesisPersistence\.FlushPending\(writeFile: false\);"
             r"[\s\S]{0,300}?ModeGProfilePersistence\.FlushPending\(writeFile: false\);"
             r"[\s\S]{0,600}?ModeGRuntimeGates\.IsModeGHostFileWriteDeferred",
             "typed Save 先于战斗帧顺延判定（typed 永不推迟）"),
            ("CombatHostWriteDeferral",
             r"if \(!forceHostWrite && ModeGRuntimeGates\.IsModeGHostFileWriteDeferred\)"
             r"[\s\S]{0,160}?_hostWriteDeferred = true;"
             r"[\s\S]{0,60}?return;",
             "战斗帧只顺延物理 SaveFile 并记欠账"),
            ("DeferredDebtEntersBatch",
             r"owedHostWrite = _hostWriteDeferred;"
             r"[\s\S]{0,200}?if \(\(!hadDirty && !owedHostWrite\)",
             "顺延欠账本身能重新进入批次（无 dirty 也补写）"),
            ("HostDestroyForcesWrite",
             r"internal static void TryFlushOnHostDestroy\(\)"
             r"[\s\S]{0,1200}?FlushBatch\(slot, generation, forceHostWrite: true\);",
             "宿主销毁强制落盘，不再顺延"),
            ("SlotChangeDropsDebt",
             r"internal static void NotifySlotChanged\(\)"
             r"[\s\S]{0,260}?_hostWriteDeferred = false;",
             "切槽/删档丢弃旧槽落盘欠账"),
            ("DirtyContinuation",
             r"_pending = false;[\s\S]*?if \(!_faulted"
             r"[\s\S]*?ModeGNemesisPersistence\.HasPendingFlush"
             r"[\s\S]*?ModeGProfilePersistence\.HasPendingFlush"
             r"[\s\S]*?if \(reschedule\) FlushNextFrame\(\)\.Forget\(\);",
             "执行期新增 dirty 会续批"),
            ("SlotChangeCancel",
             r"internal static void NotifySlotChanged\(\)"
             r"[\s\S]{0,180}?_slotGeneration\+\+;",
             "切槽/删档取消旧 generation"),
            ("HostDestroyBestEffort",
             r"internal static void TryFlushOnHostDestroy\(\)"
             r"[\s\S]{0,700}?if \(!_running && !SavesSystem\.IsSaving\)"
             r"[\s\S]{0,900}?FlushBatch\(slot, generation,",
             "宿主销毁时同步尽力 flush，繁忙时不重入"),
        ]
        for name, pattern, desc in coordinator_checks:
            if not re.search(pattern, coordinator):
                errors.append("[Coordinator:{}] 不满足: {}".format(name, desc))
        if coordinator.count("SavesSystem.SaveFile(false);") != 1:
            errors.append("[Coordinator:SingleSaveFile] coordinator 必须仅有一个 SaveFile(false) 调用点")

    # 战斗帧顺延的欠账必须在终局帧结清（Cleanup 归零相位之后补一次 RequestFlush）
    if shutdown and not re.search(
            r"ModeGCleanupController\.Cleanup\(_state, reason\);"
            r"[\s\S]{0,400}?ModeGPersistenceFlushCoordinator\.RequestFlush\(\);",
            shutdown):
        errors.append("[EndDrainsDeferredWrite] End() 未在 Cleanup 后补写顺延的落盘欠账")

    # 顺延窗口语义冻结：仅 Active + Fighting/LastStand，且 no-throw 异常 false
    if state_model and not re.search(
            r"public static bool IsModeGHostFileWriteDeferred\b"
            r"[\s\S]{0,500}?state\.lifecyclePhase == ModeGLifecyclePhase\.Active"
            r"[\s\S]{0,80}?&& ModeGPhaseGuards\.IsCombatPhase\(state\.combatPhase\)"
            r"[\s\S]{0,120}?catch[\s\S]{0,40}?return false;",
            state_model):
        errors.append("[DeferralWindowSemantics] 落盘顺延窗口语义/no-throw 不满足")

    if entry:
        preview = re.search(r"public ModeGEntryPreview GetOrCreateModeGEntryPreview\(\)([\s\S]*?)#endregion", entry)
        if not preview or "ModeGNemesisPersistence.EnsureSubscribed();" not in preview.group(1) \
                or "ModeGProfilePersistence.EnsureSubscribed();" not in preview.group(1):
            errors.append("[RuntimeSubscription] preview 前未幂等订阅两份存档事件")
        destroy = re.search(r"public static void PrepareHostDestroy\(\)([\s\S]*?)\n        \}", entry)
        if not destroy or not re.search(
                r"TryFlushOnHostDestroy\(\)[\s\S]{0,500}?"
                r"ModeGNemesisPersistence\.ShutdownSubscription\(\)[\s\S]{0,500}?"
                r"ModeGProfilePersistence\.ShutdownSubscription\(\)", destroy.group(1)):
            errors.append("[HostDestroyFlushOrder] 宿主销毁未在退订前尽力 flush 两个 key")

    if module and "ModeGNemesisPersistence.ShutdownSubscription();" in module:
        errors.append("[RunEndSubscription] 单局 End 仍退订 Mod runtime 级持久化事件")

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
