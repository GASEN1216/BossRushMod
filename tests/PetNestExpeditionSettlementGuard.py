#!/usr/bin/env python3
"""
PetNestExpeditionSettlementGuard — 天灾远征结算守卫（实施计划 步骤 8）。

不变式：
- **现实时间计时**：用 DateTime.UtcNow.Ticks，不用 GameClock
  （GameClock 离线不推进，关掉游戏远征就冻住）；
- **回拨钳制**：剩余时间一律 max(0, returnTicks - now)，不得算出负倒计时；
- **死亡率随出发记录固化**：出发时写进记录，结算与刻碑读的是同一个数字，
  后续调数值不影响已出发的远征；
- **commit-before-reveal**：roll → 结果与 settled 标记先落档 → 翻牌只回放。
  因此本文件不 import 任何 View 符号，MarkRevealed 里不得出现任何 roll；
- 结算幂等：已 settled 直接返回，不重复 roll、不重复发奖；
- 发奖必须在落档之后（先落档再发奖，崩溃最多少发一次，不会重复领取）；
- 派出期间崽锁定；真死移除 PetRecord 并刻碑，碑上**必须**有风险档位；
- 纪念碑有上限，溢出合并为碑林计数。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import read_petnest, report, strip_cs_comments  # noqa: E402

GUARD = "PetNestExpeditionSettlementGuard"

FORBIDDEN_VIEW_SYMBOLS = [
    "PetNestExpeditionRevealView",
    "PetNestHatchRevealView",
    "PetNestUI",
    "BossRushUI",
    "UnityEngine.UI",
    "TMPro",
    "Canvas",
    "DeathLottery",
]


def main():
    errors = []

    text = read_petnest("PetNestExpeditionService.cs")
    if text is None:
        return report(GUARD, ["[File] 缺少 PetNest/PetNestExpeditionService.cs"])
    code = strip_cs_comments(text)

    # 1. commit-before-reveal：不得引用演出符号
    for symbol in FORBIDDEN_VIEW_SYMBOLS:
        if symbol in code:
            errors.append("[commit-before-reveal] 远征服务不得引用演出/UI 符号: " + symbol)

    # 2. 现实时间计时，禁 GameClock
    if "DateTime.UtcNow.Ticks" not in code:
        errors.append("[计时] 必须用现实时间 DateTime.UtcNow.Ticks")
    if "GameClock" in code:
        errors.append("[计时] 禁用 GameClock：它离线不推进，关游戏远征会冻住")

    # 3. 回拨钳制
    remaining = re.search(r"internal static long GetRemainingTicks\(PetNestExpeditionRecord record\)[\s\S]{0,500}?\n        \}", code)
    if remaining is None:
        errors.append("[计时] 缺少 GetRemainingTicks 入口")
    elif "remaining > 0L ? remaining : 0L" not in remaining.group(0):
        errors.append("[回拨] 剩余时间必须 max(0, Δ) 钳制，不得出现负倒计时")

    # 4. 死亡率固化
    depart = re.search(r"internal static bool TryDepart\([\s\S]{0,3000}?\n        \}", code)
    if depart is None:
        errors.append("[出发] 缺少 TryDepart 入口")
    else:
        body = depart.group(0)
        if "r.deathRate = GetDeathRate(tier);" not in body:
            errors.append("[明示] 死亡率必须随出发记录固化")
        if "r.successRate = ComputeSuccessRate(pet, destinationId, tier);" not in body:
            errors.append("[明示] 成功率必须随出发记录固化")
        if "pet.state = (int)PetNestPetState.OnExpedition;" not in body:
            errors.append("[锁定] 派出期间必须锁定崽")
        if "data.records.Remove(r);" not in body:
            errors.append("[事务] 落档失败必须回滚内存状态")

    # 5. 结算：幂等 + roll 用固化概率 + 先落档再发奖
    settle = re.search(r"internal static bool TrySettle\(PetNestExpeditionRecord record, out string failureReasonId\)[\s\S]{0,4000}?\n        \}", code)
    if settle is None:
        errors.append("[结算] 缺少 TrySettle 入口")
    else:
        body = settle.group(0)
        if "if (record.settled) return true;" not in body:
            errors.append("[幂等] 已结算的记录必须直接返回，不重复 roll")
        if "record.deathRate" not in body:
            errors.append("[明示] roll 必须用出发时固化的死亡率，不得重读常量")
        if "record.successRate" not in body:
            errors.append("[明示] roll 必须用出发时固化的成功率")
        if "record.settled = true;" not in body:
            errors.append("[结算] 必须写 settled 标记")

        commit_pos = body.find("CommitBoth(out failureReasonId)")
        grant_pos = body.find("GrantRewards(record)")
        settled_pos = body.find("record.settled = true;")
        if commit_pos < 0 or grant_pos < 0 or settled_pos < 0:
            errors.append("[顺序] 无法定位落档/发奖/settled 三个锚点")
        else:
            if settled_pos > commit_pos:
                errors.append("[commit-before-reveal] settled 标记必须在落档之前写进内存")
            if grant_pos < commit_pos:
                errors.append("[发奖] 必须先落档再发奖，否则会出现重复领取窗口")

        if "AppendMemorial(record, pet);" not in body:
            errors.append("[纪念碑] 真死必须刻碑")
        if "PetNestService.Nest.pets.Remove(pet);" not in body:
            errors.append("[真死] 真死必须移除 PetRecord（不可逆）")

    # 6. 翻牌只回放，绝不 roll
    reveal = re.search(r"internal static bool MarkRevealed\([\s\S]{0,900}?\n        \}", code)
    if reveal is None:
        errors.append("[翻牌] 缺少 MarkRevealed 入口")
    else:
        body = reveal.group(0)
        for forbidden in ["Random.value", "Random.Range", "RollCashReward", "RollLoot"]:
            if forbidden in body:
                errors.append("[翻牌] 翻牌只回放已 commit 的结果，不得 roll: " + forbidden)
        if "if (!record.settled)" not in body:
            errors.append("[翻牌] 未结算的记录不得翻牌")

    # 7. 刻碑内容：风险档位与固化死亡率
    memorial = re.search(r"private static void AppendMemorial\([\s\S]{0,1400}?\n        \}", code)
    if memorial is None:
        errors.append("[纪念碑] 缺少 AppendMemorial")
    else:
        body = memorial.group(0)
        if "entry.riskTier = record.riskTier;" not in body:
            errors.append("[纪念碑] 碑文必须刻风险档位（那是玩家自己按下的选择）")
        if "entry.deathRate = record.deathRate;" not in body:
            errors.append("[纪念碑] 碑文必须刻出发时固化的死亡率")
        if "PetNestTuning.MaxMemorialEntries" not in body:
            errors.append("[存档体积] 纪念碑必须有上限")
        if "museum.mergedMemorialCount++" not in body:
            errors.append("[存档体积] 溢出的碑必须合并为碑林计数，不得静默丢弃")

    # 8. 三档一起提交（避免"崽没了但碑没刻"）
    commit = re.search(r"private static bool CommitBoth\(out string failureReasonId\)[\s\S]{0,1200}?\n        \}", code)
    if commit is None:
        errors.append("[事务] 缺少 CommitBoth")
    else:
        body = commit.group(0)
        for token, desc in [
            ("PetNestService.StageCommit()", "巢"),
            ("PetNestPersistenceAccess.StageExpedition()", "远征"),
            ("PetNestPersistenceAccess.StageMuseum()", "博物馆"),
        ]:
            if token not in body:
                errors.append("[事务] 结算落档必须包含: " + desc)

    # 9. 元素亲和
    if "PetNestTuning.ElementAffinityBonus" not in code:
        errors.append("[亲和] 元素亲和加成必须走常量")

    # 10. 接进回基地扫描
    module = read_petnest("PetNestRuntimeModule.cs")
    if module is None:
        errors.append("[File] 缺少 PetNest/PetNestRuntimeModule.cs")
    elif "PetNestExpeditionService.SettleDueExpeditions();" not in strip_cs_comments(module):
        errors.append("[接线] 回基地必须扫一次到期远征")

    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
