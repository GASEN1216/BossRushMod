# -*- coding: utf-8 -*-
"""天空岛 UI 的对比度复算（WCAG 2.x）：每一处前景 / 背景都**从生产常量**、按游戏的**线性色彩空间**重算一遍。

## 为什么要有这份

2026-09-13 盘点时实算出 12 条不达标（区域大标题的压暗底把厚度给错了行、F3 按钮标签写死白字），而且没有任何守卫会拦。
这份守卫把 HUD 大标题、字幕、右上卡片、剧情面板（正文 / 选项行 / 焦点 / 主视觉标题 / ESC 键帽）、F3 按钮与各处描边
按生产常量逐条复算。

## 合成模型（2026-09-14 重写，UI 优化对照审核 F-01 / F-02 / F-04）

- **线性光混合**。鸭科夫的项目色彩空间是 Linear（ProjectSettings 的 m_ActiveColorSpace=1），uGUI 的半透明在线性光里混合：
  屏幕上的相对亮度 `Y = Y(前景)·α + Y(背景)·(1-α)`。旧版在 sRGB 数值上做 alpha 混合，又把本来就是相对亮度的场景统计值
  当成 sRGB 灰度代入——两处一起让深色半透明压暗显得强得多：大标题眉题守卫算 5.95:1，线性复算只有 2.13:1。
  `MODEL_PINS` 用审核报告复算表里的旧常量把模型钉住：谁把合成退回 sRGB，这里当场红。
- **描边按真实覆盖率**。描边环是距离场画的，直边上最亮的那个纹素才有 `outer·inner` 的覆盖率；
  这里照抄生产 `BuildStrokeSprite` 的算式求它，再乘 `Stroke` 的 alpha。旧厚度 1.25 最亮只有 0.75（F-02）。
- **字幕按行首行尾算**。字幕压暗底按实测文字宽反算，行首行尾正好落在平台边上；旧版只算正中心（F-04）。

## 它证明什么、不证明什么

证明的是**算术**：给定设计 token、压暗底曲线与文字位置，合成出来的对比度是多少。
不证明 Unity 的实际渲染、字体抗锯齿与玩家的主观观感——那些只能实机截图取色，见人工验证清单。

场景亮度是代理值：`Assets/ui/SkyIsland/skyisland_scene_*.png` 12 张场景横幅的相对亮度统计（p90 平均 0.679、p99 平均 0.839）；
面板整屏底图取 13 张 `skyisland_bg_*.png` 里最亮那张的 p99（0.747）；主视觉插图是没模糊过的原图，最坏按纯白算。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# 2026-09-23：SkyIslandHud / SkyIslandStoryPresentation 超 1200 行，按 AGENTS §4.15 原样拆出同一 partial 的新文件。
# 读主文件时把拆出去的那一半接在后面，断言照旧针对整个类。
SPLIT_PARTS = {
    "DebugAndTools/SkyIsland/SkyIslandHud.cs": "DebugAndTools/SkyIsland/SkyIslandHud_Layout.cs",
    "DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs": "DebugAndTools/SkyIsland/SkyIslandStoryPresentation_Parts.cs",
}


def read_with_parts(root, rel):
    text = (root / rel).read_text(encoding="utf-8-sig")
    part = SPLIT_PARTS.get(str(rel).replace("\\", "/"))
    if part and (root / part).is_file():
        text += "\n" + (root / part).read_text(encoding="utf-8-sig")
    return text

sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source

UI = "Common/UI/BossRushUI.cs"
ART = "DebugAndTools/SkyIsland/SkyIslandUiArt.cs"
HUD = "DebugAndTools/SkyIsland/SkyIslandHud.cs"
CONTROLS = "DebugAndTools/SkyIsland/SkyIslandControls.cs"
PANEL = "DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs"
PATHS = [UI, ART, HUD, CONTROLS, PANEL]

SCENE_P90 = 0.679
SCENE_P99 = 0.839
DARK_TERRAIN = 0.20
PANEL_BG_P99 = 0.747
HERO_ART_WORST = 1.0
SCENES = (("场景 p90", SCENE_P90), ("场景 p99", SCENE_P99), ("暗地形 0.20", DARK_TERRAIN))

# 审核报告（docs/代码审查/2026-09-14-UI优化对照审核.md 第四节 F-01）复算表里「线性」一列：
# 旧常量下大标题眉题（TextSecondary，峰值 0.72，压暗色 (0.02,0.03,0.04)）p90 = 2.13，普通字幕（TextPrimary）p90 = 3.95。
# 旧版 sRGB 合成算出来是 5.95 / 11.02。
MODEL_PINS = (
    ("旧大标题眉题 p90", (0.67, 0.72, 0.75), (0.02, 0.03, 0.04), 0.72, SCENE_P90, 2.13),
    ("旧普通字幕 p90", (0.94, 0.96, 0.97), (0.02, 0.03, 0.04), 0.72, SCENE_P90, 3.95),
)

COLOR = re.compile(
    r"Color (\w+) = new Color\(\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f"
    r"(?:\s*,\s*([\d.]+)f)?\s*\)")


def lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def luminance(rgb):
    return 0.2126 * lin(rgb[0]) + 0.7152 * lin(rgb[1]) + 0.0722 * lin(rgb[2])


def blend(bg_y, fg_rgb, alpha):
    """线性光里的 alpha 混合：前景 fg_rgb（sRGB 分量）以 alpha 盖在相对亮度为 bg_y 的背景上，返回合成后的相对亮度。"""
    return bg_y * (1.0 - alpha) + luminance(fg_rgb) * alpha


def ratio(a, b):
    """两个相对亮度之间的 WCAG 对比度。"""
    if a < b:
        a, b = b, a
    return (a + 0.05) / (b + 0.05)


def lerp_white(rgb, k):
    return tuple(rgb[i] + (1.0 - rgb[i]) * k for i in range(3))


def plateau(t, edge):
    """两端 smoothstep 淡出 + 中间平台，与 SkyIslandUiArt.Plateau 同一条曲线。"""
    if edge <= 0:
        return 1.0
    t = min(1.0, max(0.0, t))
    head = min(1.0, max(0.0, t / edge))
    tail = min(1.0, max(0.0, (1.0 - t) / edge))
    return head * head * (3 - 2 * head) * tail * tail * (3 - 2 * tail)


def stroke_coverage(thickness):
    """描边环直边上最亮那个纹素的覆盖率。算式照抄 BossRushUI.BuildStrokeSprite：depth 是纹素到边界的整数深度。"""
    best = 0.0
    for depth in range(0, 16):
        outer = min(1.0, max(0.0, depth + 0.5))
        inner = min(1.0, max(0.0, thickness - depth + 0.5))
        best = max(best, outer * inner)
    return best


def hover_color(rgb, lift, darken, cap, guarded):
    """BossRushUI.GetHoverColor 的复算：向白提亮 lift；白字底（Y ≤ cap）提亮会越过 cap 时改成压暗 darken。"""
    hover = lerp_white(rgb, lift)
    if guarded and luminance(rgb) <= cap and luminance(hover) > cap:
        hover = tuple(c * (1.0 - darken) for c in rgb)
    return hover


def const(src, name, where):
    m = re.search(r"\b" + name + r"\s*=\s*(-?[\d.]+)f\s*;", src)
    if not m:
        raise AssertionError("读不到 " + where + " 的常量 " + name)
    return float(m.group(1))


def body_of(source, signature):
    """切出以 signature 开头的方法体；找不到返回空串（调用方的 require 会报出来）。"""
    start = source.find(signature)
    if start < 0:
        return ""
    brace = source.find("{", start)
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return ""


def required(px):
    """WCAG：大字（≥24px 常规字重）3:1，其余 4.5:1。本库画布 1920×1080，1 单位 ≈ 1080p 的 1px。"""
    return 3.0 if px >= 24 else 4.5


def check(raw_sources):
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    # 常量与结构一律读剥掉注释的源码：注释里记着旧值（「旧值 0.72」一类），按原文读会读到注释。
    ui, art, hud, controls, panel = (clean_source(raw_sources[p]) for p in PATHS)

    # ---------- 0. 合成模型钉 ----------
    for label, fg, scrim_rgb, alpha, scene, expected in MODEL_PINS:
        value = ratio(luminance(fg), blend(scene, scrim_rgb, alpha))
        require(abs(value - expected) < 0.03,
                "合成模型钉失效：%s 应为 %.2f:1，算出 %.2f:1——合成是不是退回了 sRGB 数值混合？" % (label, expected, value))

    tokens = {}
    for m in COLOR.finditer(ui):
        rgb = tuple(float(v) for v in m.group(2, 3, 4))
        alpha = float(m.group(5)) if m.group(5) else 1.0
        tokens[m.group(1)] = rgb + (alpha,)
    for name in ("Backdrop", "Surface", "SurfaceRaised", "Stroke", "TextPrimary", "TextSecondary", "Accent",
                 "Success", "Warning", "Danger", "WarningText", "SuccessText", "TextOnAccent", "Divider"):
        require(name in tokens, UI + " 缺少设计 token " + name)
    if errors:
        return errors

    def rgb(name):
        return tokens[name][:3]

    # ---------- 描边：厚度 → 覆盖率 → 有效不透明度 ----------
    build_stroke = body_of(ui, "private static Sprite BuildStrokeSprite(int radius)")
    for token in ("float depth = r - distance;", "float outer = Mathf.Clamp01(depth + 0.5f);",
                  "float inner = Mathf.Clamp01(StrokeThickness - depth + 0.5f);"):
        require(token in build_stroke, UI + " 的 BuildStrokeSprite 距离场算式变了（缺 " + token + "）：守卫复算的覆盖率对不上生产")
    coverage = stroke_coverage(const(ui, "StrokeThickness", UI))
    stroke_alpha = tokens["Stroke"][3] * coverage
    accent_ring_alpha = tokens["Accent"][3] * coverage

    # ---------- 1. 区域大标题：三行文字各坐在多厚的压暗底上 ----------
    add_scrim = body_of(hud, "private static RectTransform AddScrim(")
    require("Color color = BossRushUIColors.Backdrop;" in add_scrim,
            HUD + " 的压暗底不再取 Backdrop 的颜色：守卫按 Backdrop 复算")
    require("peak * 0.8f" not in add_scrim,
            HUD + " 的压暗底贴图兜底又把峰值打了折：纯色兜底也要罩得住 4.5:1 的小字")
    scrim_rgb = rgb("Backdrop")
    peak = const(art, "ScrimPeak", ART)
    vedge = const(art, "ScrimEdge", ART)
    hedge = const(art, "ScrimHorizontalEdge", ART)
    scrim_h = const(hud, "BannerScrimHeight", HUD)
    for row in ("BannerOverline", "BannerTitle", "BannerHint"):
        m = re.search(r"BossRushUIColors\.(\w+),\s*" + row + r"Y,\s*" + row + r"Height\)", hud)
        require(m is not None and m.group(1) in tokens, HUD + " 找不到 " + row + " 这一行的字色")
        if m is None or m.group(1) not in tokens:
            continue
        font = {"BannerOverline": "AreaOverlineFont", "BannerTitle": "AreaTitleFont", "BannerHint": "BannerHintFont"}[row]
        y, height, px = const(hud, row + "Y", HUD), const(hud, row + "Height", HUD), const(hud, font, HUD)
        fg = luminance(rgb(m.group(1)))
        for scene_name, scene in SCENES[:2]:
            # 文字框的上下两端都要算：只算中心的话，一行高 60 的大标题可以有半边滑出平台。
            worst = min(ratio(fg, blend(scene, scrim_rgb, plateau((edge_y + scrim_h / 2.0) / scrim_h, vedge) * peak))
                        for edge_y in (y - height / 2.0, y, y + height / 2.0))
            require(worst >= required(px),
                    "区域大标题的「%s」（%s）在%s下只有 %.2f:1（需要 %.1f:1）：压暗底没有罩住这一行"
                    % (row, m.group(1), scene_name, worst, required(px)))
    # 横向：压暗底按文字实测宽度反算，行首行尾落在平台里（带分号的才是调用点）。
    require("FitBannerScrim();" in hud,
            HUD + " 没有按文字实测宽度反算大标题压暗底：英文落地提示整句约 700px，行尾会滑出平台")
    require("1f - 2f * SkyIslandUiArt.ScrimHorizontalEdge" in hud,
            HUD + " 大标题压暗底宽度的反算没有用 ScrimHorizontalEdge，平台宽度会和实际曲线对不上")
    require(0.0 < hedge < 0.5, ART + " 压暗底横向淡出比例 %.2f 不合法（≥0.5 时平台宽度为 0）" % hedge)

    # ---------- 2. 底部字幕：行首行尾与上下沿 ----------
    caption_peak = const(art, "CaptionScrimPeak", ART)
    caption_hedge = const(art, "CaptionScrimHorizontalEdge", ART)
    caption_font = const(hud, "CaptionFont", HUD)
    start_caption = body_of(hud, "private void StartCaption(string text, bool warning)")
    require("captionText.color = warning ? BossRushUIColors.WarningText : BossRushUIColors.TextPrimary;" in start_caption,
            HUD + " 的字幕字色不再是「警示 WarningText / 普通 TextPrimary」：守卫复算的对象对不上生产")
    require("FitCaptionScrim(height);" in start_caption,
            HUD + " 的字幕压暗底不再按实测文字宽反算：宽度写死时一行 800px 的字幕行尾压暗只剩 0.40（审核 F-04）")
    fit_caption = body_of(hud, "private void FitCaptionScrim(float textHeight)")
    require("1f - 2f * SkyIslandUiArt.CaptionScrimHorizontalEdge" in fit_caption
            and "Mathf.Min(CaptionWidth, MeasuredWidth(captionText))" in fit_caption,
            HUD + " 的 FitCaptionScrim 没有按「文字宽 / 平台占比」反算：行首行尾会滑进横向淡出区")
    require("Mathf.Max(textHeight + CaptionScrimPadding, textHeight / plateau)" in body_of(hud, "private static float ScrimHeightFor("),
            HUD + " 的字幕压暗底高度不再按平台占比反算：两行字幕的上下沿会滑出平台")
    require(0.0 < caption_hedge < 0.5, ART + " 字幕压暗底横向淡出比例 %.2f 不合法" % caption_hedge)
    pad = const(hud, "CaptionScrimPadding", HUD)
    for text_h in (caption_font * 1.6, const(hud, "CaptionMaxHeight", HUD)):
        shade_h = max(text_h + pad, text_h / max(0.05, 1.0 - 2.0 * vedge))
        v_edge = 0.5 - (text_h / 2.0) / shade_h
        # 行首行尾正好落在横向平台边上（FitCaptionScrim 的反算），横向因子取平台边上的值。
        alpha = plateau(v_edge, vedge) * plateau(caption_hedge, caption_hedge) * caption_peak
        for token, label in (("TextPrimary", "普通字幕"), ("WarningText", "警示字幕")):
            for scene_name, scene in SCENES[:2]:
                value = ratio(luminance(rgb(token)), blend(scene, scrim_rgb, alpha))
                require(value >= required(caption_font),
                        "%s（字高 %.0f）的行首行尾在%s下只有 %.2f:1（需要 %.1f:1）。警示字幕尤其不能省——"
                        "噬风的预警窗口只有 1.4 秒" % (label, text_h, scene_name, value, required(caption_font)))

    # ---------- 3. 右上常驻卡片：卡底直接压在场景上 ----------
    require(re.search(r"cardSurface\.a\s*=\s*CardSurfaceAlpha;", hud) is not None,
            HUD + " 的卡片底不再按 CardSurfaceAlpha 取不透明度：守卫复算的对象对不上生产")
    card_alpha = const(hud, "CardSurfaceAlpha", HUD)
    card_texts = re.findall(r'Text\("(\w+)", card\.transform, (\w+), BossRushUIColors\.(\w+)', hud)
    require(len(card_texts) >= 4, HUD + " 找不到卡片上的几行文字（Region / Objective / Chips / Extraction / Status）")
    require("BossRushUI.ApplyPanelStroke(background, 10, BossRushUISkinPart.Card, BossRushUIColors.Stroke);" in hud,
            HUD + " 右上卡片没有描边：卡底 Surface 在暗地形上对背景只有 1.50:1，没有边就没有轮廓")
    for scene_name, scene in SCENES:
        card_bg = blend(scene, rgb("Surface"), card_alpha)
        for name, font_const, token in card_texts:
            if token not in tokens:
                errors.append(HUD + " 卡片文字 " + name + " 用了未知 token " + token)
                continue
            px = const(hud, font_const, HUD)
            value = ratio(luminance(rgb(token)), card_bg)
            require(value >= required(px),
                    "右上卡片的 %s（%s %.0fpx）在%s下只有 %.2f:1（需要 %.1f:1）：CardSurfaceAlpha=%.2f 压不住"
                    % (name, token, px, scene_name, value, required(px), card_alpha))
        value = ratio(blend(card_bg, rgb("Stroke"), stroke_alpha), card_bg)
        require(value >= 3.0, "右上卡片外框在%s下只有 %.2f:1（WCAG 1.4.11 非文本 3:1）" % (scene_name, value))
        # 模态面板：Backdrop 垫底、面板底 Surface。
        modal_bg = blend(blend(scene, rgb("Backdrop"), tokens["Backdrop"][3]), rgb("Surface"), tokens["Surface"][3])
        value = ratio(blend(modal_bg, rgb("Stroke"), stroke_alpha), modal_bg)
        require(value >= 3.0, "面板外框在%s下只有 %.2f:1（WCAG 1.4.11 非文本 3:1）" % (scene_name, value))

    # ---------- 4. 剧情面板：整屏底图之上的正文、选项行与焦点 ----------
    tint = const(panel, "BackgroundTintAlpha", PANEL)
    row_alpha = const(panel, "ChoiceRowAlpha", PANEL)
    band_alpha = const(panel, "HeroTitleBandAlpha", PANEL)
    body_font = const(panel, "BodyFont", PANEL)
    choice_font = const(panel, "ChoiceFont", PANEL)
    title_font_min = const(panel, "TitleFontMin", PANEL)
    lift = const(panel, "ChoiceFocusLift", PANEL)
    focus_alpha = const(panel, "ChoiceFocusAlpha", PANEL)
    threshold = const(ui, "LightBackgroundLuminance", UI)

    body_token = re.search(r"MakeText\(canvas\.transform, text, BodyFont,\s*BossRushUIColors\.(\w+)", panel)
    require(body_token is not None and body_token.group(1) in tokens, PANEL + " 找不到正文的字色")
    body_rgb = rgb(body_token.group(1)) if body_token and body_token.group(1) in tokens else rgb("TextSecondary")
    require("BossRushUI.GetButtonTextColor(BossRushUIColors.SurfaceRaised)" in panel,
            PANEL + " 的选项标签不再按行底 SurfaceRaised 挑字色：守卫复算的对象对不上生产")
    label_rgb = rgb("TextOnAccent") if luminance(rgb("SurfaceRaised")) > threshold else rgb("TextPrimary")

    worst = {}

    def track(key, value, at):
        if key not in worst or value < worst[key][0]:
            worst[key] = (value, at)

    # 底图亮度从 0 扫到最亮那张的 p99：边与焦点对比最差在亮底（底图透出来把行底抬亮），标签最差也在亮底。
    for step in range(0, 76):
        bg_l = min(PANEL_BG_P99, step / 100.0)
        base = blend(bg_l, rgb("Surface"), tint)
        row = blend(base, rgb("SurfaceRaised"), row_alpha)
        focus_fill = blend(base, lerp_white(rgb("SurfaceRaised"), lift), focus_alpha)
        track("正文", ratio(luminance(body_rgb), base), bg_l)
        track("选项标签", ratio(luminance(label_rgb), row), bg_l)
        track("选项行边", ratio(blend(row, rgb("Stroke"), stroke_alpha), row), bg_l)
        track("焦点环", ratio(blend(focus_fill, rgb("Accent"), accent_ring_alpha), focus_fill), bg_l)
        track("焦点行标签", ratio(luminance(label_rgb), focus_fill), bg_l)
    for key, need, why in (("正文", 4.5, "BackgroundTintAlpha=%.2f 压不住最亮那张底图（正文按 4.5:1 要求，不按大字放宽）" % tint),
                           ("选项标签", required(choice_font), "行底 ChoiceRowAlpha=%.2f 太透" % row_alpha),
                           ("选项行边", 3.0, "那圈边是「这一行是可点控件」的唯一证据，ChoiceRowAlpha=%.2f 透过头了" % row_alpha),
                           ("焦点环", 3.0, "焦点指示物（Accent 行边）对焦点行底要过 WCAG 1.4.11 的 3:1"),
                           ("焦点行标签", required(choice_font), "ChoiceFocusLift=%.2f 抬过头了" % lift)):
        value, at = worst[key]
        require(value >= need, "剧情面板%s最差只有 %.2f:1（底图亮度 %.2f，需要 %.1f:1）：%s" % (key, value, at, need, why))

    # 主视觉标题压在实底带上，而带子底下是没模糊过的插图：最坏按纯白算。
    band_bg = blend(HERO_ART_WORST, rgb("Surface"), band_alpha)
    value = ratio(luminance(rgb("TextPrimary")), band_bg)
    require(value >= required(title_font_min),
            "主视觉标题压在实底带上只有 %.2f:1（需要 %.1f:1）：HeroTitleBandAlpha=%.2f 连白底都压不住"
            % (value, required(title_font_min), band_alpha))
    require('MakeRect(hero, "TitleBand"' in panel,
            PANEL + " 主视觉没有标题实底带：单靠渐变的话标题最上面那行等于直接压在插图上")

    # ESC 键帽压在没模糊过的插图右上角（最亮处接近纯白）：常态与悬停两种底色上，13px 的字都要 4.5:1。
    hero = body_of(panel, "private static void BuildHero(")
    esc = re.search(r'KeyCap\(hero, "ESC",.*?BossRushUIColors\.(\w+)\);', hero, re.S)
    require(esc is not None and esc.group(1) in tokens, PANEL + " 找不到 ESC 键帽的字色")
    # 2026-09-23 审美审查 UE-15：键帽改走共享按钮入口 ApplyButtonColors（常态 Surface、悬停 GetHoverColor(Surface)），
    # 拿到官方 UI 音效、按下回弹与投影斜面；三态的算式与原先手写的 ColorBlock 相同。
    require(re.search(r"ZombieModeUIHelper\.ApplyButtonColors\(closeButton,\s*BossRushUIColors\.Surface,\s*"
                      r"BossRushUI\.GetHoverColor\(BossRushUIColors\.Surface\),", hero) is not None,
            PANEL + " 的 ESC 键帽三态不再是 Surface / GetHoverColor(Surface)：守卫复算的对象对不上生产")
    hover_body = body_of(ui, "internal static Color GetHoverColor(Color background)")
    hover_lift = const(ui, "HoverLift", UI)
    hover_darken = const(ui, "HoverDarken", UI)
    white_cap = const(ui, "WhiteLabelMaxLuminance", UI)
    guarded = ("RelativeLuminance(background) <= WhiteLabelMaxLuminance" in hover_body
               and "RelativeLuminance(hover) > WhiteLabelMaxLuminance" in hover_body
               and "hover = Color.Lerp(background, Color.black, HoverDarken);" in hover_body)
    # 上限本身要按 TextPrimary 的真实亮度算（按纯白算是 0.183，TextPrimary 压在上面只有 4.10:1）。
    white_label_limit = (luminance(rgb("TextPrimary")) + 0.05) / 4.5 - 0.05
    require(white_cap <= white_label_limit + 1e-4,
            UI + " 的 WhiteLabelMaxLuminance=%.3f 高于 TextPrimary 在 4.5:1 下允许的 %.3f" % (white_cap, white_label_limit))
    if esc is not None and esc.group(1) in tokens:
        glyph = luminance(rgb(esc.group(1)))
        surface_alpha = tokens["Surface"][3]
        for state, fill in (("常态", rgb("Surface")),
                            ("悬停", hover_color(rgb("Surface"), hover_lift, hover_darken, white_cap, guarded))):
            value = ratio(glyph, blend(HERO_ART_WORST, fill, surface_alpha))
            require(value >= 4.5, "ESC 键帽%s时字（%s 13px）压在白插图上只有 %.2f:1（需要 4.5:1）" % (state, esc.group(1), value))

    # ---------- 5. 焦点与悬停的结构：复算的算式就是生产的算式 ----------
    focus_fn = body_of(panel, "private static Color FocusColor(Color row)")
    require("Color focus = Color.Lerp(row, Color.white, ChoiceFocusLift);" in focus_fn
            and "focus.a = ChoiceFocusAlpha;" in focus_fn,
            PANEL + " 的 FocusColor 不再是「向白插值 ChoiceFocusLift、不透明度 ChoiceFocusAlpha」：守卫复算的对象对不上生产")
    choice = body_of(panel, "private void BuildChoice(")
    require("ZombieModeUIHelper.ApplyButtonColors(button, rowColor, FocusColor(rowColor)," in choice,
            PANEL + " 的选项悬停没有走 ApplyButtonColors + FocusColor：Graphic 不置白的话 ColorTint 会把悬停乘暗")
    require("image.color = rowColor;" not in choice,
            PANEL + " 又把行底色写进了 image.color：与 ColorBlock 的绝对色相乘，鼠标悬停变暗")
    require("buttonStrokes.Add(stroke);" in choice and "BossRushUI.ApplyPanelStroke(image, 10, BossRushUISkinPart.Card, BossRushUIColors.Stroke);" in choice,
            PANEL + " 的选项行没有描边或没有记下描边：焦点指示物画不出来")
    select = body_of(panel, "private void Select(int index)")
    focused = body_of(panel, "private void SetFocused(int index, bool focused)")
    require("SetFocused(index, true);" in select and "FocusColor(buttonColors[index])" in focused,
            PANEL + " 的键盘当前项没有走同一个 FocusColor：悬停与键盘又会是两种样子")
    # 2026-09-23 审美审查 UE-17：行边的颜色改由 CanvasRenderer 渐变承担（Graphic 置白、渲染色落在 Stroke / Accent），
    # 与行底 ColorTint 同步 0.08 秒；终值与旧写法相同，复算照旧。
    require("stroke.CrossFadeColor(focused ? BossRushUIColors.Accent : RestStroke(index), focusFade, true, true);" in focused,
            PANEL + " 的焦点不再把行边换成 Accent：只靠行底提亮的话焦点行对常态行在亮底图上只有 2.13:1（审核 F-01）")
    require("stroke.color = Color.white;" in choice and "stroke.CrossFadeColor(RestStroke(index), 0f, true, true);" in choice,
            PANEL + " 的选项行边没有当帧落在常态色：Graphic 置白之后渲染色不落定，行边会是一圈白（守卫按 Stroke 复算）")
    # 2026-09-24 UI 共识对照审查 B-32：常态行边默认仍是 Stroke（上面的复算照旧成立），只有阅读页里「正文正显示的那一栏」
    # 换成更亮的 WarningText（UI 制作共识第 6 节的选中态），焦点照旧是 Accent。
    rest = body_of(panel, "private Color RestStroke(int index)")
    require("look != null && look.Current ? BossRushUIColors.WarningText : BossRushUIColors.Stroke" in rest,
            PANEL + " 的 RestStroke 不再默认落在 Stroke：常态行边的对比度复算失效（或选中栏不再常亮 WarningText）")
    require("GetHoverColor(" not in select + focused + choice,
            PANEL + " 的选项又用回共享 GetHoverColor：向白 0.22 在半透明行底上不够非文本 3:1")

    # ---------- 6. F3 面板与共享按钮：标签按底色挑字色，悬停也不能把标签压下去 ----------
    require("BossRushUI.GetButtonTextColor(color)" in controls,
            CONTROLS + " 的按钮标签写死字色，没有走 GetButtonTextColor：白字压在 Accent 上只有 2.23:1")
    for name in ("Accent", "Success", "Warning", "Danger", "SurfaceRaised"):
        background = rgb(name)
        picked = "TextOnAccent" if luminance(background) > threshold else "TextPrimary"
        fg = luminance(rgb(picked))
        value = ratio(fg, luminance(background))
        require(value >= 4.5, "按钮底色 %s 上，GetButtonTextColor 选的 %s 只有 %.2f:1（需要 4.5:1）" % (name, picked, value))
        hover = hover_color(background, hover_lift, hover_darken, white_cap, guarded)
        value = ratio(fg, luminance(hover))
        require(value >= 4.5,
                "按钮底色 %s 悬停之后，标签 %s 只有 %.2f:1（需要 4.5:1）：GetHoverColor 对白字底又向白提亮了"
                % (name, picked, value))
        # 悬停要看得出来：与常态的亮度差不能小到肉眼分不开（旧版按上限封顶时 Success 只差 1.04 倍）。
        state_step = ratio(luminance(hover), luminance(background))
        require(state_step >= 1.1,
                "按钮底色 %s 的悬停与常态只差 %.2f 倍，几乎看不出悬停" % (name, state_step))

    # ---------- 7. 描边不许用 Divider ----------
    require("Color Stroke = new Color(" in ui,
            UI + " 缺少专用描边 token Stroke：直接用 Divider(a=0.32) 铺描边只有 1.54:1，画了等于没画")
    for rel, source in ((HUD, hud), (CONTROLS, controls), (PANEL, panel)):
        for line in source.splitlines():
            if "ApplyPanelStroke(" in line and "BossRushUIColors.Divider" in line:
                errors.append(rel + " 的描边调用还在用 Divider（a=0.32），达不到非文本 3:1：" + line.strip())
    return errors


def main():
    sources = {}
    for rel in PATHS:
        path = ROOT / rel
        if not path.is_file():
            print("SkyIslandUiContrastGuard: FAIL - 找不到 " + rel)
            return 1
        sources[rel] = read_with_parts(ROOT, rel)

    try:
        errors = check(sources)
    except AssertionError as exc:
        print("SkyIslandUiContrastGuard: FAIL - " + str(exc))
        return 1

    # 反向检查：在内存里恢复旧值或拆掉结构，确认每条断言真的抓得住。
    probes = [
        (ART, "ScrimPeak = 0.84f", "ScrimPeak = 0.60f"),                      # 大标题小字失守
        (ART, "ScrimEdge = 0.28f", "ScrimEdge = 0.45f"),                      # 平台太窄，两行小字滑出去
        (ART, "CaptionScrimPeak = 0.90f", "CaptionScrimPeak = 0.60f"),        # 字幕失守
        (HUD, "FitBannerScrim();", ""),                                       # 大标题宽度不再反算
        (HUD, "FitCaptionScrim(height);", ""),                                # 字幕宽度又写死（F-04）
        (HUD, "BossRushUIColors.TextPrimary, BannerHintY, BannerHintHeight);",
              "BossRushUIColors.TextSecondary, BannerHintY, BannerHintHeight);"),  # 落地提示退回次级色
        (HUD, "Color color = BossRushUIColors.Backdrop;", "Color color = BossRushUIColors.Header;"),
        (HUD, "CardSurfaceAlpha = 0.96f", "CardSurfaceAlpha = 0.60f"),        # 卡片底太透
        (CONTROLS, "BossRushUI.GetButtonTextColor(color)", "BossRushUIColors.TextPrimary"),
        (UI, "Color Stroke = new Color(0.50f, 0.58f, 0.62f, 0.78f)",
             "Color Stroke = new Color(0.50f, 0.58f, 0.62f, 0.32f)"),           # 描边退回 Divider 的透明度
        (UI, "StrokeThickness = 1.5f", "StrokeThickness = 1.25f"),            # 直边没有满覆盖纹素（F-02）
        (UI, "RelativeLuminance(background) <= WhiteLabelMaxLuminance", "false"),  # 悬停不再给白字底封顶
        (UI, "Color Success = new Color(0.166f, 0.484f, 0.335f, 1f)",
             "Color Success = new Color(0.18f, 0.52f, 0.36f, 1f)"),            # 白字标签跌回 4.15
        (UI, "Color Warning = new Color(0.539f, 0.390f, 0.158f, 1f)",
             "Color Warning = new Color(0.58f, 0.42f, 0.17f, 1f)"),
        (PANEL, "BackgroundTintAlpha = 0.82f", "BackgroundTintAlpha = 0.36f"),
        (PANEL, "ChoiceRowAlpha = 0.78f", "ChoiceRowAlpha = 0.30f"),
        (PANEL, "HeroTitleBandAlpha = 0.82f", "HeroTitleBandAlpha = 0.10f"),
        (PANEL, 'MakeRect(hero, "TitleBand"', 'MakeRect(hero, "Removed"'),
        (PANEL, "ChoiceFocusLift = 0.12f", "ChoiceFocusLift = 0.60f"),        # 焦点行抬太亮，标签读不清
        (PANEL, "if (stroke != null) stroke.CrossFadeColor(focused ? BossRushUIColors.Accent : RestStroke(index), focusFade, true, true);", ""),
        (PANEL, "stroke.CrossFadeColor(RestStroke(index), 0f, true, true);", ""),   # 行边渲染色不落定（白圈）
        (PANEL, "look != null && look.Current ? BossRushUIColors.WarningText : BossRushUIColors.Stroke",
                "BossRushUIColors.Accent"),   # 常态行边不再是 Stroke
        (PANEL, "ZombieModeUIHelper.ApplyButtonColors(closeButton, BossRushUIColors.Surface,",
                "ZombieModeUIHelper.ApplyButtonColors(closeButton, BossRushUIColors.SurfaceRaised,"),   # ESC 键帽换了底色
        (PANEL, "            rowColor.a = ChoiceRowAlpha;\n",
                "            rowColor.a = ChoiceRowAlpha;\n            image.color = rowColor;\n"),
        (PANEL, "FocusColor(rowColor),", "BossRushUI.GetHoverColor(rowColor),"),
        (PANEL, "SetFocused(index, true);", "buttons[index].image.color = Color.white;"),
        (PANEL, "KeyCapSize * 0.5f), BossRushUIColors.TextPrimary);",
                "KeyCapSize * 0.5f), BossRushUIColors.TextSecondary);"),     # ESC 键帽退回次级色（F-19）
        (PANEL, "BodyFont,\n                BossRushUIColors.TextPrimary, TextAlignmentOptions.TopLeft);",
                "BodyFont,\n                BossRushUIColors.TextSecondary, TextAlignmentOptions.TopLeft);"),
    ]
    for path, before, after in probes:
        if before not in sources[path]:
            errors.append("反向检查锚点失效：" + path + " -> " + before)
            continue
        altered = dict(sources)
        altered[path] = altered[path].replace(before, after, 1)
        try:
            if not check(altered):
                errors.append("未拦截旧配置：" + path + " -> " + before)
        except AssertionError:
            pass   # 常量被改没了也算抓住

    if errors:
        print("SkyIslandUiContrastGuard: FAIL\n  " + "\n  ".join(errors))
        return 1
    print("SkyIslandUiContrastGuard: PASS（线性光合成 + 描边真实覆盖率 %.2f；大标题三行 / 字幕行首行尾 / 右上卡片 / "
          "面板正文·选项·焦点环 / ESC 键帽 / 按钮悬停；%d 个反向检查；非 Unity 实机）"
          % (stroke_coverage(const(clean_source(sources[UI]), "StrokeThickness", UI)), len(probes)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
