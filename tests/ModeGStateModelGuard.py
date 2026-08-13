#!/usr/bin/env python3
"""
ModeGStateModelGuard — Mode G 状态模型守卫（规格 §20 第 5 条 + §9 门控契约）。

不变式：
- 生命周期五段 enum（None/Starting/Active/Rewarding/Exiting）；
- 战斗相位含 Fighting/LastStand/Victory/Defeat；
- ModeGPhaseGuards.IsCombatPhase 仅 Fighting/LastStand；
- 门控派生属性定义：
  IsModeGRunInProgress = lifecycle != None（只来自 lifecycle）；
  IsModeGGlobalQuarantineActive = ModeGLateCleanupSink.HasPendingLeases（只来自 sink）；
  IsModeGEntryBlocked = RunInProgress || Quarantine；
  IsModeGAchievementDamageWindowActive = Active + IsCombatPhase；
- RunState CAS：battleResultToken 仅 Victory/Defeat CAS 一次；lifecycle 单向推进；
  ResolveSlotOnce 是唯一写 resolved/committed 的入口并校验
  0 <= committed <= resolved <= expected（违反即 throw）；
- contractStreakBreakToken 只允许有效 ManualExit 消费一次。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATE_MODEL = os.path.join(REPO_ROOT, "ModeG", "ModeGStateModel.cs")
RUN_STATE = os.path.join(REPO_ROOT, "ModeG", "ModeGRunState.cs")


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.basename(path))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []
    model = read(STATE_MODEL, errors)
    state = read(RUN_STATE, errors)

    if model:
        checks = [
            ("LifecycleEnum",
             r"public enum ModeGLifecyclePhase[\s\S]*?None,[\s\S]*?Starting,[\s\S]*?Active,"
             r"[\s\S]*?Rewarding,[\s\S]*?Exiting",
             "生命周期五段 enum"),
            ("CombatPhaseEnum",
             r"public enum ModeGCombatPhase[\s\S]*?Fighting,[\s\S]*?LastStand,"
             r"[\s\S]*?Victory,[\s\S]*?Defeat",
             "战斗相位含 Fighting/LastStand/Victory/Defeat"),
            ("BattleResultEnum",
             r"public enum ModeGBattleResult[\s\S]*?Pending,[\s\S]*?Victory,[\s\S]*?Defeat",
             "战斗结果三态 enum"),
            ("CombatPhaseGuard",
             r"return phase == ModeGCombatPhase\.Fighting \|\| phase == ModeGCombatPhase\.LastStand;",
             "IsCombatPhase 仅 Fighting/LastStand"),
            ("RunInProgressFromLifecycle",
             r"state != null && state\.lifecyclePhase != ModeGLifecyclePhase\.None",
             "IsModeGRunInProgress 只来自 lifecycle"),
            ("QuarantineFromSinkOnly",
             r"return ModeGLateCleanupSink\.HasPendingLeases;",
             "IsModeGGlobalQuarantineActive 只来自 sink"),
            ("EntryBlockedComposition",
             r"return IsModeGRunInProgress \|\| IsModeGGlobalQuarantineActive;",
             "IsModeGEntryBlocked = RunInProgress || Quarantine"),
            ("AchievementWindowDefinition",
             r"state\.lifecyclePhase == ModeGLifecyclePhase\.Active"
             r"[\s\S]{0,120}?ModeGPhaseGuards\.IsCombatPhase\(state\.combatPhase\)",
             "成就窗口 = Active + Fighting/LastStand"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, model):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if state:
        checks = [
            ("BattleResultCasOnce",
             r"public bool TryLockBattleResult\(ModeGBattleResult target\)"
             r"[\s\S]*?target != ModeGBattleResult\.Victory && target != ModeGBattleResult\.Defeat"
             r"[\s\S]*?battleResult != ModeGBattleResult\.Pending",
             "battleResultToken 仅 Victory/Defeat CAS 一次"),
            ("LifecycleOneWay",
             r"private static bool CanAdvanceLifecycle\(ModeGLifecyclePhase from, ModeGLifecyclePhase to\)",
             "lifecycle 单向推进判定存在"),
            ("ResolveSlotOnceSoleWriter",
             r"public bool ResolveSlotOnce\(int ticket, ModeGSlotOutcome outcome\)",
             "ResolveSlotOnce 唯一写 resolved/committed 入口"),
            ("SlotInvariantThrow",
             r"_slotCommitted < 0 \|\| _slotCommitted > _slotResolved \|\| _slotResolved > _slotExpected"
             r"[\s\S]{0,120}?throw new InvalidOperationException",
             "0 <= committed <= resolved <= expected 违反即 throw"),
            ("TicketOnce",
             r"if\s*\(!_resolvedTickets\.Add\(ticket\)\)\s*return false;",
             "同一 ticket 只允许结案一次"),
            ("StreakBreakTokenOnce",
             r"public bool TryConsumeContractStreakBreakToken\(\)",
             "contractStreakBreakToken 一次性消费入口"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, state):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if errors:
        print("ModeGStateModelGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGStateModelGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
