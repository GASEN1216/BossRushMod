#!/usr/bin/env python3
"""
ModeGDeathRoutingGuard — Mode G 死亡路由守卫（规格 §20 第 19 条）。

不变式：
- run-lifetime OnDead Starting 首 await 前订阅：telemetry SubscribeDead 独立 run
  owner 幂等订阅（注释冻结「Starting 首 await 前订阅」），精确退订；
- Rewarding 死亡先失效 reward attempt：先置 rewardNonceInvalidated +
  ModeGRewardTransaction.InvalidateAttemptNonce() + 取消物化，再 End；
- TechnicalLoss 规则：Spawning 阶段/批量激活中主 Boss 死亡 →
  HandleTechnicalBossDeath + End(TechnicalIntegrityLoss)，禁推进波次；
- terminal credit 与 countedDead 顺序：正常死亡先 MarkKilled（exact Health 结案，
  countedDead 语义）再处理 managed handle 清理与后续路由。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROUTING = os.path.join(REPO_ROOT, "ModeG", "ModeGDeathRouting.cs")
TELEMETRY = os.path.join(REPO_ROOT, "ModeG", "ModeGCombatTelemetry.cs")
MODULE = os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeModule.cs")


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []
    routing = read(ROUTING, errors)
    telemetry = read(TELEMETRY, errors)
    module = read(MODULE, errors)

    if telemetry:
        checks = [
            ("DeadOwnerDoc",
             r"订阅 OnDead（独立 run owner，Starting 首 await 前订阅；幂等）",
             "OnDead 独立 run owner、Starting 首 await 前订阅（注释冻结）"),
            ("SubscribeDeadIdempotent",
             r"public void SubscribeDead\(\)\s*\{\s*if \(_deadSubscribed\) return;",
             "SubscribeDead 幂等（owner bool 防重复）"),
            ("UnsubscribeDeadExact",
             r"public void UnsubscribeDead\(\)[\s\S]{0,200}?Health\.OnDead -= HandleOnDead;",
             "精确退订 OnDead（独立 owner）"),
            ("DeadHandlerRegisteredOnly",
             r"private void HandleOnDead\(Health health, DamageInfo info\)"
             r"[\s\S]{0,300}?if \(!_state\.IsRegisteredBossHealth\(health\)\) return;",
             "run owner 只处理已登记 Boss（玩家死亡走独立路由）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, telemetry):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if routing:
        checks = [
            ("RewardingInvalidateFirst",
             r"if \(state\.IsRewarding\)\s*\{\s*"
             r"state\.rewardNonceInvalidated = true;\s*"
             r"ModeGRewardTransaction\.InvalidateAttemptNonce\(\);"
             r"[\s\S]{0,300}?CancelModeGRewardMaterialization_LootAndRewards\(\);"
             r"[\s\S]{0,120}?module\.End\(ModeGExitReason\.RewardInterruptedByDeath\);",
             "Rewarding 死亡先失效 reward attempt 再 End"),
            ("DefeatCasOnce",
             r"if \(!state\.TryLockBattleResult\(ModeGBattleResult\.Defeat\)\) return;",
             "败北 terminal credit 一次（battleResultToken CAS）"),
            ("VictoryCasOnce",
             r"if \(!state\.TryLockBattleResult\(ModeGBattleResult\.Victory\)\) return;",
             "胜利 terminal credit 一次（battleResultToken CAS）"),
            ("VictoryLifecycleAdvance",
             r"if \(!state\.TryAdvanceLifecycle\(ModeGLifecyclePhase\.Rewarding\)\)"
             r"[\s\S]{0,120}?module\.End\(ModeGExitReason\.TechnicalIntegrityLoss\);",
             "胜利推进 Rewarding 失败即 TechnicalLoss（禁带伤推进）"),
            ("PlayerIdentityExact",
             r"if \(player == null \|\| !ReferenceEquals\(health, player\.Health\)\) return;",
             "玩家死亡 exact Health 引用身份（不路由他人）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, routing):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if module:
        checks = [
            ("TechnicalLossRouting",
             r"if \(_state\.combatPhase == ModeGCombatPhase\.Spawning \|\| _batchActivationInProgress\)"
             r"[\s\S]{0,200}?HandleTechnicalBossDeath\(health\);"
             r"[\s\S]{0,120}?End\(ModeGExitReason\.TechnicalIntegrityLoss\);",
             "Spawning/批量激活中死亡 → TechnicalLoss 禁推进"),
            ("CountedDeadOrder",
             r"if \(!_spawnTransaction\.MarkKilled\(health\)\) return;\s*"
             r"_totalBossKills\+\+;",
             "正常死亡先 MarkKilled 结案（countedDead 顺序）再后续路由"),
            ("TechnicalCleanupReason",
             r"handle\.CleanupOnce\(ManagedBossCleanupReason\.TechnicalLoss\);",
             "技术丢失按 TechnicalLoss 原因清理 handle"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, module):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if errors:
        print("ModeGDeathRoutingGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGDeathRoutingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
