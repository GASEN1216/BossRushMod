"""日报可读性：从生产色值复算对比度，核对滚动、语言刷新与失败提示的接线。

只证明 L1 结构和离线颜色算术；不代替 Unity 字体、滚动区域与版面的实机检查。
"""
from pathlib import Path
import re
from cs_source_util import clean_source
from SkyIslandUiContrastGuard import COLOR, luminance, blend, ratio

ROOT = Path(__file__).resolve().parents[1]


def source(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def method(text, signature):
    start = text.index(signature)
    begin = text.index("{", start)
    depth = 1
    end = begin + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[begin:end]


def main():
    ui = source("Integration/DailyReport/DailyReportUI.cs")
    shared = source("Common/UI/BossRushUI.cs")
    colors = {name: tuple(float(v or 1) for v in values)
              for name, *values in COLOR.findall(ui + shared)}
    threshold = float(re.search(r"LightBackgroundLuminance\s*=\s*([\d.]+)f", shared)[1])
    for background in ("CellEmpty", "CellSigned", "CellMilestone", "CellMilestoneDone", "PaperRaised"):
        bg = colors[background]
        fg = colors["TextOnAccent" if luminance(bg) > threshold else "TextPrimary"]
        # 单元格和按钮均不透明；按钮另查共享三态的按下暗化。
        states = (1, .8) if background in ("CellMilestone", "PaperRaised") else (1,)
        for scale in states:
            contrast = ratio(luminance(fg), luminance(tuple(c * scale for c in bg[:3])))
            assert contrast >= 4.5, f"{background} label contrast {contrast:.2f} < 4.5"
    for ink in ("PaperInk", "PaperInkSoft"):
        for scene in (0, 1):
            contrast = ratio(luminance(colors[ink]), blend(scene, colors["PaperBase"][:3], colors["PaperBase"][3]))
            assert contrast >= 4.5, f"{ink} on paper contrast {contrast:.2f} < 4.5"
    grid = method(ui, "private void RefreshSignInGrid(")
    assert "label.color = BossRushUI.GetButtonTextColor(cell.color);" in grid
    scroll = method(ui, "private void BuildPaperScroll(")
    for token in ("GameplayDataSettings.UIPrefabs.ScrollRect", "Instantiate(prefab, paperRect)",
                  "paperScroll.content = panelRect;", "BossRushUI.ConfigureScrollRect(paperScroll);"):
        assert token in scroll, "日报滚动区缺接线: " + token
    layout = method(ui, "private void BuildLayout(")
    assert "BuildPaperScroll(paperRect);" in layout and '"Close", paperRect,' in layout
    assert "PinSignInContentToTop();" in layout, "签到区必须在构建后固定上沿，避免长说明挤压按钮"
    refresh = method(ui, "public void Refresh()")
    assert "RefreshLabels();" in refresh
    labels = method(ui, "private void RefreshLabels()")
    for target in ("mastheadText", "statsTitleText", "sideTitleText", "closeText", "rulesText"):
        assert f"SetText({target}, L10n.T(" in labels, target + " must refresh in both languages"
    tick = method(ui, "private void Update()")
    assert tick.index("if (!open) return;") < tick.index("DailyReportService.Data")
    assert "BossRushUI.IsGamePaused()" in tick and "nextRefreshTime = Time.unscaledTime + 1f;" in tick
    assert "ReflowPaper();" in refresh and "FitPaper();" in refresh
    reflow = method(ui, "private void ReflowPaper()")
    assert "MeasureRowPart(row.Left, row.Minimum)" in reflow
    assert "Mathf.Max(height, MeasureRowPart(row.Right, row.Minimum))" in reflow
    assert "panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y + Margin);" in reflow
    assert "BossRushUI.MeasureTextHeight(text, rect.rect.width, minimum)" in method(ui, "private static float MeasureRowPart(")
    assert "displayedDeaths !=" in tick and "GetRemainingPlayMinutes()" in tick
    assert "TodayBountyStatus" in method(ui, "private static string BuildBountyBlock(")
    runtime = source("Integration/DailyReport/DailyReportRuntimeModule.cs")
    cleanup = method(runtime, "public override void OnDestroy()")
    assert "DailyReportView.CleanupRuntime()" in cleanup
    announce = method(runtime, "private void AnnounceNewIssue()")
    assert announce.index("_announcedDayIndex == data.DayIndex && _announcedSlot == slot") < announce.index("ShowBigBanner")
    assert announce.index("_announcedDayIndex = data.DayIndex;") < announce.index("ShowBigBanner")
    print("DailyReportPresentationGuard: PASS (colors, scroll, localization, notification deduplication)")


if __name__ == "__main__":
    main()
