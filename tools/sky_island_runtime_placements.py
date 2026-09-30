"""把 C# / 数据表在运行时按坐标生成的天空岛物件离线复算成一张表（审计用）。

落点算法不在这里重写，直接借用逐字复现生产算法的两份属性测试：
`tests/SkyIslandInteractionCompetitionPropertyTest.py`（见闻点、航路图、纪念物、搜刮箱、谢礼箱、居民、信鸽、采集点）
与 `tests/SkyIslandContentPlacementPropertyTest.py`（地面射线、墙体裁决）；此外按 C# 常量补上灶火、
桥口木牌、剧情门、撤离环、巡守与遭遇槽位。改生产落点算法时同步那两份测试，这里自动跟上。

输出 `ArtSource/SkyIsland/runtime_placements.json`，由 `tools/sky_island_scene_audit.py` 与
`tests/SkyIslandSceneCleanlinessPropertyTest.py` 读取：每条 {kind, id, x, y, z, clearRadius, footprint, mesh}。
`footprint` 是它自己占的水平半径（有网格才会压路 / 穿模），`clearRadius` 是它需要别人让出的净空。
"""
import importlib.util
import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'ArtSource/SkyIsland/runtime_placements.json'


def _module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tests' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def build():
    competition = _module('SkyIslandInteractionCompetitionPropertyTest')
    content = _module('SkyIslandContentPlacementPropertyTest')
    markers = competition.MARKERS
    points = []

    def add(kind, identifier, spot, footprint, clear, mesh, **extra):
        row = {'kind': kind, 'id': identifier, 'x': round(spot[0], 3), 'y': round(spot[1], 3), 'z': round(spot[2], 3),
               'footprint': footprint, 'clearRadius': clear, 'mesh': mesh}
        row.update(extra)
        points.append(row)

    items, notes = competition.build_interactables()
    # 交互体：网格来源与占地（搜刮箱换了木箱外观，最长边 ≤ 1.3 m）；纪念物有木桩木牌；其余只有触发体。
    for name, spot, half, _group in items:
        kind, identifier = name.split(':', 1)
        if kind == 'loot':
            add('loot_crate', identifier, spot, 0.66, 1.0, 'crate')
        elif kind == 'bounty':
            add('bounty_crate', identifier, spot, 0.6, 1.0, 'official_lootbox')
        elif kind == 'memorial':
            add('memorial_stake', identifier, spot, 0.45, 1.2, 'stake_sign')
        elif kind == 'resident':
            # 居民站在标记脚下，能游走的 4 位在 2.5 m 圈里走动；圈里不该有硬物。
            wander = identifier in ('POI_B', 'EnemySpawn_B', 'POI_A', 'POI_D')
            add('resident', identifier, spot, 0.6, 0.6 + (2.5 if wander else 0.6), 'character', wander=wander)
        elif kind == 'search':
            add('search_point', identifier, spot, 0.0, half, None)
        elif kind == 'gather':
            add('gather_node', identifier, spot, 0.0, half, 'ground_glow')
        elif kind == 'guide':
            add('travel_guide', identifier, spot, 0.0, min(half, 0.65), None)
        elif kind == 'pigeon':
            # 信鸽没有网格，触发盒半宽 1.5 只管够得着；让出的是站着读信的那块地（0.6 m）。
            add('pigeon', identifier, spot, 0.0, 0.6, None)

    # 灶火：P1，方位与距离取 SkyIslandLootTables.PlacementFor(marker + ":hearth", SpotDistance)；失败贴标记下方地面。
    hearth_src = (ROOT / 'SkyIsland/SkyIslandHearthFx.cs').read_text(encoding='utf-8')
    spot_distance = float(re.search(r'SpotDistance\s*=\s*([0-9.]+)f', hearth_src).group(1))
    lights_src = (ROOT / 'SkyIsland/SkyIslandLights.cs').read_text(encoding='utf-8')
    hearths = re.findall(r'"([A-Za-z0-9_]+)"', re.search(r'HearthMarkers\s*=\s*\{([^}]*)\}', lights_src).group(1))
    for marker in hearths:
        anchor = markers[marker]
        spot = competition.resolve_crate(anchor, *competition.placement_for(marker + ':hearth', spot_distance))
        if spot is None:
            spot = anchor
        add('hearth', marker, spot, 0.55, 1.2, 'fire_particles')

    # 剧情门与桥口木牌：C# 写死的世界坐标。木桩在世界 X ±2.4（不随桥转），各 0.11。
    gates_src = (ROOT / 'SkyIsland/SkyIslandGates.cs').read_text(encoding='utf-8')
    for gid, x, y, z, yaw, width in re.findall(
            r'Add\(root, wood, wallLayer, "(\w+)", new Vector3\(([-0-9.]+)f?, ([-0-9.]+)f?, ([-0-9.]+)f?\), ([-0-9.]+)f?, ([0-9.]+)\)',
            gates_src):
        add('story_gate', gid, (float(x), float(y), float(z)), 0.0, 0.0, 'gate',
            yaw=float(yaw), width=float(width))
    for index, (gid, x, y, z) in enumerate(re.findall(
            r'AddSign\(root, wood, wallLayer, "(\w+)", new Vector3\(([-0-9.]+)f?, ([-0-9.]+)f?, ([-0-9.]+)f?\)\)', gates_src)):
        for side in (-1, 1):
            add('bridge_sign_post', '%s#%d%s' % (gid, index, 'L' if side < 0 else 'R'),
                (float(x) + side * 2.4, float(y), float(z)), 0.16, 0.4, 'post')

    # 撤离环：P6，半径 ExtractionRadius；环里不该有硬物。
    for marker in ('Exit', 'BellExtraction', 'Region_D', 'Region_G'):
        position = markers[marker]
        add('extraction_ring', marker, position, 0.0, competition.EXTRACTION_RADIUS + 0.12, 'ground_ring')

    # 巡守与遭遇槽位：角色站点，按胶囊 0.5 m 计。
    patrols = json.loads((ROOT / 'Assets/Data/SkyIsland/Patrols.json').read_text(encoding='utf-8'))
    for slot in patrols['slots']:
        add('patrol_slot', slot['id'], (slot['x'], slot['y'], slot['z']), 0.0, 0.5, 'character')
    world = json.loads((ROOT / 'Assets/Data/SkyIsland/World.json').read_text(encoding='utf-8'))
    for entry in world['encounters']:
        origin = markers[entry['marker']]
        for index in range(entry['count']):
            for attempt in range(12):
                angle = math.radians(index * 120.0 + attempt * 30.0)
                radius = 0.0 if index == 0 else 3.0
                px = origin[0] + math.cos(angle) * radius
                pz = origin[2] + math.sin(angle) * radius
                hit = content.ground_hit(px, pz, origin[1] + 2.0, 4.0)
                if hit is None or content.blocked(px, pz, hit + 0.1, 0.6, 1.5, 0.5):
                    continue
                add('encounter_slot', '%s#%d' % (entry['id'], index), (px, hit, pz), 0.0, 0.5, 'character')
                break
    return {'schemaVersion': 1, 'coordinateSystem': 'Unity XYZ metres',
            'source': 'offline replay of production placement (see module docstring)',
            'notes': notes, 'points': points}


def main():
    data = build()
    OUT.write_text(json.dumps(data, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    kinds = {}
    for row in data['points']:
        kinds[row['kind']] = kinds.get(row['kind'], 0) + 1
    print(json.dumps({'output': str(OUT.relative_to(ROOT)), 'kinds': kinds, 'notes': len(data['notes'])},
                     ensure_ascii=False))


if __name__ == '__main__':
    main()
