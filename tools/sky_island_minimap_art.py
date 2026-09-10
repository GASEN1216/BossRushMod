"""天空岛官方地图的手绘底图：俯视参考图 → 带参考图生图 → 对齐回烘焙投影 → 调色。

地图的形状（alpha）永远来自 layout.json 的几何，由 tools/build_sky_island_minimap.py 烘焙；本工具只管「颜色」：
产物是 ArtSource/SkyIsland/Minimap/Source/sky_island_minimap_art.png（与烘焙投影逐像素对齐）和同名 .json
（投影、几何指纹、调色与对齐记录）。几何一变，烘焙脚本会拒绝旧底图，需要重走本流程。

用法（BossRushMod 根目录）：
    python tools/sky_island_minimap_art.py reference            # Blender 俯视渲染 → 纯色背景参考图
    python tools/sky_island_minimap_art.py generate             # 需要网络出口：export OPENAI_BASE_URL=... OPENAI_API_KEY=...（见 docs/AI生图API和密钥.md）
    python tools/sky_island_minimap_art.py align [--raw <生成图>] [--grade vanilla|none]
    python tools/build_sky_island_minimap.py                    # 按底图烘焙 13 张贴图并同步作者工程
中间产物（俯视渲染、参考图、原始生成图）放 ArtSource/SkyIsland/MinimapArt/，本地保留、不进 git。
生成器会自己添些树丛之类的装饰；路、建筑、农田、水面与桥的位置靠参考图和提示词锁住，出图后要人工过目。
"""
import argparse
import base64
import hashlib
import json
import os
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'tools'))
import build_sky_island_minimap as baker  # noqa: E402

WORK = ROOT / 'ArtSource/SkyIsland/MinimapArt'
SOURCE = baker.ART_IMAGE.parent
PROMPT = SOURCE / 'prompt.txt'
BLEND = Path('D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/ArtSource/SkyIsland/SkyIslandWorld.blend')
# 参考图与提示词约定的纯色背景 #2E3A44：生成图保持同色，对齐时才能按「离背景多远」认出陆地。
BACKGROUND = np.array((46, 58, 68), np.float32)
RENDER_SIZE = 2048


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def projection():
    layout = baker.load_layout()
    center_x, center_z, world = baker.world_bounds(layout)
    return layout, center_x, center_z, world


def land_mask(layout, center_x, center_z, world, size):
    """(烘焙图层同款 alpha, 岛轮廓填满后的陆地掩膜)，按 size 像素的投影画。"""
    previous = baker.TEXTURE_SIZE
    baker.TEXTURE_SIZE = size
    try:
        project, _unproject, _scale = baker.make_projector(center_x, center_z, world)
        layer = baker.draw_layer(layout, project, None, (baker.GROUND_FILL, baker.BRIDGE_FILL, baker.OUTLINE, True))
        alpha = np.array(layer.getchannel('A')) > 0
        filled = Image.new('L', (size, size), 0)
        draw = ImageDraw.Draw(filled)
        for island in layout['islands']:
            draw.polygon([project(p[0], p[1]) for p in island['outline']], fill=255)
        return alpha, (np.array(filled) > 0) | alpha
    finally:
        baker.TEXTURE_SIZE = previous


def iou(a, b):
    return float((a & b).sum()) / max(float((a | b).sum()), 1.0)


def vanilla_grade(rgb):
    """把生成图偏艳的黄绿与砂岩拉回原版色带（草地 H78 S27、铺装 H43 S42 L75，见 VANILLA_GRADE.md）；水面与背景不动。"""
    hls = cv2.cvtColor(rgb.clip(0, 255).astype(np.uint8), cv2.COLOR_RGB2HLS).astype(np.float32)
    hue, light, sat = hls[..., 0] * 2.0, hls[..., 1], hls[..., 2]
    green = np.clip((hue - 48) / 10, 0, 1) * np.clip((130 - hue) / 20, 0, 1)
    warm = np.clip((hue - 12) / 10, 0, 1) * np.clip((48 - hue) / 8, 0, 1)
    hue = (hue + 10 * green + 4 * warm) % 360
    sat = sat * (1 - 0.38 * green - 0.38 * warm)
    light = light * (1 - 0.05 * warm)
    graded = np.dstack([hue / 2.0, light, sat]).clip(0, [179, 255, 255]).astype(np.uint8)
    return cv2.cvtColor(graded, cv2.COLOR_HLS2RGB).astype(np.float32)


def cmd_reference(args):
    WORK.mkdir(parents=True, exist_ok=True)
    layout, center_x, center_z, world = projection()
    render = WORK / 'topdown_2048.png'
    command = [args.blender, '-b', '--factory-startup', str(args.blend), '--python-exit-code', '1',
               '--python', str(ROOT / 'tools/sky_island_minimap_render.py'), '--',
               '--center-x', repr(center_x), '--center-z', repr(center_z), '--world', repr(world),
               '--size', str(RENDER_SIZE), '--out', str(render)]
    completed = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', errors='replace')
    # Blender 后台默认吞掉 Python 异常照样退出 0，两道判据都要看。
    if completed.returncode != 0 or 'MINIMAP_TOPDOWN_OK' not in completed.stdout:
        sys.stdout.write(completed.stdout[-4000:] + completed.stderr[-4000:])
        raise SystemExit('Blender 俯视渲染失败')
    image = np.array(Image.open(render).convert('RGBA')).astype(np.float32)
    alpha, full = land_mask(layout, center_x, center_z, world, image.shape[0])
    coverage = float((image[..., 3][alpha] > 0).mean())
    if coverage < 0.995:
        raise SystemExit('俯视渲染没盖住地图几何（%.4f），机位或投影不对' % coverage)
    opacity = image[..., 3:4] / 255.0
    over = image[..., :3] * opacity + BACKGROUND * (1 - opacity)
    reference = np.where(full[..., None], over, BACKGROUND)
    out = WORK / 'reference_1024.png'
    Image.fromarray(reference.clip(0, 255).astype(np.uint8), 'RGB').resize(
        (baker.TEXTURE_SIZE, baker.TEXTURE_SIZE), Image.LANCZOS).save(out)
    print('reference %s (render covers %.4f of map geometry)' % (out, coverage))


def cmd_generate(args):
    from openai import OpenAI
    if not os.environ.get('OPENAI_API_KEY') or not os.environ.get('OPENAI_BASE_URL'):
        raise SystemExit('缺少 OPENAI_BASE_URL / OPENAI_API_KEY（见 docs/AI生图API和密钥.md）')
    client = OpenAI(timeout=1200, max_retries=0)
    prompt = PROMPT.read_text(encoding='utf-8')
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    for attempt in range(1, 4):
        started = time.time()
        try:
            with Path(args.reference).open('rb') as image:
                result = client.images.edit(model=args.model, image=image, prompt=prompt, size='1024x1024',
                                            quality='high', input_fidelity='high', n=1)
            item = result.data[0]
            if getattr(item, 'b64_json', None):
                out.write_bytes(base64.b64decode(item.b64_json))
            else:
                urllib.request.urlretrieve(item.url, str(out))
            print('generated %s in %.0fs' % (out, time.time() - started))
            return
        except Exception as error:  # noqa: BLE001 — 网关限流时是连接错误，重试即可
            print('attempt %d failed: %s: %s' % (attempt, type(error).__name__, str(error)[:400]))
            if attempt < 3:
                time.sleep(int(os.environ.get('ART_GEN_DELAY', '20')))
    raise SystemExit('生图失败')


def cmd_align(args):
    layout, center_x, center_z, world = projection()
    size = baker.TEXTURE_SIZE
    raw_path = Path(args.raw)
    raw = Image.open(raw_path).convert('RGB')
    raw_size = list(raw.size)
    if raw.size != (size, size):
        raw = raw.resize((size, size), Image.LANCZOS)
    painted = np.array(raw).astype(np.float32)
    _alpha, full = land_mask(layout, center_x, center_z, world, size)
    land = (np.linalg.norm(painted - BACKGROUND, axis=2) > 48).astype(np.uint8)
    land = cv2.morphologyEx(land, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    land = cv2.morphologyEx(land, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
    before = iou(land.astype(bool), full)
    warp = np.eye(2, 3, dtype=np.float32)
    criteria = (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 300, 1e-7)
    try:
        _cc, warp = cv2.findTransformECC(cv2.GaussianBlur(full.astype(np.float32), (0, 0), 3),
                                         cv2.GaussianBlur(land.astype(np.float32), (0, 0), 3),
                                         warp, cv2.MOTION_AFFINE, criteria, None, 5)
    except cv2.error as error:
        raise SystemExit('生成图对不齐地图几何（ECC 不收敛）：' + str(error).splitlines()[-1])
    aligned = cv2.warpAffine(painted, warp, (size, size), flags=cv2.INTER_LINEAR + cv2.WARP_INVERSE_MAP,
                             borderMode=cv2.BORDER_REPLICATE)
    land_aligned = cv2.warpAffine(land, warp, (size, size), flags=cv2.INTER_NEAREST + cv2.WARP_INVERSE_MAP).astype(bool)
    after = iou(land_aligned, full)
    if after < args.min_iou:
        raise SystemExit('对齐后陆地重合度 %.3f 低于 %.2f：生成图改了岛的形状或位置，换一张再来' % (after, args.min_iou))
    if args.grade == 'vanilla':
        aligned = vanilla_grade(aligned)
    SOURCE.mkdir(parents=True, exist_ok=True)
    art = baker.ART_IMAGE
    Image.fromarray(aligned.clip(0, 255).astype(np.uint8), 'RGB').save(art, optimize=True)
    record = {
        'textureSize': size,
        'imageWorldSize': round(world, 4),
        'mapWorldCenter': [round(center_x, 4), round(center_z, 4)],
        'geometrySha256': baker.geometry_digest(layout),
        'sha256': sha256(art),
        'model': args.model,
        'prompt': PROMPT.relative_to(ROOT).as_posix(),
        'rawSha256': sha256(raw_path),
        'rawSize': raw_size,
        'grade': args.grade,
        'alignmentIoU': [round(before, 4), round(after, 4)],
        'warp': np.round(warp, 5).tolist(),
    }
    art.with_suffix('.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('art %s  IoU %.4f -> %.4f  grade=%s' % (art, before, after, args.grade))


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    commands = parser.add_subparsers(dest='command', required=True)
    reference = commands.add_parser('reference')
    reference.add_argument('--blender', default='D:/blender/blender.exe')
    reference.add_argument('--blend', default=str(BLEND))
    reference.set_defaults(func=cmd_reference)
    generate = commands.add_parser('generate')
    generate.add_argument('--reference', default=str(WORK / 'reference_1024.png'))
    generate.add_argument('--out', default=str(WORK / 'generated.png'))
    generate.add_argument('--model', default='gpt-image-2')
    generate.set_defaults(func=cmd_generate)
    align = commands.add_parser('align')
    align.add_argument('--raw', default=str(WORK / 'generated.png'))
    align.add_argument('--grade', choices=('vanilla', 'none'), default='vanilla')
    align.add_argument('--min-iou', type=float, default=0.93)
    align.add_argument('--model', default='gpt-image-2')
    align.set_defaults(func=cmd_align)
    args = parser.parse_args()
    args.func(args)


if __name__ == '__main__':
    main()
