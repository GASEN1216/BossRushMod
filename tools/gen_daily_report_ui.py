# -*- coding: utf-8 -*-
"""《鸭科夫日报》面板底图、图标与版面表。

owner 2026-09-20：「每日日报你能否生图弄一个和这个一样但是去掉文字的版本，
我想要把日报弄成和这个 100% 都一样的效果，而不是现在那样好丑都是文字。」
2026-09-22 / 09-23 又两轮：「总体差不多但还是有差距」。参考图 docs/reference/images/日报UI参考图.png。

为什么不是「直接让模型出一整张底图」：
  文字要落进卡片里，就必须知道每张卡片的**精确矩形**。AI 出的图每次构图都不一样，
  事后去检测边框又脆。所以版面由本脚本定义，底图按这份版面**程序化合成**
  （纸面、卡片投影、斜切缎带、分隔线），同一份版面同时写出 Assets/Data/DailyReportLayout.json，
  C# 直接读它摆文字与图标，于是底图、图标与文字**天生对齐**。

2026-09-23 第五轮（向参考图靠拢，去「盒子套盒子、灰方块、平涂」）：
  - 卡片不再描边，靠柔和投影 + 留白从纸面上浮起来；报头不再是一张卡，报名直接压在纸上，
    右侧期数 / 天气合成一张信息卡，中间一条细竖线；卡片里不再套任何带框的小盒子。
  - 三条标题带改成参考图的「斜切缎带」：整条浅色底带 + 左端深色斜切色块（横向渐变），
    三条三种颜色（收益墨绿 / 状态炭灰 / 签到暖棕）。
  - 纸面加了极淡的纸纹与四周暗角（只在纸上，卡片内部保持纯色——签到格、按钮、图例是运行时画的，
    daily_report_art_contract 要求这些位置的底图像素与卡片底色**逐字节相同**）。
  - **图标和吉祥物不再烤进底图**：底图进包时被压到 1024 宽（贴图策略上限），再被 UI 放大 1.3–1.7 倍，
    烤进去的 34px 小图标只剩二十几个纹素，糊成「灰方块」。现在它们是各自 256px 的独立 Sprite
    （production_icons 包，按原 PNG 路径借用），运行时按版面表的 "icons" 矩形摆放，放大也清楚。
  - 图标走 AI 插画管线（images.edit + 参考图风格，--icons）；网关不可用时 --procedural-icons
    画「浅色圆底 + 彩色图形」兜底，两者都写进 output/daily_report/icons_manifest.json 的 source 字段。

产物：
  Assets/ui/DailyReport/daily_report_bg.png     面板底图（无文字、无图标）
  Assets/ui/DailyReport/dr_mascot.png           报头吉祥物（256，透明底）
  Assets/ui/DailyReport/dr_icon_<id>.png        图标（256，透明底）
  Assets/Data/DailyReportLayout.json            版面表（C# 读）

用法（生图需要网络出口；密钥只从环境变量读，来源见 docs/AI生图API和密钥.md）：
    python -X utf8 tools/gen_daily_report_ui.py                      # 合成底图 + 吉祥物 + 版面表（不联网）
    python -X utf8 tools/gen_daily_report_ui.py --icons              # 补齐缺的 AI 图标（断点续跑）
    python -X utf8 tools/gen_daily_report_ui.py --icons --only income,fortune   # 先试两张看风格
    python -X utf8 tools/gen_daily_report_ui.py --icons --force --only gift     # 重出某张（连 raw 一起重出）
    python -X utf8 tools/gen_daily_report_ui.py --procedural-icons   # 不联网：缺的图标用程序化徽章补
    python -X utf8 tools/gen_daily_report_ui.py --mascot             # 重出吉祥物原图（联网）
"""
import argparse
import base64
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.request

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_codex_art as base  # noqa: E402
from imagegen_model import IMAGE_MODEL  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART_DIR = os.path.join(ROOT, "Assets", "ui", "DailyReport")
OUT_IMAGE = os.path.join(ART_DIR, "daily_report_bg.png")
OUT_MASCOT = os.path.join(ART_DIR, "dr_mascot.png")
OUT_LAYOUT = os.path.join(ROOT, "Assets", "Data", "DailyReportLayout.json")
WORK = os.path.join(ROOT, "output", "daily_report")
MASCOT = os.path.join(WORK, "mascot.png")                 # AI 原图（512，透明底，2026-09-20 出）
ICON_RAW = os.path.join(WORK, "icons_raw")                # AI 原图缓存：存在就不再调网关（断点续跑）
ICON_MANIFEST = os.path.join(WORK, "icons_manifest.json")
STYLE_REF = os.path.join(WORK, "style_ref.png")
REFERENCE = os.path.join(ROOT, "docs", "reference", "images", "日报UI参考图.png")

# 面板尺寸与 Integration/DailyReport/DailyReportUI.cs 的 PanelWidth / PanelHeight 一致
W, H = 1333, 1013
ICON_PX = 256          # 图标 / 吉祥物 PNG 边长；production_icon_manifest 里 maxSize 同为 256
SCALE = 3              # 矢量部分先按 3 倍画再缩，圆角与斜边才不毛糙

# ---------------------------------------------------------------- 纸面配色（取自参考图）
PAPER = (236, 227, 209, 255)          # 纸面：比卡片深一档，卡片靠明度差 + 投影浮起来
PAPER_EDGE = (178, 160, 132, 255)     # 纸面外沿一道细线（参考图的纸边）
CARD = (250, 246, 237, 255)           # 卡片：参考图卡片的暖白
SHADOW = (84, 58, 30)                 # 投影色（暖棕，不用纯黑，免得发灰）
BAND = (233, 225, 210, 255)           # 标题缎带的浅色底带
TIP_STRIP = (247, 233, 202, 255)      # 悬赏提示条（只填色，不描边）
DIVIDER = (224, 212, 191, 255)        # 卡片内分隔细线
RULE = (150, 134, 112, 255)           # 报名两侧「— DUCK NEWS —」的横线
# 三条缎带 = 左端深色 → 斜切处略浅的横向渐变。标题字色 PillInk（DailyReportUI.cs）对**两端**
# 都要 ≥ 4.5:1，DailyReportPresentationGuard 从这里读色值复算。
RIBBON_INCOME = ((52, 82, 56, 255), (86, 110, 82, 255))     # 今日收益：墨绿
RIBBON_STATUS = ((48, 52, 51, 255), (92, 98, 95, 255))      # 今日状态：炭灰
RIBBON_SIGNIN = ((100, 70, 34, 255), (126, 94, 52, 255))    # 今日签到：暖棕（与签到按钮同一色系）
# 签到格 / 签到按钮 / 图例色块的颜色**只在 C# 里**（DailyReportUI.cs 的 CellEmpty…
# ButtonIdle…），本脚本不画它们，因此这里也不留第二份色值（见 compose 里的长注释）。

# 图标徽章（浅色圆底，参考图口径）与程序化兜底用的色
BADGE_DISC = (238, 232, 221)
BADGE_RIM = (222, 212, 196)
GLYPH_DARK = (59, 50, 44, 255)
GLYPH_LIGHT = (248, 243, 233, 255)
FALLBACK_GREEN = (62, 122, 76, 255)
FALLBACK_GOLD = (190, 142, 56, 255)
FALLBACK_BROWN = (132, 92, 44, 255)
FALLBACK_SKY = (126, 170, 205, 255)


def rr(draw, box, radius, fill=None, outline=None, width=2):
    """box 一律是 (x, y, w, h)，与版面表同一约定，避免两套坐标混用。"""
    x, y, w, h = box
    draw.rounded_rectangle([x, y, x + w - 1, y + h - 1],
                           radius=radius, fill=fill, outline=outline, width=width)


# ================================================================ 版面
def layout():
    """版面表：所有矩形都是 [x, y, w, h]，左上原点，像素单位，与底图同一坐标系。"""
    pad = 22

    # ---- 报头：不再是一张卡。吉祥物 + 大号报名直接压在纸上，右侧一张信息卡
    header = [pad, 14, W - pad * 2, 154]
    mascot = [26, 6, 166, 166]
    title = [200, 18, 460, 144]            # C# 上 70% 写报名（居中），下段写「DUCK NEWS」
    info = [676, 30, W - pad - 676, 126]
    info_meta = [info[0] + 12, info[1] + 5, 330, info[3] - 10]
    info_weather = [info_meta[0] + info_meta[2] + 24, info[1] + 5,
                    info[0] + info[2] - (info_meta[0] + info_meta[2] + 24) - 12, info[3] - 10]

    # ---- 第二行：今日收益 / 今日状态
    row2_y = header[1] + header[3] + 14
    # 去掉底部关闭按钮后，将 60 px 让给悬赏全文；签到卡仍留在纸面内。
    row2_h = 464
    income = [pad, row2_y, 766, row2_h]
    status = [income[0] + income[2] + 16, row2_y, W - pad - (income[0] + income[2] + 16), row2_h]

    rib_h = 46
    rib_dy = 18                            # 缎带离卡片上沿；签到卡的取色基准点在上沿 +12，缎带不能碰到
    income_pill = [income[0] + 16, income[1] + rib_dy, 280, rib_h]
    status_pill = [status[0] + 16, status[1] + rib_dy, 336, rib_h]

    body_y = income_pill[1] + rib_h + 16
    income_left = [income[0] + 24, body_y, 351, 136]
    income_right = [income_left[0] + income_left[2] + 16, body_y, 351, 136]
    income_tip = [income[0] + 24, body_y + 136 + 12, income[2] - 48, 116]
    income_note = [income[0] + 24, income_tip[1] + income_tip[3] + 14, income[2] - 48,
                   income[1] + income[3] - 16 - (income_tip[1] + income_tip[3] + 14)]

    # 今日状态：四行「徽章 + 正文」，行间一条细线（参考图是图标 + 文字直接排在卡片上）
    row_h, row_gap = 85, 9
    status_rows = [[status[0] + 20, body_y + i * (row_h + row_gap), status[2] - 40, row_h] for i in range(4)]

    # ---- 第三行：签到墙 10 × 3 + 右侧按钮块 + 底部图例
    row3_y = income[1] + income[3] + 14
    signin_pill = [pad + 16, row3_y + rib_dy, 256, rib_h]
    grid_x = pad + 26
    grid_y = signin_pill[1] + rib_h + 18
    cell_w, cell_h, cell_gap = 62, 52, 10
    grid_w = cell_w * 10 + cell_gap * 9
    grid_h = cell_h * 3 + cell_gap * 2
    side_x = grid_x + grid_w + 40
    side_w = W - pad - side_x - 24
    button = [side_x, grid_y, side_w, 62]
    side_text = [side_x, button[1] + button[3] + 14, side_w, grid_h - button[3] - 14]
    # 图例高 36：18 号中文一行约 26 px，加上下 margin 才放得下；30 的时候 TMP 会把整串清空，
    # 游戏里图例只剩色块、没有文字（2026-09-22 实测）。五项：已签到 / 未签到 / 今日可签 / 奖励格 / 奖励已领。
    legend_count = 5
    legend = [grid_x, grid_y + grid_h + 14, grid_w, 36]
    legend_swatch = 16
    legend_item_w = grid_w // legend_count
    signin = [pad, row3_y, W - pad * 2, legend[1] + legend[3] + 18 - row3_y]

    # ---- 图标（运行时 Sprite）。文字缩进由 C# 按「图标右沿 + 12」算，图标取不到就不缩进。
    def badge_in(box, size, dx=4):
        return [box[0] + dx, box[1] + (box[3] - size) // 2, size, size]

    # 期数块上 55% 两行（第 N 期 · 第 N 天 / 本期进度），下 45% 一行（距离下期）；C# 用同一个比例切
    split = int(info_meta[3] * 0.55)
    button_cx = button[0] + button[2] // 2
    icons = {
        "issue": [info_meta[0] + 4, info_meta[1] + (split - 36) // 2, 36, 36],
        "deadline": [info_meta[0] + 4, info_meta[1] + split + (info_meta[3] - split - 36) // 2, 36, 36],
        "weather": badge_in(info_weather, 58),
        "income": badge_in(income_left, 70),
        "bounty": badge_in(income_right, 70),
        "tip": badge_in(income_tip, 30, 14),
        "headline": badge_in(status_rows[0], 56, 2),
        "broadcast": badge_in(status_rows[1], 56, 2),
        "fortune": badge_in(status_rows[2], 56, 2),
        "gossip": badge_in(status_rows[3], 56, 2),
        # 缎带左端的图标略大于缎带、上沿探出 5（参考图的硬币堆就是这样压在缎带头上）
        "ribbon_income": [income_pill[0] + 6, income_pill[1] - 5, 54, 54],
        "ribbon_status": [status_pill[0] + 8, status_pill[1] - 3, 50, 50],
        "ribbon_signin": [signin_pill[0] + 9, signin_pill[1] - 1, 46, 46],
        # 签到按钮上的礼盒：按钮文字居中，礼盒压在文字左侧（C# 把文字右移同样的量）
        "gift": [button_cx - 98, button[1] + (button[3] - 36) // 2, 36, 36],
    }

    return {
        "schemaVersion": 2,
        "panel": [0, 0, W, H],
        "rects": {
            "header": header,
            "mascot": mascot,
            "title": title,
            "info": info,
            "infoMeta": info_meta,
            "infoWeather": info_weather,
            "income": income,
            "incomePill": income_pill,
            "incomeLeft": income_left,
            "incomeRight": income_right,
            "incomeTip": income_tip,
            "incomeNote": income_note,
            "status": status,
            "statusPill": status_pill,
            "statusLeft": status_rows[0],
            "statusRight": status_rows[1],
            "statusLuck": status_rows[2],
            "statusTaboo": status_rows[3],
            "signin": signin,
            "signinPill": signin_pill,
            "button": button,
            "sideText": side_text,
            "legend": legend,
        },
        "grid": {
            "x": grid_x, "y": grid_y,
            "cellWidth": cell_w, "cellHeight": cell_h, "gap": cell_gap,
            "columns": 10, "rows": 3,
        },
        "legend": {"swatch": legend_swatch, "itemWidth": legend_item_w, "count": legend_count},
        # pillTextIndent：缎带标题字相对缎带左沿的缩进（让开缎带头上的图标）；
        # textGap：图标右沿到文字的间距。
        "icon": {"pillTextIndent": 68, "textGap": 12},
        "icons": icons,
    }


# ================================================================ 底图合成
def _paper_texture(size, mask):
    """纸纹 + 暗角，只作用在纸面（卡片随后整块盖上去，内部仍是纯色）。确定性：固定种子。"""
    w, h = size
    rng = np.random.default_rng(20260923)
    fine = rng.normal(0.0, 1.0, (h, w)).astype(np.float32)
    coarse = Image.fromarray(np.clip(rng.normal(128, 40, (h // 8 + 1, w // 8 + 1)), 0, 255).astype(np.uint8), "L")
    coarse = np.asarray(coarse.resize((w, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(10)),
                        dtype=np.float32) - 128.0
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    nx = (xx - w / 2.0) / (w / 2.0)
    ny = (yy - h / 2.0) / (h / 2.0)
    vignette = np.clip((nx * nx * 0.55 + ny * ny * 0.75) - 0.25, 0, None) * 16.0
    delta = fine * 1.6 + coarse * 0.10 - vignette
    base_rgb = np.array(PAPER[:3], dtype=np.float32)
    rgb = np.clip(base_rgb[None, None, :] + delta[..., None] * np.array([1.0, 0.97, 0.9])[None, None, :], 0, 255)
    rgba = np.dstack([rgb, np.asarray(mask, dtype=np.float32)]).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def _shadow(size, boxes, radius, drop=6, blur=9, alpha=74):
    """卡片柔和投影：同形圆角矩形下移后高斯模糊，暖棕色。"""
    mask = Image.new("L", size, 0)
    draw = ImageDraw.Draw(mask)
    for x, y, w, h in boxes:
        draw.rounded_rectangle([x, y + drop, x + w - 1, y + h - 1 + drop], radius=radius, fill=alpha)
    mask = mask.filter(ImageFilter.GaussianBlur(blur))
    layer = Image.new("RGBA", size, SHADOW + (0,))
    layer.putalpha(mask)
    return layer


def _ribbon(image, box, band_box, colors, s):
    """斜切缎带：浅色底带 + 左端深色斜切色块（横向渐变）+ 色块顶边一道极淡的高光。"""
    draw = ImageDraw.Draw(image)
    rr(draw, [v * s for v in band_box], 10 * s, fill=BAND)
    x, y, w, h = [v * s for v in box]
    slant = 20 * s
    mask = Image.new("L", image.size, 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([x, y, x + w - slant, y + h - 1], radius=10 * s, fill=255)
    md.polygon([(x + w - slant - 12 * s, y), (x + w, y), (x + w - slant, y + h - 1),
                (x + w - slant - 12 * s, y + h - 1)], fill=255)
    c0, c1 = colors
    ramp = np.linspace(0.0, 1.0, w, dtype=np.float32) ** 1.6
    row = (np.array(c0[:3])[None, :] * (1 - ramp[:, None]) + np.array(c1[:3])[None, :] * ramp[:, None])
    grad = np.repeat(row[None, :, :], h, axis=0)
    # 纵向极轻的受光：顶部亮 6，底部暗 6，让色块有「面」而不是平涂
    light = np.linspace(6.0, -6.0, h, dtype=np.float32)[:, None, None]
    grad = np.clip(grad + light, 0, 255).astype(np.uint8)
    tile = Image.fromarray(grad, "RGB").convert("RGBA")
    layer = Image.new("RGBA", image.size, (0, 0, 0, 0))
    layer.paste(tile, (x, y))
    layer.putalpha(mask)
    image.alpha_composite(layer)
    # 顶边高光线
    hl = Image.new("RGBA", image.size, (0, 0, 0, 0))
    ImageDraw.Draw(hl).line([x + 10 * s, y + s, x + w - slant - 4 * s, y + s],
                            fill=(255, 255, 255, 34), width=s)
    image.alpha_composite(hl)


def compose(spec):
    r = spec["rects"]
    s = SCALE

    # 1) 纸面（1 倍画：纸纹要落在最终像素上，先画 3 倍再缩会被平均掉）
    paper_mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(paper_mask).rounded_rectangle([0, 0, W - 1, H - 1], radius=22, fill=255)
    image = _paper_texture((W, H), paper_mask)

    # 2) 卡片投影（不描边：卡片靠投影 + 明度差浮起来）
    cards = [r["info"], r["income"], r["status"], r["signin"]]
    image.alpha_composite(_shadow((W, H), cards, 18))

    # 3) 矢量层按 3 倍画：卡片、缎带、分隔线
    vec = Image.new("RGBA", (W * s, H * s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(vec)

    def S(box):
        return [box[0] * s, box[1] * s, box[2] * s, box[3] * s]

    # 纸边：一道细线
    rr(draw, [0, 0, W * s, H * s], 22 * s, outline=PAPER_EDGE, width=2 * s)
    for key in ("info", "income", "status", "signin"):
        rr(draw, S(r[key]), (16 if key == "info" else 18) * s, fill=CARD)

    def vline(x, y0, y1, color=DIVIDER, width=2):
        draw.line([x * s, y0 * s, x * s, y1 * s], fill=color, width=width * s)

    def hline(x0, x1, y, color=DIVIDER, width=2):
        draw.line([x0 * s, y * s, x1 * s, y * s], fill=color, width=width * s)

    # 报名下方「— DUCK NEWS —」两侧的横线：C# 把副标题居中在 title 矩形下段
    title = r["title"]
    cx = title[0] + title[2] // 2
    ty = title[1] + int(title[3] * 0.87)
    hline(title[0] + 18, cx - 116, ty, RULE, 2)
    hline(cx + 116, title[0] + title[2] - 18, ty, RULE, 2)

    # 信息卡：期数 | 天气，中间一条细竖线（不再是两个套在报头卡里的带框小盒子）
    meta, weather = r["infoMeta"], r["infoWeather"]
    vline((meta[0] + meta[2] + weather[0]) // 2, r["info"][1] + 18, r["info"][1] + r["info"][3] - 18)

    # 缎带（带整条浅底，所以按卡片宽度铺）
    for card_key, pill_key, colors in (("income", "incomePill", RIBBON_INCOME),
                                       ("status", "statusPill", RIBBON_STATUS),
                                       ("signin", "signinPill", RIBBON_SIGNIN)):
        card, pill = r[card_key], r[pill_key]
        band = [pill[0], pill[1], card[0] + card[2] - 16 - pill[0], pill[3]]
        _ribbon(vec, pill, band, colors, s)
    draw = ImageDraw.Draw(vec)

    # 今日收益：两块数值之间一条竖线；悬赏条只填色；战绩表上方一条细线（不再垫浅底盒子）
    left, right = r["incomeLeft"], r["incomeRight"]
    vline((left[0] + left[2] + right[0]) // 2, left[1] + 16, left[1] + left[3] - 16)
    rr(draw, S(r["incomeTip"]), 12 * s, fill=TIP_STRIP)
    note = r["incomeNote"]
    hline(note[0] + 8, note[0] + note[2] - 8, note[1] - 7)

    # 今日状态：四行之间三条细线，从文字起排处开始（图标列不画线，读起来是「列表」不是「表格」）
    rows = [r["statusLeft"], r["statusRight"], r["statusLuck"], r["statusTaboo"]]
    for upper, lower in zip(rows, rows[1:]):
        y = (upper[1] + upper[3] + lower[1]) // 2
        hline(upper[0] + 70, upper[0] + upper[2] - 4, y)

    # 2026-09-20 第三轮：签到格、签到按钮、图例色块**一律不烤进底图**。
    #
    # 它们的颜色是 C# 侧的状态量（未签 / 已签 / 里程碑 / 已领；按钮的 idle / hover /
    # disabled），运行时必然要自己画一遍。底图再画一层的后果是：
    #   - 两层圆角叠出双边框（运行时的 9-slice 四角是透明的，底图那一层会透出来）；
    #   - 颜色有两个来源，一旦 C# 的 CellEmpty 与这里的 CELL_EMPTY 漂移就对不上
    #     （实测就已经漂了：C# 是 199,189,166，这里原本写的是 226,219,205）。
    # 于是口径收敛成一条：**底图只画不会变的装饰，会变色的一律归运行时**。
    # 底图缺席时（fail-open 走纯纸色底）这三样照样画得出来，表现不降级。
    grid = spec["grid"]
    grid_right = grid["x"] + grid["cellWidth"] * grid["columns"] + grid["gap"] * (grid["columns"] - 1)
    legend = r["legend"]
    vline((grid_right + r["button"][0]) // 2, grid["y"] + 4, legend[1] + legend[3] - 4)
    # 按钮下的期数 / 连签：参考图是居中排，首行两侧各一段细线（「—— 第 1 期 1/30 ——」）
    side = r["sideText"]
    scx = side[0] + side[2] // 2
    hline(side[0] + 16, scx - 118, side[1] + 15)
    hline(scx + 118, side[0] + side[2] - 16, side[1] + 15)

    vec = vec.resize((W, H), Image.LANCZOS)
    image.alpha_composite(vec)
    return image


# ================================================================ 吉祥物（独立 Sprite）
MASCOT_PROMPT = (
    "A single cartoon duck mascot bust for a newspaper masthead: a cheerful round white-and-cream duck wearing a "
    "battered olive-green army helmet with a small red star, a rolled newspaper tucked under one wing, flat orange "
    "bill, simple friendly eyes, shoulders-up view facing slightly left, chunky hand-painted game art with soft "
    "shading and a bold readable silhouette. Put it on a perfectly flat solid #ff00ff chroma-key background. "
    "Do not use #ff00ff, pink or magenta anywhere in the duck. No text, no letters, no logo, no watermark, "
    "no border, no frame, no scene, no background objects.")


def ensure_mascot(force):
    if os.path.exists(MASCOT) and not force:
        return MASCOT
    os.makedirs(os.path.dirname(MASCOT), exist_ok=True)
    imagegen, chroma = base.tool_paths()
    staging = tempfile.mkdtemp(prefix="mascot-", dir=os.path.dirname(MASCOT))
    try:
        raw = base.generate_raw(MASCOT_PROMPT, staging, imagegen)
        cut = os.path.join(staging, "cut.png")
        subprocess.run([sys.executable, chroma, "--input", raw, "--out", cut,
                        "--key-color", "#ff00ff", "--despill", "--soft-matte"],
                       check=True, capture_output=True, text=True, timeout=180)
        image = Image.open(cut).convert("RGBA")
        box = image.getbbox()
        if box:
            image = image.crop(box)
        size = 512
        scale = min(size / float(image.size[0]), size / float(image.size[1]))
        image = image.resize((max(1, int(image.size[0] * scale)), max(1, int(image.size[1] * scale))),
                             Image.LANCZOS)
        canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        canvas.paste(image, ((size - image.size[0]) // 2, (size - image.size[1]) // 2), image)
        canvas.save(MASCOT, "PNG", optimize=True)
        return MASCOT
    finally:
        shutil.rmtree(staging, ignore_errors=True)


def write_mascot():
    """吉祥物缩到 256 独立出图（不再烤进底图、也不再套圆框：参考图是整只鸭压在报头上）。"""
    if not os.path.exists(MASCOT):
        print("吉祥物原图缺失，跳过 " + OUT_MASCOT)
        return
    with Image.open(MASCOT) as source:
        im = source.convert("RGBA")
    bbox = im.getchannel("A").getbbox()
    im = im.crop(bbox)
    im.thumbnail((ICON_PX, ICON_PX), Image.LANCZOS)
    canvas = Image.new("RGBA", (ICON_PX, ICON_PX), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((ICON_PX - im.size[0]) // 2, ICON_PX - im.size[1]))
    canvas.save(OUT_MASCOT, "PNG", optimize=True)


# ================================================================ 图标
# kind：badge = 浅色圆底徽章里放插画（数值块、状态行）；plain = 直接压在纸 / 卡上；
#       ribbon = 压在深色缎带头上（浅色或彩色）；button = 压在签到按钮上（浅色）。
GLYPH_DARK_DESC = ("drawn as a solid very dark warm-brown (#3b322c) pictogram with small cream (#f4ecdc) "
                   "cut-out details, like the dice, speaker and clipboard icons in the reference")
GLYPH_LIGHT_DESC = ("drawn as a solid warm off-white (#f7f2e8) pictogram with small dark cut-out details, "
                    "like the white gift, calendar and duck icons in the reference")
ICONS = [
    ("income", "badge", "a small fan of three green paper banknotes with a round green coin in front of them; "
                        "mid green with lighter green highlights, like the green money icon in the reference"),
    ("bounty", "badge", "a plump brown burlap money bag tied with a rope at the neck, a small round gold coin "
                        "stitched on its front, warm brown and gold like the money bag in the reference"),
    ("headline", "badge", "a folded newspaper with a big headline block and text columns, " + GLYPH_DARK_DESC),
    ("broadcast", "badge", "a loudspeaker with two curved sound waves, " + GLYPH_DARK_DESC),
    ("fortune", "badge", "a single six-sided die, slightly tilted, showing three pips, " + GLYPH_DARK_DESC),
    ("gossip", "badge", "a clipboard holding a sheet of paper with three short lines, " + GLYPH_DARK_DESC),
    ("weather", "plain", "a warm yellow sun with short rounded rays, partly hidden behind a soft light-blue cloud, "
                         "like the weather icon in the reference"),
    ("issue", "plain", "a wall calendar page with two binder rings on top and a small grid of squares, " + GLYPH_DARK_DESC),
    # 2026-09-23 首稿的表盘被网关自带的抠图挖出几块透明洞，重出时写明「实心表盘」
    ("deadline", "plain", "a round wall clock: one solid, completely filled very dark warm-brown (#3b322c) disc "
                          "with no holes, four short cream hour marks and two cream hands"),
    ("tip", "plain", "a round target crosshair reticle, " + GLYPH_DARK_DESC),
    ("ribbon_income", "ribbon", "two stacks of shiny gold coins, warm yellow with orange-brown coin edges, "
                                "like the gold coin stack in the reference"),
    ("ribbon_status", "ribbon", "a cute simple cartoon duck head and shoulders with a small smile, " + GLYPH_LIGHT_DESC),
    ("ribbon_signin", "ribbon", "a calendar page with two binder rings and a check mark, " + GLYPH_LIGHT_DESC),
    ("gift", "button", "a gift box with a ribbon and a bow on top, " + GLYPH_LIGHT_DESC),
]
ICON_KIND = {key: kind for key, kind, _ in ICONS}

ICON_STYLE = (
    "The attached picture is a STYLE REFERENCE ONLY: a contact sheet of small icons cropped from a cozy cartoon "
    "newspaper-dashboard game UI. Do not copy the sheet, its layout or its grey circles. Draw exactly ONE new "
    "standalone icon in the same icon style: simple flat cartoon shapes with soft two-tone cel shading and a gentle "
    "highlight, smooth rounded silhouette, chunky and readable at 48 pixels, no thin details, no black outline. "
    "Subject: ")
ICON_FRAMING = (
    ". Centered, straight-on view, the icon fills about 80 percent of the canvas. Place it on a perfectly flat "
    "solid #ff00ff chroma-key background with no gradient, no shadow and no floor. Do not use #ff00ff, pink or "
    "magenta anywhere in the icon. No circle or badge behind the icon, no frame, no text, no letters, no numbers, "
    "no currency signs, no watermark.")


def icon_path(key):
    return os.path.join(ART_DIR, "dr_icon_%s.png" % key)


def icon_prompt(desc):
    return ICON_STYLE + desc + ICON_FRAMING


def sha256(path):
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def build_style_ref():
    """从参考图裁出一张图标联络表当风格参考（参考图只做风格，不进任何产物）。"""
    if os.path.exists(STYLE_REF):
        return STYLE_REF
    crops = [(35, 158, 80, 200), (195, 158, 240, 200), (28, 112, 68, 145), (368, 160, 410, 195),
             (368, 200, 410, 238), (368, 240, 410, 280), (533, 155, 575, 195), (595, 30, 640, 75),
             (545, 332, 580, 362), (35, 295, 62, 322), (360, 112, 400, 146)]
    with Image.open(REFERENCE) as source:
        ref = source.convert("RGB")
    sheet = Image.new("RGB", (1024, 1024), (246, 239, 225))
    cell = 1024 // 4
    for i, box in enumerate(crops):
        tile = ref.crop(box)
        scale = (cell - 24) / float(max(tile.size))
        tile = tile.resize((int(tile.size[0] * scale), int(tile.size[1] * scale)), Image.LANCZOS)
        cx, cy = (i % 4) * cell + cell // 2, (i // 4) * cell + cell // 2
        sheet.paste(tile, (cx - tile.size[0] // 2, cy - tile.size[1] // 2))
    os.makedirs(WORK, exist_ok=True)
    sheet.save(STYLE_REF, "PNG")
    return STYLE_REF


def generate_icon_raw(prompt, out_path):
    """images.edit + 参考图风格。input_fidelity 在 2.5 上未验证生效，接口拒收就去掉再试。"""
    from openai import OpenAI
    if not os.environ.get("OPENAI_API_KEY") or not os.environ.get("OPENAI_BASE_URL"):
        raise RuntimeError("缺少 OPENAI_BASE_URL / OPENAI_API_KEY（见 docs/AI生图API和密钥.md）")
    client = OpenAI(timeout=900, max_retries=0)
    style = build_style_ref()
    use_fidelity = True
    last = ""
    for attempt in range(1, 4):
        started = time.time()
        try:
            kwargs = dict(model=IMAGE_MODEL, prompt=prompt, size="1024x1024", quality="high", n=1)
            if use_fidelity:
                kwargs["input_fidelity"] = "high"
            with open(style, "rb") as handle:
                result = client.images.edit(image=handle, **kwargs)
            item = result.data[0]
            tmp = out_path + ".part"
            if getattr(item, "b64_json", None):
                with open(tmp, "wb") as handle:
                    handle.write(base64.b64decode(item.b64_json))
            else:
                urllib.request.urlretrieve(item.url, tmp)
            base.inspect_image(tmp)
            os.replace(tmp, out_path)
            print("   raw %s in %.0fs (%s)" % (os.path.basename(out_path), time.time() - started, IMAGE_MODEL), flush=True)
            return
        except Exception as error:  # noqa: BLE001 — 网关限流时是连接错误，重试即可
            last = "%s: %s" % (type(error).__name__, str(error)[:300])
            if "input_fidelity" in str(error) and use_fidelity:
                use_fidelity = False
            print("   [retry %d/3] %s" % (attempt, last), flush=True)
            if attempt < 3:
                time.sleep(int(os.environ.get("ART_GEN_DELAY", "20")) * attempt)
    raise RuntimeError("生图失败: " + last)


def cut_icon(raw, cut):
    if base.inspect_image(raw):
        shutil.copy2(raw, cut)
        return
    _, chroma = base.tool_paths()
    result = subprocess.run(
        [sys.executable, chroma, "--input", raw, "--out", cut, "--auto-key", "border", "--soft-matte",
         "--transparent-threshold", "12", "--opaque-threshold", "220", "--despill", "--force"],
        capture_output=True, text=True, timeout=180)
    if result.returncode != 0 or not os.path.isfile(cut):
        raise RuntimeError("抠图失败: " + (result.stderr or "未输出抠图文件")[-200:])
    base.inspect_image(cut, require_transparency=True)


def _fit(art, box_px):
    art = art.crop(art.getchannel("A").getbbox())
    scale = box_px / float(max(art.size))
    return art.resize((max(1, round(art.size[0] * scale)), max(1, round(art.size[1] * scale))), Image.LANCZOS)


def badge_disc():
    """浅色圆底：暖白径向渐变 + 一圈略深的边 + 向下的柔和投影（参考图的灰白圆徽章）。"""
    n = ICON_PX * 2
    canvas = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    cx, cy, radius = n // 2, n // 2 - 8, int(n * 0.44)
    shadow = Image.new("L", (n, n), 0)
    ImageDraw.Draw(shadow).ellipse([cx - radius, cy - radius + 10, cx + radius, cy + radius + 10], fill=70)
    shadow = shadow.filter(ImageFilter.GaussianBlur(10))
    layer = Image.new("RGBA", (n, n), SHADOW + (0,))
    layer.putalpha(shadow)
    canvas.alpha_composite(layer)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / radius
    light = np.clip(1.0 - ((xx - cx) * 0.3 + (yy - cy) * 0.7) / radius, 0, 2) / 2.0
    base_rgb = np.array(BADGE_DISC, dtype=np.float32)
    rgb = base_rgb[None, None, :] + (light[..., None] - 0.5) * 14.0
    rim = np.clip((d - 0.93) / 0.05, 0, 1)[..., None]
    rgb = rgb * (1 - rim) + np.array(BADGE_RIM, dtype=np.float32)[None, None, :] * rim
    alpha = np.clip((1.0 - d) * radius + 0.5, 0, 1) * 255
    disc = Image.fromarray(np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8), "RGBA")
    canvas.alpha_composite(disc)
    return canvas.resize((ICON_PX, ICON_PX), Image.LANCZOS)


# 实心剪影的图标：网关自带的去背景偶尔会在深色大色块上挖出半透明的洞（2026-09-23 表盘两稿都有），
# 这里把「剪影内部」的非不透明像素垫上底色补实。只对确认是实心剪影的图标做（准星那类本来就镂空的不做）。
ICON_FILL_HOLES = {"deadline": GLYPH_DARK[:3]}


def fill_enclosed(art, color):
    """剪影内部（与画布边缘不连通）的非不透明像素，合成到 color 上补实。确定性，不调网关。"""
    alpha = np.asarray(art.getchannel("A"), dtype=np.uint8)
    open_mask = Image.fromarray(np.where(alpha < 250, 255, 0).astype(np.uint8), "L")
    w, h = open_mask.size
    padded = Image.new("L", (w + 2, h + 2), 255)
    padded.paste(open_mask, (1, 1))
    ImageDraw.floodfill(padded, (0, 0), 128)
    enclosed = np.asarray(padded, dtype=np.uint8)[1:-1, 1:-1] == 255
    if not enclosed.any():
        return art
    rgba = np.asarray(art, dtype=np.float32).copy()
    a = rgba[..., 3:4] / 255.0
    solid = rgba[..., :3] * a + np.array(color, dtype=np.float32)[None, None, :] * (1 - a)
    rgba[..., :3] = np.where(enclosed[..., None], solid, rgba[..., :3])
    rgba[..., 3] = np.where(enclosed, 255, rgba[..., 3])
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA")


def compose_icon(key, art):
    """把抠好的插画按 kind 合成 256 方图：badge 放进浅色圆底，其余直接居中。"""
    kind = ICON_KIND[key]
    if key in ICON_FILL_HOLES:
        art = fill_enclosed(art, ICON_FILL_HOLES[key])
    if kind == "badge":
        canvas = badge_disc()
        art = _fit(art, int(ICON_PX * 0.62))
        canvas.alpha_composite(art, ((ICON_PX - art.size[0]) // 2, (ICON_PX - 8 - art.size[1]) // 2))
        return canvas
    canvas = Image.new("RGBA", (ICON_PX, ICON_PX), (0, 0, 0, 0))
    art = _fit(art, int(ICON_PX * 0.92))
    canvas.alpha_composite(art, ((ICON_PX - art.size[0]) // 2, (ICON_PX - art.size[1]) // 2))
    return canvas


def load_icon_manifest():
    if os.path.exists(ICON_MANIFEST):
        with open(ICON_MANIFEST, encoding="utf-8") as handle:
            return json.load(handle)
    return {}


def save_icon_manifest(data):
    os.makedirs(WORK, exist_ok=True)
    with open(ICON_MANIFEST, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(data, handle, ensure_ascii=False, indent=2, sort_keys=True)
        handle.write("\n")


def run_ai_icons(only, force):
    os.makedirs(ICON_RAW, exist_ok=True)
    manifest = load_icon_manifest()
    todo = [(k, kind, d) for k, kind, d in ICONS if (not only or k in only)]
    called = False
    ok = fail = 0
    for index, (key, kind, desc) in enumerate(todo, 1):
        target = icon_path(key)
        raw = os.path.join(ICON_RAW, key + "_raw.png")
        cut = os.path.join(ICON_RAW, key + "_cut.png")
        # 原图在就不调网关，只按当前合成口径重出 256 方图（改了徽章样式不用重新生图）
        print("[%d/%d] %s%s" % (index, len(todo), key, "" if force or not os.path.exists(raw) else " (cached raw)"),
              flush=True)
        try:
            prompt = icon_prompt(desc)
            if force or not os.path.exists(raw):
                if called:
                    time.sleep(int(os.environ.get("ART_GEN_DELAY", "20")))
                called = True
                generate_icon_raw(prompt, raw)
            cut_icon(raw, cut)
            with Image.open(cut) as source:
                art = source.convert("RGBA")
            compose_icon(key, art).save(target, "PNG", optimize=True)
            manifest[key] = {"source": "ai", "kind": kind, "model": IMAGE_MODEL, "prompt": prompt,
                             "rawSha256": sha256(raw), "sha256": sha256(target)}
            save_icon_manifest(manifest)
            ok += 1
            print("   [OK] -> %s" % target, flush=True)
        except Exception as error:  # noqa: BLE001 — 单张失败不中断整批，下一轮续跑
            fail += 1
            print("   [ERR] %s" % error, flush=True)
    print("AI 图标：成功 %d，失败 %d" % (ok, fail), flush=True)
    return fail


# ---------------------------------------------------------------- 程序化兜底（网关不可用 / 风格不对时）
def _proc_glyph(key, draw, box, fg, bg):
    """box 是方形像素框；fg 为线描 / 实心色，bg 为镂空色。形状保持粗、少、大。"""
    x, y, w, h = box
    u = w / 24.0
    cx, cy = x + w / 2.0, y + h / 2.0
    lw = max(2, int(2.4 * u))
    if key in ("income", "ribbon_income", "bounty"):
        if key == "bounty":
            draw.ellipse([cx - 7 * u, cy - 3 * u, cx + 7 * u, cy + 9 * u], fill=fg)
            draw.polygon([(cx - 3 * u, cy - 4 * u), (cx + 3 * u, cy - 4 * u), (cx + 5 * u, cy - 9 * u),
                          (cx - 5 * u, cy - 9 * u)], fill=fg)
            draw.line([cx - 4 * u, cy - 4 * u, cx + 4 * u, cy - 4 * u], fill=bg, width=lw)
            draw.ellipse([cx - 2.5 * u, cy + 1 * u, cx + 2.5 * u, cy + 6 * u], outline=bg, width=lw)
        elif key == "income":
            for i, dx in enumerate((-3, 0, 3)):
                draw.rounded_rectangle([cx - 8 * u + dx * u, cy - 6 * u - dx * u * 0.4, cx + 6 * u + dx * u,
                                        cy + 2 * u - dx * u * 0.4], radius=u, fill=fg, outline=bg, width=max(1, lw // 2))
            draw.ellipse([cx - 1 * u, cy - 1 * u, cx + 9 * u, cy + 9 * u], fill=fg, outline=bg, width=lw)
        else:
            for i in range(3):
                yy = cy + 6 * u - i * 4 * u
                draw.ellipse([cx - 8 * u, yy - 3 * u, cx + 8 * u, yy + 3 * u], fill=fg, outline=bg, width=max(1, lw // 2))
    elif key in ("issue", "ribbon_signin"):
        draw.rounded_rectangle([cx - 8 * u, cy - 6 * u, cx + 8 * u, cy + 8 * u], radius=2 * u, outline=fg, width=lw)
        draw.line([cx - 8 * u, cy - 2 * u, cx + 8 * u, cy - 2 * u], fill=fg, width=lw)
        for dx in (-4, 4):
            draw.line([cx + dx * u, cy - 9 * u, cx + dx * u, cy - 5 * u], fill=fg, width=lw)
        if key == "ribbon_signin":
            draw.line([cx - 4 * u, cy + 3 * u, cx - 1 * u, cy + 6 * u, cx + 5 * u, cy], fill=fg, width=lw)
    elif key == "deadline":
        draw.ellipse([cx - 9 * u, cy - 9 * u, cx + 9 * u, cy + 9 * u], outline=fg, width=lw)
        draw.line([cx, cy, cx, cy - 6 * u], fill=fg, width=lw)
        draw.line([cx, cy, cx + 4 * u, cy], fill=fg, width=lw)
    elif key == "weather":
        draw.ellipse([cx - 8 * u, cy - 9 * u, cx + 2 * u, cy + 1 * u], fill=FALLBACK_GOLD)
        draw.ellipse([cx - 6 * u, cy - 1 * u, cx + 3 * u, cy + 8 * u], fill=FALLBACK_SKY)
        draw.ellipse([cx - 1 * u, cy - 4 * u, cx + 9 * u, cy + 8 * u], fill=FALLBACK_SKY)
    elif key == "tip":
        draw.ellipse([cx - 8 * u, cy - 8 * u, cx + 8 * u, cy + 8 * u], outline=fg, width=lw)
        for a, b in (((0, -11), (0, -4)), ((0, 4), (0, 11)), ((-11, 0), (-4, 0)), ((4, 0), (11, 0))):
            draw.line([cx + a[0] * u, cy + a[1] * u, cx + b[0] * u, cy + b[1] * u], fill=fg, width=lw)
    elif key == "headline":
        draw.rounded_rectangle([cx - 9 * u, cy - 7 * u, cx + 9 * u, cy + 8 * u], radius=u, fill=fg)
        draw.rectangle([cx - 7 * u, cy - 5 * u, cx - 1 * u, cy + 1 * u], fill=bg)
        for i in range(3):
            draw.line([cx + 1 * u, cy - 4 * u + i * 3 * u, cx + 7 * u, cy - 4 * u + i * 3 * u], fill=bg, width=max(1, lw // 2))
        draw.line([cx - 7 * u, cy + 5 * u, cx + 7 * u, cy + 5 * u], fill=bg, width=max(1, lw // 2))
    elif key == "broadcast":
        draw.polygon([(cx - 8 * u, cy - 3 * u), (cx - 3 * u, cy - 3 * u), (cx + 2 * u, cy - 8 * u),
                      (cx + 2 * u, cy + 8 * u), (cx - 3 * u, cy + 3 * u), (cx - 8 * u, cy + 3 * u)], fill=fg)
        for rr_ in (5, 9):
            draw.arc([cx + 2 * u - rr_ * u, cy - rr_ * u, cx + 2 * u + rr_ * u, cy + rr_ * u], -50, 50, fill=fg, width=lw)
    elif key == "fortune":
        draw.rounded_rectangle([cx - 8 * u, cy - 8 * u, cx + 8 * u, cy + 8 * u], radius=3 * u, fill=fg)
        for dx, dy in ((-4, -4), (0, 0), (4, 4)):
            draw.ellipse([cx + (dx - 1.6) * u, cy + (dy - 1.6) * u, cx + (dx + 1.6) * u, cy + (dy + 1.6) * u], fill=bg)
    elif key == "gossip":
        draw.rounded_rectangle([cx - 7 * u, cy - 7 * u, cx + 7 * u, cy + 9 * u], radius=2 * u, fill=fg)
        draw.rounded_rectangle([cx - 3 * u, cy - 9 * u, cx + 3 * u, cy - 5 * u], radius=u, fill=fg, outline=bg, width=max(1, lw // 2))
        for i in range(3):
            draw.line([cx - 4 * u, cy - 1 * u + i * 3.4 * u, cx + 4 * u, cy - 1 * u + i * 3.4 * u], fill=bg, width=max(1, lw // 2))
    elif key == "ribbon_status":
        draw.ellipse([cx - 6 * u, cy - 9 * u, cx + 6 * u, cy + 3 * u], fill=fg)
        draw.rounded_rectangle([cx - 9 * u, cy + 1 * u, cx + 9 * u, cy + 10 * u], radius=4 * u, fill=fg)
        draw.ellipse([cx - 3 * u, cy - 4 * u, cx - 1 * u, cy - 2 * u], fill=bg)
        draw.ellipse([cx + 1 * u, cy - 4 * u, cx + 3 * u, cy - 2 * u], fill=bg)
        draw.arc([cx - 3 * u, cy - 3 * u, cx + 3 * u, cy + 1 * u], 20, 160, fill=bg, width=max(1, lw // 2))
    elif key == "gift":
        draw.rectangle([cx - 8 * u, cy - 2 * u, cx + 8 * u, cy + 9 * u], fill=fg)
        draw.rectangle([cx - 9 * u, cy - 6 * u, cx + 9 * u, cy - 2 * u], fill=fg)
        draw.line([cx, cy - 6 * u, cx, cy + 9 * u], fill=bg, width=lw)
        draw.ellipse([cx - 6 * u, cy - 11 * u, cx, cy - 6 * u], outline=fg, width=lw)
        draw.ellipse([cx, cy - 11 * u, cx + 6 * u, cy - 6 * u], outline=fg, width=lw)


PROC_COLORS = {
    "income": (FALLBACK_GREEN, (220, 238, 222, 255)),
    "bounty": (FALLBACK_BROWN, (236, 204, 132, 255)),
    "ribbon_income": ((232, 184, 64, 255), (150, 96, 30, 255)),
}


def run_procedural_icons(only, force):
    manifest = load_icon_manifest()
    for key, kind, _ in ICONS:
        if only and key not in only:
            continue
        target = icon_path(key)
        if os.path.exists(target) and not force:
            continue
        fg, bg = PROC_COLORS.get(key, (GLYPH_LIGHT if kind in ("ribbon", "button") else GLYPH_DARK,
                                       (238, 232, 221, 255) if kind not in ("ribbon", "button") else (60, 60, 60, 255)))
        n = ICON_PX * 3
        art = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        _proc_glyph(key, ImageDraw.Draw(art), (0, 0, n, n), fg, (0, 0, 0, 0) if kind in ("ribbon", "button") else bg)
        art = art.resize((ICON_PX, ICON_PX), Image.LANCZOS)
        compose_icon(key, art).save(target, "PNG", optimize=True)
        manifest[key] = {"source": "procedural", "kind": kind, "sha256": sha256(target)}
        print("程序化图标: " + target)
    save_icon_manifest(manifest)


# ================================================================ 入口
def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--mascot", action="store_true", help="重出吉祥物原图（联网）")
    parser.add_argument("--icons", action="store_true", help="用 AI 补齐图标（联网，断点续跑）")
    parser.add_argument("--procedural-icons", action="store_true", help="缺的图标用程序化徽章补（不联网）")
    parser.add_argument("--only", default="", help="只处理这些图标 id（逗号分隔）")
    parser.add_argument("--force", action="store_true", help="重出选中的图标（AI 连 raw 一起重出）")
    parser.add_argument("--list-icons", action="store_true", help="列出图标 id、类型与提示词，不生成")
    args = parser.parse_args()
    only = {v.strip() for v in args.only.split(",") if v.strip()}

    if args.list_icons:
        manifest = load_icon_manifest()
        for key, kind, desc in ICONS:
            print("%-14s %-7s %-10s %s" % (key, kind, manifest.get(key, {}).get("source", "missing"), desc))
        return 0

    code = 0
    if args.icons:
        code = run_ai_icons(only, args.force)
    if args.procedural_icons:
        run_procedural_icons(only, args.force)
    if args.mascot:
        ensure_mascot(True)

    spec = layout()
    os.makedirs(ART_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(OUT_LAYOUT), exist_ok=True)
    compose(spec).save(OUT_IMAGE, "PNG", optimize=True)
    write_mascot()
    with open(OUT_LAYOUT, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(spec, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
    missing = [key for key, _, _ in ICONS if not os.path.exists(icon_path(key))]
    print("底图: " + OUT_IMAGE)
    print("吉祥物: " + OUT_MASCOT)
    print("版面: " + OUT_LAYOUT)
    if missing:
        print("缺图标（运行时会不显示这些格、文字不缩进）: " + ", ".join(missing))
    return code


if __name__ == "__main__":
    sys.exit(main())
