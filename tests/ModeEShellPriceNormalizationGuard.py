"""Guard Mode E shell price cache: full-stack Bullet normalize, decoupled samples, long ceil."""

from pathlib import Path
import sys


SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")
MODE_E = Path("ModeE/ModeE.cs")


def fail(message: str) -> int:
    print("ModeEShellPriceNormalizationGuard: FAIL - " + message)
    return 1


def main() -> int:
    support = SUPPORT.read_text(encoding="utf-8")
    mode_e = MODE_E.read_text(encoding="utf-8")
    combined = support + "\n" + mode_e

    required = [
        "private const long MODE_E_SHELL_CASH_UNIT = 10000L;",
        "internal void NormalizeModeEShellStackForShop(StockShop shop, Item item)",
        'string.Equals(shop.MerchantID, "ModeE_Bullet"',
        "item.StackCount = item.MaxStackCount;",
        "private bool TryCalculateModeEShellPrice(StockShop shop, Item sample, out int shellPrice)",
        "long quantified = (raw + MODE_E_SHELL_CASH_UNIT - 1L) / MODE_E_SHELL_CASH_UNIT;",
        "private bool TryWriteModeEShellPrice(",
        "internal bool TryGetModeEShellPrice(StockShop shop, int itemTypeID, out int price)",
        "private struct ModeEShellPriceKey",
        "internal long MerchantGeneration;",
        "internal void EnsureModeEShellPriceScheduled(StockShop shop, int itemTypeID)",
        "private async UniTask CacheSingleModeEShellPriceAsync(",
        "PublishModeEShellPriceChanged(shop, merchantGeneration, itemTypeID)",
        "DestroyModeEShellTemporarySample(",
    ]
    for token in required:
        if token not in combined:
            return fail("missing price invariant -> " + token)

    # Shell must not own or destroy official itemInstances dictionary.
    forbidden = [
        "itemInstances[",
        'GetField("itemInstances"',
        "itemInstances =",
        "CacheItemInstances()",
    ]
    # Allow comments mentioning official itemInstances, but not writes.
    for token in ["itemInstances[", 'GetField("itemInstances"', "itemInstances ="]:
        if token in support:
            return fail("shell must not write/own official itemInstances -> " + token)

    calc = support[
        support.index("private bool TryCalculateModeEShellPrice") :
        support.index("private bool TryWriteModeEShellPrice")
    ]
    if "NormalizeModeEShellStackForShop(shop, sample)" not in calc:
        return fail("price calc must normalize sample before ConvertPrice")
    if "ConvertPrice(sample" not in calc:
        return fail("price calc must use ConvertPrice cash baseline")
    if "catch" not in calc:
        return fail("price calc exceptions must fail-closed")

    write = support[
        support.index("private bool TryWriteModeEShellPrice") :
        support.index("internal bool TryGetModeEShellPrice")
    ]
    if "IsCurrentModeEShellMerchantScope(shop, merchantGeneration)" not in write:
        return fail("price write must match merchantGeneration")
    if "modeEShellPriceCache[key] = price;" not in write:
        return fail("price write must store (Shop, ItemTypeID, merchantGeneration)")

    print("ModeEShellPriceNormalizationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
