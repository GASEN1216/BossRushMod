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
- 发奖必须在落档之后，现金/物品用独立游标表达可恢复欠账；
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
    depart = re.search(r"internal static bool TryDepart\([\s\S]{0,4500}?\n        \}", code)
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
        if "PetNestPersistenceAccess.BeginTransaction" not in body:
            errors.append("[事务] 出发必须先开启 v2 候选包事务")
        if "CommitBoth(out failureReasonId)" not in body:
            errors.append("[事务] 出发必须原子提交候选包")

    # 5. 结算：幂等 + roll 用固化概率 + 先落档再发奖
    settle = re.search(r"internal static bool TrySettle\(PetNestExpeditionRecord record, out string failureReasonId\)[\s\S]{0,6500}?\n        \}", code)
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

        # 前置检查必须在 roll / 改内存**之前**：roll 之后再发现写不下去，
        # 内存已经被改成"已结算"（真死路径还删了 PetRecord、刻了碑），回滚代价极大
        precheck_pos = body.find("PetNestPersistenceAccess.CanStoreAll")
        roll_pos = body.find("UnityEngine.Random.value < record.deathRate")
        commit_pos = body.find("CommitBoth(out failureReasonId)")
        settled_pos = body.find("record.settled = true;")
        if precheck_pos < 0 or roll_pos < 0 or commit_pos < 0 or settled_pos < 0:
            errors.append("[顺序] 无法定位前置检查/roll/落档/settled 四个锚点")
        else:
            if precheck_pos > roll_pos:
                errors.append("[事务] CanStoreAll 前置检查必须在 roll 与改内存之前")
            if settled_pos > commit_pos:
                errors.append("[commit-before-reveal] settled 标记必须在落档之前写进内存")
            if "GrantRewards(" in body:
                errors.append("[发奖] 结算阶段不得直接发奖；必须等基地资源就绪后走欠账补发")

        if "AppendMemorial(record, pet);" not in body:
            errors.append("[纪念碑] 真死必须刻碑")
        if "PetNestService.Nest.pets.Remove(pet);" not in body:
            errors.append("[真死] 真死必须移除 PetRecord（不可逆）")

    # 6. 翻牌只回放，绝不 roll；且落档失败必须整体回滚
    reveal = re.search(r"internal static bool MarkRevealed\([\s\S]{0,1600}?\n        \}", code)
    if reveal is None:
        errors.append("[翻牌] 缺少 MarkRevealed 入口")
    else:
        body = reveal.group(0)
        for forbidden in ["Random.value", "Random.Range", "RollCashReward", "RollLoot"]:
            if forbidden in body:
                errors.append("[翻牌] 翻牌只回放已 commit 的结果，不得 roll: " + forbidden)
        if "if (!record.settled)" not in body:
            errors.append("[翻牌] 未结算的记录不得翻牌")
        if "PetNestPersistenceAccess.BeginTransaction" not in body:
            errors.append("[翻牌] 必须在 v2 候选包中修改 revealed")
        if "if (candidate.rewardsGranted) Records.Remove(candidate);" not in body:
            errors.append("[翻牌] 只有奖励已发完才可移除记录，欠账必须保留")

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

    # 8. v2 聚合包原子提交（避免"崽没了但碑没刻"）
    commit = re.search(r"private static bool CommitBoth\(out string failureReasonId\)[\s\S]{0,1800}?\n        \}", code)
    if commit is None:
        errors.append("[事务] 缺少 CommitBoth")
    else:
        body = commit.group(0)
        if "PetNestPersistenceAccess.IsTransactionActive" not in body:
            errors.append("[事务] CommitBoth 必须拒绝无候选包的调用")
        if "PetNestPersistenceAccess.CommitTransaction" not in body:
            errors.append("[事务] 三分区必须通过一次 Bundle_v2 Store 提交")
        if "PetNestPersistenceAccess.CanStoreAll" not in body:
            errors.append("[原子性] 提交前必须 fail-closed 检查 Bundle store")
        if "PetNestPersistence.StoreBundle" in body:
            errors.append("[原子性] 不得绕过活动候选包直接拼装 StoreBundle")
        if "flush_deferred_is_saving" in body:
            errors.append("[原子性] 成败必须以入队为准，不得因 flush 失败让调用方回滚内存")

    # 9. 发奖走可恢复的独立标记，落档成功而发奖失败时能补发
    if "rewardsGranted" not in code:
        errors.append("[发奖] 必须有独立的 rewardsGranted 标记（settled 之外的第二个标记）")
    backfill = re.search(r"internal static int TryGrantPendingRewards\(\)[\s\S]{0,2400}?\n        \}", code)
    if backfill is None:
        errors.append("[发奖] 缺少可恢复的补发通道 TryGrantPendingRewards()")
    else:
        body = backfill.group(0)
        if "!r.settled || r.rewardsGranted" not in body:
            errors.append("[发奖] 补发必须幂等：只处理 settled 且未发奖的记录")
        if "r.rewardsGranted = true;" not in body:
            errors.append("[发奖] 补发后必须置已发放标记并落档")
        # 按条目记账：整体重试会让已到账的现金再发一次
        if "GrantRewards(r)" not in body:
            errors.append("[发奖] 补发必须经 GrantRewards 逐格续发，不得另起一套发放路径")
        if "r.rewardGrantAttempts++" not in body:
            errors.append("[发奖] 必须记录连续失败次数供可靠性诊断")
        if re.search(r"rewardGrantAttempts\s*>=\s*PetNestTuning\.MaxRewardGrantAttempts[\s\S]{0,240}rewardsGranted\s*=\s*true", body):
            errors.append("[发奖] 超过尝试次数不得伪装已发完，欠账必须永久保留")

    # 9b. 按条目记账：现金与战利品各有自己的账，补发只重做真正失败的那一格
    grant = re.search(
        r"private static bool GrantRewards\(PetNestExpeditionRecord record\)[\s\S]{0,3000}?\n        \}",
        code)
    if grant is None:
        errors.append("[发奖记账] GrantRewards 必须返回 bool（是否已全部发出），"
                      "否则注释宣称的可补发语义与实际行为不符")
    else:
        body = grant.group(0)
        if "record.cashGranted = true;" not in body:
            errors.append("[发奖记账] 现金必须单独记账，否则整体重试会重复发放现金")
        if "EconomyManager.Add(record.outcomeCash)" not in body:
            errors.append("[发奖记账] 缺少现金发放调用")
        if "EconomyManager.Add(record.outcomeCash)" not in body or "if (ok) record.cashGranted = true;" not in body:
            errors.append("[发奖记账] 官方 Add 在 Instance==null 时返回 false 不抛异常，"
                          "必须接返回值判断是否真的到账（Dev 后端亦同口径）")
        if "record.grantedLootUnits" not in body:
            errors.append("[发奖记账] 战利品必须按件数游标记账，补发从游标续投，不重投")
        if "unit < record.grantedLootUnits" not in body:
            errors.append("[发奖记账] 补发必须跳过游标之前已投出去的件数")

    # 9c. 单件投递必须区分「可重试失败」与「确定性废件」：
    # 废件当作失败会让记录永远发不全，可重试失败当作成功则静默吞奖
    one = re.search(
        r"private static bool GrantOneItem\(int typeId, PetNestExpeditionRecord record\)"
        r"[\s\S]{0,2400}?\n        \}", code)
    if one is None:
        errors.append("[发奖记账] GrantOneItem 必须返回 bool（这一件是否已了结）")
    else:
        body = one.group(0)
        if "return false;" not in body:
            errors.append("[发奖记账] 可重试失败（实例化失败 / 异常）必须返回 false 保留欠账")

    # 9d. 孤儿远征锁自愈：崽标记 OnExpedition 但远征表无匹配记录 = 永久锁死
    orphan = re.search(
        r"internal static int ReconcileOrphanedExpeditionLocks\(\)[\s\S]{0,2600}?\n        \}", code)
    if orphan is None:
        errors.append("[孤儿锁] 缺少自愈入口 ReconcileOrphanedExpeditionLocks()")
    else:
        body = orphan.group(0)
        if "PetNestPersistenceAccess.CanStoreAll" not in body:
            errors.append("[孤儿锁] 写屏障 / 故障态下 records 可能只是没读回来，"
                          "此时复位会把在途远征全部误解锁，必须先过 CanStoreAll")
        if "HasMatchingExpedition(records, pet)" not in body:
            errors.append("[孤儿锁] 必须先确认远征表里确实没有匹配记录再复位")
        if "pet.lockedByExpeditionId = null;" not in body:
            errors.append("[孤儿锁] 复位必须同时清掉 lockedByExpeditionId")
        if "PetNestPetState.OnExpedition" not in body:
            errors.append("[孤儿锁] 只有 state==OnExpedition 的崽才复位状态，"
                          "不得顺手改掉出战席位")
    module_text = read_petnest("PetNestRuntimeModule.cs")
    if module_text is not None and (
            "PetNestExpeditionService.ReconcileOrphanedExpeditionLocks();"
            not in strip_cs_comments(module_text)):
        errors.append("[孤儿锁] 回基地扫描必须接通孤儿远征锁自愈")
    reveal_block = re.search(r"internal static bool MarkRevealed\([\s\S]{0,1600}?\n        \}", code)
    if reveal_block is not None and "candidate.rewardsGranted" not in reveal_block.group(0):
        errors.append("[发奖] 翻牌可关闭，但有欠账的记录必须保留")

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
