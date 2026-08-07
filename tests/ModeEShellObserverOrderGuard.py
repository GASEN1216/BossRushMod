"""Guard committed Mode E shell observer order and stock write fail-closed semantics."""

from pathlib import Path
import sys


SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")


def fail(message: str) -> int:
    print("ModeEShellObserverOrderGuard: FAIL - " + message)
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
    buy = extract_method(support, "internal async UniTask<bool> BuyModeEShellItemAsync(")
    if not buy:
        return fail("missing BuyModeEShellItemAsync")

    order_tokens = [
        "TryWriteModeEShellCurrentStock(entry, nextStock)",
        "InvokeModeEShellStockChanged(entry)",
        "InvokeModeEShellAfterItemSold(shop)",
        "InvokeModeEShellItemPurchased(shop, deliveryItem)",
        "PushModeEShellPurchaseNotification(shop, capturedDisplayName)",
    ]
    last = -1
    for token in order_tokens:
        idx = buy.find(token)
        if idx < 0:
            return fail("missing observer step -> " + token)
        if idx < last:
            return fail("observer order violated around -> " + token)
        last = idx

    if "FailModeEShellEconomyDuringCommittedTransaction(\"currentStock write/read failed\")" not in buy:
        return fail("stock write failure must fail-close capability")
    if "string capturedDisplayName = deliveryItem.DisplayName;" not in buy:
        return fail("display name must be captured before delivery")

    for helper in [
        "private void InvokeModeEShellStockChanged(StockShop.Entry entry)",
        "private void InvokeModeEShellAfterItemSold(StockShop shop)",
        "private void InvokeModeEShellItemPurchased(StockShop shop, Item item)",
        "private void PushModeEShellPurchaseNotification(StockShop shop, string displayName)",
    ]:
        body = extract_method(support, helper)
        if not body:
            return fail("missing isolated observer helper -> " + helper)
        if "catch" not in body:
            return fail("observer helper must isolate exceptions -> " + helper)

    stock_write = extract_method(support, "private bool TryWriteModeEShellCurrentStock(")
    if "modeEShellCurrentStockField.SetValue" not in stock_write:
        return fail("stock write must use currentStock backing field")
    if "GetValue" not in stock_write:
        return fail("stock write must re-read to confirm")

    print("ModeEShellObserverOrderGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
