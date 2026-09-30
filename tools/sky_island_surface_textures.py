"""天空岛材质的「颜色进贴图」：每个环境材质一张手绘平铺贴图，`_BaseColor` 全白（2026-09-30）。

原版鸭科夫的材质 `_BaseColor` 几乎全白，颜色与明暗笔触都在贴图里（`ArtSource/SkyIsland/VANILLA_GRADE.md`）。
天空岛原来有两类偏差：
* 调色板材质（树冠、灌木、花、黄铜、深木、彩漆……）根本没有贴图，Unity 侧 `_BaseMap` 是白图，
  一块面就是一块匀色——占全场 73% 的三角形读成纯色塑料（`Build/sky-material-audit-20260930`）。
* 平铺材质靠 `_BaseColor` 的 tint 调色（草 0.84、深岩 0.51、岩体蓝通道 1.26……）。

本工具分两步：
1. `generate`：用生图网关出几张**无缝、俯视、均匀光**的手绘底纹（树叶、花瓣、旧黄铜、彩漆木板、耕土），
   原图存 `Build/sky-surface-textures/raw/`，可断点续跑（网关限流：两张之间 ≥ 20 秒，每张约 1–4 分钟）。
2. `bake`：把底纹做成真正无缝（半周期错位 + 边缘权重混合），按目标颜色在**线性空间**定级
   （保留底纹的明度笔触与少量色相扰动），写进作者工程 `Assets/SkyIsland/Textures/<材质>_handpainted_mat.png`
   （文件名含 `handpainted`，导入器按 Repeat 平铺），并把来源、目标色与指纹写进
   `ArtSource/SkyIsland/surface_textures.json`。生成器读这份清单，把这些材质登记成 tint 全白的平铺贴图。

已有的石 / 草 / 木 / 瓦 / 岩体平铺贴图也走 `bake`：原 tint 在线性空间烘进各自一张贴图，tint 回到 1。

用法：
    python tools/sky_island_surface_textures.py generate            # 需要 OPENAI_API_KEY / OPENAI_BASE_URL
    python tools/sky_island_surface_textures.py bake --project <作者工程>
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / 'Build/sky-surface-textures/raw'
MANIFEST = ROOT / 'ArtSource/SkyIsland/surface_textures.json'
IMAGEGEN = os.path.join(os.path.expanduser('~'), '.codex', 'skills', '.system', 'imagegen', 'scripts', 'image_gen.py')
SIZE = 1024

STYLE = ('Seamless tileable square game texture, flat top-down orthographic view filling the whole frame edge to edge, '
         'EVEN SOFT DIFFUSE LIGHTING with no directional light, no cast shadows, no vignette, no perspective. '
         'Stylized hand-painted texture in the style of a warm cozy isometric game, visible painterly brush strokes, '
         'gentle value variation, matte surface, no gloss, no specular highlights. ')
NEGATIVE = ('perspective, horizon, objects, border, frame, vignette, strong shadow, glossy, specular, photorealistic, '
            'text, watermark, logo, UI, seam, tiling artifacts, pure black, pure white, neon, oversaturated')
BASES = {
    'foliage': 'Dense overlapping small rounded leaves of a shrub canopy, layered leaf clusters with lighter tips and '
               'slightly darker gaps between clusters, yellow-green olive foliage (hex 71853F).',
    'petals': 'Soft overlapping flower petals, rounded petal shapes with fine pale veins and gentle creases, '
              'light cream petals (hex DFCAA1) with slightly warmer centers.',
    'brass': 'Aged hand-hammered brass metal plate, warm golden brown (hex BD914D), subtle hammer dents and fine scratches, '
             'darker olive-brown patina settled in small grooves, lighter worn rubbed areas, no reflections.',
    'painted_wood': 'Weathered wooden planks with old chipped paint, muted blue-green paint (hex 487F78) worn through to '
                    'warm brown wood (hex 8A6845) along plank edges and knots, visible wood grain under the paint.',
    'soil': 'Freshly tilled farm soil, warm brown earth (hex 7A5C40) with small clods, crumbs and a few tiny pebbles, '
            'soft furrow texture without strong lines.',
}

# 材质 → (底纹来源, 平铺米数)。底纹来源是 BASES 的键，或已有手绘贴图的文件名（原 tint 烘进去）。
# 颜色目标取生成器 PALETTE（亮度上限见 CEILING），已有平铺材质取原贴图 × 原 tint。
PLAN = {
    'Leaf': ('foliage', 2.6), 'LeafLight': ('foliage', 2.6), 'Forest': ('foliage', 2.6), 'Fern': ('foliage', 2.2),
    'LeafGold': ('foliage', 2.6), 'Blossom': ('petals', 1.8), 'Flower': ('petals', 1.6), 'Lavender': ('petals', 1.6),
    'Coral': ('petals', 1.8), 'LilyWhite': ('petals', 1.6),
    'Brass': ('brass', 2.0), 'BrassLight': ('brass', 2.0), 'Copper': ('brass', 2.0),
    'WoodDark': ('wood_handpainted_vanilla.png', 4.0),
    'PaintTeal': ('painted_wood', 3.0), 'PaintTealLight': ('painted_wood', 3.0), 'PaintTealDeep': ('painted_wood', 3.0),
    'Soil': ('soil', 6.0),
    'Glow': ('petals', 1.5), 'StarGlow': ('petals', 1.5), 'PearlGlow': ('petals', 1.5),
    'CrystalLavender': ('stone_handpainted_vanilla.png', 2.0), 'CrystalTeal': ('stone_handpainted_vanilla.png', 2.0),
}
# 贴图边长：原有平铺材质（TILED_TEXTURES_SOURCE）一律 1024²，保持原 Production 图的纹素，不许降
# （复测：路面 Limestone 曾因 512² 从 128 降到 64 px/m，低于 1080p 一屏约 69 px/m 的需求）；
# 新增的调色板材质周期 1.5–4 m，512² 已有 128–340 px/m；周期更长的耕土给 1024²。
LARGE = {'Soil'}
# 发白的件压一档（审计：Ivory 迎光面 0.94、LilyWhite 0.99 近乎过曝）。线性亮度上限。
# 发光材质白天是「受光反照率 + 压暗后的自发光」，底色再压一档（复测：Glow 贴图 12.8% 的像素亮度超过 0.92）。
CEILING = {'Ivory': .62, 'LilyWhite': .62, 'Flower': .64, 'Glow': .5, 'StarGlow': .5, 'PearlGlow': .5}
# 深色材质的笔触对比按指数放大（线性空间按比例定级后，深色的绝对起伏太小，复测 WoodDark 亮度标准差只有 0.021）。
CONTRAST = {'WoodDark': 1.9}
# 色相扰动保留比例：植物、花瓣保留多一点手绘冷暖，金属与漆面少一点。
CHROMA_KEEP = {'foliage': .45, 'petals': .35, 'brass': .3, 'painted_wood': .6, 'soil': .4}


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(np.asarray(c, dtype=np.float64), 0, 1)
    return np.where(c <= .0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - .055)


def gamma_to_linear_ext(v):
    """Unity 线性色彩空间下 SetColor 的换算（>1 的分量也按同一曲线）。"""
    v = np.asarray(v, dtype=np.float64)
    return np.where(v <= 1, srgb_to_linear(np.clip(v, 0, 1)), np.power(v, 2.2))


def seamless(img):
    """半周期错位后按离边距离混合：输出四边与对边连续，底纹的中心区域原样保留。"""
    a = np.asarray(img, dtype=np.float64)
    h, w = a.shape[:2]
    rolled = np.roll(np.roll(a, h // 2, axis=0), w // 2, axis=1)
    y = np.minimum(np.arange(h), h - 1 - np.arange(h)) / (h / 2)
    x = np.minimum(np.arange(w), w - 1 - np.arange(w)) / (w / 2)
    weight = np.clip(np.minimum.outer(y, x) * 2.2, 0, 1) ** 1.5
    return a * weight[..., None] + rolled * (1 - weight[..., None])


def load_square(path):
    img = Image.open(path).convert('RGB')
    side = min(img.size)
    left, top = (img.width - side) // 2, (img.height - side) // 2
    return img.crop((left, top, left + side, top + side)).resize((SIZE, SIZE), Image.LANCZOS)


def grade(rgb_srgb, target_linear, chroma_keep, ceiling=None, contrast=1.0):
    """线性空间定级：明度按底纹相对均值的比例保留（contrast 为比例的指数），色相扰动按比例保留，整体均值落到目标色。"""
    lin = srgb_to_linear(rgb_srgb / 255.0)
    lum = lin @ np.array([.2126, .7152, .0722])
    mean_lum = max(lum.mean(), 1e-6)
    ratio = (lum / mean_lum) ** contrast
    ratio = (ratio / max(ratio.mean(), 1e-6))[..., None]
    chroma = lin / np.maximum(lum[..., None], 1e-6)
    chroma = chroma / np.maximum(chroma.reshape(-1, 3).mean(axis=0), 1e-6)
    tint = 1 + (chroma - 1) * chroma_keep
    target = np.asarray(target_linear, dtype=np.float64)
    if ceiling is not None:
        t_lum = float(target @ np.array([.2126, .7152, .0722]))
        if t_lum > ceiling:
            target = target * (ceiling / t_lum)
    out = target * ratio * tint
    out = out / np.maximum(out.reshape(-1, 3).mean(axis=0) / np.maximum(target, 1e-6), 1e-6)
    return (linear_to_srgb(out) * 255 + .5).astype(np.uint8)


def palette():
    """生成器的 PALETTE / TILED_TEXTURES（按源码字面解析，不导入 bpy）。"""
    import ast
    tree = ast.parse((ROOT / 'tools/generate_sky_island.py').read_text(encoding='utf-8'))
    values = {}
    for node in tree.body:
        if isinstance(node, ast.Assign) and len(node.targets) == 1 and getattr(node.targets[0], 'id', None) in (
                'PALETTE', 'TILED_TEXTURES_SOURCE'):
            values[node.targets[0].id] = ast.literal_eval(node.value)
    return values


def generate(args):
    if not os.environ.get('OPENAI_API_KEY'):
        sys.exit('先设置 OPENAI_API_KEY / OPENAI_BASE_URL，见 docs/AI生图API和密钥.md')
    sys.path.insert(0, str(ROOT / 'tools'))
    from imagegen_model import IMAGE_MODEL
    RAW.mkdir(parents=True, exist_ok=True)
    names = [n for n in BASES if not args.only or n in args.only.split(',')]
    for index, name in enumerate(names):
        target = RAW / (name + '.png')
        if target.exists():
            print('skip', name, flush=True)
            continue
        for attempt in range(1, 4):
            result = subprocess.run([sys.executable, IMAGEGEN, 'generate', '--model', IMAGE_MODEL, '--size', '1024x1024',
                                     '--n', '1', '--no-augment', '--out', str(target), '--prompt', STYLE + BASES[name],
                                     '--negative', NEGATIVE], capture_output=True, text=True, timeout=900)
            if result.returncode == 0 and target.exists():
                print('ok', name, flush=True)
                break
            print('retry', name, attempt, (result.stderr or result.stdout or '')[-160:].replace('\n', ' '), flush=True)
            time.sleep(10 * attempt)
        if index < len(names) - 1:
            time.sleep(args.gap)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def bake(args):
    project = Path(args.project)
    textures = project / 'Assets/SkyIsland/Textures'
    values = palette()
    colors = values['PALETTE']
    tiled = values['TILED_TEXTURES_SOURCE']
    rows = {}
    jobs = [(name, source, repeat, None) for name, (source, repeat) in PLAN.items()]
    # 已有平铺材质：原贴图 × 原 tint 烘进新图，tint 回到 1。
    jobs += [(name, file, repeat, tint) for name, (file, repeat, tint) in tiled.items()]
    for name, source, repeat, tint in jobs:
        out = textures / (name.lower() + '_handpainted_mat.png')
        if source in BASES:
            src = RAW / (source + '.png')
            base = np.asarray(Image.fromarray(seamless(load_square(src)).astype(np.uint8)), dtype=np.float64)
            code = colors[name]
            target = srgb_to_linear([int(code[i:i + 2], 16) / 255 for i in (1, 3, 5)])
            pixels = grade(base, target, CHROMA_KEEP[source], CEILING.get(name), CONTRAST.get(name, 1.0))
            source_path = src
        else:
            source_path = textures / source
            img = Image.open(source_path).convert('RGB').resize((SIZE, SIZE), Image.LANCZOS)
            base = np.asarray(img, dtype=np.float64)
            if tint is None:
                # 调色板材质借已有底纹：均值定级到调色板色。
                code = colors[name]
                target = srgb_to_linear([int(code[i:i + 2], 16) / 255 for i in (1, 3, 5)])
                pixels = grade(base, target, .5, CEILING.get(name), CONTRAST.get(name, 1.0))
            else:
                lin = srgb_to_linear(base / 255.0) * gamma_to_linear_ext(tint[:3])
                ceiling = CEILING.get(name)
                if ceiling is not None:
                    lum = float((lin.reshape(-1, 3).mean(axis=0)) @ np.array([.2126, .7152, .0722]))
                    if lum > ceiling:
                        lin = lin * (ceiling / lum)
                pixels = (linear_to_srgb(lin) * 255 + .5).astype(np.uint8)
        size = 1024 if tint is not None or name in LARGE else 512
        image = Image.fromarray(pixels)
        if image.size != (size, size):
            image = image.resize((size, size), Image.LANCZOS)
        image.save(out, optimize=True)
        pixels = np.asarray(image)
        mean = srgb_to_linear(pixels.reshape(-1, 3).mean(axis=0) / 255.0)
        rows[name] = {'file': out.name, 'repeatMetres': repeat, 'source': str(Path(source_path).name),
                      'sourceSha256': sha(source_path), 'sha256': sha(out), 'size': size,
                      'meanSrgb': '#%02x%02x%02x' % tuple(int(v) for v in pixels.reshape(-1, 3).mean(axis=0)),
                      'meanLinear': [round(float(v), 4) for v in mean]}
        print('baked', name, out.name, rows[name]['meanSrgb'], flush=True)
    MANIFEST.write_text(json.dumps({'schemaVersion': 1, 'policy': '_BaseColor white; colour lives in the texture',
                                    'textures': rows}, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print('SKY_ISLAND_SURFACE_TEXTURES_OK', len(rows))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='cmd', required=True)
    g = sub.add_parser('generate'); g.add_argument('--only'); g.add_argument('--gap', type=float, default=22.0)
    b = sub.add_parser('bake'); b.add_argument('--project', required=True)
    args = parser.parse_args()
    (generate if args.cmd == 'generate' else bake)(args)


if __name__ == '__main__':
    main()
