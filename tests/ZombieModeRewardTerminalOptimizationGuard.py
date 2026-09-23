"""Guard ZombieMode reward/terminal quality and low-frequency UI feedback."""

from pathlib import Path
import re
import sys


CATALOG = Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs")
NPC_CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")
# 2026-09-23 审美审查：奖励选择面板与终端服务面板从 ZombieModeRewards.cs（宿主 partial）拆到独立文件。
REWARD_VIEWS = [
    Path("ZombieMode/ZombieModeRewardSelectionView.cs"),
    Path("ZombieMode/ZombieModeTemporaryNpcServiceView.cs"),
]
CLEANUP = Path("ZombieMode/ZombieModeCleanup.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print("ZombieModeRewardTerminalOptimizationGuard: FAIL - " + message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    catalog = CATALOG.read_text(encoding="utf-8")
    npc_catalog = NPC_CATALOG.read_text(encoding="utf-8")
    rewards = "\n".join(path.read_text(encoding="utf-8") for path in REWARD_VIEWS)
    cleanup = CLEANUP.read_text(encoding="utf-8")
    localization = LOCALIZATION.read_text(encoding="utf-8")

    build_catalog = extract_method(catalog, "private List<ZombieModeRewardCatalogEntry> BuildZombieModeRewardCatalogEntries")
    if not build_catalog:
        return fail("reward catalog builder missing")
    for dead_option in ["CurrentNodeFreeRefresh", "NextNodeFreeRefresh"]:
        if "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType." + dead_option in build_catalog:
            return fail("non-functional refresh reward returned to the catalog -> " + dead_option)

    cap_filter = extract_method(catalog, "private bool IsZombieModeRewardAtSelectionCap")
    for token in [
        "case ZombieModeRewardType.TempMerchant:",
        "zombieModeRunState.GuaranteedMerchantPurchasePending",
        "case ZombieModeRewardType.TempNurse:",
        'FindZombieModeTemporaryNpc("Nurse") != null',
        "case ZombieModeRewardType.HalfPricePaidRefresh:",
        "zombieModeRunState.HalfPriceNextPaidRefresh",
    ]:
        if token not in cap_filter:
            return fail("missing ineffective-repeat reward filter -> " + token)

    drink_entries = re.findall(r'DisplayKey = "BossRush_ZombieMode_Npc_Merchant_RandomDrink"[^\n]+GrantTag = "Drink"', npc_catalog)
    if len(drink_entries) != 2:
        return fail("normal and boss terminal stock must both include the reused Drink category")
    if 'InjectZombieModeString("BossRush_ZombieMode_Npc_Merchant_RandomDrink"' not in localization:
        return fail("drink terminal stock localization missing")

    # 终端格子的「买不起」反馈（2026-09-23 审美审查 UC-09 / UC-27 重做）：价格按余额换 DangerText，
    # 点了在格子上方就地提示「还差 N 净化点」（官方 Toast 被模态遮罩压着看不见）；卖完的格子写「售罄」、不可点。
    for token in [
        "BossRush_ZombieMode_Npc_MerchantSubtitle",
        "BossRush_ZombieMode_Npc_NurseSubtitle",
        "owner.GetZombieModePurificationPoints(runId)",
        "bool affordable",
        "affordable ? BossRushUIColors.WarningText : BossRushUIColors.DangerText",
        "BossRush_ZombieMode_Notify_PointsShort",
        "ZombieModeUiNudge.Flash(cell.Button",
        "ZombieModeUiNudge.Flash(button",
        "cell.Button.interactable = !soldOut;",
        "BossRush_ZombieMode_Npc_SoldOut",
        "canAffordPaidRefresh",
    ]:
        if token not in rewards:
            return fail("terminal affordability/feedback wiring missing -> " + token)

    for token in [
        'InjectZombieModeString("BossRush_ZombieMode_Npc_MerchantSubtitle"',
        'InjectZombieModeString("BossRush_ZombieMode_Npc_NurseSubtitle"',
    ]:
        if token not in localization:
            return fail("terminal subtitle localization missing -> " + token)

    register = extract_method(cleanup, "private void RegisterZombieModeRunOnlyObject")
    for token in [
        "kind == ZombieModeRunOnlyObjectKind.RewardUi",
        "existing.GameObject == null",
        "existing.CleanupAction == null",
        "zombieModeRunState.RunOnlyObjects.RemoveAt(i);",
    ]:
        if token not in register:
            return fail("destroyed RewardUi record pruning missing -> " + token)

    print("ZombieModeRewardTerminalOptimizationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
