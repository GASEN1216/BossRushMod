#!/usr/bin/env python3
"""
PetNestModeGateGuard — 遗种巢随从进局的模式门控守卫（实施计划 步骤 6）。

不变式：
- 禁入名单成文且一刀切：Mode G、末日丧尸、Mode H 命中即拒，不做局内特判；
- 允许名单：标准三档 / 竞技场 / Mode D / Mode E / Mode F；
- **只经公开只读门面判定**：不得引用 ModeG / ZombieMode / ModeH 的内部符号
  （ModeGRuntimeGates / ZombieModePhaseGuards / ModeHRuntimeGates / 各自 RunState /
  LifecyclePhase 等），只能用 ModBehaviour 上的 wrapper 与公开属性；
- 判定 no-throw，异常 fail-closed 为"不允许带崽"；
- 门控是随从入场的前置：CompanionRuntime 必须先查门控再生成。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import read_petnest, report, strip_cs_comments  # noqa: E402

GUARD = "PetNestModeGateGuard"

# 其他模式的内部符号：门控引用任何一个都说明越过了公开门面
FORBIDDEN_INTERNAL_SYMBOLS = [
    "ModeGRuntimeGates",
    "ModeGRunContext",
    "ModeGRunState",
    "ModeHRuntimeGates",
    "ModeHRunState",
    "ModeHLifecycle",
    "ZombieModePhaseGuards",
    "ZombieModeRunState",
    "ZombieModeLifecyclePhase",
    "zombieModeRunState",
]

REQUIRED_BANS = [
    ("ModBehaviour.IsModeGRunInProgressSafe()", "Mode G 禁入"),
    ("owner.IsZombieModeActive", "末日丧尸禁入"),
    ("ModBehaviour.IsModeHRunInProgressSafe()", "Mode H 禁入"),
]

REQUIRED_ALLOWS = [
    ("owner.IsActive", "标准档"),
    ("owner.IsBossRushArenaActive", "竞技场"),
    ("owner.IsModeDActive", "Mode D"),
    ("owner.IsModeEActive", "Mode E"),
    ("owner.IsModeFActive", "Mode F"),
]


def main():
    errors = []

    text = read_petnest("PetNestModeGate.cs")
    if text is None:
        return report(GUARD, ["[File] 缺少 PetNest/PetNestModeGate.cs"])
    code = strip_cs_comments(text)

    # 1. 不得引用其他模式的内部符号
    for symbol in FORBIDDEN_INTERNAL_SYMBOLS:
        if symbol in code:
            errors.append("[门面] 门控不得引用其他模式内部符号: " + symbol)

    # 2. 禁入名单齐全
    for token, desc in REQUIRED_BANS:
        if token not in code:
            errors.append("[禁入] 缺少禁入判定: " + desc + "（" + token + "）")

    # 3. 允许名单齐全
    for token, desc in REQUIRED_ALLOWS:
        if token not in code:
            errors.append("[允许] 缺少允许判定: " + desc + "（" + token + "）")

    # 4. 禁入原因 id 成文
    for const in ["ReasonModeG", "ReasonZombie", "ReasonModeH",
                  "ReasonNoRunActive", "ReasonQueryFailed"]:
        if const not in code:
            errors.append("[诊断] 缺少稳定原因常量: " + const)

    # 5. 禁入判定必须在允许名单之前（命中即拒，不做局内特判）
    ban_pos = code.find("ModBehaviour.IsModeGRunInProgressSafe()")
    allow_pos = code.find("bool allowed = owner.IsActive")
    if ban_pos < 0 or allow_pos < 0:
        errors.append("[顺序] 无法定位禁入名单与允许名单")
    elif ban_pos > allow_pos:
        errors.append("[顺序] 禁入名单必须先于允许名单判定")

    # 6. no-throw + fail-closed
    entry = re.search(r"internal static bool IsCompanionAllowed\(ModBehaviour owner, out string blockReasonId\)[\s\S]{0,2000}?\n        \}", code)
    if entry is None:
        errors.append("[入口] 缺少 IsCompanionAllowed(ModBehaviour, out string)")
    else:
        body = entry.group(0)
        if "catch (Exception" not in body:
            errors.append("[fail-closed] 门控判定必须 no-throw")
        if "blockReasonId = ReasonQueryFailed;" not in body:
            errors.append("[fail-closed] 异常必须 fail-closed 为不允许带崽")

    # 7. 入场前必须查门控
    runtime = read_petnest("PetNestCompanionRuntime.cs")
    if runtime is None:
        errors.append("[File] 缺少 PetNest/PetNestCompanionRuntime.cs")
    else:
        rcode = strip_cs_comments(runtime)
        if "PetNestModeGate.IsCompanionAllowed(owner, out blockReasonId)" not in rcode:
            errors.append("[接线] 随从入场前必须查模式门控")
        # 生成前查一次，await 之后重验一次
        if rcode.count("PetNestModeGate.IsCompanionAllowed(") < 2:
            errors.append("[接线] await 之后必须重验门控（模式可能在生成期间中止）")

    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
