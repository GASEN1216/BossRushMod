"""Blender：把末班编组站仓库区剪票处（three.js `buildEntryProp`，export.js 写出 `lastshift_entry.glb`）转成 Unity 入口小包用的 FBX。

和大图 FBX 不同，这里照潮闸镇竹筏的口径把轴向与单位烘进网格（bake_space_transform + FBX_SCALE_ALL），进 Unity 后是单位变换：
Unity 本地 = (-three.x, three.y, three.z)，即 three.js 坐标绕 Y 转 180°。原点在检票桌底面中心；`DeskLamp` 空物体是灯位。
材质改成短名 `LSY_E_###`，清单写 `entry/materials.json`（原名在 source），贴图另存 `entry/textures/`。

用法：
  D:/blender/blender.exe -b --factory-startup --python-exit-code 1 --python tools/build_lastshift_entry.py -- \
      --src ArtSource/LastShiftYard/export --out ArtSource/LastShiftYard/entry
成功打印 LASTSHIFT_ENTRY_OK {...}。
"""
import argparse
import json
import os
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_lastshift_blend as B  # noqa: E402  复用导入、顶点色、材质清单


def unity_local(v):
    """Blender → Unity 本地（FBX -Z 前 / Y 上，轴向烘进网格）。"""
    return [round(-v[0], 4), round(v[2], 4), round(-v[1], 4)]


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default='ArtSource/LastShiftYard/export')
    ap.add_argument('--out', default='ArtSource/LastShiftYard/entry')
    args = ap.parse_args(argv)
    src, out = Path(args.src).resolve(), Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    man = json.loads((src / 'manifest.json').read_text(encoding='utf-8'))
    entry = man['groups'].get('entryProp')
    if not entry:
        raise RuntimeError('manifest 里没有 entryProp：用当前 export.js 重新导出')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 1.0
    coll = B.new_collection('LSY_entry')
    objs = B.import_glb(src / entry['file'], coll)
    B.ensure_vertex_color(objs)
    for m in bpy.data.materials:
        if getattr(m, 'surface_render_method', None) == 'BLENDED':
            m.surface_render_method = 'DITHERED'
    lamp = next((o for o in coll.all_objects if o.name.split('.')[0] == 'DeskLamp'), None)
    if lamp is None:
        raise RuntimeError('剪票处缺少 DeskLamp 灯位')
    B.make_single_user(coll)
    # 烘轴向（bake_space_transform）只对没有父级、变换已应用的网格可靠：挂在空物体下的网格导出后会少转 90°（2026-11-11 实测）。
    # 先全部解除父级并保持世界变换，再把网格的变换应用进顶点，除灯位外的空物体删掉。
    for o in list(coll.all_objects):
        world = o.matrix_world.copy(); o.parent = None; o.matrix_world = world
    bpy.ops.object.select_all(action='DESELECT')
    for o in coll.all_objects:
        o.select_set(o.type == 'MESH')
    bpy.context.view_layer.objects.active = next(o for o in coll.all_objects if o.type == 'MESH')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in [o for o in coll.all_objects if o.type == 'EMPTY' and o is not lamp]:
        bpy.data.objects.remove(o, do_unlink=True)
    lamp.name = 'DeskLamp'
    used = sorted({s.material.name for o in coll.all_objects if o.type == 'MESH' for s in o.material_slots if s.material})
    for i, name in enumerate(used):
        m = bpy.data.materials[name]; m['lsy_source'] = name; m.name = 'LSY_E_%03d' % i
    B.export_material_manifest([coll], out)
    meshes = [o for o in coll.all_objects if o.type == 'MESH']
    lo = [min(c[i] for o in meshes for c in (o.matrix_world @ v.co for v in o.data.vertices)) for i in range(3)]
    hi = [max(c[i] for o in meshes for c in (o.matrix_world @ v.co for v in o.data.vertices)) for i in range(3)]
    bpy.ops.object.select_all(action='DESELECT')
    for o in coll.all_objects:
        o.select_set(o.type in {'MESH', 'EMPTY'})
    fbx = out / 'LastShiftEntry.fbx'
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                             use_mesh_modifiers=True, use_tspace=False, colors_type='SRGB',
                             bake_anim=False, add_leaf_bones=False, path_mode='COPY', embed_textures=True)
    report = {
        'fbx': str(fbx), 'meshes': len(meshes), 'triangles': B.tris_of(meshes), 'materials': len(used),
        'unityBounds': {'min': [round(-hi[0], 4), round(lo[2], 4), round(-hi[1], 4)], 'max': [round(-lo[0], 4), round(hi[2], 4), round(-lo[1], 4)]},
        'deskLampUnity': unity_local(lamp.matrix_world.translation),
    }
    bpy.ops.wm.save_as_mainfile(filepath=str(out / 'LastShiftEntry.blend'), compress=True)
    (out / 'lastshift_entry_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('LASTSHIFT_ENTRY_OK ' + json.dumps(report, ensure_ascii=False), flush=True)
    if bpy.app.background:
        sys.stdout.flush(); sys.stderr.flush()
        os._exit(0)


if __name__ == '__main__':
    main()
