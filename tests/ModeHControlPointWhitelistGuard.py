#!/usr/bin/env python3
"""
ModeHControlPointWhitelistGuard — 控制点白名单守卫（设计提案 §17.6.2、§26.1）。

不变式：
- ModeHCommandAdapters.cs 与 ModeHInjuryAndScarSystem.cs 只引用 §17.6.2 白名单控制点；
- 显式禁止 combatMoveRange / patrolRange / traceTargetChance / forgetTime / shootDelay /
  canDash / melee / hearingAbility / scatterMulti*；
- 不直接写 reactionTime（只写 baseReactionTime，它每秒被重算）；
- nextReleaseSkillTimeMarker 没有还原分支；
- 任何一次性写 MoveToPos / searchedEnemy / SetNoticedToTarget 都必须挂在重申循环上；
- Commands.json / Scars.json 里出现的控制点也必须落在白名单内。
"""
import io
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

ADAPTERS = os.path.join(REPO_ROOT, "ModeH", "ModeHCommandAdapters.cs")
INJURY = os.path.join(REPO_ROOT, "ModeH", "ModeHInjuryAndScarSystem.cs")
COMMANDS_JSON = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", "Commands.json")
SCARS_JSON = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", "Scars.json")

# §17.6.2 唯一允许的控制点
WHITELIST = {
    "skillSuccessChance",
    "skillCoolTimeRange",
    "nextReleaseSkillTimeMarker",
    "hasSkill",
    "itemSkillChance",
    "itemSkillCoolTime",
    "shootCanMove",
    "sightDistance",
    "sightAngle",
    "combatTurnSpeed",
    "patrolTurnSpeed",
    "baseReactionTime",
    "searchedEnemy",
    "setNoticedToTarget",
    "moveToPos",
    # Mode H 自结算伪控制点（不写原版字段）
    "coward_mitigation",
    "command_scale",
}

FORBIDDEN = [
    "combatMoveRange",
    "patrolRange",
    "traceTargetChance",
    "forgetTime",
    "shootDelay",
    "canDash",
    "hearingAbility",
    "scatterMultiIfOffScreen",
    "scatterMultiIfTargetRunning",
    "shootTimeRange",
    "combatMoveTimeRange",
    "dashCoolTimeRange",
    "meleeWaitTime",
]


def load_json(path):
    if not os.path.exists(path):
        return None
    with io.open(path, "r", encoding="utf-8") as fh:
        try:
            return json.load(fh)
        except ValueError:
            return None


def collect_control_points(document, sections):
    points = set()
    for section in sections:
        for entry in document.get(section) or []:
            for effect in entry.get("effects") or entry.get("components") or []:
                cp = effect.get("controlPointId")
                if cp:
                    points.add(cp)
    return points


def main():
    errors = []

    adapters = read_text(ADAPTERS)
    if adapters is None:
        print("ModeHControlPointWhitelistGuard: FAIL (1 errors)")
        print("  - [File] 缺少 ModeH/ModeHCommandAdapters.cs")
        return 1
    code = strip_cs_comments(adapters)

    for forbidden in FORBIDDEN:
        if re.search(r"\b{}\b".format(re.escape(forbidden)), code):
            errors.append("[Adapters] 出现禁止的控制点: " + forbidden)

    # melee 单独判定：允许出现在 MeleeWeapon 等无关标识里，只禁止裸 ai.melee
    if re.search(r"_ai\.melee\b", code) or re.search(r"\.melee\s*=", code):
        errors.append("[Adapters] 不得写 melee（TagillaAI 每帧按距离回写）")

    # 不得直接写 reactionTime
    if re.search(r"\breactionTime\s*=", code) and not re.search(r"baseReactionTime\s*=", code):
        errors.append("[Adapters] 不得直接写 reactionTime")
    if re.search(r"_ai\.reactionTime", code):
        errors.append("[Adapters] 不得直接读写 reactionTime，只允许 baseReactionTime")

    checks = [
        (r"public bool Apply\(", "Apply 段"),
        (r"public void Reassert\(", "Reassert 段"),
        (r"public void Restore\(\)", "Restore 段"),
        (r"public List<string> Validate\(\)", "Validate 段"),
        (r"ModeHConfig\.CommandReassertIntervalSeconds", "引用 0.1 秒重申常量"),
        (r"modulation\.OriginalFloat = original;", "保存原值快照"),
        (r"private void ApplyMarker\(ModeHEffectSpec effect\)[\s\S]{0,900}?modulation\.Restore = false;",
         "marker 写入后不还原（只交还原版所有权）"),
        (r"if \(!m\.Restore\) continue;", "还原时跳过不还原项"),
        (r"effect\.Op\.StartsWith\(\"fire_\", StringComparison\.Ordinal\)", "点火挂在重申循环上"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Adapters] 不满足: " + desc)

    # nextReleaseSkillTimeMarker 不得出现在还原分支
    restore = re.search(r"public void Restore\(\)[\s\S]*?\n        \}", code)
    if restore and "nextReleaseSkillTimeMarker" in restore.group(0):
        errors.append("[Adapters] nextReleaseSkillTimeMarker 不得出现在还原分支")

    injury = read_text(INJURY)
    if injury is not None:
        icode = strip_cs_comments(injury)
        for forbidden in FORBIDDEN:
            if re.search(r"\b{}\b".format(re.escape(forbidden)), icode):
                errors.append("[Injury] 出现禁止的控制点: " + forbidden)
        if re.search(r"\.reactionTime\s*=", icode):
            errors.append("[Injury] 不得直接写 reactionTime")

    commands = load_json(COMMANDS_JSON)
    if commands is None:
        errors.append("[Data] 缺少或无法解析 Commands.json")
    else:
        declared = set(commands.get("controlPointWhitelist") or [])
        if not declared.issubset(WHITELIST):
            errors.append("[Data] Commands.json 白名单超出 §17.6.2: "
                          + str(sorted(declared - WHITELIST)))
        used = collect_control_points(commands, ["commonCommands", "signatureCommands"])
        for cp in sorted(used):
            if cp not in WHITELIST:
                errors.append("[Data] Commands.json 使用了非白名单控制点: " + cp)

    scars = load_json(SCARS_JSON)
    if scars is None:
        errors.append("[Data] 缺少或无法解析 Scars.json")
    else:
        used = collect_control_points(scars, ["injuries", "scars"])
        for cp in sorted(used):
            if cp not in WHITELIST:
                errors.append("[Data] Scars.json 使用了非白名单控制点: " + cp)

    if errors:
        print("ModeHControlPointWhitelistGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHControlPointWhitelistGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
