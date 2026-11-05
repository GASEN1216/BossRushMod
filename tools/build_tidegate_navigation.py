# -*- coding: utf-8 -*-
"""潮闸镇导航网格：从 three.js 导出的逻辑网格烘「低潮时可走」的共形三角网，给 A* NavMeshGraph 当 sourceMesh。

输入 ArtSource/Tidegate/export/（tidegate_layout.json、grid_height.f32、grid_flag.u8），输出
ArtSource/Tidegate/navigation.json。只写这一个文件。

规则（与 three.js 原型 game.js 的 stepOK / walkable 同一口径）：
- 可走格：非 WALL、非 PROP；地面高度不低于「低潮水位 − 最大涉水深」（-1.5 − 0.5 = -2.0）。泻湖也按放干到低潮算。
- 断崖：相邻两个可走格高差超过 0.65 m（原型的上台阶上限）就当墙，两侧各让出离墙距离，不连通。
- 离墙：可走区整体内缩 0.4 m（角色半径），墙、实心物件、深水边、地图边、断崖一视同仁。
- 高度带：会被潮水淹到的格（< 0.125 m）按固定带宽切块、再按水体（外海 / 泻湖 = LAG 格）分开，
  带边界成为三角形的边，所以每个三角形三个顶点 y 的最小值就是它的「被淹高度」：
  水位 − minY > 0.5 时该三角形应设为不可走。不会被淹的格合成一块。
- 共形：各块先在共同的线网上打结（polygonize），再做覆盖简化（相邻块共用同一串顶点），
  然后逐块约束 Delaunay，没有 T 形接点（脚本会检查）。
- 顶点高度取逻辑网格（甲板格即甲板高）：以所在格为准，对四个相邻格心做双线性插值，跨断崖的格不参与。
  与物理地形网格的差记在 stats.yVsTerrain（断崖边最大，地形是平滑过的）。
- 只留出生点所在的连通块：被栈桥 / 岸壁围死的滩涂、桥南的运河坑等 AI 进不去的块丢掉，记在 stats.droppedComponents。
- 覆盖简化容差从小到大试（默认 0.9 → 1.25），顶点不超过目标 3500 就停；简化后再与「离墙 0.25 m」区求交兜底。

输出（Unity 坐标 = three.js (x, y, -z)）：
  vertices   [[x, y, z], ...]
  triangles  [[a, b, c], ...]，从 +Y 往下看顺时针（法线朝上）；外海 / 陆地在前，泻湖在后
  minY       每个三角形三个顶点 y 的最小值
  water      每个三角形受哪片水控制：0 = 外海潮位，1 = 泻湖水位
  lagoonStart 第一个泻湖三角形的下标（= 外海三角形数）
Unity 侧按 lagoonStart 拆成两个子网格（0 外海、1 泻湖），见 ArtSource/Tidegate/README.md。

用法：python tools/build_tidegate_navigation.py [--check] [--src ...] [--out ...]
  --check  另做图连通性检查（按三角形共边邻接 BFS，标记点投影到最近三角形）：低潮时从出生点到每个撤离点、
           搜刮点、阀门、刷怪点、灯塔都要连得上，不通就失败；高潮（外海 0.45、泻湖 0.62）时被淹或被隔开的标记只报告；
           另报每个三角形「最低顶点 − 其内最低可走格」的精度。结果写进输出的 check 字段。
成功打印 TIDEGATE_NAV_OK {...}。
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
import time
from collections import defaultdict, deque
from pathlib import Path

import numpy as np
import shapely
from shapely import LineString, Point, Polygon, STRtree, box

REPO = Path(__file__).resolve().parent.parent
DEFAULT_SRC = REPO / 'ArtSource' / 'Tidegate' / 'export'
DEFAULT_OUT = REPO / 'ArtSource' / 'Tidegate' / 'navigation.json'

MAX_VERTICES = 4095          # 游戏 A* 未开大 tile，单块顶点上限
TARGET_VERTICES = 3500       # 留余量
# 高度带边界：避开原型里的成片定值（泻湖底 -1.4±0.12、滩涂 -0.9±0.1、沉船 -1.22、暗渠 -1.12、
# 金库 -0.95、栈道 -0.55、运河 -1.8、潮沟 -1.85/-1.72），免得一片平地被噪声切成碎块。
BAND_EDGES = [-1.9, -1.6, -1.25, -1.05, -0.7, -0.45, -0.2, 0.0, 0.125]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Grid:
    def __init__(self, src: Path):
        self.layout = json.loads((src / 'tidegate_layout.json').read_text(encoding='utf-8'))
        g = self.layout['grid']
        self.W, self.H, self.OX, self.OZ = int(g['W']), int(g['H']), float(g['OX']), float(g['OZ'])
        self.F = {k: int(v) for k, v in g['flags'].items()}
        self.h = np.fromfile(src / g['heightFile'], dtype='<f4').reshape(self.H, self.W).astype(np.float64)
        self.f = np.fromfile(src / g['flagFile'], dtype=np.uint8).reshape(self.H, self.W)
        tide = self.layout['tide']
        self.low, self.high, self.lagoon_full = float(tide['low']), float(tide['high']), float(tide['lagoonFull'])
        self.sources = {name: sha256(src / name) for name in ('tidegate_layout.json', g['heightFile'], g['flagFile'])}
        # 物理地面（地形网格）只用来统计顶点高度与它差多少，不参与烘焙
        t = json.loads((src / 'manifest.json').read_text(encoding='utf-8'))['terrain']
        self.terrain = np.fromfile(src / t['heightFile'], dtype='<f4').reshape(int(t['height']), int(t['width']))
        self.tx0, self.tz0 = float(t['x0']), float(t['z0'])

    def terrain_at(self, x, z):
        fx, fz = x - self.tx0, z - self.tz0
        i, j = int(math.floor(fx)), int(math.floor(fz))
        tx, tz = fx - i, fz - j
        T = self.terrain
        return (T[j, i] * (1 - tx) * (1 - tz) + T[j, i + 1] * tx * (1 - tz) + T[j + 1, i] * (1 - tx) * tz + T[j + 1, i + 1] * tx * tz)

    def cell(self, x, z):
        return int(math.floor(x - self.OX)), int(math.floor(z - self.OZ))


def mask_polygon(grid: Grid, mask: np.ndarray):
    """布尔栅格 → 多边形（按行合并成长条再整体合并，坐标都是整数米）。"""
    boxes = []
    for j in range(grid.H):
        row = mask[j]
        i = 0
        while i < grid.W:
            if not row[i]:
                i += 1
                continue
            s = i
            while i < grid.W and row[i]:
                i += 1
            boxes.append(box(grid.OX + s, grid.OZ + j, grid.OX + i, grid.OZ + j + 1))
    return shapely.union_all(boxes) if boxes else Polygon()


def polygons(geom):
    if geom is None or geom.is_empty:
        return []
    if geom.geom_type == 'Polygon':
        return [geom]
    if hasattr(geom, 'geoms'):
        return [p for g in geom.geoms for p in polygons(g)]
    return []


def classify(grid: Grid, args):
    blocked = (grid.f & (grid.F['WALL'] | grid.F['PROP'])) != 0
    floor_h = grid.low - args.max_depth
    walk = (~blocked) & (grid.h >= floor_h - 1e-6)
    lag = (grid.f & grid.F['LAG']) != 0
    # 标签：0 = 不会被淹；1 + 水体 * NB + 带号 = 潮间带
    nb = len(BAND_EDGES)
    label = np.full(grid.h.shape, -1, dtype=np.int32)
    band = np.searchsorted(np.array(BAND_EDGES), grid.h, side='right')  # 0..nb；nb = 不会被淹
    inter = walk & (band < nb)
    label[walk & ~inter] = 0
    label[inter] = 1 + lag[inter].astype(np.int32) * nb + band[inter]
    # 断崖：相邻可走格高差超过台阶上限
    cliffs = []
    for dj, di in ((0, 1), (1, 0)):
        a = walk[:grid.H - dj, :grid.W - di] & walk[dj:, di:]
        dh = np.abs(grid.h[:grid.H - dj, :grid.W - di] - grid.h[dj:, di:])
        for j, i in zip(*np.nonzero(a & (dh > args.cliff))):
            x, z = grid.OX + i + di, grid.OZ + j + dj  # 两格之间那条格边
            cliffs.append(((x, z), (x + dj, z + di)))
    return walk, label, cliffs, nb


def merge_small_patches(grid: Grid, label: np.ndarray, min_cells: int):
    """潮间带里面积太小的同标签碎块并进相邻最深的那一带（只影响切块，不改顶点高度）。"""
    try:
        from scipy import ndimage
    except ImportError:
        return 0
    merged = 0
    for _ in range(4):
        changed = False
        for lab in sorted(set(np.unique(label)) - {-1}):
            comp, n = ndimage.label(label == lab)
            if n == 0:
                continue
            sizes = ndimage.sum(np.ones_like(comp), comp, index=np.arange(1, n + 1))
            for ci in np.nonzero(sizes < min_cells)[0]:
                cells = np.argwhere(comp == ci + 1)
                neigh = defaultdict(int)
                for j, i in cells:
                    for dj, di in ((0, 1), (1, 0), (0, -1), (-1, 0)):
                        jj, ii = j + dj, i + di
                        if 0 <= jj < grid.H and 0 <= ii < grid.W and label[jj, ii] not in (-1, lab):
                            neigh[int(label[jj, ii])] += 1
                if not neigh:
                    continue
                # 优先并进更深的带（标签 0 = 干地视作最浅），带号相同取接触最多的
                target = min(neigh, key=lambda t: (t == 0, (t - 1) % len(BAND_EDGES) if t else 0, -neigh[t]))
                for j, i in cells:
                    label[j, i] = target
                merged += 1
                changed = True
        if not changed:
            break
    return merged


class Heights:
    """顶点高度：以所在格为锚，四邻格心双线性插值，跨断崖 / 不可走的格不参与。"""

    def __init__(self, grid: Grid, walk: np.ndarray, cliff: float):
        self.g, self.walk, self.cliff = grid, walk, cliff

    def anchor(self, x, z):
        g = self.g
        i, j = g.cell(x, z)
        if 0 <= i < g.W and 0 <= j < g.H and self.walk[j, i]:
            return i, j
        best, bd = None, 1e9
        for jj in range(j - 2, j + 3):
            for ii in range(i - 2, i + 3):
                if 0 <= ii < g.W and 0 <= jj < g.H and self.walk[jj, ii]:
                    cx = min(max(x, g.OX + ii), g.OX + ii + 1)
                    cz = min(max(z, g.OZ + jj), g.OZ + jj + 1)
                    d = math.hypot(x - cx, z - cz)
                    if d < bd:
                        best, bd = (ii, jj), d
        return best

    def __call__(self, x, z):
        g = self.g
        a = self.anchor(x, z)
        if a is None:
            raise RuntimeError('顶点 (%.3f, %.3f) 附近没有可走格' % (x, z))
        ha = g.h[a[1], a[0]]
        fx, fz = x - g.OX - 0.5, z - g.OZ - 0.5
        i0, j0 = int(math.floor(fx)), int(math.floor(fz))
        tx, tz = fx - i0, fz - j0
        acc = wsum = 0.0
        for di, dj, w in ((0, 0, (1 - tx) * (1 - tz)), (1, 0, tx * (1 - tz)), (0, 1, (1 - tx) * tz), (1, 1, tx * tz)):
            ii, jj = i0 + di, j0 + dj
            if w <= 0 or not (0 <= ii < g.W and 0 <= jj < g.H) or not self.walk[jj, ii]:
                continue
            hv = g.h[jj, ii]
            if abs(hv - ha) > self.cliff:
                continue
            acc += w * hv
            wsum += w
        return acc / wsum if wsum > 1e-9 else ha


def build_coverage(grid: Grid, walk, label, cliffs, args):
    """可走区内缩 + 断崖切开 → 按标签切块 → 打结成覆盖 → 覆盖简化。返回 [(Polygon, 标签)]。"""
    region = mask_polygon(grid, walk)
    inset = region.buffer(-args.clearance, join_style='mitre', mitre_limit=2.0)
    if cliffs:
        cl = shapely.union_all([LineString(s) for s in cliffs])
        inset = inset.difference(cl.buffer(args.clearance, quad_segs=2))
    pieces, piece_labels = [], []
    for lab in sorted(set(np.unique(label)) - {-1}):
        part = mask_polygon(grid, label == lab).intersection(inset)
        for p in polygons(part):
            if p.area > 1e-6:
                pieces.append(p)
                piece_labels.append(lab)
    # 在共同线网上打结，保证相邻块共用同一串顶点
    lines = shapely.union_all([p.boundary for p in pieces])
    faces = [f for f in polygons(shapely.polygonize(shapely.get_parts(lines))) if f.area > 1e-6]
    tree = STRtree(pieces)
    cov, cov_labels = [], []
    for f in faces:
        rp = f.representative_point()
        hit = [k for k in tree.query(rp) if pieces[k].covers(rp)]
        if hit:
            cov.append(f)
            cov_labels.append(piece_labels[hit[0]])
    if not shapely.coverage_is_valid(cov):
        raise RuntimeError('打结后的块不是合法覆盖')
    simple = list(shapely.coverage_simplify(cov, args.simplify, simplify_boundary=True))
    # 吸附到 1 cm 网格：同一坐标在各块里吸附结果相同，共用边不受影响；顶点间距至少 1 cm，
    # 游戏 A* 按 Int3（毫米）只合并完全重合的顶点（mergeRadiusSq = 0），离得太近的顶点留着只会出细碎三角形
    simple = shapely.set_precision(np.array(simple, dtype=object), 0.01)
    # 简化可能把边界推向墙：与「最小离墙」区域求交兜底
    hard = region.buffer(-args.min_clearance, join_style='mitre', mitre_limit=2.0)
    if cliffs:
        hard = hard.difference(cl.buffer(args.min_clearance, quad_segs=2))
    out = []
    for f, lab in zip(simple, cov_labels):
        for p in polygons(f.intersection(hard) if not hard.covers(f) else f):
            if p.area > 0.01:
                out.append((shapely.set_precision(p, 0.01), lab))
    return region, inset, out


def cross2(a, b, c):
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


class MeshBuilder:
    def __init__(self, heights: Heights):
        self.heights = heights
        self.index = {}
        self.vertices = []   # three.js (x, y, z)
        self.triangles = []
        self.labels = []

    def vertex(self, x, z):
        key = (round(x * 1000), round(z * 1000))
        v = self.index.get(key)
        if v is None:
            v = len(self.vertices)
            self.index[key] = v
            xx, zz = key[0] / 1000.0, key[1] / 1000.0
            self.vertices.append((xx, self.heights(xx, zz), zz))
        return v

    def add(self, pts, lab):
        face = [self.vertex(x, z) for x, z in pts]
        if len(set(face)) < 3:
            return
        a, b, c = [(self.vertices[i][0], self.vertices[i][2]) for i in face]
        if abs(cross2(a, b, c)) < 1e-6:
            return
        self.triangles.append(face)
        self.labels.append(lab)


def triangulate(cover, heights: Heights):
    mesh = MeshBuilder(heights)
    for poly, lab in cover:
        for tri in shapely.constrained_delaunay_triangles(poly).geoms:
            if tri.area < 1e-7:
                continue
            mesh.add(list(tri.exterior.coords)[:3], lab)
    return mesh


def t_junctions(vertices, triangles):
    """只被一个三角形用到的边上若还躺着别的顶点，就是 T 形接点（A* 不会把两侧连起来）。"""
    uses = defaultdict(int)
    for t in triangles:
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            uses[(min(a, b), max(a, b))] += 1
    buckets = defaultdict(list)
    for i, v in enumerate(vertices):
        buckets[(int(math.floor(v[0])), int(math.floor(v[2])))].append(i)
    bad = []
    for (a, b), n in uses.items():
        if n != 1:
            continue
        pa, pb = vertices[a], vertices[b]
        x0, x1 = sorted((pa[0], pb[0]))
        z0, z1 = sorted((pa[2], pb[2]))
        L2 = (pb[0] - pa[0]) ** 2 + (pb[2] - pa[2]) ** 2
        for bi in range(int(math.floor(x0)), int(math.floor(x1)) + 1):
            for bj in range(int(math.floor(z0)), int(math.floor(z1)) + 1):
                for k in buckets.get((bi, bj), ()):
                    if k in (a, b):
                        continue
                    p = vertices[k]
                    t = ((p[0] - pa[0]) * (pb[0] - pa[0]) + (p[2] - pa[2]) * (pb[2] - pa[2])) / L2
                    if 1e-6 < t < 1 - 1e-6 and abs(cross2((pa[0], pa[2]), (pb[0], pb[2]), (p[0], p[2]))) / math.sqrt(L2) < 2e-3:
                        bad.append((a, b, k))
    return bad


def adjacency(triangles):
    edges = defaultdict(list)
    for ti, t in enumerate(triangles):
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            edges[(min(a, b), max(a, b))].append(ti)
    adj = [[] for _ in triangles]
    nonmanifold = 0
    for uses in edges.values():
        if len(uses) == 2:
            adj[uses[0]].append(uses[1])
            adj[uses[1]].append(uses[0])
        elif len(uses) > 2:
            nonmanifold += 1
    return adj, nonmanifold


def bfs(adj, start, allowed):
    seen = {start} if allowed[start] else set()
    q = deque(seen)
    while q:
        u = q.popleft()
        for v in adj[u]:
            if v not in seen and allowed[v]:
                seen.add(v)
                q.append(v)
    return seen


def markers(layout):
    out = [('spawn', 'PlayerSpawn', (layout['spawn']['x'], layout['spawn']['z']))]
    out += [('exit', 'Exit_' + e['id'], (e['position'][0], e['position'][2])) for e in layout['exits']]
    out += [('container', c['id'], (c['position'][0], c['position'][2])) for c in layout['containers']]
    out += [('valve', v['id'], (v['position'][0], v['position'][2])) for v in layout['valves']]
    out += [('enemy', e['id'], (e['position'][0], e['position'][2])) for e in layout['enemies']]
    out += [('lighthouse', 'Lighthouse', (layout['lighthouse']['x'], layout['lighthouse']['z']))]
    return out


def check(grid: Grid, mesh: MeshBuilder, water, min_y, args):
    tris = [Polygon([(mesh.vertices[i][0], mesh.vertices[i][2]) for i in t]) for t in mesh.triangles]
    tree = STRtree(tris)
    adj, nonmanifold = adjacency(mesh.triangles)
    marks = []
    for kind, mid, (x, z) in markers(grid.layout):
        p = Point(x, z)
        k = int(tree.nearest(p))
        marks.append({'kind': kind, 'id': mid, 'tri': k, 'dist': round(tris[k].distance(p), 3)})
    spawn = marks[0]['tri']
    everything = [True] * len(mesh.triangles)
    low_seen = bfs(adj, spawn, everything)
    level = {0: grid.high, 1: grid.lagoon_full}
    dry_high = [level[water[i]] - min_y[i] <= args.max_depth for i in range(len(mesh.triangles))]
    high_seen = bfs(adj, spawn, dry_high)
    low_bad, far, high_report = [], [], []
    for m in marks:
        if m['tri'] not in low_seen:
            low_bad.append(m['id'])
        if m['dist'] > args.marker_snap:
            far.append((m['id'], m['dist']))
        if not dry_high[m['tri']]:
            high_report.append({'id': m['id'], 'kind': m['kind'], 'state': 'flooded'})
        elif m['tri'] not in high_seen:
            high_report.append({'id': m['id'], 'kind': m['kind'], 'state': 'isolated'})
    # 连通分量统计
    comp, comps = [-1] * len(mesh.triangles), []
    for s in range(len(mesh.triangles)):
        if comp[s] >= 0:
            continue
        seen = bfs(adj, s, everything)
        for t in seen:
            comp[t] = len(comps)
        comps.append(len(seen))
    # 被淹高度的精度：每个三角形内（格心落在里面的）最低可走格 vs 三个顶点 y 的最小值；正数 = 比最低处高，会晚淹
    floor_h = grid.low - args.max_depth
    cells = [(j, i) for j in range(grid.H) for i in range(grid.W)
             if not (grid.f[j, i] & (grid.F['WALL'] | grid.F['PROP'])) and grid.h[j, i] >= floor_h - 1e-6]
    ptree = STRtree([Point(grid.OX + i + 0.5, grid.OZ + j + 0.5) for j, i in cells])
    pair = ptree.query(tris, predicate='contains')
    lowest, lag_seen = {}, defaultdict(set)
    for t, c in zip(pair[0], pair[1]):
        j, i = cells[c]
        hv = grid.h[j, i]
        lowest[t] = min(lowest.get(t, 1e9), hv)
        if hv < grid.lagoon_full - args.max_depth:
            lag_seen[t].add(1 if grid.f[j, i] & grid.F['LAG'] else 0)
    err = np.array([min_y[t] - lowest[t] for t in lowest])
    accuracy = {'sampledTriangles': int(len(err)), 'p50': round(float(np.percentile(err, 50)), 3),
                'p95': round(float(np.percentile(err, 95)), 3), 'max': round(float(err.max()), 3),
                'over035': int((err > 0.35).sum()), 'over050': int((err > 0.5).sum()),
                'waterBodyMismatch': sum(1 for t, s in lag_seen.items() if any(b != water[t] for b in s))}
    result = {
        'floodHeightAccuracy': accuracy,
        'markers': len(marks),
        'lowTideUnreachable': low_bad,
        'markersFarFromMesh': far,
        'lowTideReachableTriangles': len(low_seen),
        'components': sorted(comps, reverse=True)[:12],
        'componentCount': len(comps),
        'nonManifoldEdges': nonmanifold,
        'highTide': {'sea': grid.high, 'lagoon': grid.lagoon_full,
                     'floodedTriangles': dry_high.count(False), 'reachableTriangles': len(high_seen),
                     'cutOffMarkers': high_report},
        'markerSnap': {m['id']: m['dist'] for m in marks if m['dist'] > 0.5},
    }
    return result


def prune(mesh: MeshBuilder, layout):
    """只留出生点所在的连通块（别的块 AI 走不进去），去掉没人用的顶点。返回被丢掉的块。"""
    tris = [Polygon([(mesh.vertices[i][0], mesh.vertices[i][2]) for i in t]) for t in mesh.triangles]
    adj, _ = adjacency(mesh.triangles)
    spawn = int(STRtree(tris).nearest(Point(layout['spawn']['x'], layout['spawn']['z'])))
    everything = [True] * len(tris)
    keep = bfs(adj, spawn, everything)
    dropped, seen = [], set(keep)
    for s in range(len(tris)):
        if s in seen:
            continue
        comp = bfs(adj, s, everything)
        seen |= comp
        c = shapely.union_all([tris[t] for t in comp]).centroid
        dropped.append({'triangles': len(comp), 'area': round(sum(tris[t].area for t in comp), 2),
                        'center': [round(c.x, 1), round(c.y, 1)]})
    order = sorted(keep)
    used = sorted({i for t in order for i in mesh.triangles[t]})
    remap = {old: new for new, old in enumerate(used)}
    mesh.vertices = [mesh.vertices[i] for i in used]
    mesh.labels = [mesh.labels[t] for t in order]
    mesh.triangles = [[remap[i] for i in mesh.triangles[t]] for t in order]
    dropped.sort(key=lambda d: (-d['area'], d['center']))
    return dropped


def bake(grid: Grid, args, simplify):
    args.simplify = simplify
    walk, label, cliffs, nb = classify(grid, args)
    merged = merge_small_patches(grid, label, args.min_patch)
    heights = Heights(grid, walk, args.cliff)
    region, inset, cover = build_coverage(grid, walk, label, cliffs, args)
    mesh = triangulate(cover, heights)
    dropped = prune(mesh, grid.layout)
    info = {'walkableCells': int(walk.sum()), 'cliffEdges': len(cliffs), 'mergedPatches': merged,
            'coverPolygons': len(cover), 'insetArea': round(inset.area, 1), 'droppedComponents': dropped}
    return mesh, nb, info


def main() -> int:
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser(description='潮闸镇导航网格烘焙')
    ap.add_argument('--src', type=Path, default=DEFAULT_SRC)
    ap.add_argument('--out', type=Path, default=DEFAULT_OUT)
    ap.add_argument('--clearance', type=float, default=0.4, help='离墙内缩（角色半径）')
    ap.add_argument('--min-clearance', type=float, default=0.25, help='简化后允许的最小离墙距离')
    ap.add_argument('--max-depth', type=float, default=0.5, help='最大涉水深')
    ap.add_argument('--cliff', type=float, default=0.65, help='相邻格高差超过它就是断崖')
    ap.add_argument('--simplify', type=float, nargs='+', default=[0.9, 1.0, 1.1, 1.25],
                    help='覆盖简化容差（约等于被删三角形面积的平方根），从小到大试到顶点不超过目标为止')
    ap.add_argument('--min-patch', type=int, default=6, help='潮间带小于这么多格的碎块并入相邻带')
    ap.add_argument('--marker-snap', type=float, default=2.5, help='标记点离最近三角形超过它就报告')
    ap.add_argument('--check', action='store_true')
    args = ap.parse_args()
    t0 = time.time()
    grid = Grid(args.src)
    for tol in list(args.simplify):
        mesh, nb, info = bake(grid, args, tol)
        print('候选 简化容差 %.2f：顶点 %d，三角形 %d' % (tol, len(mesh.vertices), len(mesh.triangles)))
        if len(mesh.vertices) <= TARGET_VERTICES:
            break
    bad = t_junctions(mesh.vertices, mesh.triangles)
    def body(t):
        """潮间带块按标签定水体；「不会被淹」块的三角形最低顶点可能落在相邻低带的边界上（低于 0.125），
        所以也要归属：按形心所在格的 LAG 标记（与运行时按形心查泻湖遮罩同一思路）。"""
        lab = mesh.labels[t]
        if lab > 0:
            return 1 if (lab - 1) // nb == 1 else 0
        x = sum(mesh.vertices[i][0] for i in mesh.triangles[t]) / 3.0
        z = sum(mesh.vertices[i][2] for i in mesh.triangles[t]) / 3.0
        i, j = grid.cell(x, z)
        return 1 if 0 <= i < grid.W and 0 <= j < grid.H and grid.f[j, i] & grid.F['LAG'] else 0
    bodies = [body(t) for t in range(len(mesh.triangles))]
    order = sorted(range(len(mesh.triangles)), key=lambda t: bodies[t])
    mesh.triangles = [mesh.triangles[t] for t in order]
    mesh.labels = [mesh.labels[t] for t in order]
    water = [bodies[t] for t in order]
    min_y = [min(mesh.vertices[i][1] for i in t) for t in mesh.triangles]
    # Unity 坐标与绕序（从 +Y 看顺时针 = 法线朝上）
    verts_u = [(round(x, 4), round(y, 4), round(-z, 4)) for x, y, z in mesh.vertices]
    tris_u = []
    for a, b, c in mesh.triangles:
        pa, pb, pc = verts_u[a], verts_u[b], verts_u[c]
        ny = (pb[2] - pa[2]) * (pc[0] - pa[0]) - (pb[0] - pa[0]) * (pc[2] - pa[2])
        tris_u.append([a, b, c] if ny > 0 else [a, c, b])
    lagoon_start = water.index(1) if 1 in water else len(water)
    nav = shapely.union_all([Polygon([(mesh.vertices[i][0], mesh.vertices[i][2]) for i in t]) for t in mesh.triangles])
    blocked = mask_polygon(grid, (grid.f & (grid.F['WALL'] | grid.F['PROP'])) != 0)
    stats = {
        'vertices': len(verts_u), 'triangles': len(tris_u), 'lagoonTriangles': len(tris_u) - lagoon_start,
        'simplify': args.simplify, 'navArea': round(nav.area, 1), 'minWallClearance': round(nav.distance(blocked), 3),
        'tJunctions': len(bad), 'minYRange': [round(min(min_y), 3), round(max(min_y), 3)],
    }
    deck = grid.F['DECK']
    dev = []
    for x, y, z in mesh.vertices:  # 非甲板顶点与物理地形的高差（顶点取的是逻辑网格高度）
        i, j = grid.cell(x, z)
        if 0 <= i < grid.W and 0 <= j < grid.H and not (grid.f[j, i] & deck):
            dev.append(abs(y - grid.terrain_at(x, z)))
    if dev:
        stats['yVsTerrain'] = {'p50': round(float(np.percentile(dev, 50)), 3), 'p95': round(float(np.percentile(dev, 95)), 3),
                               'max': round(float(max(dev)), 3)}
    stats.update(info)
    doc = {
        'version': 1,
        'generator': 'tools/build_tidegate_navigation.py',
        'coordinates': 'Unity (x, y, z) = three.js (x, y, -z)；三角形从 +Y 看顺时针（法线朝上）',
        'params': {'lowTide': grid.low, 'highTide': grid.high, 'lagoonFull': grid.lagoon_full, 'maxDepth': args.max_depth,
                   'clearance': args.clearance, 'minClearance': args.min_clearance, 'cliff': args.cliff,
                   'simplify': args.simplify, 'minPatch': args.min_patch, 'bandEdges': BAND_EDGES},
        'sources': grid.sources,
        'floodRule': '三角形 t 在当前水位下不可走 ⇔ 水位(water[t]) − minY[t] > maxDepth；water 0 用外海潮位，1 用泻湖水位；'
                     'minY[t] 就是三个顶点 y 的最小值',
        'vertices': [list(v) for v in verts_u],
        'triangles': tris_u,
        'minY': [round(v, 4) for v in min_y],
        'water': water,
        'lagoonStart': lagoon_start,
        'stats': stats,
    }
    failures = []
    if len(verts_u) > MAX_VERTICES:
        failures.append('顶点 %d 超过游戏上限 %d' % (len(verts_u), MAX_VERTICES))
    if bad:
        failures.append('有 %d 处 T 形接点' % len(bad))
    if args.check:
        result = check(grid, mesh, water, min_y, args)
        doc['check'] = result
        if result['lowTideUnreachable']:
            failures.append('低潮时从出生点走不到：' + ', '.join(result['lowTideUnreachable']))
        if result['markersFarFromMesh']:
            # 网格只留出生点所在的连通块，连不上的区域整块被裁掉，所以「走不到」在这里表现为离网格太远
            failures.append('标记点离导航网格太远（所在区域连不到出生点、已被裁掉，或被墙围死）：'
                            + ', '.join('%s(%.2f m)' % m for m in result['markersFarFromMesh']))
        if result['nonManifoldEdges']:
            failures.append('有 %d 条边被三个以上三角形共用' % result['nonManifoldEdges'])
        if result['componentCount'] != 1:
            failures.append('网格不是一整块：%d 个连通块' % result['componentCount'])
    # 耗时只打印不落盘：输出文件内容只由输入决定，同步脚本按 sha256 判断要不要覆盖
    print(json.dumps(dict(stats, seconds=round(time.time() - t0, 1)), ensure_ascii=False))
    if args.check:
        c = doc['check']
        cut = c['highTide']['cutOffMarkers']
        snap = c['markerSnap']
        print('低潮：标记 %d 个，走不到 %d 个；连通块 %d 个；非流形边 %d' % (c['markers'], len(c['lowTideUnreachable']),
                                                         c['componentCount'], c['nonManifoldEdges']))
        print('被淹高度精度（三角形最低顶点 − 其内最低可走格）：' + json.dumps(c['floodHeightAccuracy'], ensure_ascii=False))
        print('高潮（外海 %.2f、泻湖 %.2f）被淹 %d 个三角形；从出生点可达 %d 个' % (
            grid.high, grid.lagoon_full, c['highTide']['floodedTriangles'], c['highTide']['reachableTriangles']))
        print('  标记被淹：' + ', '.join(m['id'] for m in cut if m['state'] == 'flooded'))
        print('  标记被隔开（没淹但走不过去）：' + ', '.join(m['id'] for m in cut if m['state'] == 'isolated'))
        if snap:
            print('标记离网格超过 0.5 m（在实心格里，投影到最近三角形）：%d 个，最远 %.2f m' % (len(snap), max(snap.values())))
    if failures:
        print('TIDEGATE_NAV_FAIL ' + '；'.join(failures))
        return 1
    if len(verts_u) > TARGET_VERTICES:
        print('警告：顶点 %d 超过目标 %d（仍在游戏上限内）' % (len(verts_u), TARGET_VERTICES))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    tmp = args.out.with_suffix('.json.tmp')
    tmp.write_text(json.dumps(doc, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    tmp.replace(args.out)
    print('TIDEGATE_NAV_OK ' + json.dumps({'out': str(args.out), 'vertices': len(verts_u), 'triangles': len(tris_u),
                                            'lagoonTriangles': stats['lagoonTriangles'],
                                            'checked': bool(args.check)}, ensure_ascii=False))
    return 0


if __name__ == '__main__':
    sys.exit(main())
