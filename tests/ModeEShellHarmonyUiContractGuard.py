"""Guard Mode E shell Harmony UI contracts, sample readiness gate, and shell display text."""

from pathlib import Path
import sys


SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")
HARMONY = Path("ModeE/ModeEHarmonyPatch.cs")
MERCHANT = Path("ModeE/ModeEMerchant.cs")
GLOBAL_SAMPLE_PATCH = Path("Patches/Economy/StockShopGetItemInstanceDirectPatch.cs")


def fail(message: str) -> int:
    print("ModeEShellHarmonyUiContractGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for idx in range(brace, len(text)):
        if text[idx] == "{":
            depth += 1
        elif text[idx] == "}":
            depth -= 1
            if depth == 0:
                return text[start : idx + 1]
    return ""


def main() -> int:
    support = SUPPORT.read_text(encoding="utf-8")
    harmony = HARMONY.read_text(encoding="utf-8")
    merchant = MERCHANT.read_text(encoding="utf-8")
    global_sample_patch = GLOBAL_SAMPLE_PATCH.read_text(encoding="utf-8")
    combined = support + "\n" + harmony + "\n" + merchant + "\n" + global_sample_patch

    required = [
        'private const string MODE_E_SHELL_HARMONY_OWNER = "com.bossrush.mod";',
        "private bool ResolveModeEShellRuntimeContracts()",
        "private static bool IsModeEShellMethodContract(",
        "private static bool IsModeEShellFieldContract(",
        "private static bool IsModeEShellPropertyContract(",
        "private static void RecordModeEShellContractFailure(",
        "internal bool VerifyModeEShellPatchInstallation()",
        'AccessTools.Method(stockShopType, "Buy", buyArgs)',
        'AccessTools.Method(stockShopType, "Sell", sellArgs)',
        'AccessTools.Method(itemEntryType, "Setup", setupArgs)',
        'AccessTools.Method(\n                    viewType,\n                    "Setup",',
        'AccessTools.Method(viewType, "RefreshInteractionButton")',
        'AccessTools.Field(stockShopType, "buying")',
        'AccessTools.Field(stockShopType, "selling")',
        'AccessTools.Field(entryType, "currentStock")',
        'AccessTools.Field(entryType, "onStockChanged")',
        'AccessTools.Field(stockShopType, "OnAfterItemSold")',
        'AccessTools.Field(stockShopType, "OnItemPurchased")',
        'AccessTools.Field(itemEntryType, "priceText")',
        'AccessTools.Field(viewType, "priceText")',
        'AccessTools.Field(viewType, "interactionButton")',
        'AccessTools.Field(viewType, "interactionButtonImage")',
        'AccessTools.Field(viewType, "interactionText")',
        "HasExpectedModeEShellPatch(modeEShellBuyTarget, buyPrefix, true)",
        "HasExpectedModeEShellPatch(modeEShellSellTarget, sellPrefix, true)",
        "HasExpectedModeEShellPatch(modeEShellItemEntrySetupTarget, itemEntryPostfix, false)",
        "HasExpectedModeEShellPatch(modeEShellViewSetupTarget, viewSetupPrefix, true)",
        "HasExpectedModeEShellPatch(modeEShellRefreshInteractionButtonTarget, buttonPostfix, false)",
        "public static class ModeEShellBuyPatch",
        "public static class ModeEShellSellPatch",
        "public static class ModeEShellShopItemEntryPatch",
        "public static class ModeEMerchantShopViewSetupReusePatch",
        "public static class ModeEShellInteractionButtonPatch",
        "internal bool AreAllModeEShopOfficialSamplesReady(StockShop shop)",
        "GetItemInstanceDirect",
        'L10n.T("贝壳 ", "Shells ")',
        'L10n.T("出售到账户", "Sell to account")',
        "ApplyModeEShellItemEntryUi",
        "ApplyModeEShellInteractionButtonUi",
        "private void FailModeEShellMerchantBuild(GameObject npcGo, string reason)",
        "private bool TryConfigureModeEMerchantShopIdentity(",
        "internal static bool VerifyModeEShellRuntimeContracts()",
        "ModeEMerchantSellAllUI.VerifyModeEShellRuntimeContracts()",
        'M0/M1 反射契约不匹配:',
        'missingPatches.Add("StockShop.Buy prefix")',
        'missingPatches.Add("StockShop.Sell prefix")',
        'missingPatches.Add("StockShopItemEntry.Setup postfix")',
        'missingPatches.Add("StockShopView.Setup reuse prefix")',
        'missingPatches.Add("StockShopView.RefreshInteractionButton postfix")',
    ]
    for token in required:
        if token not in combined:
            return fail("missing UI/Harmony contract -> " + token)

    samples = extract_method(support, "internal bool AreAllModeEShopOfficialSamplesReady(StockShop shop)")
    if not samples:
        return fail("missing sample readiness helper")
    if "GetItemInstanceDirect" not in samples:
        return fail("sample readiness must check GetItemInstanceDirect")
    if "entries" not in samples:
        return fail("sample readiness must scan current category entries")

    # Interactable should gate ShowUI on sample readiness.
    if "AreAllModeEShopOfficialSamplesReady" not in support and "AreAllModeEShopOfficialSamplesReady" not in merchant:
        return fail("ModeEShopInteractable path must consult sample readiness before ShowUI")
    if "ShowUI()" in support:
        show_idx = support.find("ShowUI()")
        # Look backward for readiness check near ShowUI usages.
        window = support[max(0, show_idx - 400) : show_idx + 80]
        if "AreAllModeEShopOfficialSamplesReady" not in window and "NotifyModeEShopSamplesNotReady" not in support:
            # Still accept if helper is called somewhere before ShowUI in interactable class.
            if "AreAllModeEShopOfficialSamplesReady" not in support:
                return fail("ShowUI must be gated by sample readiness")

    item_ui = extract_method(support, "internal void ApplyModeEShellItemEntryUi(")
    if "IsCurrentModeEShellCapability(shop)" not in item_ui:
        return fail("item entry UI must require capability")
    if "Cost.Enough" in item_ui or "new Cost(" in item_ui:
        return fail("item entry UI must not use cash Cost.Enough")

    button_ui = extract_method(support, "internal void ApplyModeEShellInteractionButtonUi(StockShopView view)")
    if "modeEShellBalance" not in button_ui:
        return fail("interaction button must use shell balance")
    if "Cost.Enough" in button_ui:
        return fail("interaction button must not use cash Cost.Enough")
    if "出售到账户" not in button_ui:
        return fail("player inventory selection must show sell-to-account")

    if "TryAttachModeEShellUi" not in support or "DetachModeEShellUi" not in support:
        return fail("UI attach/detach must manage uiBindingId")
    if "SubscribeModeEShellUiEvents" not in support or "UnsubscribeModeEShellUiEvents" not in support:
        return fail("UI must subscribe/unsubscribe balance/price/gate events symmetrically")

    resolve = extract_method(support, "private bool ResolveModeEShellRuntimeContracts()")
    for token in [
        "typeof(UniTask<bool>)",
        "typeof(UniTask)",
        "typeof(Action<StockShop.Entry>)",
        "typeof(Action<StockShop>)",
        "typeof(Action<StockShop, Item>)",
        "typeof(StockShop)",
        "typeof(TextMeshProUGUI)",
        "typeof(Button)",
        "typeof(Image)",
        "ReflectionCache.StockShop_MerchantID",
        "ReflectionCache.StockShop_AccountAvaliable",
    ]:
        if token not in resolve:
            return fail("runtime preflight must validate member types -> " + token)
    if """modeEShellSellTarget, typeof(UniTask), false, typeof(Item))""" not in resolve:
        return fail("StockShop.Sell(Item) runtime contract must return non-generic UniTask")
    if "failures.Count > 0" not in resolve or "string.Join" not in resolve:
        return fail("runtime preflight must report the complete failed-contract set")

    verify_patches = extract_method(support, "internal bool VerifyModeEShellPatchInstallation()")
    if "missingPatches.Count == 0" not in verify_patches or "string.Join" not in verify_patches:
        return fail("Harmony preflight must report each missing patch")

    sample_prefix = extract_method(global_sample_patch, "public static void Prefix(StockShop __instance, int typeID)")
    if "GetModeEShellShopPatchDisposition(__instance)" not in sample_prefix:
        return fail("global sample fallback must classify Mode E owned/retired shops")
    if "ModeEShellShopPatchDisposition.PassOriginal" not in sample_prefix:
        return fail("global sample fallback may run only for PassOriginal shops")
    if sample_prefix.index("GetModeEShellShopPatchDisposition") > sample_prefix.index("StockShop_ItemInstances"):
        return fail("Mode E sample boundary must be checked before touching itemInstances")

    build = extract_method(merchant, "private void BuildModeEMerchantShop(GameObject npcGo)")
    for reason in [
        '"merchant interactable missing"',
        '"merchant interaction group contract missing"',
        '"merchant tags unavailable"',
        '"shop registration failed"',
        '"other shop registration failed"',
        '"merchant identity contract failed"',
        '"other shop identity contract failed"',
    ]:
        if reason not in build:
            return fail("shell merchant build must fail closed -> " + reason)

    fail_build = extract_method(merchant, "private void FailModeEShellMerchantBuild(")
    for token in ["SetModeEShellEconomyUnavailable", "npcGo.SetActive(false)",
                  "UnityEngine.Object.Destroy(npcGo)", "modeEMerchantMainInteract = null"]:
        if token not in fail_build:
            return fail("merchant build failure must disable and centrally clean up -> " + token)

    identity = extract_method(merchant, "private bool TryConfigureModeEMerchantShopIdentity(")
    for token in [
        "merchantField.GetValue(shop)",
        "accountField.GetValue(shop)",
        "shop.MerchantID",
        "shop.AccountAvaliable",
    ]:
        if token not in identity:
            return fail("merchant identity must be read back -> " + token)

    spawn = extract_method(merchant, "private async UniTaskVoid SpawnModeEMerchant(")
    for reason in [
        '"merchant preset unavailable"',
        '"merchant player unavailable"',
        '"merchant character creation failed"',
        '"merchant async continuation preflight failed"',
        '"merchant spawn failed"',
    ]:
        if reason not in spawn:
            return fail("merchant spawn failure must close shell capability -> " + reason)

    sell_all_contract = extract_method(
        support, "internal static bool VerifyModeEShellRuntimeContracts()")
    for token in [
        "playerInventoryDisplayField.FieldType == typeof(Duckov.UI.InventoryDisplay)",
        "characterInventoryDisplayField.FieldType == typeof(Duckov.UI.InventoryDisplay)",
        "sortButtonField.FieldType == typeof(Button)",
        "merchantNameTextField.FieldType == typeof(TextMeshProUGUI)",
        "refreshCountDownTextField.FieldType == typeof(TextMeshProUGUI)",
        "sellMethod.ReturnType == typeof(UniTask)",
        "sellParameters[0].ParameterType == typeof(Item)",
    ]:
        if token not in sell_all_contract:
            return fail("SellAll runtime preflight must validate -> " + token)

    print("ModeEShellHarmonyUiContractGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
