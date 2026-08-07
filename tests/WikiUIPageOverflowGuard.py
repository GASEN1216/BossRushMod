"""Guard: Wiki article text must stay inside both book page rectangles."""

from pathlib import Path
import sys


WIKI_UI = Path("Integration/WikiUIManager.cs")


def fail(message: str) -> int:
    print("WikiUIPageOverflowGuard: FAIL - " + message)
    return 1


def main() -> int:
    source = WIKI_UI.read_text(encoding="utf-8")
    start = source.find("private void SetupContentWithTMPPaging(string content)")
    end = source.find("private void SyncRightTextPropertiesFromLeft()", start)
    if start < 0 or end < 0:
        return fail("cannot locate Wiki article paging setup")

    setup = source[start:end]
    required = (
        "txtLeft.text = currentParsedContent;",
        "txtLeft.overflowMode = TMPro.TextOverflowModes.Page;",
        "txtRight.text = currentParsedContent;",
        "txtRight.overflowMode = TMPro.TextOverflowModes.Page;",
        "txtRight.pageToDisplay = 2;",
        "txtRight.pageToDisplay = rightPageToDisplay;",
    )
    for pattern in required:
        if pattern not in source:
            return fail("missing right-page boundary invariant: " + pattern)

    if "txtRight.overflowMode = TMPro.TextOverflowModes.Overflow;" in setup:
        return fail("right article page uses unbounded Overflow mode")

    forbidden = (
        "ExtractPageSourceText",
        "RepairRichTextTags",
        "CollectOpenTags",
    )
    for pattern in forbidden:
        if pattern in source:
            return fail("right article page still uses a second-pass text slice: " + pattern)

    print("WikiUIPageOverflowGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
