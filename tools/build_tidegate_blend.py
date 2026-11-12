"""Blender 后台组装：把潮闸镇 three.js 精修场景的导出物装成可编辑的 .blend，并出 Unity 用 FBX 与预览图。

输入是 three.js 页面 `__tg.exportForBlender()` 写出的 ArtSource/Tidegate/export/（manifest.json、各类别 GLB、
地形二进制、贴图 PNG、tidegate_layout.json）。本脚本只写 --out 目录下自己的文件，不删任何东西。

坐标：three.js (x, y, z) → Blender (x, -z, y)，与 glTF 导入器一致，也与天空岛「Blender Y = Unity z」口径一致；
FBX 按天空岛同一设置 axis_forward='-Z', axis_up='Y' 导出，进 Unity 后是 (x, y, -z)。

用法（后台，Blender 5.2）：
  D:/blender/blender.exe -b --factory-startup --python-exit-code 1 --python tools/build_tidegate_blend.py -- \
      --src ArtSource/Tidegate/export --out ArtSource/Tidegate [--skip-render] [--fbx]
成功时打印 TIDEGATE_BLEND_OK {...}。
"""
import argparse
import json
import math
import os
import sys
import time
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

GROUPS = ['buildings', 'setpieces', 'props', 'containers', 'street', 'gameplay', 'vehicles', 'scatter']
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


def set_blended(mat, alpha):
    p = principled(mat)
    if p:
        p.inputs['Alpha'].default_value = alpha
    for attr, val in (('surface_render_method', 'BLENDED'), ('blend_method', 'BLEND')):
        if hasattr(mat, attr):
            try:
                setattr(mat, attr, val)
            except (TypeError, AttributeError):
                pass


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
    fixed = 0
    done = set()
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
            vc = nt.nodes.new('ShaderNodeVertexColor')
            vc.layer_name = attr
            mix = nt.nodes.new('ShaderNodeMix')
            mix.data_type = 'RGBA'
            mix.blend_type = 'MULTIPLY'
            mix.inputs[0].default_value = 1.0
            base = p.inputs['Base Color']
            if base.is_linked:
                nt.links.new(base.links[0].from_socket, mix.inputs[6])
            else:
                mix.inputs[6].default_value = base.default_value
            nt.links.new(vc.outputs['Color'], mix.inputs[7])
            nt.links.new(mix.outputs[2], base)
            fixed += 1
    return fixed


# ---------------- 地形 ----------------
def build_terrain(src, man, coll):
    t = man['terrain']
    W, H = t['width'], t['height']
    h = np.fromfile(src / t['heightFile'], dtype='<f4').reshape(H, W)
    sp = np.fromfile(src / t['splatFile'], dtype=np.uint8).reshape(H, W, 8).astype(np.float32) / 255.0
    lag = np.fromfile(src / t['lagoonFile'], dtype=np.uint8).reshape(H, W)
    xs = t['x0'] + np.arange(W, dtype=np.float32)
    zs = t['z0'] + np.arange(H, dtype=np.float32)
    X, Zg = np.meshgrid(xs, zs)
    verts = np.stack([X, -Zg, h], axis=-1).reshape(-1, 3)
    idx = np.arange(W * H).reshape(H, W)
    a = idx[:-1, :-1].ravel(); b = idx[:-1, 1:].ravel(); c = idx[1:, 1:].ravel(); d = idx[1:, :-1].ravel()
    quads = np.stack([a, d, c, b], axis=1)  # 从上往下看逆时针，法线朝 +Z
    me = bpy.data.meshes.new('TG_Terrain')
    me.vertices.add(len(verts)); me.vertices.foreach_set('co', verts.ravel())
    me.loops.add(quads.size); me.loops.foreach_set('vertex_index', quads.ravel().astype(np.int32))
    me.polygons.add(len(quads))
    me.polygons.foreach_set('loop_start', (np.arange(len(quads)) * 4).astype(np.int32))
    if hasattr(me.polygons[0], 'loop_total'):
        try:
            me.polygons.foreach_set('loop_total', np.full(len(quads), 4, dtype=np.int32))
        except (AttributeError, TypeError):
            pass
    me.update(calc_edges=True); me.validate()
    uv = me.uv_layers.new(name='UVMap')
    jj, ii = np.divmod(quads.ravel(), W)
    uvs = np.stack([ii / (W - 1), 1.0 - jj / (H - 1)], axis=1).astype(np.float32)
    uv.data.foreach_set('uv', uvs.ravel())
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new('VIS_Terrain', me); coll.objects.link(ob)
    # 地表权重写成两张图（左下原点），Unity 侧也直接用
    out_tex = src.parent / 'textures'; out_tex.mkdir(parents=True, exist_ok=True)
    imgs = []
    for k, ch in (('A', sp[:, :, 0:4]), ('B', sp[:, :, 4:8])):
        img = bpy.data.images.new('TG_Splat' + k, W, H, alpha=True, float_buffer=False)
        img.pixels.foreach_set(np.flipud(ch).ravel())
        img.filepath_raw = str(out_tex / ('tidegate_splat_' + k + '.png')); img.file_format = 'PNG'; img.save()
        img.colorspace_settings.name = 'Non-Color'
        imgs.append(img)
    lag_img = bpy.data.images.new('TG_LagoonMask', W, H, alpha=False)
    lm = np.flipud(lag.astype(np.float32) / 255.0)
    lag_img.pixels.foreach_set(np.stack([lm, lm, lm, np.ones_like(lm)], axis=-1).ravel())
    lag_img.filepath_raw = str(out_tex / 'tidegate_lagoon_mask.png'); lag_img.file_format = 'PNG'; lag_img.save()
    ob.data.materials.append(terrain_material(src, man, imgs))
    STATS['terrain'] = {'vertices': len(me.vertices), 'faces': len(me.polygons)}
    return ob


def terrain_material(src, man, splat_imgs):
    m = bpy.data.materials.new('TG_Terrain')
    p = principled(m); nt = m.node_tree; L = nt.links
    p.inputs['Roughness'].default_value = 0.92
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ'); L.new(geo.outputs['Position'], sep.inputs[0])
    comb = nt.nodes.new('ShaderNodeCombineXYZ')
    L.new(sep.outputs['X'], comb.inputs['X']); L.new(sep.outputs['Y'], comb.inputs['Y'])
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'UVMap'
    weights = []
    for img in splat_imgs:
        tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = img; tx.interpolation = 'Linear'; tx.extension = 'EXTEND'
        L.new(uvn.outputs['UV'], tx.inputs['Vector'])
        sc = nt.nodes.new('ShaderNodeSeparateColor'); L.new(tx.outputs['Color'], sc.inputs[0])
        weights += [sc.outputs[0], sc.outputs[1], sc.outputs[2], tx.outputs['Alpha']]
    acc = None
    for li, (name, kind, rep) in enumerate(man['terrain']['layers']):
        path = src / man['textures'][kind]['albedo']
        img = bpy.data.images.load(str(path), check_existing=True)
        scale = nt.nodes.new('ShaderNodeVectorMath'); scale.operation = 'SCALE'
        L.new(comb.outputs[0], scale.inputs[0]); scale.inputs['Scale'].default_value = 1.0 / rep
        tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = img; L.new(scale.outputs[0], tx.inputs['Vector'])
        col = tx.outputs['Color']
        if name == 'seabed':
            tint = nt.nodes.new('ShaderNodeVectorMath'); tint.operation = 'MULTIPLY'
            L.new(col, tint.inputs[0]); tint.inputs[1].default_value = (0.62, 0.6, 0.55); col = tint.outputs[0]
        mul = nt.nodes.new('ShaderNodeVectorMath'); mul.operation = 'SCALE'
        L.new(col, mul.inputs[0]); L.new(weights[li], mul.inputs['Scale'])
        if acc is None:
            acc = mul.outputs[0]
        else:
            add = nt.nodes.new('ShaderNodeVectorMath'); add.operation = 'ADD'
            L.new(acc, add.inputs[0]); L.new(mul.outputs[0], add.inputs[1]); acc = add.outputs[0]
    L.new(acc, p.inputs['Base Color'])
    return m


def build_water(man, coll, level):
    t = man['terrain']
    x0, z0 = t['x0'] - 200, t['z0'] - 200
    x1, z1 = t['x0'] + t['width'] + 200, t['z0'] + t['height'] + 260
    me = bpy.data.meshes.new('TG_Water')
    me.from_pydata([T(x0, level, z0), T(x1, level, z0), T(x1, level, z1), T(x0, level, z1)], [], [(0, 3, 2, 1)])
    ob = bpy.data.objects.new('VIS_Water_Preview', me); coll.objects.link(ob)
    m = bpy.data.materials.new('TG_Water'); p = principled(m)
    p.inputs['Base Color'].default_value = (0.045, 0.16, 0.18, 1)
    p.inputs['Roughness'].default_value = 0.06
    set_blended(m, 0.93)  # 只是预览用水面；游戏里是随潮位升降的动态水体
    me.materials.append(m)
    return ob


# ---------------- 散布：按种类归集合，并为 Unity 实例化各做一份原型 ----------------
def tagged(o):
    """glTF 导入器把每个实例建成无名子对象，种类名在父节点上（「种类__分块__部件」）。"""
    while o is not None:
        if '__' in o.name:
            return o
        o = o.parent
    return None


def kind_of(o):
    t = tagged(o)
    return t.name.split('__')[0] if t else None


def part_of(o):
    t = tagged(o)
    return t.name.split('__')[-1].split('.')[0] if t else None


def split_scatter(coll):
    kinds = {}
    for o in list(coll.objects):
        k = kind_of(o)
        if not k:
            continue
        if k not in kinds:
            kinds[k] = new_collection('TG_scatter_' + k, coll)
        coll.objects.unlink(o); kinds[k].objects.link(o)
    return {k: sum(1 for o in c.objects if o.type == 'MESH') for k, c in kinds.items()}


def build_prototypes(scatter_coll, target):
    """每个种类每个部件取一个实例的网格，放到原点，挂在名为种类的空物体下。"""
    seen = {}
    for o in scatter_coll.all_objects:
        if o.type != 'MESH' or not kind_of(o):
            continue
        k = kind_of(o); part = part_of(o)
        if (k, part) in seen:
            continue
        if k not in {kk for kk, _ in seen}:
            e = bpy.data.objects.new('PROTO_' + k, None); target.objects.link(e); seen[(k, '_root')] = e
        root_e = seen[(k, '_root')]
        p = bpy.data.objects.new('PROTO_' + k + '_' + part, o.data); target.objects.link(p); p.parent = root_e
        seen[(k, part)] = p
    target.hide_render = True
    STATS['prototypes'] = sum(1 for key in seen if key[1] != '_root')
    return target


# ---------------- 碰撞（逻辑网格合并成盒子） ----------------
def rects_from_mask(mask):
    """把布尔网格贪心合并成矩形 (i0, j0, i1, j1)，含 i0/j0、不含 i1/j1。"""
    Hn, Wn = mask.shape; used = np.zeros_like(mask); out = []
    for j in range(Hn):
        i = 0
        while i < Wn:
            if not mask[j, i] or used[j, i]:
                i += 1; continue
            i1 = i
            while i1 < Wn and mask[j, i1] and not used[j, i1]:
                i1 += 1
            j1 = j + 1
            while j1 < Hn and mask[j1, i:i1].all() and not used[j1, i:i1].any():
                j1 += 1
            used[j:j1, i:i1] = True; out.append((i, j, i1, j1)); i = i1
    return out


# ---------------- 逐件碰撞（读图通则 SkyIsland/AGENTS.md §6.1，2026-11-12） ----------------
# 玩家反馈「分不清哪些有实体」「莫名其妙卡脚」：原来墙 / 实心物件全是逻辑网格 1 m 方盒（顶高 3.2 / 1.3 m），
# 0.2 m 的轮胎也是 1×1×1.3 m 的挡弹盒，旋转大件取轴对齐包围盒，墙盒比可见墙厚 0.6 m；长椅、路灯、灌木、隔离墩反而没有实体。
# 现在：建筑墙 / 家具 / 外梯 / 管道来自 tools/build_tidegate_building_colliders.mjs，散布件、大件、路灯电线杆、搜刮箱、阀门
# 按各自可见网格取有向包围盒；逻辑网格里被它们占的格子不再出方盒（导航仍按逻辑网格 + 这些盒子，见 build_tidegate_navigation.py）。
LOW_LIMIT = 0.6     # 可见高度不到它的不给实体（规则第 2 条：0.12–0.6 m 正是「看着能跨、实际绊脚」的高度）
BODY_LAYER = 2.2    # 只用离地这个高度以内的顶点量占地（吊臂、标记圈、树冠不算）
# 散布种类 → (方式, 水平收缩)。没列出的（草、花、芦苇、海带、贝壳、积水、漂流木、地笼、渔网、托盘、轮胎、系缆桩）不给实体
SCATTER_COLLIDE = {
    'crate': ('box', 1.0), 'barrel': ('box', 1.0), 'barrelRust': ('box', 1.0), 'drumBlue': ('box', 1.0),
    'barrierConcrete': ('box', 1.0), 'bench': ('box', 1.0), 'cableSpool': ('box', 1.0), 'trashBin': ('box', 1.0),
    'gasCylinder': ('box', 1.0), 'floatPile': ('box', 0.9), 'debris': ('box', 0.9),
    'bush': ('box', 0.55),  # 叶边不挡人，按冠幅五成多给一块实心灌丛
    'rock1': ('box', 0.8), 'rock2': ('box', 0.8), 'rock3': ('box', 0.8),
    'pine': ('trunk', 0.4), 'broadleaf': ('trunk', 0.45),
    'crateStack': ('box', 1.0), 'sandbags': ('box', 1.0), 'rockCluster': ('box', 0.85),
}


# 战斗区减杂件（规则第 5 条）：可玩区里这些种类按位置哈希只留一半。决定只在这里做，写进 collision_boxes.json 的
# scatterThinned，Unity 构建器（TidegateBuilder.BuildScatter）按它跳过渲染，导航也只看留下的灌木。
THIN_KEEP = {'bush': 0.5, 'tire': 0.5, 'pallet': 0.5, 'lobsterTrap': 0.5, 'netPile': 0.5, 'driftwood': 0.5}


def thin_keep(kind, x, z):
    keep = THIN_KEEP.get(kind)
    if keep is None or not (-90 < x < 90 and -70 < z < 70):
        return True
    xi, zi = int(round(x * 10)), int(round(z * 10))
    return (abs(xi) * 92821 + abs(zi) * 68917 + (7 if xi < 0 else 0) + (13 if zi < 0 else 0)) % 1000 < keep * 1000


def scatter_thinned(layout):
    return {k: [i for i, t in enumerate(rows) if not thin_keep(k, t[0], t[2])] for k, rows in layout['instances'].items() if k in THIN_KEEP}


def three_of(v):
    """Blender 世界坐标 → three.js 坐标。"""
    return (v[0], v[2], -v[1])


def world_three_verts(root):
    pts = []
    for o in [root] + list(root.children_recursive):
        if o.type == 'MESH' and len(o.data.vertices):
            mw = o.matrix_world
            pts.extend(three_of(mw @ v.co) for v in o.data.vertices)
    return np.array(pts, dtype=np.float64) if pts else np.zeros((0, 3))


def kind_local_bounds(colls):
    """每个散布 / 大件种类取一个实例的各部件网格（实例本地坐标，换成 three.js 轴向），求并集包围盒。"""
    out, seen = {}, set()
    for coll in colls:
        for o in coll.all_objects:
            if o.type != 'MESH' or not kind_of(o):
                continue
            k, part = kind_of(o), part_of(o)
            if (k, part) in seen:
                continue
            seen.add((k, part))
            vs = np.array([three_of(v.co) for v in o.data.vertices])
            lo, hi = vs.min(0), vs.max(0)
            if k in out:
                lo, hi = np.minimum(lo, out[k][0]), np.maximum(hi, out[k][1])
            out[k] = (lo, hi)
    return out


def rot_y(x, z, ry):
    c, s = math.cos(ry), math.sin(ry)
    return x * c + z * s, -x * s + z * c


def obb_entry(oid, kind, center, size, yaw, top_above_ground, source):
    return {'id': oid, 'kind': kind, 'source': source, 'center': [round(float(v), 3) for v in center],
            'size': [round(float(v), 3) for v in size], 'yaw': round(float(yaw), 4), 'visibleHeight': round(float(top_above_ground), 3)}


def obb_from_verts(oid, kind, pts, yaw, shrink=1.0, source='object'):
    """按给定朝向量一组 three.js 坐标顶点的有向包围盒；占地只看身体层以内的顶点。"""
    if len(pts) == 0:
        return None
    ground = float(pts[:, 1].min())
    body = pts[pts[:, 1] <= ground + BODY_LAYER]
    if len(body) == 0:
        return None
    lx, lz = rot_y(body[:, 0], body[:, 2], -yaw)
    x0, x1, z0, z1 = float(lx.min()), float(lx.max()), float(lz.min()), float(lz.max())
    top = float(body[:, 1].max())
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    wx, wz = rot_y(cx, cz, yaw)
    size = ((x1 - x0) * shrink, top - ground, (z1 - z0) * shrink)
    return obb_entry(oid, kind, (wx, (ground + top) / 2, wz), size, yaw, top - ground, source)


def yaw_of(obj):
    ax = obj.matrix_world.to_3x3() @ Vector((1, 0, 0))
    x3, _, z3 = three_of(ax)
    return math.atan2(-z3, x3)


def instance_obbs(layout, bounds):
    out = []
    for kind, rows in layout['instances'].items():
        rule = SCATTER_COLLIDE.get(kind)
        if not rule or kind not in bounds:
            continue
        how, shrink = rule
        lo, hi = bounds[kind]
        for i, t in enumerate(rows):
            x, y, z, ry, sx, sy, sz = t
            if not thin_keep(kind, x, z):
                continue
            if not (-90 < x < 90 and -70 < z < 70):
                continue
            top = hi[1] * sy
            if top < LOW_LIMIT:
                continue
            if how == 'trunk':
                w = shrink * sx
                h = min(top, 3.0)
                out.append(obb_entry('%s_%d' % (kind, i), kind, (x, y + h / 2, z), (w, h, w), ry, top, 'instance'))
                continue
            bottom = max(lo[1] * sy, 0.0)
            c = ((lo[0] + hi[0]) / 2 * sx, (lo[2] + hi[2]) / 2 * sz)
            wx, wz = rot_y(c[0], c[1], ry)
            size = ((hi[0] - lo[0]) * sx * shrink, top - bottom, (hi[2] - lo[2]) * sz * shrink)
            out.append(obb_entry('%s_%d' % (kind, i), kind, (x + wx, y + (top + bottom) / 2, z + wz), size, ry, top, 'instance'))
    return out


def object_obbs(colls):
    out = []
    for o in colls['props'].all_objects:  # 车、集装箱、划艇、码头吊车（PROP_<种类>_<x>_<z>）
        if o.name.startswith('PROP_') and (o.parent is None or not o.parent.name.startswith('PROP_')):
            e = obb_from_verts(o.name, o.name.split('_')[1], world_three_verts(o), yaw_of(o))
            if e:
                out.append(e)
    for o in colls['containers'].all_objects:  # 搜刮箱
        if o.name.startswith('LOOT_') and (o.parent is None or not o.parent.name.startswith('LOOT_')):
            e = obb_from_verts(o.name, 'loot', world_three_verts(o), yaw_of(o))
            if e and e['visibleHeight'] >= LOW_LIMIT:
                out.append(e)
    for o in colls['gameplay'].all_objects:  # 阀门
        if o.name.startswith('GP_valve_') and (o.parent is None or not o.parent.name.startswith('GP_valve_')):
            e = obb_from_verts(o.name, 'valve', world_three_verts(o), 0.0)
            if e and e['visibleHeight'] >= LOW_LIMIT:
                out.append(e)
    street = bpy.data.objects.get('TG_street')
    n = 0
    for ch in (street.children if street else []):  # 路灯与电线杆：只给立柱一根细柱子
        pts = world_three_verts(ch)
        if len(pts) == 0 or ch.name.startswith(('roadDash', 'zebra')):
            continue
        ground, height = float(pts[:, 1].min()), float(pts[:, 1].max() - pts[:, 1].min())
        if height < 3.0:
            continue
        post = pts[pts[:, 1] <= ground + 1.0]
        if len(post) == 0 or max(np.ptp(post[:, 0]), np.ptp(post[:, 2])) > 1.2:
            continue
        cx = float((post[:, 0].min() + post[:, 0].max()) / 2)
        cz = float((post[:, 2].min() + post[:, 2].max()) / 2)
        w = max(0.24, float(max(np.ptp(post[:, 0]), np.ptp(post[:, 2]))))
        h = min(height, 4.0)
        out.append(obb_entry('STREET_post_%d' % n, 'post', (cx, ground + h / 2, cz), (w, h, w), 0.0, height, 'street'))
        n += 1
    return out


def building_obbs(src_fbx):
    path = src_fbx / 'tidegate_building_colliders.json'
    if not path.is_file():
        raise RuntimeError('缺少 %s：先跑 node tools/build_tidegate_building_colliders.mjs' % path)
    data = json.loads(path.read_text(encoding='utf-8'))
    out = []
    for bid, b in data['buildings'].items():
        rows = [('wall', i, q) for i, q in enumerate(b['walls'])] + [('stair', i, q) for i, q in enumerate(b['stairs'])]
        rows += [('furniture:' + f['name'], i, f['box']) for i, f in enumerate(b['furniture'])]
        rows += [('pipe', i, q) for i, q in enumerate(b['pipes'])]
        for kind, i, q in rows:
            h = q[4] - q[1]
            if kind not in ('wall', 'stair') and h < LOW_LIMIT:
                continue
            out.append(obb_entry('B_%s_%s_%d' % (bid, kind.split(':')[0], i), kind,
                                 ((q[0] + q[3]) / 2, (q[1] + q[4]) / 2, (q[2] + q[5]) / 2), (q[3] - q[0], h, q[5] - q[2]), 0.0, h, 'building'))
    return out, data['sourceSha256']


def marker_points(layout):
    pts = [('spawn', (layout['spawn']['x'], 0.8, layout['spawn']['z']))]
    pts += [(c['id'], c['position']) for c in layout['containers']] + [(v['id'], v['position']) for v in layout['valves']]
    pts += [(e['id'], e['position']) for e in layout['exits']] + [(e['id'], e['position']) for e in layout['enemies']]
    pts += [('WP_%s_%d' % (g, i), p) for g, ws in layout['waypoints'].items() for i, p in enumerate(ws)]
    return pts


def obb_contains(e, p, pad):
    lx, lz = rot_y(p[0] - e['center'][0], p[2] - e['center'][2], -e['yaw'])
    return abs(lx) <= e['size'][0] / 2 + pad and abs(lz) <= e['size'][2] / 2 + pad


def box_faces(b):
    return [(b, b + 3, b + 2, b + 1), (b + 4, b + 5, b + 6, b + 7), (b, b + 1, b + 5, b + 4),
            (b + 1, b + 2, b + 6, b + 5), (b + 2, b + 3, b + 7, b + 6), (b + 3, b, b + 4, b + 7)]


def build_collision(src, layout, coll, colls):
    g = layout['grid']; Wn, Hn = g['W'], g['H']
    hg = np.fromfile(src / g['heightFile'], dtype='<f4').reshape(Hn, Wn)
    fl = np.fromfile(src / g['flagFile'], dtype=np.uint8).reshape(Hn, Wn)
    CX, CZ = np.meshgrid(g['OX'] + np.arange(Wn) + 0.5, g['OZ'] + np.arange(Hn) + 0.5)
    # ---- 逐件盒子 ----
    bounds = kind_local_bounds([colls['scatter'], colls['props']])
    obbs = instance_obbs(layout, bounds) + object_obbs(colls)
    bobbs, kit_sha = building_obbs(src.parent / 'fbx')
    obbs += bobbs
    marks = marker_points(layout)
    dropped, keep = [], []
    for e in obbs:  # 盖住玩法点的家具 / 散布件不给实体（家具恰好压着搜刮箱之类），记下来交给设计看
        # 车、集装箱等大件照旧是实体：落在里面的刷怪点运行时会吸到最近的导航面（TidegateEnemies.Spawn）
        hit = None
        if e['source'] in ('instance', 'building') and e['kind'] not in ('wall', 'stair'):
            hit = next((name for name, p in marks if obb_contains(e, p, 0.1)), None)
        if hit:
            dropped.append([e['id'], hit])
        else:
            keep.append(e)
    obbs = keep
    # ---- 逻辑网格里被逐件盒子取代的格子 ----
    covered = np.zeros((Hn, Wn), dtype=bool)
    for b in layout['buildings']:  # 建筑墙格
        x0, z0, x1, z1 = b['footprint']
        covered |= (CX >= x0) & (CX < x1) & (CZ >= z0) & (CZ < z1)
    covered |= (CX >= 30) & (CX < 42) & (CZ >= 54) & (CZ < 64)  # 挖沙船船舷：可见栏杆网格已有实体（TidegateTraversalBuilder）
    for rows in layout['instances'].values():  # 散布件所在格（包括这次不给实体的轮胎、地笼）
        for t in rows:
            i, j = int(math.floor(t[0] - g['OX'])), int(math.floor(t[2] - g['OZ']))
            if 0 <= i < Wn and 0 <= j < Hn:
                covered[j, i] = True
    for e in obbs:  # 大件、搜刮箱、阀门、立柱：旧方盒按 layout.js 的名义尺寸、旋转后的轴对齐包围盒画
        # （集装箱的名义长边还和模型横竖颠倒），离中心「半长 + 1.5 m」以内的旧格子都归这件
        if e['kind'] in ('car', 'container', 'rowboat', 'dockCrane', 'rockCluster'):
            reach = max(e['size'][0], e['size'][2]) / 2 + 1.5
            covered |= np.hypot(CX - e['center'][0], CZ - e['center'][2]) <= reach
        elif e['source'] in ('object', 'street') or e['kind'] in ('crateStack', 'sandbags'):  # 搜刮箱、阀门、立柱、沙袋只占自己那一格（别把旁边的女儿墙挖出缺口）
            lx, lz = rot_y(CX - e['center'][0], CZ - e['center'][2], -e['yaw'])
            covered |= (np.abs(lx) <= e['size'][0] / 2 + 0.5) & (np.abs(lz) <= e['size'][2] / 2 + 0.5)
    border = np.zeros((Hn, Wn), dtype=bool); border[0, :] = border[-1, :] = True; border[:, 0] = border[:, -1] = True
    boxes = {}
    for kind, bit, top in (('Wall', g['flags']['WALL'], 3.2), ('Prop', g['flags']['PROP'], 1.3)):
        mask = (fl & bit) > 0
        if kind == 'Prop':
            mask &= (fl & g['flags']['WALL']) == 0
        mask &= ~(covered & ~border)
        verts, faces, info = [], [], []
        for (i0, j0, i1, j1) in rects_from_mask(mask):
            x0, x1 = g['OX'] + i0, g['OX'] + i1; z0, z1 = g['OZ'] + j0, g['OZ'] + j1
            sub = hg[j0:j1, i0:i1]; y0 = float(sub.min()) - 0.5; y1 = float(sub.max()) + top
            b = len(verts)
            for yy in (y0, y1):
                for (xx, zz) in ((x0, z0), (x1, z0), (x1, z1), (x0, z1)):
                    verts.append(T(xx, yy, zz))
            faces += box_faces(b)
            info.append({'min': [x0, round(y0, 3), z0], 'max': [x1, round(y1, 3), z1]})
        me = bpy.data.meshes.new('COL_' + kind); me.from_pydata(verts, [], faces)
        ob = bpy.data.objects.new('COL_' + kind, me); coll.objects.link(ob)
        ob.display_type = 'WIRE'; ob.hide_render = True
        boxes[kind] = info
    # 逐件盒子也画进 .blend（线框，不渲染），方便目视核对
    verts, faces = [], []
    for e in obbs:
        b = len(verts); hx, hy, hz = (v / 2 for v in e['size'])
        for dy in (-hy, hy):
            for (dx, dz) in ((-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz)):
                wx, wz = rot_y(dx, dz, e['yaw'])
                verts.append(T(e['center'][0] + wx, e['center'][1] + dy, e['center'][2] + wz))
        faces += box_faces(b)
    me = bpy.data.meshes.new('COL_Obb'); me.from_pydata(verts, [], faces)
    ob = bpy.data.objects.new('COL_Obb', me); coll.objects.link(ob); ob.display_type = 'WIRE'; ob.hide_render = True
    boxes['Obb'] = obbs
    boxes['obbDropped'] = dropped
    boxes['scatterThinned'] = scatter_thinned(layout)
    # 读图审计（tools/map_readability_audit.py）用：每个散布种类的单位高度与碰撞方式
    boxes['kindPolicy'] = {k: {'unitHeight': round(float(hi[1]), 3), 'policy': SCATTER_COLLIDE.get(k, ('none', 1.0))[0],
                               'maxScale': round(max((t[5] for t in layout['instances'].get(k, [])), default=1.0), 3)}
                           for k, (lo, hi) in bounds.items()}
    boxes['lowLimit'] = LOW_LIMIT
    boxes['buildingKitSha256'] = kit_sha
    coll.hide_render = True
    counts = {}
    for e in obbs:
        k = e['kind'].split(':')[0]
        counts[k] = counts.get(k, 0) + 1
    STATS['collisionBoxes'] = {'Wall': len(boxes['Wall']), 'Prop': len(boxes['Prop']), 'Obb': len(obbs), 'obbDropped': len(dropped), 'obbKinds': counts,
                               'scatterThinned': {k: len(v) for k, v in boxes['scatterThinned'].items()}}
    return boxes


# ---------------- 标记点（官方命名） ----------------
def empty(name, pos, coll, shape='PLAIN_AXES', size=0.6, **props):
    o = bpy.data.objects.new(name, None); o.empty_display_type = shape; o.empty_display_size = size
    o.location = T(*pos); coll.objects.link(o)
    for k, v in props.items():
        o[k] = v if isinstance(v, (int, float, str)) else json.dumps(v, ensure_ascii=False)
    return o


def build_markers(layout, coll):
    n = 0
    sp = layout['spawn']; empty('StartPoints/PlayerSpawn', (sp['x'], 0.8, sp['z']), coll, 'ARROWS', 1.2); n += 1
    for e in layout['exits']:
        empty('Exits/' + e['id'], e['position'], coll, 'CIRCLE', e['radius'], name_cn=e['name'], seconds=e['seconds']); n += 1
    for c in layout['containers']:
        o = empty('Loot/' + c['id'], c['position'], coll, 'CUBE', 0.5, type=c['type'], fixed=c['fixed'], name_cn=c['name'] or '')
        o.rotation_euler[2] = c['yaw']; n += 1
    for v in layout['valves']:
        empty('Interact/' + v['id'], v['position'], coll, 'SPHERE', 0.5, name_cn=v['name'], needWheel=int(v['needWheel'])); n += 1
    lh = layout['lighthouse']; empty('Interact/Lighthouse', (lh['x'], 1.6, lh['z']), coll, 'SPHERE', 0.8); n += 1
    for e in layout['enemies']:
        empty('Spawn/' + e['group'] + '/' + e['id'], e['position'], coll, 'SINGLE_ARROW', 1.0, type=e['type']); n += 1
    for g, pts in layout['waypoints'].items():
        for i, p in enumerate(pts):
            empty('WP/' + g + '/' + str(i), p, coll, 'PLAIN_AXES', 0.4); n += 1
    for i, p in enumerate(layout['nightLamps']):
        empty('NightLamp/' + str(i), p, coll, 'SPHERE', 0.2); n += 1
    for p in layout['pois']:
        empty('POI/' + p['name'], p['position'], coll, 'PLAIN_AXES', 2.0); n += 1
    STATS['markers'] = n


# ---------------- 光照、相机、预览 ----------------
def three_dir(az_deg, el_deg):
    az, el = math.radians(az_deg), math.radians(el_deg)
    return Vector((-math.sin(az) * math.cos(el), math.sin(el), math.cos(az) * math.cos(el)))


def setup_lighting():
    s = three_dir(52, 13); S = Vector((s.x, -s.z, s.y)).normalized()
    sun_data = bpy.data.lights.new('TG_Sun', 'SUN'); sun_data.energy = 4.0; sun_data.color = (1.0, 0.78, 0.55); sun_data.angle = math.radians(1.5)
    sun = bpy.data.objects.new('TG_Sun', sun_data); bpy.context.scene.collection.objects.link(sun)
    sun.rotation_euler = S.to_track_quat('Z', 'Y').to_euler()
    w = bpy.data.worlds.new('TG_Dusk'); bpy.context.scene.world = w
    if hasattr(w, 'use_nodes') and not w.use_nodes:
        w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (0.62, 0.66, 0.72, 1); bg.inputs['Strength'].default_value = 0.9


def add_camera(name, target, az_deg, el_deg, dist, fov_deg, coll):
    tg = Vector(target); d = three_dir(az_deg, el_deg)
    pos3 = tg + Vector((d.x, d.y, d.z)) * dist
    cd = bpy.data.cameras.new(name); cd.sensor_fit = 'VERTICAL'; cd.angle = math.radians(fov_deg); cd.clip_end = 3000
    cam = bpy.data.objects.new(name, cd); coll.objects.link(cam)
    cam.location = T(pos3.x, pos3.y, pos3.z)
    look = T(tg.x, tg.y, tg.z) - cam.location
    cam.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
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
    """从某个节点往上游找指定类型的节点（经过混合 / 乘色等中间节点）。"""
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
                        if not bc.is_linked:
                            entry['baseColor'] = [round(v, 4) for v in bc.default_value]
                        imgs = [n for n in upstream(p, {'TEX_IMAGE'}) if n.image] if bc.is_linked else []
                        base_imgs = [n for n in imgs if any(l.to_socket == bc or True for l in n.outputs['Color'].links)]
                        if base_imgs:
                            img = base_imgs[0].image
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


def render_minimap(scene, out, layout_cams_coll):
    """正北正交原图。可玩区以外留出羽化余地，sync 时按相同米数烘透明岸线。"""
    cd = bpy.data.cameras.new('Cam_Minimap'); cd.type = 'ORTHO'; cd.ortho_scale = 208; cd.clip_end = 500
    cam = bpy.data.objects.new('Cam_Minimap', cd); layout_cams_coll.objects.link(cam)
    cam.location = (0, 0, 200); cam.rotation_euler = (0, 0, 0)
    old = (scene.camera, scene.render.resolution_x, scene.render.resolution_y)
    scene.camera = cam; scene.render.resolution_x = scene.render.resolution_y = 1440
    md = out / 'minimap'; md.mkdir(exist_ok=True)
    scene.render.filepath = str(md / 'tidegate_ortho.png'); bpy.ops.render.render(write_still=True)
    scene.camera, scene.render.resolution_x, scene.render.resolution_y = old
    (md / 'tidegate_minimap.json').write_text(json.dumps({'sceneId': 'BossRush_Tidegate', 'imageWorldSize': cd.ortho_scale, 'mapWorldCenter': [0, 0, 0], 'note': 'Unity 坐标；图片上方是 three −z（Unity +z，北）'}, ensure_ascii=False), encoding='utf-8')
    return str(md / 'tidegate_ortho.png')


def layer_collection(name, root=None):
    root = root or bpy.context.view_layer.layer_collection
    if root.name == name:
        return root
    for c in root.children:
        r = layer_collection(name, c)
        if r:
            return r
    return None


# ---------------- 主流程 ----------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', required=True); ap.add_argument('--out', required=True)
    ap.add_argument('--skip-render', action='store_true'); ap.add_argument('--fbx', action='store_true')
    args = ap.parse_args(sys.argv[sys.argv.index('--') + 1:])
    src, out = Path(args.src).resolve(), Path(args.out).resolve(); out.mkdir(parents=True, exist_ok=True)
    man = json.loads((src / 'manifest.json').read_text(encoding='utf-8'))
    layout = json.loads((src / 'tidegate_layout.json').read_text(encoding='utf-8'))
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0  # 不留 .blend1 备份
    scene = bpy.context.scene; scene.name = 'Tidegate'
    scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 1
    root = new_collection('Tidegate')
    colls = {k: new_collection('TG_' + k, root) for k in ['terrain', 'water'] + GROUPS + ['collision', 'markers', 'cameras']}
    build_terrain(src, man, colls['terrain'])
    build_water(man, colls['water'], layout['tide']['high'])
    vc_fixed = 0
    for k in GROUPS:
        f = src / man['groups'][k]['file']
        objs = import_glb(f, colls[k])
        vc_fixed += ensure_vertex_color(objs)
        STATS['group_' + k] = {'objects': len(objs), 'meshes': sum(1 for o in objs if o.type == 'MESH'),
                               'triangles': sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if o.type == 'MESH')}
    STATS['vertexColorMaterialsPatched'] = vc_fixed
    # 导入的半透明材质（积水坑、玻璃等）改成抖动透明：写深度，才不会排到预览水面上面去
    dithered = 0
    for m in bpy.data.materials:
        if m.name != 'TG_Water' and getattr(m, 'surface_render_method', None) == 'BLENDED':
            m.surface_render_method = 'DITHERED'; dithered += 1
    STATS['blendedToDithered'] = dithered
    STATS['scatterKinds'] = split_scatter(colls['scatter'])
    protos = build_prototypes(colls['scatter'], new_collection('TG_scatter_prototypes', root))
    boxes = build_collision(src, layout, colls['collision'], colls)
    build_markers(layout, colls['markers'])
    colls['markers'].hide_render = True
    setup_lighting()
    cams = []
    for t in layout['tourCameras']:
        cams.append(add_camera('Cam_' + t['name'], t['target'], t['azimuthDeg'], t['pitchDeg'], t['distance'], 35, colls['cameras']))
    gc = layout['gameCamera']
    game_cams = [('Cam_Game_老镇街口', (-60, 0.8, -12)), ('Cam_Game_码头', (-55, 0.75, 32)), ('Cam_Game_水厂', (-4, 2.0, -52))]
    for name, tg in game_cams:
        cams.append(add_camera(name, tg, 0, gc['pitchDeg'], gc['distance'], gc['fovDeg'], colls['cameras']))
    scene.camera = cams[0]
    setup_render(scene)
    (out / 'collision_boxes.json').write_text(json.dumps(boxes, ensure_ascii=False), encoding='utf-8')
    bpy.ops.file.pack_all()
    blend = out / 'Tidegate.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend), compress=True)
    previews = []
    # EEVEE 在 5.2 后台渲染时不认对象级 hide_render（碰撞线框会被画出来），渲染期间把辅助集合整组排除，渲完恢复
    helper_layers = [layer_collection(n) for n in ('TG_collision', 'TG_markers', 'TG_scatter_prototypes', 'TG_cameras')]
    for lc in helper_layers:
        if lc:
            lc.exclude = True
    if not args.skip_render:
        pv = out / 'previews'; pv.mkdir(exist_ok=True)
        for cam in cams:
            if not (cam.name in ('Cam_全景', 'Cam_老镇街口', 'Cam_码头与栈桥', 'Cam_灯塔岬', 'Cam_第三水厂') or cam.name.startswith('Cam_Game_')):
                continue
            scene.camera = cam; path = pv / (cam.name.replace('Cam_', 'tidegate_') + '.png')
            scene.render.filepath = str(path); bpy.ops.render.render(write_still=True); previews.append(str(path))
        scene.camera = cams[0]
        previews.append(render_minimap(scene, out, colls['cameras']))
    for lc in helper_layers:
        if lc:
            lc.exclude = False
    fbx = []
    if args.fbx:
        fd = out / 'fbx'; fd.mkdir(exist_ok=True)
        # 标签只写顶点第二套 UV，不改墙体/家具的三角形、材质、阴影或碰撞。
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from tidegate_wall_uv import stamp_wall_uv
        stamp_wall_uv(colls['buildings'], layout, fd / 'tidegate_wall_faces.json')
        export_material_manifest([colls[k] for k in ['terrain', 'buildings', 'setpieces', 'props', 'containers', 'street', 'vehicles', 'gameplay']] + [bpy.data.collections['TG_scatter_prototypes']], fd)
        for k in ['terrain', 'buildings', 'setpieces', 'props', 'containers', 'street', 'vehicles', 'gameplay']:
            bpy.ops.object.select_all(action='DESELECT')
            for o in colls[k].all_objects:
                o.select_set(o.type in {'MESH', 'EMPTY'})
            path = fd / ('Tidegate_' + k + '.fbx')
            bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                                     bake_anim=False, add_leaf_bones=False, path_mode='COPY', embed_textures=True)
            fbx.append(str(path))
        bpy.ops.object.select_all(action='DESELECT')
        for o in protos.all_objects:
            o.select_set(True)
        path = fd / 'Tidegate_scatter_prototypes.fbx'
        bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                                 bake_anim=False, add_leaf_bones=False, path_mode='COPY', embed_textures=True)
        fbx.append(str(path))
    STATS['seconds'] = round(time.time() - t0, 1)
    report = {'blend': str(blend), 'previews': previews, 'fbx': fbx, 'stats': STATS}
    (out / 'tidegate_blend_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('TIDEGATE_BLEND_OK ' + json.dumps(report, ensure_ascii=False))
    # EEVEE 后台渲染后 Blender 退出时会在 GPU 清理阶段崩（EXCEPTION_ACCESS_VIOLATION），产物已全部写完，直接结束进程
    sys.stdout.flush(); sys.stderr.flush()
    os._exit(0)


if __name__ == '__main__':
    main()
