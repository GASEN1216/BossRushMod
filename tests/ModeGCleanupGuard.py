#!/usr/bin/env python3
"""
ModeGCleanupGuard — Mode G 清理守卫（规格 §20 第 29 条）。

不变式：
- 九种终局共用幂等 End：ModeGExitReason = None + 九种终局冻结；
  End 首行 `if (_ended || _state == null) return; _ended = true;`；
- 四事件按 owner 退订：End 内 telemetry.UnsubscribeCombat()
  （OnHurt/OnShoot/OnControllingCharacterChanged 三 combat 委托）+
  UnsubscribeDead()（独立 OnDead owner）+ UnsubscribePlayerDeath()，
  均 owner bool 幂等守卫；
- Modifier/lease 原值恢复：End 内 _adaptive.RestoreAllModifiers()；
  sink ReleaseLease `if (leaseId <= 0) return;` 守卫（不释放未持有 lease）；
  managed handle CleanupOnce(RunEnded) + 清空，Dispatcher 仅
  ReferenceEquals 自持时复位；
- OnDestroy 顺序：RuntimeModule.OnDestroy 委托 ModeG.PrepareHostDestroy；
  ModBehaviour.OnDestroy 中 PrepareHostDestroy 先于其他清理；
- 战斗中主 Boss 技术丢失禁推进：批量激活失败 ->
  CleanupOnce(TechnicalLoss) + End(TechnicalIntegrityLoss) + return；
  CanContinueRun 纳入 !_ended 闸门（End 后不得继续生成/推进）；
- Rewarding 的非胜利退出必须在 Cleanup 前失效 attempt nonce 并取消物化，
  防止切图/销毁后继续发奖；Victory 完成路径不取消；
- sink 非空时 EntryBlocked 持续而 RunInProgress/NPC 抑制不得因 sink 为
  true：EntryBlocked = RunInProgress || Quarantine；IsModeGRunInProgress
  仅读 lifecycle（不读 HasPendingLeases）；EnemyRecoveryMonitor（NPC
  侧）只读 IsModeGRunInProgress，不引用 IsModeGEntryBlocked。

规格偏差注明：规格「幂等 async End」在实现中为同步幂等 End（void），
幂等/统一出口不变式等价满足。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUNTIME_PARTS = [
    os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeModule.cs"),
    os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeModule_PublicApiAndShutdown.cs"),
]
CLEANUP = os.path.join(REPO_ROOT, "ModeG", "ModeGCleanupController.cs")
STATE = os.path.join(REPO_ROOT, "ModeG", "ModeGStateModel.cs")
TELEMETRY = os.path.join(REPO_ROOT, "ModeG", "ModeGCombatTelemetry.cs")
MODBEHAVIOUR = os.path.join(REPO_ROOT, "ModBehaviour.cs")
RECOVERY = os.path.join(REPO_ROOT, "Utilities", "EnemyRecoveryMonitor.cs")


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
    runtime = "\n".join(read(path, errors) for path in RUNTIME_PARTS)
    cleanup = read(CLEANUP, errors)
    state = read(STATE, errors)
    telemetry = read(TELEMETRY, errors)
    host = read(MODBEHAVIOUR, errors)
    recovery = read(RECOVERY, errors)

    if state:
        # 九种终局枚举冻结（None + 9）
        m = re.search(r"public enum ModeGExitReason\s*\{([\s\S]*?)\}", state)
        if m:
            names = [n.strip() for n in strip_comments(m.group(1)).split(",")
                     if n.strip()]
            expected = ["None", "Victory", "PlayerDeath", "SceneChanged",
                        "ManualExit", "SpawnExhausted", "TechnicalIntegrityLoss",
                        "RewardAbandoned", "RewardInterruptedByDeath", "ModDestroyed"]
            if names != expected:
                errors.append("[NineEndgame] ModeGExitReason 成员变化: {}".format(names))
        else:
            errors.append("[NineEndgame] ModeGExitReason 枚举未找到")

        checks = [
            ("EntryBlockedUnion",
             r"return IsModeGRunInProgress \|\| IsModeGGlobalQuarantineActive;",
             "EntryBlocked = RunInProgress || Quarantine（sink 非空持续隔离）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, state):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # RunInProgress 仅读 lifecycle，不读 sink
        m = re.search(r"public static bool IsModeGRunInProgress[\s\S]{0,400}?\}", state)
        if m:
            body = m.group(0)
            if "HasPendingLeases" in body:
                errors.append("[RunInProgressNoSink] IsModeGRunInProgress 读取 sink lease")
            if "lifecyclePhase" not in body and "LifecyclePhase" not in body:
                errors.append("[RunInProgressLifecycle] IsModeGRunInProgress 未读 lifecycle")
        else:
            errors.append("[RunInProgress] IsModeGRunInProgress 未找到")

    if runtime:
        checks = [
            ("IdempotentEnd",
             r"public void End\(ModeGExitReason reason\)\s*\{\s*"
             r"if \(_ended \|\| _state == null\) return;\s*_ended = true;",
             "End 幂等（_ended 一次置位）"),
            ("EndOrderContract",
             r"统一幂等 End：Cleanup 状态机 -> 退订 -> Modifier 恢复 -> managed handle 清理",
             "End 统一出口顺序契约注释"),
            ("TelemetryUnsubscribe",
             r"_telemetry\.UnsubscribeCombat\(\);"
             r"[\s\S]{0,60}?_telemetry\.UnsubscribeDead\(\);",
             "End 退订 telemetry 三 combat 委托 + 独立 OnDead owner"),
            ("PlayerDeathUnsubscribe",
             r"UnsubscribePlayerDeath\(\);",
             "End 退订玩家死亡订阅"),
            ("ModifierRestore",
             r"if \(_adaptive != null\) _adaptive\.RestoreAllModifiers\(\);",
             "End 恢复全部 Modifier 原值"),
            ("ManagedHandleCleanup",
             r"_managedHandles\[i\]\.CleanupOnce\(ManagedBossCleanupReason\.RunEnded\);"
             r"[\s\S]{0,120}?_managedHandles\.Clear\(\);",
             "End managed handle CleanupOnce(RunEnded) + 清空"),
            ("DispatcherResetConditional",
             r"if \(_dispatcherRef != null\s*"
             r"&& ReferenceEquals\(ModBehaviour\.ManagedBossSpawnDispatcher, _dispatcherRef\)\)"
             r"[\s\S]{0,80}?ModBehaviour\.ManagedBossSpawnDispatcher = null;",
             "Dispatcher 仅自持时复位（不覆盖他人接管）"),
            ("ModuleOnDestroyDelegates",
             r"public override void OnDestroy\(\)"
             r"[\s\S]{0,120}?ModeG\.PrepareHostDestroy\(\);",
             "RuntimeModule.OnDestroy 委托 PrepareHostDestroy"),
            ("TechnicalLossNoAdvance",
             r"if \(handle == null \|\| !handle\.ActivateOnce\(\)\)\s*\{\s*"
             r"if \(handle != null\) handle\.CleanupOnce\(ManagedBossCleanupReason\.TechnicalLoss\);"
             r"[\s\S]{0,80}?End\(ModeGExitReason\.TechnicalIntegrityLoss\);"
             r"[\s\S]{0,40}?return;",
             "战斗中主 Boss 激活失败 -> 技术丢失结案并立即返回（禁推进）"),
            ("CanContinueEndGate",
             r"return !_disposed && !_ended",
             "CanContinueRun 纳入 !_ended（End 后禁生成/推进）"),
            ("NonVictoryRewardCancellation",
             r"if \(reason != ModeGExitReason\.Victory && _state\.IsRewarding\)"
             r"[\s\S]{0,180}?_state\.rewardNonceInvalidated = true;"
             r"[\s\S]{0,180}?ModeGRewardTransaction\.InvalidateAttemptNonce\(\);"
             r"[\s\S]{0,300}?CancelModeGRewardMaterialization_LootAndRewards\(\);"
             r"[\s\S]{0,500}?RefundStartupPaymentOnTechnicalFailure\(reason\);",
             "Rewarding 非胜利退出先失效 nonce、取消物化，再进入统一 Cleanup"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, runtime):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if telemetry:
        checks = [
            ("CombatUnsubscribeOwner",
             r"if \(!_combatSubscribed\) return;"
             r"[\s\S]{0,120}?Health\.OnHurt -= HandleOnHurt;",
             "UnsubscribeCombat owner bool 幂等 + 精确退订"),
            ("DeadUnsubscribeSeparateOwner",
             r"精确退订 OnDead（独立 owner）",
             "OnDead 独立 owner 精确退订（契约注释）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, telemetry):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if cleanup:
        checks = [
            ("LeaseReleaseGuard",
             r"public static void ReleaseLease\(int leaseId\)\s*\{\s*"
             r"if \(leaseId <= 0\) return;",
             "ReleaseLease 守卫（不释放未持有 lease）"),
            ("CleanupIdempotentPhases",
             r"if \(state\.exitReason == ModeGExitReason\.None\) state\.exitReason = reason;"
             r"[\s\S]*?state\.spawnLeasesInvalidated = true;"
             r"[\s\S]{0,120}?state\.ClearTrackedBosses\(\);",
             "Cleanup 幂等状态推进（exitReason 只写一次 + lease 失效 + 清 tracked）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, cleanup):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if host:
        # OnDestroy 顺序：PrepareHostDestroy 先于其他清理
        idx_prepare = host.find("BossRush.ModeG.PrepareHostDestroy();")
        idx_debug = host.find("CleanupDebugToolsOnDestroy();")
        if idx_prepare < 0:
            errors.append("[OnDestroyOrder] ModBehaviour 未调用 PrepareHostDestroy")
        elif idx_debug >= 0 and idx_prepare > idx_debug:
            errors.append("[OnDestroyOrder] PrepareHostDestroy 不在其他清理之前")

    if recovery:
        if not re.search(r"ModeGRuntimeGates\.IsModeGRunInProgress", recovery):
            errors.append("[NpcSuppression] EnemyRecoveryMonitor 未读 IsModeGRunInProgress")
        if "IsModeGEntryBlocked" in recovery:
            errors.append("[NpcSuppressionNoEntryBlocked] NPC 侧引用 IsModeGEntryBlocked")

    if errors:
        print("ModeGCleanupGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
