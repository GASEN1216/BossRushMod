#!/usr/bin/env python3
"""
ModeHStateMachineGuard — Mode H 状态机守卫（设计提案 §18.2、§26.1）。

不变式：
- 正式入口只按 SceneLoading -> ProductionCertifying -> Drafting 前进；
  认证失败退款回 None，且不保留残缺 Season；
- 主干从 LoadoutLocked 直接到 MatchSpawning，不要求 StakePrepared（后者只属真实资产路径）；
- 早期恢复子表（EntryIntent/SceneLoading/Drafting/RosterLocked）独立存在，
  这些源状态不得由默认恢复出口直接跳到 MatchBrief；
- 已提交事实必须先回 ErrorRecoveryPending，Recovering 不得直接跳过该中间态到 RelayPending/MatchSettling；
- 状态跳转单点：只有 ModeHStateMachine.TryTransition 能改 lifecycle；
- 无非法直写与 Terminal 回环（SeasonEnded 只能到 None，None 只能到 EntryIntent）；
- runOwnerToken 只存在内存，不进任何 DTO。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments, read_modeh_group  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
MACHINE = os.path.join(MODEH_DIR, "ModeHStateMachine.cs")
RUN_STATE = os.path.join(MODEH_DIR, "ModeHRunState.cs")
STATE_MODEL = os.path.join(MODEH_DIR, "ModeHStateModel.cs")

REQUIRED_EDGES = [
    ("None", ["EntryIntent"]),
    ("EntryIntent", ["SceneLoading"]),
    ("SceneLoading", ["ProductionCertifying"]),
    ("ProductionCertifying", ["Drafting", "None"]),
    ("Drafting", ["RosterLocked"]),
    ("RosterLocked", ["MatchBrief"]),
    ("MatchBrief", ["LoadoutEditing"]),
    ("LoadoutEditing", ["OddsPreview"]),
    ("OddsPreview", ["LoadoutEditing", "LoadoutLocked", "StakePrepared"]),
    ("LoadoutLocked", ["MatchSpawning", "StakePrepared"]),
    ("StakePrepared", ["MatchSpawning"]),
    ("MatchSpawning", ["MatchFighting"]),
    ("MatchFighting", ["RelayPending", "MatchSettling", "ErrorRecoveryPending"]),
    ("ErrorRecoveryPending", ["RelayPending", "MatchSettling", "Recovering"]),
    ("RelayPending", ["MatchFighting", "MatchSettling"]),
    ("MatchSettling", ["Intermission"]),
    ("Intermission", ["SeasonEnded", "HallOfFame", "TransferWindow", "MatchBrief", "Suspended"]),
    ("TransferWindow", ["MatchBrief"]),
    ("HallOfFame", ["SeasonEnded"]),
    ("Suspended", ["Recovering"]),
    ("SeasonEnded", ["None"]),
]

UNIFIED_FAILURE_SOURCES = [
    "MatchBrief", "LoadoutEditing", "OddsPreview", "LoadoutLocked", "StakePrepared",
    "MatchSpawning", "MatchFighting", "RelayPending", "MatchSettling", "Intermission",
    "TransferWindow", "HallOfFame",
]

EARLY_RECOVERY_SOURCES = ["EntryIntent", "SceneLoading", "Drafting", "RosterLocked"]


def extract_block(code, source):
    pattern = (r"map\[ModeHLifecycle\." + re.escape(source)
               + r"\] = new ModeHLifecycle\[\][\s\S]*?\};")
    match = re.search(pattern, code)
    return match.group(0) if match else None


def main():
    errors = []

    machine = read_text(MACHINE)
    if machine is None:
        errors.append("[File] 缺少 ModeH/ModeHStateMachine.cs")
        print("ModeHStateMachineGuard: FAIL (1 errors)")
        print("  - " + errors[0])
        return 1
    code = strip_cs_comments(machine)

    transitions_match = re.search(
        r"BuildTransitions\(\)\s*\{[\s\S]*?return map;", code)
    if not transitions_match:
        print("ModeHStateMachineGuard: FAIL (1 errors)")
        print("  - [Table] 未找到 BuildTransitions 转换表实现")
        return 1
    transitions_section = transitions_match.group(0)

    for source, targets in REQUIRED_EDGES:
        block = extract_block(transitions_section, source)
        if block is None:
            errors.append("[Table] 缺少转换表条目: " + source)
            continue
        for target in targets:
            if "ModeHLifecycle.{}".format(target) not in block:
                errors.append("[Table] {} 缺少合法目标: {}".format(source, target))

    # 入口五态：ProductionCertifying 认证失败必须能回 None
    cert_block = extract_block(transitions_section, "ProductionCertifying")
    if cert_block and "ModeHLifecycle.MatchBrief" in cert_block:
        errors.append("[Table] ProductionCertifying 不得直接进入 MatchBrief")

    # 主干不得要求 StakePrepared
    locked_block = extract_block(transitions_section, "LoadoutLocked")
    if locked_block and "ModeHLifecycle.MatchSpawning" not in locked_block:
        errors.append("[Table] LoadoutLocked 必须能直接进入 MatchSpawning")

    # Terminal 回环
    ended_block = extract_block(transitions_section, "SeasonEnded")
    if ended_block:
        for forbidden in ["MatchBrief", "Intermission", "Drafting", "MatchFighting"]:
            if "ModeHLifecycle.{}".format(forbidden) in ended_block:
                errors.append("[Table] SeasonEnded 存在 Terminal 回环: " + forbidden)
    none_block = extract_block(transitions_section, "None")
    if none_block:
        targets = re.findall(r"ModeHLifecycle\.(\w+)", none_block)
        if [t for t in targets if t not in ("None", "EntryIntent")]:
            errors.append("[Table] None 只能进入 EntryIntent")

    # 统一异常出口
    for source in UNIFIED_FAILURE_SOURCES:
        if "ModeHLifecycle.{},".format(source) not in code and \
                "ModeHLifecycle.{}\r\n".format(source) not in code and \
                "ModeHLifecycle.{}\n".format(source) not in code:
            errors.append("[Failure] 统一异常出口未覆盖: " + source)
    failure_section = re.search(
        r"UnifiedFailureSources = new ModeHLifecycle\[\][\s\S]*?\};", code)
    if not failure_section:
        errors.append("[Failure] 缺少统一异常出口清单")
    else:
        body = failure_section.group(0)
        for source in UNIFIED_FAILURE_SOURCES:
            if "ModeHLifecycle.{}".format(source) not in body:
                errors.append("[Failure] 统一异常出口清单缺少: " + source)
        for forbidden in ["EntryIntent", "SceneLoading", "ProductionCertifying", "Drafting",
                          "RosterLocked", "Recovering", "Suspended", "SeasonEnded",
                          "ErrorRecoveryPending"]:
            if "ModeHLifecycle.{}".format(forbidden) in body:
                errors.append("[Failure] 统一异常出口不得覆盖: " + forbidden)

    # 早期恢复子表
    early_match = re.search(r"BuildEarlyRecoveryTargets\(\)\s*\{[\s\S]*?return map;", code)
    if not early_match:
        errors.append("[EarlyRecovery] 缺少早期恢复子表")
    else:
        body = early_match.group(0)
        for source in EARLY_RECOVERY_SOURCES:
            if "map[ModeHLifecycle.{}]".format(source) not in body:
                errors.append("[EarlyRecovery] 子表缺少源状态: " + source)
        # 早期恢复出口不得直接跳到 MatchBrief
        if "ModeHLifecycle.MatchBrief" in body:
            errors.append("[EarlyRecovery] 早期恢复出口不得直接跳到 MatchBrief")

    # 单点转换与 CAS
    checks = [
        (r"public static bool TryTransition\(", "唯一转换入口"),
        (r"if \(!runState\.IsOwnerTokenValid\(ownerToken\)\)", "owner token 校验"),
        (r"if \(runState\.Lifecycle != expected\)", "expected CAS 校验"),
        (r"if \(!IsTransitionAllowed\(expected, next\)\)", "转换表校验"),
        (r"state_early_recovery_target_rejected", "早期恢复目标拒绝"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Machine] 不满足: " + desc)

    run_state = read_text(RUN_STATE)
    if run_state is None:
        errors.append("[File] 缺少 ModeH/ModeHRunState.cs")
    else:
        rs = strip_cs_comments(run_state)
        if not re.search(r"internal ModeHTransitionRecord ApplyTransition\(", rs):
            errors.append("[RunState] lifecycle 只能由 internal ApplyTransition 修改")
        if not re.search(r"public long OwnerToken \{ get; private set; \}", rs):
            errors.append("[RunState] 缺少内存 owner token")
        to_dto = re.search(r"public ModeHRunStateDto ToDto\(\)[\s\S]*?return dto;", rs)
        if to_dto and re.search(r"dto\.\w*[Oo]wnerToken", to_dto.group(0)):
            errors.append("[RunState] owner token 不得写入 DTO")
        if not re.search(r"public bool TryApplyEventToken\(string eventTokenId\)", rs):
            errors.append("[RunState] 缺少事件 token CAS")
        if not re.search(r"public bool IsCallbackValid\(long token, string runId, int sceneGeneration\)", rs):
            errors.append("[RunState] 延迟回调必须比较 owner/runId/sceneGeneration")

    model = read_modeh_group("ModeHStateModel.cs", "ModeHStateDtos.cs")
    if model:
        mcode = strip_cs_comments(model)
        # lifecycle 字段只能被状态机路径写：DTO 之外不得出现第二个权威状态
        if re.search(r"public\s+ModeHMatchPhase\s+\w+\s*;", mcode):
            errors.append("[Model] 不得持久化第二个权威状态 matchPhase")

    # DTO 中不得出现 ownerToken 字段
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        text = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        if re.search(r"public\s+long\s+\w*[Oo]wnerToken\s*;", text):
            errors.append("[RunState] {} 的 DTO 中出现 ownerToken 字段".format(name))

    if errors:
        print("ModeHStateMachineGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHStateMachineGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
