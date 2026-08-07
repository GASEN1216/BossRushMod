"""Guard two-stage Mode E shell scope: owned shops vs active capability vs Mode F pass-through."""

from pathlib import Path
import sys


SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")
HARMONY = Path("ModeE/ModeEHarmonyPatch.cs")
MERCHANT = Path("ModeE/ModeEMerchant.cs")
MODE_E = Path("ModeE/ModeE.cs")


def fail(message: str) -> int:
    print("ModeEShellModeScopeGuard: FAIL - " + message)
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
    mode_e = MODE_E.read_text(encoding="utf-8")
    combined = support + "\n" + harmony + "\n" + merchant + "\n" + mode_e

    required = [
        "internal enum ModeEShellShopPatchDisposition",
        "PassOriginal",
        "Block",
        "HandleModeE",
        "internal ModeEShellShopPatchDisposition GetModeEShellShopPatchDisposition(StockShop shop)",
        "internal bool IsCurrentModeEShellCapability(StockShop shop)",
        "modeEOwnedShopTombstones",
        "modeEMerchantShops",
        "modeEShellEconomyAvailable",
        "modeFActive",
        "modeEActive",
        "VerifyModeEShellPatchInstallation()",
        "IsCurrentModeEShellUiBinding(shop, uiBindingID)",
    ]
    for token in required:
        if token not in combined:
            return fail("missing scope invariant -> " + token)

    disposition = extract_method(
        support,
        "internal ModeEShellShopPatchDisposition GetModeEShellShopPatchDisposition",
    )
    if not disposition:
        return fail("missing shop patch disposition method")
    if "PassOriginal" not in disposition:
        return fail("disposition must allow unowned shops to pass original")
    if "Block" not in disposition or "HandleModeE" not in disposition:
        return fail("disposition must distinguish Block vs HandleModeE")
    if "modeEOwnedShopTombstones" not in disposition and "modeEOwnedShopTombstones.Contains" not in support:
        return fail("owned tombstones must participate in disposition")
    if "object.ReferenceEquals(shop, null)" not in disposition:
        return fail("real CLR null must be separated from Unity destroyed-object null")
    if "shop == null" in disposition:
        return fail("Unity fake-null owned shops must Block instead of passing original cash logic")
    null_idx = disposition.index("object.ReferenceEquals(shop, null)")
    owned_idx = disposition.index("modeEOwnedShopTombstones.Contains(shop)")
    scope_idx = disposition.index("IsCurrentModeEShellMerchantScope")
    if not (null_idx < owned_idx < scope_idx):
        return fail("owned tombstone classification must precede active-scope validation")

    buy_prefix = harmony[
        harmony.index("public static class ModeEShellBuyPatch") :
        harmony.index("public static class ModeEShellSellPatch")
    ]
    if "GetModeEShellShopPatchDisposition" not in buy_prefix:
        return fail("Buy Prefix must consult disposition")
    if "PassOriginal" not in buy_prefix or "Block" not in buy_prefix:
        return fail("Buy Prefix must handle PassOriginal and Block")
    if "BuyModeEShellItemAsync" not in buy_prefix:
        return fail("Buy Prefix must route HandleModeE to dedicated async buy")

    if "MerchantID" in buy_prefix and "modeEMerchantShops.Contains" not in support:
        return fail("MerchantID alone must not be the authority for shell buy")

    print("ModeEShellModeScopeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
