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


C = {n: const(n) for n in ('PanelWidth', 'Pad', 'Gap', 'BannerMaxHeight', 'BannerMinHeight',
                           'PortraitSize', 'TitleMinHeight', 'BodyMinHeight', 'BodyPreferredMax',
                           'ChoiceMinHeight', 'ChoicePadY', 'ChoicePadX', 'FooterHeight',
                           'ScrollbarGutter')}
COL = {n: color(n) for n in ('Surface', 'SurfaceRaised', 'Divider', 'TextPrimary',
                             'TextSecondary', 'Accent', 'TextOnAccent', 'Backdrop')}
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


def render(title, body, choices, banner_name=None, portrait_name=None):
    scratch = Image.new('RGBA', (10, 10))
    d0 = ImageDraw.Draw(scratch)
    f_title, f_body, f_choice = font(31, True), font(20), font(21)

    title_w = CONTENT_W - C['PortraitSize'] - C['Gap'] if portrait_name else CONTENT_W
    title_lines = wrap(d0, title, f_title, title_w)
    title_h = max(C['TitleMinHeight'], len(title_lines) * 31 * 1.25 + 4)
    header_h = max(C['PortraitSize'], title_h) if portrait_name else title_h

    banner = None
    banner_h = 0.0
    if banner_name and (ART / (banner_name + '.png')).exists():
        banner = Image.open(ART / (banner_name + '.png')).convert('RGBA')
        banner_h = min(max(CONTENT_W / (banner.width / banner.height),
                           C['BannerMinHeight']), C['BannerMaxHeight'])

    choice_lines, choice_hs = [], []
    for label in choices:
        ls = wrap(d0, label, f_choice, CONTENT_W - C['ChoicePadX'] * 2)
        choice_lines.append(ls)
        choice_hs.append(max(C['ChoiceMinHeight'], len(ls) * 21 * 1.25 + 4 + C['ChoicePadY'] * 2))
    choices_h = sum(choice_hs) + max(0, len(choice_hs) - 1) * C['Gap'] * 0.5

    body_lines = wrap(d0, body, f_body, CONTENT_W - C['ScrollbarGutter'])
    body_h = min(C['BodyPreferredMax'], max(C['BodyMinHeight'], len(body_lines) * 20 * 1.25 + 4))

    divider_block = 1 + C['Gap']
    panel_h = (C['Pad'] * 2 + header_h + divider_block + choices_h + C['FooterHeight']
               + C['Gap'] * 3 + banner_h + (C['Gap'] if banner_h else 0) + body_h)

    W, H = int(C['PanelWidth']) + 120, int(panel_h) + 120
    img = Image.new('RGBA', (W, H), (18, 22, 28, 255))
    draw = ImageDraw.Draw(img)
    px, py = 60, 60
    rounded(img, (px, py, px + C['PanelWidth'], py + panel_h), 18, COL['Surface'][:3] + (245,))
    draw = ImageDraw.Draw(img)

    y = py + C['Pad']
    if banner:
        scaled = banner.resize((int(CONTENT_W), int(banner_h)), Image.LANCZOS)
        img.alpha_composite(scaled, (int(px + C['Pad']), int(y)))
        y += banner_h + C['Gap']

    tx = px + C['Pad']
    if portrait_name and (ART / (portrait_name + '.png')).exists():
        face = Image.open(ART / (portrait_name + '.png')).convert('RGBA')
        size = int(C['PortraitSize'])
        rounded(img, (tx, y, tx + size, y + size), 12, COL['SurfaceRaised'][:3] + (250,))
        img.alpha_composite(face.resize((size - 10, size - 10), Image.LANCZOS), (int(tx + 5), int(y + 5)))
        draw = ImageDraw.Draw(img)
        tx += C['PortraitSize'] + C['Gap']
    ty = y + (header_h - len(title_lines) * 31 * 1.25) / 2
    for line in title_lines:
        w = draw.textlength(line, font=f_title)
        cx = tx if portrait_name else px + C['Pad'] + (CONTENT_W - w) / 2
        draw.text((cx, ty), line, font=f_title, fill=COL['TextPrimary'])
        ty += 31 * 1.25
    y += header_h + C['Gap']

    draw.rectangle((px + C['Pad'], y, px + C['Pad'] + CONTENT_W, y + 1), fill=COL['Divider'])
    y += divider_block

    by = y
    for line in body_lines:
        if by + 20 * 1.25 > y + body_h:
            draw.text((px + C['Pad'], by), '…', font=f_body, fill=COL['TextSecondary'])
            break
        draw.text((px + C['Pad'], by), line, font=f_body, fill=COL['TextSecondary'])
        by += 20 * 1.25
    y += body_h + C['Gap']

    for ls, h in zip(choice_lines, choice_hs):
        rounded(img, (px + C['Pad'], y, px + C['Pad'] + CONTENT_W, y + h), 10,
                COL['SurfaceRaised'][:3] + (250,))
        draw = ImageDraw.Draw(img)
        cy = y + (h - len(ls) * 21 * 1.25) / 2
        for line in ls:
            draw.text((px + C['Pad'] + C['ChoicePadX'], cy), line, font=f_choice,
                      fill=COL['TextPrimary'])
            cy += 21 * 1.25
        y += h + C['Gap'] * 0.5

    fy = py + panel_h - C['Pad'] - C['FooterHeight']
    rounded(img, (px + C['Pad'], fy, px + C['Pad'] + CONTENT_W, fy + C['FooterHeight']), 10,
            COL['Accent'][:3] + (255,))
    draw = ImageDraw.Draw(img)
    label = '继续旅程 · ESC'
    w = draw.textlength(label, font=f_choice)
    draw.text((px + C['Pad'] + (CONTENT_W - w) / 2, fy + (C['FooterHeight'] - 21 * 1.25) / 2),
              label, font=f_choice, fill=COL['TextOnAccent'])
    return img


HUD_CS = ROOT / 'DebugAndTools/SkyIsland/SkyIslandHud.cs'
HUD_OUT = ROOT / 'output/sky_island_hud_preview.png'


def hud_const(name):
    src = clean_source(HUD_CS.read_text(encoding='utf-8-sig'))
    m = re.search(r'\b' + name + r'\s*=\s*(-?[0-9.]+)f', src)
    if not m:
        raise AssertionError('读不到 HUD 常量 ' + name)
    return float(m.group(1))


def render_hud():
    """1920x1080 参考分辨率下的 HUD 占位示意，铺一张真实岛景当底，好判断对比度与遮挡。"""
    H = {n: hud_const(n) for n in ('CardWidth', 'CardRight', 'CardTop', 'CardPadX', 'CardPadY',
                                   'AccentBarWidth', 'TitleFont', 'BodyFont', 'ChipFont')}
    W, Ht = 1920, 1080
    backdrop = Image.open(ART / 'skyisland_scene_E.png').convert('RGBA')
    scale = max(W / backdrop.width, Ht / backdrop.height)
    backdrop = backdrop.resize((int(backdrop.width * scale), int(backdrop.height * scale)), Image.LANCZOS)
    img = Image.new('RGBA', (W, Ht), (12, 14, 18, 255))
    img.alpha_composite(backdrop, ((W - backdrop.width) // 2, (Ht - backdrop.height) // 2))
    draw = ImageDraw.Draw(img)

    f_title, f_body, f_chip = font(int(H['TitleFont']), True), font(int(H['BodyFont'])), font(int(H['ChipFont']))
    rows = [('晴岚群岛 · 鸣风栈道', f_title, COL['Accent'], H['TitleFont']),
            ('修复悬根林风标与残星工坊星灯，让双航标门重新工作。', f_body, COL['TextSecondary'], H['BodyFont']),
            ('物资 27/39 · 委托 清理航路威胁 2/3', f_chip, COL['TextSecondary'], H['ChipFont'])]
    inner = H['CardWidth'] - H['CardPadX'] * 2 - H['AccentBarWidth'] - 6
    laid, y = [], H['CardPadY']
    for text, fnt, col, size in rows:
        lines = wrap(draw, text, fnt, inner)
        h = max(size * 1.3, len(lines) * size * 1.3 + 2)
        laid.append((lines, fnt, col, size, y))
        y += h + 4
    card_h = y + H['CardPadY']
    x0 = W + H['CardRight'] - H['CardWidth']
    y0 = -H['CardTop']
    rounded(img, (x0, y0, x0 + H['CardWidth'], y0 + card_h), 10, COL['Surface'][:3] + (240,))
    draw = ImageDraw.Draw(img)
    bx = x0 + H['CardPadX']
    draw.rounded_rectangle((bx, y0 + 8, bx + H['AccentBarWidth'], y0 + card_h - 8), radius=2,
                           fill=COL['Accent'])
    for lines, fnt, col, size, top in laid:
        ty = y0 + top
        for line in lines:
            draw.text((x0 + H['CardPadX'] + H['AccentBarWidth'] + 6, ty), line, font=fnt, fill=col)
            ty += size * 1.3

    # 区域大标题：中线偏下的一次性淡入淡出（这里画的是它完全显形的那一瞬）。
    # Unity 的 anchoredPosition y 向上为正，PIL 的 y 向下为正 —— 必须减不能加，
    # 早先写成加号，把「中线偏下」画成了中线偏上（预览工具自己的 bug）。
    ay = Ht / 2 - hud_const('AreaTitleY')
    # 压暗底：与生产同一条上下对称渐变，否则预览会高估浅色字在云海上的可读性。
    # 与生产同一条二维柔边：竖向余弦钟形 × 横向两侧 30% smoothstep。
    # 只做竖向的话左右两侧是笔直硬边（上一版预览里 x≈370 / x≈1550 那两条竖线就是它）。
    import math as _m
    scrim = Image.new('RGBA', (1180, 200), (0, 0, 0, 0))
    px_ = scrim.load()
    for xx in range(1180):
        u = xx / 1179.0
        a = min(1.0, u / 0.30)
        b = min(1.0, (1.0 - u) / 0.30)
        horizontal = (a * a * (3 - 2 * a)) * (b * b * (3 - 2 * b))
        for yy in range(200):
            vertical = 0.5 - 0.5 * _m.cos(yy / 199.0 * _m.pi * 2)
            px_[xx, yy] = (5, 8, 10, int(horizontal * vertical * 0.60 * 255))
    img.alpha_composite(scrim, (int((W - 1180) / 2), int(ay - 100)))
    draw = ImageDraw.Draw(img)
    # 偏移照生产的锚点：标题中心在压暗核心上方 26、横线 -10、提示 -28，
    # 三者都落在 scrim alpha >= 0.45 的中心带里。早先预览把提示画到 +52，
    # 那已经出了压暗核心，于是「看不清」是预览自己造出来的假象。
    f_area, f_hint = font(44, True), font(15)
    area = '鸣风栈道'
    w = draw.textlength(area, font=f_area)
    draw.text(((W - w) / 2, ay - 26 - 22), area, font=f_area, fill=COL['TextPrimary'])
    draw.rectangle(((W - 120) / 2, ay + 10, (W + 120) / 2, ay + 11), fill=COL['Divider'])
    hint = '地图键查阅全岛 · 站进撤离环停留 3 秒返航'
    w = draw.textlength(hint, font=f_hint)
    draw.text(((W - w) / 2, ay + 28 - 8), hint, font=f_hint, fill=COL['TextSecondary'])

    HUD_OUT.parent.mkdir(parents=True, exist_ok=True)
    img.convert('RGB').save(HUD_OUT, quality=95)
    print('hud preview -> %s (%dx%d)' % (HUD_OUT, W, Ht))


def main():
    render_hud()
    device = render(
        '风铃集留言板 · 种植记录与航务委托',
        '留言板上钉着三张纸：苇白在找修复两端航标的帮手，晴禾在找落在蛙鸣池的种植记录，'
        '还有一张空白的委托单，谁都可以揭。即使主人离岛，留言也能送到。\n\n'
        '修复悬根林风标与残星工坊星灯，让双航标门重新工作。',
        ['收录见闻 / 物证', '把种植记录留给晴禾', '接委托 · 清理航路威胁 ×3',
         '接委托 · 打捞物资 ×4', '接委托 · 巡视群岛区域 ×4'],
        banner_name='skyisland_scene_B')
    resident = render(
        '晴岚群岛 · 眠苔',
        '眠苔：风标困在根环那头，先清掉附近的威胁再校准。倒挂邮亭还吊着一封信——'
        '风没有把它送到，或许你可以。',
        ['请眠苔敷一副苔药'],
        portrait_name='skyisland_portrait_sky_miantai')

    # 第三种形态：完成纪念物（0 个选项），顺带看一张支路横幅。
    memorial = render(
        '星图重新连接',
        '修复悬根林风标与残星工坊星灯，让双航标门重新工作。\n'
        '支线：种植记录 ✓ 旧信 ○ 航路图 ✓ 观星镜 ✓\n'
        '群岛记录已同步',
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


if __name__ == '__main__':
    main()
