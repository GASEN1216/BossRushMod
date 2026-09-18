"""图鉴呈现的有界构建、语言刷新与输入归还；统计行为另由执行回归覆盖。"""
from pathlib import Path
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(name):
    return clean_source((ROOT / "Integration/Codex" / name).read_text(encoding="utf-8-sig"))


def body(source, signature):
    start = source.index("{", source.index(signature))
    depth = 1
    for end in range(start + 1, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return source[start + 1:end]
    raise AssertionError("unclosed method: " + signature)


def main():
    view = read("CodexView.cs")
    grid = read("CodexView_Grid.cs")
    catalog = read("CodexBossCatalog.cs")
    page_size = re.search(r"const int CardsPerPage\s*=\s*(\d+)\s*;", grid)
    assert page_size and 1 <= int(page_size[1]) <= 12, "single page must create at most 12 cards"
    populate = body(grid, "void PopulateGrid(")
    assert "Math.Min(_pageEntries.Count, (_pageIndex + 1) * CardsPerPage)" in populate
    loop = body(populate, "for (int i = _pageIndex * CardsPerPage; i < end; i++)")
    assert loop.count("CreateCardFor(") == populate.count("CreateCardFor(") == 1, "card creation must stay within page bounds"
    assert "entry.Kills > 0" in populate and "_onlyMissing" in populate, "missing filter must use saved unlocks"
    assert "CodexBossCatalog.GetEncounterHint(info)" in body(grid, "void ShowDetail("), "details must show actual encounter guidance"
    assert "ResolveCurrentDisplayName(Key, _fallbackName)" in catalog, "catalog names must resolve current language"
    update = body(view, "void Update(")
    assert "_isChinese != L10n.IsChinese" in update and "ReferenceEquals(_renderedData" in update
    assert not re.search(r"\b(for|foreach|while)\s*\(", update), "closed/stable UI cannot scan each frame"
    layout = body(view, "void EnsureGridLayout(")
    assert "DestroyImmediate(vertical)" in layout, "remove incompatible layout immediately before adding grid"
    assert layout.index("DestroyImmediate(vertical)") < layout.index("AddComponent<GridLayoutGroup>()"), "remove incompatible layout before adding grid"
    assert "Close();" in body(view, "void OnDestroy("), "destroying an open view must release input"
    assert "card.SetActive(false);" in body(grid, "void ClearCards("), "old cards cannot affect this-frame layout"
    assert "locked ? BossRushUIColors.Disabled : BossRushUIColors.Accent" not in grid, "locked labels need readable text tokens"
    print("CodexPresentationGuard: PASS (UI wiring only; Unity rendering requires in-game checks)")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, ValueError) as error:
        print("CodexPresentationGuard: FAIL - " + str(error))
        sys.exit(1)
