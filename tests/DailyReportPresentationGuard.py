"""日报可读性：从生产色值复算对比度，核对版面表接线、语言刷新与通知去重。

2026-09-20 改版：面板从「可滚动的报纸长条」换成「卡片仪表盘」，底图与坐标表
由 tools/gen_daily_report_ui.py 一次产出，摆位代码在 DailyReportUI_Dashboard.cs。
原先断言滚动区接线的那几条随之换成「底图 + 版面表」的接线断言。

只证明 L1 结构和离线颜色算术；不代替 Unity 字体与版面的实机检查。
"""
from pathlib import Path
import json
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


def check_single_source_chrome(dashboard):
    """底图与运行时不得画同一处（2026-09-20 第三轮）。

    分工：底图只画不会变的装饰；会变色 / 会响应状态的（签到格、签到按钮、图例色块）
    一律归运行时。两边都画会叠出双描边，而且颜色有两个来源必然漂移
    （实测曾漂：C# CellEmpty 是 199,189,166，脚本里写的是 226,219,205）。

    判据不是「脚本里有没有这些字」，而是**照版面表去底图上取色**：
    签到格中心、按钮中心、图例色块中心必须仍是卡片底色，不能被烤上别的颜色。
    """
    generator = (ROOT / "tools/gen_daily_report_ui.py").read_text(encoding="utf-8")
    # 只看模块级**赋值**，注释里提到这些名字是解释历史，不算持有副本
    for name in ("CELL_EMPTY", "CELL_EDGE", "BUTTON", "BUTTON_EDGE"):
        duplicated = re.search(r"^" + name + r"\s*=", generator, re.M)
        assert not duplicated, "底图脚本不得再持有运行时颜色的第二份副本: " + name

    # 运行时必须真的画这三样（否则底图不画就等于谁都不画）
    for token in ("signInCells.Add(image);", "signInButton.targetGraphic = buttonImage;",
                  "image.color = colors[i];"):
        assert token in dashboard, "运行时缺少动态控件绘制: " + token

    layout_path = ROOT / "Assets/Data/DailyReportLayout.json"
    image_path = ROOT / "Assets/ui/DailyReport/daily_report_bg.png"
    if not layout_path.exists() or not image_path.exists():
        return  # 散图 local-only：fresh clone 上缺文件只跳过取色，不红
    try:
        from PIL import Image
    except ImportError:
        return  # 没装 Pillow 的环境只跑结构断言

    spec = json.loads(layout_path.read_text(encoding="utf-8"))
    image = Image.open(image_path).convert("RGBA")
    rects, grid, legend = spec["rects"], spec["grid"], spec["legend"]
    card = image.getpixel((int(rects["signin"][0] + rects["signin"][2] - 30),
                           int(rects["signin"][1] + 12)))
    samples = {
        "签到格": (int(grid["x"] + grid["cellWidth"] / 2), int(grid["y"] + grid["cellHeight"] / 2)),
        "签到按钮": (int(rects["button"][0] + rects["button"][2] / 2),
                     int(rects["button"][1] + rects["button"][3] / 2)),
        "图例色块": (int(rects["legend"][0] + legend["swatch"] / 2),
                     int(rects["legend"][1] + rects["legend"][3] / 2)),
    }
    for name, point in samples.items():
        assert image.getpixel(point) == card, (
            name + " 被烤进了底图（" + str(image.getpixel(point)) + " != 卡片底色 "
            + str(card) + "），运行时会在上面再画一层")


def check_scrollable_body(dashboard):
    """卡片内部滚动必须真的能滚（2026-09-20 第三轮）。

    上一轮给长正文加了 ScrollRect，但内容高度写死成 viewport 高度
    （sizeDelta = slice.height），Clamped 模式下 ScrollRect 认为内容刚好装得下，
    一格都滚不动——控件在、事件在，就是滚不到最后一行。
    内容高度必须由 TMP 的 preferredHeight 决定。
    """
    create = method(dashboard, "private TextMeshProUGUI CreateText(")
    assert "ScrollRect scroll = viewport.AddComponent<ScrollRect>();" in create, "长正文缺少卡片内滚动"
    assert "fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;" in create,         "滚动内容高度必须由 ContentSizeFitter 按实际文本撑开"
    assert re.search(r"text\.rectTransform\.sizeDelta\s*=\s*new Vector2\([^)]*slice\.height", create) is None,         "滚动内容高度不得写死成 viewport 高度，那样 ScrollRect 永远滚不动"
    assert "text.overflowMode = TextOverflowModes.Overflow;" in create, "滚动正文不得按 viewport 截断"
    anchors = create.index("text.rectTransform.anchorMax = new Vector2(1f, 1f);")
    reset = create.index("text.rectTransform.sizeDelta = Vector2.zero;")
    fit = create.index("fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;")
    assert anchors < reset < fit, "横向 stretch 后必须清旧固定宽度，再由 PreferredSize 撑开高度"
    assert "fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;" in create


def main():
    ui = source("Integration/DailyReport/DailyReportUI.cs")
    dashboard = source("Integration/DailyReport/DailyReportUI_Dashboard.cs")
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
    # 版面来自与底图同源的坐标表：不允许在 C# 里另写一份魔法坐标
    layout = method(dashboard, "private void BuildDashboard(")
    for token in ("DailyReportLayoutTable.PanelWidth", "DailyReportBackground.Load()",
                  "BuildHeader();", "BuildIncomeCard();", "BuildStatusCard();", "BuildSignInCard();"):
        assert token in layout, "日报版面缺接线: " + token
    assert "background.color = PaperBase;" in layout, "底图缺席必须退回纯纸色底（fail-open）"
    signin = method(dashboard, "private void BuildSignInCard(")
    for token in ("DailyReportLayoutTable.GetCell(i)", "signInButton.onClick.AddListener(OnSignInClicked)",
                  "BuildLegend();", "BuildCloseButton();"):
        assert token in signin, "签到卡缺接线: " + token
    # 所有摆位必须经版面表转换，禁止直接写 anchoredPosition 常量
    assert "DailyReportLayoutTable.ToAnchored(" in dashboard
    check_single_source_chrome(dashboard)
    check_scrollable_body(dashboard)
    refresh = method(ui, "public void Refresh()")
    assert "RefreshLabels();" in refresh
    labels = method(ui, "private void RefreshLabels()")
    for target in ("mastheadText", "subtitleText", "incomeTitleText", "statusTitleText",
                   "signInTitleText", "closeText"):
        assert f"SetText({target}, L10n.T(" in labels, target + " must refresh in both languages"
    tick = method(ui, "private void Update()")
    assert tick.index("if (!open) return;") < tick.index("DailyReportService.Data")
    assert "BossRushUI.IsGamePaused()" in tick and "nextRefreshTime = Time.unscaledTime + 1f;" in tick
    # 面板不再滚动：定高卡片 + 整体缩放适配窗口
    assert "FitPaper();" in refresh and "ReflowPaper" not in ui, "仪表盘版面不应再有滚动排版"
    for target in ("metaIssueText", "metaDeadlineText", "weatherText", "statsText",
                   "bountyText", "fortuneText", "editorText", "gossipText"):
        assert f"SetText({target}," in refresh, target + " 必须在刷新里被填"
    assert "displayedDeaths !=" in tick and "GetRemainingPlayMinutes()" in tick
    assert "TodayBountyStatus" in method(ui, "private static string BuildBountyBlock(")
    runtime = source("Integration/DailyReport/DailyReportRuntimeModule.cs")
    cleanup = method(runtime, "public override void OnDestroy()")
    assert "DailyReportView.CleanupRuntime()" in cleanup
    announce = method(runtime, "private void AnnounceNewIssue()")
    assert announce.index("_announcedDayIndex == data.DayIndex && _announcedSlot == slot") < announce.index("ShowBigBanner")
    assert announce.index("_announcedDayIndex = data.DayIndex;") < announce.index("ShowBigBanner")
    print("DailyReportPresentationGuard: PASS (colors, layout table, localization, notification deduplication)")


if __name__ == "__main__":
    main()
