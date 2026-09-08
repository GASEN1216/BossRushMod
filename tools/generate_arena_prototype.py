"""Blender 内运行：生成自建竞技场的 FBX、源文件与预览；仅写指定新目录。"""
import argparse
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    project = Path(args.project).resolve()
    if not (project / 'ProjectSettings' / 'ProjectVersion.txt').is_file():
        raise ValueError('必须指定已有 Unity 工程')
    source = project / 'ArtSource' / 'ArenaPrototype'
    assets = project / 'Assets' / 'ArenaPrototype'
    source.mkdir(parents=True, exist_ok=True)
    assets.mkdir(parents=True, exist_ok=True)
    # 只清空本次 --factory-startup 后台进程的内存场景，不删除磁盘文件。
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    rng = random.Random(908)
    colors = {
        'Slate': (0.24, 0.30, 0.32, 1),
        'Stone': (0.42, 0.49, 0.47, 1),
        'StoneLight': (0.54, 0.60, 0.55, 1),
        'StoneDark': (0.31, 0.37, 0.36, 1),
        'Brass': (0.71, 0.45, 0.16, 1),
        'Azure': (0.10, 0.66, 0.73, 1),
        'Crimson': (0.61, 0.19, 0.13, 1),
    }
    materials = {}
    for name, rgba in colors.items():
        mat = bpy.data.materials.new('Arena_' + name)
        mat.diffuse_color = rgba
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = rgba
        bsdf.inputs['Roughness'].default_value = 0.82
        if name == 'Azure':
            bsdf.inputs['Emission Color'].default_value = rgba
            bsdf.inputs['Emission Strength'].default_value = 0.65
        materials[name] = mat
    visuals = []
    collision_boxes = []

    def box(name, location, size, material='Stone', bevel=0.04, visible=True):
        bpy.ops.mesh.primitive_cube_add(size=1, location=location)
        obj = bpy.context.object
        obj.name = name
        obj.dimensions = size
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        obj.data.materials.append(materials[material])
        if bevel:
            mod = obj.modifiers.new('Chipped edges', 'BEVEL')
            mod.width = bevel
            mod.segments = 1
            bpy.ops.object.modifier_apply(modifier=mod.name)
        obj.hide_render = not visible
        if visible:
            visuals.append(obj)
        return obj

    def collider(name, location, size):
        collision_boxes.append({'name': name, 'center_blender': location, 'size_blender': size})
        return box(name, location, size, bevel=0, visible=False)

    def marker(name, x, z):
        obj = bpy.data.objects.new(name, None)
        obj.location = (x, z, 0.2)
        bpy.context.scene.collection.objects.link(obj)

    box('Foundation', (0, 0, -0.42), (30, 30, 0.8), 'Slate', 0.12)
    collider('COL_Ground', (0, 0, -0.25), (30, 30, 0.5))
    for x in range(10):
        for z in range(10):
            box('Flagstone', (-13.5 + x * 3, -13.5 + z * 3, -0.08),
                (2.94, 2.94, 0.16), rng.choice(['Stone', 'StoneLight', 'StoneDark']), 0.035)
    for side in range(4):
        for n in range(10):
            p = -13.5 + n * 3
            x, z = ((p, -14.6) if side == 0 else (p, 14.6) if side == 1
                    else (-14.6, p) if side == 2 else (14.6, p))
            size = (2.94, 0.75, 1.35) if side < 2 else (0.75, 2.94, 1.35)
            box('Rampart', (x, z, 0.67), size, 'StoneDark', 0.10)
            box('Coping', (x, z, 1.42), (size[0] + 0.08, size[1] + 0.08, 0.15), 'StoneLight')
    for name, loc, size in [
        ('COL_Wall_N', (0, 14.6, 2), (30, 0.8, 4)),
        ('COL_Wall_S', (0, -14.6, 2), (30, 0.8, 4)),
        ('COL_Wall_E', (14.6, 0, 2), (0.8, 30, 4)),
        ('COL_Wall_W', (-14.6, 0, 2), (0.8, 30, 4)),
    ]:
        collider(name, loc, size)
    # 同一碰撞布局同时决定可行走网格，避免看见墙却直线穿墙的导航。
    obstacles = [(-1.6, 1.6, -7, 3), (3, 8, 4, 6), (-9, -6.5, 3, 5.5)]
    for i, (xmin, xmax, zmin, zmax) in enumerate(obstacles):
        cx, cz = (xmin + xmax) / 2, (zmin + zmax) / 2
        width, depth = xmax - xmin, zmax - zmin
        box('Obstacle_' + str(i), (cx, cz, 1.10), (width, depth, 2.2), 'StoneDark', 0.13)
        box('ObstacleCap_' + str(i), (cx, cz, 2.25), (width + 0.12, depth + 0.12, 0.18), 'StoneLight')
        collider('COL_Obstacle_' + str(i), (cx, cz, 1.2), (width + 0.12, depth + 0.12, 2.4))
        for line in (-0.3, 0.3):
            box('Inlay', (cx + line, cz, 2.36), (0.055, depth * 0.8, 0.015), 'Brass', 0)
    for x in (-12, 12):
        for z in (-12, 12):
            box('PillarFoot', (x, z, 0.2), (1.45, 1.45, 0.4), 'Slate')
            box('Pillar', (x, z, 1.7), (1, 1, 2.7), 'Stone', 0.1)
            box('PillarCapital', (x, z, 3.15), (1.4, 1.4, 0.22), 'StoneLight')
            box('Beacon', (x, z, 3.4), (0.48, 0.48, 0.3), 'Azure', 0.10)
            obstacles.append((x - 0.75, x + 0.75, z - 0.75, z + 0.75))
            collider('COL_Pillar_' + str(x) + '_' + str(z), (x, z, 1.7), (1.5, 1.5, 3.6))
    for name, x, z, color in [('PlayerSpawn', -9, -9, 'Brass'), ('EnemySpawn', 7, 9, 'Crimson'), ('Exit', 9, -9, 'Azure')]:
        marker(name, x, z)
        bpy.ops.mesh.primitive_torus_add(major_radius=1.05, minor_radius=0.065, major_segments=32, minor_segments=6, location=(x, z, 0.04))
        ring = bpy.context.object
        ring.name = name + '_Ring'
        ring.data.materials.append(materials[color])
        visuals.append(ring)
        for a in range(4):
            angle = a * math.pi / 2
            box('Compass', (x + math.cos(angle) * 1.45, z + math.sin(angle) * 1.45, 0.03), (0.17, 0.17, 0.045), color, 0.01)

    vertices, faces, shared = [], [], {}
    def vertex(ix, iz):
        key = (ix, iz)
        if key not in shared:
            shared[key] = len(vertices)
            vertices.append((-14 + ix * 0.5, -14 + iz * 0.5, 0.02))
        return shared[key]
    cells = set()
    for ix in range(56):
        for iz in range(56):
            x0, z0 = -14 + ix * 0.5, -14 + iz * 0.5
            if any(x0 + 0.5 > a - 0.7 and x0 < b + 0.7 and z0 + 0.5 > c - 0.7 and z0 < d + 0.7 for a, b, c, d in obstacles):
                continue
            cells.add((ix, iz))
            a, b, c, d = vertex(ix, iz), vertex(ix + 1, iz), vertex(ix + 1, iz + 1), vertex(ix, iz + 1)
            faces.extend([(a, b, c), (a, c, d)])
    # 静态连通性验证：全部可行走格子必须连通。
    seen, pending = set(), [next(iter(cells))]
    while pending:
        cell = pending.pop()
        if cell in seen:
            continue
        seen.add(cell)
        x, z = cell
        pending.extend(n for n in [(x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1)] if n in cells and n not in seen)
    assert seen == cells, '路径网格存在孤岛'
    mesh = bpy.data.meshes.new('ArenaNavigationMesh')
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    nav = bpy.data.objects.new('NAV_Arena', mesh)
    bpy.context.scene.collection.objects.link(nav)
    nav.hide_render = True

    # 按材质合并可见模型，避免每块地砖一个 renderer。
    groups = {material: [o for o in visuals if o.data.materials[0] == material] for material in materials.values()}
    for material, group in groups.items():
        if not group:
            continue
        bpy.ops.object.select_all(action='DESELECT')
        for obj in group:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = group[0]
        bpy.ops.object.join()
        bpy.context.object.name = 'VIS_' + material.name
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.data.objects:
        obj.select_set(obj.type in {'MESH', 'EMPTY'})
    fbx = assets / 'ArenaPrototype.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'},
        axis_forward='-Z', axis_up='Y', bake_anim=False, add_leaf_bones=False)
    metadata = {'size': 30, 'nav_vertices': len(vertices), 'nav_triangles': len(faces),
                'nav_cells': len(cells), 'connected': True, 'collision_boxes': collision_boxes, 'materials': colors}
    (source / 'arena_geometry.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    bpy.ops.object.camera_add(location=(35, -43, 42))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0, 0, 0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 44
    bpy.context.scene.camera = camera
    bpy.ops.object.light_add(type='AREA', location=(-12, -8, 30))
    bpy.context.object.data.energy = 8000
    bpy.context.object.data.shape = 'DISK'
    bpy.context.object.data.size = 25
    bpy.context.object.rotation_euler = (0.3, -0.3, 0)
    scene = bpy.context.scene
    scene.world.color = (0.15, 0.18, 0.22)
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(source / 'arena_preview.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(source / 'arena_source.blend'))
    bpy.ops.render.render(write_still=True)
    print('ARENA_MODEL_OK ' + json.dumps({'fbx': str(fbx), 'nav_triangles': len(faces), 'connected': True}))


if __name__ == '__main__':
    main()
