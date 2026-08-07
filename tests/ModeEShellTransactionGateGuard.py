"""Guard owner-aware Mode E shell transaction gate shared by Buy/Sell/SellAll."""

from pathlib import Path
import sys


SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")
MODE_E = Path("ModeE/ModeE.cs")


def fail(message: str) -> int:
    print("ModeEShellTransactionGateGuard: FAIL - " + message)
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
    mode_e = MODE_E.read_text(encoding="utf-8")
    combined = support + "\n" + mode_e

    required = [
        "private sealed class ModeEShellTransactionOwner",
        "internal int SessionToken;",
        "internal long MerchantGeneration;",
        "internal long TransactionID;",
        "internal bool OwnsBuying;",
        "internal bool OwnsSelling;",
        "internal bool IsSellAll;",
        "private bool TryAcquireModeEShellTransaction(",
        "private void ClearBusyAndReleaseModeEShellTransactionIfOwned(",
        "PublishModeEShellTransactionGateChanged(",
        "private ModeEShellTransactionOwner modeEShellTransactionOwner = null;",
        "internal bool TryBeginModeEShellSellAll(StockShop shop, out long transactionID)",
        "internal void EndModeEShellSellAll(StockShop shop, long transactionID)",
        "internal async UniTask WrapModeEShellSellAsync(StockShop shop, Item item)",
    ]
    for token in required:
        if token not in combined:
            return fail("missing transaction-gate invariant -> " + token)

    acquire = support[
        support.index("private bool TryAcquireModeEShellTransaction(") :
        support.index("private void SetModeEShellBusyField(")
    ]
    if "modeEShellTransactionOwner != null" not in acquire and "modeEShellTransactionOwner ==" not in acquire:
        return fail("acquire must refuse when gate already held")
    if "NextModeEShellCounter(ref modeEShellNextTransactionID)" not in acquire and "modeEShellNextTransactionID" not in acquire:
        return fail("acquire must allocate monotonic transaction ID")
    if "PublishModeEShellTransactionGateChanged" not in acquire:
        return fail("acquire must publish TransactionGateChanged")
    if "OwnsBuying = false" not in acquire or "OwnsSelling = false" not in acquire:
        return fail("acquire must not claim Busy fields before checking the captured shop")

    release = support[
        support.index("private void ClearBusyAndReleaseModeEShellTransactionIfOwned(") :
        support.index("private bool IsModeEShellTransactionContextValid(")
    ]
    if "IsModeEShellTransactionOwner" not in release:
        return fail("release must match full owner before clearing busy")
    if "PublishModeEShellTransactionGateChanged" not in release:
        return fail("owner-aware release must publish gate change")

    buy = extract_method(support, "internal async UniTask<bool> BuyModeEShellItemAsync(")
    buy_busy = buy.find("if (shop.Busy) return false;")
    buy_owns = buy.find("owner.OwnsBuying = true;")
    if min(buy_busy, buy_owns) < 0 or buy_busy > buy_owns:
        return fail("Buy may claim buying only after the pre-existing Busy check")

    sell = extract_method(support, "internal async UniTask WrapModeEShellSellAsync(")
    sell_busy = sell.find("if (shop.Busy")
    sell_owns = sell.find("owner.OwnsSelling = true;")
    if min(sell_busy, sell_owns) < 0 or sell_busy > sell_owns:
        return fail("Sell may claim selling only after the pre-existing Busy check")

    sell_all = support[
        support.index("internal bool TryBeginModeEShellSellAll") :
        support.index("internal void EndModeEShellSellAll")
    ]
    if "IsSellAll" not in sell_all and "true, false, true" not in sell_all and "isSellAll" not in sell_all.lower():
        # Accept either field assignment style.
        if "TryAcquireModeEShellTransaction" not in sell_all:
            return fail("SellAll must acquire shared owner-aware gate")
    sell_all_busy = sell_all.find("if (shop.Busy)")
    sell_all_owns = sell_all.find("owner.OwnsSelling = true;")
    if min(sell_all_busy, sell_all_owns) < 0 or sell_all_busy > sell_all_owns:
        return fail("SellAll may claim selling only after the pre-existing Busy check")

    context = extract_method(support, "private bool IsModeEShellTransactionContextValid(")
    if "IsCurrentModeEShellEntryReference(owner.Shop, entry)" not in context:
        return fail("async Buy must revalidate the captured Entry by reference")
    entry_reference = extract_method(support, "private static bool IsCurrentModeEShellEntryReference(")
    if "object.ReferenceEquals(shop.entries[i], expectedEntry)" not in entry_reference:
        return fail("current Entry validation must use reference identity")

    print("ModeEShellTransactionGateGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
