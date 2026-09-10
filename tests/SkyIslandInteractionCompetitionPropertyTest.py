"""用真实作者几何算出天空岛全部**静态**交互体的落点，回答 U1「哪些点位会抢交互」。

为什么必须算：官方 `CA_Interact.SearchInteractableAround` 按「玩家到 collider 的距离**严格小于**」
取唯一交互目标，同距时由 `Physics.OverlapSphereNonAlloc` 的返回顺序决定，而且不同交互组之间
滚轮切不过去。两个交互体的触发体积一旦相交，玩家站在重叠区里就必有一个按不到 ——
这正是 CR-2026-09-09-004 / -005 的机制，也是 `U1` 至今仍开放的那条风险。

生产侧的三条退避（纪念物 3.2 m、航路图 3.2 m、谢礼箱 3 m）只躲开了**各自的那一个锚点**：
`SkyIslandRewardCrate.TryFindCratePosition` 本身只做地面射线 + 墙体胶囊两项裁决，
**没有任何「与其它交互体净空」的检查**（源码注释自己写明「刻意不带与已放置点净空那一段」）。
于是「纪念物 ↔ 别人的搜刮箱」这类跨系统的组合从来没人算过 —— 而搜刮箱只保证距玩法标记
≥ 4.5 m，纪念物就摆在标记外 3.2 m，两者最近可以到 1.3 m。

这份复算把所有静态交互体的世界坐标一次性算出来，两两比距离。
几何源与 `SkyIslandContentPlacementPropertyTest` **共用同一份复算器**（直接 import），
避免两处各写一遍地面射线再各漂一遍。

## 这份测试**不**证明什么

- 触发体尺寸里，官方 `InteractableLootbox` 预制体的 collider 离线拿不到，
  这里用保守常量 `LOOTBOX_HALF_EXTENT`；真实尺寸由岛内 F3 用例 `SKY_INTERACTION_SEPARATION`
  按 `Collider.bounds` 实测（它跑在真实场景里，两者互补）。
- 居民会走动（`DuckNpcMovement`，蓝图里的 `wanderRadius`）。这里只算**出生点**，
  漫游半径按数据表读出来一并报出，真实站位必须实机看。
- 敌人尸体、掉落箱、噬风战利品是运行时动态对象，不在静态复算范围内。
"""
import json
import math
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

# 复用同一份地面/墙体复算器：它逐字复现了生产的 TryResolve / FindGround，
# 并且已有 4 条破坏探针证明它真的会拒绝坏落点。
from SkyIslandContentPlacementPropertyTest import (  # noqa: E402
    MARKERS, blocked, ground_hit, read_anchors, resolve_anchor,
)

ROOT = Path(__file__).resolve().parents[1]

# ---------------------------------------------------------------------------
# 生产常量。**必须从源码读出来**，不能在这里写死第二份：
# 写死的话，生产改了间距而这里没改，测试会继续用旧值算出「安全」。
# ---------------------------------------------------------------------------


def _read(path):
    return (ROOT / path).read_text(encoding='utf-8-sig')


def _const(source, pattern, label):
    match = re.search(pattern, source)
    if not match:
        raise AssertionError('读不到生产常量：' + label)
    return float(match.group(1))


CRATE_SRC = _read('DebugAndTools/SkyIsland/SkyIslandRewardCrate.cs')
LOOT_SRC = _read('DebugAndTools/SkyIsland/SkyIslandLootTables.cs')
SERVICES_SRC = _read('DebugAndTools/SkyIsland/SkyIslandServices.cs')
SESSION_SRC = _read('DebugAndTools/SkyIsland/SkyIslandSession.cs')
PRESENTATION_SRC = _read('DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs')
GUIDE_SRC = _read('DebugAndTools/SkyIsland/SkyIslandGuideInteractable.cs')
RESIDENTS_SRC = _read('DebugAndTools/SkyIsland/SkyIslandResidents.cs')

SEPARATION = _const(CRATE_SRC, r'InteractableSeparation\s*=\s*([0-9.]+)f', 'InteractableSeparation')
PLACEMENT_ATTEMPTS = int(_const(CRATE_SRC, r'PlacementAttempts\s*=\s*(\d+)', 'PlacementAttempts'))
PLACEMENT_STEP = _const(CRATE_SRC, r'PlacementBearingStep\s*=\s*([0-9.]+)f', 'PlacementBearingStep')
MARKER_CLEARANCE = _const(LOOT_SRC, r'MarkerClearance\s*=\s*([0-9.]+)f', 'MarkerClearance')
BOUNTY_BEARING = _const(SERVICES_SRC, r'BountyRewardBearing\s*=\s*([0-9.]+)f', 'BountyRewardBearing')
BOUNTY_STEP = _const(SERVICES_SRC, r'BountyRewardBearingStep\s*=\s*([0-9.]+)f', 'BountyRewardBearingStep')
BOUNTY_DISTANCE = _const(SERVICES_SRC, r'BountyRewardDistance\s*=\s*([0-9.]+)f', 'BountyRewardDistance')
BOUNTY_ROUNDS = int(_const(SERVICES_SRC, r'MaxRounds\s*=\s*(\d+)',
                           'MaxRounds') if 'MaxRounds' in SERVICES_SRC else 3)
EXTRACTION_RADIUS = _const(SESSION_SRC, r'ExtractionRadius\s*=\s*([0-9.]+)f', 'ExtractionRadius')

# 触发体的水平半宽。前三个从源码读，最后一个是官方预制体、离线拿不到。
SEARCH_HALF = _const(SESSION_SRC, r'trigger\.size\s*=\s*new Vector3\(([0-9.]+)f', 'search trigger size') / 2.0
STORY_HALF = _const(PRESENTATION_SRC, r'trigger\.size\s*=\s*new Vector3\((\d+), 2, 3\)', 'story trigger size') / 2.0
GUIDE_HALF = _const(GUIDE_SRC, r'trigger\.radius\s*=\s*([0-9.]+)f', 'guide trigger radius')
RESIDENT_HALF = _const(RESIDENTS_SRC, r'capsule\.radius\s*=\s*([0-9.]+)f', 'resident capsule radius')
# 官方 `InteractableLootbox` 预制体的 collider 离线不可知。0.6 m 是保守上界
# （原版箱子约 1 m 见方）；真实尺寸由岛内 F3 用例按 Collider.bounds 实测。
LOOTBOX_HALF_EXTENT = 0.6

# 纪念物挂在哪些标记上：`SkyIslandWorldStory.Tick` 里每条 flag 对应一次 Beacon(...)。
WORLD_STORY_SRC = _read('DebugAndTools/SkyIsland/SkyIslandWorldStory.cs')
MEMORIAL_MARKERS = re.findall(r'Beacon\("([A-Za-z0-9_]+)"', WORLD_STORY_SRC)
GUIDE_MARKERS = re.findall(r'marker\.name != "([A-Za-z0-9_]+)"', GUIDE_SRC)
RESIDENT_MARKERS = re.findall(r'"(POI_[A-Z0-9]+|EnemySpawn_[A-Z0-9]+)"',
                              RESIDENTS_SRC.split('private static readonly string[] Markers', 1)[1]
                              .split('};', 1)[0])
# 交单锚点：装置版用 Search_B 本身（`BoardPosition("Search_B")`）。
BOUNTY_ANCHOR = 'Search_B'
# 信鸽：`SkyIslandLetters` 里每封信登记的 (id, 锚点)；落点算法见 `SkyIslandWorldStory.PlacePigeon`。
LETTERS_SRC = _read('DebugAndTools/SkyIsland/SkyIslandLetters.cs')
LETTERS = re.findall(r'Letter\("(Letter_\d+)",\s*"([A-Za-z0-9_]+)"', LETTERS_SRC)


def stable_hash(value):
    """逐字复现 SkyIslandLootTables.StableHash（FNV-1a 32 位，最后掩到 31 位）。"""
    h = 2166136261
    for ch in value:
        h = (h ^ ord(ch)) & 0xFFFFFFFF
        h = (h * 16777619) & 0xFFFFFFFF
    return h & 0x7FFFFFFF


def resolve_crate(anchor, bearing, distance):
    """逐字复现 SkyIslandRewardCrate.TryFindCratePosition：**只做地面 + 墙体两项裁决**。

    刻意不加「与已放置点净空」——生产就是这么写的，加了反而会掩盖本测试要暴露的问题。
    失败返回 None；生产在失败时的行为各不相同（纪念物放弃挂交互体、航路图退回纯方位角、
    谢礼箱退回锚点本身），由调用方各自处理。
    """
    for attempt in range(PLACEMENT_ATTEMPTS):
        angle = math.radians(bearing + attempt * PLACEMENT_STEP)
        px = anchor[0] + math.cos(angle) * distance
        pz = anchor[2] + math.sin(angle) * distance
        hit = ground_hit(px, pz, anchor[1] + 3.0, 7.0)
        if hit is None:
            continue
        gy = hit + 0.05
        if blocked(px, pz, gy, 0.4, 1.2, 0.45):
            continue
        return (px, gy, pz)
    return None


def ground_at(marker, up=2.0, length=5.0):
    """居民落点：`Physics.Raycast(marker + up*2, down, 5, groundLayerMask)`。"""
    pos = MARKERS[marker]
    hit = ground_hit(pos[0], pos[2], pos[1] + up, length)
    return None if hit is None else (pos[0], hit + 0.15, pos[2])


def build_interactables():
    """算出全部静态交互体：(名字, 世界坐标, 水平半宽, 交互组)。同组不构成竞争。"""
    items = []
    notes = []

    # 1) 见闻点：`SkyIslandSession.PrepareMarkers` 给每个 Search* 都挂一个。
    for marker in sorted(m for m in MARKERS if m.startswith('Search')):
        items.append(('search:' + marker, MARKERS[marker], SEARCH_HALF, 'search:' + marker))

    # 2) 航路图（含它自己的光色子选项，同组）。
    for marker in GUIDE_MARKERS:
        anchor = MARKERS[marker]
        bearing = stable_hash(marker) % 360
        spot = resolve_crate(anchor, bearing, SEPARATION)
        if spot is None:
            # 生产在这里退回纯方位角偏移而不是放弃：码头与集市都是平坦安全区。
            radians = math.radians(bearing)
            spot = (anchor[0] + math.cos(radians) * SEPARATION, anchor[1],
                    anchor[2] + math.sin(radians) * SEPARATION)
            notes.append('航路图 %s 没有净空落点，按纯方位角偏移放置（与生产一致）' % marker)
        items.append(('guide:' + marker, spot, GUIDE_HALF, 'guide:' + marker))

    # 3) 完成纪念物：按持久 flag 重建，本测试按「全部已完成」的最坏情况算。
    for marker in MEMORIAL_MARKERS:
        anchor = MARKERS[marker]
        spot = resolve_crate(anchor, stable_hash(marker) % 360, SEPARATION)
        if spot is None:
            # 生产找不到净空就只留光、不挂交互体（绝不退回原点）——没有交互体就没有竞争。
            notes.append('纪念物 %s 没有净空落点，生产只留光不挂交互体' % marker)
            continue
        items.append(('memorial:' + marker, spot, STORY_HALF, 'memorial:' + marker))

    # 4) 搜刮箱：39 个锚点，落点算法与顺序都与生产一致（含「与已落位点 3 m 净空」）。
    placed = []
    for anchor_id, marker, bearing, distance, _tier, _region in read_anchors():
        point = resolve_anchor(marker, bearing, distance, placed)
        if point is None:
            notes.append('搜刮点 %s 落不了位（几何回归会单独报红）' % anchor_id)
            continue
        placed.append(point)
        items.append(('loot:' + anchor_id, point, LOOTBOX_HALF_EXTENT, 'loot:' + anchor_id))

    # 5) 委托谢礼箱：三轮各差 120°，落点探测失败退回锚点本身（fail-open）。
    anchor = MARKERS[BOUNTY_ANCHOR]
    for round_index in range(BOUNTY_ROUNDS):
        bearing = BOUNTY_BEARING + round_index * BOUNTY_STEP
        spot = resolve_crate(anchor, bearing, BOUNTY_DISTANCE)
        if spot is None:
            spot = anchor
            notes.append('谢礼箱第 %d 轮没有净空落点，生产退回锚点本身' % (round_index + 1))
        items.append(('bounty:round%d' % (round_index + 1), spot, LOOTBOX_HALF_EXTENT,
                      'bounty:round%d' % (round_index + 1)))

    # 6) 居民剧情交互：挂在角色身上，落点是标记脚下的地面。
    for index, marker in enumerate(RESIDENT_MARKERS):
        spot = ground_at(marker)
        if spot is None:
            notes.append('居民落点 %s 没有地面（生产会抛「居民落点没有地面」）' % marker)
            continue
        items.append(('resident:' + marker, spot, RESIDENT_HALF, 'resident:' + marker))

    # 7) 信鸽：每趟至多一只（`SkyIslandWorldStory.PlacePigeon`），落点是信的锚点外一个交互间距、方位按信的 id 取稳定散列。
    #    12 封信不会同时在场（同组不比），但每一封都必须与其它交互体不抢——下一趟来哪一封只取决于存档。
    for letter_id, anchor_marker in LETTERS:
        spot = resolve_crate(MARKERS[anchor_marker], stable_hash(letter_id) % 360, SEPARATION)
        if spot is None:
            # 生产找不到净空就这趟不放信鸽（信留到下一趟）：没有交互体就没有竞争。
            notes.append('信鸽 %s 在 %s 没有净空落点，生产这趟不放信鸽' % (letter_id, anchor_marker))
            continue
        items.append(('pigeon:' + letter_id, spot, STORY_HALF, 'pigeon'))

    return items, notes


def horizontal(a, b):
    """俯视游戏的交互选择由水平距离决定，高度差不参与。"""
    return math.hypot(a[0] - b[0], a[2] - b[2])


def check_pairs(items):
    """两两比距离。重叠 = 玩家站得进两个触发体，必有一个按不到。"""
    overlaps = []
    tight = []
    closest = (float('inf'), None)
    for i in range(len(items)):
        name_a, pos_a, half_a, group_a = items[i]
        for j in range(i + 1, len(items)):
            name_b, pos_b, half_b, group_b = items[j]
            if group_a == group_b:
                continue
            distance = horizontal(pos_a, pos_b)
            required = half_a + half_b
            margin = distance - required
            if margin < closest[0]:
                closest = (margin, '%s ↔ %s (%.2f m, 需 %.2f m)' % (name_a, name_b, distance, required))
            if margin < 0:
                overlaps.append('%s ↔ %s：%.2f m < %.2f m' % (name_a, name_b, distance, required))
            elif margin < 0.5:
                # 余量不足半米：几何上没重叠，但生产任何一个常量微调就会重叠。
                tight.append('%s ↔ %s：余量 %.2f m' % (name_a, name_b, margin))
    return overlaps, tight, closest


def check_own_anchor_clearance(items):
    """CR-2026-09-09-004 / -005 的不变式：纪念物与航路图必须退开**自己那个**锚点。"""
    errors = []
    lookup = {name: (pos, half) for name, pos, half, _ in items}
    for name, pos, half, _ in items:
        if not (name.startswith('memorial:') or name.startswith('guide:')):
            continue
        marker = name.split(':', 1)[1]
        anchor_name = 'search:' + marker
        if anchor_name not in lookup:
            continue  # EnemySpawn_F（折翎旧腰牌）与 Lamp_A_02（归航船名册）上没有见闻点
        anchor_pos, anchor_half = lookup[anchor_name]
        distance = horizontal(pos, anchor_pos)
        if distance < half + anchor_half:
            errors.append('%s 与自己的装置同点（%.2f m < %.2f m）：装置会被盖住，捷径/终章点不到'
                          % (name, distance, half + anchor_half))
    return errors


def check_negative_probes():
    """破坏探针：复算器必须真的会拒绝坏落点，否则上面的全绿没有意义。"""
    # 推出整张地图：六个方位全部悬空。
    assert resolve_crate(MARKERS['Lamp_G'], 30.0, 2000.0) is None, '推出地图的落点必须算不出来'
    # 常量必须真的从源码读出来，而不是默认值。
    assert SEPARATION > 0 and MARKER_CLEARANCE > 0 and SEARCH_HALF > 0, '生产常量没读到'
    # 同一坐标必然重叠：判据本身要能报红。
    fake = [('a', (0.0, 0.0, 0.0), 1.0, 'a'), ('b', (0.0, 0.0, 0.0), 1.0, 'b')]
    overlaps, _tight, _closest = check_pairs(fake)
    assert len(overlaps) == 1, '同点的两个交互体必须被判为重叠'
    # 同组不算竞争。
    same = [('a', (0.0, 0.0, 0.0), 1.0, 'g'), ('b', (0.0, 0.0, 0.0), 1.0, 'g')]
    assert not check_pairs(same)[0], '同组交互体（滚轮可切）不该算竞争'


def main():
    check_negative_probes()
    assert len(LETTERS) >= 12, '信鸽锚点没解析全：%d 封' % len(LETTERS)
    items, notes = build_interactables()
    assert len(items) >= 60, '静态交互体数量异常偏少：%d' % len(items)

    anchor_errors = check_own_anchor_clearance(items)
    overlaps, tight, closest = check_pairs(items)

    for note in notes:
        print('  note: ' + note)
    for line in tight:
        print('  tight: ' + line)

    problems = anchor_errors + overlaps
    if problems:
        for line in problems:
            print('  - ' + line)
        print('SkyIslandInteractionCompetitionPropertyTest: FAIL')
        raise SystemExit(1)

    print('PASS SkyIslandInteractionCompetitionPropertyTest '
          '(%d 个静态交互体两两复算, 最紧一对: %s, %d 对余量 < 0.5 m, 4 条破坏探针被拒)'
          % (len(items), closest[1], len(tight)))


if __name__ == '__main__':
    main()
