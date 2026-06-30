"""Guard: storage deposit UI refresh should avoid valid-entry set allocations."""

from pathlib import Path
import sys


SERVICE = Path("Integration/NPCs/Courier/StorageDepositService.cs")
TRANSACTIONS = Path("Integration/NPCs/Courier/StorageDepositTransactions.cs")
BULK = Path("Integration/NPCs/Courier/StorageDepositBulkActions.cs")


def fail(message: str) -> int:
    print("StorageDepositValidEntryScratchGuard: FAIL - " + message)
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
    transactions = TRANSACTIONS.read_text(encoding="utf-8-sig")
    bulk = BULK.read_text(encoding="utf-8-sig")

    required_service_snippets = [
        "private static bool IsCurrentDepositEntry(StockShop.Entry stockEntry)",
        "return stockEntry != null && entryIndexMapping.ContainsKey(stockEntry);",
    ]
    for snippet in required_service_snippets:
        if snippet not in service and snippet not in transactions and snippet not in bulk:
            return fail("missing exact-entry helper snippet -> " + snippet)

    if "validEntryScratch" in service + transactions + bulk:
        return fail("valid-entry state must not use a shared mutable scratch set")

    for source_name, text, signature in [
        ("StorageDepositTransactions.UpdateAllItemDisplays", transactions, "private static void UpdateAllItemDisplays()"),
        ("StorageDepositBulkActions.HideInvalidUIEntries", bulk, "private static void HideInvalidUIEntries()"),
    ]:
        body = extract_method_body(text, signature)
        if body is None:
            return fail("missing method body: " + source_name)

        if "new HashSet<StockShop.Entry>()" in body:
            return fail(source_name + " still allocates a valid-entry HashSet")

        if "BuildValidEntryScratch()" in body:
            return fail(source_name + " still uses shared valid-entry scratch")

        if "IsCurrentDepositEntry(stockEntry)" not in body:
            return fail(source_name + " must use exact Entry membership helper")

        if "validEntries.Contains(stockEntry)" in body:
            return fail(source_name + " still uses a temporary valid-entry collection")

    print("StorageDepositValidEntryScratchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
