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

用法：
    python tools/build_sky_island_minimap.py [--unity-project <路径>]

产物：
    ArtSource/SkyIsland/Minimap/sky_island_minimap.png          底图（暗灰）
    ArtSource/SkyIsland/Minimap/sky_island_minimap_<区域>.png   12 张分区彩色图层
    ArtSource/SkyIsland/Validation/sky_island_minimap.json      尺寸/世界范围/每层偏移/sha256
"""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
LAYOUT = ROOT / 'ArtSource/SkyIsland/layout.json'
OUT_DIR = ROOT / 'ArtSource/SkyIsland/Minimap'
OUT_JSON = ROOT / 'ArtSource/SkyIsland/Validation/sky_island_minimap.json'

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


def write(path, image):
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, 'PNG', optimize=True)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--unity-project', default=r'D:/code/ykf/duckov_modding-main/UnityFiles/BossRush')
    args = parser.parse_args()

    layout = load_layout()
    center_x, center_z, size = world_bounds(layout)
    project, unproject, scale = make_projector(center_x, center_z, size)

    # ---- 底图：全岛暗灰，始终显示，给未探索区域一个轮廓 ----
    base = draw_layer(layout, project, None, (BASE_GROUND, BASE_BRIDGE, BASE_OUTLINE, False))
    base_path = OUT_DIR / (BASE_NAME + '.png')
    base_sha = write(base_path, base)

    layers = []
    for region in ISLAND_IDS:
        coloured = draw_layer(layout, project, region, (GROUND_FILL, BRIDGE_FILL, OUTLINE, True))
        crop, centre_px = square_crop(coloured)
        if crop is None:
            raise SystemExit('区域没有任何地面三角：' + region)
        world_cx, world_cz = unproject(centre_px[0], centre_px[1])
        path = OUT_DIR / ('%s_%s.png' % (BASE_NAME, region))
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
    OUT_JSON.parent.mkdir(parents=True, exist_ok=True)
    OUT_JSON.write_text(json.dumps(meta, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

    unity = Path(args.unity_project)
    if unity.is_dir():
        target = unity / 'Assets/SkyIsland/Minimap'
        target.mkdir(parents=True, exist_ok=True)
        for png in OUT_DIR.glob(BASE_NAME + '*.png'):
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
