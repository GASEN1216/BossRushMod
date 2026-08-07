"""Guard: the in-game Wiki must reopen with content and keep the external link out of article routing."""

from pathlib import Path
import csv
import sys


ROOT = Path(__file__).resolve().parents[1]
WIKI_UI = ROOT / "Integration" / "WikiUIManager.cs"
CATALOG = ROOT / "WikiContent" / "catalog.tsv"
EXTERNAL_CATEGORY = "_wiki_link"


def fail(message: str) -> int:
    print("WikiUIRuntimeFlowGuard: FAIL - " + message)
    return 1


def main() -> int:
    source = WIKI_UI.read_text(encoding="utf-8")

    required = (
        "WikiContentManager.Instance.LoadCatalog();",
        "currentCategoryId = ResolveContentCategoryId(categories);",
        "categories[i].Id != ExternalWikiCategoryId",
        "toggle.SetIsOnWithoutNotify(false);",
        "Application.OpenURL(ExternalWikiUrl);",
    )
    for pattern in required:
        if pattern not in source:
            return fail("missing reopen/default-category invariant: " + pattern)

    with CATALOG.open("r", encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle, delimiter="\t"))

    seen = set()
    for row in rows:
        entry_id = row["entryId"].strip()
        category_id = row["categoryId"].strip()
        if entry_id in seen:
            return fail("duplicate catalog entry: " + entry_id)
        seen.add(entry_id)

        if category_id == EXTERNAL_CATEGORY:
            continue

        entry_category = entry_id.split("__", 1)[0]
        for language in ("zh", "en"):
            nested = ROOT / "WikiContent" / language / entry_category / (entry_id + ".md")
            flat = ROOT / "WikiContent" / language / (entry_id + ".md")
            if not nested.is_file() and not flat.is_file():
                return fail("missing article: " + language + "/" + entry_id)

    print("WikiUIRuntimeFlowGuard: PASS - reopen reload, external route, and bilingual catalog are valid")
    return 0


if __name__ == "__main__":
    sys.exit(main())
