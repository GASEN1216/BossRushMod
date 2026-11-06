# -*- coding: utf-8 -*-
"""把潮闸镇 Blender 阶段的产物同步进 Unity 作者工程的 Assets/Tidegate/（只增改这一个目录，不删任何文件）。

来源是 ArtSource/Tidegate/（tools/build_tidegate_blend.py 的输出）。内容没变的文件不覆盖，免得 Unity 白白重新导入。
二进制数据改成 .bytes 扩展名，Unity 才会当 TextAsset 导入。

用法：python tools/sync_tidegate_to_unity.py [--project <Unity 工程根目录>]
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
SRC = REPO / 'ArtSource' / 'Tidegate'
TERRAIN_LAYERS = ['grassTex', 'sandTex', 'mudTex', 'rockTex', 'paving', 'asphalt', 'concrete']

def bake_minimap(src: Path):
    """按低潮可走的地形、水域、甲板及实际搜集点给正射图画透明岸线，不裁切或改变世界坐标比例。"""
    import numpy as np
    from PIL import Image, ImageDraw, ImageFilter
    md, export = src / 'minimap', src / 'export'
    raw = Image.open(md / 'tidegate_ortho.png').convert('RGBA')
    side = int(json.loads((md / 'tidegate_minimap.json').read_text(encoding='utf-8'))['imageWorldSize'])
    layout = json.loads((export / 'tidegate_layout.json').read_text(encoding='utf-8'))
    g = layout['grid']
    h = np.fromfile(export / g['heightFile'], '<f4').reshape(g['H'], g['W'])
    flags = np.fromfile(export / g['flagFile'], 'u1').reshape(h.shape)
    zones = np.fromfile(export / g['zoneFile'], 'u1').reshape(h.shape)
    # 正射图 X 往右、three 的 Z 往下；与官方 M 图 X/+Z 投影对应。地形与标记都用米。
    # 依地物而非矩形边界描海图：街路、滩涂、码头及内湖保留，远景草坡、岩堆和空海床隐入底色。
    backdrop = [g['zones'][k] for k in ('GRASS', 'ROCK', 'SEABED')]
    visible = ((h >= -0.55) & ~np.isin(zones, backdrop)) | ((flags & (g['flags']['LAG'] | g['flags']['DECK'])) != 0)
    mask = Image.new('L', (side, side))
    mask.paste(Image.fromarray(visible.astype('uint8') * 255), (side // 2 + g['OX'], side // 2 + g['OZ']))
    ink = ImageDraw.Draw(mask)
    for obj in layout['containers'] + layout['exits'] + layout['pois'] + layout['valves']:
        x, _, z = obj['position']
        x += side / 2; z += side / 2
        ink.ellipse((x - 2, z - 2, x + 2, z + 2), fill=255)
    mask = mask.filter(ImageFilter.MaxFilter(11)).filter(ImageFilter.GaussianBlur(3.0))
    # 在海岸外缘留下薄薄的水色，而不是把整张海面画成实心方块。缩放时仍同原图共用中心与比例。
    alpha = mask.resize(raw.size, Image.Resampling.BICUBIC)
    raw.putalpha(alpha)
    raw.save(md / 'tidegate_minimap.png', optimize=True)


def plan():
    """(来源, 作者工程里 Assets/Tidegate 下的相对路径)。"""
    items = []
    for f in sorted((SRC / 'fbx').glob('*.fbx')):
        items.append((f, 'Models/' + f.name))
    for f in sorted((SRC / 'fbx' / 'textures').glob('*.png')):
        items.append((f, 'Textures/Materials/' + f.name))
    for k in TERRAIN_LAYERS:
        items.append((SRC / 'export' / 'tex' / (k + '.png'), 'Textures/Terrain/' + k + '.png'))
    for n in ('tidegate_splat_A.png', 'tidegate_splat_B.png', 'tidegate_lagoon_mask.png'):
        items.append((SRC / 'textures' / n, 'Textures/Terrain/' + n))
    items += [
        (SRC / 'fbx' / 'materials.json', 'Data/materials.json'),
        (SRC / 'export' / 'tidegate_layout.json', 'Data/tidegate_layout.json'),
        (SRC / 'export' / 'manifest.json', 'Data/export_manifest.json'),
        (SRC / 'collision_boxes.json', 'Data/collision_boxes.json'),
        (SRC / 'export' / 'terrain_height.f32', 'Data/terrain_height.bytes'),
        (SRC / 'export' / 'terrain_lagoon.u8', 'Data/terrain_lagoon.bytes'),
        (SRC / 'export' / 'grid_height.f32', 'Data/grid_height.bytes'),
        (SRC / 'export' / 'grid_flag.u8', 'Data/grid_flag.bytes'),
        (SRC / 'navigation.json', 'Data/navigation.json'),  # tools/build_tidegate_navigation.py
        (SRC / 'minimap' / 'tidegate_minimap.png', 'Minimap/tidegate_minimap.png'),
        (SRC / 'minimap' / 'tidegate_minimap.json', 'Minimap/tidegate_minimap.json'),
        (SRC / 'raft' / 'TidegateRaft.fbx', 'Entry/TidegateRaft.fbx'),  # tools/build_tidegate_raft.py：基地入口竹筏，单独打 tidegate_entry 包
    ]
    return items


def digest(p: Path) -> str:
    h = hashlib.sha256()
    with p.open('rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--project')
    args = ap.parse_args()
    project = Path(args.project) if args.project else (Path(p) if (p := unity_project_path.find_unity_project()) else None)
    if project is None or not (project / 'Assets').is_dir():
        print('找不到 Unity 作者工程：' + unity_project_path.describe_missing() if hasattr(unity_project_path, 'describe_missing') else '找不到 Unity 作者工程')
        return 2
    dest_root = project / 'Assets' / 'Tidegate'
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
        print('缺少来源文件（先跑 tools/build_tidegate_blend.py --fbx；navigation.json 由 tools/build_tidegate_navigation.py --check 生成；竹筏由 tools/build_tidegate_raft.py 生成）：\n  '
              + '\n  '.join(missing))
        return 1
    print(f'TIDEGATE_SYNC_OK 复制 {copied} 个，未变 {same} 个 → {dest_root}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
