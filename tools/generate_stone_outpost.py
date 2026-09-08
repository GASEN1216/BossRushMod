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
    source = project / 'ArtSource' / 'StoneOutpost'
    assets = project / 'Assets' / 'StoneOutpost'
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

    box('Foundation', (0, 0, -0.42), (100, 100, 0.8), 'Slate', 0.12)
    collider('COL_Ground', (0, 0, -0.25), (100, 100, 0.5))
    for x in range(20):
        for z in range(20):
            box('Flagstone', (-47.5 + x * 5, -47.5 + z * 5, -0.08),
                (4.94, 4.94, 0.16), rng.choice(['Stone', 'StoneLight', 'StoneDark']), 0.035)
    obstacles = []
    def wall(name, x, z, width, depth, height=2.6):
        box(name, (x, z, height / 2), (width, depth, height), 'StoneDark', 0.10)
        box(name + '_Cap', (x, z, height + 0.10), (width + 0.10, depth + 0.10, 0.2), 'StoneLight')
        collider('COL_' + name, (x, z, height / 2), (width + 0.1, depth + 0.1, height + 0.2))
        obstacles.append((x-width/2-0.05, x+width/2+0.05, z-depth/2-0.05, z+depth/2+0.05))
    for name, x, z, w, d in [('BoundaryN',0,49,100,1), ('BoundaryS',0,-49,100,1),
                              ('BoundaryW',-49,0,1,100), ('BoundaryE',49,0,1,100)]:
        wall(name,x,z,w,d,2.0)
    # 中部遗迹以错开的墙提供两条绕行路径。
    for i, (x,z,w,d) in enumerate([(0,-13,2,25),(-14,8,22,2),(16,10,2,23),
                                    (3,29,24,2),(-26,-12,11,2),(31,-16,14,2)]):
        wall('Ruin'+str(i),x,z,w,d)
    # 两间可进入的仓房；南墙留下 6m 门洞，屋顶只覆盖后侧，俯视相机可看见室内。
    for i, (cx,cz) in enumerate([(-31,29),(32,30)]):
        wall('Warehouse'+str(i)+'N',cx,cz+9,22,1,3.8)
        wall('Warehouse'+str(i)+'W',cx-11,cz,1,18,3.8)
        wall('Warehouse'+str(i)+'E',cx+11,cz,1,18,3.8)
        for dx in (-7,7): wall('Warehouse'+str(i)+'Door'+str(dx),cx+dx,cz-9,8,1,3.0)
        box('WarehouseAwning', (cx,cz+7,4.05), (22,4,0.25), 'Slate')
        marker('Lamp'+str(i),cx,cz)
        box('LampFixture', (cx,cz,3.6), (0.8,0.8,0.3), 'Brass')
        marker('Search'+str(i),cx+6,cz+3)
        box('SupplyCrate', (cx+6,cz+3,0.6), (2,1.5,1.2), 'Brass')
        collider('COL_Crate'+str(i), (cx+6,cz+3,0.6), (2,1.5,1.2))
        obstacles.append((cx+5,cx+7,cz+2.25,cz+3.75))
    marker('Search2',-24,-21)
    box('DispatchCrate', (-24,-21,0.6), (2,1.5,1.2), 'Brass')
    collider('COL_Crate2', (-24,-21,0.6), (2,1.5,1.2))
    obstacles.append((-25,-23,-21.75,-20.25))
    for i, (x,z) in enumerate([(-40,-37),(39,-37),(-13,39),(13,-35)]):
        wall('Pillar'+str(i),x,z,1.6,1.6,4.0)
        box('Beacon', (x,z,4.3), (0.7,0.7,0.35), 'Azure')
    for name, x, z, color in [('PlayerSpawn', -36, -34, 'Brass'), ('EnemySpawn', 8, 17, 'Crimson'),
                            ('Exit', 37, -34, 'Azure'), ('EnemySpawn1', -30, 16, 'Crimson'),
                            ('EnemySpawn2', 31, 16, 'Crimson')]:
        marker(name,x,z)
        bpy.ops.mesh.primitive_torus_add(major_radius=1.4, minor_radius=0.10, major_segments=32,
            minor_segments=6, location=(x,z,0.04))
        ring=bpy.context.object
        ring.name=name+'_Ring'
        ring.data.materials.append(materials[color])
        visuals.append(ring)

    cells = set()
    for ix in range(96):
        for iz in range(96):
            x0, z0 = -48 + ix, -48 + iz
            if any(x0 + 1 > a - 0.7 and x0 < b + 0.7 and z0 + 1 > c - 0.7 and z0 < d + 0.7 for a, b, c, d in obstacles):
                continue
            cells.add((ix, iz))
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
    # 保留原 1m 格子的精确通行域，合并无变化的行列，不降低障碍精度。
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from stone_outpost_navigation import build_navigation
    grid_vertices, faces = build_navigation(cells, 96)
    vertices = [(-48 + x, -48 + y, 0.02) for x, y in grid_vertices]
    print('OUTPOST_NAV_COMPRESSED ' + json.dumps({'vertices': len(vertices), 'triangles': len(faces),
          'area': len(cells), 'coverage_exact': True, 'connected': True}))
    mesh = bpy.data.meshes.new('ArenaNavigationMesh')
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    nav = bpy.data.objects.new('NAV_Outpost', mesh)
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
    fbx = assets / 'StoneOutpost.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'},
        axis_forward='-Z', axis_up='Y', bake_anim=False, add_leaf_bones=False)
    metadata = {'size': 100, 'nav_vertices': len(mesh.vertices), 'nav_triangles': len(mesh.polygons),
                'nav_cells': len(cells), 'connected': True, 'collision_boxes': collision_boxes, 'materials': colors}
    (source / 'outpost_geometry.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    bpy.ops.object.camera_add(location=(115, -143, 138))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0, 0, 0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 145
    bpy.context.scene.camera = camera
    bpy.ops.object.light_add(type='AREA', location=(-40, -25, 100))
    bpy.context.object.data.energy = 90000
    bpy.context.object.data.shape = 'DISK'
    bpy.context.object.data.size = 90
    bpy.context.object.rotation_euler = (0.3, -0.3, 0)
    scene = bpy.context.scene
    scene.world.color = (0.15, 0.18, 0.22)
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(source / 'outpost_preview.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(source / 'outpost_source.blend'))
    bpy.ops.render.render(write_still=True)
    print('OUTPOST_MODEL_OK ' + json.dumps({'fbx': str(fbx), 'nav_triangles': len(mesh.polygons), 'connected': True}))


if __name__ == '__main__':
    main()
