"""Guard: Wiki pages must stay bounded and consume every page slice exactly once."""

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

    required = (
        "txtLeft.text = currentParsedContent;",
        "txtLeft.overflowMode = TMPro.TextOverflowModes.Page;",
        "private readonly List<string> currentArticlePages",
        "BuildArticlePageSlices();",
        "boundaries[0] = 0;",
        "boundaries[pageCount] = currentParsedContent.Length;",
        "currentParsedContent.Substring(sliceStart, sliceEnd - sliceStart)",
        "RenderArticlePage(txtLeft, leftPageToDisplay);",
        "RenderArticlePage(txtRight, rightPageToDisplay);",
        "text.text = currentArticlePages[pageNumber - 1];",
        "text.overflowMode = TMPro.TextOverflowModes.Page;",
        "text.pageToDisplay = 1;",
        "NormalizeArticlePageSlices();",
        "TrySplitOverflowingPage",
        "renderer.ForceMeshUpdate(true, true);",
        "currentArticlePages.Insert(pageIndex + 1, remainder);",
    )
    for pattern in required:
        if pattern not in source:
            return fail("missing bounded contiguous-page invariant: " + pattern)

    forbidden = (
        "TextOverflowModes.Overflow",
        "txtRight.text = currentParsedContent;",
        "txtRight.pageToDisplay = rightPageToDisplay;",
    )
    for pattern in forbidden:
        if pattern in source[start:]:
            return fail("Wiki article can overflow or independently skip pages: " + pattern)

    print("WikiUIPageOverflowGuard: PASS - bounded renderers use contiguous cached page slices")
    return 0


if __name__ == "__main__":
    sys.exit(main())
