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


# ── 第二轮：程序化几何替换件（2026-09-10）──────────────────────────────
#
# 与第一轮（26 件地标/建筑）不同，这一批全是**高复用散件**：一件要摆几百份。
# 因此面数按**实例数**倒推，单件预算比第一轮小一个量级，见
# ArtSource/SkyIsland/LOWPOLY_REPLACEMENT_PLAN.md 的「第二轮清单」。
#
# 元组多一个 face_limit（Tripo quad 模式，1 quad ≈ 2 三角面）。出图按它分文件夹，
# 导入 Tripo 时一个文件夹一个批次，face_limit 在界面上只需设一次。
ITEMS_ROUND2 = [
    # ── 500：高复用小散件（一件摆几百份，单件三角面必须压到 ~500 以内）──
    # 自然件一律用 'nature' 配色（原因见 NATURE 常量上方）。标 ★ 的五件第一次出成了建筑，
    # 主体描述改成「具体名词 + 数量 + nothing else」，并由 NATURE_NEGATIVE 点名禁止建筑。
    ('cliff_chunk_a', 500, 'nature', '岛缘石块 A · 宽扁',
     'a single chunky floating rock boulder, wide and flat, warm tan sandstone with '
     'weathered flat facets and rounded worn edges, a little dry moss on top'),
    ('cliff_chunk_b', 500, 'nature', '岛缘石块 B · 细长柱',
     'a single tall narrow hanging rock column tapering to a blunt point at the bottom, '
     'warm tan sandstone, horizontal strata lines, chipped edges'),
    ('cliff_chunk_c', 500, 'nature', '岛缘石块 C · 锥形',
     'a single cone shaped hanging rock stalactite, broad at the top and pointed below, '
     'warm tan sandstone with layered strata and small cracks'),
    ('cliff_shrub_cap', 500, 'nature', '岛缘林冠 · 压在石块顶上的矮树丛',
     'a low wide cushion of dense yellow-green shrubbery sitting on a thin slab of warm tan '
     'rock, flat underside, rounded leafy top'),
    ('cliff_vine', 500, 'nature', '岛缘垂藤 ★',
     'a single long hanging ivy vine seen on its own, one thin twisting warm brown stem about '
     'ten times longer than it is wide, small yellow-green leaves along its whole length, '
     'hanging straight down from a fist-sized chunk of tan rock at the top, nothing else'),
    ('bush_a', 500, 'nature', '灌木丛 A · 圆润',
     'a single rounded garden shrub, dense yellow-green foliage in soft clumped masses, '
     'short warm brown stems visible at the base'),
    ('bush_b', 500, 'nature', '灌木丛 B · 扁平铺开',
     'a single low spreading shrub, wider than it is tall, layered yellow-green foliage '
     'clumps, a few thin warm brown twigs'),
    ('bush_c', 500, 'nature', '灌木丛 C · 带小花 ★',
     'a single rounded wild shrub growing directly from bare soil, dense yellow-green leaf '
     'clumps dotted with about a dozen tiny cream and dusty-rose flowers, short warm brown '
     'stems at the base, nothing else'),
    ('rock_a', 500, 'nature', '散落岩石 A ★',
     'a single weathered natural boulder lying on bare ground, rounded warm tan sandstone with '
     'a few flat facets and cracks, a patch of yellow-green moss on one side, nothing else'),
    ('rock_b', 500, 'nature', '散落岩石 B · 碎石堆',
     'a small pile of three or four angular warm tan stones resting together on the ground, '
     'gravel around the base'),
    ('flower_patch', 500, 'nature', '地被花丛 ★',
     'a single small clump of wild meadow flowers growing from a flat patch of grass turf, '
     'about ten slender yellow-green stems with small cream and dusty-rose daisy-like blooms, '
     'a few broad leaves at the base, nothing else'),
    ('lavender_clump', 500, 'nature', '薰衣草丛 ★',
     'a single wild lavender bush growing from a small mound of soil, about twenty upright '
     'slender stalks topped with muted dusty lavender flower spikes, narrow grey-green leaves '
     'at the base, nothing else'),
    ('fern_clump', 500, 'nature', '蕨类丛',
     'a small clump of arching fern fronds, deep yellow-green feathered leaves radiating '
     'from a single base, no flowers'),
    ('coral_clump', 500, True, '珊瑚状枝丛',
     'a small branching coral-like ornamental plant, chunky terracotta-orange branches '
     'radiating upward from a warm tan rock base'),

    # ── 1500：中等件（数量几十以内，可以给更多细节）──
    # 远景岛的三条提示词必须**明确禁止建筑**。第一次跑 a 和 c 时网关自作主张加了整栋房子、
    # 旗幡和石墙——它们是 600 米外的背景剪影（现状仅 632 面），加建筑既超预算又莫名其妙。
    # b 那条没跑偏，因为它本来就写死了「one small tree and a boulder」这种具体到数量的约束。
    ('distant_islet_a', 1500, True, '远景悬浮岛 A · 宽缓',
     'a small uninhabited floating sky island seen as a complete object, flat grassy top with '
     'yellow-green turf, warm tan rock underside tapering to a blunt point, '
     'exactly two small trees and one boulder on top and nothing else. '
     'Bare wild nature only: NO buildings, NO house, NO tower, NO walls, NO fence, NO path, '
     'NO banner, NO man-made structure of any kind'),
    ('distant_islet_b', 1500, True, '远景悬浮岛 B · 高瘦',
     'a small floating sky island, narrow and tall, grassy cap on top and a long tapering '
     'warm tan rock spire below, one small tree and a boulder on the cap'),
    ('distant_islet_c', 1500, True, '远景悬浮岛 C · 断裂双块',
     'a small uninhabited floating sky island split into two rock masses of different sizes '
     'floating close together, grassy tops with yellow-green turf, warm tan layered rock '
     'undersides tapering downward, one small tree on the larger mass and nothing else. '
     'Bare wild nature only: NO buildings, NO house, NO tower, NO walls, NO fence, NO bridge, '
     'NO path, NO banner, NO man-made structure of any kind'),
    ('mushroom_cluster', 1500, True, '蘑菇群',
     'a cluster of five stylized mushrooms of different heights, cream stems and rounded '
     'terracotta caps with pale spots, growing from a small mossy warm tan base'),
    ('glow_crystal', 1500, False, '发光水晶（保留冷色）',
     'a small cluster of translucent pale cyan crystal shards rising from a warm tan rock '
     'base, faint inner glow, clean faceted geometry'),
    ('brass_lamp_post', 1500, True, '铜灯柱',
     'a single ornate street lamp post, slender weathered brass column with a decorative ring, '
     'a glass lantern head with warm cream glow, small square stone footing'),
    ('brass_railing_module', 1500, True, '铜栏杆模块 · 可平铺一段',
     'a single straight section of ornate railing fence, weathered brass posts and a top rail '
     'with simple scroll ornament between them, flat ends so sections can repeat'),

    # ── 4000：少量大件 ──
    ('waterfall', 4000, False, '云瀑（保留冷色）',
     'a tall narrow waterfall falling from a rock lip into a puff of white cloud at the '
     'bottom, pale blue-green water, warm tan rock at the top, spray and mist'),
]


# 第二轮自然散件的配色约束。**不能沿用 WARM**：WARM 点名的 terracotta clay / cream plaster /
# weathered warm wood 恰好是「地中海小屋」的全部建材。第一次跑时 flower_patch 出成了石屋、
# lavender_clump 与 rock_a 出成了水井、cliff_vine 出成了石拱门、bush_c 出成了带招牌的民居——
# 全是主体描述短而抽象（花丛、藤蔓、小石块）的件被配色词带跑；有具体名词的蕨类、珊瑚、
# 锥形石柱都没跑偏。所以自然件只给自然材质的色值，并在负面词里点名禁止建筑。
NATURE = ('Natural materials only. Colors limited to warm tan sandstone (hex A98A6F), '
          'yellow-green foliage (hex 7F9154), deep olive green (hex 5E6E3A), warm brown bark '
          '(hex 7A5C40), and small accents of cream, dusty rose and muted lavender (hex B3A6C4) '
          'petals. Absolutely no teal, no turquoise, no pastel candy colors. ')
NATURE_NEGATIVE = (', building, house, cottage, hut, tower, well, arch, ruin, roof, roof tiles, '
                   'wall, brick, plaster, door, window, fence, sign, banner, pot, man-made structure')


def build_prompt(palette, subject):
    """palette：True=WARM（第一轮建筑/地标与铜件），'nature'=NATURE（第二轮自然散件），False=不加。"""
    extra = WARM if palette is True else NATURE if palette == 'nature' else ''
    return STYLE + extra + subject + '.'


def negative_for(palette):
    return NEGATIVE + (NATURE_NEGATIVE if palette == 'nature' else '')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', required=True)
    parser.add_argument('--only', help='逗号分隔的名字，只生成这些')
    parser.add_argument('--size', default='1024x1024')
    parser.add_argument('--gap', type=float, default=22.0, help='两张之间的间隔秒数，网关限流要求 >=20')
    parser.add_argument('--retries', type=int, default=3)
    parser.add_argument('--list', action='store_true', help='只打印清单不生成')
    parser.add_argument('--set', dest='item_set', default='round2',
                        choices=['round1', 'round2', 'all'],
                        help='round1=第一轮 26 件地标/建筑（已完成）；round2=第二轮程序化替换件')
    args = parser.parse_args()

    # 第一轮没有分档字段，统一按 500 归档（它们本来就是逐件在 TRIPO_SETTINGS.md 里单独列 face_limit 的）。
    pool = []
    if args.item_set in ('round1', 'all'):
        pool += [(n, 0, w, l, s) for n, w, l, s in ITEMS]
    if args.item_set in ('round2', 'all'):
        pool += ITEMS_ROUND2

    wanted = set(args.only.split(',')) if args.only else None
    items = [i for i in pool if wanted is None or i[0] in wanted]
    if args.list:
        tiers = {}
        for name, tier, warm, label, _s in items:
            tiers.setdefault(tier, []).append((name, label, warm))
        for tier in sorted(tiers):
            head = ('face_%d' % tier) if tier else '第一轮（face_limit 见 TRIPO_SETTINGS.md）'
            print('== %s  共 %d 件' % (head, len(tiers[tier])))
            for name, label, warm in tiers[tier]:
                print('   %-22s %-26s %s' % (name, label, '暖色' if warm else '保留冷色'))
        print('合计 %d 件' % len(items))
        return

    if not os.environ.get('OPENAI_API_KEY'):
        sys.exit('先设置 OPENAI_API_KEY / OPENAI_BASE_URL，见 docs/AI生图API和密钥.md')
    out = Path(args.out); out.mkdir(parents=True, exist_ok=True)

    done = failed = skipped = 0
    for index, (name, tier, warm, label, subject) in enumerate(items, 1):
        # 按 face_limit 分文件夹：Tripo 的批量导入是一批一个 face_limit，
        # 分好档就能一个文件夹拖一次、参数只设一次，不用逐件调。
        folder = out / ('face_%d' % tier) if tier else out
        folder.mkdir(parents=True, exist_ok=True)
        target = folder / (name + '.png')
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
                 '--prompt', prompt, '--negative', negative_for(warm)],
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
