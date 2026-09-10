"""天空岛必须用官方地图，且烘焙贴图与场景构建器口径一致。

背景（2026-09-09 审核）：官方地图系统是纯场景数据驱动的——`MiniMapDisplay.AutoSetup()`
就是一句 `FindAnyObjectByType<MiniMapSettings>()`，`MiniMapView.OnOpen()` 每次开图都会重跑。
天空岛原先没往场景里放这份数据，于是官方地图键在岛上是死键，Mod 反而自绘了一整套 F6 界面。
现在改成：场景包带 `MiniMapSettings`，Mod 侧不再有任何自绘地图。

这个守卫钉住四件事：
1. 自绘地图不得复活，Mod 侧不得接管地图输入/模态状态；分区迷雾按存档到访位刷、按 sprite 名认领；
2. 烘焙贴图与它的元数据一致（sha256 逐字节、宽高自洽、必须正方形），覆盖范围罩得住地面网格；
3. Unity 场景构建器确实装配 `MiniMapSettings`，且分区图层**默认可见**（失败开放）；
4. 这些素材必须在 `.gitignore` 里放行，否则 fresh clone 读不到。
"""
import hashlib
import json
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent
PNG = ROOT / 'ArtSource/SkyIsland/Minimap/sky_island_minimap.png'
META = ROOT / 'ArtSource/SkyIsland/Validation/sky_island_minimap.json'
LAYOUT = ROOT / 'ArtSource/SkyIsland/layout.json'
NEWLINE = chr(10)
BUILDER = Path(r'D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/Assets/Editor/SkyIslandRaidBuilder.cs')


def png_size(path):
    """直接读 PNG 头里的宽高，避免为了一个尺寸校验去依赖 Pillow。"""
    data = path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b'\x89PNG\r\n\x1a\n':
        return -1, -1
    return (int.from_bytes(data[16:20], 'big'), int.from_bytes(data[20:24], 'big'))


def main():
    errors = []

    # ---- 1) 自绘地图不得复活 ----
    if (ROOT / 'DebugAndTools/SkyIsland/SkyIslandMap.cs').exists():
        errors.append('自绘旅程图已废弃，不得重新引入 SkyIslandMap.cs')
    session = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandSession.cs').read_text(encoding='utf-8-sig'))
    if 'MiniMapView.Show()' not in session:
        errors.append('OpenMap 必须打开官方地图（MiniMapView.Show）')
    # 注意用 `new SkyIslandMap(`，不是裸的 `SkyIslandMap`：
    # 后者会被 `SkyIslandMapFog`（分区迷雾，走的是官方图层数据）误伤。
    for token in ('new SkyIslandMap(', 'ClaimModalInput', 'Time.timeScale ='):
        if token in session:
            errors.append('会话不得重建自绘地图或接管模态状态：' + token)
    # 分区「未探索」灰显：必须按存档到访位刷，且新到访区域要立刻点亮。
    fog = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandMapFog.cs').read_text(encoding='utf-8-sig'))
    for token in ('MiniMapSettings.Instance', 'pair.Value.hide', 'SkyIslandStoryService.RegionBit',
                  'SpritePrefix', 'name.StartsWith(SpritePrefix'):
        if token not in fog:
            errors.append('分区迷雾缺少：' + token)
    # 认领图层只能按 sprite 名字，不能按列表下标——下标会随烘焙输出顺序漂移。
    if 'settings.maps[' in fog:
        errors.append('分区图层不得按下标认领，必须按 sprite 名字')
    if session.count('mapFog.Apply(story.Current.visitedRegions)') < 2:
        errors.append('分区迷雾必须在装配时刷一次，并在新到访区域时再刷一次')
    if 'mapFog.Dispose()' not in session:
        errors.append('分区迷雾必须随会话释放')
    module = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs').read_text(encoding='utf-8-sig'))
    if 'KeyCode.F6' in module or 'KeyCode.F6' in session:
        errors.append('官方地图由玩家自己的地图键开合，Mod 侧不得再处理 F6')

    # ---- 2) 烘焙贴图与元数据一致 ----
    if not PNG.is_file():
        errors.append('缺少烘焙的小地图贴图，请运行 tools/build_sky_island_minimap.py')
    elif not META.is_file():
        errors.append('缺少小地图烘焙元数据')
    else:
        meta = json.loads(META.read_text(encoding='utf-8'))
        digest = hashlib.sha256(PNG.read_bytes()).hexdigest()
        if meta.get('sha256') != digest:
            errors.append('小地图贴图与元数据 sha256 不一致，请重新运行烘焙脚本')
        if meta.get('sceneId') != 'BossRush_SkyIsland':
            errors.append('小地图元数据场景 ID 必须是 BossRush_SkyIsland')
        size = meta.get('imageWorldSize')
        texture = meta.get('textureSize')
        center = meta.get('mapWorldCenter')
        if not isinstance(size, (int, float)) or size <= 0:
            errors.append('imageWorldSize 必须为正数')
        if not isinstance(texture, int) or texture <= 0:
            errors.append('textureSize 必须为正整数')
        if not isinstance(center, list) or len(center) != 3:
            errors.append('mapWorldCenter 必须是三元组')
        layers = meta.get('regionLayers')
        if not isinstance(layers, list) or len(layers) != 12:
            errors.append('必须有 12 张分区图层，未探索灰显才完整')
        else:
            for layer in layers:
                png = PNG.parent / layer.get('texture', '')
                if not png.is_file():
                    errors.append('缺少分区贴图：' + str(layer.get('texture')))
                elif hashlib.sha256(png.read_bytes()).hexdigest() != layer.get('sha256'):
                    errors.append('分区贴图与元数据 sha256 不一致：' + str(layer.get('texture')))
                else:
                    width, height = png_size(png)
                    if width != height:
                        errors.append('分区贴图必须是正方形（官方 sizeDelta 只取 width）：'
                                      + str(layer.get('texture')))
                    if width != layer.get('textureSize'):
                        errors.append('分区贴图实际宽高与元数据 textureSize 不符：'
                                      + str(layer.get('texture')))
        # 手绘底图模式：元数据记着底图的 sha256，底图必须在仓库里且逐字节一致，否则这版贴图无法复现。
        art = meta.get('art')
        if art is not None:
            source = ROOT / str(art.get('image', ''))
            if not source.is_file():
                errors.append('小地图元数据声明了手绘底图，但文件不存在：' + str(art.get('image')))
            elif hashlib.sha256(source.read_bytes()).hexdigest() != art.get('sha256'):
                errors.append('手绘底图与小地图元数据 sha256 不一致，请重新运行烘焙脚本')
        # 覆盖范围必须真的罩住地面网格，否则玩家点位会跑到图外。
        if isinstance(size, (int, float)) and isinstance(center, list) and len(center) == 3:
            verts = json.loads(LAYOUT.read_text(encoding='utf-8'))['ground']['vertices']
            half = size / 2.0
            for axis, index in (('X', 0), ('Z', 2)):
                lo = min(v[index] for v in verts)
                hi = max(v[index] for v in verts)
                if lo < center[index] - half or hi > center[index] + half:
                    errors.append('小地图覆盖范围没有罩住地面网格的 ' + axis + ' 轴')

    # ---- 3) Unity 构建器确实装配 MiniMapSettings ----
    # 作者工程是 local-only（不进 git），因此缺失时只跳过，不当作失败。
    if BUILDER.is_file():
        builder = clean_source(BUILDER.read_text(encoding='utf-8-sig'))
        # 断言真实的装配调用，不是光看类型名出现过：
        # 只找 'MiniMapSettings' 会被 using 和 MapEntry 的类型限定名满足。
        for token in ('AddComponent<MiniMapSettings>()', 'settings.maps = entries;',
                      'new List<MiniMapSettings.MapEntry>',
                      'sceneID = "BossRush_SkyIsland"',
                      'Assets/SkyIsland/Minimap/sky_island_minimap.png',
                      'Assets/SkyIsland/Minimap/sky_island_minimap.json',
                      # 分区图层：偏移只能经 offsetReference（官方读它的 localPosition）。
                      # 初始可见性见下面的「失败开放」断言。
                      'SpriteRenderer reference = anchor.AddComponent<SpriteRenderer>()',
                      'offsetReference = reference',
                      'foreach (MinimapLayer layer in meta.regionLayers)'):
            if token not in builder:
                errors.append('场景构建器缺少官方小地图装配：' + token)
        # combinedSprite 必须留空：置了它就走 combined 通道，而 SetupCombined 会把 sceneID 清成空串，
        # 让按 sceneID 的坐标换算找不到条目。
        if 'settings.combinedSprite = null;' not in builder:
            errors.append('单图场景必须把 combinedSprite 留空，否则按 sceneID 的坐标换算会失效')
        # MiniMapCenter 会用自己的 transform 覆盖 mapWorldCenter，和离线烘焙的中心打架。
        if 'MiniMapCenter' in builder:
            errors.append('不要放 MiniMapCenter，它会覆盖烘焙出来的 mapWorldCenter')
        layer_block = builder.split('foreach (MinimapLayer layer in meta.regionLayers)', 1)[1]
        layer_block = layer_block.split(NEWLINE + '            }', 1)[0]
        # 刻意「失败开放」：分区图层默认可见，运行时把没去过的关掉。
        # 反过来默认隐藏的话，一旦运行时认领图层失败，整张地图只剩暗灰底图、等于废掉。
        if 'hide = false' not in layer_block:
            errors.append('分区图层必须默认可见（失败开放），由运行时关掉未到访的')

    # 守卫要读并比对 sha256 的素材必须纳管；ArtSource/SkyIsland 默认整个 local-only，
    # 漏加例外就是「本地一直绿、fresh clone 必红」那一类失败。
    ignore = (ROOT / '.gitignore').read_text(encoding='utf-8')
    if '!/ArtSource/SkyIsland/Minimap/' not in ignore:
        errors.append('小地图贴图未在 .gitignore 里放行，fresh clone 会读不到')

    print('SkyIslandMiniMapGuard: ' + ('FAIL\n' + '\n'.join(errors) if errors else 'PASS'))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
