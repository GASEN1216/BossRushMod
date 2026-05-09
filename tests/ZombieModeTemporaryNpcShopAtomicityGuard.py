"""ZombieModeTemporaryNpcShopAtomicityGuard: temporary purification shops must not free-purchase on missing price data."""

from pathlib import Path


SHOP = Path("Integration/Affinity/Systems/NPCShopSystem.cs")


def fail(message: str) -> int:
    print("ZombieModeTemporaryNpcShopAtomicityGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str:
    start = text.find(signature)
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
                return text[brace:index + 1]
    return ""


def main() -> int:
    text = SHOP.read_text(encoding="utf-8")
    required = [
        "TryGetPurificationPriceForType(",
        "CanPurchaseTemporaryPurificationShopSelection(",
        "RollbackTemporaryPurificationShopPurchase(",
        "priceAvailable",
        "TemporaryPurificationPriceUnavailable",
    ]
    for token in required:
        if token not in text:
            return fail("missing atomic shop guard token: " + token)

    forbidden = [
        "return temporaryPurificationPrices.TryGetValue(typeId, out price) ? price : 0;",
        "bool canAfford = price <= 0 || ModBehaviour.Instance.CanAffordZombieModePurificationPointsForRealNpc(currentNpcTransform, price);",
        "int price = GetPurificationPriceForType(purchasedItem.TypeID);",
    ]
    for token in forbidden:
        if token in text:
            return fail("shop still treats missing price as free: " + token)

    rollback = extract_method_body(text, "private static void RollbackTemporaryPurificationShopPurchase")
    if not rollback:
        return fail("missing temporary purification shop rollback method")
    if "int stockRollbackCount = 1;" not in rollback:
        return fail("rollback must restore the single stock entry actually purchased")
    if "purchasedItem.StackCount" in rollback:
        return fail("rollback must not restore stock by purchased item stack count")

    print("ZombieModeTemporaryNpcShopAtomicityGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
