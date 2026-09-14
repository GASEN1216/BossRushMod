# -*- coding: utf-8 -*-
"""把天空岛剧情面板离线画出来，供不进游戏时预览版式。

**它不是截图，是按生产常量重画的示意图。** 尺寸、间距、颜色、优先级收缩算法全部从
`SkyIslandStoryPresentation.cs` 与 `Common/UI/BossRushUI.cs` 实读，插图用真实生成的 PNG，
文案用真实的 `PointName` / `Lore` / 选项标签。所以版式比例是可信的；
但字体度量与 TMP 不同、圆角与九宫格是近似，**最终观感仍须实机确认**。

已知的示意图失真一处：预览用的是微软雅黑，它**没有 U+2713「✓」这个字形**，
所以 `story.Summary` 里的支线勾选在示意图里是方框。游戏内用的是 TMP 字体资产而不是雅黑，
且 `CampaignHud` / `WishFountainUI` / `ModeH` 早就在玩家可见 UI 里用同一个字符，
因此**大概率没问题**——但这只是旁证，真要确认得实机看一眼。

用法：
    python tools/preview_sky_island_panel.py
输出：
    output/sky_island_panel_preview.png
"""
import os
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source

PANEL_CS = ROOT / 'DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs'
UI_CS = ROOT / 'Common/UI/BossRushUI.cs'
ART = ROOT / 'Assets/ui/SkyIsland'
OUT = ROOT / 'output/sky_island_panel_preview.png'

FONT_CANDIDATES = [r'C:\Windows\Fonts\msyh.ttc', r'C:\Windows\Fonts\msyhbd.ttc',
                   r'C:\Windows\Fonts\simhei.ttf', r'C:\Windows\Fonts\simsun.ttc']


def font(size, bold=False):
    for path in (FONT_CANDIDATES[1:] if bold else FONT_CANDIDATES):
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


def const(name):
    src = clean_source(PANEL_CS.read_text(encoding='utf-8-sig'))
    m = re.search(r'\b' + name + r'\s*=\s*([0-9.]+)f', src)
    if not m:
        raise AssertionError('读不到面板常量 ' + name)
    return float(m.group(1))


def color(name):
    """从 BossRushUIColors 读色值（0–1 浮点）转成 8 位 RGBA。"""
    src = clean_source(UI_CS.read_text(encoding='utf-8-sig'))
    m = re.search(r'\b' + name + r'\s*=\s*new Color\(([^)]+)\)', src)
    if not m:
        raise AssertionError('读不到颜色 ' + name)
    parts = [float(x.strip().rstrip('f')) for x in m.group(1).split(',')]
    while len(parts) < 4:
        parts.append(1.0)
    return tuple(int(round(p * 255)) for p in parts)


C = {n: const(n) for n in ('PanelWidth', 'Pad', 'Gap', 'HeroMaxHeight', 'HeroMinHeight',
                           'HeroInset', 'HeroFadeFraction', 'HeroTitleBandAlpha', 'HeroFadeMin',
                           'BackgroundTintAlpha',
                           'PortraitSize', 'TitleMinHeight', 'TitleFontMax', 'BodyMinHeight',
                           'BodyPreferredMax', 'BodyFont', 'ChoiceFont',
                           'ChoiceMinHeight', 'ChoicePadY', 'ChoicePadX',
                           'PortraitShadowWidth', 'PortraitShadowHeight',
                           'ScrollbarGutter', 'KeyHintWidth', 'KeyCapSize', 'DividerHeight',
                           'ChoiceRowAlpha')}

# 与布局属性测试同一个上界：生产是 Clamp(viewport.y - 140, 520, 980)，1080p 下取 940。
MAX_PANEL = min(980.0, 1080.0 - 140.0)
COL = {n: color(n) for n in ('Surface', 'SurfaceRaised', 'Divider', 'Stroke', 'TextPrimary',
                             'TextSecondary', 'Accent', 'TextOnAccent', 'Backdrop', 'WarningText')}
CONTENT_W = C['PanelWidth'] - C['Pad'] * 2


def wrap(draw, text, fnt, width):
    lines = []
    for para in text.split('\n'):
        cur = ''
        for ch in para:
            probe = cur + ch
            if draw.textlength(probe, font=fnt) > width and cur:
                lines.append(cur)
                cur = ch
            else:
                cur = probe
        lines.append(cur)
    return lines


def rounded(img, box, radius, fill):
    layer = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle(box, radius=radius, fill=fill)
    img.alpha_composite(layer)


ART_CS = ROOT / 'DebugAndTools/SkyIsland/SkyIslandUiArt.cs'


def art_const(name):
    src = clean_source(ART_CS.read_text(encoding='utf-8-sig'))
    m = re.search(r'\b' + name + r'\s*=\s*(-?[0-9.]+)f', src)
    if not m:
        raise AssertionError('读不到 SkyIslandUiArt 常量 ' + name)
    return float(m.group(1))


def stroke(img, box, radius, color, width=1.25):
    """1px 圆角描边，对应生产的 BossRushUI.ApplyPanelStroke。

    **这不是装饰**：SurfaceRaised 对面板底 Surface 实算只有 1.03:1，
    不画边的话选项行根本不是「一个可点的区域」，只是几行浮着的字。"""
    layer = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle(box, radius=radius, outline=color,
                                            width=max(1, int(round(width))))
    img.alpha_composite(layer)


def render(title, body, choices, banner_name=None, portrait_name=None):
    """按生产常量重画一块剧情面板。**不是截图**：字体度量、九宫格与描边都是近似，
    比例与明暗可信，观感仍须实机。

    2026-09-13 版式：整块面板底铺那一区的模糊底图，标题压在**全出血**的主视觉上。
    旧版是「顶上一条 ContentWidth 宽的插图 + 剩下大半屏纯色板」。
    """
    scratch = Image.new('RGBA', (10, 10))
    d0 = ImageDraw.Draw(scratch)
    f_title = font(int(C['TitleFontMax']), True)
    f_body = font(int(C['BodyFont']))
    f_choice = font(int(C['ChoiceFont']))
    PW = C['PanelWidth']
    inset = C['HeroInset']

    hero_content_w = PW - inset * 2
    # 立绘贴右下角、不留 inset；标题让到左边。
    title_w = (PW - inset - C['PortraitSize'] - C['Gap']) if portrait_name else hero_content_w
    title_lines = wrap(d0, title, f_title, title_w)
    title_h = max(C['TitleMinHeight'], len(title_lines) * C['TitleFontMax'] * 1.25 + 4)
    title_block = title_h
    hero_floor = max(C['HeroMinHeight'], title_block + inset * 2)
    if portrait_name:
        hero_floor = max(hero_floor, C['PortraitSize'])

    banner = None
    hero_art = 0.0
    if banner_name and (ART / (banner_name + '.png')).exists():
        banner = Image.open(ART / (banner_name + '.png')).convert('RGBA')
        hero_art = min(PW / (banner.width / banner.height), C['HeroMaxHeight'])
    hero_h = max(hero_floor, hero_art)

    choice_lines, choice_hs = [], []
    for label in choices:
        ls = wrap(d0, label, f_choice, CONTENT_W - C['ChoicePadX'] * 2 - C['KeyHintWidth'])
        choice_lines.append(ls)
        choice_hs.append(max(C['ChoiceMinHeight'],
                             len(ls) * C['ChoiceFont'] * 1.25 + 4 + C['ChoicePadY'] * 2))
    choices_h = sum(choice_hs) + max(0, len(choice_hs) - 1) * C['Gap'] * 0.5

    body_lines = wrap(d0, body, f_body, CONTENT_W - C['ScrollbarGutter'])
    body_h = min(C['BodyPreferredMax'],
                 max(C['BodyMinHeight'], len(body_lines) * C['BodyFont'] * 1.25 + 4))

    divider_block = C['DividerHeight'] + C['Gap']
    chrome = C['Pad'] + hero_h + divider_block + choices_h + C['Gap'] * 2
    panel_h = chrome + body_h
    if panel_h > MAX_PANEL:
        excess = panel_h - MAX_PANEL
        give = min(excess, max(0.0, body_h - C['BodyMinHeight']))
        body_h -= give
        excess -= give
        if excess > 0:
            give = min(excess, max(0.0, hero_h - hero_floor))
            hero_h -= give
            chrome -= give
        panel_h = min(MAX_PANEL, chrome + body_h)

    PW_i, PH_i = int(PW), int(panel_h)
    # ---- 面板层：先整块画完，最后用圆角蒙版切一次 ----
    panel = Image.new('RGBA', (PW_i, PH_i), COL['Surface'][:3] + (245,))

    # 整屏底图 + 压暗。底图是 tools/gen_sky_island_panel_backgrounds.py 从场景横幅派生的模糊小图。
    bg_region = 'journal'
    if banner_name and banner_name.startswith('skyisland_scene_'):
        bg_region = banner_name[len('skyisland_scene_'):]
    bg_path = ART / ('skyisland_bg_' + bg_region + '.png')
    if not bg_path.exists():
        bg_path = ART / 'skyisland_bg_journal.png'
    if bg_path.exists():
        panel.alpha_composite(Image.open(bg_path).convert('RGBA').resize((PW_i, PH_i), Image.LANCZOS))
        tint_a = int(round(C['BackgroundTintAlpha'] * 255))
        panel.alpha_composite(Image.new('RGBA', (PW_i, PH_i), COL['Surface'][:3] + (tint_a,)))

    # ---- 主视觉：插图全出血 + 底部渐隐 + 压在上面的标题 ----
    hero_i = int(hero_h)
    if banner is not None:
        panel.alpha_composite(banner.resize((PW_i, hero_i), Image.LANCZOS), (0, 0))
    # 实底带罩住标题 + 带子上方再淡出到全透，与生产 BuildHero 同一条算式。
    # 只用一条 t² 渐变的话，不透明度全堆在底边，标题上沿只落在 0.19 的淡出区上（CR-2026-09-13-007）。
    band_a = C['HeroTitleBandAlpha']
    band_h = int(min(hero_h, inset * 2 + title_block * 0.5 + title_h * 0.5))
    panel.alpha_composite(
        Image.new('RGBA', (PW_i, band_h), COL['Surface'][:3] + (int(round(band_a * 255)),)),
        (0, hero_i - band_h))
    falloff = int(min(hero_i - band_h, max(C['HeroFadeMin'], hero_h * C['HeroFadeFraction'] - band_h)))
    if falloff > 1:
        fade = Image.new('RGBA', (PW_i, falloff), (0, 0, 0, 0))
        fpx = fade.load()
        for fy in range(falloff):
            t = fy / float(max(1, falloff - 1))
            a = int(round(t * t * band_a * 255))
            for fx in range(PW_i):
                fpx[fx, fy] = COL['Surface'][:3] + (a,)
        panel.alpha_composite(fade, (0, hero_i - band_h - falloff))

    draw = ImageDraw.Draw(panel)
    title_x = inset
    if portrait_name and (ART / (portrait_name + '.png')).exists():
        # 立绘贴主视觉右下角、**没有底板**：源图本来就是抠图，垫板子只会变成「黑方块里贴张小图」。
        size = int(C['PortraitSize'])
        px0 = PW_i - size
        py0 = hero_i - size
        # 脚下落影：径向柔光非等比拉成扁椭圆，把抠图钉在画面里。
        sw = int(size * C['PortraitShadowWidth'])
        sh = int(size * C['PortraitShadowHeight'])
        shadow = Image.new('RGBA', (sw, sh), (0, 0, 0, 0))
        spx = shadow.load()
        for sy in range(sh):
            for sx in range(sw):
                dx = (sx - (sw - 1) / 2.0) / ((sw - 1) / 2.0)
                dy = (sy - (sh - 1) / 2.0) / ((sh - 1) / 2.0)
                t = max(0.0, 1.0 - (dx * dx + dy * dy) ** 0.5)
                a = t * t * (3 - 2 * t) * (COL['Backdrop'][3] / 255.0)
                spx[sx, sy] = COL['Backdrop'][:3] + (int(round(a * 255)),)
        panel.alpha_composite(shadow, (px0 + (size - sw) // 2, hero_i - sh // 2))
        face = Image.open(ART / (portrait_name + '.png')).convert('RGBA')
        panel.alpha_composite(face.resize((size, size), Image.LANCZOS), (px0, py0))
        draw = ImageDraw.Draw(panel)
    # 标题左对齐、底边距 hero 底边一个 inset（海报式主视觉的通用写法）。
    ty = hero_i - inset - title_block + (title_block - len(title_lines) * C['TitleFontMax'] * 1.25) / 2
    for line in title_lines:
        draw.text((title_x, ty), line, font=f_title, fill=COL['TextPrimary'])
        ty += C['TitleFontMax'] * 1.25

    y = hero_h + C['Gap']
    dh = C['DividerHeight']
    draw.rectangle((C['Pad'], y + dh * 0.375, C['Pad'] + CONTENT_W, y + dh * 0.625),
                   fill=COL['Divider'][:3] + (int(COL['Divider'][3] * 1.6),))
    y += divider_block

    by = y
    for line in body_lines:
        if by + C['BodyFont'] * 1.25 > y + body_h:
            draw.text((C['Pad'], by), '…', font=f_body, fill=COL['TextSecondary'])
            break
        draw.text((C['Pad'], by), line, font=f_body, fill=COL['TextSecondary'])
        by += C['BodyFont'] * 1.25
    y += body_h + C['Gap']

    f_cap = font(13, True)
    for index, (ls, h) in enumerate(zip(choice_lines, choice_hs)):
        # 行底刻意不铺满：让区域底图透出来（生产同一个常量 ChoiceRowAlpha）。
        rounded(panel, (C['Pad'], y, C['Pad'] + CONTENT_W, y + h), 10,
                COL['SurfaceRaised'][:3] + (int(round(C['ChoiceRowAlpha'] * 255)),))
        stroke(panel, (C['Pad'], y, C['Pad'] + CONTENT_W, y + h), 10, COL['Stroke'])
        kx = C['Pad'] + C['ChoicePadX']
        ky = y + (h - C['KeyCapSize']) / 2
        if index < 9:
            rounded(panel, (kx, ky, kx + C['KeyCapSize'], ky + C['KeyCapSize']), 6,
                    COL['Surface'][:3] + (255,))
        draw = ImageDraw.Draw(panel)
        if index < 9:
            digit = str(index + 1)
            w = draw.textlength(digit, font=f_cap)
            draw.text((kx + (C['KeyCapSize'] - w) / 2, ky + 3), digit, font=f_cap,
                      fill=COL['TextSecondary'])
        cy = y + (h - len(ls) * C['ChoiceFont'] * 1.25) / 2
        for line in ls:
            draw.text((kx + C['KeyHintWidth'], cy), line, font=f_choice, fill=COL['TextPrimary'])
            cy += C['ChoiceFont'] * 1.25
        y += h + C['Gap'] * 0.5

    # 页脚「继续旅程」已删：与 ESC 完全等价。关闭提示改成主视觉右上角的 ESC 键帽。
    ex = PW_i - inset - 44
    ey = inset
    rounded(panel, (ex, ey, ex + 44, ey + C['KeyCapSize']), 6, COL['Surface'][:3] + (255,))
    draw = ImageDraw.Draw(panel)
    f_esc = font(13, True)
    w = draw.textlength('ESC', font=f_esc)
    draw.text((ex + (44 - w) / 2, ey + 3), 'ESC', font=f_esc, fill=COL['TextSecondary'])

    # ---- 圆角蒙版 + 描边（生产里是 Mask + ApplyPanelStroke）----
    corner = Image.new('L', (PW_i, PH_i), 0)
    ImageDraw.Draw(corner).rounded_rectangle((0, 0, PW_i - 1, PH_i - 1), radius=18, fill=255)
    panel.putalpha(Image.composite(panel.getchannel('A'), Image.new('L', (PW_i, PH_i), 0), corner))

    img = Image.new('RGBA', (PW_i + 120, PH_i + 120), (18, 22, 28, 255))
    img.alpha_composite(panel, (60, 60))
    stroke(img, (60, 60, 60 + PW_i - 1, 60 + PH_i - 1), 18, COL['Stroke'])
    return img


HUD_CS = ROOT / 'DebugAndTools/SkyIsland/SkyIslandHud.cs'
HUD_OUT = ROOT / 'output/sky_island_hud_preview.png'


def hud_const(name):
    src = clean_source(HUD_CS.read_text(encoding='utf-8-sig'))
    m = re.search(r'\b' + name + r'\s*=\s*(-?[0-9.]+)f', src)
    if not m:
        raise AssertionError('读不到 HUD 常量 ' + name)
    return float(m.group(1))


def spaced(draw, center_x, top, text, fnt, fill, spacing):
    """按 TMP characterSpacing（1/100 em）拉开字距，水平居中绘制。"""
    extra = fnt.size * spacing / 100.0
    widths = [draw.textlength(ch, font=fnt) for ch in text]
    x = center_x - (sum(widths) + extra * max(0, len(text) - 1)) / 2
    for ch, w in zip(text, widths):
        draw.text((x, top), ch, font=fnt, fill=fill)
        x += w + extra


def _plateau(t, edge):
    """与生产 SkyIslandUiArt.Plateau 同一条曲线：两端 smoothstep 淡出 + 中间平台。"""
    if edge <= 0:
        return 1.0
    t = min(1.0, max(0.0, t))
    a = min(1.0, max(0.0, t / edge))
    b = min(1.0, max(0.0, (1.0 - t) / edge))
    return a * a * (3 - 2 * a) * b * b * (3 - 2 * b)


def soft_scrim(w, h):
    """与生产 SkyIslandUiArt.GetTitleScrim 同一条二维柔边，常量从生产源码读。

    竖向从余弦钟形改成平台（2026-09-13）：钟形的峰值坐在只需要 3:1 的 44px 大地名上，
    而需要 4.5:1 的两行小字被甩到腰上，α 只有 0.405 / 0.423 → 实算 2.42:1 / 2.54:1。"""
    peak = art_const('ScrimPeak')
    vedge = art_const('ScrimEdge')
    hedge = art_const('ScrimHorizontalEdge')
    w, h = int(w), int(h)
    scrim = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    px_ = scrim.load()
    for xx in range(w):
        horizontal = _plateau(xx / float(w - 1), hedge)
        for yy in range(h):
            vertical = _plateau(yy / float(h - 1), vedge)
            px_[xx, yy] = (5, 8, 10, int(horizontal * vertical * peak * 255))
    return scrim


def render_hud():
    """1920x1080 参考分辨率下的 HUD 占位示意，铺一张真实岛景当底，好判断对比度与遮挡。

    画的是三层**同时**出现的最拥挤时刻：右侧卡片（目标刚更新）、落地那一次区域大标题（带提示）、
    中下方警示字幕（两行，最坏情况）。底部那条虚线是官方底部 HUD 堆叠的最高点——交互读条
    ActionProgress_Slider 的顶边，由 HUD 常量 OfficialBottomStackTop 给出（UnityPy 读官方预制体换算），
    只用来看字幕有没有压到它。撤离读条是官方 EvacuationCountdownUI，这里不画，不替官方控件编样子。
    """
    H = {n: hud_const(n) for n in ('CardWidth', 'CardRight', 'CardTop', 'CardPadX', 'CardPadY',
                                   'AccentBarWidth', 'TitleFont', 'BodyFont', 'ChipFont',
                                   'AreaTitleY', 'AreaTitleFont', 'AreaOverlineFont', 'AreaTitleSpacing',
                                   'AreaOverlineSpacing', 'CaptionY', 'CaptionWidth', 'CaptionFont',
                                   'CaptionMaxHeight', 'CaptionScrimPadding', 'OfficialBottomStackTop',
                                   'BannerHintY')}
    W, Ht = 1920, 1080
    backdrop = Image.open(ART / 'skyisland_scene_E.png').convert('RGBA')
    scale = max(W / backdrop.width, Ht / backdrop.height)
    backdrop = backdrop.resize((int(backdrop.width * scale), int(backdrop.height * scale)), Image.LANCZOS)
    img = Image.new('RGBA', (W, Ht), (12, 14, 18, 255))
    img.alpha_composite(backdrop, ((W - backdrop.width) // 2, (Ht - backdrop.height) // 2))
    draw = ImageDraw.Draw(img)

    # ---- 右侧卡片：目标刚更新，目标行上方挂着「目标更新」眉题 ----
    f_title, f_body, f_chip = font(int(H['TitleFont']), True), font(int(H['BodyFont'])), font(int(H['ChipFont']))
    f_over = font(max(9, int(round(H['BodyFont'] * 0.85))), True)
    rows = [('鸣风栈道', f_title, COL['Accent'], H['TitleFont'], None),
            ('双航标已亮 · 经鸣风栈道前往归航钟庭 · 和解或战胜钟守 · 栈道上可挑战「噬风」',
             f_body, COL['TextSecondary'], H['BodyFont'], '目标更新'),
            ('物资 27/39 · 委托 清理航路威胁 2/3', f_chip, COL['TextSecondary'], H['ChipFont'], None)]
    inner = H['CardWidth'] - H['CardPadX'] * 2 - H['AccentBarWidth'] - 6
    laid, y = [], H['CardPadY']
    for text, fnt, col, size, overline in rows:
        lines = wrap(draw, text, fnt, inner)
        h = max(size * 1.3, len(lines) * size * 1.3 + 2) + (size * 1.3 if overline else 0)
        laid.append((lines, fnt, col, size, y, overline))
        y += h + 4
    card_h = y + H['CardPadY']
    x0 = W + H['CardRight'] - H['CardWidth']
    y0 = -H['CardTop']
    rounded(img, (x0, y0, x0 + H['CardWidth'], y0 + card_h), 10, COL['Surface'][:3] + (240,))
    # 卡片描边：这张卡没有 Backdrop 垫底，直接压在场景上。暗地形下卡底对背景只有 1.50:1，
    # 没有这一圈就没有轮廓（生产走 BossRushUI.ApplyPanelStroke，色用 BossRushUIColors.Stroke）。
    stroke(img, (x0, y0, x0 + H['CardWidth'], y0 + card_h), 10, COL['Stroke'])
    draw = ImageDraw.Draw(img)
    bx = x0 + H['CardPadX']
    draw.rounded_rectangle((bx, y0 + 8, bx + H['AccentBarWidth'], y0 + card_h - 8), radius=2,
                           fill=COL['Accent'])
    text_x = x0 + H['CardPadX'] + H['AccentBarWidth'] + 6
    for lines, fnt, col, size, top, overline in laid:
        ty = y0 + top
        if overline:
            draw.text((text_x, ty), overline, font=f_over, fill=COL['Accent'])
            ty += size * 1.3
        for line in lines:
            draw.text((text_x, ty), line, font=fnt, fill=col)
            ty += size * 1.3

    # ---- 区域大标题（落地那一次）----
    # Unity 的 anchoredPosition y 向上为正，PIL 的 y 向下为正 —— 必须减不能加，
    # 早先写成加号，把「中线偏下」画成了中线偏上（预览工具自己的 bug）。
    ay = Ht / 2 - H['AreaTitleY']
    img.alpha_composite(soft_scrim(1180, 230), (int((W - 1180) / 2), int(ay - 115)))
    draw = ImageDraw.Draw(img)
    # 偏移照生产 BuildBanner 的 CenteredText：眉题 +44、标题 +10、细线 -24、提示 -42（相对标题根节点中心）。
    f_line, f_area, f_hint = font(int(H['AreaOverlineFont'])), font(int(H['AreaTitleFont']), True), font(15)
    spaced(draw, W / 2, ay - 44 - H['AreaOverlineFont'] * 0.7, '晴岚群岛', f_line, COL['TextSecondary'],
           H['AreaOverlineSpacing'])
    spaced(draw, W / 2, ay - 10 - H['AreaTitleFont'] * 0.7, '鸣风栈道', f_area, COL['TextPrimary'],
           H['AreaTitleSpacing'])
    # 细横线：现在铺图集的 divider（8×8 / border 2），高度 8、亮带约 2px；
    # 旧写法是 120×1 的裸 Image，非整数画布缩放下会被采样吃掉，示意图里也基本看不见。
    _rw = hud_const('BannerRuleWidth')
    _rh = hud_const('BannerRuleHeight')
    draw.rectangle(((W - _rw) / 2, ay + 24 + _rh * 0.375, (W + _rw) / 2, ay + 24 + _rh * 0.625),
                   fill=COL['Divider'][:3] + (int(COL['Divider'][3] * 1.6),))
    hint = '地图键查阅全岛 · 站进撤离环停留 3 秒返航'
    w = draw.textlength(hint, font=f_hint)
    draw.text(((W - w) / 2, ay - H['BannerHintY'] - 10), hint, font=f_hint, fill=COL['TextSecondary'])

    # ---- 中下方字幕（警示色那一类：Boss 机制提示，最坏情况两行）----
    # 生产里字幕根的轴心在**底边**（CaptionY 是底边相对屏幕中心的 y），文字向上长，封顶 CaptionMaxHeight。
    cap_bottom = Ht / 2 - H['CaptionY']
    caption = '噬风收拢了风眼 —— 离开它脚下的那一圈。附近还有威胁 —— 先把这一段航路清干净，再静下心来。'
    f_cap = font(int(H['CaptionFont']))
    cap_lines = wrap(draw, caption, f_cap, H['CaptionWidth'])[:2]
    line_h = H['CaptionFont'] * 1.3
    cap_h = min(H['CaptionMaxHeight'], max(H['CaptionFont'] * 1.6, len(cap_lines) * line_h + 8))
    cy = cap_bottom - cap_h / 2
    # 与生产 ScrimHeightFor 同一条算式：文字必须整个落在压暗底的平台上。
    _plat = max(0.05, 1.0 - 2.0 * art_const('ScrimEdge'))
    shade_w = H['CaptionWidth'] + 180
    shade_h = max(cap_h + H['CaptionScrimPadding'], cap_h / _plat)
    img.alpha_composite(soft_scrim(shade_w, shade_h), (int((W - shade_w) / 2), int(cy - shade_h / 2)))
    draw = ImageDraw.Draw(img)
    ty = cy - len(cap_lines) * line_h / 2 - 2
    for line in cap_lines:
        w = draw.textlength(line, font=f_cap)
        draw.text(((W - w) / 2, ty), line, font=f_cap, fill=COL['WarningText'])
        ty += line_h

    # ---- 官方底部 HUD 堆叠的最高点（交互读条顶边，OfficialBottomStackTop，UnityPy 读预制体换算）----
    stack_y = Ht - H['OfficialBottomStackTop']
    for x in range(324, 1540, 18):
        draw.line((x, stack_y, x + 9, stack_y), fill=(170, 176, 182, 255))
    draw.text((324, stack_y + 6), '官方底部 HUD 最高点（交互读条顶边）', font=font(12), fill=(170, 176, 182, 255))

    HUD_OUT.parent.mkdir(parents=True, exist_ok=True)
    img.convert('RGB').save(HUD_OUT, quality=95)
    print('hud preview -> %s (%dx%d)' % (HUD_OUT, W, Ht))


STORY_CS = ROOT / 'DebugAndTools/SkyIsland/SkyIslandWorldStory.cs'
PAIR_RE = r'L10n\.T\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\)'
EN_OUT = ROOT / 'output/sky_island_panel_preview_en.png'
SHEET_OUT = ROOT / 'output/sky_island_art_contact_sheet.png'


def english_strings():
    """从生产源码里取**真实**的英文文案，不自己编。

    英文比中文长是这份预览要看的重点（同一句英文常宽出 1.6~2 倍），所以按长度挑最坏的几条：
    最长的当标题、次长的几条当选项、把几条拼起来当正文。它不是某一页的真实组合，
    但每一条都是真实存在的英文，长度是真实的最坏情况。"""
    import re as _re
    src = clean_source(STORY_CS.read_text(encoding='utf-8-sig'))
    en = [b.replace('\\n', ' ') for _, b in _re.findall(PAIR_RE, src)]
    en = [t for t in en if len(t) > 8 and '{' not in t]
    en.sort(key=len, reverse=True)
    title = next((t for t in en if 20 <= len(t) <= 70), en[0])
    body = '\n\n'.join(en[:2])
    choices = [t for t in en if 18 <= len(t) <= 90][:5]
    return title, body, choices


def render_sheet():
    """13 张区域横幅 + 6 张居民立绘**逐张**放进面板里看一遍。

    横幅单看是好看的，放进面板才知道：顶边会不会被标题压住、底部渐隐有没有吃掉主体、
    与面板底色接不接得上。立绘同理——512×512 抠图缩到 160 见方之后还认不认得出是谁。"""
    banners = sorted(x.stem.replace('skyisland_scene_', '')
                     for x in ART.glob('skyisland_scene_*.png'))
    portraits = sorted(x.stem for x in ART.glob('skyisland_portrait_*.png'))
    tiles = []
    for region in banners:
        tiles.append(render('晴岚群岛 · 区域 ' + region,
                            '这一页用来逐张目检横幅：主体有没有被标题压住、底部渐隐有没有吃掉画面、'
                            '色调与面板底接不接得上。',
                            ['收录见闻 / 物证'],
                            banner_name='skyisland_scene_' + region))
    for name in portraits:
        tiles.append(render('晴岚群岛 · ' + name.replace('skyisland_portrait_sky_', ''),
                            '立绘目检：512 见方抠图缩到 160 之后还认不认得出是谁，抠图边缘有没有脏点；'
                            '立绘压在区域插图上时，抠图边缘会不会和插图糊在一起。',
                            ['聊聊航路'], banner_name='skyisland_scene_D', portrait_name=name))
    cols = 4
    scale = 0.5
    tiles = [t.resize((int(t.width * scale), int(t.height * scale)), Image.LANCZOS) for t in tiles]
    cw = max(t.width for t in tiles) + 16
    rows = (len(tiles) + cols - 1) // cols
    rh = [max(t.height for t in tiles[r * cols:(r + 1) * cols]) + 16 for r in range(rows)]
    sheet = Image.new('RGBA', (cw * cols, sum(rh)), (18, 22, 28, 255))
    y = 0
    for r in range(rows):
        for c, tile in enumerate(tiles[r * cols:(r + 1) * cols]):
            sheet.alpha_composite(tile, (c * cw + 8, y + 8))
        y += rh[r]
    SHEET_OUT.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert('RGB').save(SHEET_OUT, quality=92)
    print('art contact sheet -> %s (%dx%d, %d 张)'
          % (SHEET_OUT, sheet.width, sheet.height, len(tiles)))


def main():
    render_hud()
    # 装置面板：正文只剩一句导语（长文去了官方笔记图鉴，目标卡常驻右上角 HUD），
    # 选项按剧情进度开启——新档这一页只有「收录」与已经能接的委托。
    device = render(
        '风铃集留言板 · 种植记录与航务委托',
        '留言板上钉着三张纸，谁都可以揭一张。',
        ['收录见闻 / 物证', '接委托 · 清理航路威胁 ×3', '接委托 · 打捞物资 ×4'],
        banner_name='skyisland_scene_B')
    # 居民面板：立绘 + 他家那一区的插图（眠苔站在 POI_D，悬根林）。
    # 台词已经由**官方对话**一句一屏说完了，这里只剩功能项，正文空着。
    resident = render(
        '晴岚群岛 · 眠苔',
        '',
        ['请眠苔敷一副苔药', '眠苔的药臼'],
        banner_name='skyisland_scene_D',
        portrait_name='skyisland_portrait_sky_miantai')

    # 第三种形态：完成纪念物（0 个选项），顺带看一张支路横幅。
    # ⚠️ 零选项 = 去掉页脚之后**只有 ESC 能关**（W/S/Enter 全部早退），右上角那个键帽就是提示。
    # 正文末尾**不再**跟一句「群岛记录已同步」：那是存档系统的内部诊断，
    # 生产侧已经由 `SkyIslandStoryService.SaveProblem` 在一切正常时返回 null 收掉了。
    memorial = render(
        '星图重新连接',
        '修复悬根林风标与残星工坊星灯，让双航标门重新工作。\n'
        '支线：种植记录 ✓ 旧信 ○ 航路图 ✓ 观星镜 ✓',
        [],
        banner_name='skyisland_scene_S4')

    gap = 28
    W = device.width + resident.width + gap
    H = max(device.height, resident.height + gap + memorial.height)
    sheet = Image.new('RGBA', (W, H), (18, 22, 28, 255))
    sheet.alpha_composite(device, (0, 0))
    sheet.alpha_composite(resident, (device.width + gap, 0))
    sheet.alpha_composite(memorial, (device.width + gap, resident.height + gap))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert('RGB').save(OUT, quality=95)
    print('preview -> %s (%dx%d)' % (OUT, sheet.width, sheet.height))

    # 群岛手记：全链条里文本最长、此前唯一既无立绘也无插图的一页，现在有自己的横幅。
    # 手记首页：6 项平铺 -> 2 个入口，正文换成一句导语。
    # 20 处见闻整块搬进了官方笔记图鉴，不在这里再列 `□ …（尚未收录）`。
    journal = render(
        '群岛手记',
        '手记记了 12 页，还空着 8 页。',
        ['来信与人', '岛上的事'],
        banner_name='skyisland_scene_journal')
    JOURNAL_OUT = ROOT / 'output/sky_island_journal_preview.png'
    journal.convert('RGB').save(JOURNAL_OUT, quality=95)
    print('journal -> %s (%dx%d)' % (JOURNAL_OUT, journal.width, journal.height))

    # 英文最坏情况：文案全部取自生产源码里真实存在的 L10n 英文，按长度挑最长的几条。
    en_title, en_body, en_choices = english_strings()
    en_device = render(en_title, en_body, en_choices, banner_name='skyisland_scene_B')
    en_resident = render(en_title, en_body, en_choices[:1],
                         banner_name='skyisland_scene_D',
                         portrait_name='skyisland_portrait_sky_miantai')
    gap = 28
    en_sheet = Image.new('RGBA',
                         (en_device.width + en_resident.width + gap,
                          max(en_device.height, en_resident.height)), (18, 22, 28, 255))
    en_sheet.alpha_composite(en_device, (0, 0))
    en_sheet.alpha_composite(en_resident, (en_device.width + gap, 0))
    en_sheet.convert('RGB').save(EN_OUT, quality=95)
    print('english preview -> %s (%dx%d)' % (EN_OUT, en_sheet.width, en_sheet.height))

    if '--sheet' in sys.argv:
        render_sheet()


if __name__ == '__main__':
    main()
