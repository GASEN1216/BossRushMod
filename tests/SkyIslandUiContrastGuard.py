# -*- coding: utf-8 -*-
"""天空岛 UI 的对比度复算（WCAG 2.x）：把每一处前景/背景**从生产常量重算一遍**。

## 为什么要有这份

2026-09-13 盘点时实算出 12 条不达标，全部集中在两处，而且**没有任何守卫会拦**：

  1. **区域大标题的压暗底把厚度给错了行**。旧版竖向是 `0.5-0.5·cos(2πv)` 的余弦钟形，
     峰值正好落在 44px 的大地名上——它按 WCAG 只需要 3:1；而真正需要 4.5:1 的两行小字
     （眉题 y=+44、落地提示 y=-42）被甩到钟形的腰上，在 230 高的压暗底里 α 只有 0.405 / 0.423。
     实算：亮云海下眉题 **2.42:1**、提示行 **2.54:1**，云海高光下 1.66 / 1.75。
  2. **F3 天空岛面板的按钮标签写死白字**，绕开了专为此写的 `BossRushUI.GetButtonTextColor`：
     白字压在 Accent 上 **2.23:1**、Success 4.15:1、Warning 4.34:1。

## 它证明什么、不证明什么

证明的是**算术**：给定设计 token、压暗底曲线与三行文字的位置，合成出来的对比度是多少。
不证明 Unity 的实际渲染、字体抗锯齿与玩家的主观观感——那些只能实机看，
见 docs/制作教程/天空岛/天空岛_待人工验证清单.md。

另外从 2026-09-13 起，剧情面板的底从纯色板换成了**区域底图**，所以「正文/选项/标题坐在什么上面」
不再是一个常量，而是一张活的画面。下面按 13 张 `skyisland_bg_*.png` 里**最亮那张的 p99**（0.747）
复算；主视觉标题底下是没模糊过的原图，直接按纯白（1.0）算——连白底都压得住才算过。

**场景亮度是代理值，不是实机读数**：取自 `Assets/ui/SkyIsland/skyisland_scene_*.png`
12 张场景横幅的相对亮度统计（p90 平均 0.679、p99 平均 0.839，2026-09-13 实测）。
那 12 张是生图产物、与岛上实际渲染同色系但不是同一份像素。结论的方向在 0.55 以上的
任何背景亮度下都成立，确切数字仍须实机复核。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

UI = "Common/UI/BossRushUI.cs"
ART = "DebugAndTools/SkyIsland/SkyIslandUiArt.cs"
HUD = "DebugAndTools/SkyIsland/SkyIslandHud.cs"
CONTROLS = "DebugAndTools/SkyIsland/SkyIslandControls.cs"
PANEL = "DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs"
PATHS = [UI, ART, HUD, CONTROLS, PANEL]

# 13 张场景横幅的相对亮度统计（见文件头）。p90 是「常见的亮」，p99 是「云海高光」。
SCENE_P90 = 0.679
SCENE_P99 = 0.839
# 面板整屏底图（13 张 skyisland_bg_*.png，由 tools/gen_sky_island_panel_backgrounds.py 派生）
# 的实测亮度：中位 0.098–0.397，最亮那张（S2）的 p99 = 0.747。压暗与正文对比度按这个最坏值算。
PANEL_BG_P99 = 0.747
# 主视觉插图是**原图**（没模糊过），最坏按纯白算：标题实底带必须连白底都压得住。
HERO_ART_WORST = 1.0
# 压暗底的颜色写在 GetTitleScrim 的 SetPixel 里，这里按同一个三元组复算。
SCRIM_RGB = (0.02, 0.03, 0.04)

COLOR = re.compile(
    r"Color (\w+) = new Color\(\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f"
    r"(?:\s*,\s*([\d.]+)f)?\s*\)")


def lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def luminance(rgb):
    return 0.2126 * lin(rgb[0]) + 0.7152 * lin(rgb[1]) + 0.0722 * lin(rgb[2])


def over(fg, bg):
    """fg=(r,g,b,a) 合成到不透明的 bg 上。"""
    a = fg[3]
    return tuple(fg[i] * a + bg[i] * (1 - a) for i in range(3))


def ratio(fg, bg):
    a, b = luminance(fg), luminance(bg)
    if a < b:
        a, b = b, a
    return (a + 0.05) / (b + 0.05)


def plateau(t, edge):
    """两端 smoothstep 淡出 + 中间平台，与 SkyIslandUiArt.Plateau 同一条曲线。"""
    if edge <= 0:
        return 1.0
    t = min(1.0, max(0.0, t))
    head = min(1.0, max(0.0, t / edge))
    tail = min(1.0, max(0.0, (1.0 - t) / edge))
    return head * head * (3 - 2 * head) * tail * tail * (3 - 2 * tail)


def const(src, name, where):
    m = re.search(r"\b" + name + r"\s*=\s*(-?[\d.]+)f\s*;", src)
    if not m:
        raise AssertionError("读不到 " + where + " 的常量 " + name)
    return float(m.group(1))


def required(px):
    """WCAG：大字（≥24px 常规字重）3:1，正文 4.5:1。本库画布 1920×1080，1 单位 ≈ 1080p 的 1px。"""
    return 3.0 if px >= 24 else 4.5


def check(sources):
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    ui = sources[UI]
    art = sources[ART]
    hud = sources[HUD]
    controls = sources[CONTROLS]
    panel = sources[PANEL]

    tokens = {}
    for m in COLOR.finditer(ui):
        rgb = tuple(float(v) for v in m.group(2, 3, 4))
        alpha = float(m.group(5)) if m.group(5) else 1.0
        tokens[m.group(1)] = rgb + (alpha,)
    for name in ("Backdrop", "Surface", "SurfaceRaised", "Divider", "TextPrimary",
                 "TextSecondary", "Accent", "Success", "Warning", "Danger",
                 "WarningText", "SuccessText", "TextOnAccent"):
        require(name in tokens, UI + " 缺少设计 token " + name)
    if errors:
        return errors

    # ---------- 1. 区域大标题：三行文字各坐在多厚的压暗底上 ----------
    peak = const(art, "ScrimPeak", ART)
    vedge = const(art, "ScrimEdge", ART)
    hedge = const(art, "ScrimHorizontalEdge", ART)
    scrim_h = const(hud, "BannerScrimHeight", HUD)
    rows = [
        ("眉题「晴岚群岛」", "TextSecondary",
         const(hud, "BannerOverlineY", HUD), const(hud, "BannerOverlineHeight", HUD),
         const(hud, "AreaOverlineFont", HUD)),
        ("地名大标题", "TextPrimary",
         const(hud, "BannerTitleY", HUD), const(hud, "BannerTitleHeight", HUD),
         const(hud, "AreaTitleFont", HUD)),
        ("落地操作提示", "TextSecondary",
         const(hud, "BannerHintY", HUD), const(hud, "BannerHintHeight", HUD),
         const(hud, "BannerHintFont", HUD)),
    ]
    for label, token, y, height, font in rows:
        for scene_name, scene in (("场景 p90", SCENE_P90), ("场景 p99", SCENE_P99)):
            base = (scene, scene, scene)
            # 文字框的上下两端都要算：只算中心的话，一行高 60 的大标题可以有半边滑出平台。
            worst = None
            for edge_y in (y - height / 2.0, y, y + height / 2.0):
                v = (edge_y + scrim_h / 2.0) / scrim_h
                alpha = plateau(v, vedge) * peak
                bg = over(SCRIM_RGB + (alpha,), base)
                value = ratio(tokens[token][:3], bg)
                if worst is None or value < worst:
                    worst = value
            require(worst >= required(font),
                    "区域大标题的「%s」在%s下只有 %.2f:1（需要 %.1f:1）："
                    "压暗底的厚度没有罩住这一行" % (label, scene_name, worst, required(font)))

    # 横向：压暗底必须按文字实测宽度扩，否则英文长句的行首行尾会滑进淡出区。
    # 带分号的才是**调用点**：只找方法名的话，把调用删掉、留个定义在那儿照样绿。
    require("FitBannerScrim();" in hud,
            HUD + " 没有按文字实测宽度反算压暗底宽度：英文落地提示整句能到约 700px，"
                  "行尾会滑出平台、背后又变回亮云海")
    require("1f - 2f * SkyIslandUiArt.ScrimHorizontalEdge" in hud,
            HUD + " 压暗底宽度的反算没有用 ScrimHorizontalEdge，平台宽度会和实际曲线对不上")
    require(0.0 < hedge < 0.5,
            ART + " 压暗底横向淡出比例 %.2f 不合法（≥0.5 时平台宽度为 0）" % hedge)

    # ---------- 2. 底部字幕 ----------
    caption_font = const(hud, "CaptionFont", HUD)
    # 字幕的压暗底随文字高度走，文字始终在正中 v=0.5。
    caption_alpha = plateau(0.5, vedge) * peak
    for token, label in (("TextPrimary", "普通字幕"), ("WarningText", "警示字幕")):
        for scene_name, scene in (("场景 p90", SCENE_P90), ("场景 p99", SCENE_P99)):
            bg = over(SCRIM_RGB + (caption_alpha,), (scene, scene, scene))
            value = ratio(tokens[token][:3], bg)
            require(value >= required(caption_font),
                    "%s在%s下只有 %.2f:1（需要 %.1f:1）。警示字幕尤其不能省——"
                    "噬风的预警窗口只有 1.4 秒" % (label, scene_name, value, required(caption_font)))

    # ---------- 2.5 剧情面板：整屏底图之上的正文、选项行与主视觉标题 ----------
    # 面板底从「纯色板」换成了区域底图（CR-2026-09-13-007）。底图是活的画面，
    # 所以下面每一条都按**最亮的那一张的 p99** 复算，不是按平均。
    tint = const(panel, "BackgroundTintAlpha", PANEL)
    row_alpha = const(panel, "ChoiceRowAlpha", PANEL)
    band_alpha = const(panel, "HeroTitleBandAlpha", PANEL)
    body_font = const(panel, "BodyFont", PANEL)
    choice_font = const(panel, "ChoiceFont", PANEL)
    title_font_min = const(panel, "TitleFontMin", PANEL)

    # 正文直接坐在「底图 × 压暗」上，没有行底垫着。
    panel_bg = over(tokens["Surface"][:3] + (tint,), (PANEL_BG_P99,) * 3)
    value = ratio(tokens["TextSecondary"][:3], panel_bg)
    require(value >= required(body_font),
            "剧情面板正文压在整屏底图上只有 %.2f:1（需要 %.1f:1）："
            "BackgroundTintAlpha=%.2f 压不住最亮那张底图" % (value, required(body_font), tint))

    # 选项行底刻意不铺满（让底图透出来），所以标签与**边**都要在行底上重算。
    row_bg = over(tokens["SurfaceRaised"][:3] + (row_alpha,), panel_bg)
    value = ratio(tokens["TextPrimary"][:3], row_bg)
    require(value >= required(choice_font),
            "选项标签在半透明行底上只有 %.2f:1（需要 %.1f:1）" % (value, required(choice_font)))
    value = ratio(over(tokens["Stroke"], row_bg), row_bg)
    require(value >= 3.0,
            "选项行的边在半透明行底上只有 %.2f:1（WCAG 1.4.11 非文本 3:1）。"
            "那圈边是「这一行是可点控件」的唯一证据，ChoiceRowAlpha=%.2f 透过头了"
            % (value, row_alpha))

    # 主视觉标题压在实底带上，而带子底下是**没模糊过**的插图：最坏按纯白算。
    band_bg = over(tokens["Surface"][:3] + (band_alpha,), (HERO_ART_WORST,) * 3)
    value = ratio(tokens["TextPrimary"][:3], band_bg)
    require(value >= required(title_font_min),
            "主视觉标题压在实底带上只有 %.2f:1（需要 %.1f:1）："
            "HeroTitleBandAlpha=%.2f 连白底都压不住" % (value, required(title_font_min), band_alpha))
    # 标题必须整个落在实底带里——只用一条 t² 渐变时，标题上沿那里 alpha 只有 0.19（旧 bug）。
    require("HeroTitleBandAlpha" in panel and 'MakeRect(hero, "TitleBand"' in panel,
            PANEL + " 主视觉没有标题实底带：单靠 t² 渐变的话不透明度全堆在底边，"
                    "标题最上面那行等于直接压在插图上")

    # ---------- 3. F3 面板：按钮标签必须按底色挑字色 ----------
    require("BossRushUI.GetButtonTextColor(color)" in controls,
            CONTROLS + " 的按钮标签写死字色，没有走 GetButtonTextColor："
                       "白字压在 Accent 上只有 2.23:1")
    threshold = const(ui, "LightBackgroundLuminance", UI)
    for name in ("Accent", "Success", "Warning", "Danger", "SurfaceRaised"):
        background = tokens[name][:3]
        picked = "TextOnAccent" if luminance(background) > threshold else "TextPrimary"
        value = ratio(tokens[picked][:3], background)
        require(value >= 4.5,
                "F3 面板按钮底色 %s 上，GetButtonTextColor 选的 %s 只有 %.2f:1（需要 4.5:1）"
                % (name, picked, value))

    # ---------- 4. 容器分层：卡片、面板、列表行必须靠描边分开，不能靠底色 ----------
    # SurfaceRaised 对 Surface 实算只有约 1.03:1（远低于非文本 3:1）。**不去拉开两个底色**——
    # 那会破坏整套深色调；改成给每一块补一圈描边，并在这里按 WCAG 1.4.11 的 3:1 复算。
    # 选项行尤其是硬要求：它是可点控件的边界，不是装饰。
    require("Color Stroke = new Color(" in ui,
            UI + " 缺少专用描边 token Stroke：直接用 Divider(a=0.32) 铺描边只有 1.54:1，画了等于没画")
    for rel, source in ((HUD, hud), (CONTROLS, controls), (PANEL, panel)):
        for line in source.splitlines():
            if "ApplyPanelStroke(" not in line:
                continue
            require("BossRushUIColors.Divider" not in line,
                    rel + " 的描边调用还在用 Divider（a=0.32），达不到非文本 3:1：" + line.strip())
    for scene_name, scene in (("场景 p90", SCENE_P90), ("场景 p99", SCENE_P99), ("暗地形 0.20", 0.20)):
        base = (scene, scene, scene)
        panel_bg = over(tokens["Surface"], over(tokens["Backdrop"], base))
        raised_bg = over(tokens["SurfaceRaised"], panel_bg)
        # 右上卡片没有 Backdrop 垫底，直接压在场景上。
        card_bg = over(tokens["Surface"], base)
        for label, bg in (("面板外框", panel_bg), ("右上卡片外框", card_bg), ("选项行边框", raised_bg)):
            value = ratio(over(tokens["Stroke"], bg), bg)
            require(value >= 3.0,
                    "%s在%s下只有 %.2f:1（WCAG 1.4.11 对非文本要求 3:1）" % (label, scene_name, value))
    return errors


def main():
    sources = {}
    for rel in PATHS:
        path = ROOT / rel
        if not path.is_file():
            print("SkyIslandUiContrastGuard: FAIL - 找不到 " + rel)
            return 1
        sources[rel] = path.read_text(encoding="utf-8-sig")

    try:
        errors = check(sources)
    except AssertionError as exc:
        print("SkyIslandUiContrastGuard: FAIL - " + str(exc))
        return 1

    # 反向检查：在内存里恢复旧值，确认每条断言真的抓得住。
    probes = [
        # 压暗底峰值退回旧的 0.60：眉题与提示行立刻跌回 4.10 / 3.08
        (ART, "ScrimPeak = 0.72f", "ScrimPeak = 0.60f"),
        # 竖向淡出区拉宽到 0.45：平台只剩 [0.45,0.55]，两行小字整个滑出去
        (ART, "ScrimEdge = 0.28f", "ScrimEdge = 0.45f"),
        # 横向反算被拆掉：英文长句行尾又没有底
        (HUD, "FitBannerScrim();", ""),
        # F3 按钮又写死白字
        (CONTROLS, "BossRushUI.GetButtonTextColor(color)", "BossRushUIColors.TextPrimary"),
        # 描边退回 Divider 那 0.32 的 alpha：三处边全部跌回 1.5:1
        (UI, "Color Stroke = new Color(0.42f, 0.52f, 0.58f, 0.78f)",
             "Color Stroke = new Color(0.42f, 0.52f, 0.58f, 0.32f)"),
        # 整屏底图压暗退回一半：正文在最亮那张底图上失守
        (PANEL, "BackgroundTintAlpha = 0.72f", "BackgroundTintAlpha = 0.36f"),
        # 选项行底透过头：边跌破非文本 3:1
        (PANEL, "ChoiceRowAlpha = 0.78f", "ChoiceRowAlpha = 0.30f"),
        # 标题实底带退回全透：标题直接压在插图上
        (PANEL, "HeroTitleBandAlpha = 0.82f", "HeroTitleBandAlpha = 0.10f"),
        # 实底带整条拆掉：回到「只有一条 t² 渐变」的旧写法
        (PANEL, 'MakeRect(hero, "TitleBand"', 'MakeRect(hero, "Removed"'),
        # 正文次级色压暗：字幕与小字一起失守
        (UI, "Color TextSecondary = new Color(0.67f, 0.72f, 0.75f, 1f)",
             "Color TextSecondary = new Color(0.33f, 0.36f, 0.38f, 1f)"),
        # Success / Warning 退回压暗前的旧值：白字标签跌回 4.15 / 4.34
        (UI, "Color Success = new Color(0.166f, 0.484f, 0.335f, 1f)",
             "Color Success = new Color(0.18f, 0.52f, 0.36f, 1f)"),
        (UI, "Color Warning = new Color(0.539f, 0.390f, 0.158f, 1f)",
             "Color Warning = new Color(0.58f, 0.42f, 0.17f, 1f)"),
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
    print("SkyIslandUiContrastGuard: PASS（大标题三行 + 字幕两态 × 两档场景亮度 + "
          "F3 按钮五色 + 描边分层；%d 个反向检查；非 Unity 实机）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
