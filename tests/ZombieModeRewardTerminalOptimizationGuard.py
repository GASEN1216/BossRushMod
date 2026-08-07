"""Guard ZombieMode reward/terminal quality and low-frequency UI feedback."""

from pathlib import Path
import re
import sys


CATALOG = Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs")
NPC_CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
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
    rewards = REWARDS.read_text(encoding="utf-8")
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

    for token in [
        "BossRush_ZombieMode_Npc_MerchantSubtitle",
        "BossRush_ZombieMode_Npc_NurseSubtitle",
        "owner.GetZombieModePurificationPoints(runId)",
        "bool affordable",
        "BossRush_ZombieMode_Notify_NpcServiceNoPoints",
        "bool interactable, bool affordable",
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
