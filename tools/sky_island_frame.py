"""天空岛布局 v2 的坐标框架：旧版手写坐标 → 当前岛位。

2026-09-10 起岛群整体挪近、主岛缩小（岛与桥的 ID、两端和 5 道门语义不变）。聚落、地标、
铺路、布景里有大量按 2026-09-08 版岛位手写的坐标；它们保持原写法，统一经这里换算到当前
岛位。同一件东西的导航障碍与可见模型都走同一个换算，才不会在两份数据之间错位。

当前岛位有两种绑定方式，模块本身不导入 shapely：
- 导航生成器：bind_specs(ISLAND_SPECS, BRIDGE_SPECS)；
- Blender 侧脚本（聚落、世界、布景、Tripo 摆放）：bind_layout(已生成的 layout)。

- relocate(region, x, z)：旧岛内的绝对坐标 → 新岛内同一相对位置（按岛尺寸逐轴缩放）。
- relocate_y(region, y)：旧岛高度基准上的绝对高度 → 新岛高度基准（离地高度不变）。
- scale_offset / scale_radius：相对岛心的偏移或半径，按同一比例缩放。
- remap_bridge_mouth(point)：旧桥口附近的点 → 同一座桥同一端的新桥口（铺路端点用）。

分类 COMPAT：只被离线生成工具导入，不进游戏运行时。
"""

import math

# 写进聚落规划文件；导航读到旧参照系的规划会整份忽略，走「导航 → 聚落 → 导航」自举。
FRAME_VERSION = "skyisland-layout-v2-2026-09-10"

# 2026-09-08 版岛位：id -> (中心 X, 高度 Y, 中心 Z, 尺寸 X, 尺寸 Z)。只作手写坐标的参照系，不要改。
LEGACY_ISLANDS = {
    "A": (0, 0, -300, 130, 100), "B": (0, 8, -130, 190, 150), "C": (-230, 16, -110, 190, 170),
    "D": (-250, 28, 110, 190, 180), "E": (0, 42, 110, 160, 150), "F": (240, 26, -40, 190, 180),
    "G": (250, 44, 180, 170, 150), "H": (0, 62, 300, 180, 150), "S1": (-350, 12, -230, 60, 50),
    "S2": (-370, 24, 220, 55, 55), "S3": (355, 18, -170, 65, 65), "S4": (350, 52, 310, 65, 65),
}

# 2026-09-08 版桥口：桥 id -> (from 端 XZ, to 端 XZ)。
LEGACY_BRIDGE_MOUTHS = {
    "AB": ((0, -250), (0, -205)), "BC": ((-95, -110), (-135, -110)), "CD": ((-250, -25), (-250, 20)),
    "DE": ((-155, 110), (-80, 110)), "BF": ((95, -120), (145, -40)), "FG": ((230, 50), (285, 105)),
    "GE": ((165, 180), (80, 110)), "EH": ((40, 185), (90, 270)), "CS1": ((-325, -150), (-350, -205)),
    "DS2": ((-345, 140), (-370, 192.5)), "FS3": ((335, -80), (355, -137.5)), "GS4": ((335, 210), (350, 277.5)),
    "K1": ((-210, 20), (-60, -55)), "K2": ((205, 105), (55, -55)), "K3": ((-25, -55), (-80, 80)),
}

_islands = {}
_mouths = {}


def bind_specs(island_specs, bridge_specs):
    _islands.clear()
    _mouths.clear()
    for sid, _name, center, size, _theme in island_specs:
        _islands[sid] = (tuple(center), tuple(size))
    for bid, _first, _last, _width, points in bridge_specs:
        _mouths[bid] = (tuple(points[0]), tuple(points[-1]))


def bind_layout(layout):
    _islands.clear()
    _mouths.clear()
    for island in layout["islands"]:
        _islands[island["id"]] = (tuple(island["center"]), tuple(island["size"]))
    for bridge in layout["bridges"]:
        first, last = bridge["path"][0], bridge["path"][-1]
        _mouths[bridge["id"]] = ((first[0], first[2]), (last[0], last[2]))


def _current(region):
    if region not in _islands:
        raise RuntimeError("sky_island_frame 尚未绑定当前岛位：先调用 bind_specs 或 bind_layout")
    return _islands[region]


def island_scale(region):
    _lx, _ly, _lz, lw, ld = LEGACY_ISLANDS[region]
    _center, (width, depth) = _current(region)
    return width / lw, depth / ld


def relocate(region, x, z):
    lx, _ly, lz, lw, ld = LEGACY_ISLANDS[region]
    (cx, _cy, cz), (width, depth) = _current(region)
    return (cx + (x - lx) * width / lw, cz + (z - lz) * depth / ld)


def relocate_y(region, y):
    return y - LEGACY_ISLANDS[region][1] + _current(region)[0][1]


def scale_offset(region, dx, dz):
    sx, sz = island_scale(region)
    return dx * sx, dz * sz


def scale_radius(region, radius):
    sx, sz = island_scale(region)
    return radius * (sx + sz) / 2


def scale_radius_for_island(island, radius):
    """不依赖全局绑定：直接用 layout 里这座岛的尺寸算（离线测试也能构造 PlantingSpace）。"""
    _lx, _ly, _lz, lw, ld = LEGACY_ISLANDS[island["id"]]
    width, depth = island["size"]
    return radius * (width / lw + depth / ld) / 2


def remap_bridge_mouth(point, tolerance=4.0):
    for bid, ends in LEGACY_BRIDGE_MOUTHS.items():
        for index, legacy in enumerate(ends):
            if math.dist(point, legacy) <= tolerance:
                return tuple(_mouths[bid][index])
    return None
