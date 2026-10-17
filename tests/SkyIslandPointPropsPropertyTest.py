"""天空岛「每一处会出交互提示的地方都有看得见的实体物件」（owner 2026-10-17：不能是空交互）。

盘点报告：docs/reports/reviews/2026-10-17-天空岛交互点可见性盘点.md（local-only）。本测试钉三件事：

1. **建交互体的地方都登记了它的实物**（结构）：扫 `SkyIsland/*.cs` 里所有创建世界交互体的写法——
   `AddComponent<天空岛交互体类型>`、`SkyIslandStoryInteractable.Create(`、`GetOrCreateStandaloneInteractable<`、
   官方搜刮箱预制体实例化——每一处都必须落在 `SITES` 登记过的方法里，且该方法体里有登记的实物证据
   （`SkyIslandPointProps.*`、纪念物木桩、航向仪构件、居民角色、官方箱子预制体……）。新加一种交互体不登记就红。
   挂进已有交互组的子选项（`AddSubInteractable`：委托、商店、签筒、合成台、点灯、谜题……）跟随宿主，不单独算。
2. **运行时摆放表里每个交互体都有物件**（几何）：`ArtSource/SkyIsland/runtime_placements.json` 里
   - 每处见闻点 `search_point` 有一块自己的木牌（`point_sign`，`SkyIslandGates.AddPointSign / AddPointPost`），
     牌心离见闻点 ≤ 3 m（风标那一处站在铺装广场正中，登记为 3.7 m 的例外并写明理由）；
   - 每个航路图 / 信鸽 / 采集点有一条与它同点（≤ 5 cm）的 `interact_prop`，物件种类与交互种类对得上
     （采集点按 `SkyIslandFieldcraftRules` 的种类，C# 的 `Gather` 分派覆盖每个种类）；
   - 新物件之间、新物件与已有实体（搜刮箱、谢礼箱、纪念物木桩、灶火、桥口木牌）的占地圆不互相压。
   压路 / 穿模 / 净空圈里有硬物由 `SkyIslandSceneCleanlinessPropertyTest` 按证据表复核，这里不重复。
3. **判据自己会报红**：拿掉一块木牌、把物件挪开 1 m、把两件物件叠在一起、多一处没登记的交互体，都必须被拒。
"""
import json
import math
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
SKY = ROOT / 'SkyIsland'
RUNTIME = ROOT / 'ArtSource/SkyIsland/runtime_placements.json'

# (文件, 方法名) → (实物证据：方法体里必须出现的片段, 说明)。证据为 None 的是「由几何检查兜」。
SITES = {
    ('SkyIslandSession.cs', 'PrepareMarkers'): (None, '见闻点：木牌在 SkyIslandGates，由下面的几何检查逐处核对'),
    ('SkyIslandGathering.cs', 'Build'): ('SkyIslandPointProps.Gather(', '采集物件（草丛 / 浮木 / 苔垫 / 矿石 / 晶簇）'),
    ('SkyIslandGuideInteractable.cs', 'Attach'): ('SkyIslandPointProps.ChartStand(', '航路图架'),
    ('SkyIslandWorldStory.cs', 'PlacePigeon'): ('SkyIslandPointProps.Pigeon(', '白鸽'),
    ('SkyIslandWorldStory.cs', 'Beacon'): ('SkyIslandGates.AddMemorialPost(', '纪念物 / 归航船名册：木桩 + 小木牌'),
    ('SkyIslandPreludeFlow.cs', 'CreateObjective'): ('CreateInstrumentPiece(', '零号区摔坏的航向仪'),
    ('SkyIslandResidents.cs', 'AttachStoryInteraction'): ('child.transform.SetParent(npc.transform', '挂在居民角色身上'),
    ('SkyIslandRewardCrate.cs', 'Build'): ('GameplayDataSettings.Prefabs', '官方搜刮箱预制体自带箱子模型（地上的搜刮点再换木箱外观）'),
    ('SkyIslandStoryPresentation_Parts.cs', 'Create'): (None, '剧情交互体工厂：实物由调用方（Beacon / PlacePigeon）挂'),
}
# 风标见闻点 Search_D 站在铺装广场正中，3 m 内放不下实体（场景净空不许压路），窄牌立在广场东北沿。
SIGN_DISTANCE_LIMIT = 3.0
SIGN_DISTANCE_EXCEPTIONS = {'Search_D': 3.8}

METHOD_HEADER = re.compile(
    r'(?:private|internal|public|protected)(?:\s+(?:static|override|sealed|virtual|new))*\s+[\w<>\[\],.?]+\s+(\w+)\s*\([^;{)]*\)\s*\{')


def interactable_types():
    """天空岛自己的交互体类型：直接或间接继承官方 InteractableBase / Mod 的建筑交互基类。"""
    bases = {'InteractableBase', 'BossRushBuildingInteractableBase', 'InteractableLootbox'}
    found = set()
    for path in SKY.glob('*.cs'):
        for name, base in re.findall(r'class\s+(\w+)\s*:\s*(\w+)', clean_source(path.read_text(encoding='utf-8-sig'))):
            if base in bases:
                found.add(name)
    return found


def creation_sites(sources, types):
    """[(文件, 所在方法, 写法)]：每一处创建世界交互体的位置。"""
    patterns = [re.compile(r'AddComponent<(%s)>' % '|'.join(sorted(types))),
                re.compile(r'SkyIslandStoryInteractable\.Create\('),
                re.compile(r'GetOrCreateStandaloneInteractable<'),
                re.compile(r'Object\.Instantiate\(prefab\b')]
    sites = []
    for name, source in sources.items():
        headers = [(m.start(), m.group(1)) for m in METHOD_HEADER.finditer(source)]
        for pattern in patterns:
            for match in pattern.finditer(source):
                method = None
                for start, header in headers:
                    if start < match.start():
                        method = header
                    else:
                        break
                sites.append((name, method, match.group(0)))
    return sites


def method_body(source, method):
    for match in METHOD_HEADER.finditer(source):
        if match.group(1) != method:
            continue
        depth = 0
        for i in range(match.end() - 1, len(source)):
            if source[i] == '{':
                depth += 1
            elif source[i] == '}':
                depth -= 1
                if depth == 0:
                    return source[match.end():i]
    return None


def check_sites(sources, types):
    errors = []
    sites = creation_sites(sources, types)
    for name, method, what in sites:
        if (name, method) not in SITES:
            errors.append('UNREGISTERED_INTERACTABLE：%s 的 %s() 里 %s 建了交互体，没在 SITES 登记它看得见的实物' % (name, method, what))
    for (name, method), (evidence, why) in SITES.items():
        if not any(site[0] == name and site[1] == method for site in sites):
            errors.append('STALE_SITE：%s 的 %s() 里已经不建交互体（%s），登记过期' % (name, method, why))
            continue
        if evidence is None:
            continue
        body = method_body(sources[name], method)
        if body is None or evidence not in body:
            errors.append('MISSING_PROP：%s 的 %s() 建了交互体却没挂实物（缺 %s：%s）' % (name, method, evidence, why))
    return errors, len(sites)


def gather_kinds():
    source = (SKY / 'SkyIslandFieldcraftRules.cs').read_text(encoding='utf-8-sig')
    return dict(re.findall(r'Node\("([A-Za-z0-9]+)",\s*"[A-Za-z0-9_]+",\s*[0-9.]+f,\s*[0-9.]+f,\s*SkyIslandGatherKind\.(\w+)', source))


def check_gather_dispatch():
    """C# 的 Gather 分派覆盖 SkyIslandGatherKind 的每一个成员。"""
    rules = clean_source((SKY / 'SkyIslandFieldcraftRules.cs').read_text(encoding='utf-8-sig'))
    enum = re.search(r'enum\s+SkyIslandGatherKind\s*\{([^}]*)\}', rules).group(1)
    members = re.findall(r'(\w+)\s*=', enum)
    props = clean_source((SKY / 'SkyIslandPointProps.cs').read_text(encoding='utf-8-sig'))
    handled = set(re.findall(r'case\s+SkyIslandGatherKind\.(\w+)\s*:', props))
    return ['GATHER_KIND_WITHOUT_PROP：采集种类 %s 没有实物' % m for m in members if m not in handled], len(members)


def horizontal(a, b):
    return math.hypot(a['x'] - b['x'], a['z'] - b['z'])


def check_runtime(points, kinds_of_node):
    errors = []
    signs = {}
    for row in points:
        if row['kind'] == 'point_sign':
            signs.setdefault(row['id'].split('#', 1)[0], []).append(row)
    props = {row['id']: row for row in points if row['kind'] == 'interact_prop'}
    searches = [row for row in points if row['kind'] == 'search_point']
    for search in searches:
        rows = signs.get(search['id'])
        if not rows:
            errors.append('SEARCH_WITHOUT_SIGN：见闻点 %s 没有木牌' % search['id'])
            continue
        center = next((r for r in rows if r['id'].endswith('#C') or r['id'].endswith('#P')), rows[0])
        limit = SIGN_DISTANCE_EXCEPTIONS.get(search['id'], SIGN_DISTANCE_LIMIT)
        distance = horizontal(center, search)
        if distance > limit:
            errors.append('SIGN_TOO_FAR：见闻点 %s 的木牌离它 %.2f m（上限 %.1f m）' % (search['id'], distance, limit))
        nearest = min(searches, key=lambda other: horizontal(center, other))
        if nearest['id'] != search['id']:
            errors.append('SIGN_CLOSER_TO_OTHER：%s 的木牌离 %s 更近' % (search['id'], nearest['id']))
    for key in signs:
        if not any(s['id'] == key for s in searches):
            errors.append('SIGN_WITHOUT_SEARCH：木牌 %s 没有对应的见闻点' % key)
    expected = {'travel_guide': ('guide', lambda row: 'chart_stand'),
                'pigeon': ('pigeon', lambda row: 'pigeon'),
                'gather_node': ('gather', lambda row: 'gather_' + kinds_of_node[row['id']].lower())}
    covered = 0
    for row in points:
        if row['kind'] not in expected:
            continue
        prefix, mesh = expected[row['kind']]
        prop = props.get(prefix + ':' + row['id'])
        if prop is None:
            errors.append('INTERACTABLE_WITHOUT_PROP：%s %s 没有实体物件' % (row['kind'], row['id']))
            continue
        covered += 1
        if horizontal(prop, row) > 0.05:
            errors.append('PROP_OFF_INTERACTABLE：%s 离 %s %.2f m（应与交互体同点）' % (prop['id'], row['id'], horizontal(prop, row)))
        if prop['mesh'] != mesh(row):
            errors.append('PROP_KIND_MISMATCH：%s 的物件是 %s，应为 %s' % (prop['id'], prop['mesh'], mesh(row)))
    # 新物件不与其它有占地的实体互压（同一块木牌的牌心与木桩、物件与自己的交互体不算）。
    solids = [row for row in points if row.get('footprint') and row.get('mesh')
              and row['kind'] in ('interact_prop', 'point_sign', 'loot_crate', 'bounty_crate', 'memorial_stake', 'hearth', 'bridge_sign_post')]
    for i, a in enumerate(solids):
        for b in solids[i + 1:]:
            if 'interact_prop' not in (a['kind'], b['kind']) and 'point_sign' not in (a['kind'], b['kind']):
                continue
            if a['kind'] == b['kind'] == 'point_sign' and a['id'].split('#')[0] == b['id'].split('#')[0]:
                continue
            # 信鸽一趟至多一只（同组），两只信鸽的白鸽不会同时在场。
            if a['id'].startswith('pigeon:') and b['id'].startswith('pigeon:'):
                continue
            gap = horizontal(a, b) - a['footprint'] - b['footprint']
            if gap < 0.1:
                errors.append('PROPS_OVERLAP：%s:%s 与 %s:%s 占地圆相距 %.2f m' % (a['kind'], a['id'], b['kind'], b['id'], gap))
    return errors, covered, sum(len(v) for v in signs.values())


def probes(points, kinds_of_node, sources, types):
    """破坏探针：每一类判据都要真的会拒绝。"""
    rejected = 0
    without_sign = [r for r in points if not (r['kind'] == 'point_sign' and r['id'].startswith('Search_C#'))]
    assert any(e.startswith('SEARCH_WITHOUT_SIGN') for e in check_runtime(without_sign, kinds_of_node)[0]), 'PROBE_MISSING_SIGN_ACCEPTED'
    rejected += 1
    moved = [dict(r, x=r['x'] + 1.0) if r['id'] == 'gather:E1' else r for r in points]
    assert any(e.startswith('PROP_OFF_INTERACTABLE') for e in check_runtime(moved, kinds_of_node)[0]), 'PROBE_MOVED_PROP_ACCEPTED'
    rejected += 1
    pigeon = next(r for r in points if r['id'] == 'pigeon:Letter_01')
    stacked = points + [dict(pigeon, id='gather:Fake', mesh='gather_ore', footprint=0.6)]
    assert any(e.startswith('PROPS_OVERLAP') for e in check_runtime(stacked, kinds_of_node)[0]), 'PROBE_STACKED_PROPS_ACCEPTED'
    rejected += 1
    far = [dict(r, x=r['x'] + 5.0) if r['id'].startswith('Search_S2#') else r for r in points]
    assert any(e.startswith('SIGN_TOO_FAR') for e in check_runtime(far, kinds_of_node)[0]), 'PROBE_FAR_SIGN_ACCEPTED'
    rejected += 1
    no_prop = [r for r in points if r['id'] != 'guide:Search_A_02']
    assert any(e.startswith('INTERACTABLE_WITHOUT_PROP') for e in check_runtime(no_prop, kinds_of_node)[0]), 'PROBE_MISSING_PROP_ACCEPTED'
    rejected += 1
    extra = dict(sources)
    extra['SkyIslandShellLottery.cs'] = extra['SkyIslandShellLottery.cs'] + \
        '\nclass Probe { private void Spawn() { go.AddComponent<SkyIslandSearchPoint>(); } }\n'
    assert any(e.startswith('UNREGISTERED_INTERACTABLE') for e in check_sites(extra, types)[0]), 'PROBE_UNREGISTERED_ACCEPTED'
    rejected += 1
    stripped = dict(sources)
    stripped['SkyIslandWorldStory.cs'] = stripped['SkyIslandWorldStory.cs'].replace('SkyIslandPointProps.Pigeon(', 'SkyIslandPointProps.Nothing(')
    assert any(e.startswith('MISSING_PROP') for e in check_sites(stripped, types)[0]), 'PROBE_MISSING_PIGEON_ACCEPTED'
    rejected += 1
    return rejected


def main():
    sources = {path.name: clean_source(path.read_text(encoding='utf-8-sig')) for path in sorted(SKY.glob('*.cs'))}
    types = interactable_types()
    assert 'SkyIslandSearchPoint' in types and 'SkyIslandGatherPoint' in types, '交互体类型没扫出来：%r' % sorted(types)
    errors, site_count = check_sites(sources, types)
    dispatch_errors, kind_count = check_gather_dispatch()
    errors += dispatch_errors
    points = json.loads(RUNTIME.read_text(encoding='utf-8'))['points']
    kinds_of_node = gather_kinds()
    runtime_errors, covered, sign_rows = check_runtime(points, kinds_of_node)
    errors += runtime_errors
    if errors:
        for error in errors:
            print(error)
        raise SystemExit(1)
    rejected = probes(points, kinds_of_node, sources, types)
    searches = sum(1 for r in points if r['kind'] == 'search_point')
    print('PASS SkyIslandPointPropsPropertyTest (%d 处建交互体的写法都登记了实物, %d 处见闻点各有木牌（%d 条木牌行）, '
          '%d 个航路图 / 信鸽 / 采集点各有同点物件, %d 种采集物都有造型, %d 条破坏探针被拒)'
          % (site_count, searches, sign_rows, covered, kind_count, rejected))


if __name__ == '__main__':
    main()
