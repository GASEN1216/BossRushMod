"""在 Blender 后台按天空岛官方地图的投影渲染正交俯视图（只读 .blend，不保存）。

由 tools/sky_island_minimap_art.py reference 调用：
    blender -b --factory-startup SkyIslandWorld.blend --python-exit-code 1 --python tools/sky_island_minimap_render.py -- \
        --center-x <米> --center-z <米> --world <米> --size 2048 --out <png>
生成器把 Unity (x, y, z) 写成 Blender (x, z, y)：相机放在 (中心 x, 中心 z, 高处) 且不旋转，
画面上方就是 Unity +Z（北），与 build_sky_island_minimap.py 的投影一致。云海与远景岛不渲染。
"""
import json
import sys

import bpy

args = sys.argv[sys.argv.index('--') + 1:]
opts = dict(zip(args[0::2], args[1::2]))
size = int(opts.get('--size', '2048'))
world = float(opts['--world'])

scene = bpy.context.scene
engines = [item.identifier for item in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
engine = next(e for e in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE', 'BLENDER_WORKBENCH') if e in engines)
scene.render.engine = engine
if engine.startswith('BLENDER_EEVEE'):
    scene.eevee.taa_render_samples = 24

hidden = 0
for obj in scene.objects:
    names = [obj.name.lower()] + [slot.material.name.lower() for slot in getattr(obj, 'material_slots', []) if slot.material]
    if any(token in name for name in names for token in ('cloud', 'distant')):
        obj.hide_render = True
        hidden += 1

camera_data = bpy.data.cameras.new('MinimapTopDown')
camera_data.type = 'ORTHO'
camera_data.ortho_scale = world
camera_data.clip_start = 0.1
camera_data.clip_end = 10000
camera = bpy.data.objects.new('MinimapTopDown', camera_data)
scene.collection.objects.link(camera)
camera.location = (float(opts['--center-x']), float(opts['--center-z']), 2000.0)
camera.rotation_euler = (0.0, 0.0, 0.0)
scene.camera = camera

render = scene.render
render.resolution_x = size
render.resolution_y = size
render.resolution_percentage = 100
render.pixel_aspect_x = 1
render.pixel_aspect_y = 1
render.film_transparent = True
render.image_settings.file_format = 'PNG'
render.image_settings.color_mode = 'RGBA'
render.filepath = opts['--out']
bpy.ops.render.render(write_still=True)
print('MINIMAP_TOPDOWN_OK ' + json.dumps({'engine': engine, 'blender': bpy.app.version_string, 'hidden': hidden}))
