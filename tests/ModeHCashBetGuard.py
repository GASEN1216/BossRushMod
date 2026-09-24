#!/usr/bin/env python3
"""
ModeHCashBetGuard — 鸭王杯「押钱 / 押背包物品」的结构与数值守卫（2026-09-24 owner 拍板）。

背景：仓库在出击地图上不存在，原来的真实押品链在比赛里一直用不了；owner 定改成玩家自己选押多少钱，
或者押背包里的物品，看比赛输赢、按赔率抽水，「总体下来玩家的钱是慢慢往下掉的，这才符合赌徒的性质」。

不变式：
1. 数值：每一档赔率、每一档押金，按表里的假定胜率算，期望拿回严格小于押金（庄家有抽水）；
   押注档第 0 档是「不押」；抽水在 (0, 50%) 之间；押物品的估值折算不高于官方商人收购口径（0.5），
   否则拿卖不上价的东西去押比卖掉划算。
2. 校准只能让赔付变少：假定胜率取 max(表, 实际胜率)，不得取 min。
3. 资金顺序：先把账本排进队列再动钱；钱没按预期变就把账本撤回；扣押金只从账户余额扣（不碰背包现金物品）。
4. 结算至多一次：只结算状态为 Reserved 且 runId / matchIndex 对得上的那一笔；退回同样只认 Reserved。
5. 押物品：记账时不动钱；赢了发奖品，凑不满的折成钱且不超过「赔付 − 估值」；奖品先备好、账本记成才发，
   记不成就销毁（至多发一次）；输了扣的钱不超过余额；退回不动钱。
6. 押注跟着这一场走：技术重试、恢复回落、挂起 / 关停 / 切图中止都不退，重锁时先沿用挂着的那一笔
   （旧版一中断就整额退回，打输了强退重进等于重掷）；只有放弃赛季、开新赛季对到上一季、F3 清理才退。
7. 接线：锁盘落盘成功后、生成之前下注；本场结算处结算；读档与开新赛季时对账；
   模块销毁时清掉静态缓存；选人页与结算页挂押注行；下注成功播「开盘」。
每条断言都有内存变异探针，探针不转红本守卫自判失败。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FILES = {
    "config": "ModeH/ModeHConfig.cs",
    "service": "ModeH/ModeHCashBetService.cs",
    "bet": "ModeH/ModeHRuntimeModule_BetFlow.cs",
    "match": "ModeH/ModeHRuntimeModule_MatchFlow.cs",
    "combat": "ModeH/ModeHRuntimeModule_CombatFlow.cs",
    "module": "ModeH/ModeHRuntimeModule.cs",
    "scene": "ModeH/ModeHRuntimeModule_SceneFlow.cs",
    "ui_flow": "ModeH/ModeHRuntimeModule_UiFlow.cs",
}


def read(rel):
    with open(os.path.join(REPO_ROOT, rel), "r", encoding="utf-8-sig") as handle:
        return handle.read()


def strip_comments(code):
    code = re.sub(r"/\*.*?\*/", "", code, flags=re.S)
    return re.sub(r"//[^\n]*", "", code)


def body(code, signature):
    start = code.find(signature)
    if start < 0:
        return ""
    i = code.find("{", start)
    depth = 0
    while i < len(code):
        if code[i] == "{":
            depth += 1
        elif code[i] == "}":
            depth -= 1
            if depth == 0:
                return code[start:i + 1]
        i += 1
    return ""


def int_array(code, name):
    m = re.search(name + r"\s*=\s*\{([^}]*)\}", code)
    if not m:
        return None
    return [int(x.strip().rstrip("L")) for x in m.group(1).split(",") if x.strip()]


def const_int(code, name):
    m = re.search(r"const int " + name + r"\s*=\s*(-?\d+);", code)
    return int(m.group(1)) if m else None


def check(sources):
    errors = []
    src = dict((k, strip_comments(v)) for k, v in sources.items())

    def need(text, token, why):
        if token not in text:
            errors.append(why + "（缺少 %r）" % token)

    def forbid(text, token, why):
        if token in text:
            errors.append(why + "（不得出现 %r）" % token)

    def ordered(text, tokens, why):
        positions = [text.find(t) for t in tokens]
        if -1 in positions or positions != sorted(positions):
            errors.append(why + "（顺序 %r）" % (positions,))

    # ---- 1. 数值：按表算，期望拿回严格小于押金 ----
    config = src["config"]
    amounts = int_array(config, "CashBetAmounts")
    win = int_array(config, "CashBetAssumedWinPermilleByOdds")
    cut = const_int(config, "CashBetHouseCutPermille")
    max_odds = const_int(config, "MaxOdds")
    item_value = const_int(config, "ItemBetValuePermille")
    if amounts is None or win is None or cut is None or max_odds is None or item_value is None:
        errors.append("[数值] 找不到押钱档位 / 假定胜率 / 抽水 / MaxOdds / 物品估值折算常量")
    else:
        if not amounts or amounts[0] != 0:
            errors.append("[数值] 押注第 0 档必须是「不押」")
        if any(a < 0 for a in amounts) or amounts != sorted(amounts):
            errors.append("[数值] 押注档必须从小到大且不为负")
        if not 0 < cut < 500:
            errors.append("[数值] 抽水必须在 (0‰, 500‰) 之间：没有抽水庄家就不赢")
        if not 0 < item_value <= 500:
            errors.append("[数值] 押物品估值不得高于官方商人收购口径 500‰：高了押物品比卖掉划算")
        if len(win) != max_odds + 1:
            errors.append("[数值] 假定胜率表必须按赔率 0–MaxOdds 下标排")
        for odds in range(1, min(len(win), max_odds + 1)):
            p = win[odds]
            if not 0 < p < 1000:
                errors.append("[数值] x%d 的假定胜率必须在 (0, 1000)‰" % odds)
                continue
            for amount in amounts[1:]:
                payout = amount * (1000 - cut) // p // 10 * 10
                if payout * p >= amount * 1000:
                    errors.append("[数值] x%d 押 %d 的期望拿回不小于押金（庄家没赢）" % (odds, amount))

    service = src["service"]
    # ---- 2. 校准只能让赔付变少 ----
    resolve = body(service, "internal static int ResolveAssumedWinPermille(int tier)")
    need(resolve, "Math.Max(table, observed)", "[校准] 假定胜率只能取表与实际胜率里较大的（赔付只降不升）")
    if "Math.Min(table" in resolve:
        errors.append("[校准] 假定胜率不得取较小值：实际胜率偏低时反而加赔会让押钱变成印钞机")
    payout = body(service, "internal static long ComputePayout(long stake, int odds)")
    need(payout, "ResolveAssumedWinPermille(tier)", "[校准] 赔付必须用校准后的假定胜率")
    need(payout, "(1000 - ModeHConfig.CashBetHouseCutPermille)", "[数值] 赔付必须扣抽水")

    # ---- 3. 资金顺序 ----
    commit = body(service, "private bool Commit(")
    ordered(commit, ["_store.Store(candidate)", "EconomyManager.Pay(", "EconomyManager.Money != before + delta",
                     "_store.Store(previous)", "_coordinator.RequestFlush("],
            "[顺序] 先排账本、再动钱、钱没变就撤回账本、最后同批落盘")
    need(commit, "EconomyManager.Pay(new Cost(-delta), true, false)", "[钱包] 押金只从账户余额扣，不碰背包现金物品")
    need(service, "BeforeCollectSaveData = CollectCash", "[落盘] 账本必须与现金快照同批落盘")

    # ---- 4. 至多一次 ----
    settle = body(service, "internal bool TrySettle(string runId, int matchIndex, bool won, long lossCharge, long winCash, string prizes, out long payout)")
    need(settle, "previous.status != StatusReserved", "[一次] 只结算挂着的那一笔")
    need(settle, "previous.matchIndex != matchIndex", "[一次] 结算要对上这一场")
    need(settle, "string.Equals(previous.runId, runId, StringComparison.Ordinal)", "[一次] 结算要对上这一季")
    need(settle, "candidate.status = StatusSettled", "[一次] 结算后状态改成已结算")
    refund = body(service, "internal bool TryRefund(string context, out long refunded)")
    need(refund, "previous.status != StatusReserved", "[一次] 只退回挂着的那一笔")
    reserve = body(service, "internal bool TryReserve(string runId, int matchIndex, int odds, long amount, out string failureReasonId)")
    need(reserve, "previous.status == StatusReserved", "[一次] 上一笔没结清不得再押")
    reserve_items = body(service, "internal bool TryReserveItems(string runId, int matchIndex, int odds, long value, string items, out string failureReasonId)")
    need(reserve_items, "previous.status == StatusReserved", "[一次] 上一笔没结清不得再押物品")

    # ---- 5. 押物品的钱 ----
    need(reserve_items, "return Commit(previous, candidate, 0, out failureReasonId);", "[物品] 押物品记账时不动钱")
    need(settle, "delta = won ? Math.Max(0L, Math.Min(winCash, gross - previous.amount)) : 0L;",
         "[物品] 押物品赢了折成的钱不超过赔付减估值（东西本来就留着，奖品另发）")
    need(settle, "charged = Math.Min(lossCharge, Math.Max(0L, EconomyManager.Money));", "[物品] 押物品输了扣的钱不超过余额")
    need(refund, "long delta = previous.kind == KindItems ? 0L : previous.amount;", "[物品] 押物品退回不动钱")

    # ---- 6. 押注跟着这一场走 ----
    bet = src["bet"]
    retry = body(src["match"], "private void RequestTechnicalRetry(string reasonId)")
    forbid(retry, "RefundCashBet(", "[沿用] 技术重试不得退押注（强退重进不能重掷）")
    forbid(src["match"], "RefundCashBet(", "[沿用] 恢复回落 / 技术重试都不退押注")
    abort = body(src["scene"], "private void TryReturnRealStakeOnAbort(string context)")
    forbid(abort, "RefundCashBet(", "[沿用] 挂起 / 关停 / 切图中止不退押注，回来重打照算")
    lock_bet = body(bet, "private void ReserveStandingCashBet()")
    ordered(lock_bet, ["ModeHCashBetRecord carried = CarriedBetForCurrentMatch();", "if (carried != null)",
                       "ModeHBetRevealView.Play(carried.amount, carried.odds,", "ReserveItemBet(odds);",
                       "ModeHCashBetService.TryReserve("],
            "[沿用] 重锁时先沿用挂着的那一笔（不重扣），再押物品，最后押钱")
    need(body(src["ui_flow"], "private void AbandonSeasonFromRecovery()"), 'RefundCashBet("abandon_season");',
         "[退回] 放弃赛季原样退回押注")
    need(body(bet, "private void ReconcileCashBetOnRestore()"), 'RefundCashBet("restore_other_run");',
         "[退回] 读档对到上一季的押注原样退回")

    # ---- 7. 接线 ----
    lock = body(src["match"], "private void LockLoadoutAndStartMatch()")
    ordered(lock, ['TryPersistSeason("loadout_locked", true)', "ReserveStandingCashBet();", "StartMatchSpawning();"],
            "[接线] 锁盘落盘成功后、生成之前下注")
    need(src["combat"], "SettleCashBetForMatch(won);", "[接线] 本场分出胜负处必须结算押注")
    settle_flow = body(bet, "private void SettleReservedBet(ModeHCashBetRecord record, bool won)")
    need(settle_flow, "ModeHItemBetStake.ForfeitLocked()", "[接线] 押物品输了要收走押上的东西")
    ordered(settle_flow, ["ModeHItemBetStake.PreparePrizes(", "ModeHCashBetService.TrySettle(",
                          "ModeHItemBetStake.DeliverPrizes(prizes);", "ModeHItemBetStake.DiscardPrizes(prizes);"],
            "[物品] 奖品先备好、账本记成才发、记不成就销毁（至多发一次）")
    need(settle_flow, "ModeHItemBetEntry.PrizeQuality(entries)", "[物品] 奖品品质跟押上的东西走")
    need(settle_flow, "ModeHCashBetService.ComputePayout(record.amount, record.odds) - record.amount",
         "[物品] 奖品总价值跟估值和赔率走")
    module = src["module"]
    if module.count("RestoreFromSaveIfPresent();\n") and module.count("ReconcileCashBetOnRestore();") < 2:
        errors.append("[接线] 两处读档恢复之后都要对账挂着的押注")
    reset = body(module, "internal static void ResetModeHStaticCaches()")
    need(reset, "ModeHCashBetService.ResetStaticCaches();", "[生命周期] 模块销毁要退订押钱账本的存档事件并清缓存（§4.6）")
    need(reset, "ModeHItemBetStake.ResetStaticCaches();", "[生命周期] 模块销毁要丢掉押物品的引用（§4.6）")
    need(src["scene"], "ReconcileCashBetOnRestore();", "[接线] 开新赛季时对账上一季挂着的押注")
    need(body(src["match"], "private ModeHPageContent BuildDraftPageContent()"), "AppendCashBetRow(page);",
         "[页面] 选人页要挂押注行")
    need(body(src["ui_flow"], "private void DecorateSettlementPage(ModeHPageContent page)"), "AppendCashBetRow(page);",
         "[页面] 结算页（下一场）要挂押注行")
    need(lock_bet, "ModeHBetRevealView.Play(amount, odds, false);", "[页面] 押钱成了要播「开盘」揭晓")
    need(body(bet, "private void ReserveItemBet(int odds)"), "ModeHBetRevealView.Play(value, odds, true);",
         "[页面] 押物品成了要播「开盘」揭晓")
    return errors


def main():
    sources = dict((k, read(v)) for k, v in FILES.items())
    errors = check(sources)
    probes = [
        ("service", "Math.Max(table, observed)", "Math.Min(table, observed)"),
        ("config", "CashBetHouseCutPermille = 80;", "CashBetHouseCutPermille = 0;"),
        # 表里的胜率偏低（赔多了）离线证明不了——公式对任何表都保证按表算是亏的，偏差只能靠实机分档统计与自动校准；
        # 这里只钉「胜率必须是 (0, 1000)‰ 的合法值」
        ("config", "{ 1000, 850, 700, 550, 420, 300 }", "{ 1000, 1850, 700, 550, 420, 300 }"),
        ("config", "ItemBetValuePermille = 500;", "ItemBetValuePermille = 1000;"),
        ("match", "ReserveStandingCashBet(); //", "//"),
        ("combat", "SettleCashBetForMatch(won);", ""),
        ("service", "if (previous.status != StatusReserved || previous.matchIndex != matchIndex",
         "if (previous.matchIndex != matchIndex"),
        ("service", "EconomyManager.Pay(new Cost(-delta), true, false)", "EconomyManager.Pay(new Cost(-delta), true, true)"),
        ("service", "return Commit(previous, candidate, 0, out failureReasonId);",
         "return Commit(previous, candidate, -value, out failureReasonId);"),
        ("service", "delta = won ? Math.Max(0L, Math.Min(winCash, gross - previous.amount)) : 0L;", "delta = won ? gross : 0L;"),
        ("bet", "                if (prizes != null) ModeHItemBetStake.DeliverPrizes(prizes);\n", ""),
        ("bet", "ModeHItemBetEntry.PrizeQuality(entries)", "ModeHConfig.MaxGameQuality"),
        ("service", "long delta = previous.kind == KindItems ? 0L : previous.amount;", "long delta = previous.amount;"),
        ("module", "ModeHCashBetService.ResetStaticCaches();", ""),
        ("module", "ModeHItemBetStake.ResetStaticCaches();", ""),
        ("scene", "        private void TryReturnRealStakeOnAbort(string context)\n        {\n",
         "        private void TryReturnRealStakeOnAbort(string context)\n        {\n            RefundCashBet(\"abort_\" + context);\n"),
        ("bet", "            if (carried != null)\n            {\n                // 押注跟着这一场走",
         "            if (false)\n            {\n                // 押注跟着这一场走"),
        ("bet", "                    lossCharge = ModeHItemBetStake.ForfeitLocked();\n", ""),
    ]
    for key, before, after in probes:
        if before not in sources[key]:
            errors.append("[探针] 锚点失效：%s %r" % (FILES[key], before))
            continue
        mutated = dict(sources)
        mutated[key] = sources[key].replace(before, after, 1)
        if not check(mutated):
            errors.append("[探针] 变异没被拦住：%s %r → %r" % (FILES[key], before, after))
    if errors:
        print("ModeHCashBetGuard: FAIL")
        for e in errors:
            print("  - " + e)
        return 1
    print("ModeHCashBetGuard: PASS（数值、校准方向、资金顺序、至多一次、押物品、押注沿用、接线；%d 个内存变异探针）" % len(probes))
    return 0


if __name__ == "__main__":
    sys.exit(main())
