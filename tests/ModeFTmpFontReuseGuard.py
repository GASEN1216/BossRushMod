"""Guard: Mode F bounty radar must reuse the shared TMP font helper."""

from pathlib import Path
import sys


MODEF = Path("ModeF/ModeFUI_BountyRadarAndHealthBars.cs")
ZOMBIE_UI = Path("ZombieMode/ZombieModeUIHelper.cs")
WISH_UI = Path("Integration/WishFountain/WishFountainUI.cs")


def fail(message: str) -> int:
    print("ModeFTmpFontReuseGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace_start = text.find("{", start)
    if brace_start < 0:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    modef = MODEF.read_text(encoding="utf-8")
    zombie_ui = ZOMBIE_UI.read_text(encoding="utf-8")
    wish_ui = WISH_UI.read_text(encoding="utf-8")

    helper = extract_method(zombie_ui, "internal static TMP_FontAsset GetGameFont()")
    if not helper:
        return fail("missing ZombieModeUIHelper.GetGameFont")
    for token in [
        "TMP_Settings.defaultFontAsset",
        "ObjectCache.GetFirstTmpFont()",
        "_cachedFont",
    ]:
        if token not in helper:
            return fail("shared helper missing token: " + token)

    modef_font = extract_method(modef, "private TMP_FontAsset GetModeFBountyRadarFont()")
    if not modef_font:
        return fail("missing Mode F bounty radar font helper")
    if "ZombieModeUIHelper.GetGameFont();" not in modef_font:
        return fail("Mode F font helper must call ZombieModeUIHelper.GetGameFont")
    for forbidden in [
        "TMP_Settings.defaultFontAsset",
        "ObjectCache.GetFirstTmpFont()",
        "FindObjectOfType<TMP_Text>",
    ]:
        if forbidden in modef_font:
            return fail("Mode F font helper duplicates shared lookup: " + forbidden)

    if "ZombieModeUIHelper.GetGameFont()" not in wish_ui:
        return fail("WishFountain UI must keep using the shared TMP font helper")

    print("ModeFTmpFontReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
