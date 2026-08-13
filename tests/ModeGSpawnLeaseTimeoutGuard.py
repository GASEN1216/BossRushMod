#!/usr/bin/env python3
"""
ModeGSpawnLeaseTimeoutGuard — spawn lease 有界失效守卫（规格 §20 第 26 条）。

不变式：
- lease-aware 有界失效：spawnLeasesInvalidated 置位后 TryAcquireLease
  fail-closed（返回 Exhausted），事务文件剥注释后无计时器/异步定时符号；
- 失效/失败路径不写槽结案：槽位写入口唯一为 ResolveSlotOnce（lock +
  HashSet.Add 一次性），调用方仅 TryCommit / MarkExhausted；
  生成循环中 lease Exhausted -> break、CanContinueRun=false -> 清理后
  return，均不结案不启额外后备（attempt1 reserve key 属计划内二次尝试）；
- 未 settled factory continuation 移交 sink：PrepareHostDestroy 步骤 7
  注释契约 + 无 run 无 lease O(1) 早返 + 未完成 staging 角色静默销毁；
  sink lease 有界常量（LateCleanupMaxWaitSeconds=15f /
  LateCleanupCheckInterval=0.5f）；reward materializer 对称
  Acquire/Release lease；
- quarantine 与本地 None 分离：全局 quarantine 仅
  ModeGLateCleanupSink.HasPendingLeases；EntryBlocked =
  RunInProgress || Quarantine；移交后本地 Unbind 为 None 而入口仍隔离。

规格偏差注明：规格「有界 timeout」在实现中表达为 spawnLeasesInvalidated
fail-closed + sink 有界等待常量，无独立计时器 poller（LateCleanupSink
方法与两个有界常量暂无消费点）；「fake character task」无专属符号，等价
表达为 in-flight spawn 失败路径 finally 静默销毁未完成角色 +
PrepareHostDestroy 步骤 7 sink 移交契约。核心不变式（不 ResolveSlot、
不启备用、quarantine 分离）均按实现符号严格断言。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TX = os.path.join(REPO_ROOT, "ModeG", "ModeGSpawnTransaction.cs")
RUNTIME = os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeModule.cs")
CLEANUP = os.path.join(REPO_ROOT, "ModeG", "ModeGCleanupController.cs")
ENTRY = os.path.join(REPO_ROOT, "ModeG", "ModeGEntry.cs")
STATE = os.path.join(REPO_ROOT, "ModeG", "ModeGStateModel.cs")
RUNSTATE = os.path.join(REPO_ROOT, "ModeG", "ModeGRunState.cs")
REWARD = os.path.join(REPO_ROOT, "ModeG", "ModeGRewardTransaction.cs")


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
    tx = read(TX, errors)
    runtime = read(RUNTIME, errors)
    cleanup = read(CLEANUP, errors)
    entry = read(ENTRY, errors)
    state = read(STATE, errors)
    runstate = read(RUNSTATE, errors)
    reward = read(REWARD, errors)

    if tx:
        checks = [
            ("LeaseFailClosed",
             r"if \(_state\.spawnLeasesInvalidated \|\| _committedSlots\.Contains\(slotIndex\)\)"
             r"[\s\S]{0,120}?ModeGSlotOutcome\.Exhausted",
             "spawnLeasesInvalidated 后 TryAcquireLease fail-closed(Exhausted)"),
            ("MaxAttemptsFrozen",
             r"public const int MaxAttemptsPerSlot = 2;",
             "每槽至多 2 次尝试（有界）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, tx):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # 事务类体剥注释后禁计时器/异步定时符号（lease 失效不得由定时器驱动写结案）
        m = re.search(r"public sealed class ModeGSpawnTransaction([\s\S]*?)\n    \}", tx)
        if m:
            body = strip_comments(m.group(1))
            for token in ["Invoke(", "StartCoroutine", "InvokeRepeating",
                          "Timer", "Time.unscaledTime"]:
                if token in body:
                    errors.append("[NoTimer] 事务类含定时符号 {}".format(token))
        else:
            errors.append("[TxClass] ModeGSpawnTransaction 类体未找到")

    if runtime:
        checks = [
            ("ExhaustedBreakNoFallback",
             r"ModeGSpawnTransaction\.SpawnAttemptLease lease =\s*"
             r"_spawnTransaction\.TryAcquireLease\(slotIndex, key\);"
             r"[\s\S]{0,60}?if \(lease\.outcome == ModeGSlotOutcome\.Exhausted\) break;",
             "lease Exhausted -> break（不结案、不启备用）"),
            ("OwnerInvalidCleanupNoResolve",
             r"if \(!CanContinueRun\(\)\)\s*\{\s*"
             r"if \(managedHandle != null\)\s*"
             r"managedHandle\.CleanupOnce\(ManagedBossCleanupReason\.OwnerInvalid\);"
             r"[\s\S]{0,120}?return;",
             "CanContinueRun=false -> 清理后 return（不写槽结案）"),
            ("CommitRejectedCleanup",
             r"if \(!_spawnTransaction\.TryCommit\(slotIndex, key, boss\)\)"
             r"[\s\S]{0,200}?CleanupOnce\(ManagedBossCleanupReason\.SpawnRejected\)",
             "提交失败 -> CleanupOnce（失败路径不结案）"),
            ("ExhaustedMarkOnly",
             r"if \(!committed\) _spawnTransaction\.MarkExhausted\(slotIndex\);",
             "两尝试耗尽才 MarkExhausted 结案（唯一失败结案点）"),
            ("CanContinueLeaseGate",
             r"!_state\.spawnLeasesInvalidated",
             "CanContinueRun 纳入 spawnLeasesInvalidated 闸门"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, runtime):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if runstate:
        checks = [
            ("ResolveSlotOnceUnique",
             r"public bool ResolveSlotOnce\(int ticket, ModeGSlotOutcome outcome\)"
             r"[\s\S]{0,200}?lock \(_casLock\)"
             r"[\s\S]{0,120}?if \(!_resolvedTickets\.Add\(ticket\)\) return false;",
             "ResolveSlotOnce 唯一写入口（lock + ticket 一次性）"),
            ("SlotInvariantCheck",
             r"if \(_slotCommitted < 0 \|\| _slotCommitted > _slotResolved"
             r" \|\| _slotResolved > _slotExpected\)",
             "结案后 0<=committed<=resolved<=expected 不变式校验"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, runstate):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if cleanup:
        checks = [
            ("BoundedWaitConstants",
             r"public const float LateCleanupMaxWaitSeconds = 15f;"
             r"[\s\S]{0,80}?public const float LateCleanupCheckInterval = 0\.5f;",
             "sink 有界等待常量 15s / 0.5s（有界 timeout）"),
            ("SinkLeaseLifecycle",
             r"public static int AcquireLease\(string owner\)"
             r"[\s\S]*?public static void ReleaseLease\(int leaseId\)"
             r"[\s\S]{0,200}?if \(leaseId <= 0\) return;",
             "sink lease Acquire/Release 生命周期（Release 幂等守卫）"),
            ("HasPendingLeases",
             r"public static bool HasPendingLeases[\s\S]{0,120}?Leases\.Count > 0",
             "quarantine 查询 = lease 计数 > 0"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, cleanup):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if entry:
        checks = [
            ("FactoryContinuationSinkHandoff",
             r"未 settled factory continuation 转 ModeGLateCleanupSink",
             "步骤 7：未 settled factory continuation 移交 sink 契约"),
            ("O1EarlyReturn",
             r"if \(state == null && !ModeGLateCleanupSink\.HasPendingLeases\)"
             r"[\s\S]{0,80}?return;",
             "无 run 无 lease 时 O(1) 早返"),
            ("SinkOnlyDrain",
             r"// 仅有 sink pending lease：交给 sink 自行 drain",
             "本地 None 时 sink 自行 drain（quarantine 与本地分离）"),
            ("IdempotentHostDestroy",
             r"System\.Threading\.Interlocked\.Exchange\(ref _prepareHostDestroyState, 1\) != 0",
             "PrepareHostDestroy Interlocked CAS 幂等"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, entry):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if state:
        checks = [
            ("QuarantineOnlySink",
             r"public static bool IsModeGGlobalQuarantineActive"
             r"[\s\S]{0,200}?return ModeGLateCleanupSink\.HasPendingLeases;",
             "全局 quarantine 仅 HasPendingLeases"),
            ("EntryBlockedUnion",
             r"public static bool IsModeGEntryBlocked"
             r"[\s\S]{0,200}?return IsModeGRunInProgress \|\| IsModeGGlobalQuarantineActive;",
             "EntryBlocked = RunInProgress || Quarantine"),
            ("HandoffComment",
             r"移交 sink 后本地可为 None，但入口仍被隔离",
             "移交后本地 None 而入口仍隔离（契约注释）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, state):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if reward:
        checks = [
            ("MaterializerLeaseAcquire",
             r'ModeGLateCleanupSink\.AcquireLease\("reward_materializer"\)',
             "reward materializer 获取 sink lease（有界）"),
            ("MaterializerLeaseRelease",
             r"ModeGLateCleanupSink\.ReleaseLease\(leaseId\);",
             "materializer 完成/取消释放 lease"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, reward):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if errors:
        print("ModeGSpawnLeaseTimeoutGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGSpawnLeaseTimeoutGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
