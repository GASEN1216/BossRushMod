"""天空岛搜刮箱的三种外观网格（owner 2026-09-30 选方案 A：复用岛上已有的 Tripo 木箱，打进特效小包 skyisland_fx）。

三档外观全部拼自场景里已经在用的 Tripo 网格（作者工程 ArtSource/SkyIsland/tripo/*.json，与世界 FBX 同一份 UV）：
- Supply（生活物资）：桶箱堆 crate_barrel，一人高不到的一堆木箱加木桶。
- Voyage（航务补给）：货箱墙 cover_crates 缩成一排长货箱，长轴沿 X。
- Starworks（星工遗存）：小一号的货箱上长出一簇风晶 crystal_cluster，第二个子网格给晶体材质。
材质不打进包：运行时直接拿场景里已加载的 Sky_TripoCrateBarrel / Sky_TripoCoverCrates / Sky_TripoCrystalCluster，
这样包里没有着色器，不会碰 URP 剥变体的坑（ArtSource/SkyIsland/VANILLA_GRADE.md 同一套贴图与色调）。

网格按 generate_sky_island.create_object 的同一写法进 Blender（顶点 (x, z, y)、面反序、重算外向法线、按面平滑），
再按世界 FBX 的换轴结果写成 Unity 坐标：Unity = (-bx, bz, -by)。换轴后三角形的顶点顺序要反过来：
Unity 的正面是 cross(b - a, c - a) 与法线同向（对照内置 Quad：三角形 0,3,1 的叉积即 (0,0,-1) 法线），
不反的话 1400 个三角形全部背面朝外（2026-09-30 离线核对：叉积与法线一致 0/1400）。

输出：<作者工程>/Assets/SkyIsland/Fx/Crates/sky_island_loot_crates.json，作者工程 SkyIslandFxBundleBuilder 读它建 Mesh 资产。

用法（Blender 后台抛异常默认仍退出 0：必须同时查退出码与最后一行的 PASS 标记）：
    blender -b --factory-startup --python-exit-code 1 --python tools/sky_island_loot_crates.py -- [--project <作者工程>]
成功时最后一行打印 SKY_ISLAND_LOOT_CRATES_OK。
"""

import argparse
import json
import math
from pathlib import Path
import sys

import bmesh
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
from sky_island_mesh_hygiene import clean_faces  # noqa: E402
from unity_project_path import find_unity_project  # noqa: E402

# 外观名 -> 零件列表：(Tripo 件名, 子网格材质名, 等比缩放, 绕竖直轴转角度, 零件底面抬高米)
# 尺寸按官方尸体箱与搜刮点落点净空（胶囊半径 0.45 m）挑：最长边不超过 1.3 m，最高不超过 1.2 m。
CRATES = {
    'LootCrate_Supply': [('crate_barrel', 'Sky_TripoCrateBarrel', 0.55, 0.0, 0.0)],
    'LootCrate_Voyage': [('cover_crates', 'Sky_TripoCoverCrates', 0.36, 90.0, 0.0)],
    'LootCrate_Starworks': [('cover_crates', 'Sky_TripoCoverCrates', 0.30, 90.0, 0.0),
                            ('crystal_cluster', 'Sky_TripoCrystalCluster', 0.17, 25.0, 0.36)],
}
MAX_EXTENT = 1.3
MAX_HEIGHT = 1.2


def load_tripo(project, name):
    data = json.loads((project / 'ArtSource' / 'SkyIsland' / 'tripo' / (name + '.json')).read_text(encoding='utf-8'))
    return data['mesh']


def place_part(mesh, scale, yaw, lift):
    """Tripo 件：水平居中、底面落在 y=0，再缩放、转向、抬高。坐标仍是 Tripo JSON 的 Y-up 口径。"""
    xs = [v[0] for v in mesh['v']]
    zs = [v[2] for v in mesh['v']]
    ys = [v[1] for v in mesh['v']]
    cx, cz, y0 = (min(xs) + max(xs)) / 2.0, (min(zs) + max(zs)) / 2.0, min(ys)
    s, c = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
    out = []
    for x, y, z in mesh['v']:
        x, y, z = (x - cx) * scale, (y - y0) * scale + lift, (z - cz) * scale
        out.append((x * c + z * s, y, -x * s + z * c))
    return out


def build_crate(project, name, parts):
    verts, faces, uvs, smooth, material_of_face, materials = [], [], [], [], [], []
    for tripo_name, material, scale, yaw, lift in parts:
        mesh = load_tripo(project, tripo_name)
        placed = place_part(mesh, scale, yaw, lift)
        part_faces, part_smooth, _, _ = clean_faces(placed, mesh['f'], mesh.get('smooth', True), uv=mesh['uv'])
        if material not in materials:
            materials.append(material)
        slot = materials.index(material)
        offset = len(verts)
        verts.extend(placed)
        uvs.extend(mesh['uv'])
        faces.extend(tuple(offset + i for i in f) for f in part_faces)
        smooth.extend(part_smooth if isinstance(part_smooth, list) else [part_smooth] * len(part_faces))
        material_of_face.extend([slot] * len(part_faces))

    # 与 generate_sky_island.create_object 同一写法：Blender 顶点 (x, z, y)、面反序，再重算外向法线。
    data = bpy.data.meshes.new(name)
    data.from_pydata([(p[0], p[2], p[1]) for p in verts], [], [tuple(reversed(f)) for f in faces])
    data.update()
    for material in materials:
        data.materials.append(bpy.data.materials.get(material) or bpy.data.materials.new(material))
    for poly, slot in zip(data.polygons, material_of_face):
        poly.material_index = slot
    layer = data.uv_layers.new(name='UVMap')
    for poly in data.polygons:
        for li in poly.loop_indices:
            layer.data[li].uv = uvs[data.loops[li].vertex_index]
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    for poly, flag in zip(data.polygons, smooth):
        poly.use_smooth = bool(flag)
    data.update()
    data.calc_loop_triangles()
    if hasattr(data, 'calc_normals_split'):
        data.calc_normals_split()

    # 按三角形角点展开（UV 与分裂法线都是按角点的），相同的 (位置, 法线, UV) 合并成一个顶点。
    out_v, out_n, out_uv, index_of = [], [], [], {}
    submeshes = [[] for _ in materials]
    for tri in data.loop_triangles:
        corner = []
        for li in tri.loops:
            loop = data.loops[li]
            b = data.vertices[loop.vertex_index].co
            n = loop.normal
            uv = layer.data[li].uv
            unity_p = (-b.x, b.z, -b.y)
            unity_n = (-n.x, n.z, -n.y)
            key = tuple(round(v, 5) for v in unity_p + unity_n + (uv.x, uv.y))
            if key not in index_of:
                index_of[key] = len(out_v)
                out_v.append(unity_p)
                out_n.append(unity_n)
                out_uv.append((uv.x, uv.y))
            corner.append(index_of[key])
        submeshes[data.polygons[tri.polygon_index].material_index].extend(reversed(corner))
    bpy.data.meshes.remove(data)

    # 绕序自检：每个三角形的叉积要与三个角点法线之和同向（朝外）。
    agree = total = 0
    for triangles in submeshes:
        for i in range(0, len(triangles), 3):
            a, b, c = (out_v[j] for j in triangles[i:i + 3])
            u = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
            w = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
            cross = (u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0])
            normal = [sum(out_n[j][k] for j in triangles[i:i + 3]) for k in range(3)]
            total += 1
            agree += sum(cross[k] * normal[k] for k in range(3)) > 0
    if agree < total * 0.95:
        raise ValueError('%s 绕序与法线不一致 %d/%d' % (name, agree, total))
    xs, ys, zs = zip(*out_v)
    size = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    if max(size[0], size[2]) > MAX_EXTENT or size[1] > MAX_HEIGHT or min(ys) < -0.01:
        raise ValueError('%s 尺寸越界 %s（最长边 ≤ %.1f、高 ≤ %.1f、底面在 0）' % (name, size, MAX_EXTENT, MAX_HEIGHT))
    return {
        'name': name,
        'materials': materials,
        'vertices': [round(c, 5) for p in out_v for c in p],
        'normals': [round(c, 5) for p in out_n for c in p],
        'uv': [round(c, 5) for p in out_uv for c in p],
        'submeshes': [{'material': m, 'triangles': t} for m, t in zip(materials, submeshes)],
        'size': [round(v, 3) for v in size],
        'windingAgree': [agree, total],
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', default=None)
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    args = parser.parse_args(argv)
    project = Path(args.project) if args.project else Path(find_unity_project())
    crates = [build_crate(project, name, parts) for name, parts in CRATES.items()]
    out = project / 'Assets' / 'SkyIsland' / 'Fx' / 'Crates' / 'sky_island_loot_crates.json'
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({'crates': crates}, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    summary = {c['name']: {'size': c['size'], 'vertices': len(c['vertices']) // 3,
                           'triangles': [len(s['triangles']) // 3 for s in c['submeshes']],
                           'windingAgree': c['windingAgree']} for c in crates}
    print('SKY_ISLAND_LOOT_CRATES ' + json.dumps(summary, ensure_ascii=False))
    print('SKY_ISLAND_LOOT_CRATES_OK ' + str(out))


main()
