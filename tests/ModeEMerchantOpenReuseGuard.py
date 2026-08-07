"""Guard scoped shop-view and auxiliary-control reuse for Mode E/F merchants."""

from pathlib import Path
import sys


SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")
HARMONY = Path("ModeE/ModeEHarmonyPatch.cs")


def fail(message: str) -> int:
    print("ModeEMerchantOpenReuseGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
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
                return text[start : index + 1]
    return ""


def main() -> int:
    support = SUPPORT.read_text(encoding="utf-8")
    harmony = HARMONY.read_text(encoding="utf-8")

    reuse = extract_method(
        support,
        "internal static bool CanReuseShopViewSetup(StockShopView shopView, StockShop shop)",
    )
    for token in [
        "object.ReferenceEquals(shopView.Target, shop)",
        "GetModeEShellShopPatchDisposition(shop)",
        "ModeEShellShopPatchDisposition.HandleModeE",
        "ModeEShellShopPatchDisposition.PassOriginal",
        "inst.IsModeFActive",
        'shop.MerchantID.StartsWith("ModeE_", StringComparison.Ordinal)',
    ]:
        if token not in reuse:
            return fail("shop-view reuse scope missing -> " + token)

    if '[HarmonyPatch(typeof(StockShopView), "Setup", new Type[] { typeof(StockShop) })]' not in harmony:
        return fail("StockShopView.Setup signature-specific Harmony contract is missing")
    for token in [
        "ModeEMerchantSellAllUI.CanReuseShopViewSetup(__instance, target)",
        "ModeEMerchantSellAllUI.BeginShopViewSetup(target);",
        "ModeEMerchantSellAllUI.EndShopViewSetup();",
        "[HarmonyFinalizer]",
    ]:
        if token not in harmony:
            return fail("StockShopView.Setup scoped reuse lifecycle missing -> " + token)

    inventory_reuse = extract_method(
        support,
        "internal static bool CanReuseInventoryDisplaySetup",
    )
    for token in [
        "modeEMerchantShopViewSetupInProgress",
        "object.ReferenceEquals(display.Target, target)",
    ]:
        if token not in inventory_reuse:
            return fail("inventory display reuse scope missing -> " + token)

    if "typeof(Duckov.UI.InventoryDisplay)" not in harmony:
        return fail("InventoryDisplay.Setup Harmony target is missing")
    for token in [
        "typeof(Inventory)",
        "typeof(Func<Item, bool>)",
        "ModeEMerchantSellAllUI.CanReuseInventoryDisplaySetup(__instance, target)",
    ]:
        if token not in harmony:
            return fail("InventoryDisplay.Setup signature/scoping missing -> " + token)

    attach = extract_method(support, "internal static void Attach(StockShop shop)")
    if "Cleanup(false);" not in attach:
        return fail("shop attach must preserve reusable auxiliary controls")

    create_button = extract_method(support, "private static void CreateSellAllButton()")
    if create_button.find("if (sellAllButtonObject == null)") > create_button.find("UnityEngine.Object.Instantiate"):
        return fail("sell-all button must instantiate only on cache miss")

    create_balance = extract_method(support, "private static void CreateShellBalanceText()")
    if create_balance.find("if (shellBalanceTextObject == null)") > create_balance.find("UnityEngine.Object.Instantiate"):
        return fail("shell balance text must instantiate only on cache miss")

    cleanup = extract_method(support, "private static void Cleanup(bool destroyUiObjects)")
    for token in ["if (destroyUiObjects)", "sellAllButtonObject.SetActive(false)",
                  "shellBalanceTextObject.SetActive(false)"]:
        if token not in cleanup:
            return fail("cleanup reuse lifecycle missing -> " + token)

    reset = extract_method(support, "internal static void ResetStaticCaches()")
    for token in ["modeEMerchantShopViewSetupInProgress = false;", "Cleanup(true);"]:
        if token not in reset:
            return fail("runtime teardown must clear merchant UI reuse state -> " + token)

    print("ModeEMerchantOpenReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
