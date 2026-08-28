#!/usr/bin/env python3
"""
PetNestHatchCommitGuard — 遗种巢孵化 commit-before-reveal 守卫（实施计划 步骤 5）。

不变式：
- **commit-before-reveal**：孵化结果必须先落档再交给演出层。HatchService 因此
  不得 import 任何 View / UI 符号；演出层只读已 commit 的结果；
- **fail-closed 不消耗蛋**：血脉 KV 读不到、或血脉不在目录里时返回失败且不销毁蛋
  （官方 preset 改名会让老蛋血脉漂移，绝不能把玩家的蛋吞掉）；
- 入巢成功之后才消耗蛋；消耗失败必须回滚已入巢的崽（否则一枚蛋孵两只）；
- 凝蛋是事务：先扣遗魂再入巢，入巢失败必须退还遗魂；
- roll 三层（天赋 ×2 / 性格 ×1 / 异色）走 PetNestTuning 常量，孵化即锁定；
- 天赋里的 PetCapcity 必须是常量加（格子数），不能按百分比。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import read_petnest, report, strip_cs_comments  # noqa: E402

GUARD = "PetNestHatchCommitGuard"

# 演出 / UI 符号：HatchService 出现任何一个都说明 reveal 侵入了 commit 路径
FORBIDDEN_VIEW_SYMBOLS = [
    "PetNestHatchRevealView",
    "PetNestExpeditionRevealView",
    "PetNestCompanionHudView",
    "PetNestUI",
    "BossRushUI",
    "ZombieModeUIHelper",
    "UnityEngine.UI",
    "TMPro",
    "Canvas",
    "LotteryBox",
    "ItemPicker",
]


def main():
    errors = []

    text = read_petnest("PetNestHatchService.cs")
    if text is None:
        return report(GUARD, ["[File] 缺少 PetNest/PetNestHatchService.cs"])
    code = strip_cs_comments(text)

    # 1. commit-before-reveal：不得引用任何 View 符号
    for symbol in FORBIDDEN_VIEW_SYMBOLS:
        if symbol in code:
            errors.append("[commit-before-reveal] 孵化服务不得引用演出/UI 符号: " + symbol)

    # 2. fail-closed 不消耗蛋
    hatch = re.search(r"internal static bool TryHatchEgg\([\s\S]{0,2600}?\n        \}", code)
    if hatch is None:
        errors.append("[入口] 缺少 TryHatchEgg(Item, out PetNestHatchResult, out string)")
    else:
        body = hatch.group(0)
        if 'failureReasonId = "lineage_unknown";' not in body:
            errors.append("[fail-closed] 血脉不可识别时必须给出 lineage_unknown 并保留蛋")
        # 血脉判定必须发生在消耗蛋之前
        lineage_pos = body.find("RelicEggConfig.ReadLineage(egg)")
        consume_pos = body.find("TryConsumeEgg(egg)")
        if lineage_pos < 0 or consume_pos < 0:
            errors.append("[顺序] 无法定位血脉判定与消耗蛋的位置")
        elif lineage_pos > consume_pos:
            errors.append("[fail-closed] 血脉判定必须发生在消耗蛋之前")
        # 入巢在消耗之前，且消耗失败要回滚
        add_pos = body.find("PetNestService.TryAddPet(pet, out failureReasonId)")
        if add_pos < 0:
            errors.append("[顺序] 孵化必须经 PetNestService.TryAddPet 入巢")
        elif add_pos > consume_pos:
            errors.append("[commit-before-reveal] 必须先入巢落档，成功后才消耗蛋")
        if "PetNestService.TryRemovePet(pet.id, out rollbackReason)" not in body:
            errors.append("[回滚] 消耗蛋失败必须回滚已入巢的崽，避免一蛋两崽")

    # 3. 凝蛋事务：扣遗魂 -> 入巢 -> 失败退还
    condense = re.search(r"internal static bool TryCondenseAndHatch\([\s\S]{0,2200}?\n        \}", code)
    if condense is None:
        errors.append("[入口] 缺少 TryCondenseAndHatch 入口")
    else:
        body = condense.group(0)
        spend_pos = body.find("PetNestService.TrySpendSouls(")
        add_pos = body.find("PetNestService.TryAddPet(")
        refund_pos = body.find("PetNestService.AddSouls(lineageKey, PetNestTuning.SoulsPerCondensedEgg, true)")
        if spend_pos < 0 or add_pos < 0:
            errors.append("[事务] 凝蛋必须先扣遗魂再入巢")
        elif spend_pos > add_pos:
            errors.append("[事务] 扣遗魂必须发生在入巢之前")
        if refund_pos < 0:
            errors.append("[事务] 入巢失败必须退还遗魂")

    # 4. roll 走常量且孵化即锁定
    for const in ["PetNestTuning.ShinyChance", "PetNestTuning.TalentRollCount",
                  "PetNestTuning.AllPersonalityIds", "PetNestTuning.SoulsPerCondensedEgg"]:
        if const not in code:
            errors.append("[数值归位] roll 必须走常量: " + const)

    # 5. PetCapcity 天赋必须是常量加（格子数）
    cap = re.search(r'MakeTalent\("[\w]+", "PetCapcity", [0-9.]+f, (true|false)\)', code)
    if cap is None:
        errors.append("[天赋] 天赋池缺少 PetCapcity 条目")
    elif cap.group(1) != "false":
        errors.append("[天赋] PetCapcity 是格子数，必须常量加而不是百分比")

    # 6. 天赋不重复抽
    if "indices.RemoveAt(slot);" not in code:
        errors.append("[roll] 出身天赋必须不重复抽取")

    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
