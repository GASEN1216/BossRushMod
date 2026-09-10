"""从概念参考图切出逐件建模用的图，供 Tripo3D 的 Image-to-3D 使用。

参考图是粉彩奇幻风（青瓦、白岩），天空岛已按原版鸭科夫定级为暖色（赤陶、暖砂岩）。
owner 定的方向是「只学密度和氛围，保留原版色」，因此切图在上传前先做**局部**暖化：
只把青绿色相带 H130-220 转向赤陶 H25，绿草（H80-110）和暖石原样保留。全局色相旋转会
把草地一起转成粉紫，实测过，不能用。

水晶、符文这类冷色点缀整张不暖化——它们与调色板里保留的 CrystalTeal / StarGlow /
PaintTeal 同属刻意保留的冷色点缀。

用法：
    python tools/sky_island_reference_crops.py --image <参考图> --out <输出目录>
    python tools/sky_island_reference_crops.py --image <参考图> --out <目录> --only bell_arch
"""

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image

# 目标赤陶色相，与 sky_island_texture_grade.py 的屋顶定级一致。
TERRACOTTA_HUE = 24.0
TEAL_BAND = (130.0, 220.0)      # 只有落在这个色相带内的像素才被暖化

# name: (归一化裁剪框 x0,y0,x1,y1, 是否暖化, 中文说明)
# 裁剪框按 1664x936 的参考图目测标定，--preview 会输出带框的总览图供校对。
ITEMS = {
    'crystal_fountain':  ((.455, .310, .580, .470), True,  '悬浮水晶喷泉（水晶部分生成后单独处理）'),
    'bell_tower':        ((.515, .155, .605, .330), True,  '村庄青瓦钟塔'),
    'cottage_dormer':    ((.395, .245, .500, .360), True,  '民居A 带老虎窗'),
    'cottage_large':     ((.585, .285, .690, .390), True,  '民居B 较大'),
    'cottage_small':     ((.355, .300, .430, .375), True,  '民居C 小屋'),
    'shop_sign':         ((.560, .330, .650, .420), True,  '店铺带招牌'),
    'market_stall':      ((.545, .395, .625, .470), True,  '条纹遮阳市集摊'),
    'street_lamp':       ((.470, .380, .510, .470), True,  '雕花路灯'),
    'banner_pole':       ((.415, .560, .470, .690), True,  '旗帜立柱'),
    'stone_stair':       ((.470, .420, .560, .520), True,  '石阶梯'),
    'planter':           ((.605, .400, .665, .450), True,  '花箱种植槽'),
    'crate_barrel':      ((.375, .390, .440, .445), True,  '木桶板条箱堆'),
    'bell_arch':         ((.665, .020, .800, .180), True,  '悬钟石拱门（遗迹岛主角）'),
    'crystal_cluster':   ((.775, .075, .860, .190), False, '青水晶簇（保留冷色）'),
    'ruined_column':     ((.660, .095, .720, .200), True,  '断裂遗迹柱'),
    'stone_platform':    ((.680, .130, .820, .230), True,  '圆形石台带阶'),
    'shrine_pavilion':   ((.635, .105, .685, .190), True,  '小型青瓦亭'),
    'great_tree':        ((.800, .400, .965, .620), True,  '巨树带板根（最难代码化）'),
    'rune_stone':        ((.885, .530, .945, .600), False, '发光符文石（保留冷色）'),
    'root_rock':         ((.815, .530, .900, .640), True,  '缠根岩体'),
    'cherry_pavilion':   ((.150, .075, .245, .190), True,  '白石凉亭'),
    'cherry_tree':       ((.100, .020, .230, .150), True,  '樱花树'),
    'stone_bench':       ((.185, .150, .250, .205), True,  '石凳护栏'),
    'farm_barn':         ((.030, .390, .110, .480), True,  '谷仓农舍'),
    'pergola':           ((.100, .400, .190, .470), True,  '木藤架'),
    'farm_cart':         ((.105, .530, .190, .600), True,  '农用推车'),
}


def warm_teal(image):
    """只把青绿色相带转向赤陶，其余像素原样返回。"""
    rgb = np.asarray(image.convert('RGB'), dtype=np.float32) / 255.0
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx, mn = rgb.max(axis=-1), rgb.min(axis=-1)
    span = mx - mn
    v = mx
    s = np.where(mx > 0, span / np.maximum(mx, 1e-6), 0.0)

    h = np.zeros_like(mx)
    safe = span > 1e-6
    idx = safe & (mx == r); h[idx] = ((g[idx] - b[idx]) / span[idx]) % 6
    idx = safe & (mx == g); h[idx] = (b[idx] - r[idx]) / span[idx] + 2
    idx = safe & (mx == b); h[idx] = (r[idx] - g[idx]) / span[idx] + 4
    hue = h * 60.0

    # 只动落在青绿带、且有一定彩度的像素；灰白墙面和高光不受影响。
    low, high = TEAL_BAND
    mask = (hue >= low) & (hue <= high) & (s > 0.12)
    centre = (low + high) / 2.0
    # 带内偏移压缩到 ±10 度后平移到赤陶，保留瓦片之间的色相层次。
    hue = np.where(mask, (TERRACOTTA_HUE + np.clip((hue - centre) * 0.22, -10, 10)) % 360.0, hue)
    s = np.where(mask, np.clip(s * 1.08, 0, 1), s)

    h = hue / 60.0
    i = np.floor(h); f = h - i
    p, q, t = v * (1 - s), v * (1 - f * s), v * (1 - (1 - f) * s)
    i = (i.astype(np.int32) % 6)[..., None]
    out = np.select(
        [i == 0, i == 1, i == 2, i == 3, i == 4, i == 5],
        [np.stack([v, t, p], -1), np.stack([q, v, p], -1), np.stack([p, v, t], -1),
         np.stack([p, q, v], -1), np.stack([t, p, v], -1), np.stack([v, p, q], -1)])
    return Image.fromarray(np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGB')


def crop(image, box, pad=0.06):
    """按归一化框裁剪，四周留出 pad 比例的余量让物件不贴边。"""
    w, h = image.size
    x0, y0, x1, y1 = box
    dx, dy = (x1 - x0) * pad, (y1 - y0) * pad
    return image.crop((max(0, int((x0 - dx) * w)), max(0, int((y0 - dy) * h)),
                       min(w, int((x1 + dx) * w)), min(h, int((y1 + dy) * h))))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--image', required=True)
    parser.add_argument('--out', required=True)
    parser.add_argument('--only', help='只导出这一件')
    parser.add_argument('--preview', action='store_true', help='额外输出带框总览，用来校对裁剪位置')
    parser.add_argument('--min-side', type=int, default=768, help='短边不足则放大，Tripo3D 对小图效果差')
    args = parser.parse_args()

    source = Image.open(args.image).convert('RGB')
    out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
    items = {args.only: ITEMS[args.only]} if args.only else ITEMS

    manifest = []
    for name, (box, warm, label) in items.items():
        piece = crop(source, box)
        if min(piece.size) < args.min_side:
            scale = args.min_side / min(piece.size)
            piece = piece.resize((int(piece.width * scale), int(piece.height * scale)), Image.LANCZOS)
        if warm:
            piece = warm_teal(piece)
        path = out / (name + '.png')
        piece.save(path)
        manifest.append({'name': name, 'label': label, 'warm': warm,
                         'box': box, 'size': piece.size, 'file': path.name})
        print('%-18s %-28s %s  %dx%d' % (name, label, '暖化' if warm else '保留冷色', *piece.size))

    if args.preview:
        from PIL import ImageDraw
        overview = source.copy(); draw = ImageDraw.Draw(overview)
        w, h = overview.size
        for name, (box, warm, _label) in items.items():
            x0, y0, x1, y1 = box
            draw.rectangle([x0 * w, y0 * h, x1 * w, y1 * h],
                           outline=(255, 90, 40) if warm else (60, 200, 255), width=3)
            draw.text((x0 * w + 4, y0 * h + 4), name, fill=(255, 255, 255))
        overview.save(out / '_overview.png')
        print('校对图: ' + str(out / '_overview.png'))

    (out / 'manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding='utf-8')
    print('SKY_ISLAND_CROPS_OK %d 件' % len(manifest))


if __name__ == '__main__':
    main()
