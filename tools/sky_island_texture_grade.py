"""把天空岛手绘贴图重新定级到原版鸭科夫的暖色区间。

原版 `resources.assets` 里 154 张 `_C` 反照率贴图的色相集中在 H18–40（石、木、墙、
屋顶全是暖琥珀），草地在 H78–89。天空岛原始屋顶贴图是 H156 的青绿鱼鳞瓦，与原版差距
最大；石/草/木三张色相接近但偏黄偏艳。四张统一在贴图层定级到原版区间后，
`generate_sky_island.py` 里各材质的 tint 才能都留在 1.0 附近，不必靠 >1 的 tint 拉色
而顶掉手绘高光。原图一律保留，定级结果另存新文件。

色相按「围绕均值压缩再平移」处理：保留每片瓦各自的色相扰动（窑变感），但把扰动幅度收窄
后整体挪到目标色相，避免直接整体旋转把个别偏蓝的瓦片转成紫色。明度通道原样保留，手绘的
笔触和高光不受影响。

用法：python tools/sky_island_texture_grade.py --project <作者工程>
"""

import argparse
import colorsys
from pathlib import Path

import numpy as np
from PIL import Image

# 目标区间全部取自原版实测：石 H28–35 S21–36、草 H78–89 S27–31、
# 木 H26–31 S42–48、屋顶 H18–28 S33–56。输出文件名必须含 `handpainted`，
# SkyIslandBundleBuilder 按这个子串把贴图导入模式设为 Repeat，否则平铺会出现接缝。
GRADES = {
    'roof_handpainted.png': {
        'output': 'roof_handpainted_terracotta.png',
        'hue': 24.0,          # 原版赤陶屋顶 T_Tile_Roof02_2_C 是 H28
        'spread': 0.45,       # 保留 45% 的原始色相扰动，维持每片瓦的窑变感
        'band': 8.0,          # 扰动后仍钳在 H16–32，防止偏黄绿的瓦绕环变成粉紫
        'saturation': 1.34,   # 原始 S41 → 约 S55，落进原版屋顶 S35–56
        'value': 1.06,        # 提亮一档，让各屋顶材质的 tint 都能留在 1.0 附近不顶高光
    },
    'stone_handpainted.png': {
        'output': 'stone_handpainted_vanilla.png',
        'hue': 33.0,          # 原始 H41 偏黄，原版砂岩在 H28–35
        'spread': 0.5,
        'band': 7.0,
        'saturation': 1.0,    # 原始 S26 已落在原版石材 S21–36 内
        'value': 1.0,         # 亮度差异交给各材质 tint，贴图保留全动态范围
    },
    'grass_handpainted.png': {
        'output': 'grass_handpainted_vanilla.png',
        'hue': 82.0,          # 原版草地 T_Tile_Grass_01_C H78 / _03_C H89
        'spread': 0.55,
        'band': 12.0,
        'saturation': 0.72,   # 原始 S41 → 约 S30，对上原版 S27–31
        'value': 0.97,
    },
    'wood_handpainted.png': {
        'output': 'wood_handpainted_vanilla.png',
        'hue': 30.0,          # 原版木材 T_Tile_WallWoodBlank01_2_C H31
        'spread': 0.5,
        'band': 10.0,
        'saturation': 0.80,   # 原始 S61 偏艳 → 约 S49，对上原版 S42–48
        'value': 1.0,
    },
}


def grade(image, hue_deg, spread, band, saturation, value):
    """围绕均值压缩色相扰动后平移到目标色相，明度按比例缩放。"""
    rgb = np.asarray(image.convert('RGB'), dtype=np.float32) / 255.0
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx, mn = rgb.max(axis=-1), rgb.min(axis=-1)
    span = mx - mn
    v = mx
    s = np.where(mx > 0, span / np.maximum(mx, 1e-6), 0.0)

    # 手写 RGB→H，避免 colorsys 逐像素调用。
    h = np.zeros_like(mx)
    safe = span > 1e-6
    idx = safe & (mx == r)
    h[idx] = ((g[idx] - b[idx]) / span[idx]) % 6
    idx = safe & (mx == g)
    h[idx] = (b[idx] - r[idx]) / span[idx] + 2
    idx = safe & (mx == b)
    h[idx] = (r[idx] - g[idx]) / span[idx] + 4
    h = h / 6.0

    # 只用有彩度的像素统计均值色相，灰像素的色相是噪声。
    weight = s * v
    mean_angle = np.arctan2(
        (np.sin(h * 2 * np.pi) * weight).sum(),
        (np.cos(h * 2 * np.pi) * weight).sum(),
    )
    mean_h = (mean_angle / (2 * np.pi)) % 1.0

    delta = (h - mean_h + 0.5) % 1.0 - 0.5           # 有符号的色相偏移，环绕安全
    delta = np.clip(delta * spread * 360.0, -band, band)
    h = ((hue_deg + delta) / 360.0) % 1.0
    s = np.clip(s * saturation, 0.0, 1.0)
    v = np.clip(v * value, 0.0, 1.0)

    # HSV→RGB，同样向量化。
    i = np.floor(h * 6.0)
    f = h * 6.0 - i
    p, q, t = v * (1 - s), v * (1 - f * s), v * (1 - (1 - f) * s)
    i = (i.astype(np.int32) % 6)[..., None]          # 条件需与 RGB 三通道同形
    out = np.select(
        [i == 0, i == 1, i == 2, i == 3, i == 4, i == 5],
        [np.stack([v, t, p], -1), np.stack([q, v, p], -1), np.stack([p, v, t], -1),
         np.stack([p, q, v], -1), np.stack([t, p, v], -1), np.stack([v, p, q], -1)],
    )
    return Image.fromarray(np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGB')


def describe(image):
    small = np.asarray(image.convert('RGB').resize((64, 64)), dtype=np.float32).reshape(-1, 3).mean(axis=0)
    h, l, s = colorsys.rgb_to_hls(*(small / 255.0))
    return '#%02X%02X%02X H%3.0f S%3.0f L%3.0f' % (
        int(small[0]), int(small[1]), int(small[2]), h * 360, s * 100, l * 100)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', required=True)
    args = parser.parse_args()
    textures = Path(args.project).resolve() / 'Assets' / 'SkyIsland' / 'Textures'
    if not textures.is_dir():
        raise SystemExit('未找到贴图目录：' + str(textures))
    for source_name, spec in GRADES.items():
        source = textures / source_name
        if not source.is_file():
            raise SystemExit('缺少源贴图：' + str(source))
        image = Image.open(source)
        result = grade(image, spec['hue'], spec['spread'], spec['band'], spec['saturation'], spec['value'])
        target = textures / spec['output']
        result.save(target)
        print('%s -> %s' % (source_name, spec['output']))
        print('    before %s' % describe(image))
        print('    after  %s' % describe(result))
    print('SKY_ISLAND_TEXTURE_GRADE_OK')


if __name__ == '__main__':
    main()
