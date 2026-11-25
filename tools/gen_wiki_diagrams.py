# -*- coding: utf-8 -*-
"""gen_wiki_diagrams.py - 生成核心重难点机制与动线示意图（中英双版，水墨羊皮卷配色）。

产物落到 Assets/wiki_diagrams/*.png，同时供：
  1. tools/build_wiki_images.py 转成 WebP 供在线 Wiki (wiki-site) 使用；
  2. tools/production_icon_manifest.json 打包进 Assets/ui/production_icons 供游戏内《冒险家日志》展示。
"""
import math
import os
import re
import sys

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:
    print("gen_wiki_diagrams: FAIL - 需要 Pillow: python -m pip install Pillow")
    sys.exit(2)

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(REPO, "Assets", "wiki_diagrams")

W, H = 1280, 800

BG = (242, 232, 210, 255)
BG_ALT = (232, 219, 192, 255)
CARD_BG = (250, 244, 228, 255)
CARD_DARK = (46, 36, 28, 255)
INK = (36, 26, 19, 255)
INK_SOFT = (92, 74, 56, 255)
BORDER = (118, 92, 62, 255)
CINNABAR = (158, 42, 34, 255)
CINNABAR_BG = (248, 226, 222, 255)
JADE = (28, 108, 82, 255)
JADE_BG = (223, 240, 232, 255)
TEAL = (24, 126, 134, 255)
TEAL_BG = (220, 241, 242, 255)
AMBER = (168, 108, 18, 255)
AMBER_BG = (250, 238, 212, 255)
INDIGO = (46, 82, 128, 255)
INDIGO_BG = (226, 234, 246, 255)
PURPLE = (116, 56, 136, 255)
PURPLE_BG = (240, 229, 246, 255)


def load_font(size, bold=False):
    candidates = [
        r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc",
        r"C:\Windows\Fonts\Dengb.ttf" if bold else r"C:\Windows\Fonts\Deng.ttf",
        r"C:\Windows\Fonts\arialbd.ttf" if bold else r"C:\Windows\Fonts\arial.ttf",
    ]
    for path in candidates:
        if os.path.isfile(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


FONT_TITLE = load_font(33, bold=True)
FONT_SUB = load_font(18, bold=False)
FONT_HEAD = load_font(20, bold=True)
FONT_BODY = load_font(17, bold=False)
FONT_BOLD = load_font(18, bold=True)
FONT_SMALL = load_font(16, bold=False)
FONT_TINY = load_font(14, bold=True)


def text_w(d, text, font):
    bb = d.textbbox((0, 0), text, font=font)
    return bb[2] - bb[0]


def new_canvas(title, subtitle):
    im = Image.new("RGBA", (W, H), BG)
    d = ImageDraw.Draw(im)
    d.rectangle([14, 14, W - 15, H - 15], outline=BORDER, width=3)
    d.rectangle([24, 24, W - 25, H - 25], outline=(168, 142, 108, 255), width=1)
    d.rectangle([25, 25, W - 26, 100], fill=BG_ALT)
    d.line([25, 100, W - 26, 100], fill=BORDER, width=2)
    d.rectangle([42, 40, 50, 84], fill=CINNABAR)
    d.text((64, 33), title, font=FONT_TITLE, fill=INK)
    d.text((64, 72), subtitle, font=FONT_SUB, fill=INK_SOFT)
    return im, d


def tokenize_for_wrap(para):
    """English words stay whole while CJK characters can break cleanly."""
    tokens = []
    for m in re.finditer(r"[A-Za-z0-9_+\-./:%><=!']+|\s+|.", para):
        tokens.append(m.group(0))
    return tokens


def wrap_lines(d, text, font, max_w):
    lines = []
    for para in text.split("\n"):
        if not para:
            lines.append("")
            continue
        cur = ""
        for tok in tokenize_for_wrap(para):
            cand = cur + tok
            if text_w(d, cand, font) <= max_w or not cur:
                cur = cand
            else:
                lines.append(cur.rstrip())
                cur = tok.lstrip()
        if cur.strip() or not lines:
            lines.append(cur.rstrip())
    return lines


def draw_box(d, xy, title, body_lines, fill=CARD_BG, outline=BORDER, title_color=INK,
             badge=None, badge_bg=CINNABAR, radius=10, title_font=FONT_HEAD, body_font=FONT_SMALL):
    x1, y1, x2, y2 = xy
    d.rounded_rectangle([x1 + 3, y1 + 3, x2 + 3, y2 + 3], radius=radius, fill=(180, 160, 130, 75))
    d.rounded_rectangle([x1, y1, x2, y2], radius=radius, fill=fill, outline=outline, width=2)
    cy = y1 + 10
    badge_reserve = 0
    if badge:
        bw = text_w(d, badge, FONT_TINY) + 14
        badge_reserve = bw + 14
        d.rounded_rectangle([x2 - bw - 10, y1 + 8, x2 - 10, y1 + 31], radius=6, fill=badge_bg)
        d.text((x2 - bw - 3, y1 + 10), badge, font=FONT_TINY, fill=(255, 250, 240, 255))
    if title:
        avail_tw = (x2 - x1) - 24 - badge_reserve
        t_font = title_font
        if text_w(d, title, t_font) > avail_tw and avail_tw > 60:
            t_font = FONT_BOLD if text_w(d, title, FONT_BOLD) <= avail_tw else FONT_SMALL
        d.text((x1 + 12, cy), title, font=t_font, fill=title_color)
        cy += 27
        d.line([x1 + 10, cy, x2 - 10, cy], fill=(outline[0], outline[1], outline[2], 120), width=1)
        cy += 6
    max_w = (x2 - x1) - 24
    for line in body_lines:
        for wrapped in wrap_lines(d, line, body_font, max_w):
            if cy + 19 > y2 - 6:
                break
            d.text((x1 + 12, cy), wrapped, font=body_font, fill=INK)
            cy += 20


def draw_arrow(d, p1, p2, color=BORDER, width=3, dashed=False, label=None, label_bg=BG):
    x1, y1 = p1
    x2, y2 = p2
    dx, dy = x2 - x1, y2 - y1
    dist = math.hypot(dx, dy)
    if dist < 1:
        return
    ux, uy = dx / dist, dy / dist
    if dashed:
        seg, gap, pos = 12, 8, 0.0
        while pos < dist - 10:
            nxt = min(pos + seg, dist - 10)
            d.line([(x1 + ux * pos, y1 + uy * pos), (x1 + ux * nxt, y1 + uy * nxt)], fill=color, width=width)
            pos = nxt + gap
    else:
        d.line([(x1, y1), (x2 - ux * 8, y2 - uy * 8)], fill=color, width=width)
    ah, aw = 11, 6
    px, py = -uy, ux
    d.polygon([(x2, y2), (x2 - ux * ah + px * aw, y2 - uy * ah + py * aw),
               (x2 - ux * ah - px * aw, y2 - uy * ah - py * aw)], fill=color)
    if label:
        mx, my = (x1 + x2) / 2, (y1 + y2) / 2
        tb = d.textbbox((0, 0), label, font=FONT_TINY)
        tw, th = tb[2] - tb[0], tb[3] - tb[1]
        d.rounded_rectangle([mx - tw / 2 - 6, my - th / 2 - 4, mx + tw / 2 + 6, my + th / 2 + 4],
                            radius=5, fill=label_bg, outline=color, width=1)
        d.text((mx - tw / 2, my - th / 2 - 2), label, font=FONT_TINY, fill=color)


def build_sky_island(lang):
    zh = (lang == "zh")
    im, d = new_canvas(
        "天空岛 · 晴岚群岛：双航标与捷径动线图" if zh else "Sky Islands: Beacons, Shortcuts & Night Route",
        "序章找杰夫接「云上的坐标」→ 零号区打断风游猎 · 守拿航向仪 → 基地船点登岛"
        if zh else "Prelude: Take Jeff's quest -> Beat Galebreaker Warden in Ground Zero -> Board at Base Boat"
    )

    draw_arrow(d, (640, 675), (640, 628), color=TEAL, width=4)
    draw_arrow(d, (460, 578), (370, 520), color=JADE, width=4)
    draw_arrow(d, (820, 578), (910, 520), color=INDIGO, width=4)
    draw_arrow(d, (295, 425), (295, 385), color=JADE, width=4)
    draw_arrow(d, (985, 425), (985, 385), color=INDIGO, width=4)
    draw_arrow(d, (168, 330), (156, 330), color=PURPLE, width=2)
    draw_arrow(d, (1112, 330), (1124, 330), color=PURPLE, width=2)
    draw_arrow(d, (430, 300), (460, 275), color=AMBER, width=4)
    draw_arrow(d, (850, 300), (820, 275), color=AMBER, width=4)
    draw_arrow(d, (640, 212), (640, 178), color=CINNABAR, width=4)
    draw_arrow(d, (425, 385), (510, 535), color=JADE, width=2, dashed=True, label="K1", label_bg=BG)
    draw_arrow(d, (855, 385), (770, 535), color=INDIGO, width=2, dashed=True, label="K2", label_bg=BG)
    draw_arrow(d, (600, 316), (615, 535), color=CINNABAR, width=3, dashed=True,
               label="K3 必开!" if zh else "K3 Must-Open!", label_bg=CINNABAR_BG)

    draw_box(d, (395, 114, 885, 178),
             "⑤ 归航钟庭（终点）" if zh else "5. Homecoming Bell Court",
             ["交涉或挑战 折翎 / 守钟装置 → 敲归航钟拿晴岚航徽；双航标亮即开绿撤离环"
              if zh else "Parley or fight Zheling / Bell Engine -> Ring Bell for Badge; green exit"],
             fill=CINNABAR_BG, outline=CINNABAR, title_color=CINNABAR,
             badge="绿撤离环" if zh else "Green Exit", badge_bg=JADE)

    draw_box(d, (452, 212, 828, 316),
             "④ 鸣风栈道（风眼）" if zh else "4. Windsong Boardwalk",
             ["双航标都亮后从码头接「钟庭之争」由此北上" if zh else "Unlocks after both beacons are lit",
              "风眼 Boss 噬风（可选，打过可减免和平劝说门槛）" if zh else "Optional Windeater (3000 HP); eases finale",
              "结局后带噬风之核 + 晴岚风晶可再引「回响」" if zh else "Post-ending: burn crystal to summon Echo"],
             fill=AMBER_BG, outline=AMBER, title_color=AMBER,
             badge="K3 省半程!" if zh else "K3 Halves Walk", badge_bg=CINNABAR)

    draw_box(d, (168, 425, 432, 520),
             "② 西路：青穗梯田" if zh else "2W. Green Terraces",
             ["岛主「穗镰」（掉蓑衣农装）" if zh else "Lord Harvestick (Straw Raincoat)",
              "晴禾灶台：驱风香 / 便当 / 纱笠" if zh else "Qinghe Stove: Bento / Incense / Veil"],
             fill=JADE_BG, outline=JADE, title_color=JADE)

    draw_box(d, (168, 255, 432, 385),
             "③ 西 · 悬根林" if zh else "3W. Root Wood",
             ["先清风标东 & 北（药臼旁）2 伙守卫再修灯" if zh else "Clear BOTH 2 guard packs first!",
              "岛主「悬根猎首」（掉悬根猎装）" if zh else "Lord Huntmaster (Rootweave set)",
              "眠苔苔药（按缺血收费）/ 开绿环" if zh else "Miantai heal / opens Green Exit"],
             fill=JADE_BG, outline=JADE, title_color=JADE,
             badge="西航标" if zh else "Beacon W", badge_bg=JADE)

    draw_box(d, (32, 208, 156, 400),
             "西支路" if zh else "West Sides",
             ["蛙鸣池(22-6点)", "蚋笛翁 / 蛙卵", "", "倒挂邮亭", "截信人(偷耗材)"]
             if zh else ["Frogsong Pool", "22-06: Gnat-Piper", "Bring frogspawn", "", "Hanging Post", "Letter-Snatcher"],
             fill=PURPLE_BG, outline=PURPLE, title_color=PURPLE, body_font=FONT_TINY)

    draw_box(d, (848, 425, 1112, 520),
             "② 东路：镜水寺" if zh else "2E. Mirrorwater",
             ["夜间 22-6 点「镜中客」+ 蛙卵源" if zh else "Night 22-06: Mirror-Guest + Spawn",
              "剧情对手「折翎」也在此处" if zh else "Story rival Zheling stays here"],
             fill=INDIGO_BG, outline=INDIGO, title_color=INDIGO)

    draw_box(d, (848, 255, 1112, 385),
             "③ 东 · 残星工坊" if zh else "3E. Starworks",
             ["先清星灯前 & 北（日志旁）2 伙守卫再修灯" if zh else "Clear BOTH 2 guard packs first!",
              "岛主「残星匠首」（掉星工装备）" if zh else "Lord Starwright (Starworks set)",
              "修好后可熔「晴岚风晶」/ 开绿环" if zh else "Unlocks Windcrystal & Green Exit"],
             fill=INDIGO_BG, outline=INDIGO, title_color=INDIGO,
             badge="东航标" if zh else "Beacon E", badge_bg=INDIGO)

    draw_box(d, (1124, 208, 1248, 400),
             "东支路" if zh else "East Sides",
             ["残星瞭台", "瞭台观星手", "星屑高产", "", "听雨洞", "听雨人8枪落石"]
             if zh else ["Star Overlook", "Stargazer + Stars", "", "Rainhush Cave", "Rain-Listener", "8 shots=rockfall"],
             fill=PURPLE_BG, outline=PURPLE, title_color=PURPLE, body_font=FONT_TINY)

    draw_box(d, (460, 535, 820, 628),
             "① 风铃集（村中心）" if zh else "1. Windchime Market (Hub)",
             ["找「苇白」接主线「点亮两端航标」与航务委托" if zh else "Weibai: Main beacon quest & contracts",
              "K1/K2/K3 捷径的门在北侧桥头，须从对岸绕回开" if zh else "Shortcut gates open from far side"],
             fill=CARD_BG, outline=BORDER)

    draw_box(d, (380, 675, 900, 764),
             "起点：云起渡口（登云码头）" if zh else "Start: Cloudrise Dock",
             ["浮舟：渡口整备(修装备) + 工台(造风灯/护符/灭蚊灯/熔风晶)" if zh else "Fuzhou: Gear repair + workbench + Lv3 Ledger",
              "航路图看地图/换天色/抽贝壳签(3小签·10大签)；青环常开" if zh else "Chart: Map / Sky / Shell Lots (3 or 10); Cyan Exit open"],
             fill=TEAL_BG, outline=TEAL, title_color=TEAL,
             badge="青环常开" if zh else "Cyan Exit", badge_bg=TEAL)

    out = os.path.join(OUT_DIR, f"sky_island_route_{lang}.png")
    im.save(out)
    return out


def build_tidegate(lang):
    zh = (lang == "zh")
    im, d = new_canvas(
        "潮闸镇：潮汐循环、三阀开闸与撤离图解" if zh else "Tidegate: Tide Cycle, 3 Valves & Exits",
        "阿稳寄「闸伯的信」读信解锁地堡下水道竹筏 → 潮水约 4.5 游戏小时一轮"
        if zh else "Read Old Zha's Letter from Awen -> Sewer Raft -> Tide cycles ~4.5 in-game hours"
    )

    draw_box(d, (42, 118, 625, 365),
             "潮汐四阶段（看右上角卡片或带「潮汐怀表」）" if zh else "4 Tide Phases (HUD / Tide Watch)",
             [
                 "1. 退潮 → 滩涂与「挖沙船跳板」露出，排水暗渠可撤离"
                 if zh else "1. Ebb -> Mudflats & Dredger plank expose; Culvert opens",
                 "2. 低潮 → 沉没的「储蓄所金库」与水下箱露出（水下大奖！）"
                 if zh else "2. Low -> Sunken Bank Vault & underwater crates expose",
                 "3. 涨潮 → 警报提示后淹回低洼，轻装仍可去抓栈桥缆绳"
                 if zh else "3. Flood -> Alerts sound, ground floods (rope still works)",
                 "4. 高潮 → 「栈桥渡口」船靠岸，满载超重也能直接走！"
                 if zh else "4. High -> Ferry docks at Jetty: extract at ANY weight!",
             ],
             fill=TEAL_BG, outline=TEAL, title_color=TEAL)

    draw_box(d, (655, 118, 1238, 365),
             "负重生死线 & 两处秒杀机关" if zh else "Weight Limit & 2 Instant-Kill Hazards",
             [
                 "• 下水负重上限：全身 <= 14 kg（带「防水袋」-> 20 kg）"
                 if zh else "• Swim weight cap: <= 14 kg (Dry Bag boosts to <= 20 kg)",
                 "• 超重入深水会沉没缺氧；带「鸭蹼脚掌」游更快且难被发现"
                 if zh else "• Overweight sinks & drowns; Flippers speed swim, stealthier",
                 "• [危险] 泻湖放水闸口：红圈内留满 4 秒被卷走，从坝顶绕行"
                 if zh else "• [HAZARD] Open Spillway: 4s in red ring pulls you under",
                 "• [危险] 码头吊机：首响试闸看地上黄框，下一吊落地砸死线内"
                 if zh else "• [HAZARD] Dock Crane: 1st bell shows yellow box; next drop kills",
             ],
             fill=CINNABAR_BG, outline=CINNABAR, title_color=CINNABAR)

    draw_box(d, (42, 388, 355, 535),
             "阀门 A & B：随时开" if zh else "Valves A & B",
             ["第三水厂泵房（拧 3 秒）" if zh else "Waterworks Pump #3 (3s)",
              "镇区泵站（拧 3 秒）" if zh else "Town Pump Station (3s)",
              "顺路拿 4 页日志插画" if zh else "Grab 4 Keeper's Log pages"],
             fill=INDIGO_BG, outline=INDIGO, title_color=INDIGO)

    draw_box(d, (395, 388, 765, 535),
             "阀门 C：退潮上挖沙船" if zh else "Valve C: Dredger at Ebb",
             ["退潮上挖沙船船舱拿「阀门手轮」" if zh else "Ebb/Low: loot Handwheel in cabin",
              "装到「潮位站」拧 3 秒（手轮消耗）" if zh else "Fit at Tide Station & turn 3s",
              "打「沙老大·铅锤」爆淘沙响铊！" if zh else "Boss Leadweight drops Sounding Lead"],
             fill=AMBER_BG, outline=AMBER, title_color=AMBER)

    draw_box(d, (805, 388, 1238, 535),
             "大闸提起 → 储蓄所金库" if zh else "Gate Opens -> Sunken Vault",
             ["三阀全开泻湖放干！4 秒后冲来 3 名抢劫帮众" if zh else "Lagoon drains! 3 Sand Dredgers arrive",
              "低潮开「所长保险柜」拿「潮汐怀表」" if zh else "Low tide: loot Tide Watch from Safe",
              "带回给地堡闸伯换「防水袋」（或留着用）" if zh else "Bring to Old Zha for Dry Bag (or keep)"],
             fill=JADE_BG, outline=JADE, title_color=JADE)

    draw_arrow(d, (355, 462), (395, 462), color=BORDER, width=3)
    draw_arrow(d, (765, 462), (805, 462), color=JADE, width=4)

    exits = [
        ("公路检查站（西北）" if zh else "Highway Checkpoint", "随时开放，站满 10 秒\n负重无限制，最稳保底" if zh else "Always open (10s)\nAny weight, safest", CARD_BG, BORDER),
        ("排水暗渠" if zh else "Drainage Culvert", "退潮露出时可走，站 6 秒\n顺路搜滩涂/挖沙船后走" if zh else "Ebb/Low exposed (6s)\nGreat after mudflats", TEAL_BG, TEAL),
        ("栈桥渡口" if zh else "Jetty Ferry", "高潮船靠岸直接走；涨/退潮\n<=14kg(带袋20kg) 抓缆 5 秒" if zh else "High: any weight\nEbb/Flood: <=14kg rope (5s)", JADE_BG, JADE),
        ("灯塔码头" if zh else "Lighthouse Pier", "点灯 (2s) 鸣笛 45s 来船\nM 图亮敌 40s，站 5 秒走" if zh else "Light (2s), wait 45s\nMap pings 40s, extract 5s", INDIGO_BG, INDIGO),
    ]
    ew, gap = 284, 19
    for idx, (t, b, fbg, fob) in enumerate(exits):
        ex1 = 42 + idx * (ew + gap)
        draw_box(d, (ex1, 562, ex1 + ew, 756), t, b.split("\n"), fill=fbg, outline=fob, title_font=FONT_BOLD)

    out = os.path.join(OUT_DIR, f"tidegate_map_{lang}.png")
    im.save(out)
    return out


def build_lastshift(lang):
    zh = (lang == "zh")
    im, d = new_canvas(
        "末班编组站：道岔停靠、签货增援与第 8 班撤离图" if zh else "Last Shift Yard: Switches, Signing & Train #8 Exit",
        "官方仓库区找「余响」免费剪票上车 → 每班约 2 分钟，车头第 2 班起正面撞到即死"
        if zh else "Board via Yuxiang in Hidden Warehouse (free) -> ~2 min/train; front engine is fatal from Train #2"
    )

    draw_box(d, (42, 120, 625, 340),
             "悬车楼司机室：扳道岔选哪边？" if zh else "Signal Tower: Switch Track",
             [
                 "• 第 1 班首班示范：等站到南护栏后发车（停 90s，先让你摸清规则）"
                 if zh else "• Train #1 Demo: departs at South Rail; stops 90s to learn",
                 "• 停靠「西台」：在棚内，离守卫近；停靠「东台」：需穿过敞坪"
                 if zh else "• West: roofed, near guards | East: cross the open yard",
                 "• 列车类型：普通 / 慢车(58s) / 重载 / 直通快车(13s冲站不停!)"
                 if zh else "• Types: Normal / Slow(58s) / Heavy / Express(13s nonstop!)",
                 "• 小技巧：进站车头正面也能撞死、撞残追你的押运，算你击杀！"
                 if zh else "• Tip: lure escorts onto rails — engine kills count for you!",
             ],
             fill=INDIGO_BG, outline=INDIGO, title_color=INDIGO)

    draw_box(d, (655, 120, 1238, 340),
             "一班车 118 秒时间轴" if zh else "118-Second Train Rhythm",
             [
                 "1. 鸣笛预告 15s -> 进站广播宣布「加价台」在哪边"
                 if zh else "1. Horn 15s -> Radio announces which side gets Bonus",
                 "2. 进站 22s（锁线不可扳道岔，车厢实心可掩护横穿中线）"
                 if zh else "2. Arrival 22s (switch locked; solid cars shield center cross)",
                 "3. 停靠卸货 40s -> 去车旁「铅封柜」签一柜货 + 车尾下押运"
                 if zh else "3. Unload 40s -> Sign sealed locker + rear escorts drop",
                 "4. 出站 16s -> 净空 25s；下一班停同侧时会扛走没搬完的旧货！"
                 if zh else "4. Exit 16s -> Clear 25s (next train hauls leftover crates!)",
             ],
             fill=AMBER_BG, outline=AMBER, title_color=AMBER)

    draw_box(d, (42, 362, 625, 560),
             "封签货与风险递增" if zh else "Signing Cargo = Better Loot, Harder Waves",
             [
                 "• 首柜 8 件（前 2 格保底 Q4+），后续每柜 4 件（头格 Q5 率 40%->70%）"
                 if zh else "• 1st sign: 8 items (2x Q4+); later: 4 (slot1 Q5 40%->70%)",
                 "• 签在「加价台」：多 1 件 + 头格 Q5 概率大涨（最高 90%）！"
                 if zh else "• Bonus side: +1 item & +20% slot1 Q5 chance (up to 90%)!",
                 "• 每多签 1 柜：下车押运 +1 人（3->5 人）、补员更快（16s->8s）"
                 if zh else "• Each sign: +1 escort (3->5), faster spawns (16s->8s)",
                 "• 签 2 柜起出小队长；打「押运长」拿押运钢印（下趟开铅封侧柜）"
                 if zh else "• 2+ signs spawn Leaders; Escort Captain drops Steel Stamp",
             ],
             fill=CARD_BG, outline=BORDER)

    draw_box(d, (655, 362, 1238, 560),
             "三大危险小队长 & 余响暗线" if zh else "3 Squad Leaders & Yuxiang's Clues",
             [
                 "• 信号工长：红灯照满 1.4s 群体点名醒全场（躲墙后/蒸汽断视线）"
                 if zh else "• Signal Master: 1.4s red beam wakes all in 40m (break LoS)",
                 "• 调车长：黄黑带放出推车（会撞死他自己手下！侧移让开）"
                 if zh else "• Shunter: launches trolley on yellow tape (hits allies too!)",
                 "• 押运班长：横推检查车当盾，躲掩体超过 2.5s 就喊两侧包抄"
                 if zh else "• Escort Sgt: mobile cover + orders 5 escorts to flank",
                 "• 找齐 3 条线索合成「末班票根」交给余响 -> 预知下班并提前扳岔"
                 if zh else "• 3 clues -> Ticket Stub -> Yuxiang pre-switches bonus track",
             ],
             fill=PURPLE_BG, outline=PURPLE, title_color=PURPLE)

    draw_box(d, (42, 582, 1238, 756),
             "第 8 班「末班车」与 5 个撤离点（出站即全站落锁封场）" if zh else "Train #8 (The Last Train) & 5 Exits",
             [
                 "• 北公路（随时开，末班车进站设重卡） | 西线检修便门（西线无车时开，封站锁） | 东台押运门（东台卸完后开，封站锁）"
                 if zh else "• North Road (always, guarded at #8) | West Gate (track clear) | East Gate (east unloaded)",
                 "• 轨道车接客亭：拉铃 14s 后发车（声音引巡逻，封站停运）"
                 if zh else "• Handcar Booth: pull bell -> 14s arrival (noise pulls patrols; stops at lockdown)",
                 "• 第 8 班末班车尾梯：末班车总是重载且必停，车尾放下可移动撤离环，车开动后跟着跑也能读完条！"
                 if zh else "• Train #8 Rear Ladder: Always heavy & stops; moving exit ring trails caboose — run alongside!",
             ],
             fill=CINNABAR_BG, outline=CINNABAR, title_color=CINNABAR,
             badge="8班后封站" if zh else "Lockdown #8", badge_bg=CINNABAR)

    out = os.path.join(OUT_DIR, f"lastshift_yard_{lang}.png")
    im.save(out)
    return out


def build_eclipse_bloom(lang):
    zh = (lang == "zh")
    im, d = new_canvas(
        "蚀日花冠：四瓣护核与 60 度扇区走位图解" if zh else "Eclipse Bloom: Petals, 60-Deg Sectors & Prominence",
        "基地接杰夫任务前往 37 号实验区目标圈（完成后约 30% 再遇）；必掉「残日盾片」"
        if zh else "Take Jeff's quest -> Zone 37 marked ring (30% chance later); 100% drops Residual Sun Shield"
    )

    d.rounded_rectangle([42, 118, 670, 756], radius=12, fill=CARD_BG, outline=BORDER, width=2)
    cx, cy = 356, 445

    r_far = 235
    for ang in range(0, 360, 12):
        d.arc([cx - r_far, cy - r_far, cx + r_far, cy + r_far], ang, ang + 7, fill=CINNABAR, width=3)
    d.text((cx - (170 if zh else 195), cy - r_far - 32),
           "超过 9.5m 放风筝 -> 落日珥 (1/2/3道)" if zh else "> 9.5m Kiting Ring -> Solar Prominences",
           font=FONT_SMALL, fill=CINNABAR)

    r_sec = 200
    for deg_start in (35, 165, 270):
        d.pieslice([cx - r_sec, cy - r_sec, cx + r_sec, cy + r_sec], deg_start, deg_start + 60,
                   fill=(242, 182, 148, 170), outline=CINNABAR, width=3)

    # 安全缺口标注放在干净无填充的右侧/左上空隙区（330°..35° 正东偏上，或 225°..270° 左上）
    d.rounded_rectangle([cx + 122, cy - 58, cx + 236, cy + 2], radius=8, fill=JADE_BG, outline=JADE, width=2)
    d.text((cx + 132, cy - 50),
           "安全缺口!\n锁定后横移" if zh else "Safe Gap!\nDodge here",
           font=FONT_TINY, fill=JADE)
    # 扇区文字放进左边空缺外侧（左下空白区外）避免踩实线
    d.rounded_rectangle([62, 616, 290, 690], radius=8, fill=CINNABAR_BG, outline=CINNABAR, width=1)
    d.text((74, 622),
           "60° 日光扇区\n前 0.45s 瞄准后锁定，再躲!" if zh else "60-Deg Sun Sector\nTurns 0.45s, then locks!",
           font=FONT_TINY, fill=CINNABAR)

    petals = [
        (0, True, "花瓣1" if zh else "P1"),
        (90, True, "花瓣2" if zh else "P2"),
        (180, False, "已碎缺口" if zh else "Broken"),
        (270, True, "花瓣4" if zh else "P4"),
    ]
    for p_deg, alive, plabel in petals:
        rad = math.radians(p_deg)
        px = cx + math.cos(rad) * 92
        py = cy + math.sin(rad) * 92
        tw_p = text_w(d, plabel, FONT_TINY)
        if alive:
            d.ellipse([px - 28, py - 28, px + 28, py + 28], fill=AMBER_BG, outline=AMBER, width=3)
            d.text((px - tw_p / 2, py - 9), plabel, font=FONT_TINY, fill=AMBER)
        else:
            d.ellipse([px - 32, py - 28, px + 32, py + 28], fill=JADE_BG, outline=JADE, width=2)
            d.text((px - tw_p / 2, py - 9), plabel, font=FONT_TINY, fill=JADE)

    d.ellipse([cx - 38, cy - 38, cx + 38, cy + 38], fill=CARD_DARK, outline=AMBER, width=4)
    ct = "核心\n7500" if zh else "Core\n7500"
    d.text((cx - 22, cy - 18), ct, font=FONT_TINY, fill=(255, 240, 210, 255))

    dx, dy = cx + 195, cy + 165
    d.ellipse([dx - 24, dy - 24, dx + 24, dy + 24], fill=CINNABAR_BG, outline=CINNABAR, width=3)
    d.ellipse([dx - 10, dy - 10, dx + 10, dy + 10], fill=CINNABAR)
    d.text((dx - 68, dy + 28), "内圈烧满落日焰" if zh else "Fills -> beam drop", font=FONT_TINY, fill=CINNABAR)

    draw_box(d, (700, 118, 1238, 290),
             "为什么要先「拆瓣」？" if zh else "Why Break Petals First?",
             [
                 "• 每片未碎花瓣让核心少受 18% 伤害；四瓣全在核心只吃 28% 伤！"
                 if zh else "• Each intact petal cuts core damage by 18% (all 4 = 28% dmg taken!)",
                 "• 击碎任意一瓣：立刻打断该瓣攻击，并留出永久安全缺口"
                 if zh else "• Breaking any petal cancels its attack & opens a safe gap",
                 "• 每次预警前 0.45 秒扇区会转过来瞄你，等锁定后再横移进缺口！"
                 if zh else "• Sectors turn to track for 0.45s; sidestep ONLY after they lock!",
             ],
             fill=AMBER_BG, outline=AMBER, title_color=AMBER)

    draw_box(d, (700, 312, 1238, 560),
             "三阶段节奏切换" if zh else "Three Combat Phases",
             [
                 "• P1（> 60% 血）：四瓣依次单扇区攻击（预警 ~1.45s）"
                 if zh else "• Phase 1 (>60% HP): Single sector casts (~1.45s telegraph)",
                 "  -> 盯住一两瓣尽快拆碎，同时注意 >9.5m 会落 1 道日珥"
                 if zh else "  -> Focus 1-2 petals early; watch 1 Prominence beyond 9.5m",
                 "• P2（60% ~ 25% 血）：花冠旋转，对向双/多扇区交替（预警 ~1.65s）"
                 if zh else "• Phase 2 (60%-25% HP): Spinning dual/multi sectors (~1.65s)",
                 "  -> 安全缺口会跟着转，>9.5m 每次多落 2 道预判日珥"
                 if zh else "  -> Gaps rotate; 2 predictive Prominences beyond 9.5m",
                 "• P3（< 25% 或四瓣全碎）：核心接管，双组对向扇区轮换（~1.8s）"
                 if zh else "• Phase 3 (<25% / 0 petals): Core takes over (~1.8s dual alternates)",
                 "  -> 贴身隔一轮也会落 3 道日珥，保持移动！"
                 if zh else "  -> Even up close spawns Prominences every other cast — keep moving!",
             ],
             fill=INDIGO_BG, outline=INDIGO, title_color=INDIGO)

    draw_box(d, (700, 582, 1238, 756),
             "专属掉落：残日盾片（图腾槽）" if zh else "100% Drop: Residual Sun Shield (Totem)",
             [
                 "• 朝来袭方向翻滚 -> 前方 120° 展开 0.65s 护盾，挡下一次直击 70%（至多 60 点）"
                 if zh else "• Roll toward strike -> 0.65s 120° shield blocks 70% (max 60 dmg)",
                 "• 挡住后 6 秒内下一发直击追加等量「余晖反击」（至多 +60 伤）；冷却 4 秒"
                 if zh else "• Grants 6s charge: next direct hit adds blocked dmg (+60 max); 4s CD",
             ],
             fill=JADE_BG, outline=JADE, title_color=JADE)

    out = os.path.join(OUT_DIR, f"eclipse_bloom_{lang}.png")
    im.save(out)
    return out


def build_mode_g(lang):
    zh = (lang == "zh")
    im, d = new_canvas(
        "宿命回响（Mode G）：九波三幕与三轴反制循环" if zh else "Fate Echo (Mode G): 9-Wave Loop & 3 Counter Axes",
        "自带装备 + 船票 + 宿命回响信物；胜利第 9 波按「决意」拿 6~10 件 Q5-Q8 极品并返还信物"
        if zh else "Current gear + Ticket + Relic -> 9 waves, 3 acts -> 6-10 Q5-Q8 drops by Resolve (+ Relic back)"
    )

    waves = [
        ("W1", "1 Boss\n建画像" if zh else "1 Boss\nProfile", CARD_BG, BORDER),
        ("W2", "2 Boss\n距离+处决" if zh else "2 Boss\nDist+Exec", TEAL_BG, TEAL),
        ("W3", "宿敌 R1\n弹药点名" if zh else "Nemesis R1\nAmmo", CINNABAR_BG, CINNABAR),
        ("W4", "1 Boss\n属性封锁" if zh else "1 Boss\nStyle", PURPLE_BG, PURPLE),
        ("W5", "3 Boss\n距离+处决" if zh else "3 Boss\nDist+Exec", TEAL_BG, TEAL),
        ("W6", "宿敌 R2\n弹药点名" if zh else "Nemesis R2\nAmmo", CINNABAR_BG, CINNABAR),
        ("W7", "1 Boss\n属性封锁" if zh else "1 Boss\nStyle", PURPLE_BG, PURPLE),
        ("W8", "3 Boss\n距离+处决" if zh else "3 Boss\nDist+Exec", TEAL_BG, TEAL),
        ("W9", "终极 R3\n弹药收尾" if zh else "Nemesis R3\nFinale", AMBER_BG, AMBER),
    ]
    ww, wgap = 118, 12
    for idx, (wt, wb, fbg, fob) in enumerate(waves):
        wx1 = 42 + idx * (ww + wgap)
        draw_box(d, (wx1, 120, wx1 + ww, 236), wt, wb.split("\n"),
                 fill=fbg, outline=fob, title_font=FONT_BOLD, body_font=FONT_TINY)
        if idx < len(waves) - 1:
            draw_arrow(d, (wx1 + ww, 178), (wx1 + ww + wgap, 178), color=BORDER, width=2)

    draw_box(d, (42, 262, 424, 558),
             "轴 1：距离回声（2 / 5 / 8 波）" if zh else "Axis 1: Distance (W2 / 5 / 8)",
             [
                 "• 看第 1 / 4 / 7 波「最后一击」距离：" if zh else "• Tracks LAST HIT distance in W1/4/7:",
                 "  贴近 (<=13m) 收尾 -> 下波须 >=18m 远打" if zh else "  Kill <=13m -> Next wave must hit >=18m",
                 "  拉远 (>13m) 收尾 -> 下波须 <=8m 贴脸打" if zh else "  Kill >13m -> Next wave must hit <=8m",
                 "• 达标：目标距离打出 >=35% 直伤，且至少占开战总血量 20%"
                 if zh else "• Pass: >=35% wave dmg & >=20% Boss HP at target range",
             ],
             fill=TEAL_BG, outline=TEAL, title_color=TEAL)

    draw_box(d, (449, 262, 831, 558),
             "轴 2：弹药点名（3 / 6 / 9 波）" if zh else "Axis 2: Ammo Ban (W3 / 6 / 9)",
             [
                 "• 第 2 / 5 / 8 波只要开火 >= 5 次就会学入高威胁弹种并禁用"
                 if zh else "• >=5 shots in W2/5/8 teaches & bans a top ammo type",
                 "• 禁令公布后：休整期、刷怪期「空放」也算违规！"
                 if zh else "• Once announced, even dry-firing banned ammo in rest FAILS!",
                 "• 解法：提前带 2~3 种口径，或切近战武器收尾得分"
                 if zh else "• Fix: pack 2-3 ammo types or swap to melee for final blow",
             ],
             fill=CINNABAR_BG, outline=CINNABAR, title_color=CINNABAR)

    draw_box(d, (856, 262, 1238, 558),
             "轴 3：属性封锁（4 / 7 波）" if zh else "Axis 3: Style Lock (W4 / 7)",
             [
                 "• 上次宿敌波（3 / 6）输出更高的一侧（枪械或近战）伤害 x0.75"
                 if zh else "• Dominant style from Nemesis (Gun or Melee) drops to x0.75",
                 "• 换另一把枪没用！必须切换到「另一侧」（枪 / 近战）"
                 if zh else "• Another gun won't dodge Gun lock — switch Gun / Melee!",
                 "• 用弱势那一侧打出 >=35% 直伤 + >=20% 总血并收尾才拿分"
                 if zh else "• Deal >=35% dmg + >=20% Boss HP & final hit with off-style",
             ],
             fill=PURPLE_BG, outline=PURPLE, title_color=PURPLE)

    draw_box(d, (42, 580, 625, 756),
             "多 Boss 波（2/5/8）：12 秒「最后处决」" if zh else "Multi-Boss (W2/5/8): 12s Final Execution",
             [
                 "• 只剩最后 1 只 Boss 时倒计时 12 秒，用枪或近战收掉 +1 决意"
                 if zh else "• Last Boss triggers 12s timer: Gun/Melee kill = +1 Resolve",
                 "• 建议先均匀压低 2~3 只血量再收；超时会回血加攻防，量力而行"
                 if zh else "• Weaken group first! Timeout heals & buffs Boss — don't throw run",
             ],
             fill=AMBER_BG, outline=AMBER, title_color=AMBER)

    draw_box(d, (655, 580, 1238, 756),
             "决意（上限 11）-> 通关第 9 波战利品数" if zh else "Resolve (Max 11) -> Wave 9 Q5-Q8 Drops",
             [
                 "• 0-2 点：6 件   |   3-5 点：7 件   |   6-8 点：8 件"
                 if zh else "• 0-2 pts: 6 items  |  3-5 pts: 7 items  |  6-8 pts: 8 items",
                 "• 9 点：9 件     |   10-11 点：10 件（Q5-Q8 极品 + 返还信物！）"
                 if zh else "• 9 pts: 9 items    |  10-11 pts: 10 items (+ Relic refunded!)",
                 "• 提示：打不过按 K 键主动放弃可保住记录，但船票与信物不退"
                 if zh else "• Press K to abandon early (no ticket/relic refund)",
             ],
             fill=JADE_BG, outline=JADE, title_color=JADE)

    out = os.path.join(OUT_DIR, f"mode_g_counter_{lang}.png")
    im.save(out)
    return out


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    outputs = []
    for builder in (build_sky_island, build_tidegate, build_lastshift, build_eclipse_bloom, build_mode_g):
        for lang in ("zh", "en"):
            outputs.append(builder(lang))
    print(f"gen_wiki_diagrams: PASS - generated {len(outputs)} diagrams")
    return 0


if __name__ == "__main__":
    sys.exit(main())
