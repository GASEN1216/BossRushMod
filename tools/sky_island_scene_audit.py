"""天空岛场景缺陷审计：路面净空、穿模、接地、重复实例、门口 / 桥头 / 交互点净空。

输入是 `generate_sky_island.py --audit-ledger <目录>` 写出的实例台账（每件东西的真实世界网格），
以及 `ArtSource/SkyIsland/layout.json`、`road_layout.json`、运行时摆放表。
判据全部按实际网格：每件东西取离地 0.06–2.2 m「身体层」的三角形，按平面切开后投到 XZ，
不用中心点、名义半径或登记包围盒。

用法（需要 Shapely 2.1 与 numpy，放在 Build/sky-island-python-deps）：
    PYTHONPATH=Build/sky-island-python-deps python tools/sky_island_scene_audit.py \
        --ledger Build/sky-scene-audit-20260930/ledger_before --out Build/sky-scene-audit-20260930/before

输出 `defects.json`（逐条缺陷）与 `summary.json`（按区域 / 类型计数）；`--compact` 另写一份可进仓库、
不依赖 Blender 的实例脚印表，供 `tests/SkyIslandSceneCleanlinessPropertyTest.py` 复算。
"""
import argparse
import collections
import hashlib
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Build/sky-island-python-deps'))
sys.path.insert(0, str(ROOT / 'tools'))

import numpy as np  # noqa: E402
import shapely  # noqa: E402
from shapely.geometry import Point, Polygon, box as shp_box  # noqa: E402

BODY_LOW, BODY_HIGH = 0.06, 2.2          # 玩家身体层（离地）
BANDS = [(0.06, 0.6), (0.6, 1.3), (1.3, 2.2), (2.2, 3.2), (3.2, 4.5), (4.5, 6.0), (6.0, 8.0),
         (8.0, 11.0), (11.0, 15.0), (15.0, 20.0), (20.0, 30.0), (30.0, 45.0)]
TRUNK_BANDS = 3                           # 树只有前三层（≤2.2 m）算硬的树干与板根；树冠是软的
DECAL_HEIGHT = 0.13                       # 全部几何低于地面 +0.13 m 的是贴地嵌饰（星纹地面、水纹），按面处理
# 有意悬空的东西：钟庭悬浮光环、系在码头外的飞艇……在这里逐项登记，不靠放宽判据。
INTENTIONAL_FLOAT = {('area_landmarks', 'H'): 'hovering fractured halo / arcade ring (H bell court)',
                     ('dock_landmark', 'A'): 'moored sky skiff beyond the dock rim'}
# 有正面的件：前脸必须朝路或广场。
FRONT_MODELS = {'cottage_large', 'cottage_dormer', 'cottage_small', 'farm_barn', 'market_stall', 'shop_sign',
                'cherry_pavilion', 'shrine_pavilion', 'pergola', 'stone_bench', 'bell_arch',
                'village_house_a', 'village_house_b', 'village_house_c', 'dock_house', 'tea_house', 'mill',
                'barn', 'workshop_shed', 'temple', 'pavilion_hall', 'post_house', 'workshop_dome'}
ROAD_CURB = 0.2                           # 硬物离路缘的净空，与道路规划 CLEARANCE 同值
SOFT_ROAD_EROSION = 0.15                  # 柔性植被允许搭在路缘 0.15 m 内
MIN_AREA = 0.01
# 花草也要让开的净空（与 tools/sky_island_prop_placement.py 的 SOFT_BLOCKING 同一口径）。
SOFT_ZONE_KINDS = {'bridge_mouth', 'doorway', 'search', 'lamp', 'runtime_loot_crate', 'runtime_bounty_crate',
                   'runtime_memorial_stake', 'runtime_hearth', 'runtime_gather_node', 'runtime_search_point',
                   'runtime_bridge_sign_post', 'runtime_story_gate'}

# 这些是「面」而不是「东西」：路、广场铺装、贴地细节、地面。
SURFACE_KINDS = {'road', 'paving'}
SKY_CALLERS = {'sky_festival', 'world_clouds'}


def load_ledger(folder):
    folder = Path(folder)
    meta = json.loads((folder / 'instances.json').read_text(encoding='utf-8'))
    arrays = np.load(folder / 'geometry.npz')
    return meta, arrays['vertices'].astype(np.float64), arrays['triangles'], arrays['owner']


def island_lookup(layout):
    islands = {}
    for island in layout['islands']:
        islands[island['id']] = {'id': island['id'], 'height': island['height'],
                                 'poly': Polygon(island['outline']), 'center': island['center']}
    bridge_tris = []
    for bridge in layout['bridges']:
        for tri in bridge['surfaceTriangles']:
            bridge_tris.append((bridge['id'], Polygon([(p[0], p[2]) for p in tri]), tri))
    return islands, bridge_tris


def ground_at(islands, bridge_tris, x, z):
    """(区域 id, 地面高度)；岛面按岛高，桥面按三角形插值；都不在返回 (None, None)。"""
    p = Point(x, z)
    for island in islands.values():
        if island['poly'].covers(p):
            return island['id'], float(island['height'])
    for bid, poly, tri in bridge_tris:
        if poly.covers(p):
            (x0, y0, z0), (x1, y1, z1), (x2, y2, z2) = tri
            det = (z1 - z2) * (x0 - x2) + (x2 - x1) * (z0 - z2)
            if abs(det) < 1e-12:
                return bid, float(max(y0, y1, y2))
            a = ((z1 - z2) * (x - x2) + (x2 - x1) * (z - z2)) / det
            b = ((z2 - z0) * (x - x2) + (x0 - x2) * (z - z2)) / det
            return bid, float(a * y0 + b * y1 + (1 - a - b) * y2)
    # 护栏线上的件（中继平台的护栏灯）中心在桥面外几十厘米：取 0.6 m 内最近的桥面三角形高度。
    near = min(((poly.distance(p), bid, tri) for bid, poly, tri in bridge_tris), default=None, key=lambda r: r[0])
    if near is not None and near[0] <= .6:
        return near[1], float(max(v[1] for v in near[2]))
    return None, None


def callers_of(record):
    return set(record.get('chain') or []) | {record['caller']}


def classify(record):
    """把一件东西归到判据类：surface / sky / cliff / flora / tree / hard / rail。按调用链判断，闭包也追得到外层。"""
    kind, region = record['kind'], str(record.get('region', ''))
    callers = callers_of(record)
    model = (record.get('model') or '')
    if kind in SURFACE_KINDS:
        return 'surface'
    if kind == 'cloud' or callers & SKY_CALLERS or kind == 'sky_lantern' or region.startswith('Distant'):
        return 'sky'
    if kind in ('cliff', 'vine') or region.endswith(('_Cliff', '_Waterfall')) or callers & {'floating_gardens', 'cliff_dressing'}:
        return 'cliff'
    if model in ('cliff_vine', 'waterfall', 'distant_islet_a', 'distant_islet_b', 'distant_islet_c',
                 'cliff_chunk_a', 'cliff_chunk_b', 'cliff_chunk_c', 'cliff_shrub_cap'):
        return 'cliff'
    if region.endswith('_GroundDetail') or 'bridge_details' in callers or ('relay_platforms' in callers and kind == 'part'):
        return 'surface'
    if 'terrain_and_boundary' in callers:
        return 'rail'
    if kind == 'registered' and 'tree' in (record.get('obstacleKind') or ''):
        return 'tree'
    if kind in ('nature', 'garden', 'lotus') or (callers & {'meadows', 'finish_gardens'} and kind == 'part'):
        return 'flora'
    if region.endswith('_Flora'):
        return 'flora'
    if model.startswith(('bush_', 'flower_', 'fern_', 'coral_', 'lavender_')):
        return 'flora'
    if kind == 'tree' or model.startswith('tree_') or model in ('cherry_tree', 'great_tree'):
        return 'tree'
    if kind == 'part' and callers & {'scatter_flowers', 'scatter_bush'}:
        return 'flora'
    return 'hard'


# 设计上就接在一起的两件（不是穿模）：理由逐条写，审计报告里原样列出。
DESIGNED_CONTACT = {
    frozenset(('D_GreatRootTree', 'area_landmarks@D')): 'root arch rises out of the great root tree (area_landmarks 根拱两脚落在巨树上)',
    frozenset(('D_RootTree02', 'area_landmarks@D')): 'root arch rises out of the second root tree',
    frozenset(('F_MirrorPool', 'F_PavilionSupport03')): 'waterside pavilion pillar stands on the mirror-pool rim (layout contract)',
    frozenset(('F_MirrorPool', 'F_PavilionSupport05')): 'waterside pavilion pillar stands on the mirror-pool rim (layout contract)',
}


def contact_key(thing):
    return thing.get('obstacleId') or (thing['caller'] + '@' + str(thing['region']))


# 按类别登记的设计接触：护栏灯坐在护栏上梁顶（generate_sky_island.relay_platforms / rail_lamp）。
DESIGNED_KIND_CONTACT = {frozenset(('rail_lamp', 'rail')): 'relay lamp sits on the rail top beam (rail_lamp)'}


def designed_contact(a, b):
    reason = DESIGNED_CONTACT.get(frozenset((contact_key(a), contact_key(b))))
    if reason:
        return reason
    kinds = (a['kind'] if a['kind'] == 'rail_lamp' else a['category'], b['kind'] if b['kind'] == 'rail_lamp' else b['category'])
    return DESIGNED_KIND_CONTACT.get(frozenset(kinds))


def name_of(record):
    if record.get('obstacleId'):
        return record['obstacleId'] + ('(' + record['model'] + ')' if record.get('model') else '')
    if record.get('model'):
        return record['model']
    return record['kind'] + ':' + record['caller']


def cluster_parts(meta):
    """编排函数直接画的零件按 (调用者, 岛区) 分桶，3D 包围盒相接（5 cm）的并成一件。"""
    instances = meta['instances']
    parent = list(range(len(instances)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    buckets = collections.defaultdict(list)
    for record in instances:
        if record['kind'] == 'part':
            buckets[(record['caller'], str(record['region']).split('_')[0])].append(record['id'])
    for ids in buckets.values():
        grid = collections.defaultdict(list)
        for i in ids:
            lo, hi = instances[i]['bounds']
            for gx in range(int(math.floor((lo[0] - .05) / 4)), int(math.floor((hi[0] + .05) / 4)) + 1):
                for gz in range(int(math.floor((lo[2] - .05) / 4)), int(math.floor((hi[2] + .05) / 4)) + 1):
                    grid[(gx, gz)].append(i)
        for cell in grid.values():
            for a_index, a in enumerate(cell):
                la, ha = instances[a]['bounds']
                for b in cell[a_index + 1:]:
                    lb, hb = instances[b]['bounds']
                    if all(la[k] - .05 <= hb[k] and lb[k] - .05 <= ha[k] for k in range(3)):
                        ra, rb = find(a), find(b)
                        if ra != rb:
                            parent[rb] = ra
    groups = collections.defaultdict(list)
    for record in instances:
        groups[find(record['id'])].append(record['id'])
    return groups


def clipped_footprints(P, lo, hi):
    """每个三角形被 y∈[lo,hi] 切下的部分投到 XZ 的凸包点集。返回 (点 (m,9,2), 有效 (m,9))。"""
    y = P[:, :, 1]
    pts = [P[:, :, [0, 2]]]
    valid = [(y >= lo[:, None]) & (y <= hi[:, None])]
    for i, j in ((0, 1), (1, 2), (2, 0)):
        a, b = P[:, i], P[:, j]
        den = b[:, 1] - a[:, 1]
        safe = np.where(np.abs(den) < 1e-12, 1.0, den)
        for plane in (lo, hi):
            t = (plane - a[:, 1]) / safe
            ok = (np.abs(den) >= 1e-12) & (t >= 0) & (t <= 1)
            q = a + t[:, None] * (b - a)
            pts.append(q[:, None, :][:, :, [0, 2]])
            valid.append(ok[:, None])
    return np.concatenate(pts, axis=1), np.concatenate(valid, axis=1)


def footprint(P, ground, low, high, fill=True):
    """P (m,3,3) 的三角形在离地 [low,high] 内的 XZ 投影并集；竖墙投成线后加 3 cm 宽。"""
    if len(P) == 0:
        return None
    lo = np.full(len(P), ground + low)
    hi = np.full(len(P), ground + high)
    ymin, ymax = P[:, :, 1].min(axis=1), P[:, :, 1].max(axis=1)
    keep = (ymax >= lo) & (ymin <= hi)
    if not keep.any():
        return None
    pts, valid = clipped_footprints(P[keep], lo[keep], hi[keep])
    counts = valid.sum(axis=1)
    keep2 = counts >= 1
    pts, valid, counts = pts[keep2], valid[keep2], counts[keep2]
    if len(pts) == 0:
        return None
    coords = pts[valid]
    indices = np.repeat(np.arange(len(pts)), counts)
    hulls = shapely.convex_hull(shapely.multipoints(coords, indices=indices))
    geoms = shapely.buffer(hulls, 0.03, quad_segs=1)
    union = shapely.union_all(geoms, grid_size=0.005)
    if union.is_empty:
        return None
    if fill:
        parts = [Polygon(p.exterior) for p in getattr(union, 'geoms', [union]) if p.geom_type == 'Polygon']
        union = shapely.union_all(parts) if parts else union
    return union


def surface_samples(P, box, step=0.08):
    """box（3D，已外扩）里的三角形按 step 间距撒点（含顶点）。"""
    lo, hi = box
    tmin, tmax = P.min(axis=1), P.max(axis=1)
    keep = np.all(tmax >= lo, axis=1) & np.all(tmin <= hi, axis=1)
    out = []
    for tri in P[keep]:
        a, b, c = tri
        n = int(min(40, max(1, math.ceil(max(np.linalg.norm(b - a), np.linalg.norm(c - b), np.linalg.norm(a - c)) / step))))
        for i in range(n + 1):
            for j in range(n + 1 - i):
                u, v = i / n, j / n
                out.append(a + (b - a) * u + (c - a) * v)
    return np.array(out) if out else np.zeros((0, 3))


def touching(PA, PB, lo_a, hi_a, lo_b, hi_b, gap=0.08):
    """两组三角形表面是否相接（采样点量化到 gap 体素，26 邻域有共用即接触）。"""
    pad = .1
    region_a = (np.maximum(lo_a, np.array(lo_b) - pad), np.minimum(hi_a, np.array(hi_b) + pad))
    A = surface_samples(PA, (np.array(region_a[0]) - pad, np.array(region_a[1]) + pad))
    B = surface_samples(PB, (np.array(region_a[0]) - pad, np.array(region_a[1]) + pad))
    if not len(A) or not len(B):
        return False
    cells = set(map(tuple, np.floor(A / gap).astype(np.int64)))
    for cell in map(tuple, np.floor(B / gap).astype(np.int64)):
        x, y, z = cell
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    if (x + dx, y + dy, z + dz) in cells:
                        return True
    return False


MARKER_CLEAR = {'search': 1.4, 'point_of_interest': 1.6, 'spawn': 2.0, 'extraction': 3.2,
                'enemy_spawn': 1.6, 'lamp': 1.2, 'relay': 2.0}


def road_surface(roads):
    """road_layout.json 的最终道路合并面（全部岛）。"""
    polys = []
    for mesh in roads['meshes']:
        verts = mesh['vertices']
        polys.append(shapely.union_all([Polygon([(verts[i][0], verts[i][2]) for i in face]) for face in mesh['faces']],
                                       grid_size=1e-4))
    return shapely.union_all(polys)


def protected_zones(layout, roads, runtime_points):
    """[(类型, id, 多边形)]：桥口直段、门口、玩法标记、运行时物件（剧情门按门体 OBB）。"""
    zones = []
    for portal in roads['portals']:
        p, d = portal['point'], portal['inward']
        length = max(3.0, min(8.0, portal['straightLength']))
        q = [p[0] + d[0] * length, p[1] + d[1] * length]
        zones.append(('bridge_mouth', portal['island'] + '@' + portal['bridge'],
                      shapely.LineString([p, q]).buffer(portal['bridgeWidth'] / 2, cap_style=2)))
    for front in roads['frontages']:
        zones.append(('doorway', front['obstacle'], Point(front['point']).buffer(1.6)))
    for marker in layout['markers']:
        radius = MARKER_CLEAR.get(marker['kind'], 0)
        if radius:
            x, _, z = marker['position']
            zones.append((marker['kind'], marker['id'], Point(x, z).buffer(radius)))
    for row in runtime_points:
        if row['kind'] == 'story_gate':
            yaw = math.radians(row['yaw'])
            half_w, half_d = (row['width'] + 1) / 2, 0.5
            corners = [(sx * half_w, sz * half_d) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
            poly = Polygon([(row['x'] + cx * math.cos(yaw) + cz * math.sin(yaw),
                             row['z'] - cx * math.sin(yaw) + cz * math.cos(yaw)) for cx, cz in corners])
            zones.append(('runtime_story_gate', row['id'], poly))
        elif row.get('clearRadius', 0) > 0:
            zones.append(('runtime_' + row['kind'], row['id'], Point(row['x'], row['z']).buffer(row['clearRadius'])))
    return zones


COMPACT_BANDS = BANDS[:5]                 # 证据表只存到 4.5 m：身体与屋檐 / 灯头 / 招牌这一层的穿插


def recheck(things, layout, roads, runtime_points):
    """从证据表（各件身体层与低层高度带的实测截面）独立复核：压路、净空、运行时物件、低层互穿。

    things: [{category, kind, caller, chain, obstacleId, region, ground, bounds, body, bands}]，
    body / bands 为 shapely 几何。返回 [(类型, 件名, 数值, 说明)]，只含非设计接触。
    """
    found = []
    roads_all = road_surface(roads)
    curb = roads_all.buffer(ROAD_CURB, quad_segs=4)
    core = roads_all.buffer(-SOFT_ROAD_EROSION, quad_segs=4)
    for t in things:
        body = t.get('body')
        if body is None:
            continue
        if t['category'] in ('hard', 'tree'):
            on_road = body.intersection(roads_all).area
            if t['kind'] == 'registered' and on_road <= MIN_AREA:
                continue
            area = body.intersection(curb).area
            if area > MIN_AREA:
                found.append(('road_intrusion', t['name'], area, 'hard body within road + curb'))
        elif t['category'] == 'flora':
            area = body.intersection(core).area
            if area > 0.05:
                found.append(('road_intrusion_soft', t['name'], area, 'planting inside road core'))
    candidates = [t for t in things if t.get('body') is not None and t['category'] in ('hard', 'tree', 'flora')]
    tree = shapely.STRtree([t['body'] for t in candidates])
    for kind, zone_id, poly in protected_zones(layout, roads, runtime_points):
        for j in tree.query(poly):
            t = candidates[int(j)]
            body = t['body']
            if kind == 'lamp' and t['kind'] == 'lantern' and ('marker_lanterns' in t['chain'] or t['caller'] == 'main'):
                continue
            if t['category'] == 'flora' and kind not in SOFT_ZONE_KINDS:
                continue
            area = body.intersection(poly).area
            limit = 0.25 if t['category'] == 'flora' else MIN_AREA
            if kind == 'runtime_resident':
                limit = max(limit, 0.05)
            if area > limit:
                found.append(('blocks_' + kind, t['name'], area, zone_id))
    solids = [t for t in things if t['category'] in ('hard', 'tree', 'rail') and t.get('body') is not None]
    solid_tree = shapely.STRtree([t['body'] for t in solids])
    for row in runtime_points:
        if not row.get('footprint') or not row.get('mesh'):
            continue
        disc = Point(row['x'], row['z']).buffer(row['footprint'])
        if row['kind'] not in ('bridge_sign_post', 'resident') and disc.intersection(curb).area > MIN_AREA:
            found.append(('runtime_on_road', row['kind'] + ':' + row['id'], disc.intersection(curb).area, ''))
        for j in solid_tree.query(disc):
            t = solids[int(j)]
            if abs(t['ground'] - row['y']) <= 1.5 and disc.intersection(t['body']).area > MIN_AREA:
                found.append(('runtime_interpenetration', row['kind'] + ':' + row['id'], disc.intersection(t['body']).area, t['name']))
    banded = [t for t in solids if t.get('bands')]
    index = shapely.STRtree([t['body'] for t in banded])
    for i, a in enumerate(banded):
        for j in index.query(a['body']):
            b = banded[int(j)]
            if int(j) <= i or (a['category'] == 'rail' and b['category'] == 'rail'):
                continue
            if a['bounds'][0][1] >= b['bounds'][1][1] - .6 or b['bounds'][0][1] >= a['bounds'][1][1] - .6:
                continue
            if designed_contact(a, b):
                continue
            if a['caller'] == b['caller'] and a['region'] == b['region'] and a['kind'] in ('part', 'bell') \
                    and b['kind'] in ('part', 'bell'):
                continue
            worst = max((fa.intersection(fb).area for fa, fb in zip(a['bands'], b['bands'])
                         if fa is not None and fb is not None), default=0.0)
            smaller = min(a['body'].area, b['body'].area)
            if worst > 0.02 and (worst > .05 * max(smaller, 1e-6) or worst > .25):
                found.append(('interpenetration', a['name'], worst, b['name']))
    return found


def compact_things(rows):
    """证据表行 → recheck 的输入（WKT 还原成 shapely）。"""
    out = []
    for row in rows:
        thing = dict(row)
        # 证据表按 1 cm 取整存盘，取整后个别多边形会自交：读回时先修成合法几何再做布尔运算。
        thing['body'] = shapely.make_valid(shapely.from_wkt(row['body'])) if row.get('body') else None
        thing['bands'] = [shapely.make_valid(shapely.from_wkt(w)) if w else None for w in row['bands']] if row.get('bands') else None
        out.append(thing)
    return out


def rotate_facing(yaw_deg):
    """生成器约定：正面 -Z，偏航后正面世界方向为 (-sin, -cos)。"""
    a = math.radians(yaw_deg)
    return -math.sin(a), -math.cos(a)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ledger', required=True)
    parser.add_argument('--out', required=True)
    parser.add_argument('--layout', default=str(ROOT / 'ArtSource/SkyIsland/layout.json'))
    parser.add_argument('--roads', default=str(ROOT / 'ArtSource/SkyIsland/road_layout.json'))
    parser.add_argument('--runtime', default=str(ROOT / 'ArtSource/SkyIsland/runtime_placements.json'))
    parser.add_argument('--compact', help='write compact footprint table for the offline property test')
    args = parser.parse_args()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    layout = json.loads(Path(args.layout).read_text(encoding='utf-8'))
    roads = json.loads(Path(args.roads).read_text(encoding='utf-8'))
    meta, V, T, owner = load_ledger(args.ledger)
    islands, bridge_tris = island_lookup(layout)
    instances = meta['instances']

    # ---- 道路合并面（每岛一片）与广场 ----
    all_roads = road_surface(roads)
    road_curb = all_roads.buffer(ROAD_CURB, quad_segs=4)
    road_core = all_roads.buffer(-SOFT_ROAD_EROSION, quad_segs=4)

    # ---- 按实例（零件聚成件）整理三角形 ----
    order = np.argsort(owner, kind='stable')
    sorted_owner = owner[order]
    starts = np.searchsorted(sorted_owner, np.arange(len(instances)))
    ends = np.searchsorted(sorted_owner, np.arange(len(instances)), side='right')
    groups = cluster_parts(meta)
    island_or_bridge = shapely.union_all([i['poly'] for i in islands.values()] + [p for _, p, _ in bridge_tris])

    def triangles_of(ids):
        rows = np.concatenate([order[starts[i]:ends[i]] for i in ids]) if ids else np.zeros(0, dtype=np.int64)
        return V[T[rows]]

    things = []
    for root, ids in groups.items():
        record = instances[root]
        category = classify(record)
        P = triangles_of(ids)
        lo = P.reshape(-1, 3).min(axis=0)
        hi = P.reshape(-1, 3).max(axis=0)
        cx, cz = float((lo[0] + hi[0]) / 2), float((lo[2] + hi[2]) / 2)
        region, gy = ground_at(islands, bridge_tris, cx, cz)
        if region is None:
            # 包围盒中心落在虚空（岛缘外的崖饰）：取最近岛。
            nearest = min(islands.values(), key=lambda i: i['poly'].distance(Point(cx, cz)))
            region, gy = nearest['id'], float(nearest['height'])
        if category in ('hard', 'tree', 'flora'):
            if hi[1] < gy - 0.3:
                category = 'cliff'                      # 整件在岛面以下：崖下花园、垂藤
            elif hi[1] - gy < DECAL_HEIGHT and category == 'hard':
                category = 'surface'                    # 贴地嵌饰
            elif record['kind'] == 'part' and record['caller'] == 'meadows' and lo[1] - gy > 1.0:
                category = 'sky'                        # 花粉光点
        if category in ('hard', 'tree', 'flora') and not island_or_bridge.intersects(
                shp_box(lo[0], lo[2], hi[0], hi[2])):
            category = 'offshore'                       # 整件在岛与桥之外：码头外的飞艇、崖外悬浮花园
        thing = {'id': len(things), 'root': root, 'parts': len(ids), 'category': category,
                 'chain': sorted(callers_of(record)),
                 'name': name_of(record), 'kind': record['kind'], 'caller': record['caller'],
                 'fn': record.get('fn'), 'model': record.get('model'), 'obstacleId': record.get('obstacleId'),
                 'region': region, 'ground': gy, 'bounds': [lo.tolist(), hi.tolist()],
                 'position': record.get('position'), 'yaw': record.get('yaw'),
                 'triangles': int(len(P)), '_P': P}
        things.append(thing)
    print('THINGS', len(things), collections.Counter(t['category'] for t in things), flush=True)

    defects = []

    def defect(thing, kind, metric, value, fix, **extra):
        lo, hi = thing['bounds']
        row = {'region': thing['region'], 'name': thing['name'], 'category': thing['category'],
               'caller': thing['caller'], 'type': kind, 'metric': metric, 'value': round(float(value), 3),
               'world': [round((lo[0] + hi[0]) / 2, 2), round(thing['ground'], 2), round((lo[2] + hi[2]) / 2, 2)],
               'fix': fix, 'thingId': thing['id']}
        row.update(extra)
        defects.append(row)

    # ---- 身体层脚印 ----
    for index, thing in enumerate(things):
        if thing['category'] in ('surface', 'sky', 'cliff'):
            thing['body'] = None
            continue
        thing['body'] = footprint(thing['_P'], thing['ground'], BODY_LOW, BODY_HIGH,
                                  fill=thing['category'] in ('hard', 'tree'))
        if thing['category'] in ('hard', 'tree', 'rail'):
            bands = BANDS[:TRUNK_BANDS] if thing['category'] == 'tree' else BANDS
            thing['bands'] = [footprint(thing['_P'], thing['ground'], a, b, fill=thing['category'] != 'rail')
                              for a, b in bands] + [None] * (len(BANDS) - len(bands))
        if index % 500 == 0:
            print('FOOTPRINT', index, len(things), flush=True)

    # ---- 1. 路面净空 ----
    for thing in things:
        body = thing.get('body')
        if body is None or thing['category'] == 'rail':
            continue
        if thing['category'] in ('hard', 'tree'):
            overlap = body.intersection(road_curb).area
            inside = body.intersection(all_roads).area
            # 登记障碍是道路规划绕开的固定件（只按 0.2 m 规划净空绕），只有真压到路面才算。
            if thing['kind'] == 'registered' and inside <= MIN_AREA:
                continue
            if overlap > MIN_AREA:
                defect(thing, 'road_intrusion', 'm² in road (+0.2 m curb)', overlap,
                       'move off road / reroute', onRoadArea=round(inside, 3))
        else:
            overlap = body.intersection(road_core).area
            if overlap > 0.05:
                defect(thing, 'road_intrusion_soft', 'm² inside road core', overlap, 'omit or move planting')

    # ---- 2. 硬物互穿（分高度带） ----
    hard = [t for t in things if t['category'] in ('hard', 'tree', 'rail') and t.get('bands')]
    tree_index = shapely.STRtree([shp_box(t['bounds'][0][0], t['bounds'][0][2], t['bounds'][1][0], t['bounds'][1][2])
                                  for t in hard])
    seen = set()
    for i, a in enumerate(hard):
        for j in tree_index.query(shp_box(a['bounds'][0][0], a['bounds'][0][2], a['bounds'][1][0], a['bounds'][1][2])):
            j = int(j)
            if j <= i:
                continue
            b = hard[j]
            if a['category'] == 'rail' and b['category'] == 'rail':
                continue
            if (a['bounds'][1][1] < b['bounds'][0][1]) or (b['bounds'][1][1] < a['bounds'][0][1]):
                continue
            # 一件搁在另一件顶上（梁架在柱头、亭顶落在柱顶、水纹浮在池面）：接触在上件底与下件顶之间 0.6 m 内。
            if a['bounds'][0][1] >= b['bounds'][1][1] - .6 or b['bounds'][0][1] >= a['bounds'][1][1] - .6:
                continue
            designed = designed_contact(a, b)
            worst, band_at = 0.0, None
            for k, (fa, fb) in enumerate(zip(a['bands'], b['bands'])):
                if fa is None or fb is None:
                    continue
                # 两者的高度带以各自地面为基准；同岛同地面，桥上以各自插值。
                area = fa.intersection(fb).area
                if area > worst:
                    worst, band_at = area, k
            smaller = min(a['body'].area if a.get('body') is not None else 1e9,
                          b['body'].area if b.get('body') is not None else 1e9)
            if worst > 0.02 and band_at is not None:
                # 高度带是粗筛：同一带里上下错开的两件（钟挂在梁下、光环在钟顶上方）投影会重叠。
                # 在这一带里按 0.25 m 细切复核，两件必须在同一细层里真重叠才算。
                lo_band, hi_band = BANDS[band_at]
                fine = 0.0
                level = lo_band
                while level < hi_band - 1e-9:
                    top = min(level + .25, hi_band)
                    fa = footprint(a['_P'], a['ground'], level, top, fill=a['category'] != 'rail')
                    fb = footprint(b['_P'], b['ground'], level, top, fill=b['category'] != 'rail')
                    if fa is not None and fb is not None:
                        fine = max(fine, fa.intersection(fb).area)
                    level = top
                worst = fine
            if worst > 0.02 and (worst > .05 * max(min(smaller, 1e8), 1e-6) or worst > .25):
                key = (a['id'], b['id'])
                if key in seen:
                    continue
                seen.add(key)
                same = (a['caller'] == b['caller'] and a['region'] == b['region'] and a['kind'] in ('part', 'bell')
                        and b['kind'] in ('part', 'bell'))
                defect(a, 'interpenetration', 'm² overlap in band %s' % (BANDS[band_at],), worst,
                       'separate by actual mesh', other=b['name'], otherCategory=b['category'],
                       otherThingId=b['id'], sameComposite=same, intentional=designed,
                       otherWorld=[round((b['bounds'][0][0] + b['bounds'][1][0]) / 2, 2),
                                   round((b['bounds'][0][2] + b['bounds'][1][2]) / 2, 2)])
    # 硬物插进树冠（树冠软，但柱子、招牌、屋顶整截插进叶团一眼就看得出）。
    trees = [t for t in things if t['category'] == 'tree']
    for tree in trees:
        crown = footprint(tree['_P'], tree['ground'], 2.2, 30.0, fill=True)
        tree['crown'] = crown
        if crown is None:
            continue
        lo_t, hi_t = tree['bounds']
        for other in hard:
            if other['category'] != 'hard':
                continue
            lo, hi = other['bounds']
            if hi[1] < tree['ground'] + 2.6 or lo[0] > hi_t[0] or hi[0] < lo_t[0] or lo[2] > hi_t[2] or hi[2] < lo_t[2]:
                continue
            high = footprint(other['_P'], other['ground'], 2.6, 30.0, fill=True)
            if high is None:
                continue
            area = high.intersection(crown).area
            if area > 1.0:
                defect(other, 'pierces_canopy', 'm² of structure inside tree crown', area,
                       'move tree or structure apart', other=tree['name'], otherThingId=tree['id'],
                       intentional=designed_contact(other, tree))

    # ---- 3. 接地：悬空 / 陷地 / 伸出岛缘 ----
    island_union = shapely.union_all([i['poly'] for i in islands.values()] + [p for _, p, _ in bridge_tris])
    # 悬空按「接触图」判：贴地（最低点离地 ≤ 5 cm）的件是根，和根有实体接触（表面采样体素 8 cm 内相邻）一路连上的都算有支撑。
    solid_like = [t for t in things if t['category'] in ('hard', 'tree', 'rail', 'surface')]
    grounded = {t['id'] for t in solid_like if t['bounds'][0][1] - t['ground'] <= 0.05 or t['category'] in ('rail', 'surface')}
    candidates = [t for t in things if t['category'] in ('hard', 'tree', 'flora') and t['id'] not in grounded
                  and t['bounds'][0][1] - t['ground'] > 0.05 and t['kind'] != 'lotus'
                  and not INTENTIONAL_FLOAT.get((t['caller'], t['region']))]
    supported = set(grounded)
    pending = list(candidates)
    changed = True
    while changed and pending:
        changed = False
        for t in list(pending):
            lo, hi = t['bounds']
            for other in solid_like:
                if other['id'] not in supported or other['id'] == t['id']:
                    continue
                ol, oh = other['bounds']
                if any(lo[k] > oh[k] + .1 or hi[k] < ol[k] - .1 for k in range(3)):
                    continue
                if touching(t['_P'], other['_P'], lo, hi, ol, oh):
                    supported.add(t['id']); pending.remove(t); changed = True
                    break
    for t in pending:
        defect(t, 'floating', 'lowest point above ground (m), no solid contact', t['bounds'][0][1] - t['ground'],
               'drop to ground or attach to support')
    for thing in things:
        if thing['category'] not in ('hard', 'tree', 'flora'):
            continue
        lo, hi = thing['bounds']
        base = lo[1] - thing['ground']
        height = hi[1] - lo[1]
        if thing['category'] == 'hard' and -base > max(0.3, .25 * height) and thing['kind'] != 'registered':
            defect(thing, 'sunk', 'depth below ground (m)', -base, 'raise base to ground')
        body = thing.get('body')
        if body is not None and not body.is_empty:
            outside = body.difference(island_union).area
            # 护栏灯坐在护栏线上，一半在桥面外是设计（见 generate_sky_island.relay_platforms）。
            if outside > max(0.05, .1 * body.area) and thing['category'] != 'flora' and thing['kind'] != 'rail_lamp':
                defect(thing, 'overhangs_edge', 'm² of body outside walkable ground', outside, 'pull inside island rim',
                       intentional=INTENTIONAL_FLOAT.get((thing['caller'], thing['region'])))
    # 花草 / 树长进硬物里（花从房子地板冒出来、树干插进墙）。
    hard_bodies = [t for t in things if t['category'] in ('hard', 'rail') and t.get('body') is not None]
    hard_tree = shapely.STRtree([t['body'] for t in hard_bodies]) if hard_bodies else None
    for thing in things:
        if thing['category'] not in ('flora', 'tree') or thing.get('body') is None or hard_tree is None:
            continue
        if thing['kind'] == 'lotus':
            continue                                    # 睡莲只摆在池面上，本来就在池子的包围里
        for j in hard_tree.query(thing['body']):
            other = hard_bodies[int(j)]
            if other['bounds'][0][1] >= thing['bounds'][1][1] - .05:
                continue
            area = thing['body'].intersection(other['body']).area
            share = area / max(thing['body'].area, 1e-6)
            limit = (0.3, .4) if thing['category'] == 'flora' else (MIN_AREA, 0.0)
            if area > limit[0] and share > limit[1]:
                designed = designed_contact(thing, other)
                defect(thing, 'grows_into_solid', 'm² of planting / trunk inside solid', area,
                       'move planting out of the solid', other=other['name'], otherThingId=other['id'],
                       share=round(share, 3), intentional=designed)

    # ---- 4. 同位重复实例 ----
    buckets = collections.defaultdict(list)
    for thing in things:
        if thing['category'] in ('sky', 'cloud'):
            continue
        lo, hi = thing['bounds']
        buckets[(thing['name'], round(lo[0], 1), round(lo[2], 1), round(hi[0], 1), round(hi[2], 1))].append(thing)
    for key, rows in buckets.items():
        if len(rows) > 1:
            defect(rows[0], 'duplicate_instance', 'copies at same place', len(rows), 'emit once')

    # ---- 4b. 共面 z-fighting：不同两件（或一件与岛面）的朝上面落在同一高度（±3 mm）且投影重叠 ----
    flats = collections.defaultdict(list)
    for thing in things:
        if thing['category'] in ('sky', 'cliff', 'offshore'):
            continue
        P = thing['_P']
        e1, e2 = P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]
        normal = np.cross(e1, e2)
        length = np.linalg.norm(normal, axis=1)
        up = (length > 1e-9) & (np.abs(normal[:, 1]) > .98 * np.maximum(length, 1e-12))
        flat = up & (np.ptp(P[:, :, 1], axis=1) < .002)
        near = flat & (P[:, :, 1].mean(axis=1) < thing['ground'] + .3)
        # 只看从上面看得见的那层：一件东西自己的顶面（离它最高点 5 cm 内），或本身就是铺装 / 贴地嵌饰。
        # 立在地上的物件底面与岛面共面，但总被它自己的顶面挡着，不会闪。
        if thing['category'] != 'surface':
            near &= P[:, :, 1].mean(axis=1) >= thing['bounds'][1][1] - .05
        if not near.any():
            continue
        heights = np.round(P[near][:, :, 1].mean(axis=1) / .003).astype(np.int64)
        for level in np.unique(heights):
            rows = P[near][heights == level]
            poly = shapely.union_all(shapely.polygons(rows[:, :, [0, 2]]), grid_size=.001)
            flats[(thing['region'], int(level))].append((thing, poly))
    zfight = 0
    for (region, level), rows in flats.items():
        candidates = rows + flats.get((region, level - 1), []) + flats.get((region, level + 1), [])
        for i, (a, pa) in enumerate(rows):
            # 与岛面共面（岛面是 VIS_Ground，不在台账里）
            if abs(level * .003 - a['ground']) < .0035 and a['category'] != 'surface':
                area = pa.intersection(islands[region]['poly']).area if region in islands else 0
                if area > MIN_AREA:
                    defect(a, 'coplanar_zfight', 'm² of up-facing face flush with island ground', area,
                           'lift the face ≥ 5 mm or drop it below ground')
            for b, pb in candidates:
                if b['id'] <= a['id']:
                    continue
                area = pa.intersection(pb).area
                if area > MIN_AREA:
                    zfight += 1
                    defect(a, 'coplanar_zfight', 'm² of coplanar up-facing overlap', area,
                           'separate heights by ≥ 5 mm', other=b['name'], otherThingId=b['id'])

    # ---- 5. 桥头、门口、交互点净空 ----
    runtime = Path(args.runtime)
    runtime_points = json.loads(runtime.read_text(encoding='utf-8')).get('points', []) if runtime.is_file() else []
    zones = protected_zones(layout, roads, runtime_points)
    for zone_kind, zone_id, poly in zones:
        for thing in things:
            body = thing.get('body')
            if body is None or thing['category'] not in ('hard', 'tree', 'flora'):
                continue
            # 灯标记上那盏灯就是这个标记本身（旧台账里调用者是 main，新台账是 marker_lanterns）。
            if zone_kind == 'lamp' and thing['kind'] == 'lantern' and (
                    'marker_lanterns' in thing['chain'] or thing['caller'] == 'main'):
                continue
            if thing['category'] == 'flora' and zone_kind not in SOFT_ZONE_KINDS:
                continue
            if zone_kind == 'doorway' and thing.get('obstacleId') == zone_id:
                continue
            overlap = body.intersection(poly).area
            limit = 0.25 if thing['category'] == 'flora' else MIN_AREA
            if zone_kind == 'runtime_resident':
                # 居民游走圈（2.5 m + 胶囊 0.6）边缘几厘米的擦边由角色物理挡住，按 0.05 m² 计。
                limit = max(limit, 0.05)
            if overlap > limit:
                defect(thing, 'blocks_' + zone_kind, 'm² inside clearance of ' + zone_id, overlap,
                       'keep clearance free', zone=zone_id)

    # ---- 5b. 运行时有网格的东西：不压路，不插进作者硬物 / 花草 ----
    solid = [t for t in things if t['category'] in ('hard', 'tree', 'rail') and t.get('body') is not None]
    flora = [t for t in things if t['category'] == 'flora' and t.get('body') is not None]
    solid_tree = shapely.STRtree([t['body'] for t in solid]) if solid else None
    flora_tree = shapely.STRtree([t['body'] for t in flora]) if flora else None
    for row in runtime_points:
        if not row.get('footprint') or not row.get('mesh'):
            continue
        disc = Point(row['x'], row['z']).buffer(row['footprint'])
        region, _ = ground_at(islands, bridge_tris, row['x'], row['z'])
        fake = {'id': -1, 'region': region or '?', 'ground': row['y'], 'name': row['kind'] + ':' + row['id'],
                'category': 'runtime', 'caller': 'runtime',
                'bounds': [[row['x'], row['y'], row['z']], [row['x'], row['y'], row['z']]]}
        if row['kind'] not in ('bridge_sign_post', 'resident'):
            on_road = disc.intersection(road_curb).area
            if on_road > MIN_AREA:
                defect(fake, 'runtime_on_road', 'm² of runtime object on road (+curb)', on_road,
                       'change bearing / distance data so it lands off the road')
        for j in (solid_tree.query(disc) if solid_tree is not None else []):
            other = solid[int(j)]
            if abs(other['ground'] - row['y']) > 1.5:
                continue
            area = disc.intersection(other['body']).area
            if area > MIN_AREA:
                defect(fake, 'runtime_interpenetration', 'm² overlap with author solid', area,
                       'move runtime object', other=other['name'], otherThingId=other['id'])
        for j in (flora_tree.query(disc) if flora_tree is not None else []):
            other = flora[int(j)]
            area = disc.intersection(other['body']).area
            if area > 0.15 and row['kind'] in ('loot_crate', 'bounty_crate', 'memorial_stake', 'hearth'):
                defect(fake, 'runtime_in_planting', 'm² overlap with author planting', area,
                       'move runtime object or planting', other=other['name'], otherThingId=other['id'])

    # ---- 6. 朝向：有正面的件不能背对最近的路 ----
    for thing in things:
        model = thing.get('model') or ''
        if thing.get('yaw') is None or thing['category'] != 'hard':
            continue
        if model not in FRONT_MODELS:
            continue
        x, _, z = thing['position']
        near = shapely.ops.nearest_points(all_roads, Point(x, z))[0] if hasattr(shapely, 'ops') else None
        from shapely.ops import nearest_points
        near = nearest_points(all_roads, Point(x, z))[0]
        dx, dz = near.x - x, near.y - z
        dist = math.hypot(dx, dz)
        if dist < 1e-6:
            continue
        fx, fz = rotate_facing(thing['yaw'])
        angle = math.degrees(math.acos(max(-1, min(1, (fx * dx + fz * dz) / dist))))
        thing['facingRoadAngle'] = angle
        if angle > 100 and dist < 25:
            defect(thing, 'faces_away_from_road', 'degrees between front and nearest road', angle,
                   'turn front toward road', roadDistance=round(dist, 2))

    # ---- 汇总 ----
    summary = collections.Counter((d['region'], d['type']) for d in defects)
    by_region = collections.defaultdict(dict)
    for (region, kind), count in summary.items():
        by_region[region][kind] = count
    totals = collections.Counter(d['type'] for d in defects)
    (out / 'defects.json').write_text(json.dumps(defects, ensure_ascii=False, indent=1), encoding='utf-8')
    (out / 'summary.json').write_text(json.dumps({'totals': totals, 'byRegion': by_region,
                                                   'things': len(things),
                                                   'categories': collections.Counter(t['category'] for t in things)},
                                                  ensure_ascii=False, indent=1), encoding='utf-8')
    if args.compact:
        import hashlib
        rows = []
        # 花草只会违反「压路」与「挡交互点」两条：离路缘与这些净空 3 m 以外的花草不进证据表（省体积）。
        soft_reach = shapely.union_all([road_curb] + [poly for kind, _, poly in zones if kind in SOFT_ZONE_KINDS]).buffer(3.0)
        for thing in things:
            body = thing.get('body')
            if body is None:
                continue
            if thing['category'] == 'flora' and not body.intersects(soft_reach):
                continue
            tolerance = 0.05 if thing['category'] == 'flora' else 0.02
            row = {'id': thing['id'], 'name': thing['name'], 'category': thing['category'], 'kind': thing['kind'],
                   'caller': thing['caller'], 'chain': thing['chain'], 'obstacleId': thing.get('obstacleId'),
                   'region': thing['region'], 'ground': round(thing['ground'], 3),
                   'bounds': [[round(v, 3) for v in thing['bounds'][0]], [round(v, 3) for v in thing['bounds'][1]]],
                   'body': shapely.to_wkt(shapely.simplify(body, tolerance), rounding_precision=2)}
            if thing.get('bands'):
                row['bands'] = [shapely.to_wkt(shapely.simplify(band, 0.03), rounding_precision=2) if band is not None
                                else None for band in thing['bands'][:len(COMPACT_BANDS)]]
            rows.append(row)
        runtime_bytes = runtime.read_bytes() if runtime.is_file() else b''
        payload = {'schemaVersion': 1,
                   'source': {'layoutCanonicalSha256': __import__('sky_island_road_layout').digest(layout),
                              'roadPayloadHash': roads.get('payloadHash'),
                              'runtimePlacementsSha256': hashlib.sha256(runtime_bytes).hexdigest(),
                              'ledgerTriangles': int(len(T)),
                              'ledgerInstances': len(instances)},
                   'criteria': {'bodySlab': [BODY_LOW, BODY_HIGH], 'bands': COMPACT_BANDS, 'roadCurb': ROAD_CURB,
                                'softRoadErosion': SOFT_ROAD_EROSION, 'markerClear': MARKER_CLEAR},
                   'defects': defects, 'things': rows}
        Path(args.compact).write_text(json.dumps(payload, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    print('DEFECTS', dict(totals), flush=True)


if __name__ == '__main__':
    main()
