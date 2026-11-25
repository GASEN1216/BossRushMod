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


def check_ink_gazette_chrome(ui, dashboard):
    """古宣水墨邸报呈现契约：运行时使用水墨底板、金/朱毛笔底衬与印章，不再混合浅色烤图或现代细条。"""
    assert "BossRushUISkinPart.Hairline" not in dashboard, "古宣邸报不得残留 Hairline 现代细条"
    for token in ("BossRushInkSkin.ApplyInkPanel", "BossRushInkSkin.GetSurfaceSprite()",
                  "BossRushInkSkin.GetBrushSprite()", "BossRushInkWidgets.CreateSealBadge"):
        assert token in dashboard, "古宣邸报版面缺少水墨构件: " + token
    assert "BossRushInkSkin.StampSeal" in ui or "BossRushInkWidgets.CreateSealBadge" in ui, "签到成功缺少落印特效"

    # 运行时仍须唯一驱动全部动态格与图例
    for token in ("signInCells.Add(image);", "signInButton.targetGraphic = buttonImage;",
                  "image.color = colors[i];"):
        assert token in dashboard, "运行时缺少动态控件绘制: " + token


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


def check_bounty_and_close(ui, dashboard):
    """人工复核：悬赏全文直接显示；ESC 由官方 View 关闭，淡出曲线是插值进度。"""
    income = method(dashboard, "private void BuildIncomeCard(")
    assert re.search(r'headlineText\s*=\s*CreateIconText\("Tip",\s*DailyReportLayoutTable.Get\("incomeTip"\),'
                     # 字号走正文常量（A-16 字号只留三档常量，见 check_ui_consensus）；这里守的是「换行、不滚动」
                     r'\s*0f,\s*1f,\s*BodyFontSize,\s*PaperInk,\s*"tip",\s*true,\s*false\);', income), \
        "今日悬赏必须启用换行并关闭卡片内滚动"
    icon = method(dashboard, "private TextMeshProUGUI CreateIconText(")
    assert "TextAlignmentOptions.Left, color, wrap, scrollBody);" in icon, "滚动选项必须传入文字控件"
    create = method(dashboard, "private TextMeshProUGUI CreateText(")
    assert "if (wrap && scrollBody)" in create, "ScrollRect 创建必须受 scrollBody 门控"
    assert "text.enableWordWrapping = wrap;" in create, "不滚动的悬赏仍须保留换行"
    assert "BuildCloseButton(" not in dashboard and "closeText" not in ui, "不再占用底部空间放合上报纸按钮"
    assert "override void OnCancel(" not in ui, "官方 View.OnCancel 随后还会 TryQuit，不得再次手动 Close"
    configure = method(ui, "private static void ConfigureCanvasGroupFade(")
    for curve in ("showingCurve", "hidingCurve"):
        assert ('SetPrivateInstanceField(canvasFade, "' + curve
                + '", AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));') in configure, \
            curve + " 必须是 0 -> 1 的插值进度，否则关闭会反向闪亮"
    spec = json.loads((ROOT / "Assets/Data/DailyReportLayout.json").read_text(encoding="utf-8"))
    rects = spec["rects"]
    assert rects["incomeTip"][3] >= 116, "撤掉关闭按钮释放的高度必须用于展示悬赏全文"
    assert rects["incomeTip"][1] + rects["incomeTip"][3] < rects["incomeNote"][1], "悬赏与战绩不得重叠"
    assert rects["signin"][1] + rects["signin"][3] <= spec["panel"][3] - 22, "签到卡不得挤出纸面"


def check_layout_fallback_sync():
    """版面表的硬编码兜底必须与 Assets/Data/DailyReportLayout.json 同源（2026-09-22）。

    DailyReportLayoutTable 的注释早就写着「与版面表同源」并引用了一个并不存在的守卫；
    改版面只重跑生成器、忘了改兜底，JSON 坏掉时退回的就是另一套坐标，文字与底图错位。
    同时钉住图例行高：18 号中文一行约 26 px，加 TMP 上下 margin，低于 31 时整串标签会被清空
    （2026-09-22 实测图例只剩色块）。
    """
    table = source("Integration/DailyReport/DailyReportLayoutTable.cs")
    spec = json.loads((ROOT / "Assets/Data/DailyReportLayout.json").read_text(encoding="utf-8"))
    names = re.findall(r'"(\w+)"', table[table.index("FallbackNames ="):table.index("FallbackRects =")])
    rows = re.findall(r"\{\s*(-?\d+),\s*(-?\d+),\s*(\d+),\s*(\d+)\s*\}",
                      table[table.index("FallbackRects ="):table.index("IconNames =")])
    assert len(names) == len(rows) == len(spec["rects"]), "兜底矩形与版面表条目数不一致"
    for name, row in zip(names, rows):
        assert [int(v) for v in row] == spec["rects"][name], "兜底矩形与版面表不同源: " + name
    fallback = method(table, "private static void ApplyFallback(")
    legend = spec["legend"]
    for token in ("_legendItemWidth = %sf;" % legend["itemWidth"], "_legendCount = %s;" % legend.get("count", 4),
                  "_legendSwatch = %sf;" % legend["swatch"]):
        assert token in fallback, "图例兜底与版面表不同源: " + token
    # 2026-09-23：图标是运行时 Sprite，位置同样来自版面表；兜底的图标段与参数也必须同源
    icon_names = re.findall(r'"(\w+)"', table[table.index("IconNames ="):table.index("FallbackIconRects =")])
    icon_rows = re.findall(r"\{\s*(-?\d+),\s*(-?\d+),\s*(\d+),\s*(\d+)\s*\}",
                           table[table.index("FallbackIconRects ="):table.index("private static void ApplyFallback(")])
    assert len(icon_names) == len(icon_rows) == len(spec["icons"]), "兜底图标与版面表条目数不一致"
    for name, row in zip(icon_names, icon_rows):
        assert [int(v) for v in row] == spec["icons"][name], "兜底图标与版面表不同源: " + name
    icon = spec["icon"]
    for token in ("_pillTextIndent = %sf;" % icon["pillTextIndent"], "_iconTextGap = %sf;" % icon["textGap"]):
        assert token in fallback, "图标参数兜底与版面表不同源: " + token
    assert spec["rects"]["legend"][3] >= 31, "图例行高 %d 放不下一行 18 号中文" % spec["rects"]["legend"][3]
    assert legend.get("count", 4) * legend["itemWidth"] <= spec["rects"]["legend"][2], "图例项排出了图例行"


def check_ink_banner_contrast(colors):
    """水墨古宣邸报的分区毛笔底衬字色（PillInk on PillInkBrushBg）与已签字色（SignedTagInk on PaperCard）必须 ≥ 4.5:1。"""
    pill_fg = luminance(colors["PillInk"][:3])
    brush_bg = colors["PillInkBrushBg"]
    card = colors["PaperCard"]
    for scene in (0, 1):
        card_rgb = tuple(card[i] * card[3] + scene * (1.0 - card[3]) for i in range(3))
        blended_brush = tuple(brush_bg[i] * brush_bg[3] + card_rgb[i] * (1.0 - brush_bg[3]) for i in range(3))
        contrast = ratio(pill_fg, luminance(blended_brush))
        assert contrast >= 4.5, f"水墨毛笔标题对比度 {contrast:.2f} < 4.5"
    signed_fg = luminance(colors["SignedTagInk"][:3])
    for name in ("PaperBase", "PaperCard"):
        bg = colors[name]
        for scene in (0, 1):
            c = ratio(signed_fg, blend(scene, bg[:3], bg[3]))
            assert c >= 4.5, f"SignedTagInk on {name} contrast {c:.2f} < 4.5"


def check_runtime_icons(dashboard):
    """图标与吉祥物是运行时 Sprite（2026-09-23）：取不到就不画、不缩进；每一张都必须在发布清单里。

    「有代码」不等于「拿得到」：图标没登记进 production_icon_manifest，Unity 就不会把它打进包，
    正式构建里整排图标静默消失。
    """
    create = method(dashboard, "private TextMeshProUGUI CreateIconText(")
    assert "DailyReportBackground.LoadIcon(iconId)" in create, "带图标的正文必须按版面表摆运行时图标"
    assert "indent = icon.xMax + DailyReportLayoutTable.IconTextGap - box.x;" in create, \
        "文字缩进必须跟着实际摆出来的图标走（取不到图标就不缩进）"
    art = method(dashboard, "private Image CreateArt(")
    assert "if (sprite == null" in art and "return null;" in art, "图标取不到必须不画那一格，不退回汉字或灰方块"
    assert "DailyReportBackground.LoadArt(DailyReportBackground.MascotFile)" in dashboard, "报头吉祥物缺接线"
    spec = json.loads((ROOT / "Assets/Data/DailyReportLayout.json").read_text(encoding="utf-8"))
    manifest = json.loads((ROOT / "tools/production_icon_manifest.json").read_text(encoding="utf-8"))
    budgets = {row["path"]: row["maxSize"] for row in manifest["icons"]}
    wanted = ["Assets/ui/DailyReport/daily_report_bg.png", "Assets/ui/DailyReport/dr_mascot.png"] + [
        "Assets/ui/DailyReport/dr_icon_%s.png" % name for name in spec["icons"]]
    for path in wanted:
        assert path in budgets, "日报资源没登记进 production_icon_manifest: " + path
    for path in wanted[1:]:
        assert 128 <= budgets[path] <= 512, "图标贴图上限要在 128–512（AGENTS 4.16）: " + path
    assert budgets[wanted[0]] <= 1024, "底图是展示图，上限 1024（AGENTS 4.16）"


def check_ui_consensus(ui, dashboard):
    """2026-09-24 UI 共识对照审查 A-13…A-17。

    - 签过之后不留灰按钮：按钮收起，同一位置换成状态字；
    - 签到没受理的原因写进签到状态行（官方横幅可能压在报纸下面）；
    - 卡片内滚动要有（AutoHide 的）滚动条；
    - 字号四级：报头 / 标题 / 正文三个常量，数值块只用一个缩小百分比 + 大数字 200%；
    - 奖品品质写名字不写数字。
    """
    button = method(ui, "private void RefreshSignInButton(")
    assert "signInButton.gameObject.SetActive(!signed)" in button, "签过之后签到按钮必须收起，不留灰按钮（A-13）"
    assert "signedTagText.gameObject.SetActive(signed)" in button, "签过之后要在按钮位置显示已签状态字（A-13）"
    assert "今日已签\", \"SIGNED\"" not in ui, "又把「今日已签」写回了按钮文字（A-13）"
    assert "signInFailure" in button and "InkDangerHex" in button, "签到失败原因必须写进签到状态行（A-14）"
    click = method(ui, "private void OnSignInClicked()")
    blocked = click[click.index("case DailyReportSignInOutcome.PersistBlocked:"):]
    blocked = blocked[:blocked.index("break;")]
    assert "signInFailure =" in blocked and "ShowBanner" not in blocked, "存档写不进的失败要写在状态行，不只走横幅（A-14）"
    create = method(dashboard, "private TextMeshProUGUI CreateText(")
    assert "BossRushUI.ConfigureScrollRect(scroll);" in create, "卡片内滚动必须挂共享滚动条（A-15）"
    sizes = set(re.findall(r"private const float (\w+FontSize) = ", dashboard))
    assert sizes == {"MastheadFontSize", "HeadingFontSize", "BodyFontSize"}, "日报字号常量只留三档（A-16）: %s" % sorted(sizes)
    for match in re.finditer(r"(ZombieModeUIHelper\.)?(CreateText|CreateIconText)\(", dashboard):
        head = dashboard[max(0, match.start() - 20):match.start()]
        if "TextMeshProUGUI " in head:
            continue  # 方法定义本身
        depth, index, args, current = 1, match.end(), [], ""
        while depth:
            ch = dashboard[index]
            if ch in "([{":
                depth += 1
            elif ch in ")]}":
                depth -= 1
            if depth == 1 and ch == ",":
                args.append(current.strip())
                current = ""
            elif depth:
                current += ch
            index += 1
        args.append(current.strip())
        size = args[3] if match.group(1) else args[4]
        assert size.endswith("FontSize") or size == "fontSize", "日报文字又写回了字面量字号（A-16）: %s(%s…)" % (match.group(2), ", ".join(args[:5]))
    value = method(ui, "private static string BuildValueBlock(")
    assert set(re.findall(r"<size=(\d+)%>", value)) == {"82", "200"}, "数值块只用 82% 与 200% 两档（A-16）"
    assert "QualityName(" in button and "品质 \"" not in button, "签到奖品品质要写名字不写数字（A-17）"


def main():
    ui = source("Integration/DailyReport/DailyReportUI.cs")
    dashboard = source("Integration/DailyReport/DailyReportUI_Dashboard.cs")
    shared = source("Common/UI/BossRushUI.cs")
    ink_skin = source("Common/UI/BossRushInkSkin.cs")
    colors = {name: tuple(float(v or 1) for v in values)
              for name, *values in COLOR.findall(ui + shared + ink_skin)}
    for alias, target in re.findall(r"(\w+)\s*=\s*BossRushInkColors\.(\w+)\s*;", ui):
        if target in colors:
            colors[alias] = colors[target]
    threshold = float(re.search(r"LightBackgroundLuminance\s*=\s*([\d.]+)f", shared)[1])
    for background in ("CellEmpty", "CellSigned", "CellToday", "CellTodayBright", "CellMilestone", "CellMilestoneDone",
                       "ButtonIdle", "ButtonDisabled"):
        bg = colors[background]
        fg = colors["TextOnAccent" if luminance(bg) > threshold else "TextPrimary"]
        # 单元格和按钮均不透明；按钮另查共享三态的按下暗化。
        states = (1, .8) if background in ("CellMilestone", "ButtonIdle") else (1,)
        for scale in states:
            contrast = ratio(luminance(fg), luminance(tuple(c * scale for c in bg[:3])))
            assert contrast >= 4.5, f"{background} label contrast {contrast:.2f} < 4.5"
    for ink in ("PaperInk", "PaperInkSoft", "PaperInkDanger", "SignedTagInk"):
        for surface_name in ("PaperBase", "PaperCard"):
            surface = colors[surface_name]
            for scene in (0, 1):
                contrast = ratio(luminance(colors[ink]), blend(scene, surface[:3], surface[3]))
                assert contrast >= 4.5, f"{ink} on {surface_name} contrast {contrast:.2f} < 4.5"
    grid = method(ui, "private void RefreshSignInGrid(")
    assert "label.color = BossRushUI.GetButtonTextColor(cell.color);" in grid
    # 版面来自与底图同源的坐标表，并在运行时渲染古宣水墨邸报底板与分卡
    layout = method(dashboard, "private void BuildDashboard(")
    for token in ("DailyReportLayoutTable.PanelWidth", "BossRushInkSkin.ApplyInkPanel", "CreateInkCard(",
                  "BuildHeader();", "BuildIncomeCard();", "BuildStatusCard();", "BuildSignInCard();"):
        assert token in layout, "日报版面缺接线: " + token
    assert "background.color = PaperBase;" in layout, "底图主色必须取水墨深纸色 PaperBase"
    # 对比度按 PaperBase / PaperCard / ButtonIdle 复算，前提是它们真的被渲染：ApplyInkPanel 会把底色改成主题 Surface，
    # StyleInkButton(Primary) 会把按钮底换成 CinnabarFill。所以底色必须在皮肤之后设，ButtonIdle 必须就是 CinnabarFill。
    assert layout.index("BossRushInkSkin.ApplyInkPanel(background") < layout.index("background.color = PaperBase;"), (
        "PaperBase 必须在 ApplyInkPanel 之后设，否则被主题 Surface 覆盖、守卫算的是没渲染的颜色")
    ink_card = method(dashboard, "private void CreateInkCard(")
    assert ink_card.index("BossRushInkSkin.ApplyInkPanel(img") < ink_card.index("img.color = raised ? PaperCard : PaperBase;"), (
        "分区卡面色必须在 ApplyInkPanel 之后设")
    assert re.search(r"ButtonIdle\s*=\s*BossRushInkColors\.CinnabarFill\s*;", ui), (
        "签到按钮 ButtonIdle 必须是 StyleInkButton(Primary) 实际铺的 CinnabarFill")
    signin = method(dashboard, "private void BuildSignInCard(")
    for token in ("DailyReportLayoutTable.GetCell(i)", "signInButton.onClick.AddListener(OnSignInClicked)",
                  "BuildLegend();"):
        assert token in signin, "签到卡缺接线: " + token
    # 所有摆位必须经版面表转换，禁止直接写 anchoredPosition 常量
    assert "DailyReportLayoutTable.ToAnchored(" in dashboard
    check_ink_gazette_chrome(ui, dashboard)
    check_scrollable_body(dashboard)
    check_bounty_and_close(ui, dashboard)
    check_layout_fallback_sync()
    check_ink_banner_contrast(colors)
    check_runtime_icons(dashboard)
    refresh = method(ui, "public void Refresh()")
    assert "RefreshLabels();" in refresh
    labels = method(ui, "private void RefreshLabels()")
    for target in ("mastheadText", "subtitleText", "incomeTitleText", "statusTitleText",
                   "signInTitleText"):
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
    check_ui_consensus(ui, dashboard)
    print("DailyReportPresentationGuard: PASS (ink gazette colors, brush banners, layout table, runtime icons, localization, notification deduplication)")


if __name__ == "__main__":
    main()
