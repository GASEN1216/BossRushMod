#!/usr/bin/env python3
"""
ModeGPlayerLoadoutGuard — Mode G 玩家装备守卫（owner 2026-08-18 裁决）。

不变式：
- 不 Patch CharacterDieTask、不快照/保险玩家 Item：ModeG/ 全目录禁
  CharacterDieTask 及玩家物品快照式操作；
- 玩家可携带当前装备、弹药、消耗品入场；入口不得复用裸装扫描；
- 唯一物品变更是入场消耗 1 信物（TypeID 500057，TryConsumeModeEntryItem）；
- 胜利返还幂等一次：TryReturnRelicOnce + Interlocked CAS 全局闸门。
- 启动退款所有权在 ArmStartupRefund 后只归 Runtime；外层仅在尚未移交时退款，
  避免后续 HUD/呈现异常导致双退，首波已开战后也不得错误退款。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")
ENTRY = os.path.join(MODEG_DIR, "ModeGEntry.cs")
REWARD = os.path.join(MODEG_DIR, "ModeGRewardTransaction.cs")
ENTRY_FLOW = os.path.join(REPO_ROOT, "WavesArena", "BossRushEntryFlow.cs")

FORBIDDEN = ["CharacterDieTask"]


def strip_comments(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []

    # 1. ModeG/ 全目录禁止快照/保险玩家 Item 相关符号
    for f in sorted(os.listdir(MODEG_DIR)):
        if not f.endswith(".cs"):
            continue
        content = strip_comments(read(os.path.join(MODEG_DIR, f)))
        for sym in FORBIDDEN:
            if sym in content:
                errors.append("[NoDieTaskPatch] ModeG/{} 引用 {}".format(f, sym))
        if re.search(r"\[HarmonyPatch", content):
            errors.append("[NoNewHarmony] ModeG/{} 新增 Harmony patch（装备隔离要求零 patch）".format(f))

    # 2. 唯一物品变更 = 入场消耗 1 信物
    if os.path.exists(ENTRY):
        entry = strip_comments(read(ENTRY))
        if "IsPlayerNakedWithAllowedItems" in entry or "IsPlayerNakedForModeG" in entry:
            errors.append("[BringLoadout] Mode G 入口重新引入裸装扫描")
        if not re.search(
                r"private bool IsModeGLoadoutEligible\(\)\s*\{\s*return true;\s*\}",
                entry):
            errors.append("[BringLoadout] Mode G 未显式允许玩家保留当前装备")
        if not re.search(r"TryConsumeModeEntryItem\(", entry):
            errors.append("[RelicConsume] Entry 缺少信物消耗入口 TryConsumeModeEntryItem")
        if "500057" not in read(ENTRY):
            errors.append("[RelicTypeId] Entry 未绑定 TypeID 500057 信物")
    else:
        errors.append("ModeGEntry.cs 不存在")

    if os.path.exists(ENTRY_FLOW):
        entry_flow = strip_comments(read(ENTRY_FLOW))
        if not re.search(
                r"IsModeGLoadoutEligible\(\)[\s\S]{0,180}?"
                r"return BossRushEntryMode\.ModeG;",
                entry_flow):
            errors.append("[AutomaticEntryLoadout] 自动分流未允许携带装备进入 Mode G")
    else:
        errors.append("WavesArena/BossRushEntryFlow.cs 不存在")

    # 2b. 启动退款所有权只能由外层或 Runtime 一方持有
    if os.path.exists(ENTRY):
        entry = read(ENTRY)
        startup_checks = [
            ("RefundOwnershipOut",
             r"out bool startupRefundOwnedByRuntime",
             "StartModeGRuntime 显式返回启动退款所有权"),
            ("OuterRefundGate",
             r"if \(!started && !startupRefundOwnedByRuntime\)",
             "外层只在退款所有权未移交时返还"),
            ("OwnershipTransferAfterArm",
             r"ArmStartupRefund\([\s\S]{0,180}?startupRefundOwnedByRuntime = true;",
             "ArmStartupRefund 后移交退款所有权"),
            ("RuntimeSettlesStartFailure",
             r"if \(!modeGRuntime\.StartRun\(\)\)[\s\S]{0,300}?"
             r"modeGRuntime\.End\(ModeGExitReason\.TechnicalIntegrityLoss\);",
             "Runtime 结算 StartRun 失败，不把已移交退款交回外层"),
        ]
        for name, pattern, desc in startup_checks:
            if not re.search(pattern, entry):
                errors.append("[{}] 不满足: {}".format(name, desc))

    # 3. 胜利返还幂等一次
    if os.path.exists(REWARD):
        reward = read(REWARD)
        checks = [
            ("ReturnRelicOnce",
             r"private static bool TryReturnRelicOnce\(",
             "信物返还幂等入口"),
            ("InterlockedGate",
             r"Interlocked\.Exchange\(ref _relicReturnExecuted, 1\) != 0\) return true;",
             "Interlocked CAS 全局一次闸门"),
            ("VictoryOnlyReturn",
             r"battleResult != ModeGBattleResult\.Victory",
             "返还仅在 Victory 结算"),
            ("ReliableDelivery",
             r"TryCommitItemWithGroundFallback\(\s*relic,\s*inventory,"
             r"[\s\S]{0,600}?CharacterMainControl\.Main,",
             "信物调用统一的背包/仓库/地面可靠交付入口"),
            ("GroundFallbackPostcondition",
             r"internal static bool TryCommitItemWithGroundFallback\("
             r"[\s\S]{0,700}?TryCommitItemToInventoryOrStorage\(item, inventory, itemLabel\)"
             r"[\s\S]{0,700}?DuckovItemAgent pickup = item\.Drop\("
             r"[\s\S]{0,900}?item\.ActiveAgent != null",
             "地面 fallback 验证实际拾取代理"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, reward):
                errors.append("[{}] 不满足: {}".format(name, desc))
    else:
        errors.append("ModeGRewardTransaction.cs 不存在")

    if errors:
        print("ModeGPlayerLoadoutGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGPlayerLoadoutGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
