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
        "modeEMerchantProgressivePopulationComplete",
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
        "ModeEMerchantSellAllUI.PrepareProgressiveShopViewSetup(",
        "ModeEMerchantSellAllUI.CompleteProgressiveShopViewSetup(",
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

    for token in [
        "private const int MODE_E_SHOP_INITIAL_ENTRY_COUNT = 24;",
        "private const int MODE_E_SHOP_ENTRIES_PER_FRAME = 12;",
        "internal sealed class ProgressiveShopViewSetupState",
        "PrepareProgressiveShopViewSetup(",
        "PopulateRemainingModeEShopEntriesAsync(",
        "await UniTask.Yield();",
        "PrefabPool<StockShopItemEntry>",
        "stockShopItemEntrySetup(itemEntry, shopView, entry);",
        "owner.ApplyModeEShellItemEntryUi(itemEntry, shopView, entry);",
        "TryRecycleActiveModeEShopEntries(shopView, state);",
        "activeEntries.Clear();",
        "entryPool.Release(entriesToRelease[i]);",
        "RestoreModeEShopEntryContentRoot(state);",
        '"[ModeE] [Profile] shop setup sync:',
        '"[ModeE] [Profile] shop open sync:',
        "CancelProgressiveShopViewPopulation(currentShop);",
        "object.ReferenceEquals(shopView.Target, state.Shop)",
    ]:
        if token not in support:
            return fail("first-open progressive population missing -> " + token)

    prepare = extract_method(
        support,
        "internal static ProgressiveShopViewSetupState PrepareProgressiveShopViewSetup(",
    )
    for token in [
        "ModeEShellShopPatchDisposition.HandleModeE",
        "stockShopEntryPoolField == null",
        "stockShopEntryPoolField.FieldType != typeof(PrefabPool<StockShopItemEntry>)",
        "stockShopItemEntrySetup == null",
        "shop.entries = initialEntries;",
    ]:
        if token not in prepare:
            return fail("progressive setup must scope/fail open -> " + token)

    recycle = extract_method(
        support,
        "private static void TryRecycleActiveModeEShopEntries(",
    )
    for token in [
        "stockShopPoolActiveEntriesField.FieldType != typeof(List<StockShopItemEntry>)",
        "state.EntryContentRoot.SetActive(false);",
        "activeEntries.Clear();",
        "entryPool.Release(entriesToRelease[i]);",
        "activeEntries.Add(entry);",
    ]:
        if token not in recycle:
            return fail("large previous category must use guarded linear recycle -> " + token)

    complete = extract_method(
        support,
        "internal static void CompleteProgressiveShopViewSetup(",
    )
    if "state.Shop.entries = state.OriginalEntries;" not in complete:
        return fail("progressive setup must restore the complete shop entries list")

    cancel = extract_method(
        support,
        "private static void CancelProgressiveShopViewPopulation(StockShop shop)",
    )
    if "NextProgressivePopulationID();" not in cancel:
        return fail("closing an in-flight shop population must invalidate its task")
    if "modeEMerchantProgressivePopulationComplete = false" in cancel:
        return fail("closing a fully populated shop must preserve same-shop reuse")

    attach = extract_method(support, "internal static void Attach(StockShop shop)")
    if "Cleanup(false);" not in attach:
        return fail("shop attach must preserve reusable auxiliary controls")

    refresh = extract_method(
        support,
        "internal void RefreshOpenModeEShellUi(",
    )
    for token in [
        "if (itemTypeID.HasValue)",
        "GetComponentsInChildren<StockShopItemEntry>(false)",
        "row.Target.ItemTypeID != itemTypeID.Value",
        "modeEShellRefreshInteractionButtonTarget.Invoke(view, null)",
    ]:
        if token not in refresh:
            return fail("shell UI refresh must only revisit active price-event rows -> " + token)
    if "GetComponentsInChildren<StockShopItemEntry>(true)" in refresh:
        return fail("shell UI refresh must not traverse inactive pooled shop rows")

    create_button = extract_method(support, "private static void CreateSellAllButton()")
    if create_button.find("if (sellAllButtonObject == null)") > create_button.find("UnityEngine.Object.Instantiate"):
        return fail("sell-all button must instantiate only on cache miss")
    for token in [
        "sourceLayoutElement.preferredWidth + 80f",
        "sourceLayoutElement.minWidth + 80f",
    ]:
        if token not in create_button:
            return fail("reused sell-all button dimensions must derive from the source -> " + token)
    for token in ["layoutElement.preferredWidth +=", "layoutElement.minWidth +="]:
        if token in create_button:
            return fail("reused sell-all button dimensions must not accumulate -> " + token)

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
