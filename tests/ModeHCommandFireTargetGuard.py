#!/usr/bin/env python3
"""
ModeHCommandFireTargetGuard — Mode H 口令点火目标守卫（设计提案 §17.6.4、§26.1）。

【为什么需要这条守卫】
2026-09-03 查出：`ModeHCommandFireContext.NearestEnemy` / `LowestHealthEnemy`
两个字段有消费者（ModeHCommandAdapter.Fire 的 fire_notice_nearest 与
fire_lowest_health_target 两个 op），但**没有任何生产者**——唯一的设值口
`ModeHCombatControl.SetFireTargets(...)` 全仓库零调用点。后果：

- `finish` 口令（Commands.json，intent=execute）两个 effect 全依赖这两个字段，
  整条退化成空操作；玩家每场只有一次拍铃，选它等于什么都没做；
- `press` 口令 4 个 effect 里的 setNoticedToTarget 一项失效；
- Scars.json 里带 fire_lowest_health_target 的战痕同样空转。

而且它**查不出来**：ModeHCommandAdapter.Validate() 对这两个控制点的判据是
`_ai.searchedEnemy != null` / `_ai.noticed`——AI 自己有目标就算"保持住了"，
于是生产认证照样把 finish 标成 VerifiedBehavior 发给玩家选，遥测也报 held。
编译绿、guard 绿、认证绿，功能不存在。

本守卫锁住修复后的形态：目标必须在 ModeHCombatControl 内部按存活敌军名单算出来。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

CONTROL = os.path.join(REPO_ROOT, "ModeH", "ModeHCombatControl.cs")
TELEMETRY = os.path.join(REPO_ROOT, "ModeH", "ModeHCombatTelemetry.cs")
ADAPTERS = os.path.join(REPO_ROOT, "ModeH", "ModeHCommandAdapters.cs")
INJURY = os.path.join(REPO_ROOT, "ModeH", "ModeHInjuryAndScarSystem.cs")

# 依赖点火目标的 op：这两个是「有消费者就必须有生产者」的那两条
TARGET_OPS = ["fire_notice_nearest", "fire_lowest_health_target"]


def fail(message):
    print("ModeHCommandFireTargetGuard: FAIL - " + message)
    return 1


def main():
    errors = []

    control = read_text(CONTROL)
    telemetry = read_text(TELEMETRY)
    adapters = read_text(ADAPTERS)
    injury = read_text(INJURY)
    for path, text in [
        (CONTROL, control), (TELEMETRY, telemetry),
        (ADAPTERS, adapters), (INJURY, injury),
    ]:
        if text is None:
            errors.append("[File] 缺少 " + os.path.basename(path))
    if errors:
        print("ModeHCommandFireTargetGuard: FAIL (%d errors)" % len(errors))
        for e in errors:
            print("  - " + e)
        return 1

    control_code = strip_cs_comments(control)
    telemetry_code = strip_cs_comments(telemetry)
    adapters_code = strip_cs_comments(adapters)
    injury_code = strip_cs_comments(injury)

    # 1) 消费侧仍在（守卫的前提没变）
    for op in TARGET_OPS:
        if op not in adapters_code:
            errors.append("[Consumer] 点火 op 消失，本守卫的前提已变，请复核: " + op)

    # 2) 生产侧：目标必须由 RefreshFireTargets 在战斗控制内部算出
    if not re.search(r"private void RefreshFireTargets\(float deltaTime, bool force\)", control_code):
        errors.append("[Producer] 缺少 RefreshFireTargets(float, bool)：点火目标又没有生产者了")

    targets = re.search(
        r"private void RefreshFireTargets\(float deltaTime, bool force\)[\s\S]*?\n        \}",
        control_code)
    if targets is None:
        errors.append("[Producer] 无法定位 RefreshFireTargets 方法体")
    else:
        body = targets.group(0)
        # 必须真的赋一个目标进去，不能只有开头那两句清空。
        # （第一版这里写的是 `field not in body`，而清空语句本身就含字段名，
        #  把真正的赋值删掉照样 PASS —— 反向验证时抓到的，故收紧成"赋非 null"。）
        for field in ["NearestEnemy", "LowestHealthEnemy"]:
            assigned = re.findall(
                r"_fireContext\." + field + r"\s*=\s*([A-Za-z_][A-Za-z0-9_.]*)\s*;", body)
            if not [v for v in assigned if v != "null"]:
                errors.append(
                    "[Producer] RefreshFireTargets 只清空未赋值 _fireContext." + field
                    + "：目标会恒为 null，点火 op 静默空转")
        # 名单来源必须是遥测的存活敌军，不得改成场景扫描
        if "_telemetry.GetLiveEnemyAt(" not in body:
            errors.append("[Producer] 必须按遥测的存活敌军名单取目标（GetLiveEnemyAt）")
        if "mainDamageReceiver" not in body:
            errors.append("[Producer] 目标必须是官方 mainDamageReceiver（searchedEnemy 的类型）")
        # 热路径纪律：不得在这里做场景查找或分配
        for forbidden in ["FindObjectsOfType", "FindObjectOfType", "GetComponentsInChildren",
                          "new List", "Physics.Overlap"]:
            if forbidden in body:
                errors.append("[HotPath] RefreshFireTargets 是每帧路径，不得出现: " + forbidden)

    # 3) 节流必须对齐重申节奏，且拍铃强制重扫
    if "ModeHConfig.CommandReassertIntervalSeconds" not in control_code:
        errors.append("[HotPath] 目标重扫节流必须引用 CommandReassertIntervalSeconds 冻结常量")
    if not re.search(r"RefreshFireContext\(0f, true\)", control_code):
        errors.append("[Bell] 拍铃必须 RefreshFireContext(0f, true) 强制重扫，不得用缓存目标")
    if not re.search(r"RefreshFireContext\(deltaTime, false\)", control_code):
        errors.append("[HotPath] Tick 必须走节流分支 RefreshFireContext(deltaTime, false)")

    # 4) 每场重置：不得把上一场已销毁的敌军引用带进新一场
    begin = re.search(r"public void BeginMatch\([\s\S]*?\n        \}", control_code)
    if begin is not None:
        for field in ["_fireContext.NearestEnemy", "_fireContext.LowestHealthEnemy"]:
            if field not in begin.group(0):
                errors.append("[Lifecycle] BeginMatch 必须清空 " + field)

    # 5) 旧的外部设值口不得回来：它是本 bug 的成因（有设值口 => 没人调 => 恒 null）
    if "SetFireTargets" in control_code:
        errors.append(
            "[Producer] 不得恢复 SetFireTargets 外部设值口："
            "目标只应由 RefreshFireTargets 内部计算，外部设值口历史上从未被调用过")

    # 6) 战痕开窗的首次点火必须用转发进来的活上下文，不能用空上下文
    if "_sharedFireContext" not in injury_code:
        errors.append(
            "[Scar] 伤病/战痕系统必须缓存战斗控制器转发的活上下文（_sharedFireContext），"
            "否则带 fire_lowest_health_target 的战痕在开窗那一下会空转")

    # 7) 遥测的零分配访问口
    if not re.search(r"public ModeHParticipantRef GetLiveEnemyAt\(int index\)", telemetry_code):
        errors.append("[Telemetry] 缺少 GetLiveEnemyAt(int)：目标扫描需要零分配遍历存活敌军")

    if errors:
        print("ModeHCommandFireTargetGuard: FAIL (%d errors)" % len(errors))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHCommandFireTargetGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
