"""为「替换程序化低模」批次生成 Tripo3D 输入图。

与首批 26 件（sky_island_asset_images.py）的区别：这批是**替换**现有程序化几何，
因此每件都对应 layout 里已登记的 footprint，提示词必须写清长宽比——
镜水寺 43×23 米、工坊穹顶 32×28 米这种，若出成方正比例，按 footprint 缩放会明显变形。

风格常量直接复用首批，保证两批模型在同一画风里。

用法：
    set OPENAI_API_KEY / OPENAI_BASE_URL 后
    python tools/sky_island_replacement_images.py --out Build/skyisland-ref/replace
    python tools/sky_island_replacement_images.py --out <目录> --list
"""

import argparse
import os
from pathlib import Path
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
from sky_island_asset_images import IMAGEGEN, STYLE, WARM, NEGATIVE   # noqa: E402

# name: (face_limit, 单件面数预算, 实例数, 中文说明, 主体描述)
# face_limit 约为单件预算的 2 倍，留给 Blender 最后一道精确减面。
ITEMS = [
    # ── 建筑：按 layout 登记的 footprint 出图，比例写进提示词 ──────────────
    ('temple', 10000, 6000, 1, '镜水寺 43×23m 高12',
     'a long low temple hall, roughly twice as wide as it is deep, cream plaster walls, '
     'wide terracotta tiled hip roof with deep eaves, a colonnade of short stone pillars '
     'along the front, stone step platform'),
    ('workshop_dome', 10000, 6000, 1, '工坊穹顶 32×28m 高18',
     'a large round workshop building with a broad copper-green patina dome roof on a '
     'cream plaster drum, tall arched windows, stone base ring, brass trim'),
    ('mill', 8000, 4000, 1, '水车坊 16×12m 高13',
     'a tall narrow watermill building, warm weathered timber upper storey over a stone base, '
     'terracotta tiled roof, a large wooden water wheel on one side'),
    ('barn', 7000, 3500, 1, '谷仓 20×12m 高10',
     'a wide farm barn, roughly twice as wide as deep, warm weathered wood plank walls, '
     'terracotta tiled gable roof, large double doors, hay loft opening'),
    ('workshop_shed', 6000, 3000, 1, '工坊棚 20×14m 高8',
     'a wide low workshop shed, open timber frame front, terracotta tiled shed roof, '
     'stone half wall, workbenches and tools under the eaves'),
    ('dock_house', 6000, 3000, 1, '码头屋 18×12m 高8',
     'a wide low harbour house on a stone quay base, cream plaster walls, terracotta tiled roof, '
     'wide covered porch facing front, mooring posts and rope'),
    ('tea_house', 6000, 3000, 1, '茶铺 16×12m 高10',
     'a village tea house, cream plaster walls, terracotta tiled roof with upturned eaves, '
     'open serving counter along the front, cloth awning, hanging lanterns'),
    ('village_house_a', 6000, 3000, 2, '民居A 14×12m 高9',
     'a village dwelling wider than it is tall, cream plaster walls, terracotta tiled gable roof, '
     'wooden shutters, stone foundation course, window flower boxes'),
    ('village_house_b', 6000, 3000, 2, '民居B 16×12m 高8',
     'a low broad village dwelling, cream plaster walls with exposed timber framing, '
     'wide terracotta tiled hip roof, small covered entry porch, brick chimney'),
    ('village_house_c', 5000, 2500, 1, '民居C 10×8m 高7',
     'a compact village dwelling, cream plaster walls, steep terracotta tiled roof, '
     'one door and two shuttered windows, stone step'),
    ('pavilion_hall', 6000, 3000, 1, '亭堂 14×14m 高9',
     'a square open pavilion hall, eight stone columns, terracotta tiled pyramid roof, '
     'raised stone platform with steps on all sides, carved railing'),
    ('post_house', 5000, 2500, 1, '邮亭 12×10m 高8',
     'a small post house, cream plaster walls, terracotta tiled roof, a covered notice board '
     'beside the door, letter slot, lantern on a bracket'),

    # ── 地标与装置 ────────────────────────────────────────────────────
    ('homecoming_bell', 8000, 4000, 1, '归航钟',
     'a monumental bronze bell hanging in a heavy timber and stone frame, weathered patina, '
     'carved stone base platform, thick rope pull'),
    ('astrolabe', 8000, 4000, 1, '观星镜',
     'a large brass astrolabe instrument with nested rotating rings and a sighting tube, '
     'mounted on a carved stone pedestal, weathered verdigris'),
    ('wind_beacon', 7000, 3500, 1, '风标装置',
     'a tall wind beacon device, a brass weather vane and spinning cups on a timber mast, '
     'stone base, guide ropes, small signal lantern'),
    ('lookout_tower', 7000, 3500, 1, '瞭望台',
     'a small stone lookout platform on four stout pillars, timber railing, '
     'short stair up one side, terracotta tiled canopy'),
    ('duck_statue', 5000, 2500, 1, '鸭子雕像',
     'a carved stone statue of a plump duck on a square pedestal, weathered sandstone, '
     'simple rounded stylised forms, moss in the crevices'),
    ('cave_rock', 6000, 3000, 1, '洞窟岩',
     'a large weathered sandstone rock formation with a dark cave opening at its base, '
     'moss and hanging vines around the mouth'),
    ('wind_pillar', 5000, 2500, 2, '风柱',
     'a tall slender carved stone pillar with a brass ring finial at the top, '
     'weathered sandstone, spiral carving, small stone base'),
    ('memorial_stele', 5000, 2500, 2, '纪念碑',
     'an upright carved stone memorial stele with an inscribed panel, weathered sandstone, '
     'stepped base, a small offering ledge'),
    ('chime_rack', 5000, 2500, 2, '风铃架',
     'a timber frame rack hung with a row of small brass wind chimes, warm weathered wood posts, '
     'rope lashings, stone footings'),
    ('pavilion_pillar', 3000, 1200, 8, '亭柱',
     'a single carved stone pillar with a square capital and moulded base, weathered sandstone, '
     'climbing vine at the foot'),

    # ── 树（106 棵按 4 种变体复用）─────────────────────────────────────
    ('tree_green', 3000, 1500, 40, '常绿树',
     'a stylised broadleaf tree with a warm brown trunk and a rounded yellow-green canopy, '
     'visible root flare at the base, slightly asymmetric crown'),
    ('tree_gold', 3000, 1500, 24, '金叶树',
     'a stylised broadleaf tree with a warm brown trunk and a rounded canopy of golden amber '
     'autumn leaves, visible root flare, slightly asymmetric crown'),
    ('tree_blossom', 3000, 1500, 26, '花树',
     'a stylised blossom tree with a warm brown trunk and a rounded canopy of muted dusty-rose '
     'blossoms mixed with yellow-green leaves, visible root flare'),
    ('tree_olive', 3000, 1500, 16, '橄榄叶树',
     'a stylised tree with a warm brown gnarled trunk and a rounded canopy of deep olive-green '
     'narrow leaves, visible root flare, slightly windswept crown'),

    # ── 掩体 ─────────────────────────────────────────────────────────
    ('cover_crates', 1200, 600, 9, '货箱掩体',
     'a low barricade of stacked wooden supply crates and sandbags, warm weathered timber, '
     'roughly waist height, a folded tarp over one corner'),
    ('cover_stone', 1200, 600, 9, '石垒掩体',
     'a low barricade of stacked weathered sandstone blocks, roughly waist height, '
     'moss in the joints, one block fallen at the end'),
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', required=True)
    parser.add_argument('--only')
    parser.add_argument('--size', default='1024x1024')
    parser.add_argument('--gap', type=float, default=22.0)
    parser.add_argument('--retries', type=int, default=3)
    parser.add_argument('--list', action='store_true')
    args = parser.parse_args()

    wanted = set(args.only.split(',')) if args.only else None
    items = [i for i in ITEMS if wanted is None or i[0] in wanted]
    if args.list:
        total = 0
        for name, limit, budget, inst, label, _s in items:
            total += budget * inst
            print('%-20s face_limit=%-6d 预算=%-5d ×%-2d = %-6d  %s'
                  % (name, limit, budget, inst, budget * inst, label))
        print('共 %d 件，入场景合计 %d 面' % (len(items), total))
        return

    if not os.environ.get('OPENAI_API_KEY'):
        sys.exit('先设置 OPENAI_API_KEY / OPENAI_BASE_URL，见 docs/AI生图API和密钥.md')
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    done = failed = skipped = 0
    for index, (name, _limit, _budget, _inst, label, subject) in enumerate(items, 1):
        target = out / (name + '.png')
        if target.exists():
            print('[%d/%d] %-20s 已存在，跳过' % (index, len(items), name), flush=True)
            skipped += 1
            continue
        print('[%d/%d] %-20s %s' % (index, len(items), name, label), flush=True)
        prompt = STYLE + WARM + subject + '.'
        ok = False
        for attempt in range(1, args.retries + 1):
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
            print('    [FAIL] %s，稍后重跑本脚本可断点续传' % name, flush=True)
        if index < len(items):
            time.sleep(args.gap)

    print('SKY_ISLAND_REPLACEMENT_IMAGES done=%d skipped=%d failed=%d' % (done, skipped, failed))


if __name__ == '__main__':
    main()
