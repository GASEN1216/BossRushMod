#!/usr/bin/env python3
"""
MoneyDeliveryNetDeltaGuard — Mod 发钱 / 收钱认官方 Add / Pay 的结果或余额确实变了，不按余额净变化判到账。

旧代码在 EconomyManager.Add 之后要求余额「恰好 +奖金」。官方 Add 先改余额再同步派发 OnMoneyChanged，
别的订阅者（其它 Mod、收益倍率补丁）可能在通知里再改一次钱或直接抛异常，净变化一不同就判失败、不记账，
钱却已经到账，玩家每点一次就再发一笔。2026-11-26 玩家反馈「云上的坐标」交不了时曾归因到这里；
次日玩家确认根因是前置 HarmonyLib 排在本 Mod 后面（任务交付走 Harmony 补丁），这条判定仍防重复发钱。

不变式：
1. 全部编译源里不得出现 `EconomyManager.Money ==/!= x ± y` 这类按净变化判到账的写法。
2. BossRushMoneyTransfer：TryAdd 认官方 Add 返回 true 或这次调用里余额已变；Add 抛异常时也看余额（通知里抛异常 = 钱已到账）。
   TryDebit 认官方 Pay(Cost) 返回 true 或账户 / 现金已变，异常时同样看账户 / 现金。
3. 天空岛交付：到账即上闩（cashPaidPendingAction），存档排队失败时退款成功才撤闩；退不回就保留闩，下次只记账不再发钱。
4. 成就奖金：只认 TryAdd 结果，失败撤回账本。
每条断言都有内存变异探针，探针不转红本守卫自判失败。
"""
import os
import re
import sys

from guard_mutation_util import REPO_ROOT, cleaned, method_body, run_guard, squash

sys.path.insert(0, os.path.join(REPO_ROOT, "tools"))
from compile_list import read_compile_sources  # noqa: E402

NET_DELTA = re.compile(r"EconomyManager\.Money\s*[!=]=\s*[\w.()]+\s*[-+]\s*[\w.()]+")

FILES = {
    "transfer": "Common/Infrastructure/BossRushMoneyTransfer.cs",
    "sky": "SkyIsland/SkyIslandStoryService.cs",
    "achievement": "Achievement/AchievementRewardJournal.cs",
    "modeh": "ModeH/ModeHCashBetService.cs",
}
for _source in read_compile_sources():
    if _source not in FILES.values():
        FILES["compiled:" + _source] = _source


def check(raw):
    src = cleaned(raw)
    errors = []
    for key, text in src.items():
        for match in NET_DELTA.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            errors.append("[净变化] %s:%d 按余额净变化判到账: %s" % (FILES[key], line, match.group(0)))

    transfer = src["transfer"]
    add = squash(method_body(transfer, "internal static bool TryAdd(long delta, string logTag)"))
    if "try { return EconomyManager.Add(delta) || Moved(before, 0L, false); }" not in add \
            or "bool moved = Moved(before, 0L, false);" not in add:
        errors.append("[到账] TryAdd 必须认官方 Add 返回 true 或余额已变，异常时也看余额")
    debit = squash(method_body(transfer, "internal static bool TryDebit(long amount, bool accountAvailable, bool cashAvailable, string logTag)"))
    if "try { return EconomyManager.Pay(new Cost(amount), accountAvailable, cashAvailable) || Moved(beforeMoney, beforeCash, cashAvailable); }" not in debit \
            or "bool moved = Moved(beforeMoney, beforeCash, cashAvailable);" not in debit:
        errors.append("[到账] TryDebit 必须认官方 Pay 返回 true 或账户 / 现金已变")
    moved = squash(method_body(transfer, "private static bool Moved(long beforeMoney, long beforeCash, bool includeCash)"))
    if "return EconomyManager.Instance != null && (EconomyManager.Money != beforeMoney || (includeCash && EconomyManager.Cash != beforeCash));" not in moved:
        errors.append("[到账] Moved 必须比较调用前后的账户余额（扣钱含背包现金）")

    sky = squash(method_body(src["sky"], "internal bool TryDeliverQuest(SkyIslandStoryAction action, int money, out string message)"))
    pay = sky.find('if (!BossRushMoneyTransfer.TryAdd(money, "SkyIsland")) return false; cashPaidPendingAction = action;')
    store = sky.find("if (!store.Store(candidate))", pay)
    refund = sky.find('bool refunded = BossRushMoneyTransfer.TryDebit(money, true, true, "SkyIsland"); if (refunded) cashPaidPendingAction = null;', store)
    if pay < 0 or store < 0 or refund < 0:
        errors.append("[天空岛] 到账即上闩，排队失败时只有退款成功才撤闩")

    achievement = squash(method_body(src["achievement"], "internal bool TryPay(string id, long amount, Func<string, bool> isClaimed)"))
    if 'if (!BossRushMoneyTransfer.TryAdd(amount, "Achievement")) { store.Store(previous); return false; }' not in achievement:
        errors.append("[成就] 奖金只认 TryAdd 结果，失败撤回账本")
    return errors


PROBES = [
    ("sky", 'if (!BossRushMoneyTransfer.TryAdd(money, "SkyIsland")) return false;',
     'Duckov.Economy.EconomyManager.Add(money); if (Duckov.Economy.EconomyManager.Money != before + money) return false;',
     "天空岛退回按净变化判到账", "[净变化]"),
    ("sky", "if (refunded) cashPaidPendingAction = null;", "cashPaidPendingAction = null;", "退款失败也撤闩", "[天空岛]"),
    ("transfer", "return EconomyManager.Add(delta) || Moved(before, 0L, false);",
     "EconomyManager.Add(delta); return EconomyManager.Money == before + delta;", "TryAdd 改按净变化", "[净变化]"),
    ("transfer", "return EconomyManager.Add(delta) || Moved(before, 0L, false);", "return EconomyManager.Add(delta);",
     "补丁过的 Add 动了钱却返回 false 时当作没到账", "[到账]"),
    ("transfer", "bool moved = Moved(before, 0L, false);", "bool moved = false;", "通知抛异常时当作没到账", "[到账]"),
    ("transfer", "|| Moved(beforeMoney, beforeCash, cashAvailable);", ";", "扣了钱却返回 false 时当作没扣", "[到账]"),
    ("transfer", "|| (includeCash && EconomyManager.Cash != beforeCash)", "", "扣背包现金不算扣过", "[到账]"),
    ("achievement", 'BossRushMoneyTransfer.TryAdd(amount, "Achievement")',
     "EconomyManager.Add(amount) && EconomyManager.Money == before + amount", "成就奖金按净变化判到账", "[净变化]"),
    ("modeh", "if (!moved)", "if (EconomyManager.Money != before + delta)", "鸭王杯押注按净变化判到账", "[净变化]"),
]


if __name__ == "__main__":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    sys.exit(run_guard("MoneyDeliveryNetDeltaGuard", FILES, check, PROBES))
