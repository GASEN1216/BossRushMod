#!/usr/bin/env python3
"""
ModeGDeathRoutingGuard — Mode G 死亡路由守卫（规格 §20 第 19 条）。

不变式：
- run-lifetime OnDead Starting 首 await 前订阅：telemetry SubscribeDead 独立 run
  owner 幂等订阅（注释冻结「Starting 首 await 前订阅」），精确退订；
- Rewarding 死亡先失效 reward attempt：先置 rewardNonceInvalidated +
  ModeGRewardTransaction.InvalidateAttemptNonce() + 取消物化，再 End；同步完成回调
  必须在 nonce 已失效时立即退出，不能抢先改写为 RewardAbandoned；
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
SWEEP = os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeModule_SilentDeathSweep.cs")


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
    sweep = read(SWEEP, errors)

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
            ("CancelledRewardCallbackNoop",
             r"if \(state\.rewardNonceInvalidated\) return;"
             r"[\s\S]{0,220}?if \(failed > 0\)",
             "取消奖励的同步完成回调不得抢先改写终局原因"),
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
              r"if \(IsCoopAssetCurrent && !CoopPersonalDefeated && \(!BossRushCoop\.IsConnected \|\| BossRushCoop\.GetPlayerId\(info\.fromCharacter\) == BossRushCoop\.LocalPlayerId\)\) _totalBossKills\+\+;",
             "正常死亡先 MarkKilled 结案，再给原资产仍在场的本人累计击杀"),
            ("TechnicalCleanupReason",
             r"handle\.CleanupOnce\(ManagedBossCleanupReason\.TechnicalLoss\);",
             "技术丢失按 TechnicalLoss 原因清理 handle"),
            # 2026-10-16 玩家反馈：流血致死被别的 OnDead 订阅者异常截断，Boss 倒了不计击杀、整波卡住。
            ("SilentDeathSweepDriven",
             r"private void DriveCore\(float deltaTime\)[\s\S]{0,2500}?SweepSilentBossDeaths\(\);",
             "每帧驱动要跑已登记 Boss 死亡兜底轮询"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, module):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if sweep:
        checks = [
            ("SilentDeathSweepPhases",
             r"private void SweepSilentBossDeaths\(\)\s*\{\s*"
             r"if \(!CoopRunHasWorldAuthority \|\| !CanContinueRun\(\)\s*"
             r"\|\| _waveSpawnInFlight \|\| _batchActivationInProgress \|\| _spawnTransaction\.ActiveBossCount == 0\) return;\s*"
             r"if \(_state\.combatPhase != ModeGCombatPhase\.Fighting && _state\.combatPhase != ModeGCombatPhase\.LastStand\) return;",
             "兜底只在房主（世界权威）且本局可继续时、战斗 / 最后处决阶段扫，生成与批量激活期间交给技术丢失路由"),
            ("SilentDeathSweepRoutes",
             r"private void SweepSilentBossDeaths\(\)[\s\S]{0,1500}?dead = health == null \|\| health\.IsDead;"
             r"[\s\S]{0,400}?ModeGEntryRuntime\.PublishCoopCombat\(health, default\(DamageInfo\), true\);"
             r"\s*HandleBossDead\(health, default\(DamageInfo\)\);"
             r"[\s\S]{0,400}?if \(settled && _spawnTransaction\.ActiveBossCount == 0\) _waveSettlementPending = true;",
             "兜底先给原资产发联机个人战斗事实，再交给同一条死亡路由，存活归零必进结算"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, sweep):
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
