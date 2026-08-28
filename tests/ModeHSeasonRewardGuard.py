#!/usr/bin/env python3
"""
ModeHSeasonRewardGuard — Mode H 虚拟奖励守卫（设计提案 §17.8、§20.1、§26.1）。

不变式：
- report / profile / roster / 虚拟下注 / 候选列表 / 虚拟奖励都在单一 Season payload；
- `Offered -> Applied -> Archived` 单向，且 Archived 必须来自 Applied；
- 候选列表由 seed 确定性构造，重启后不重抽；
- 败场固定 `rewardKind=None` 且直接 `Applied`；
- 候选耗尽自动 `FameDisplay + Applied`；
- 套装/名声目标字段互斥：`UnlockKit` 才写 selectedRewardKitId；
- `operationId` / `eventTokenId` 幂等：同一事件只产生一条 operation；
- `rewardKind=Kit` 才允许调用 WishFountainRewardAnimationView.PlayRuntime；
  `rewardKind=Fame` 必须走 Mode H 自有静态横幅且不构造任何物品 typeId；
- 虚拟奖励服务不得创建或交付真实 Item（不引用 Inventory / PlayerStorage /
  ItemAssetsCollection / ModeHRewardTransaction / journal）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import contains_symbol, read_modeh_group, read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
SERVICE = os.path.join(MODEH_DIR, "ModeHSeasonRewardService.cs")
MARKET = os.path.join(MODEH_DIR, "ModeHTransferMarket.cs")

FORBIDDEN_ASSET_SYMBOLS = [
    "Inventory", "PlayerStorage", "ItemAssetsCollection", "ItemTreeData",
    "ModeHRewardTransaction", "ModeHWarehouseStakeJournal",
    "ModeHInventoryPersistenceBridge", "InstantiateSync",
]


def check_service(errors):
    source = read_text(SERVICE)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHSeasonRewardService.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"public static ModeHSeasonRewardOperationDto BuildOrGet\(", "构造入口"),
        (r"ModeHSeasonRewardOperationDto existing = FindByEventToken\(season, eventTokenId\);",
         "同一 eventTokenId 幂等"),
        (r"if \(existing != null\) return existing;", "幂等返回既有条目"),
        (r"public static bool TrySelectKit\(", "选择候选套装"),
        (r"public static bool TryDeclineToFame\(", "拒绝转名声"),
        (r"public static bool TryArchive\(", "归档入口"),
        (r"public static bool AllowsRewardWheel\(", "奖励滚轮门"),
        (r"ModeHSeedStream\.Domains\.Reward", "候选由 seed 域派生"),
        (r"stream\.TakeDistinct\(pool, candidateCount\)", "候选确定性抽取"),
        (r"picked\.Sort\(StringComparer\.Ordinal\);", "候选顺序稳定"),
        (r"ModeHConfig\.MaxFameDisplayCount", "名声上限引用冻结常量"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Service] 不满足: " + desc)

    # 败场固定 None + Applied
    if not re.search(
            r"operation\.rewardKind = \(int\)ModeHRewardKind\.None;\s*\n\s*"
            r"operation\.status = \(int\)ModeHSeasonRewardOperationStatus\.Applied;", code):
        errors.append("[Service] 败场必须固定 None + Applied")

    # 候选耗尽 -> FameDisplay + Applied
    if not re.search(
            r"operation\.rewardKind = \(int\)ModeHRewardKind\.FameDisplay;\s*\n\s*"
            r"operation\.status = \(int\)ModeHSeasonRewardOperationStatus\.Applied;", code):
        errors.append("[Service] 候选耗尽必须自动 FameDisplay + Applied")

    # 有候选 -> UnlockKit + Offered 且所选项为空
    if not re.search(r"operation\.rewardKind = \(int\)ModeHRewardKind\.UnlockKit;", code) \
            or not re.search(r"operation\.status = \(int\)ModeHSeasonRewardOperationStatus\.Offered;",
                             code):
        errors.append("[Service] 胜场有候选必须是 UnlockKit + Offered")
    if not re.search(r"operation\.selectedRewardKitId = string\.Empty;", code):
        errors.append("[Service] Offered 阶段所选项必须为空")

    # 单向状态机：Archived 只能来自 Applied
    archive = re.search(r"public static bool TryArchive\([\s\S]*?\n        \}", code)
    if archive and "reward_archive_requires_applied" not in archive.group(0):
        errors.append("[Service] Archived 只能来自 Applied")

    # 选择套装必须先校验候选归属
    select = re.search(r"public static bool TrySelectKit\([\s\S]*?\n        \}", code)
    if select:
        body = select.group(0)
        for required in ["reward_not_offered", "reward_kind_mismatch", "reward_kit_not_candidate"]:
            if required not in body:
                errors.append("[Service] 选择套装缺少校验: " + required)

    # 名声路径不得构造 typeId
    fame = re.search(r"private static void ApplyFameDisplay\([\s\S]*?\n        \}", code)
    if fame and re.search(r"typeId|TypeID|InstantiateSync", fame.group(0)):
        errors.append("[Service] 名声路径不得构造任何物品 typeId")

    for forbidden in FORBIDDEN_ASSET_SYMBOLS:
        if contains_symbol(code, forbidden):
            errors.append("[Service] 虚拟奖励服务不得引用真实资产符号: " + forbidden)


def check_reward_wheel_gate(errors):
    """
    rewardKind=Kit 才允许**播放**官方奖励滚轮。
    只在出现 PlayRuntime 调用时要求 AllowsRewardWheel 门；
    §23.1 允许恢复壳在显示前销毁奖励揭晓层，那不是播放。
    """
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        code = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        if "WishFountainRewardAnimationView" not in code:
            continue
        if "PlayRuntime" not in code:
            continue
        if "AllowsRewardWheel" not in code:
            errors.append(
                "[Wheel] {} 播放奖励滚轮前必须先过 AllowsRewardWheel 门".format(name))


def check_market(errors):
    source = read_text(MARKET)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHTransferMarket.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"ModeHConfig\.FirstTransferWindowMatchIndex", "第 2 场窗口引用冻结常量"),
        (r"ModeHConfig\.SecondTransferWindowMatchIndex", "第 4 场窗口引用冻结常量"),
        (r"public static ModeHOfferDto BuildOffer\(", "offer 构造入口"),
        (r"if \(season\.currentOffer != null && season\.currentOffer\.windowMatchIndex == matchIndex\)",
         "同一窗口不得刷新"),
        (r"report\.finalDefeatedProfileSnapshot", "第 4 场读最近已结算战报快照"),
        (r"report\.specialEnemyEligible", "读资格标志"),
        (r"report\.specialEnemySourceTag", "读来源标签"),
        (r"ModeHPresetRegistry\.IsProductionKey\(template\.StableKey\)", "资格快照过同一安全审计"),
        (r'failureReasonId = "market_profile_removed";', "撕票不得返场"),
        (r'failureReasonId = "market_profile_released";', "被释放者本季不可签回"),
        (r"public static bool ApplyRetirement\(", "退役结算"),
        (r'failureReasonId = "retire_season_ended";', "无存活合同即赛季结束"),
        (r"public static List<string> GetLiveContractProfileIds\(", "存活合同列表"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Market] 不满足: " + desc)

    # contractMain 不可主动出售或释放
    accept = re.search(r"public static bool TryAcceptOffer\([\s\S]*?\n        \}", code)
    if accept:
        body = accept.group(0)
        if "contractMainProfileId =" in body:
            errors.append("[Market] contractMain 永远不可被市场替换")
        if "ModeHParticipantStatus.Released" not in body:
            errors.append("[Market] 被替换且未退役的旧合同选手必须记为 Released")

    # 不得运行时临时生成新 Boss
    ensure = re.search(r"private static void EnsureProfilePresent\([\s\S]*?\n        \}", code)
    if ensure and "ModeHProfileRegistry.GetByTemplateId" not in ensure.group(0):
        errors.append("[Market] 新合同选手只能来自签名目录，不得运行时生成")

    # offer 状态不得退化成单个 accepted 布尔
    if re.search(r"\bbool\s+accepted\b", code):
        errors.append("[Market] 禁止用单个 accepted 布尔混淆未选择/拒绝/过期")


def check_single_payload(errors):
    """report / roster / 下注 / 候选 / 奖励都必须在同一个 Season payload 内。"""
    model = read_modeh_group("ModeHStateModel.cs", "ModeHStateDtos.cs")
    if model is None:
        errors.append("[File] 缺少 Mode H 状态模型")
        return
    code = strip_cs_comments(model)

    season = re.search(r"public sealed class ModeHSeasonDto[\s\S]*?\n    \}", code)
    if not season:
        errors.append("[DTO] 未找到 ModeHSeasonDto")
        return
    body = season.group(0)
    for field in ["matchReports", "profiles", "contract", "matchRoster",
                  "virtualStakeCredits", "reservedVirtualStake", "unlockedKitIds",
                  "seasonRewardOperations", "currentOffer", "appliedEventTokenIds"]:
        if not re.search(r"\b{}\s*;".format(field), body):
            errors.append("[DTO] Season 缺少字段: " + field)

    operation = re.search(
        r"public sealed class ModeHSeasonRewardOperationDto[\s\S]*?\n    \}", code)
    if operation:
        op_body = operation.group(0)
        for field in ["operationId", "eventTokenId", "matchIndex", "resultToken",
                      "rewardKind", "candidateKitIds", "selectedRewardKitId",
                      "rewardProfileId", "status"]:
            if not re.search(r"\b{}\s*;".format(field), op_body):
                errors.append("[DTO] 虚拟奖励 operation 缺少字段: " + field)
        # 不含 item / receipt / 库存字段
        for forbidden in ["itemResults", "receipts", "inventoryPreDigest", "typeId"]:
            if re.search(r"\b{}\s*;".format(forbidden), op_body):
                errors.append("[DTO] 虚拟奖励 operation 不得包含真实资产字段: " + forbidden)


def main():
    errors = []
    check_service(errors)
    check_reward_wheel_gate(errors)
    check_market(errors)
    check_single_payload(errors)

    if errors:
        print("ModeHSeasonRewardGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHSeasonRewardGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
