# -*- coding: utf-8 -*-
"""把末班编组站 Blender 阶段的产物同步进 Unity 作者工程的 Assets/LastShiftYard/（只增改这一个目录，不删任何文件）。

来源是 ArtSource/LastShiftYard/（tools/build_lastshift_blend.py 与 tools/build_lastshift_navigation.py 的输出）。
内容没变的文件不覆盖，免得 Unity 白白重新导入。二进制数据改成 .bytes 扩展名，Unity 才会当 TextAsset 导入。
着色器不在这里同步：它们直接写在作者工程 Assets/LastShiftYard/Shaders/。

M 图：正射原图 + 沿站界（站界遮罩外扩、高斯羽化）和各标记点画透明边，不裁切、不改比例，与原图同中心同尺寸。

用法：python tools/sync_lastshift_to_unity.py [--project <Unity 工程根目录>]，成功打印 LASTSHIFT_SYNC_OK。
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import unity_project_path  # noqa: E402

REPO = Path(__file__).resolve().parent.parent
SRC = REPO / 'ArtSource' / 'LastShiftYard'


def bake_minimap(src: Path):
    import numpy as np
    from PIL import Image, ImageDraw, ImageFilter
    md, export = src / 'minimap', src / 'export'
    raw = Image.open(md / 'lastshift_ortho.png').convert('RGBA')
    side = int(round(json.loads((md / 'lastshift_minimap.json').read_text(encoding='utf-8'))['imageWorldSize']))
    man = json.loads((export / 'manifest.json').read_text(encoding='utf-8'))
    layout = json.loads((export / 'lastshift_layout.json').read_text(encoding='utf-8'))
    t = man['terrain']
    yard = np.fromfile(export / t['yardMaskFile'], 'u1').reshape(int(t['height']), int(t['width']))
    # 1 像素 = 1 米、上方 = three −z（北）：取站界遮罩里以原点为中心、边长 side 的一块
    i0, j0 = -side // 2 - int(t['x0']), -side // 2 - int(t['z0'])
    crop = yard[j0:j0 + side, i0:i0 + side]
    if crop.shape != (side, side):
        raise RuntimeError('站界遮罩不够覆盖 M 图范围')
    mask = Image.fromarray(crop)
    ink = ImageDraw.Draw(mask)
    for obj in layout['loot'] + layout['exits'] + layout['pois'] + layout['interacts']:
        x, _, z = obj['position']
        x += side / 2; z += side / 2
        ink.ellipse((x - 3, z - 3, x + 3, z + 3), fill=255)
    mask = mask.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(3.5))
    raw.putalpha(mask.resize(raw.size, Image.Resampling.BICUBIC))
    # 站界画成墨色虚线（读图通则 §6.1，2026-11-12）：场景里沿站界是一圈废钢轨栅栏，撤离点处留门口
    k = raw.size[0] / side
    pen = ImageDraw.Draw(raw)
    land = layout['land']
    pts = [((p[0] if isinstance(p, list) else p['x']) + side / 2, (p[1] if isinstance(p, list) else p['z']) + side / 2) for p in land]
    dash, gap, width = 5.0, 2.5, max(2, int(round(1.2 * k)))
    for i in range(len(pts)):
        (ax, az), (bx, bz) = pts[i], pts[(i + 1) % len(pts)]
        length, t = ((bx - ax) ** 2 + (bz - az) ** 2) ** 0.5, 0.0
        while t < length:
            u0, u1 = t / length, min(length, t + dash) / length
            pen.line(((ax + (bx - ax) * u0) * k, (az + (bz - az) * u0) * k, (ax + (bx - ax) * u1) * k, (az + (bz - az) * u1) * k), fill=(59, 50, 38, 210), width=width)
            t += dash + gap
    raw.save(md / 'lastshift_minimap.png', optimize=True)


def plan():
    """(来源, 作者工程里 Assets/LastShiftYard 下的相对路径)。"""
    items = []
    for f in sorted((SRC / 'fbx').glob('*.fbx')):
        items.append((f, 'Models/' + f.name))
    for f in sorted((SRC / 'fbx' / 'textures').glob('*.png')):
        items.append((f, 'Textures/Materials/' + f.name))
    ex = SRC / 'export'
    items += [
        (ex / 'tex' / 'yard_ground_atlas.png', 'Textures/Terrain/yard_ground_atlas.png'),  # 原分辨率，比 GLB 里压到 1024 的那份清楚
        (ex / 'tex' / 'grit.png', 'Textures/Terrain/grit.png'),
        (SRC / 'fbx' / 'materials.json', 'Data/materials.json'),
        (SRC / 'fbx' / 'lastshift_instances.json', 'Data/lastshift_instances.json'),
        (SRC / 'fbx' / 'lastshift_roof_faces.json', 'Data/lastshift_roof_faces.json'),
        (ex / 'lastshift_layout.json', 'Data/lastshift_layout.json'),
        (ex / 'manifest.json', 'Data/export_manifest.json'),
        (SRC / 'collision_boxes.json', 'Data/collision_boxes.json'),
        (ex / 'terrain_height.f32', 'Data/terrain_height.bytes'),
        (ex / 'terrain_yardmask.u8', 'Data/terrain_yardmask.bytes'),
        (ex / 'grid_height.f32', 'Data/grid_height.bytes'),
        (ex / 'grid_flag.u8', 'Data/grid_flag.bytes'),
        (SRC / 'navigation.json', 'Data/navigation.json'),
        (SRC / 'minimap' / 'lastshift_minimap.png', 'Minimap/lastshift_minimap.png'),
        (SRC / 'minimap' / 'lastshift_minimap.json', 'Minimap/lastshift_minimap.json'),
        # 仓库区剪票处（tools/build_lastshift_entry.py）：单独打 lastshift_entry 小包，不随出击场景包重打
        (SRC / 'entry' / 'LastShiftEntry.fbx', 'Entry/LastShiftEntry.fbx'),
        (SRC / 'entry' / 'materials.json', 'Entry/materials.json'),
    ]
    for f in sorted((SRC / 'entry' / 'textures').glob('*.png')):
        items.append((f, 'Entry/Textures/' + f.name))
    return items


def digest(p: Path) -> str:
    h = hashlib.sha256()
    with p.open('rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('--project')
    args = ap.parse_args()
    found = unity_project_path.find_unity_project()
    project = Path(args.project) if args.project else (Path(found) if found else None)
    if project is None or not (project / 'Assets').is_dir():
        print('找不到 Unity 作者工程：' + unity_project_path.describe_missing())
        return 2
    dest_root = project / 'Assets' / 'LastShiftYard'
    bake_minimap(SRC)
    missing, copied, same = [], 0, 0
    for src, rel in plan():
        if not src.is_file():
            missing.append(str(src)); continue
        dst = dest_root / rel
        if dst.is_file() and digest(dst) == digest(src):
            same += 1; continue
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst); copied += 1
    if missing:
        print('缺少来源文件（先跑 tools/build_lastshift_blend.py --fbx 与 tools/build_lastshift_navigation.py --check）：\n  ' + '\n  '.join(missing))
        return 1
    print(f'LASTSHIFT_SYNC_OK 复制 {copied} 个，未变 {same} 个 → {dest_root}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
