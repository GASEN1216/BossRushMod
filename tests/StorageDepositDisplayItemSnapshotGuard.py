"""Guard: storage deposit item-display refresh should copy deposit data once."""

from pathlib import Path
import sys


SOURCE = Path("Integration/NPCs/Courier/StorageDepositTransactions.cs")


def fail(message: str) -> int:
    print("StorageDepositDisplayItemSnapshotGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "private static void UpdateAllItemDisplays()")
    if body is None:
        return fail("missing UpdateAllItemDisplays body")

    if "List<DepositedItemData> depositedItemsSnapshot = null;" not in body:
        return fail("missing lazy deposit item snapshot")

    if "if (depositedItemsSnapshot == null)" not in body:
        return fail("snapshot should be populated lazily only when a price text is present")

    if "depositedItemsSnapshot = DepositDataManager.GetAllItems();" not in body:
        return fail("snapshot must be copied from DepositDataManager.GetAllItems")

    if "depositIndex < depositedItemsSnapshot.Count" not in body:
        return fail("price update must read from the shared snapshot")

    print("StorageDepositDisplayItemSnapshotGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
