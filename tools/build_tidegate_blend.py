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


def build_collision(src, layout, coll):
    g = layout['grid']; Wn, Hn = g['W'], g['H']
    hg = np.fromfile(src / g['heightFile'], dtype='<f4').reshape(Hn, Wn)
    fl = np.fromfile(src / g['flagFile'], dtype=np.uint8).reshape(Hn, Wn)
    boxes = {}
    for kind, bit, top in (('Wall', g['flags']['WALL'], 3.2), ('Prop', g['flags']['PROP'], 1.3)):
        mask = (fl & bit) > 0
        if kind == 'Prop':
            mask &= (fl & g['flags']['WALL']) == 0
        rects = rects_from_mask(mask)
        verts, faces, info = [], [], []
        for (i0, j0, i1, j1) in rects:
            x0, x1 = g['OX'] + i0, g['OX'] + i1; z0, z1 = g['OZ'] + j0, g['OZ'] + j1
            sub = hg[j0:j1, i0:i1]; y0 = float(sub.min()) - 0.5; y1 = float(sub.max()) + top
            b = len(verts)
            for (xx, zz) in ((x0, z0), (x1, z0), (x1, z1), (x0, z1)):
                verts.append(T(xx, y0, zz))
            for (xx, zz) in ((x0, z0), (x1, z0), (x1, z1), (x0, z1)):
                verts.append(T(xx, y1, zz))
            faces += [(b, b + 3, b + 2, b + 1), (b + 4, b + 5, b + 6, b + 7), (b, b + 1, b + 5, b + 4), (b + 1, b + 2, b + 6, b + 5), (b + 2, b + 3, b + 7, b + 6), (b + 3, b, b + 4, b + 7)]
            info.append({'min': [x0, round(y0, 3), z0], 'max': [x1, round(y1, 3), z1]})
        me = bpy.data.meshes.new('COL_' + kind); me.from_pydata(verts, [], faces)
        ob = bpy.data.objects.new('COL_' + kind, me); coll.objects.link(ob)
        ob.display_type = 'WIRE'; ob.hide_render = True
        boxes[kind] = info
    coll.hide_render = True
    STATS['collisionBoxes'] = {k: len(v) for k, v in boxes.items()}
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
    """俯视正交图给官方小地图：以原点为中心 180 m 见方（three x −90..90、z −90..90）。"""
    cd = bpy.data.cameras.new('Cam_Minimap'); cd.type = 'ORTHO'; cd.ortho_scale = 180; cd.clip_end = 500
    cam = bpy.data.objects.new('Cam_Minimap', cd); layout_cams_coll.objects.link(cam)
    cam.location = (0, 0, 200); cam.rotation_euler = (0, 0, 0)
    old = (scene.camera, scene.render.resolution_x, scene.render.resolution_y)
    scene.camera = cam; scene.render.resolution_x = scene.render.resolution_y = 1440
    md = out / 'minimap'; md.mkdir(exist_ok=True)
    scene.render.filepath = str(md / 'tidegate_minimap.png'); bpy.ops.render.render(write_still=True)
    scene.camera, scene.render.resolution_x, scene.render.resolution_y = old
    (md / 'tidegate_minimap.json').write_text(json.dumps({'sceneId': 'BossRush_Tidegate', 'imageWorldSize': 180, 'mapWorldCenter': [0, 0, 0], 'note': 'Unity 坐标；图片上方是 three −z（Unity +z，北）'}, ensure_ascii=False), encoding='utf-8')
    return str(md / 'tidegate_minimap.png')


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
    boxes = build_collision(src, layout, colls['collision'])
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
