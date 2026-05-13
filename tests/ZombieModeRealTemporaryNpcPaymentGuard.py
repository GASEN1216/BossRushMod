from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)

REFORGE_UI_PARTS = [
    Path("Integration/Reforge/ReforgeUIManager.cs"),
    Path("Integration/Reforge/ReforgeUIManager_ComparisonAndState.cs"),
    Path("Integration/Reforge/ReforgeUIManager_RuntimeAndCleanup.cs"),
]


def read_reforge_ui_manager() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REFORGE_UI_PARTS)

NURSE_HEAL = Path("Integration/NPCs/Nurse/NurseHealInteractable.cs")
NURSE_SERVICE = Path("Integration/NPCs/Nurse/NurseHealingService.cs")
COURIER_SERVICE_PARTS = [
    Path("Integration/NPCs/Courier/CourierService.cs"),
    Path("Integration/NPCs/Courier/CourierService_Buttons.cs"),
    Path("Integration/NPCs/Courier/CourierService_CloseAndCleanup.cs"),
]
COURIER_SWEEP = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")
COURIER_SWEEP_PARTS = [
    COURIER_SWEEP,
    Path("Integration/NPCs/Courier/CourierPaidLootSweepAccountingAndSort.cs"),
]
COURIER_STORAGE = Path("Integration/NPCs/Courier/StorageDepositService.cs")
STORAGE_DEPOSIT_PARTS = [
    Path("Integration/NPCs/Courier/StorageDepositService.cs"),
    Path("Integration/NPCs/Courier/StorageDepositLifecycle.cs"),
    Path("Integration/NPCs/Courier/StorageDepositTransactions.cs"),
    Path("Integration/NPCs/Courier/StorageDepositSingleRetrieve.cs"),
    Path("Integration/NPCs/Courier/StorageDepositInventoryQuickDeposit.cs"),
    Path("Integration/NPCs/Courier/StorageDepositBulkActions.cs"),
]


def read_storage_deposit_service() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in STORAGE_DEPOSIT_PARTS)


def read_courier_sweep_service() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in COURIER_SWEEP_PARTS)


def read_courier_service() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in COURIER_SERVICE_PARTS)

NPC_SHOP = Path("Integration/Affinity/Systems/NPCShopSystem.cs")


def fail(message: str) -> int:
    print("ZombieModeRealTemporaryNpcPaymentGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = read_rewards()
    reforge_ui = read_reforge_ui_manager()
    nurse_heal = NURSE_HEAL.read_text(encoding="utf-8")
    nurse_service = NURSE_SERVICE.read_text(encoding="utf-8")
    courier_service = read_courier_service()
    courier_sweep = read_courier_sweep_service()
    courier_storage = read_storage_deposit_service()
    npc_shop = NPC_SHOP.read_text(encoding="utf-8")

    for snippet in [
        "public sealed class ZombieModeTemporaryRealNpcMarker",
        "public int RunId;",
        "public string NpcType",
        "public bool UsesPurificationPayment",
    ]:
        result = require(models, snippet, "real NPC marker model")
        if result:
            return result

    for snippet in [
        "IsZombieModeTemporaryRealNpc(",
        "CanAffordZombieModePurificationPointsForRealNpc(",
        "TrySpendZombieModePurificationPointsForRealNpc(",
        "RefundZombieModePurificationPointsForRealNpc(",
        "GetZombieModePurificationPointsForRealNpcUi(",
        "GetZombieModeNpcHealCurrencyLabel(",
    ]:
        result = require(rewards, snippet, "payment helper")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "RefundZombieModePurificationPointsForRealNpc",
        "GetZombieModePurificationPointsForRealNpcUi",
        "IsZombieModeTemporaryRealNpc",
    ]:
        result = require(reforge_ui, snippet, "goblin reforge purification path")
        if result:
            return result

    reforge_click = extract_method(reforge_ui, "private static void OnReforgeButtonClick")
    for snippet in [
        "paidWithPurification",
        "reforgeCompleted",
        "RefundZombieModePurificationPointsForRealNpc(currentController, totalCost, paidWithPurification && !reforgeCompleted)",
    ]:
        result = require(reforge_click, snippet, "goblin reforge purification exception rollback")
        if result:
            return result

    for snippet in [
        "GetZombieModeNpcHealCurrencyLabel",
        "IsZombieModeTemporaryRealNpc",
    ]:
        result = require(nurse_heal, snippet, "nurse purification path")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "CanAffordZombieModePurificationPointsForRealNpc",
        "RefundZombieModePurificationPointsForRealNpc",
    ]:
        result = require(nurse_service, snippet, "nurse payment service path")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "CanAffordZombieModePurificationPointsForRealNpc",
        "IsZombieModeTemporaryRealNpc",
    ]:
        result = require(courier_service, snippet, "courier purification delivery path")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "RefundZombieModePurificationPointsForRealNpc",
        "CanAffordZombieModePurificationPointsForRealNpc",
        "IsZombieModeTemporaryRealNpc",
    ]:
        result = require(courier_sweep, snippet, "courier sweep purification path")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "CanAffordZombieModePurificationPointsForRealNpc",
        "IsZombieModeTemporaryRealNpc(courierNPCTransform)",
        "private static void OnItemPurchased(StockShop shop, Item purchasedItem)",
    ]:
        result = require(courier_storage, snippet, "courier storage purification path")
        if result:
            return result

    for snippet in [
        "TrySpendZombieModePurificationPointsForRealNpc",
        "CanAffordZombieModePurificationPointsForRealNpc",
        "IsZombieModeTemporaryRealNpc(currentNpcTransform)",
        "private static void OnItemPurchased(StockShop shop, Item purchasedItem)",
        "private static void OnItemSoldByPlayer(StockShop shop, Item soldItem, int price)",
        "StockShop.OnItemPurchased +=",
        "StockShop.OnItemPurchased -=",
        "StockShop.OnItemSoldByPlayer +=",
        "StockShop.OnItemSoldByPlayer -=",
    ]:
        result = require(npc_shop, snippet, "npc shop purification path")
        if result:
            return result

    print("ZombieModeRealTemporaryNpcPaymentGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
