# -*- coding: utf-8 -*-
"""《鸭科夫日报》面板底图与版面表。

owner 2026-09-20：「每日日报你能否生图弄一个和这个一样但是去掉文字的版本，
我想要把日报弄成和这个 100% 都一样的效果，而不是现在那样好丑都是文字。」
（参考图 docs/testing/image-9.png：卡片式仪表盘，不是现在的报纸长条。）

为什么不是「直接让模型出一整张底图」：
  文字要落进卡片里，就必须知道每张卡片的**精确矩形**。AI 出的图每次构图都不一样，
  事后去检测边框又脆。所以这里反过来做——版面由本脚本定义，
  底图按这份版面**程序化合成**（圆角卡片、标题药丸、签到格、分隔线、图标），
  只有「吉祥物鸭」这一件走 AI 出图（色键抠图后贴到固定位置）。
  同一份版面同时写出 Assets/Data/DailyReportLayout.json，C# 直接读它摆文字，
  于是底图与文字**天生对齐**，不存在「图改了文字没跟上」。

产物：
  Assets/ui/DailyReport/daily_report_bg.png   面板底图（无任何文字）
  Assets/Data/DailyReportLayout.json          版面表（C# 读）

用法（吉祥物需要网络出口；密钥只从环境变量读，来源见 docs/AI生图API和密钥.md）：
    python -X utf8 tools/gen_daily_report_ui.py            # 合成底图 + 版面表（吉祥物已存在则复用）
    python -X utf8 tools/gen_daily_report_ui.py --mascot   # 重出吉祥物
    python -X utf8 tools/gen_daily_report_ui.py --no-mascot  # 不联网，吉祥物位留空
"""
import argparse
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_codex_art as base  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_IMAGE = os.path.join(ROOT, "Assets", "ui", "DailyReport", "daily_report_bg.png")
OUT_LAYOUT = os.path.join(ROOT, "Assets", "Data", "DailyReportLayout.json")
MASCOT = os.path.join(ROOT, "output", "daily_report", "mascot.png")

# 面板尺寸与 Integration/DailyReport/DailyReportUI.cs 的 PanelWidth / PanelHeight 一致
W, H = 1333, 1013

# ---------------------------------------------------------------- 纸面配色
PAPER = (242, 235, 222, 255)          # 外框纸色
PAPER_EDGE = (214, 201, 178, 255)
CARD = (252, 248, 240, 255)           # 卡片
CARD_EDGE = (221, 209, 188, 255)
PILL_GREEN = (46, 90, 75, 255)        # 「今日收益」「今日签到」标题药丸
PILL_SLATE = (58, 63, 69, 255)        # 「今日状态」标题药丸
TIP_STRIP = (250, 240, 214, 255)      # 黄色提示条
TIP_EDGE = (231, 206, 145, 255)
# 签到格 / 签到按钮 / 图例色块的颜色**只在 C# 里**（DailyReportUI.cs 的 CellEmpty…
# ButtonIdle…），本脚本不再画它们，因此这里也不留第二份色值（见 compose 里的长注释）。
ICON_BG = (58, 63, 69, 255)
ICON_FG = (250, 246, 238, 255)
RULE = (223, 212, 192, 255)

SCALE = 3  # 先按 3 倍画再缩，圆角与细线才不毛糙


def rr(draw, box, radius, fill=None, outline=None, width=2):
    """box 一律是 (x, y, w, h)，与版面表同一约定，避免两套坐标混用。"""
    x, y, w, h = box
    draw.rounded_rectangle([x, y, x + w - 1, y + h - 1],
                           radius=radius, fill=fill, outline=outline, width=width)


def layout():
    """版面表：所有矩形都是 [x, y, w, h]，左上原点，像素单位，与底图同一坐标系。"""
    pad = 22
    header_h = 150
    gap = 16

    header = [pad, 20, W - pad * 2, header_h]
    mascot = [header[0] + 18, header[1] + 12, 126, 126]
    title = [mascot[0] + mascot[2] + 16, header[1] + 26, 430, 98]
    info_meta = [header[0] + 660, header[1] + 16, 330, header_h - 32]
    info_weather = [info_meta[0] + info_meta[2] + 14, header[1] + 16,
                    header[0] + header[2] - (info_meta[0] + info_meta[2] + 14) - 16, header_h - 32]

    row2_y = header[1] + header_h + gap
    row2_h = 402
    income = [pad, row2_y, 766, row2_h]
    status = [income[0] + income[2] + gap, row2_y, W - pad - (income[0] + income[2] + gap), row2_h]

    row3_y = row2_y + row2_h + gap
    row3_h = H - row3_y - 20

    pill_h = 46
    signin_pill = [pad + 20, row3_y + 14, 232, pill_h]
    income_pill = [income[0] + 20, income[1] + 14, 232, pill_h]
    status_pill = [status[0] + 20, status[1] + 14, 300, pill_h]

    # 今日收益：两个数值块 + 提示条 + 进行中一行
    income_body = [income[0] + 24, income_pill[1] + pill_h + 14, income[2] - 48, 132]
    income_left = [income_body[0], income_body[1], income_body[2] // 2 - 8, income_body[3]]
    income_right = [income_left[0] + income_left[2] + 16, income_body[1],
                    income_body[2] - income_left[2] - 16, income_body[3]]
    income_tip = [income[0] + 24, income_body[1] + income_body[3] + 12, income[2] - 48, 56]
    income_note = [income[0] + 24, income_tip[1] + income_tip[3] + 12, income[2] - 48,
                   income[1] + income[3] - (income_tip[1] + income_tip[3] + 12) - 18]

    # 今日状态：两行「图标 + 标题 + 正文」，外加宜 / 忌两行
    status_body = [status[0] + 24, status_pill[1] + pill_h + 14, status[2] - 48, 132]
    status_left = [status_body[0], status_body[1], status_body[2] // 2 - 8, status_body[3]]
    status_right = [status_left[0] + status_left[2] + 16, status_body[1],
                    status_body[2] - status_left[2] - 16, status_body[3]]
    status_luck = [status[0] + 24, status_body[1] + status_body[3] + 12, status[2] - 48, 68]
    status_taboo = [status[0] + 24, status_luck[1] + status_luck[3] + 10, status[2] - 48,
                    status[1] + status[3] - (status_luck[1] + status_luck[3] + 10) - 18]

    # 签到墙：10 × 3 网格 + 右侧按钮块 + 底部图例
    grid_x = pad + 26
    grid_y = signin_pill[1] + pill_h + 16
    cell_w, cell_h, cell_gap = 62, 52, 10
    grid_w = cell_w * 10 + cell_gap * 9
    grid_h = cell_h * 3 + cell_gap * 2
    side_x = grid_x + grid_w + 22
    side_w = W - pad - side_x - 26
    button = [side_x, grid_y, side_w, 62]
    side_text = [side_x, button[1] + button[3] + 12, side_w, grid_h - button[3] - 12]
    legend = [grid_x, grid_y + grid_h + 16, grid_w, 30]
    legend_swatch = 16
    legend_item_w = grid_w // 4
    # 卡片高度按内容收紧：药丸 + 网格 + 图例 + 下留白，避免底部一大片死区
    signin = [pad, row3_y, W - pad * 2, legend[1] + legend[3] + 18 - row3_y]

    return {
        "schemaVersion": 1,
        "panel": [0, 0, W, H],
        "rects": {
            "header": header,
            "mascot": mascot,
            "title": title,
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
            "statusLeft": status_left,
            "statusRight": status_right,
            "statusLuck": status_luck,
            "statusTaboo": status_taboo,
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
        "legend": {"swatch": legend_swatch, "itemWidth": legend_item_w},
        "icon": {"size": 34, "inset": 12, "textIndent": 56},
    }


# ---------------------------------------------------------------- 小图标
def icon_coin(draw, box):
    x, y, w, h = box
    cx, cy, r = x + w // 2, y + h // 2, int(min(w, h) * 0.34)
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=ICON_FG)
    draw.ellipse([cx - r + 5 * SCALE, cy - r + 5 * SCALE, cx + r - 5 * SCALE, cy + r - 5 * SCALE],
                 outline=ICON_BG, width=2 * SCALE)


def icon_clock(draw, box):
    x, y, w, h = box
    cx, cy, r = x + w // 2, y + h // 2, int(min(w, h) * 0.34)
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=ICON_FG, width=3 * SCALE)
    draw.line([cx, cy, cx, cy - int(r * 0.6)], fill=ICON_FG, width=3 * SCALE)
    draw.line([cx, cy, cx + int(r * 0.5), cy], fill=ICON_FG, width=3 * SCALE)


def icon_calendar(draw, box):
    x, y, w, h = box
    pad = int(min(w, h) * 0.24)
    inner = [x + pad, y + pad + 2 * SCALE, x + w - pad, y + h - pad]
    draw.rounded_rectangle(inner, radius=3 * SCALE, outline=ICON_FG, width=3 * SCALE)
    draw.line([inner[0], inner[1] + 9 * SCALE, inner[2], inner[1] + 9 * SCALE], fill=ICON_FG, width=3 * SCALE)
    draw.line([x + w // 2 - 7 * SCALE, y + pad - 3 * SCALE, x + w // 2 - 7 * SCALE, y + pad + 4 * SCALE],
              fill=ICON_FG, width=3 * SCALE)
    draw.line([x + w // 2 + 7 * SCALE, y + pad - 3 * SCALE, x + w // 2 + 7 * SCALE, y + pad + 4 * SCALE],
              fill=ICON_FG, width=3 * SCALE)


def icon_sun(draw, box):
    x, y, w, h = box
    cx, cy, r = x + w // 2, y + h // 2, int(min(w, h) * 0.22)
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=ICON_FG)
    for i in range(8):
        a = math.pi * i / 4.0
        x0 = cx + int(math.cos(a) * r * 1.6)
        y0 = cy + int(math.sin(a) * r * 1.6)
        x1 = cx + int(math.cos(a) * r * 2.2)
        y1 = cy + int(math.sin(a) * r * 2.2)
        draw.line([x0, y0, x1, y1], fill=ICON_FG, width=3 * SCALE)


def icon_dice(draw, box):
    x, y, w, h = box
    pad = int(min(w, h) * 0.24)
    draw.rounded_rectangle([x + pad, y + pad, x + w - pad, y + h - pad],
                           radius=4 * SCALE, outline=ICON_FG, width=3 * SCALE)
    for dx, dy in ((0.33, 0.33), (0.67, 0.67), (0.5, 0.5)):
        px = x + int(w * dx)
        py = y + int(h * dy)
        draw.ellipse([px - 2 * SCALE, py - 2 * SCALE, px + 2 * SCALE, py + 2 * SCALE], fill=ICON_FG)


def icon_note(draw, box):
    x, y, w, h = box
    pad = int(min(w, h) * 0.24)
    draw.rounded_rectangle([x + pad, y + pad - 2 * SCALE, x + w - pad, y + h - pad],
                           radius=3 * SCALE, outline=ICON_FG, width=3 * SCALE)
    for i in range(3):
        ly = y + pad + (6 + i * 7) * SCALE
        draw.line([x + pad + 5 * SCALE, ly, x + w - pad - 5 * SCALE, ly], fill=ICON_FG, width=2 * SCALE)


def icon_speaker(draw, box):
    x, y, w, h = box
    cx, cy = x + w // 2, y + h // 2
    draw.polygon([(cx - 9 * SCALE, cy - 4 * SCALE), (cx - 3 * SCALE, cy - 4 * SCALE),
                  (cx + 3 * SCALE, cy - 10 * SCALE), (cx + 3 * SCALE, cy + 10 * SCALE),
                  (cx - 3 * SCALE, cy + 4 * SCALE), (cx - 9 * SCALE, cy + 4 * SCALE)], fill=ICON_FG)
    for i in range(2):
        r = (7 + i * 5) * SCALE
        draw.arc([cx + 3 * SCALE - r, cy - r, cx + 3 * SCALE + r, cy + r], -60, 60,
                 fill=ICON_FG, width=2 * SCALE)


def icon_warning(draw, box):
    x, y, w, h = box
    cx = x + w // 2
    top = y + int(h * 0.24)
    bottom = y + int(h * 0.74)
    half = int(w * 0.26)
    draw.polygon([(cx, top), (cx + half, bottom), (cx - half, bottom)], outline=ICON_FG, width=3 * SCALE)
    draw.line([cx, top + 9 * SCALE, cx, bottom - 12 * SCALE], fill=ICON_FG, width=3 * SCALE)
    draw.ellipse([cx - 2 * SCALE, bottom - 8 * SCALE, cx + 2 * SCALE, bottom - 4 * SCALE], fill=ICON_FG)


def icon_gift(draw, box):
    x, y, w, h = box
    pad = int(min(w, h) * 0.24)
    body = [x + pad, y + pad + 6 * SCALE, x + w - pad, y + h - pad]
    draw.rounded_rectangle(body, radius=3 * SCALE, outline=ICON_FG, width=3 * SCALE)
    draw.line([body[0], body[1] + 8 * SCALE, body[2], body[1] + 8 * SCALE], fill=ICON_FG, width=3 * SCALE)
    draw.line([x + w // 2, body[1], x + w // 2, body[3]], fill=ICON_FG, width=3 * SCALE)


def badge(draw, box, painter):
    rr(draw, box, 10 * SCALE, fill=ICON_BG)
    painter(draw, box)


# ---------------------------------------------------------------- 吉祥物
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


# ---------------------------------------------------------------- 合成
def compose(spec, mascot_path):
    image = Image.new("RGBA", (W * SCALE, H * SCALE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    s = SCALE

    def S(box):
        """版面单位 (x, y, w, h) -> 画布像素 (x, y, w, h)。"""
        return [box[0] * s, box[1] * s, box[2] * s, box[3] * s]

    r = spec["rects"]

    # 外框纸面
    rr(draw, [0, 0, W * s, H * s], 26 * s, fill=PAPER, outline=PAPER_EDGE, width=3 * s)

    for key in ("header", "income", "status", "signin"):
        rr(draw, S(r[key]), 18 * s, fill=CARD, outline=CARD_EDGE, width=2 * s)
    for key in ("infoMeta", "infoWeather"):
        rr(draw, S(r[key]), 12 * s, fill=PAPER, outline=CARD_EDGE, width=2 * s)

    for key, color in (("incomePill", PILL_GREEN), ("statusPill", PILL_SLATE), ("signinPill", PILL_GREEN)):
        rr(draw, S(r[key]), 12 * s, fill=color)
        # 药丸左端的圆形图标底（C# 的药丸文字从这里右侧起排）
        box = r[key]
        rr(draw, S([box[0] + 9, box[1] + 8, 30, 30]), 8 * s, fill=(255, 255, 255, 48))

    # 标题下的细分隔线（报头与正文之间）
    title = r["title"]
    draw.line([title[0] * s, (title[1] + title[3] - 6) * s,
               (title[0] + title[2]) * s, (title[1] + title[3] - 6) * s], fill=RULE, width=2 * s)

    # 收益两块数值底
    for key in ("incomeLeft", "incomeRight", "statusLeft", "statusRight"):
        rr(draw, S(r[key]), 12 * s, fill=PAPER, outline=CARD_EDGE, width=2 * s)
    rr(draw, S(r["incomeTip"]), 10 * s, fill=TIP_STRIP, outline=TIP_EDGE, width=2 * s)
    for key in ("statusLuck", "statusTaboo", "incomeNote"):
        rr(draw, S(r[key]), 10 * s, fill=PAPER, outline=CARD_EDGE, width=2 * s)

    # 2026-09-20 第三轮：签到格、签到按钮、图例色块**一律不烤进底图**。
    #
    # 它们的颜色是 C# 侧的状态量（未签 / 已签 / 里程碑 / 已领；按钮的 idle / hover /
    # disabled），运行时必然要自己画一遍。底图再画一层的后果是：
    #   - 两层圆角+描边叠出双边框（运行时的 9-slice 四角是透明的，底图那一层会透出来）；
    #   - 颜色有两个来源，一旦 C# 的 CellEmpty 与这里的 CELL_EMPTY 漂移就对不上
    #     （实测就已经漂了：C# 是 199,189,166，这里原本写的是 226,219,205）。
    # 于是口径收敛成一条：**底图只画不会变的装饰，会变色的一律归运行时**。
    # 底图缺席时（fail-open 走纯纸色底）这三样照样画得出来，表现不降级。
    rr(draw, S(r["sideText"]), 10 * s, fill=PAPER, outline=CARD_EDGE, width=2 * s)

    # 图标徽章：位置与 C# 里文字的缩进一致（文字从徽章右侧起排）
    icon = spec["icon"]
    badges = [
        (r["infoMeta"], 0, 2, icon_calendar),
        (r["infoMeta"], 1, 2, icon_clock),
        (r["infoWeather"], 0, 1, icon_sun),
        (r["incomeLeft"], 0, 1, icon_coin),
        (r["incomeRight"], 0, 1, icon_gift),
        (r["statusLeft"], 0, 1, icon_dice),
        (r["statusRight"], 0, 1, icon_note),
        (r["statusLuck"], 0, 1, icon_speaker),
        (r["statusTaboo"], 0, 1, icon_warning),
    ]
    for box, slot, slots, painter in badges:
        bx = box[0] + icon["inset"]
        by = box[1] + (box[3] // slots - icon["size"]) // 2 + slot * (box[3] // slots)
        badge(draw, S([bx, by, icon["size"], icon["size"]]), painter)

    # 吉祥物
    if mascot_path and os.path.exists(mascot_path):
        mascot = Image.open(mascot_path).convert("RGBA")
        box = r["mascot"]
        target = (box[2] * s, box[3] * s)
        mascot = mascot.resize(target, Image.LANCZOS)
        draw.ellipse([box[0] * s, box[1] * s, (box[0] + box[2]) * s, (box[1] + box[3]) * s],
                     fill=PAPER, outline=CARD_EDGE, width=3 * s)
        image.paste(mascot, (box[0] * s, box[1] * s), mascot)

    image = image.resize((W, H), Image.LANCZOS)
    image = image.filter(ImageFilter.SMOOTH)
    return image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mascot", action="store_true", help="重出吉祥物（联网）")
    parser.add_argument("--no-mascot", action="store_true", help="不联网，吉祥物位留空")
    args = parser.parse_args()

    spec = layout()
    mascot_path = None
    if not args.no_mascot:
        mascot_path = ensure_mascot(args.mascot)

    os.makedirs(os.path.dirname(OUT_IMAGE), exist_ok=True)
    os.makedirs(os.path.dirname(OUT_LAYOUT), exist_ok=True)
    compose(spec, mascot_path).save(OUT_IMAGE, "PNG", optimize=True)
    with open(OUT_LAYOUT, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(spec, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
    print("底图: " + OUT_IMAGE)
    print("版面: " + OUT_LAYOUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
