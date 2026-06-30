"""Guard: storage deposit UI refresh should reuse reflection metadata."""

from pathlib import Path
import sys


SERVICE = Path("Integration/NPCs/Courier/StorageDepositService.cs")
LIFECYCLE = Path("Integration/NPCs/Courier/StorageDepositLifecycle.cs")
TRANSACTIONS = Path("Integration/NPCs/Courier/StorageDepositTransactions.cs")
BULK = Path("Integration/NPCs/Courier/StorageDepositBulkActions.cs")


def fail(message: str) -> int:
    print("StorageDepositUiReflectionCacheGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    service = SERVICE.read_text(encoding="utf-8-sig")
    lifecycle = LIFECYCLE.read_text(encoding="utf-8-sig")
    transactions = TRANSACTIONS.read_text(encoding="utf-8-sig")
    bulk = BULK.read_text(encoding="utf-8-sig")

    service_required = [
        "private static MethodInfo setupAndShowMethod = null;",
        "private static FieldInfo entryTemplateField = null;",
        "private static FieldInfo stockShopItemDisplayField = null;",
        "private static FieldInfo stockShopItemPriceTextField = null;",
        "setupAndShowMethod = null;",
        "entryTemplateField = null;",
        "stockShopItemDisplayField = null;",
        "stockShopItemPriceTextField = null;",
    ]
    for snippet in service_required:
        if snippet not in service:
            return fail("missing cached reflection field/reset -> " + snippet)

    lifecycle_required = [
        'setupAndShowMethod = typeof(StockShopView).GetMethod("SetupAndShow"',
        'entryTemplateField = typeof(StockShopView).GetField("entryTemplate"',
        'stockShopItemDisplayField = typeof(StockShopItemEntry).GetField("itemDisplay"',
        'stockShopItemPriceTextField = typeof(StockShopItemEntry).GetField("priceText"',
    ]
    for snippet in lifecycle_required:
        if snippet not in lifecycle:
            return fail("InitializeReflection missing cache assignment -> " + snippet)

    for source_name, text, signatures in [
        ("StorageDepositTransactions", transactions, [
            "private static async UniTaskVoid RefreshShopUIAsync()",
            "private static void UpdateAllItemDisplays()",
            "private static void RefreshShopUIAndUpdateNewEntry(int newIndex)",
        ]),
        ("StorageDepositBulkActions", bulk, [
            "private static void HideAllCachedUIEntries()",
            "private static void HideInvalidUIEntries()",
        ]),
    ]:
        for signature in signatures:
            body = extract_method_body(text, signature)
            if body is None:
                return fail(f"missing method body: {source_name}.{signature}")

            if "typeof(StockShopView).GetMethod(\"SetupAndShow\"" in body:
                return fail(f"{source_name}.{signature} still looks up SetupAndShow in refresh path")

            if "typeof(StockShopView).GetField(\"entryTemplate\"" in body:
                return fail(f"{source_name}.{signature} still looks up entryTemplate in refresh path")

            if "typeof(StockShopItemEntry).GetField(\"itemDisplay\"" in body:
                return fail(f"{source_name}.{signature} still looks up itemDisplay in refresh path")

            if "typeof(StockShopItemEntry).GetField(\"priceText\"" in body:
                return fail(f"{source_name}.{signature} still looks up priceText in refresh path")

    print("StorageDepositUiReflectionCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
