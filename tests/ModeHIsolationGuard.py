#!/usr/bin/env python3
"""
ModeHIsolationGuard — Mode H 隔离与玩家资产白名单守卫（设计提案 §17.1、§24.3、§26.1）。

不变式：
- 玩家真实资产的引用点严格等于 §17.1 白名单的三条路径，且每条只允许其表内对象：
  1) ModeHEntry.TryRefundPrepaidTicket()：只允许 Inventory + 一个船票 typeId，禁止 PlayerStorage；
  2) ModeHLoadoutKitApplicator：只允许 owner 标记且 inactive 的临时选手实例 slots/inventory；
  3) ModeHWarehouseStakeJournal + ModeHInventoryPersistenceBridge：真实押品/奖励根物品；
- 白名单以外的任何 Mode H 文件都不得出现 Inventory / PlayerStorage / ItemTreeData；
- Mode H 不写旧模式状态（波次计数、Mode E/F/G/Zombie profile/loot/mutator、全局玩家生命）；
- 退款路径必须先 ClearPendingEntryFlowState 再发票，失败时 DestroyTree；
- InputManager.ActiveInput 调用点必须有 instance 判空；
- 退出后 IsModeHStandInActive 必须归零。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")

# 白名单：文件名 -> 允许出现的玩家资产符号
ASSET_WHITELIST = {
    "ModeHEntry.cs": {"Inventory"},
    "ModeHLoadoutKitApplicator.cs": {"Inventory"},
    "ModeHWarehouseStakeJournal.cs": {"Inventory", "PlayerStorage", "ItemTreeData"},
    "ModeHInventoryPersistenceBridge.cs": {"Inventory", "PlayerStorage", "ItemTreeData"},
    "ModeHItemTreeNormalizer.cs": {"ItemTreeData"},
}

ASSET_SYMBOLS = ["PlayerStorage", "ItemTreeData", "Inventory"]

# 旧模式状态符号：Mode H 不得写入
LEGACY_STATE_SYMBOLS = [
    "MutatorManager.RollAndApply",
    "MutatorContext",
    "modeEActive =",
    "modeFActive =",
    "modeGActive =",
    "zombieModeRunState",
    "currentWave =",
    "bossesPerWave =",
]


def main():
    errors = []

    if not os.path.isdir(MODEH_DIR):
        print("ModeHIsolationGuard: FAIL (1 errors)")
        print("  - [File] 缺少 ModeH 目录")
        return 1

    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        code = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        allowed = ASSET_WHITELIST.get(name, set())
        for symbol in ASSET_SYMBOLS:
            if re.search(r"\b{}\b".format(re.escape(symbol)), code) and symbol not in allowed:
                errors.append("[Whitelist] {} 不得引用玩家资产符号: {}".format(name, symbol))

        for symbol in LEGACY_STATE_SYMBOLS:
            if symbol in code:
                errors.append("[Legacy] {} 不得写入旧模式状态: {}".format(name, symbol))

        # ActiveInput 不检查内部 instance：调用点必须自行判空。
        # InputManager.instance 是私有静态成员，Mod 侧等价判空是 LevelManager.Instance.InputManager。
        if "InputManager.ActiveInput" in code:
            has_guard = (re.search(r"IsInputManagerAlive\(\)", code)
                         and re.search(r"LevelManager\.Instance\.InputManager != null", code))
            if not has_guard:
                errors.append("[Input] {} 调用 ActiveInput 前必须判空 InputManager 实例".format(name))

    entry = read_text(os.path.join(MODEH_DIR, "ModeHEntry.cs"))
    if entry is None:
        errors.append("[File] 缺少 ModeH/ModeHEntry.cs")
    else:
        code = strip_cs_comments(entry)
        checks = [
            (r"internal static bool TryRefundPrepaidTicket\(\)", "唯一退款入口"),
            (r"BossRushMapSelectionHelper\.GetBossRushTicketTypeId\(\)", "只允许船票 typeId"),
            (r"item\.DestroyTree\(\)", "发票失败清理临时 Item"),
            (r"player\.CharacterItem\.Inventory", "只写玩家 Inventory"),
        ]
        for pattern, desc in checks:
            if not re.search(pattern, code):
                errors.append("[Refund] 不满足: " + desc)

        refund = re.search(r"internal static bool TryRefundPrepaidTicket\(\)[\s\S]*?\n        \}", code)
        if refund:
            body = refund.group(0)
            clear_pos = body.find("CancelPendingEntry()")
            instantiate_pos = body.find("InstantiateSync")
            if clear_pos < 0:
                errors.append("[Refund] 必须先清除预扣所有权")
            elif instantiate_pos >= 0 and clear_pos > instantiate_pos:
                errors.append("[Refund] 必须在实例化船票之前清除预扣所有权")

        # 退款路径禁止碰仓库
        if "SendToPlayerStorage" in code or "PlayerStorage" in code:
            errors.append("[Refund] 退款路径不得触碰 PlayerStorage")

        # 只允许一个 typeId：不得出现第二个 typeId 常量来源
        type_id_sources = re.findall(r"ItemAssetsCollection\.InstantiateSync\(([^)]+)\)", code)
        for source in type_id_sources:
            if source.strip() != "typeId":
                errors.append("[Refund] 只允许实例化船票 typeId，发现: " + source.strip())

    # StandIn gate 归零（文件存在时才断言，ERROR 部件在步骤 11 落地）
    combat = read_text(os.path.join(MODEH_DIR, "ModeHCombatControl.cs"))
    if combat is not None:
        code = strip_cs_comments(combat)
        if "IsModeHStandInActive" in code and "IsModeHStandInActive = false" not in code:
            errors.append("[StandIn] 退出路径必须把 IsModeHStandInActive 归零")

    if errors:
        print("ModeHIsolationGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHIsolationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
