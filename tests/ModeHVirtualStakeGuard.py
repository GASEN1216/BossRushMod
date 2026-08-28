#!/usr/bin/env python3
"""
ModeHVirtualStakeGuard — Mode H 虚拟筹码守卫（设计提案 §17.5 定价修订、§26.1）。

不变式：
- 初始 6 点、上限 30、每场 0..min(2, credits)，0 点始终合法；
- 净赔率语义：gross = stake * (1 + odds)，net = gross - stake；
- settledBalance = clamp(balanceAfterReservation + gross, 0, 30)；
- rewardCandidateCount = 1 + min(2, floor(max(0, net) / 2))；
- §17.5 九行冻结取值表逐行成立（本守卫用与 C# 同一套公式独立复算）；
- 旧规则符号（gross = stake * odds、floor(net/3)）不得出现；
- 断言不存在净收益为 0 的非零下注档位（旧 x1 死档位不得复活）；
- 下注额不参与赔率计算：赔率控制器不得引用 stake/credits 符号；
- 虚拟筹码在玩家资产白名单之外：不得引用 Inventory / PlayerStorage；
- 虚拟筹码与真实押品是两条独立结算，字段互不复用；
- 锁盘写 virtualStakeBalanceBeforeReservation 并原子更新两个根字段；
- 开战前技术中止恢复原余额并清零 reservedVirtualStake；
- MatchSettling 写回 settledBalance 并清零 reservedVirtualStake；
- 败场不产生奖励候选。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import contains_symbol, read_text, strip_cs_comments  # noqa: E402

STAKE = os.path.join(REPO_ROOT, "ModeH", "ModeHVirtualStakeController.cs")
ODDS = os.path.join(REPO_ROOT, "ModeH", "ModeHOddsController.cs")
CONFIG = os.path.join(REPO_ROOT, "ModeH", "ModeHConfig.cs")

# §17.5 冻结取值表：(stake, odds, gross, net, rewardCandidates)
FROZEN_WIN_TABLE = [
    (0, 1, 0, 0, 1),
    (0, 3, 0, 0, 1),
    (0, 5, 0, 0, 1),
    (1, 1, 2, 1, 1),
    (1, 3, 4, 3, 2),
    (1, 5, 6, 5, 3),
    (2, 1, 4, 2, 2),
    (2, 2, 6, 4, 3),
    (2, 3, 8, 6, 3),
    (2, 5, 12, 10, 3),
]

MAX_CREDITS = 30
MAX_STAKE_PER_MATCH = 2
MAX_REWARD_CANDIDATES = 3
NET_DIVISOR = 2


def gross(stake, odds, won):
    return stake * (1 + odds) if won else 0


def net(stake, payout):
    return payout - stake


def settled(balance_after_reservation, payout):
    value = balance_after_reservation + payout
    return max(0, min(MAX_CREDITS, value))


def reward_candidates(profit):
    profit = max(0, profit)
    return 1 + min(MAX_REWARD_CANDIDATES - 1, profit // NET_DIVISOR)


def check_frozen_table(errors):
    for stake, odds, expected_gross, expected_net, expected_candidates in FROZEN_WIN_TABLE:
        actual_gross = gross(stake, odds, True)
        if actual_gross != expected_gross:
            errors.append("[Table] 胜利 stake={} odds=x{} gross 应为 {}，公式得 {}".format(
                stake, odds, expected_gross, actual_gross))
        actual_net = net(stake, actual_gross)
        if actual_net != expected_net:
            errors.append("[Table] 胜利 stake={} odds=x{} net 应为 {}，公式得 {}".format(
                stake, odds, expected_net, actual_net))
        actual_candidates = reward_candidates(actual_net)
        if actual_candidates != expected_candidates:
            errors.append("[Table] 胜利 stake={} odds=x{} 奖励候选数应为 {}，公式得 {}".format(
                stake, odds, expected_candidates, actual_candidates))

    # 第九行：任意下注、失败 -> gross 0、net -stake、无奖励候选
    for stake in range(0, MAX_STAKE_PER_MATCH + 1):
        for odds in range(1, 6):
            if gross(stake, odds, False) != 0:
                errors.append("[Table] 失败场 gross 必须为 0")
            if net(stake, 0) != -stake:
                errors.append("[Table] 失败场 net 必须为 -stake")

    # 定价修订的目的：押满 2 点在任何赔率档胜利时都严格优于不下注
    baseline = reward_candidates(net(0, gross(0, 1, True)))
    for odds in range(1, 6):
        candidates = reward_candidates(net(2, gross(2, odds, True)))
        if candidates <= baseline:
            errors.append("[Pricing] x{} 档押满 2 点的奖励候选数未严格优于不下注".format(odds))

    # 不得存在净收益为 0 的非零下注档位（这正是 2026-08-27 定价修订要消灭的死档位）
    for stake in range(1, MAX_STAKE_PER_MATCH + 1):
        for odds in range(1, 6):
            profit = net(stake, gross(stake, odds, True))
            if profit <= 0:
                errors.append("[Pricing] stake={} odds=x{} 胜利净收益为 {}，存在死档位".format(
                    stake, odds, profit))

    # 上限 clamp 必须保留
    if settled(MAX_CREDITS, 12) != MAX_CREDITS:
        errors.append("[Table] settledBalance 未按 30 上限 clamp")
    if settled(0, 0) != 0:
        errors.append("[Table] settledBalance 下界必须为 0")


def check_config(errors):
    config = read_text(CONFIG)
    if config is None:
        errors.append("[File] 缺少 ModeH/ModeHConfig.cs")
        return
    frozen = [
        (r"public const int InitialVirtualStakeCredits = 6;", "初始 6 点"),
        (r"public const int MaxVirtualStakeCredits = 30;", "上限 30 点"),
        (r"public const int MaxVirtualStakePerMatch = 2;", "每场至多 2 点"),
        (r"public const int MaxRewardCandidateCount = 3;", "奖励候选上限 3"),
        (r"public const int RewardCandidateNetDivisor = 2;", "候选数除数为 2"),
        (r"public const int ScarOfferMinOdds = 3;", "战痕门槛 x3"),
    ]
    for pattern, desc in frozen:
        if not re.search(pattern, config):
            errors.append("[Config] 未冻结: " + desc)


def check_controller(errors):
    source = read_text(STAKE)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHVirtualStakeController.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"public static int GetMaxStake\(int virtualStakeCredits\)", "最大下注额入口"),
        (r"public static bool IsStakeLegal\(", "下注合法性校验"),
        (r"return reservedStake \* \(1 \+ lockedOdds\);", "净赔率 gross = stake * (1 + odds)"),
        (r"public static int ComputeNetProfit\(int reservedStake, int grossPayout\)", "净收益入口"),
        (r"return grossPayout - reservedStake;", "net = gross - stake"),
        (r"public static int ComputeSettledBalance\(", "结算余额入口"),
        (r"ModeHConfig\.MaxVirtualStakeCredits : settled;", "settledBalance 按 30 上限 clamp"),
        (r"public static int ComputeRewardCandidateCount\(int netProfit\)", "奖励候选数入口"),
        (r"int extra = netProfit / ModeHConfig\.RewardCandidateNetDivisor;", "候选数按 floor(net/2)"),
        (r"int cap = ModeHConfig\.MaxRewardCandidateCount - 1;", "额外候选封顶 2"),
        (r"public static bool TryReserve\(", "锁盘保留入口"),
        (r"snapshot\.virtualStakeBalanceBeforeReservation = season\.virtualStakeCredits;",
         "锁盘先写赛前余额快照"),
        (r"season\.reservedVirtualStake = stake;", "锁盘写根字段 reservedVirtualStake"),
        (r"season\.virtualStakeCredits = snapshot\.virtualStakeBalanceBeforeReservation - stake;",
         "锁盘原子更新 virtualStakeCredits"),
        (r"public static bool RestoreReservation\(", "技术中止恢复入口"),
        (r"public static int Settle\(", "结算入口"),
        (r"season\.virtualStakeCredits = settled;", "结算写回 settledBalance"),
        (r"public static bool MeetsScarOfferGate\(int lockedOdds\)", "x3+ 战痕门"),
        (r"public static bool VerifyFrozenTable\(out string failureReasonId\)", "冻结取值表运行时自检"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Controller] 不满足: " + desc)

    # reservedVirtualStake 必须在恢复与结算两条路径上都被清零
    for name in ["RestoreReservation", "Settle"]:
        body = re.search(
            r"public static [\w<>]+ {}\([\s\S]*?\n        \}}".format(name), code)
        if body and "season.reservedVirtualStake = 0;" not in body.group(0):
            errors.append("[Controller] {} 未清零 reservedVirtualStake".format(name))

    # 败场不得产生奖励候选
    settle_body = re.search(r"public static int Settle\([\s\S]*?\n        \}", code)
    if settle_body and "if (!won) return 0;" not in settle_body.group(0):
        errors.append("[Controller] 败场必须返回 0 个奖励候选")

    # 旧定价规则不得复活
    if re.search(r"reservedStake \* lockedOdds\s*;", code):
        errors.append("[Legacy] 出现旧规则 gross = stake * odds")
    if re.search(r"/\s*3\s*;", code):
        errors.append("[Legacy] 出现旧规则 floor(net / 3)")


def check_asset_isolation(errors):
    source = read_text(STAKE)
    if source is None:
        return
    code = strip_cs_comments(source)
    for forbidden in ["Inventory", "PlayerStorage", "ItemTreeData", "ItemAssetsCollection"]:
        if contains_symbol(code, forbidden):
            errors.append("[Isolation] 虚拟筹码不得引用玩家资产符号: " + forbidden)
    # 真实押品字段不得被虚拟筹码结算复用
    for forbidden in ["stakeTxId", "ModeHStakeJournalDto", "ModeHWarehouseStakeJournal",
                      "realStakeSelected"]:
        if contains_symbol(code, forbidden):
            errors.append("[Isolation] 虚拟筹码不得复用真实押品字段: " + forbidden)


def check_odds_isolation(errors):
    odds = read_text(ODDS)
    if odds is None:
        errors.append("[File] 缺少 ModeH/ModeHOddsController.cs")
        return
    code = strip_cs_comments(odds)
    for forbidden in ["reservedVirtualStake", "virtualStakeCredits", "ModeHVirtualStakeController"]:
        if contains_symbol(code, forbidden):
            errors.append("[Circular] 赔率不得读下注额相关符号: " + forbidden)


def main():
    errors = []
    check_frozen_table(errors)
    check_config(errors)
    check_controller(errors)
    check_asset_isolation(errors)
    check_odds_isolation(errors)

    if errors:
        print("ModeHVirtualStakeGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHVirtualStakeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
