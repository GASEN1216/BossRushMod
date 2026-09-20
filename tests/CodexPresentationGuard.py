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
    # 2026-09-20：分页整条取消（owner 定），整册铺一页靠滚动看。
    # 构建量的上界从「每页 12 张」换成「目录上限 CodexTuning.MaxEntries」——
    # _pageEntries 由目录过滤而来，目录本身 fail-closed 在 MaxEntries 处停止新增。
    assert "CardsPerPage" not in grid and "_pageIndex" not in grid, "paging must be gone, not merely hidden"
    assert "_pageText" not in view and "NextPage" not in view, "footer page hint and nav buttons must be gone"
    populate = body(grid, "void PopulateGrid(")
    loop = body(populate, "for (int i = 0; i < _pageEntries.Count; i++)")
    assert loop.count("CreateCardFor(") == populate.count("CreateCardFor(") == 1, "cards must be built in exactly one bounded loop"
    assert "entry.Kills > 0" in populate and "_onlyMissing" in populate, "missing filter must use saved unlocks"
    assert "MaxEntries" in read("CodexTuning.cs"), "catalog growth must stay bounded by MaxEntries"
    detail = body(grid, "void ShowDetail(")
    assert "GetEncounterHint" not in detail, "owner removed the encounter guidance block from the detail panel"
    # 初见场景必须走本地化解析，绝不能把裸场景 id 摆给玩家
    assert "FormatFirstScene(entry)" in detail, "detail must show the localized first-seen scene"
    assert "CodexSceneNames.Resolve" in grid, "first-seen scene must resolve through the official scene table"
    # 滚轮灵敏度必须来自具名常量，不写魔法数字（owner 实测旧值一滚就翻过大半屏）
    assert "scrollSensitivity = ScrollSensitivity;" in view, "scroll speed must come from a named constant"
    sensitivity = re.search(r"const float ScrollSensitivity\s*=\s*([0-9.]+)f\s*;", view)
    assert sensitivity and float(sensitivity[1]) <= 12.0, "scroll speed must stay slower than the old 28"
    # 分类必须以官方 Boss 名单为判据，不再把整张过滤池一律标成官方 Boss
    category = body(grid, "string FormatCategory(")
    assert "CodexOfficialBossRegistry.IsOfficialBoss" in category, "official boss category must come from the registry"
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
