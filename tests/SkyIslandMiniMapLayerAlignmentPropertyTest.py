"""分区图层的偏移量必须能把整张地图拼回去（离线复算，不用进游戏）。

官方 `MiniMapDisplayEntry.Setup` 是这么摆一条 MapEntry 的：

    rectTransform.sizeDelta        = Vector2.one * sprite.texture.width * PixelSize
    rectTransform.anchoredPosition = cur.Offset          // MapEntry.offsetReference.localPosition
    PixelSize                      = imageWorldSize / sprite.texture.width

也就是说：图层在地图上的边长 = 它自己的 `imageWorldSize`，中心落在相对全图中心的
`Offset`（世界 XZ 米）。这个测试就按同一套公式，把 12 张分区贴图贴回底图坐标系，
再和「一次性画出来的全彩参考图」逐像素比对。

对得上，说明 `imageWorldSize` / `offset` 这两组数是对的——这正是原本只能靠实机
开一次地图才能确认的东西。对不上就说明烘焙脚本的投影或裁剪改坏了。
"""
import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'tools'))

MINIMAP = ROOT / 'ArtSource/SkyIsland/Minimap'
META = ROOT / 'ArtSource/SkyIsland/Validation/sky_island_minimap.json'

# 允许的错位像素比例。0 是做不到的：分区图层裁剪时按整像素取整，
# 参考图是一次画成的，边界上会有亚像素级差异。
MAX_MISMATCH = 0.01


def main():
    import build_sky_island_minimap as baker

    meta = json.loads(META.read_text(encoding='utf-8'))
    size = meta['textureSize']
    world = meta['imageWorldSize']
    scale = size / world

    # 参考图：一次性画出的全彩全岛，和分区图层用同一套调色。
    layout = baker.load_layout()
    center_x, center_z, world_size = baker.world_bounds(layout)
    if abs(world_size - world) > 1e-6:
        return fail('元数据 imageWorldSize 与布局重算不一致：%s vs %s' % (world, world_size))
    if abs(center_x - meta['mapWorldCenter'][0]) > 1e-6 or abs(center_z - meta['mapWorldCenter'][2]) > 1e-6:
        return fail('元数据 mapWorldCenter 与布局重算不一致')
    project, _unproject, _scale = baker.make_projector(center_x, center_z, world_size)
    reference = baker.draw_layer(layout, project,
                                None, (baker.GROUND_FILL, baker.BRIDGE_FILL, baker.OUTLINE, True))

    # 按官方公式把分区图层贴回去。
    canvas = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    for layer in meta['regionLayers']:
        image = Image.open(MINIMAP / layer['texture']).convert('RGBA')
        if image.width != image.height:
            return fail('分区贴图必须是正方形（官方按 width 定尺寸）：' + layer['texture'])
        if image.width != layer['textureSize']:
            return fail('分区贴图边长与元数据不符：' + layer['texture'])
        expected_world = layer['textureSize'] / scale
        if abs(expected_world - layer['imageWorldSize']) > 0.01:
            return fail('分区 imageWorldSize 与贴图边长不自洽：' + layer['region'])
        offset_x, offset_z = layer['offset']
        centre_x = size / 2.0 + offset_x * scale
        centre_y = size / 2.0 - offset_z * scale          # +Z 是北，图像 y 向下
        canvas.alpha_composite(image, (int(round(centre_x - image.width / 2.0)),
                                       int(round(centre_y - image.height / 2.0))))

    ref = reference.load()
    got = canvas.load()
    mismatch = 0
    total = 0
    for y in range(size):
        for x in range(size):
            a = ref[x, y][3] > 0
            b = got[x, y][3] > 0
            if a or b:
                total += 1
                if a != b:
                    mismatch += 1
    ratio = mismatch / float(max(total, 1))
    if ratio > MAX_MISMATCH:
        return fail('分区图层拼不回整图：错位像素 %d/%d (%.3f%%)' % (mismatch, total, ratio * 100))

    # 每个区域都必须有自己的图层，缺一个就等于那块地永远不会点亮。
    regions = [layer['region'] for layer in meta['regionLayers']]
    if sorted(regions) != sorted(baker.ISLAND_IDS):
        return fail('分区图层与岛屿列表不一致：' + str(regions))
    if len(set(regions)) != len(regions):
        return fail('分区图层有重复区域')

    print('SkyIslandMiniMapLayerAlignmentPropertyTest: PASS (%d layers, mismatch %.3f%%)' % (len(regions), ratio * 100))
    return 0


def fail(message):
    print('SkyIslandMiniMapLayerAlignmentPropertyTest: FAIL ' + message)
    return 1


if __name__ == '__main__':
    raise SystemExit(main())
