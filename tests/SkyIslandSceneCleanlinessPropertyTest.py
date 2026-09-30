"""天空岛场景净空属性测试（2026-09-30）：路面干净、桥口 / 门口 / 交互点让开、运行时物件不压路不插墙、低层不互穿。

为什么要有它：57a5408f 修道路家具时只查了村口家具和 Tripo 锚点件、按道路中心线查，岛缘散布的树石、
按旧坐标画的桌凳 / 石灯 / 邮亭 / 码头货箱、灯标记处的铜灯、桥上中继灯都没过判据，实机仍看到东西压在路上。
这次所有按坐标画的件都走 `PlacementSpace.fit`（量实际网格截面），并把全岛实测结果写成证据表。

证据：`ArtSource/SkyIsland/Validation/sky_island_scene_audit.json`，由
`generate_sky_island.py --audit-ledger` 的实例台账经 `tools/sky_island_scene_audit.py --compact` 写出。本测试：
1. 新鲜度：证据表里的 layout 规范哈希、道路 payloadHash、运行时摆放表 sha256 等于当前文件；台账三角形数加岩体
   三角形数等于 `Validation/sky_island_geometry.json` 的 totalVisualTriangles（证据与交付几何出自同一次生成）。
2. 运行时摆放表与 C# 落点同步：`tools/sky_island_runtime_placements.build()` 现算结果逐项等于提交的表。
3. 复核：`sky_island_scene_audit.recheck()` 在证据表上重算压路 / 净空 / 运行时 / 低层互穿，非设计接触为 0；
   审计记录的缺陷只剩 DESIGNED_CONTACT / INTENTIONAL_FLOAT 里逐条写了理由的设计接触。
4. 结构：生成器 main 里统一裁决先建好，地标、码头、村景、灯标记、岛缘散布、Tripo 件、花草依次发射；
   按坐标画的件都经 `fitted` / `PROP_SPACE`。
5. 反向探针（内存里，不改文件）：路面正中塞一件硬物、把一只搜刮箱挪到路上、造两件低层互穿的硬物、
   运行时表改一个坐标——各自必须报红。

这是离线几何证据（L2），不证明游戏内镜头遮挡、角色碰撞或性能。需要 Shapely 2.1（CI 已安装）。
"""
import ast
import copy
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Build/sky-island-python-deps'))
sys.path.insert(0, str(ROOT / 'tools'))

import shapely  # noqa: E402
from shapely.geometry import box  # noqa: E402
import sky_island_road_layout  # noqa: E402
import sky_island_runtime_placements  # noqa: E402
import sky_island_scene_audit as audit  # noqa: E402

ART = ROOT / 'ArtSource/SkyIsland'
EVIDENCE = ART / 'Validation/sky_island_scene_audit.json'
GENERATOR = ROOT / 'tools/generate_sky_island.py'


def load():
    evidence = json.loads(EVIDENCE.read_text(encoding='utf-8'))
    layout = json.loads((ART / 'layout.json').read_text(encoding='utf-8'))
    roads = json.loads((ART / 'road_layout.json').read_text(encoding='utf-8'))
    runtime_bytes = (ART / 'runtime_placements.json').read_bytes()
    geometry = json.loads((ART / 'Validation/sky_island_geometry.json').read_text(encoding='utf-8'))
    return evidence, layout, roads, runtime_bytes, geometry


def freshness(evidence, layout, roads, runtime_bytes, geometry):
    source = evidence['source']
    problems = []
    if source['layoutCanonicalSha256'] != sky_island_road_layout.digest(layout):
        problems.append('STALE_LAYOUT')
    if source['roadPayloadHash'] != roads['payloadHash']:
        problems.append('STALE_ROADS')
    if source['runtimePlacementsSha256'] != hashlib.sha256(runtime_bytes).hexdigest():
        problems.append('STALE_RUNTIME_PLACEMENTS')
    geology = sum(row['triangles'] for name, row in geometry['visualMeshes'].items() if name.endswith('_Geology'))
    if source['ledgerTriangles'] + geology != geometry['totalVisualTriangles']:
        problems.append('EVIDENCE_NOT_FROM_SHIPPED_GEOMETRY')
    return problems


def check_runtime_table(runtime_bytes):
    current = sky_island_runtime_placements.build()
    committed = json.loads(runtime_bytes.decode('utf-8'))
    assert current['points'] == committed['points'], \
        'RUNTIME_TABLE_OUT_OF_SYNC：C# 落点数据变了，重跑 python tools/sky_island_runtime_placements.py 并重做场景审计'
    return len(current['points'])


def check_defects(evidence):
    open_rows = [row for row in evidence['defects'] if not row.get('intentional')]
    assert not open_rows, 'SCENE_DEFECTS_OPEN：%r' % [(r['type'], r['region'], r['name']) for r in open_rows[:10]]
    designed = [row for row in evidence['defects'] if row.get('intentional')]
    assert all(row['intentional'] for row in designed)
    return len(designed)


def function_source(tree, source, name):
    node = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == name)
    return ast.get_source_segment(source, node)


def check_structure():
    source = GENERATOR.read_text(encoding='utf-8').replace('\r\n', '\n')
    tree = ast.parse(source)
    main = function_source(tree, source, 'main')
    order = ['PROP_SPACE=PlacementSpace(', 'dock_landmark(islands)', 'area_landmarks(islands)',
             'PROP_SPACE.reserve_emitted(', 'village_life(layout,islands)', 'marker_lanterns(layout)',
             'landscape_scatter(islands', 'sky_island_tripo_props.build(', 'sky_island_dressing.build(',
             'sky_island_settlement.finish_gardens(']
    positions = [main.find(token) for token in order]
    assert all(p >= 0 for p in positions), 'EMISSION_ORDER_TOKEN_MISSING：%r' % [t for t, p in zip(order, positions) if p < 0]
    assert positions == sorted(positions), 'EMISSION_ORDER_CHANGED：统一裁决要先于一切按坐标画的件，花草最后'
    # 旧流程在全部摆放之后才按灯标记画铜灯（后画的件看不见它），不许回来。
    assert "lantern(*m['position'])" not in main.split('marker_lanterns(layout)')[1], 'LAMP_LANTERN_AFTER_PLACEMENT'
    fitted = {
        'landscape_scatter': ('scatter_tree', 'scatter_bush', 'scatter_rock', 'scatter_flowers'),
        'village_life': ('village_lantern', 'village_garden', 'village_banner'),
        'dock_landmark': ('dock_cargo', 'arch_lantern'),
        'marker_lanterns': ('marker_lantern',),
        'area_landmarks': ('village_table', 'stone_lantern', 'mural', 'bunting', 'post_kiosk', 'cave_lantern',
                           'pond_rock', 'root_forest_tree'),
    }
    for function, names in fitted.items():
        body = function_source(tree, source, function)
        for name in names:
            assert "fitted('%s'" % name in body, 'NOT_FITTED：%s 里的 %s 没走统一摆放裁决' % (function, name)
    props = (ROOT / 'tools/sky_island_tripo_props.py').read_text(encoding='utf-8')
    assert 'g.PROP_SPACE.fit(g, island_id, name,' in props, 'TRIPO_ANCHORS_NOT_FITTED'
    # 巡守槽位从导航三角形中心派生，导航又由本次几何烘出：生成器给它让位就成环，重跑结果随上一轮槽位漂移
    # （2026-09-30 第二轮）。统一裁决必须跳过它，其余运行时物件照常让位；场景审计仍按最终槽位查净空。
    import sky_island_prop_placement as placement
    from types import SimpleNamespace
    stub = SimpleNamespace(markers=[], islands={}, zones=[])
    rows = [{'kind': 'patrol_slot', 'id': 'probe_patrol', 'x': 0.0, 'z': 0.0, 'clearRadius': 0.5},
            {'kind': 'loot_crate', 'id': 'probe_crate', 'x': 0.0, 'z': 0.0, 'clearRadius': 1.0}]
    placement.PlacementSpace._zones(stub, {'markers': []}, None, {'points': rows})
    ids = {zone['id'] for zone in stub.zones}
    assert 'probe_crate' in ids, 'RUNTIME_ZONE_MISSING：运行时物件没进统一裁决'
    assert 'probe_patrol' not in ids, 'DERIVED_RUNTIME_RESERVED：生成器给导航派生的巡守槽位让位，几何与导航成环'
    return sum(len(v) for v in fitted.values())


def probes(things, layout, roads, runtime):
    road = audit.road_surface(roads)
    # 1. 路面正中一件 0.6 m 见方的硬物。
    point = road.representative_point()
    fake = {'id': -1, 'name': 'probe_crate_on_road', 'category': 'hard', 'kind': 'model', 'caller': 'probe',
            'chain': ['probe'], 'obstacleId': None, 'region': 'B', 'ground': 5.0,
            'bounds': [[point.x - .3, 5, point.y - .3], [point.x + .3, 6, point.y + .3]],
            'body': box(point.x - .3, point.y - .3, point.x + .3, point.y + .3), 'bands': None}
    found = audit.recheck(things + [fake], layout, roads, runtime)
    assert any(row[0] == 'road_intrusion' and row[1] == 'probe_crate_on_road' for row in found), 'PROBE_ROAD_ACCEPTED'
    # 2. 一只搜刮箱挪到路面上。
    moved = copy.deepcopy(runtime)
    crate = next(row for row in moved if row['kind'] == 'loot_crate')
    crate['x'], crate['z'] = point.x, point.y
    found = audit.recheck(things, layout, roads, moved)
    assert any(row[0] == 'runtime_on_road' and row[1] == 'loot_crate:' + crate['id'] for row in found), 'PROBE_CRATE_ACCEPTED'
    # 3. 两件低层互穿的硬物（放在群岛外的空处，只考互穿判据本身）。
    square = box(1000, 1000, 1002, 1002)
    bands = [square] + [None] * (len(audit.COMPACT_BANDS) - 1)
    a = dict(fake, name='probe_a', body=square, bands=bands, bounds=[[1000, 5, 1000], [1002, 6.5, 1002]])
    b = dict(fake, name='probe_b', body=square, bands=bands, bounds=[[1000, 5, 1000], [1002, 6.5, 1002]])
    found = audit.recheck([a, b], layout, roads, [])
    assert any(row[0] == 'interpenetration' for row in found), 'PROBE_INTERPENETRATION_ACCEPTED'
    return 3


def main():
    evidence, layout, roads, runtime_bytes, geometry = load()
    problems = freshness(evidence, layout, roads, runtime_bytes, geometry)
    assert not problems, '证据表过期：%r（重跑 --audit-ledger 与 sky_island_scene_audit.py --compact）' % problems
    # 4. 新鲜度判据自己要会报红：运行时表改一个坐标、道路 payload 换一个字。
    mutated = runtime_bytes.replace(b'"x":', b'"x" :', 1)
    assert 'STALE_RUNTIME_PLACEMENTS' in freshness(evidence, layout, roads, mutated, geometry), 'PROBE_STALE_RUNTIME_ACCEPTED'
    roads_mutated = dict(roads, payloadHash='0' * 64)
    assert 'STALE_ROADS' in freshness(evidence, layout, roads_mutated, runtime_bytes, geometry), 'PROBE_STALE_ROADS_ACCEPTED'
    points = check_runtime_table(runtime_bytes)
    designed = check_defects(evidence)
    runtime = json.loads(runtime_bytes.decode('utf-8'))['points']
    things = audit.compact_things(evidence['things'])
    found = audit.recheck(things, layout, roads, runtime)
    assert not found, 'RECHECK_FOUND_DEFECTS：%r' % [(r[0], r[1], round(r[2], 3), r[3]) for r in found[:12]]
    structure = check_structure()
    probe_count = probes(things, layout, roads, runtime)
    print('PASS SkyIslandSceneCleanlinessPropertyTest (%d things, %d runtime points, 0 open defects, %d designed contacts, '
          '%d fitted emitters, %d geometry probes + 2 freshness probes rejected)'
          % (len(things), points, designed, structure, probe_count))


if __name__ == '__main__':
    main()
