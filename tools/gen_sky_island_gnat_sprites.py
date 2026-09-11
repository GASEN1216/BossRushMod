#!/usr/bin/env python3
"""天空岛「云蚋」（夜里的蚊群）16×16 像素画：确定性地按像素网格画，不走生图接口。

python tools/gen_sky_island_gnat_sprites.py [--check] [--preview DIR]

产物 Assets/ui/SkyIsland/sky_island_gnat_sheet.png：7 帧横排 112×16，RGBA，由正式编译脚本随 Mod 部署（该目录已在部署段里）。
帧序与 `SkyIslandGnats` 的帧常量一致：0–2 振翅、3 冲刺前摇（身体发亮、翅膀竖起）、4 冲刺拉伸、5 叮咬（肚子吸红）、6 被拍死的血点。
朝向：头朝 +x（贴图右侧）；运行时按屏幕上的运动方向翻转或旋转。世界尺寸约 0.48 m（PPU = 16 / 0.48），1080p 下约 32 px。

画法只有像素中心落在几何形状里、按固定顺序叠层，没有抗锯齿与随机数：同一份脚本每次输出逐字节一致。
1 px 深色描边由程序补上（任何填色像素的上下左右只要挨着透明就补一格描边），保证夜里暗地面上也看得清轮廓。
`--check` 在内存里重画一遍并校验：尺寸、帧数、四角透明、描边完整、描边够暗、各帧互不相同、血点与前摇的颜色在位；
磁盘上已有产物时再核对与重画结果逐像素一致。守卫 `tests/SkyIslandMosquitoGuard.py` 直接 import 本脚本复用这套检查。
"""
import argparse
import math
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/ui/SkyIsland/sky_island_gnat_sheet.png"
FRAME = 16
FRAME_NAMES = ("fly_up", "fly_mid", "fly_down", "windup", "dash", "bite", "splat")

OUTLINE = (24, 22, 34, 255)
BODY = (205, 220, 230, 255)
SHADE = (132, 150, 170, 255)
WING = (225, 240, 255, 140)
EYE = (235, 70, 55, 255)
BLOOD = (178, 32, 44, 255)
BLOOD_DARK = (104, 14, 26, 255)
FLASH = (255, 250, 214, 255)
CLEAR = (0, 0, 0, 0)


def inside_ellipse(px, py, cx, cy, rx, ry, angle_deg=0.0):
    a = math.radians(angle_deg)
    dx, dy = px - cx, py - cy
    u = dx * math.cos(a) + dy * math.sin(a)
    v = -dx * math.sin(a) + dy * math.cos(a)
    return (u / rx) ** 2 + (v / ry) ** 2 <= 1.0


def paint_ellipse(grid, cx, cy, rx, ry, color, shade=None, angle=0.0):
    for y in range(FRAME):
        for x in range(FRAME):
            if inside_ellipse(x + 0.5, y + 0.5, cx, cy, rx, ry, angle):
                grid[y][x] = shade if shade is not None and y + 0.5 > cy + 0.45 else color


def paint_wing(grid, angle_deg, length=2.8, rx=3.2, ry=0.9, attach=(9.0, 6.4)):
    a = math.radians(angle_deg)
    cx, cy = attach[0] + math.cos(a) * length, attach[1] + math.sin(a) * length
    paint_ellipse(grid, cx, cy, rx, ry, WING, angle=angle_deg)


def paint_dark(grid, points):
    """腿与口器：细线，只画在透明处（不压住身体）。"""
    for x, y in points:
        if grid[y][x] == CLEAR:
            grid[y][x] = OUTLINE


def outline(grid):
    fills = [[grid[y][x] not in (CLEAR, OUTLINE) for x in range(FRAME)] for y in range(FRAME)]
    for y in range(FRAME):
        for x in range(FRAME):
            if grid[y][x] != CLEAR:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < FRAME and 0 <= ny < FRAME and fills[ny][nx]:
                    grid[y][x] = OUTLINE
                    break


def gnat(wing_angle, body_color=BODY, shade=SHADE, stretch=0.0, drop=0.0, engorged=False, head_down=False):
    """蚋的侧影：驼起的胸、分节下垂的细长腹、小头、长口器、三条细长腿、一片窄长的半透明翅。"""
    grid = [[CLEAR] * FRAME for _ in range(FRAME)]
    if wing_angle is not None:
        paint_wing(grid, wing_angle, attach=(9.0, 6.4 + drop), rx=3.2 + stretch * 0.5, ry=0.9 - stretch * 0.25)
    belly, belly_shade = (BLOOD, BLOOD_DARK) if engorged else (body_color, shade)
    # 冲刺拉伸只往后拉到离帧边还剩一格：描边要落在帧里。
    belly_x = 5.2 - stretch * 0.1
    paint_ellipse(grid, belly_x, 9.0 + drop - stretch * 0.4, 3.0 + stretch * 0.75, 1.9 if engorged else 1.25 - stretch * 0.3,
                  belly, belly_shade, angle=-20.0 * (1.0 - stretch * 0.55))
    if not engorged:
        # 腹节：隔一列压一格暗色，远看是一截一截的，而不是一根棍。
        for x in (3, 5, 7):
            for y in range(FRAME):
                if grid[y][x] == body_color:
                    grid[y][x] = shade
                    break
    paint_ellipse(grid, 9.0 + stretch * 0.3, 7.8 + drop + stretch * 0.5, 1.8 + stretch * 0.4, 1.9 - stretch * 0.6,
                  body_color, shade)
    head_x, head_y = (11.8, 9.4 + drop) if head_down else (11.6 + stretch * 1.0, 7.6 + drop + stretch * 0.5)
    paint_ellipse(grid, head_x, head_y, 1.0, 1.0, body_color)
    grid[int(head_y - 0.5)][int(head_x + 0.5)] = EYE
    if head_down:
        paint_dark(grid, [(12, int(10 + drop) + 1), (12, int(10 + drop) + 2), (13, int(10 + drop) + 3)])
    else:
        mouth_y = int(8 + drop + stretch * 0.5)
        paint_dark(grid, [(int(13 + stretch), mouth_y), (int(14 + stretch * 0.5), mouth_y + 1)])
    if stretch == 0.0:
        legs_y = int(9 + drop)
        paint_dark(grid, [(8, legs_y + 1), (7, legs_y + 2), (6, legs_y + 3), (5, legs_y + 4),
                          (9, legs_y + 1), (9, legs_y + 2), (9, legs_y + 3), (8, legs_y + 4),
                          (10, legs_y + 1), (11, legs_y + 2), (12, legs_y + 3), (13, legs_y + 4)])
    outline(grid)
    return grid


def splat():
    grid = [[CLEAR] * FRAME for _ in range(FRAME)]
    paint_ellipse(grid, 7.6, 8.4, 2.4, 2.0, BLOOD)
    paint_ellipse(grid, 7.4, 8.6, 1.1, 0.9, BLOOD_DARK)
    for x, y in ((3, 6), (12, 5), (11, 12), (4, 11), (13, 9)):
        grid[y][x] = BLOOD
    outline(grid)
    return grid


def frames():
    return [
        gnat(-120.0),
        gnat(-165.0),
        gnat(160.0),
        gnat(-90.0, body_color=FLASH, shade=FLASH, drop=1.0),
        gnat(178.0, stretch=1.6),
        gnat(-105.0, engorged=True, head_down=True),
        splat(),
    ]


def render():
    sheet = Image.new("RGBA", (FRAME * len(FRAME_NAMES), FRAME), CLEAR)
    for index, grid in enumerate(frames()):
        for y in range(FRAME):
            for x in range(FRAME):
                sheet.putpixel((index * FRAME + x, y), grid[y][x])
    return sheet


def frame_pixels(sheet, index):
    return [[sheet.getpixel((index * FRAME + x, y)) for x in range(FRAME)] for y in range(FRAME)]


def check_sheet(sheet):
    """返回错误列表（空表示通过）。守卫直接调用。"""
    errors = []
    if sheet.mode != "RGBA" or sheet.size != (FRAME * len(FRAME_NAMES), FRAME):
        return ["精灵表必须是 %dx%d RGBA，实际 %s %r" % (FRAME * len(FRAME_NAMES), FRAME, sheet.mode, sheet.size)]
    seen = []
    widths = {}
    for index, name in enumerate(FRAME_NAMES):
        px = frame_pixels(sheet, index)
        opaque = [(x, y) for y in range(FRAME) for x in range(FRAME) if px[y][x][3] > 0]
        if len(opaque) < 12:
            errors.append("帧 %s 几乎是空的（%d 个像素）" % (name, len(opaque)))
            continue
        for cx, cy in ((0, 0), (FRAME - 1, 0), (0, FRAME - 1), (FRAME - 1, FRAME - 1)):
            if px[cy][cx][3] != 0:
                errors.append("帧 %s 的角 (%d,%d) 不透明" % (name, cx, cy))
        outline_count = 0
        for x, y in opaque:
            color = px[y][x]
            if color == OUTLINE:
                outline_count += 1
                continue
            if x in (0, FRAME - 1) or y in (0, FRAME - 1):
                errors.append("帧 %s 的填色像素 (%d,%d) 贴着帧边，描边没有位置" % (name, x, y))
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if px[y + dy][x + dx][3] == 0:
                    errors.append("帧 %s 的填色像素 (%d,%d) 直接挨着透明：缺描边" % (name, x, y))
                    break
        luma = 0.299 * OUTLINE[0] + 0.587 * OUTLINE[1] + 0.114 * OUTLINE[2]
        if outline_count < 12 or luma >= 60:
            errors.append("帧 %s 的深色描边不足（%d 格，亮度 %.0f）" % (name, outline_count, luma))
        colors = {px[y][x] for x, y in opaque}
        if name in ("bite", "splat") and BLOOD not in colors:
            errors.append("帧 %s 没有血色" % name)
        if name == "windup" and FLASH not in colors:
            errors.append("前摇帧必须发亮（缺 FLASH 色），否则冲刺之前没有可见的预兆")
        xs = [x for x, _ in opaque]
        widths[name] = max(xs) - min(xs) + 1
        key = tuple(tuple(row) for row in px)
        if key in seen:
            errors.append("帧 %s 与前面某一帧完全相同" % name)
        seen.append(key)
    if widths.get("dash", 0) <= widths.get("fly_mid", 99):
        errors.append("冲刺帧必须比振翅帧更长（拉伸），实际 %r" % widths)
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="只校验，不写产物")
    parser.add_argument("--preview", help="另存 8 倍放大的暗底 / 亮底预览到这个目录（本地看效果用）")
    args = parser.parse_args()
    sheet = render()
    errors = check_sheet(sheet)
    if errors:
        print("FAIL\n  - " + "\n  - ".join(errors))
        return 1
    if not args.check:
        OUT.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(OUT)
    if OUT.exists():
        on_disk = Image.open(OUT).convert("RGBA")
        if on_disk.size != sheet.size or on_disk.tobytes() != sheet.tobytes():
            print("FAIL 磁盘上的 %s 与脚本重画结果不一致（手改过或脚本改了没重跑）" % OUT.relative_to(ROOT))
            return 1
    if args.preview:
        out_dir = Path(args.preview)
        out_dir.mkdir(parents=True, exist_ok=True)
        big = sheet.resize((sheet.width * 8, sheet.height * 8), Image.NEAREST)
        for label, ground in (("dark", (30, 34, 46, 255)), ("light", (196, 184, 150, 255))):
            canvas = Image.new("RGBA", big.size, ground)
            canvas.alpha_composite(big)
            canvas.save(out_dir / ("gnat_preview_%s.png" % label))
    print("PASS sky_island_gnat_sheet %dx%d, %d frames" % (sheet.width, sheet.height, len(FRAME_NAMES)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
