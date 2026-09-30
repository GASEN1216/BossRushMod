"""Tripo 件图集按原件重导到更高分辨率（只换贴图，网格与 UV 一个字节不动）。

材质审计（`Build/sky-material-audit-20260930` F2）：民居、谷仓、磨坊、茶楼等 22 个 Tripo 大件的图集在导入时
被压到 512²（神庙、工坊穹顶 1024²），纹素只有 4–20 px/m，1080p 一屏约需 69 px/m，看上去糊；图集均值亮度
L 32–50，比原版石木（56–59）暗一截。原件 GLB 的底色贴图多是 4096²，留存在
`BossRushMod-Build留存/skyisland-ref-Tripo原件`（仓库外）。

本工具不重跑 `sky_island_tripo_import.py`（那会重新减面、重新拆 UV、改网格包络、连带道路与导航重烘），
只从 GLB 里取出**同一张**底色贴图（与现有图集缩到 128² 比相关系数，低于 0.9 拒绝），按新档位缩放，
把偏暗的图集用明度伽马提到均值 L≈0.50（只提不压，伽马不低于 0.72），覆盖作者工程
`Assets/SkyIsland/Textures/tripo_<件>.png`，并把 meta 里的贴图尺寸同步到 `tripo/<件>.json`。
清单写 `ArtSource/SkyIsland/tripo_texture_upgrade.json`。

用法：python tools/sky_island_tripo_textures.py --project <作者工程> --originals <原件目录>
"""
import argparse
import hashlib
import io
import json
import struct
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / 'ArtSource/SkyIsland/tripo_texture_upgrade.json'
# 件 → 新边长。只升屏幕上体量大、离镜头近的建筑与地标；高复用小件维持原档。
UPGRADE = {name: 1024 for name in (
    'village_house_a', 'village_house_b', 'village_house_c', 'mill', 'barn', 'workshop_shed', 'dock_house',
    'tea_house', 'pavilion_hall', 'post_house', 'cottage_large', 'cottage_dormer', 'cottage_small', 'farm_barn',
    'market_stall', 'cherry_pavilion', 'shrine_pavilion', 'lookout_tower', 'cave_rock', 'wind_beacon')}
UPGRADE.update({'temple': 2048, 'workshop_dome': 2048})
# 复测补上（2026-09-30）：悬挂水晶 256² 只有 12.7 px/m；远景岛 A/B/C 512²（3.9–10.9 px/m）。
# 瀑布不升：它的水 / 石分面按图集像素颜色判定，重导改了明度会让分面漂移。
UPDATE_LATE = {'glow_crystal': 1024, 'distant_islet_a': 1024, 'distant_islet_b': 1024, 'distant_islet_c': 1024}
UPGRADE.update(UPDATE_LATE)
TARGET_LIGHTNESS = .50
MIN_GAMMA = .72


def glb_base_color(path):
    data = path.read_bytes()
    magic, _version, _length = struct.unpack_from('<III', data, 0)
    if magic != 0x46546C67:
        raise ValueError('not a GLB: %s' % path)
    offset, doc, blob = 12, None, None
    while offset < len(data):
        size, kind = struct.unpack_from('<II', data, offset)
        chunk = data[offset + 8:offset + 8 + size]
        if kind == 0x4E4F534A:
            doc = json.loads(chunk.decode('utf-8'))
        elif kind == 0x004E4942:
            blob = chunk
        offset += 8 + size
    material = doc['materials'][0]
    texture = doc['textures'][material['pbrMetallicRoughness']['baseColorTexture']['index']]
    image = doc['images'][texture['source']]
    view = doc['bufferViews'][image['bufferView']]
    raw = blob[view.get('byteOffset', 0):view.get('byteOffset', 0) + view['byteLength']]
    return Image.open(io.BytesIO(raw)).convert('RGB')


def correlation(a, b):
    x = np.asarray(a.resize((128, 128), Image.BILINEAR).convert('L'), dtype=np.float64).ravel()
    y = np.asarray(b.resize((128, 128), Image.BILINEAR).convert('L'), dtype=np.float64).ravel()
    x -= x.mean(); y -= y.mean()
    return float((x @ y) / max(np.sqrt((x @ x) * (y @ y)), 1e-9))


def lift(image):
    """HSL 明度均值低于目标时用伽马提亮（色相、饱和度比例保持）。返回 (图, 伽马, 前后均值)。"""
    rgb = np.asarray(image, dtype=np.float64) / 255.0
    mx, mn = rgb.max(axis=-1), rgb.min(axis=-1)
    lightness = (mx + mn) / 2
    before = float(lightness.mean())
    if before >= TARGET_LIGHTNESS:
        return image, 1.0, before, before
    gamma = max(MIN_GAMMA, np.log(TARGET_LIGHTNESS) / np.log(max(before, 1e-3)))
    scale = np.power(np.maximum(lightness, 1e-4), gamma) / np.maximum(lightness, 1e-4)
    out = np.clip(rgb * scale[..., None], 0, 1)
    after = float(((out.max(axis=-1) + out.min(axis=-1)) / 2).mean())
    return Image.fromarray((out * 255 + .5).astype(np.uint8)), float(gamma), before, after


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True)
    parser.add_argument('--originals', required=True)
    args = parser.parse_args()
    project, originals = Path(args.project), Path(args.originals)
    textures = project / 'Assets/SkyIsland/Textures'
    data_dir = project / 'ArtSource/SkyIsland/tripo'
    rows = {}
    # 重跑时已升级的图集就是本工具上次的输出：previousSize 沿用清单里记下的升级前尺寸。
    shipped = json.loads(MANIFEST.read_text(encoding='utf-8'))['textures'] if MANIFEST.is_file() else {}
    for name, size in sorted(UPGRADE.items()):
        source = next(iter(sorted(originals.rglob(name + '.glb'))), None)
        if source is None:
            raise SystemExit('missing original GLB: ' + name)
        payload_path = data_dir / (name + '.json')
        payload = json.loads(payload_path.read_text(encoding='utf-8'))
        target = textures / payload['meta']['texture']['file']
        current = Image.open(target).convert('RGB')
        base = glb_base_color(source)
        match = correlation(base, current)
        if match < .9:
            raise SystemExit('%s: original base colour does not match the shipped atlas (r=%.3f)' % (name, match))
        side = min(size, max(base.size))
        resized = base.resize((side, side), Image.LANCZOS)
        graded, gamma, before, after = lift(resized)
        graded.save(target, optimize=True)
        payload['meta']['texture']['size'] = [side, side]
        payload['meta']['textureLimit'] = size
        payload_path.write_text(json.dumps(payload, ensure_ascii=False), encoding='utf-8')
        rows[name] = {'source': source.relative_to(originals).as_posix(), 'sourceSize': list(base.size),
                      'sourceSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                      'previousSize': shipped.get(name, {}).get('previousSize', list(current.size)), 'size': [side, side],
                      # 同理，相关系数记首次核对时（对原发布图集）的值；重跑时对比的是本工具自己的输出。
                      'atlasCorrelation': shipped.get(name, {}).get('atlasCorrelation', round(match, 4)),
                      'lightnessGamma': round(gamma, 4), 'meanLightness': [round(before, 4), round(after, 4)],
                      'sha256': hashlib.sha256(target.read_bytes()).hexdigest()}
        print('%-16s %s -> %d  r=%.3f  L %.3f -> %.3f' % (name, current.size, side, match, before, after), flush=True)
    MANIFEST.write_text(json.dumps({'schemaVersion': 1, 'textures': rows}, ensure_ascii=False, indent=1) + '\n',
                        encoding='utf-8')
    print('SKY_ISLAND_TRIPO_TEXTURES_OK', len(rows))


if __name__ == '__main__':
    main()
