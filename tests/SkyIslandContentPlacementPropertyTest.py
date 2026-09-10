"""用真实作者几何复算天空岛新增内容的落点，回答「这些东西玩家真的拿得到吗」。

守卫断言的是结构不变式；这份断言的是**几何可达性**：编译绿、结构守卫绿，
但一个锚点落在岛外、悬空或墙里，运行时只会静默跳过该点（fail-open），玩家什么都看不到。

复现生产算法（改动生产代码时必须同步这里）：
- 搜刮点 `SkyIslandScavenging.TryResolve`：6 次尝试，每次 bearing + 60°；地面射线 (+3, 下 7)；
  墙体胶囊 (ground+0.4 .. ground+1.2, r=0.45)；距玩法标记 >= 4.5；距已落位点 >= 3。
- 遭遇落点 `SkyIslandEncounters.FindGround`：12 次尝试，angle = index*120 + attempt*30，
  半径 index==0 ? 0 : 3；地面射线 (+2, 下 4)；墙体胶囊 (hit+0.6 .. hit+1.5, r=0.5)。

障碍取 layout 的全部 88 个 obstacle，比运行时实际的 60 个 wall collider **更严**，
因此这里通过可推断运行时通过；这里失败则必须人工复核。
既有 15 组遭遇一并复算，作为算法口径的校准——它们红了说明是复算器写错了，不是内容错了。

这不能替代游戏内实机行走：它证明的是「有可站立的地面且不被墙挡」，
不证明 Unity 实体碰撞、A* 工作项与交互拾取顺序。
"""
import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LAYOUT = json.loads((ROOT / 'ArtSource/SkyIsland/layout.json').read_text(encoding='utf-8'))

MARKERS = {m['id']: m['position'] for m in LAYOUT['markers']}
GROUND_VERTS = LAYOUT['ground']['vertices']
GROUND_TRIS = LAYOUT['ground']['triangles']
OBSTACLES = LAYOUT['obstacles']

# 与生产 CollectGameplayMarkers 同一口径
BLOCKER_PREFIX = ('Search', 'POI_', 'EnemySpawn')
BLOCKER_EXACT = ('PlayerSpawn', 'Exit', 'BellExtraction', 'Region_D', 'Region_G')
BLOCKERS = [pos for mid, pos in MARKERS.items()
            if mid.startswith(BLOCKER_PREFIX) or mid in BLOCKER_EXACT]

EXPECTED_ANCHORS = 39
EXPECTED_ENCOUNTERS = 16


def _tri_height(px, pz, tri):
    a, b, c = (GROUND_VERTS[i] for i in tri)
    ax, az, bx, bz, cx, cz = a[0], a[2], b[0], b[2], c[0], c[2]
    denom = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz)
    if abs(denom) < 1e-12:
        return None
    w1 = ((bz - cz) * (px - cx) + (cx - bx) * (pz - cz)) / denom
    w2 = ((cz - az) * (px - cx) + (ax - cx) * (pz - cz)) / denom
    w3 = 1.0 - w1 - w2
    if w1 < -1e-9 or w2 < -1e-9 or w3 < -1e-9:
        return None
    return w1 * a[1] + w2 * b[1] + w3 * c[1]


def ground_hit(px, pz, from_y, ray_len):
    """模拟从 from_y 向下 ray_len 的地面射线；返回最高命中面，未命中返回 None。"""
    best = None
    for tri in GROUND_TRIS:
        h = _tri_height(px, pz, tri)
        if h is None:
            continue
        if from_y - ray_len - 1e-6 <= h <= from_y + 1e-6 and (best is None or h > best):
            best = h
    return best


def blocked(px, pz, y, low, high, radius):
    for obstacle in OBSTACLES:
        x0, z0, x1, z1 = obstacle['bounds']
        if not (x0 - radius <= px <= x1 + radius and z0 - radius <= pz <= z1 + radius):
            continue
        center_y, height = obstacle['center'][1], obstacle['size'][1]
        if center_y + height / 2.0 >= y + low and center_y - height / 2.0 <= y + high:
            return obstacle['id']
    return None


def near_blocker(px, pz, py, clearance):
    for pos in BLOCKERS:
        if (px - pos[0]) ** 2 + (pz - pos[2]) ** 2 + (py - pos[1]) ** 2 < clearance ** 2:
            return True
    return False


def read_anchors():
    src = (ROOT / 'DebugAndTools/SkyIsland/SkyIslandLootTables.cs').read_text(encoding='utf-8')
    return [(a, m, float(b), float(d), t, r) for a, m, b, d, t, r in re.findall(
        r'Anchor\("([A-Za-z0-9_]+)",\s*"([A-Za-z0-9_]+)",\s*([0-9.]+)f,\s*([0-9.]+)f,\s*'
        r'SkyIslandLootTier\.(\w+),\s*"([A-Za-z0-9]+)"\)', src)]


def resolve_anchor(marker, bearing, distance, placed):
    """返回落点或 None，逐字复现 TryResolve。"""
    if marker not in MARKERS:
        return None
    origin = MARKERS[marker]
    for attempt in range(6):
        angle = math.radians(bearing + attempt * 60.0)
        px = origin[0] + math.cos(angle) * distance
        pz = origin[2] + math.sin(angle) * distance
        hit = ground_hit(px, pz, origin[1] + 3.0, 7.0)
        if hit is None:
            continue
        gy = hit + 0.05
        if blocked(px, pz, gy, 0.4, 1.2, 0.45):
            continue
        if near_blocker(px, pz, gy, 4.5):
            continue
        if any((px - p[0]) ** 2 + (pz - p[2]) ** 2 + (gy - p[1]) ** 2 < 9.0 for p in placed):
            continue
        return (px, gy, pz)
    return None


def resolve_encounter_slot(marker, index):
    """返回该槽位是否可站立，逐字复现 FindGround。"""
    if marker not in MARKERS:
        return False
    origin = MARKERS[marker]
    for attempt in range(12):
        angle = math.radians(index * 120.0 + attempt * 30.0)
        radius = 0.0 if index == 0 else 3.0
        px = origin[0] + math.cos(angle) * radius
        pz = origin[2] + math.sin(angle) * radius
        hit = ground_hit(px, pz, origin[1] + 2.0, 4.0)
        if hit is None:
            continue
        if blocked(px, pz, hit + 0.1, 0.6, 1.5, 0.5):
            continue
        return True
    return False


def check_anchors():
    anchors = read_anchors()
    assert len(anchors) == EXPECTED_ANCHORS, '锚点数量变了：%d' % len(anchors)
    placed, failed = [], []
    for anchor_id, marker, bearing, distance, _tier, _region in anchors:
        point = resolve_anchor(marker, bearing, distance, placed)
        if point is None:
            failed.append(anchor_id)
        else:
            placed.append(point)
    assert not failed, '这些搜刮点在真实几何上落不了位（运行时会静默跳过）：%r' % failed
    return len(placed)


def check_encounters():
    world = json.loads((ROOT / 'Assets/Data/SkyIsland/World.json').read_text(encoding='utf-8'))
    assert len(world['encounters']) == EXPECTED_ENCOUNTERS, '遭遇组数量变了'
    failed = []
    for entry in world['encounters']:
        for index in range(entry['count']):
            if not resolve_encounter_slot(entry['marker'], index):
                failed.append('%s#%d' % (entry['id'], index))
    assert not failed, '这些遭遇槽位站不住（生成会抛异常并 10 秒后重试）：%r' % failed
    return len(world['encounters'])


def check_negative_probes():
    """破坏探针：复算器必须真的会拒绝坏落点，否则上面的全绿没有意义。"""
    assert resolve_anchor('Search_Gg', 250.0, 7.0, []) is None, '不存在的标记必须落不了位'
    # 推出整张地图（世界约 900×800 米）：六个方位全部悬空。
    # 注意不能用「几百米」当探针——群岛有多座岛高度接近（E=42 / G=44），
    # 大偏移可能悄悄落到**另一座岛**上。真正挡住这种事的是
    # `SkyIslandContentExpansionGuard` 里 4–12 米的偏移上限。
    assert resolve_anchor('Lamp_G', 30.0, 2000.0, []) is None, '推出地图的锚点必须落不了位'
    # 复算器本身必须会说「这里没有地面」，否则上面的全绿只是恒真。
    assert ground_hit(0.0, 0.0, 45.0, 7.0) is None, '岛间空隙不应命中地面'
    # 已落位点占住同一处时，3 米净距必须挡下第二个。
    occupied = [resolve_anchor('Lamp_G', 30.0, 6.0, [])]
    assert occupied[0] is not None
    assert resolve_anchor('Lamp_G', 30.0, 6.0, occupied) is None or \
        resolve_anchor('Lamp_G', 30.0, 6.0, occupied) != occupied[0], '重叠落点必须被净距挡下'
    # 玩法标记本身 4.5 米内不允许放箱子。
    assert resolve_anchor('POI_G', 0.0, 1.0, []) is None, '压在玩法标记上的锚点必须被拒绝'
    assert not resolve_encounter_slot('NoSuchMarker', 0), '不存在的遭遇标记必须站不住'


def main():
    check_negative_probes()
    anchors = check_anchors()
    encounters = check_encounters()
    print('PASS SkyIslandContentPlacementPropertyTest '
          '(%d/%d 搜刮点落位, %d/%d 遭遇组全槽位可站立, 4 条破坏探针被拒)'
          % (anchors, EXPECTED_ANCHORS, encounters, EXPECTED_ENCOUNTERS))


if __name__ == '__main__':
    main()
