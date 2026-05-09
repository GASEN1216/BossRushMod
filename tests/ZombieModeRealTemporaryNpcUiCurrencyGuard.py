from pathlib import Path
import sys


COURIER_SERVICE = Path("Integration/NPCs/Courier/CourierService.cs")
COURIER_SWEEP = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")
COURIER_STORAGE = Path("Integration/NPCs/Courier/StorageDepositService.cs")
NPC_SHOP = Path("Integration/Affinity/Systems/NPCShopSystem.cs")


def fail(message: str) -> int:
    print("ZombieModeRealTemporaryNpcUiCurrencyGuard: FAIL - " + message)
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


def require_not(text: str, snippet: str, label: str) -> int:
    if snippet in text:
        return fail("stale " + label + " -> " + snippet)
    return 0


def main() -> int:
    courier_service = COURIER_SERVICE.read_text(encoding="utf-8")
    courier_sweep = COURIER_SWEEP.read_text(encoding="utf-8")
    courier_storage = COURIER_STORAGE.read_text(encoding="utf-8")
    npc_shop = NPC_SHOP.read_text(encoding="utf-8")

    for snippet in [
        "IsZombieModeTemporaryRealNpc(courierNPCTransform)",
        "净化点",
    ]:
        result = require(courier_service, snippet, "courier service button purification wording")
        if result:
            return result

    courier_auto_bubble = extract_method(courier_service, "private static void ShowAutoDeliveryBubble")
    for snippet in [
        "usePurification",
        "净化点我就收下了",
        "钱我就收下了",
        "Purification Points",
        "money",
    ]:
        result = require(courier_auto_bubble, snippet, "courier auto completion bubble currency branch")
        if result:
            return result

    courier_goodbye_bubble = extract_method(courier_service, "private static void ShowGoodbyeBubbleInternal")
    for snippet in [
        "净化点",
        "Purification",
        "usePurification",
    ]:
        result = require(courier_goodbye_bubble, snippet, "courier manual completion bubble purification wording")
        if result:
            return result
    for snippet in ["￥\" + lastDeliveryFee"]:
        result = require_not(courier_goodbye_bubble, snippet, "courier manual completion bubble cash wording")
        if result:
            return result

    for snippet in [
        "IsZombieModeTemporaryRealNpc(activeServiceController)",
        "净化点",
    ]:
        result = require(courier_sweep, snippet, "courier sweep purification wording")
        if result:
            return result

    for snippet in [
        "IsZombieModeTemporaryRealNpc(courierNPCTransform)",
        "净化点",
        "interactionButton",
        "interactionText",
        "priceText",
    ]:
        result = require(courier_storage, snippet, "courier storage purification wording")
        if result:
            return result

    storage_retrieve_all = extract_method(courier_storage, "private static async UniTaskVoid RetrieveAllItemsAsync")
    result = require(storage_retrieve_all, "GetTemporaryCourierPurificationInsufficientText()", "courier storage retrieve-all purification insufficient helper")
    if result:
        return result

    storage_purification_insufficient = extract_method(courier_storage, "private static string GetTemporaryCourierPurificationInsufficientText")
    for snippet in ["净化点不足", "Not enough Purification"]:
        result = require(storage_purification_insufficient, snippet, "courier storage retrieve-all purification insufficient wording")
        if result:
            return result

    storage_sync_selected = extract_method(courier_storage, "private static void SyncSelectedItemInstance")
    result = require(
        storage_sync_selected,
        "float newPriceFactor = IsZombieModeTemporaryCourierPurificationService() ? 0f : (float)correctFee / actualValue;",
        "courier storage temporary single retrieve zero stock price factor",
    )
    if result:
        return result

    storage_format_fee = extract_method(courier_storage, "private static string FormatRetrieveFeeText")
    result = require(storage_format_fee, "GetRetrieveFeePrefix() + fee.ToString(\"N0\")", "courier storage retrieve fee formatter uses currency prefix")
    if result:
        return result

    storage_fee_prefix = extract_method(courier_storage, "private static string GetRetrieveFeePrefix")
    for snippet in ["净化点 ", "Purification "]:
        result = require(storage_fee_prefix, snippet, "courier storage retrieve fee purification prefix")
        if result:
            return result

    for snippet in [
        "priceTextComp.text = fee.ToString(\"n0\");",
        "priceTextComp.text = fee.ToString(\"N0\");",
    ]:
        result = require_not(courier_storage, snippet, "courier storage bare retrieve list price")
        if result:
            return result

    for snippet in [
        "IsZombieModeTemporaryRealNpc(currentNpcTransform)",
        "净化点",
        "interactionButton",
        "interactionText",
        "priceText",
    ]:
        result = require(npc_shop, snippet, "npc shop purification wording")
        if result:
            return result

    for snippet in [
        "StockShop.OnItemSoldByPlayer += OnItemSoldByPlayer",
        "StockShop.OnItemSoldByPlayer -= OnItemSoldByPlayer",
        "private static void OnItemSoldByPlayer(StockShop shop, Item soldItem, int price)",
        "RejectTemporaryPurificationShopSell",
    ]:
        result = require(npc_shop, snippet, "npc shop temporary purification sell rejection")
        if result:
            return result

    print("ZombieModeRealTemporaryNpcUiCurrencyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
