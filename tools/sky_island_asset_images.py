"""为天空岛逐件生成 Tripo3D Image-to-3D 的输入图。

与场景概念图分开：概念图追求强光深影好看，但那种阴影会被 Tripo3D 烘进 albedo，
出模后拆不掉。因此素材图一律**均匀柔光、纯净浅灰背景、单体居中、四分之三视角**，
这是 image-to-3D 出干净模型的前提。

配色锁死在原版鸭科夫实测区间（见 ArtSource/SkyIsland/VANILLA_GRADE.md）：赤陶 A4653F、
暖砂岩 A98A6F、黄绿草 7F9154、暖木 BF9364、奶油墙 D9C9A4。只有水晶与符文保留冷色，
它们对应调色板里刻意留下的 CrystalTeal / StarGlow 点缀。

网关限流：每张约 3-4 分钟，两张之间至少间隔 20 秒。本脚本可断点续跑，已存在的文件跳过。

用法：
    set OPENAI_API_KEY / OPENAI_BASE_URL 后
    python tools/sky_island_asset_images.py --out Build/skyisland-ref/assets
    python tools/sky_island_asset_images.py --out <目录> --only bell_arch,great_tree
    python tools/sky_island_asset_images.py --out <目录> --list
"""

import argparse
import os
from pathlib import Path
import subprocess
import sys
import time

IMAGEGEN = os.path.join(os.path.expanduser('~'), '.codex', 'skills', '.system',
                        'imagegen', 'scripts', 'image_gen.py')

# 单体素材图的共用要求。放在每条提示词前面，保证 26 件风格一致。
STYLE = ('Single isolated 3D game asset centered on a plain flat light grey background, '
         'three-quarter view, full object visible with clear readable silhouette. '
         'Stylized chunky low-poly geometry with soft bevels, hand-painted matte texture, '
         'no gloss and no reflections. EVEN SOFT STUDIO LIGHTING from all sides, '
         'no strong directional shadow, no cast shadow on the background. '
         'Muted low-saturation warm earthy palette. ')

# 暖色件的共用配色约束。水晶/符文两件不加这条。
WARM = ('Colors limited to terracotta clay (hex A4653F), warm tan sandstone (hex A98A6F), '
        'weathered warm wood (hex BF9364), cream plaster (hex D9C9A4), yellow-green foliage '
        '(hex 7F9154). Absolutely no teal, no turquoise, no pastel candy colors. ')

NEGATIVE = ('scene, environment, background clutter, multiple objects, cropped, cut off, '
            'strong cast shadow, dramatic lighting, teal roof, turquoise, pastel, neon, '
            'oversaturated, glossy, photorealistic, text, watermark, UI, human, character')

# name: (是否暖色约束, 中文说明, 主体描述)
ITEMS = [
    ('bell_arch', True, '悬钟石拱门',
     'a weathered stone ruin archway with a large brass bell hanging from its centre, '
     'carved stone blocks, two thick columns, cracked capstone, small vines'),
    ('great_tree', True, '巨树带板根',
     'a huge ancient broadleaf tree with dramatic exposed buttress roots spreading over rock, '
     'thick gnarled warm-brown trunk, dense rounded yellow-green canopy'),
    ('crystal_fountain', True, '悬浮水晶喷泉',
     'a round carved stone fountain basin with a small glowing pale cyan crystal floating '
     'above its centre, water spouts, sandstone rim, moss at the base'),
    ('bell_tower', True, '青瓦钟塔改暖瓦钟塔',
     'a slender village bell tower, cream plaster walls, terracotta tiled pyramid roof, '
     'open belfry with a small brass bell, wooden shutters, stone base'),
    ('cherry_pavilion', True, '石亭',
     'a small open stone pavilion gazebo with four carved columns and a terracotta tiled roof, '
     'stepped stone platform base, simple railing'),
    ('shrine_pavilion', True, '小型亭',
     'a very small roadside shrine pavilion, single terracotta tiled roof on four short wooden '
     'posts, stone offering slab underneath'),
    ('cottage_dormer', True, '民居A 带老虎窗',
     'a small cottage house with cream plaster walls, terracotta tiled gable roof with one '
     'dormer window, wooden door and window frames, stone foundation, window flower box'),
    ('cottage_large', True, '民居B 较大',
     'a two-storey cottage with cream plaster walls, terracotta tiled hipped roof, brick chimney, '
     'wooden balcony, shuttered windows'),
    ('cottage_small', True, '民居C 小屋',
     'a tiny one-room cottage with cream plaster walls and a steep terracotta tiled roof, '
     'one wooden door, one small window, stone step'),
    ('farm_barn', True, '谷仓农舍',
     'a rustic farm barn with warm weathered wood plank walls, terracotta tiled roof, '
     'large double doors, hay loft opening above'),
    ('shop_sign', True, '店铺带招牌',
     'a small village shop front, cream plaster wall, terracotta roof, wooden counter window, '
     'a hanging carved wooden sign board on an iron bracket'),
    ('market_stall', True, '条纹遮阳市集摊',
     'a market stall with a wooden counter and four posts holding a striped cloth awning in '
     'muted cream and warm red, crates of vegetables on the counter'),
    ('street_lamp', True, '雕花路灯',
     'an ornate cast iron street lamp post with a warm amber glass lantern head, '
     'decorative scroll brackets, small stone base'),
    ('banner_pole', True, '旗帜立柱',
     'a tall wooden banner pole with a long hanging cloth banner in muted cream and warm ochre, '
     'rope ties, carved stone footing'),
    ('pergola', True, '木藤架',
     'a wooden garden pergola trellis of warm weathered timber beams with climbing green vines '
     'and hanging leaves, four posts'),
    ('planter', True, '花箱种植槽',
     'a rectangular wooden planter box of warm weathered timber, filled with soil and small '
     'green leafy plants and a few muted flowers'),
    ('crate_barrel', True, '木桶板条箱堆',
     'a small stack of wooden supply crates and one wooden barrel with iron hoops, '
     'warm weathered timber, a folded sack on top'),
    ('farm_cart', True, '农用推车',
     'a simple two-wheel wooden farm handcart of warm weathered timber, iron rimmed wheels, '
     'loaded with sacks and vegetables'),
    ('stone_bench', True, '石凳护栏',
     'a curved carved sandstone bench with a low decorative stone railing behind it, '
     'weathered edges, small moss patches'),
    ('stone_stair', True, '石阶梯',
     'a short flight of weathered sandstone steps with low side walls and carved edging, '
     'freestanding module'),
    ('stone_platform', True, '圆形石台带阶',
     'a circular sandstone platform dais with radial paving pattern and three shallow steps '
     'around its rim, weathered and cracked'),
    ('ruined_column', True, '断裂遗迹柱',
     'a broken weathered sandstone column, fluted shaft snapped near the top, '
     'fallen capital fragment at its base, small vines'),
    ('root_rock', True, '缠根岩体',
     'a chunk of warm tan sandstone rock wrapped and gripped by thick tree roots, '
     'moss and small grass tufts on top'),
    ('cherry_tree', True, '花树',
     'a medium blossom tree with a warm brown trunk and a rounded canopy of small muted '
     'dusty-rose blossoms mixed with yellow-green leaves'),
    # 以下两件保留冷色，对应调色板里刻意留下的 CrystalTeal / StarGlow 点缀。
    ('crystal_cluster', False, '青水晶簇（保留冷色）',
     'a cluster of tall pointed translucent pale cyan crystals growing out of a small '
     'warm tan rock base, faint inner glow, clean faceted geometry'),
    ('rune_stone', False, '发光符文石（保留冷色）',
     'an upright weathered standing stone carved with glowing pale cyan runes, '
     'warm tan sandstone body, moss at the base'),
]


def build_prompt(warm, subject):
    return STYLE + (WARM if warm else '') + subject + '.'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', required=True)
    parser.add_argument('--only', help='逗号分隔的名字，只生成这些')
    parser.add_argument('--size', default='1024x1024')
    parser.add_argument('--gap', type=float, default=22.0, help='两张之间的间隔秒数，网关限流要求 >=20')
    parser.add_argument('--retries', type=int, default=3)
    parser.add_argument('--list', action='store_true', help='只打印清单不生成')
    args = parser.parse_args()

    wanted = set(args.only.split(',')) if args.only else None
    items = [i for i in ITEMS if wanted is None or i[0] in wanted]
    if args.list:
        for name, warm, label, _s in items:
            print('%-18s %-22s %s' % (name, label, '暖色' if warm else '保留冷色'))
        print('共 %d 件' % len(items))
        return

    if not os.environ.get('OPENAI_API_KEY'):
        sys.exit('先设置 OPENAI_API_KEY / OPENAI_BASE_URL，见 docs/AI生图API和密钥.md')
    out = Path(args.out); out.mkdir(parents=True, exist_ok=True)

    done = failed = skipped = 0
    for index, (name, warm, label, subject) in enumerate(items, 1):
        target = out / (name + '.png')
        if target.exists():
            print('[%d/%d] %-18s 已存在，跳过' % (index, len(items), name), flush=True)
            skipped += 1
            continue
        print('[%d/%d] %-18s %s' % (index, len(items), name, label), flush=True)
        prompt = build_prompt(warm, subject)
        ok = False
        for attempt in range(1, args.retries + 1):
            # 网关会间歇抛 APIConnectionError，单次失败不代表这张出不来，指数退避重试。
            result = subprocess.run(
                [sys.executable, IMAGEGEN, 'generate', '--model', 'gpt-image-2',
                 '--size', args.size, '--n', '1', '--no-augment', '--out', str(target),
                 '--prompt', prompt, '--negative', NEGATIVE],
                capture_output=True, text=True, timeout=600)
            if result.returncode == 0 and target.exists():
                ok = True
                break
            tail = (result.stderr or result.stdout or '')[-140:].replace('\n', ' ')
            print('    重试 %d/%d: %s' % (attempt, args.retries, tail), flush=True)
            time.sleep(8 * attempt)
        if ok:
            done += 1
        else:
            failed += 1
            print('    [FAIL] %s 生成失败，稍后可重跑本脚本补齐' % name, flush=True)
        if index < len(items):
            time.sleep(args.gap)

    print('SKY_ISLAND_ASSET_IMAGES done=%d skipped=%d failed=%d' % (done, skipped, failed))


if __name__ == '__main__':
    main()
