"""天空岛「当前区域」判定的离线几何复算；只用标准库，不依赖 Blender、Unity 或游戏进程。

【口径】会话按玩家脚下那块地判定区域：生成器把地面按区域切成 `COL_Ground_{区域}` 碰撞体
（12 个岛 + 15 座桥，tools/generate_sky_island.py），会话每 0.2 秒朝脚下打一条地面射线
（起点 +0.25 m、向下 1.4 m），命中的碰撞体名经 `SkyIslandStoryService.GroundRegionOf` 解析：
岛返回区域 id，桥返回 null（保持上一个区域）。

【为什么要复算】旧口径是「离最近的 POI_ 地标不到 60 米」。按 ArtSource/SkyIsland/layout.json 的
可走导航网格算，主岛上只有 30–53% 的面积落在本岛地标 60 米内，其余地方 HUD 卡片停在上一个岛；
而 CS1 / FS3 两座桥的大半段、C / F 两岛边缘又落在 S1 / S3 的 60 米内，隔着桥就点亮了支路迷雾
并推进「巡视群岛区域」。代码注释当年的论证只量了「主岛侧桥头到支路地标」的距离，没量岛边缘与桥身。

【本测试断言】
1. 生成器确实按 ground.triangleRegions 给地面碰撞体命名（改名或合并成一块，会话的区域表就是空的）。
2. 区域表：layout 里 12 个岛的 id 恰好是 C# `RegionBit` 登记的 12 个区域，15 座桥一个都不在其中。
3. 逐个导航三角形模拟运行时那条地面射线：岛上的可走面必须解析到**本岛**，桥上的可走面必须解析到桥或
   「null」，任何主岛与桥面都不得解析到 S1–S4（支路只能在支路岛上点亮）。
4. 反向：把一块岛面的区域标签改成支路、或把生成器的命名前缀改掉，检查器必须转红。

另外打印旧 60 米口径在同一份几何上的错判面积，作为这次改动的量化依据（只打印，不做断言）。
"""
from collections import defaultdict
import copy
import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LAYOUT = ROOT / "ArtSource/SkyIsland/layout.json"
GENERATOR = ROOT / "tools/generate_sky_island.py"
STORY_SERVICE = ROOT / "DebugAndTools/SkyIsland/SkyIslandStoryService.cs"

RAY_UP = 0.25      # SkyIslandSession.Update：player.position + up * 0.25
RAY_LENGTH = 1.4   # 向下 1.4 m
CELL = 12.0


def region_bits():
    source = STORY_SERVICE.read_text(encoding="utf-8-sig")
    body = source.split("internal static int RegionBit(string id)", 1)[1].split("default:", 1)[0]
    return {name: int(bit) for name, bit in re.findall(r'case "(\w+)": return (\d+);', body)}


def generator_names_ground_by_region(generator_source):
    return re.search(r"create_object\('COL_Ground_'\+str\(region\)", generator_source) is not None


def tri_area_xz(a, b, c):
    return abs((b[0] - a[0]) * (c[2] - a[2]) - (b[2] - a[2]) * (c[0] - a[0])) / 2


def barycentric_y(p, a, b, c):
    """p=(x,z)；若落在三角形 XZ 投影内返回插值高度，否则 None。"""
    det = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
    if abs(det) < 1e-12:
        return None
    l1 = ((b[2] - c[2]) * (p[0] - c[0]) + (c[0] - b[0]) * (p[1] - c[2])) / det
    l2 = ((c[2] - a[2]) * (p[0] - c[0]) + (a[0] - c[0]) * (p[1] - c[2])) / det
    l3 = 1 - l1 - l2
    eps = -1e-7
    if l1 < eps or l2 < eps or l3 < eps:
        return None
    return l1 * a[1] + l2 * b[1] + l3 * c[1]


def build_index(ground):
    vertices = ground["vertices"]
    index = defaultdict(list)
    for face_id, face in enumerate(ground["triangles"]):
        xs = [vertices[v][0] for v in face]
        zs = [vertices[v][2] for v in face]
        for cx in range(int(math.floor(min(xs) / CELL)), int(math.floor(max(xs) / CELL)) + 1):
            for cz in range(int(math.floor(min(zs) / CELL)), int(math.floor(max(zs) / CELL)) + 1):
                index[(cx, cz)].append(face_id)
    return index


def ray_region(point, ground, index):
    """模拟会话那条地面射线：返回命中的地面三角形所属区域标签（最高的那一面），没命中返回 None。"""
    vertices = ground["vertices"]
    x, y, z = point
    best_y, best_region = None, None
    for face_id in index.get((int(math.floor(x / CELL)), int(math.floor(z / CELL))), ()):
        a, b, c = (vertices[v] for v in ground["triangles"][face_id])
        hit_y = barycentric_y((x, z), a, b, c)
        if hit_y is None or hit_y > y + RAY_UP or hit_y < y + RAY_UP - RAY_LENGTH:
            continue
        if best_y is None or hit_y > best_y:
            best_y, best_region = hit_y, str(ground["triangleRegions"][face_id])
    return best_region


def check(layout, generator_source, bits):
    errors = []
    if not generator_names_ground_by_region(generator_source):
        errors.append("生成器没有按区域给地面碰撞体命名（COL_Ground_{区域}）：会话的区域表会是空的")
    islands = {str(i["id"]) for i in layout["islands"]}
    bridges = {str(b["id"]) for b in layout["bridges"]}
    registered = {name for name, bit in bits.items() if bit != 0}
    if islands != registered:
        errors.append("RegionBit 登记的区域 %s 与 layout 的岛 %s 不一致" % (sorted(registered), sorted(islands)))
    leaked = sorted(bridges & registered)
    if leaked:
        errors.append("桥被登记成了区域（走在桥上会点亮/记账）：%s" % leaked)
    side = {name for name in registered if name.startswith("S")}

    nav, ground = layout["navigation"], layout["ground"]
    index = build_index(ground)
    area = defaultdict(float)
    own = defaultdict(float)
    wrong = defaultdict(float)
    side_leaks = defaultdict(float)
    for tri, label in zip(nav["triangles"], nav["triangleRegions"]):
        label = str(label)
        a, b, c = (nav["vertices"][v] for v in tri)
        centroid = [(a[k] + b[k] + c[k]) / 3 for k in range(3)]
        tri_area = tri_area_xz(a, b, c)
        area[label] += tri_area
        hit = ray_region(centroid, ground, index)
        resolved = hit if hit in registered else None
        if label in registered:
            if resolved == label:
                own[label] += tri_area
            else:
                wrong[label] += tri_area
        elif resolved is not None:
            # 桥面只允许解析到「无区域」；解析到岛只可能是桥头压在岛边缘上，量很小时允许，但绝不能是支路。
            wrong[label] += tri_area
        if resolved in side and label not in side:
            side_leaks[label] += tri_area

    for label in sorted(registered):
        total = area.get(label, 0.0)
        if total <= 0:
            errors.append("岛 %s 在导航网格上没有可走面" % label)
            continue
        ratio = own[label] / total
        if ratio < 0.995:
            errors.append("岛 %s 只有 %.1f%% 的可走面解析到本岛（应 ≥ 99.5%%）" % (label, ratio * 100))
    for label in sorted(bridges):
        total = area.get(label, 0.0)
        if total > 0 and wrong[label] / total > 0.02:
            errors.append("桥 %s 有 %.1f%% 的可走面被解析成了某个岛（应 ≤ 2%%）" % (label, wrong[label] / total * 100))
    for label, leak in sorted(side_leaks.items()):
        errors.append("%s 上有 %.0f m² 的可走面被解析成支路区域（支路只能在支路岛上点亮）" % (label, leak))
    return errors, area, own


def legacy_rule_report(layout):
    """旧口径「离最近 POI_ 地标 < 60 米」在同一份几何上的表现（只打印）。"""
    pois = {m["id"][4:]: m["position"] for m in layout["markers"] if m["id"].startswith("POI_")}
    nav = layout["navigation"]
    stale = defaultdict(float)
    total = defaultdict(float)
    side_early = defaultdict(float)
    for tri, label in zip(nav["triangles"], nav["triangleRegions"]):
        label = str(label)
        a, b, c = (nav["vertices"][v] for v in tri)
        centroid = [(a[k] + b[k] + c[k]) / 3 for k in range(3)]
        tri_area = tri_area_xz(a, b, c)
        total[label] += tri_area
        nearest, distance = None, 1e18
        for name, position in pois.items():
            d = math.dist(centroid, position)
            if d < distance:
                nearest, distance = name, d
        inside = distance < 60
        if label in pois and not (inside and nearest == label):
            stale[label] += tri_area
        if inside and nearest.startswith("S") and nearest != label:
            side_early[label] += tri_area
    lines = []
    for label in sorted(pois):
        if total[label]:
            lines.append("%s 卡片停在上一个岛 %.0f%%" % (label, stale[label] / total[label] * 100))
    early = ", ".join("%s %.0f%%" % (k, v / total[k] * 100) for k, v in sorted(side_early.items()))
    return "; ".join(lines) + " | 隔着桥提前点亮支路：" + (early or "无")


def main():
    layout = json.loads(LAYOUT.read_text(encoding="utf-8"))
    generator_source = GENERATOR.read_text(encoding="utf-8")
    bits = region_bits()
    if len(bits) < 12:
        raise SystemExit("SkyIslandRegionResolutionPropertyTest: FAIL\n找不到 RegionBit 的区域登记表")

    errors, area, own = check(layout, generator_source, bits)

    # ---- 反向：检查器必须真的会红 ----
    broken = copy.deepcopy(layout)
    regions = broken["ground"]["triangleRegions"]
    victims = [i for i, label in enumerate(regions) if str(label) == "C"]
    for i in victims[: max(1, len(victims) // 3)]:
        regions[i] = "S1"
    probe_errors, _, _ = check(broken, generator_source, bits)
    if not any("支路" in e or "只有" in e for e in probe_errors):
        errors.append("反向验证失败：把 C 岛三分之一地面改标成 S1，检查器没有转红")
    probe_errors, _, _ = check(layout, generator_source.replace("'COL_Ground_'+str(region)", "'COL_Ground'"), bits)
    if not any("命名" in e for e in probe_errors):
        errors.append("反向验证失败：生成器命名前缀被改掉，检查器没有转红")
    renamed = dict(bits)
    renamed["AB"] = 4096
    probe_errors, _, _ = check(layout, generator_source, renamed)
    if not any("桥被登记" in e for e in probe_errors):
        errors.append("反向验证失败：把桥 AB 登记成区域，检查器没有转红")

    covered = ", ".join("%s %.1f%%" % (k, own[k] / area[k] * 100) for k in sorted(own) if area[k])
    if errors:
        print("SkyIslandRegionResolutionPropertyTest: FAIL")
        for error in errors:
            print("  - " + error)
        raise SystemExit(1)
    print("SkyIslandRegionResolutionPropertyTest: PASS（脚下地面判定：%s；旧 60 米口径对照：%s）"
          % (covered, legacy_rule_report(layout)))


if __name__ == "__main__":
    main()
