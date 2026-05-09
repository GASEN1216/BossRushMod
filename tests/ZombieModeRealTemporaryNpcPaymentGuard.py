from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REFORGE_UI = Path("Integration/Reforge/ReforgeUIManager.cs")
NURSE_HEAL = Path("Integration/NPCs/Nurse/NurseHealInteractable.cs")
NURSE_SERVICE = Path("Integration/NPCs/Nurse/NurseHealingService.cs")
COURIER_SERVICE = Path("Integration/NPCs/Courier/CourierService.cs")
COURIER_SWEEP = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")
COURIER_STORAGE = Path("Integration/NPCs/Courier/StorageDepositService.cs")
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
    rewards = REWARDS.read_text(encoding="utf-8")
    reforge_ui = REFORGE_UI.read_text(encoding="utf-8")
    nurse_heal = NURSE_HEAL.read_text(encoding="utf-8")
    nurse_service = NURSE_SERVICE.read_text(encoding="utf-8")
    courier_service = COURIER_SERVICE.read_text(encoding="utf-8")
    courier_sweep = COURIER_SWEEP.read_text(encoding="utf-8")
    courier_storage = COURIER_STORAGE.read_text(encoding="utf-8")
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
