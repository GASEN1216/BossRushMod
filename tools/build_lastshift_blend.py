"""Blender 组装：把末班编组站 three.js 场景的导出物装成可编辑的 .blend，并出 Unity 用 FBX、碰撞、标记点、预览与正北 M 图原图。

输入是 three.js 页面 `__lsy.exportForBlender()` 写出的 ArtSource/LastShiftYard/export/（manifest.json、分组 GLB、
地形高度与站界遮罩、逻辑网格、贴图 PNG、lastshift_layout.json）。本脚本只写 --out 目录下自己的文件，不删任何东西。
以 tools/build_tidegate_blend.py 为底子（docs/guides/new-map/03_Blender组装.md），差别：
- 碰撞直接用布局里的实心体盒（中心、尺寸、底高、朝向），不再从网格贪心合并；
- 标记点沿用布局自带的官方路径名（StartPoints/、Exits/、Loot/、Interact/、Spawn/<组>/、WP/<组>/、NightLamp/、POI/）；
- 建筑语义来自 GLB 的 extras.semantic（structure / contents / upper），写成 fbx/lastshift_roof_faces.json 给 Unity 构建器切组。

坐标：three.js (x, y, z) → Blender (x, -z, y)；FBX 按 axis_forward='-Z', axis_up='Y' 导出，进 Unity 后是 (x, y, -z)。

用法（后台，Blender 5.2）：
  D:/blender/blender.exe -b --factory-startup --python-exit-code 1 --python tools/build_lastshift_blend.py -- \
      --src ArtSource/LastShiftYard/export --out ArtSource/LastShiftYard [--skip-render] [--fbx]
成功时打印 LASTSHIFT_BLEND_OK {...}。live（MCP）里导入本模块后调用 build(src, out, fbx, render)，不会退出 Blender。
"""
import argparse
import hashlib
import json
import math
import os
import struct
import sys
import time
from pathlib import Path

import bpy
import numpy as np
from collections import defaultdict

from mathutils import Euler, Matrix, Vector, kdtree

GROUPS = ['terrain', 'buildings', 'landmark', 'track', 'structures', 'props', 'scatter', 'gameplay', 'yard', 'vehicles', 'entrance']
FBX_GROUPS = ['terrain', 'buildings', 'landmark', 'track', 'structures', 'props', 'scatter', 'gameplay', 'yard', 'vehicles']
# 这几组里共享网格的对象（GPU 实例展开来的轨枕、道砟石、草丛、栏杆柱……）不逐个进 FBX，改出「原型 FBX + 实例矩阵表」，
# Unity 构建器按区块合批（潮闸镇散布的做法）。其余组实例很少、又要保留层级（列车整列移动、建筑语义），导出前复制成单用户网格。
INSTANCE_GROUPS = ['scatter', 'track', 'yard', 'structures']
C3B = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))  # three.js → Blender
S_UNITY = Matrix.Diagonal((1, 1, -1, 1))                                 # three.js → Unity
STATS = {}


def T(x, y, z):
    """three.js → Blender。"""
    return Vector((x, -z, y))


def new_collection(name, parent=None):
    c = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(c)
    return c


def principled(mat):
    if hasattr(mat, 'use_nodes') and not mat.use_nodes:
        mat.use_nodes = True
    return next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)


def import_glb(path, target):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    new = [o for o in bpy.data.objects if o not in before]
    for o in new:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        target.objects.link(o)
    return new


def ensure_vertex_color(objs):
    """glTF 的 COLOR_0 要乘到底色上；导入器没接的材质这里补一个颜色属性节点。"""
    fixed, done = 0, set()
    for o in objs:
        if o.type != 'MESH' or not o.data.color_attributes:
            continue
        attr = o.data.color_attributes[0].name
        for slot in o.material_slots:
            m = slot.material
            if not m or m.name in done:
                continue
            done.add(m.name)
            nt = m.node_tree
            if any(n.type == 'VERTEX_COLOR' for n in nt.nodes):
                continue
            p = principled(m)
            if not p:
                continue
            vc = nt.nodes.new('ShaderNodeVertexColor'); vc.layer_name = attr
            mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs[0].default_value = 1.0
            base = p.inputs['Base Color']
            if base.is_linked:
                nt.links.new(base.links[0].from_socket, mix.inputs[6])
            else:
                mix.inputs[6].default_value = base.default_value
            nt.links.new(vc.outputs['Color'], mix.inputs[7])
            nt.links.new(mix.outputs[2], base)
            fixed += 1
    return fixed


def tris_of(objs):
    n = 0
    for o in objs:
        if o.type == 'MESH':
            n += sum(len(p.vertices) - 2 for p in o.data.polygons)
    return n


# ---------------- 实例颜色：glTF 导入器丢掉 EXT_mesh_gpu_instancing 的 _COLOR_0，从 GLB 读回来 ----------------
GLTF_TYPES = {5126: ('f', 4), 5121: ('B', 1), 5123: ('H', 2)}
GLTF_COUNT = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def read_glb(path):
    b = path.read_bytes()
    jl = struct.unpack_from('<I', b, 12)[0]
    doc = json.loads(b[20:20 + jl])
    bin_off = 20 + jl + 8
    return doc, b[bin_off:bin_off + struct.unpack_from('<I', b, 20 + jl)[0]]


def read_accessor(doc, blob, idx):
    a = doc['accessors'][idx]; v = doc['bufferViews'][a['bufferView']]
    fmt, size = GLTF_TYPES[a['componentType']]; n = GLTF_COUNT[a['type']]
    stride = v.get('byteStride') or size * n
    base = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    scale = {1: 255.0, 2: 65535.0}.get(size) if a.get('normalized') else None
    out = []
    for i in range(a['count']):
        vals = struct.unpack_from('<' + fmt * n, blob, base + i * stride)
        out.append([x / scale for x in vals] if scale else list(vals))
    return out


def apply_instance_colors(src, man, colls):
    """按节点名（导出时临时命名为 LSYI_<序号>）和实例位置把颜色对回 Blender 对象：o['lsy_tint'] + 视图颜色 o.color。"""
    to_gltf = C3B.inverted()
    tinted, worst, unmatched = 0, 0.0, 0
    for k in GROUPS:
        g = man['groups'].get(k)
        if not g:
            continue
        doc, blob = read_glb(src / g['file'])
        nodes = {}
        for nd in doc.get('nodes', []):
            ext = nd.get('extensions', {}).get('EXT_mesh_gpu_instancing')
            if ext and '_COLOR_0' in ext['attributes'] and nd.get('name'):
                nodes[nd['name']] = (read_accessor(doc, blob, ext['attributes']['TRANSLATION']), read_accessor(doc, blob, ext['attributes']['_COLOR_0']))
        if not nodes:
            continue
        for parent in colls[k].all_objects:
            hit = nodes.get(parent.name.split('.')[0]) if parent.name.startswith('LSYI_') else None
            if not hit:
                continue
            pos, col = hit
            kd = kdtree.KDTree(len(pos))
            for i, t in enumerate(pos):
                kd.insert(t, i)
            kd.balance()
            used, inv = set(), parent.matrix_world.inverted()
            for o in parent.children:
                if o.type != 'MESH':
                    continue
                q = to_gltf @ (inv @ o.matrix_world).to_translation()
                best = next(((i, d) for (_, i, d) in sorted(kd.find_range(q, 0.01), key=lambda r: r[2]) if i not in used), None)
                if best is None or best[1] > 0.01:
                    unmatched += 1
                    continue
                used.add(best[0]); worst = max(worst, best[1])
                c = col[best[0]]
                o['lsy_tint'] = [round(c[0], 4), round(c[1], 4), round(c[2], 4)]
                o.color = (c[0], c[1], c[2], 1.0)
                for slot in o.material_slots:
                    if slot.material:
                        tint_material(slot.material)
                tinted += 1
    STATS['instanceColors'] = {'tinted': tinted, 'unmatched': unmatched, 'maxMatchDistance': round(worst, 5)}
    if unmatched:
        raise RuntimeError('有 %d 个实例对不上 GLB 里的实例颜色' % unmatched)


def tint_material(m):
    """底色乘物体颜色（Object Info），只影响带 lsy_tint 的实例；其余对象的物体颜色是白色。原底色记在 lsy_base。"""
    if m.get('lsy_tinted'):
        return
    p = principled(m)
    if not p:
        return
    nt = m.node_tree; bc = p.inputs['Base Color']
    m['lsy_base'] = [round(v, 4) for v in bc.default_value]
    m['lsy_base_linked'] = int(bc.is_linked)
    oi = nt.nodes.new('ShaderNodeObjectInfo')
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs[0].default_value = 1.0
    if bc.is_linked:
        nt.links.new(bc.links[0].from_socket, mix.inputs[6])
    else:
        mix.inputs[6].default_value = bc.default_value
    nt.links.new(oi.outputs['Color'], mix.inputs[7])
    nt.links.new(mix.outputs[2], bc)
    m['lsy_tinted'] = 1


def fix_outer_grit(colls):
    """站外地面：three.js 是 diffuse *= 0.72 + 0.57 * grit（onBeforeCompile），glTF 只带出了贴图本身，直接相乘会压暗。"""
    fixed = 0
    for o in colls['terrain'].all_objects:
        names = (o.name + ' ' + (o.parent.name if o.parent else '')).lower()
        if o.type != 'MESH' or 'beyond' not in names:
            continue
        for slot in o.material_slots:
            m = slot.material
            if not m or m.get('lsy_grit'):
                continue
            nt = m.node_tree
            for tex in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
                for link in list(tex.outputs['Color'].links):
                    dest = link.to_socket; nt.links.remove(link)
                    mad = nt.nodes.new('ShaderNodeMath'); mad.operation = 'MULTIPLY_ADD'
                    mad.inputs[1].default_value = 0.57; mad.inputs[2].default_value = 0.72
                    nt.links.new(tex.outputs['Color'], mad.inputs[0]); nt.links.new(mad.outputs[0], dest)
                    fixed += 1
            m['lsy_grit'] = 1
    STATS['outerGritRemapped'] = fixed


# ---------------- 地形高度场（碰撞 / 导航用，渲染隐藏；可见地面来自 terrain GLB） ----------------
def build_height_collider(src, man, coll):
    t = man['terrain']; W, H = t['width'], t['height']
    h = np.fromfile(src / t['heightFile'], dtype='<f4').reshape(H, W)
    step = 2  # 2 m 足够做碰撞与导航参照；可见地形用 GLB
    hs = h[::step, ::step]; Hh, Wh = hs.shape
    xs = t['x0'] + np.arange(Wh, dtype=np.float32) * step
    zs = t['z0'] + np.arange(Hh, dtype=np.float32) * step
    X, Z = np.meshgrid(xs, zs)
    verts = np.stack([X, -Z, hs], axis=-1).reshape(-1, 3)
    idx = np.arange(Wh * Hh).reshape(Hh, Wh)
    a = idx[:-1, :-1].ravel(); b = idx[:-1, 1:].ravel(); c = idx[1:, 1:].ravel(); d = idx[1:, :-1].ravel()
    quads = np.stack([a, d, c, b], axis=1)
    me = bpy.data.meshes.new('COL_TerrainHeight')
    me.vertices.add(len(verts)); me.vertices.foreach_set('co', verts.ravel())
    me.loops.add(quads.size); me.loops.foreach_set('vertex_index', quads.ravel().astype(np.int32))
    me.polygons.add(len(quads)); me.polygons.foreach_set('loop_start', (np.arange(len(quads)) * 4).astype(np.int32))
    me.update(calc_edges=True); me.validate()
    ob = bpy.data.objects.new('COL_TerrainHeight', me); coll.objects.link(ob)
    ob.display_type = 'WIRE'; ob.hide_render = True
    STATS['terrainCollider'] = {'vertices': len(me.vertices), 'faces': len(me.polygons), 'cell': step,
                                'minY': round(float(h.min()), 3), 'maxY': round(float(h.max()), 3)}
    return ob


# ---------------- 碰撞：布局里的实心体盒 ----------------
# 读图通则（SkyIsland/AGENTS.md §6.1，2026-11-12）在这里落两条：
# - 坡道 / 梯段 / 跨线桥引坡两侧的栏杆跟着坡走：原先是一整块水平盒（底高取一个常数），低处悬空、高处埋进坡面，
#   上半段坡道实际没有侧挡。现在按逻辑网格高度（栏杆内侧 1 m 内的最高格）每 1 m 切一段，写成两端点 `segment`。
# - 0.12–0.6 m 的矮实心体不给实体：压在另一根柱子下面的（悬车桁架柱脚）并进那根柱子，其余（药箱 0.58 m）去掉。
LOW_LIMIT = 0.6
SLOPED_RAILS = ('northRail', 'rampRail')


def grid_heights(src, layout):
    g = layout['grid']
    h = np.fromfile(src / g['heightFile'], dtype='<f4').reshape(g['depth'], g['width'])
    ox, oz = g['originX'], g['originZ']

    def at(x, z):
        i, j = int(math.floor(x - ox + 0.5)), int(math.floor(z - oz + 0.5))
        if 0 <= i < g['width'] and 0 <= j < g['depth']:
            return float(h[j, i])
        return 0.0
    return at


def rail_segments(s, height_at):
    cx, cz = s['center']; w, h, d = s['size']
    along_x = w >= d
    length, thick = (w, d) if along_x else (d, w)
    n = max(1, int(math.ceil(length - 1e-6)))
    out = []

    def floor_at(t):
        x = cx + (t if along_x else 0.0); z = cz + (0.0 if along_x else t)
        best = 0.0
        for off in (-1.0, -0.5, 0.0, 0.5, 1.0):  # 栏杆立在边上：取内外 1 m 内最高的那格当脚下
            best = max(best, height_at(x + (0.0 if along_x else off), z + (off if along_x else 0.0)))
        return x, best, z
    for k in range(n):
        t0, t1 = -length / 2 + length * k / n, -length / 2 + length * (k + 1) / n
        x0, y0, z0 = floor_at(t0); x1, y1, z1 = floor_at(t1)
        out.append({'id': '%s_%d' % (s['id'], k), 'segment': [[round(x0, 3), round(y0, 3), round(z0, 3)], [round(x1, 3), round(y1, 3), round(z1, 3)]],
                    'size': [thick, h, round(math.hypot(x1 - x0, z1 - z0, y1 - y0), 3)]})
    return out


def readable_solids(layout, src):
    solids = [dict(s) for s in layout['solids']]
    notes = {'merged': [], 'dropped': []}
    for s in solids:
        if not (0.12 < s['size'][1] < LOW_LIMIT):
            continue
        top = s['baseY'] + s['size'][1]
        above = next((o for o in solids if o is not s and abs(o['baseY'] - top) < 0.05
                      and abs(o['center'][0] - s['center'][0]) < s['size'][0] / 2 and abs(o['center'][1] - s['center'][1]) < s['size'][2] / 2), None)
        if above is not None:  # 柱脚并进柱子：柱子一直落到地
            above['size'] = [above['size'][0], round(above['size'][1] + (above['baseY'] - s['baseY']), 3), above['size'][2]]
            above['baseY'] = s['baseY']
            notes['merged'].append([s['id'], above['id']])
        else:
            notes['dropped'].append(s['id'])
        s['_skip'] = True
    return [s for s in solids if not s.get('_skip')], notes


def build_collision(layout, coll, src):
    verts, faces, info = [], [], {}
    height_at = grid_heights(src, layout)
    solids, notes = readable_solids(layout, src)
    for s in solids:
        cx, cz = s['center']; w, h, d = s['size']; y0 = s['baseY']; yaw = s.get('yaw', 0) or 0
        if w <= 0 or d <= 0 or h <= 0:
            continue
        if s['type'] == 'rail' and s['id'].startswith(SLOPED_RAILS):
            segs = rail_segments(s, height_at)
            info.setdefault('railSegments', []).extend(segs)
            for seg in segs:
                (x0, a0, z0), (x1, a1, z1) = seg['segment']
                b = len(verts); t = seg['size'][0] / 2
                nx, nz = (0.0, t) if abs(x1 - x0) >= abs(z1 - z0) else (t, 0.0)
                for (x, y, z) in ((x0, a0, z0), (x1, a1, z1)):
                    verts.append(T(x - nx, y, z - nz)); verts.append(T(x + nx, y, z + nz))
                for (x, y, z) in ((x0, a0, z0), (x1, a1, z1)):
                    verts.append(T(x - nx, y + h, z - nz)); verts.append(T(x + nx, y + h, z + nz))
                faces += [(b, b + 1, b + 3, b + 2), (b + 4, b + 6, b + 7, b + 5), (b, b + 4, b + 5, b + 1),
                          (b + 2, b + 3, b + 7, b + 6), (b, b + 2, b + 6, b + 4), (b + 1, b + 5, b + 7, b + 3)]
            continue
        co, sn = math.cos(yaw), math.sin(yaw)
        corners = []
        for (dx, dz) in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)):
            corners.append((cx + co * dx + sn * dz, cz - sn * dx + co * dz))
        b = len(verts)
        for (x, z) in corners:
            verts.append(T(x, y0, z))
        for (x, z) in corners:
            verts.append(T(x, y0 + h, z))
        faces += [(b, b + 3, b + 2, b + 1), (b + 4, b + 5, b + 6, b + 7), (b, b + 1, b + 5, b + 4),
                  (b + 1, b + 2, b + 6, b + 5), (b + 2, b + 3, b + 7, b + 6), (b + 3, b, b + 4, b + 7)]
        info.setdefault(s['type'] or 'block', []).append({'id': s['id'], 'center': [cx, round(y0 + h / 2, 3), cz], 'size': [w, h, d], 'yaw': yaw})
    me = bpy.data.meshes.new('COL_Solids'); me.from_pydata(verts, [], faces)
    ob = bpy.data.objects.new('COL_Solids', me); coll.objects.link(ob)
    ob.display_type = 'WIRE'; ob.hide_render = True
    coll.hide_render = True
    STATS['collisionBoxes'] = {k: len(v) for k, v in info.items()}
    STATS['readability'] = notes
    info['readability'] = notes
    return info


# ---------------- 标记点（官方命名，路径来自布局） ----------------
def empty(name, pos, coll, shape='PLAIN_AXES', size=0.6, yaw=None, **props):
    o = bpy.data.objects.new(name, None); o.empty_display_type = shape; o.empty_display_size = size
    o.location = T(*pos); coll.objects.link(o)
    if yaw is not None:
        o.rotation_euler = Euler((0, 0, yaw))
    for k, v in props.items():
        if v is None:
            continue
        o[k] = v if isinstance(v, (int, float, str)) else json.dumps(v, ensure_ascii=False)
    return o


def build_markers(layout, coll):
    n = 0
    sp = layout['spawn']; empty(sp['path'], sp['position'], coll, 'ARROWS', 1.2, sp.get('yaw')); n += 1
    for e in layout['exits']:
        empty(e['path'], e['position'], coll, 'CIRCLE', e['radius'], e.get('yaw'), name_cn=e['name'], seconds=e['seconds'], rule=e['rule']); n += 1
    for c in layout['loot']:
        empty(c['path'], c['position'], coll, 'CUBE', 0.5, c['yaw'], name_cn=c['name'], type=c['type'], capacity=c['capacity'], fixed=int(bool(c['fixed'])), use=c['use']); n += 1
    for i in layout['interacts']:
        empty(i['path'], i['position'], coll, 'SPHERE', 0.5, i['yaw'], name_cn=i['name'], use=i['use']); n += 1
    for s in layout['spawns']:
        empty(s['path'], s['position'], coll, 'SINGLE_ARROW', 1.0, s['yaw'], name_cn=s['name'], type=s['type'], quality=s['quality'], line=s['line']); n += 1
    for p in layout['patrols']:
        for k, pt in enumerate(p['points']):
            empty('WP/' + p['group'] + '/' + p['id'] + '_' + str(k), pt, coll, 'PLAIN_AXES', 0.4); n += 1
    for k, l in enumerate(layout['lamps']):
        empty('NightLamp/' + str(k), [l['position'][0], l['height'], l['position'][2]], coll, 'SPHERE', 0.2); n += 1
    for p in layout['pois']:
        empty('POI/' + p['id'], p['position'], coll, 'PLAIN_AXES', 2.0, name_cn=p['name']); n += 1
    STATS['markers'] = n


# ---------------- 建筑语义：对象 → structure / contents / upper ----------------
def building_semantics(coll, out_path, sources):
    rows = []
    for o in coll.all_objects:
        if o.type != 'MESH':
            continue
        sem, bld, p = o.get('semantic'), o.get('building'), o
        while p is not None and (sem is None or bld is None):
            if sem is None and p.get('semantic') is not None:
                sem = p.get('semantic')
            if bld is None and p.get('building') is not None:
                bld = p.get('building')
            elif bld is None and p.name.startswith('BLD_'):
                bld = p.name[4:].split('_')[0].split('.')[0]
            p = p.parent
        rows.append({'object': o.name, 'building': bld, 'semantic': sem or 'structure', 'triangles': sum(len(f.vertices) - 2 for f in o.data.polygons)})
    summary = {}
    for r in rows:
        s = summary.setdefault(r['building'] or '?', {}).setdefault(r['semantic'], {'objects': 0, 'triangles': 0})
        s['objects'] += 1; s['triangles'] += r['triangles']
    out_path.parent.mkdir(parents=True, exist_ok=True)
    # 语义按对象（three.js 生成时 semantic.mjs 打的 extras），不是逐面；sourceSha256 让 Unity 侧核对语义与 FBX 出自同一次导出
    out_path.write_text(json.dumps({'schema': 1, 'granularity': 'object', 'sourceSha256': sources, 'objects': rows, 'summary': summary},
                                   ensure_ascii=False, indent=1), encoding='utf-8')
    STATS['semantics'] = summary
    return summary


# ---------------- 实例：原型 + 矩阵表 ----------------
def source_label(o):
    while o is not None:
        n = o.name.split('.')[0]
        if not (n.startswith('Node_') or n.startswith('Mesh_')):
            return o.name
        o = o.parent
    return None


def split_instances(colls, proto_coll):
    """返回不进分组 FBX 的对象名集合。矩阵是 Unity 世界坐标的 3×4（行优先），作用在原型网格导入 Unity 后的本地坐标上
    （= three.js 本地坐标 z 取反，与分组 FBX 同一套导入换算）。"""
    inv = C3B.inverted(); skip, table, zero = set(), {}, 0
    for g in INSTANCE_GROUPS:
        by = defaultdict(list)
        for o in colls[g].all_objects:
            if o.type == 'MESH':
                by[o.data.name].append(o)
        k = 0
        for mname in sorted(by):
            users = sorted(by[mname], key=lambda o: o.name)
            if len(users) < 2:
                continue
            name = 'PROTO_%s_%02d' % (g, k); k += 1
            proto = bpy.data.objects.new(name, users[0].data); proto_coll.objects.link(proto)
            rows, tints = [], []
            for o in users:
                skip.add(o.name)
                if abs(o.matrix_world.to_3x3().determinant()) < 1e-9:
                    zero += 1  # 缩放为 0 的空槽（实例池里没用上的位置），不进 Unity
                    continue
                mu = S_UNITY @ (inv @ o.matrix_world @ C3B) @ S_UNITY
                rows.append([round(mu[r][c], 5) for r in range(3) for c in range(4)])
                tints.append(list(o.get('lsy_tint', [1.0, 1.0, 1.0])))
            me = users[0].data
            table[name] = {'group': g, 'mesh': mname, 'source': source_label(users[0]),
                           'materials': list(me.materials),  # 材质对象；写盘时取名字（导出前会改成短名）
                           'triangles': sum(len(f.vertices) - 2 for f in me.polygons), 'instances': rows,
                           'tints': tints if any(t != [1.0, 1.0, 1.0] for t in tints) else None}
    STATS['instancePrototypes'] = len(table); STATS['zeroScaleInstancesDropped'] = zero
    STATS['instances'] = {g: sum(len(v['instances']) for v in table.values() if v['group'] == g) for g in INSTANCE_GROUPS}
    STATS['instanceTriangles'] = sum(v['triangles'] * len(v['instances']) for v in table.values())
    return skip, table


def write_instances(table, out_json):
    for v in table.values():
        v['materials'] = [m.name if m else None for m in v['materials']]
    out_json.write_text(json.dumps({'matrix': 'Unity 世界坐标 3×4 行优先 [r00 r01 r02 tx, r10 r11 r12 ty, r20 r21 r22 tz]；'
                                              '原型网格在 LastShiftYard_instance_prototypes.fbx，节点名 = 键',
                                    'tints': '与 instances 平行的线性 RGB，乘在材质底色上（three.js instanceColor）；null = 全白',
                                    'prototypes': table}, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')


def line_tubes(coll, radius=0.035):
    """three.js 的 LineSegments（站外电杆之间的接触网线）导入后是只有边的网格，FBX 进 Unity 没有三角形、材质掉成默认 Lit。
    导出前每条边换成 4 面细管（对象变换、材质不变），45 m 游戏镜头下约一像素宽，和 three.js 的 1 像素线相当。"""
    n = 0
    for o in coll.all_objects:
        if o.type != 'MESH' or len(o.data.polygons) or not len(o.data.edges):
            continue
        me = o.data; verts, faces = [], []
        for e in me.edges:
            a, b = me.vertices[e.vertices[0]].co.copy(), me.vertices[e.vertices[1]].co.copy()
            d = b - a
            if d.length < 1e-6:
                continue
            d.normalize()
            side = d.cross(Vector((0, 0, 1)))
            if side.length < 1e-3:
                side = d.cross(Vector((1, 0, 0)))
            side.normalize(); up = side.cross(d).normalized()
            ring = [side * radius, up * radius, -side * radius, -up * radius]
            base = len(verts)
            verts += [a + r for r in ring] + [b + r for r in ring]
            for k in range(4):
                k2 = (k + 1) % 4
                faces.append((base + k, base + k2, base + 4 + k2, base + 4 + k))
        tube = bpy.data.meshes.new(me.name + '_tubes'); tube.from_pydata([tuple(v) for v in verts], [], faces)
        for m in me.materials:
            tube.materials.append(m)
        o.data = tube; n += 1
    return n


TINT_VARIANTS = {}


def make_single_user(coll):
    """FBX 导出器对共享网格只给第一个对象连材质，其余对象进 Unity 没有材质；层级要保留的小组逐个复制网格。
    带实例颜色的对象换成「材质 + 颜色」变体（lsy_tint 记在材质上，materials.json 写成 tint），因为 FBX 只认材质。"""
    seen, copied = set(), 0
    for o in sorted(coll.all_objects, key=lambda o: o.name):
        if o.type != 'MESH':
            continue
        if o.data.name in seen:
            o.data = o.data.copy(); copied += 1
        seen.add(o.data.name)
        tint = o.get('lsy_tint')
        if tint is None:
            continue
        tint = [round(v, 3) for v in tint]
        for i, m in enumerate(o.data.materials):
            if m is None:
                continue
            key = (m.name, tuple(tint))
            v = TINT_VARIANTS.get(key)
            if v is None:
                v = m.copy(); v.name = m.name[:36] + '#%.3f,%.3f,%.3f' % tuple(tint); v['lsy_tint'] = tint
                TINT_VARIANTS[key] = v
            o.data.materials[i] = v
    STATS['tintVariants'] = len(TINT_VARIANTS)
    return copied


# ---------------- 光照、相机、预览 ----------------
def setup_lighting():
    S = T(-46, 76, 42).normalized()  # 与 three.js 白天太阳同向
    sun_data = bpy.data.lights.new('LSY_Sun', 'SUN'); sun_data.energy = 4.2; sun_data.color = (1.0, 0.94, 0.85); sun_data.angle = math.radians(1.5)
    sun = bpy.data.objects.new('LSY_Sun', sun_data); bpy.context.scene.collection.objects.link(sun)
    sun.rotation_euler = S.to_track_quat('Z', 'Y').to_euler()
    w = bpy.data.worlds.new('LSY_Day'); bpy.context.scene.world = w
    if hasattr(w, 'use_nodes') and not w.use_nodes:
        w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (0.68, 0.76, 0.82, 1); bg.inputs['Strength'].default_value = 1.0


def add_camera(name, target, yaw, pitch, dist, fov_deg, coll):
    tx, ty, tz = target
    pos = (tx + math.sin(yaw) * math.cos(pitch) * dist, ty + math.sin(pitch) * dist, tz + math.cos(yaw) * math.cos(pitch) * dist)
    cd = bpy.data.cameras.new(name); cd.sensor_fit = 'VERTICAL'; cd.angle = math.radians(fov_deg); cd.clip_end = 3000
    cam = bpy.data.objects.new(name, cd); coll.objects.link(cam)
    cam.location = T(*pos)
    cam.rotation_euler = (T(tx, ty, tz) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    return cam


def setup_render(scene):
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            scene.render.engine = eng; break
        except TypeError:
            continue
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    scene.render.image_settings.file_format = 'PNG'
    try:
        scene.view_settings.view_transform = 'AgX'
    except TypeError:
        pass
    ev = getattr(scene, 'eevee', None)
    if ev is not None:
        for attr, val in (('taa_render_samples', 32), ('use_shadows', True), ('use_raytracing', True)):
            if hasattr(ev, attr):
                try:
                    setattr(ev, attr, val)
                except (TypeError, AttributeError):
                    pass


def upstream(node, types, seen=None):
    seen = seen if seen is not None else set()
    found = []
    for inp in node.inputs:
        for link in inp.links:
            n = link.from_node
            if n.name in seen:
                continue
            seen.add(n.name)
            if n.type in types:
                found.append(n)
            found += upstream(n, types, seen)
    return found


def export_material_manifest(colls, out_dir):
    """给 Unity 构建器的材质清单：底色、贴图、自发光、透明、是否乘顶点色。贴图另存 PNG。"""
    tex_dir = out_dir / 'textures'; tex_dir.mkdir(parents=True, exist_ok=True)
    mats, saved = {}, {}
    for coll in colls:
        for o in coll.all_objects:
            if o.type != 'MESH':
                continue
            has_vc = len(o.data.color_attributes) > 0
            for slot in o.material_slots:
                m = slot.material
                if not m:
                    continue
                entry = mats.get(m.name)
                if entry is None:
                    p = principled(m)
                    entry = {'baseColor': [1, 1, 1, 1], 'texture': None, 'emission': [0, 0, 0], 'roughness': 0.8, 'metallic': 0.0, 'alpha': 1.0, 'vertexColor': False, 'meshesWithColors': 0}
                    if p:
                        bc = p.inputs['Base Color']
                        if m.get('lsy_tinted') and not m.get('lsy_base_linked'):
                            entry['baseColor'] = list(m['lsy_base'])
                        elif not bc.is_linked:
                            entry['baseColor'] = [round(v, 4) for v in bc.default_value]
                        if m.get('lsy_tint') is not None:
                            entry['tint'] = list(m['lsy_tint'])
                        if m.get('lsy_source'):
                            entry['source'] = m['lsy_source']
                        imgs = [n for n in upstream(p, {'TEX_IMAGE'}) if n.image] if bc.is_linked else []
                        if imgs:
                            img = imgs[0].image
                            if img.name not in saved:
                                safe = ''.join(c if c.isalnum() or c in '-_' else '_' for c in img.name) + '.png'
                                try:
                                    img.filepath_raw = str(tex_dir / safe); img.file_format = 'PNG'; img.save(); saved[img.name] = 'textures/' + safe
                                except RuntimeError:
                                    saved[img.name] = None
                            entry['texture'] = saved[img.name]
                        entry['vertexColor'] = bool(upstream(p, {'VERTEX_COLOR'})) if bc.is_linked else False
                        es = p.inputs.get('Emission Strength'); ec = p.inputs.get('Emission Color')
                        if ec is not None and es is not None and not ec.is_linked:
                            entry['emission'] = [round(v * es.default_value, 4) for v in ec.default_value[:3]]
                        entry['roughness'] = round(p.inputs['Roughness'].default_value, 3)
                        entry['metallic'] = round(p.inputs['Metallic'].default_value, 3)
                        entry['alpha'] = round(p.inputs['Alpha'].default_value, 3)
                    mats[m.name] = entry
                if has_vc:
                    entry['meshesWithColors'] += 1
    (out_dir / 'materials.json').write_text(json.dumps(mats, ensure_ascii=False, indent=1), encoding='utf-8')
    STATS['materials'] = len(mats); STATS['materialTextures'] = sum(1 for v in saved.values() if v)
    return mats


def layer_collection(name, root=None):
    root = root or bpy.context.view_layer.layer_collection
    if root.name == name:
        return root
    for c in root.children:
        r = layer_collection(name, c)
        if r:
            return r
    return None


def render_minimap(scene, out, cams_coll):
    """正北正交原图（图片上方 = three −z = Unity +z）。站界 168×132 m，四周各留 20 m 给羽化。"""
    cd = bpy.data.cameras.new('Cam_Minimap'); cd.type = 'ORTHO'; cd.ortho_scale = 208; cd.clip_end = 500
    cam = bpy.data.objects.new('Cam_Minimap', cd); cams_coll.objects.link(cam)
    cam.location = (0, 0, 200); cam.rotation_euler = (0, 0, 0)
    old = (scene.camera, scene.render.resolution_x, scene.render.resolution_y)
    scene.camera = cam; scene.render.resolution_x = scene.render.resolution_y = 1440
    md = out / 'minimap'; md.mkdir(exist_ok=True)
    scene.render.filepath = str(md / 'lastshift_ortho.png'); bpy.ops.render.render(write_still=True)
    scene.camera, scene.render.resolution_x, scene.render.resolution_y = old
    (md / 'lastshift_minimap.json').write_text(json.dumps({'sceneId': 'BossRush_LastShiftYard', 'imageWorldSize': cd.ortho_scale, 'mapWorldCenter': [0, 0, 0],
                                                           'note': 'Unity 坐标；图片上方是 three −z（Unity +z，北）'}, ensure_ascii=False), encoding='utf-8')
    return str(md / 'lastshift_ortho.png')


# ---------------- 主流程 ----------------
def build(src, out, fbx=True, render=True):
    src, out = Path(src).resolve(), Path(out).resolve(); out.mkdir(parents=True, exist_ok=True)
    STATS.clear()
    man = json.loads((src / 'manifest.json').read_text(encoding='utf-8'))
    layout = json.loads((src / 'lastshift_layout.json').read_text(encoding='utf-8'))
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene; scene.name = 'LastShiftYard'
    scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 1
    root = new_collection('LastShiftYard')
    colls = {k: new_collection('LSY_' + k, root) for k in GROUPS + ['collision', 'markers', 'cameras']}
    vc_fixed = 0
    for k in GROUPS:
        g = man['groups'].get(k)
        if not g:
            continue
        objs = import_glb(src / g['file'], colls[k])
        vc_fixed += ensure_vertex_color(objs)
        STATS['group_' + k] = {'objects': len(objs), 'meshes': sum(1 for o in objs if o.type == 'MESH'), 'triangles': tris_of(objs),
                               'exportedTriangles': g.get('triangles')}
    STATS['vertexColorMaterialsPatched'] = vc_fixed
    dithered = 0
    for m in bpy.data.materials:
        if getattr(m, 'surface_render_method', None) == 'BLENDED':
            m.surface_render_method = 'DITHERED'; dithered += 1
    STATS['blendedToDithered'] = dithered
    apply_instance_colors(src, man, colls)
    fix_outer_grit(colls)
    # 入口接驳小景在 three.js 里放在 (400, 0, 400)，不属于出击场景：只保留在 .blend，不进渲染与出击 FBX。
    colls['entrance'].hide_render = True
    build_height_collider(src, man, colls['collision'])
    boxes = build_collision(layout, colls['collision'], src)
    build_markers(layout, colls['markers'])
    colls['markers'].hide_render = True
    setup_lighting()
    cams = []
    for st in layout['stations']:
        if isinstance(st, dict):
            cams.append(add_camera('Cam_' + st['name'].replace(' · ', '_').replace(' ', ''), st['target'], st['yaw'], st['pitch'], st['distance'], st['fovDeg'], colls['cameras']))
    gc = layout['gameCamera']; pitch = math.radians(gc['pitchDeg'])
    sp, fac = layout['spawn']['position'], layout['facilities']
    cab = next(b for b in layout['buildings'] if b['id'] == 'signal')
    game_targets = (('Cam_Game_出生点', (sp[0], sp[1] + 0.9, sp[2])),
                    ('Cam_Game_司机室', (cab['center'][0], cab['baseY'] + 0.9, cab['center'][1])),
                    ('Cam_Game_西货台', (fac['platformW']['x'], 0.9, fac['platformW']['z'])),
                    ('Cam_Game_东货台', (fac['platformE']['x'], 0.9, fac['platformE']['z'])))
    for name, tg in game_targets:
        cams.append(add_camera(name, tg, 0, pitch, gc['distance'], gc['fovDeg'], colls['cameras']))
    home = add_camera('Cam_全景', (0, 2, -3), 0.64, 0.76, 230, 40, colls['cameras']); cams.insert(0, home)
    scene.camera = home
    setup_render(scene)
    (out / 'collision_boxes.json').write_text(json.dumps(boxes, ensure_ascii=False), encoding='utf-8')
    fd = out / 'fbx'; fd.mkdir(exist_ok=True)
    sources = {n: hashlib.sha256((src / n).read_bytes()).hexdigest() for n in (man['groups']['buildings']['file'], 'lastshift_layout.json')}
    building_semantics(colls['buildings'], fd / 'lastshift_roof_faces.json', sources)
    bpy.ops.file.pack_all()
    blend = out / 'LastShiftYard.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend), compress=True)
    previews = []
    helper_layers = [layer_collection(n) for n in ('LSY_collision', 'LSY_markers', 'LSY_cameras', 'LSY_entrance')]
    for lc in helper_layers:
        if lc:
            lc.exclude = True
    if render:
        pv = out / 'previews'; pv.mkdir(exist_ok=True)
        wanted = ('Cam_全景', 'Cam_岔心_悬车楼', 'Cam_西台_货棚', 'Cam_东台_装卸坪', 'Cam_中场_换轨备料')
        for cam in cams:
            if not (cam.name in wanted or cam.name.startswith('Cam_Game_')):
                continue
            scene.camera = cam; path = pv / (cam.name.replace('Cam_', 'lastshift_') + '.png')
            scene.render.filepath = str(path); bpy.ops.render.render(write_still=True); previews.append(str(path))
        scene.camera = home
        previews.append(render_minimap(scene, out, colls['cameras']))
    for lc in helper_layers:
        if lc:
            lc.exclude = False
    fbx_files = []
    if fbx:
        protos = new_collection('LSY_instance_prototypes', root)
        skip, inst_table = split_instances(colls, protos)
        STATS['singleUserCopies'] = {k: make_single_user(colls[k]) for k in FBX_GROUPS if k not in INSTANCE_GROUPS}
        STATS['lineTubes'] = sum(line_tubes(colls[k]) for k in FBX_GROUPS)
        # glTF 材质名是 three.js 的 JSON 串（带引号、花括号，常超 63 字节），FBX 往返会被截断加哈希；统一改成短名，原名进清单 source
        used = sorted({s.material.name for c in [colls[k] for k in FBX_GROUPS] + [protos] for o in c.all_objects if o.type == 'MESH'
                       for s in o.material_slots if s.material})
        for i, name in enumerate(used):
            m = bpy.data.materials[name]; m['lsy_source'] = name; m.name = 'LSY_M_%03d' % i
        export_material_manifest([colls[k] for k in FBX_GROUPS] + [protos], fd)
        write_instances(inst_table, fd / 'lastshift_instances.json')
        for k in FBX_GROUPS:
            bpy.ops.object.select_all(action='DESELECT')
            for o in colls[k].all_objects:
                o.select_set(o.type in {'MESH', 'EMPTY'} and o.name not in skip)
            path = fd / ('LastShiftYard_' + k + '.fbx')
            bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                                     bake_anim=False, add_leaf_bones=False, path_mode='COPY', embed_textures=True, use_custom_props=True)
            fbx_files.append(str(path))
        bpy.ops.object.select_all(action='DESELECT')
        for o in protos.all_objects:
            o.select_set(True)
        path = fd / 'LastShiftYard_instance_prototypes.fbx'
        bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                                 bake_anim=False, add_leaf_bones=False, path_mode='COPY', embed_textures=True)
        fbx_files.append(str(path))
        bpy.ops.object.select_all(action='DESELECT')
        for o in colls['markers'].all_objects:
            o.select_set(True)
        path = fd / 'LastShiftYard_markers.fbx'
        bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'EMPTY'}, axis_forward='-Z', axis_up='Y',
                                 bake_anim=False, add_leaf_bones=False, use_custom_props=True)
        fbx_files.append(str(path))
    STATS['seconds'] = round(time.time() - t0, 1)
    report = {'blend': str(blend), 'previews': previews, 'fbx': fbx_files, 'stats': STATS}
    (out / 'lastshift_blend_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    return report


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', required=True); ap.add_argument('--out', required=True)
    ap.add_argument('--skip-render', action='store_true'); ap.add_argument('--fbx', action='store_true')
    args = ap.parse_args(sys.argv[sys.argv.index('--') + 1:])
    report = build(args.src, args.out, args.fbx, not args.skip_render)
    print('LASTSHIFT_BLEND_OK ' + json.dumps(report, ensure_ascii=False), flush=True)
    if bpy.app.background:
        # EEVEE 后台渲染后退出时可能在 GPU 清理阶段崩溃；产物已全部写完，直接结束进程。
        sys.stdout.flush(); sys.stderr.flush()
        os._exit(0)


if __name__ == '__main__' and bpy.app.background:
    main()
