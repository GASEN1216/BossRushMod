from pathlib import Path
import re
import sys


UI_HELPER = Path("ZombieMode/ZombieModeUIHelper.cs")


def fail(message: str) -> int:
    print("ZombieModeUIHelperGraphicCompositionGuard: FAIL - " + message)
    return 1


def extract_method(text: str, name: str) -> str:
    match = re.search(r"internal\s+static\s+[A-Za-z0-9_<>,\[\]\s]+\s+" + re.escape(name) + r"\s*\([^)]*\)\s*\{", text)
    if match is None:
        return ""

    depth = 1
    index = match.end()
    while index < len(text) and depth > 0:
        ch = text[index]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
        index += 1
    return text[match.end():index - 1]


def main() -> int:
    text = UI_HELPER.read_text(encoding="utf-8")
    method = extract_method(text, "CreateHighlightBar")
    if not method:
        return fail("CreateHighlightBar missing")

    for required in [
        'GameObject textObject = CreateRect(name + "_Text"',
        "textRect.anchorMin = Vector2.zero;",
        "textRect.anchorMax = Vector2.one;",
        "textRect.offsetMin = Vector2.zero;",
        "textRect.offsetMax = Vector2.zero;",
        "return CreateTMPText(textObject, text, fontSize, alignment, textColor);",
    ]:
        if required not in method:
            return fail("CreateHighlightBar must create text on a child RectTransform: " + required)

    forbidden = "return CreateTMPText(bar, text, fontSize, alignment, textColor);"
    if forbidden in method:
        return fail("CreateHighlightBar must not put TextMeshProUGUI on the same object as Image")

    print("ZombieModeUIHelperGraphicCompositionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
