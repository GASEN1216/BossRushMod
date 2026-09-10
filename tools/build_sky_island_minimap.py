"""烘焙天空岛官方小地图贴图（含分区「未探索」图层）。

官方地图系统（`Duckov.MiniMaps.MiniMapSettings`）是纯场景数据驱动的：`maps` 是一个
`MapEntry` 列表，每条带 `sprite` / `imageWorldSize` / `offsetReference` / `hide`。
`MiniMapDisplay.Setup` 里 `if (cur.Hide) showGraphics = false;`，而 `MiniMapView.OnOpen()`
每次开图都会重跑 `AutoSetup()` —— 于是**分区迷雾可以纯数据实现**：

- 一张**底图**（全岛暗灰剪影，始终显示）给出未探索区域的轮廓；
- 12 张**分区彩色图层**（每个岛一张，裁到自己的方形包围盒），
  运行时按存档里的 `visitedRegions` 逐个翻 `hide`，去过的才上色。

不需要运行时重绘贴图，也不需要可读纹理；官方的缩放、拖动、手动标点、指北针全部照旧。

桥面同时算进**两端**岛屿的图层，任一端去过就会跟着上色，避免出现「两头亮着、中间断开」。

**手绘底图**：`Minimap/Source/sky_island_minimap_art.png` 存在时，形状（alpha）照旧按几何画，颜色改取这张与投影
逐像素对齐的手绘图（由 `tools/sky_island_minimap_art.py` 生成）；未探索底图取它去色压暗的版本。
底图的同名 .json 记着投影与几何指纹，布局几何一变就拒绝烘焙——重新生成底图，或加 `--flat` 烘纯色版。

用法：
    python tools/build_sky_island_minimap.py [--unity-project <路径>] [--flat]

产物：
    ArtSource/SkyIsland/Minimap/sky_island_minimap.png          底图（暗灰）
    ArtSource/SkyIsland/Minimap/sky_island_minimap_<区域>.png   12 张分区彩色图层
    ArtSource/SkyIsland/Validation/sky_island_minimap.json      尺寸/世界范围/每层偏移/sha256
"""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageOps, ImageStat

ROOT = Path(__file__).resolve().parent.parent
LAYOUT = ROOT / 'ArtSource/SkyIsland/layout.json'
OUT_DIR = ROOT / 'ArtSource/SkyIsland/Minimap'
OUT_JSON = ROOT / 'ArtSource/SkyIsland/Validation/sky_island_minimap.json'
# 手绘底图放子目录：部署到作者工程时只复制 Minimap 根目录下的 sky_island_minimap*.png，它不会被当成图层带进包。
ART_IMAGE = OUT_DIR / 'Source/sky_island_minimap_art.png'

BASE_NAME = 'sky_island_minimap'
# 纹理边长（像素）与地图覆盖的世界边长（米）。官方 `MiniMapDisplayEntry.Setup` 用
# `Vector2.one * sprite.texture.width * PixelSize` 定尺寸——**只取 width**，
# 所以每张图都必须是正方形，每条 MapEntry 的 imageWorldSize 也要按自己那张的边长算。
TEXTURE_SIZE = 1024
WORLD_MARGIN = 20.0
# 分区图层的最小边长，避免支路小岛裁出十几像素的贴图。
MIN_REGION_TEXTURE = 128

# 配色对齐原版鸭科夫的暖琥珀基调（见 ArtSource/SkyIsland/VANILLA_GRADE.md）。
GROUND_FILL = (217, 201, 164, 255)
BRIDGE_FILL = (196, 170, 128, 255)
OBSTACLE_FILL = (169, 138, 111, 255)
OUTLINE = (109, 88, 68, 255)
# 底图是「还没去过」的样子：同一套形状压暗去饱和，只留轮廓感。
BASE_GROUND = (96, 90, 80, 255)
BASE_BRIDGE = (86, 80, 71, 255)
BASE_OUTLINE = (66, 61, 54, 255)
TRANSPARENT = (0, 0, 0, 0)

ISLAND_IDS = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'S1', 'S2', 'S3', 'S4']


def load_layout():
    return json.loads(LAYOUT.read_text(encoding='utf-8'))


def world_bounds(layout):
    verts = layout['ground']['vertices']
    xs = [v[0] for v in verts]
    zs = [v[2] for v in verts]
    min_x, max_x = min(xs) - WORLD_MARGIN, max(xs) + WORLD_MARGIN
    min_z, max_z = min(zs) - WORLD_MARGIN, max(zs) + WORLD_MARGIN
    center_x = (min_x + max_x) / 2.0
    center_z = (min_z + max_z) / 2.0
    size = max(max_x - min_x, max_z - min_z)
    return center_x, center_z, size


def make_projector(center_x, center_z, size):
    """世界 XZ -> 像素。Unity 的 +Z 是北，图像 y 向下，所以 Z 要翻转。"""
    scale = TEXTURE_SIZE / size
    origin_x = center_x - size / 2.0
    origin_z = center_z - size / 2.0

    def project(x, z):
        return (x - origin_x) * scale, TEXTURE_SIZE - (z - origin_z) * scale

    def unproject(px, py):
        return origin_x + px / scale, origin_z + (TEXTURE_SIZE - py) / scale

    return project, unproject, scale


def bridge_endpoints(layout):
    """桥面区域 id -> 两端岛屿 id。"""
    return {b['id']: (b['from'], b['to']) for b in layout.get('bridges', [])}


def regions_for(region, bridges):
    """一块地面三角属于哪些图层。桥同时进两端，任一端去过就跟着亮。"""
    if region in ISLAND_IDS:
        return [region]
    ends = bridges.get(region)
    return list(ends) if ends else []


def draw_layer(layout, project, wanted, palette):
    """把属于 wanted（None = 全部）的地面画到一张全画布图上。"""
    ground_fill, bridge_fill, outline_colour, with_obstacles = palette
    image = Image.new('RGBA', (TEXTURE_SIZE, TEXTURE_SIZE), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    bridges = bridge_endpoints(layout)
    verts = layout['ground']['vertices']
    tris = layout['ground']['triangles']
    regions = layout['ground']['triangleRegions']
    for index, tri in enumerate(tris):
        region = regions[index] if index < len(regions) else ''
        if wanted is not None and wanted not in regions_for(region, bridges):
            continue
        fill = ground_fill if region in ISLAND_IDS else bridge_fill
        draw.polygon([project(verts[i][0], verts[i][2]) for i in tri], fill=fill)
    if with_obstacles:
        for obstacle in layout.get('obstacles', []):
            bounds = obstacle.get('bounds')
            if not bounds or len(bounds) != 4:
                continue
            if wanted is not None and obstacle.get('island') != wanted:
                continue
            a = project(bounds[0], bounds[1])
            b = project(bounds[2], bounds[3])
            box = [min(a[0], b[0]), min(a[1], b[1]), max(a[0], b[0]), max(a[1], b[1])]
            # 太小的障碍物在这个分辨率下不足一像素，画出来只会变成噪点。
            if box[2] - box[0] < 1.5 or box[3] - box[1] < 1.5:
                continue
            draw.rectangle(box, fill=OBSTACLE_FILL)
    for island in layout.get('islands', []):
        if wanted is not None and island['id'] != wanted:
            continue
        outline = island.get('outline')
        if not outline or len(outline) < 3:
            continue
        points = [project(p[0], p[1]) for p in outline]
        draw.line(points + [points[0]], fill=outline_colour, width=3, joint='curve')
    return image


def square_crop(image):
    """裁到内容的方形包围盒。返回 (裁好的图, 中心像素)。"""
    box = image.getbbox()
    if box is None:
        return None, None
    left, top, right, bottom = box
    side = max(right - left, bottom - top, MIN_REGION_TEXTURE)
    cx = (left + right) / 2.0
    cy = (top + bottom) / 2.0
    half = side / 2.0
    crop = (int(round(cx - half)), int(round(cy - half)),
            int(round(cx - half)) + side, int(round(cy - half)) + side)
    return image.crop(crop), ((crop[0] + crop[2]) / 2.0, (crop[1] + crop[3]) / 2.0)


def geometry_digest(layout):
    """地图形状用到的几何（地面三角、区域、岛轮廓、障碍脚印）的指纹。手绘底图按它判断是否过期。"""
    ground = layout['ground']
    payload = {
        'vertices': [[round(v[0], 3), round(v[2], 3)] for v in ground['vertices']],
        'triangles': ground['triangles'],
        'regions': ground['triangleRegions'],
        'outlines': {island['id']: island.get('outline') for island in layout.get('islands', [])},
        'obstacles': sorted([obstacle.get('island') or '', obstacle['bounds']]
                            for obstacle in layout.get('obstacles', []) if obstacle.get('bounds')),
    }
    return hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(',', ':')).encode('utf-8')).hexdigest()


def load_art(path, layout, center_x, center_z, size):
    """读手绘底图并核对它是按当前投影与几何对齐的；对不上就硬失败，不拿旧图凑合。"""
    record_path = path.with_suffix('.json')
    if not record_path.is_file():
        raise SystemExit('手绘底图缺少同名 .json 记录：' + str(record_path))
    record = json.loads(record_path.read_text(encoding='utf-8'))
    expected = {'textureSize': TEXTURE_SIZE, 'imageWorldSize': round(size, 4),
                'mapWorldCenter': [round(center_x, 4), round(center_z, 4)], 'geometrySha256': geometry_digest(layout)}
    stale = [key for key, value in expected.items() if record.get(key) != value]
    if stale:
        raise SystemExit('手绘底图是按另一版布局对齐的（%s 不符）：用 tools/sky_island_minimap_art.py 重新生成，'
                         '或加 --flat 烘焙纯色版' % '、'.join(stale))
    image = Image.open(path).convert('RGB')
    if image.size != (TEXTURE_SIZE, TEXTURE_SIZE):
        raise SystemExit('手绘底图必须是 %d px 正方形：%s' % (TEXTURE_SIZE, path))
    return image, record


def paint(shape, art):
    """保留 shape 的形状（alpha 原样），颜色换成手绘底图同位置的像素。
    透明像素上的颜色无所谓：作者工程导入时 alphaIsTransparency 会把边缘颜色外扩。"""
    layer = art.convert('RGBA')
    layer.putalpha(shape.getchannel('A'))
    return layer


def fog(art):
    """未探索底图：手绘图去色、轻微模糊，再压回原底图的暗灰色调，只剩轮廓和大路的笔触。"""
    gray = ImageOps.grayscale(art).filter(ImageFilter.GaussianBlur(1.2))
    mean = ImageStat.Stat(gray).mean[0] / 255.0
    channels = [gray.point(lambda v, c=c: max(0, min(255, int(c * (0.72 + 0.55 * (v / 255.0 - mean))))))
                for c in BASE_GROUND[:3]]
    return Image.merge('RGB', channels)


def write(path, image):
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, 'PNG', optimize=True)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--unity-project', default=r'D:/code/ykf/duckov_modding-main/UnityFiles/BossRush')
    # 默认读写仓库路径；换布局时可先指到草稿目录烘一版，与其它产物一起再换进仓库。
    parser.add_argument('--layout', default=str(LAYOUT))
    parser.add_argument('--out-dir', default=str(OUT_DIR))
    parser.add_argument('--out-json', default=str(OUT_JSON))
    parser.add_argument('--art', default=str(ART_IMAGE), help='手绘底图；文件不存在时烘焙纯色版')
    parser.add_argument('--flat', action='store_true', help='不用手绘底图，烘焙纯色版')
    args = parser.parse_args()
    out_dir, out_json = Path(args.out_dir), Path(args.out_json)
    out_dir.mkdir(parents=True, exist_ok=True)

    layout = json.loads(Path(args.layout).read_text(encoding='utf-8'))
    center_x, center_z, size = world_bounds(layout)
    project, unproject, scale = make_projector(center_x, center_z, size)
    art_path = Path(args.art)
    art, art_record = (None, None) if args.flat or not art_path.is_file() else load_art(art_path, layout, center_x, center_z, size)

    # ---- 底图：始终显示，给未探索区域一个轮廓（纯色版暗灰带障碍镂空；手绘版去色压暗、不镂空）----
    if art is None:
        base = draw_layer(layout, project, None, (BASE_GROUND, BASE_BRIDGE, BASE_OUTLINE, False))
    else:
        base = paint(draw_layer(layout, project, None, (BASE_GROUND, BASE_BRIDGE, BASE_OUTLINE, True)), fog(art))
    base_path = out_dir / (BASE_NAME + '.png')
    base_sha = write(base_path, base)

    layers = []
    for region in ISLAND_IDS:
        coloured = draw_layer(layout, project, region, (GROUND_FILL, BRIDGE_FILL, OUTLINE, True))
        if art is not None:
            coloured = paint(coloured, art)
        crop, centre_px = square_crop(coloured)
        if crop is None:
            raise SystemExit('区域没有任何地面三角：' + region)
        world_cx, world_cz = unproject(centre_px[0], centre_px[1])
        path = out_dir / ('%s_%s.png' % (BASE_NAME, region))
        layers.append({
            'region': region,
            'texture': path.name,
            'textureSize': crop.width,
            # 每条 MapEntry 的 imageWorldSize 必须按自己那张贴图的边长算。
            'imageWorldSize': round(crop.width / scale, 4),
            # Offset 是这张图的中心相对全图中心的世界 XZ 偏移，
            # 官方用它做 `rectTransform.anchoredPosition`。
            'offset': [round(world_cx - center_x, 4), round(world_cz - center_z, 4)],
            'sha256': write(path, crop),
        })

    meta = {
        'status': 'PASS',
        'sceneId': 'BossRush_SkyIsland',
        'texture': base_path.name,
        'textureSize': TEXTURE_SIZE,
        'imageWorldSize': round(size, 4),
        'mapWorldCenter': [round(center_x, 4), 0.0, round(center_z, 4)],
        'pixelSize': round(size / TEXTURE_SIZE, 6),
        'sha256': base_sha,
        'regionLayers': layers,
    }
    if art is not None:
        resolved = art_path.resolve()
        meta['art'] = {
            'image': resolved.relative_to(ROOT).as_posix() if resolved.is_relative_to(ROOT) else str(resolved),
            'sha256': hashlib.sha256(art_path.read_bytes()).hexdigest(),
            'grade': art_record.get('grade'),
        }
    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_json.write_text(json.dumps(meta, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

    unity = Path(args.unity_project)
    if unity.is_dir():
        target = unity / 'Assets/SkyIsland/Minimap'
        target.mkdir(parents=True, exist_ok=True)
        for png in out_dir.glob(BASE_NAME + '*.png'):
            (target / png.name).write_bytes(png.read_bytes())
        (target / 'sky_island_minimap.json').write_text(
            json.dumps(meta, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print('deployed to ' + str(target))
    else:
        print('unity project not found, skipped deployment: ' + str(unity))

    print('base %dx%d  world %.1fm  center (%.1f, %.1f)' % (TEXTURE_SIZE, TEXTURE_SIZE, size, center_x, center_z))
    for layer in layers:
        print('  layer %-3s %4dpx  world %6.1fm  offset (%7.1f, %7.1f)'
              % (layer['region'], layer['textureSize'], layer['imageWorldSize'],
                 layer['offset'][0], layer['offset'][1]))


if __name__ == '__main__':
    main()
