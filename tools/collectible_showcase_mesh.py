"""收集品陈列模型 · 第 1 步（Blender）：Tripo GLB → 规范化网格（Unity 坐标）+ 预览图。

为什么不走 Unity 作者工程打包：陈列柜、地上掉落都经官方 ItemGraphicInfo.CreateAGraphic 用物品的 ItemGraphic，
运行时用 Mesh + 官方奖杯（TypeID 111）的 SodaLit 材质拼一个模板就够了。离线把网格和 DXT1 贴图写成 .brcm，
运行时只做内存拷贝（Mesh.SetVertices / Texture2D.LoadRawTextureData），不经 Unity 批处理、不碰 URP 变体剥离，
也不在运行时解码图片。见 Integration/Collectibles/CollectibleShowcaseModels.cs。

每件的处理：
  合并 → 底面中心对齐原点 → 最长边缩放到目标尺寸（官方展示品 0.2–1.0 m，陈列柜位距 1 m）
  → 减面到预算 → 按角度平滑 → 换算成 Unity 坐标（x_u = -x_b, y_u = z_b, z_u = -y_b：Blender 正面 −Y 落到 Unity +Z，
  陈列柜展示位的本地 +Z 正对镜头）→ 按 Unity 正面规则（cross(b-a, c-a) 与顶点法线同向）定三角形绕序 → 写 .mesh.bin。

用法（后台 Blender，必须带 --python-exit-code 1 并看 PASS 标记）：
    D:/blender/blender.exe -b --python-exit-code 1 --python tools/collectible_showcase_mesh.py -- \
        --plan <plan.json> --glb-dir <dir> --out-dir <dir> [--only key]
"""

import argparse
import json
import math
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Vector

# 品质 → (最长边米数, 三角面预算)。7、8 档稍大一圈，面数给得多一些（陈列柜镜头会推近）。
SIZE_BY_QUALITY = {8: (0.65, 16000), 7: (0.65, 16000), 6: (0.55, 12000), 5: (0.55, 12000)}
DEFAULT_SIZE = (0.55, 8000)
SMOOTH_ANGLE_DEG = 40.0
MESH_MAGIC = b'BRMS'


def parse_args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--plan', required=True)
    ap.add_argument('--glb-dir', required=True)
    ap.add_argument('--out-dir', required=True)
    ap.add_argument('--only', default=None)
    ap.add_argument('--skip-existing', action='store_true', help='网格比 GLB 新就跳过（批量分段跑时省时间）')
    return ap.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    bpy.ops.import_scene.gltf(filepath=str(path))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        raise RuntimeError('GLB 里没有网格')
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    # 去掉父级（GLB 常带一个空物体根），把世界变换烘进网格
    if obj.parent is not None:
        mw = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = mw
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def normalize(obj, target_size):
    co = [v.co for v in obj.data.vertices]
    mn = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    mx = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    dims = mx - mn
    scale = target_size / max(dims.x, dims.y, dims.z, 1e-6)
    offset = Vector(((mn.x + mx.x) * 0.5, (mn.y + mx.y) * 0.5, mn.z))
    for v in obj.data.vertices:
        v.co = (v.co - offset) * scale
    obj.data.update()
    return dims * scale


def decimate(obj, budget):
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris <= budget:
        return tris
    mod = obj.modifiers.new('decimate', 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = budget / tris
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def reanchor(obj):
    """减面会挪动顶点（坍缩到新位置），底面可能低出 1 mm 左右：减面后再把最低点抬回 z=0。"""
    min_z = min(v.co.z for v in obj.data.vertices)
    if abs(min_z) > 1e-6:
        for v in obj.data.vertices:
            v.co.z -= min_z
        obj.data.update()


def smooth(obj):
    bpy.ops.object.shade_smooth()
    try:
        bpy.ops.object.shade_auto_smooth(angle=math.radians(SMOOTH_ANGLE_DEG))
    except Exception:
        # 旧版本没有 shade_auto_smooth：用网格自带的自动平滑
        if hasattr(obj.data, 'use_auto_smooth'):
            obj.data.use_auto_smooth = True
            obj.data.auto_smooth_angle = math.radians(SMOOTH_ANGLE_DEG)


def to_unity(x, y, z):
    return (-x, z, -y)


def build_arrays(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(depsgraph)
    mesh = ev.to_mesh()
    mesh.calc_loop_triangles()
    uv_layer = mesh.uv_layers.active
    if uv_layer is None:
        raise RuntimeError('没有 UV')
    corner_normals = mesh.corner_normals if hasattr(mesh, 'corner_normals') else None
    if corner_normals is None:
        mesh.calc_normals_split()
    verts, normals, uvs, index_of = [], [], [], {}
    tris = []
    for lt in mesh.loop_triangles:
        tri = []
        for li in lt.loops:
            vi = mesh.loops[li].vertex_index
            p = mesh.vertices[vi].co
            n = corner_normals[li].vector if corner_normals is not None else mesh.loops[li].normal
            uv = uv_layer.data[li].uv
            pu = to_unity(p.x, p.y, p.z)
            nu = to_unity(n.x, n.y, n.z)
            key = (vi, round(nu[0], 3), round(nu[1], 3), round(nu[2], 3), round(uv.x, 5), round(uv.y, 5))
            idx = index_of.get(key)
            if idx is None:
                idx = len(verts)
                index_of[key] = idx
                verts.append(pu)
                normals.append(nu)
                uvs.append((uv.x, uv.y))
            tri.append(idx)
        tris.append(tri)
    ev.to_mesh_clear()
    # Unity 正面：cross(b - a, c - a) 与顶点法线同向。按多数票统一绕序，不靠推导。
    agree = 0
    for a, b, c in tris:
        pa, pb, pc = verts[a], verts[b], verts[c]
        e1 = (pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2])
        e2 = (pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2])
        cr = (e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0])
        na = normals[a]
        agree += 1 if (cr[0] * na[0] + cr[1] * na[1] + cr[2] * na[2]) > 0 else -1
    flipped = agree < 0
    if flipped:
        tris = [(a, c, b) for a, b, c in tris]
    return verts, normals, uvs, tris, flipped, agree


def write_mesh(path, verts, normals, uvs, tris):
    mn = [min(v[i] for v in verts) for i in range(3)]
    mx = [max(v[i] for v in verts) for i in range(3)]
    idx32 = len(verts) > 65535
    with open(path, 'wb') as f:
        f.write(MESH_MAGIC)
        f.write(struct.pack('<iiii', 1, len(verts), len(tris) * 3, 32 if idx32 else 16))
        f.write(struct.pack('<6f', *mn, *mx))
        for v in verts:
            f.write(struct.pack('<3f', *v))
        for n in normals:
            f.write(struct.pack('<3f', *n))
        for uv in uvs:
            f.write(struct.pack('<2f', *uv))
        fmt = '<3I' if idx32 else '<3H'
        for t in tris:
            f.write(struct.pack(fmt, *t))
        if not idx32 and (len(tris) * 3) % 2:
            f.write(b'\0\0')
    return mn, mx


def render_preview(obj, path):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 256
    sc.render.resolution_y = 256
    if sc.world is None:
        sc.world = bpy.data.worlds.new('w')
    cam = sc.camera
    if cam is None:
        cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
        sc.collection.objects.link(cam)
        sc.camera = cam
    cam.data.type = 'ORTHO'
    co = [obj.matrix_world @ v.co for v in obj.data.vertices]
    mn = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    mx = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    ctr = (mn + mx) * 0.5
    size = max(mx - mn)
    cam.data.ortho_scale = size * 1.35
    d = Vector((0.7, -1.0, 0.55)).normalized()  # 正面（−Y）偏右上方
    cam.location = ctr + d * size * 4
    cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    plan = json.loads(Path(args.plan).read_text(encoding='utf-8'))
    glb_dir, out_dir = Path(args.glb_dir), Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / 'preview').mkdir(exist_ok=True)
    report = []
    for entry in plan:
        key = entry['key']
        if args.only and key != args.only:
            continue
        glb = glb_dir / (Path(entry['file']).stem + '.glb')
        if not glb.exists():
            report.append({'key': key, 'status': 'missing'})
            continue
        done = out_dir / (key + '.mesh.bin')
        if args.skip_existing and done.exists() and done.stat().st_mtime >= glb.stat().st_mtime:
            report.append({'key': key, 'status': 'skipped'})
            continue
        try:
            reset_scene()
            obj = import_glb(glb)
            size, budget = SIZE_BY_QUALITY.get(entry['quality'], DEFAULT_SIZE)
            dims = normalize(obj, size)
            tris = decimate(obj, budget)
            reanchor(obj)
            smooth(obj)
            verts, normals, uvs, tri_list, flipped, agree = build_arrays(obj)
            mn, mx = write_mesh(out_dir / (key + '.mesh.bin'), verts, normals, uvs, tri_list)
            render_preview(obj, out_dir / 'preview' / (key + '.png'))
            report.append({'key': key, 'status': 'ok', 'tris': len(tri_list), 'verts': len(verts),
                           'dims': [round(d, 3) for d in dims], 'flipped': flipped, 'agree': agree,
                           'min': [round(v, 3) for v in mn], 'max': [round(v, 3) for v in mx]})
            print('MESH_OK', key, len(tri_list), len(verts), 'flipped' if flipped else '')
        except Exception as e:
            report.append({'key': key, 'status': 'error', 'error': str(e)})
            print('MESH_ERR', key, e)
    (out_dir / 'mesh_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding='utf-8')
    errors = [r for r in report if r['status'] == 'error']
    print('MESH_PASS' if not errors else 'MESH_FAIL %d' % len(errors))


main()
