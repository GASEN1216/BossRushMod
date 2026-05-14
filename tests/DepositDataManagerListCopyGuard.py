"""Guard: DepositDataManager.GetAllItems must not expose the mutable cache list."""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/NPCs/Courier/DepositDataManager.cs")


def fail(message: str) -> int:
    print("DepositDataManagerListCopyGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[brace + 1:index]
    return ""


def main() -> int:
    if not SOURCE.exists():
        return fail("missing " + str(SOURCE))

    text = SOURCE.read_text(encoding="utf-8", errors="ignore")
    body = extract_method_body(text, "public static List<DepositedItemData> GetAllItems()")
    if not body:
        return fail("GetAllItems body not found")

    if re.search(r"return\s+cachedItems\s*;", body):
        return fail("GetAllItems returns the mutable cache list directly")

    if "return new List<DepositedItemData>(cachedItems);" not in body:
        return fail("GetAllItems must return a list copy preserving current order")

    print("DepositDataManagerListCopyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
