"""复算旧潮闸镇发布包的实体修复与受影响搜集点的入口通路（L2）。"""
from collections import deque
import hashlib
import json
import math
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from tidegate_collision_boxes import box_distance_sq, blocks_player, verify_standing_markers

SOURCE = ROOT / 'Tidegate/TidegateBuildingCollision.cs'
ART = ROOT / 'ArtSource/Tidegate'
BUNDLE = ROOT / 'Assets/arenas/tidegate_raid'
STEP = .1
# CA_Interact.SearchInteractableAround，按脚点向上、朝向偏移后对原版交互层做 OverlapSphere。
PROBE_Y, PROBE_FORWARD, PROBE_RADIUS = .5, .2, .3
# GameplayDataSettings.Prefabs.LootBoxPrefab 的根 BoxCollider 实测（原版 resources.assets）。
LOOT_CENTER = (.007711, -.174147, .173611)
LOOT_SIZE = (1.114452, 3.520267, 1.245769)


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8'))


def source_check():
    source = SOURCE.read_text(encoding='utf-8')
    world = (ROOT / 'Tidegate/TidegateWorld.cs').read_text(encoding='utf-8')
    assert source.count('node.gameObject.AddComponent<BoxCollider>().size = part.Size;') == 1
    assert world.count('TidegateBuildingCollision.RestoreMissingParts(Root.transform);') == 1
    assert world.index('TidegateBuildingCollision.RestoreMissingParts(Root.transform);') < world.index('foreach (Collider collider')
    box = dict(center=(0, .5, 0), size=(1, 1, 1), yaw=0.0, id='test')
    assert blocks_player(box, (0, 0, 0))
    assert not blocks_player(dict(box, center=(0, 1.75, 0)), (0, 0, 0)), '上方门楣不可当成地面障碍'
    assert not blocks_player(box, (.72, 0, .72)), '胶囊不可按方形脚印判断转角'


def serialized_boxes(scene):
    from verify_raid_indoor_roofs import world_matrix, xyz

    def active(node):
        while node:
            g, t = scene.nodes[node]
            if not g.m_IsActive:
                return False
            node = t.m_Father.path_id
        return True

    for node in scene.descendants(scene.unique('Collision')):
        go, _ = scene.nodes[node]
        if not active(node) or not go.m_Name.startswith(('COL_Prop', 'COL_Wall')):
            continue
        for c in go.m_Component:
            if c.component.type.name != 'BoxCollider':
                continue
            box = c.component.read()
            if not box.m_Enabled or box.m_IsTrigger:
                continue
            m = world_matrix(scene, node)
            center = m[:3, :3] @ xyz(box.m_Center) + m[:3, 3]
            size = [s * math.sqrt(sum(m[r, i] ** 2 for r in range(3))) for i, s in enumerate(xyz(box.m_Size))]
            yield dict(id=go.m_Name, center=(center[0], center[1], -center[2]), size=size,
                       yaw=-math.atan2(m[0, 2], m[2, 2]))


def door_routes(building, boxes):
    """房内 .1 m 网格，边的中点也要能容纳胶囊。种子为真实门洞内的落脚带。"""
    x0, z0, x1, z1 = building['footprint']
    floor = building['floorY']
    nearby = [b for b in boxes if x0 - 3 <= b['center'][0] <= x1 + 3 and z0 - 3 <= b['center'][2] <= z1 + 3]
    nx, nz = round((x1 - x0) / STEP), round((z1 - z0) / STEP)

    def foot(ix, iz):
        return x0 + ix * STEP, floor, z0 + iz * STEP

    def clear(at):
        return not any(blocks_player(b, at) for b in nearby)

    free = {(ix, iz) for ix in range(nx + 1) for iz in range(nz + 1) if clear(foot(ix, iz))}
    seeds = set()
    for side, pos, *width in building['doors']:
        for ix, iz in free:
            x, _, z = foot(ix, iz)
            edge = {'n': iz == 0, 's': iz == nz, 'w': ix == 0, 'e': ix == nx}[side]
            if edge and abs((x if side in ('n', 's') else z) - pos) <= (width[0] if width else 2) * .5 - .3:
                seeds.add((ix, iz))
    assert seeds, f'{building["id"]} 门口没有胶囊净空'
    seen, queue = set(seeds), deque(seeds)
    while queue:
        x, z = queue.popleft()
        for dx, dz in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            p = x + dx, z + dz
            if p in free and p not in seen and clear(foot(x + .5 * dx, z + .5 * dz)):
                seen.add(p)
                queue.append(p)
    return [foot(*point) for point in seen]


def can_interact(foot, anchor, yaw, valve):
    x, y, z = anchor
    dx, dz = x - foot[0], z - foot[2]
    reach = math.hypot(dx, dz)
    if reach > 1.65:
        return False
    direction = PROBE_FORWARD / max(.001, reach)
    query = foot[0] + dx * direction, foot[1] + PROBE_Y, foot[2] + dz * direction
    if valve:
        return math.dist(query, anchor) <= 1.1 + PROBE_RADIUS
    ox, oy, oz = LOOT_CENTER
    c, s = math.cos(yaw), math.sin(yaw)
    center = x + ox*c - oz*s, y + oy, z + ox*s - oz*c
    trigger = dict(center=center, size=LOOT_SIZE, yaw=yaw)
    return box_distance_sq(trigger, query) <= PROBE_RADIUS**2


def verify_door_openings(buildings, before, added):
    """量每栋真门洞的进出短线，不将架空横梁的整个水平投影当成墙。"""
    verified = 0
    for building in buildings:
        x0, z0, x1, z1 = building['footprint']
        floor = building['floorY']
        for side, center, *width in building['doors']:
            edge = {'n': z0, 's': z1, 'w': x0, 'e': x1}[side]
            horizontal = side in ('n', 's')
            doorway = (center, edge) if horizontal else (edge, center)
            def neighbors(boxes):
                return [b for b in boxes if math.dist(doorway, (b['center'][0], b['center'][2]))
                        <= math.hypot(b['size'][0], b['size'][2]) * .5 + 1.7]
            old, new = neighbors(before), neighbors(added)
            alternatives = []
            half = (width[0] if width else 2) * .5 - .3
            for i in range(-int(half / STEP), int(half / STEP) + 1):
                cross = center + i * STEP
                path = [(cross, floor, edge + depth * STEP) if horizontal else
                        (edge + depth * STEP, floor, cross) for depth in range(-5, 6)]
                if not any(blocks_player(b, foot) for foot in path for b in old):
                    alternatives.append(path)
            if alternatives:
                assert any(not any(blocks_player(b, foot) for foot in path for b in new) for path in alternatives), (
                    f'{building["id"]} 原有 {side} 门被新增实体封住'
                )
                verified += 1
    assert verified > 0
    return verified


def marker_data(scene, ids):
    from verify_raid_indoor_roofs import world_matrix
    nodes = scene.indexed('Loot') | scene.indexed('Interact')
    for name in ids:
        m = world_matrix(scene, nodes[name])
        x, y, z = m[:3, 3]
        yield name, ((x, y, -z), -math.atan2(m[0, 2], m[2, 2]))


def verify_visuals(scene, parts):
    """代理每块须实际命中同楼或同种散布包网格，防止打包坐标翻转或网格已移走。"""
    import numpy as np
    from verify_raid_indoor_roofs import world_matrix
    from UnityPy.helpers.MeshHelper import MeshHandler
    vertices = {}
    for row in parts:
        fields = row['id'].split('_')
        if fields[0] == 'B':
            building = fields[1]
            nodes = tuple(scene.unique(f'BLD_{building}_{suffix}') for suffix in ('Contents', 'LowWalls', 'Roof'))
        else:
            nodes = tuple(node for name in scene.named if name.startswith(f'Scatter_{fields[0]}_')
                          for node in scene.named[name])
        if nodes not in vertices:
            points = []
            for node in nodes:
                for ref in scene.components(node, 'MeshFilter'):
                    filt = ref.read()
                    mesh = MeshHandler(filt.m_Mesh.read())
                    mesh.process()
                    obj = filt.m_GameObject.read()
                    owner = next(c.component.path_id for c in obj.m_Component if c.component.type.name == 'Transform')
                    matrix = world_matrix(scene, owner)
                    world = np.asarray(mesh.m_Vertices) @ matrix[:3, :3].T + matrix[:3, 3]
                    world[:, 2] *= -1
                    points.append(world)
            vertices[nodes] = np.concatenate(points)
        points = vertices[nodes] - row['center']
        c, s = math.cos(row['yaw']), math.sin(row['yaw'])
        local = np.column_stack((points[:, 0]*c - points[:, 2]*s, points[:, 1], points[:, 0]*s + points[:, 2]*c))
        assert np.any(np.all(np.abs(local) <= np.asarray(row['size']) * .5 + .012, axis=1)), (
            f'{row["id"]} 没有对到发布包可见模型'
        )


def artifact_check():
    from tidegate_legacy_collision import legacy_interactions, legacy_parts, runtime_rows, scatter_bounds
    from verify_raid_indoor_roofs import Scene

    kit = read_json(ART / 'fbx/tidegate_building_colliders.json')
    kit_source = ROOT / 'docs/design/tidegate-town/v2/js/kit-buildings.js'
    if kit_source.is_file():
        assert hashlib.sha256(kit_source.read_bytes()).hexdigest() == kit['sourceSha256'], '建筑生成器已变，请重算部件'
    ledger = read_json(ART / 'collision_boxes.json')
    layout = read_json(ART / 'export/tidegate_layout.json')
    parts = legacy_parts(kit, ledger, layout, scatter_bounds(ART / 'export/tidegate_scatter.glb'))
    actual = [line for line in SOURCE.read_text(encoding='utf-8').splitlines() if 'new Part("' in line]
    assert actual == list(runtime_rows(parts)), '生产恢复盒与作者部件尺寸或位置不一致'
    scene = Scene(BUNDLE)
    original = list(serialized_boxes(scene))
    existing = {row['id'] for row in original}
    parts = [row for row, line in zip(parts, actual) if line.split('"')[1] not in existing]
    verify_visuals(scene, parts)
    boxes = original + parts
    from verify_raid_indoor_roofs import world_matrix
    spawn_height = world_matrix(scene, scene.indexed('Markers')['PlayerSpawn'])[1, 3]
    verify_standing_markers(layout, boxes, spawn_height)
    open_doors = verify_door_openings(layout['buildings'], original, parts)
    buildings = {b['id']: b for b in layout['buildings']}
    interactions = legacy_interactions(layout)
    markers = dict(marker_data(scene, (name for _, name in interactions)))
    routes = {}
    for part, name in interactions:
        building = part.split('_')[1]
        if building not in routes:
            routes[building] = door_routes(buildings[building], boxes)
        anchor, yaw = markers[name]
        assert any(can_interact(foot, anchor, yaw, name.startswith('GP_valve_')) for foot in routes[building]), (
            f'{name} 从 {building} 门口到不了官方交互触发范围'
        )
    print(f'实包复算：{len(parts)} 块新增实体、{len(markers)} 个交互点、{len(routes)} 座建筑入口、{open_doors} 个原有门洞保持可通行')


def main():
    source_check()
    if os.environ.get('BOSSRUSH_GUARD_SOURCE_ONLY') == '1':
        print('TidegateBuildingCollisionPropertyTest: PARTIAL 仅检查生产接线与几何判据')
        return 2
    if not (BUNDLE.is_file() and (ART / 'fbx/tidegate_building_colliders.json').is_file()):
        print('TidegateBuildingCollisionPropertyTest: PARTIAL 缺本地发布包或作者部件数据')
        return 2
    artifact_check()
    print('TidegateBuildingCollisionPropertyTest: PASS')
    return 0


if __name__ == '__main__':
    sys.exit(main())
