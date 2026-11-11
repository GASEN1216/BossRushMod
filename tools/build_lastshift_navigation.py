# -*- coding: utf-8 -*-
"""末班编组站导航网格：从 three.js 导出的布局与逻辑网格烘一张共形三角网，给 A* NavMeshGraph 当 sourceMesh。

输入 ArtSource/LastShiftYard/export/（lastshift_layout.json、grid_height.f32、grid_flag.u8、manifest.json），
输出 ArtSource/LastShiftYard/navigation.json。只写这一个文件。以 tools/build_tidegate_navigation.py 为底子，差别：

- 可走区用布局里的精确几何，不用 1 m 格子：站界多边形 − 水池 − 全部实心体（墙、货柜、掩体、栏杆，带朝向），
  与原型 layout.mjs 的 walkable() 同一口径（原型对实心体留 0.29 m 身位，这里按 03 指南留 0.4 m，兜底 0.25 m）。
- 断崖：相邻两格（格心都可走）高差超过 0.5 m（原型 graph 的台阶上限）就切开，两边各让出离墙距离。
  坡道、司机室楼面、北端跨线桥都靠这一条和地面分开，只能从坡道 / 梯段上下。
- 机制状态是列车占线，不是潮水：西线、东线车体包络（原型 trainEnvelope，司机室楼面与跨线桥下除外）
  按格切块，块边界就是三角形的边。每个三角形带 train 位掩码（1 = 西线包络，2 = 东线包络，3 = 两线重叠），
  运行时哪条线上有车停着或在走，就把掩码含这一位的三角形设为不可走。
- 逻辑网格的格心在整数坐标 (originX + i, originZ + j)，格子覆盖格心 ±0.5 m（潮闸镇是左下角对齐，这里不同）。
- 共形、T 形接点检查、只留出生点连通块、顶点 ≤ 4095、1 cm 吸附，照潮闸镇。

输出（Unity 坐标 = three.js (x, y, -z)）：
  vertices   [[x, y, z], ...]
  triangles  [[a, b, c], ...]，从 +Y 往下看顺时针（法线朝上）
  train      每个三角形的占线掩码：0 = 不受列车影响，1 = 西线，2 = 东线，3 = 两线
用法：python tools/build_lastshift_navigation.py [--check] [--src ...] [--out ...]
  --check  另做连通性检查：两线都空时出生点必须连通全部撤离点、搜刮点（站位）、机关（站位）、刷怪点、巡逻点；
           西线有车、东线有车、两线都有车三种状态只报告被隔开的标记。结果写进输出的 check 字段。
成功打印 LASTSHIFT_NAV_OK {...}，失败打印 LASTSHIFT_NAV_FAIL 且不写文件。
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
DEFAULT_SRC = REPO / 'ArtSource' / 'LastShiftYard' / 'export'
DEFAULT_OUT = REPO / 'ArtSource' / 'LastShiftYard' / 'navigation.json'

MAX_VERTICES = 4095
TARGET_VERTICES = 3500
STATES = {'clear': 0, 'westTrain': 1, 'eastTrain': 2, 'bothTrains': 3}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Grid:
    def __init__(self, src: Path):
        self.layout = json.loads((src / 'lastshift_layout.json').read_text(encoding='utf-8'))
        g = self.layout['grid']
        self.W, self.H = int(g['width']), int(g['depth'])
        # 格子 (i, j) 覆盖 [originX + i − 0.5, originX + i + 0.5]；OX/OZ 是格子左下角
        self.OX, self.OZ = float(g['originX']) - 0.5, float(g['originZ']) - 0.5
        self.F = {k: int(v) for k, v in g['flags'].items()}
        if 'trainW' not in self.F:
            raise RuntimeError('逻辑网格没有列车包络标记：用当前 v2/js/export.js 重新导出')
        self.h = np.fromfile(src / g['heightFile'], dtype='<f4').reshape(self.H, self.W).astype(np.float64)
        self.f = np.fromfile(src / g['flagFile'], dtype=np.uint8).reshape(self.H, self.W)
        self.sources = {name: sha256(src / name) for name in ('lastshift_layout.json', g['heightFile'], g['flagFile'])}
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


def rect(cx, cz, w, d, yaw):
    """three.js rotation.y = yaw 的矩形（与 build_lastshift_blend.py 的碰撞盒同一公式）。"""
    co, sn = math.cos(yaw), math.sin(yaw)
    pts = [(cx + co * dx + sn * dz, cz - sn * dx + co * dz) for dx, dz in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2))]
    return Polygon(pts)


def blocked_geometry(layout):
    bodies = [rect(s['center'][0], s['center'][1], s['size'][0], s['size'][2], s.get('yaw') or 0) for s in layout['solids']]
    bodies += [rect(w['x'], w['z'], w['w'], w['d'], w.get('yaw') or 0) for w in layout['waters']]
    return shapely.union_all(bodies)


def classify(grid: Grid, args):
    """格心可走（导出时按原型 walkable() 算）+ 断崖 + 列车包络标签。"""
    walk = (grid.f & grid.F['walkable']) != 0
    label = (((grid.f & grid.F['trainW']) != 0).astype(np.int32) | (((grid.f & grid.F['trainE']) != 0).astype(np.int32) << 1))
    cliffs = []
    for dj, di in ((0, 1), (1, 0)):
        a = walk[:grid.H - dj, :grid.W - di] & walk[dj:, di:]
        dh = np.abs(grid.h[:grid.H - dj, :grid.W - di] - grid.h[dj:, di:])
        for j, i in zip(*np.nonzero(a & (dh > args.cliff))):
            x, z = grid.OX + i + di, grid.OZ + j + dj
            cliffs.append(((x, z), (x + dj, z + di)))
    return walk, label, cliffs


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
    land = Polygon(grid.layout['land'])
    blocked = blocked_geometry(grid.layout)
    region = land.difference(blocked)
    inset = region.buffer(-args.clearance, join_style='mitre', mitre_limit=2.0)
    cl = shapely.union_all([LineString(s) for s in cliffs]) if cliffs else None
    if cl is not None:
        inset = inset.difference(cl.buffer(args.clearance, quad_segs=2))
    pieces, piece_labels = [], []
    for lab in sorted(set(np.unique(label).tolist())):
        part = mask_polygon(grid, label == lab).intersection(inset)
        for p in polygons(part):
            if p.area > 1e-6:
                pieces.append(p)
                piece_labels.append(lab)
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
    simple = shapely.set_precision(np.array(simple, dtype=object), 0.01)
    hard = region.buffer(-args.min_clearance, join_style='mitre', mitre_limit=2.0)
    if cl is not None:
        hard = hard.difference(cl.buffer(args.min_clearance, quad_segs=2))
    out = []
    for f, lab in zip(simple, cov_labels):
        for p in polygons(f.intersection(hard) if not hard.covers(f) else f):
            if p.area > 0.01:
                out.append((shapely.set_precision(p, 0.01), lab))
    return region, inset, blocked, out


def cross2(a, b, c):
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


class MeshBuilder:
    def __init__(self, heights: Heights):
        self.heights = heights
        self.index = {}
        self.vertices = []
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
    """要连通的点：箱柜和机关取玩家站位（use），本体是实心体。"""
    xz = lambda p: (p[0], p[2]) if len(p) == 3 else (p[0], p[1])
    out = [('spawn', layout['spawn']['path'], xz(layout['spawn']['position']), 0.0)]
    out += [('exit', e['path'], xz(e['position']), 0.0) for e in layout['exits']]
    out += [('loot', c['path'], xz(c['use']) if c.get('use') else xz(c['position']), 0.0) for c in layout['loot']]
    out += [('interact', i['path'], xz(i['use']) if i.get('use') else xz(i['position']), i['position'][1]) for i in layout['interacts']]
    out += [('spawn-enemy', s['path'], xz(s['position']), 0.0) for s in layout['spawns']]
    out += [('patrol', 'WP/%s/%s_%d' % (p['group'], p['id'], k), xz(pt), 0.0) for p in layout['patrols'] for k, pt in enumerate(p['points'])]
    return out


def check(grid: Grid, mesh: MeshBuilder, train, args):
    tris = [Polygon([(mesh.vertices[i][0], mesh.vertices[i][2]) for i in t]) for t in mesh.triangles]
    tree = STRtree(tris)
    adj, nonmanifold = adjacency(mesh.triangles)
    marks = []
    for kind, mid, (x, z), _ in markers(grid.layout):
        p = Point(x, z)
        k = int(tree.nearest(p))
        marks.append({'kind': kind, 'id': mid, 'tri': k, 'dist': round(tris[k].distance(p), 3)})
    spawn = marks[0]['tri']
    everything = [True] * len(mesh.triangles)
    clear_seen = bfs(adj, spawn, everything)
    unreachable = [m['id'] for m in marks if m['tri'] not in clear_seen]
    far = [(m['id'], m['dist']) for m in marks if m['dist'] > args.marker_snap]
    states = {}
    for name, mask in STATES.items():
        if not mask:
            continue
        allowed = [not (train[i] & mask) for i in range(len(mesh.triangles))]
        seen = bfs(adj, spawn, allowed)
        states[name] = {'blockedTriangles': allowed.count(False), 'reachableTriangles': len(seen),
                        'onTrack': [m['id'] for m in marks if not allowed[m['tri']]],
                        'cutOff': [m['id'] for m in marks if allowed[m['tri']] and m['tri'] not in seen]}
    comp, comps = [-1] * len(mesh.triangles), []
    for s in range(len(mesh.triangles)):
        if comp[s] >= 0:
            continue
        seen = bfs(adj, s, everything)
        for t in seen:
            comp[t] = len(comps)
        comps.append(len(seen))
    return {'markers': len(marks), 'clearUnreachable': unreachable, 'markersFarFromMesh': far,
            'componentCount': len(comps), 'nonManifoldEdges': nonmanifold, 'trainStates': states,
            'markerSnap': {m['id']: m['dist'] for m in marks if m['dist'] > 0.5}}


def prune(mesh: MeshBuilder, layout):
    tris = [Polygon([(mesh.vertices[i][0], mesh.vertices[i][2]) for i in t]) for t in mesh.triangles]
    adj, _ = adjacency(mesh.triangles)
    sp = layout['spawn']['position']
    spawn = int(STRtree(tris).nearest(Point(sp[0], sp[2])))
    everything = [True] * len(tris)
    keep = bfs(adj, spawn, everything)
    dropped, seen = [], set(keep)
    for s in range(len(tris)):
        if s in seen:
            continue
        comp = bfs(adj, s, everything)
        seen |= comp
        c = shapely.union_all([tris[t] for t in comp]).centroid
        dropped.append({'triangles': len(comp), 'area': round(sum(tris[t].area for t in comp), 2), 'center': [round(c.x, 1), round(c.y, 1)]})
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
    walk, label, cliffs = classify(grid, args)
    heights = Heights(grid, walk, args.cliff)
    region, inset, blocked, cover = build_coverage(grid, walk, label, cliffs, args)
    mesh = triangulate(cover, heights)
    dropped = prune(mesh, grid.layout)
    info = {'walkableCells': int(walk.sum()), 'cliffEdges': len(cliffs), 'coverPolygons': len(cover),
            'insetArea': round(inset.area, 1), 'droppedComponents': dropped}
    return mesh, blocked, info


def main() -> int:
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser(description='末班编组站导航网格烘焙')
    ap.add_argument('--src', type=Path, default=DEFAULT_SRC)
    ap.add_argument('--out', type=Path, default=DEFAULT_OUT)
    ap.add_argument('--clearance', type=float, default=0.4, help='离墙内缩（角色半径）')
    ap.add_argument('--min-clearance', type=float, default=0.25, help='简化后允许的最小离墙距离')
    ap.add_argument('--cliff', type=float, default=0.5, help='相邻格高差超过它就是断崖（原型台阶上限）')
    ap.add_argument('--simplify', type=float, nargs='+', default=[0.6, 0.75, 0.9, 1.0, 1.1, 1.25])
    ap.add_argument('--marker-snap', type=float, default=2.5, help='标记点离最近三角形超过它就报告')
    ap.add_argument('--check', action='store_true')
    args = ap.parse_args()
    t0 = time.time()
    grid = Grid(args.src)
    for tol in list(args.simplify):
        mesh, blocked, info = bake(grid, args, tol)
        print('候选 简化容差 %.2f：顶点 %d，三角形 %d' % (tol, len(mesh.vertices), len(mesh.triangles)))
        if len(mesh.vertices) <= TARGET_VERTICES:
            break
    bad = t_junctions(mesh.vertices, mesh.triangles)
    train = list(mesh.labels)
    verts_u = [(round(x, 4), round(y, 4), round(-z, 4)) for x, y, z in mesh.vertices]
    tris_u = []
    for a, b, c in mesh.triangles:
        pa, pb, pc = verts_u[a], verts_u[b], verts_u[c]
        ny = (pb[2] - pa[2]) * (pc[0] - pa[0]) - (pb[0] - pa[0]) * (pc[2] - pa[2])
        tris_u.append([a, b, c] if ny > 0 else [a, c, b])
    nav = shapely.union_all([Polygon([(mesh.vertices[i][0], mesh.vertices[i][2]) for i in t]) for t in mesh.triangles])
    stats = {'vertices': len(verts_u), 'triangles': len(tris_u), 'simplify': args.simplify, 'navArea': round(nav.area, 1),
             'minWallClearance': round(nav.distance(blocked), 3), 'tJunctions': len(bad),
             'trainTriangles': {'west': sum(1 for t in train if t & 1), 'east': sum(1 for t in train if t & 2), 'both': sum(1 for t in train if t == 3)},
             'yRange': [round(min(v[1] for v in verts_u), 3), round(max(v[1] for v in verts_u), 3)]}
    dev = [abs(y - grid.terrain_at(x, z)) for x, y, z in mesh.vertices if y < 0.3]
    if dev:
        stats['groundVsTerrain'] = {'p50': round(float(np.percentile(dev, 50)), 3), 'p95': round(float(np.percentile(dev, 95)), 3), 'max': round(float(max(dev)), 3)}
    stats.update(info)
    doc = {
        'version': 1,
        'generator': 'tools/build_lastshift_navigation.py',
        'coordinates': 'Unity (x, y, z) = three.js (x, y, -z)；三角形从 +Y 看顺时针（法线朝上）',
        'params': {'clearance': args.clearance, 'minClearance': args.min_clearance, 'cliff': args.cliff, 'simplify': args.simplify},
        'sources': grid.sources,
        'trainRule': '三角形 t 不可走 ⇔ train[t] & 占线位 ≠ 0；占线位 1 = 西线有车，2 = 东线有车（停靠或行驶中都算）',
        'vertices': [list(v) for v in verts_u],
        'triangles': tris_u,
        'train': train,
        'stats': stats,
    }
    failures = []
    if len(verts_u) > MAX_VERTICES:
        failures.append('顶点 %d 超过游戏上限 %d' % (len(verts_u), MAX_VERTICES))
    if bad:
        failures.append('有 %d 处 T 形接点' % len(bad))
    if stats['minWallClearance'] < args.min_clearance - 0.011:
        failures.append('离墙最近 %.3f m，低于 %.2f m' % (stats['minWallClearance'], args.min_clearance))
    if args.check:
        result = check(grid, mesh, train, args)
        doc['check'] = result
        if result['clearUnreachable']:
            failures.append('两线都空时从出生点走不到：' + ', '.join(result['clearUnreachable']))
        if result['markersFarFromMesh']:
            failures.append('标记点离导航网格太远：' + ', '.join('%s(%.2f m)' % m for m in result['markersFarFromMesh']))
        if result['nonManifoldEdges']:
            failures.append('有 %d 条边被三个以上三角形共用' % result['nonManifoldEdges'])
        if result['componentCount'] != 1:
            failures.append('网格不是一整块：%d 个连通块' % result['componentCount'])
    print(json.dumps(dict(stats, seconds=round(time.time() - t0, 1)), ensure_ascii=False))
    if args.check:
        c = doc['check']
        print('两线都空：标记 %d 个，走不到 %d 个；连通块 %d 个；非流形边 %d' % (c['markers'], len(c['clearUnreachable']), c['componentCount'], c['nonManifoldEdges']))
        for name, s in c['trainStates'].items():
            print('  %s：封 %d 个三角形，可达 %d；标记在轨道上 %d 个，被隔开 %d 个%s' % (
                name, s['blockedTriangles'], s['reachableTriangles'], len(s['onTrack']), len(s['cutOff']),
                ('（' + ', '.join(s['cutOff'][:12]) + (' …' if len(s['cutOff']) > 12 else '') + '）') if s['cutOff'] else ''))
        if c['markerSnap']:
            print('标记离网格超过 0.5 m：%d 个，最远 %.2f m' % (len(c['markerSnap']), max(c['markerSnap'].values())))
    if failures:
        print('LASTSHIFT_NAV_FAIL ' + '；'.join(failures))
        return 1
    if len(verts_u) > TARGET_VERTICES:
        print('警告：顶点 %d 超过目标 %d（仍在游戏上限内）' % (len(verts_u), TARGET_VERTICES))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    tmp = args.out.with_suffix('.json.tmp')
    tmp.write_text(json.dumps(doc, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    tmp.replace(args.out)
    print('LASTSHIFT_NAV_OK ' + json.dumps({'out': str(args.out), 'vertices': len(verts_u), 'triangles': len(tris_u), 'checked': bool(args.check)}, ensure_ascii=False))
    return 0


if __name__ == '__main__':
    sys.exit(main())
