"""Tripo 批量回收件对号工具。

对应 docs/制作教程/天空岛_Tripo3D建模接入与画风对齐教程.md 的 §3.5（批量流水线）与 §11（自动命名不可信）。

Tripo3D 按图片内容自动起名，批量下载回来是 `bushes+3d+model.glb`、
`floating+island+3d+model (2).glb` 这类名字，同一批里还会出现名字与内容对不上的件。
导入器只认 `face_<档>/<素材名>.glb`，所以回收后必须先对号改名。四个子命令：

    inspect  系统 Python，秒级。纯解析 GLB 的 JSON 块：三角面、网格数、贴图数、原始包围盒与比例。
             **贴图数 0 就是白模**（Tripo 没开 texture），要回 Tripo 补贴图，不能直接导。
    render   Blender 后台。逐件出一张四分之三视角缩略图，写 _thumbs/index.json。
    sheet    系统 Python。按档把「素材图」与「模型渲染」拼成对号表，逐行人工确认。
    rename   系统 Python。按 _thumbs/mapping.json 把回收件改成素材名；目标已存在就拒绝，
             原名留在 mapping.json 里，可逆。

用法：
    python tools/sky_island_tripo_contact_sheet.py inspect --glb-dir Build/skyisland-ref/round2
    blender -b --python tools/sky_island_tripo_contact_sheet.py -- render --glb-dir Build/skyisland-ref/round2
    python tools/sky_island_tripo_contact_sheet.py sheet --glb-dir Build/skyisland-ref/round2
    python tools/sky_island_tripo_contact_sheet.py rename --glb-dir Build/skyisland-ref/round2
"""

import json
import struct
import sys
from pathlib import Path

THUMBS = '_thumbs'


def model_files(glb_dir):
    """档目录下的回收件。下划线开头的目录（`_thumbs`、存放退件的 `_rejected`）一律跳过，
    否则退回去重做的件会混进下一轮对号表。"""
    root = Path(glb_dir)
    return sorted(p for p in root.rglob('*')
                  if p.is_file() and p.suffix.lower() in ('.glb', '.fbx')
                  and not any(part.startswith('_') for part in p.relative_to(root).parts))


def tier_of(path, root):
    rel = Path(path).resolve().relative_to(Path(root).resolve())
    return rel.parts[0] if len(rel.parts) > 1 else '.'


# ── inspect：纯 Python 读 GLB ──────────────────────────────────────────

def read_glb_json(path):
    data = Path(path).read_bytes()
    if data[:4] != b'glTF':
        raise ValueError('不是 GLB')
    length, kind = struct.unpack_from('<I4s', data, 12)
    if kind != b'JSON':
        raise ValueError('首块不是 JSON')
    return json.loads(data[20:20 + length].decode('utf-8'))


def inspect_glb(path):
    doc = read_glb_json(path)
    accessors = doc.get('accessors', [])
    triangles = 0
    lo = [float('inf')] * 3
    hi = [float('-inf')] * 3
    for mesh in doc.get('meshes', []):
        for prim in mesh.get('primitives', []):
            if prim.get('mode', 4) != 4:
                continue
            if 'indices' in prim:
                triangles += accessors[prim['indices']]['count'] // 3
            else:
                triangles += accessors[prim['attributes']['POSITION']]['count'] // 3
            pos = accessors[prim['attributes']['POSITION']]
            for axis in range(3):
                lo[axis] = min(lo[axis], pos.get('min', [0, 0, 0])[axis])
                hi[axis] = max(hi[axis], pos.get('max', [0, 0, 0])[axis])
    # glTF 是 Y 朝上：宽 = X、高 = Y、深 = Z。
    size = [hi[i] - lo[i] for i in range(3)]
    width, height, depth = size
    footprint = max(width, depth)
    return {
        'triangles': triangles,
        'meshes': len(doc.get('meshes', [])),
        'images': len(doc.get('images', [])),
        'materials': len(doc.get('materials', [])),
        'nodeTransforms': sum(1 for n in doc.get('nodes', [])
                              if any(k in n for k in ('rotation', 'scale', 'matrix'))),
        'size': [round(v, 4) for v in size],
        'heightOverFootprint': round(height / footprint, 3) if footprint > 0 else None,
        'footprintRatio': round(max(width, depth) / min(width, depth), 3) if min(width, depth) > 0 else None,
    }


def cmd_inspect(glb_dir):
    rows = []
    for path in model_files(glb_dir):
        if path.suffix.lower() != '.glb':
            rows.append((tier_of(path, glb_dir), path.name, None))
            continue
        try:
            rows.append((tier_of(path, glb_dir), path.name, inspect_glb(path)))
        except Exception as error:  # 坏文件也要列出来，不能静默跳过
            rows.append((tier_of(path, glb_dir), path.name, {'error': str(error)}))
    print('%-10s %-44s %7s %4s %4s  %-26s %6s %6s' %
          ('tier', 'file', 'tris', 'mesh', 'img', 'size W x H x D', 'H/F', 'W/D'))
    for tier, name, info in rows:
        if info is None:
            print('%-10s %-44s   (FBX，用 render 看)' % (tier, name))
            continue
        if 'error' in info:
            print('%-10s %-44s   READ FAILED: %s' % (tier, name, info['error']))
            continue
        flag = '  <- 白模：没有贴图' if info['images'] == 0 else ''
        print('%-10s %-44s %7d %4d %4d  %-26s %6s %6s%s' % (
            tier, name, info['triangles'], info['meshes'], info['images'],
            '%.2f x %.2f x %.2f' % tuple(info['size']),
            info['heightOverFootprint'], info['footprintRatio'], flag))


# ── render：Blender 后台出缩略图 ────────────────────────────────────────

def cmd_render(glb_dir):
    import bpy
    from mathutils import Vector

    root = Path(glb_dir).resolve()
    out_root = root / THUMBS
    index = {}

    def purge():
        # import_scene.gltf 不清场景：不先删干净，渲出来的会是默认 Cube（§11 的 RAW_SIZE 2x2x2 信号）。
        for obj in list(bpy.data.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        for pool in (bpy.data.meshes, bpy.data.materials, bpy.data.images,
                     bpy.data.cameras, bpy.data.lights, bpy.data.textures):
            for block in list(pool):
                pool.remove(block)

    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'TEXTURE'
    scene.render.resolution_x = scene.render.resolution_y = 384
    scene.render.image_settings.file_format = 'PNG'
    if scene.world is None:
        scene.world = bpy.data.worlds.new('ThumbWorld')
    scene.world.color = (0.62, 0.62, 0.62)

    for path in model_files(root):
        purge()
        if path.suffix.lower() == '.glb':
            bpy.ops.import_scene.gltf(filepath=str(path))
        else:
            bpy.ops.import_scene.fbx(filepath=str(path))
        bpy.context.view_layer.update()
        meshes = [o for o in scene.objects if o.type == 'MESH']
        points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
        if not points:
            print('THUMB_SKIP %s 没有网格' % path.name)
            continue
        lo = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
        hi = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
        size = hi - lo
        centre = (lo + hi) / 2
        radius = max(size.x, size.y, size.z)
        camera_data = bpy.data.cameras.new('ThumbCam')
        camera_data.type = 'ORTHO'
        camera_data.ortho_scale = radius * 1.45
        camera = bpy.data.objects.new('ThumbCam', camera_data)
        scene.collection.objects.link(camera)
        direction = Vector((1.0, -1.0, 0.75)).normalized()
        camera.location = centre + direction * radius * 4
        camera.rotation_euler = (centre - camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.camera = camera
        tier = tier_of(path, root)
        target = out_root / tier / (path.stem + '.png')
        target.parent.mkdir(parents=True, exist_ok=True)
        scene.render.filepath = str(target)
        bpy.ops.render.render(write_still=True)
        triangles = sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons)
        # Blender 导入后是 Z 朝上：宽 = X、深 = Y、高 = Z。
        index[str(path.relative_to(root)).replace('\\', '/')] = {
            'tier': tier, 'thumb': str(target.relative_to(root)).replace('\\', '/'),
            'triangles': triangles, 'images': len(bpy.data.images),
            'size': [round(size.x, 4), round(size.z, 4), round(size.y, 4)],
        }
        print('THUMB_OK %-44s tris=%d images=%d size=%.2f x %.2f x %.2f' % (
            path.name, triangles, len(bpy.data.images), size.x, size.z, size.y))

    out_root.mkdir(parents=True, exist_ok=True)
    (out_root / 'index.json').write_text(json.dumps(index, ensure_ascii=False, indent=2), encoding='utf-8')
    print('THUMB_DONE %d' % len(index))


# ── sheet：拼对号表 ───────────────────────────────────────────────────

def cmd_sheet(glb_dir):
    from PIL import Image, ImageDraw

    root = Path(glb_dir).resolve()
    out_root = root / THUMBS
    index = json.loads((out_root / 'index.json').read_text(encoding='utf-8'))
    cell, label, columns = 256, 30, 7
    for tier in sorted({v['tier'] for v in index.values()}):
        refs = sorted((root / tier).glob('*.png'))
        renders = sorted((k, v) for k, v in index.items() if v['tier'] == tier)
        rows_ref = -(-len(refs) // columns)
        rows_ren = -(-len(renders) // columns)
        height = (rows_ref + rows_ren) * (cell + label) + 40
        sheet = Image.new('RGB', (columns * cell, height), (40, 40, 40))
        draw = ImageDraw.Draw(sheet)
        draw.text((6, 4), '%s  top: reference images (asset names)   bottom: returned models (#index)' % tier,
                  fill=(255, 255, 0))
        y0 = 20
        for i, ref in enumerate(refs):
            x, y = (i % columns) * cell, y0 + (i // columns) * (cell + label)
            sheet.paste(Image.open(ref).convert('RGB').resize((cell, cell)), (x, y))
            draw.text((x + 4, y + cell + 6), ref.stem, fill=(120, 220, 255))
        y0 += rows_ref * (cell + label) + 20
        for i, (key, info) in enumerate(renders):
            x, y = (i % columns) * cell, y0 + (i // columns) * (cell + label)
            thumb = root / info['thumb']
            if thumb.exists():
                sheet.paste(Image.open(thumb).convert('RGB').resize((cell, cell)), (x, y))
            w, h, d = info['size']
            footprint = max(w, d) or 1
            draw.text((x + 4, y + 4), '#%d' % (i + 1), fill=(255, 80, 80))
            draw.text((x + 4, y + cell + 2), Path(key).stem[:34], fill=(255, 255, 255))
            draw.text((x + 4, y + cell + 15), 'H/F=%.2f tris=%d img=%d' % (h / footprint, info['triangles'],
                                                                           info['images']), fill=(200, 200, 200))
        target = out_root / ('sheet_%s.png' % tier)
        sheet.save(target)
        print('SHEET %s -> %s' % (tier, target))


# ── rename：按人工确认的对号表改名 ─────────────────────────────────────

def cmd_rename(glb_dir):
    root = Path(glb_dir).resolve()
    mapping_path = root / THUMBS / 'mapping.json'
    mapping = json.loads(mapping_path.read_text(encoding='utf-8'))
    planned = []
    for original, asset in mapping.items():
        source = root / original
        target = source.with_name(asset + source.suffix.lower())
        if not source.exists():
            if target.exists():
                continue  # 已经改过，幂等
            sys.exit('找不到回收件：%s' % original)
        if target.exists():
            sys.exit('目标已存在，拒绝覆盖：%s' % target.relative_to(root))
        planned.append((source, target))
    for source, target in planned:
        source.rename(target)
        print('RENAMED %s -> %s' % (source.relative_to(root), target.relative_to(root)))
    print('RENAME_DONE %d（原名记录在 %s）' % (len(planned), mapping_path.relative_to(root)))


# ── refs：出图后、上传 Tripo 之前逐张看 ────────────────────────────────

def cmd_refs(glb_dir):
    """按档把素材图拼成一张审图表。

    第二轮有 5 张素材图主体整个画错（花丛出成石屋、藤蔓出成石拱门、小石块出成水井），
    抽查几张发现不了，直到模型做回来才暴露——白花一轮 Tripo 额度。上传前必须全量过一遍。
    """
    from PIL import Image, ImageDraw

    root = Path(glb_dir).resolve()
    out_root = root / THUMBS
    out_root.mkdir(parents=True, exist_ok=True)
    cell, label, columns = 256, 18, 7
    for tier_dir in sorted(p for p in root.iterdir() if p.is_dir() and not p.name.startswith('_')):
        refs = sorted(tier_dir.glob('*.png'))
        if not refs:
            continue
        rows = -(-len(refs) // columns)
        sheet = Image.new('RGB', (columns * cell, rows * (cell + label) + 20), (40, 40, 40))
        draw = ImageDraw.Draw(sheet)
        draw.text((6, 4), '%s  reference images - check EVERY subject before uploading to Tripo' % tier_dir.name,
                  fill=(255, 255, 0))
        for i, ref in enumerate(refs):
            x, y = (i % columns) * cell, 20 + (i // columns) * (cell + label)
            sheet.paste(Image.open(ref).convert('RGB').resize((cell, cell)), (x, y))
            draw.text((x + 4, y + cell + 3), ref.stem, fill=(120, 220, 255))
        target = out_root / ('refs_%s.png' % tier_dir.name)
        sheet.save(target)
        print('REFS %s -> %s' % (tier_dir.name, target))


def main(argv):
    commands = {'inspect': cmd_inspect, 'render': cmd_render, 'sheet': cmd_sheet,
                'rename': cmd_rename, 'refs': cmd_refs}
    if not argv or argv[0] not in commands:
        sys.exit(__doc__)
    if '--glb-dir' not in argv:
        sys.exit('缺少 --glb-dir')
    commands[argv[0]](argv[argv.index('--glb-dir') + 1])


if __name__ == '__main__':
    main(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:])
