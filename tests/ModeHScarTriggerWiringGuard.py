#!/usr/bin/env python3
"""
ModeHScarTriggerWiringGuard — Mode H 战痕触发接线守卫（设计提案 §17.4、§26.1）。

【为什么需要这条守卫】
2026-09-03 查出：`Assets/Data/ModeH/Scars.json` 里 8 条战痕，**只有 2 条真能开窗**。

`ModeHInjuryAndScarSystem.TryOpenScarWindow(scarId, triggerId, ...)` 用
`string.Equals(spec.Trigger, triggerId, StringComparison.Ordinal)` 逐字比对，
不匹配时 `return false` 且**不设** failureReasonId——调用方拿到 (false, null)，
只会当作"这次不该触发"，于是整条静默。三种坏法当时同时存在：

- 字面量对不上：`broken_shield_charge` 传 `armor_broken`，表里是 `armor_first_break`；
  `crowd_favorite` 传 `crowd_present`，表里是 `enemy_count`；
- 根本没有调用点：`blood_rush`、`longshot_memory`；
- 把常驻战痕当触发型调：`crowd_favorite` 的 windowSeconds=0，
  本该由 `ApplyStandingScars` 施加，多出来的触发调用永远返回 false。

战痕是 Mode H 的核心成长产出（打完一场获得永久战痕），
"拿到了但永远不生效"属于玩家看不见的静默失败。

本守卫按 JSON 反查代码，锁住三条不变式。
"""
import io
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

SCARS_JSON = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", "Scars.json")
CONTROL = os.path.join(REPO_ROOT, "ModeH", "ModeHCombatControl.cs")
SYSTEM = os.path.join(REPO_ROOT, "ModeH", "ModeHInjuryAndScarSystem.cs")
ADAPTERS = os.path.join(REPO_ROOT, "ModeH", "ModeHCommandAdapters.cs")
THREAT_PLANS = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", "ThreatPlans.json")

CALL_RE = re.compile(
    r'TryOpenScarWindow\(\s*"([A-Za-z0-9_]+)"\s*,\s*"([A-Za-z0-9_]+)"')


def main():
    errors = []

    try:
        data = json.load(io.open(SCARS_JSON, encoding="utf-8-sig"))
    except Exception as exc:
        print("ModeHScarTriggerWiringGuard: FAIL - 无法读取 Scars.json: %s" % exc)
        return 1

    scars = data.get("scars") or []
    if not scars:
        print("ModeHScarTriggerWiringGuard: FAIL - Scars.json 没有 scars 条目")
        return 1

    # 擂台条件 id 全集，用来校验 condition_<id> 指向真实存在的条件
    arena_condition_ids = set()
    try:
        plans = json.load(io.open(THREAT_PLANS, encoding="utf-8-sig"))
        for c in plans.get("arenaConditions") or []:
            if c.get("conditionId"):
                arena_condition_ids.add(c["conditionId"])
    except Exception as exc:
        errors.append("[Condition] 无法读取 ThreatPlans.json: %s" % exc)

    control = read_text(CONTROL)
    system = read_text(SYSTEM)
    if control is None or system is None:
        print("ModeHScarTriggerWiringGuard: FAIL - 缺少 ModeH 战斗控制或伤病战痕源码")
        return 1
    control_code = strip_cs_comments(control)
    system_code = strip_cs_comments(system)

    # 代码里出现的全部 (scarId, triggerId) 调用对
    calls = {}
    for scar_id, trigger_id in CALL_RE.findall(control_code):
        calls.setdefault(scar_id, set()).add(trigger_id)

    # 常驻战痕的施加路径必须还在
    if "ApplyStandingScars" not in control_code:
        errors.append("[Standing] ModeHCombatControl 必须调 ApplyStandingScars 施加常驻战痕")
    if not re.search(r"if \(spec\.WindowSeconds > 0\) continue;", system_code):
        errors.append("[Standing] ApplyStandingScars 必须只处理 windowSeconds == 0 的常驻战痕")

    for scar in scars:
        scar_id = scar.get("scarId")
        trigger = scar.get("trigger")
        window = scar.get("windowSeconds")
        if not scar_id or not trigger:
            errors.append("[Data] 战痕条目缺少 scarId 或 trigger: %r" % scar)
            continue

        if window and window > 0:
            # 触发型：必须有调用点，且 triggerId 与表里逐字相同
            triggers = calls.get(scar_id)
            if not triggers:
                errors.append(
                    "[Trigger] 触发型战痕 %s 没有任何 TryOpenScarWindow 调用点，"
                    "玩家拿到它也永远不生效" % scar_id)
            elif trigger not in triggers:
                errors.append(
                    "[Trigger] 战痕 %s 的 triggerId 与数据表对不上：代码传 %s，"
                    "Scars.json 是 %s（Ordinal 逐字比对，不匹配即静默失败）"
                    % (scar_id, "/".join(sorted(triggers)), trigger))
        else:
            # 常驻：不该出现在触发调用里
            if scar_id in calls:
                errors.append(
                    "[Standing] 常驻战痕 %s（windowSeconds=%r）不该走 TryOpenScarWindow："
                    "它由 ApplyStandingScars 施加，触发调用恒返回 false"
                    % (scar_id, window))

    # 自结算分量：数据表里 command_scale 有两种写法，两种都必须被识别。
    # 2026-09-03 之前只认 self_settled_command_scale，于是 bell_dependence 的
    # +20% 收益从未生效，而它的 -10% 代价照常生效——一条纯负面的"利弊绑定"，
    # 恰好违反本系统"不允许收益生效、代价失效"的冻结契约。
    if 'string.Equals(component.Op, "self_settled", StringComparison.Ordinal)' not in system_code \
            or '"command_scale"' not in system_code:
        errors.append(
            "[SelfSettled] ApplySelfSettledComponents 必须同时识别 "
            "op=self_settled_command_scale 与 (op=self_settled + controlPointId=command_scale)，"
            "否则用后一种写法的条目只生效代价不生效收益")
    if "gatedByCondition" not in system_code:
        errors.append(
            "[SelfSettled] 带条件门的条目（requiresEnemyCountAtLeast）必须跳过无条件施加，"
            "否则 spirit 的 x0.85 会与 OnEnemyCountChanged 叠成两次")

    # 数据侧反查：每个自结算 command_scale 分量都要能被上面两种写法之一命中
    for group in ("injuries", "scars"):
        for entry in data.get(group) or []:
            entry_id = entry.get("injuryId") or entry.get("scarId")
            for comp in entry.get("components") or []:
                if not comp.get("selfSettled"):
                    continue
                op = comp.get("op")
                cp = comp.get("controlPointId")
                known = (op in ("self_settled_command_scale", "self_settled_kit_slot_disabled")
                         or (op == "self_settled" and cp == "command_scale"))
                if not known:
                    errors.append(
                        "[SelfSettled] %s 的自结算分量 op=%r controlPointId=%r "
                        "没有任何 C# 分支会命中，该分量恒不生效" % (entry_id, op, cp))

    # ---- appliesWhen 条件层（owner 2026-09-03 拍板：随战斗持续求值）----
    adapters = read_text(ADAPTERS)
    if adapters is None:
        errors.append("[Condition] 缺少 ModeH/ModeHCommandAdapters.cs")
        adapters_code = ""
    else:
        adapters_code = strip_cs_comments(adapters)

    # 求值器必须存在，且必须挂在重申路径上——只在开窗时算一次是被明确否决的口径：
    # 常驻战痕在选手登场时开窗，那时敌军尚未生成，enemy_count_at_least_3 恒假，
    # crowd_favorite 的收益会永远拿不到。
    if "class ModeHEffectConditions" not in adapters_code:
        errors.append("[Condition] 缺少 ModeHEffectConditions 求值器")
    if "SyncConditionalEffects" not in adapters_code:
        errors.append(
            "[Condition] 缺少 SyncConditionalEffects：appliesWhen 必须随重申持续求值，"
            "分量要能随条件真伪上下线")
    reassert = re.search(r"public void Reassert\([\s\S]*?\n        \}\n", adapters_code)
    if reassert is None or "SyncConditionalEffects" not in reassert.group(0):
        errors.append("[Condition] Reassert 必须调用 SyncConditionalEffects，否则条件只在开窗时算一次")
    # 点火类分量也要受条件约束，否则"下线"只对调制类生效
    if reassert is not None and "ModeHEffectConditions.IsSatisfied" not in reassert.group(0):
        errors.append("[Condition] 重申式点火必须先过条件判定，否则点火分量会绕过条件继续重发")

    # 数据侧反查：每个 appliesWhen 取值都要有对应的判定分支
    seen_conditions = set()
    for group in ("injuries", "scars"):
        for entry in data.get(group) or []:
            entry_id = entry.get("injuryId") or entry.get("scarId")
            for comp in entry.get("components") or []:
                cond = comp.get("appliesWhen")
                if not cond:
                    continue
                seen_conditions.add(cond)
                handled = ('"%s"' % cond) in adapters_code or cond.startswith("condition_")
                if not handled:
                    errors.append(
                        "[Condition] %s 的分量条件 %r 在 ModeHEffectConditions 里没有判定分支，"
                        "该条件会被当成无条件生效" % (entry_id, cond))
                # condition_<id> 必须真的对应 ThreatPlans 的一个 arenaCondition
                if cond.startswith("condition_"):
                    cid = cond[len("condition_"):]
                    if cid not in arena_condition_ids:
                        errors.append(
                            "[Condition] %s 的 %r 指向的擂台条件 %r 不在 ThreatPlans.json 的 "
                            "arenaConditions 里，永远不会成立" % (entry_id, cond, cid))
                # 自结算分量只能带整场恒定的条件：_selfSettledCommandScale 是累乘标量，
                # 无法只撤销其中一项，所以它只在开窗时求值一次。
                if comp.get("selfSettled") and not cond.startswith("condition_"):
                    errors.append(
                        "[Condition] 自结算分量 %s 带了动态条件 %r。自结算系数是累乘标量、"
                        "只在开窗时求值一次，动态条件会算错；要么改成调制类分量，"
                        "要么把系数改成可撤销结构" % (comp.get("effectId"), cond))

    # 不匹配时静默是本 bug 的放大器：保留这一行为就必须保留本守卫
    if not re.search(
            r"if \(!string\.Equals\(spec\.Trigger, triggerId, StringComparison\.Ordinal\)\) return false;",
            system_code):
        errors.append(
            "[Contract] TryOpenScarWindow 的逐字 trigger 比对已改动，"
            "本守卫的比对口径需同步复核")

    if errors:
        print("ModeHScarTriggerWiringGuard: FAIL (%d errors)" % len(errors))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHScarTriggerWiringGuard: PASS (%d 条战痕，触发型逐字匹配，常驻不误走触发路径)"
          % len(scars))
    return 0


if __name__ == "__main__":
    sys.exit(main())
