#!/usr/bin/env python3
"""
PetNestCompanionNeutralizationGuard — 遗种巢幼体中性化守卫（实施计划 步骤 0）。

不变式：
- 中性化五件套齐全且写在 clone preset 上：
  hasSkill=false / exp=0 / hasSoul=false / team=Teams.player / dropBoxOnDead=false；
- 五件套必须发生在 CreateCharacterAsync 调用**之前**（await 窗口期间，波次统计、
  掉落、经验、成就只能看到 clone 身份）；
- 只克隆不改源 preset：中性化只能作用于 Instantiate 出来的 clone；
- CreateCharacterAsync 一律 group=null / isLeader=false（成组会双向同步 searchedEnemy）；
- 只缩 modelRoot，不缩角色根 transform（碰撞体只在 SetCharacterModel 内算一次）；
- 回收是幂等的 CleanupOnce，顺序为「组件 -> 角色 -> clone preset」；
- 跟随只写 AICharacterController.leader，不写 searchedEnemy / leaderAI。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    read_petnest,
    report,
    strip_cs_comments,
)

GUARD = "PetNestCompanionNeutralizationGuard"

# 中性化五件套：(正则, 人类可读描述)
NEUTRALIZATION_FIVE = [
    (r"clone\.hasSkill\s*=\s*false\s*;", "hasSkill=false（幼体不带 Boss 技能）"),
    (r"clone\.exp\s*=\s*0\s*;", "exp=0（幼体不给经验）"),
    (r"clone\.hasSoul\s*=\s*false\s*;", "hasSoul=false（幼体不掉灵魂方块）"),
    (r"clone\.team\s*=\s*Teams\.player\s*;", "team=Teams.player（玩家方随从）"),
    (r"clone\.dropBoxOnDead\s*=\s*false\s*;", "dropBoxOnDead=false（幼体不掉落箱）"),
]


def main():
    errors = []

    spawner = read_petnest("PetNestCompanionSpawner.cs")
    if spawner is None:
        return report(GUARD, ["[File] 缺少 PetNest/PetNestCompanionSpawner.cs"])
    code = strip_cs_comments(spawner)

    # 1. 五件套齐全
    for pattern, desc in NEUTRALIZATION_FIVE:
        if not re.search(pattern, code):
            errors.append("[中性化] 缺少五件套条目: " + desc)

    # 2. 中性化必须写在独立入口里，便于复用与断言
    if not re.search(r"internal static void NeutralizeClonePreset\(CharacterRandomPreset clone\)", code):
        errors.append("[中性化] 缺少统一入口 NeutralizeClonePreset(CharacterRandomPreset clone)")

    # 3. 五件套必须发生在 CreateCharacterAsync 之前
    create_pos = code.find("CreateCharacterAsync(")
    neutralize_call = re.search(r"NeutralizeClonePreset\(clone\)\s*;", code)
    if create_pos < 0:
        errors.append("[创建] 未找到 CreateCharacterAsync 调用")
    elif neutralize_call is None:
        errors.append("[中性化] 创建前未调用 NeutralizeClonePreset(clone)")
    elif neutralize_call.start() > create_pos:
        errors.append("[顺序] NeutralizeClonePreset 必须发生在 CreateCharacterAsync 之前")

    # 4. 只克隆不改源 preset
    if not re.search(r"clone\s*=\s*UnityEngine\.Object\.Instantiate\(sourcePreset\)\s*;", code):
        errors.append("[克隆] 必须 Instantiate(sourcePreset) 得到 clone，禁止直接改源 preset")
    for forbidden in ["sourcePreset.hasSkill", "sourcePreset.exp", "sourcePreset.hasSoul",
                      "sourcePreset.team", "sourcePreset.dropBoxOnDead"]:
        if forbidden + " =" in code or forbidden + "=" in code:
            errors.append("[克隆] 禁止写源 preset 字段: " + forbidden)

    # 5. group=null / isLeader=false
    if not re.search(r"CreateCharacterAsync\(\s*[\s\S]{0,200}?,\s*null,\s*false\s*\)", code):
        errors.append("[创建] CreateCharacterAsync 必须传 group=null、isLeader=false")

    # 6. 只缩 modelRoot，不缩角色根
    if not re.search(r"modelRoot\.localScale\s*=", code):
        errors.append("[缩放] 缺少 modelRoot.localScale 视觉缩放")
    if re.search(r"Character\.transform\.localScale\s*=", code) or \
            re.search(r"character\.transform\.localScale\s*=", code):
        errors.append("[缩放] 禁止缩放角色根 transform（碰撞体只在 SetCharacterModel 内计算一次）")

    # 7. 幂等 CleanupOnce，且顺序为 组件 -> 角色 -> clone preset
    if not re.search(r"internal static void CleanupOnce\(PetNestCompanionHandle handle\)", code):
        errors.append("[回收] 缺少幂等入口 CleanupOnce(PetNestCompanionHandle handle)")
    if not re.search(r"if \(handle == null \|\| handle\.CleanedUp\) return;\s*handle\.CleanedUp = true;", code):
        errors.append("[回收] CleanupOnce 缺少 CleanedUp 幂等早返")
    cleanup = re.search(r"internal static void CleanupOnce\(PetNestCompanionHandle handle\)[\s\S]{0,1600}?\n        \}", code)
    if cleanup:
        body = cleanup.group(0)
        agent_pos = body.find("handle.Agent")
        char_pos = body.find("handle.Character.gameObject")
        clone_pos = body.find("DestroyClone(handle.ClonePreset)")
        if agent_pos < 0 or char_pos < 0 or clone_pos < 0:
            errors.append("[回收] CleanupOnce 必须依次处理 Agent / Character / ClonePreset")
        elif not (agent_pos < char_pos < clone_pos):
            errors.append("[回收] 回收顺序必须是「组件 -> 角色 -> clone preset」")

    # 8. 激活幂等
    if not re.search(r"if \(handle\.Activated\)\s*\{\s*return true;", code):
        errors.append("[激活] TryActivate 缺少 Activated 幂等早返")

    # 9. 血脉解析 fail-closed：找不到 nameKey 只返回 null，不回落同阵营强敌
    if "showName" in code:
        errors.append("[解析] 血脉解析禁止回落到 showName 同阵营强敌，必须 fail-closed")

    # ---- 跟随组件侧 ----
    agent = read_petnest("PetNestCompanionAgent.cs")
    if agent is None:
        errors.append("[File] 缺少 PetNest/PetNestCompanionAgent.cs")
    else:
        acode = strip_cs_comments(agent)
        if not re.search(r"_ai\.leader\s*=\s*_master\s*;", acode):
            errors.append("[跟随] 必须通过写 AICharacterController.leader 驱动跟随")
        if re.search(r"_ai\.searchedEnemy\s*=", acode):
            errors.append("[跟随] 禁止写 searchedEnemy：索敌作战交还原生行为树")
        if re.search(r"\.leaderAI\s*=", acode):
            errors.append("[跟随] 禁止写 leaderAI：会与成员双向同步目标")
        if "TeleportDistance = 40f" not in acode:
            errors.append("[跟随] 缺少 >40m 传送兜底阈值（官方 PetAI 同款）")
        if "MaintainIntervalSeconds = 0.25f" not in acode:
            errors.append("[性能] 维护必须节流到 4Hz")
        if not re.search(r"private void OnDestroy\(\)[\s\S]{0,300}?UnregisterIdentity\(\)", acode):
            errors.append("[生命周期] OnDestroy 必须退出静态身份表")
        if "internal static bool IsCompanionArmed" not in acode:
            errors.append("[性能] 缺少 IsCompanionArmed 零分配 bool 快路径")

    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
